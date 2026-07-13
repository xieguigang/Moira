---
name: apply-cfd-player-theme-to-3dview
overview: 将 3d-view.html 引用的 styles.css 在视觉上对齐 cfd-player.html 的设计主题（白卡片、蓝-青-Teal 配色、边框/圆角/阴影、输入框/滑块/开关/spinner 组件样式），保留亮/暗切换按钮，并确保页面默认以 light 主题呈现。
design:
  architecture:
    framework: html
  styleKeywords:
    - Light Theme
    - White Card
    - Blue-Cyan-Teal Accent
    - Clean Minimal
    - Subtle Shadow
  fontSystem:
    fontFamily: Roboto, PingFang SC, Microsoft YaHei, system-ui, sans-serif
    heading:
      size: 13px
      weight: 600
    subheading:
      size: 15px
      weight: 600
    body:
      size: 13px
      weight: 400
  colorSystem:
    primary:
      - "#2563EB"
      - "#0EA5E9"
      - "#14B8A6"
    background:
      - "#F4F6FA"
      - "#FFFFFF"
    text:
      - "#1E293B"
      - "#475569"
      - "#94A3B8"
    functional:
      - "#22C55E"
      - "#EF4444"
      - "#F59E0B"
      - "#0EA5E9"
todos:
  - id: align-tokens
    content: 对齐 styles.css 设计令牌与全局背景/明暗过渡
    status: completed
  - id: align-cards
    content: 将 .panel/.glass 与 .panel-head 对齐为 cfd-player 卡片/标题风格
    status: completed
    dependencies:
      - align-tokens
  - id: align-controls
    content: 对齐输入框/按钮/.chip/.spinner 圆角、聚焦环与 spinner 尺寸
    status: completed
    dependencies:
      - align-tokens
  - id: verify-light
    content: 浏览器核对 3d-view 默认 light 与亮暗切换无回归
    status: completed
    dependencies:
      - align-cards
      - align-controls
---

## 用户需求

将 cfd-player.html 的视觉主题应用到 3d-view.html 页面，使两者外观风格一致；同时 3d-view.html 默认显示 light 颜色主题，并保留亮/暗切换能力。

## 产品概述

仅对 3d-view.html 所引用的 `styles.css` 进行调整，使其设计令牌（颜色、圆角、阴影、边框）与组件视觉（卡片、标题、输入框、按钮、滑块/开关、加载动画）与 cfd-player.html 的 `css/style.css` 主题对齐。不重构 HTML 结构、不改用 cfd-player 的 CSS 文件、不修改 JS 逻辑。页面默认以浅色（light）呈现，右上角主题切换按钮保留并可切到暗色。

## 核心特性

- 设计令牌对齐：蓝-青-Teal 强调色（#2563EB / #0EA5E9 / #14B8A6）、浅灰蓝背景（#F4F6FA）、白色卡片（#FFFFFF）、slate 边框（#E2E8F0）与对应文字色。
- 卡片风格对齐：侧栏各 `.panel`（含 `.glass`）呈现为 cfd-player 风格的白色卡片（1px 边框、14px 圆角、朴素阴影 + 悬停阴影）。
- 面板标题对齐：`.panel-head h2` 采用 `.card-title` 风格（渐变强调条 + 底部分隔线）。
- 表单控件对齐：输入框/按钮/芯片的圆角、聚焦蓝色光环与 cfd-player 一致；加载 spinner 尺寸由 46px 调整为 34px。
- 默认 light 主题且亮/暗切换无回归（保留过渡动画与 three.js 场景同步）。

## 技术栈选择

- 沿用现有项目技术栈：原生 HTML + 自定义 `styles.css`（无框架、无组件库）。
- 仅编辑 `g:/Moira/src/app/styles.css`（3d-view.html 通过 `<link rel="stylesheet" href="./styles.css">` 引用）。不改动 `3d-view.html` 结构与 `js/main.js` 主题逻辑。

## 实现方案

**策略**：以 cfd-player.html 的 `css/style.css` 为视觉基准，将 3d-view.html 的 `styles.css` 中对应的设计令牌与组件样式做"对齐式"微调，使两者观感统一，同时保留 `[data-theme="dark"]` 暗色配色与 `body.theme-anim` 过渡动画（已与 `js/main.js` 的 `applyTheme`/`initTheme` 联动）。

**关键技术决策**：

1. **不改 HTML/JS**：3d-view.html 的 class 命名（`.panel`/`.glass`/`.input-wrap`/`.chip`/`.btn` 等）与 cfd-player 不同，重构成本高且有回归风险；采用"就地对齐样式"而非"换用 cfd-player 的 css/style.css"，最低侵入、可独立验证。
2. **设计令牌已高度一致**：当前 `styles.css` `:root` 已使用相同配色，重点在组件层（卡片内边距/标题/输入框圆角/ spinner 尺寸）的精细化对齐。
3. **保留暗色切换**：`main.js` 的 `initTheme()` 已默认 `light`（兜底 localStorage）；本任务不改变该行为，仅确保样式层默认即浅色无错乱。

**性能与可靠性**：纯 CSS 调整，无运行时开销；原有主题过渡（`transition` 列表、`themeFlash` 动画）完整保留，避免切换闪烁。改动集中在单文件，blast-radius 小。

## 实现要点（执行细节）

- 复用既有 `--primary / --primary-2 / --primary-3 / --panel / --border / --bg-0` 等变量，避免引入新变量名造成 `js/main.js` 中不涉及的断裂（JS 不读取 CSS 变量，安全）。
- `.panel` 在 light 下由半透明 `.glass` 改为更接近 cfd-player `.card` 的纯白实底（保留 `[data-theme="dark"]` 下的 `.glass` 毛玻璃），保证暗色体验不退化。
- `.panel-head h2` 增加 `border-bottom` + `padding-bottom`，强调条由 3px×15px 调整为 3px×13px，字号 15px→13px，对齐 `.card-title`。
- `.input-wrap` / `#resInput` 圆角 10px→9px；聚焦蓝色光环（`box-shadow 0 0 0 3px rgba(37,99,235,.12)`）维持。
- `.spinner` 尺寸 46px→34px，边框与动画不变。
- 滚动条配色已与 cfd-player 一致，核对即可。
- 不触及 `:root` 中 `#3d-view` 用到的 `--viewport-bg-*`、`--glass-*` 等其余变量，保持 three.js 场景同步正常。

## 架构设计

本任务为单文件样式对齐，无新架构。数据流不变：HTML(class) → styles.css(样式) → 浏览器渲染；主题切换仍由 `js/main.js` 写 `data-theme` 并 lerp three.js 调色板。

## 目录结构

```
g:/Moira/src/app/
└── styles.css          # [MODIFY] 3d-view.html 引用的样式表。对齐 cfd-player 主题：
                        #  - :root 设计令牌核对（颜色/圆角/阴影/边框）
                        #  - .panel/.glass 卡片化（白底、1px 边框、14px 圆角、朴素+悬停阴影）
                        #  - .panel-head h2 改为 .card-title 风格（13px、底部分隔线、3px×13px 渐变条）
                        #  - .input-wrap / #resInput 圆角 9px、聚焦蓝环
                        #  - .spinner 46px → 34px
                        #  - 保留 [data-theme="dark"] 与过渡动画
└── 3d-view.html        # [不动] 结构保持；仅确认默认 light（initTheme 已满足）
└── js/main.js          # [不动] 主题切换/场景同步逻辑保持
```

## 设计风格

采用 cfd-player.html 的浅色卡片式主题：浅灰蓝背景（#F4F6FA）、纯白卡片（#FFFFFF）配 1px slate 边框与 14px 圆角，柔和投影；标题以蓝-青-Teal 渐变强调条点缀；交互元素（按钮、输入框、芯片）统一圆角与聚焦蓝色光环。整体干净、专业、低饱和度，强调信息层级而非装饰。暗色模式保留为可切换项。

## 页面关系

仅 3d-view.html 单页，其视觉系统统一对齐 cfd-player.html 的 `css/style.css` 设计语言，无新增页面。