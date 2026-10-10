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
        Me.components = New System.ComponentModel.Container()
        Me.SuspendLayout()
        '
        'Form1
        '
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(1440, 900)
        Me.MinimumSize = New Size(1024, 640)
        Me.Text = "Fluid Box · 3D SPH 液体盒子模拟器"
        Me.BackColor = Color.FromArgb(11, 15, 20)
        Me.ForeColor = Color.FromArgb(230, 237, 243)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.WindowState = FormWindowState.Maximized
        '
        'm_body
        '
        Me.m_body = New Panel()
        Me.m_body.Dock = DockStyle.Fill
        Me.m_body.BackColor = Color.FromArgb(11, 15, 20)
        '
        'm_params
        '
        Me.m_params = New Panel()
        Me.m_params.Dock = DockStyle.Right
        Me.m_params.Width = 272
        Me.m_params.BackColor = Color.FromArgb(19, 26, 34)
        Me.m_params.Padding = New Padding(14, 12, 14, 12)
        Me.m_params.AutoScroll = True
        '
        'm_stage
        '
        Me.m_stage = New Panel()
        Me.m_stage.Dock = DockStyle.Fill
        Me.m_stage.BackColor = Color.FromArgb(11, 15, 20)
        '
        'm_canvas
        '
        Me.m_canvas = New FluidSceneCanvas()
        Me.m_canvas.Dock = DockStyle.Fill
        Me.m_canvas.BackColor = Color.FromArgb(11, 15, 20)
        Me.m_canvas.Margin = New Padding(0)
        '
        'm_legend
        '
        Me.m_legend = New Panel()
        Me.m_legend.Width = 236
        Me.m_legend.Height = 96
        Me.m_legend.BackColor = Color.FromArgb(19, 26, 34)
        Me.m_legend.Padding = New Padding(12, 10, 12, 8)
        '
        'm_legendBar
        '
        Me.m_legendBar = New Panel()
        Me.m_legendBar.Height = 16
        Me.m_legendBar.Dock = DockStyle.Top
        '
        'm_legendMin
        '
        Me.m_legendMin = New Label()
        Me.m_legendMin.Dock = DockStyle.Top
        Me.m_legendMin.Height = 18
        Me.m_legendMin.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_legendMin.Font = New Font("Segoe UI", 8.5F)
        Me.m_legendMin.Text = "0.00 m/s"
        '
        'm_legendMax
        '
        Me.m_legendMax = New Label()
        Me.m_legendMax.Dock = DockStyle.Top
        Me.m_legendMax.Height = 18
        Me.m_legendMax.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_legendMax.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_legendMax.Text = "0.00 m/s"
        Me.m_legendMax.TextAlign = ContentAlignment.TopRight
        '
        'm_legendName
        '
        Me.m_legendName = New Label()
        Me.m_legendName.Dock = DockStyle.Bottom
        Me.m_legendName.Height = 18
        Me.m_legendName.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_legendName.Font = New Font("Segoe UI", 8.5F)
        Me.m_legendName.Text = "Jet"
        '
        'm_hud
        '
        Me.m_hud = New Label()
        Me.m_hud.Width = 360
        Me.m_hud.Height = 22
        Me.m_hud.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_hud.Font = New Font("Segoe UI", 8.5F)
        Me.m_hud.BackColor = Color.FromArgb(19, 26, 34)
        Me.m_hud.TextAlign = ContentAlignment.MiddleLeft
        Me.m_hud.Padding = New Padding(8, 0, 0, 0)
        Me.m_hud.Text = ""
        '
        'm_status
        '
        Me.m_status = New Panel()
        Me.m_status.Dock = DockStyle.Bottom
        Me.m_status.Height = 30
        Me.m_status.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_lstBackend
        '
        Me.m_lstBackend = New Label()
        Me.m_lstBackend.Width = 300
        Me.m_lstBackend.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_lstBackend.Font = New Font("Consolas", 8.5F)
        Me.m_lstBackend.TextAlign = ContentAlignment.MiddleLeft
        Me.m_lstBackend.AutoSize = False
        Me.m_lstBackend.Padding = New Padding(12, 0, 0, 0)
        Me.m_lstBackend.Dock = DockStyle.Left
        '
        'm_lstStep
        '
        Me.m_lstStep = New Label()
        Me.m_lstStep.Width = 260
        Me.m_lstStep.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_lstStep.Font = New Font("Consolas", 8.5F)
        Me.m_lstStep.TextAlign = ContentAlignment.MiddleLeft
        Me.m_lstStep.AutoSize = False
        Me.m_lstStep.Padding = New Padding(12, 0, 0, 0)
        Me.m_lstStep.Dock = DockStyle.Left
        '
        'm_lstRender
        '
        Me.m_lstRender = New Label()
        Me.m_lstRender.Width = 280
        Me.m_lstRender.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_lstRender.Font = New Font("Consolas", 8.5F)
        Me.m_lstRender.TextAlign = ContentAlignment.MiddleLeft
        Me.m_lstRender.AutoSize = False
        Me.m_lstRender.Padding = New Padding(12, 0, 0, 0)
        Me.m_lstRender.Dock = DockStyle.Left
        '
        'm_lstMemory
        '
        Me.m_lstMemory = New Label()
        Me.m_lstMemory.Width = 320
        Me.m_lstMemory.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_lstMemory.Font = New Font("Consolas", 8.5F)
        Me.m_lstMemory.TextAlign = ContentAlignment.MiddleLeft
        Me.m_lstMemory.AutoSize = False
        Me.m_lstMemory.Padding = New Padding(12, 0, 0, 0)
        Me.m_lstMemory.Dock = DockStyle.Fill
        '
        'm_toolbar
        '
        Me.m_toolbar = New Panel()
        Me.m_toolbar.Dock = DockStyle.Top
        Me.m_toolbar.Height = 52
        Me.m_toolbar.BackColor = Color.FromArgb(19, 26, 34)
        Me.m_toolbar.Padding = New Padding(12, 0, 12, 0)
        '
        'm_flow
        '
        Me.m_flow = New FlowLayoutPanel()
        Me.m_flow.Dock = DockStyle.Fill
        Me.m_flow.BackColor = Color.FromArgb(19, 26, 34)
        Me.m_flow.FlowDirection = FlowDirection.LeftToRight
        Me.m_flow.WrapContents = False
        Me.m_flow.Padding = New Padding(0, 11, 0, 0)
        '
        'm_btnRun
        '
        Me.m_btnRun = New Button()
        Me.m_btnRun.Text = "▶  开始"
        Me.m_btnRun.Width = 108
        Me.m_btnRun.Height = 28
        Me.m_btnRun.FlatStyle = FlatStyle.Flat
        Me.m_btnRun.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_btnRun.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_btnRun.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Me.m_btnRun.Cursor = Cursors.Hand
        Me.m_btnRun.Margin = New Padding(0)
        Me.m_btnRun.FlatAppearance.BorderSize = 1
        Me.m_btnRun.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191)
        Me.m_btnRun.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59)
        Me.m_btnRun.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 212, 191)
        '
        'm_btnStep
        '
        Me.m_btnStep = New Button()
        Me.m_btnStep.Text = "单步"
        Me.m_btnStep.Width = 78
        Me.m_btnStep.Height = 28
        Me.m_btnStep.FlatStyle = FlatStyle.Flat
        Me.m_btnStep.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_btnStep.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_btnStep.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Me.m_btnStep.Cursor = Cursors.Hand
        Me.m_btnStep.Margin = New Padding(0)
        Me.m_btnStep.FlatAppearance.BorderSize = 1
        Me.m_btnStep.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191)
        Me.m_btnStep.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59)
        Me.m_btnStep.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 212, 191)
        '
        'm_btnReset
        '
        Me.m_btnReset = New Button()
        Me.m_btnReset.Text = "重置"
        Me.m_btnReset.Width = 78
        Me.m_btnReset.Height = 28
        Me.m_btnReset.FlatStyle = FlatStyle.Flat
        Me.m_btnReset.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_btnReset.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_btnReset.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Me.m_btnReset.Cursor = Cursors.Hand
        Me.m_btnReset.Margin = New Padding(0)
        Me.m_btnReset.FlatAppearance.BorderSize = 1
        Me.m_btnReset.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191)
        Me.m_btnReset.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59)
        Me.m_btnReset.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 212, 191)
        '
        'm_btnFit
        '
        Me.m_btnFit = New Button()
        Me.m_btnFit.Text = "视角复位"
        Me.m_btnFit.Width = 96
        Me.m_btnFit.Height = 28
        Me.m_btnFit.FlatStyle = FlatStyle.Flat
        Me.m_btnFit.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_btnFit.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_btnFit.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Me.m_btnFit.Cursor = Cursors.Hand
        Me.m_btnFit.Margin = New Padding(0)
        Me.m_btnFit.FlatAppearance.BorderSize = 1
        Me.m_btnFit.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191)
        Me.m_btnFit.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59)
        Me.m_btnFit.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 212, 191)
        '
        'm_paletteBox
        '
        Me.m_paletteBox = New ComboBox()
        Me.m_paletteBox.DropDownStyle = ComboBoxStyle.DropDownList
        Me.m_paletteBox.Width = 118
        Me.m_paletteBox.Height = 26
        Me.m_paletteBox.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_paletteBox.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_paletteBox.Font = New Font("Segoe UI", 9.0F)
        Me.m_paletteBox.FlatStyle = FlatStyle.Flat
        Me.m_paletteBox.Margin = New Padding(0)
        '
        'm_sizeBar
        '
        Me.m_sizeBar = New TrackBar()
        Me.m_sizeBar.Minimum = 1
        Me.m_sizeBar.Maximum = 6
        Me.m_sizeBar.Value = 2
        Me.m_sizeBar.Width = 96
        Me.m_sizeBar.Height = 28
        Me.m_sizeBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_flow content
        '
        Me.m_flow.Controls.Add(Me.m_btnRun)
        Me.m_flow.Controls.Add(New Panel() With {.Width = 8, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(Me.m_btnStep)
        Me.m_flow.Controls.Add(New Panel() With {.Width = 8, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(Me.m_btnReset)
        Me.m_flow.Controls.Add(New Panel() With {.Width = 8, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(Me.m_btnFit)
        Me.m_flow.Controls.Add(New Panel() With {.Width = 20, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(New Panel() With {.Width = 14, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(New Label() With {
            .Text = "色表",
            .AutoSize = True,
            .ForeColor = Color.FromArgb(139, 152, 165),
            .Font = New Font("Segoe UI", 9.0F),
            .Margin = New Padding(0, 5, 0, 0),
            .Padding = New Padding(0)})
        Me.m_flow.Controls.Add(Me.m_paletteBox)
        Me.m_flow.Controls.Add(New Panel() With {.Width = 14, .Height = 1, .BackColor = Color.FromArgb(19, 26, 34)})
        Me.m_flow.Controls.Add(New Label() With {
            .Text = "点大小",
            .AutoSize = True,
            .ForeColor = Color.FromArgb(139, 152, 165),
            .Font = New Font("Segoe UI", 9.0F),
            .Margin = New Padding(0, 5, 0, 0),
            .Padding = New Padding(0)})
        Me.m_flow.Controls.Add(Me.m_sizeBar)
        Me.m_toolbar.Controls.Add(Me.m_flow)
        '
        'm_loading
        '
        Me.m_loading = New Panel()
        Me.m_loading.BackColor = Color.FromArgb(11, 15, 20)
        Me.m_loading.Dock = DockStyle.Fill
        '
        'm_loadingText
        '
        Me.m_loadingText = New Label()
        Me.m_loadingText.Text = "正在生成液体盒子 …"
        Me.m_loadingText.AutoSize = False
        Me.m_loadingText.Width = 520
        Me.m_loadingText.Height = 24
        Me.m_loadingText.ForeColor = Color.FromArgb(230, 237, 243)
        Me.m_loadingText.Font = New Font("Segoe UI", 12.0F, FontStyle.Bold)
        Me.m_loadingText.TextAlign = ContentAlignment.MiddleCenter
        Me.m_loadingText.Location = New Point(20, 8)
        '
        'm_loadingPct
        '
        Me.m_loadingPct = New Label()
        Me.m_loadingPct.Text = "0%"
        Me.m_loadingPct.AutoSize = False
        Me.m_loadingPct.Width = 520
        Me.m_loadingPct.Height = 20
        Me.m_loadingPct.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_loadingPct.Font = New Font("Segoe UI", 9.5F)
        Me.m_loadingPct.TextAlign = ContentAlignment.MiddleCenter
        Me.m_loadingPct.Location = New Point(20, 66)
        '
        'm_loadingBar
        '
        Me.m_loadingBar = New Panel()
        Me.m_loadingBar.Width = 420
        Me.m_loadingBar.Height = 8
        Me.m_loadingBar.BackColor = Color.FromArgb(27, 36, 48)
        Me.m_loadingBar.Location = New Point(70, 48)
        '
        'm_loadingStack
        '
        Me.m_loadingStack = New Panel()
        Me.m_loadingStack.Width = 560
        Me.m_loadingStack.Height = 120
        Me.m_loadingStack.Controls.Add(Me.m_loadingPct)
        Me.m_loadingStack.Controls.Add(Me.m_loadingBar)
        Me.m_loadingStack.Controls.Add(Me.m_loadingText)
        Me.m_loading.Controls.Add(Me.m_loadingStack)
        '
        'm_legend children
        '
        Me.m_legend.Controls.Add(Me.m_legendName)
        Me.m_legend.Controls.Add(Me.m_legendMax)
        Me.m_legend.Controls.Add(Me.m_legendMin)
        Me.m_legend.Controls.Add(Me.m_legendBar)
        '
        'm_stage children
        '
        Me.m_stage.Controls.Add(Me.m_canvas)
        Me.m_stage.Controls.Add(Me.m_hud)
        Me.m_stage.Controls.Add(Me.m_legend)
        Me.m_legend.BringToFront()
        Me.m_hud.BringToFront()
        '
        'm_body children
        '
        Me.m_body.Controls.Add(Me.m_stage)
        Me.m_body.Controls.Add(Me.m_params)
        '
        'm_status children
        '
        Me.m_status.Controls.Add(Me.m_lstMemory)
        Me.m_status.Controls.Add(Me.m_lstRender)
        Me.m_status.Controls.Add(Me.m_lstStep)
        Me.m_status.Controls.Add(Me.m_lstBackend)
        '
        'm_params: title
        '
        Me.m_params.Controls.Add(New Label() With {
            .Text = "物理参数",
            .Dock = DockStyle.Top,
            .Height = 26,
            .ForeColor = Color.FromArgb(230, 237, 243),
            .Font = New Font("Segoe UI", 11.0F, FontStyle.Bold)})
        '
        'm_gravityHost
        '
        Me.m_gravityHost = New Panel()
        Me.m_gravityHost.Dock = DockStyle.Top
        Me.m_gravityHost.Height = 52
        Me.m_gravityHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_gravityBar
        '
        Me.m_gravityBar = New TrackBar()
        '
        'm_gravityRead
        '
        Me.m_gravityRead = New Label()
        Me.m_gravityRead.Dock = DockStyle.Right
        Me.m_gravityRead.Width = 58
        Me.m_gravityRead.Height = 18
        Me.m_gravityRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_gravityRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_gravityRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_gravityName
        '
        Me.m_gravityName = New Label()
        Me.m_gravityName.Dock = DockStyle.Top
        Me.m_gravityName.Height = 18
        Me.m_gravityName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_gravityName.Font = New Font("Segoe UI", 8.5F)
        Me.m_gravityName.Text = "重力 (m/s²)"
        '
        Me.m_gravityHost.Controls.Add(Me.m_gravityBar)
        Me.m_gravityHost.Controls.Add(Me.m_gravityRead)
        Me.m_gravityHost.Controls.Add(Me.m_gravityName)
        Me.m_params.Controls.Add(Me.m_gravityHost)
        Me.m_gravityBar.Minimum = 0
        Me.m_gravityBar.Maximum = 300
        Me.m_gravityBar.Value = 98
        Me.m_gravityBar.Dock = DockStyle.Top
        Me.m_gravityBar.Height = 30
        Me.m_gravityBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_viscosityHost
        '
        Me.m_viscosityHost = New Panel()
        Me.m_viscosityHost.Dock = DockStyle.Top
        Me.m_viscosityHost.Height = 52
        Me.m_viscosityHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_viscosityBar
        '
        Me.m_viscosityBar = New TrackBar()
        '
        'm_viscosityRead
        '
        Me.m_viscosityRead = New Label()
        Me.m_viscosityRead.Dock = DockStyle.Right
        Me.m_viscosityRead.Width = 58
        Me.m_viscosityRead.Height = 18
        Me.m_viscosityRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_viscosityRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_viscosityRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_viscosityName
        '
        Me.m_viscosityName = New Label()
        Me.m_viscosityName.Dock = DockStyle.Top
        Me.m_viscosityName.Height = 18
        Me.m_viscosityName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_viscosityName.Font = New Font("Segoe UI", 8.5F)
        Me.m_viscosityName.Text = "粘性强度"
        '
        Me.m_viscosityHost.Controls.Add(Me.m_viscosityBar)
        Me.m_viscosityHost.Controls.Add(Me.m_viscosityRead)
        Me.m_viscosityHost.Controls.Add(Me.m_viscosityName)
        Me.m_params.Controls.Add(Me.m_viscosityHost)
        Me.m_viscosityBar.Minimum = 0
        Me.m_viscosityBar.Maximum = 100
        Me.m_viscosityBar.Value = 30
        Me.m_viscosityBar.Dock = DockStyle.Top
        Me.m_viscosityBar.Height = 30
        Me.m_viscosityBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_dampingHost
        '
        Me.m_dampingHost = New Panel()
        Me.m_dampingHost.Dock = DockStyle.Top
        Me.m_dampingHost.Height = 52
        Me.m_dampingHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_dampingBar
        '
        Me.m_dampingBar = New TrackBar()
        '
        'm_dampingRead
        '
        Me.m_dampingRead = New Label()
        Me.m_dampingRead.Dock = DockStyle.Right
        Me.m_dampingRead.Width = 58
        Me.m_dampingRead.Height = 18
        Me.m_dampingRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_dampingRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_dampingRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_dampingName
        '
        Me.m_dampingName = New Label()
        Me.m_dampingName.Dock = DockStyle.Top
        Me.m_dampingName.Height = 18
        Me.m_dampingName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_dampingName.Font = New Font("Segoe UI", 8.5F)
        Me.m_dampingName.Text = "碰撞阻尼"
        '
        Me.m_dampingHost.Controls.Add(Me.m_dampingBar)
        Me.m_dampingHost.Controls.Add(Me.m_dampingRead)
        Me.m_dampingHost.Controls.Add(Me.m_dampingName)
        Me.m_params.Controls.Add(Me.m_dampingHost)
        Me.m_dampingBar.Minimum = 0
        Me.m_dampingBar.Maximum = 100
        Me.m_dampingBar.Value = 40
        Me.m_dampingBar.Dock = DockStyle.Top
        Me.m_dampingBar.Height = 30
        Me.m_dampingBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_soundHost
        '
        Me.m_soundHost = New Panel()
        Me.m_soundHost.Dock = DockStyle.Top
        Me.m_soundHost.Height = 52
        Me.m_soundHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_soundBar
        '
        Me.m_soundBar = New TrackBar()
        '
        'm_soundRead
        '
        Me.m_soundRead = New Label()
        Me.m_soundRead.Dock = DockStyle.Right
        Me.m_soundRead.Width = 58
        Me.m_soundRead.Height = 18
        Me.m_soundRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_soundRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_soundRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_soundName
        '
        Me.m_soundName = New Label()
        Me.m_soundName.Dock = DockStyle.Top
        Me.m_soundName.Height = 18
        Me.m_soundName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_soundName.Font = New Font("Segoe UI", 8.5F)
        Me.m_soundName.Text = "声速因子"
        '
        Me.m_soundHost.Controls.Add(Me.m_soundBar)
        Me.m_soundHost.Controls.Add(Me.m_soundRead)
        Me.m_soundHost.Controls.Add(Me.m_soundName)
        Me.m_params.Controls.Add(Me.m_soundHost)
        Me.m_soundBar.Minimum = 10
        Me.m_soundBar.Maximum = 100
        Me.m_soundBar.Value = 30
        Me.m_soundBar.Dock = DockStyle.Top
        Me.m_soundBar.Height = 30
        Me.m_soundBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_shakeHost
        '
        Me.m_shakeHost = New Panel()
        Me.m_shakeHost.Dock = DockStyle.Top
        Me.m_shakeHost.Height = 52
        Me.m_shakeHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_shakeBar
        '
        Me.m_shakeBar = New TrackBar()
        '
        'm_shakeRead
        '
        Me.m_shakeRead = New Label()
        Me.m_shakeRead.Dock = DockStyle.Right
        Me.m_shakeRead.Width = 58
        Me.m_shakeRead.Height = 18
        Me.m_shakeRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_shakeRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_shakeRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_shakeName
        '
        Me.m_shakeName = New Label()
        Me.m_shakeName.Dock = DockStyle.Top
        Me.m_shakeName.Height = 18
        Me.m_shakeName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_shakeName.Font = New Font("Segoe UI", 8.5F)
        Me.m_shakeName.Text = "晃动强度"
        '
        Me.m_shakeHost.Controls.Add(Me.m_shakeBar)
        Me.m_shakeHost.Controls.Add(Me.m_shakeRead)
        Me.m_shakeHost.Controls.Add(Me.m_shakeName)
        Me.m_params.Controls.Add(Me.m_shakeHost)
        Me.m_shakeBar.Minimum = 0
        Me.m_shakeBar.Maximum = 300
        Me.m_shakeBar.Value = 100
        Me.m_shakeBar.Dock = DockStyle.Top
        Me.m_shakeBar.Height = 30
        Me.m_shakeBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_tiltHost
        '
        Me.m_tiltHost = New Panel()
        Me.m_tiltHost.Dock = DockStyle.Top
        Me.m_tiltHost.Height = 52
        Me.m_tiltHost.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_tiltBar
        '
        Me.m_tiltBar = New TrackBar()
        '
        'm_tiltRead
        '
        Me.m_tiltRead = New Label()
        Me.m_tiltRead.Dock = DockStyle.Right
        Me.m_tiltRead.Width = 58
        Me.m_tiltRead.Height = 18
        Me.m_tiltRead.ForeColor = Color.FromArgb(45, 212, 191)
        Me.m_tiltRead.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
        Me.m_tiltRead.TextAlign = ContentAlignment.MiddleRight
        '
        'm_tiltName
        '
        Me.m_tiltName = New Label()
        Me.m_tiltName.Dock = DockStyle.Top
        Me.m_tiltName.Height = 18
        Me.m_tiltName.ForeColor = Color.FromArgb(139, 152, 165)
        Me.m_tiltName.Font = New Font("Segoe UI", 8.5F)
        Me.m_tiltName.Text = "最大倾角 (°)"
        '
        Me.m_tiltHost.Controls.Add(Me.m_tiltBar)
        Me.m_tiltHost.Controls.Add(Me.m_tiltRead)
        Me.m_tiltHost.Controls.Add(Me.m_tiltName)
        Me.m_params.Controls.Add(Me.m_tiltHost)
        Me.m_tiltBar.Minimum = 3
        Me.m_tiltBar.Maximum = 60
        Me.m_tiltBar.Value = 26
        Me.m_tiltBar.Dock = DockStyle.Top
        Me.m_tiltBar.Height = 30
        Me.m_tiltBar.BackColor = Color.FromArgb(19, 26, 34)
        '
        'm_params: hint
        '
        Me.m_params.Controls.Add(New Label() With {
            .Dock = DockStyle.Top,
            .Height = 78,
            .ForeColor = Color.FromArgb(139, 152, 165),
            .Font = New Font("Segoe UI", 8.0F),
            .Text = "左键拖拽 = 晃动盒子（惯性 + 倾斜）" & vbCrLf &
                    "右键拖拽 = 环绕视角" & vbCrLf &
                    "滚轮 = 缩放" & vbCrLf &
                    "物理在后台线程推进，画面始终显示最新一帧。"})
        '
        'm_params: spacer
        '
        Me.m_params.Controls.Add(New Panel() With {.Dock = DockStyle.Top, .Height = 14})
        '
        'Form1 final assembly
        '
        Me.Controls.Add(Me.m_body)
        Me.Controls.Add(Me.m_status)
        Me.Controls.Add(Me.m_toolbar)
        Me.Controls.Add(Me.m_loading)
        Me.m_loading.BringToFront()
        Me.ResumeLayout(False)
    End Sub

End Class
