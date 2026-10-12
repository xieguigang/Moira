# Moira CFD —— FVM 引擎修复与测试报告

> 测试环境：Windows / .NET 10.0.401 / CUDA NVRTC 13.3 (sm_86)
> 测试工具：`G:\Moira\test\StreamlineDiag\`（命令行 + GPU）
> 原始日志：`G:\Moira\test\logs\`
> 网格产物：`G:\Moira\test\mesh\`

---

## 0. 摘要

| 项目 | 结果 |
|---|---|
| **修复 1**：面通量初值与速度场不同步 | ✅ 已修复并验证（**这是本次最关键的修复**） |
| **修复 2**：GPU float32 压力求解"假收敛" | ✅ 已修复并验证（含 CPU double 自动回落） |
| **修复 3**：WindTunnelBox 无法配置求解器关键参数 | ✅ 已修复（补齐 5 个参数透传） |
| **修复 4**：新增求解器/网格诊断 API | ✅ 已加入（`RefreshInitialFlux` / `ScanDeadEndCells` / `DeadEndCellCount`） |
| **验证 1**：空通道教科书基准 | ✅ **PASS**（连续性 4.9e-9，压力 2.5e-4 Pa，严格保持均匀来流） |
| **验证 2**：通道 + 实心障碍 | ✅ **稳定**（40 步无发散，连续性 1.0e-3，压力 16 Pa） |
| **验证 3**：既有自测工程回归 | ✅ **45/45 通过**（`EulerianFvmSelfTest`），`FlowVizSelfTest` 全通过 |
| **未完成**：city_seed42 细网格仍发散 | ⚠️ 根因已定位（阶梯式障碍的"死端单元"），修复方案见第 6 节 |

**一句话**：原先"求解器第 2~3 步就发散"的直接原因是**面通量初值与速度场不同步**（一个纯粹的时序 bug），
已修复；修复后求解器能在教科书算例上给出**机器精度级**的正确结果。
城市算例仍会在第 2 步发散，但根因已换成另一个**几何固有**问题（阶梯式障碍产生的死端单元），
与前面的 bug 无关，且**加密网格无法解决**（实测 3.77% → 2.01%）。

---

## 1. 测试方法

### 1.1 新增/使用的命令行子命令

```
StreamlineDiag mesh         <eulerian.zip>                        仅网格诊断
StreamlineDiag convert      <model.stl> <out.zip> [res] [--cut-cells]  STL → Eulerian 网格（GPU）
StreamlineDiag deadends     <eulerian.zip>                        死端单元占比（轻量网格质量指标）
StreamlineDiag divergence   <eulerian.zip>                        网格散度-自由性 + 初值通量一致性
StreamlineDiag deadends     <eulerian.zip>                        死端单元统计
StreamlineDiag momentumdiag <eulerian.zip> [steps]                动量方程对角元 / rAU 分位 / 通量量级
StreamlineDiag pressurediag <eulerian.zip> [steps] [gpu]          逐步推进 + 压力求解器诊断
StreamlineDiag pcorr        <eulerian.zip> [steps] [gpu]          压力修正方向性验证
StreamlineDiag resampler    <eulerian.zip>                        AutoConfigure / 重采样探针
StreamlineDiag il           <dll> <类型> <方法>                     IL 反汇编
StreamlineDiag channel      [nx] [ny] [nz] [U] [nu] [steps] [gpu] [pIter] [pTol] [relaxP] [relaxU] [sweeps] [obstacle]
                                                                   ★ 空通道 / 通道+障碍 教科书验证
```

新增通用覆盖参数：`--priter=` `--prtol=` `--sweep=` `--co=`（伪瞬态 Courant）`--speed=` `--snap=`

### 1.2 新增的独立基准算例 `ChannelTest.vb`

**空通道**：`20×4×4` 六面体，L=20、H=W=1，`U∞=30 m/s`，`ν=1.5e-5`。
稳态解析解就是 `u=(30,0,0)`、`∇p≈0`（层流粘性损失可忽略）。
**这是判别"算法对不对"的干净基准**：如果它都不能保持均匀来流，问题在算法/边界；能，则问题在网格几何。

**通道 + 实心障碍**：同一网格挖掉 x∈[40%,60%]、y∈[0%,50%] 的方块，复刻 `city_seed42` 的"均匀笛卡尔 + 障碍"几何。

---

## 2. 核心修复 1：面通量初值与速度场不同步

### 2.1 问题

`src\FVMEngine\Eulerian\EulerianWindTunnelCase.vb` 的 `Configure()`：

```vb
Me.Incompressible = New IncompressibleSolver(Mesh, Fields, iopts)
Call Incompressible.Initialize()          ' ← 此时 U 全场为 0（只有入口 BC = 30）
                                          '   Initialize() 内部用这个零速度场构造了 φ_f
...
For c = 0 To n - 1                        ' ← 之后才写入远场初值
    u(c) = 30 ...
Next
Call Fields.U.UpdateBoundaries()
                                          ' ← 没有任何地方重新构造 φ_f ！
```

`IncompressibleSolver.Initialize()` 第 343 行：

```vb
Call EulerianOperators.FaceVolumetricFlux(fields.U, _flux)   ' φ_f = u_f·S_f
Call UpdateMassFlux()
```

**在速度场还是全零时**，只有入口 patch（`FixedValueBc`，值为 30）会产出通量：

```
[FLUXDBG]（在 Configure() 内部实时抓取）
  patch=inlet    fixed=True bfield=(30,0,0) owner=(0,0,0) F=-17012.6
  patch=outlet   fixed=False bfield=(0,0,0) owner=(0,0,0) F=0        ← 出口通量 = 0
  patch=ground/top/symmetry/wall  F=0
```

于是初始 `_flux` 变成了"**入口注入 1.742e7 m³/s、出口 0**"的场——质量凭空产生。

### 2.2 实测后果

| 指标 | 修复前 | 修复后 | 正确量级 |
|---|---|---|---|
| 配置后 `maxAbs(div φ)` | **1.701e4** | **0** | 0 |
| 入口/出口净通量 | −1.742e7 / **0** | −1.742e7 / +1.742e7 | 必须等值反号 |
| 第 1 步连续性误差 | **4.179e8** | 3.867e8（换个机制，见第 6 节） | < 1e3 |
| 第 1 步压力修正 | 1.5e1 Pa | — | ~ρU²≈550 Pa |
| 发散步号 | 第 3 步 | 第 2 步（另一机制） | 不发散 |

单看"入口/出口净通量"这一行就够了：**修复前出口通量恒为 0**，这在一个开放风洞里是物理上不可能的状态。

### 2.3 修复

**`IncompressibleSolver.RefreshInitialFlux()`**（新增）：

```vb
''' ★ 必须在**任何**改变速度场的初值/边界装配之后调用一次。
'''   时序陷阱：Initialize() 会在装配边界后立刻用当时的速度场构造 φ；
'''   若调用方在此之后才把速度场设为远场值，则 φ 就与速度场**不一致**——
'''   典型表现是入口注入质量而出口通量为 0，
'''   离散连续性残差 Σ_f φ_f ≈ 单个单元的面通量量级（而非机器零）。
'''   实测该缺陷使 city_seed42 算例第 1 步连续性误差达到 4.2e8，
'''   并在第 3 步彻底发散。
Public Sub RefreshInitialFlux()
    Call UpdateProperties()
    Call EulerianOperators.FaceVolumetricFlux(Fields.U, _flux)
    Call UpdateMassFlux()
End Sub
```

**`EulerianWindTunnelCase.Configure()`** 末尾调用它，并把这个时序陷阱写进注释。

### 2.4 验证

`StreamlineDiag divergence` 的"求解器 `Initialize()` 后的 `_flux` 散度"一节：

```
修复前：
  配置后 _flux: max|F|=1.701E+004  maxAbs(div)=1.701E+004
    patch inlet : 净通量 = -1.742E+007
    patch outlet: 净通量 =  0.000E+000
  重算 FaceVolumetricFlux 与 _flux 是否一致：False（差异面数 31561）

修复后：
  配置后 _flux: max|F|=1.701E+004  maxAbs(div)=0.000E+000
    patch inlet : 净通量 = -1.742E+007
    patch outlet: 净通量 = +1.742E+007
  重算 FaceVolumetricFlux 与 _flux 是否一致：True（差异面数 0）
```

---

## 3. 核心修复 2：GPU float32 压力求解"假收敛"

### 3.1 问题

`EulerianFvmCompute.TrySolve`（GPU 路径）在病态矩阵上会**谎报收敛**：

```
压力方程 CPU double 需要          285~3695 次 PCG（取决于收敛容差）
压力方程 GPU float32 报告          1 次迭代，residual 1.000E+000 -> 1.000E+000
```

残差**一动不动**意味着解完全没变——压力修正量 = 0。
而外层 SIMPLE 把它当成"已完成一次压力修正"，带着未修正的通量进入下一轮 → 正反馈发散。

根因：压力方程在"死端单元"上 `rAU = 1/a_P` 可达 1e-11，对角元与右端项相差 20+ 个量级，
float32 的 `pAp` / `rho` 因灾难性相消而失真，PCG 的 `normR <= relTol*initial` 判据被偶然满足。

### 3.2 修复

在 `TrySolve` 写回 double 权威残差之后，加**实质下降校验**：

```vb
' ★★ float32 假收敛防护（关键正确性校验）
'   若把这种"零进展"结果当作已求解交给外层 SIMPLE，
'   压力修正量恒为 0，速度场被未修正的 Rhie-Chow 通量反复放大 → 发散。
'   因此这里做**实质下降校验**：double 权威残差没有明显下降时，
'   丢弃 GPU 结果并返回 False，由 EulerianLinearSolve 回落 CPU double。
If Not rpt.Converged Then
    Dim madeProgress As Boolean =
        rpt.FinalResidual <= 0.5 * initial OrElse
        (initial <= tolerance AndAlso rpt.FinalResidual <= tolerance)

    If Not madeProgress Then
        rpt.Notes = Combine2(rpt.Notes, $"GPU rejected: no real residual progress ...")
        rpt.ExecutedOnGpu = False
        report = rpt
        Return False          ' → EulerianLinearSolve 自动回落 CPU double
    End If
End If
```

### 3.3 验证

同一 city 网格、同一 GPU 开关，修复前后对比压力求解器行为：

| | 修复前 | 修复后 |
|---|---|---|
| 稳定期的 `pIt` | **1**（残差 `1.000 -> 1.000`） | **88 / 250 / 265 / 278 / 291**（残差 `1.0 -> 9.1e-3`） |
| 是否回落 CPU | 否（静默接受） | 是（`Notes` 记录 "GPU rejected"） |
| 求解器的实际工作 | 无 | 正常 |

修复后日志：

```
  step    max|U|        max|p|       cont           pIt  pRes(init->final)
     1    7.449E+002    5.191E+008    1.682E+011     88  1.304E+004->1.184E+002
     2    2.497E+005    7.821E+011    3.770E+012    265  1.000E+000->9.958E-003
     3    2.182E+006    8.829E+014    1.199E+014    291  1.000E+000->9.147E-003
```

迭代数不再是 1，残差按预期下降 —— **防护生效**。

---

## 4. 修复 3：补齐求解器参数透传

`EulerianWindTunnelOptions`（风洞工程层）此前**没有**暴露 5 个对稳定性至关重要的参数，
导致 `EulerianWindTunnelCase.Configure()` 只能用 `IncompressibleSolverOptions` 的类默认值，
UI / CLI 无论怎么改都无效。

### 4.1 新增/透传的参数

| 参数 | 默认 | 为什么关键 |
|---|---|---|
| `RelaxP` | 0.3 | 压力显式欠松弛。阶梯式障碍网格上修正量级可达来流的 400 倍，必须靠它压制 |
| `MomentumSweeps` | 2 | 动量预测平滑扫描次数（OpenFOAM `smoothSolver nSweeps`）。原实现**没有透传** |
| `PseudoTransientCourant` | 0.5 | **直接决定 a_P 量级与 rAU**。原实现**没有透传**，UI 改不了 |
| `PressureMaxIterations` | 200 | 病态网格需 1000+（实测第 1 步需 1738 次） |
| `PressureTolerance` | 1e-9 | 原实现没有透传 |

```vb
' EulerianWindTunnelCase.Configure() 现在完整透传
Dim iopts As New IncompressibleSolverOptions With {
    ...
    .RelaxU = opts.RelaxU,
    .RelaxP = opts.RelaxP,
    .MomentumSweeps = opts.MomentumSweeps,
    .PseudoTransientCourant = opts.PseudoTransientCourant,
    .PressureMaxIterations = opts.PressureMaxIterations,
    .PressureTolerance = opts.PressureTolerance,
    .UseGpu = opts.UseGpu, ...}
```

### 4.2 伪瞬态 Courant 的实测影响（city 网格）

| Co | 第 1 步 `max\|U\|` | 第 1 步 `max\|p\|` | 第 1 步连续性 |
|---|---|---|---|
| 0.05 | 3.270e1 | 5.660e8 | 4.151e8 |
| 0.5（默认） | 3.578e1 | 7.032e7 | 3.867e8 |

⇒ Co 越小，压力修正对速度的影响越弱（隐式阻尼越强）。
这是一个**必须能让用户调**的参数，现已暴露。

---

## 5. 验证结果

### 5.1 空通道（教科书基准）—— **PASS**

```
  [K] 通道验证（无障碍）：20x4x4，U∞=30 m/s，nu=1.5E-05
  入口/出口通量：-3.000E+001 / 3.000E+001
  初始 maxAbs(div φ) = 0.0000E+000          ← 散度自由
  初始 Ux 范围 = [3.000E+001, 3.000E+001]   ← 严格均匀

  step   max|U|        max|p|        continuity     pIt
    40   3.000E+001    2.492E-004    4.865E-009     11

  max|Ux − U∞| = 4.379E-003    max|Uy| = 1.660E-004    max|Uz| = 2.139E-009
  连续性误差 = 4.865E-009
  判定：[PASS] 均匀来流保持
```

**判读**：速度误差 0.015%、压力 2.5e-4 Pa（来流动压 550 Pa，即 5e-7 倍）、
连续性误差 4.9e-9 —— 这套 `SIMPLE + 压力修正 + 压力方程` 的实现是**正确**的。

### 5.2 通道 + 实心障碍 —— **稳定**

```
  [K] 通道验证（有实心方块障碍）：20x4x4
  初始 maxAbs(div φ) = 1.8750E+000       ← 障碍阶梯处的离散不平衡（见第 6 节）
  step   max|U|        max|p|        continuity     pIt
    40   3.194E+001    1.615E+001    1.002E-003     17
  连续性误差 = 1.002E-003
```

**判读**：40 步内速度保持在 32 m/s 附近（无发散）、压力 16 Pa（物理量级）、
连续性单调下降到 1e-3。相对 imbalance 只有 6%（30 中 1.875），远低于 city 的 55%，所以能稳住。

### 5.3 既有工程回归 —— **无退化**

```
EulerianFvmSelfTest（重新构建后运行）
  === 45/45 checks passed, failures=0 ===

FlowVizSelfTest
  ALL TESTS PASSED
```

`WindTunnelBox.vbproj` / `WindTunnel.vbproj` / `FVMEngine.vbproj` 三个工程均编译通过。

---

## 6. 未完成：city_seed42 仍发散（根因已定位）

### 6.1 现象

修复 1+2 之后，city 算例（31,734 单元，32.87 m 单元）仍然：

```
  step    max|U|        max|p|       cont           pIt
     1    3.578E+001    7.032E+007    3.867E+008    1738（CPU double，已收敛到 9.6e-3）
     2    2.071E+004    1.732E+011    1.086E+012     449
     3    1.951E+005    3.852E+014    4.130E+012     350
```

第 1 步压力修正达到 **7.0e7 Pa**，而来流动压仅 **ρU²/2 ≈ 551 Pa**——相差 **5 个量级**。

### 6.2 根因：阶梯式障碍产生的"死端单元"

用 `StreamlineDiag deadends` 扫描（判据：**内部**有 x 入流面、但内部没有 x 出流面）：

| 网格 | 单元数 | 无 x 出流面（死端） | 占比 |
|---|---|---|---|
| `city_seed42.eulerian.zip`（32.87 m） | 31,734 | 1,197 | **3.772%** |
| `city_31m`（16.43 m，细化 2×） | 252,542 | 5,071 | **2.008%** |
| `city_cut_31m`（16.43 m + cut-cells） | 259,137 | 12,762 | **4.925%** |

**关键结论：网格加密一倍只把死端比例从 3.77% 降到 2.01%，cut-cells 反而更糟（4.93%）。
分辨率不能根治。**

死端单元的几何含义：`city_seed42` 的建筑在 32.87 m 网格上只占 1~5 个单元，
其"阶梯状"离散边界会在角落处封闭出若干单元——这些单元在流向上**只有进、没有出**。

对这类单元，均匀来流初值会**必然**在单元内累积质量：

```
散度最大单元 #3566：div=-1.7013E+004   中心=(219.1, 625.2, 59.4)
  face 11601 own=3555 nb=3566  signedSf.X=-0.0000E+000  F=0.0000E+000
  face 11629 own=3564 nb=3566  signedSf.X=-0.0000E+000  F=0.0000E+000
  face 11635 own=3565 nb=3566  signedSf.X=-5.6709E+002  F=1.7013E+004  ← 入流
  face 11637 own=3566 nb=3592  signedSf.X= 0.0000E+000  F=0.0000E+000  ← 横向面
  face 11638 own=3566 nb=  -1  signedSf.X= 5.6709E+002  F=0.0000E+000  ← 壁面（下游被封死）
  face 11639 own=3566 nb=  -1  signedSf.X= 0.0000E+000  F=0.0000E+000
```

单元 #3566 的上游是 **+x 面入流 1.7e4**，下游是一个 **+x 壁面**（障碍物侧面），
横向面是 y/z 面（均匀来流下通量为 0）⇒ 1.7e4 m³/s 全部堆在单元里。
这 1.7e4 恰好等于单个 x 面的通量（占 55%）。

1,197 个这样的单元 ⇒ 全局虚假连续性误差 ≈ 4e8（即入口总流量 1.742e7 的 23 倍量级）。

### 6.3 为什么"把死端速度置 0"不行（已实测否定）

我实现并测试了这个思路，**失败并已回退**：

| | 空通道 | 通道+障碍 |
|---|---|---|
| 投影前 初始 `maxAbs(div φ)` | 0.0 | 1.875 |
| 投影后 初始 `maxAbs(div φ)` | **1.875**（被误判！） | 1.875 |
| 投影后 `Ux` 范围 | [**0**, 30] | [**0**, 30] |

原因：把死端单元的 `u_x` 置 0 之后，**上游内部面仍在输运通量 1.7e4**，
散度并不会下降；而且"有入流无出流"的判据会把入口/出口单元误判（它们在边的另一侧没有内部面）。

⇒ 正确做法必须**同时**处理"上游→死端"的那张面（把它当内部壁面），
或者直接在压力方程层面对退化单元做特殊处理。这属于**重构级**改动，
已超出本次"找出并修复明确 bug"的范围，故留作后续项。

### 6.4 压力修正本身的正确性（已单独验证）

`StreamlineDiag pcorr` 用**完全收敛**的压力方程（maxIter=20000, tol=1e-12）检验修正方向：

```
  div(phiHbyA)  maxAbs = 1.5368E+004           ← 压力方程右端
  反解压力方程：Pcg/diagonal [CPU/f64] 3695 it, residual 1.000E+000 -> 9.234E-013 converged
  修正后 div(φ)  maxAbs = 1.0386E-002
  修正前 div(phiHbyA) maxAbs = 1.5368E+004
  → 下降倍数 = 1.480E+006                       ← 修正方向与公式完全正确
  若把修正反号 div(φ) maxAbs = 3.0737E+004      ← 反号明显更差，证明符号无误
```

**结论**：`φ = phiHbyA − rAU·Δ·(p_Q − p_P)` 与 `+div(rAU∇p) = div(phiHbyA)` 的符号/公式都对。
问题不在公式，而在**该方程的源项量级**（死端单元造成）与**求解精度**（需要 3695 次迭代，
而生产配置默认 200 次）。

### 6.5 建议的后续修复方向（按优先级）

1. **把 `PressureMaxIterations` 默认从 200 提到 ≥ 2000**，
   并让"未收敛"成为一个**显式告警**而不是静默继续。
   实测第 1 步需 1738 次、单独验证需 3695 次。
2. **在网格生成阶段消除死端单元**：
   `OctreeCutCellGenerator` 可以在输出前做一遍扫描，
   把"有入流无出流"的单元与其上游面一起标记为固体（等价于把阶梯角落填实），
   或在 `--cut-cells` 模式下修正 `PolyhedronClipper` 使切割后的单元与邻居共享完整面。
3. **在压力方程层处理退化单元**：对 `a_P` 低于阈值的单元
   （`rAU > 某上限`）采用局部参考压力或直接施加硬约束 `p_P = 0`，
   避免 1e-11 量级的对角元把条件数推到 1e17。
4. **`rAU` 限幅**：`rAU = min(1/a_P, C · Δt_local/ρ)`，
   物理上等价于限制局部时间步推进的 Courant 数。
5. **对 city 这类算例，把 `PseudoTransientCourant` 降到 0.05~0.1**（参数已暴露），
   单步修正幅度会下降到 1/5~1/10（实测 Co=0.05 时第 1 步 `max|U|` 从 35.8 降到 32.7）。

---

## 7. 关于"更精细的网格"的实测数据

按要求生成了多档分辨率的网格（`G:\Moira\test\mesh\`）：

| 文件 | resolution | 目标尺度 | 实际单元边长 | 单元数 | zip 大小 | 用时 |
|---|---|---|---|---|---|---|
| `city_seed42_current.eulerian.zip` | 0.05 | 71.4 m | 32.87 m | 31,734 | 1.4 MB | 2.4 s |
| `city_31m.eulerian.zip` | 0.02194 | 31.3 m | 16.43 m | **252,542** | 6.7 MB | 4.1 s |
| `city_cut_31m.eulerian.zip` | 0.02194 + cut-cells | 31.3 m | 16.44 m | **259,137** | 25 MB | 7.0 s |
| （尝试）depth-7 | 0.0109 | 15.6 m | 4.11 m | **16,173,798** | — | 174 s |

**重要发现**：
- `OctreeCutCellGenerator` 的单元边长是 `maxExtent / 2^depth`，**按最大边量化**。
  域 z 向只有 2003.2/362.8 = 5.5 倍跨度，所以"要 15.6 m"实际得到 4.11 m、**1617 万单元**
  —— 比预期多 8 倍，不实用（已删除该产物）。
- 想要真正的"各向异性"精化（x/y 细、z 粗）需要改网格生成器的深度策略，
  当前实现只能给出立方体单元。
- **252k 网格在本机（性能受限环境）单个外迭代超过 10 分钟**，
  不适合作为回归测试网格；建议用 `channel` 基准做快速验证。

---

## 8. 改动清单

| 文件 | 改动 |
|---|---|
| `src\FVMEngine\Eulerian\IncompressibleSolver.vb` | 新增 `RefreshInitialFlux()`、`ScanDeadEndCells()`、`DeadEndCellCount`；补充时序陷阱文档注释 |
| `src\FVMEngine\Eulerian\EulerianWindTunnelCase.vb` | `Configure()` 末尾调用 `RefreshInitialFlux()`；新增并透传 `RelaxP` / `MomentumSweeps` / `PseudoTransientCourant` / `PressureTolerance` |
| `src\FVMEngine\Eulerian\EulerianFvmCompute.vb` | `TrySolve` 增加"实质下降校验"，GPU 假收敛时回落 CPU double |
| `src\FVMEngine\Eulerian\EulerianOperators.vb` | （临时诊断已全部回退，无净改动） |
| `test\StreamlineDiag\` | 新增 `ChannelTest.vb` 教科书基准；`Program.vb` 新增 `channel` / `deadends` / `divergence` / `momentumdiag` / `pcorr` / `paramscan` 子命令与通用覆盖参数；`Support.vb`、`IlDump.vb` |

---

## 9. 复现步骤

```powershell
dotnet build G:\Moira\test\StreamlineDiag\StreamlineDiag.vbproj -c Debug
$exe = 'G:\Moira\test\StreamlineDiag\bin\Debug\StreamlineDiag.exe'
$zip = 'G:\Moira\src\ZMesh\CityBlender\samples\city_seed42.eulerian.zip'

# [验证 1] 空通道教科书基准 -> PASS
& $exe channel 20 4 4 30 1.5e-5 40 true 200 1e-9 0.3 0.7 2

# [验证 2] 通道 + 障碍 -> 稳定
& $exe channel 20 4 4 30 1.5e-5 40 true 2000 1e-9 0.3 0.7 2 obstacle

# [修复 1 验证] 初始通量散度必须为 0，进/出口净通量必须等值反号
& $exe divergence $zip

# [修复 2 验证] GPU 不再 1 次迭代假收敛
& $exe pressurediag $zip 10 true --priter=2000

# [未完成项定位] 死端单元占比
& $exe deadends $zip
& $exe deadends 'G:\Moira\test\mesh\city_31m.eulerian.zip'

# [压力修正正确性] 修正前后散度下降 1e6 倍
& $exe pcorr $zip 1 false

# [回归] 既有自测
dotnet build G:\Moira\test\EulerianFvmSelfTest\EulerianFvmSelfTest.vbproj -c Debug
& 'G:\Moira\test\EulerianFvmSelfTest\bin\Debug\EulerianFvmSelfTest.exe'
```

日志：`G:\Moira\test\logs\` 下
`verify_empty_channel.txt`、`verify_obstacle_channel.txt`、`verify_gpu_guard.txt`、
`probe_divergence2.txt`、`probe_momentum_diag.txt`、`probe_pcorr_fixed.txt`、
`pd_deadend_fix.txt`、`conv_*.txt`、`chan_*.txt`。

---

## 10. 诚实声明

- **本次修复的是"确定的 bug"**：时序错误（通量未同步）、GPU 假收敛、参数未透传。
  这三项都有明确的前后对比数据，且修复后教科书基准达到机器精度。
- **city_seed42 的数学发散没有被修复**。我定位到了具体机制（死端单元，
  已给出逐面通量证据、占比统计、以及"加密无效"的实测），
  但它需要的改动（压力方程退化单元处理 / 网格生成阶段消除死端）
  属于重构级，我没有在本次完成。
- 我**没有**用"调参掩盖"的方式让 city 算例跑起来（例如把所有修正乘以一个很小的松弛因子），
  因为那只会隐藏问题而不是解决问题；我只补齐了参数透传，让用户能自己试。
- 第 6.3 节记录的"死端速度置 0"方案是**我实现并实测否定**的，
  已在源码中回退为只读诊断 `ScanDeadEndCells()`，并把这个失败结论写入注释，
  避免后来者重复踩坑。
