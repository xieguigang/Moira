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
'   文件格式：
'       Legacy VTK ASCII 格式，STRUCTURED_POINTS 数据集。
'       包含：
'         - SCALARS pressure  （压力标量场）
'         - SCALARS density   （密度/示踪剂标量场）
'         - SCALARS speed     （速度大小标量场）
'         - VECTORS velocity  （速度向量场）
'
'   使用方法：
'       VTKExporter.Export(engine.Tank, "output_0010.vtk")
'       然后在 ParaView 中打开该文件即可。
'
' /********************************************************************************/

Imports System.IO
Imports std = System.Math

Namespace CFDEngine

    ''' <summary>
    ''' 将 CFD 模拟结果导出为 VTK 文件（可用 ParaView 打开）。
    ''' </summary>
    Public Class VTKExporter

        ''' <summary>
        ''' 导出整个流体场到 VTK 文件。
        ''' </summary>
        ''' <param name="tank">发酵罐模拟</param>
        ''' <param name="filePath">输出文件路径</param>
        Public Shared Sub Export(tank As FermentationTank, filePath As String)

            Dim f = tank.Field
            Dim nx = f.Nx
            Dim ny = f.Ny
            Dim nz = f.Nz

            Using writer As New StreamWriter(filePath)
                ' ---- VTK 文件头 ----
                writer.WriteLine("# vtk DataFile Version 3.0")
                writer.WriteLine("CFD Engine Output - Step " & tank.StepCount & " - Time " & tank.Time.ToString("F4"))
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
        ''' 导出单个水平切片（某 k 层）的 CSV 文件，便于用 Excel 或 Python 查看。
        ''' </summary>
        Public Shared Sub ExportSliceCSV(tank As FermentationTank, k As Integer, filePath As String)

            Dim f = tank.Field
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
