Imports System.Text.Json.Serialization
Imports Moira.CFDEngine.CFDEngine

Namespace Snapshot.JSON

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
        ''' <summary>体素模型 X 维度（↔ Nx）</summary>
        Public Property Width As Integer
        ''' <summary>体素模型 Y 维度（↔ Ny）</summary>
        Public Property Height As Integer
        ''' <summary>体素模型 Z 维度（↔ Nz）</summary>
        Public Property Depth As Integer
        ''' <summary>原点坐标 [x, y, z]（网格单位）</summary>
        Public Property Origin As Double()
        ''' <summary>格子间距 [dx, dy, dz]（网格单位）</summary>
        Public Property Spacing As Double()
        ''' <summary>格子总数 = Nx * Ny * Nz</summary>
        Public Property TotalVoxels As Integer
        ''' <summary>活动（属于模拟空间）体素总数</summary>
        Public Property ActiveVoxels As Integer
        ''' <summary>
        ''' 体素掩膜（0/1 扁平数组，长度 = Width*Height*Depth），顺序 = (x*Height + y)*Depth + z。
        ''' 1 = 活动体素（模拟空间），0 = 空腔（固体障碍物）。阅读端据其还原到全网格。
        ''' </summary>
        Public Property Mask As Integer()
        ''' <summary>扁平数组索引顺序说明：i*ny*nz + j*nz + k（等价于 (x*Height+y)*Depth+z）</summary>
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
        <JsonPropertyName("Step")>
        Public Property StepIndex As Integer
        ''' <summary>该帧模拟时间</summary>
        Public Property Time As Double
        ''' <summary>该帧文件相对 outputDir 的文件名（如 frame_0000.json）</summary>
        Public Property File As String
    End Class
End Namespace