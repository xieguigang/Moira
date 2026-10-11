---
name: fluidbox-axis-orientation-fix
overview: 在 FluidSceneCanvas 中引入固定的求解器→场景基映射 W（求解器 Z 竖直轴 → 场景 −Y 即屏幕上方，求解器 Y 深度轴 → 场景 Z），组合旋转 M = W·R 应用于粒子云、地面网格、盒子线框与坐标轴 gizmo，使盒子在屏幕上直立、重力视觉向下、网格位于底部，gizmo 显示求解器坐标轴（Z 向上、X 向右、Y 指向屏幕内），不再有轴指向下方。
todos:
  - id: world-rotation-matrix
    content: 在 FluidSceneCanvas.vb 新增 WorldRotation() 函数计算 M=W·R 组合旋转矩阵
    status: completed
  - id: apply-mapping
    content: 将 PushFrame 的 packer 请求、UploadBoxLines 的地面网格、DrawBoxWire 的盒棱全部改用 M 变换
    status: completed
  - id: fix-axis-gizmo
    content: 修改 DrawAxisGizmo 用 M 投影求解器单位轴，使 Z 向上、X 向右、Y 指向屏幕内，并同步修正相关注释（含 BoxShakeController.vb 的错误声明）
    status: completed
  - id: build-verify
    content: 编译 FluidBox 项目并运行 --selftest 验证无错误，确认盒子直立、重力向下、网格在底部、gizmo 无轴朝下
    status: completed
---

## Product Overview

修复 FluidBox 三维场景的坐标轴显示方向问题：用户运行窗体程序后发现左下角坐标轴 gizmo 中 Y 轴指向屏幕下方，与常规三维场景认知相反，要求重新调整整个三维坐标轴的显示方向。

## Root Cause（已核实）

- 场景相机约定为“屏幕竖直方向 = 世界 Y 轴”：`Camera.RotatePoint` 公式下世界 +Y 投影到屏幕 (−0.17, +0.94) 即竖直向下；GPU 常量 `ViewportScale.y = −2/h` 注释"the screen y axis points downwards"证实
- 求解器坐标系是 Z-up（重力沿 −z、液体在 z 底部、网格在 z=−hz），目前以恒等映射直接送入场景
- 结果：盒子竖直轴在屏幕上指向左上约 36°，gizmo 如实显示场景系（Y 朝下），相机 yaw 环绕轴错为深度轴

## Core Features

- 引入固定基映射 W（solver_z → 场景 −Y 即屏幕上方；solver_y → 场景 Z），盒子在屏幕上直立、重力视觉向下、地面网格位于盒子底部
- 坐标轴 gizmo 改为显示求解器坐标系方向：Z 向上、X 向右、Y 指向屏幕内（深度），不再有轴指向下方
- 不反转重力场、不移动网格数据位置（求解器内部约定保持不变）
- 相机 yaw 环绕轴变为盒子竖直轴，轨道交互变为正确的转台语义

## Tech Stack

- 现有项目：VB.NET WinForms（FluidBox.vbproj，net10.0-windows），DxScene3DCanvas（D3D11 点云管线）+ SPH 求解器
- 全部修改限于 `FluidSceneCanvas.vb` 与注释修正，不改动共享渲染库 DXApi 与求解器

## Implementation Approach

- 在 `FluidSceneCanvas` 中新增私有函数 `WorldRotation()`：计算组合旋转 M = W·R，其中 R 为 `m_shake.Rotation`（行主序 3x3），W 为固定基映射（scene_x = solver_x；scene_y = −solver_z；scene_z = solver_y），即 M = (r0, r1, r2, −r6, −r7, −r8, r3, r4, r5)
- 将 `PushFrame` 的 `m_packer.Request(...)`、`UploadBoxLines` 的 `BuildGroundGrid(...)`、`DrawBoxWire` 的 `Transform(...)` 全部改传 M（`SpeedHeatMapper.BuildInto` 的变换为 p_scene = R·(p_solver − center)，偏移先减后旋转，直接兼容）
- `DrawAxisGizmo` 改用 M 投影三个求解器单位轴，使标签方向与求解器语义一致（Z 向上、X 向右、Y 指向屏幕内）
- 数学验证（默认相机 AngleX=20°/AngleY=−30°）：solver Z → 屏幕 (0.17, −0.94) 向上；重力 → 向下；网格 z=−hz → 屏幕；solver X → 向右；solver Y → 屏幕内左上；W 正交且 det=+1

## Key Code Structures

```
''' <summary>
''' the combined rotation of the shake tilt and the fixed basis mapping
''' from the solver frame (z up, y depth) into the scene frame
''' (the scene y axis points downwards on the screen)
''' </summary>
Private Function WorldRotation() As Single()
    Dim r As Single() = m_shake.Rotation
    Return New Single() {r(0), r(1), r(2),
                         -r(6), -r(7), -r(8),
                         r(3), r(4), r(5)}
End Function
```

## Performance Notes

- WorldRotation() 为 9 次乘法的纯计算，每帧调用三次以内，可忽略
- 不增加任何 GPU/内存开销，不影响现有节流与打包管线