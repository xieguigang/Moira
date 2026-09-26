' /********************************************************************************/
'
'   VtiReader.vb
'
'   VTK XML ImageData (.vti) 二进制解析器（通用多字段版）
'
'   作用：
'       与 CFDEngine.Snapshot.VTIExporter 的写出格式互逆。导出器改造为
'       "字段可配置 + 可选 zlib 压缩"形态以后：
'           - 字段不再固定为 mask/pressure/density/velocity，
'             而是由调用方任意组合（压力 + 温度 + pH + 各物种细胞数 +
'             代谢物浓度 + 逐基因表达量……）
'           - 可以同时出现标量场（NumberOfComponents="1"）与
'             多分量场（velocity×3、gene_*×8）
'           - AppendedData 可能被 vtkZLibDataCompressor 压缩
'       因此本读取器改为：
'           1. 只解析 XML 头（不读整个文件），得到各 DataArray 的
'              （名称 / 类型 / 分量数 / offset）
'           2. 按需 Canonical 读取：只把需要的字段从磁盘上取出来，
'              避免每帧读 50 MB（近百个字段）的内存与 I/O 开销
'           3. 自动支持 raw 与 zlib 分块压缩两种 appended 编码
'           4. VTK 布局（x 最快，t = k*nx*ny + j*nx + i）→
'              引擎布局（idx = i*ny*nz + j*nz + k）重排
'
'   多分量字段的展开：
'       名称为 N、分量数为 C 的数组会被展开成 C 个标量场：
'           N     （第 0 分量）
'           N[i]  （第 i 分量）
'       于是 gene_glc_fermenter[3] 这样的单基因表达量也可以直接作为
'       可视化标量场被选中。
'
' /********************************************************************************/

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>
    ''' 单帧体数据（引擎索引顺序 i*ny*nz + j*nz + k，长度 = nx*ny*nz）。
    ''' 固体体素（mask=0）的各场值为 0。
    ''' </summary>
    Public Class VtiFrameData

        Public Property Nx As Integer
        Public Property Ny As Integer
        Public Property Nz As Integer

        ''' <summary>体素掩膜，True = 活动（流体）体素。</summary>
        Public Property Mask As Boolean()

        ''' <summary>
        ''' 已加载的标量场（按名称索引，引擎布局）。多分量数组已展开为 name[i]。
        ''' </summary>
        Public Property Fields As New Dictionary(Of String, Single())(StringComparer.OrdinalIgnoreCase)

        ''' <summary>速度分量（取自 velocity 数组的三个分量；无速度场时为 Nothing）。</summary>
        Public Property U As Single()
        Public Property V As Single()
        Public Property W As Single()

        ''' <summary>速度模长 |V|（优先取导出的 speed 场，否则由 u/v/w 现算）。</summary>
        Public Property Speed As Single()

        ''' <summary>体素总数。</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return Nx * Ny * Nz
            End Get
        End Property

        ''' <summary>取某标量场；未加载则返回 Nothing。</summary>
        Public Function TryGet(name As String) As Single()
            If name Is Nothing Then Return Nothing

            Dim hit As Single() = Nothing
            If Fields.TryGetValue(name, hit) Then
                Return hit
            End If
            Return Nothing
        End Function

        ''' <summary>取某标量场在某体素上的值；缺失时返回 0。</summary>
        Public Function ValueAt(name As String, idx As Integer) As Double
            Dim arr = TryGet(name)
            If arr Is Nothing OrElse idx < 0 OrElse idx >= arr.Length Then Return 0.0
            Return arr(idx)
        End Function

    End Class

    ''' <summary>单个 DataArray 的声明信息。</summary>
    Public Class VtiArrayInfo
        Public Property Name As String
        Public Property TypeName As String
        Public Property Components As Integer = 1
        Public Property Offset As Long

        ''' <summary>每个值占用的字节数。</summary>
        Public ReadOnly Property BytesPerValue As Integer
            Get
                Select Case TypeName
                    Case "UInt8", "Int8" : Return 1
                    Case "UInt16", "Int16" : Return 2
                    Case "Float64", "Int64", "UInt64" : Return 8
                    Case Else : Return 4  ' Float32 / Int32 / UInt32
                End Select
            End Get
        End Property

        ''' <summary>单个体素的分量所占字节数。</summary>
        Public ReadOnly Property BytesPerVoxel As Integer
            Get
                Return BytesPerValue * Math.Max(1, Components)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' .vti 快照文件的懒加载读取器：先解析头部，再按需读取数组。
    ''' </summary>
    Public Class VtiFile

        ''' <summary>头部缓冲上限（XML 头通常远小于此值）。</summary>
        Const HeaderBudget As Integer = 4 * 1024 * 1024

        ReadOnly m_path As String
        ReadOnly m_arrays As New List(Of VtiArrayInfo)
        ReadOnly m_byName As New Dictionary(Of String, VtiArrayInfo)(StringComparer.OrdinalIgnoreCase)

        Dim m_binaryStart As Long
        Dim m_compressed As Boolean = False

        Private Sub New(path As String)
            m_path = path
        End Sub

        Public ReadOnly Property FilePath As String
            Get
                Return m_path
            End Get
        End Property

        Public ReadOnly Property Arrays As IReadOnlyList(Of VtiArrayInfo)
            Get
                Return m_arrays
            End Get
        End Property

        Public Property Nx As Integer
        Public Property Ny As Integer
        Public Property Nz As Integer

        Public ReadOnly Property VoxelCount As Integer
            Get
                Return Nx * Ny * Nz
            End Get
        End Property

        Public Function Find(name As String) As VtiArrayInfo
            Dim hit As VtiArrayInfo = Nothing
            If m_byName.TryGetValue(name, hit) Then Return hit
            Return Nothing
        End Function

        ' ------------------------------------------------------------------
        '  头部解析
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' 解析一个 .vti 文件的 XML 头（不读取 appended 数据段）。
        ''' </summary>
        Public Shared Function Parse(path As String) As VtiFile
            Dim info As New VtiFile(path)

            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim head As Byte() = ReadAppendedHeader(fs)
                Dim start As Integer = LocateBinaryStart(head)

                If start < 0 Then
                    Throw New InvalidDataException($"'{path}' 中未找到 AppendedData 段的二进制起始标记 '_'")
                End If

                Dim header As String = Encoding.ASCII.GetString(head, 0, start)

                info.m_binaryStart = start
                info.m_compressed = CompressorPattern.IsMatch(header)

                Dim extent = ExtentPattern.Match(header)
                If Not extent.Success Then
                    Throw New InvalidDataException($"'{path}' 中未找到 WholeExtent 声明")
                End If

                info.Nx = Integer.Parse(extent.Groups("nx").Value) + 1
                info.Ny = Integer.Parse(extent.Groups("ny").Value) + 1
                info.Nz = Integer.Parse(extent.Groups("nz").Value) + 1

                ' base64 编码的 appended 段（本导出器不用，但保持兼容判断）
                If Base64Pattern.IsMatch(header) Then
                    Throw New NotSupportedException($"'{path}' 使用了 base64 编码的 AppendedData，当前读取器只支持 raw 编码")
                End If

                For Each m As Match In DataArrayPattern.Matches(header)
                    Dim name As String = m.Groups("name").Value
                    Dim rest As String = m.Groups("rest").Value
                    Dim om = OffsetPattern.Match(rest)

                    If Not om.Success Then Continue For

                    Dim cm = ComponentsPattern.Match(rest)
                    Dim arr As New VtiArrayInfo With {
                        .Name = name,
                        .TypeName = m.Groups("type").Value,
                        .Components = If(cm.Success, Integer.Parse(cm.Groups("n").Value), 1),
                        .Offset = Long.Parse(om.Groups("offset").Value)
                    }

                    info.m_arrays.Add(arr)
                    info.m_byName(name) = arr
                Next
            End Using

            If info.m_arrays.Count = 0 Then
                Throw New InvalidDataException($"'{path}' 的 PointData 中没有任何 DataArray 声明")
            End If

            Return info
        End Function

        ''' <summary>读取从文件开头到 appended 段之前的内容（上限 HeaderBudget 字节）。</summary>
        Private Shared Function ReadAppendedHeader(fs As FileStream) As Byte()
            Dim budget As Integer = CInt(Math.Min(HeaderBudget, fs.Length))
            Dim buf(budget - 1) As Byte
            Dim pos As Integer = 0

            While pos < budget
                Dim got As Integer = fs.Read(buf, pos, Math.Min(65536, budget - pos))
                If got <= 0 Then Exit While
                pos += got

                ' 读到 '_' 之后再留一点余量即可
                If IndexOf(buf, pos - got, pos, AscW("_"c)) >= 0 AndAlso pos > 4096 Then
                    Exit While
                End If
            End While

            If pos < budget Then
                ReDim Preserve buf(pos - 1)
            End If

            Return buf
        End Function

        Private Shared Function IndexOf(bytes As Byte(), from As Integer, upto As Integer, value As Integer) As Integer
            For i As Integer = from To upto - 1
                If bytes(i) = value Then Return i
            Next
            Return -1
        End Function

        ''' <summary>定位 appended 二进制段的起始位置（"_" 标记之后）。</summary>
        Private Shared Function LocateBinaryStart(bytes As Byte()) As Integer
            Dim tag As Byte() = Encoding.ASCII.GetBytes("<AppendedData")
            Dim pos As Integer = IndexBytes(bytes, tag, 0)
            If pos < 0 Then Return -1

            ' 跳到 <AppendedData ...> 的闭合 '>'
            Dim tagEnd As Integer = -1
            For i As Integer = pos + tag.Length To bytes.Length - 1
                If bytes(i) = AscW(">"c) Then
                    tagEnd = i
                    Exit For
                End If
            Next
            If tagEnd < 0 Then Return -1

            For i As Integer = tagEnd + 1 To bytes.Length - 1
                Dim b As Byte = bytes(i)

                If b = AscW("_"c) Then
                    Return i + 1
                ElseIf Not (b = AscW(" "c) OrElse b = 9 OrElse b = 13 OrElse b = 10) Then
                    Return -1
                End If
            Next

            Return -1
        End Function

        Private Shared Function IndexBytes(bytes As Byte(), pattern As Byte(), from As Integer) As Integer
            For i As Integer = from To bytes.Length - pattern.Length
                Dim ok As Boolean = True
                For j As Integer = 0 To pattern.Length - 1
                    If bytes(i + j) <> pattern(j) Then
                        ok = False
                        Exit For
                    End If
                Next
                If ok Then Return i
            Next
            Return -1
        End Function

        ' ------------------------------------------------------------------
        '  数组读取
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' 读取某个数组的第 component 个分量（返回 VTK 布局的 Single 数组）。
        ''' </summary>
        Public Function ReadComponent(name As String, Optional component As Integer = 0) As Single()
            Dim arr As VtiArrayInfo = Find(name)
            If arr Is Nothing Then Return Nothing

            component = Math.Min(Math.Max(0, component), Math.Max(1, arr.Components) - 1)

            Dim n As Integer = VoxelCount
            Dim raw As Byte() = ReadRaw(arr)
            Dim expected As Long = CLng(n) * arr.BytesPerValue * Math.Max(1, arr.Components)

            If raw.LongLength < expected Then
                Throw New InvalidDataException(
                    $"'{m_path}' 数组 '{name}' 的数据长度不足：期望 {expected} 字节，实际 {raw.LongLength} 字节")
            End If

            Dim out(n - 1) As Single
            Dim stride As Integer = arr.BytesPerValue * arr.Components
            Dim baseOff As Integer = component * arr.BytesPerValue

            Select Case arr.TypeName
                Case "Float32"
                    For t As Integer = 0 To n - 1
                        out(t) = BitConverter.ToSingle(raw, t * stride + baseOff)
                    Next
                Case "Float64"
                    For t As Integer = 0 To n - 1
                        out(t) = CSng(BitConverter.ToDouble(raw, t * stride + baseOff))
                    Next
                Case "UInt8"
                    For t As Integer = 0 To n - 1
                        out(t) = raw(t * stride + baseOff)
                    Next
                Case "Int8"
                    For t As Integer = 0 To n - 1
                        out(t) = CSByte(raw(t * stride + baseOff))
                    Next
                Case "UInt16"
                    For t As Integer = 0 To n - 1
                        out(t) = BitConverter.ToUInt16(raw, t * stride + baseOff)
                    Next
                Case "Int16"
                    For t As Integer = 0 To n - 1
                        out(t) = BitConverter.ToInt16(raw, t * stride + baseOff)
                    Next
                Case "UInt32"
                    For t As Integer = 0 To n - 1
                        out(t) = BitConverter.ToUInt32(raw, t * stride + baseOff)
                    Next
                Case "Int32"
                    For t As Integer = 0 To n - 1
                        out(t) = BitConverter.ToInt32(raw, t * stride + baseOff)
                    Next
                Case "UInt64"
                    For t As Integer = 0 To n - 1
                        out(t) = CSng(BitConverter.ToUInt64(raw, t * stride + baseOff))
                    Next
                Case "Int64"
                    For t As Integer = 0 To n - 1
                        out(t) = CSng(BitConverter.ToInt64(raw, t * stride + baseOff))
                    Next
                Case Else
                    Throw New NotSupportedException($"'{m_path}' 数组 '{name}' 使用了不支持的类型: {arr.TypeName}")
            End Select

            Return out
        End Function

        ''' <summary>读取某个数组的原始字节（已解压）。</summary>
        Private Function ReadRaw(arr As VtiArrayInfo) As Byte()
            Using fs As New FileStream(m_path, FileMode.Open, FileAccess.Read, FileShare.Read)
                fs.Seek(m_binaryStart + arr.Offset, SeekOrigin.Begin)

                If Not m_compressed Then
                    Dim len As Integer = ReadInt32(fs)

                    If len < 0 OrElse fs.Position + len > fs.Length Then
                        Throw New InvalidDataException($"'{m_path}' 数组 '{arr.Name}' 的长度头非法: {len}")
                    End If

                    Dim buf(len - 1) As Byte
                    Call ReadExactly(fs, buf, 0, len)
                    Return buf
                End If

                Return ReadCompressed(fs)
            End Using
        End Function

        ''' <summary>
        ''' 读取 VTK zlib 分块压缩的一个数组：
        ''' [nBlocks][blockSize][lastBlockSize][各块压缩后大小][压缩数据]
        ''' </summary>
        Private Function ReadCompressed(fs As FileStream) As Byte()
            Dim nBlocks As Integer = ReadInt32(fs)
            Dim blockSize As Integer = ReadInt32(fs)
            Dim lastSize As Integer = ReadInt32(fs)

            If nBlocks <= 0 OrElse nBlocks > 1 << 24 Then
                Throw New InvalidDataException($"'{m_path}' 的压缩块数非法: {nBlocks}")
            End If

            Dim sizes(nBlocks - 1) As Integer
            For i As Integer = 0 To nBlocks - 1
                sizes(i) = ReadInt32(fs)
            Next

            Dim total As Long = CLng(nBlocks - 1) * blockSize + lastSize
            Dim out(CInt(total) - 1) As Byte
            Dim pos As Integer = 0

            For i As Integer = 0 To nBlocks - 1
                Dim chunk(sizes(i) - 1) As Byte
                Call ReadExactly(fs, chunk, 0, sizes(i))

                Dim inflated As Byte() = ZlibDecompress(chunk)

                If pos + inflated.Length > out.Length Then
                    Throw New InvalidDataException($"'{m_path}' 的压缩数据解压后越界")
                End If

                Buffer.BlockCopy(inflated, 0, out, pos, inflated.Length)
                pos += inflated.Length
            Next

            Return out
        End Function

        Private Shared Function ZlibDecompress(chunk As Byte()) As Byte()
            ' zlib: 2 字节头 + deflate + 4 字节 Adler32
            Using ms As New MemoryStream(chunk, 2, chunk.Length - 2, writable:=False)
                Using ds As New DeflateStream(ms, CompressionMode.Decompress)
                    Using out As New MemoryStream()
                        ds.CopyTo(out)
                        Return out.ToArray()
                    End Using
                End Using
            End Using
        End Function

        Private Shared Function ReadInt32(fs As FileStream) As Integer
            Dim b(3) As Byte
            Call ReadExactly(fs, b, 0, 4)
            Return BitConverter.ToInt32(b, 0)
        End Function

        Private Shared Sub ReadExactly(fs As FileStream, buffer As Byte(), offset As Integer, count As Integer)
            Dim done As Integer = 0

            While done < count
                Dim got As Integer = fs.Read(buffer, offset + done, count - done)
                If got <= 0 Then
                    Throw New EndOfStreamException("vti 文件在预期的数据之前结束")
                End If
                done += got
            End While
        End Sub

        ' ------------------------------------------------------------------
        '  帧加载
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' 读取一帧的数据。
        ''' </summary>
        ''' <param name="wanted">
        ''' 需要加载的字段（例如 pressure、velocity、gene_glc_fermenter[3]）；
        ''' 为 Nothing 时加载全部标量场。
        ''' </param>
        Public Function ReadFrame(Optional wanted As IEnumerable(Of String) = Nothing) As VtiFrameData
            Dim frame As New VtiFrameData With {.Nx = Nx, .Ny = Ny, .Nz = Nz}

            ' ---- 掩膜 ----
            Dim maskInfo = Find("mask")
            Dim maskSrc As Single() = Nothing

            If maskInfo IsNot Nothing Then
                maskSrc = ReadComponent("mask")
            End If

            Dim n As Integer = VoxelCount
            Dim maskVtk(n - 1) As Boolean

            If maskSrc IsNot Nothing Then
                For t As Integer = 0 To n - 1
                    maskVtk(t) = maskSrc(t) <> 0
                Next
            Else
                For t As Integer = 0 To n - 1
                    maskVtk(t) = True
                Next
            End If

            ' ---- 需要加载的字段 ----
            Dim plan As New List(Of KeyValuePair(Of String, Integer))

            If wanted Is Nothing Then
                For Each arr In m_arrays
                    If arr.Components <= 1 Then
                        plan.Add(New KeyValuePair(Of String, Integer)(arr.Name, 0))
                    Else
                        For c As Integer = 0 To arr.Components - 1
                            plan.Add(New KeyValuePair(Of String, Integer)(arr.Name, c))
                        Next
                    End If
                Next
            Else
                plan.AddRange(ParseWanted(wanted))
            End If

            For Each item In plan
                Dim name As String = FieldKey(item.Key, item.Value, Find(item.Key))
                If frame.Fields.ContainsKey(name) Then Continue For

                Dim src As Single() = ReadComponent(item.Key, item.Value)
                If src Is Nothing Then Continue For

                frame.Fields(name) = VtkToEngine(src, Nx, Ny, Nz)
            Next

            frame.Mask = VtkToEngine(maskVtk, Nx, Ny, Nz)

            Call FillVelocity(frame)

            Return frame
        End Function

        ''' <summary>把 "gene_x[3]" / "velocity" 这样的请求解析成 (数组名, 分量下标)。</summary>
        Private Function ParseWanted(wanted As IEnumerable(Of String)) As List(Of KeyValuePair(Of String, Integer))
            Dim plan As New List(Of KeyValuePair(Of String, Integer))

            For Each raw As String In wanted
                If raw Is Nothing Then Continue For

                Dim name As String = raw.Trim()
                Dim comp As Integer = 0
                Dim open As Integer = name.LastIndexOf("["c)

                If open > 0 AndAlso name.EndsWith("]") Then
                    Dim txt As String = name.Substring(open + 1, name.Length - open - 2)
                    If Integer.TryParse(txt, comp) Then
                        name = name.Substring(0, open)
                    Else
                        comp = 0
                    End If
                End If

                Dim arr As VtiArrayInfo = Find(name)
                If arr Is Nothing Then
                    ' 也可能是某个多分量数组的第 0 分量别名
                    Continue For
                End If

                Dim comps As Integer = Math.Max(1, arr.Components)

                If comp >= comps Then
                    comp = 0
                End If

                If comps > 1 Then
                    ' velocity 这类向量场：整场一起取，供 u/v/w 使用
                    For c As Integer = 0 To comps - 1
                        plan.Add(New KeyValuePair(Of String, Integer)(name, c))
                    Next
                Else
                    plan.Add(New KeyValuePair(Of String, Integer)(name, 0))
                End If
            Next

            Return plan
        End Function

        ''' <summary>多分量数组的第 i 分量的标量场名。</summary>
        Private Shared Function FieldKey(name As String, component As Integer, arr As VtiArrayInfo) As String
            If arr Is Nothing OrElse arr.Components <= 1 Then
                Return name
            End If
            Return $"{name}[{component}]"
        End Function

        ''' <summary>
        ''' 补齐 u/v/w 与 speed：优先用导出好的 speed 场，否则由 velocity 现算。
        ''' </summary>
        Private Sub FillVelocity(frame As VtiFrameData)
            Dim vel As VtiArrayInfo = Find("velocity")

            If vel IsNot Nothing AndAlso vel.Components >= 3 Then
                Dim u0 = frame.TryGet("velocity[0]")
                Dim v0 = frame.TryGet("velocity[1]")
                Dim w0 = frame.TryGet("velocity[2]")

                If u0 IsNot Nothing AndAlso v0 IsNot Nothing AndAlso w0 IsNot Nothing Then
                    frame.U = u0
                    frame.V = v0
                    frame.W = w0

                    Dim spd As Single() = frame.TryGet("speed")
                    If spd Is Nothing Then
                        Dim n As Integer = frame.Count
                        spd = New Single(n - 1) {}

                        For t As Integer = 0 To n - 1
                            Dim uu As Double = u0(t), vv As Double = v0(t), ww As Double = w0(t)
                            spd(t) = CSng(Math.Sqrt(uu * uu + vv * vv + ww * ww))
                        Next

                        frame.Fields("speed") = spd
                    End If

                    frame.Speed = spd
                End If
            End If
        End Sub

        ' ------------------------------------------------------------------
        '  布局重排
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' VTK 布局（x 最快，t = k*nx*ny + j*nx + i）→ 引擎布局（i*ny*nz + j*nz + k）。
        ''' </summary>
        Public Shared Function VtkToEngine(src As Single(), nx As Integer, ny As Integer, nz As Integer) As Single()
            Dim n As Integer = nx * ny * nz
            Dim dst(n - 1) As Single
            Dim plane As Integer = ny * nz
            Dim t As Integer = 0

            For k As Integer = 0 To nz - 1
                For j As Integer = 0 To ny - 1
                    For i As Integer = 0 To nx - 1
                        dst(i * plane + j * nz + k) = src(t)
                        t += 1
                    Next
                Next
            Next

            Return dst
        End Function

        Public Shared Function VtkToEngine(src As Boolean(), nx As Integer, ny As Integer, nz As Integer) As Boolean()
            Dim n As Integer = nx * ny * nz
            Dim dst(n - 1) As Boolean
            Dim plane As Integer = ny * nz
            Dim t As Integer = 0

            For k As Integer = 0 To nz - 1
                For j As Integer = 0 To ny - 1
                    For i As Integer = 0 To nx - 1
                        dst(i * plane + j * nz + k) = src(t)
                        t += 1
                    Next
                Next
            Next

            Return dst
        End Function

        ' ------------------------------------------------------------------
        '  字段清单（供上层生成可选标量场下拉）
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' 列出该文件中的全部可视化标量场（多分量数组展开为 name[i]）。
        ''' </summary>
        Public Function ListScalarFields() As List(Of String)
            Dim names As New List(Of String)()

            For Each arr In m_arrays
                If String.Equals(arr.Name, "mask", StringComparison.OrdinalIgnoreCase) Then Continue For
                If arr.Components <= 1 Then
                    names.Add(arr.Name)
                Else
                    For c As Integer = 0 To arr.Components - 1
                        names.Add(FieldKey(arr.Name, c, arr))
                    Next
                End If
            Next

            Return names
        End Function

        ''' <summary>该文件中是否存在某字段（含 name[i] 形式）。</summary>
        Public Function HasField(name As String) As Boolean
            Dim pair As KeyValuePair(Of String, Integer) = ParseOne(name)
            Return pair.Key IsNot Nothing
        End Function

        Private Function ParseOne(raw As String) As KeyValuePair(Of String, Integer)
            Dim plan = ParseWanted({raw})
            If plan.Count = 0 Then Return New KeyValuePair(Of String, Integer)(Nothing, -1)
            Return New KeyValuePair(Of String, Integer)(plan(0).Key, plan(0).Value)
        End Function

        ' ------------------------------------------------------------------
        '  头部正则
        ' ------------------------------------------------------------------

        Shared ReadOnly DataArrayPattern As New Regex(
            "<DataArray\s+type=""(?<type>[^""]+)""\s+Name=""(?<name>[^""]+)""(?<rest>[^>]*?)/>",
            RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Shared ReadOnly OffsetPattern As New Regex(
            "offset=""(?<offset>\d+)""", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Shared ReadOnly ComponentsPattern As New Regex(
            "NumberOfComponents=""(?<n>\d+)""", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Shared ReadOnly ExtentPattern As New Regex(
            "WholeExtent=""0\s+(?<nx>\d+)\s+0\s+(?<ny>\d+)\s+0\s+(?<nz>\d+)""",
            RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Shared ReadOnly CompressorPattern As New Regex(
            "compressor=""(?<c>[^""]+)""", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Shared ReadOnly Base64Pattern As New Regex(
            "encoding=""base64""", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

    End Class

    ''' <summary>
    ''' .vti 读取器的简化入口（保留与旧代码兼容的 Load 方法）。
    ''' </summary>
    Public Class VtiReader

        ''' <summary>
        ''' 读取一个 .vti 快照文件的全部标量场。
        ''' </summary>
        Public Shared Function Load(filePath As String) As VtiFrameData
            Return VtiFile.Parse(filePath).ReadFrame()
        End Function

        ''' <summary>只解析头部，得到字段清单。</summary>
        Public Shared Function ListFields(filePath As String) As List(Of String)
            Return VtiFile.Parse(filePath).ListScalarFields()
        End Function

    End Class

End Namespace
