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

Imports System.Collections.Generic
Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace CFDEngine

    ''' <summary>
    ''' 三维体素网格信息（写入 metadata.json 的 grid 段）。
    ''' </summary>
    Public Class GridInfo
        ''' <summary>X 方向格子数</summary>
        Public Property Nx As Integer
        ''' <summary>Y 方向格子数</summary>
        Public Property Ny As Integer
        ''' <summary>Z 方向格子数</summary>
        Public Property Nz As Integer
        ''' <summary>原点坐标 [x, y, z]（网格单位）</summary>
        Public Property Origin As Double()
        ''' <summary>格子间距 [dx, dy, dz]（网格单位）</summary>
        Public Property Spacing As Double()
        ''' <summary>格子总数 = Nx * Ny * Nz</summary>
        Public Property TotalVoxels As Integer
        ''' <summary>扁平数组索引顺序说明：i*ny*nz + j*nz + k</summary>
        Public Property IndexOrder As String
    End Class

    ''' <summary>
    ''' 搅拌器几何与运动参数（写入 simulation.stirrer 段，可空）。
    ''' </summary>
    Public Class StirrerInfo
        Public Property CenterX As Double
        Public Property CenterY As Double
        Public Property ZCenter As Double
        Public Property Radius As Double
        Public Property Height As Double
        Public Property AngularVelocity As Double
        Public Property AxialVelocity As Double

        ''' <summary>从 Stirrer 对象构造 StirrerInfo。</summary>
        Public Shared Function FromStirrer(s As Stirrer) As StirrerInfo
            Return New StirrerInfo With {
                .CenterX = s.CenterX,
                .CenterY = s.CenterY,
                .ZCenter = s.ZCenter,
                .Radius = s.Radius,
                .Height = s.Height,
                .AngularVelocity = s.AngularVelocity,
                .AxialVelocity = s.AxialVelocity
            }
        End Function
    End Class

    ''' <summary>
    ''' 模拟计算配置（写入 metadata.json 的 simulation 段）。
    ''' </summary>
    Public Class SimulationInfo
        ''' <summary>运动粘度 ν（网格单位）</summary>
        Public Property Viscosity As Double
        ''' <summary>示踪剂扩散系数（网格单位）</summary>
        Public Property Diffusion As Double
        ''' <summary>时间步长 dt</summary>
        Public Property TimeStep As Double
        ''' <summary>求解器名称 / 算法</summary>
        Public Property Solver As String
        ''' <summary>搅拌器参数；无搅拌器时为 null</summary>
        Public Property Stirrer As StirrerInfo
    End Class

    ''' <summary>
    ''' 单帧引用（写入 metadata.json 的 frames 列表）。
    ''' </summary>
    Public Class FrameRef
        ''' <summary>该帧时间步序号</summary>
        <JsonPropertyName("step")>
        Public Property StepIndex As Integer
        ''' <summary>该帧模拟时间</summary>
        Public Property Time As Double
        ''' <summary>该帧文件相对 outputDir 的文件名（如 frame_0000.json）</summary>
        Public Property File As String
    End Class

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
            Dim grid = New GridInfo With {
                .Nx = f.Nx,
                .Ny = f.Ny,
                .Nz = f.Nz,
                .Origin = New Double() {0.0, 0.0, 0.0},
                .Spacing = New Double() {1.0, 1.0, 1.0},
                .TotalVoxels = f.TotalVoxels,
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
