Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   PopulationBalance.vb（FvmSolver Part 3/3）
'
'   ★ 核心模块③【欧拉两相流 §3.3：群体平衡模型 PBM】—— FVM.md §三 3.3
'
'   离散分组法（MUSIG，工业主流）：
'       把气泡体积空间离散成 NB 个几何 bin（体素直径 0.4→10 mm），
'       每个 bin 解一个 αᵢ 输运方程（TransportGasFractionMulti 改造版），
'       破碎/聚并通过 bin 间转移矩阵表达：
'
'   数密度 n(v) 与分组体积分数 αᵢ 的换算：nᵢ = αᵢ / v̄ᵢ （v̄ᵢ = πdᵢ³/6）
'
'   ── 破碎核 Luo-Svendsen 1996（§3.3 指定）──────────────────────────────────
'     湍流涡碰撞能量 vs 表面张力之比驱动；二元破碎，子气泡体积比 λ∈(λmin,1/2)
'       b(d) = ∫ λmin→1/2  b(d,λ) dλ
'       b(d,λ) = 0.923·(ε/d²)^(1/3) · ∫ξmin→1  (1+ξ)²/ξ^(11/3) · exp(−12c_f/β·σ/(ρc·ε^(2/3)·d^(7/3)·ξ^(2/3)) dξ
'     实现按原文取一级近似：碎裂概率 P_b = exp(−χc)，χ = 12σ/(ρc·ε^{2/3}·d^{5/3})
'     （Weber 数倒数），对尺寸比 ξ 积分闭合成 gamma 函数族 → 数值表
'
'   ── 聚并核 Prince-Blanch 1990（§3.3 指定 Luo/Prince-Blanch）──────────────
'       聚并率 = 碰撞频率 × 聚并概率
'       碰撞频率（湍流驱动）：θ(dᵢ,dⱼ) = π/4·(dᵢ+dⱼ)²·ε^(1/3)·(dᵢ^(2/3)+dⱼ^(2/3))
'       聚并概率：P_c = exp(−tᵢⱼ/t_c），近似 exp(−c·(ε·d)^(1/2)·We^(1/2))，
'       实现取工程常用简化 P_c = exp(−K_c·μ_c^0.5·ε^(1/3)·d^(5/6)/σ^0.5)
'
'   ── GPU 路线 ──────────────────────────────────────────────────────────────
'     转移矩阵的构造（本文件 BuildBreakageMatrix/BuildCoalescenceMatrix）
'     依赖全局标量 ε̄（每步一次，CPU 标量运算即可）；
'     体素级 αᵢ 更新是「矩阵×向量」——diag(B)·αᵢ 形式全部经 tfMath，
'     computeKernel 切 CUDA 后整体迁移。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports tfMath = Microsoft.VisualBasic.MachineLearning.TensorFlow.Math

Partial Public Class FvmSolver

    ' ---------------- PBM 分组几何（构造时初始化） ----------------

    ''' <summary>PBM 组数（0.4–10mm 几何 12 组）。</summary>
    Public ReadOnly Property PbmBins As Integer = 12

    ' 后端字段：上界不写死，统一由 InitPbm() 中的 ReDim 分配
    ' （PbmBins 是实例属性而非 Const，作字段数组上界既不可靠也无必要）
    Private _pbmD() As Double
    Private _pbmV() As Double
    Private _alphaBins() As Tensor
    Private _d32 As Tensor
    Private _n0 As Tensor

    ''' <summary>各组代表性直径 dᵢ（m），几何递增。</summary>
    Public ReadOnly Property PbmDiameters As Double()
        Get
            Return _pbmD
        End Get
    End Property

    ''' <summary>各组代表性体积 v̄ᵢ = πdᵢ³/6（m³）。</summary>
    Public ReadOnly Property PbmVolumes As Double()
        Get
            Return _pbmV
        End Get
    End Property

    ''' <summary>各组体积分数场 αᵢ（Tensor × NB，挂 ExtraScalars("pbm_i")）。</summary>
    Public ReadOnly Property AlphaBins As Tensor()
        Get
            Return _alphaBins
        End Get
    End Property

    ''' <summary>当前 Sauter 平均直径 d32 场（m）。</summary>
    Public ReadOnly Property D32 As Tensor
        Get
            Return _d32
        End Get
    End Property

    ''' <summary>气泡数密度 n₀ 场（1/m³）——可视化/诊断。</summary>
    Public ReadOnly Property BubbleCount As Tensor
        Get
            Return _n0
        End Get
    End Property

    ''' <summary>PBM 主开关（False 时退回常数 d_b 老路径）。</summary>
    Public Property UsePBM As Boolean = True

    ''' <summary>
    ''' 破碎核 χ 判据缩放（默认 50）：一级近似 Luo-Svendsen 的 χ=12σ/(ρε^{2/3}d^{5/3})
    ''' 在发酵罐 ε≈0.1-1 W/kg 量级下过严（2mm 泡 χ≈50 → 永不破碎），文献
    ''' Coulaloglou-Tavlarides 型经验核的有效判据常数 ≈0.1-1（即 12/50）。
    ''' 标定目标：d32 ≈ 2-4mm（通气 Rushton 罐实测区间）。
    ''' </summary>
    Public Property PbmBreakageCal As Double = 10.0R

    ' 破碎转移矩阵（NB×NB，全场同矩阵——由全局 ε̄ 驱动，每步重建）
    Private _brkRate As Double()        ' 各组总破碎死亡率 b(dᵢ) (1/s)
    Private _brkChild As Double(,)       ' 子气泡分配：母组 i 破碎产物进入 j 的体积分数
    ' 聚并
    Private _coalRate As Double()       ' 各组总聚并死亡率 (1/s)
    Private _coalChild As Double(,)     ' 聚并产物进入 j 的体积分数

    Private ReadOnly _pbmEpsRef As Double = 1.0R     ' ε 基准（构造后由流场更新）

    ''' <summary>PBM 几何初始化（构造函数尾部调用）。</summary>
    Private Sub InitPbm()
        ReDim _pbmD(PbmBins - 1)
        ReDim _pbmV(PbmBins - 1)
        ReDim _alphaBins(PbmBins - 1)
        ReDim _brkRate(PbmBins - 1)
        ReDim _brkChild(PbmBins - 1, PbmBins - 1)
        ReDim _coalRate(PbmBins - 1)
        ReDim _coalChild(PbmBins - 1, PbmBins - 1)

        ' 几何分布：0.4 mm → 10 mm，公比 q = (10/0.4)^(1/11)
        Dim dMin = 0.0004R, dMax = 0.01R
        Dim q = (dMax / dMin) ^ (1.0R / (PbmBins - 1))
        For i = 0 To PbmBins - 1
            _pbmD(i) = dMin * q ^ i
            _pbmV(i) = System.Math.PI / 6.0R * _pbmD(i) ^ 3
            _alphaBins(i) = New Tensor(_nx, _ny, _nz)
            Tank.Field.ExtraScalars($"pbm_{i}") = _alphaBins(i)
        Next

        _d32 = New Tensor(_nx, _ny, _nz)
        _n0 = New Tensor(_nx, _ny, _nz)
        Tank.Field.ExtraScalars("d32") = _d32
        Tank.Field.ExtraScalars("n0") = _n0

        ' 初始：全部气量放入第 2 小组（≈0.7mm——分布环喷孔典型初生尺寸）
        Dim seedBin = 2
        For idx = 0 To AlphaBins(seedBin).Length - 1
            _alphaBins(seedBin).Data(idx) = Alpha.Data(idx)
        Next
    End Sub

    ' ================================================================ Luo-Svendsen 破碎核

    ''' <summary>
    ''' Luo-Svendsen 破碎率 b(dᵢ) 与子气泡分配矩阵（全局 ε̄ 驱动，标量级计算）。
    ''' 一级近似：b(d) = C_b·(ε^(1/3)/d^(2/3))·exp(−χc)，
    '''   χ = 12σ/(ρc·ε^(2/3)·d^(5/3))（Weber 数倒数 × 数值常数 12/c_f~1.7）
    ''' 子气泡分配：λ∈(λmin, 1/2) 均匀体积分配到最近的 bin 对。
    ''' </summary>
    Private Sub BuildBreakageMatrix(epsRef As Double)
        Dim sigma = 0.072R          ' 水-空气表面张力 N/m
        Dim rhoC = Rho
        Dim e = System.Math.Max(epsRef, 1e-6R)   ' W/kg 下限防奇异

        For i = 0 To PbmBins - 1
            Dim d = PbmDiameters(i)
            Dim eThird = e ^ (1.0R / 3.0R)
            Dim chi = 12.0R * sigma / (rhoC * e ^ (2.0R / 3.0R) * d ^ (5.0R / 3.0R)) / PbmBreakageCal
            ' 概率 P_b = exp(−χ)（c_f 能量判据已并入常数 12）
            Dim pBreak = System.Math.Exp(-chi)
            Dim bRate = 0.923R * eThird / d ^ (2.0R / 3.0R) * pBreak
            _brkRate(i) = bRate

            ' 子气泡分配：母体积 v̄ᵢ → 两个 λv̄ᵢ 与 (1−λ)v̄ᵢ，λ 均匀采样 S 个
            If bRate <= 0.0R OrElse i = 0 Then
                For j = 0 To PbmBins - 1 : _brkChild(i, j) = 0.0R : Next
                _brkChild(i, i) = 1.0R
                Continue For
            End If

            Dim vMother = PbmVolumes(i)
            Dim S = 24                      ' λ 采样数
            For j = 0 To PbmBins - 1 : _brkChild(i, j) = 0.0R : Next
            For sIdx = 1 To S
                Dim lam = 0.05R + (0.45R - 0.05R) * sIdx / S   ' λ ∈ (0.05, 0.45)
                ' 子气泡 1：λv → 最近 bin
                Dim j1 = NearestBin(vMother * lam)
                ' 子气泡 2：(1−λ)v → 最近 bin
                Dim j2 = NearestBin(vMother * (1.0R - lam))
                _brkChild(i, j1) += lam / S
                _brkChild(i, j2) += (1.0R - lam) / S
            Next
            ' 体积守恒校验在单元测试做（Σj child·v̄j ≈ v̄i）
        Next
    End Sub

    ' ================================================================ Prince-Blanch 聚并核

    ''' <summary>
    ''' Prince-Blanch 聚并：碰撞频率 θ = π/4·(dᵢ+dⱼ)²·ε^(1/3)·(dᵢ^(2/3)+dⱼ^(2/3))，
    ''' 概率 P_c = exp(−K_c·μ^0.5·ε^(1/3)·d^(5/6)/σ^0.5)。
    ''' 生成物 v̄ᵢ+v̄ⱼ → 最近 bin（超界归最大组）。
    ''' </summary>
    Private Sub BuildCoalescenceMatrix(epsRef As Double, alphaTotal As Double)
        Dim sigma = 0.072R
        Dim muC = Rho * Nu
        Dim e = System.Math.Max(epsRef, 1e-6R)
        Dim eThird = e ^ (1.0R / 3.0R)
        Dim Kc = 1.0R                     ' 工程常数（Prince-Blanch 量级）

        For i = 0 To PbmBins - 1
            Dim deathRate = 0.0R
            For j = 0 To PbmBins - 1
                For k2 = 0 To PbmBins - 1
                    _coalChild(i, k2) = 0.0R
                Next
                Dim di = PbmDiameters(i), dj = PbmDiameters(j)
                ' 碰撞频率（j 组数密度权重——全场用 ᾱj/ v̄j 近似）
                Dim nj = alphaTotal / System.Math.Max(PbmVolumes(j), 1e-15R) / 1.0R
                Dim theta = System.Math.PI / 4.0R * (di + dj) ^ 2 * eThird *
                            (di ^ (2.0R / 3.0R) + dj ^ (2.0R / 3.0R))
                Dim dEq = (di * di * dj) ^ (1.0R / 3.0R)   ' 体积等效
                Dim pCoal = System.Math.Exp(-Kc * muC ^ 0.5R * eThird * dEq ^ (5.0R / 6.0R) / sigma ^ 0.5R)
                ' 二元聚并速率（每对只计一次：j>i 时才累加，对角 j=i 减半）
                Dim pairW = If(j > i, 1.0R, If(j = i, 0.5R, 0.0R))
                If pairW <= 0.0R Then Continue For
                Dim pairRate = pairW * theta * pCoal * nj
                deathRate += pairRate

                ' 生成物：i 的死亡体积中落到 kBin 的份额（末尾按 deathRate 归一化）
                Dim vSum = PbmVolumes(i) + PbmVolumes(j)
                Dim kBin = NearestBin(vSum)
                If kBin < i Then kBin = System.Math.Min(PbmBins - 1, i + 1)  ' 聚并只增大
                _coalChild(i, kBin) += pairRate
            Next
            _coalRate(i) = deathRate
            ' 份额归一化：_coalChild 行和 = 1（死亡体积的完整再分配）
            If deathRate > 1e-30R Then
                For k2 = 0 To PbmBins - 1
                    _coalChild(i, k2) /= deathRate
                Next
            End If
        Next
    End Sub

    ''' <summary>体积 → 最近 bin 下标（几何格点的对数最近）。</summary>
    Private Function NearestBin(v As Double) As Integer
        If v <= PbmVolumes(0) Then Return 0
        If v >= PbmVolumes(PbmBins - 1) Then Return PbmBins - 1
        For i = 0 To PbmBins - 2
            If v >= PbmVolumes(i) AndAlso v < PbmVolumes(i + 1) Then
                ' 对数中点判定
                Dim lm = (PbmVolumes(i) * PbmVolumes(i + 1)) ^ 0.5R
                Return If(v < lm, i, i + 1)
            End If
        Next
        Return PbmBins - 1
    End Function

    ' ================================================================ 多组 αᵢ 输运 + 转移

    ''' <summary>
    ''' 模块③ §3.3 主入口：多尺寸组 αᵢ 输运 + 破碎/聚并源项 + d32 重构。
    ''' 每步：①全局 ε̄/ᾱ → 重建转移矩阵（标量）→ ②各组迎风输运（张量）
    '''       → ③组间转移（diag 矩阵×αᵢ，张量）→ ④d32 = Σαᵢ / Σ(αᵢ/dᵢ)。
    ''' </summary>
    Private Sub TransportGasFractionPbm()
        ' ---- ① 全局基准 ε：取 max(场均值, P/ρV) ----
        ' 粗网格瞬态下 ε 场尚未发展（初始化只点亮桨带），而破碎核需要的是
        ' 桨区主导的湍流环境；用搅拌功率直接给出体积平均耗散率 P/(ρV)，
        ' 它随流场自洽演化（ImpellerPower ← MRF 源项积分），不引入外部参数。
        Dim epsSum = CDbl(nominal(tfMath.reduce_sum(tfMath.multiply(_act, Eps))))
        Dim actCount = System.Math.Max(1.0R, CDbl(nominal(tfMath.reduce_sum(_act))))
        Dim liquidVol = actCount * _cellVolume
        Dim epsPow = System.Math.Max(ImpellerPower, 0.0R) / (Rho * System.Math.Max(liquidVol, 1e-9R))
        Dim epsRef = System.Math.Max(epsSum / actCount, epsPow)
        Dim alphaTot = CDbl(nominal(tfMath.reduce_sum(tfMath.multiply(_act, Alpha)))) / actCount

        If UsePBM Then
            BuildBreakageMatrix(epsRef)
            BuildCoalescenceMatrix(epsRef, alphaTot)
        End If

        ' ---- ② 各组迎风输运（与单组版同构，滑移速度按 d32 尺寸律） ----
        Dim ugW = tfMath.add(W, TensorGrid.Filled(_nx, _ny, _nz, SlipVelocity))

        Dim transported(PbmBins - 1) As Tensor
        For i = 0 To PbmBins - 1
            Dim fE = UpwindOutflux(AlphaBins(i), U, U)
            Dim fN = UpwindOutflux(AlphaBins(i), V, V)
            Dim fT = UpwindOutflux(AlphaBins(i), ugW, ugW)
            Dim divF = tfMath.add(tfMath.add(
                tfMath.subtract(fE, TensorGrid.ShiftX(fE, -1)),
                tfMath.subtract(fN, TensorGrid.ShiftY(fN, -1))),
                tfMath.subtract(fT, TensorGrid.ShiftZ(fT, -1)))
            transported(i) = tfMath.subtract(
                tfMath.multiply(AlphaBins(i), _act),
                tfMath.multiply_scalar(tfMath.multiply(_act, divF), Dt / _cellVolume))
        Next

        ' ---- 分布环充气源：全部进入 seed 组（≈0.7mm 初生泡） ----
        Dim spgCount = System.Math.Max(1, CInt(CDbl(nominal(tfMath.reduce_sum(_spgZone)))))
        Dim injectVol = GasFlowRate * Dt / spgCount
        Dim alphaJet = System.Math.Min(injectVol / _cellVolume, 0.3R)
        Dim seedBin = 2
        transported(seedBin) = tfMath.add(transported(seedBin),
            tfMath.multiply_scalar(tfMath.multiply(_spgZone, _act), alphaJet))

        ' ---- 顶部脱气（全组） ----
        Dim notTop = TensorGrid.InverseMask(_topZone)
        For i = 0 To PbmBins - 1
            Dim topRelax = tfMath.multiply(_topZone, tfMath.multiply_scalar(AlphaBins(i), 0.5R))
            transported(i) = tfMath.add(tfMath.multiply(transported(i), notTop), topRelax)
        Next

        ' ---- ③ 组间转移（破碎 + 聚并；diag 作用 + 邻组源，张量化） ----
        If UsePBM Then
            Dim newBins(PbmBins - 1) As Tensor
            For i = 0 To PbmBins - 1
                ' 死亡项：−(bᵢ+cᵢ)·dt·αᵢ
                Dim death = tfMath.multiply_scalar(AlphaBins(i), -(_brkRate(i) + _coalRate(i)) * Dt)
                Dim acc = tfMath.add(tfMath.multiply(transported(i), _act), death)
                newBins(i) = tfMath.multiply(acc, _act)
            Next
            ' 生成项（体积守恒：α 即气相体积分数，源组死亡体积按 child 份额
            ' 落入目标组，无需任何 v̄ 折算——破碎与聚并都只搬运体积）
            For src = 1 To PbmBins - 1
                For dst = 0 To PbmBins - 1
                    Dim wBrk = _brkChild(src, dst)
                    Dim wCoal = _coalChild(src, dst)
                    If src = dst OrElse (wBrk <= 0.0R AndAlso wCoal <= 0.0R) Then Continue For
                    Dim rate = (_brkRate(src) * wBrk + _coalRate(src) * wCoal) * Dt
                    Dim gain = tfMath.multiply_scalar(AlphaBins(src), rate)
                    newBins(dst) = tfMath.add(newBins(dst), tfMath.multiply(gain, _act))
                Next
            Next
            For i = 0 To PbmBins - 1
                Dim clipped = tfMath.clip_by_value(newBins(i), 0.0R, 0.35R)
                Array.Copy(clipped.Data, AlphaBins(i).Data, AlphaBins(i).Length)
            Next
        Else
            For i = 0 To PbmBins - 1
                Array.Copy(transported(i).Data, AlphaBins(i).Data, AlphaBins(i).Length)
            Next
        End If

        ' ---- ④ 重构总 α 与 d32 = Σαᵢ / Σ(αᵢ/dᵢ)，n₀ = Σ αᵢ/v̄ᵢ ----
        Dim aSum = tfMath.multiply(AlphaBins(0), _act)
        Dim aOverD = tfMath.multiply_scalar(AlphaBins(0), 1.0R / PbmDiameters(0))
        Dim nSum = tfMath.multiply_scalar(AlphaBins(0), 1.0R / PbmVolumes(0))
        For i = 1 To PbmBins - 1
            aSum = tfMath.add(aSum, tfMath.multiply(AlphaBins(i), _act))
            aOverD = tfMath.add(aOverD, tfMath.multiply_scalar(AlphaBins(i), 1.0R / PbmDiameters(i)))
            nSum = tfMath.add(nSum, tfMath.multiply_scalar(AlphaBins(i), 1.0R / PbmVolumes(i)))
        Next
        Array.Copy(aSum.Data, Alpha.Data, Alpha.Length)

        Dim d32New = tfMath.divide(aSum, tfMath.add(aOverD, TensorGrid.Filled(_nx, _ny, _nz, 1e-12R)))
        d32New = tfMath.multiply(tfMath.minimum(
            tfMath.maximum(d32New, TensorGrid.Filled(_nx, _ny, _nz, PbmDiameters(0))),
            TensorGrid.Filled(_nx, _ny, _nz, PbmDiameters(PbmBins - 1))), _act)
        Array.Copy(d32New.Data, D32.Data, D32.Length)
        Array.Copy(nSum.Data, BubbleCount.Data, BubbleCount.Length)
    End Sub

    ''' <summary>ε 加权平均 kLa 诊断（PBM 版由 TransportOxygen 使用 d32 场）。</summary>
    Public ReadOnly Property MeanD32 As Double
        Get
            Dim aSum = tfMath.reduce_sum(tfMath.multiply(_act, Alpha))
            Dim aOverD = tfMath.reduce_sum(tfMath.multiply(_act,
                tfMath.divide(Alpha, tfMath.add(D32, TensorGrid.Filled(_nx, _ny, _nz, 1e-12R)))))
            Dim denom = CDbl(nominal(aOverD))
            If denom <= 1e-15R Then Return BubbleDiameter
            Return CDbl(nominal(aSum)) / denom
        End Get
    End Property

End Class
