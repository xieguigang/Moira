Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Moira.CDFDxCanvas

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
        m_playTimer = New Timer(components)
        m_canvas = New CFDCanvas()
        leftPanel = New Panel()
        lblTitleData = New Label()
        btnLoad = New Button()
        lblTitleScalar = New Label()
        lblScalarHint = New Label()
        cboField = New ComboBox()
        lblPaletteCaption = New Label()
        cboPalette = New ComboBox()
        lblTitleRange = New Label()
        cboRangeMode = New ComboBox()
        lblMin = New Label()
        lblMax = New Label()
        txtRangeMin = New TextBox()
        txtRangeMax = New TextBox()
        lblThresholdTitle = New Label()
        lblThresholdVal = New Label()
        trackThreshold = New TrackBar()
        lblTitleArrows = New Label()
        chkArrows = New CheckBox()
        lblArrowDensity = New Label()
        cboArrowDensity = New ComboBox()
        lblTitleSection = New Label()
        lblSectionModeLabel = New Label()
        cboSectionMode = New ComboBox()
        lblSectionAxisLabel = New Label()
        cboSectionAxis = New ComboBox()
        lblPosition = New Label()
        lblSectionPosVal = New Label()
        trackSectionPos = New TrackBar()
        lblTitleViewport = New Label()
        chkTooltip = New CheckBox()
        btnAll = New Button()
        btnNone = New Button()
        clbTooltipFields = New CheckedListBox()
        chkDebug = New CheckBox()
        rightPanel = New Panel()
        lblVoxelInfo = New Label()
        pnlSeries = New Panel()
        lblSeriesHint = New Label()
        picSlice = New PictureBox()
        propPanel = New Panel()
        lblProp = New Label()
        pgVoxel = New PropertyGrid()
        bottomPanel = New Panel()
        btnPlay = New Button()
        lblSpeed = New Label()
        cboSpeed = New ComboBox()
        trackFrame = New TrackBar()
        lblFrame = New Label()
        leftPanel.SuspendLayout()
        CType(trackThreshold, ISupportInitialize).BeginInit()
        CType(trackSectionPos, ISupportInitialize).BeginInit()
        rightPanel.SuspendLayout()
        CType(picSlice, ISupportInitialize).BeginInit()
        propPanel.SuspendLayout()
        bottomPanel.SuspendLayout()
        CType(trackFrame, ISupportInitialize).BeginInit()
        SuspendLayout()
        ' 
        ' m_playTimer
        ' 
        ' 
        ' m_canvas
        ' 
        m_canvas.ArrowDensity = 2
        m_canvas.AutoPointSize = True
        m_canvas.AutoRange = True
        m_canvas.Dock = DockStyle.Fill
        m_canvas.Field = "pressure"
        m_canvas.FrameIndex = -1
        m_canvas.Location = New Point(292, 0)
        m_canvas.Name = "m_canvas"
        m_canvas.Palette = ScalerPalette.Jet
        m_canvas.RangeMax = 1R
        m_canvas.RangeMin = 0R
        m_canvas.SectionAxis = Data.CfdAxis.X
        m_canvas.SectionEnabled = False
        m_canvas.SectionPosition = 0
        m_canvas.SelectedVoxel = -1
        m_canvas.ShowArrows = False
        m_canvas.ShowDebugInfo = False
        m_canvas.ShowHoverTooltip = False
        m_canvas.Size = New Size(802, 757)
        m_canvas.SliceOnly = False
        m_canvas.TabIndex = 0
        m_canvas.Threshold = 0R
        m_canvas.TooltipFields = Nothing
        ' 
        ' leftPanel
        ' 
        leftPanel.AutoScroll = True
        leftPanel.BackColor = Color.White
        leftPanel.Controls.Add(lblTitleData)
        leftPanel.Controls.Add(btnLoad)
        leftPanel.Controls.Add(lblTitleScalar)
        leftPanel.Controls.Add(lblScalarHint)
        leftPanel.Controls.Add(cboField)
        leftPanel.Controls.Add(lblPaletteCaption)
        leftPanel.Controls.Add(cboPalette)
        leftPanel.Controls.Add(lblTitleRange)
        leftPanel.Controls.Add(cboRangeMode)
        leftPanel.Controls.Add(lblMin)
        leftPanel.Controls.Add(lblMax)
        leftPanel.Controls.Add(txtRangeMin)
        leftPanel.Controls.Add(txtRangeMax)
        leftPanel.Controls.Add(lblThresholdTitle)
        leftPanel.Controls.Add(lblThresholdVal)
        leftPanel.Controls.Add(trackThreshold)
        leftPanel.Controls.Add(lblTitleArrows)
        leftPanel.Controls.Add(chkArrows)
        leftPanel.Controls.Add(lblArrowDensity)
        leftPanel.Controls.Add(cboArrowDensity)
        leftPanel.Controls.Add(lblTitleSection)
        leftPanel.Controls.Add(lblSectionModeLabel)
        leftPanel.Controls.Add(cboSectionMode)
        leftPanel.Controls.Add(lblSectionAxisLabel)
        leftPanel.Controls.Add(cboSectionAxis)
        leftPanel.Controls.Add(lblPosition)
        leftPanel.Controls.Add(lblSectionPosVal)
        leftPanel.Controls.Add(trackSectionPos)
        leftPanel.Controls.Add(lblTitleViewport)
        leftPanel.Controls.Add(chkTooltip)
        leftPanel.Controls.Add(btnAll)
        leftPanel.Controls.Add(btnNone)
        leftPanel.Controls.Add(clbTooltipFields)
        leftPanel.Controls.Add(chkDebug)
        leftPanel.Dock = DockStyle.Left
        leftPanel.Location = New Point(0, 0)
        leftPanel.Name = "leftPanel"
        leftPanel.Size = New Size(292, 757)
        leftPanel.TabIndex = 1
        ' 
        ' lblTitleData
        ' 
        lblTitleData.AutoSize = True
        lblTitleData.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleData.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleData.Location = New Point(16, 14)
        lblTitleData.Name = "lblTitleData"
        lblTitleData.Size = New Size(37, 19)
        lblTitleData.TabIndex = 0
        lblTitleData.Text = "数据"
        ' 
        ' btnLoad
        ' 
        btnLoad.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        btnLoad.FlatAppearance.BorderSize = 0
        btnLoad.FlatStyle = FlatStyle.Flat
        btnLoad.Font = New Font("Microsoft YaHei UI", 9F)
        btnLoad.ForeColor = Color.White
        btnLoad.Location = New Point(16, 42)
        btnLoad.Name = "btnLoad"
        btnLoad.Size = New Size(140, 32)
        btnLoad.TabIndex = 1
        btnLoad.Text = "加载数据文件夹"
        btnLoad.UseVisualStyleBackColor = False
        ' 
        ' lblTitleScalar
        ' 
        lblTitleScalar.AutoSize = True
        lblTitleScalar.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleScalar.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleScalar.Location = New Point(16, 86)
        lblTitleScalar.Name = "lblTitleScalar"
        lblTitleScalar.Size = New Size(51, 19)
        lblTitleScalar.TabIndex = 2
        lblTitleScalar.Text = "标量场"
        ' 
        ' lblScalarHint
        ' 
        lblScalarHint.AutoSize = True
        lblScalarHint.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblScalarHint.Location = New Point(16, 114)
        lblScalarHint.Name = "lblScalarHint"
        lblScalarHint.Size = New Size(179, 17)
        lblScalarHint.TabIndex = 3
        lblScalarHint.Text = "标量场（由 VTI 实际字段生成）"
        ' 
        ' cboField
        ' 
        cboField.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboField.DropDownStyle = ComboBoxStyle.DropDownList
        cboField.DropDownWidth = 420
        cboField.FlatStyle = FlatStyle.Flat
        cboField.Font = New Font("Microsoft YaHei UI", 9F)
        cboField.Location = New Point(16, 136)
        cboField.Name = "cboField"
        cboField.Size = New Size(250, 25)
        cboField.TabIndex = 4
        ' 
        ' lblPaletteCaption
        ' 
        lblPaletteCaption.AutoSize = True
        lblPaletteCaption.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblPaletteCaption.Location = New Point(16, 170)
        lblPaletteCaption.Name = "lblPaletteCaption"
        lblPaletteCaption.Size = New Size(44, 17)
        lblPaletteCaption.TabIndex = 5
        lblPaletteCaption.Text = "调色板"
        ' 
        ' cboPalette
        ' 
        cboPalette.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboPalette.DropDownStyle = ComboBoxStyle.DropDownList
        cboPalette.FlatStyle = FlatStyle.Flat
        cboPalette.Font = New Font("Microsoft YaHei UI", 9F)
        cboPalette.Items.AddRange(New Object() {"Jet", "Autumn", "Cool", "Gray", "Hot", "Spring", "Summer", "Winter", "Red", "Green", "Blue", "ColorBrewer_OrRd", "ColorBrewer_PuBu", "ColorBrewer_BuPu", "ColorBrewer_Oranges", "ColorBrewer_BuGn", "ColorBrewer_YlOrBr", "ColorBrewer_YlGn", "ColorBrewer_RdPu", "ColorBrewer_YlGnBu", "ColorBrewer_Purples", "ColorBrewer_GnBu", "ColorBrewer_YlOrRd", "ColorBrewer_PuRd", "ColorBrewer_PuBuGn", "Rainbow", "FlexImaging", "Typhoon", "Icefire", "Seismic", "viridis", "magma", "inferno", "plasma", "cividis", "mako", "rocket", "turbo"})
        cboPalette.Location = New Point(16, 192)
        cboPalette.Name = "cboPalette"
        cboPalette.Size = New Size(250, 25)
        cboPalette.TabIndex = 6
        ' 
        ' lblTitleRange
        ' 
        lblTitleRange.AutoSize = True
        lblTitleRange.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleRange.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleRange.Location = New Point(16, 232)
        lblTitleRange.Name = "lblTitleRange"
        lblTitleRange.Size = New Size(65, 19)
        lblTitleRange.TabIndex = 7
        lblTitleRange.Text = "颜色值域"
        ' 
        ' cboRangeMode
        ' 
        cboRangeMode.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboRangeMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboRangeMode.FlatStyle = FlatStyle.Flat
        cboRangeMode.Font = New Font("Microsoft YaHei UI", 9F)
        cboRangeMode.Items.AddRange(New Object() {"自动", "手动"})
        cboRangeMode.Location = New Point(16, 260)
        cboRangeMode.Name = "cboRangeMode"
        cboRangeMode.Size = New Size(250, 25)
        cboRangeMode.TabIndex = 8
        ' 
        ' lblMin
        ' 
        lblMin.AutoSize = True
        lblMin.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblMin.Location = New Point(16, 294)
        lblMin.Name = "lblMin"
        lblMin.Size = New Size(44, 17)
        lblMin.TabIndex = 9
        lblMin.Text = "最小值"
        ' 
        ' lblMax
        ' 
        lblMax.AutoSize = True
        lblMax.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblMax.Location = New Point(150, 294)
        lblMax.Name = "lblMax"
        lblMax.Size = New Size(44, 17)
        lblMax.TabIndex = 10
        lblMax.Text = "最大值"
        ' 
        ' txtRangeMin
        ' 
        txtRangeMin.BorderStyle = BorderStyle.FixedSingle
        txtRangeMin.Font = New Font("Microsoft YaHei UI", 9F)
        txtRangeMin.Location = New Point(16, 314)
        txtRangeMin.Name = "txtRangeMin"
        txtRangeMin.Size = New Size(115, 23)
        txtRangeMin.TabIndex = 11
        txtRangeMin.Visible = False
        ' 
        ' txtRangeMax
        ' 
        txtRangeMax.BorderStyle = BorderStyle.FixedSingle
        txtRangeMax.Font = New Font("Microsoft YaHei UI", 9F)
        txtRangeMax.Location = New Point(150, 314)
        txtRangeMax.Name = "txtRangeMax"
        txtRangeMax.Size = New Size(115, 23)
        txtRangeMax.TabIndex = 12
        txtRangeMax.Visible = False
        ' 
        ' lblThresholdTitle
        ' 
        lblThresholdTitle.AutoSize = True
        lblThresholdTitle.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblThresholdTitle.Location = New Point(16, 370)
        lblThresholdTitle.Name = "lblThresholdTitle"
        lblThresholdTitle.Size = New Size(56, 17)
        lblThresholdTitle.TabIndex = 13
        lblThresholdTitle.Text = "透明阈值"
        ' 
        ' lblThresholdVal
        ' 
        lblThresholdVal.AutoSize = True
        lblThresholdVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblThresholdVal.Location = New Point(220, 332)
        lblThresholdVal.Name = "lblThresholdVal"
        lblThresholdVal.Size = New Size(32, 17)
        lblThresholdVal.TabIndex = 14
        lblThresholdVal.Text = "0.00"
        ' 
        ' trackThreshold
        ' 
        trackThreshold.Location = New Point(12, 342)
        trackThreshold.Maximum = 100
        trackThreshold.Name = "trackThreshold"
        trackThreshold.Size = New Size(260, 45)
        trackThreshold.TabIndex = 15
        trackThreshold.TickStyle = TickStyle.None
        ' 
        ' lblTitleArrows
        ' 
        lblTitleArrows.AutoSize = True
        lblTitleArrows.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleArrows.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleArrows.Location = New Point(16, 410)
        lblTitleArrows.Name = "lblTitleArrows"
        lblTitleArrows.Size = New Size(93, 19)
        lblTitleArrows.TabIndex = 16
        lblTitleArrows.Text = "速度矢量箭头"
        ' 
        ' chkArrows
        ' 
        chkArrows.AutoSize = True
        chkArrows.Location = New Point(16, 438)
        chkArrows.Name = "chkArrows"
        chkArrows.Size = New Size(75, 21)
        chkArrows.TabIndex = 17
        chkArrows.Text = "显示箭头"
        ' 
        ' lblArrowDensity
        ' 
        lblArrowDensity.AutoSize = True
        lblArrowDensity.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblArrowDensity.Location = New Point(16, 466)
        lblArrowDensity.Name = "lblArrowDensity"
        lblArrowDensity.Size = New Size(56, 17)
        lblArrowDensity.TabIndex = 18
        lblArrowDensity.Text = "箭头密度"
        ' 
        ' cboArrowDensity
        ' 
        cboArrowDensity.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboArrowDensity.DropDownStyle = ComboBoxStyle.DropDownList
        cboArrowDensity.FlatStyle = FlatStyle.Flat
        cboArrowDensity.Font = New Font("Microsoft YaHei UI", 9F)
        cboArrowDensity.Items.AddRange(New Object() {"高 (2×2×2)", "中 (3×3×3)", "低 (4×4×4)"})
        cboArrowDensity.Location = New Point(16, 488)
        cboArrowDensity.Name = "cboArrowDensity"
        cboArrowDensity.Size = New Size(250, 25)
        cboArrowDensity.TabIndex = 19
        ' 
        ' lblTitleSection
        ' 
        lblTitleSection.AutoSize = True
        lblTitleSection.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleSection.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleSection.Location = New Point(16, 528)
        lblTitleSection.Name = "lblTitleSection"
        lblTitleSection.Size = New Size(51, 19)
        lblTitleSection.TabIndex = 20
        lblTitleSection.Text = "横截面"
        ' 
        ' lblSectionModeLabel
        ' 
        lblSectionModeLabel.AutoSize = True
        lblSectionModeLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSectionModeLabel.Location = New Point(16, 556)
        lblSectionModeLabel.Name = "lblSectionModeLabel"
        lblSectionModeLabel.Size = New Size(56, 17)
        lblSectionModeLabel.TabIndex = 21
        lblSectionModeLabel.Text = "截面模式"
        ' 
        ' cboSectionMode
        ' 
        cboSectionMode.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSectionMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionMode.FlatStyle = FlatStyle.Flat
        cboSectionMode.Font = New Font("Microsoft YaHei UI", 9F)
        cboSectionMode.Items.AddRange(New Object() {"不启用", "启用（裁剪远侧）", "切片模式（单层）"})
        cboSectionMode.Location = New Point(16, 578)
        cboSectionMode.Name = "cboSectionMode"
        cboSectionMode.Size = New Size(250, 25)
        cboSectionMode.TabIndex = 22
        ' 
        ' lblSectionAxisLabel
        ' 
        lblSectionAxisLabel.AutoSize = True
        lblSectionAxisLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSectionAxisLabel.Location = New Point(16, 612)
        lblSectionAxisLabel.Name = "lblSectionAxisLabel"
        lblSectionAxisLabel.Size = New Size(44, 17)
        lblSectionAxisLabel.TabIndex = 23
        lblSectionAxisLabel.Text = "截面轴"
        ' 
        ' cboSectionAxis
        ' 
        cboSectionAxis.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSectionAxis.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionAxis.FlatStyle = FlatStyle.Flat
        cboSectionAxis.Font = New Font("Microsoft YaHei UI", 9F)
        cboSectionAxis.Items.AddRange(New Object() {"X 轴", "Y 轴", "Z 轴"})
        cboSectionAxis.Location = New Point(16, 634)
        cboSectionAxis.Name = "cboSectionAxis"
        cboSectionAxis.Size = New Size(250, 25)
        cboSectionAxis.TabIndex = 24
        ' 
        ' lblPosition
        ' 
        lblPosition.AutoSize = True
        lblPosition.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblPosition.Location = New Point(16, 668)
        lblPosition.Name = "lblPosition"
        lblPosition.Size = New Size(32, 17)
        lblPosition.TabIndex = 25
        lblPosition.Text = "位置"
        ' 
        ' lblSectionPosVal
        ' 
        lblSectionPosVal.AutoSize = True
        lblSectionPosVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblSectionPosVal.Location = New Point(220, 650)
        lblSectionPosVal.Name = "lblSectionPosVal"
        lblSectionPosVal.Size = New Size(15, 17)
        lblSectionPosVal.TabIndex = 26
        lblSectionPosVal.Text = "0"
        ' 
        ' trackSectionPos
        ' 
        trackSectionPos.Location = New Point(12, 660)
        trackSectionPos.Maximum = 47
        trackSectionPos.Name = "trackSectionPos"
        trackSectionPos.Size = New Size(260, 45)
        trackSectionPos.TabIndex = 27
        trackSectionPos.TickStyle = TickStyle.None
        ' 
        ' lblTitleViewport
        ' 
        lblTitleViewport.AutoSize = True
        lblTitleViewport.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleViewport.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleViewport.Location = New Point(16, 730)
        lblTitleViewport.Name = "lblTitleViewport"
        lblTitleViewport.Size = New Size(37, 19)
        lblTitleViewport.TabIndex = 28
        lblTitleViewport.Text = "视口"
        ' 
        ' chkTooltip
        ' 
        chkTooltip.AutoSize = True
        chkTooltip.Location = New Point(16, 758)
        chkTooltip.Name = "chkTooltip"
        chkTooltip.Size = New Size(147, 21)
        chkTooltip.TabIndex = 29
        chkTooltip.Text = "悬停显示体素数据提示"
        ' 
        ' btnAll
        ' 
        btnAll.FlatStyle = FlatStyle.Flat
        btnAll.Font = New Font("Microsoft YaHei UI", 8F)
        btnAll.Location = New Point(16, 784)
        btnAll.Name = "btnAll"
        btnAll.Size = New Size(62, 24)
        btnAll.TabIndex = 30
        btnAll.Text = "全选"
        ' 
        ' btnNone
        ' 
        btnNone.FlatStyle = FlatStyle.Flat
        btnNone.Font = New Font("Microsoft YaHei UI", 8F)
        btnNone.Location = New Point(84, 784)
        btnNone.Name = "btnNone"
        btnNone.Size = New Size(62, 24)
        btnNone.TabIndex = 31
        btnNone.Text = "清空"
        ' 
        ' clbTooltipFields
        ' 
        clbTooltipFields.BorderStyle = BorderStyle.FixedSingle
        clbTooltipFields.CheckOnClick = True
        clbTooltipFields.Font = New Font("Consolas", 8F)
        clbTooltipFields.HorizontalScrollbar = True
        clbTooltipFields.Location = New Point(12, 814)
        clbTooltipFields.Name = "clbTooltipFields"
        clbTooltipFields.Size = New Size(264, 167)
        clbTooltipFields.TabIndex = 32
        ' 
        ' chkDebug
        ' 
        chkDebug.AutoSize = True
        chkDebug.Location = New Point(16, 994)
        chkDebug.Name = "chkDebug"
        chkDebug.Size = New Size(149, 21)
        chkDebug.TabIndex = 33
        chkDebug.Text = "显示 DirectX 调试信息"
        ' 
        ' rightPanel
        ' 
        rightPanel.AutoScroll = True
        rightPanel.BackColor = Color.White
        rightPanel.Controls.Add(lblVoxelInfo)
        rightPanel.Controls.Add(pnlSeries)
        rightPanel.Controls.Add(lblSeriesHint)
        rightPanel.Controls.Add(picSlice)
        rightPanel.Controls.Add(propPanel)
        rightPanel.Dock = DockStyle.Right
        rightPanel.Location = New Point(1094, 0)
        rightPanel.Name = "rightPanel"
        rightPanel.Size = New Size(330, 757)
        rightPanel.TabIndex = 2
        ' 
        ' lblVoxelInfo
        ' 
        lblVoxelInfo.Dock = DockStyle.Top
        lblVoxelInfo.Location = New Point(0, 404)
        lblVoxelInfo.Name = "lblVoxelInfo"
        lblVoxelInfo.Padding = New Padding(16, 2, 8, 0)
        lblVoxelInfo.Size = New Size(313, 92)
        lblVoxelInfo.TabIndex = 0
        ' 
        ' pnlSeries
        ' 
        pnlSeries.Dock = DockStyle.Top
        pnlSeries.Location = New Point(0, 232)
        pnlSeries.Margin = New Padding(12, 0, 12, 0)
        pnlSeries.Name = "pnlSeries"
        pnlSeries.Size = New Size(313, 172)
        pnlSeries.TabIndex = 1
        ' 
        ' lblSeriesHint
        ' 
        lblSeriesHint.Dock = DockStyle.Top
        lblSeriesHint.Location = New Point(0, 214)
        lblSeriesHint.Name = "lblSeriesHint"
        lblSeriesHint.Padding = New Padding(16, 0, 0, 0)
        lblSeriesHint.Size = New Size(313, 18)
        lblSeriesHint.TabIndex = 2
        lblSeriesHint.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' picSlice
        ' 
        picSlice.Dock = DockStyle.Top
        picSlice.Location = New Point(0, 0)
        picSlice.Margin = New Padding(12, 0, 12, 0)
        picSlice.Name = "picSlice"
        picSlice.Size = New Size(313, 214)
        picSlice.TabIndex = 3
        picSlice.TabStop = False
        ' 
        ' propPanel
        ' 
        propPanel.BackColor = Color.White
        propPanel.Controls.Add(lblProp)
        propPanel.Controls.Add(pgVoxel)
        propPanel.Dock = DockStyle.Bottom
        propPanel.Location = New Point(0, 496)
        propPanel.Name = "propPanel"
        propPanel.Size = New Size(313, 316)
        propPanel.TabIndex = 4
        ' 
        ' lblProp
        ' 
        lblProp.Dock = DockStyle.Top
        lblProp.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblProp.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblProp.Location = New Point(0, 0)
        lblProp.Name = "lblProp"
        lblProp.Padding = New Padding(12, 0, 0, 0)
        lblProp.Size = New Size(313, 26)
        lblProp.TabIndex = 0
        lblProp.Text = "体素属性"
        lblProp.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' pgVoxel
        ' 
        pgVoxel.BackColor = Color.White
        pgVoxel.CategoryForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        pgVoxel.Dock = DockStyle.Fill
        pgVoxel.Font = New Font("Microsoft YaHei UI", 8.5F)
        pgVoxel.LineColor = Color.FromArgb(CByte(226), CByte(232), CByte(240))
        pgVoxel.Location = New Point(0, 0)
        pgVoxel.Name = "pgVoxel"
        pgVoxel.PropertySort = PropertySort.Alphabetical
        pgVoxel.Size = New Size(313, 316)
        pgVoxel.TabIndex = 1
        pgVoxel.ViewBackColor = Color.White
        ' 
        ' bottomPanel
        ' 
        bottomPanel.BackColor = Color.White
        bottomPanel.Controls.Add(btnPlay)
        bottomPanel.Controls.Add(lblSpeed)
        bottomPanel.Controls.Add(cboSpeed)
        bottomPanel.Controls.Add(trackFrame)
        bottomPanel.Controls.Add(lblFrame)
        bottomPanel.Dock = DockStyle.Bottom
        bottomPanel.Location = New Point(0, 757)
        bottomPanel.Name = "bottomPanel"
        bottomPanel.Size = New Size(1424, 64)
        bottomPanel.TabIndex = 3
        ' 
        ' btnPlay
        ' 
        btnPlay.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        btnPlay.Enabled = False
        btnPlay.FlatAppearance.BorderSize = 0
        btnPlay.FlatStyle = FlatStyle.Flat
        btnPlay.Font = New Font("Microsoft YaHei UI", 11F)
        btnPlay.ForeColor = Color.White
        btnPlay.Location = New Point(16, 12)
        btnPlay.Name = "btnPlay"
        btnPlay.Size = New Size(40, 40)
        btnPlay.TabIndex = 0
        btnPlay.Text = "▶"
        btnPlay.UseVisualStyleBackColor = False
        ' 
        ' lblSpeed
        ' 
        lblSpeed.AutoSize = True
        lblSpeed.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSpeed.Location = New Point(72, 22)
        lblSpeed.Name = "lblSpeed"
        lblSpeed.Size = New Size(32, 17)
        lblSpeed.TabIndex = 1
        lblSpeed.Text = "速度"
        ' 
        ' cboSpeed
        ' 
        cboSpeed.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSpeed.DropDownStyle = ComboBoxStyle.DropDownList
        cboSpeed.FlatStyle = FlatStyle.Flat
        cboSpeed.Font = New Font("Microsoft YaHei UI", 9F)
        cboSpeed.Items.AddRange(New Object() {"2 fps", "5 fps", "10 fps", "20 fps", "30 fps"})
        cboSpeed.Location = New Point(112, 18)
        cboSpeed.Name = "cboSpeed"
        cboSpeed.Size = New Size(84, 25)
        cboSpeed.TabIndex = 2
        ' 
        ' trackFrame
        ' 
        trackFrame.Enabled = False
        trackFrame.Location = New Point(210, 20)
        trackFrame.Maximum = 0
        trackFrame.Name = "trackFrame"
        trackFrame.Size = New Size(400, 45)
        trackFrame.TabIndex = 3
        trackFrame.TickStyle = TickStyle.None
        ' 
        ' lblFrame
        ' 
        lblFrame.AutoSize = True
        lblFrame.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblFrame.Location = New Point(0, 24)
        lblFrame.Name = "lblFrame"
        lblFrame.Size = New Size(45, 17)
        lblFrame.TabIndex = 4
        lblFrame.Text = "— · —"
        ' 
        ' DemoTestForm
        ' 
        BackColor = Color.FromArgb(CByte(244), CByte(246), CByte(250))
        ClientSize = New Size(1424, 821)
        Controls.Add(m_canvas)
        Controls.Add(leftPanel)
        Controls.Add(rightPanel)
        Controls.Add(bottomPanel)
        Font = New Font("Microsoft YaHei UI", 9F)
        MinimumSize = New Size(1100, 640)
        Name = "DemoTestForm"
        StartPosition = FormStartPosition.CenterScreen
        Text = "CFD 可视化 · CFDCanvas 控件测试"
        leftPanel.ResumeLayout(False)
        leftPanel.PerformLayout()
        CType(trackThreshold, ISupportInitialize).EndInit()
        CType(trackSectionPos, ISupportInitialize).EndInit()
        rightPanel.ResumeLayout(False)
        CType(picSlice, ISupportInitialize).EndInit()
        propPanel.ResumeLayout(False)
        bottomPanel.ResumeLayout(False)
        bottomPanel.PerformLayout()
        CType(trackFrame, ISupportInitialize).EndInit()
        ResumeLayout(False)
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
