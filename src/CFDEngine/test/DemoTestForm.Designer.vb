Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports CDFDxCanvas
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Public Class DemoTestForm
    Inherits Form

    Sub New()
        Call InitializeComponent()
    End Sub

    'UserControl overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
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
        components = New Container()
        SuspendLayout()

        ' 
        ' m_playTimer（播放定时器）
        ' 
        m_playTimer = New Timer(components)
        m_playTimer.Enabled = False
        ' m_playTimer.Name = "m_playTimer"

        ' 
        ' DemoTestForm
        ' 
        Text = "CFD 可视化 · CFDCanvas 控件测试"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(1440, 860)
        MinimumSize = New Size(1100, 640)
        BackColor = Color.FromArgb(244, 246, 250)
        Font = New Font("Microsoft YaHei UI", 9.0F)

        ' 
        ' m_canvas（中央 3D 视口，占满剩余空间）
        ' 
        m_canvas = New CFDCanvas()
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Name = "m_canvas"

        ' 
        ' leftPanel（左侧控制面板）
        ' 
        leftPanel = New Panel()
        leftPanel.Dock = DockStyle.Left
        leftPanel.Width = 292
        leftPanel.BackColor = Color.White
        leftPanel.AutoScroll = True
        leftPanel.Name = "leftPanel"

        ' lblTitleData
        lblTitleData = New Label()
        lblTitleData.Text = "数据"
        lblTitleData.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleData.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleData.Location = New Point(16, 14)
        lblTitleData.AutoSize = True
        lblTitleData.Name = "lblTitleData"
        leftPanel.Controls.Add(lblTitleData)

        ' btnLoad
        btnLoad = New Button()
        btnLoad.Text = "加载数据文件夹"
        btnLoad.Size = New Size(140, 32)
        btnLoad.Location = New Point(16, 42)
        btnLoad.FlatStyle = FlatStyle.Flat
        btnLoad.BackColor = Color.FromArgb(37, 99, 235)
        btnLoad.ForeColor = Color.White
        btnLoad.Font = New Font("Microsoft YaHei UI", 9.0F)
        btnLoad.FlatAppearance.BorderSize = 0
        btnLoad.Name = "btnLoad"
        leftPanel.Controls.Add(btnLoad)

        ' lblTitleScalar
        lblTitleScalar = New Label()
        lblTitleScalar.Text = "标量场"
        lblTitleScalar.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleScalar.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleScalar.Location = New Point(16, 86)
        lblTitleScalar.AutoSize = True
        lblTitleScalar.Name = "lblTitleScalar"
        leftPanel.Controls.Add(lblTitleScalar)

        ' lblScalarHint
        lblScalarHint = New Label()
        lblScalarHint.Text = "标量场（由 VTI 实际字段生成）"
        lblScalarHint.ForeColor = Color.FromArgb(71, 85, 105)
        lblScalarHint.Location = New Point(16, 114)
        lblScalarHint.AutoSize = True
        lblScalarHint.Name = "lblScalarHint"
        leftPanel.Controls.Add(lblScalarHint)

        ' cboField
        cboField = New ComboBox()
        cboField.DropDownStyle = ComboBoxStyle.DropDownList
        cboField.FlatStyle = FlatStyle.Flat
        cboField.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboField.BackColor = Color.FromArgb(248, 250, 252)
        cboField.DropDownWidth = 420
        cboField.Location = New Point(16, 136)
        cboField.Size = New Size(250, 26)
        cboField.Name = "cboField"
        leftPanel.Controls.Add(cboField)

        ' lblPaletteCaption
        lblPaletteCaption = New Label()
        lblPaletteCaption.Text = "调色板"
        lblPaletteCaption.ForeColor = Color.FromArgb(71, 85, 105)
        lblPaletteCaption.Location = New Point(16, 170)
        lblPaletteCaption.AutoSize = True
        lblPaletteCaption.Name = "lblPaletteCaption"
        leftPanel.Controls.Add(lblPaletteCaption)

        ' cboPalette
        cboPalette = New ComboBox()
        cboPalette.DropDownStyle = ComboBoxStyle.DropDownList
        cboPalette.FlatStyle = FlatStyle.Flat
        cboPalette.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboPalette.BackColor = Color.FromArgb(248, 250, 252)
        cboPalette.Items.AddRange(New Object() {
            "Jet", "Autumn", "Cool", "Gray", "Hot", "Spring", "Summer", "Winter",
            "Red", "Green", "Blue",
            "ColorBrewer_OrRd", "ColorBrewer_PuBu", "ColorBrewer_BuPu", "ColorBrewer_Oranges",
            "ColorBrewer_BuGn", "ColorBrewer_YlOrBr", "ColorBrewer_YlGn", "ColorBrewer_RdPu",
            "ColorBrewer_YlGnBu", "ColorBrewer_Purples", "ColorBrewer_GnBu", "ColorBrewer_YlOrRd",
            "ColorBrewer_PuRd", "ColorBrewer_PuBuGn",
            "Rainbow", "FlexImaging", "Typhoon", "Icefire", "Seismic",
            "viridis", "magma", "inferno", "plasma", "cividis", "mako", "rocket", "turbo"})
        cboPalette.SelectedItem = "Jet"
        cboPalette.Location = New Point(16, 192)
        cboPalette.Size = New Size(250, 26)
        cboPalette.Name = "cboPalette"
        leftPanel.Controls.Add(cboPalette)

        ' lblTitleRange
        lblTitleRange = New Label()
        lblTitleRange.Text = "颜色值域"
        lblTitleRange.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleRange.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleRange.Location = New Point(16, 232)
        lblTitleRange.AutoSize = True
        lblTitleRange.Name = "lblTitleRange"
        leftPanel.Controls.Add(lblTitleRange)

        ' cboRangeMode
        cboRangeMode = New ComboBox()
        cboRangeMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboRangeMode.FlatStyle = FlatStyle.Flat
        cboRangeMode.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboRangeMode.BackColor = Color.FromArgb(248, 250, 252)
        cboRangeMode.Items.AddRange(New Object() {"自动", "手动"})
        cboRangeMode.SelectedIndex = 0
        cboRangeMode.Location = New Point(16, 260)
        cboRangeMode.Size = New Size(250, 26)
        cboRangeMode.Name = "cboRangeMode"
        leftPanel.Controls.Add(cboRangeMode)

        ' lblMin
        lblMin = New Label()
        lblMin.Text = "最小值"
        lblMin.ForeColor = Color.FromArgb(71, 85, 105)
        lblMin.Location = New Point(16, 294)
        lblMin.AutoSize = True
        lblMin.Name = "lblMin"
        leftPanel.Controls.Add(lblMin)

        ' lblMax
        lblMax = New Label()
        lblMax.Text = "最大值"
        lblMax.ForeColor = Color.FromArgb(71, 85, 105)
        lblMax.Location = New Point(150, 294)
        lblMax.AutoSize = True
        lblMax.Name = "lblMax"
        leftPanel.Controls.Add(lblMax)

        ' txtRangeMin
        txtRangeMin = New TextBox()
        txtRangeMin.BorderStyle = BorderStyle.FixedSingle
        txtRangeMin.Font = New Font("Microsoft YaHei UI", 9.0F)
        txtRangeMin.Location = New Point(16, 314)
        txtRangeMin.Size = New Size(115, 24)
        txtRangeMin.Visible = False
        txtRangeMin.Name = "txtRangeMin"
        leftPanel.Controls.Add(txtRangeMin)

        ' txtRangeMax
        txtRangeMax = New TextBox()
        txtRangeMax.BorderStyle = BorderStyle.FixedSingle
        txtRangeMax.Font = New Font("Microsoft YaHei UI", 9.0F)
        txtRangeMax.Location = New Point(150, 314)
        txtRangeMax.Size = New Size(115, 24)
        txtRangeMax.Visible = False
        txtRangeMax.Name = "txtRangeMax"
        leftPanel.Controls.Add(txtRangeMax)

        ' lblThresholdTitle
        lblThresholdTitle = New Label()
        lblThresholdTitle.Text = "透明阈值"
        lblThresholdTitle.ForeColor = Color.FromArgb(71, 85, 105)
        lblThresholdTitle.Location = New Point(16, 350)
        lblThresholdTitle.AutoSize = True
        lblThresholdTitle.Name = "lblThresholdTitle"
        leftPanel.Controls.Add(lblThresholdTitle)

        ' lblThresholdVal
        lblThresholdVal = New Label()
        lblThresholdVal.Text = "0.00"
        lblThresholdVal.ForeColor = Color.FromArgb(14, 165, 233)
        lblThresholdVal.Location = New Point(220, 332)
        lblThresholdVal.AutoSize = True
        lblThresholdVal.Name = "lblThresholdVal"
        leftPanel.Controls.Add(lblThresholdVal)

        ' trackThreshold
        trackThreshold = New TrackBar()
        trackThreshold.Minimum = 0
        trackThreshold.Maximum = 100
        trackThreshold.TickStyle = TickStyle.None
        trackThreshold.Location = New Point(12, 342)
        trackThreshold.Size = New Size(260, 30)
        trackThreshold.Name = "trackThreshold"
        leftPanel.Controls.Add(trackThreshold)

        ' lblTitleArrows
        lblTitleArrows = New Label()
        lblTitleArrows.Text = "速度矢量箭头"
        lblTitleArrows.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleArrows.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleArrows.Location = New Point(16, 410)
        lblTitleArrows.AutoSize = True
        lblTitleArrows.Name = "lblTitleArrows"
        leftPanel.Controls.Add(lblTitleArrows)

        ' chkArrows
        chkArrows = New CheckBox()
        chkArrows.Text = "显示箭头"
        chkArrows.AutoSize = True
        chkArrows.Location = New Point(16, 438)
        chkArrows.Name = "chkArrows"
        leftPanel.Controls.Add(chkArrows)

        ' lblArrowDensity
        lblArrowDensity = New Label()
        lblArrowDensity.Text = "箭头密度"
        lblArrowDensity.ForeColor = Color.FromArgb(71, 85, 105)
        lblArrowDensity.Location = New Point(16, 466)
        lblArrowDensity.AutoSize = True
        lblArrowDensity.Name = "lblArrowDensity"
        leftPanel.Controls.Add(lblArrowDensity)

        ' cboArrowDensity
        cboArrowDensity = New ComboBox()
        cboArrowDensity.DropDownStyle = ComboBoxStyle.DropDownList
        cboArrowDensity.FlatStyle = FlatStyle.Flat
        cboArrowDensity.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboArrowDensity.BackColor = Color.FromArgb(248, 250, 252)
        cboArrowDensity.Items.AddRange(New Object() {"高 (2×2×2)", "中 (3×3×3)", "低 (4×4×4)"})
        cboArrowDensity.SelectedIndex = 0
        cboArrowDensity.Location = New Point(16, 488)
        cboArrowDensity.Size = New Size(250, 26)
        cboArrowDensity.Name = "cboArrowDensity"
        leftPanel.Controls.Add(cboArrowDensity)

        ' lblTitleSection
        lblTitleSection = New Label()
        lblTitleSection.Text = "横截面"
        lblTitleSection.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleSection.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleSection.Location = New Point(16, 528)
        lblTitleSection.AutoSize = True
        lblTitleSection.Name = "lblTitleSection"
        leftPanel.Controls.Add(lblTitleSection)

        ' lblSectionModeLabel
        lblSectionModeLabel = New Label()
        lblSectionModeLabel.Text = "截面模式"
        lblSectionModeLabel.ForeColor = Color.FromArgb(71, 85, 105)
        lblSectionModeLabel.Location = New Point(16, 556)
        lblSectionModeLabel.AutoSize = True
        lblSectionModeLabel.Name = "lblSectionModeLabel"
        leftPanel.Controls.Add(lblSectionModeLabel)

        ' cboSectionMode
        cboSectionMode = New ComboBox()
        cboSectionMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionMode.FlatStyle = FlatStyle.Flat
        cboSectionMode.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboSectionMode.BackColor = Color.FromArgb(248, 250, 252)
        cboSectionMode.Items.AddRange(New Object() {"不启用", "启用（裁剪远侧）", "切片模式（单层）"})
        cboSectionMode.SelectedIndex = 0
        cboSectionMode.Location = New Point(16, 578)
        cboSectionMode.Size = New Size(250, 26)
        cboSectionMode.Name = "cboSectionMode"
        leftPanel.Controls.Add(cboSectionMode)

        ' lblSectionAxisLabel
        lblSectionAxisLabel = New Label()
        lblSectionAxisLabel.Text = "截面轴"
        lblSectionAxisLabel.ForeColor = Color.FromArgb(71, 85, 105)
        lblSectionAxisLabel.Location = New Point(16, 612)
        lblSectionAxisLabel.AutoSize = True
        lblSectionAxisLabel.Name = "lblSectionAxisLabel"
        leftPanel.Controls.Add(lblSectionAxisLabel)

        ' cboSectionAxis
        cboSectionAxis = New ComboBox()
        cboSectionAxis.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionAxis.FlatStyle = FlatStyle.Flat
        cboSectionAxis.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboSectionAxis.BackColor = Color.FromArgb(248, 250, 252)
        cboSectionAxis.Items.AddRange(New Object() {"X 轴", "Y 轴", "Z 轴"})
        cboSectionAxis.SelectedIndex = 0
        cboSectionAxis.Location = New Point(16, 634)
        cboSectionAxis.Size = New Size(250, 26)
        cboSectionAxis.Name = "cboSectionAxis"
        leftPanel.Controls.Add(cboSectionAxis)

        ' lblPosition
        lblPosition = New Label()
        lblPosition.Text = "位置"
        lblPosition.ForeColor = Color.FromArgb(71, 85, 105)
        lblPosition.Location = New Point(16, 668)
        lblPosition.AutoSize = True
        lblPosition.Name = "lblPosition"
        leftPanel.Controls.Add(lblPosition)

        ' lblSectionPosVal
        lblSectionPosVal = New Label()
        lblSectionPosVal.Text = "0"
        lblSectionPosVal.ForeColor = Color.FromArgb(14, 165, 233)
        lblSectionPosVal.Location = New Point(220, 650)
        lblSectionPosVal.AutoSize = True
        lblSectionPosVal.Name = "lblSectionPosVal"
        leftPanel.Controls.Add(lblSectionPosVal)

        ' trackSectionPos
        trackSectionPos = New TrackBar()
        trackSectionPos.Minimum = 0
        trackSectionPos.Maximum = 47
        trackSectionPos.TickStyle = TickStyle.None
        trackSectionPos.Location = New Point(12, 660)
        trackSectionPos.Size = New Size(260, 30)
        trackSectionPos.Name = "trackSectionPos"
        leftPanel.Controls.Add(trackSectionPos)

        ' lblTitleViewport
        lblTitleViewport = New Label()
        lblTitleViewport.Text = "视口"
        lblTitleViewport.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleViewport.ForeColor = Color.FromArgb(30, 41, 59)
        lblTitleViewport.Location = New Point(16, 730)
        lblTitleViewport.AutoSize = True
        lblTitleViewport.Name = "lblTitleViewport"
        leftPanel.Controls.Add(lblTitleViewport)

        ' chkTooltip
        chkTooltip = New CheckBox()
        chkTooltip.Text = "悬停显示体素数据提示"
        chkTooltip.AutoSize = True
        chkTooltip.Location = New Point(16, 758)
        chkTooltip.Name = "chkTooltip"
        leftPanel.Controls.Add(chkTooltip)

        ' btnAll
        btnAll = New Button()
        btnAll.Text = "全选"
        btnAll.Size = New Size(62, 24)
        btnAll.Location = New Point(16, 784)
        btnAll.FlatStyle = FlatStyle.Flat
        btnAll.Font = New Font("Microsoft YaHei UI", 8.0F)
        btnAll.Name = "btnAll"
        leftPanel.Controls.Add(btnAll)

        ' btnNone
        btnNone = New Button()
        btnNone.Text = "清空"
        btnNone.Size = New Size(62, 24)
        btnNone.Location = New Point(84, 784)
        btnNone.FlatStyle = FlatStyle.Flat
        btnNone.Font = New Font("Microsoft YaHei UI", 8.0F)
        btnNone.Name = "btnNone"
        leftPanel.Controls.Add(btnNone)

        ' clbTooltipFields
        clbTooltipFields = New CheckedListBox()
        clbTooltipFields.CheckOnClick = True
        clbTooltipFields.BorderStyle = BorderStyle.FixedSingle
        clbTooltipFields.Font = New Font("Consolas", 8.0F)
        clbTooltipFields.HorizontalScrollbar = True
        clbTooltipFields.Location = New Point(12, 814)
        clbTooltipFields.Size = New Size(264, 170)
        clbTooltipFields.Name = "clbTooltipFields"
        leftPanel.Controls.Add(clbTooltipFields)

        ' chkDebug
        chkDebug = New CheckBox()
        chkDebug.Text = "显示 DirectX 调试信息"
        chkDebug.AutoSize = True
        chkDebug.Location = New Point(16, 994)
        chkDebug.Name = "chkDebug"
        leftPanel.Controls.Add(chkDebug)

        ' 
        ' rightPanel（右侧信息面板）
        ' 
        rightPanel = New Panel()
        rightPanel.Dock = DockStyle.Right
        rightPanel.Width = 330
        rightPanel.BackColor = Color.White
        rightPanel.AutoScroll = True
        rightPanel.Name = "rightPanel"

        ' propPanel（右下角：体素属性 PropertyGrid 容器）
        propPanel = New Panel()
        propPanel.Dock = DockStyle.Bottom
        propPanel.Height = 316
        propPanel.BackColor = Color.White
        propPanel.Name = "propPanel"

        ' lblProp
        lblProp = New Label()
        lblProp.Text = "体素属性"
        lblProp.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblProp.ForeColor = Color.FromArgb(30, 41, 59)
        lblProp.Dock = DockStyle.Top
        lblProp.Height = 26
        lblProp.TextAlign = ContentAlignment.MiddleLeft
        lblProp.Padding = New Padding(12, 0, 0, 0)
        lblProp.Name = "lblProp"
        propPanel.Controls.Add(lblProp)

        ' pgVoxel
        pgVoxel = New PropertyGrid()
        pgVoxel.Dock = DockStyle.Fill
        pgVoxel.Font = New Font("Microsoft YaHei UI", 8.5F)
        pgVoxel.LineColor = Color.FromArgb(226, 232, 240)
        pgVoxel.ViewBackColor = Color.White
        pgVoxel.CategoryForeColor = Color.FromArgb(30, 41, 59)
        pgVoxel.PropertySort = PropertySort.Alphabetical
        pgVoxel.Name = "pgVoxel"
        propPanel.Controls.Add(pgVoxel)

        ' lblVoxelInfo
        lblVoxelInfo = New Label()
        lblVoxelInfo.Dock = DockStyle.Top
        lblVoxelInfo.Height = 92
        lblVoxelInfo.Padding = New Padding(16, 2, 8, 0)
        lblVoxelInfo.Name = "lblVoxelInfo"
        rightPanel.Controls.Add(lblVoxelInfo)

        ' pnlSeries
        pnlSeries = New Panel()
        pnlSeries.Dock = DockStyle.Top
        pnlSeries.Height = 172
        pnlSeries.Margin = New Padding(12, 0, 12, 0)
        pnlSeries.Name = "pnlSeries"
        rightPanel.Controls.Add(pnlSeries)

        ' lblSeriesHint
        lblSeriesHint = New Label()
        lblSeriesHint.Dock = DockStyle.Top
        lblSeriesHint.Height = 18
        lblSeriesHint.TextAlign = ContentAlignment.MiddleLeft
        lblSeriesHint.Padding = New Padding(16, 0, 0, 0)
        lblSeriesHint.Name = "lblSeriesHint"
        rightPanel.Controls.Add(lblSeriesHint)

        ' picSlice
        picSlice = New PictureBox()
        picSlice.Dock = DockStyle.Top
        picSlice.Height = 214
        picSlice.Margin = New Padding(12, 0, 12, 0)
        picSlice.Name = "picSlice"
        rightPanel.Controls.Add(picSlice)

        ' 停靠布局按添加顺序：propPanel 最后添加占据底部
        rightPanel.Controls.Add(propPanel)

        ' 
        ' bottomPanel（底部时间轴）
        ' 
        bottomPanel = New Panel()
        bottomPanel.Dock = DockStyle.Bottom
        bottomPanel.Height = 64
        bottomPanel.BackColor = Color.White
        bottomPanel.Name = "bottomPanel"

        ' btnPlay
        btnPlay = New Button()
        btnPlay.Text = "▶"
        btnPlay.Size = New Size(40, 40)
        btnPlay.Location = New Point(16, 12)
        btnPlay.FlatStyle = FlatStyle.Flat
        btnPlay.FlatAppearance.BorderSize = 0
        btnPlay.BackColor = Color.FromArgb(37, 99, 235)
        btnPlay.ForeColor = Color.White
        btnPlay.Font = New Font("Microsoft YaHei UI", 11.0F)
        btnPlay.Enabled = False
        btnPlay.Name = "btnPlay"
        bottomPanel.Controls.Add(btnPlay)

        ' lblSpeed
        lblSpeed = New Label()
        lblSpeed.Text = "速度"
        lblSpeed.ForeColor = Color.FromArgb(71, 85, 105)
        lblSpeed.Location = New Point(72, 22)
        lblSpeed.AutoSize = True
        lblSpeed.Name = "lblSpeed"
        bottomPanel.Controls.Add(lblSpeed)

        ' cboSpeed
        cboSpeed = New ComboBox()
        cboSpeed.DropDownStyle = ComboBoxStyle.DropDownList
        cboSpeed.FlatStyle = FlatStyle.Flat
        cboSpeed.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboSpeed.BackColor = Color.FromArgb(248, 250, 252)
        cboSpeed.Items.AddRange(New Object() {"2 fps", "5 fps", "10 fps", "20 fps", "30 fps"})
        cboSpeed.SelectedIndex = 1
        cboSpeed.Location = New Point(112, 18)
        cboSpeed.Size = New Size(84, 26)
        cboSpeed.Name = "cboSpeed"
        bottomPanel.Controls.Add(cboSpeed)

        ' trackFrame
        trackFrame = New TrackBar()
        trackFrame.Minimum = 0
        trackFrame.Maximum = 0
        trackFrame.TickStyle = TickStyle.None
        trackFrame.Enabled = False
        trackFrame.Location = New Point(210, 20)
        trackFrame.Size = New Size(400, 28)
        trackFrame.Name = "trackFrame"
        bottomPanel.Controls.Add(trackFrame)

        ' lblFrame
        lblFrame = New Label()
        lblFrame.Text = "— · —"
        lblFrame.ForeColor = Color.FromArgb(71, 85, 105)
        lblFrame.AutoSize = True
        lblFrame.Location = New Point(0, 24)
        lblFrame.Name = "lblFrame"
        bottomPanel.Controls.Add(lblFrame)

        ' 
        ' 将容器加入窗体（m_canvas 先加入占满剩余空间）
        ' 
        Controls.Add(m_canvas)
        Controls.Add(leftPanel)
        Controls.Add(rightPanel)
        Controls.Add(bottomPanel)

        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents m_canvas As CFDCanvas
    Friend WithEvents m_playTimer As Timer
    Friend WithEvents leftPanel As Panel
    Friend WithEvents rightPanel As Panel
    Friend WithEvents bottomPanel As Panel
    Friend WithEvents propPanel As Panel
    Friend WithEvents cboField As ComboBox
    Friend WithEvents cboPalette As ComboBox
    Friend WithEvents cboRangeMode As ComboBox
    Friend WithEvents txtRangeMin As TextBox
    Friend WithEvents txtRangeMax As TextBox
    Friend WithEvents lblThresholdVal As Label
    Friend WithEvents trackThreshold As TrackBar
    Friend WithEvents chkArrows As CheckBox
    Friend WithEvents cboArrowDensity As ComboBox
    Friend WithEvents cboSectionMode As ComboBox
    Friend WithEvents cboSectionAxis As ComboBox
    Friend WithEvents lblSectionPosVal As Label
    Friend WithEvents trackSectionPos As TrackBar
    Friend WithEvents lblVoxelInfo As Label
    Friend WithEvents pnlSeries As Panel
    Friend WithEvents lblSeriesHint As Label
    Friend WithEvents picSlice As PictureBox
    Friend WithEvents pgVoxel As PropertyGrid
    Friend WithEvents btnPlay As Button
    Friend WithEvents cboSpeed As ComboBox
    Friend WithEvents trackFrame As TrackBar
    Friend WithEvents lblFrame As Label
    Friend WithEvents chkTooltip As CheckBox
    Friend WithEvents clbTooltipFields As CheckedListBox
    Friend WithEvents chkDebug As CheckBox
    Friend WithEvents btnLoad As Button
    Friend WithEvents btnAll As Button
    Friend WithEvents btnNone As Button
    Friend WithEvents lblTitleData As Label
    Friend WithEvents lblTitleScalar As Label
    Friend WithEvents lblScalarHint As Label
    Friend WithEvents lblPaletteCaption As Label
    Friend WithEvents lblTitleRange As Label
    Friend WithEvents lblMin As Label
    Friend WithEvents lblMax As Label
    Friend WithEvents lblThresholdTitle As Label
    Friend WithEvents lblTitleArrows As Label
    Friend WithEvents lblArrowDensity As Label
    Friend WithEvents lblTitleSection As Label
    Friend WithEvents lblSectionModeLabel As Label
    Friend WithEvents lblSectionAxisLabel As Label
    Friend WithEvents lblPosition As Label
    Friend WithEvents lblTitleViewport As Label
    Friend WithEvents lblProp As Label
    Friend WithEvents lblSpeed As Label
End Class
