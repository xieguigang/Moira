Option Strict On
Option Explicit On

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow

' /********************************************************************************/
'
'   LbmSolverF.vb
'
'   LbmSolver 的 float32（混合精度）版本 —— D3Q19 BGK / TRT，封闭方腔顶盖驱动。
'
'   几何、物理参数推导与 Double 基准 LbmSolver 逐项一致（仅精度为 float）；
'   数值核心由 LbmCoreF 提供（CPU 融合遍历 / CudaLbmBackend 设备驻留双路径）。
'
'   索引布局（与 TensorGrid / TensorF / VoxelShape 一致）：
'       idx = (i * ny + j) * nz + k，i:x  j:y  k:z（k = nz-1 平面为运动顶盖）
'
' /********************************************************************************/

''' <summary>
''' D3Q19 float32 LBM 求解器（封闭方腔顶盖驱动，BGK / TRT）。
''' </summary>
Public Class LbmSolverF : Implements IDisposable

#Region "网格与物理参数"

    Public ReadOnly Nx As Integer
    Public ReadOnly Ny As Integer
    Public ReadOnly Nz As Integer

    ''' <summary>雷诺数（基于 UWall 与 H = Nz-3）。</summary>
    Public ReadOnly Re As Double

    ''' <summary>运动粘度（格子单位）。</summary>
    Public ReadOnly Nu As Double

    ''' <summary>松弛时间 τ。</summary>
    Public ReadOnly Tau As Double

    ''' <summary>两半程壁间距（格子单位）。</summary>
    Public ReadOnly H As Double

    ''' <summary>格点总数。</summary>
    Public ReadOnly N As Integer

    ''' <summary>顶盖运动速度 U_wall（+x 方向）。</summary>
    Public Property UWall As Double

    ''' <summary>float32 求解核心（边界掩码 + 双路径步进）。</summary>
    Public ReadOnly Core As LbmCoreF

    ''' <summary>流体格点数。</summary>
    Public ReadOnly Property FluidCount As Integer
        Get
            Return Core.FluidCount
        End Get
    End Property

    ''' <summary>矩量场（TensorF 零拷贝视图，布局 (Nx, Ny, Nz)）。</summary>
    Public ReadOnly Property RhoF As TensorF
        Get
            Return Core.Rho
        End Get
    End Property

    Public ReadOnly Property UxF As TensorF
        Get
            Return Core.Ux
        End Get
    End Property

    Public ReadOnly Property UyF As TensorF
        Get
            Return Core.Uy
        End Get
    End Property

    Public ReadOnly Property UzF As TensorF
        Get
            Return Core.Uz
        End Get
    End Property

    ''' <summary>当前步数。</summary>
    Public ReadOnly Property StepCount As Long
        Get
            Return Core.StepCount
        End Get
    End Property

    ''' <summary>当前模拟时间。</summary>
    Public ReadOnly Property SimTime As Double
        Get
            Return Core.SimTime
        End Get
    End Property

    ''' <summary>是否正在使用 CUDA 加速。</summary>
    Public ReadOnly Property UseGpu As Boolean
        Get
            Return Core.UseGpu
        End Get
    End Property

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建封闭方腔（外层固体壳 + 顶盖驱动）float32 LBM 求解器。
    ''' </summary>
    ''' <param name="useGpu">True 时尝试 CUDA 加速，失败自动回退 CPU float 路径</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   re As Double,
                   Optional uWall As Double = 0.05R,
                   Optional model As CollisionModel = CollisionModel.BGK,
                   Optional useGpu As Boolean = True)

        If nx < 8 OrElse ny < 8 OrElse nz < 8 Then
            Throw New ArgumentException("网格各方向至少 8 节点（壳 2 + 流体 6）")
        End If

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me.UWall = uWall
        Me.Re = re
        Me.N = nx * ny * nz
        Me.H = nz - 3

        ' ---- 物理参数：nu = Uwall * H / Re，tau = nu / cs2 + 1/2 ----
        If uWall > 0.0R Then
            Nu = uWall * H / re
            Tau = Nu / LatticeD3Q19.Cs2 + 0.5R

            Dim tauMin As Double = If(model = CollisionModel.BGK, 0.51R, 0.5005R)
            If Tau < tauMin Then
                Throw New ArgumentException(
                    $"Re={re} 对该网格过高：τ={Tau:F4} < {tauMin:F4}（{model} 失稳）。" &
                    "请降低 Re 或 U_wall 或加密网格，或改用 TRT 模型。")
            End If
        Else
            Nu = 0.0R
            Tau = 1.0R
        End If

        ' ---- 几何：外层固体壳，z = nz-1 平面为运动顶盖（CellMovingSolid）----
        Dim plane As Integer = Ny * Nz
        Dim btype(N - 1) As Byte

        For i As Integer = 0 To nx - 1
            For j As Integer = 0 To ny - 1
                For k As Integer = 0 To nz - 1
                    Dim idx As Integer = i * plane + j * Nz + k
                    Dim isShell As Boolean =
                        i = 0 OrElse i = nx - 1 OrElse
                        j = 0 OrElse j = ny - 1 OrElse
                        k = 0 OrElse k = nz - 1

                    If isShell Then
                        If k = nz - 1 Then
                            btype(idx) = CudaLbmBackend.CellMovingSolid
                        Else
                            btype(idx) = CudaLbmBackend.CellSolid
                        End If
                    Else
                        btype(idx) = CudaLbmBackend.CellFluid
                    End If
                Next
            Next
        Next

        Core = New LbmCoreF(nx, ny, nz, btype, 1.0R / Tau, model, useGpu)
    End Sub

#End Region

#Region "时间步进"

    ''' <summary>执行一个时间步（碰撞 → 流传播+边界+矩量）。</summary>
    Public Sub Advance()
        Call Core.Advance(UWall)
    End Sub

    ''' <summary>执行多个时间步。</summary>
    Public Sub Run(steps As Integer)
        Call Core.Run(steps, UWall)
    End Sub

    ''' <summary>全域静止初始化：f = w，rho = 1，u = 0。</summary>
    Public Sub InitRest()
        Call Core.InitRest()
    End Sub

#End Region

#Region "诊断指标"

    ''' <summary>GPU 路径下按需把矩量场回读到宿主数组。</summary>
    Public Sub EnsureMoments()
        Call Core.EnsureMoments()
    End Sub

    ''' <summary>回读完整的分布函数（19*n，方向主序，新数组）。</summary>
    Public Function DownloadF() As Single()
        Return Core.DownloadF()
    End Function

    ''' <summary>全场最大速度大小（仅流体格点）。</summary>
    Public Function MaxSpeed() As Double
        Return Core.MaxSpeed()
    End Function

    ''' <summary>总动能（仅流体格点，0.5·ρ|u|²）。</summary>
    Public Function KineticEnergy() As Double
        Return Core.KineticEnergy()
    End Function

    ''' <summary>质量漂移（相对初始流体质量的相对偏差）。</summary>
    Public Function MassDrift() As Double
        Return Core.MassDrift()
    End Function

    ''' <summary>
    ''' 中心竖直线（i = Nx/2, j = Ny/2）上的 Uy 剖面（Ghia 基准对比用）。
    ''' 返回数组下标为 k（z 自下而上）。
    ''' </summary>
    Public Function CenterlineVy() As Double()
        Call Core.EnsureMoments()

        Dim i As Integer = Nx \ 2
        Dim plane As Integer = Ny * Nz
        Dim result(Nz - 1) As Double

        For k As Integer = 0 To Nz - 1
            result(k) = Core.Uy(i * plane + (Ny \ 2) * Nz + k)
        Next

        Return result
    End Function

    ''' <summary>
    ''' 中心水平线（i = Nx/2, k = Nz/2）上的 Ux 剖面（Ghia 基准对比用）。
    ''' 返回数组下标为 j（y 自下而上）。
    ''' </summary>
    Public Function CenterlineUx() As Double()
        Call Core.EnsureMoments()

        Dim i As Integer = Nx \ 2
        Dim plane As Integer = Ny * Nz
        Dim result(Ny - 1) As Double

        For j As Integer = 0 To Ny - 1
            result(j) = Core.Ux(i * plane + j * Nz + (Nz \ 2))
        Next

        Return result
    End Function

    ''' <summary>获取单元边界类型。</summary>
    Public Function CellType(i As Integer, j As Integer, k As Integer) As Byte
        Return Core.CellType(i, j, k)
    End Function

#End Region

#Region "IDisposable"

    Public Sub Dispose() Implements IDisposable.Dispose
        Call Core.Dispose()
    End Sub

#End Region

End Class
