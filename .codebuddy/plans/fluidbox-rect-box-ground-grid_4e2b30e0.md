---
name: fluidbox-rect-box-ground-grid
overview: 将 FluidBox 的三维场景盒子从正方体改为长方体（宽/高/深三个 UI 输入框可配置），模拟边界与粒子点阵同步改为 width×height×depth，并在盒子底面添加随盒子一起旋转的网格地面，用于标记盒子放在地面上的位置。
todos:
  - id: sim-rect-box
    content: 修改 FluidBoxSim.vb：构造函数与点阵生成支持 width×height×depth，新增 BoxWidth/BoxHeight/BoxDepth 属性，边界改为 BoxBoundary3D(width,height,depth)
    status: completed
  - id: canvas-rect-grid
    content: 修改 FluidSceneCanvas.vb：三向半尺寸替换 BoxSize/m_half，BuildBoxEdges 支持三轴，新增 BuildGroundGrid 随盒子旋转的底面网格并合并进 UploadBoxLines
    status: completed
    dependencies:
      - sim-rect-box
  - id: selftest-adapt
    content: 更新 ApplicationEvents.vb 中 --selftest 为新构造签名与三向 mapper 偏移
    status: completed
    dependencies:
      - sim-rect-box
  - id: ui-box-size-inputs
    content: 在 Form1.Designer.vb 参数面板新增宽/高/深三个 NumericUpDown 输入，Form1.vb 的 StartLoading 读取输入创建模拟并钳制范围
    status: completed
    dependencies:
      - sim-rect-box
  - id: build-verify
    content: 编译 FluidBox 项目验证无错误，检查重置按钮重建后渲染与模拟盒子一致、地面网格随倾斜旋转
    status: completed
    dependencies:
      - canvas-rect-grid
      - selftest-adapt
      - ui-box-size-inputs
---

## 产品概述

优化 FluidBox 的三维场景：将 SPH 流体盒子从正方体（100×100×100）改为长方体，并在盒子底面添加网格地面，直观标记地面位置、清晰展示"盒子放在地面上"的效果。

## 核心功能

- **长方体盒子**：将模拟边界与渲染线框从正方体改为 width×height×depth 长方体，渲染与模拟尺寸严格一致，粒子不会超出可视盒子
- **尺寸 UI 可配置**：在窗体参数区新增宽度/高度/深度三个输入框（默认 宽160 × 高100 × 深120），通过已有"重置"按钮重建模拟以应用新尺寸
- **网格地面**：在盒子底面（y = -高/2 平面）生成网格线段，范围略大于盒底 footprint（约 1.5 倍），淡灰半透明，与盒子 12 条棱一起随倾斜/摇晃旋转，随相机可从任意角度看清盒子与地面的关系

## 技术栈

- 现有项目：VB.NET WinForms（FluidBox.vbproj），3D 渲染基于 `DxScene3DCanvas`（Direct3D11 点云管线 + LineSegment 线框），SPH 求解器 `FluidEngine3D` + `BoxBoundary3D`
- 全部修改复用现有模式，不引入新依赖

## 实现方案

### 1. 模拟层（FluidBoxSim.vb）

- 构造函数改为接收 `width/height/depth`（保留单一 `boxSize` 可选参数兼容旧调用：三边相等）
- 新增只读属性 `BoxWidth/BoxHeight/BoxDepth`，`BoxSize` 保留为最大边或标记 Obsolete（selftest 与 canvas 引用处同步更新）
- 点阵生成改为按 `nx = floor(width/want)`、`ny = floor(LiquidHeight/want)`、`nz = round(count/(nx*ny))` 三维独立求解，`sx=width/nx`、`sy=LiquidHeight/ny`、`sz=depth/nz`
- 边界改为 `New BoxBoundary3D(width, height, depth)`；`LiquidHeight = height * fillFraction`

### 2. 渲染层（FluidSceneCanvas.vb）

- `BoxSize/m_half` 替换为 `m_hx/m_hy/m_hz` 三个半尺寸（`Attach` 从 sim 读取）
- `BuildBoxEdges(r, hx, hy, hz)`：8 角点按三个半尺寸生成，12 棱逻辑不变
- 新增 `BuildGroundGrid(r, hx, hz)`：在局部坐标 y=-hy 平面上生成网格线段——沿 X 方向与 Z 方向各若干条平行线，覆盖范围取 `max(hx,hz)*1.5` 的方形 footprint，颜色淡灰半透明（如 `Color.FromArgb(70, 120, 135, 150)`），与盒棱一同乘 `m_shake.Rotation` 后随盒子旋转
- `UploadBoxLines` 内将盒棱与地面网格合并为一次 `UpdateConnections` 调用（约 20+ 条线段，开销可忽略），复用现有 `LineTiltEpsilon` + 50ms 节流，不额外增加 Scene.Version 重建频率
- `PushFrame` 中 `m_packer.Request(..., m_hx, m_hy, m_hz)` 传入三向半尺寸（InstancePacker/SpeedHeatMapper.SetTransform 本就支持三向偏移，无需修改）

### 3. UI 层（Form1.Designer.vb / Form1.vb）

- Designer 中在右侧参数面板 `m_params` 顶部新增一组"盒子尺寸"host Panel：三个 `NumericUpDown`（宽/高/深，范围 40–400，默认 160/100/120）+ 标签，遵循现有滑块 host 的 Dock/BackColor/字体模式
- Form1 增加字段 `m_boxWidth/m_boxHeight/m_boxDepth`；`StartLoading` 读取三个输入框的值创建 `FluidBoxSim(width, height, depth, ...)`；数值变更后提示"点击重置应用新尺寸"（复用现有 `OnResetClick` → `StartLoading` 重建链路，不新增重建按钮）

### 4. 冒烟测试适配（ApplicationEvents.vb）

- `--selftest` 改用新构造签名，`mapper.SetTransform(...)` 的偏移改为 `sim.BoxWidth/2, sim.BoxHeight/2, sim.BoxDepth/2`，并在日志中输出三维尺寸

### 性能与可靠性

- 线段重建仍走现有节流路径，网格线为固定拓扑（数量随尺寸不变），无逐帧分配压力
- 输入框值做范围钳制，防止非法尺寸（≤0）导致点阵生成异常