<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormWindTunnel
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
        components = New System.ComponentModel.Container()
        Me.SuspendLayout()

        ' 
        ' m_canvas（顶层，先创建）
        ' 
        m_canvas = New WindTunnelCanvas()
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Location = New Point(0, 150)
        m_canvas.Name = "m_canvas"
        m_canvas.Size = New Size(1280, 588)
        m_canvas.TabIndex = 0

        ' 
        ' m_timer
        ' 
        m_timer = New Timer(components)
        m_timer.Interval = 30

        ' ---- 第一层：叶子控件（必须先 New，再配置）----
        ' 
        ' m_lblStatus
        ' 
        m_lblStatus = New ToolStripStatusLabel()
        m_lblStatus.Name = "m_lblStatus"
        m_lblStatus.Size = New Size(400, 17)
        m_lblStatus.Text = "就绪。点击「加载模型」开始风洞测试。"

        ' 
        ' m_btnLoad
        ' 
        m_btnLoad = New Button()
        m_btnLoad.Name = "m_btnLoad"
        m_btnLoad.Size = New Size(88, 23)
        m_btnLoad.Text = "加载模型"

        ' 
        ' m_btnRun
        ' 
        m_btnRun = New Button()
        m_btnRun.Enabled = False
        m_btnRun.Name = "m_btnRun"
        m_btnRun.Size = New Size(64, 23)
        m_btnRun.Text = "启动"

        ' 
        ' m_btnStep
        ' 
        m_btnStep = New Button()
        m_btnStep.Enabled = False
        m_btnStep.Name = "m_btnStep"
        m_btnStep.Size = New Size(56, 23)
        m_btnStep.Text = "单步"

        ' 
        ' m_btnReset
        ' 
        m_btnReset = New Button()
        m_btnReset.Enabled = False
        m_btnReset.Name = "m_btnReset"
        m_btnReset.Size = New Size(80, 23)
        m_btnReset.Text = "重置流场"

        ' 
        ' m_sep1 (row1 分隔)
        ' 
        m_sep1 = New Label()
        m_sep1.AutoSize = True
        m_sep1.Margin = New Padding(3)
        m_sep1.Name = "m_sep1"
        m_sep1.Size = New Size(20, 12)
        m_sep1.Text = "   "

        ' 
        ' m_chkGpu
        ' 
        m_chkGpu = New CheckBox()
        m_chkGpu.AutoSize = True
        m_chkGpu.Checked = True
        m_chkGpu.Name = "m_chkGpu"
        m_chkGpu.Size = New Size(72, 17)
        m_chkGpu.Text = "CUDA 加速"

        ' 
        ' m_lblViz
        ' 
        m_lblViz = New Label()
        m_lblViz.AutoSize = True
        m_lblViz.Margin = New Padding(6, 8, 0, 0)
        m_lblViz.Name = "m_lblViz"
        m_lblViz.Size = New Size(64, 12)
        m_lblViz.Text = "可视化:"

        ' 
        ' m_cmbViz（可视化类型：热图 / 矢量图 / 流线图，严格互斥）
        ' 
        m_cmbViz = New ComboBox()
        m_cmbViz.DropDownStyle = ComboBoxStyle.DropDownList
        m_cmbViz.Items.AddRange(New Object() {"热图", "矢量图", "流线图"})
        m_cmbViz.Name = "m_cmbViz"
        m_cmbViz.Size = New Size(88, 21)
        m_cmbViz.TabIndex = 1
        m_cmbViz.SelectedIndex = 0

        ' 
        ' m_chkSlice
        ' 
        m_chkSlice = New CheckBox()
        m_chkSlice.AutoSize = True
        m_chkSlice.Name = "m_chkSlice"
        m_chkSlice.Size = New Size(48, 17)
        m_chkSlice.Text = "截面"

        ' 
        ' m_lblDispField
        ' 
        m_lblDispField = New Label()
        m_lblDispField.AutoSize = True
        m_lblDispField.Margin = New Padding(6, 8, 0, 0)
        m_lblDispField.Name = "m_lblDispField"
        m_lblDispField.Size = New Size(48, 12)
        m_lblDispField.Text = "显示场:"

        ' 
        ' m_cmbField
        ' 
        m_cmbField = New ComboBox()
        m_cmbField.DropDownStyle = ComboBoxStyle.DropDownList
        m_cmbField.FormattingEnabled = True
        m_cmbField.Items.AddRange(New Object() {"速度幅值 |u|", "Ux (来流)", "Uy (竖直)", "Uz (展向)", "压力 cs²·Δρ"})
        m_cmbField.Name = "m_cmbField"
        m_cmbField.Size = New Size(120, 21)
        m_cmbField.TabIndex = 0
        m_cmbField.SelectedIndex = 0

        ' 
        ' m_sep2 (field 与 voxelizer 之间分隔)
        ' 
        m_sep2 = New Label()
        m_sep2.AutoSize = True
        m_sep2.Margin = New Padding(3)
        m_sep2.Name = "m_sep2"
        m_sep2.Size = New Size(20, 12)
        m_sep2.Text = "   "

        ' 
        ' m_lblVoxelizer
        ' 
        m_lblVoxelizer = New Label()
        m_lblVoxelizer.AutoSize = True
        m_lblVoxelizer.Margin = New Padding(6, 8, 0, 0)
        m_lblVoxelizer.Name = "m_lblVoxelizer"
        m_lblVoxelizer.Size = New Size(48, 12)
        m_lblVoxelizer.Text = "体素化:"

        ' 
        ' m_cmbVoxelizer
        ' 
        m_cmbVoxelizer = New ComboBox()
        m_cmbVoxelizer.DropDownStyle = ComboBoxStyle.DropDownList
        m_cmbVoxelizer.FormattingEnabled = True
        m_cmbVoxelizer.Items.AddRange(New Object() {"Standard", "Sdf", "Physical", "SdfRaw"})
        m_cmbVoxelizer.Name = "m_cmbVoxelizer"
        m_cmbVoxelizer.Size = New Size(90, 21)
        m_cmbVoxelizer.TabIndex = 1
        m_cmbVoxelizer.SelectedIndex = 0

        ' 
        ' m_sep3 (voxelizer 与 resolution 之间分隔)
        ' 
        m_sep3 = New Label()
        m_sep3.AutoSize = True
        m_sep3.Margin = New Padding(3)
        m_sep3.Name = "m_sep3"
        m_sep3.Size = New Size(20, 12)
        m_sep3.Text = "   "

        ' 
        ' m_lblResolution
        ' 
        m_lblResolution = New Label()
        m_lblResolution.AutoSize = True
        m_lblResolution.Margin = New Padding(6, 8, 0, 0)
        m_lblResolution.Name = "m_lblResolution"
        m_lblResolution.Size = New Size(48, 12)
        m_lblResolution.Text = "分辨率:"

        ' 
        ' m_lblFreestream
        ' 
        m_lblFreestream = New Label()
        m_lblFreestream.AutoSize = True
        m_lblFreestream.Margin = New Padding(6, 8, 0, 0)
        m_lblFreestream.Name = "m_lblFreestream"
        m_lblFreestream.Size = New Size(56, 12)
        m_lblFreestream.Text = "来流 U∞:"

        ' 
        ' m_lblStepsPerFrame
        ' 
        m_lblStepsPerFrame = New Label()
        m_lblStepsPerFrame.AutoSize = True
        m_lblStepsPerFrame.Margin = New Padding(6, 8, 0, 0)
        m_lblStepsPerFrame.Name = "m_lblStepsPerFrame"
        m_lblStepsPerFrame.Size = New Size(40, 12)
        m_lblStepsPerFrame.Text = "步/帧:"

        ' 
        ' m_lblVoxelSize
        ' 
        m_lblVoxelSize = New Label()
        m_lblVoxelSize.AutoSize = True
        m_lblVoxelSize.Margin = New Padding(6, 8, 0, 0)
        m_lblVoxelSize.Name = "m_lblVoxelSize"
        m_lblVoxelSize.Size = New Size(60, 12)
        m_lblVoxelSize.Text = "voxelSize:"

        ' 
        ' m_numResolution
        ' 
        m_numResolution = New NumericUpDown()
        m_numResolution.Maximum = New Decimal(New Integer() {160, 0, 0, 0})
        m_numResolution.Minimum = New Decimal(New Integer() {16, 0, 0, 0})
        m_numResolution.Name = "m_numResolution"
        m_numResolution.Size = New Size(64, 23)
        m_numResolution.TabIndex = 2
        m_numResolution.Value = New Decimal(New Integer() {40, 0, 0, 0})

        ' 
        ' m_sep4 (resolution 与 freestream 之间分隔)
        ' 
        m_sep4 = New Label()
        m_sep4.AutoSize = True
        m_sep4.Margin = New Padding(3)
        m_sep4.Name = "m_sep4"
        m_sep4.Size = New Size(20, 12)
        m_sep4.Text = "   "

        ' 
        ' m_numFreestream
        ' 
        m_numFreestream = New NumericUpDown()
        m_numFreestream.DecimalPlaces = 2
        m_numFreestream.Increment = New Decimal(New Integer() {1, 0, 0, 131072})
        m_numFreestream.Maximum = New Decimal(New Integer() {2, 0, 0, 65536})
        m_numFreestream.Minimum = New Decimal(New Integer() {2, 0, 0, 131072})
        m_numFreestream.Name = "m_numFreestream"
        m_numFreestream.Size = New Size(64, 23)
        m_numFreestream.TabIndex = 3
        m_numFreestream.Value = New Decimal(New Integer() {1, 0, 0, 65536})

        ' 
        ' m_sep5 (freestream 与 steps 之间分隔)
        ' 
        m_sep5 = New Label()
        m_sep5.AutoSize = True
        m_sep5.Margin = New Padding(3)
        m_sep5.Name = "m_sep5"
        m_sep5.Size = New Size(20, 12)
        m_sep5.Text = "   "

        ' 
        ' m_numStepsPerFrame
        ' 
        m_numStepsPerFrame = New NumericUpDown()
        m_numStepsPerFrame.Maximum = New Decimal(New Integer() {20, 0, 0, 0})
        m_numStepsPerFrame.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        m_numStepsPerFrame.Name = "m_numStepsPerFrame"
        m_numStepsPerFrame.Size = New Size(48, 23)
        m_numStepsPerFrame.TabIndex = 4
        m_numStepsPerFrame.Value = New Decimal(New Integer() {2, 0, 0, 0})

        ' 
        ' m_sep6 (steps 与 voxelSize 之间分隔)
        ' 
        m_sep6 = New Label()
        m_sep6.AutoSize = True
        m_sep6.Margin = New Padding(3)
        m_sep6.Name = "m_sep6"
        m_sep6.Size = New Size(20, 12)
        m_sep6.Text = "   "

        ' 
        ' m_numVoxelSize
        ' 
        m_numVoxelSize = New NumericUpDown()
        m_numVoxelSize.DecimalPlaces = 1
        m_numVoxelSize.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        m_numVoxelSize.Maximum = New Decimal(New Integer() {10, 0, 0, 0})
        m_numVoxelSize.Minimum = New Decimal(New Integer() {1, 0, 0, 65536})
        m_numVoxelSize.Name = "m_numVoxelSize"
        m_numVoxelSize.Size = New Size(56, 23)
        m_numVoxelSize.TabIndex = 5
        m_numVoxelSize.Value = New Decimal(New Integer() {10, 0, 0, 65536})

        ' 
        ' m_lblTransThreshold
        ' 
        m_lblTransThreshold = New Label()
        m_lblTransThreshold.AutoSize = True
        m_lblTransThreshold.Margin = New Padding(6, 8, 0, 0)
        m_lblTransThreshold.Name = "m_lblTransThreshold"
        m_lblTransThreshold.Size = New Size(60, 12)
        m_lblTransThreshold.Text = "透明阈值:"

        ' 
        ' m_thresholdBar
        ' 
        m_thresholdBar = New TrackBar()
        m_thresholdBar.AutoSize = False
        m_thresholdBar.Height = 28
        m_thresholdBar.Location = New Point(0, 0)
        m_thresholdBar.Maximum = 100
        m_thresholdBar.Minimum = 0
        m_thresholdBar.Name = "m_thresholdBar"
        m_thresholdBar.Size = New Size(240, 28)
        m_thresholdBar.TabIndex = 0
        m_thresholdBar.TickFrequency = 10
        m_thresholdBar.Width = 240

        ' 
        ' m_lblThreshold
        ' 
        m_lblThreshold = New Label()
        m_lblThreshold.AutoSize = True
        m_lblThreshold.Margin = New Padding(6, 9, 0, 0)
        m_lblThreshold.Name = "m_lblThreshold"
        m_lblThreshold.Size = New Size(27, 12)
        m_lblThreshold.Text = "0%"

        ' 
        ' m_cmbSliceAxis
        ' 
        m_cmbSliceAxis = New ComboBox()
        m_cmbSliceAxis.DropDownStyle = ComboBoxStyle.DropDownList
        m_cmbSliceAxis.Enabled = False
        m_cmbSliceAxis.FormattingEnabled = True
        m_cmbSliceAxis.Items.AddRange(New Object() {"X", "Y", "Z"})
        m_cmbSliceAxis.Name = "m_cmbSliceAxis"
        m_cmbSliceAxis.Size = New Size(56, 21)
        m_cmbSliceAxis.TabIndex = 0
        m_cmbSliceAxis.SelectedIndex = 0

        ' 
        ' m_sliceBar
        ' 
        m_sliceBar = New TrackBar()
        m_sliceBar.AutoSize = False
        m_sliceBar.Enabled = False
        m_sliceBar.Height = 28
        m_sliceBar.Location = New Point(0, 0)
        m_sliceBar.Maximum = 100
        m_sliceBar.Minimum = 0
        m_sliceBar.Name = "m_sliceBar"
        m_sliceBar.Size = New Size(220, 28)
        m_sliceBar.TabIndex = 1
        m_sliceBar.TickFrequency = 10
        m_sliceBar.Width = 220

        ' 
        ' m_chkSliceOnly
        ' 
        m_chkSliceOnly = New CheckBox()
        m_chkSliceOnly.AutoSize = True
        m_chkSliceOnly.Enabled = False
        m_chkSliceOnly.Name = "m_chkSliceOnly"
        m_chkSliceOnly.Size = New Size(72, 17)
        m_chkSliceOnly.Text = "仅切片层"

        ' 
        ' m_chkClipSolid（裁剪远端横截面时是否一并裁剪不活动的固体模型本体）
        ' 
        m_chkClipSolid = New CheckBox()
        m_chkClipSolid.AutoSize = True
        m_chkClipSolid.Checked = False
        m_chkClipSolid.Enabled = False
        m_chkClipSolid.Name = "m_chkClipSolid"
        m_chkClipSolid.Size = New Size(96, 17)
        m_chkClipSolid.Text = "裁剪模型本体"

        ' ---- 第二层：行容器（FlowLayoutPanel，先 New 再添加已创建的子控件）----
        ' 
        ' m_row1 (第一行：动作)
        ' 
        m_row1 = New FlowLayoutPanel()
        m_row1.Controls.Add(m_btnLoad)
        m_row1.Controls.Add(m_btnRun)
        m_row1.Controls.Add(m_btnStep)
        m_row1.Controls.Add(m_btnReset)
        m_row1.Controls.Add(m_sep1)
        m_row1.Controls.Add(m_chkGpu)
        m_row1.Controls.Add(m_lblViz)
        m_row1.Controls.Add(m_cmbViz)
        m_row1.Controls.Add(m_chkSlice)
        m_row1.Dock = DockStyle.Top
        m_row1.Height = 36
        m_row1.Location = New Point(0, 0)
        m_row1.Name = "m_row1"
        m_row1.Size = New Size(1280, 36)
        m_row1.WrapContents = False

        ' 
        ' m_row2 (第二行：参数)
        ' 
        m_row2 = New FlowLayoutPanel()
        m_row2.Controls.Add(m_lblDispField)
        m_row2.Controls.Add(m_cmbField)
        m_row2.Controls.Add(m_sep2)
        m_row2.Controls.Add(m_lblVoxelizer)
        m_row2.Controls.Add(m_cmbVoxelizer)
        m_row2.Controls.Add(m_sep3)
        m_row2.Controls.Add(m_lblResolution)
        m_row2.Controls.Add(m_lblFreestream)
        m_row2.Controls.Add(m_lblStepsPerFrame)
        m_row2.Controls.Add(m_lblVoxelSize)
        m_row2.Controls.Add(m_numResolution)
        m_row2.Controls.Add(m_sep4)
        m_row2.Controls.Add(m_numFreestream)
        m_row2.Controls.Add(m_sep5)
        m_row2.Controls.Add(m_numStepsPerFrame)
        m_row2.Controls.Add(m_sep6)
        m_row2.Controls.Add(m_numVoxelSize)
        m_row2.Dock = DockStyle.Top
        m_row2.Height = 36
        m_row2.Location = New Point(0, 0)
        m_row2.Name = "m_row2"
        m_row2.Size = New Size(1280, 36)
        m_row2.WrapContents = False

        ' 
        ' m_rowT (第三行：阈值)
        ' 
        m_rowT = New FlowLayoutPanel()
        m_rowT.Controls.Add(m_lblTransThreshold)
        m_rowT.Controls.Add(m_thresholdBar)
        m_rowT.Controls.Add(m_lblThreshold)
        m_rowT.Dock = DockStyle.Top
        m_rowT.Height = 32
        m_rowT.Location = New Point(0, 0)
        m_rowT.Name = "m_rowT"
        m_rowT.Size = New Size(1280, 32)
        m_rowT.WrapContents = False

        ' 
        ' m_row3 (第四行：截面控制，默认隐藏)
        ' 
        m_row3 = New FlowLayoutPanel()
        m_row3.Controls.Add(m_cmbSliceAxis)
        m_row3.Controls.Add(m_sliceBar)
        m_row3.Controls.Add(m_chkSliceOnly)
        m_row3.Controls.Add(m_chkClipSolid)
        m_row3.Dock = DockStyle.Top
        m_row3.Height = 34
        m_row3.Location = New Point(0, 0)
        m_row3.Name = "m_row3"
        m_row3.Size = New Size(1280, 34)
        m_row3.Visible = False
        m_row3.WrapContents = False

        ' ---- 第三层：状态栏（先 New 再添加已创建的标签）----
        ' 
        ' m_status
        ' 
        m_status = New StatusStrip()
        m_status.SuspendLayout()
        m_status.Dock = DockStyle.Bottom
        m_status.Items.AddRange(New ToolStripItem() {m_lblStatus})
        m_status.Location = New Point(0, 738)
        m_status.Name = "m_status"
        m_status.Size = New Size(1280, 22)
        m_status.TabIndex = 1
        m_status.Text = "m_status"
        m_status.ResumeLayout(False)

        ' ---- 第四层：工具栏（先 New，再把已创建的行加入）----
        ' 高度 = row1(36) + row2(36) + rowT(32) + row3(34) + 12 = 150
        ' m_toolbar
        ' 
        m_toolbar = New Panel()
        m_toolbar.SuspendLayout()
        m_toolbar.Controls.Add(m_row3)
        m_toolbar.Controls.Add(m_rowT)
        m_toolbar.Controls.Add(m_row2)
        m_toolbar.Controls.Add(m_row1)
        m_toolbar.Dock = DockStyle.Top
        m_toolbar.Height = 150
        m_toolbar.Location = New Point(0, 0)
        m_toolbar.Name = "m_toolbar"
        m_toolbar.Padding = New Padding(8, 6, 8, 2)
        m_toolbar.Size = New Size(1280, 150)
        m_toolbar.TabIndex = 0
        m_toolbar.ResumeLayout(False)

        ' 
        ' FormWindTunnel
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1280, 760)
        Controls.Add(m_canvas)
        Controls.Add(m_toolbar)
        Controls.Add(m_status)
        Name = "FormWindTunnel"
        StartPosition = FormStartPosition.CenterScreen
        Text = "LBM 风洞测试模拟器 (float32 CUDA / CPU)"
        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

    Friend WithEvents m_canvas As WindTunnelCanvas
    Friend WithEvents m_timer As Timer
    Friend WithEvents m_toolbar As Panel
    Friend WithEvents m_status As StatusStrip
    Friend WithEvents m_lblStatus As ToolStripStatusLabel
    Friend WithEvents m_btnLoad As Button
    Friend WithEvents m_btnRun As Button
    Friend WithEvents m_btnStep As Button
    Friend WithEvents m_btnReset As Button
    Friend WithEvents m_chkGpu As CheckBox
    Friend WithEvents m_lblViz As Label
    Friend WithEvents m_cmbViz As ComboBox
    Friend WithEvents m_chkSlice As CheckBox
    Friend WithEvents m_chkClipSolid As CheckBox
    Friend WithEvents m_cmbField As ComboBox
    Friend WithEvents m_cmbVoxelizer As ComboBox
    Friend WithEvents m_numResolution As NumericUpDown
    Friend WithEvents m_numFreestream As NumericUpDown
    Friend WithEvents m_numStepsPerFrame As NumericUpDown
    Friend WithEvents m_numVoxelSize As NumericUpDown
    Friend WithEvents m_thresholdBar As TrackBar
    Friend WithEvents m_lblThreshold As Label
    Friend WithEvents m_cmbSliceAxis As ComboBox
    Friend WithEvents m_sliceBar As TrackBar
    Friend WithEvents m_chkSliceOnly As CheckBox
    Friend WithEvents m_row1 As FlowLayoutPanel
    Friend WithEvents m_row2 As FlowLayoutPanel
    Friend WithEvents m_rowT As FlowLayoutPanel
    Friend WithEvents m_row3 As FlowLayoutPanel
    Friend WithEvents m_sep1 As Label
    Friend WithEvents m_sep2 As Label
    Friend WithEvents m_sep3 As Label
    Friend WithEvents m_sep4 As Label
    Friend WithEvents m_sep5 As Label
    Friend WithEvents m_sep6 As Label
    Friend WithEvents m_lblDispField As Label
    Friend WithEvents m_lblVoxelizer As Label
    Friend WithEvents m_lblResolution As Label
    Friend WithEvents m_lblFreestream As Label
    Friend WithEvents m_lblStepsPerFrame As Label
    Friend WithEvents m_lblVoxelSize As Label
    Friend WithEvents m_lblTransThreshold As Label

End Class
