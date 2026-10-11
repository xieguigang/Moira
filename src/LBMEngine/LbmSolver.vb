Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   LbmSolver.vb
'
'   3D 格子 Boltzmann（D3Q19 + BGK 碰撞）求解器 —— 顶盖驱动方腔基准。
'
'   ── 核心算法（全部张量化，走 tfMath/computeKernel 路径，可切 CUDA）──────
'   演化两步（每个时间步）：
'       ① 碰撞（局部非线性，BGK 单松弛）：
'            f_α^c = f_α − ω·(f_α − f_α^eq)，  ω = 1/τ
'            f_α^eq = w_α·[ρ + 3(e_α·u) − 4.5(e_α·u)² + 1.5|u|²]   （不可压形式）
'       ② 拉格朗日流 + 半程反弹（halfway bounce-back，二阶壁面精度）：
'            f_α(x, t+1) = solid(x−e_α) ?
'                f_opp(α)^c(x) + 6·w_α·ρ·(e_α·u_w)     [壁面反弹+运动壁修正]
'              : f_α^c(x−e_α)                            [TensorGrid 移位]
'            运动壁修正仅作用于顶盖（u_w = (U_wall, 0, 0)）
'
'   ── 物理量（格子单位 dx = dt = 1）─────────────────────────────────────────
'       ν = cs²·(τ − 1/2)，  Re = U_wall·H/ν，  p = cs²·(ρ − ρ₀)（表压）
'
'   ── 守恒性 ────────────────────────────────────────────────────────────────
'       BGK 精确保持质量/动量（碰撞局部和不变）；
'       半程反弹封闭域内质量精确守恒（xunit 覆盖）。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CFDEngine
Imports tfMath = Microsoft.VisualBasic.MachineLearning.TensorFlow.Math

''' <summary>
''' D3Q19-BGK 格子 Boltzmann 求解器：外层为固体壳的封闭腔
''' （默认顶盖驱动方腔），场变量全部为 Tensor（19 个分布函数 + 宏观量）。
''' </summary>
Public Class LbmSolver

#Region "网格 / 几何"

    ''' <summary>x 方向节点数（含固体壳）。</summary>
    Public ReadOnly Nx As Integer
    ''' <summary>y 方向节点数。</summary>
    Public ReadOnly Ny As Integer
    ''' <summary>z 方向节点数（z = Nz−1 为顶盖）。</summary>
    Public ReadOnly Nz As Integer

    ''' <summary>流体掩膜（0/1 Tensor，与固体互补）。</summary>
    Public ReadOnly Fluid As Tensor
    ''' <summary>固体掩膜（腔体外壳）。</summary>
    Public ReadOnly Solid As Tensor
    ''' <summary>运动壁（顶盖）固体掩膜。</summary>
    Public ReadOnly LidSolid As Tensor

    ''' <summary>流体几何（供 VTI 快照导出）。</summary>
    Public ReadOnly Property FluidShape As VoxelShape

    ''' <summary>流体节点数。</summary>
    Public ReadOnly FluidCount As Integer

#End Region

#Region "分布函数与宏观场（Tensor）"

    ''' <summary>分布函数 f_α（当前时刻，α = 0..18）。</summary>
    Public ReadOnly F(Q - 1) As Tensor

    ''' <summary>密度场 ρ（格子单位，参考 ρ₀ = 1）。</summary>
    Public ReadOnly Rho As Tensor
    ''' <summary>速度分量 u_x。</summary>
    Public ReadOnly Ux As Tensor
    ''' <summary>速度分量 u_y。</summary>
    Public ReadOnly Uy As Tensor
    ''' <summary>速度分量 u_z。</summary>
    Public ReadOnly Uz As Tensor

#End Region

#Region "物理参数（格子单位）"

    ''' <summary>顶盖驱动速度 U_wall（默认 0.05，低马赫）。</summary>
    Private _uWall As Double

    ''' <summary>顶盖驱动速度（格子单位；测试可注入）。</summary>
    Public Property UWall As Double
        Get
            Return _uWall
        End Get
        Set(value As Double)
            _uWall = value
        End Set
    End Property

    ''' <summary>松弛时间 τ（由 Re 反解）。</summary>
    Private _tau As Double

    ''' <summary>松弛时间 τ（测试可注入；ν = cs²(τ−½)）。</summary>
    Public Property Tau As Double
        Get
            Return _tau
        End Get
        Set(value As Double)
            _tau = value
        End Set
    End Property

    ''' <summary>松弛频率 ω = 1/τ。</summary>
    Public ReadOnly Property Omega As Double
        Get
            Return 1.0R / _tau
        End Get
    End Property

    ''' <summary>运动粘度 ν = cs²(τ−1/2)。</summary>
    Public ReadOnly Nu As Double

    ''' <summary>雷诺数 Re = U_wall·H/ν。</summary>
    Public ReadOnly Re As Double

    ''' <summary>有效腔体尺寸 H（半程壁之间，格子单位）。</summary>
    Public ReadOnly H As Double

    Private _step As Integer = 0
    Private _time As Double = 0.0R

    ''' <summary>当前时间步序号。</summary>
    Public ReadOnly Property StepCount As Integer
        Get
            Return _step
        End Get
    End Property

    ''' <summary>模拟时间（格子单位，dt = 1）。</summary>
    Public ReadOnly Property SimTime As Double
        Get
            Return _time
        End Get
    End Property

#End Region

    ' ---- 碰撞模型与 MRT 矩阵 ----
    Private _model As CollisionModel = CollisionModel.BGK
    Private ReadOnly _mrtM As Double(,)
    Private ReadOnly _mrtMinv As Double(,)

    ''' <summary>碰撞模型（BGK/TRT/MRT，可运行时切换；粘性标定不变）。</summary>
    Public Property Model As CollisionModel
        Get
            Return _model
        End Get
        Set(value As CollisionModel)
            _model = value
        End Set
    End Property

    ' ---- 内部：反弹掩膜（静态几何，构造时预计算，避免每步重复移位） ----
    ' _pull(a)：solid(x − e_α) 的 0/1 场 —— 标记该方向“拉取源为固体”
    Private ReadOnly _pullSolid(Q - 1) As Tensor
    Private ReadOnly _pullLid(Q - 1) As Tensor

    ' 缓冲：宏观速度的组合项（碰撞时复用）
    Private ReadOnly _ones As Tensor

    ''' <summary>
    ''' 构造顶盖驱动方腔求解器。
    ''' </summary>
    ''' <param name="nx">x 节点数（含壳，≥ 8）</param>
    ''' <param name="ny">y 节点数</param>
    ''' <param name="nz">z 节点数（顶盖 z = nz−1）</param>
    ''' <param name="re">雷诺数（顶盖驱动）</param>
    ''' <param name="uWall">顶盖速度（格子单位，建议 ≤ 0.1 保证低马赫）</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer,
                   re As Double, Optional uWall As Double = 0.05R,
                   Optional model As CollisionModel = CollisionModel.BGK)
        If nx < 8 OrElse ny < 8 OrElse nz < 8 Then
            Throw New ArgumentException("网格各方向至少 8 节点（壳 2 + 流体 6）")
        End If

        Me.Nx = nx : Me.Ny = ny : Me.Nz = nz
        Me._uWall = uWall
        Me.Re = re

        ' ---- 几何：外层固体壳，z = nz−1 平面为运动顶盖 ----
        Dim shape(nx * ny * nz - 1) As Boolean
        Dim solids(nx * ny * nz - 1) As Boolean
        Dim lid(nx * ny * nz - 1) As Boolean
        Dim plane = ny * nz, row = nz
        For i = 0 To nx - 1
            For j = 0 To ny - 1
                For k = 0 To nz - 1
                    Dim idx = i * plane + j * row + k
                    Dim isShell = (i = 0 OrElse i = nx - 1 OrElse
                                   j = 0 OrElse j = ny - 1 OrElse
                                   k = 0 OrElse k = nz - 1)
                    shape(idx) = Not isShell
                    solids(idx) = isShell
                    lid(idx) = isShell AndAlso k = nz - 1
                Next
            Next
        Next

        Fluid = TensorGrid.MaskTensor(shape, nx, ny, nz)
        Solid = TensorGrid.MaskTensor(solids, nx, ny, nz)
        LidSolid = TensorGrid.MaskTensor(lid, nx, ny, nz)
        FluidShape = New VoxelShape(nx, ny, nz, shape, solids, Nothing, Nothing)

        Dim n = 0
        For Each b In shape : If b Then n += 1
        Next
        FluidCount = n

        ' ---- 物理参数：H = 两半程壁间距 = Nz − 3 格子单位 ----
        H = nz - 3
        _model = model
        _mrtM = MrtBasisD3Q19.BuildMatrix()
        _mrtMinv = MrtBasisD3Q19.Invert(_mrtM)

        If uWall > 0.0R Then
            Nu = uWall * H / re
            _tau = Nu / Cs2 + 0.5R
            ' 稳定域下限按模型分档（均经腔体驱动实测）：
            '   BGK τ ≥ 0.51（τ=0.506-0.507 时驱动腔体实测仍发散，0.505 偏乐观）
            '   TRT/MRT（偶扇区 ω⁺ 幻影阻尼）可下探到 τ ≥ 0.5005
            Dim tauMin = If(model = CollisionModel.BGK, 0.51R, 0.5005R)
            If _tau < tauMin Then
                Throw New ArgumentException(
                    $"Re={re} 对该网格过高：τ={_tau:F4} < {tauMin:F4}（{model} 失稳）。" &
                    "请降低 Re 或 U_wall 或加密网格，或改用 TRT/MRT 模型。")
            End If
        Else
            ' uWall = 0（静止测试构造）：τ 无定义，取稳定默认值（可注入覆盖）
            Nu = 0.0R
            _tau = 1.0R
        End If

        ' ---- 分布函数与宏观场 ----
        For a = 0 To Q - 1
            F(a) = New Tensor(nx, ny, nz)
        Next
        Rho = New Tensor(nx, ny, nz)
        Ux = New Tensor(nx, ny, nz)
        Uy = New Tensor(nx, ny, nz)
        Uz = New Tensor(nx, ny, nz)
        _ones = TensorGrid.Filled(nx, ny, nz, 1.0R)

        ' ---- 预计算反弹掩膜：_pull(a)(x) = solid(x − e_α) ----
        ' ShiftX(t,off)(x) = t(x+off) → 取 x−e 处值需 off = −e
        For a = 0 To Q - 1
            Dim sx = -EX(a), sy = -EY(a), sz = -EZ(a)
            Dim s = Solid, l = LidSolid
            If sx <> 0 Then s = TensorGrid.ShiftX(s, sx) : l = TensorGrid.ShiftX(l, sx)
            If sy <> 0 Then s = TensorGrid.ShiftY(s, sy) : l = TensorGrid.ShiftY(l, sy)
            If sz <> 0 Then s = TensorGrid.ShiftZ(s, sz) : l = TensorGrid.ShiftZ(l, sz)
            _pullSolid(a) = s
            _pullLid(a) = l
        Next

        ' ---- 初始化：全域静止 f = f_eq(ρ=1, u=0) = w_α ----
        For a = 0 To Q - 1
            Dim wa = W(a)
            For idx = 0 To F(a).Length - 1
                F(a).Data(idx) = wa
            Next
        Next
        ComputeMoments()
    End Sub

#Region "宏观量与诊断"

    ''' <summary>从分布函数重构宏观量（ρ, u）——F 必须为流传播后的当前态。</summary>
    Public Sub ComputeMoments()
        ' ρ = Σ f_α ；ρ·u = Σ f_α·e_α
        Dim r = F(0)
        Dim mx = tfMath.multiply_scalar(F(1), 1.0R) : mx = tfMath.subtract(mx, F(2))
        Dim my = tfMath.multiply_scalar(F(3), 1.0R) : my = tfMath.subtract(my, F(4))
        Dim mz = tfMath.multiply_scalar(F(5), 1.0R) : mz = tfMath.subtract(mz, F(6))
        For a = 1 To Q - 1
            r = tfMath.add(r, F(a))
            If a >= 7 Then
                Dim eax = CDbl(EX(a)), eay = CDbl(EY(a)), eaz = CDbl(EZ(a))
                If eax <> 0.0R Then mx = tfMath.add(mx, tfMath.multiply_scalar(F(a), eax))
                If eay <> 0.0R Then my = tfMath.add(my, tfMath.multiply_scalar(F(a), eay))
                If eaz <> 0.0R Then mz = tfMath.add(mz, tfMath.multiply_scalar(F(a), eaz))
            End If
        Next
        Array.Copy(r.Data, Rho.Data, r.Length)

        Dim invRho = tfMath.reciprocal(tfMath.add(r, tfMath.multiply_scalar(_ones, 0.000000000001R)))
        Array.Copy(tfMath.multiply(mx, invRho).Data, Ux.Data, Ux.Length)
        Array.Copy(tfMath.multiply(my, invRho).Data, Uy.Data, Uy.Length)
        Array.Copy(tfMath.multiply(mz, invRho).Data, Uz.Data, Uz.Length)
    End Sub

    ''' <summary>调试：方向 a 的拉取固体掩膜（0/1 张量）。</summary>
    Public Function DebugPullSolid(a As Integer) As Tensor
        Return _pullSolid(a)
    End Function

    ''' <summary>调试：方向 a 的拉取顶盖掩膜（0/1 张量）。</summary>
    Public Function DebugPullLid(a As Integer) As Tensor
        Return _pullLid(a)
    End Function

    ''' <summary>测试：碰撞后分布 fc_α（与 Advance 完全相同的 dispatch 流水线）。</summary>
    Public Function DebugCollided(a As Integer) As Tensor
        Return CollideAll(EquilibriumAll())(a)
    End Function

    ''' <summary>流体区最大速度（诊断）。</summary>
    Public ReadOnly Property MaxSpeed As Double
        Get
            Dim s2 = tfMath.multiply(Ux, Ux)
            s2 = tfMath.add(s2, tfMath.multiply(Uy, Uy))
            s2 = tfMath.add(s2, tfMath.multiply(Uz, Uz))
            Dim mx = 0.0R
            For idx = 0 To s2.Length - 1
                Dim v = s2.Data(idx) * Fluid.Data(idx)
                If Double.IsFinite(v) AndAlso v > mx Then mx = v
            Next
            Return System.Math.Sqrt(mx)
        End Get
    End Property

    ''' <summary>总动能 E = Σ ½·ρ·|u|²（流体区，格子单位）——收敛性诊断。</summary>
    Public ReadOnly Property Energy As Double
        Get
            Dim e = tfMath.reduce_sum(tfMath.multiply(tfMath.multiply_scalar(
                tfMath.add(tfMath.multiply(Ux, Ux),
                tfMath.add(tfMath.multiply(Uy, Uy), tfMath.multiply(Uz, Uz))), 0.5R),
                tfMath.multiply(Rho, Fluid)))
            Return CDbl(e.Data(0))
        End Get
    End Property

    ''' <summary>质量漂移 |Σρ − ρ₀·N_fluid| / (ρ₀·N_fluid)（守恒性诊断）。</summary>
    Public ReadOnly Property MassDrift As Double
        Get
            Dim s = 0.0R
            For idx = 0 To Rho.Length - 1
                s += Rho.Data(idx) * Fluid.Data(idx)
            Next
            Return System.Math.Abs(s - FluidCount) / FluidCount
        End Get
    End Property

    ''' <summary>总动能（×0.5ρ，诊断收敛用）。</summary>
    Public ReadOnly Property KineticEnergy As Double
        Get
            Dim e = 0.0R
            For idx = 0 To Ux.Length - 1
                Dim w = Fluid.Data(idx)
                e += w * (Ux.Data(idx) ^ 2 + Uy.Data(idx) ^ 2 + Uz.Data(idx) ^ 2)
            Next
            Return 0.5R * e
        End Get
    End Property

#End Region

#Region "主步进：碰撞 + 拉格朗日流 + 半程反弹"

    ''' <summary>执行一个时间步（碰撞 → 流传播 + 反弹）。</summary>
    Public Sub Advance()
        ' ---- ① 碰撞（BGK/TRT/MRT 按模型分派）+ ② 流传播 + 反弹 ----
        Dim fEq = EquilibriumAll()
        Dim fc = CollideAll(fEq)

        Dim newF(Q - 1) As Tensor
        newF(0) = fc(0)   ' 静止方向不流

        For a = 1 To Q - 1
            ' 流传播：f_α(x) ← f^c_α(x − e_α)（TensorGrid 移位；壳外零填充不进入流体）
            Dim streamed = fc(a)
            Dim sx = -EX(a), sy = -EY(a), sz = -EZ(a)
            If sx <> 0 Then streamed = TensorGrid.ShiftX(streamed, sx)
            If sy <> 0 Then streamed = TensorGrid.ShiftY(streamed, sy)
            If sz <> 0 Then streamed = TensorGrid.ShiftZ(streamed, sz)

            ' 半程反弹：拉取源为固体 → f_α = f^c_opp(α)(x) + 6·w_α·ρ·(e_αx·U_wall)
            '   （运动壁修正仅顶盖：2·w·(e·u_w)/cs²，cs²=1/3，u_w=(U_wall,0,0)）
            Dim corr = tfMath.multiply_scalar(
                tfMath.multiply(Rho, _pullLid(a)),
                6.0R * W(a) * EX(a) * UWall)
            Dim bounce = tfMath.add(fc(Opp(a)), corr)

            ' 合成：流区用 streamed，反弹区用 bounce（几何掩膜构造时已预移位）
            Dim notPull = tfMath.add_scalar(tfMath.multiply_scalar(_pullSolid(a), -1.0R), 1.0R)
            newF(a) = tfMath.add(
                tfMath.multiply(streamed, notPull),
                tfMath.multiply(bounce, _pullSolid(a)))
        Next

        ' ---- 写回（双缓冲交换）----
        For a = 0 To Q - 1
            Array.Copy(newF(a).Data, F(a).Data, F(a).Length)
        Next

        ' ---- 固体节点冻结在静止平衡 f = w ----
        ' 若让固体节点的分布自由演化，其（无物理意义的）矩会不受控增长，
        ' 数百步后 Rho(solid) 溢出 → lid 修正项 Inf → newF(solid)=NaN，
        ' 再经下一步 streamed 的 NaN·0=NaN 掩膜乘泄漏进流体区。
        ' 冻结在 w 使固体矩恒为 (ρ=1, u=0)，lid 修正恒有限，彻底截断该路径。
        Dim fl = Fluid.Data
        For a = 0 To Q - 1
            Dim fd = F(a).Data, wd = W(a)
            For idx = 0 To fd.Length - 1
                If fl(idx) = 0.0R Then fd(idx) = wd
            Next
        Next

        ComputeMoments()

        _step += 1
        _time += 1.0R
    End Sub

    ''' <summary>
    ''' 碰撞（按 Model 分派）。返回碰撞后分布 fc_α（未流传播）。
    ''' 所有实现均保持 f = f_eq 不动点与质量/动量精确守恒（xunit 覆盖）。
    ''' </summary>
    Private Function CollideAll(fEq As Tensor()) As Tensor()
        Select Case _model
            Case CollisionModel.BGK : Return CollideBGK(fEq)
            Case CollisionModel.TRT : Return CollideTRT(fEq)
            Case CollisionModel.MRT : Return CollideMRT(fEq)
            Case Else
                Throw New InvalidOperationException($"未知碰撞模型: {_model}")
        End Select
    End Function

    ''' <summary>BGK：f^c = (1−ω)f + ω·f_eq。</summary>
    Private Function CollideBGK(fEq As Tensor()) As Tensor()
        Dim fc(Q - 1) As Tensor
        For a = 0 To Q - 1
            fc(a) = tfMath.add(
                tfMath.multiply_scalar(F(a), 1.0R - Omega),
                tfMath.multiply_scalar(fEq(a), Omega))
        Next
        Return fc
    End Function

    ''' <summary>
    ''' TRT：奇偶分解松弛。f^c_a = A·f_a + B·f_opp + C·f^eq_a + D·f^eq_opp，
    ''' ω⁻ = 1/τ（粘性），ω⁺ 由幻影参数 Λ=1/4 确定（τ⁺ = ½ + Λ/(τ−½)）。
    ''' 高 Re（τ→½）时偶模（应力）被强阻尼 → 稳定。
    ''' </summary>
    Private Function CollideTRT(fEq As Tensor()) As Tensor()
        Dim omM = Omega                                 ' ω⁻ = 1/τ
        Dim omP = 1.0R / (0.5R + MrtBasisD3Q19.MagicLambda / (_tau - 0.5R))   ' ω⁺
        Dim cA = 1.0R - (omP + omM) / 2.0R
        Dim cB = (omM - omP) / 2.0R
        Dim cC = (omP + omM) / 2.0R
        Dim cD = (omP - omM) / 2.0R

        Dim fc(Q - 1) As Tensor
        ' 静止方向：f⁺ = f₀，f⁻ = 0 → 只受 ω⁺ 松弛
        fc(0) = tfMath.add(
            tfMath.multiply_scalar(F(0), 1.0R - omP),
            tfMath.multiply_scalar(fEq(0), omP))
        For a = 1 To Q - 1
            Dim o = Opp(a)
            fc(a) = tfMath.add(
                tfMath.add(
                    tfMath.multiply_scalar(F(a), cA),
                    tfMath.multiply_scalar(F(o), cB)),
                tfMath.add(
                    tfMath.multiply_scalar(fEq(a), cC),
                    tfMath.multiply_scalar(fEq(o), cD)))
        Next
        Return fc
    End Function

    ''' <summary>
    ''' MRT：f^c = f − M⁻¹·S·(m − m_eq)，m = M·f，m_eq = M·f_eq。
    ''' 守恒模 s=0；应力/能量模 s=1/τ；高阶幻影模 s=1。
    ''' 全张量化：矩 = 方向张量的加权线性组合（19×19），逆变换同。
    ''' </summary>
    Private Function CollideMRT(fEq As Tensor()) As Tensor()
        Dim s = MrtBasisD3Q19.RelaxationSpectrum(_tau)

        ' g_a = f_a − f_eq_a（19 个差场）
        Dim g(Q - 1) As Tensor
        For a = 0 To Q - 1
            g(a) = tfMath.subtract(F(a), fEq(a))
        Next

        ' d_k = Σ_a M[k][a]·g_a（矩空间偏差；守恒模跳过）
        Dim d(18) As Tensor
        For k = 0 To 18
            If s(k) = 0.0R Then Continue For
            Dim acc As Tensor = Nothing
            For a = 0 To Q - 1
                Dim wgt = _mrtM(k, a)
                If wgt = 0.0R Then Continue For
                Dim term = tfMath.multiply_scalar(g(a), wgt)
                acc = If(acc Is Nothing, term, tfMath.add(acc, term))
            Next
            d(k) = acc
        Next

        ' f^c_a = f_a − Σ_k Minv[a][k]·s_k·d_k
        Dim fc(Q - 1) As Tensor
        For a = 0 To Q - 1
            Dim acc As Tensor = Nothing
            For k = 0 To 18
                If s(k) = 0.0R OrElse d(k) Is Nothing Then Continue For
                Dim wgt = _mrtMinv(a, k) * s(k)
                If wgt = 0.0R Then Continue For
                Dim term = tfMath.multiply_scalar(d(k), wgt)
                acc = If(acc Is Nothing, term, tfMath.add(acc, term))
            Next
            fc(a) = If(acc Is Nothing, F(a), tfMath.subtract(F(a), acc))
        Next
        Return fc
    End Function

    ''' <summary>
    ''' 逐方向平衡态 f_α^eq = w_α·ρ·[1 + 3(e·u) + 4.5(e·u)² − 1.5|u|²]
    ''' （不可压 He-Zou 形式，低马赫精度更好）。
    ''' </summary>
    Private Function EquilibriumAll() As Tensor()
        Dim eq(Q - 1) As Tensor
        Dim u2 = tfMath.add(tfMath.multiply(Ux, Ux),
               tfMath.add(tfMath.multiply(Uy, Uy), tfMath.multiply(Uz, Uz)))

        ' e·u 的 7 种组合值（D3Q19 只有 ±1 分量 → 组合数有限）
        ' 组合表：permutation (ex,ey,ez) → 逐方向计算更直接
        For a = 0 To Q - 1
            Dim eax = CDbl(EX(a)), eay = CDbl(EY(a)), eaz = CDbl(EZ(a))
            Dim eu = tfMath.add(
                tfMath.multiply_scalar(Ux, eax),
                tfMath.add(
                    tfMath.multiply_scalar(Uy, eay),
                    tfMath.multiply_scalar(Uz, eaz)))
            ' f_eq = w·ρ·[1 + 3(e·u) + 4.5(e·u)² − 1.5|u|²]（ρ 乘满整个括号！）
            Dim inner = tfMath.add(
                tfMath.add_scalar(tfMath.multiply_scalar(eu, 3.0R), 1.0R),
                tfMath.add(
                    tfMath.multiply_scalar(tfMath.multiply(eu, eu), 4.5R),
                    tfMath.multiply_scalar(u2, -1.5R)))
            eq(a) = tfMath.multiply_scalar(tfMath.multiply(Rho, inner), W(a))
        Next
        Return eq
    End Function

#End Region

#Region "Ghia 基准中心线（3D 取展向中面）"

    ''' <summary>
    ''' 垂直中心线 u_x(z)：x = Nx/2, y = Ny/2 展向中面。
    ''' 返回 (yNorm, uNorm) 采样对（y 从底 0 到顶 1，u 对 U_wall 归一）。
    ''' </summary>
    Public Function CenterlineX() As Double(,)
        Dim ci = Nx \ 2, cj = Ny \ 2
        Dim plane = Ny * Nz, row = Nz
        Dim m = Nz - 2
        Dim res(m - 1, 1) As Double
        For k = 1 To Nz - 2
            Dim idx = ci * plane + cj * row + k
            res(k - 1, 0) = (k - 1) / H
            res(k - 1, 1) = Ux.Data(idx) / UWall
        Next
        Return res
    End Function

    ''' <summary>
    ''' 水平中心线 v(x)=u_z：z = Nz/2 高度中面，y = Ny/2（Ghia 的 v 是 2D 垂直速度分量）。
    ''' 返回 (xNorm, vNorm) 采样对（x 从左 0 到右 1）。
    ''' </summary>
    Public Function CenterlineY() As Double(,)
        Dim cj = Ny \ 2, ck = Nz \ 2
        Dim plane = Ny * Nz, row = Nz
        Dim m = Nx - 2
        Dim res(m - 1, 1) As Double
        For i = 1 To Nx - 2
            Dim idx = i * plane + cj * row + ck
            res(i - 1, 0) = (i - 1) / (CDbl(Nx - 3))
            res(i - 1, 1) = Uz.Data(idx) / UWall   ' Ghia 的 v = 2D 垂直速度 = 本 3D 的 u_z（k 竖直向上）
        Next
        Return res
    End Function

#End Region

End Class
