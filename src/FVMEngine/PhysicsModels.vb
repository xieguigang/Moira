Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   PhysicsModels.vb（FvmSolver Part 2/2）—— 五个核心模块中的 ②③④ 全部在此
'
'   ★★★ FVM.md 核心模块 → 本文件方法位置 ★★★
'     模块② 旋转处理（MRF 虚拟桨盘 §二）        ComputeImpellerSource
'     模块③ 欧拉两相流 §三                       TransportGasFraction（α 输运）
'         └ §3.4 氧传质与反应                    TransportOxygen（DO + Higbie kLa）
'     模块④ 湍流 k-ε §四                         UpdateTurbulentViscosity
'                                                 TransportTurbulence
'     模块① 通用离散算子（被上方各模块复用）     UpwindOutflux / Laplacian
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports tfMath = Microsoft.VisualBasic.MachineLearning.TensorFlow.Math

Partial Public Class FvmSolver

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ②【旋转处理 —— MRF 多重参考系】 —— FVM.md §二 2.1（网格静止 + 参考系变换附加源项）
    ' ║ MRF 虚拟桨盘（rotorDiskMRF 思路）：桨叶带切向/径向动量源 S=ρ(u_θT−u_θ)/τ
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' MRF 虚拟桨盘（rotorDiskMRF 思路）：桨叶带内把切向速度拉向叶轮线速度
    ''' Ω·r（Rushton 以切向为主），叠加径向泵出分量（径向流桨特征），
    ''' 源项强度由 ImpellerTau 控制；同步累计搅拌功率 P = Σ Sθ·uθ_target·V。
    ''' </summary>
    Private Sub ComputeImpellerSource(ByRef sx As Tensor, ByRef sy As Tensor)
        ' 当前切向速度 uθ = −sinφ·u + cosφ·v
        Dim uTheta = tfMath.add(
            tfMath.multiply(tfMath.multiply_scalar(_ty, -1.0R), U),
            tfMath.multiply(_tx, V))
        ' 目标切向速度（叶轮线速度剖面）：min(Ωr, 1.05·tip)（轮毂区限幅）
        Dim uThetaT = tfMath.minimum(
            tfMath.multiply_scalar(_rPhys, Omega),
            TensorGrid.Filled(_nx, _ny, _nz, 1.05R * TipSpeed))
        ' 目标径向速度（径向泵出，桨带内向外）
        Dim uRadT = TensorGrid.Filled(_nx, _ny, _nz, 0.35R * TipSpeed)
        Dim uRad = tfMath.add(tfMath.multiply(_tx, U), tfMath.multiply(_ty, V))

        ' 源：S = ρ(ut − u)/τ（力密度 N/m³）
        Dim sTheta = tfMath.multiply(_impZone, tfMath.multiply_scalar(
            tfMath.subtract(uThetaT, uTheta), Rho / ImpellerTau))
        Dim sRad = tfMath.multiply(_impZone, tfMath.multiply_scalar(
            tfMath.subtract(uRadT, uRad), Rho / ImpellerTau))

        ' 功率：P = Σ Sθ·uθT·V（= Ω·Σ Sθ·r·V 转矩功率；uθT≈Ωr，仅切向计）
        Dim workDens = tfMath.multiply(sTheta, uThetaT)
        ImpellerPower = CDbl(nominal(tfMath.reduce_sum(tfMath.multiply(workDens, _act)))) * _cellVolume
        ' 只计注入功率（>0 部分），排除反抽吸
        If ImpellerPower < 0.0R Then ImpellerPower = 0.0R

        ' 笛卡尔投影：Sx = Sr·cosφ − Sθ·sinφ；Sy = Sr·sinφ + Sθ·cosφ
        sx = tfMath.subtract(tfMath.multiply(sRad, _tx), tfMath.multiply(sTheta, _ty))
        sy = tfMath.add(tfMath.multiply(sRad, _ty), tfMath.multiply(sTheta, _tx))
    End Sub

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ④【湍流 k-ε（粘度闭包）】 —— FVM.md §四 4.1 RANS：νt = Cμ·k²/ε + 上限截断
    ' ║ 每步先于动量预估更新（Sato 含气增粘可选扩展点）
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>ν_t = Cμ k²/ε（有界截断，realizable 风格上限）。</summary>
    Private Sub UpdateTurbulentViscosity()
        Dim epsSafe = tfMath.maximum(Eps, TensorGrid.Filled(_nx, _ny, _nz, 1e-10R))
        Dim k2 = tfMath.square(K)
        Dim nutNew = tfMath.multiply(_act, tfMath.divide(tfMath.multiply_scalar(k2, Cmu), epsSafe))
        ' 上限：0.5·tip·D/20 量级，防早期奇异
        nutNew = tfMath.minimum(nutNew, TensorGrid.Filled(_nx, _ny, _nz, 0.05R * TipSpeed * _dx * 50.0R))
        Array.Copy(nutNew.Data, Nut.Data, Nut.Length)
    End Sub

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ③【欧拉-欧拉两相流 —— 气相体积分数 α 输运】 —— FVM.md §三 3.1 双流体 + 3.2 相间曳力
    ' ║ ∂α/∂t+∇·(α(u+w_slip ẑ))=源−脱气；滑移为 Schiller-Naumann（常数 d_b：Phase-1 简化，PBM 为 §6.2 扩展点）
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' 气相体积分数输运：∂α/∂t + ∇·(α·(u + w_slip·ẑ)) = 分布环源 − 顶部脱气。
    ''' 一阶迎风 + 显式时间推进 + clip [0, αmax]。
    ''' </summary>
    Private Sub TransportGasFraction()
        ' 气相速度场：ug = (u, v, w + w_slip)
        Dim ugW = tfMath.add(W, TensorGrid.Filled(_nx, _ny, _nz, SlipVelocity))

        ' 三方向迎风通量（体积分数 × 体积流量）
        Dim outflux = UpwindOutflux(Alpha, U, U)
        Dim outfluxN = UpwindOutflux(Alpha, V, V)
        Dim outfluxT = UpwindOutflux(Alpha, ugW, ugW)

        ' α_new = α − dt/V·(div out)
        Dim divOut = tfMath.add(tfMath.add(
            tfMath.subtract(outflux, TensorGrid.ShiftX(outflux, -1)),
            tfMath.subtract(outfluxN, TensorGrid.ShiftY(outfluxN, -1))),
            tfMath.subtract(outfluxT, TensorGrid.ShiftZ(outfluxT, -1)))
        Dim alphaNew = tfMath.subtract(
            tfMath.multiply(Alpha, _act),
            tfMath.multiply_scalar(tfMath.multiply(_act, divOut), Dt / _cellVolume))

        ' ---- 分布环充气源：环形喷入，均匀分配 GasFlowRate ----
        Dim spgCount = System.Math.Max(1, CInt(CDbl(nominal(tfMath.reduce_sum(_spgZone)))))
        Dim injectVol = GasFlowRate * Dt / spgCount       ' 每环体素注入体积 m³
        Dim alphaJet = System.Math.Min(injectVol / _cellVolume, 0.3R)
        Dim spgAdd = tfMath.multiply_scalar(_spgZone, alphaJet / Dt * Dt)   ' = α_jet·(dt 已含)
        alphaNew = tfMath.add(alphaNew, tfMath.multiply_scalar(tfMath.multiply(_spgZone, _act), alphaJet))

        ' ---- 顶部脱气：液面层 α 快速衰减（气泡逸出） ----
        Dim notTop = TensorGrid.InverseMask(_topZone)
        Dim topRelax = tfMath.multiply(_topZone, tfMath.multiply_scalar(Alpha, 0.5R))
        alphaNew = tfMath.add(tfMath.multiply(alphaNew, notTop), topRelax)

        ' clip + 边界
        alphaNew = tfMath.clip_by_value(tfMath.multiply(alphaNew, _act), 0.0R, 0.35R)
        Array.Copy(alphaNew.Data, Alpha.Data, Alpha.Length)
    End Sub

    ''' <summary>
    ' ★ 模块① 通用离散算子：一阶迎风面通量（FVM.md §一 1.2，被 ③④ 各输运方程复用）
    ''' 标量 φ 沿速度分量 vel 的迎风面通量（East 面约定）：
    ''' F = [0.5(uP+uE) − 0.5|uP+uE|]·φ_P…——返回“以 P 为参考的出流面通量” φ_f·q_f，
    ''' div 用 ShiftX(F, -1) 组合。
    ''' </summary>
    Private Function UpwindOutflux(phi As Tensor, vel As Tensor, velFace As Tensor) As Tensor
        ' 面速度（这里 vel 与 velFace 相同时即简单面插值）
        Dim uFace = tfMath.multiply_scalar(tfMath.add(vel, TensorGrid.East(velFace)), 0.5R)
        Dim uAbs = tfMath.abs(uFace)
        Dim sgn = tfMath.divide(uFace, tfMath.add(uAbs, TensorGrid.Filled(_nx, _ny, _nz, 1e-12R)))
        ' 迎风 φ_f = 0.5(φP+φE) + 0.5·sgn·(φP−φE)
        Dim phiF = tfMath.add(
            tfMath.multiply_scalar(tfMath.add(phi, TensorGrid.East(phi)), 0.5R),
            tfMath.multiply(tfMath.multiply_scalar(sgn, 0.5R),
                            tfMath.subtract(phi, TensorGrid.East(phi))))
        ' 面通量（体积流量加权）
        Return tfMath.multiply(phiF, tfMath.multiply_scalar(uFace, _dx * _dx))
    End Function

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ④【湍流 k-ε 联立输运】 —— FVM.md §四 4.1（标准 k-ε，P_G=νt·2S:S 显式生成 + 隐式衰减）
    ' ║ k/ε 皆 clip 保正（§6.1 有界性优先）
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' k-ε 联立输运（显式对流 + 隐式衰减）：
    ''' k:  (k + dt·(P_G + ∇·(ν_eff/σk ∇k)))/(1 + dt·ε/k)
    ''' ε:  (ε + dt·(Cε1·ε/k·P_G + ∇·(ν_eff/σε ∇ε)))/(1 + dt·Cε2·ε/k)
    ''' 源项全部 clip 保正。
    ''' </summary>
    Private Sub TransportTurbulence()
        ' ---- 应变率张量（9 分量，中心差分）----
        Dim dudx = TensorGrid.Ddx(U, _dx), dudy = TensorGrid.Ddy(U, _dx), dudz = TensorGrid.Ddz(U, _dx)
        Dim dvdx = TensorGrid.Ddx(V, _dx), dvdy = TensorGrid.Ddy(V, _dx), dvdz = TensorGrid.Ddz(V, _dx)
        Dim dwdx = TensorGrid.Ddx(W, _dx), dwdy = TensorGrid.Ddy(W, _dx), dwdz = TensorGrid.Ddz(W, _dx)

        ' ---- 湍流生成 P_G = ν_t·2S:S ----
        Dim sxy = tfMath.multiply_scalar(tfMath.add(dudy, dvdx), 0.5R)
        Dim sxz = tfMath.multiply_scalar(tfMath.add(dudz, dwdx), 0.5R)
        Dim syz = tfMath.multiply_scalar(tfMath.add(dvdz, dwdy), 0.5R)
        Dim diag = tfMath.add(tfMath.add(tfMath.square(dudx), tfMath.square(dvdy)), tfMath.square(dwdz))
        Dim shear = tfMath.add(tfMath.multiply_scalar(tfMath.square(sxy), 2.0R),
                               tfMath.add(tfMath.multiply_scalar(tfMath.square(sxz), 2.0R),
                                          tfMath.multiply_scalar(tfMath.square(syz), 2.0R)))
        Dim pG = tfMath.multiply(Nut, tfMath.multiply_scalar(tfMath.add(diag, shear), 2.0R))
        ' 桨带内生成下限（保证叶轮区供能）
        pG = tfMath.maximum(pG, tfMath.multiply(_impZone,
            TensorGrid.Filled(_nx, _ny, _nz, 0.05R * TipSpeed * TipSpeed * TipSpeed / _dx)))
        pG = tfMath.multiply(pG, _act)

        Dim epsSafe = tfMath.maximum(Eps, TensorGrid.Filled(_nx, _ny, _nz, 1e-10R))
        Dim kSafe = tfMath.maximum(K, TensorGrid.Filled(_nx, _ny, _nz, 1e-8R))

        ' ---- k 方程 ----
        Dim lapK = Laplacian(K)
        Dim kDiff = tfMath.multiply_scalar(lapK, (Nu + 1.0R / Sigmak * 0.01R))
        Dim kRhs = tfMath.add(tfMath.add(K, tfMath.multiply_scalar(tfMath.multiply(_act, tfMath.add(pG, kDiff)), Dt)),
                              TensorGrid.Filled(_nx, _ny, _nz, 0.0R))
        Dim kDenom = tfMath.add_scalar(tfMath.multiply(_act, tfMath.divide(epsSafe, kSafe)), 1.0R)
        Dim kNew = tfMath.multiply(tfMath.divide(kRhs, kDenom), _act)
        kNew = tfMath.maximum(kNew, TensorGrid.Filled(_nx, _ny, _nz, 1e-6R * TipSpeed * TipSpeed))
        kNew = tfMath.minimum(kNew, TensorGrid.Filled(_nx, _ny, _nz, 0.5R * TipSpeed * TipSpeed))

        ' ---- ε 方程 ----
        Dim lapE = Laplacian(Eps)
        Dim eDiff = tfMath.multiply_scalar(lapE, (Nu + 1.0R / Sigmae * 0.01R))
        Dim eProd = tfMath.multiply_scalar(tfMath.multiply(tfMath.divide(epsSafe, kSafe), pG), Ce1)
        Dim eRhs = tfMath.add(Eps, tfMath.multiply_scalar(tfMath.multiply(_act, tfMath.add(eProd, eDiff)), Dt))
        Dim eDenom = tfMath.add_scalar(tfMath.multiply_scalar(tfMath.multiply(_act, tfMath.divide(epsSafe, kSafe)), Ce2), 1.0R)
        Dim eNew = tfMath.multiply(tfMath.divide(eRhs, eDenom), _act)
        Dim epsMin = 1e-8R * TipSpeed * TipSpeed * TipSpeed / _dx
        eNew = tfMath.maximum(eNew, TensorGrid.Filled(_nx, _ny, _nz, epsMin))
        eNew = tfMath.minimum(eNew, TensorGrid.Filled(_nx, _ny, _nz, 10.0R * TipSpeed * TipSpeed * TipSpeed / _dx))

        Array.Copy(kNew.Data, K.Data, K.Length)
        Array.Copy(eNew.Data, Eps.Data, Eps.Length)
    End Sub

    ' ★ 模块① 通用离散算子：扩散项中心差分（FVM.md §一 1.2 D_f ∇φ，被 ③④ 复用）
    ''' <summary>显式拉普拉斯（7 点，零扩散系数版：∇²t / dx²）。</summary>
    Private Function Laplacian(t As Tensor) As Tensor
        Dim sum_ = tfMath.add(tfMath.add(tfMath.add(
            TensorGrid.East(t), TensorGrid.West(t)),
            tfMath.add(TensorGrid.North(t), TensorGrid.South(t))),
            tfMath.add(TensorGrid.Top(t), TensorGrid.Bottom(t)))
        Dim sixT = tfMath.multiply_scalar(t, 6.0R)
        Return tfMath.multiply_scalar(tfMath.subtract(sum_, sixT), 1.0R / (_dx * _dx))
    End Function

    ' ╔════════════════════════════════════════════════════════════════════════════╗
    ' ║ ★ 核心模块 ③【欧拉两相流 §3.4：氧传质与反应】 —— FVM.md §三 3.4（核心耦合链：ε→kL→kLa→DO）
    ' ║ ∂C/∂t+u·∇C=D_eff∇²C+kLa(C*−C)−OUR；kL=0.4(D_L·ε/ν)^0.25（Higbie/Lamont-Scott）
    ' ╚════════════════════════════════════════════════════════════════════════════╝

    ''' <summary>
    ''' 溶氧输运：∂C/∂t + u·∇C = D_eff∇²C + kLa(C*−C) − OUR。
    ''' kLa 闭包（Higbie/Lamont-Scott）：
    '''   kL = 0.4·(D_L·ε/ν)^0.25，a = 6α/d_b → kLa = a·kL
    ''' </summary>
    Private Sub TransportOxygen()
        ' ---- kLa 场 ----
        Dim dL = 2.0R * 1e-9R                     ' 液相氧扩散系数 m²/s
        Dim epsSafe = tfMath.maximum(Eps, TensorGrid.Filled(_nx, _ny, _nz, 1e-10R))
        Dim kL = tfMath.multiply_scalar(
            tfMath.pow(tfMath.multiply_scalar(tfMath.divide(epsSafe,
                TensorGrid.Filled(_nx, _ny, _nz, Nu)), dL), 0.25R), 0.4R)
        ' ★ §3.3→§3.4 传质链打通：a = 6α/d32（Sauter 直径来自 PBM，不再是常数 d_b）
        ' 活动区取 d32（PBM），非活动区回退常数 d_b
        Dim dEff = tfMath.add(tfMath.multiply(D32, _act),
                              tfMath.multiply_scalar(
                                  tfMath.add_scalar(tfMath.multiply_scalar(_act, -1.0R), 1.0R), BubbleDiameter))
        Dim aInterf = tfMath.multiply(tfMath.divide(tfMath.multiply_scalar(Alpha, 6.0R), dEff), _act)
        Dim klaField = tfMath.multiply(tfMath.multiply(aInterf, kL), _act)
        Array.Copy(klaField.Data, KLa.Data, KLa.Length)

        ' ---- 对流（迎风，液相速度）----
        Dim fluxE = UpwindOutflux(O2, U, U)
        Dim fluxN = UpwindOutflux(O2, V, V)
        Dim fluxT = UpwindOutflux(O2, W, W)
        Dim divF = tfMath.add(tfMath.add(
            tfMath.subtract(fluxE, TensorGrid.ShiftX(fluxE, -1)),
            tfMath.subtract(fluxN, TensorGrid.ShiftY(fluxN, -1))),
            tfMath.subtract(fluxT, TensorGrid.ShiftZ(fluxT, -1)))

        ' ---- 扩散（湍流主导）+ 传质源 ----
        Dim diffCoeff = Nu * 1e-0R + 0.7R                          ' 有效扩散（湍流 Sc_t=0.7）
        Dim lapC = Laplacian(O2)
        Dim transfer = tfMath.multiply(KLa, tfMath.add_scalar(O2, -DOSturation))
        Dim ourSink = tfMath.multiply(_act, TensorGrid.Filled(_nx, _ny, _nz, OUR))

        Dim advect = tfMath.multiply_scalar(tfMath.multiply(_act, divF), -1.0R / _cellVolume)
        Dim dcDt = tfMath.subtract(tfMath.subtract(advect, transfer), ourSink)
        dcDt = tfMath.add(dcDt, tfMath.multiply_scalar(tfMath.multiply(_act, lapC), diffCoeff))

        Dim cNew = tfMath.add(O2, tfMath.multiply_scalar(dcDt, Dt))
        cNew = tfMath.clip_by_value(tfMath.multiply(cNew, _act), 0.0R, DOSturation)
        Array.Copy(cNew.Data, O2.Data, O2.Length)
    End Sub

End Class
