' /********************************************************************************/
'
'   VtiReader.vb
'
'   VTK XML ImageData (.vti) 二进制解析器
'
'   作用：
'       与 CFDEngine.Snapshot.VTIExporter 的写出格式互逆，读取单帧 .vti
'       快照文件，还原出引擎索引顺序（i*ny*nz + j*nz + k）的标量场与
'       速度场数据，并现算 |V| (speed)。
'
'   文件格式（见 VTIExporter.vb）：
'       - ASCII XML 头：WholeExtent 声明网格维度，PointData 中的 DataArray
'         声明各数组（mask/UInt8、pressure/density/Float32、velocity/3xFloat32）
'         及其在 appended 段中的偏移
'       - <AppendedData encoding="raw"> 之后的 "_" 标记紧接二进制数据，
'         每个数组前有一个 UInt32 长度头（字节数）
'       - VTI 内部数据顺序为 x 最快：t = k*nx*ny + j*nx + i
'         （写出循环为 k(外) → j → i(内)）
'
' /********************************************************************************/

Imports System.IO
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

        Public Property Pressure As Single()
        Public Property Density As Single()
        Public Property U As Single()
        Public Property V As Single()
        Public Property W As Single()

        ''' <summary>速度模长 |V| = sqrt(u²+v²+w²)，由速度分量现算。</summary>
        Public Property Speed As Single()

    End Class

    ''' <summary>
    ''' .vti 快照文件读取器。
    ''' </summary>
    Public Class VtiReader

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

        ''' <summary>
        ''' 读取一个 .vti 快照文件。
        ''' </summary>
        Public Shared Function Load(filePath As String) As VtiFrameData
            Dim bytes As Byte() = File.ReadAllBytes(filePath)
            Dim start As Integer = LocateBinaryStart(bytes)
            If start < 0 Then
                Throw New InvalidDataException($"'{filePath}' 中未找到 AppendedData 段的二进制起始标记 '_'")
            End If

            ' ---- 解析 ASCII XML 头 ----
            Dim header As String = Encoding.ASCII.GetString(bytes, 0, start)

            Dim extent = ExtentPattern.Match(header)
            If Not extent.Success Then
                Throw New InvalidDataException($"'{filePath}' 中未找到 WholeExtent 声明")
            End If

            Dim frame As New VtiFrameData With {
                .Nx = Integer.Parse(extent.Groups("nx").Value) + 1,
                .Ny = Integer.Parse(extent.Groups("ny").Value) + 1,
                .Nz = Integer.Parse(extent.Groups("nz").Value) + 1
            }

            Dim nx = frame.Nx, ny = frame.Ny, nz = frame.Nz
            Dim n As Integer = nx * ny * nz
            Dim plane As Integer = ny * nz

            ' ---- 收集各 DataArray 的（名称 → 类型, 偏移）----
            Dim offsets As New Dictionary(Of String, Long)
            Dim components As New Dictionary(Of String, Integer)

            For Each m As Match In DataArrayPattern.Matches(header)
                Dim name As String = m.Groups("name").Value
                Dim type As String = m.Groups("type").Value
                Dim om = OffsetPattern.Match(m.Groups("rest").Value)
                If Not om.Success Then
                    Continue For
                End If

                offsets(name) = Long.Parse(om.Groups("offset").Value)

                Dim cm = ComponentsPattern.Match(m.Groups("rest").Value)
                components(name) = If(cm.Success, Integer.Parse(cm.Groups("n").Value), 1)

                ' 类型校验
                Select Case type
                    Case "UInt8", "UInt32", "Float32" : ' 支持的类型
                    Case Else
                        Throw New InvalidDataException($"'{filePath}' 数组 '{name}' 使用了不支持的类型: {type}")
                End Select
            Next

            For Each required As String In {"mask", "pressure", "density", "velocity"}
                If Not offsets.ContainsKey(required) Then
                    Throw New InvalidDataException($"'{filePath}' 中缺少 DataArray: {required}")
                End If
            Next

            ' ---- 按偏移读取二进制数组 ----
            Dim maskVtk As Byte() = ReadArray(bytes, start + offsets("mask"), n)
            Dim pressureVtk As Single() = ReadFloatArray(bytes, start + offsets("pressure"), n)
            Dim densityVtk As Single() = ReadFloatArray(bytes, start + offsets("density"), n)
            Dim velVtk As Single() = ReadFloatArray(bytes, start + offsets("velocity"), 3 * n)

            ' ---- 重排 VTK 序(t = k*nx*ny + j*nx + i) → 引擎序(i*ny*nz + j*nz + k) ----
            Dim mask(n - 1) As Boolean
            Dim pressure(n - 1) As Single
            Dim density(n - 1) As Single
            Dim u(n - 1) As Single
            Dim v(n - 1) As Single
            Dim w(n - 1) As Single
            Dim speed(n - 1) As Single

            Dim t As Integer = 0
            For k As Integer = 0 To nz - 1
                For j As Integer = 0 To ny - 1
                    For i As Integer = 0 To nx - 1
                        Dim dst As Integer = i * plane + j * nz + k

                        If maskVtk(t) <> 0 Then
                            mask(dst) = True
                            pressure(dst) = pressureVtk(t)
                            density(dst) = densityVtk(t)
                            u(dst) = velVtk(3 * t)
                            v(dst) = velVtk(3 * t + 1)
                            w(dst) = velVtk(3 * t + 2)

                            Dim uu As Double = u(dst), vv As Double = v(dst), ww As Double = w(dst)
                            speed(dst) = CSng(Math.Sqrt(uu * uu + vv * vv + ww * ww))
                        End If

                        t += 1
                    Next
                Next
            Next

            frame.Mask = mask
            frame.Pressure = pressure
            frame.Density = density
            frame.U = u
            frame.V = v
            frame.W = w
            frame.Speed = speed

            Return frame
        End Function

        ''' <summary>
        ''' 定位 appended 二进制段的起始位置（"_" 标记之后）。
        ''' </summary>
        Private Shared Function LocateBinaryStart(bytes As Byte()) As Integer
            ' 在 ASCII 头中查找 <AppendedData ...> 标签，
            ' 跳过属性文本与标签闭合 '>'，其后（允许空白）第一个 "_" 即二进制起始标记
            Dim tag As Byte() = Encoding.ASCII.GetBytes("<AppendedData")
            Dim pos As Integer = IndexOf(bytes, tag, 0)
            If pos < 0 Then
                Return -1
            End If

            ' 找到 <AppendedData ...> 标签的闭合 '>'
            Dim tagEnd As Integer = -1
            For i As Integer = pos + tag.Length To bytes.Length - 1
                If bytes(i) = CByte(AscW(">"c)) Then
                    tagEnd = i
                    Exit For
                End If
            Next

            If tagEnd < 0 Then
                Return -1
            End If

            For i As Integer = tagEnd + 1 To bytes.Length - 1
                Dim b As Byte = bytes(i)

                If b = CByte(AscW("_"c)) Then
                    Return i + 1
                ElseIf Not (b = CByte(AscW(" "c)) OrElse b = CByte(AscW(vbTab)) OrElse
                            b = CByte(AscW(vbCr)) OrElse b = CByte(AscW(vbLf))) Then
                    ' 闭合 '>' 与 "_" 之间只允许空白
                    Return -1
                End If
            Next

            Return -1
        End Function

        Private Shared Function IndexOf(bytes As Byte(), pattern As Byte(), start As Integer) As Integer
            For i As Integer = start To bytes.Length - pattern.Length
                Dim ok As Boolean = True
                For j As Integer = 0 To pattern.Length - 1
                    If bytes(i + j) <> pattern(j) Then
                        ok = False
                        Exit For
                    End If
                Next
                If ok Then
                    Return i
                End If
            Next
            Return -1
        End Function

        ''' <summary>读取一个 [UInt32 长度头 + UInt8 数组]。</summary>
        Private Shared Function ReadArray(bytes As Byte(), offset As Long, count As Integer) As Byte()
            Dim length As Integer = BitConverter.ToInt32(bytes, CInt(offset))
            If length < count Then
                Throw New InvalidDataException("appended 数组长度头与声明不符")
            End If

            Dim data(count - 1) As Byte
            Buffer.BlockCopy(bytes, CInt(offset + 4), data, 0, count)
            Return data
        End Function

        ''' <summary>读取一个 [UInt32 长度头 + Float32 数组]。</summary>
        Private Shared Function ReadFloatArray(bytes As Byte(), offset As Long, count As Integer) As Single()
            Dim length As Integer = BitConverter.ToInt32(bytes, CInt(offset))
            If length < count * 4 Then
                Throw New InvalidDataException("appended 数组长度头与声明不符")
            End If

            Dim data As Single() = New Single(count - 1) {}
            Buffer.BlockCopy(bytes, CInt(offset + 4), data, 0, count * 4)
            Return data
        End Function

    End Class

End Namespace
