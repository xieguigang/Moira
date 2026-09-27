' /********************************************************************************/
'
'   VTIExporter.vb
'
'   VTI 导出器 —— 把仿真结果导出为 VTK XML ImageData (.vti) 二进制文件
'
'   作用：
'       取代体积灾难的 JSON 快照（128³ 时约 360 MB/帧、200 帧约 70 GB），
'       改为 float32 二进制 + 去派生场。64³ 单帧约 5.5 MB，
'       可被 ParaView 与浏览器端的 VTK.js 直接读取。
'
'   ★ 可配置字段（扩展）：
'       原始的 Export(FluidField, ...) 只能输出 mask / pressure / density /
'       velocity 五个标准场，而耦合仿真里还有大量**生物学标量场**
'       （温度 / pH / 离子强度 / 各物种细胞数 / 各代谢物浓度 / 交叉喂养通量 /
'        每个基因的表达量……）需要在同一帧里与流场一起可视化。
'       本导出器因此改为"字段可配置"形态：
'           - <see cref="VtiField"/> 描述一个输出字段（名称 / 类型 / 分量数 / 数据）
'           - <see cref="Export(String, Integer, Integer, Integer, IEnumerable(Of VtiField), VoxelShape, Integer, Double, Boolean)"/>
'             可输出任意组合的字段
'           - <see cref="StandardFields"/> 给出标准五场，<see cref="Export(FluidField, ...)"/>
'             仍然走它，输出与改造前逐字节一致（零回归）
'
'   ★ 可选压缩（扩展）：
'       字段一多（几十个分量）、且固体体素占多数时，未压缩的 .vti 会达到
'       几十 MB/帧。开启 compress 后按 VTK 规范的 zlib 分块压缩写出
'       （compressor="vtkZLibDataCompressor"），零值密集的数据可缩到 1/3 ~ 1/10。
'       默认关闭：未压缩格式最稳妥，任何版本的 ParaView 都能读。
'
'   VTK 数据顺序约定：
'       VTK ImageData 要求 "x 最快"。与 legacy VTKExporter 保持一致，
'       声明 WholeExtent 为 (0..nx-1, 0..ny-1, 0..nz-1)，
'       并按 k(外) → j → i(内) 的顺序写出，使 x↔i、y↔j、z↔k，
'       与 ParaView 中看到的方位一致。
'       调用方传入的数据按**引擎布局** idx = i*(ny*nz) + j*nz + k 组织，
'       由本导出器负责重排与固体归零。
'
'   文件格式：
'       <VTKFile type="ImageData" header_type="UInt32">
'       AppendedData encoding="raw"，每个数组前有一个 UInt32 长度头（字节数），
'       数组在 appended 段中的偏移写入对应 DataArray 的 offset 属性。
'       压缩模式下该长度头被"分块头"取代：
'         [nBlocks][blockSize][lastBlockSize][压缩后块大小 × nBlocks][压缩数据]
'
'   使用方法：
'       VTIExporter.Export(tank.Field, "frame_0000.vti", step:=10, time:=1.0)   ' 标准五场
'       VTIExporter.Export("frame_0000.vti", nx, ny, nz, fields, shape)         ' 任意字段组合
'
' /********************************************************************************/

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports std = System.Math

Namespace Snapshot

    ''' <summary>VTI 字段的数据类型</summary>
    Public Enum VtiFieldType

        ''' <summary>32 位浮点（通用物理量 / 生物学量）</summary>
        Float32

        ''' <summary>8 位无符号整数（如活动体素掩膜）</summary>
        UInt8
    End Enum

    ''' <summary>
    ''' 一个待输出的 VTI 字段。
    ''' </summary>
    ''' <remarks>
    ''' 数据按**引擎布局**给出：体素 idx = i*(ny*nz) + j*nz + k，
    ''' 多分量字段在该索引处连续存放 components 个值。
    ''' 三种数据载体按 <see cref="Type"/> 选用其一：
    '''   * Float32 → <see cref="Data32"/>
    '''   * UInt8   → <see cref="Data8"/>
    '''   * 也可以只给 <see cref="Data64"/>，导出器会降到 Float32（生物学量常用 Double）
    ''' 数据不足时缺省补 0，因此调用方可以为稀疏量（例如只有少数格点有细胞）
    ''' 只填非零部分。
    ''' </remarks>
    Public Class VtiField

        ''' <summary>字段名（ParaView 中显示的属性名，建议只含 ASCII 字母数字与下划线）</summary>
        Public Property Name As String = "field"

        ''' <summary>数据类型</summary>
        Public Property Type As VtiFieldType = VtiFieldType.Float32

        ''' <summary>分量数（1 = 标量，3 = 向量，N = 逐基因的向量场）</summary>
        Public Property Components As Integer = 1

        ''' <summary>Float32 数据，长度 ≥ n × Components</summary>
        Public Property Data32 As Single() = Nothing

        ''' <summary>Float64 数据，长度 ≥ n × Components（导出时降到 Float32）</summary>
        Public Property Data64 As Double() = Nothing

        ''' <summary>UInt8 数据，长度 ≥ n × Components</summary>
        Public Property Data8 As Byte() = Nothing

        ''' <summary>每个分量值占用的字节数</summary>
        Public ReadOnly Property BytesPerValue As Integer
            Get
                Return If(Type = VtiFieldType.UInt8, 1, 4)
            End Get
        End Property

        ''' <summary>创建一个 Float32 标量场</summary>
        Public Shared Function Scalar(name As String, data As Single()) As VtiField
            Return New VtiField With {.Name = name, .Type = VtiFieldType.Float32, .Components = 1, .Data32 = data}
        End Function

        ''' <summary>创建一个 Float64 标量场（导出时降到 Float32）</summary>
        Public Shared Function Scalar64(name As String, data As Double()) As VtiField
            Return New VtiField With {.Name = name, .Type = VtiFieldType.Float32, .Components = 1, .Data64 = data}
        End Function

        ''' <summary>创建一个多分量 Float64 场（如逐基因表达向量）</summary>
        Public Shared Function Vector64(name As String, components As Integer, data As Double()) As VtiField
            Return New VtiField With {
                .Name = name, .Type = VtiFieldType.Float32, .Components = components, .Data64 = data}
        End Function

        ''' <summary>创建一个 UInt8 掩膜场</summary>
        Public Shared Function Mask(name As String, data As Byte()) As VtiField
            Return New VtiField With {.Name = name, .Type = VtiFieldType.UInt8, .Components = 1, .Data8 = data}
        End Function
    End Class

    ''' <summary>
    ''' 把 CFD 仿真结果导出为二进制 VTK XML ImageData (.vti) 文件。
    ''' 仅依赖 FluidField，与引擎对象解耦；同时支持任意可配置字段组合与可选压缩。
    ''' </summary>
    Public Class VTIExporter

        ''' <summary>压缩分块大小（未压缩字节数/块），与 VTK 默认一致</summary>
        Private Const BlockSize As Integer = 32768

        ''' <summary>
        ''' 导出整个流体场到 .vti 文件（标准五场：mask / pressure / density / velocity）。
        ''' </summary>
        ''' <remarks>输出与"字段可配置"改造前逐字节一致，既有调用方零回归。</remarks>
        Public Shared Sub Export(field As FluidField, filePath As String,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            If field Is Nothing Then Throw New ArgumentNullException(NameOf(field))

            Call Export(filePath, field.Nx, field.Ny, field.Nz,
                        StandardFields(field), field.Shape, stepIndex, time, compress:=False)
        End Sub

        ''' <summary>导出快照到 .vti 文件（重载，便于逐帧导出）。</summary>
        Public Shared Sub Export(snapshot As Snapshot, filePath As String)
            Export(snapshot.Field, filePath, snapshot.StepIndex, snapshot.Time)
        End Sub

        ''' <summary>
        ''' 标准五场：mask(UInt8) / pressure / density / velocity(Float32×3)。
        ''' </summary>
        Public Shared Function StandardFields(field As FluidField) As List(Of VtiField)
            Dim nx = field.Nx
            Dim ny = field.Ny
            Dim nz = field.Nz
            Dim n = nx * ny * nz

            Dim maskBuf = New Byte(n - 1) {}
            Dim pBuf = New Single(n - 1) {}
            Dim dBuf = New Single(n - 1) {}
            Dim velBuf = New Single(3 * n - 1) {}

            ' 这里填的是引擎布局，重排与固体归零由 Export 统一处理
            For idx As Integer = 0 To n - 1
                maskBuf(idx) = 1
                pBuf(idx) = field.Pressure.Data(idx)
                dBuf(idx) = field.Density.Data(idx)
                velBuf(3 * idx) = field.U.Data(idx)
                velBuf(3 * idx + 1) = field.V.Data(idx)
                velBuf(3 * idx + 2) = field.W.Data(idx)
            Next

            Return New List(Of VtiField) From {
                VtiField.Mask("mask", maskBuf),
                VtiField.Scalar("pressure", pBuf),
                VtiField.Scalar("density", dBuf),
                New VtiField With {.Name = "velocity", .Type = VtiFieldType.Float32,
                                   .Components = 3, .Data32 = velBuf}
            }
        End Function

        ''' <summary>
        ''' 把**任意组合的字段**导出为 .vti 文件（本次扩展的核心入口）。
        ''' </summary>
        ''' <param name="filePath">输出文件路径</param>
        ''' <param name="nx">X 方向格点数</param>
        ''' <param name="ny">Y 方向格点数</param>
        ''' <param name="nz">Z 方向格点数</param>
        ''' <param name="fields">待输出的字段（数据在引擎布局下给出）</param>
        ''' <param name="shape">体素形状；非活动（固体）体素的所有字段归零</param>
        ''' <param name="stepIndex">时间步序号（供调用方生成索引，可选）</param>
        ''' <param name="time">模拟时间（供调用方生成索引，可选）</param>
        ''' <param name="compress">是否按 VTK 规范做 zlib 分块压缩（默认关闭）</param>
        Public Shared Sub Export(filePath As String,
                                 nx As Integer, ny As Integer, nz As Integer,
                                 fields As IEnumerable(Of VtiField),
                                 Optional shape As VoxelShape = Nothing,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0,
                                 Optional compress As Boolean = False)

            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Dim list As List(Of VtiField) = fields.ToList()

            If list.Count = 0 Then
                Throw New ArgumentException("至少需要一个输出字段", NameOf(fields))
            End If

            Dim n = nx * ny * nz
            Dim plane = ny * nz

            ' ---- 预计算"引擎布局 → VTK 布局"的下标映射 + 活动掩膜 ----
            ' 逐帧、逐字段都要用它：一次建表、之后每个字段只需一次平坦循环 + 块拷贝，
            ' 避免三重嵌套 × 逐值 BitConverter 的开销（数十字段 × 十几万体素 × 上百帧）。
            Dim tOf = New Integer(n - 1) {}
            Dim active = New Boolean(n - 1) {}

            For k As Integer = 0 To nz - 1
                For j As Integer = 0 To ny - 1
                    For i As Integer = 0 To nx - 1
                        Dim idx = i * plane + j * nz + k

                        tOf(idx) = (k * ny + j) * nx + i
                        active(idx) = (shape Is Nothing) OrElse shape.IsActive(i, j, k)
                    Next
                Next
            Next

            ' ---- 逐字段重排到 VTK 顺序，非活动体素保持 0 ----
            Dim payloads As New List(Of Byte())(list.Count)
            Dim offsets = New Integer(list.Count - 1) {}

            Dim cursor As Integer = 0

            For f As Integer = 0 To list.Count - 1
                Dim field = list(f)
                Dim comps = std.Max(1, field.Components)
                Dim bytesPer = field.BytesPerValue
                Dim buf = New Byte(n * comps * bytesPer - 1) {}

                If bytesPer = 1 Then
                    Dim src8 As Byte() = field.Data8

                    If src8 IsNot Nothing Then
                        For idx As Integer = 0 To n - 1
                            If Not active(idx) Then Continue For

                            Array.Copy(src8, idx * comps, buf, tOf(idx) * comps, comps)
                        Next
                    End If
                Else
                    ' Float64 → Float32 只转换一次（整块），之后按体素块拷贝
                    Dim src32 As Single() = field.Data32

                    If src32 Is Nothing AndAlso field.Data64 IsNot Nothing Then
                        Dim d = field.Data64
                        src32 = New Single(d.Length - 1) {}

                        For i As Integer = 0 To d.Length - 1
                            src32(i) = CSng(d(i))
                        Next
                    End If

                    If src32 IsNot Nothing Then
                        For idx As Integer = 0 To n - 1
                            If Not active(idx) Then Continue For

                            Buffer.BlockCopy(src32, idx * comps * 4, buf, tOf(idx) * comps * 4, comps * 4)
                        Next
                    End If
                End If

                ' 未压缩：[UInt32 长度][数据]；压缩：[分块头][压缩数据]
                Dim payload As Byte()

                If compress Then
                    payload = CompressBlob(buf)
                Else
                    payload = New Byte(4 + buf.Length - 1) {}
                    WriteUInt32(payload, 0, CUInt(buf.Length))
                    Array.Copy(buf, 0, payload, 4, buf.Length)
                End If

                offsets(f) = cursor
                cursor += payload.Length

                payloads.Add(payload)
            Next

            ' ---- XML 头（'_' 之后紧接二进制，中间不能有换行）----
            Dim sb As New StringBuilder()
            sb.AppendLine("<?xml version=""1.0""?>")
            sb.AppendLine("<VTKFile type=""ImageData"" version=""1.0"" byte_order=""LittleEndian"" header_type=""UInt32"">")
            sb.AppendLine($"  <ImageData WholeExtent=""0 {nx - 1} 0 {ny - 1} 0 {nz - 1}"" Origin=""0 0 0"" Spacing=""1 1 1"">")
            sb.AppendLine($"    <Piece Extent=""0 {nx - 1} 0 {ny - 1} 0 {nz - 1}"">")
            sb.AppendLine("      <PointData>")

            For f As Integer = 0 To list.Count - 1
                Dim field = list(f)
                Dim typeName = If(field.Type = VtiFieldType.UInt8, "UInt8", "Float32")
                Dim comps = std.Max(1, field.Components)
                Dim compAttr = If(comps > 1, $" NumberOfComponents=""{comps}""", "")

                sb.AppendLine($"        <DataArray type=""{typeName}"" Name=""{field.Name}""{compAttr} format=""appended"" offset=""{offsets(f)}""/>")
            Next

            sb.AppendLine("      </PointData>")
            sb.AppendLine("      <CellData/>")
            sb.AppendLine("    </Piece>")
            sb.AppendLine("  </ImageData>")

            If compress Then
                sb.AppendLine("  <AppendedData encoding=""raw"" compressor=""vtkZLibDataCompressor"">")
            Else
                sb.AppendLine("  <AppendedData encoding=""raw"">")
            End If

            sb.Append("    _")

            Using fs As New FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None)
                Dim headBytes = Encoding.ASCII.GetBytes(sb.ToString())
                fs.Write(headBytes, 0, headBytes.Length)

                Using bw As New BinaryWriter(fs, Encoding.ASCII, leaveOpen:=True)
                    ' 顺序必须与 XML 中声明的 offset 一致
                    For f As Integer = 0 To list.Count - 1
                        bw.Write(payloads(f))
                    Next
                End Using

                Dim tail = Encoding.ASCII.GetBytes(vbLf & "  </AppendedData>" & vbLf & "</VTKFile>" & vbLf)
                fs.Write(tail, 0, tail.Length)
            End Using
        End Sub

#Region "压缩（VTK zlib 分块格式）"

        ''' <summary>
        ''' 把一个数组打包成 VTK 压缩分块格式：
        ''' [nBlocks][blockSize][lastBlockSize][各块压缩后大小][压缩数据]
        ''' </summary>
        Private Shared Function CompressBlob(data As Byte()) As Byte()
            Dim n As Integer = data.Length
            Dim nBlocks As Integer = std.Max(1, (n + BlockSize - 1) \ BlockSize)
            Dim lastSize As Integer = n - (nBlocks - 1) * BlockSize

            If lastSize <= 0 Then
                lastSize = If(n > 0, std.Min(BlockSize, n), 0)
            End If

            Dim blocks As New List(Of Byte())(nBlocks)

            For b As Integer = 0 To nBlocks - 1
                Dim start As Integer = b * BlockSize
                Dim len As Integer = std.Max(0, std.Min(BlockSize, n - start))
                Dim chunk = New Byte(std.Max(0, len) - 1) {}

                If len > 0 Then
                    Array.Copy(data, start, chunk, 0, len)
                End If

                blocks.Add(ZlibCompress(chunk))
            Next

            Dim total As Integer = 4 * (3 + nBlocks)

            For Each block As Byte() In blocks
                total += block.Length
            Next

            Dim out = New Byte(total - 1) {}
            Dim pos As Integer = 0

            WriteUInt32(out, pos, CUInt(nBlocks)) : pos += 4
            WriteUInt32(out, pos, CUInt(BlockSize)) : pos += 4
            WriteUInt32(out, pos, CUInt(lastSize)) : pos += 4

            For Each block As Byte() In blocks
                WriteUInt32(out, pos, CUInt(block.Length)) : pos += 4
            Next

            For Each block As Byte() In blocks
                Array.Copy(block, 0, out, pos, block.Length)
                pos += block.Length
            Next

            Return out
        End Function

        ''' <summary>
        ''' zlib 包装的 deflate（2 字节 zlib 头 + 原始 deflate + Adler32），
        ''' 与 VTK 的 vtkZLibDataCompressor 输出格式一致。
        ''' </summary>
        Private Shared Function ZlibCompress(data As Byte()) As Byte()
            Using ms As New MemoryStream()
                ms.WriteByte(&H78)
                ms.WriteByte(&H9C)

                Using ds As New DeflateStream(ms, CompressionLevel.Fastest, leaveOpen:=True)
                    ds.Write(data, 0, data.Length)
                End Using

                Dim adler As UInteger = Adler32(data)

                ms.WriteByte(CByte((adler >> 24) And &HFFUI))
                ms.WriteByte(CByte((adler >> 16) And &HFFUI))
                ms.WriteByte(CByte((adler >> 8) And &HFFUI))
                ms.WriteByte(CByte(adler And &HFFUI))

                Return ms.ToArray()
            End Using
        End Function

        Private Shared Function Adler32(data As Byte()) As UInteger
            Dim a As UInteger = 1
            Dim b As UInteger = 0

            For Each value As Byte In data
                a = (a + value) Mod 65521UI
                b = (b + a) Mod 65521UI
            Next

            Return (b << 16) Or a
        End Function

        Private Shared Sub WriteUInt32(buffer As Byte(), offset As Integer, value As UInteger)
            buffer(offset) = CByte(value And &HFFUI)
            buffer(offset + 1) = CByte((value >> 8) And &HFFUI)
            buffer(offset + 2) = CByte((value >> 16) And &HFFUI)
            buffer(offset + 3) = CByte((value >> 24) And &HFFUI)
        End Sub

#End Region

    End Class

End Namespace
