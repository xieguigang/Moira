# 基础 CFD 引擎 — 发酵罐搅拌模拟

> 使用 VB.NET 基础数学函数实现的三维计算流体力学（CFD）引擎，面向**原理学习与教学**。
> 基于 Jos Stam 的 Stable Fluids 算法，模拟发酵罐内旋转搅拌器驱动的液体流动。

---

## 目录

1. [项目简介](#项目简介)
2. [文件结构](#文件结构)
3. [物理原理](#物理原理)
4. [数值方法](#数值方法)
5. [搅拌器模型](#搅拌器模型)
6. [编译与运行](#编译与运行)
7. [演示输出解读](#演示输出解读)
8. [API 使用指南](#api-使用指南)
9. [参数调优指南](#参数调优指南)
10. [扩展思路](#扩展思路)

---

## 项目简介

本项目实现了一个**基础可用**的三维 CFD 引擎，具有以下特点：

| 特性 | 说明 |
|------|------|
| **算法** | Stable Fluids（Jos Stam, 1999）—— 无条件稳定 |
| **维度** | 三维（3D） |
| **流体模型** | 不可压缩牛顿流体 |
| **网格** | 结构化均匀网格（co-located 同位网格） |
| **数据结构** | 基于用户提供的 `Tensor` 对象 |
| **应用场景** | 发酵罐内搅拌器驱动的液体混合 |
| **输出** | 每个体素的速度 (U,V,W)、压力、密度 |
| **可视化** | 控制台字符图 + VTK 文件（ParaView 可打开） |
| **设计目标** | 教学清晰，非性能优化 |

---

## 文件结构

```
CFDEngine/
├── CFDEngine.vbproj          # 项目文件（.NET 10.0）
├── Tensor.vb                 # Tensor 对象（用户提供，未修改）
├── FluidField.vb             # 流体场数据结构（速度/压力/密度）
├── StableFluidsSolver.vb     # 核心求解器（平流/扩散/投影）
├── Stirrer.vb                # 搅拌器（旋转叶轮）模型
├── FermentationTank.vb       # 发酵罐容器 + 主时间步进循环
├── CFDEngine.vb              # 顶层门面（FluidSim 类 + SnapshotFormat 枚举）
├── VTKExporter.vb            # VTK 文件导出器
├── Snapshot.vb               # 单帧快照数据单元（FluidField 深拷贝）
├── ISnapshotRecorder.vb      # 快照记录器统一接口
├── SnapshotRecorder.vb       # 基于 VTK 的逐帧记录器（.vtk + .pvd）
├── SnapshotMetadata.vb       # JSON 快照元数据对象（网格 + 配置 + 帧引用）
├── JsonSnapshotRecorder.vb   # 基于 JSON 的逐帧记录器（metadata.json + frame_xxx.json）
├── Program.vb                # 演示入口
├── README.md                 # 本文档
└── fermentation_tank_stirring.vtk  # 演示生成的 VTK 结果文件
```

### 各文件职责

| 文件 | 核心类 | 职责 |
|------|--------|------|
| `Tensor.vb` | `Tensor` | 多维数组容器，提供索引、运算、克隆等基础操作 |
| `FluidField.vb` | `FluidField`, `VoxelData` | 用 5 个 Tensor 存储 U/V/W/Pressure/Density 三维场 |
| `StableFluidsSolver.vb` | `StableFluidsSolver` | 实现半拉格朗日平流、隐式扩散、压力投影三大数值方法 |
| `Stirrer.vb` | `Stirrer` | 圆盘叶轮几何 + 旋转速度边界条件 |
| `FermentationTank.vb` | `FermentationTank` | 组装组件，执行算子分裂时间步进 |
| `CFDEngine.vb` | `FluidSim` | 顶层 API，工厂方法 + Run + 查询接口；含 `SnapshotFormat` 枚举 |
| `VTKExporter.vb` | `VTKExporter` | 导出 legacy VTK 文件供 ParaView 可视化 |
| `Snapshot.vb` | `Snapshot` | 单帧快照数据单元（持有 FluidField 深拷贝） |
| `ISnapshotRecorder.vb` | `ISnapshotRecorder` | 快照记录器统一接口，`Run` 以多态方式调用 |
| `SnapshotRecorder.vb` | `SnapshotRecorder` | 基于 VTK 的逐帧记录器：每帧 .vtk + 结束 animation.pvd |
| `SnapshotMetadata.vb` | `SnapshotMetadata` | JSON 元数据对象：网格模型 + 仿真配置 + 帧引用列表 |
| `JsonSnapshotRecorder.vb` | `JsonSnapshotRecorder` | 基于 JSON 的逐帧记录器：metadata.json + frame_xxx.json |
| `Program.vb` | `Module Program` | 演示：创建罐→注入染料→运行→打印→导出；支持 `--json` / `--vtk` 切换 |

---

## 物理原理

### 控制方程：不可压缩 Navier-Stokes 方程

本引擎求解的是**不可压缩粘性流体**的 Navier-Stokes 方程：

```
动量方程:  ∂u/∂t + (u·∇)u = -∇p/ρ + ν∇²u + f
连续性方程: ∇·u = 0
```

其中：
- **u = (U, V, W)** — 速度向量场（3个分量）
- **p** — 压力场
- **ρ** — 密度（假设常密度，故 ρ 被吸收进压力定义）
- **ν** — 运动粘度（控制流体的"粘稠程度"）
- **f** — 外力（本引擎中为搅拌器）

各项物理含义：

| 项 | 表达式 | 物理含义 |
|----|--------|----------|
| 局部加速度 | ∂u/∂t | 速度随时间的变化 |
| 对流加速度 | (u·∇)u | 流体随自身运动产生的加速度 |
| 压力梯度 | -∇p/ρ | 压力差驱动的流动（高压→低压） |
| 粘性扩散 | ν∇²u | 流体内部摩擦导致的动量扩散 |
| 外力 | f | 搅拌器对流体的作用力 |

### 不可压缩约束

**∇·u = 0** 是不可压缩流体的核心约束，表示流体既不会凭空产生也不会消失。
数学上，它要求速度场的**散度为零**。本引擎通过**压力投影**步骤来强制满足这一约束。

### 被动标量（密度/示踪剂）

除了速度和压力，本引擎还追踪一个**被动标量场**（Density）。
它不影响力场（"被动"），但会被流体带着运动（平流）并缓慢扩散。
在教学上，它用于**可视化混合效果**——就像在水中滴入一滴墨水观察它如何被搅拌散开。

---

## 数值方法

本引擎采用 Jos Stam 1999 年提出的 **Stable Fluids** 算法。
该算法的核心优势是**无条件稳定**——无论时间步长 dt 多大都不会数值爆炸，
这使它非常适合教学使用（不需要精细调节 CFL 条件）。

### 算子分裂法（Operator Splitting）

每个时间步 dt 内，按以下顺序依次执行：

```
Step 1: 添加外力        u ← u + dt·f          （搅拌器强制速度）
Step 2: 扩散速度        求解 u - dt·ν·∇²u = u_old  （隐式）
Step 3: 平流速度        半拉格朗日回溯           （无条件稳定）
Step 4: 压力投影        求解 ∇²p = ∇·u/dt，u ← u - dt·∇p
Step 5: 平流密度        半拉格朗日回溯（被动标量）
Step 6: 扩散密度        可选
Step 7: 重新施加搅拌器   保持搅拌器区域速度
```

### 1. 半拉格朗日平流（Semi-Lagrangian Advection）

**目标**：模拟物理量随流体运动被"搬运"的过程。

**核心思想**：不追踪流体去向，而是**反向追踪**——对每个网格点，
问"这个位置的流体是从哪里来的？"，然后从来源位置插值取值。

```
对每个网格点 (i, j, k):
    1. 获取当前位置速度 u = (U, V, W)
    2. 反向追踪：x_prev = (i, j, k) - dt × (U, V, W)
    3. 在 x_prev 处做三线性插值，得到 field_prev
    4. 令 field_new(i, j, k) = field_prev
```

**为什么稳定**：因为总是从网格点插值取值（不会外推放大），
所以无论 dt 多大，结果都有界。

**代码位置**：`StableFluidsSolver.Advect()`

### 2. 隐式扩散（Implicit Diffusion）

**目标**：模拟粘性导致的动量扩散（速度的"平滑"）。

**方程**：`u_new - dt·ν·∇²u_new = u_old`

**为什么用隐式**：显式扩散 `u_new = u_old + dt·ν·∇²u_old` 有严格的稳定性条件
（dt < dx²/(6ν)），而隐式格式无条件稳定。

**求解方法**：Jacobi 迭代。将方程离散化为：
```
u_new(i,j,k) = (u_old(i,j,k) + dt·ν·Σ邻居) / (1 + 6·dt·ν)
```
然后迭代多次直到收敛。

**代码位置**：`StableFluidsSolver.Diffuse()`

### 3. 压力投影（Pressure Projection）

**目标**：强制速度场满足不可压缩约束 ∇·u = 0。

**三步走**：

```
(a) 计算散度:
    div(i,j,k) = [U(i+1)-U(i-1)]/(2dx) + [V(j+1)-V(j-1)]/(2dy) + [W(k+1)-W(k-1)]/(2dz)

(b) 求解 Poisson 方程 ∇²p = div/dt:
    Jacobi 迭代: p(i,j,k) = [Σ邻居p - div·dx²/dt] / 6

(c) 修正速度（减去压力梯度）:
    U ← U - dt·[p(i+1)-p(i-1)]/(2dx)
    V ← V - dt·[p(j+1)-p(j-1)]/(2dy)
    W ← W - dt·[p(k+1)-p(k-1)]/(2dz)
```

**物理直觉**：压力像一个"修正力"，把速度场中不符合不可压缩条件的部分"推回去"。
高压区会把流体推开，低压区会吸引流体，最终使全场散度为零。

**代码位置**：`StableFluidsSolver.Project()`

### 4. 边界条件

| 边界 | 速度 | 压力/密度 |
|------|------|-----------|
| 罐壁（6个面） | 法向分量取反（不穿透壁面），切向分量复制（自由滑移） | 零梯度（复制相邻内部值） |

**代码位置**：`StableFluidsSolver.SetBoundary()`

### 5. 三线性插值（Trilinear Interpolation）

用于在非整数网格位置取值（半拉格朗日平流需要）。

```
给定连续坐标 (x, y, z):
    i0 = floor(x),  i1 = i0+1,  fx = x - i0
    j0 = floor(y),  j1 = j0+1,  fy = y - j0
    k0 = floor(z),  k1 = k0+1,  fz = z - k0

    value = (1-fx)(1-fy)(1-fz)·f(i0,j0,k0)
          + fx·(1-fy)(1-fz)·f(i1,j0,k0)
          + ... （共8项）
```

**代码位置**：`StableFluidsSolver.TrilinearSample()`

---

## 搅拌器模型

### 几何模型

本引擎采用最简单的**圆盘叶轮（disk impeller）**模型：

```
        ┌─────────────────────┐  ← 罐顶 (k=Nz-1)
        │                     │
        │        │            │  ← 搅拌轴（概念上，未建模）
        │        │            │
        │     ╔══╧══╗         │  ← 叶轮（圆盘）
        │     ║  ◐  ║         │     高度 ZCenter, 半径 Radius, 厚度 Height
        │     ╚══╤══╝         │
        │        │            │
        │        │            │
        │                     │  ← 罐底 (k=0)
        └─────────────────────┘
```

一个体素 (i,j,k) 属于搅拌器，当且仅当：
```
(i - CenterX)² + (j - CenterY)² ≤ Radius²
且 |k - ZCenter| ≤ Height / 2
```

### 速度模型

搅拌器以角速度 ω 绕 Z 轴旋转。在搅拌器占据的体素处，表面线速度为：

```
v_surface = ω × r

其中:
  ω = (0, 0, ω)           — 角速度向量（沿 Z 轴）
  r = (i - cx, j - cy, 0) — 从旋转轴到体素的向量

展开:
  U = -ω · (j - cy)        — 切向 X 分量
  V =  ω · (i - cx)        — 切向 Y 分量
  W =  AxialVelocity       — 轴向泵送（可选，默认 0）
```

### 实现方式

搅拌器作为**运动固体边界**实现：每个时间步结束后，
将搅拌器占据的体素速度强制设为搅拌器表面速度。
这样流体就会被"带着转"，产生搅拌效果。

**代码位置**：`Stirrer.ApplyToField()` / `Stirrer.ApplyToFieldInternal()`

---

## 编译与运行

### 环境要求

- .NET SDK 8.0 或更高版本
- 下载地址：https://dotnet.microsoft.com/download

### 编译

```bash
cd CFDEngine
dotnet build
```

### 运行

```bash
dotnet run
```

### 输出文件

演示默认导出一份 VTK 结果（最后一帧）：
```
CFDEngine/bin/Debug/net10.0/fermentation_tank_stirring.vtk
CFDEngine/fermentation_tank_stirring.vtk   （副本）
```

此外，逐帧快照会写入 `frames/` 目录。快照格式可通过命令行参数切换：

- **VTK（默认）**：每帧一个 `.vtk` + 结束时一个 `animation.pvd`（ParaView 动画集合）。
  ```
  dotnet run            # 默认 VTK
  dotnet run --vtk
  ```
- **JSON（低冗余）**：一份 `metadata.json`（网格模型 + 仿真配置）+ 一系列 `frame_xxx.json`（每帧全部物理场）。
  ```
  dotnet run --json
  ```

两者都通过 `FluidSim.Run(steps, dt, progressCallback, format, outputDir)` 驱动，
`format` 为 `SnapshotFormat.Vtk` 或 `SnapshotFormat.Json` 枚举。原有的
`SnapshotRecorder`（VTK）与新增的 `JsonSnapshotRecorder` 均实现 `ISnapshotRecorder` 接口，
因此也可显式构造任一记录器直接传给 `Run(..., recorder:=...)`，向后完全兼容。

### 用 ParaView 查看三维结果

1. 下载安装 ParaView：https://www.paraview.org/download/
2. 打开 ParaView，File → Open → 选择 `.vtk` 文件
3. 点击 "Apply"
4. 可查看的数据：
   - **pressure**：压力标量场（建议用 Surface 表示，开启 Color Map）
   - **density**：示踪剂浓度（观察混合效果）
   - **speed**：速度大小
   - **velocity**：速度向量场（可用 Glyph 或 Streamline 表示）

---

## 演示输出解读

运行 `dotnet run` 后，控制台会输出以下内容：

### 1. 速度场水平切片（叶轮高度 k=8）

用方向箭头表示速度方向，可以看到明显的**旋转流场**——
箭头围绕中心呈圆形排列，这正是搅拌器驱动的结果。

### 2. 密度场水平切片

用字符浓度（`.`→`@`）表示示踪剂浓度。
可以看到染料从搅拌器位置向外扩散，形成混合 pattern。

### 3. 垂直纵切面速度大小

从下到上显示速度大小。可以看到：
- **叶轮高度**速度最高（`@`、`#`、`%`）
- **远离叶轮**（罐顶/罐底）速度较低（`-`、`:`、`.`）
- 侧壁附近因旋转流也有一定速度

### 4. 统计信息

| 指标 | 典型值 | 含义 |
|------|--------|------|
| 最大速度 | ~31.6 | = ω × R（叶轮边缘线速度） |
| 平均速度 | ~10.0 | 全场平均 |
| 压力范围 | [-65, 198] | 中心低压（涡核），壁面高压（离心） |
| 最大密度 | ~0.13 | 染料最高浓度（已被稀释） |

### 5. 采样体素

打印 6 个关键位置的详细数据：
- 叶轮中心、罐中央、罐顶、罐底、两侧壁

---

## API 使用指南

### 基本用法

```vbnet
' 1. 创建引擎（24×24×24 网格，角速度 4.0）
Dim engine = CFDEngine.FluidSim.CreateDefault(24, 24, 24, angularVelocity:=4.0)

' 2. 注入示踪剂
engine.InjectDyeAtStirrer(1.0)

' 3. 运行 100 步，dt=0.1
engine.Run(100, 0.1)

' 4. 查询任意体素
Dim voxel = engine.GetVoxel(12, 12, 8)
Console.WriteLine($"U={voxel.U}, V={voxel.V}, W={voxel.W}")
Console.WriteLine($"Pressure={voxel.Pressure}, Density={voxel.Density}")

' 5. 导出 VTK
CFDEngine.VTKExporter.Export(engine.Tank, "result.vtk")
```

### 自定义配置

```vbnet
' 自定义搅拌器
Dim stirrer As New CFDEngine.Stirrer()
stirrer.CenterX = 12.0
stirrer.CenterY = 12.0
stirrer.ZCenter = 6.0       ' 叶轮高度
stirrer.Radius = 8.0        ' 叶轮半径
stirrer.Height = 2.0        ' 叶轮厚度
stirrer.AngularVelocity = 5.0   ' 角速度
stirrer.AxialVelocity = 0.5     ' 轴向泵送（可选）

' 自定义罐
Dim tank As New CFDEngine.FermentationTank(24, 24, 24, stirrer)
tank.Viscosity = 0.0001     ' 运动粘度
tank.Diffusion = 0.00001    ' 示踪剂扩散系数

Dim engine As New CFDEngine.FluidSim(tank)

' 逐步运行（可加入自定义逻辑）
For step = 1 To 200
    tank.StepForward(0.1)
    ' 每步可查询、记录、修改...
Next
```

### 获取完整场数据

```vbnet
' 获取整个场的 Tensor 对象（可直接访问 Data 数组）
Dim uTensor = engine.GetVelocityU()   ' Tensor, shape=(Nx,Ny,Nz)
Dim pTensor = engine.GetPressure()
Dim dTensor = engine.GetDensity()

' 直接访问底层 Double 数组（高性能遍历）
Dim uData = uTensor.Data   ' Double(Nx*Ny*Nz)
For k = 0 To nz-1
    For j = 0 To ny-1
        For i = 0 To nx-1
            Dim idx = i*ny*nz + j*nz + k
            Dim u = uData(idx)
            ' ...
        Next
    Next
Next
```

---

## 参数调优指南

### 网格尺寸 (Nx, Ny, Nz)

| 尺寸 | 内存 | 单步耗时 | 精度 | 建议 |
|------|------|----------|------|------|
| 16³ | ~0.3 MB | ~0.1s | 低 | 快速测试 |
| 24³ | ~1.0 MB | ~0.4s | 中 | **教学默认** |
| 32³ | ~2.5 MB | ~1.2s | 中高 | 较好效果 |
| 48³ | ~8.3 MB | ~6s | 高 | 需耐心 |

### 时间步长 dt

Stable Fluids 对 dt **不敏感**（无条件稳定），但 dt 影响精度：
- dt 太大：平流误差增大，可能"跳过"细节
- dt 太小：需要更多步数才能达到相同模拟时间
- **建议**：dt = 0.1 ~ 0.5（网格单位）

### 运动粘度 Viscosity (ν)

| ν 值 | 流体类型 | 效果 |
|------|----------|------|
| 0.00001 | 水（近似） | 流动剧烈，涡旋多 |
| 0.0001 | **教学默认** | 适中 |
| 0.001 | 油 | 流动粘滞，涡旋少 |
| 0.01 | 蜂蜜 | 几乎层流 |

### 角速度 AngularVelocity (ω)

- ω 越大，搅拌越剧烈，最大速度 = ω × R
- **建议**：2.0 ~ 8.0

### Jacobi 迭代次数

- 控制压力投影和扩散的求解精度
- 次数越多越精确但越慢
- **建议**：20~40（教学），50+（高精度）

---

## JSON 快照系统（低冗余）

基于 VTK 的逐帧快照每帧都会重复写入 `DIMENSIONS / ORIGIN / SPACING` 等
**三维体素模型定义信息**，导致帧与帧之间数据冗余度很高。
JSON 快照系统将「不随时间变化的网格与配置」抽离到一份 `metadata.json`，
每帧只写物理场数据（`frame_xxx.json`），从而彻底消除上述冗余。

### 文件结构

运行 `dotnet run --json` 后，`frames/` 目录下生成：

```
frames/
├── metadata.json        # 网格模型 + 仿真配置 + 帧引用列表（仅写一次）
├── frame_0000.json      # 第 0 帧：step / time + 7 个物理场扁平数组
├── frame_0001.json
└── ...
```

### metadata.json 结构

```json
{
  "format": "json",
  "schemaVersion": 1,
  "createdAt": "2026-07-13T...",
  "grid": {
    "nx": 48, "ny": 48, "nz": 48,
    "origin": [0.0, 0.0, 0.0],
    "spacing": [1.0, 1.0, 1.0],
    "totalVoxels": 110592,
    "indexOrder": "i*ny*nz + j*nz + k"
  },
  "simulation": {
    "viscosity": 0.0001,
    "diffusion": 0.00001,
    "timeStep": 0.1,
    "solver": "StableFluids (Jos Stam, 1999)",
    "stirrer": {
      "centerX": 23.5, "centerY": 23.5, "zCenter": 16.0,
      "radius": 16.0, "height": 2.0, "angularVelocity": 4.0, "axialVelocity": 0.0
    }
  },
  "frames": [
    { "step": 1,  "time": 0.1, "file": "frame_0000.json" },
    { "step": 2,  "time": 0.2, "file": "frame_0001.json" }
  ]
}
```

> 无搅拌器时，`simulation.stirrer` 为 `null`。

### frame_xxx.json 结构

每帧保存 `step`、`time`、网格维度与全部 **7 个物理场**的扁平一维数组
（索引顺序 `i*ny*nz + j*nz + k`，与 VTK 导出完全对齐）：

```json
{
  "step": 0,
  "time": 0.0,
  "grid": { "nx": 48, "ny": 48, "nz": 48 },
  "fields": {
    "pressure":  [ ... ],          // 长度 N
    "density":   [ ... ],          // 长度 N
    "u":         [ ... ],          // 长度 N（X 方向速度）
    "v":         [ ... ],          // 长度 N（Y 方向速度）
    "w":         [ ... ],          // 长度 N（Z 方向速度）
    "speed":     [ ... ],          // 长度 N（速度大小，由 U/V/W 派生）
    "velocity":  [ ... ]           // 长度 3N（u,v,w 交错扁平数组）
  }
}
```

### 编程用法

```vbnet
' JSON 模式（自动由 Tank 派生 metadata）
engine.Run(80, 0.1, progressCallback,
           format:=SnapshotFormat.Json, outputDir:="frames")

' 或显式构造记录器（传入自定义 metadata）
Dim md = SnapshotMetadata.FromTank(engine.Tank, dt:=0.1)
Dim rec = New JsonSnapshotRecorder("frames", md, baseName:="frame", interval:=1)
engine.Run(80, 0.1, progressCallback, recorder:=rec)
```

---

## 扩展思路

以下扩展适合作为教学项目或课程作业：

### 1. 搅拌轴建模
在搅拌器上方添加一个细圆柱（搅拌轴），强制其速度为 0 或随轴旋转。

### 2. 挡板（Baffles）
在罐壁上添加 4 个垂直挡板，防止流体整体刚体旋转，促进轴向混合。
挡板实现：将挡板占据的体素速度强制为 0。

### 3. 多层叶轮
在不同高度放置多个叶轮，模拟多层搅拌。

### 4. Rushton 涡轮叶片
用更复杂的几何（6 叶片涡轮）替代简单圆盘，
在叶片位置施加更高的局部速度。

### 5. 自由液面
将罐顶改为自由液面边界（压力 = 大气压），
模拟液面漩涡（vortex）形成。

### 6. 温度场
添加一个温度标量场，模拟热扩散和热对流。

### 7. 多组分混合
追踪两种不同密度的示踪剂，模拟不同液体的混合过程。

### 8. 非牛顿流体
修改粘度模型，使 ν 依赖于剪切率（如幂律流体）。

### 9. 并行化
使用 Task Parallel Library 并行化 Jacobi 迭代和平流计算。

### 10. 自适应网格
在搅拌器附近使用更细的网格，远离区域使用粗网格。

---

## 参考文献

1. **Jos Stam**, "Stable Fluids", Proceedings of SIGGRAPH 1999, pp. 121-128.
   — 本引擎的核心算法来源。

2. **Jos Stam**, "Real-Time Fluid Dynamics for Games", Proceedings of the Game Developer Conference 2003.
   — 更简洁的实现说明，适合入门阅读。

3. **Charles Hirsch**, "Numerical Computation of Internal and External Flows", 2nd ed.
   — CFD 数值方法的全面教科书。

4. **H. Versteeg & W. Malalasekera**, "An Introduction to Computational Fluid Dynamics: The Finite Volume Method".
   — 有限体积法经典教材。

5. **H. K. Versteeg**, "Turbulence and CFD Modelling of Stirred Tanks".
   — 搅拌罐 CFD 建模专题。

---

## 许可

本项目为教学用途，可自由使用、修改、分发。
