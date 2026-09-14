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
'   与 legacy VTKExporter 的区别：
'       - legacy 是 ASCII 文本（每个数值约 19 字符）
'       - 本导出器是 appended 二进制（每个数值固定 4 字节），体积约 1/5
'       - 只落 5 个分量：mask(UInt8)、pressure、density、velocity(Float32×3)
'         speed 可由 u/v/w 现算，不再落盘
'
'   VTK 数据顺序约定：
'       VTK ImageData 要求 "x 最快"。与 legacy VTKExporter 保持一致，
'       声明 WholeExtent 为 (0..nx-1, 0..ny-1, 0..nz-1)，
'       并按 k(外) → j → i(内) 的顺序写出，使 x↔i、y↔j、z↔k，
'       与 ParaView 中看到的方位一致。
'
'   文件格式：
'       <VTKFile type="ImageData" header_type="UInt32">
'       AppendedData encoding="raw"，每个数组前有一个 UInt32 长度头（字节数），
'       数组在 appended 段中的偏移写入对应 DataArray 的 offset 属性。
'
'   使用方法：
'       VTIExporter.Export(tank.Field, "frame_0000.vti", step:=10, time:=1.0)
'
' /********************************************************************************/

Imports System.IO
Imports System.Text

Namespace Snapshot

    ''' <summary>
    ''' 把 CFD 仿真结果导出为二进制 VTK XML ImageData (.vti) 文件。
    ''' 仅依赖 FluidField，与引擎对象解耦。
    ''' </summary>
    Public Class VTIExporter

        ''' <summary>
        ''' 导出整个流体场到 .vti 文件。
        ''' </summary>
        ''' <param name="field">流体场数据</param>
        ''' <param name="filePath">输出文件路径</param>
        ''' <param name="stepIndex">时间步序号（供调用方生成索引，可选）</param>
        ''' <param name="time">模拟时间（供调用方生成索引，可选）</param>
        Public Shared Sub Export(field As FluidField, filePath As String,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            Dim f = field
            Dim nx = f.Nx
            Dim ny = f.Ny
            Dim nz = f.Nz
            Dim n = nx * ny * nz
            Dim plane = ny * nz

            Dim u = f.U.Data
            Dim v = f.V.Data
            Dim w = f.W.Data
            Dim p = f.Pressure.Data
            Dim d = f.Density.Data
            Dim shape = f.Shape

            ' ---- 重排到 VTK 顺序（i 最内层），同时按掩膜把固体归零 ----
            Dim maskBuf = New Byte(n - 1) {}
            Dim pBuf = New Single(n - 1) {}
            Dim dBuf = New Single(n - 1) {}
            Dim velBuf = New Single(3 * n - 1) {}

            Dim t = 0
            For k = 0 To nz - 1
                For j = 0 To ny - 1
                    For i = 0 To nx - 1
                        Dim idx = i * plane + j * nz + k

                        If shape Is Nothing OrElse shape.IsActive(i, j, k) Then
                            maskBuf(t) = 1
                            pBuf(t) = p(idx)
                            dBuf(t) = d(idx)
                            velBuf(3 * t) = u(idx)
                            velBuf(3 * t + 1) = v(idx)
                            velBuf(3 * t + 2) = w(idx)
                        End If
                        ' 非活动（固体）体素保持 0：mask=0 且各物理量=0，
                        ' 体绘制时天然被掩掉，无需在前端额外做阈值处理

                        t += 1
                    Next
                Next
            Next

            ' ---- 各数组在 appended 段中的偏移（每个数组前有一个 UInt32 长度头）----
            Dim offMask = 0
            Dim offP = 4 + n
            Dim offD = offP + 4 + 4 * n
            Dim offVel = offD + 4 + 4 * n

            ' ---- XML 头（'_' 之后紧接二进制，中间不能有换行）----
            Dim sb As New StringBuilder()
            sb.AppendLine("<?xml version=""1.0""?>")
            sb.AppendLine("<VTKFile type=""ImageData"" version=""1.0"" byte_order=""LittleEndian"" header_type=""UInt32"">")
            sb.AppendLine($"  <ImageData WholeExtent=""0 {nx - 1} 0 {ny - 1} 0 {nz - 1}"" Origin=""0 0 0"" Spacing=""1 1 1"">")
            sb.AppendLine($"    <Piece Extent=""0 {nx - 1} 0 {ny - 1} 0 {nz - 1}"">")
            sb.AppendLine("      <PointData>")
            sb.AppendLine($"        <DataArray type=""UInt8"" Name=""mask"" format=""appended"" offset=""{offMask}""/>")
            sb.AppendLine($"        <DataArray type=""Float32"" Name=""pressure"" format=""appended"" offset=""{offP}""/>")
            sb.AppendLine($"        <DataArray type=""Float32"" Name=""density"" format=""appended"" offset=""{offD}""/>")
            sb.AppendLine($"        <DataArray type=""Float32"" Name=""velocity"" NumberOfComponents=""3"" format=""appended"" offset=""{offVel}""/>")
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
                    bw.Write(CUInt(n))
                    bw.Write(maskBuf)

                    bw.Write(CUInt(4 * n))
                    WriteFloats(bw, pBuf)

                    bw.Write(CUInt(4 * n))
                    WriteFloats(bw, dBuf)

                    bw.Write(CUInt(12 * n))
                    WriteFloats(bw, velBuf)
                End Using

                Dim tail = Encoding.ASCII.GetBytes(vbLf & "  </AppendedData>" & vbLf & "</VTKFile>" & vbLf)
                fs.Write(tail, 0, tail.Length)
            End Using

        End Sub

        ''' <summary>把单精度数组转成字节流写出（不含长度头）。</summary>
        Private Shared Sub WriteFloats(bw As BinaryWriter, data As Single())
            Dim bytes(data.Length * 4 - 1) As Byte
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length)
            bw.Write(bytes)
        End Sub

        ''' <summary>
        ''' 导出快照到 .vti 文件（重载，便于逐帧导出）。
        ''' </summary>
        Public Shared Sub Export(snapshot As Snapshot, filePath As String)
            Export(snapshot.Field, filePath, snapshot.StepIndex, snapshot.Time)
        End Sub

    End Class

End Namespace
