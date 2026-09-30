' /********************************************************************************/
'
'   VoxelSphere.vb
'
'   参数化体素球体几何工厂 —— GNN 风洞代理模型的训练/测试几何来源
'
'   作用：
'       在一个长方体风洞计算域（VoxelShape.FullBox）内放置一个球体障碍物，
'       球体最低点按指定的离地间隙（groundClearance）落于地面 j=0 之上，
'       其余空间全部为流体。用于批量生成不同 (半径, 离地间隙, 来流速度)
'       参数组合的风洞仿真场景。
'
'   坐标约定（与 CFDEngine 一致）：
'       i ∈ [0, Nx-1] → X（水平，来流方向 +X）
'       j ∈ [0, Ny-1] → Y（竖直，地面在 j=0）
'       k ∈ [0, Nz-1] → Z（水平，展向）
'
' /********************************************************************************/

Imports Moira.CFDEngine
Imports std = System.Math

''' <summary>
''' 参数化球体风洞场景配置：球体半径 + 离地间隙 + 来流速度 + 计算域尺寸。
''' </summary>
Public Class SphereConfig

    ''' <summary>球体半径（体素单位）。</summary>
    Public Property Radius As Integer

    ''' <summary>球体最低固体体素离地面 (j=0) 的间隙（体素层数）。</summary>
    Public Property GroundClearance As Integer

    ''' <summary>来流速度 U∞（沿 +X）。</summary>
    Public Property Freestream As Double

    ''' <summary>计算域 X 方向尺寸（来流方向）。</summary>
    Public Property DomainNx As Integer

    ''' <summary>计算域 Y 方向尺寸（竖直，含地面）。</summary>
    Public Property DomainNy As Integer

    ''' <summary>计算域 Z 方向尺寸（展向）。</summary>
    Public Property DomainNz As Integer

    ''' <summary>球心 X 位置（体素索引，默认自动放在来流上游约 40% 处，为下游尾流留出空间）。</summary>
    Public Property CenterX As Integer = -1

    Public Sub New(Optional radius As Integer = 4,
                   Optional groundClearance As Integer = 1,
                   Optional freestream As Double = 2.0,
                   Optional domainNx As Integer = 20,
                   Optional domainNy As Integer = 14,
                   Optional domainNz As Integer = 20)

        Me.Radius = radius
        Me.GroundClearance = groundClearance
        Me.Freestream = freestream
        Me.DomainNx = domainNx
        Me.DomainNy = domainNy
        Me.DomainNz = domainNz
    End Sub

    Public Overrides Function ToString() As String
        Return $"sphere(r={Radius}, clearance={GroundClearance}, U∞={Freestream:F1}, " &
               $"domain={DomainNx}x{DomainNy}x{DomainNz})"
    End Function

End Class

''' <summary>
''' 体素球体几何工厂：构建包含贴地/离地球体障碍的风洞计算域。
''' </summary>
Public Module VoxelSphere

    ''' <summary>
    ''' 构建一个长方体风洞计算域：域内放置球体固体障碍（其余为流体）。
    ''' 球体沿 X 位于来流上游约 40% 处、Z 方向居中，最低固体体素位于 j = clearance。
    ''' </summary>
    ''' <param name="config">球体场景配置</param>
    Public Function BuildGroundDomain(config As SphereConfig) As VoxelShape
        Dim nx = config.DomainNx
        Dim ny = config.DomainNy
        Dim nz = config.DomainNz
        Dim r = config.Radius
        Dim clearance = config.GroundClearance

        ' ---- 参数合法性检查 ----
        If r < 1 Then Throw New ArgumentException($"球体半径必须 >= 1，当前 {r}")
        If clearance < 0 Then Throw New ArgumentException($"离地间隙必须 >= 0，当前 {clearance}")
        If ny < clearance + 2 * r + 1 Then
            Throw New ArgumentException(
                $"计算域高度 {ny} 不足以容纳半径 {r}、离地 {clearance} 的球体（需 >= {clearance + 2 * r + 1}）")
        End If

        Dim cx = config.CenterX
        If cx < 0 Then
            ' 默认：来流上游约 40% 处，尾流可向下游发展
            cx = CInt(std.Floor(nx * 0.4))
        End If
        cx = std.Min(std.Max(cx, r + 1), nx - r - 2)

        Dim cy = clearance + r    ' 球心高度：最低固体体素恰好在 j = clearance
        Dim cz = nz \ 2

        Dim data(nx * ny * nz - 1) As Boolean   ' True = 流体
        Dim r2 As Double = r * r

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim dx = i - cx
                    Dim dy = j - cy
                    Dim dz = k - cz
                    ' 球体内部（含球面）为固体 → 非流体
                    If dx * dx + dy * dy + dz * dz <= r2 Then
                        data((i * ny + j) * nz + k) = False
                    Else
                        data((i * ny + j) * nz + k) = True
                    End If
                Next
            Next
        Next

        Return New VoxelShape(nx, ny, nz, data)
    End Function

    ''' <summary>
    ''' 构建一个无障碍的纯流体长方体计算域（用于对照 / 自回归 rollout 初始化）。
    ''' </summary>
    Public Function FullDomain(nx As Integer, ny As Integer, nz As Integer) As VoxelShape
        Return VoxelShape.FullBox(nx, ny, nz)
    End Function

End Module
