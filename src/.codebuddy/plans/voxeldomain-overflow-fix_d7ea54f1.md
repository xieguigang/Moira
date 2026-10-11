---
name: voxeldomain-overflow-fix
overview: 修复大分辨率体素模型加载崩溃：BuildDomain 的 Integer 溢出改为 Long 安全运算并加域容量硬校验；ZMesh/WindTunnelBox 增加自动 domainScale 收缩与超限清晰报错（指引降低 --resolution），LbmCoreF 增加格点数上限前置校验。
todos:
  - id: builddomain-long-safe
    content: 修改 CFDEngine/VoxelModelLoader.vb BuildDomain：total 与索引改 Long 安全运算，超 Int32 上限时抛带重新体素化指引的清晰异常
    status: completed
  - id: auto-domain-scale
    content: ZMesh/VoxelJsonLoader.vb 增加 ResolveDomainScale 自动收缩（预算 1 亿格点）与 BuildSceneFromJson budgetVoxels 参数，超限抛指引异常
    status: completed
  - id: lbm-grid-limit
    content: LbmCoreF/WindTunnelSolver 增加格点数硬上限前置校验（MaxGridPoints ≈ 1.13 亿）与可读报错
    status: completed
  - id: ui-integration
    content: FormWindTunnel.LoadModel 与 LBMProgram /voxeljson 接入自动收缩，状态栏展示实际 DomainScale，失败对话框呈现指引
    status: completed
  - id: e2e-overflow-validation
    content: 编译全链并用 F1 zip（res=2048 预期清晰报错）与 res=96 重新体素化的 zip 验证端到端模拟成功，回归 city_seed42 场景
    status: completed
---

## 问题诊断（根因已确认）

加载 `Custom_F1_2022_3D.voxel.zip`（res=2048 → 模型 736×2048×428 = **6.45 亿体素**）时崩溃，根因是 **`VoxelModelLoader.BuildDomain` 的 Integer 算术溢出**：

- `VoxelModelLoader.vb:248`：`Dim total = dnx * dny * dnz` 为 Integer 乘法。domainScale=2 时计算域 = 1472×4096×856 = **51.6 亿 > Int32.MaxValue（21.4 亿）**，回绕成 ~8.66 亿 → `mask` 数组按错误（过小）尺寸分配
- `VoxelModelLoader.vb:281`：`mask((tx * dny + ty) * dnz + tz) = False` 索引同样 Integer 溢出为负数 → `IndexOutOfRangeException`（即用户所见异常）
- 深层问题：即便修掉溢出，51.6 亿体素的 Boolean 域（5.2 GB）也远超 `.NET` 单数组上限（Int32.MaxValue 元素），更远超 LBM 求解能力（`LbmCoreF` 的 f/fc 为 19×N Single 数组，N 硬上限 ≈ **1.13 亿格点**）——高分辨率模型必须收缩 domainScale 或降低体素化分辨率

## 修复方案（三层防御）

1. **CFDEngine（正确性）**：`BuildDomain` 全部尺寸/索引运算改用 Long，溢出安全；域体素数超过 Int32 上限时抛出带指引的清晰异常（而非 IndexOutOfRange）
2. **ZMesh / WindTunnelBox（可用性）**：`BuildSceneFromJson` / `LoadModel` 增加**自动 domainScale 收缩**——按域体素预算（默认 1 亿格点）从请求的 scale 逐级下调至 ≥1.0，并把实际采用的 scale 写入场景元数据与状态栏；域仍超预算时给出明确报错（指引用 Voxelizer CLI `--resolution ≤ 128` 重新体素化）
3. **LBMEngine（前置校验）**：`LbmCoreF` 构造与 `WindTunnelSolver` 工厂前置校验格点数 ≤ 硬上限（112,936,676），超限抛出可读异常，替代不可读的 Array 分配失败

## 核心功能

- 高分辨率体素包（.voxel.zip）加载不再崩溃，域容量自动适配
- 超限时报错信息可读且包含修复指引（降低 resolution / 自动收缩后的 scale）
- 既有低分辨率工作流行为完全不变（scale 足够时不收缩）

## 技术方案

### 1. CFDEngine `VoxelModelLoader.BuildDomain`（正确性修复）

- `dnx/dny/dnz` 计算保持 Integer（各轴尺寸本身不溢出），但 `total` 改 `Dim total As Long = CLng(dnx) * dny * dnz`
- `total > Integer.MaxValue` 时抛 `InvalidOperationException`：消息含模型尺寸、scale、域体素数与修复指引（「请降低 domainScale 或用 Voxelizer CLI --resolution ≤ 128 重新体素化」）
- :281 索引改 `CInt(CLng(tx) * dny + ty) * dnz + tz`（Long 中间量防溢出，最终值已由 total 校验保证 < Int32.MaxValue）
- `:251` 全 True 填充循环改按 Long 上界（或用 Array.Fill 消除循环溢出风险）

### 2. ZMesh `WindTunnelVoxelJson.BuildSceneFromJson`（自动收缩）

- 新增私有函数 `ResolveDomainScale(model As VoxelModel, requestedScale As Double, budgetVoxels As Long) As Double`：
- 从 requestedScale 开始（步长 0.1 下调），取使 `ceil(W*s)*ceil(H*s)*ceil(D*s) ≤ budget` 的最大 scale，下限 1.0
- 若 scale=1.0 仍超预算 → 抛带指引异常（模型本身格点数 > 1 亿，须降低体素化分辨率）
- `BuildSceneFromJson` 签名增加 `Optional budgetVoxels As Long = 100_000_000`，内部先 `FromVoxelFile` 得到模型再解析 scale，实际 scale 记入 `WindTunnelScene.DomainScale`（既有元数据字段）
- 对原始 `BuildScene`（GUI 内体素化）路径不做改动（分辨率受 resolution 参数约束，天然受限）

### 3. WindTunnelBox `FormWindTunnel.LoadModel`

- `BuildSceneFromJson` 调用不再硬编码 domainScale:=2.0，交给自动收缩（budget 默认 1 亿）
- 状态栏/加载失败对话框展示实际采用的 DomainScale；捕获指引性异常原样呈现

### 4. LBMEngine 前置校验

- `LbmCoreF`：新增 `Public Const MaxGridPoints As Long = Int32.MaxValue \ LatticeD3Q19.Q`（≈1.13 亿）；构造函数校验 `CLng(nx)*ny*nz > MaxGridPoints` 时抛清晰异常
- `WindTunnelSolver` 构造函数同样前置校验（在分配掩码/域之前）

### 5. 验证

- `/voxeljson` 命令同步改用自动收缩（去掉硬编码 1.5）
- 端到端：用 F1 的 .voxel.zip（res=2048，6.45 亿体素）验证——预期自动收缩到 scale=1.0 后仍超预算 → 得到清晰指引报错；再用 CLI 以 `--resolution 96` 重新体素化 F1 模型 → zip 加载 → 风洞模拟全链路成功
- 回归：city_seed42（96×96×18）低分辨率行为不变

### 性能与兼容

- Long 索引运算在热循环外（BuildDomain 是一次性构建），无性能影响
- 所有既有调用点（GUI 内体素化、city demo）行为不变；新增校验只影响原本就会崩溃/不可仿真的超大域场景