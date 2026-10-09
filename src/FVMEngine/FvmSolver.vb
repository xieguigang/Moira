Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   FvmSolver.vb（Part 1/2）
'
'   FVM 有限体积求解器主类 —— SIMPLE 压力-速度耦合。
'
'   ★★★ FVM.md 五个核心模块 → 代码位置总览 ★★★
'
'   ┌─ 模块① FVM 骨架 §一 ──────────────────────────────────────────────────┐
'   │  本文件        MomentumPredictor / SolveComponent / FaceFluxX/Y/Z     │
'   │                （半离散积分守恒：迎风对流 + 中心扩散 + 源项线性化）     │
'   │  PhysicsModels UpwindOutflux / Laplacian（通用离散算子）               │
'   │  TensorGrid    移位 / 梯度 / 散度（网格算子积木）                      │
'   ├─ 模块② 旋转处理（MRF）§二 ────────────────────────────────────────────┤
'   │  PhysicsModels ComputeImpellerSource（MRF 虚拟桨盘动量源）             │
'   ├─ 模块③ 欧拉两相流 §三（含 §3.4 氧传质）───────────────────────────────┤
'   │  PhysicsModels TransportGasFraction（α 输运 + Schiller-Naumann 滑移） │
'   │                TransportOxygen（DO 输运 + Higbie kLa 闭包）            │
'   ├─ 模块④ 湍流 k-ε §四 ──────────────────────────────────────────────────┤
'   │  PhysicsModels UpdateTurbulentViscosity / TransportTurbulence         │
'   └─ 模块⑤ 压力-速度耦合 SIMPLE §五 ──────────────────────────────────────┘
'      本文件        PressureCorrection（Rhie-Chow + p' 泊松 Jacobi + 校正）
'                    Advance（§5.1 分离式求解的单步内迭代流程）
'
'   GPU 路线：本类所有算术均经 tfMath（Tensor.computeKernel）；
'   TensorGrid 的移位是纯数据搬运。切换 computeKernel 即整体迁移。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports tfMath = Microsoft.VisualBasic.MachineLearning.TensorFlow.Math

''' <summary>
''' 发酵罐 FVM 求解器：持有全部场变量（Tensor），按伪瞬态 SIMPLE 步进。
''' </summary>
Public Partial Class FvmSolver

#Region "网格与几何（预计算 Tensor）"

    Public ReadOnly Property Tank As FermentationTank
    Private ReadOnly _nx, _ny, _nz As Integer
    Private ReadOnly _dx As Double
    Private ReadOnly _cellVolume As Double

    ''' <summary>活动流体体素（0/1）。</summary>
    Private ReadOnly _act As Tensor
    ''' <summary>桨叶带（0/1）。</summary>
    Private ReadOnly _impZone As Tensor
    ''' <summary>分布环（0/1）。</summary>
    Private ReadOnly _spgZone As Tensor
    ''' <summary>液面顶层（0/1，出流/脱气层）。</summary>
    Private ReadOnly _topZone As Tensor
    ''' <summary>cos φ = (x-cx)/r。</summary>
    Private ReadOnly _tx As Tensor
    ''' <summary>sin φ = (y-cy)/r。</summary>
    Private ReadOnly _ty As Tensor
    ''' <summary>体素到轴距离（物理单位 m）。</summary>
    Private ReadOnly _rPhys As Tensor

#End Region

#Region "场变量（全部 Tensor）"

    ''' <summary>x 速度 m/s。</summary>
    Public ReadOnly Property U As Tensor
    ''' <summary>y 速度 m/s。</summary>
    Public ReadOnly Property V As Tensor
    ''' <summary>z 速度 m/s。</summary>
    Public ReadOnly Property W As Tensor
    ''' <summary>压力（表压，Pa）。</summary>
    Public ReadOnly Property P As Tensor
    ''' <summary>气相体积分数 α_g。</summary>
    Public ReadOnly Property Alpha As Tensor
    ''' <summary>湍动能 k (m²/s²)。</summary>
    Public ReadOnly Property K As Tensor
    ''' <summary>湍流耗散率 ε (m²/s³)。</summary>
    Public ReadOnly Property Eps As Tensor
    ''' <summary>溶解氧浓度 C (mg/L)。</summary>
    Public ReadOnly Property O2 As Tensor
    ''' <summary>湍流运动粘度 ν_t (m²/s)。</summary>
    Public ReadOnly Property Nut As Tensor
    ''' <summary>kLa 传质系数 (1/s)。</summary>
    Public ReadOnly Property KLa As Tensor

#End Region

#Region "参数"

    ''' <summary>液相密度 kg/m³。</summary>
    Public Property Rho As Double = 1000.0R
    ''' <summary>液相运动粘度 m²/s。</summary>
    Public Property Nu As Double = 0.000001R
    ''' <summary>时间步长 s（伪瞬态）。</summary>
    Public Property Dt As Double = 0.003R
    ''' <summary>桨角速度 rad/s。</summary>
    Public Property Omega As Double
    ''' <summary>桨叶梢速度 m/s。</summary>
    Public ReadOnly Property TipSpeed As Double
    ''' <summary>桨盘动量交换时间常数（标定：0.06s 时 Np≈Rushton 实验区间）。</summary>
    Public Property ImpellerTau As Double = 0.06R
    ''' <summary>表观充气速率（分布环体积流量 m³/s）。</summary>
    Public Property GasFlowRate As Double = 0.0004R
    ''' <summary>气泡直径 m。</summary>
    Public Property BubbleDiameter As Double = 0.003R
    ''' <summary>气泡终端滑移速度 m/s。</summary>
    Public Property SlipVelocity As Double = 0.22R
    ''' <summary>O2 饱和浓度 mg/L。</summary>
    Public Property DOSturation As Double = 7.8R
    ''' <summary>体积耗氧速率 mg/(L·s)。</summary>
    Public Property OUR As Double = 0.05R
    ''' <summary>压力修正欠松弛。</summary>
    Public Property PressureUrf As Double = 0.7R
    ''' <summary>p' Jacobi 内迭代数。</summary>
    Public Property PressureSweeps As Integer = 40

    ' k-ε 常数
    Private Const Cmu As Double = 0.09
    Private Const Ce1 As Double = 1.44
    Private Const Ce2 As Double = 1.92
    Private Const Sigmak As Double = 1.0
    Private Const Sigmae As Double = 1.3

#End Region

#Region "诊断"

    ''' <summary>当前步号。</summary>
    Public ReadOnly Property StepIndex As Integer
        Get
            Return _step
        End Get
    End Property
    Private _step As Integer

    ''' <summary>当前模拟时间 s。</summary>
    Public ReadOnly Property Time As Double
        Get
            Return _time
        End Get
    End Property
    Private _time As Double

    ''' <summary>最近一步连续性残差（相对值）。</summary>
    Public ReadOnly Property MassResidual As Double
        Get
            Return _massRes
        End Get
    End Property
    Private _massRes As Double

    ''' <summary>桨输入功率 W（最近一步）。</summary>
    Public Property ImpellerPower As Double
    ''' <summary>功率准数 Np = P/(ρN³D⁵)。</summary>
    Public ReadOnly Property Np As Double
        Get
            Dim N = Omega / (2.0R * System.Math.PI)
            Dim denom = Rho * N * N * N * Tank.ImpellerDiameter ^ 5
            If denom <= 0.0R Then Return 0.0R
            Return ImpellerPower / denom
        End Get
    End Property

    ''' <summary>平均气含率（液体区）。</summary>
    Public ReadOnly Property GasHoldup As Double
        Get
            Dim s = tfMath.reduce_sum(tfMath.multiply(Alpha, _act))
            Dim nAct = tfMath.reduce_sum(_act)
            If CDbl(nominal(nAct)) <= 0.0R Then Return 0.0R
            Return CDbl(nominal(s)) / CDbl(nominal(nAct))
        End Get
    End Property

    ''' <summary>平均 kLa (1/s)。</summary>
    Public ReadOnly Property MeanKLa As Double
        Get
            Dim s = tfMath.reduce_sum(tfMath.multiply(KLa, _act))
            Dim nAct = tfMath.reduce_sum(_act)
            If CDbl(nominal(nAct)) <= 0.0R Then Return 0.0R
            Return CDbl(nominal(s)) / CDbl(nominal(nAct))
        End Get
    End Property

    Private Shared Function nominal(t As Tensor) As Double
        If t.Length = 1 Then Return t.Data(0)
        Return t.Data(0)
    End Function

#End Region

    ''' <summary>
    ''' 从发酵罐几何构建求解器（场变量挂到 tank.Field 上供快照）。
    ''' </summary>
    Public Sub New(tank As FermentationTank)
        Me.Tank = tank
        _nx = tank.Nx
        _ny = tank.Ny
        _nz = tank.Nz
        _dx = tank.Dx
        _cellVolume = _dx * _dx * _dx
        Omega = tank.Stirrer.AngularVelocity
        TipSpeed = Omega * (tank.ImpellerDiameter / 2.0R)

        _act = TensorGrid.MaskTensor(tank.VoxelShape.Shape, _nx, _ny, _nz)
        _impZone = TensorGrid.MaskTensor(tank.VoxelShape.ImpellerZone, _nx, _ny, _nz)
        _spgZone = TensorGrid.MaskTensor(tank.VoxelShape.SpargerZone, _nx, _ny, _nz)
        _topZone = TopZoneMask()

        ' 极坐标方向张量
        Dim txd(_act.Length - 1) As Double
        Dim tyd(_act.Length - 1) As Double
        Dim rd(_act.Length - 1) As Double
        For i = 0 To _nx - 1
            For j = 0 To _ny - 1
                Dim x = i - tank.CenterX
                Dim y = j - tank.CenterY
                Dim r = System.Math.Sqrt(x * x + y * y)
                Dim invR = If(r > 0.01R, 1.0R / r, 0.0R)
                For kk = 0 To _nz - 1
                    Dim idx = i * (_ny * _nz) + j * _nz + kk
                    txd(idx) = x * invR
                    tyd(idx) = y * invR
                    rd(idx) = r * _dx
                Next
            Next
        Next
        _tx = TensorGrid.Wrap(txd, _nx, _ny, _nz)
        _ty = TensorGrid.Wrap(tyd, _nx, _ny, _nz)
        _rPhys = TensorGrid.Wrap(rd, _nx, _ny, _nz)

        ' ---- 场变量（挂 FluidField，五标准场 + 扩展场） ----
        U = tank.Field.U
        V = tank.Field.V
        W = tank.Field.W
        P = tank.Field.Pressure
        Alpha = New Tensor(_nx, _ny, _nz)
        K = New Tensor(_nx, _ny, _nz)
        Eps = New Tensor(_nx, _ny, _nz)
        O2 = New Tensor(_nx, _ny, _nz)
        Nut = New Tensor(_nx, _ny, _nz)
        KLa = New Tensor(_nx, _ny, _nz)
        tank.Field.ExtraScalars("alpha_g") = Alpha
        tank.Field.ExtraScalars("k") = K
        tank.Field.ExtraScalars("epsilon") = Eps
        tank.Field.ExtraScalars("DO") = O2
        tank.Field.ExtraScalars("nut") = Nut
        tank.Field.ExtraScalars("kLa") = KLa

        ' 湍流初始化：桨区高湍流，别处小值
        ' ε₀ = Cμ^0.75·k^1.5/L（L = 0.05T 混合长度）——保证 νt ≈ Cμ^0.25·√k·L 合理
        Dim k0 = 0.02R * TipSpeed * TipSpeed
        Dim eps0 = Cmu ^ 0.75R * k0 ^ 1.5R / (0.05R * tank.TankDiameter)
        For idx = 0 To K.Length - 1
            K.Data(idx) = k0 * _impZone.Data(idx) + 1e-6R
            Eps.Data(idx) = eps0 * _impZone.Data(idx) + 1e-10R
            Nut.Data(idx) = Cmu * K.Data(idx) ^ 2 / Eps.Data(idx)
        Next
        ' ---- 旋流初始化（加速起转）：罐内液体 55% 刚体涡 uθ = 0.55·min(Ωr, 1.05·tip) ----
        ' uθ 方向 = (−ty, tx)；桨带内给满速、远场按 (1−r/R)·z 衰减，避免突然启动
        Dim uThetaInit = tfMath.multiply_scalar(
            tfMath.minimum(
                tfMath.multiply_scalar(_rPhys, Omega),
                TensorGrid.Filled(_nx, _ny, _nz, 1.05R * TipSpeed)), 0.55R)
        Array.Copy(tfMath.multiply(tfMath.multiply_scalar(uThetaInit, -1.0R), _ty).Data,
                   U.Data, U.Length)
        Array.Copy(tfMath.multiply(uThetaInit, _tx).Data, V.Data, V.Length)

        ' 自适应时间步：CFL ~ 0.25
        Dt = 0.25R * _dx / System.Math.Max(TipSpeed, 0.5R)

        ' ★ 模块③ §3.3 PBM 初始化（12 组离散分组 + Luo-Svendsen/Prince-Blanch 核）
        InitPbm()
    End Sub

    ''' <summary>液面顶层掩膜（k = LiquidTop）。</summary>
    Private Function TopZoneMask() As Tensor
        Dim t As New Tensor(_nx, _ny, _nz)
        For i = 0 To _nx - 1
            For j = 0 To _ny - 1
                Dim idx = i * (_ny * _nz) + j * _nz + Tank.LiquidTop
                t.Data(idx) = 1.0R
            Next
        Next
        Return t
    End Function

    ' ================================================================ 主步进

    ''' <summary>推进一个伪瞬态 SIMPLE 步。</summary>
    Public Property DebugMode As Boolean = False

    Private Sub Dbg(tag As String)
        If Not DebugMode Then Return
        Dim mx = 0.0R
        For Each x In U.Data
            If Double.IsFinite(x) AndAlso System.Math.Abs(x) > mx Then mx = System.Math.Abs(x)
        Next
        For Each x In V.Data
            If Double.IsFinite(x) AndAlso System.Math.Abs(x) > mx Then mx = System.Math.Abs(x)
        Next
        For Each x In W.Data
            If Double.IsFinite(x) AndAlso System.Math.Abs(x) > mx Then mx = System.Math.Abs(x)
        Next
        For Each x In P.Data
            If Double.IsFinite(x) AndAlso System.Math.Abs(x) > mx Then mx = System.Math.Abs(x)
        Next
        Console.WriteLine($"    dbg {tag}: max|UVWP|={mx:E3}")
    End Sub

    ' ★ 模块⑤ SIMPLE 分离式求解流程（FVM.md §5.1：单个时间步内的内迭代顺序）
    '   νt 更新 → 动量预估 u* → 压力修正 p' → α 输运 → k-ε → DO 传质 → 边界
    Public Sub Advance()
        Dbg("start")
        UpdateTurbulentViscosity() : Dbg("nut")
        MomentumPredictor() : Dbg("mom")
        PressureCorrection() : Dbg("pcorr")
        TransportGasFractionPbm() : Dbg("alpha")   ' §3.3 多组 PBM（UsePBM=False 退化单组）
        TransportTurbulence() : Dbg("turb")
        TransportOxygen() : Dbg("o2")
        ApplyBoundaryMasks() : Dbg("mask")
        _step += 1
        _time += Dt
    End Sub

    ''' <summary>把步进结果同步回 FluidField 的 Density（混合密度）。</summary>
    Public Sub SyncField()
        ' ρ_mix = α·ρ_g + (1-α)·ρ_l ≈ (1-α)·ρ_l
        Dim rhoG = 1.2R
        Dim rhoMix = tfMath.add(
            tfMath.multiply_scalar(Alpha, rhoG),
            tfMath.multiply_scalar(tfMath.add_scalar(tfMath.multiply_scalar(_act, -1.0R), 1.0R), Rho))
        Array.Copy(rhoMix.Data, Tank.Field.Density.Data, _act.Length)
    End Sub

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ①+⑤【FVM 骨架 / SIMPLE 第 1 步：动量预估】 —— FVM.md §一 1.1 守恒律离散 + §五 5.1
    ' ║ 半离散守恒：aP·u* = aP0·uⁿ + Σ_nb[max(∓F,0)+D]·u_nb + V·S
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' 动量预估（每方向一次 Jacobi 扫，伪瞬态主导对角）：
    ''' aP·u = aP0·u_old + Σ_nb [max(−F,0)+D]·u_nb + V·S
    ''' aP  = aP0 + Σ_nb [max(F,0)+D]（迎风对流 + 中心扩散，全部张量化）
    ''' </summary>
    Private Sub MomentumPredictor()
        ' 湍流有效粘度
        Dim nuEff = tfMath.add_scalar(Nut, Nu)

        ' ---- 面体积通量（当前速度，面插值）----
        Dim qe = FaceFluxX()   ' m³/s（East 面，属 P）
        Dim qn = FaceFluxY()
        Dim qt = FaceFluxZ()

        ' ---- 扩散面系数 D = ν_eff·A/d ----
        Dim dcoef = _dx * _dx / _dx       ' A/dx = dx
        Dim dE = tfMath.multiply_scalar(nuEff, dcoef * Rho)

        ' ---- MRF 桨盘源（力密度）----
        Dim sx, sy As Tensor
        ComputeImpellerSource(sx, sy)

        Dim aP0 = Rho * _cellVolume / Dt

        ' ---- 六面迎风系数（E/W/N/S/T/B 各用自己面的通量）----
        ' qe = P 的 E 面通量；West 邻居的 E 面 = P 的 W 面（ShiftX(qe,-1)）
        Dim qeW = TensorGrid.ShiftX(qe, -1)
        Dim qnS = TensorGrid.ShiftY(qn, -1)
        Dim qtB = TensorGrid.ShiftZ(qt, -1)
        Dim rhoT = TensorGrid.Filled(_nx, _ny, _nz, Rho)

        ' a_nb = D + ρ·max(∓F, 0)（流入侧携带邻位值）
        Dim anbE = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.minimum(qe, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), -Rho))
        Dim anbW = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.maximum(qeW, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), Rho))
        Dim anbN = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.minimum(qn, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), -Rho))
        Dim anbS = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.maximum(qnS, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), Rho))
        Dim anbT = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.minimum(qt, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), -Rho))
        Dim anbB = tfMath.add(dE, tfMath.multiply_scalar(
            tfMath.maximum(qtB, TensorGrid.Filled(_nx, _ny, _nz, 0.0R)), Rho))

        ' aP = aP0 + Σ六面 [流出通量 + D]（对角占优）
        Dim zero = TensorGrid.Filled(_nx, _ny, _nz, 0.0R)
        Dim sixD = tfMath.multiply_scalar(dE, 6.0R)
        ' 各面流出（E:+qe,W:−qeW,N:+qn,S:−qnS,T:+qt,B:−qtB 的正部）
        Dim outE = tfMath.maximum(qe, zero)
        Dim outW = tfMath.multiply_scalar(tfMath.minimum(qeW, zero), -1.0R)
        Dim outN = tfMath.maximum(qn, zero)
        Dim outS = tfMath.multiply_scalar(tfMath.minimum(qnS, zero), -1.0R)
        Dim outT = tfMath.maximum(qt, zero)
        Dim outB = tfMath.multiply_scalar(tfMath.minimum(qtB, zero), -1.0R)
        Dim convOut = tfMath.multiply(tfMath.add(tfMath.add(
            tfMath.add(outE, outW), tfMath.add(outN, outS)), tfMath.add(outT, outB)), rhoT)
        Dim aPdiag = tfMath.add_scalar(
            tfMath.multiply(tfMath.add(sixD, convOut), _act), aP0 + 1e-6R)

        ' ---- 三方向求解（Jacobi 1 扫）----
        SolveComponent(U, aPdiag, anbE, anbW, anbN, anbS, anbT, anbB, aP0,
                       tfMath.multiply(sx, TensorGrid.Filled(_nx, _ny, _nz, _cellVolume)))
        SolveComponent(V, aPdiag, anbE, anbW, anbN, anbS, anbT, anbB, aP0,
                       tfMath.multiply(sy, TensorGrid.Filled(_nx, _ny, _nz, _cellVolume)))
        SolveComponent(W, aPdiag, anbE, anbW, anbN, anbS, anbT, anbB, aP0, Nothing)
    End Sub

    ''' <summary>单个速度分量的 Jacobi 更新（in-place 写回 u.Data）。</summary>
    Private Sub SolveComponent(u As Tensor, aP As Tensor,
                               anbE As Tensor, anbW As Tensor, anbN As Tensor, anbS As Tensor,
                               anbT As Tensor, anbB As Tensor, aP0 As Double, sourceV As Tensor)
        ' H = Σ_nb a_nb·u_nb（六面各自系数）
        Dim hSum = tfMath.multiply(anbE, TensorGrid.East(u))
        hSum = tfMath.add(hSum, tfMath.multiply(anbW, TensorGrid.West(u)))
        hSum = tfMath.add(hSum, tfMath.multiply(anbN, TensorGrid.North(u)))
        hSum = tfMath.add(hSum, tfMath.multiply(anbS, TensorGrid.South(u)))
        hSum = tfMath.add(hSum, tfMath.multiply(anbT, TensorGrid.Top(u)))
        hSum = tfMath.add(hSum, tfMath.multiply(anbB, TensorGrid.Bottom(u)))

        ' RHS = aP0·u_old + H + S·V
        Dim rhs = tfMath.multiply_scalar(u, aP0)
        rhs = tfMath.add(rhs, hSum)
        If sourceV IsNot Nothing Then rhs = tfMath.add(rhs, sourceV)

        ' u = RHS / aP（仅在活动体素）
        Dim uNew = tfMath.multiply(_act, tfMath.divide(rhs, aP))
        ' 速度限幅（稳健）
        uNew = tfMath.clip_by_value(uNew, -4.0R * TipSpeed, 4.0R * TipSpeed)
        Array.Copy(uNew.Data, u.Data, u.Length)
    End Sub

    ' ★ 模块① 面通量三关键量之一：F_f = ρ·u_f·A（FVM.md §1.2 引擎核心数据结构）
    ''' <summary>East 面体积通量（面插值，m³/s）。</summary>
    Private Function FaceFluxX() As Tensor
        Return tfMath.multiply_scalar(tfMath.add(U, TensorGrid.East(U)), 0.5R * _dx * _dx)
    End Function

    Private Function FaceFluxY() As Tensor
        Return tfMath.multiply_scalar(tfMath.add(V, TensorGrid.North(V)), 0.5R * _dx * _dx)
    End Function

    Private Function FaceFluxZ() As Tensor
        Return tfMath.multiply_scalar(tfMath.add(W, TensorGrid.Top(W)), 0.5R * _dx * _dx)
    End Function

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ⑤【压力-速度耦合 SIMPLE】 —— FVM.md §五 5.1 分离式求解 + 5.3 时间推进
    ' ║ Rhie-Chow 面通量（防棋盘压）→ p' 泊松（逐面系数 Jacobi）→ u -= d·∇p'
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' SIMPLE：Rhie-Chow 面通量 → 连续性残差 → p' 泊松（Jacobi）→ 速度/压力校正。
    ''' </summary>
    Private Sub PressureCorrection()
        ' ---- aP（动量对角，近似重算：只用稳定部分 ρV/dt + 6D + Σ|F|）----
        Dim nuEff = tfMath.add_scalar(Nut, Nu)
        Dim dE = tfMath.multiply_scalar(nuEff, _dx * Rho)
        Dim qe = FaceFluxX(), qn = FaceFluxY(), qt = FaceFluxZ()
        Dim aP0 = Rho * _cellVolume / Dt
        Dim absQ = tfMath.add(tfMath.add(tfMath.abs(qe), tfMath.abs(qn)), tfMath.abs(qt))
        Dim aPdiag = tfMath.add_scalar(
            tfMath.multiply(tfMath.add(tfMath.multiply_scalar(dE, 6.0R), absQ), _act), aP0 + 1e-6R)
        ' d_P = V/a_P（SIMPLE 速度校正系数，m³·s/kg）
        Dim invAP = tfMath.multiply_scalar(tfMath.multiply(_act, tfMath.reciprocal(aPdiag)), _cellVolume)

        ' ---- Rhie-Chow 面通量：F = 0.5(uP+uE)A − d̄_f·(pE−pP)·A/δ ----
        Dim dFace = tfMath.multiply_scalar(tfMath.add(invAP, TensorGrid.East(invAP)), 0.5R)
        Dim fE = tfMath.subtract(
            tfMath.multiply_scalar(tfMath.add(U, TensorGrid.East(U)), 0.5R * _dx * _dx),
            tfMath.multiply(dFace, tfMath.multiply_scalar(tfMath.subtract(TensorGrid.East(P), P), _dx)))
        Dim dFaceN = tfMath.multiply_scalar(tfMath.add(invAP, TensorGrid.North(invAP)), 0.5R)
        Dim fN = tfMath.subtract(
            tfMath.multiply_scalar(tfMath.add(V, TensorGrid.North(V)), 0.5R * _dx * _dx),
            tfMath.multiply(dFaceN, tfMath.multiply_scalar(tfMath.subtract(TensorGrid.North(P), P), _dx)))
        Dim dFaceT = tfMath.multiply_scalar(tfMath.add(invAP, TensorGrid.Top(invAP)), 0.5R)
        Dim fT = tfMath.subtract(
            tfMath.multiply_scalar(tfMath.add(W, TensorGrid.Top(W)), 0.5R * _dx * _dx),
            tfMath.multiply(dFaceT, tfMath.multiply_scalar(tfMath.subtract(TensorGrid.Top(P), P), _dx)))

        ' ---- 连续性残差：b = −Σ_f F_f（净流出，m³/s）----
        Dim divF = tfMath.add(tfMath.add(
            tfMath.subtract(fE, TensorGrid.ShiftX(fE, -1)),
            tfMath.subtract(fN, TensorGrid.ShiftY(fN, -1))),
            tfMath.subtract(fT, TensorGrid.ShiftZ(fT, -1)))
        Dim bVec = tfMath.multiply(_act, tfMath.multiply_scalar(divF, -1.0R))
        ' 顶部 Dirichlet（p'=0 出流）：残差中去除顶层
        bVec = tfMath.multiply(bVec, TensorGrid.InverseMask(_topZone))

        ' ---- p' Jacobi：Σ_f a_f(p'_P − p'_nb) = b，a_f = d̄_f·A/δ ----
        ' 逐面系数（对角 = 六面系数之和，保证 Jacobi 严格对角占优收敛）
        Dim apfE = tfMath.multiply_scalar(dFace, _dx)
        Dim apfW = tfMath.multiply_scalar(tfMath.multiply_scalar(
            tfMath.add(invAP, TensorGrid.West(invAP)), 0.5R), _dx)
        Dim apfN = tfMath.multiply_scalar(dFaceN, _dx)
        Dim apfS = tfMath.multiply_scalar(tfMath.multiply_scalar(
            tfMath.add(invAP, TensorGrid.South(invAP)), 0.5R), _dx)
        Dim apfT = tfMath.multiply_scalar(dFaceT, _dx)
        Dim apfB = tfMath.multiply_scalar(tfMath.multiply_scalar(
            tfMath.add(invAP, TensorGrid.Bottom(invAP)), 0.5R), _dx)
        Dim pp As Tensor = TensorGrid.Filled(_nx, _ny, _nz, 0.0R)
        Dim aPp = tfMath.add_scalar(
            tfMath.multiply(tfMath.add(tfMath.add(
                tfMath.add(apfE, apfW), tfMath.add(apfN, apfS)), tfMath.add(apfT, apfB)), _act), 1e-6R)
        For sweep = 1 To PressureSweeps
            Dim nbSum = tfMath.add(tfMath.add(
                tfMath.add(tfMath.multiply(apfE, TensorGrid.East(pp)),
                           tfMath.multiply(apfW, TensorGrid.West(pp))),
                tfMath.add(tfMath.multiply(apfN, TensorGrid.North(pp)),
                           tfMath.multiply(apfS, TensorGrid.South(pp)))),
                tfMath.add(tfMath.multiply(apfT, TensorGrid.Top(pp)),
                           tfMath.multiply(apfB, TensorGrid.Bottom(pp))))
            pp = tfMath.divide(tfMath.add(nbSum, bVec), aPp)

        Next

        ' 残差诊断（相对平均进出通量）
        Dim resSum = tfMath.reduce_sum(tfMath.abs(tfMath.multiply(_act, divF)))
        Dim scaleSum = tfMath.reduce_sum(tfMath.abs(tfMath.multiply(_act,
            tfMath.add(tfMath.add(tfMath.abs(fE), tfMath.abs(fN)), tfMath.abs(fT)))))
        Dim sc = CDbl(nominal(scaleSum)) + 1e-12R
        _massRes = CDbl(nominal(resSum)) / sc

        ' ---- 速度校正：u -= d_P·∇p'（在预估速度上叠加，而非替换！）----
        Dim gpx = TensorGrid.Ddx(pp, _dx)
        Dim gpy = TensorGrid.Ddy(pp, _dx)
        Dim gpz = TensorGrid.Ddz(pp, _dx)
        Dim uNew = tfMath.clip_by_value(
            tfMath.multiply(_act, tfMath.subtract(U, tfMath.multiply(invAP, gpx))), -4.0R * TipSpeed, 4.0R * TipSpeed)
        Dim vNew = tfMath.clip_by_value(
            tfMath.multiply(_act, tfMath.subtract(V, tfMath.multiply(invAP, gpy))), -4.0R * TipSpeed, 4.0R * TipSpeed)
        Dim wNew = tfMath.clip_by_value(
            tfMath.multiply(_act, tfMath.subtract(W, tfMath.multiply(invAP, gpz))), -4.0R * TipSpeed, 4.0R * TipSpeed)
        Array.Copy(uNew.Data, U.Data, U.Length)
        Array.Copy(vNew.Data, V.Data, V.Length)
        Array.Copy(wNew.Data, W.Data, W.Length)

        ' ---- 压力校正（欠松弛 + 顶层 Dirichlet p=0）----
        Dim pNew = tfMath.multiply(tfMath.add(P, tfMath.multiply_scalar(pp, PressureUrf)), _act)
        Dim pTopZero = tfMath.multiply(pNew, TensorGrid.InverseMask(_topZone))
        Array.Copy(pTopZero.Data, P.Data, P.Length)
    End Sub

    ' ================================================================ 边界收敛

    ''' <summary>统一边界处理：固体区速度/标量置零。</summary>
    Private Sub ApplyBoundaryMasks()
        Array.Copy(tfMath.multiply(U, _act).Data, U.Data, U.Length)
        Array.Copy(tfMath.multiply(V, _act).Data, V.Data, V.Length)
        Array.Copy(tfMath.multiply(W, _act).Data, W.Data, W.Length)
        Array.Copy(tfMath.multiply(Alpha, _act).Data, Alpha.Data, Alpha.Length)
        Array.Copy(tfMath.multiply(K, _act).Data, K.Data, K.Length)
        Array.Copy(tfMath.multiply(Eps, _act).Data, Eps.Data, Eps.Length)
    End Sub

End Class
