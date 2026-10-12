# WindTunnelBox「流线图」空白问题 —— 诊断报告

> 诊断工具：`G:\Moira\test\StreamlineDiag\`（命令行，全程启用 CUDA 加速）
> 原始日志：`G:\Moira\test\logs\`
> 结论可复现命令见本文末尾「复现步骤」。

---

## 0. 一句话结论

**流线图空白是「直线流线过滤」真的把全部流线滤掉了，但根本原因不是流线追踪器，也不是网格转换：
`city_seed42.eulerian.zip` 的网格只有 61×49×11 个单元（单元边长 32.87 m，覆盖 2 km × 1.6 km 的域），
城市建筑只占 1~5 个单元，风洞解在前 2 步几乎就是均匀来流（全场 |U| 14~47 m/s、最大横向偏移仅 1.16 格），
于是全部 39 条流线的 `Straightness` 最大只有 0.0111，全部低于 `StreamlineSet.DefaultStraightness = 0.05`，
被 `HideStraightStreamlines = True` 滤掉 → `BuildLines()` 返回 0 条线段、`StreamlineAnimator.ParticleCount = 0` → 画布全空。**

同时发现两个**独立的严重问题**（它们在流线可见性之外，但会真正毁掉模拟结果）：

1. **FVM 不可压求解器在该网格上第 2 步就发散**：`max|U|` 从 47 m/s → 4.19e1 → 1.8e4 → 8.6e7 → 2.4e12 m/s，
   连续性误差 4.2e8 → 1.3e12 → 5.1e19。任何 relaxation / 湍流模型 / GPU 开关都救不回来。
2. **GPU（CUDA float32）压力方程求解器会"假收敛"**：压力方程实际需要 ~285 次 PCG，
   GPU 路径报 `1 it, residual 1.000E+000 -> 1.000E+000`（解**完全没变**），外层却当作已完成一次压力修正。

---

## 1. 诊断方法（做了什么）

`StreamlineDiag` 命令行程序**逐字复刻**了 WindTunnelBox 的完整数据链路（无 GUI）：

| 层 | 复刻对象（源码位置） | 说明 |
|---|---|---|
| 网格 | `src\EulerianMesh\Program.vb` → `ZMesh\Eulerian\EulerianPipeline.vb` | STL → `*.eulerian.zip`（GPU 打开） |
| 求解 | `src\WindTunnelBox\Simulation\WindTunnelSimulationRunner.vb` + `EulerianWindTunnelCase` | 参数 = `FormWindTunnel.BuildOptions` 的 UI 默认值（U∞=30 m/s、ρ=1.225、ν=1.5e-5、标准 k-ε、Ti=2%、GPU 开） |
| 重采样 | `src\WindTunnelBox\Simulation\EulerianFieldSnapshot.vb` 的 `EulerianFieldSampler` | **逐字复制**，含 `ConfigureGrid` / `BuildNearestMap` |
| 流线 | `src\CDFDxCanvas\Rendering\Streamlines.vb` 的 `StreamlineTracer` **（工程引用真身，非复制）** | `Trace` / `Straightness` / `MinCurvature` / `BuildLines` / `StreamlineAnimator` |
| 适配器 | `src\WindTunnelBox\Rendering\WindTunnelFlowField.vb` 的 `WindTunnelFlowField` | 逐字复制 |

命令行子命令：

```
StreamlineDiag mesh        <eulerian.zip>                       仅网格诊断
StreamlineDiag convert     <model.stl> <out.zip> [resolution]    STL → Eulerian 网格（GPU）
StreamlineDiag solve       <eulerian.zip> [steps] [--snap=a,b]   求解 + 流线诊断
StreamlineDiag compare     <eulerian.zip> [steps] [only-new]     求解稳定性对照矩阵（19 组参数）
StreamlineDiag pressurediag <eulerian.zip> [steps] [gpu]         逐步推进 + 压力方程线性求解探针
StreamlineDiag resampler   <eulerian.zip>                        AutoConfigure / 重采样方式探针
StreamlineDiag il          <dll> <类型全名> <方法名>              IL 反汇编（核对编译产物与源码）
```

---

## 2. 证据链

### 2.1 网格层：**没有问题**（STL → Eulerian 转换完全正确）

`StreamlineDiag convert city_seed42.stl`（默认 resolution=0.05，GPU 开）的产物与工作区里已有的
`city_seed42.eulerian.zip` **逐字节同构**：

| 指标 | 现场重算 | 已有 zip | 结论 |
|---|---|---|---|
| points | 35474 | 35474 | 一致 |
| faces | 98925 | 98925 | 一致 |
| cells | 31734 | 31734 | 一致 |
| boundary faces | 7446 | 7446 | 一致 |
| bounds | [-500.8, -100.04, -93.70] … [1502.4, 1500.53, 269.10] | 同 | 一致 |
| patches | symmetry-z0/inlet/ground/wall/outlet/top/symmetry-z1 | 同 | 一致 |

网格几何量：

```
Eulerian FVM mesh: cells=31734, faces=98925 (internal=91479, boundary=7446), patches=7
  volume: total=1.12654E+09, min=35499.6, max=35499.6, ratio=1      ← 单元体积完全均匀
  csr: nnz=214692 (avg 6.77 / cell)                                  ← 纯六面体 7 点模板
  non-orthogonality: avg=0.00 deg, max=0.00 deg                      ← 纯正交笛卡尔
计算域 = 2003.2 × 1600.57 × 362.806 m
等效立方单元边长 = 32.866 m（min = p05 = p50 = p95 = max，完全均匀）
  → 约 61.0 × 48.7 × 11.0 个单元
```

**关键事实**：`OctreeCutCellGenerator` 实际产出的是**纯均匀笛卡尔网格**（无任何八叉树细分/切割单元），
单元边长 32.87 m。城市最大建筑高 178.4 m ⇒ **最高的楼也只有 5.4 个单元**，普通楼房只有 1~2 个单元。
这是"解几乎均匀"的物理根源。

> `resolution` 语义提示：`EulerianPipeline.Convert` 里 `res = 模型对角线 × resolution = 1427 m × 0.05 = 71.36 m`，
> 但八叉树把它归整到 2 的幂次细分（domain max extent 2003.2 m / 2^6 = 31.3 m → 实际 32.87 m）。
> 所以想提高分辨率不能只调 `resolution`，要看到这条"2 的幂次"量化规则。

### 2.2 重采样层：零阶重采样确实制造了"分片常数"场

`EulerianFieldSampler`：

```
重采样结构格 = 61 × 49 × 12（35868 节点，h = 32.8656 m）
域外掩膜     = 0 / 35868（0% 判为固体/域外）
结构格间距 h = 32.8656 m，网格 p50 单元边长 = 32.8656 m → 每个单元平均覆盖 1.00 个结构格节点
```

**映射块状度**（相邻结构格节点是否落在同一个网格单元里）：

| 方向 | 同单元比例 |
|---|---|
| +x | 41.94% |
| +y | 33.69% |
| +z | **0.12%** |
| 平均 | **25.25%** |

即平均有 **1/4 的相邻结构格节点速度完全相同**。`EulerianFieldSampler` 用的是
`EulerianToCartesianResampler.NearestCell`（**最近单元中心，零阶**），
而 `StreamlineTracer.Sample` 内部做**三线性插值**——在一个"所有 8 个角点取自同一单元"的
结构格方块内，三线性插值退化为常数，`RK2` 的搜索方向不变 ⇒ **该方块内流线严格是直线**。
只有跨方块的台阶处才会拐折。

`src\WindTunnelBox\Simulation\EulerianFieldSnapshot.vb` 里的注释把这一点当成优点：

> 采样策略：最近单元中心（零阶）。对分片常数（cell-centered）单元场是**精确**的，不引入插值误差，
> 代价是单元边界呈阶梯状。

**这个说法对显示用的标量（热图）成立，对"要被插值/微分的速度矢量和流线追踪"是错的**——
流线追踪的输入必须是连续场，零阶采样恰恰把速度场打成了分片常数。

### 2.3 流线层：**决定性证据** —— 前 2 步全部流线都低于阈值

`StreamlineDiag solve city_seed42.eulerian.zip 2 --snap=1,2`（零阶路径，即 WindTunnelBox 实际路径）：

| step | 种子数 | 有效流线 | 总线数 | Straightness min / median / max | 阈值 | **过滤后可见线段** | 光点粒子 |
|---|---|---|---|---|---|---|---|
| **1** | 39 | 39 | 2141 | 0 / 0.00446 / **0.01107** | 0.05 | **0** | **0** |
| **2** | 39 | 33 | 2012 | 0.00099 / 0.00830 / **0.01686** | 0.05 | **0** | **0** |

最弯的一条（step 1）：

```
straightness = 0.0110662   （阈值 0.05）
顶点数 = 86，弧长 = 59.5 格
种子点位移：Δy = 0.9155（max|Δy| = 1.161），Δz = 0.0027（max|Δz| = 0.0039）
```

**一条横穿整个 60 格计算域的流线，横向只偏了 1.16 个结构格。** 这就是"直线"。

第 3 步开始流线才出现曲率（max straightness = 0.79），**但那时求解器已经发散**
（`max|U| = 1.83e4 m/s`），得到的"曲线"是数值噪声，不是物理。

### 2.4 对照实验：不是"零阶重采样"的锅，是网格/流场本身太均匀

用**同一帧**（最终帧）、同一个 `StreamlineTracer`，只把重采样换成
**单元中心反距离平方插值**（连续场，不是分片常数）：

| 重采样方式 | 种子数 | 有效流线 | Straightness median / max | 阈值 | **过滤后可见线段** |
|---|---|---|---|---|---|
| 最近单元（零阶，当前实现） | 39 | 33 | 0.00830 / **0.01686** | 0.05 | **0** |
| 单元中心反距离平方插值（连续） | 39 | 33 | 0.00784 / **0.01614** | 0.05 | **0** |
| 最近单元 + 3 轮 Jacobi 平滑 | 39 | 35 | 0.00553 / **0.01135** | 0.05 | **0** |
| 最近单元，RK2 步长 0.7 → 0.2 | 39 | 33 | 0.00830 / **0.01668** | 0.05 | **0** |
| 最近单元，种子步长 4 → 8 | 14 | 11 | 0.00613 / **0.01122** | 0.05 | **0** |

**换成连续插值、做平滑、把步长缩到 1/3.5、把种子加密 —— 四种手段都无法把 Straightness 推过 0.05。**
⇒ **主导因素是"速度场本身近乎均匀"（网格太粗），零阶重采样只是雪上加霜
（它额外保证"方块内严格零曲率"）。**

旁证：把 RK2 步长从 0.7 缩到 0.2（顶点数 ×3.5），`Straightness` 几乎不变
（0.01686 → 0.01668）⇒ 几何真的是折线，追踪器没有丢信息。

### 2.5 求解器层：第 2 步起发散（这是更严重的问题）

`StreamlineDiag pressurediag city_seed42.eulerian.zip 15 true` 逐步输出：

```
  step    max|U|        max|p|       cont           pIt  pRes(init->final)      RauCell(max)
     1    4.714E+001    1.465E+001    4.179E+008    200  1.000E+000->2.286E-002   1.272E+002
     2    4.188E+001    6.706E+007    7.323E+008    200  1.000E+000->3.246E-001   1.698E-001
     3    1.827E+004    1.343E+011    1.342E+012    200  1.000E+000->5.045E-002   1.543E-004
     4    2.550E+005    2.316E+014    5.353E+012    200  1.000E+000->1.213E-001   4.062E-006
     5    8.526E+007    4.613E+017    2.623E+015    200  1.000E+000->6.537E-002   2.327E-007
     6    5.651E+008    7.710E+020    1.483E+016    200  1.000E+000->1.226E-001   1.966E-008
     7    2.132E+011    1.589E+024    6.011E+018    200  1.000E+000->6.331E-002   1.978E-010
     8    2.387E+012    1.589E+024    9.629E+019      1  1.000E+000->1.000E+000   4.190E-012
     9    1.916E+012    1.589E+024    7.527E+019      1  1.000E+000->1.000E+000   1.301E-012
     …
```

三个致命信号：

1. **`max|U|` 第 3 步就跳出物理量级**（1.8e4 → 8.6e7 → 2.4e12 m/s），第 7 步 `max|p|` 已达 1.6e24 Pa。
2. **`RauCell(max) = 1/a_P` 从 1.27e2 单调崩到 1e-14** ⇒ 动量方程对角元 `a_P` 从 ~8e-3 涨到 ~1e14，
   正反馈：`a_P↑ → rAU↓ → 压力修正爆炸 → 速度爆炸 → 源项↑ → a_P↑`。
3. **第 8 步起 GPU 压力求解器"假收敛"**：`pIt` 从 200 突然变成 **1**，
   `pRes 1.000E+000 -> 1.000E+000`（残差一动不动）⇒ **压力修正量 ≈ 0，SIMPLE 的空转**。

`StreamlineDiag pressurediag … 20` 重建压力方程后逐一求解：

```
_rauCell  范围 = [9.2831E-017, 8.7058E-014]      （= 1/a_P）
_rauFace  范围 = [9.4554E-017, 8.7058E-014]，均值 |rAU| = 6.1365E-016
→ 动量方程对角元 a_P ≈ [1.1487E+013, 1.0772E+016]

压力方程 reconstruction:
  |b| (L2) = 1.415682E+016
  对角元范围 = [-1.419E-011, -5.294E-014]          ← 对角元 ~1e-11，比 b 小 27 个量级

  maxIter=1     → GPU/f32  1 it,  1.000E+000 -> 1.000E+000  NOT converged
  maxIter=2000  → GPU/f32  1 it,  1.000E+000 -> 1.000E+000  NOT converged     ← 迭代上限完全无效
  maxIter=5     → CPU/f64  5 it,  1.000E+000 -> 2.669E+000  NOT converged
  maxIter=500   → CPU/f64  289 it, 1.000E+000 -> 9.843E-003  converged        ← CPU 能解，需要 289 次
```

手工 PCG（CPU double，纯 `FvMatrix` API）残差历史：前 5 次残差**先涨到 2.67** 才慢慢回落
（iter=200 时 0.153）。这就是 `1/a_P` 是 **1e-11 量级对角元 + 1e16 右端项**的超病态系统。

**两个独立的求解器缺陷**：

- **GPU 路径假收敛**（`src\FVMEngine\Eulerian\EulerianFvmCompute.vb` 的 `TrySolve` / `GpuPcg`）：
  GPU 用 float32 内残差做停止判据，最后在 double 下重算"权威残差"，
  但 `rpt.Converged = rpt.FinalResidual <= tolerance OrElse (relTol > 0 AndAlso …)`
  只报告、不**否决**；`usedIter = 1` 且残差纹丝不动（说明 `pAp`/`rho` 在 float32 下
  被 1e-11 对角元的巨大条件数吃掉，`alpha` 实际上被算成 ~0），外层却接受了这次"求解"。
  **修复要点：`TrySolve` 必须把 `FinalResidual` 与 `InitialResidual` 做实质下降校验
  （例如要求下降 ≥ 某比例），否则 `Return False` 回落 CPU double。**
- **CPU 路径迭代上限不足**：`Options.PressureMaxIterations` 默认 200，而该方程需要 285+ 次；
  且 `RelaxP=0.3` 显式松弛会让"未收敛的压力修正"继续被施加。测试里把上限提到 2000 后
  CPU 依然发散（因为 rAU 已经崩了），说明**病态本身要靠限制 rAU / 更好的伪瞬态与松弛解决**。

### 2.6 参数对照矩阵：**没有任何参数组合能救**

`StreamlineDiag compare city_seed42.eulerian.zip 100`（19 组，每组 100 步）：

```
① GUI 默认（k-ε, RelaxU=0.7, L=0.05, 伪瞬态 Co=0.5, GPU）   max|U|=3.531E+013  cont=9.029E+020
② 层流                                                     max|U|=3.527E+013  cont=9.031E+020
③ 关闭 GPU（CPU double）                                    max|U|=NaN         cont=NaN
④ 湍流长度尺度 = 域尺度自动值（≈115 m）                      max|U|=3.531E+013  cont=9.029E+020
⑤ 湍流长度尺度 = 2000 m                                     max|U|=3.527E+013  cont=9.030E+020
⑥ 速度欠松弛 0.3                                            max|U|=NaN         cont=NaN
⑦ 速度欠松弛 0.9                                            max|U|=NaN         cont=NaN
⑧ 压力欠松弛 0.1                                            max|U|=5.887E+013  cont=1.895E+021
⑨ 压力欠松弛 0.7                                            max|U|=6.578E+012  cont=2.017E+020
⑩ 关闭伪瞬态（Co=0）                                        max|U|=NaN         cont=NaN
⑪ 伪瞬态 Co=0.1                                             max|U|=3.307E+012  cont=1.350E+020
⑫ 来流 5 m/s（低雷诺数）                                     max|U|=NaN         cont=NaN
⑬ 稳态 → 瞬态 PIMPLE                                        max|U|=NaN         cont=NaN
⑭ 对流格式（迎风/其它）                                      max|U|=3.531E+013  cont=9.029E+020
⑮ CPU double + 压力迭代 2000                                max|U|=NaN         cont=NaN      pIt=2000
⑯ GPU + 压力迭代 5000                                       max|U|=2.522E+013  cont=6.044E+020 pIt=1
⑰ CPU + 压力迭代 2000 + RelaxU=0.5                          max|U|=NaN         cont=NaN      pIt=2000
⑱ CPU + 压力迭代 2000 + 压力容差 1e-6                        max|U|=NaN         cont=NaN      pIt=2000
⑲ CPU + 压力迭代 2000 + 层流                                max|U|=NaN         cont=NaN      pIt=2000
```

跑 100 步后，**流线统计里 `min=0` 或整场 NaN**：即使有曲线，也是发散的数值噪声。
唯一还"物理"的时刻是第 1 步。

### 2.7 附带发现：`EulerianToCartesianResampler.AutoConfigure()` 对该网格返回 1×1×1（现象已确认，根因未锁定）

`src\WindTunnel\WindTunnelRunner.vb` 的 `--format vti` 路径用 `AutoConfigure()`：

```
CLI AutoConfigure: 1×1×1, h=32.8656 m, origin=(-500.8, -100.036, -93.7015)
Resample(velocity) 用时 0.00 s，输出 3 个 float        ← 只有 1 个节点 × 3 分量
Resample 输出非零项 = 0                                 ← 全是 0
```

`StreamlineDiag resampler <zip>` 的专项探针（`logs\run_resampler_probe.txt`）确认：

```
网格：cells=31734, totalVolume=1.12654E+09
extent = (2003.2, 1600.57, 362.806)
平均单元尺度 h0 = (V/N)^(1/3) = 32.8656

构造后（AutoConfigure 之前）：1x1x1, h=1, origin=(0, 0, 0)
AutoConfigure()（默认 maxCells=4000000）→ 1x1x1, h=32.8656, origin=(-500.8, -100.036, -93.7015)
AutoConfigure(cellSize=0, maxCells=1000000/4000000/10000000) → 全部 1x1x1
AutoConfigure(cellSize=8)  → 1x1x1, h=8
AutoConfigure(cellSize=16) → 1x1x1, h=16
AutoConfigure(cellSize=33) → 1x1x1, h=33

用反射读私有字段：_SpacingX=32.8656、_OriginX=-500.8 都已正确写入，
                 但 _Nx=_Ny=_Nz=1（未写入）

对照实验（同一进程内，逐行复算 AutoConfigure 的源码）：
    逐行复算（同源码）：61x49x12  (h=32.8656)
    直接给属性赋值后读回：61x49x12
    AutoConfigure(cellSize=逐行复算的 h=32.8656) -> 1x1x1
```

也就是说：**同一份源码逐行手写会得到 61×49×12，直接给 `Nx/Ny/Nz` 属性赋值也能读回 61×49×12，
但调用编译进 `CFDEngine.dll` 的 `AutoConfigure` 只会得到 1×1×1**（`h`/`Origin` 却写对了）。
`StreamlineDiag il CFDEngine.dll Moira.CFDEngine.Snapshot.EulerianToCartesianResampler AutoConfigure`
（自写的 IL 反汇编子命令）显示 IL 与源码一致（`Floor(extent.X/ldloc.3) → conv.i4 → +1 → Max(1,…)`）。

**结论：这是一个"现象确凿、根因未锁定"的缺陷。** 无论原因是什么，
`WindTunnel.vbproj` 的 CLI `--format vti` 路径产出的重采样数据是**无用的**（1 个节点、全 0）。
`WindTunnelBox` 已经因为历史上踩过同一个坑（见 `EulerianFieldSnapshot.vb` 的 ★ 注释）
而绕开 `AutoConfigure` 自己实现 `ConfigureGrid`（本报告第 2.2 节的 61×49×12 就是它算出来的），
所以这条只在 CLI 路径上暴露。

> 建议：先用一次 `dotnet build src\CFDEngine\CFDEngine.vbproj -c Debug -t:Rebuild`
> 强制全量重编译再复测（本诊断程序观测到 `CFDEngine.dll` 的反射字段名是 `_Nx`，
> 而源码里是自动属性 + `Nx`，两者风格不一致，**不排除构建产物与当前源码不同步**）。
> 若重编译后仍为 1×1×1，则需要在此方法内部加临时日志进一步定位。

---

## 3. 问题定位总结（按影响排序）

| # | 问题 | 位置 | 影响 |
|---|---|---|---|
| **P1** | 网格太粗 + 建筑不可分辨 ⇒ 解近乎均匀 ⇒ 全部流线 `Straightness < 0.05` ⇒ 被过滤 | `ZMesh\Eulerian\MeshGenerators\OctreeCutCellGenerator.vb`（恒为均匀网格）+ 使用时选的 `resolution` | **流线图全空的直接原因** |
| **P2** | FVM 不可压求解器（SIMPLE + 伪瞬态）该网格上第 2~3 步发散；`rAU=1/a_P` 正反馈崩溃 | `FVMEngine\Eulerian\IncompressibleSolver.vb`（`MomentumPredictor` / `PressureCorrection` / `BuildPseudoTransientCoefficient`） | 任何可视化都无意义 |
| **P3** | GPU float32 压力求解器"假收敛"（1 次迭代、残差不变、不回落 CPU） | `FVMEngine\Eulerian\EulerianFvmCompute.vb` `TrySolve`/`GpuPcg`；`IncompressibleSolver.PressureCorrection` 的 `PressureMaxIterations=200` 也不够（需 285+） | 压力修正=0，加速发散 |
| **P4** | 零阶（最近单元）重采样把速度场打成"分片常数"，保证方块内流线严格为直线；25.25% 的相邻结构格节点速度完全相同 | `WindTunnelBox\Simulation\EulerianFieldSnapshot.vb` 的 `Sample` | 加剧 P1；速度场不该用零阶重采样 |
| **P5** | `Straightness` 用"顶点到首尾弦的最大垂距 / 弦长"定义，对"S 形 / 对称弧"会低估（弦两侧偏差互相掩盖）；`HideStraightStreamlines = True` 又是默认 | `CDFDxCanvas\Rendering\Streamlines.vb` | 次要，但对"看起来该有曲线却没有"有贡献 |
| **P6** | GUI 未暴露 `TurbulenceLengthScale`，用类默认 0.05 m（域尺寸 2000 m，相差 4 个量级） | `WindTunnelBox\FormWindTunnel.vb` `BuildOptions` vs `WindTunnel\CliOptions.vb` 的 `AutoTurbulenceLength` | 湍流场完全不合理（但实测不是发散主因） |
| **P7** | `AutoConfigure()` 对真实网格返回 1×1×1（现象确凿、根因未锁定） | `CFDEngine\Snapshot\EulerianSnapshotRecorder.vb` | WindTunnel CLI 的 `--format vti` 输出无效 |

---

## 4. 建议的修复顺序

### 立刻可验证（不改源码，改 UI 就能看到流线）
1. `WindTunnelCanvas.HideStraightStreamlines = False`
   （或把 `StreamlineSet.DefaultStraightness` 从 0.05 暂时改到 0.002）。
   诊断程序已证明：**step 1 时 `BuildLines` 会从 0 条线段变成 2141 条**。
   → 立刻能确认"空白 = 被过滤"，但画出来的是几乎笔直的线。

### 真正解决问题
2. **提高网格分辨率到建筑可分辨**（建筑高 178 m ⇒ 单元 5~10 m 量级）。
   注意 `OctreeCutCellGenerator` 的目标深度是
   `Ceiling(Log(maxExtent/resolution, 2))`、单元边长 = `maxExtent / 2^depth`：
   扩域 maxExtent = 2003 m 时 depth=8 才是 7.8 m，而均匀网格单元数 ≈ 256×205×46 ≈ **2.4e6**
   （当前 3.17e4，即 76 倍），depth=9 更是 1.9e7 —— 需要自适应/ROI 细化或分段计算。
   **这是让流线真的弯曲的前提。**
3. **修压力修正的病态**：限制 `rAU` 的上界（如 `min(1/a_P, C·V/dt)`）、
   用 Aitken / 更小的 `RelaxP`、或改成耦合式（coupled）压力-速度求解；
   并让 `PressureMaxIterations` 至少 1000 且**未收敛时报错/降级而不是静默接受**。
4. **GPU 路径加"实质下降"校验**：`TrySolve` 里若 `FinalResidual >= 0.5 * InitialResidual`
   （或 `usedIter <= 1` 且残差未变）就 `Return False` 回落 CPU double。
5. **速度场重采样改连续插值**：把 `EulerianToCartesianResampler` 的
   `NearestCell` 换成"单元中心反距离/三线性插值"（标量显示可保留零阶，
   矢量场必须连续）。诊断已给出对照数据（2.4 节）。
6. **修 `AutoConfigure()`**（P4），或像 WindTunnelBox 一样绕开它。
7. `Straightness` 改用"弧长/弦长比 + 方向变化累积"的组合指标，
   避免 S 形曲线被误判为直线。

---

## 5. 复现步骤

```powershell
# 1. 构建诊断程序（引用 CDFDxCanvas / CFDEngine / FVMEngine / ZMesh 源码工程）
dotnet build G:\Moira\test\StreamlineDiag\StreamlineDiag.vbproj -c Debug

$exe = 'G:\Moira\test\StreamlineDiag\bin\Debug\StreamlineDiag.exe'
$zip = 'G:\Moira\src\ZMesh\CityBlender\samples\city_seed42.eulerian.zip'

# 2. 网格诊断（GPU 注册）
& $exe mesh $zip

# 3. STL → Eulerian 网格（复现已有 zip）
& $exe convert 'G:\Moira\src\ZMesh\CityBlender\samples\city_seed42.stl' `
              'G:\Moira\test\mesh\city_seed42_current.eulerian.zip'

# 4. 流线诊断：前 2 步全部流线 straightness < 0.05 ⇒ 0 条可见线段
& $exe solve $zip 2 --snap=1,2

# 5. 逐步发散 + 压力方程线性求解探针
& $exe pressurediag $zip 15 true

# 6. 19 组参数对照矩阵（约 7 分钟）
& $exe compare $zip 100

# 7. AutoConfigure / 重采样探针
& $exe resampler $zip

# 8. IL 反汇编（核对编译产物与源码）
& $exe il 'G:\Moira\src\CFDEngine\bin\Debug\net10.0\CFDEngine.dll' `
         'Moira.CFDEngine.Snapshot.EulerianToCartesianResampler' 'AutoConfigure'
```

对应日志：`G:\Moira\test\logs\` 下的
`run_convert_stl.txt`、`run_solve_final.txt`、`run_pressure_step_trace.txt`、
`run_pressure_diag.txt`、`run_compare_cases.txt`、`run_compare_pressure_iterations.txt`、
`run_resampler_probe.txt`
（以及每次运行的完整 `streamline_diag_*.log`）。

---

## 6. 诊断程序的已知局限

- 「单元中心反距离平方插值」对照组只在**同一帧**上与零阶做定性比较；
  它用的 8 邻域是 `x + {0,h}³`（全部向正方向偏移），不是严格对称模板，
  因此**不能**把它的绝对 Straightness 当作"理想重采样"的数值。
  结论（两种重采样都远低于 0.05 阈值）在 step 1/2 上是一致的，方向性可信。
- `compare` 矩阵里 CPU double 的用例结果为 NaN，
  是因为该网格上 CPU 路径也会发散（`pressurediag` 已验证 CPU 需要 285 次 PCG 且
  `rAU` 已病态），不是"CPU 路径本身没跑"。
- GUI 的 `Timer` 渲染节拍、DirectX 通道（`LoadConnections`）本身未测；
  但 `WindTunnelCanvas.RefreshField` 传给 `LoadConnections` 的 `lines` 数组
  在 step 1/2 时长度确实是 0，所以画布空白与渲染后端无关。
