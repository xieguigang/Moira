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
'   ★ 可配置字段（本次扩展）：
'       原始的 Export(FluidField, ...) 只能输出 mask / pressure / density /
'       velocity 五个标准场，而耦合仿真里还有大量**生物学标量场**
'       （温度 / pH / 离子强度 / 各物种细胞数 / 各代谢物浓度 / 交叉喂养通量 /
'        每个基因的表达量……）需要在同一帧里与流场一起可视化。
'       本导出器因此改为"字段可配置"形态：
'           - <see cref="VtiField"/> 描述一个输出字段（名称 / 类型 / 分量数 / 数据）
'           - <see cref="Export(String, Integer, Integer, Integer, IEnumerable(Of VtiField), VoxelShape, Integer, Double)"/>
'             可输出任意组合的字段
'           - <see cref="StandardFields"/> 给出标准五场，<see cref="Export(FluidField, ...)"/>
'             仍然走它，输出与改造前逐字节一致（零回归）
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
'
'   使用方法：
'       VTIExporter.Export(tank.Field, "frame_0000.vti", step:=10, time:=1.0)   ' 标准五场
'       VTIExporter.Export("frame_0000.vti", nx, ny, nz, fields, shape)         ' 任意字段组合
'
' /********************************************************************************/

Imports System.IO
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
    ''' 仅依赖 FluidField，与引擎对象解耦；同时支持任意可配置字段组合。
    ''' </summary>
    Public Class VTIExporter

        ''' <summary>
        ''' 导出整个流体场到 .vti 文件（标准五场：mask / pressure / density / velocity）。
        ''' </summary>
        ''' <remarks>输出与"字段可配置"改造前逐字节一致，既有调用方零回归。</remarks>
        Public Shared Sub Export(field As FluidField, filePath As String,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            If field Is Nothing Then Throw New ArgumentNullException(NameOf(field))

            Call Export(filePath, field.Nx, field.Ny, field.Nz,
                        StandardFields(field), field.Shape, stepIndex, time)
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
        Public Shared Sub Export(filePath As String,
                                 nx As Integer, ny As Integer, nz As Integer,
                                 fields As IEnumerable(Of VtiField),
                                 Optional shape As VoxelShape = Nothing,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Dim list As List(Of VtiField) = fields.ToList()

            If list.Count = 0 Then
                Throw New ArgumentException("至少需要一个输出字段", NameOf(fields))
            End If

            Dim n = nx * ny * nz
            Dim plane = ny * nz

            ' ---- 重排到 VTK 顺序（i 最内层），同时按掩膜把固体归零 ----
            Dim buffers As New List(Of Byte())(list.Count)
            Dim offsets = New Integer(list.Count - 1) {}

            Dim cursor As Integer = 0

            For f As Integer = 0 To list.Count - 1
                Dim field = list(f)
                Dim comps = std.Max(1, field.Components)
                Dim bytesPer = field.BytesPerValue
                Dim buf = New Byte(n * comps * bytesPer - 1) {}

                ' 预取：Data64 需要降到 Float32
                Dim src32 As Single() = field.Data32
                Dim src64 As Double() = field.Data64
                Dim src8 As Byte() = field.Data8

                Dim isActive As Boolean = True

                For k As Integer = 0 To nz - 1
                    For j As Integer = 0 To ny - 1
                        For i As Integer = 0 To nx - 1
                            Dim idx = i * plane + j * nz + k
                            Dim t = (k * ny + j) * nx + i

                            If shape IsNot Nothing Then
                                isActive = shape.IsActive(i, j, k)
                            End If

                            If Not isActive Then
                                Continue For
                            End If

                            Dim srcBase = idx * comps
                            Dim dstBase = t * comps * bytesPer

                            If bytesPer = 1 Then
                                For c As Integer = 0 To comps - 1
                                    If src8 IsNot Nothing AndAlso srcBase + c < src8.Length Then
                                        buf(dstBase + c) = src8(srcBase + c)
                                    End If
                                Next
                            Else
                                For c As Integer = 0 To comps - 1
                                    Dim value As Single = 0.0F

                                    If src32 IsNot Nothing Then
                                        If srcBase + c < src32.Length Then value = src32(srcBase + c)
                                    ElseIf src64 IsNot Nothing Then
                                        If srcBase + c < src64.Length Then value = CSng(src64(srcBase + c))
                                    End If

                                    Dim bytes = BitConverter.GetBytes(value)

                                    For b As Integer = 0 To 3
                                        buf(dstBase + c * 4 + b) = bytes(b)
                                    Next
                                Next
                            End If
                        Next
                    Next
                Next

                offsets(f) = cursor
                cursor += 4 + buf.Length

                buffers.Add(buf)
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
            sb.AppendLine("  <AppendedData encoding=""raw"">")
            sb.Append("    _")

            Using fs As New FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None)
                Dim headBytes = Encoding.ASCII.GetBytes(sb.ToString())
                fs.Write(headBytes, 0, headBytes.Length)

                Using bw As New BinaryWriter(fs, Encoding.ASCII, leaveOpen:=True)
                    ' 顺序必须与 XML 中声明的 offset 一致
                    For f As Integer = 0 To list.Count - 1
                        bw.Write(CUInt(buffers(f).Length))
                        bw.Write(buffers(f))
                    Next
                End Using

                Dim tail = Encoding.ASCII.GetBytes(vbLf & "  </AppendedData>" & vbLf & "</VTKFile>" & vbLf)
                fs.Write(tail, 0, tail.Length)
            End Using
        End Sub

    End Class

End Namespace
