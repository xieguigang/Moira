Option Strict On
Option Explicit On

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow

' /********************************************************************************/
'
'   LbmCoreF.vb
'
'   LBM float32 求解核心 —— 方腔（LbmSolverF）与风洞（WindTunnelSolver）共享的
'   双路径实现层：
'
'       1. CPU 路径：每步两个融合遍历（碰撞 → 流传播+边界+矩量），全 float 运算；
'       2. GPU 路径：CudaLbmBackend 设备驻留（f / fc / 矩量常驻显存），
'          每步仅 2 次 kernel launch，矩量按需回读；
'
'   两路径算法语义与 moira_lbm_f32.cu 内核逐项一致。
'
'   单元边界类型（Btype，与 moira_lbm_f32.cu 严格一致）：
'       0 = 流体；1 = 固体（无滑移反弹）；2 = 入口（定常平衡态）；
'       3 = 出口（零梯度）；5 = 运动固体（顶盖）。
'       越界方向自动 specular 镜面反射（自由滑移），用于顶面 / 侧面 / 自由滑移地面。
'
' /********************************************************************************/

''' <summary>
''' D3Q19 float32 LBM 求解核心（BGK / TRT）。
''' </summary>
Public Class LbmCoreF : Implements IDisposable

#Region "容量上限"

    ''' <summary>
    ''' 单求解器格点数硬上限：f / fc 为 19×N 的 Single 数组（方向主序），
    ''' 单数组长度不得超过 Int32.MaxValue（.NET 数组元素上限），
    ''' 即 N ≤ Int32.MaxValue \ 19 ≈ 112,936,676。
    ''' </summary>
    Public Const MaxGridPoints As Long = Int32.MaxValue \ LatticeD3Q19.Q

#End Region

#Region "网格"

    Public ReadOnly Nx As Integer
    Public ReadOnly Ny As Integer
    Public ReadOnly Nz As Integer
    Public ReadOnly N As Integer

    ''' <summary>流体格点数（Btype = CellFluid）。</summary>
    Public ReadOnly FluidCount As Integer

    ''' <summary>单元边界类型掩码（长度 N，取值见 CudaLbmBackend.Cell* 常量）。</summary>
    Public ReadOnly Btype As Byte()

#End Region

#Region "状态数据（方向主序打包 + 零拷贝矩量张量）"

    ''' <summary>分布函数（19*n，方向主序）。</summary>
    Protected ReadOnly _f As Single()
    ''' <summary>碰撞后分布函数暂存（仅 CPU 路径使用）。</summary>
    Protected ReadOnly _fc As Single()

    Private ReadOnly _rho As Single()
    Private ReadOnly _ux As Single()
    Private ReadOnly _uy As Single()
    Private ReadOnly _uz As Single()

    ''' <summary>矩量场（零拷贝包裹宿主数组，布局 (Nx, Ny, Nz)）。</summary>
    Public ReadOnly Rho As TensorF
    Public ReadOnly Ux As TensorF
    Public ReadOnly Uy As TensorF
    Public ReadOnly Uz As TensorF

#End Region

#Region "物理参数"

    Private _omega As Double
    Private _omegaP As Double

    ''' <summary>松弛频率 ω⁻ = 1/τ。</summary>
    Public Property Omega As Double
        Get
            Return _omega
        End Get
        Set(value As Double)
            _omega = value
            _omegaP = 1.0R / (0.5R + MrtBasisD3Q19.MagicLambda /
                              (1.0R / System.Math.Max(value, 1.0E-9R) - 0.5R))
        End Set
    End Property

    ''' <summary>TRT 的 ω⁺（由 ω⁻ 与 MagicLambda = 1/4 推导）。</summary>
    Public ReadOnly Property OmegaP As Double
        Get
            Return _omegaP
        End Get
    End Property

    Private _model As CollisionModel = CollisionModel.BGK

    ''' <summary>碰撞模型（BGK / TRT）。</summary>
    Public Property Model As CollisionModel
        Get
            Return _model
        End Get
        Set(value As CollisionModel)
            If value = CollisionModel.MRT Then
                Throw New ArgumentException("float 求解核心仅支持 BGK / TRT")
            End If
            _model = value
        End Set
    End Property

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
    ''' 创建 float32 LBM 求解核心。
    ''' </summary>
    ''' <param name="btype">单元边界类型掩码（长度 nx*ny*nz）</param>
    ''' <param name="omega">松弛频率 ω⁻ = 1/τ</param>
    ''' <param name="model">碰撞模型（BGK / TRT）</param>
    ''' <param name="useGpu">True 时尝试 CUDA 加速，失败自动回退 CPU float 路径</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   btype As Byte(),
                   omega As Double,
                   Optional model As CollisionModel = CollisionModel.BGK,
                   Optional useGpu As Boolean = True)

        If nx < 5 OrElse ny < 5 OrElse nz < 5 Then
            Throw New ArgumentException("网格各方向至少 5 节点")
        End If

        Dim nTotal As Long = CLng(nx) * ny * nz

        If nTotal > MaxGridPoints Then
            Throw New InvalidOperationException(
                $"网格格点数 {nTotal:N0} 超出硬上限 {MaxGridPoints:N0}" &
                $"（f/fc 分布函数为 19×N 的 Single 数组，受 .NET 数组长度上限约束）。" &
                "请降低体素化分辨率或减小计算域。")
        End If

        If btype Is Nothing OrElse btype.Length <> nx * ny * nz Then
            Throw New ArgumentException($"btype 长度必须为 {nx * ny * nz}")
        End If
        If omega <= 0 OrElse omega >= 2 Then
            Throw New ArgumentException($"ω = {omega} 越界（要求 0 < ω < 2）")
        End If

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me.N = nx * ny * nz
        Me.Btype = CType(btype.Clone(), Byte())
        Me.Omega = omega
        Me.Model = model

        For Each bt As Byte In Btype
            If bt = CudaLbmBackend.CellFluid Then
                FluidCount += 1
            End If
        Next

        _f = New Single(LatticeD3Q19.Q * N - 1) {}
        _fc = New Single(LatticeD3Q19.Q * N - 1) {}
        _rho = New Single(N - 1) {}
        _ux = New Single(N - 1) {}
        _uy = New Single(N - 1) {}
        _uz = New Single(N - 1) {}

        Rho = TensorF.Wrap(_rho, {Nx, Ny, Nz})
        Ux = TensorF.Wrap(_ux, {Nx, Ny, Nz})
        Uy = TensorF.Wrap(_uy, {Nx, Ny, Nz})
        Uz = TensorF.Wrap(_uz, {Nx, Ny, Nz})

        InitRest()

        ' ---- GPU 注册（失败静默回退 CPU）----
        If useGpu AndAlso CudaLbmBackend.TryRegister() AndAlso CudaLbmBackend.Current.IsReady Then
            _gpu = CudaLbmBackend.Current
            _gpuState = _gpu.Allocate(N, Btype)
            Call _gpu.UploadF(_gpuState, _f)
            Call _gpu.ComputeMoments(_gpuState)
            _momentsDirty = False
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
            Call _gpu.ComputeMoments(_gpuState)
            _momentsDirty = False
        End If
    End Sub

    ''' <summary>
    ''' 全域来流初始化：f = f_eq(rho = 1, u = uin)（固体格点随后被步进冻结）。
    ''' </summary>
    Public Sub SetFreestream(uinx As Double, uiny As Double, uinz As Double)
        Dim vx As Single = CSng(uinx)
        Dim vy As Single = CSng(uiny)
        Dim vz As Single = CSng(uinz)

        For a As Integer = 0 To LatticeD3Q19.Q - 1
            Dim fe As Single = FEquilibrium(a, 1.0F, vx, vy, vz)
            Dim offset As Integer = a * N

            For idx As Integer = 0 To N - 1
                _f(offset + idx) = fe
            Next
        Next

        For idx As Integer = 0 To N - 1
            _rho(idx) = 1.0F

            If Btype(idx) = CudaLbmBackend.CellSolid OrElse
                Btype(idx) = CudaLbmBackend.CellMovingSolid Then
                _ux(idx) = 0.0F : _uy(idx) = 0.0F : _uz(idx) = 0.0F
            Else
                _ux(idx) = vx : _uy(idx) = vy : _uz(idx) = vz
            End If
        Next

        If _gpuState IsNot Nothing Then
            Call _gpu.UploadF(_gpuState, _f)
            Call _gpu.ComputeMoments(_gpuState)
            _momentsDirty = False
        End If
    End Sub

#End Region

#Region "时间步进"

    ''' <summary>
    ''' 执行一个时间步（碰撞 → 流传播+边界+矩量）。
    ''' </summary>
    ''' <param name="uwall">运动固体（CellMovingSolid）的壁面速度（+x）</param>
    ''' <param name="uinx">入口（CellInlet）平衡态速度 x 分量</param>
    ''' <param name="uiny">入口平衡态速度 y 分量</param>
    ''' <param name="uinz">入口平衡态速度 z 分量</param>
    Public Sub Advance(uwall As Double,
                       Optional uinx As Double = 0,
                       Optional uiny As Double = 0,
                       Optional uinz As Double = 0)

        If _gpuState IsNot Nothing Then
            Call _gpu.Collide(_gpuState, CSng(_omega), CSng(_omegaP), _model = CollisionModel.TRT)
            Call _gpu.Stream(_gpuState, CSng(uwall), CSng(uinx), CSng(uiny), CSng(uinz),
                             Nx, Ny, Nz)
            _momentsDirty = True
        Else
            AdvanceCpu(CSng(uwall), CSng(uinx), CSng(uiny), CSng(uinz))
        End If

        _step += 1L
        _time += 1.0R
    End Sub

    ''' <summary>执行多个时间步。</summary>
    Public Sub Run(steps As Integer,
                   Optional uwall As Double = 0,
                   Optional uinx As Double = 0,
                   Optional uiny As Double = 0,
                   Optional uinz As Double = 0)

        For s As Integer = 1 To steps
            Call Advance(uwall, uinx, uiny, uinz)
        Next
    End Sub

    Private Sub AdvanceCpu(uwall As Single, uinx As Single, uiny As Single, uinz As Single)
        Dim trt As Boolean = _model = CollisionModel.TRT
        Dim omM As Single = CSng(_omega)
        Dim omP As Single = CSng(_omegaP)
        Dim cA As Single = 1.0F - 0.5F * (omP + omM)
        Dim cB As Single = 0.5F * (omM - omP)
        Dim cC As Single = 0.5F * (omP + omM)
        Dim cD As Single = 0.5F * (omP - omM)
        Dim plane As Integer = Ny * Nz
        Dim q As Integer = LatticeD3Q19.Q

        ' ---- Pass 1：碰撞（f -> fc）----
        For idx As Integer = 0 To N - 1
            Dim r As Single = _rho(idx)
            Dim vx As Single = _ux(idx)
            Dim vy As Single = _uy(idx)
            Dim vz As Single = _uz(idx)

            If trt Then
                For a As Integer = 0 To q - 1
                    Dim o As Integer = LatticeD3Q19.Opp(a)
                    Dim fe As Single = FEquilibrium(a, r, vx, vy, vz)
                    Dim feo As Single = FEquilibrium(o, r, vx, vy, vz)
                    _fc(a * N + idx) = cA * _f(a * N + idx) + cB * _f(o * N + idx) +
                                       cC * fe + cD * feo
                Next
            Else
                For a As Integer = 0 To q - 1
                    Dim fe As Single = FEquilibrium(a, r, vx, vy, vz)
                    _fc(a * N + idx) = (1.0F - omM) * _f(a * N + idx) + omM * fe
                Next
            End If
        Next

        ' ---- Pass 2：流传播 + 边界 + 矩量（fc -> f）----
        For idx As Integer = 0 To N - 1
            Dim i As Integer = idx \ plane
            Dim remd As Integer = idx - i * plane
            Dim j As Integer = remd \ Nz
            Dim k As Integer = remd - j * Nz

            Dim bt As Byte = Btype(idx)

            ' 固体（含运动顶盖）：冻结在静止平衡 f = w
            If bt = CudaLbmBackend.CellSolid OrElse bt = CudaLbmBackend.CellMovingSolid Then
                _f(idx) = 1.0F / 3.0F
                For a As Integer = 1 To q - 1
                    _f(a * N + idx) = If(a <= 6, 1.0F / 18.0F, 1.0F / 36.0F)
                Next
                _rho(idx) = 1.0F
                _ux(idx) = 0.0F : _uy(idx) = 0.0F : _uz(idx) = 0.0F
                Continue For
            End If

            ' 入口：定常平衡态分布
            If bt = CudaLbmBackend.CellInlet Then
                For a As Integer = 0 To q - 1
                    _f(a * N + idx) = FEquilibrium(a, 1.0F, uinx, uiny, uinz)
                Next
                _rho(idx) = 1.0F
                _ux(idx) = uinx : _uy(idx) = uiny : _uz(idx) = uinz
                Continue For
            End If

            ' 出口：零梯度（复制 x-1 邻居的碰撞后分布）
            If bt = CudaLbmBackend.CellOutlet Then
                Dim src As Integer = idx - plane
                Dim r As Single = 0.0F
                Dim mx As Single = 0.0F, my As Single = 0.0F, mz As Single = 0.0F

                For a As Integer = 0 To q - 1
                    Dim v As Single = _fc(a * N + src)
                    _f(a * N + idx) = v
                    r += v
                    mx += v * LatticeD3Q19.EX(a)
                    my += v * LatticeD3Q19.EY(a)
                    mz += v * LatticeD3Q19.EZ(a)
                Next

                Dim inv As Single = 1.0F / If(r > 1.0E-9F, r, 1.0E-9F)
                _rho(idx) = r
                _ux(idx) = mx * inv : _uy(idx) = my * inv : _uz(idx) = mz * inv
                Continue For
            End If

            ' 流体：逐方向 gather（流传播 + halfway bounce-back / specular 自由滑移）
            Dim rho As Single = 0.0F
            Dim sx As Single = 0.0F, sy As Single = 0.0F, sz As Single = 0.0F

            For a As Integer = 0 To q - 1
                Dim si As Integer = i - LatticeD3Q19.EX(a)
                Dim sj As Integer = j - LatticeD3Q19.EY(a)
                Dim sk As Integer = k - LatticeD3Q19.EZ(a)
                Dim v As Single

                If si < 0 OrElse si >= Nx OrElse sj < 0 OrElse sj >= Ny OrElse sk < 0 OrElse sk >= Nz Then
                    ' 越界：顶 / 底 / 侧面 specular 镜面自由滑移；
                    ' x 向越界在合法边界配置（入口/出口平面为 btype=2/3）下不应出现，兜底置零。
                    If sk < 0 OrElse sk >= Nz Then
                        v = _fc(SpecularZ(a) * N + idx)
                    ElseIf sj < 0 OrElse sj >= Ny Then
                        v = _fc(SpecularY(a) * N + idx)
                    Else
                        v = 0.0F
                    End If
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
                sx += v * LatticeD3Q19.EX(a)
                sy += v * LatticeD3Q19.EY(a)
                sz += v * LatticeD3Q19.EZ(a)
            Next

            Dim invRho As Single = 1.0F / If(rho > 1.0E-9F, rho, 1.0E-9F)
            _rho(idx) = rho
            _ux(idx) = sx * invRho
            _uy(idx) = sy * invRho
            _uz(idx) = sz * invRho
        Next
    End Sub

    ''' <summary>f_eq = w·ρ·[1 + 3(e·u) + 4.5(e·u)² − 1.5|u|²]</summary>
    Public Shared Function FEquilibrium(a As Integer, rho As Single,
                                        vx As Single, vy As Single, vz As Single) As Single
        Dim eu As Single = LatticeD3Q19.EX(a) * vx + LatticeD3Q19.EY(a) * vy + LatticeD3Q19.EZ(a) * vz
        Dim u2 As Single = vx * vx + vy * vy + vz * vz
        Return CSng(LatticeD3Q19.W(a)) * rho *
               (1.0F + 3.0F * eu + 4.5F * eu * eu - 1.5F * u2)
    End Function

    ''' <summary>specular 镜面：翻转 EZ 分量（顶 / 底自由滑移）。</summary>
    Private Shared Function SpecularZ(a As Integer) As Integer
        ' D3Q19 z 镜像映射
        Select Case a
            Case 5 : Return 6
            Case 6 : Return 5
            Case 11 : Return 13
            Case 12 : Return 14
            Case 13 : Return 11
            Case 14 : Return 12
            Case 15 : Return 17
            Case 16 : Return 18
            Case 17 : Return 15
            Case 18 : Return 16
            Case Else : Return a
        End Select
    End Function

    ''' <summary>specular 镜面：翻转 EY 分量（前后侧面自由滑移）。</summary>
    Private Shared Function SpecularY(a As Integer) As Integer
        Select Case a
            Case 3 : Return 4
            Case 4 : Return 3
            Case 7 : Return 9
            Case 8 : Return 10
            Case 9 : Return 7
            Case 10 : Return 8
            Case 15 : Return 18
            Case 16 : Return 17
            Case 17 : Return 16
            Case 18 : Return 15
            Case Else : Return a
        End Select
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

    ''' <summary>上传宿主分布函数到设备（GPU 路径）。</summary>
    Public Sub UploadF(f As Single())
        If f.Length <> LatticeD3Q19.Q * N Then
            Throw New ArgumentException($"f 长度必须为 {LatticeD3Q19.Q * N}")
        End If

        Call Array.Copy(f, _f, f.Length)

        If _gpuState IsNot Nothing Then
            Call _gpu.UploadF(_gpuState, _f)
            _momentsDirty = True
        End If
    End Sub

    ''' <summary>获取单元边界类型。</summary>
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
            Dim s As Double = System.Math.Sqrt(vx * vx + vy * vy + vz * vz)

            If s > maxS Then
                maxS = s
            End If
        Next

        Return maxS
    End Function

    ''' <summary>全场平均速度大小（仅流体格点）。</summary>
    Public Function AverageSpeed() As Double
        Call EnsureMoments()

        Dim sum As Double = 0.0R
        Dim count As Integer = 0

        For idx As Integer = 0 To N - 1
            If Btype(idx) <> CudaLbmBackend.CellFluid Then
                Continue For
            End If

            Dim vx As Double = _ux(idx)
            Dim vy As Double = _uy(idx)
            Dim vz As Double = _uz(idx)

            sum += System.Math.Sqrt(vx * vx + vy * vy + vz * vz)
            count += 1
        Next

        Return If(count > 0, sum / count, 0.0R)
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

#End Region

#Region "IDisposable"

    Public Sub Dispose() Implements IDisposable.Dispose
        If _gpuState IsNot Nothing Then
            Call _gpuState.Dispose()
        End If
    End Sub

#End Region

End Class
