用于风洞CFD的随机城市生成，推荐采用**"道路网划分 + 街区填充（泊松盘/高度映射）+ 建筑体块挤出"**的程序化生成算法链；导出格式首推**STL（二进制、单位为米、水密流形）**，用于OpenFOAM/snappyHexMesh等主流CFD求解器的贴体网格划分，OBJ可作为可视化中间格式，STEP仅在做精细CAD后处理时考虑。下面分算法、格式、流程三部分展开。
---
## 一、随机城市生成的算法设计
### 1. 推荐的算法链（从宏观到微观）
城市几何生成是一个多尺度问题，学术上通常拆解为**道路网 → 街区 → 地块 → 建筑**四级流程。针对风洞模拟，推荐以下三级组合：
- **第一级：道路网生成** —— 采用**波前法（Wavefront / Incremental Growth）**或**L-System**在矩形/不规则边界内递归分裂出主干道与次干道，形成街区多边形。这是目前程序化城市中最成熟的一步，替代方案是基于GAN的布局生成（研究性质，控制性不如传统方法）。
- **第二级：街区内部建筑放置** —— 在每个街区多边形内用**泊松盘采样**保证建筑不重叠且间距可控；同时结合**高度图映射**，即预先定义一张"密度/高度图"（中心高、周边低，或按风向上下游人为加密），使建筑高度分布呈现从CBD到郊区的梯度，这一点对暴风雨风场模拟尤为关键。
- **第三级：单体建筑几何** —— 对每个建筑脚印执行**拉伸**生成盒体（LOD1，平屋顶），这是风工程CFD最常用的简化；若需要LOD2可加坡屋顶，但对风场影响通常在几个百分点以内。
TU Delft开源的**Random3Dcity**就是这一思路的参考实现，可生成多LOD的合成建筑与CityGML输出。Esri CityEngine的CGA规则文件是工业级替代方案（商业软件，2026版已支持Python 3）；若追求完全可控与开源，**Blender Python API**是性价比最高的实现载体。
### 2. 算法方案对比
| 算法/方案 | 随机性来源 | 几何复杂度 | 实现难度 | 与CFD的契合度 |
|---|---|---|---|---|
| 泊松盘 + 盒体挤出 | 种子点随机分布 | 低（LOD1） | 低 | **最优**：尖锐角利于snappyHexMesh捕捉 |
| 波前法 + L-System | 递归分裂参数 | 低 | 中 | 好，但需后续街区填充 |
| CityEngine CGA | 规则+随机属性 | 中-高 | 中（需学CGA） | 需导出后清理网格 |
| Blender Python + 自建脚本 | 完全自定义 | 任意 | 高（编程量） | **最优**，可精确控制单位与水密 |
| GAN布局生成 | 训练数据 | 中 | 高 | 研究性，几何可控性弱 |
| Random3Dcity（开源） | 内置随机参数 | LOD0-LOD2 | 低（直接用） | 输出CityGML，需转换 |
对于风洞模拟，**"低几何复杂度 + 高参数可控性"**的组合优于高保真建筑形态，因为CFD关心的是建筑体积对气流的阻塞效应，而不是立面细节。
---
## 二、导出格式的选择与依据
### 1. 主流CFD求解器的输入偏好
主流CFD代码（OpenFOAM、ANSYS Fluent、STAR-CCM+、MicroCFD Virtual Wind Tunnel等）的几何输入都围绕**三角面片网格**或**B-Rep CAD**两类。MicroCFD 3D Virtual Wind Tunnel明确只接受STL格式的表面三角网格；OpenFOAM的snappyHexMesh工作流同样以STL为核心输入，通过`surfaceFeatureExtract`提取特征边后再做贴体网格。
### 2. 格式对比
| 格式 | 拓扑类型 | 单位信息 | CFD兼容性 | 适用场景 |
|---|---|---|---|---|
| **STL（二进制）** | 三角面片（无拓扑） | **无单位**（需生成器内部锁定为米） | OpenFOAM/snappyHexMesh、Fluent、Virtual Wind Tunnel等**直接支持** | **首选导出格式** |
| OBJ | 三角面片 + 法线 + UV | 无单位 | 支持广泛，但CFD管线常忽略其附加属性 | 中间可视化、3D打印 |
| STEP (AP203/214) | 精确B-Rep（NURBS） | 携带单位 | 需先在CAD/网格器里离散为面网格 | 仅在做精细CAD修正后考虑 |
| IGES | B-Rep | 携带单位 | 老旧，兼容性差 | 不推荐 |
| CityGML | 语义化多LOD | 带地理坐标 | **不直接被CFD接受**，需转换为STL | Random3Dcity等工具的中间产物 |
**结论：导出二进制STL，单位强制为米。** STL由80字节头、三角面片数和法向量+三顶点三元组构成，是CFD网格划分的事实标准。OBJ适合同时保留可视化；STEP只在需要把几何回送到Solidworks/FreeCAD做人工修补时才有价值。
### 3. 导出前必须满足的几何质量要求
CFD网格器（尤其snappyHexMesh）对输入几何非常挑剔，导出前需检查：
- **水密**：每个边恰好被两个三角形共享；盒体不能有开放面。
- **单位与尺度**：STL本身不带单位，必须在生成代码里统一设定1 unit = 1 m，否则导入CFD后尺度错乱。
- **无自交/共面重叠**：相邻建筑底面不能相交；如需布尔运算（合并到地面），要在Blender里做`Boolean Union`后再导出。
- **三角形质量**：避免退化三角形（面积趋零）；snappyHexMesh的`snapControls`对狭长三角形敏感。
- **法向量一致外向**：面法向量必须统一指向流体外侧，否则snappyHexMesh会判定体积为负。
- **多区域STL**：把地面、建筑群、 inlet/outlet/侧面分别存为带`solid <name>`标记的区域，便于在OpenFOAM的`snappyHexMeshDict`里分别指定边界条件。
---
## 三、从生成到CFD的完整技术路线
以**Blender Python API生成 → STL导出 → OpenFOAM snappyHexMesh**为例，给出可直接复用的流程：
### 步骤 1：生成地面与计算域底板
```python
import bpy, bmesh, random, mathutils
# 清空场景
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
# 城市尺寸（例如 500 m × 500 m，单位=米）
CITY_SIZE = 500
ground = bpy.ops.mesh.primitive_plane_add(size=CITY_SIZE, location=(0,0,0))
ground_obj = bpy.context.active_object
ground_obj.name = 'ground'
```
### 步骤 2：泊松盘采样建筑脚印
```python
import numpy as np
from scipy.spatial import cKDTree
def poisson_disk(W, D, r_min, r_max, seed=42, k=30):
    """在 [-W/2,W/2]×[-D/2,D/2] 内做变半径泊松盘采样"""
    rng = np.random.default_rng(seed)
    samples, active = [], []
    def rand_in_rect(): return (rng.uniform(-W/2, W/2), rng.uniform(-D/2, D/2))
    p0 = rand_in_rect(); samples.append(p0); active.append(p0)
    while active:
        i = rng.integers(len(active)); p = active[i]
        found = False
        for _ in range(k):
            r = rng.uniform(r_min, r_max)
            theta = rng.uniform(0, 2*np.pi)
            q = (p[0]+r*np.cos(theta), p[1]+r*np.sin(theta))
            if abs(q[0])<W/2 and abs(q[1])<D/2:
                ok = True
                for s in samples:
                    d = math.hypot(q[0]-s[0], q[1]-s[1])
                    if d < r_min: ok=False; break
                if ok: samples.append(q); active.append(q); found=True; break
        if not found: active.pop(i)
    return samples
footprints = poisson_disk(CITY_SIZE, CITY_SIZE, r_min=15, r_max=40)
```
### 步骤 3：高度图映射生成建筑体量
```python
def height_map(x, y, H_max=120, sigma=120):
    """中心高、四周低的二维高斯高度图"""
    return H_max * np.exp(-(x**2+y**2)/(2*sigma**2))
buildings = []
for (x, y) in footprints:
    w = rng.uniform(12, 25)   # 宽
    d = rng.uniform(12, 25)   # 深
    h = height_map(x, y) * rng.uniform(0.4, 1.2)   # 加随机扰动
    h = max(h, 10)            # 最小高度
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, h/2))
    b = bpy.context.active_object
    b.scale = (w/2, d/2, h/2)
    b.name = f'bldg_{len(buildings)}'
    buildings.append(b)
```
### 步骤 4：导出为多区域二进制STL
```python
# 把所有建筑 join 成一个 mesh（保证水密合并，避免共面裂缝）
bpy.ops.object.select_all(action='DESELECT')
for b in buildings: b.select_set(True)
bpy.context.view_layer.objects.active = buildings[0]
bpy.ops.object.join()
bpy.context.active_object.name = 'city'
# 与地面做 boolean union，或直接分层导出
# 导出 STL（Blender 的 STL 导出默认 1 unit = 1 m，注意场景单位设置）
bpy.ops.export_mesh.stl(
    filepath='/tmp/city_random.stl',
    use_selection=True,
    ascii=False,           # 二进制，体积更小
    global_scale=1.0       # 确保米单位
)
```
Blender的STL导出器会把场景单位映射到STL数值，需在`Scene Properties > Units`里把`Unit System`设为`Metric`、`Unit Scale`设为`1.0`，避免尺度漂移。
### 步骤 5：OpenFOAM侧的网格划分要点
将STL拷入`constant/triSurface/`，在`snappyHexMeshDict`中：
- 用`surfaceFeatureExtract`提取建筑尖角特征边，让snap阶段贴合90°墙角。
- 对城市面（`city`）设`level (3 4)`附近的加密，对地面（`ground`）可适当放粗。
- **入口边界层**：按对数律/幂律廓线给定 inlet；地面采用固定`0`速度或带粗糙度长度的壁面函数。
- **计算域尺寸**：遵循风工程CFD惯例——以最高建筑 H 为基准，上游取 5H，下游取 15-20H（尾流充分发展），两侧与顶面各 5H，**阻塞比控制在3%以内**。
- **网格无关性**：对同一STL做3档背景网格加密对比，直到目标位置风速/Uref变化 <1%。
---
## 四、工程注意事项
- **单体建筑用盒体而非复杂屋顶**：风工程经验表明，尖角盒体已能复现街谷涡旋与角部分离流；金字塔/坡屋顶会显著增加三角形数量而对宏观风场改进有限。
- **水密优先于细节**：一个非流形顶点就会让snappyHexMesh失败，生成阶段宁可用`Boolean Union`/`Join`牺牲外观，也要保证每个mesh闭合。
- **多区域STL**：将地面与建筑分属不同`solid`块，便于分别设置`ground`（粗糙壁面）与`buildings`（光滑壁面）。
- **尺度检查**：导出后可用`surfaceCheck`（OpenFOAM自带工具）或`meshlab`统计三角面数、检测非流形边与法向量不一致。
- **可复现性**：把泊松盘与高度图的随机种子写入日志，保证同一种子可复现同一城市，用于参数敏感性研究。
**简而言之：算法上"波前法街区 + 泊松盘间距 + 高度图梯度 + 盒体挤出"，格式上"米单位二进制STL + 多区域标记"，工具上"Blender Python生成 + OpenFOAM snappyHexMesh划分"，是风洞CFD随机城市模拟的最优技术栈。**
