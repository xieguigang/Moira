# CFDEngine 仿真结果查看器

基于 **VTK.js** 的浏览器端 CFD 结果可视化页面，用于查看 `CFDEngine` demo 输出的 `.vti` 时间序列。

相比旧的 JSON 快照（128³ 时约 360 MB/帧、200 帧约 70 GB，浏览器根本加载不动），
新的 `.vti` 是 **Float32 二进制 + 去派生场**，64³ 单帧约 **5.5 MB**，
而且页面**一次只加载一帧**，内存里永远只有一帧数据。

---

## 1. 先跑 demo 生成数据

在 `test` 项目输出目录下运行（默认已是 64³，50 步，每 5 步存一帧）：

```powershell
dotnet test.dll --size 64 --steps 50 --interval 5
```

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
- **色表**：青蓝 / 琥珀 / 翠绿
- **不透明度**：整体不透明度上限
- **下阈值 / 上阈值**：传输函数的有效区间，用来滤掉背景噪声、突出结构

### 正交切片
- 勾选「启用切片」后，X / Y / Z 三个方向的切片平面可分别拖动定位
- 切片与体绘制共用同一套颜色/不透明度传输函数

### 流线 / 矢量箭头
- 在速度场上做 **RK2（中点法）积分**生成流线，按速度大小着色
- 可调**种子密度**（每轴种子数）与**积分步数**
- 可切换「流线」/「箭头」形态
- 流线由本页自行积分生成 `vtkPolyData`，不依赖 vtk.js 里 API 不稳定的 filter

### 时间轴
- 播放 / 暂停 / 上一帧 / 下一帧
- 拖动进度条直接跳帧
- 播放帧率 2 / 5 / 10 / 20 FPS 可选，可开循环

### 状态栏
- 实时 FPS、当前帧文件大小、全场最大速度 |v|max、平均速度 |v|avg

---

## 4. VTK.js 依赖说明

页面通过 **esm.sh** 从 CDN 加载 `@kitware/vtk.js@37.0.0`
（该版本是纯 ESM 包，浏览器无法直接解析裸模块名，需要 CDN 做依赖改写）。

### CDN 不可达时的本地兜底

页面顶部会显示红色的错误条提示，而不是白屏。此时改为本地加载：

```powershell
cd viz
npm i @kitware/vtk.js@37.0.0
```

然后编辑 `app.js` 顶部的常量：

```js
// 改前
const VTK_BASE = `https://esm.sh/@kitware/vtk.js@${VTK_VERSION}`;
// 改后
const VTK_BASE = './node_modules/@kitware/vtk.js';
```

> 本地方式仍需要一个支持裸模块名解析的开发服务器（例如 `npx vite`），
> 或者改用 import map。若只是临时离线查看，更简单的方式是把 esm.sh 换成
> 任意可达的镜像 CDN。

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
