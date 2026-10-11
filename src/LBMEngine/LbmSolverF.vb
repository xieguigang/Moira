Option Strict On
Option Explicit On

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow

' /********************************************************************************/
'
'   LbmSolverF.vb
'
'   LbmSolver 的 float32（混合精度）版本 —— D3Q19 BGK / TRT，封闭方腔顶盖驱动。
'
'   设计要点：
'       1. 分布函数以方向主序打包为 Single() 数组：_f(a * n + idx)，
'          与 CUDA 内核 moira_lbm_f32.cu 的内存布局严格一致。
'       2. CPU 路径：每步两个融合遍历（碰撞 → 流传播+边界+矩量），
'          全 float 运算，避免中间张量分配；矩量场以 TensorF.Wrap 零拷贝暴露。
'       3. GPU 路径：CudaLbmBackend.TryRegister 成功时，f / fc / 矩量常驻显存，
'          每步仅 2 次 kernel launch；矩量仅在诊断 / 可视化时回读。
'       4. 算法语义（平衡态、碰撞系数、halfway bounce-back、运动壁修正、
'          固体冻结）与 Double 基准 LbmSolver 逐项一致，仅精度为 float。
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

    ''' <summary>松弛频率 ω⁻ = 1/τ。</summary>
    Public ReadOnly Omega As Double

    ''' <summary>TRT 的 ω⁺（MagicLambda = 1/4）。</summary>
    Public ReadOnly OmegaP As Double

    ''' <summary>顶盖运动速度 U_wall（+x 方向）。</summary>
    Public Property UWall As Double

    ''' <summary>碰撞模型（BGK / TRT）。</summary>
    Public Property Model As CollisionModel

    ''' <summary>两半程壁间距（格子单位）。</summary>
    Public ReadOnly H As Double

    ''' <summary>流体格点数。</summary>
    Public ReadOnly FluidCount As Integer

    ''' <summary>格点总数。</summary>
    Public ReadOnly N As Integer

#End Region

#Region "状态数据（方向主序打包）"

    ''' <summary>分布函数（19*n，方向主序：f(a*n + idx)）。</summary>
    Private ReadOnly _f As Single()
    ''' <summary>碰撞后分布函数暂存（仅 CPU 路径使用）。</summary>
    Private ReadOnly _fc As Single()

    ''' <summary>密度场（长度 n，与矩量 TensorF 共享内存）。</summary>
    Private ReadOnly _rho As Single()
    Private ReadOnly _ux As Single()
    Private ReadOnly _uy As Single()
    Private ReadOnly _uz As Single()

    ''' <summary>单元边界类型掩码（取值见 CudaLbmBackend.Cell* 常量）。</summary>
    Public ReadOnly Btype As Byte()

    ''' <summary>矩量场（零拷贝包裹宿主数组，供可视化 / 诊断使用）。</summary>
    Public ReadOnly Rho As TensorF
    Public ReadOnly Ux As TensorF
    Public ReadOnly Uy As TensorF
    Public ReadOnly Uz As TensorF

#End Region

#Region "GPU 路径"

    Private ReadOnly _gpu As CudaLbmBackend
    Private ReadOnly _gpuState As CudaLbmBackend.DeviceState
    Private _momentsDirty As Boolean

    ''' <summary>是否正在使用 CUDA 加速。</summary>
    Public ReadOnly Property UseGpu As Boolean
        Get
            Return _gpuState IsNot Nothing
        End Get
    End Property

#End Region

#Region "步进计数"

    Private _step As Long
    Private _time As Double

    Public ReadOnly Property StepCount As Long
        Get
            Return _step
        End Get
    End Property

    Public ReadOnly Property SimTime As Double
        Get
            Return _time
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
        Me.Model = model
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

        Omega = 1.0R / Tau
        OmegaP = 1.0R / (0.5R + MrtBasisD3Q19.MagicLambda / (Tau - 0.5R))

        ' ---- 几何：外层固体壳，z = nz-1 平面为运动顶盖（CellMovingSolid）----
        _f = New Single(LatticeD3Q19.Q * N - 1) {}
        _fc = New Single(LatticeD3Q19.Q * N - 1) {}
        _rho = New Single(N - 1) {}
        _ux = New Single(N - 1) {}
        _uy = New Single(N - 1) {}
        _uz = New Single(N - 1) {}
        Btype = New Byte(N - 1) {}

        Dim plane As Integer = Ny * Nz

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
                            Btype(idx) = CudaLbmBackend.CellMovingSolid
                        Else
                            Btype(idx) = CudaLbmBackend.CellSolid
                        End If
                    Else
                        Btype(idx) = CudaLbmBackend.CellFluid
                        FluidCount += 1
                    End If
                Next
            Next
        Next

        Rho = TensorF.Wrap(_rho, {Nx, Ny, Nz})
        Ux = TensorF.Wrap(_ux, {Nx, Ny, Nz})
        Uy = TensorF.Wrap(_uy, {Nx, Ny, Nz})
        Uz = TensorF.Wrap(_uz, {Nx, Ny, Nz})

        ' ---- 初始化：全域 f = f_eq(rho = 1, u = 0) = w_α ----
        InitRest()

        ' ---- GPU 注册（失败静默回退 CPU）----
        If useGpu AndAlso CudaLbmBackend.TryRegister() AndAlso CudaLbmBackend.Current.IsReady Then
            _gpu = CudaLbmBackend.Current
            _gpuState = _gpu.Allocate(N, Btype)
            UploadFToDevice()
            _momentsDirty = True
        End If
    End Sub

    ''' <summary>全域静止初始化：f = w，rho = 1，u = 0。</summary>
    Public Sub InitRest()
        For a As Integer = 0 To LatticeD3Q19.Q - 1
            Dim wa As Single = CSng(LatticeD3Q19.W(a))
            Dim offset As Integer = a * N

            For idx As Integer = 0 To N - 1
                _f(offset + idx) = wa
            Next
        Next

        For idx As Integer = 0 To N - 1
            _rho(idx) = 1.0F
            _ux(idx) = 0.0F
            _uy(idx) = 0.0F
            _uz(idx) = 0.0F
        Next

        If _gpuState IsNot Nothing Then
            Call _gpu.UploadF(_gpuState, _f)
            _momentsDirty = True
        End If
    End Sub

#End Region

#Region "时间步进"

    ''' <summary>执行一个时间步（碰撞 → 流传播+边界+矩量）。</summary>
    Public Sub Advance()
        If _gpuState IsNot Nothing Then
            AdvanceGpu()
        Else
            AdvanceCpu()
        End If

        _step += 1L
        _time += 1.0R
    End Sub

    ''' <summary>执行多个时间步。</summary>
    Public Sub Run(steps As Integer)
        For s As Integer = 1 To steps
            Call Advance()
        Next
    End Sub

    Private Sub AdvanceGpu()
        Call _gpu.Collide(_gpuState, CSng(Omega), CSng(OmegaP), Model = CollisionModel.TRT)
        Call _gpu.Stream(_gpuState, CSng(UWall), 0.0F, 0.0F, 0.0F, Nx, Ny, Nz)
        _momentsDirty = True
    End Sub

    ''' <summary>
    ''' CPU 融合路径：两个遍历完成整步计算（与 CUDA 内核语义逐项一致）。
    ''' </summary>
    Private Sub AdvanceCpu()
        Dim trt As Boolean = Model = CollisionModel.TRT
        Dim omM As Single = CSng(Omega)
        Dim omP As Single = CSng(OmegaP)
        Dim cA As Single = 1.0F - 0.5F * (omP + omM)
        Dim cB As Single = 0.5F * (omM - omP)
        Dim cC As Single = 0.5F * (omP + omM)
        Dim cD As Single = 0.5F * (omP - omM)
        Dim uwall As Single = CSng(UWall)
        Dim plane As Integer = Ny * Nz

        ' ---- Pass 1：碰撞（f -> fc）----
        For idx As Integer = 0 To N - 1
            Dim r As Single = _rho(idx)
            Dim vx As Single = _ux(idx)
            Dim vy As Single = _uy(idx)
            Dim vz As Single = _uz(idx)

            If trt Then
                For a As Integer = 0 To LatticeD3Q19.Q - 1
                    Dim o As Integer = LatticeD3Q19.Opp(a)
                    Dim fe As Single = FEquilibrium(a, r, vx, vy, vz)
                    Dim feo As Single = FEquilibrium(o, r, vx, vy, vz)
                    _fc(a * N + idx) = cA * _f(a * N + idx) + cB * _f(o * N + idx) +
                                       cC * fe + cD * feo
                Next
            Else
                For a As Integer = 0 To LatticeD3Q19.Q - 1
                    Dim fe As Single = FEquilibrium(a, r, vx, vy, vz)
                    _fc(a * N + idx) = (1.0F - omM) * _f(a * N + idx) + omM * fe
                Next
            End If
        Next

        ' ---- Pass 2：流传播 + 边界 + 矩量（fc -> f）----
        For idx As Integer = 0 To N - 1
            Dim i As Integer = idx \ plane
            Dim rem As Integer = idx - i * plane
            Dim j As Integer = rem \ Nz
            Dim k As Integer = rem - j * Nz

            Dim bt As Byte = Btype(idx)

            ' 固体（含运动顶盖）：冻结在静止平衡 f = w
            If bt = CudaLbmBackend.CellSolid OrElse bt = CudaLbmBackend.CellMovingSolid Then
                _f(idx) = 1.0F / 3.0F
                For a As Integer = 1 To LatticeD3Q19.Q - 1
                    _f(a * N + idx) = If(a <= 6, 1.0F / 18.0F, 1.0F / 36.0F)
                Next
                _rho(idx) = 1.0F
                _ux(idx) = 0.0F : _uy(idx) = 0.0F : _uz(idx) = 0.0F
                Continue For
            End If

            ' 流体：逐方向 gather
            Dim rho As Single = 0.0F
            Dim mx As Single = 0.0F, my As Single = 0.0F, mz As Single = 0.0F

            For a As Integer = 0 To LatticeD3Q19.Q - 1
                Dim si As Integer = i - LatticeD3Q19.EX(a)
                Dim sj As Integer = j - LatticeD3Q19.EY(a)
                Dim sk As Integer = k - LatticeD3Q19.EZ(a)
                Dim v As Single

                If si < 0 OrElse si >= Nx OrElse sj < 0 OrElse sj >= Ny OrElse sk < 0 OrElse sk >= Nz Then
                    ' 越界：方腔中只会写入固体壳格点（随后被冻结），兜底置零
                    v = 0.0F
                Else
                    Dim sidx As Integer = si * plane + sj * Nz + sk
                    Dim sb As Byte = Btype(sidx)

                    If sb = CudaLbmBackend.CellSolid OrElse sb = CudaLbmBackend.CellMovingSolid Then
                        ' halfway bounce-back（源格点为固体）
                        v = _fc(LatticeD3Q19.Opp(a) * N + idx)
                        If sb = CudaLbmBackend.CellMovingSolid Then
                            ' 运动壁修正：+6 * w * (e_a . u_w) * rho，u_w = (uwall, 0, 0)
                            v += 6.0F * CSng(LatticeD3Q19.W(a)) * CSng(LatticeD3Q19.EX(a)) * uwall * _rho(idx)
                        End If
                    Else
                        v = _fc(a * N + sidx)
                    End If
                End If

                _f(a * N + idx) = v

                rho += v
                mx += v * LatticeD3Q19.EX(a)
                my += v * LatticeD3Q19.EY(a)
                mz += v * LatticeD3Q19.EZ(a)
            Next

            Dim invRho As Single = 1.0F / If(rho > 1.0E-9F, rho, 1.0E-9F)
            _rho(idx) = rho
            _ux(idx) = mx * invRho
            _uy(idx) = my * invRho
            _uz(idx) = mz * invRho
        Next
    End Sub

    ''' <summary>f_eq = w·ρ·[1 + 3(e·u) + 4.5(e·u)² − 1.5|u|²]</summary>
    Private Shared Function FEquilibrium(a As Integer, rho As Single,
                                         vx As Single, vy As Single, vz As Single) As Single
        Dim eu As Single = LatticeD3Q19.EX(a) * vx + LatticeD3Q19.EY(a) * vy + LatticeD3Q19.EZ(a) * vz
        Dim u2 As Single = vx * vx + vy * vy + vz * vz
        Return CSng(LatticeD3Q19.W(a)) * rho *
               (1.0F + 3.0F * eu + 4.5F * eu * eu - 1.5F * u2)
    End Function

#End Region

#Region "矩量同步与宿主访问"

    ''' <summary>GPU 路径下按需把矩量场回读到宿主数组。</summary>
    Public Sub EnsureMoments()
        If _gpuState IsNot Nothing AndAlso _momentsDirty Then
            Dim rho(), ux(), uy(), uz() As Single
            Call _gpu.ReadMoments(_gpuState, rho, ux, uy, uz)
            Array.Copy(rho, _rho, N)
            Array.Copy(ux, _ux, N)
            Array.Copy(uy, _uy, N)
            Array.Copy(uz, _uz, N)
            _momentsDirty = False
        End If
    End Sub

    ''' <summary>上传宿主分布函数到设备（GPU 路径）。</summary>
    Private Sub UploadFToDevice()
        If _gpuState IsNot Nothing Then
            Call _gpu.UploadF(_gpuState, _f)
        End If
    End Sub

    ''' <summary>回读完整的分布函数（19*n，方向主序，新数组）。</summary>
    Public Function DownloadF() As Single()
        If _gpuState IsNot Nothing Then
            Return _gpu.DownloadF(_gpuState)
        Else
            Dim copy(LatticeD3Q19.Q * N - 1) As Single
            Call Array.Copy(_f, copy, copy.Length)
            Return copy
        End If
    End Function

    ''' <summary>获取单元边界类型（便于风洞求解器复用掩码构造逻辑）。</summary>
    Public Function CellType(i As Integer, j As Integer, k As Integer) As Byte
        Return Btype((i * Ny + j) * Nz + k)
    End Function

#End Region

#Region "诊断指标"

    ''' <summary>全场最大速度大小（仅流体格点）。</summary>
    Public Function MaxSpeed() As Double
        Call EnsureMoments()

        Dim maxS As Double = 0.0R

        For idx As Integer = 0 To N - 1
            If Btype(idx) <> CudaLbmBackend.CellFluid Then
                Continue For
            End If

            Dim vx As Double = _ux(idx)
            Dim vy As Double = _uy(idx)
            Dim vz As Double = _uz(idx)
            Dim s As Double = Math.Sqrt(vx * vx + vy * vy + vz * vz)

            If s > maxS Then
                maxS = s
            End If
        Next

        Return maxS
    End Function

    ''' <summary>总动能（仅流体格点，0.5·ρ|u|²）。</summary>
    Public Function KineticEnergy() As Double
        Call EnsureMoments()

        Dim sum As Double = 0.0R

        For idx As Integer = 0 To N - 1
            If Btype(idx) <> CudaLbmBackend.CellFluid Then
                Continue For
            End If

            Dim vx As Double = _ux(idx)
            Dim vy As Double = _uy(idx)
            Dim vz As Double = _uz(idx)

            sum += 0.5R * _rho(idx) * (vx * vx + vy * vy + vz * vz)
        Next

        Return sum
    End Function

    ''' <summary>质量漂移（相对初始流体质量的相对偏差）。</summary>
    Public Function MassDrift() As Double
        Call EnsureMoments()

        Dim sum As Double = 0.0R

        For idx As Integer = 0 To N - 1
            If Btype(idx) = CudaLbmBackend.CellFluid Then
                sum += _rho(idx)
            End If
        Next

        Return If(FluidCount > 0, (sum - FluidCount) / FluidCount, 0.0R)
    End Function

    ''' <summary>指定平面 (i = nx/2) 中心线上的 Uy 剖面（Ghia 对比用）。</summary>
    Public Function CenterlineVy() As Double()
        Call EnsureMoments()

        Dim i As Integer = Nx \ 2
        Dim plane As Integer = Ny * Nz
        Dim result(Nz - 1) As Double

        For k As Integer = 0 To Nz - 1
            result(k) = _uy(i * plane + (Ny \ 2) * Nz + k)
        Next

        Return result
    End Function

#End Region

#Region "IDisposable"

    Public Sub Dispose() Implements IDisposable.Dispose
        If _gpuState IsNot Nothing Then
            Call _gpuState.Dispose()
        End If
    End Sub

#End Region

End Class
