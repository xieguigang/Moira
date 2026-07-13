Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports std = System.Math

' /********************************************************************************/
'
'   WindTunnel.vb
'
'   风洞外流动模拟容器 —— 与 FermentationTank 平行的 "模拟容器"
'
'   作用：
'       把由体素模型加载而来的计算空间（VoxelShape）放入一个水平向右（+X）的
'       均匀来流场中，模拟气流掠过模型表面的外部绕流（external flow），在模型
'       后方形成尾流与涡旋，并量化涡量、总涡量（enstrophy）、速度、尾流亏损等
'       湍流特征物理结果。
'
'   与发酵罐的区别：
'       - 发酵罐是内部封闭流动，由旋转搅拌器驱动；
'       - 风洞是外部开放流动，由左边界定常来流驱动。
'       两者共享底层 FluidField + StableFluidsSolver（复用 Advect/Diffuse/Project）。
'
'   边界条件（每步压力投影后强制覆盖）：
'       - 左面 i=0        ：定常入流 U=U∞, V=W=0（仅施加于流体体素）
'       - 右面 i=nx-1     ：零梯度出流（复制内部一层）
'       - 底面 j=0        ：地面壁面（GroundNoSlip 决定无滑移 / 自由滑移）
'       - 顶 / 前 / 后三面 ：沿用求解器自由滑移壁面
'
'   坐标约定（与引擎一致）：
'       i ∈ [0, Nx-1] → X（水平，来流方向）
'       j ∈ [0, Ny-1] → Y（竖直 / up，地面在 j=0）
'       k ∈ [0, Nz-1] → Z（水平，展向）
'
' /********************************************************************************/

''' <summary>
''' 风洞外流动模拟 —— 计算空间 + 来流边界 + 稳定流体求解器 + 湍流指标。
''' </summary>
Public Class WindTunnel

#Region "组件"

    ''' <summary>流体场（速度、压力、密度）。</summary>
    Public ReadOnly Property Field As FluidField

    ''' <summary>稳定流体求解器。</summary>
    Public ReadOnly Property Solver As StableFluidsSolver

#End Region

#Region "物理与配置参数"

    ''' <summary>来流速度 U∞（网格单位 / 时间，沿 +X）。</summary>
    Public Property FreestreamVelocity As Double = 2.0

    ''' <summary>运动粘度 ν（网格单位）。</summary>
    Public Property Viscosity As Double = 0.0001

    ''' <summary>
    ''' 底面（地面，j=0）是否为无滑移壁。
    ''' True：更真实地模拟贴地行驶（地面拖拽气流）；False：空中飞行（自由滑移）。
    ''' </summary>
    Public Property GroundNoSlip As Boolean = False

    ''' <summary>实际采用的计算空间放大倍数（仅记录，便于测试断言 / 输出）。</summary>
    Public Property DomainScale As Double = 1.0

    ''' <summary>实际采用的模型离地高度（仅记录，便于测试断言 / 输出）。</summary>
    Public Property GroundClearance As Integer = 0

    ''' <summary>当前模拟时间（累计）。</summary>
    Public Property Time As Double = 0.0

    ''' <summary>已执行的时间步数。</summary>
    Public Property StepCount As Integer = 0

#End Region

#Region "构造函数与工厂"

    ''' <summary>
    ''' 用给定计算空间创建风洞模拟。
    ''' </summary>
    ''' <param name="shape">计算空间体素模型（True = 流体，False = 固体障碍）</param>
    ''' <param name="freestream">来流速度 U∞（沿 +X）</param>
    ''' <param name="viscosity">运动粘度 ν</param>
    Public Sub New(shape As VoxelShape,
                   Optional freestream As Double = 2.0,
                   Optional viscosity As Double = 0.0001)

        Me.FreestreamVelocity = freestream
        Me.Viscosity = viscosity
        _Field = New FluidField(shape)
        _Solver = New StableFluidsSolver()
        _Solver.SolidMask = shape.ToSolidMask()
        ' 外流动采用自由滑移壁面（顶 / 前 / 后），更贴近开放来流
        _Solver.NoSlipWalls = False

    End Sub

    ''' <summary>
    ''' 从体素模型构建风洞：按 domainScale 放大计算空间，把模型定位到指定离地高度，
    ''' 再创建风洞模拟。
    ''' </summary>
    ''' <param name="model">已加载的体素模型</param>
    ''' <param name="freestream">来流速度 U∞</param>
    ''' <param name="domainScale">计算空间相对模型 grid 的放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型最低固体体素离地面 (j=0) 的高度（默认 0，贴地）</param>
    ''' <param name="viscosity">运动粘度 ν</param>
    ''' <param name="groundNoSlip">底面地壁是否无滑移（默认 False，自由滑移）</param>
    Public Shared Function FromVoxelModel(model As VoxelModel,
                                          Optional freestream As Double = 2.0,
                                          Optional domainScale As Double = 2.0,
                                          Optional groundClearance As Integer = 0,
                                          Optional viscosity As Double = 0.0001,
                                          Optional groundNoSlip As Boolean = False) As WindTunnel

        Dim domain = VoxelModelLoader.BuildDomain(model, domainScale, groundClearance)
        Dim wt As New WindTunnel(domain, freestream, viscosity)
        wt.DomainScale = domainScale
        wt.GroundClearance = groundClearance
        wt.GroundNoSlip = groundNoSlip
        Return wt

    End Function

#End Region

#Region "来流初始化与边界"

    ''' <summary>
    ''' 初始化流场：把所有流体体素的速度置为来流 (U∞, 0, 0)，
    ''' 使来流即时存在，模型扰动后在尾流区发展出涡旋。
    ''' </summary>
    Public Sub InitializeFlow()
        Dim f = Field
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    If f.IsActive(i, j, k) Then
                        f.U(i, j, k) = FreestreamVelocity
                        f.V(i, j, k) = 0.0
                        f.W(i, j, k) = 0.0
                    Else
                        f.U(i, j, k) = 0.0
                        f.V(i, j, k) = 0.0
                        f.W(i, j, k) = 0.0
                    End If
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' 施加风洞边界条件（在压力投影之后覆盖，取代求解器默认的封闭箱体壁面）：
    '''   - 左面 i=0    ：定常入流 U=U∞, V=W=0（仅流体体素）
    '''   - 右面 i=nx-1 ：零梯度出流（复制内部一层 i=nx-2）
    '''   - 底面 j=0    ：地面壁面（不可穿透 V=0；GroundNoSlip 时切向亦取反）
    ''' </summary>
    Private Sub ApplyWindTunnelBoundary()
        Dim f = Field
        Dim nx = f.Nx, ny = f.Ny, nz = f.Nz

        ' ---- 左面入流 (i=0) ----
        For j = 0 To ny - 1
            For k = 0 To nz - 1
                If f.IsActive(0, j, k) Then
                    f.U(0, j, k) = FreestreamVelocity
                    f.V(0, j, k) = 0.0
                    f.W(0, j, k) = 0.0
                End If
            Next
        Next

        ' ---- 右面零梯度出流 (i=nx-1)，复制内部一层 ----
        For j = 0 To ny - 1
            For k = 0 To nz - 1
                If f.IsActive(nx - 1, j, k) Then
                    f.U(nx - 1, j, k) = f.U(nx - 2, j, k)
                    f.V(nx - 1, j, k) = f.V(nx - 2, j, k)
                    f.W(nx - 1, j, k) = f.W(nx - 2, j, k)
                End If
            Next
        Next

        ' ---- 底面地壁 (j=0) ----
        For i = 0 To nx - 1
            For k = 0 To nz - 1
                If f.IsActive(i, 0, k) Then
                    ' 法向 (V) 不可穿透
                    f.V(i, 0, k) = 0.0
                    If GroundNoSlip Then
                        ' 无滑移：切向速度亦置零
                        f.U(i, 0, k) = 0.0
                        f.W(i, 0, k) = 0.0
                    End If
                End If
            Next
        Next
    End Sub

#End Region

#Region "主时间步进"

    ''' <summary>
    ''' 执行一个时间步的完整外流动求解（算子分裂：扩散 → 平流 → 投影 → 边界）。
    ''' </summary>
    ''' <param name="dt">时间步长</param>
    Public Sub StepForward(dt As Double)

        Dim nx = Field.Nx
        Dim ny = Field.Ny
        Dim nz = Field.Nz

        Dim u0 = Field.U
        Dim v0 = Field.V
        Dim w0 = Field.W

        Dim u1 As New Tensor(nx, ny, nz)
        Dim v1 As New Tensor(nx, ny, nz)
        Dim w1 As New Tensor(nx, ny, nz)

        ' ---- 边界（施加来流后再扩散）----
        ApplyWindTunnelBoundary()

        ' ---- Step 1: 扩散速度（隐式，模拟粘性）----
        If Viscosity > 0 Then
            Solver.Diffuse(u0, Viscosity, dt, u1)
            Solver.Diffuse(v0, Viscosity, dt, v1)
            Solver.Diffuse(w0, Viscosity, dt, w1)
        Else
            Array.Copy(u0.Data, u1.Data, u0.Length)
            Array.Copy(v0.Data, v1.Data, v0.Length)
            Array.Copy(w0.Data, w1.Data, w0.Length)
        End If

        ' ---- Step 2: 平流速度（半拉格朗日）----
        Solver.Advect(u1, u1, v1, w1, dt, u0)
        Solver.Advect(v1, u1, v1, w1, dt, v0)
        Solver.Advect(w1, u1, v1, w1, dt, w0)

        ' ---- Step 3: 压力投影（强制不可压缩）----
        Solver.Project(u0, v0, w0, Field.Pressure, dt)

        ' ---- Step 4: 覆盖风洞边界（取代封闭箱体壁面）----
        ApplyWindTunnelBoundary()

        ' 清理临时缓冲
        u1.Dispose()
        v1.Dispose()
        w1.Dispose()

        ' 保证固体（模型本体）单元的速度 / 压力 / 密度恒为 0，形成清晰壁面
        Solver.EnforceSolidMask(u0, v0, w0, Field.Pressure, Field.Density)

        ' 更新统计
        Time += dt
        StepCount += 1

    End Sub

    ''' <summary>
    ''' 执行多个时间步。
    ''' </summary>
    ''' <param name="steps">步数</param>
    ''' <param name="dt">每步时间步长</param>
    ''' <param name="progressCallback">可选：每步回调 (stepIndex, time)</param>
    Public Sub Run(steps As Integer, dt As Double,
                   Optional progressCallback As Action(Of Integer, Double) = Nothing)
        For s = 1 To steps
            StepForward(dt)
            If progressCallback IsNot Nothing Then
                progressCallback(s, Time)
            End If
        Next
    End Sub

#End Region

#Region "湍流 / 物理结果指标"

    ''' <summary>
    ''' 计算全场最大速度大小。
    ''' </summary>
    Public Function ComputeMaxSpeed() As Double
        Dim f = Field
        Dim maxS = 0.0
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    If Not f.IsActive(i, j, k) Then Continue For
                    Dim u = f.U(i, j, k), v = f.V(i, j, k), w = f.W(i, j, k)
                    Dim s = std.Sqrt(u * u + v * v + w * w)
                    If s > maxS Then maxS = s
                Next
            Next
        Next
        Return maxS
    End Function

    ''' <summary>
    ''' 计算全场平均速度大小（仅统计流体体素）。
    ''' </summary>
    Public Function ComputeAverageSpeed() As Double
        Dim f = Field
        Dim sum = 0.0
        Dim count = 0
        For i = 0 To f.Nx - 1
            For j = 0 To f.Ny - 1
                For k = 0 To f.Nz - 1
                    If Not f.IsActive(i, j, k) Then Continue For
                    Dim u = f.U(i, j, k), v = f.V(i, j, k), w = f.W(i, j, k)
                    sum += std.Sqrt(u * u + v * v + w * w)
                    count += 1
                Next
            Next
        Next
        Return If(count > 0, sum / count, 0.0)
    End Function

    ''' <summary>
    ''' 计算总涡量 enstrophy = Σ |ω|²（内部流体体素），
    ''' 其中涡量 ω = ∇×u（中心差分，dx=1）。
    ''' 作为涡旋 / 湍流结构强度的代理指标：值越大表示流场中涡旋越强、越 "湍"。
    ''' </summary>
    Public Function ComputeEnstrophy() As Double
        Dim f = Field
        Dim nx = f.Nx, ny = f.Ny, nz = f.Nz
        Dim total = 0.0
        For i = 1 To nx - 2
            For j = 1 To ny - 2
                For k = 1 To nz - 2
                    If Not f.IsActive(i, j, k) Then Continue For
                    ' 涡量分量（中心差分，dx=1）
                    ' ωx = ∂W/∂y - ∂V/∂z
                    ' ωy = ∂U/∂z - ∂W/∂x
                    ' ωz = ∂V/∂x - ∂U/∂y
                    Dim wx = (f.W(i, j + 1, k) - f.W(i, j - 1, k)) * 0.5 -
                             (f.V(i, j, k + 1) - f.V(i, j, k - 1)) * 0.5
                    Dim wy = (f.U(i, j, k + 1) - f.U(i, j, k - 1)) * 0.5 -
                             (f.W(i + 1, j, k) - f.W(i - 1, j, k)) * 0.5
                    Dim wz = (f.V(i + 1, j, k) - f.V(i - 1, j, k)) * 0.5 -
                             (f.U(i, j + 1, k) - f.U(i, j - 1, k)) * 0.5
                    total += wx * wx + wy * wy + wz * wz
                Next
            Next
        Next
        Return total
    End Function

    ''' <summary>
    ''' 计算指定下游平面 (i = downstreamPlaneI) 的尾流速度亏损：
    '''   deficit = U∞ - mean(U over 该平面流体体素)。
    ''' 正值表示该平面平均流向速度低于来流（模型阻挡形成尾流 / 阻力代理）。
    ''' </summary>
    ''' <param name="downstreamPlaneI">下游平面的 i 索引</param>
    Public Function ComputeWakeDeficit(downstreamPlaneI As Integer) As Double
        Dim f = Field
        If downstreamPlaneI < 0 OrElse downstreamPlaneI > f.Nx - 1 Then
            Throw New ArgumentOutOfRangeException(NameOf(downstreamPlaneI))
        End If
        Dim sumU = 0.0
        Dim count = 0
        For j = 0 To f.Ny - 1
            For k = 0 To f.Nz - 1
                If f.IsActive(downstreamPlaneI, j, k) Then
                    sumU += f.U(downstreamPlaneI, j, k)
                    count += 1
                End If
            Next
        Next
        Dim meanU = If(count > 0, sumU / count, 0.0)
        Return FreestreamVelocity - meanU
    End Function

    ''' <summary>
    ''' 获取指定体素的全部基础物理量。
    ''' </summary>
    Public Function GetVoxel(i As Integer, j As Integer, k As Integer) As VoxelData
        Return Field.GetVoxel(i, j, k)
    End Function

#End Region

End Class
