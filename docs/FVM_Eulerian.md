构建一套以 Eulerian（网格）数据为基础的 FVM CFD 引擎，其核心逻辑是：**以控制体积分守恒律为出发点，将 PDE 转化为基于单元面通量的代数方程组，再通过压力–速度耦合算法迭代求解，整体围绕"网格数据结构 + 场变量与算子 + 离散矩阵 + 线性求解器 + 耦合循环 + 边界条件/并行后处理"六大模块分层组装**。下面为从架构到代码骨架的完整工程实现路径。
## 一、引擎总体架构与模块拆解
一个典型的 FVM 求解器（以 OpenFOAM 为参考范式）由六大子系统构成：
| 模块层 | 职责 | 关键数据结构 | 可参考的开源范式 |
|---|---|---|---|
| **网格层** | 提供单元/面/点的拓扑与几何 | owner/neighbour 面邻接数组、面法向、面心、体积 | OpenFOAM `fvMesh` |
| **场变量层** | 存储与插值物理量 | `GeometricField<T, PatchField, GeoMesh>`（cell/surface/internal/patch 四段式） | OpenFOAM `GeometricField` |
| **离散算子层** | 将散度/梯度/拉普拉斯映射到面通量求和 | `fvm::div`、`fvm::laplacian`、`fvc::` 显式算子 | OpenFOAM `fvm/fvc` 命名空间 |
| **代数系统层** | 组装稀疏线性方程组 | `fvMatrix<T>`（对角、非对角、源项、边界矩阵贡献） | OpenFOAM `fvMatrix` |
| **求解器层** | 迭代求解线性系统 | PCG、BiCGStab、GAMG | PETSc、hypre、COMSOL 迭代求解器 |
| **顶层循环层** | 压力-速度耦合外循环、时间推进 | SIMPLE / PISO / PIMPLE 主循环 | simpleFoam、pisoFoam、pimpleFoam |
## 二、网格数据层
这是 Eulerian 框架的地基。建议采用 **面基（face-based）拓扑**：一个面连接两个单元（owner 与 neighbour），边界面仅连一个单元。
### 2.1 需要的核心数据结构
```cpp
struct Face {
    label ownerCell;      // 左侧单元
    label neighbourCell;  // 右侧单元（-1 表示边界面）
    Vector centroid;      // 面心
    Vector Sf;            // 面法向量（模为面积）
};
struct Cell {
    Vector centroid;
    scalar volume;
    labelList faceLabels;  // 构成该单元的面
};
```
### 2.2 必须预计算的几何量
- **单元体积** V_P、**单元心到面心的向量** d = x_f - x_P
- **面法向 S_f**、**非正交修正系数**（用于扩散项的 over-relaxed correction）
- **面插值权重** w_f = |x_f - x_N| / (|x_f - x_P| + |x_f - x_N|)
## 三、场变量层
采用 **cell-centered 存储策略**：所有物理量存储于单元心，面值通过插值获得。需要设计：
### 3.1 GeometricField 四段式存储
OpenFOAM 的设计范式非常值得借鉴：
- `internalField`：内部单元值
- `boundaryField`：各 patch 的边界条件对象
- 派生层：`surfaceFields`（面心值）、`pointFields`（点值，用于某些梯度重构）
### 3.2 插值算子
- **线性插值**（对流项的中心差分）：φ_f = w_f φ_P + (1-w_f) φ_N
- **梯度重构**：采用 **高斯最小二乘**（Gauss LSQ）法：通过最小化 Σ_faces (φ_N - φ_P - ∇φ·d)² 求解 ∇φ。对于非正交网格，需要加上 **方向修正** 或采用 **MPFA/LST** 等多面通量逼近以保证精度。
## 四、输运方程的 FVM 离散化
这是引擎的数学核心。以通用标量输运方程为例：
$$\int_V \frac{\partial(\rho\phi)}{\partial t} dV + \int_V \nabla \cdot (\rho \mathbf{u} \phi) dV = \int_V \nabla \cdot (\Gamma_\phi \nabla\phi) dV + \int_V S_\phi dV$$
### 4.1 对流项离散
将体积散度积分用 **高斯定理** 转化为面通量求和：
$$\sum_f \dot{m}_f \phi_f$$
其中质量通量 $\dot{m}_f = \rho \mathbf{u}_f \cdot \mathbf{S}_f$。对流格式决定精度与稳定性：
| 格式 | 精度 | 稳定性 | 适用场景 |
|---|---|---|---|
| 一阶迎风 | 1 阶 | 无条件稳定 | 初场/启动 |
| 中心差分 | 2 阶 | 易振荡（需 Péclet < 2） | 扩散主导 |
| QUICK | 3 阶 | 中等 | 对流主导 |
| TVD/MUSCL/ van Leer | 2 阶 + 限制器 | 稳定 | 激波/大梯度 |
### 4.2 扩散项离散
$$\sum_f \Gamma_f (\nabla\phi)_f \cdot \mathbf{S}_f \approx \sum_f \Gamma_f \frac{\phi_N - \phi_P}{|\mathbf{d}|} |\mathbf{S}_f| + \text{non-orthogonal correction}$$
非正交修正项需要拆分为正交部分（隐式）+ 非正交部分（显式迭代修正）。
### 4.3 时间项离散
采用 **Euler 隐式**（稳健，1 阶）或 **Crank-Nicolson**（2 阶，需欠松弛）或 **backward 差分**（2 阶隐式）。
### 4.4 fvMatrix 组装
对每个单元 P，最终得到：
$$a_P \phi_P + \sum_N a_N \phi_N = b_P$$
其中 a_N 来自对流+扩散的面贡献，b_P 包含源项、时间项、边界贡献。
## 五、压力-速度耦合（不可压核心）
这是不可压求解器最关键的设计。N-S 方程中压力没有独立的输运方程，需要通过 **压力 Poisson 方程** 耦合。
### 5.1 SIMPLE 算法（稳态）
参考 Fluent 理论手册与 Perić 教程，步骤为：
```
1. 动量预测:   解 aP uP + Σ aN uN = -∇p* + S  (u*)
2. 通量计算:   φ*f = u*f · Sf
3. 压力修正:   解 Poisson 方程 ∇·(d ∇p') = ∇·u*
              其中 d = V/aP（动量矩阵对角倒数）
4. 速度修正:   u' = -d ∇p'
5. 更新:       u = u* + u',  p = p* + αp·p'
6. 收敛检查 → 否则回到 1
```
关键点：**压力修正方程的系数由动量矩阵对角元素的倒数 d = V/a_P 决定**，保证与动量方程的一致性。
### 5.2 PISO（瞬态）
增加 **多次压力修正** 循环（典型 2-3 次），适合大时间步长瞬态问题。
### 5.3 PIMPLE（合并）
**PIMPLE = PISO 内循环 + SIMPLE 外循环 + 欠松弛**，兼顾瞬态稳健性与大时间步长。
### 5.4 Rhie-Chow 插值（必须掌握！）
同位网格上直接线性插值速度计算面通量会引入 **棋盘压力振荡**。Rhie-Chow 插值通过在面速度中引入压力梯度差分项进行去相关：
$$u_f = \overline{u}_f - \overline{d}_f \left(\nabla p_f - \overline{\nabla p}_f\right)$$
这是构建稳健同位 FVM 求解器不可或缺的技术。
## 六、可压缩流与通量分裂
对于可压缩 Euler/N-S 方程，需要用 **黎曼求解器** 或 **通量向量分裂** 处理对流项：
- **FVS（通量向量分裂）**：Steger-Warming、van Leer
- **FDS（通量差分分裂）**：Roe（需要熵修正）
- **AUSM 系列**：兼具 FVS 的鲁棒与 FDS 的接触分辨力，特别适合激波-边界层干扰
- **HLLC**：对接触间断分辨率好
时间推进上，显式多步 Runge-Kutta + **局部时间步长**（CFL 条件）常用于定常解，隐式 LU-SGS 用于大规模计算。
## 七、湍流模型
**RANS**：在 FVM 框架下对 k-ε、k-ω SST 等模型解额外的输运方程，并对雷诺应力项做 Boussinesq 假设。近壁面采用壁面函数（对数律）或低 Re 直接积分。
**LES**：需要 **空间滤波** 与亚格子模型（Smagorinsky、WALE、动态模型），对网格分辨率要求极高。
## 八、线性求解器与收敛控制
### 8.1 稀疏线性求解器选型
| 求解器 | 特点 | 推荐场景 |
|---|---|---|
| **PCG** | 对称正定，需预条件 | 压力 Poisson（对称） |
| **BiCGStab** | 非对称，内存省 | 动量方程 |
| **GAMG**（几何代数多重网格） | 收敛速度与网格规模无关 | 大规模压力方程 |
| **GMRES**（+ 预条件） | 鲁棒，内存随重启数增长 | 病态非对称系统 |
### 8.2 收敛准则
**初始残差 + 起始残差归一化** 是常用做法：当某时间步/迭代的初始残差相比上一时间步/迭代的初始残差下降 3-4 个量级，或绝对残差 < 10⁻⁶ 时认为内迭代收敛。同时监控物理量（如阻力系数、出口流量）的稳定性。
### 8.3 欠松弛
稳态 SIMPLE 需要对速度（0.7）与压力（0.3）分别欠松弛以保证收敛；PIMPLE 中通过 **外循环松弛** 与 **校正次数** 共同控制。
## 九、边界条件
设计 **基类 + 派生** 策略。每个 patch 上派生边界类型需贡献：
```cpp
class BoundaryCondition {
    virtual void updateCoefficients(fvMatrix&, GeometricField&) = 0;
    virtual scalar value(const scalar internalVal) const = 0;
    virtual scalar gradient() const = 0;
};
// 派生类
class FixedValue : public BoundaryCondition;      // Dirichlet
class FixedGradient : public BoundaryCondition;   // Neumann
class Mixed : public BoundaryCondition;           // Robin
class InletOutlet : public BoundaryCondition;     // 开口
class CyclicAMI : public BoundaryCondition;       // 周期/滑移网格
class WallFunction : public BoundaryCondition;    // 湍流壁面函数
```
边界条件必须正确贡献到 fvMatrix 的对角元与源项。
## 十、并行计算与 AMR
### 10.1 并行化
采用 **MPI 域分解**：
- 将网格按 METIS/Scotch 分区，每个进程处理一个子域
- 交换 **面通量** 与 **梯度插值所需邻域信息**（overlap ghost cells）
- 关键是保证 **分区面上的通量连续性**（一次性求和，避免重复计算）
### 10.2 自适应网格加密（AMR）
- **structured AMR**：嵌套的笛卡尔块（如 Chombo、p4est 范式）
- **unstructured AMR**：直接在原始网格上分裂单元，需要处理 hanging nodes
- 触发指标：梯度、涡量、Q-criterion、误差估计
- 加密/粗化后需要 **通量守恒修正**（父子面通量平衡）与 **并行重平衡**
## 十一、验证与确认
- **MMS（Manufactured Solutions）**：验证离散精度的阶数
- **收敛阶测试**：均匀网格加密，p-refinement
- **网格无关性**：h-refinement 三层以上
- **经典算例**：
  - 驱动方腔（Ghia et al. 基准解）
  - 后台阶流动（Gartling 基准）
  - NACA0012 绕流（可压缩，跨声速）
  - Rayleigh-Taylor 不稳定性（LES/多相）
## 十二、完整代码骨架参考（OpenFOAM 风格）
以 simpleFoam 主循环为模板：
```cpp
// 主程序伪代码
int main(int argc, char* argv[]) {
    // 1. 网格加载与初始化
    fvMesh mesh = readMesh();
    GeometricField<scalar> p(mesh), U(mesh);
    // 2. 边界条件初始化
    applyInitialConditions(p, U);
    // 3. SIMPLE 主循环
    while (!converged) {
        // 3.1 动量方程离散与求解
        fvMatrix<vector> UEqn = 
            fvm::div(phi, U)              // 对流（隐式）
          - fvm::laplacian(nu, U);         // 扩散（隐式）
        UEqn.relax();                      // 欠松弛
        solve(UEqn == -fvc::grad(p));      // 动量预测
        // 3.2 压力修正方程
        volScalarField rAU = 1.0/UEqn.A();  // d = 1/aP
        volVectorField HbyA = UEqn.H()/UEqn.A();
        surfaceScalarField phiHbyA = 
            fvc::interpolate(HbyA) & mesh.Sf();  // 面通量
        // Rhie-Chow 修正（关键！）
        phiHbyA += fvc::ddtCorr(U, phiHbyA);
        // 3.3 压力 Poisson 方程
        fvScalarMatrix pEqn = 
            fvm::laplacian(rAU, p) == fvc::div(phiHbyA);
        pEqn.setReference(pRefCell, pRefValue);
        pEqn.solve();
        // 3.4 速度与通量修正
        U = HbyA - rAU*fvc::grad(p);
        phi = phiHbyA - pEqn.flux();  // 通量修正，保证连续性
        // 3.5 收敛监控
        convergenceCheck();
    }
    // 4. 输出结果
    writeResults();
}
```
## 十三、实施路线图与推荐路径
按 **五阶段** 递进开发（避免直接陷入复杂场景）：
1. **阶段 1：标量对流-扩散**：1D/2D 结构网格，均匀速度场，验证迎风/中心差分的精度阶
2. **阶段 2：Laplace/Poisson 方程**：实现 fvMatrix 与线性求解器，验证混合边界条件
3. **阶段 3：Stokes 流**（蠕动流）：首次耦合压力-速度，验证 SIMPLE 在小 Re 下的稳定性
4. **阶段 4：完整不可压 Navier-Stokes**：驱动方腔、管道流，加入湍流 RANS（k-ε/k-ω）
5. **阶段 5：可压缩/多相/LES**：加入通量分裂、ALE 动网格、LES 滤波
**推荐参考的开源求解器源码**：
- **OpenFOAM**（C++，最完整的工程实现，`src/finiteVolume` 包含全部 FVM 核心类）
- **UCNS3D**（高阶非结构可压缩）
- **polmes/fvm-cfd**（轻量 FVM 入门，GitHub）
- **Chalmers/Open-Source CFD Course**（含 PIMPLE 算法详细讲义）
- **cf-python**（英国气象局，Fortran/Python 混合，简洁的有限体积框架）
**经典教材**：
- Moukalled, Darwish & Mangani - *The Finite Volume Method in Computational Fluid Dynamics*（最详尽的 FVM 工程化教程）
- Versteeg & Malalasekera - *An Introduction to Computational Fluid Dynamics: The Finite Volume Method*（入门经典）
- Ferziger & Perić - *Computational Methods for Fluid Dynamics*（算法层面深入）
- Blazek - *Computational Fluid Dynamics: Principles and Applications*（可压缩、通量分裂、多重网格）

