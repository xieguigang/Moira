' /********************************************************************************/
'
'   SnapshotMetadata.vb
'
'   快照元数据数据对象
'
'   作用：
'       保存三维体素网格模型（维度 / 原点 / 间距 / 索引顺序）与模拟计算配置
'       参数（粘度、扩散、时间步、求解器、搅拌器几何与运动参数），以及逐帧
'       引用列表。该对象被序列化为 metadata.json 写出一次，从而消除 VTK
'       逐帧快照中每帧重复定义的网格信息冗余。
'
'   设计说明：
'       - 可被 System.Text.Json 直接序列化（公共属性 + 公共嵌套类）。
'       - FromTank 工厂从 FermentationTank 自动收集网格与配置；搅拌器为 Nothing 时
'         写出 null（JSON 合法）。
'       - 与引擎解耦：仅依赖 FluidField / FermentationTank / Stirrer，不反向依赖记录器。
'
' /********************************************************************************/

Imports System.Text.Json

Namespace Snapshot.JSON

    ''' <summary>
    ''' 快照元数据根对象 —— 序列化为 metadata.json。
    ''' 包含网格模型、仿真配置与逐帧引用列表。
    ''' </summary>
    Public Class SnapshotMetadata

        ''' <summary>格式标识（固定为 "json"）。</summary>
        Public Property Format As String = "json"

        ''' <summary>元数据 schema 版本，便于后续兼容。</summary>
        Public Property SchemaVersion As Integer = 1

        ''' <summary>生成时间（ISO8601）。</summary>
        Public Property CreatedAt As String

        ''' <summary>三维体素网格信息。</summary>
        Public Property Grid As GridInfo

        ''' <summary>模拟计算配置。</summary>
        Public Property Simulation As SimulationInfo

        ''' <summary>逐帧引用列表（运行结束前由记录器填充）。</summary>
        Public Property Frames As List(Of FrameRef)

        ''' <summary>
        ''' 从发酵罐与 dt 自动构造快照元数据（网格模型 + 仿真配置）。
        ''' 搅拌器为 Nothing 时 Stirrer 段写为 null。
        ''' </summary>
        ''' <param name="tank">发酵罐（提供网格、粘度、扩散、搅拌器）</param>
        ''' <param name="dt">时间步长</param>
        Public Shared Function FromTank(tank As FermentationTank, dt As Double) As SnapshotMetadata
            Dim f = tank.Field
            Dim shape = f.Shape
            Dim mask As Integer() = Nothing
            Dim active As Integer = f.TotalVoxels
            If shape IsNot Nothing Then
                active = shape.TotalActive
                mask = New Integer(shape.Shape.Length - 1) {}
                For i = 0 To shape.Shape.Length - 1
                    mask(i) = If(shape.Shape(i), 1, 0)
                Next
            End If
            Dim grid = New GridInfo With {
                .Nx = f.Nx,
                .Ny = f.Ny,
                .Nz = f.Nz,
                .Width = f.Nx,
                .Height = f.Ny,
                .Depth = f.Nz,
                .Origin = New Double() {0.0, 0.0, 0.0},
                .Spacing = New Double() {1.0, 1.0, 1.0},
                .TotalVoxels = f.TotalVoxels,
                .ActiveVoxels = active,
                .Mask = mask,
                .IndexOrder = "i*ny*nz + j*nz + k"
            }
            Dim sim = New SimulationInfo With {
                .Viscosity = tank.Viscosity,
                .Diffusion = tank.Diffusion,
                .TimeStep = dt,
                .Solver = "StableFluids (Jos Stam, 1999)",
                .Stirrer = If(tank.Stirrer Is Nothing, Nothing, StirrerInfo.FromStirrer(tank.Stirrer))
            }
            Return New SnapshotMetadata With {
                .Grid = grid,
                .Simulation = sim,
                .Frames = New List(Of FrameRef)(),
                .CreatedAt = DateTime.UtcNow.ToString("O")
            }
        End Function

        ''' <summary>
        ''' 将本对象序列化为缩进 JSON 字符串。
        ''' </summary>
        Public Function ToJson() As String
            Return JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True})
        End Function

    End Class

End Namespace
