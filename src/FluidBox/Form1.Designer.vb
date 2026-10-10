Imports System.Drawing
Imports System.Windows.Forms
Imports FluidBox.Rendering

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        m_body = New Panel()
        m_stage = New Panel()
        m_canvas = New FluidSceneCanvas()
        m_hud = New Label()
        m_legend = New Panel()
        m_legendName = New Label()
        m_legendMax = New Label()
        m_legendMin = New Label()
        m_legendBar = New Panel()
        m_params = New Panel()
        m_lblParamsTitle = New Label()
        m_gravityHost = New Panel()
        m_gravityBar = New TrackBar()
        m_gravityRead = New Label()
        m_gravityName = New Label()
        m_viscosityHost = New Panel()
        m_viscosityBar = New TrackBar()
        m_viscosityRead = New Label()
        m_viscosityName = New Label()
        m_dampingHost = New Panel()
        m_dampingBar = New TrackBar()
        m_dampingRead = New Label()
        m_dampingName = New Label()
        m_soundHost = New Panel()
        m_soundBar = New TrackBar()
        m_soundRead = New Label()
        m_soundName = New Label()
        m_shakeHost = New Panel()
        m_shakeBar = New TrackBar()
        m_shakeRead = New Label()
        m_shakeName = New Label()
        m_tiltHost = New Panel()
        m_tiltBar = New TrackBar()
        m_tiltRead = New Label()
        m_tiltName = New Label()
        m_lblParamsHint = New Label()
        m_gapParams = New Panel()
        m_status = New Panel()
        m_lstMemory = New Label()
        m_lstRender = New Label()
        m_lstStep = New Label()
        m_lstBackend = New Label()
        m_toolbar = New Panel()
        m_flow = New FlowLayoutPanel()
        m_btnRun = New Button()
        m_gap1 = New Panel()
        m_btnStep = New Button()
        m_gap2 = New Panel()
        m_btnReset = New Button()
        m_gap3 = New Panel()
        m_btnFit = New Button()
        m_gap4 = New Panel()
        m_gap5 = New Panel()
        m_lblPalette = New Label()
        m_paletteBox = New ComboBox()
        m_gap6 = New Panel()
        m_lblPointSize = New Label()
        m_sizeBar = New TrackBar()
        m_loading = New Panel()
        m_loadingStack = New Panel()
        m_loadingPct = New Label()
        m_loadingBar = New Panel()
        m_loadingText = New Label()
        m_body.SuspendLayout()
        m_stage.SuspendLayout()
        m_legend.SuspendLayout()
        m_params.SuspendLayout()
        m_gravityHost.SuspendLayout()
        CType(m_gravityBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_viscosityHost.SuspendLayout()
        CType(m_viscosityBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_dampingHost.SuspendLayout()
        CType(m_dampingBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_soundHost.SuspendLayout()
        CType(m_soundBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_shakeHost.SuspendLayout()
        CType(m_shakeBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_tiltHost.SuspendLayout()
        CType(m_tiltBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_status.SuspendLayout()
        m_toolbar.SuspendLayout()
        m_flow.SuspendLayout()
        CType(m_sizeBar, System.ComponentModel.ISupportInitialize).BeginInit()
        m_loading.SuspendLayout()
        m_loadingStack.SuspendLayout()
        SuspendLayout()
        ' 
        ' m_body
        ' 
        m_body.BackColor = Color.White
        m_body.Controls.Add(m_stage)
        m_body.Controls.Add(m_params)
        m_body.Dock = DockStyle.Fill
        m_body.Location = New Point(0, 52)
        m_body.Name = "m_body"
        m_body.Size = New Size(1440, 818)
        m_body.TabIndex = 0
        ' 
        ' m_stage
        ' 
        m_stage.BackColor = Color.White
        m_stage.Controls.Add(m_canvas)
        m_stage.Controls.Add(m_hud)
        m_stage.Controls.Add(m_legend)
        m_stage.Dock = DockStyle.Fill
        m_stage.Location = New Point(0, 0)
        m_stage.Name = "m_stage"
        m_stage.Size = New Size(1168, 818)
        m_stage.TabIndex = 0
        ' 
        ' m_canvas
        ' 
        m_canvas.AutoClear = False
        m_canvas.BackColor = Color.White
        m_canvas.BackgroundColor = Color.FromArgb(CByte(237), CByte(250), CByte(254))
        m_canvas.BackgroundGridColor = Color.Black
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Location = New Point(0, 0)
        m_canvas.Margin = New Padding(0)
        m_canvas.Name = "m_canvas"
        m_canvas.Size = New Size(1168, 818)
        m_canvas.TabIndex = 0
        ' 
        ' m_hud
        ' 
        m_hud.BackColor = Color.White
        m_hud.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_hud.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_hud.Location = New Point(0, 0)
        m_hud.Name = "m_hud"
        m_hud.Padding = New Padding(8, 0, 0, 0)
        m_hud.Size = New Size(360, 22)
        m_hud.TabIndex = 1
        m_hud.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_legend
        ' 
        m_legend.BackColor = Color.White
        m_legend.Controls.Add(m_legendName)
        m_legend.Controls.Add(m_legendMax)
        m_legend.Controls.Add(m_legendMin)
        m_legend.Controls.Add(m_legendBar)
        m_legend.Location = New Point(0, 0)
        m_legend.Name = "m_legend"
        m_legend.Padding = New Padding(12, 10, 12, 8)
        m_legend.Size = New Size(236, 96)
        m_legend.TabIndex = 2
        ' 
        ' m_legendName
        ' 
        m_legendName.Dock = DockStyle.Bottom
        m_legendName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_legendName.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_legendName.Location = New Point(12, 70)
        m_legendName.Name = "m_legendName"
        m_legendName.Size = New Size(212, 18)
        m_legendName.TabIndex = 0
        m_legendName.Text = "Jet"
        ' 
        ' m_legendMax
        ' 
        m_legendMax.Dock = DockStyle.Top
        m_legendMax.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_legendMax.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_legendMax.Location = New Point(12, 44)
        m_legendMax.Name = "m_legendMax"
        m_legendMax.Size = New Size(212, 18)
        m_legendMax.TabIndex = 1
        m_legendMax.Text = "0.00 m/s"
        m_legendMax.TextAlign = ContentAlignment.TopRight
        ' 
        ' m_legendMin
        ' 
        m_legendMin.Dock = DockStyle.Top
        m_legendMin.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_legendMin.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_legendMin.Location = New Point(12, 26)
        m_legendMin.Name = "m_legendMin"
        m_legendMin.Size = New Size(212, 18)
        m_legendMin.TabIndex = 2
        m_legendMin.Text = "0.00 m/s"
        ' 
        ' m_legendBar
        ' 
        m_legendBar.Dock = DockStyle.Top
        m_legendBar.Location = New Point(12, 10)
        m_legendBar.Name = "m_legendBar"
        m_legendBar.Size = New Size(212, 16)
        m_legendBar.TabIndex = 3
        ' 
        ' m_params
        ' 
        m_params.AutoScroll = True
        m_params.BackColor = Color.White
        m_params.Controls.Add(m_lblParamsTitle)
        m_params.Controls.Add(m_gravityHost)
        m_params.Controls.Add(m_viscosityHost)
        m_params.Controls.Add(m_dampingHost)
        m_params.Controls.Add(m_soundHost)
        m_params.Controls.Add(m_shakeHost)
        m_params.Controls.Add(m_tiltHost)
        m_params.Controls.Add(m_lblParamsHint)
        m_params.Controls.Add(m_gapParams)
        m_params.Dock = DockStyle.Right
        m_params.Location = New Point(1168, 0)
        m_params.Name = "m_params"
        m_params.Padding = New Padding(14, 12, 14, 12)
        m_params.Size = New Size(272, 818)
        m_params.TabIndex = 1
        ' 
        ' m_lblParamsTitle
        ' 
        m_lblParamsTitle.Dock = DockStyle.Top
        m_lblParamsTitle.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        m_lblParamsTitle.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_lblParamsTitle.Location = New Point(14, 416)
        m_lblParamsTitle.Name = "m_lblParamsTitle"
        m_lblParamsTitle.Size = New Size(244, 26)
        m_lblParamsTitle.TabIndex = 0
        m_lblParamsTitle.Text = "物理参数"
        ' 
        ' m_gravityHost
        ' 
        m_gravityHost.BackColor = Color.White
        m_gravityHost.Controls.Add(m_gravityBar)
        m_gravityHost.Controls.Add(m_gravityRead)
        m_gravityHost.Controls.Add(m_gravityName)
        m_gravityHost.Dock = DockStyle.Top
        m_gravityHost.Location = New Point(14, 364)
        m_gravityHost.Name = "m_gravityHost"
        m_gravityHost.Size = New Size(244, 52)
        m_gravityHost.TabIndex = 1
        ' 
        ' m_gravityBar
        ' 
        m_gravityBar.BackColor = Color.White
        m_gravityBar.Dock = DockStyle.Top
        m_gravityBar.Location = New Point(0, 18)
        m_gravityBar.Maximum = 300
        m_gravityBar.Name = "m_gravityBar"
        m_gravityBar.Size = New Size(186, 45)
        m_gravityBar.TabIndex = 0
        m_gravityBar.TickStyle = TickStyle.None
        m_gravityBar.Value = 98
        ' 
        ' m_gravityRead
        ' 
        m_gravityRead.Dock = DockStyle.Right
        m_gravityRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_gravityRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_gravityRead.Location = New Point(186, 18)
        m_gravityRead.Name = "m_gravityRead"
        m_gravityRead.Size = New Size(58, 34)
        m_gravityRead.TabIndex = 1
        m_gravityRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_gravityName
        ' 
        m_gravityName.Dock = DockStyle.Top
        m_gravityName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_gravityName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_gravityName.Location = New Point(0, 0)
        m_gravityName.Name = "m_gravityName"
        m_gravityName.Size = New Size(244, 18)
        m_gravityName.TabIndex = 2
        m_gravityName.Text = "重力 (m/s²)"
        ' 
        ' m_viscosityHost
        ' 
        m_viscosityHost.BackColor = Color.White
        m_viscosityHost.Controls.Add(m_viscosityBar)
        m_viscosityHost.Controls.Add(m_viscosityRead)
        m_viscosityHost.Controls.Add(m_viscosityName)
        m_viscosityHost.Dock = DockStyle.Top
        m_viscosityHost.Location = New Point(14, 312)
        m_viscosityHost.Name = "m_viscosityHost"
        m_viscosityHost.Size = New Size(244, 52)
        m_viscosityHost.TabIndex = 2
        ' 
        ' m_viscosityBar
        ' 
        m_viscosityBar.BackColor = Color.White
        m_viscosityBar.Dock = DockStyle.Top
        m_viscosityBar.Location = New Point(0, 18)
        m_viscosityBar.Maximum = 100
        m_viscosityBar.Name = "m_viscosityBar"
        m_viscosityBar.Size = New Size(186, 45)
        m_viscosityBar.TabIndex = 0
        m_viscosityBar.TickStyle = TickStyle.None
        m_viscosityBar.Value = 30
        ' 
        ' m_viscosityRead
        ' 
        m_viscosityRead.Dock = DockStyle.Right
        m_viscosityRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_viscosityRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_viscosityRead.Location = New Point(186, 18)
        m_viscosityRead.Name = "m_viscosityRead"
        m_viscosityRead.Size = New Size(58, 34)
        m_viscosityRead.TabIndex = 1
        m_viscosityRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_viscosityName
        ' 
        m_viscosityName.Dock = DockStyle.Top
        m_viscosityName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_viscosityName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_viscosityName.Location = New Point(0, 0)
        m_viscosityName.Name = "m_viscosityName"
        m_viscosityName.Size = New Size(244, 18)
        m_viscosityName.TabIndex = 2
        m_viscosityName.Text = "粘性强度"
        ' 
        ' m_dampingHost
        ' 
        m_dampingHost.BackColor = Color.White
        m_dampingHost.Controls.Add(m_dampingBar)
        m_dampingHost.Controls.Add(m_dampingRead)
        m_dampingHost.Controls.Add(m_dampingName)
        m_dampingHost.Dock = DockStyle.Top
        m_dampingHost.Location = New Point(14, 260)
        m_dampingHost.Name = "m_dampingHost"
        m_dampingHost.Size = New Size(244, 52)
        m_dampingHost.TabIndex = 3
        ' 
        ' m_dampingBar
        ' 
        m_dampingBar.BackColor = Color.White
        m_dampingBar.Dock = DockStyle.Top
        m_dampingBar.Location = New Point(0, 18)
        m_dampingBar.Maximum = 100
        m_dampingBar.Name = "m_dampingBar"
        m_dampingBar.Size = New Size(186, 45)
        m_dampingBar.TabIndex = 0
        m_dampingBar.TickStyle = TickStyle.None
        m_dampingBar.Value = 40
        ' 
        ' m_dampingRead
        ' 
        m_dampingRead.Dock = DockStyle.Right
        m_dampingRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_dampingRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_dampingRead.Location = New Point(186, 18)
        m_dampingRead.Name = "m_dampingRead"
        m_dampingRead.Size = New Size(58, 34)
        m_dampingRead.TabIndex = 1
        m_dampingRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_dampingName
        ' 
        m_dampingName.Dock = DockStyle.Top
        m_dampingName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_dampingName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_dampingName.Location = New Point(0, 0)
        m_dampingName.Name = "m_dampingName"
        m_dampingName.Size = New Size(244, 18)
        m_dampingName.TabIndex = 2
        m_dampingName.Text = "碰撞阻尼"
        ' 
        ' m_soundHost
        ' 
        m_soundHost.BackColor = Color.White
        m_soundHost.Controls.Add(m_soundBar)
        m_soundHost.Controls.Add(m_soundRead)
        m_soundHost.Controls.Add(m_soundName)
        m_soundHost.Dock = DockStyle.Top
        m_soundHost.Location = New Point(14, 208)
        m_soundHost.Name = "m_soundHost"
        m_soundHost.Size = New Size(244, 52)
        m_soundHost.TabIndex = 4
        ' 
        ' m_soundBar
        ' 
        m_soundBar.BackColor = Color.White
        m_soundBar.Dock = DockStyle.Top
        m_soundBar.Location = New Point(0, 18)
        m_soundBar.Maximum = 100
        m_soundBar.Minimum = 10
        m_soundBar.Name = "m_soundBar"
        m_soundBar.Size = New Size(186, 45)
        m_soundBar.TabIndex = 0
        m_soundBar.TickStyle = TickStyle.None
        m_soundBar.Value = 30
        ' 
        ' m_soundRead
        ' 
        m_soundRead.Dock = DockStyle.Right
        m_soundRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_soundRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_soundRead.Location = New Point(186, 18)
        m_soundRead.Name = "m_soundRead"
        m_soundRead.Size = New Size(58, 34)
        m_soundRead.TabIndex = 1
        m_soundRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_soundName
        ' 
        m_soundName.Dock = DockStyle.Top
        m_soundName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_soundName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_soundName.Location = New Point(0, 0)
        m_soundName.Name = "m_soundName"
        m_soundName.Size = New Size(244, 18)
        m_soundName.TabIndex = 2
        m_soundName.Text = "声速因子"
        ' 
        ' m_shakeHost
        ' 
        m_shakeHost.BackColor = Color.White
        m_shakeHost.Controls.Add(m_shakeBar)
        m_shakeHost.Controls.Add(m_shakeRead)
        m_shakeHost.Controls.Add(m_shakeName)
        m_shakeHost.Dock = DockStyle.Top
        m_shakeHost.Location = New Point(14, 156)
        m_shakeHost.Name = "m_shakeHost"
        m_shakeHost.Size = New Size(244, 52)
        m_shakeHost.TabIndex = 5
        ' 
        ' m_shakeBar
        ' 
        m_shakeBar.BackColor = Color.White
        m_shakeBar.Dock = DockStyle.Top
        m_shakeBar.Location = New Point(0, 18)
        m_shakeBar.Maximum = 300
        m_shakeBar.Name = "m_shakeBar"
        m_shakeBar.Size = New Size(186, 45)
        m_shakeBar.TabIndex = 0
        m_shakeBar.TickStyle = TickStyle.None
        m_shakeBar.Value = 100
        ' 
        ' m_shakeRead
        ' 
        m_shakeRead.Dock = DockStyle.Right
        m_shakeRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_shakeRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_shakeRead.Location = New Point(186, 18)
        m_shakeRead.Name = "m_shakeRead"
        m_shakeRead.Size = New Size(58, 34)
        m_shakeRead.TabIndex = 1
        m_shakeRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_shakeName
        ' 
        m_shakeName.Dock = DockStyle.Top
        m_shakeName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_shakeName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_shakeName.Location = New Point(0, 0)
        m_shakeName.Name = "m_shakeName"
        m_shakeName.Size = New Size(244, 18)
        m_shakeName.TabIndex = 2
        m_shakeName.Text = "晃动强度"
        ' 
        ' m_tiltHost
        ' 
        m_tiltHost.BackColor = Color.White
        m_tiltHost.Controls.Add(m_tiltBar)
        m_tiltHost.Controls.Add(m_tiltRead)
        m_tiltHost.Controls.Add(m_tiltName)
        m_tiltHost.Dock = DockStyle.Top
        m_tiltHost.Location = New Point(14, 104)
        m_tiltHost.Name = "m_tiltHost"
        m_tiltHost.Size = New Size(244, 52)
        m_tiltHost.TabIndex = 6
        ' 
        ' m_tiltBar
        ' 
        m_tiltBar.BackColor = Color.White
        m_tiltBar.Dock = DockStyle.Top
        m_tiltBar.Location = New Point(0, 18)
        m_tiltBar.Maximum = 60
        m_tiltBar.Minimum = 3
        m_tiltBar.Name = "m_tiltBar"
        m_tiltBar.Size = New Size(186, 45)
        m_tiltBar.TabIndex = 0
        m_tiltBar.TickStyle = TickStyle.None
        m_tiltBar.Value = 26
        ' 
        ' m_tiltRead
        ' 
        m_tiltRead.Dock = DockStyle.Right
        m_tiltRead.Font = New Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
        m_tiltRead.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_tiltRead.Location = New Point(186, 18)
        m_tiltRead.Name = "m_tiltRead"
        m_tiltRead.Size = New Size(58, 34)
        m_tiltRead.TabIndex = 1
        m_tiltRead.TextAlign = ContentAlignment.MiddleRight
        ' 
        ' m_tiltName
        ' 
        m_tiltName.Dock = DockStyle.Top
        m_tiltName.Font = New Font("Microsoft YaHei UI", 8.5F)
        m_tiltName.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_tiltName.Location = New Point(0, 0)
        m_tiltName.Name = "m_tiltName"
        m_tiltName.Size = New Size(244, 18)
        m_tiltName.TabIndex = 2
        m_tiltName.Text = "最大倾角 (°)"
        ' 
        ' m_lblParamsHint
        ' 
        m_lblParamsHint.Dock = DockStyle.Top
        m_lblParamsHint.Font = New Font("Microsoft YaHei UI", 8F)
        m_lblParamsHint.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblParamsHint.Location = New Point(14, 26)
        m_lblParamsHint.Name = "m_lblParamsHint"
        m_lblParamsHint.Size = New Size(244, 78)
        m_lblParamsHint.TabIndex = 7
        m_lblParamsHint.Text = "左键拖拽 = 晃动盒子（惯性 + 倾斜）" & vbCrLf & "右键拖拽 = 环绕视角" & vbCrLf & "滚轮 = 缩放" & vbCrLf & "物理在后台线程推进，画面始终显示最新一帧。"
        ' 
        ' m_gapParams
        ' 
        m_gapParams.Dock = DockStyle.Top
        m_gapParams.Location = New Point(14, 12)
        m_gapParams.Name = "m_gapParams"
        m_gapParams.Size = New Size(244, 14)
        m_gapParams.TabIndex = 8
        ' 
        ' m_status
        ' 
        m_status.BackColor = Color.White
        m_status.Controls.Add(m_lstMemory)
        m_status.Controls.Add(m_lstRender)
        m_status.Controls.Add(m_lstStep)
        m_status.Controls.Add(m_lstBackend)
        m_status.Dock = DockStyle.Bottom
        m_status.Location = New Point(0, 870)
        m_status.Name = "m_status"
        m_status.Size = New Size(1440, 30)
        m_status.TabIndex = 1
        ' 
        ' m_lstMemory
        ' 
        m_lstMemory.Dock = DockStyle.Fill
        m_lstMemory.Font = New Font("Consolas", 8.5F)
        m_lstMemory.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lstMemory.Location = New Point(840, 0)
        m_lstMemory.Name = "m_lstMemory"
        m_lstMemory.Padding = New Padding(12, 0, 0, 0)
        m_lstMemory.Size = New Size(600, 30)
        m_lstMemory.TabIndex = 0
        m_lstMemory.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_lstRender
        ' 
        m_lstRender.Dock = DockStyle.Left
        m_lstRender.Font = New Font("Consolas", 8.5F)
        m_lstRender.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lstRender.Location = New Point(560, 0)
        m_lstRender.Name = "m_lstRender"
        m_lstRender.Padding = New Padding(12, 0, 0, 0)
        m_lstRender.Size = New Size(280, 30)
        m_lstRender.TabIndex = 1
        m_lstRender.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_lstStep
        ' 
        m_lstStep.Dock = DockStyle.Left
        m_lstStep.Font = New Font("Consolas", 8.5F)
        m_lstStep.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lstStep.Location = New Point(300, 0)
        m_lstStep.Name = "m_lstStep"
        m_lstStep.Padding = New Padding(12, 0, 0, 0)
        m_lstStep.Size = New Size(260, 30)
        m_lstStep.TabIndex = 2
        m_lstStep.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_lstBackend
        ' 
        m_lstBackend.Dock = DockStyle.Left
        m_lstBackend.Font = New Font("Consolas", 8.5F)
        m_lstBackend.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lstBackend.Location = New Point(0, 0)
        m_lstBackend.Name = "m_lstBackend"
        m_lstBackend.Padding = New Padding(12, 0, 0, 0)
        m_lstBackend.Size = New Size(300, 30)
        m_lstBackend.TabIndex = 3
        m_lstBackend.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' m_toolbar
        ' 
        m_toolbar.BackColor = Color.White
        m_toolbar.Controls.Add(m_flow)
        m_toolbar.Dock = DockStyle.Top
        m_toolbar.Location = New Point(0, 0)
        m_toolbar.Name = "m_toolbar"
        m_toolbar.Padding = New Padding(12, 0, 12, 0)
        m_toolbar.Size = New Size(1440, 52)
        m_toolbar.TabIndex = 2
        ' 
        ' m_flow
        ' 
        m_flow.BackColor = Color.White
        m_flow.Controls.Add(m_btnRun)
        m_flow.Controls.Add(m_gap1)
        m_flow.Controls.Add(m_btnStep)
        m_flow.Controls.Add(m_gap2)
        m_flow.Controls.Add(m_btnReset)
        m_flow.Controls.Add(m_gap3)
        m_flow.Controls.Add(m_btnFit)
        m_flow.Controls.Add(m_gap4)
        m_flow.Controls.Add(m_gap5)
        m_flow.Controls.Add(m_lblPalette)
        m_flow.Controls.Add(m_paletteBox)
        m_flow.Controls.Add(m_gap6)
        m_flow.Controls.Add(m_lblPointSize)
        m_flow.Controls.Add(m_sizeBar)
        m_flow.Dock = DockStyle.Fill
        m_flow.Location = New Point(12, 0)
        m_flow.Name = "m_flow"
        m_flow.Padding = New Padding(0, 11, 0, 0)
        m_flow.Size = New Size(1416, 52)
        m_flow.TabIndex = 0
        m_flow.WrapContents = False
        ' 
        ' m_btnRun
        ' 
        m_btnRun.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        m_btnRun.Cursor = Cursors.Hand
        m_btnRun.FlatAppearance.BorderSize = 0
        m_btnRun.FlatStyle = FlatStyle.Flat
        m_btnRun.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnRun.ForeColor = Color.White
        m_btnRun.Location = New Point(0, 11)
        m_btnRun.Margin = New Padding(0)
        m_btnRun.Name = "m_btnRun"
        m_btnRun.Size = New Size(108, 28)
        m_btnRun.TabIndex = 0
        m_btnRun.Text = "▶  开始"
        m_btnRun.UseVisualStyleBackColor = False
        ' 
        ' m_gap1
        ' 
        m_gap1.BackColor = Color.White
        m_gap1.Location = New Point(111, 14)
        m_gap1.Name = "m_gap1"
        m_gap1.Size = New Size(8, 1)
        m_gap1.TabIndex = 1
        ' 
        ' m_btnStep
        ' 
        m_btnStep.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        m_btnStep.Cursor = Cursors.Hand
        m_btnStep.FlatAppearance.BorderSize = 0
        m_btnStep.FlatStyle = FlatStyle.Flat
        m_btnStep.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnStep.ForeColor = Color.White
        m_btnStep.Location = New Point(122, 11)
        m_btnStep.Margin = New Padding(0)
        m_btnStep.Name = "m_btnStep"
        m_btnStep.Size = New Size(78, 28)
        m_btnStep.TabIndex = 2
        m_btnStep.Text = "单步"
        m_btnStep.UseVisualStyleBackColor = False
        ' 
        ' m_gap2
        ' 
        m_gap2.BackColor = Color.White
        m_gap2.Location = New Point(203, 14)
        m_gap2.Name = "m_gap2"
        m_gap2.Size = New Size(8, 1)
        m_gap2.TabIndex = 3
        ' 
        ' m_btnReset
        ' 
        m_btnReset.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        m_btnReset.Cursor = Cursors.Hand
        m_btnReset.FlatAppearance.BorderSize = 0
        m_btnReset.FlatStyle = FlatStyle.Flat
        m_btnReset.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnReset.ForeColor = Color.White
        m_btnReset.Location = New Point(214, 11)
        m_btnReset.Margin = New Padding(0)
        m_btnReset.Name = "m_btnReset"
        m_btnReset.Size = New Size(78, 28)
        m_btnReset.TabIndex = 4
        m_btnReset.Text = "重置"
        m_btnReset.UseVisualStyleBackColor = False
        ' 
        ' m_gap3
        ' 
        m_gap3.BackColor = Color.White
        m_gap3.Location = New Point(295, 14)
        m_gap3.Name = "m_gap3"
        m_gap3.Size = New Size(8, 1)
        m_gap3.TabIndex = 5
        ' 
        ' m_btnFit
        ' 
        m_btnFit.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        m_btnFit.Cursor = Cursors.Hand
        m_btnFit.FlatAppearance.BorderSize = 0
        m_btnFit.FlatStyle = FlatStyle.Flat
        m_btnFit.Font = New Font("Microsoft YaHei UI", 9F)
        m_btnFit.ForeColor = Color.White
        m_btnFit.Location = New Point(306, 11)
        m_btnFit.Margin = New Padding(0)
        m_btnFit.Name = "m_btnFit"
        m_btnFit.Size = New Size(96, 28)
        m_btnFit.TabIndex = 6
        m_btnFit.Text = "视角复位"
        m_btnFit.UseVisualStyleBackColor = False
        ' 
        ' m_gap4
        ' 
        m_gap4.BackColor = Color.White
        m_gap4.Location = New Point(405, 14)
        m_gap4.Name = "m_gap4"
        m_gap4.Size = New Size(20, 1)
        m_gap4.TabIndex = 7
        ' 
        ' m_gap5
        ' 
        m_gap5.BackColor = Color.White
        m_gap5.Location = New Point(431, 14)
        m_gap5.Name = "m_gap5"
        m_gap5.Size = New Size(14, 1)
        m_gap5.TabIndex = 8
        ' 
        ' m_lblPalette
        ' 
        m_lblPalette.AutoSize = True
        m_lblPalette.Font = New Font("Microsoft YaHei UI", 9F)
        m_lblPalette.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblPalette.Location = New Point(448, 16)
        m_lblPalette.Margin = New Padding(0, 5, 0, 0)
        m_lblPalette.Name = "m_lblPalette"
        m_lblPalette.Size = New Size(32, 17)
        m_lblPalette.TabIndex = 9
        m_lblPalette.Text = "色表"
        ' 
        ' m_paletteBox
        ' 
        m_paletteBox.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_paletteBox.DropDownStyle = ComboBoxStyle.DropDownList
        m_paletteBox.FlatStyle = FlatStyle.Flat
        m_paletteBox.Font = New Font("Microsoft YaHei UI", 9F)
        m_paletteBox.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_paletteBox.Location = New Point(480, 11)
        m_paletteBox.Margin = New Padding(0)
        m_paletteBox.Name = "m_paletteBox"
        m_paletteBox.Size = New Size(118, 25)
        m_paletteBox.TabIndex = 10
        ' 
        ' m_gap6
        ' 
        m_gap6.BackColor = Color.White
        m_gap6.Location = New Point(601, 14)
        m_gap6.Name = "m_gap6"
        m_gap6.Size = New Size(14, 1)
        m_gap6.TabIndex = 11
        ' 
        ' m_lblPointSize
        ' 
        m_lblPointSize.AutoSize = True
        m_lblPointSize.Font = New Font("Microsoft YaHei UI", 9F)
        m_lblPointSize.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        m_lblPointSize.Location = New Point(618, 16)
        m_lblPointSize.Margin = New Padding(0, 5, 0, 0)
        m_lblPointSize.Name = "m_lblPointSize"
        m_lblPointSize.Size = New Size(44, 17)
        m_lblPointSize.TabIndex = 12
        m_lblPointSize.Text = "点大小"
        ' 
        ' m_sizeBar
        ' 
        m_sizeBar.BackColor = Color.White
        m_sizeBar.Location = New Point(665, 14)
        m_sizeBar.Maximum = 6
        m_sizeBar.Minimum = 1
        m_sizeBar.Name = "m_sizeBar"
        m_sizeBar.Size = New Size(96, 45)
        m_sizeBar.TabIndex = 13
        m_sizeBar.TickStyle = TickStyle.None
        m_sizeBar.Value = 2
        ' 
        ' m_loading
        ' 
        m_loading.BackColor = Color.White
        m_loading.Controls.Add(m_loadingStack)
        m_loading.Dock = DockStyle.Fill
        m_loading.Location = New Point(0, 0)
        m_loading.Name = "m_loading"
        m_loading.Size = New Size(1440, 900)
        m_loading.TabIndex = 3
        ' 
        ' m_loadingStack
        ' 
        m_loadingStack.Controls.Add(m_loadingPct)
        m_loadingStack.Controls.Add(m_loadingBar)
        m_loadingStack.Controls.Add(m_loadingText)
        m_loadingStack.Location = New Point(0, 0)
        m_loadingStack.Name = "m_loadingStack"
        m_loadingStack.Size = New Size(560, 120)
        m_loadingStack.TabIndex = 0
        ' 
        ' m_loadingPct
        ' 
        m_loadingPct.Font = New Font("Microsoft YaHei UI", 9.5F)
        m_loadingPct.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        m_loadingPct.Location = New Point(20, 66)
        m_loadingPct.Name = "m_loadingPct"
        m_loadingPct.Size = New Size(520, 20)
        m_loadingPct.TabIndex = 0
        m_loadingPct.Text = "0%"
        m_loadingPct.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' m_loadingBar
        ' 
        m_loadingBar.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        m_loadingBar.Location = New Point(70, 48)
        m_loadingBar.Name = "m_loadingBar"
        m_loadingBar.Size = New Size(420, 8)
        m_loadingBar.TabIndex = 1
        ' 
        ' m_loadingText
        ' 
        m_loadingText.Font = New Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
        m_loadingText.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        m_loadingText.Location = New Point(20, 8)
        m_loadingText.Name = "m_loadingText"
        m_loadingText.Size = New Size(520, 24)
        m_loadingText.TabIndex = 2
        m_loadingText.Text = "正在生成液体盒子 …"
        m_loadingText.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 17F)
        AutoScaleMode = AutoScaleMode.Font
        BackColor = Color.White
        ClientSize = New Size(1440, 900)
        Controls.Add(m_body)
        Controls.Add(m_status)
        Controls.Add(m_toolbar)
        Controls.Add(m_loading)
        Font = New Font("Microsoft YaHei UI", 9F)
        ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        MinimumSize = New Size(1024, 640)
        Name = "Form1"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Fluid Box · 3D SPH 液体盒子模拟器"
        WindowState = FormWindowState.Maximized
        m_body.ResumeLayout(False)
        m_stage.ResumeLayout(False)
        m_legend.ResumeLayout(False)
        m_params.ResumeLayout(False)
        m_gravityHost.ResumeLayout(False)
        m_gravityHost.PerformLayout()
        CType(m_gravityBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_viscosityHost.ResumeLayout(False)
        m_viscosityHost.PerformLayout()
        CType(m_viscosityBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_dampingHost.ResumeLayout(False)
        m_dampingHost.PerformLayout()
        CType(m_dampingBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_soundHost.ResumeLayout(False)
        m_soundHost.PerformLayout()
        CType(m_soundBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_shakeHost.ResumeLayout(False)
        m_shakeHost.PerformLayout()
        CType(m_shakeBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_tiltHost.ResumeLayout(False)
        m_tiltHost.PerformLayout()
        CType(m_tiltBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_status.ResumeLayout(False)
        m_toolbar.ResumeLayout(False)
        m_flow.ResumeLayout(False)
        m_flow.PerformLayout()
        CType(m_sizeBar, System.ComponentModel.ISupportInitialize).EndInit()
        m_loading.ResumeLayout(False)
        m_loadingStack.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

End Class
