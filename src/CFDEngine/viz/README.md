# CFDEngine 仿真结果查看器

基于 **VTK.js** 的浏览器端 CFD 结果可视化页面，用于查看 `CFDEngine` demo 输出的 `.vti` 时间序列。

相比旧的 JSON 快照（128³ 时约 360 MB/帧、200 帧约 70 GB，浏览器根本加载不动），
新的 `.vti` 是 **Float32 二进制 + 去派生场**，64³ 单帧约 **5.5 MB**，
而且页面**一次只加载一帧**，内存里永远只有一帧数据。

---

## 1. 先跑 demo 生成数据

在 `test` 项目输出目录下运行（默认已是 64³、50 步、每 10 步存一帧）：

```powershell
dotnet test.dll --size 64 --steps 50 --interval 5
```

> 上面的 `--interval 5` 会得到 10 帧（step 5/10/…/50），与本页示例数据一致；
> 直接用默认值则每 10 步一帧，共 5 帧。

可选参数：

| 参数 | 说明 | 默认 |
|---|---|---|
| `--size N` | 网格边长 N（立方体 N³） | 64 |
| `--steps N` | 时间步数 | 50 |
| `--interval N` | 每隔多少步存一帧 | 10 |
| `--capsule` | 用胶囊形计算空间替代立方体 | 关闭 |
| `--vti` / `--vtk` / `--json` | 快照格式 | `--vti` |
| `--bench` | 纯求解器基准，不写任何文件 | 关闭 |

产物目录（`test/bin/x64/Release/net10.0/frames/`）：

```
frames/
  ├── frame_0000.vti      # 二进制 Float32：mask / pressure / density / velocity
  ├── frame_0001.vti
  ├── ...
  ├── animation.pvd       # ParaView 时间集合（桌面端可直接打开播放）
  └── frames.json         # 浏览器端帧清单
```

> `animation.pvd` 用 **ParaView** 打开即可播放时间动画；本页读的是 `frames.json`。

---

## 2. 启动页面

**不能直接双击 `index.html`** —— 浏览器在 `file://` 下会阻止 `fetch`。必须走 HTTP：

```powershell
cd viz
.\serve.ps1              # 默认 8080
# 或
.\serve.ps1 9000         # 指定端口
```

等价的手动方式：

```powershell
node serve.js 8080                 # 推荐，零依赖
python -m http.server 8080         # 在项目根目录执行
```

然后浏览器打开：<http://localhost:8080/viz/index.html>

### 换一个数据目录

页面默认从 `../test/bin/x64/Release/net10.0/frames` 读取。用 `?frames=` 覆盖：

```
http://localhost:8080/viz/index.html?frames=myrun/frames
```

---

## 3. 界面功能

### 体绘制
- **标量场**：`密度 density` / `速度 speed` / `压力 pressure`
  （`speed` 由 `velocity` 三分量在前端现算，导出时未落盘以省体积）
- **色表**：青蓝 / 琥珀 / 翠绿（右下色条会跟着切换）
- **不透明度**：整体不透明度上限
- **下阈值 / 上阈值**：传输函数的有效区间，用来滤掉背景噪声、突出结构

### 正交切片
- 勾选「启用切片」后，X / Y / Z 三个方向的切片平面可分别拖动定位
- 切片**不复用**体绘制的传送函数：体绘制靠沿视线累积几十个采样点出效果，
  而切片只有一层采样。密度场里 97% 以上的体素接近 0，若沿用"低值 0 不透明 +
  起始色接近黑"的映射，整张切片会黑到看不见。
  因此切片单独一套映射——调色板不变，但最暗一档提亮到可见的暗色调，
  并给不透明度加了下限，保证切片平面始终可见。

### 流线 / 矢量箭头
- 在速度场上做 **RK2（中点法）积分**生成流线，按速度大小着色
- 可调**种子密度**（每轴种子数）与**积分步数**
- **形态**可在「流线」/「箭头」之间切换：
  - 流线：整条轨迹做成细管
  - 箭头：每个种子点画一根沿当地速度方向的"针"，长度按速度归一化
- 速度取色用**对数归一化**。速度场常横跨两个数量级（本例 p50≈3、p90≈59），
  线性映射会把绝大多数流线压到色标最底端，看起来"整片是暗的"
- 两种形态都过 TubeFilter 加粗：WebGL 核心配置下 `lineWidth > 1` 无效，
  裸线段细到看不见
- 流线由本页自行积分生成 `vtkPolyData`，不依赖 vtk.js 里 API 不稳定的 filter

### 时间轴
- 播放 / 暂停 / 上一帧 / 下一帧
- 拖动进度条直接跳帧
- 播放帧率 2 / 5 / 10 / 20 FPS 可选，可开循环

### 状态栏
- 实时 FPS、当前帧文件大小、全场最大速度 |v|max、平均速度 |v|avg

---

## 4. VTK.js 依赖说明

`index.html` 里用 **import map** 把裸模块名映射到 **jsDelivr**：

```html
<script type="importmap">
{ "imports": { "@kitware/vtk.js/": "https://cdn.jsdelivr.net/npm/@kitware/vtk.js@37.0.0/" } }
</script>
```

vtk.js v37 是纯 ESM 包，且内部各模块通过 `registerOverride` 共享一份全局类注册表。
因此必须让浏览器按「原始单文件 ESM」逐个加载（相对导入解析到同一 URL），
**不能**用 esm.sh 那类"按入口整体打包"的方式——打包会让注册表出现多份副本，
渲染 Profile 注册不到渲染器上，报 `No vtkOpenGLViewNodeFactory implementation found`。

> 首次打开需要拉取约 250 个模块（几秒到数十秒，取决于网络），之后走浏览器缓存。
> 期间顶部状态显示「加载 VTK.js…」。

### CDN 不可达时的本地兜底

页面顶部会显示红色的错误条提示，而不是白屏。此时改为本地加载：

```powershell
cd viz
npm i @kitware/vtk.js@37.0.0
```

然后把 `index.html` 的 import map 指向本地目录：

```json
"@kitware/vtk.js/": "./node_modules/@kitware/vtk.js/"
```

> 本地方式同样需要一个能按真实路径提供 ESM 文件的服务器（本仓库的 `serve.js`
> 即可），不需要打包器。

---

## 5. 文件说明

| 文件 | 作用 |
|---|---|
| `index.html` | 页面骨架：顶部导航、左侧控制面板、中央视口、底部时间轴、右下状态栏 |
| `app.js` | 全部逻辑：VTK.js 加载、`.vti` 解析、体积渲染、切片、流线积分、播放控制 |
| `style.css` | 深色科学仪表盘样式（玻璃拟态 + 青色/琥珀强调） |
| `serve.js` | 零依赖 Node 静态服务器 |
| `serve.ps1` | 一键启动脚本（node 优先，python 兜底） |

---

## 6. 常见问题

**Q：页面提示「读不到 frames.json」**
A：先跑一次 demo 生成数据；或确认 `?frames=` 指向的目录下确实有 `frames.json`。

**Q：体积渲染一片漆黑 / 什么都看不见**
A：把「下阈值」往左拖（降低显示下限），并把「不透明度」调高。
密度场在充分混合后数值很小，默认阈值可能把它全滤掉了。

**Q：流线看不到**
A：确认已勾选「启用流线」；密度场静止时速度接近 0 会画不出流线，
可以切到压力/速度场观察，或增加「积分步数」。

**Q：数据太大浏览器还是卡**
A：减小 `--interval` 的倒数（即增大采样间隔，少存几帧），
或用更小的 `--size` 重跑。128³ 单帧约 44 MB，建议配合切片查看而不是全量体绘制。
