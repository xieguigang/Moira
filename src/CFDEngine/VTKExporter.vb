' /********************************************************************************/
'
'   VTKExporter.vb
'
'   VTK 导出器 —— 将模拟结果导出为 legacy VTK 文件
'
'   作用：
'       把 CFD 引擎计算出的速度、压力、密度场导出为 .vtk 文件，
'       可用 ParaView（免费开源可视化软件）打开查看三维结果。
'
'   设计说明：
'       本导出器只依赖 FluidField（流体场纯数据），不依赖 FermentationTank /
'       FluidSim 等引擎对象，因此与引擎解耦、可被任意来源的场数据复用。
'       也提供接收 Snapshot 的重载，便于逐帧快照导出。
'
'   文件格式：
'       Legacy VTK ASCII 格式，STRUCTURED_POINTS 数据集。
'       每个文件导出全部物理场，顺序固定为：
'         - SCALARS pressure  （压力标量场）
'         - SCALARS density   （密度/示踪剂标量场）
'         - SCALARS u         （X 方向速度分量标量场）
'         - SCALARS v         （Y 方向速度分量标量场）
'         - SCALARS w         （Z 方向速度分量标量场）
'         - SCALARS speed     （速度大小标量场）
'         - VECTORS velocity  （速度向量场）
'
'   使用方法：
'       VTKExporter.Export(tank.Field, "output_0010.vtk", step:=10, time:=1.0)
'       VTKExporter.Export(snapshot, "output_0010.vtk")
'       然后在 ParaView 中打开该文件即可。
'
' /********************************************************************************/

Imports System.IO
Imports std = System.Math

Namespace CFDEngine

    ''' <summary>
    ''' 将 CFD 模拟结果导出为 VTK 文件（可用 ParaView 打开）。
    ''' 仅依赖 FluidField，与引擎对象解耦。
    ''' </summary>
    Public Class VTKExporter

        ''' <summary>
        ''' 导出整个流体场到 VTK 文件（主入口，仅依赖 FluidField）。
        ''' </summary>
        ''' <param name="field">流体场数据</param>
        ''' <param name="filePath">输出文件路径</param>
        ''' <param name="stepIndex">时间步序号（写入文件头注释，可选）</param>
        ''' <param name="time">模拟时间（写入文件头注释，可选）</param>
        Public Shared Sub Export(field As FluidField, filePath As String,
                                 Optional stepIndex As Integer = 0,
                                 Optional time As Double = 0.0)

            Dim f = field
            Dim nx = f.Nx
            Dim ny = f.Ny
            Dim nz = f.Nz

            Using writer As New StreamWriter(filePath)
                ' ---- VTK 文件头 ----
                writer.WriteLine("# vtk DataFile Version 3.0")
                writer.WriteLine("CFD Engine Output - Step " & stepIndex & " - Time " & time.ToString("F4"))
                writer.WriteLine("ASCII")
                writer.WriteLine("DATASET STRUCTURED_POINTS")

                ' ---- 网格维度 ----
                writer.WriteLine("DIMENSIONS {0} {1} {2}", nx, ny, nz)

                ' ---- 原点与间距（网格单位）----
                writer.WriteLine("ORIGIN 0 0 0")
                writer.WriteLine("SPACING 1 1 1")

                ' ---- 点数据 ----
                Dim nPoints = nx * ny * nz
                writer.WriteLine("POINT_DATA {0}", nPoints)

                ' ---- 压力标量场 ----
                writer.WriteLine("SCALARS pressure double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.Write("{0:F6} ", f.Pressure(i, j, k))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 密度标量场 ----
                writer.WriteLine("SCALARS density double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.Write("{0:F6} ", f.Density(i, j, k))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 速度 X 分量标量场 (u) ----
                writer.WriteLine("SCALARS u double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.Write("{0:F6} ", f.U(i, j, k))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 速度 Y 分量标量场 (v) ----
                writer.WriteLine("SCALARS v double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.Write("{0:F6} ", f.V(i, j, k))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 速度 Z 分量标量场 (w) ----
                writer.WriteLine("SCALARS w double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.Write("{0:F6} ", f.W(i, j, k))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 速度大小标量场 ----
                writer.WriteLine("SCALARS speed double 1")
                writer.WriteLine("LOOKUP_TABLE default")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            Dim u = f.U(i, j, k)
                            Dim v = f.V(i, j, k)
                            Dim w = f.W(i, j, k)
                            writer.Write("{0:F6} ", std.Sqrt(u * u + v * v + w * w))
                        Next
                        writer.WriteLine()
                    Next
                Next

                ' ---- 速度向量场 ----
                writer.WriteLine("VECTORS velocity double")
                For k = 0 To nz - 1
                    For j = 0 To ny - 1
                        For i = 0 To nx - 1
                            writer.WriteLine("{0:F6} {1:F6} {2:F6}",
                                             f.U(i, j, k), f.V(i, j, k), f.W(i, j, k))
                        Next
                    Next
                Next

            End Using

        End Sub

        ''' <summary>
        ''' 导出快照到 VTK 文件（重载，便于逐帧导出）。
        ''' </summary>
        ''' <param name="snapshot">数据快照</param>
        ''' <param name="filePath">输出文件路径</param>
        Public Shared Sub Export(snapshot As Snapshot, filePath As String)
            Export(snapshot.Field, filePath, snapshot.StepIndex, snapshot.Time)
        End Sub

        ''' <summary>
        ''' 导出单个水平切片（某 k 层）的 CSV 文件，便于用 Excel 或 Python 查看。
        ''' 仅依赖 FluidField，与引擎对象解耦。
        ''' </summary>
        ''' <param name="field">流体场数据</param>
        ''' <param name="k">切片所在的 Z 层索引</param>
        ''' <param name="filePath">输出文件路径</param>
        Public Shared Sub ExportSliceCSV(field As FluidField, k As Integer, filePath As String)

            Dim f = field
            Using writer As New StreamWriter(filePath)
                writer.WriteLine("i,j,u,v,w,speed,pressure,density")
                For i = 0 To f.Nx - 1
                    For j = 0 To f.Ny - 1
                        Dim u = f.U(i, j, k)
                        Dim v = f.V(i, j, k)
                        Dim w = f.W(i, j, k)
                        Dim spd = std.Sqrt(u * u + v * v + w * w)
                        writer.WriteLine("{0},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6}",
                                         i, j, u, v, w, spd, f.Pressure(i, j, k), f.Density(i, j, k))
                    Next
                Next
            End Using

        End Sub

    End Class

End Namespace
