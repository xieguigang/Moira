Option Strict On
Option Explicit On

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine

' /********************************************************************************/
'
'   WindTunnelSolver.vb
'
'   LBM float32 风洞求解器 —— 接口设计参照 CFDEngine/WindTunnel.vb，但数值核心
'   为 LbmCoreF（D3Q19 BGK / TRT float32，CUDA 设备驻留 / CPU 融合双路径）。
'
'   边界条件（Btype 掩码，见 CudaLbmBackend / moira_lbm_f32.cu）：
'       - 障碍体素（solid = True）      : CellSolid，halfway bounce-back 无滑移
'       - 左面 i=0 的流体格点           : CellInlet，定常平衡态来流 (U∞, 0, 0)
'       - 右面 i=nx-1 的流体格点        : CellOutlet，零梯度出流
'       - 底面 j=0 的流体格点           : GroundNoSlip ? CellSolid : 流体（specular 自由滑移）
'       - 顶面 / 前后面越界             : specular 镜面反射（自由滑移）
'
'   坐标约定（与 CFDEngine 一致）：
'       i ∈ [0, Nx-1] → X（水平，来流方向）
'       j ∈ [0, Ny-1] → Y（竖直 / up，地面在 j=0）
'       k ∈ [0, Nz-1] → Z（水平，展向）
'
'   LBM 格子单位约束：来流速度必须远小于格子声速 cs = 1/√3 ≈ 0.577
'   （马赫数 Ma < 0.3），推荐 U∞ ∈ [0.05, 0.15]。
'
' /********************************************************************************/

''' <summary>
''' LBM float32 风洞外流模拟 —— 体素障碍 + 定常来流 + 零梯度出流。
''' </summary>
Public Class WindTunnelSolver : Implements IDisposable

#Region "物理与配置参数"

    ''' <summary>来流速度 U∞（格子单位 / 步，沿 +X；须远小于 cs ≈ 0.577）。</summary>
    Public Property FreestreamVelocity As Double

    ''' <summary>运动粘度 ν（格子单位）。</summary>
    Public ReadOnly Viscosity As Double

    ''' <summary>底面（地面，j=0）是否为无滑移壁。False = 自由滑移（specular）。</summary>
    Public Property GroundNoSlip As Boolean

    ''' <summary>实际采用的计算空间放大倍数（仅记录）。</summary>
    Public Property DomainScale As Double = 1.0

    ''' <summary>实际采用的模型离地高度（仅记录）。</summary>
    Public Property GroundClearance As Integer = 0

    ''' <summary>推荐来流速度格子值上限（Ma ≈ 0.35）。</summary>
    Public Const MaxFreestream As Double = 0.2R

#End Region

#Region "网格与求解核心"

    Public ReadOnly Nx As Integer
    Public ReadOnly Ny As Integer
    Public ReadOnly Nz As Integer

    ''' <summary>float32 LBM 求解核心。</summary>
    Public ReadOnly Lattice As LbmCoreF

    Public ReadOnly Property Btype As Byte()
        Get
            Return Lattice.Btype
        End Get
    End Property

    Public ReadOnly Property FluidCount As Integer
        Get
            Return Lattice.FluidCount
        End Get
    End Property

    Public ReadOnly Property StepCount As Long
        Get
            Return Lattice.StepCount
        End Get
    End Property

    Public ReadOnly Property Time As Double
        Get
            Return Lattice.SimTime
        End Get
    End Property

    ''' <summary>是否正在使用 CUDA 加速。</summary>
    Public ReadOnly Property UseGpu As Boolean
        Get
            Return Lattice.UseGpu
        End Get
    End Property

    ''' <summary>矩量场（TensorF 零拷贝视图，布局 (Nx, Ny, Nz)）。</summary>
    Public ReadOnly Property RhoF As TensorF
        Get
            Return Lattice.Rho
        End Get
    End Property

    Public ReadOnly Property UxF As TensorF
        Get
            Return Lattice.Ux
        End Get
    End Property

    Public ReadOnly Property UyF As TensorF
        Get
            Return Lattice.Uy
        End Get
    End Property

    Public ReadOnly Property UzF As TensorF
        Get
            Return Lattice.Uz
        End Get
    End Property

#End Region

#Region "构造函数与工厂"

    ''' <summary>
    ''' 用固体掩膜创建 LBM 风洞模拟。
    ''' </summary>
    ''' <param name="solid">固体掩膜（长度 nx*ny*nz，True = 固体障碍）</param>
    ''' <param name="freestream">来流速度 U∞（格子单位，须 ≤ 0.2）</param>
    ''' <param name="viscosity">运动粘度 ν（格子单位）</param>
    ''' <param name="groundNoSlip">地面（j=0）是否无滑移</param>
    ''' <param name="useGpu">True 时尝试 CUDA 加速，失败自动回退 CPU float 路径</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   solid As Boolean(),
                   Optional freestream As Double = 0.1R,
                   Optional viscosity As Double = 0.005R,
                   Optional groundNoSlip As Boolean = False,
                   Optional model As CollisionModel = CollisionModel.TRT,
                   Optional useGpu As Boolean = True)

        If solid.Length <> nx * ny * nz Then
            Throw New ArgumentException($"solid 长度必须为 {nx * ny * nz}")
        End If

        ' 前置容量校验（在分配 btype / f 场之前给出可读错误）
        Dim nTotal As Long = CLng(nx) * ny * nz

        If nTotal > LbmCoreF.MaxGridPoints Then
            Throw New InvalidOperationException(
                $"风洞计算域格点数 {nTotal:N0} 超出硬上限 {LbmCoreF.MaxGridPoints:N0}" &
                $"（{nx}x{ny}x{nz}）。请降低体素化分辨率" &
                "（Voxelizer CLI：--resolution ≤ 128）或减小计算域后再加载。")
        End If

        If freestream > MaxFreestream Then
            Throw New ArgumentException(
                $"来流速度 U∞={freestream:F3} 过大（格子单位下要求 ≤ {MaxFreestream}，" &
                $"Ma ≤ 0.35）。请把物理风速换算到格子单位（缩小 U 或加密网格）。")
        End If

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me.FreestreamVelocity = freestream
        Me.Viscosity = viscosity
        Me.GroundNoSlip = groundNoSlip

        ' ---- 松弛时间：tau = nu / cs2 + 1/2 ----
        Dim tau As Double = viscosity / LatticeD3Q19.Cs2 + 0.5R
        Dim tauMin As Double = If(model = CollisionModel.BGK, 0.51R, 0.5005R)

        If tau < tauMin Then
            Throw New ArgumentException(
                $"粘度 ν={viscosity} 过低：τ={tau:F4} < {tauMin:F4}（{model} 失稳）。" &
                "请增大 ν（或降低网格分辨率 / 提高来流速度的格子值），或改用 TRT。")
        End If

        ' ---- 构造边界类型掩码 ----
        Dim plane As Integer = ny * nz
        Dim btype(nx * ny * nz - 1) As Byte

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim idx As Integer = i * plane + j * nz + k

                    If solid(idx) Then
                        btype(idx) = CudaLbmBackend.CellSolid

                    ElseIf i = 0 Then
                        btype(idx) = CudaLbmBackend.CellInlet

                    ElseIf i = nx - 1 Then
                        btype(idx) = CudaLbmBackend.CellOutlet

                    ElseIf j = 0 AndAlso groundNoSlip Then
                        ' 无滑移地面：halfway bounce-back
                        btype(idx) = CudaLbmBackend.CellSolid

                    Else
                        ' 流体：顶面 / 前后面 / 自由滑移地面靠越界 specular 反射
                        btype(idx) = CudaLbmBackend.CellFluid
                    End If
                Next
            Next
        Next

        Lattice = New LbmCoreF(nx, ny, nz, btype, 1.0R / tau, model, useGpu)
    End Sub

    ''' <summary>
    ''' 从 VoxelShape 创建风洞（接口对齐 CFDEngine/WindTunnel）。
    ''' </summary>
    ''' <param name="shape">计算空间体素模型（True = 流体，False = 固体障碍）</param>
    Public Shared Function FromVoxelShape(shape As VoxelShape,
                                          Optional freestream As Double = 0.1R,
                                          Optional viscosity As Double = 0.005R,
                                          Optional groundNoSlip As Boolean = False,
                                          Optional model As CollisionModel = CollisionModel.TRT,
                                          Optional useGpu As Boolean = True) As WindTunnelSolver

        ' ToSolidMask()：True = 固体
        Return New WindTunnelSolver(shape.Width, shape.Height, shape.Depth,
                                    shape.ToSolidMask(), freestream, viscosity,
                                    groundNoSlip, model, useGpu)
    End Function

    ''' <summary>
    ''' 从体素模型构建风洞：按 domainScale 放大计算空间，把模型定位到指定离地高度。
    ''' </summary>
    Public Shared Function FromVoxelModel(model As VoxelModel,
                                          Optional freestream As Double = 0.1R,
                                          Optional domainScale As Double = 2.0R,
                                          Optional groundClearance As Integer = 0,
                                          Optional viscosity As Double = 0.005R,
                                          Optional groundNoSlip As Boolean = False,
                                          Optional useGpu As Boolean = True) As WindTunnelSolver

        Dim domain = VoxelModelLoader.BuildDomain(model, domainScale, groundClearance)
        Dim tunnel = FromVoxelShape(domain, freestream, viscosity, groundNoSlip, CollisionModel.TRT, useGpu)

        tunnel.DomainScale = domainScale
        tunnel.GroundClearance = groundClearance
        Return tunnel
    End Function

#End Region

#Region "来流初始化与时间步进"

    ''' <summary>
    ''' 初始化流场：全域 f = f_eq(rho = 1, u = U∞)，来流即时存在。
    ''' </summary>
    Public Sub InitializeFlow()
        Call Lattice.SetFreestream(FreestreamVelocity, 0.0R, 0.0R)
    End Sub

    ''' <summary>
    ''' 执行一个时间步（碰撞 → 流传播+边界+矩量）。
    ''' </summary>
    Public Sub Advance()
        Call Lattice.Advance(0.0R, FreestreamVelocity, 0.0R, 0.0R)
    End Sub

    ''' <summary>执行多个时间步。</summary>
    Public Sub Run(steps As Integer)
        For s As Integer = 1 To steps
            Call Advance()
        Next
    End Sub

    ''' <summary>GPU 路径下按需把矩量场回读到宿主数组。</summary>
    Public Sub EnsureMoments()
        Call Lattice.EnsureMoments()
    End Sub

#End Region

#Region "湍流 / 物理结果指标"

    ''' <summary>全场最大速度大小（仅流体格点）。</summary>
    Public Function ComputeMaxSpeed() As Double
        Return Lattice.MaxSpeed()
    End Function

    ''' <summary>全场平均速度大小（仅流体格点）。</summary>
    Public Function ComputeAverageSpeed() As Double
        Return Lattice.AverageSpeed()
    End Function

    ''' <summary>总动能（仅流体格点）。</summary>
    Public Function ComputeKineticEnergy() As Double
        Return Lattice.KineticEnergy()
    End Function

    ''' <summary>质量漂移（相对偏差，衡量不可压缩约束的满足程度）。</summary>
    Public Function ComputeMassDrift() As Double
        Return Lattice.MassDrift()
    End Function

    ''' <summary>
    ''' 指定下游平面 (i = downstreamPlaneI) 的尾流速度亏损：
    ''' deficit = U∞ − mean(U over 该平面流体格点)。
    ''' </summary>
    Public Function ComputeWakeDeficit(downstreamPlaneI As Integer) As Double
        If downstreamPlaneI < 0 OrElse downstreamPlaneI > Nx - 1 Then
            Throw New ArgumentOutOfRangeException(NameOf(downstreamPlaneI))
        End If

        Call Lattice.EnsureMoments()

        Dim plane As Integer = Ny * Nz
        Dim sumU As Double = 0.0R
        Dim count As Integer = 0

        For j As Integer = 0 To Ny - 1
            For k As Integer = 0 To Nz - 1
                Dim idx As Integer = downstreamPlaneI * plane + j * Nz + k

                If Lattice.Btype(idx) = CudaLbmBackend.CellFluid Then
                    sumU += Lattice.Ux(idx)
                    count += 1
                End If
            Next
        Next

        Dim meanU As Double = If(count > 0, sumU / count, 0.0R)
        Return FreestreamVelocity - meanU
    End Function

#End Region

#Region "IDisposable"

    Public Sub Dispose() Implements IDisposable.Dispose
        Call Lattice.Dispose()
    End Sub

#End Region

End Class
