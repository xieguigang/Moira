Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports Galaxy.Workbench.DockDocument
Imports CDFDxCanvas
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PanelPlayerLeft
    Inherits ToolWindow

    Sub New()
        Call InitializeComponent()
    End Sub

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
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
        CType(trackThreshold, ISupportInitialize).BeginInit()
        CType(trackSectionPos, ISupportInitialize).BeginInit()
        SuspendLayout()
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
        lblThresholdTitle.Location = New Point(16, 390)
        lblThresholdTitle.Name = "lblThresholdTitle"
        lblThresholdTitle.Size = New Size(56, 17)
        lblThresholdTitle.TabIndex = 13
        lblThresholdTitle.Text = "透明阈值"
        ' 
        ' lblThresholdVal
        ' 
        lblThresholdVal.AutoSize = True
        lblThresholdVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblThresholdVal.Location = New Point(278, 366)
        lblThresholdVal.Name = "lblThresholdVal"
        lblThresholdVal.Size = New Size(32, 17)
        lblThresholdVal.TabIndex = 14
        lblThresholdVal.Text = "0.00"
        ' 
        ' trackThreshold
        ' 
        trackThreshold.Location = New Point(12, 352)
        trackThreshold.Maximum = 100
        trackThreshold.Name = "trackThreshold"
        trackThreshold.Size = New Size(260, 45)
        trackThreshold.TabIndex = 15
        trackThreshold.TickStyle = TickStyle.None
        ' 
        ' lblTitleArrows
        ' 
        lblTitleArrows.AutoSize = True
        lblTitleArrows.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
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
        cboArrowDensity.Font = New Font("Microsoft YaHei UI", 9.0F)
        cboArrowDensity.Items.AddRange(New Object() {"高 (2×2×2)", "中 (3×3×3)", "低 (4×4×4)"})
        cboArrowDensity.Location = New Point(16, 488)
        cboArrowDensity.Name = "cboArrowDensity"
        cboArrowDensity.Size = New Size(250, 25)
        cboArrowDensity.TabIndex = 19
        ' 
        ' lblTitleSection
        ' 
        lblTitleSection.AutoSize = True
        lblTitleSection.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
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
        cboSectionMode.Font = New Font("Microsoft YaHei UI", 9.0F)
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
        cboSectionAxis.Font = New Font("Microsoft YaHei UI", 9.0F)
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
        lblPosition.Location = New Point(16, 723)
        lblPosition.Name = "lblPosition"
        lblPosition.Size = New Size(32, 17)
        lblPosition.TabIndex = 25
        lblPosition.Text = "位置"
        ' 
        ' lblSectionPosVal
        ' 
        lblSectionPosVal.AutoSize = True
        lblSectionPosVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblSectionPosVal.Location = New Point(278, 688)
        lblSectionPosVal.Name = "lblSectionPosVal"
        lblSectionPosVal.Size = New Size(15, 17)
        lblSectionPosVal.TabIndex = 26
        lblSectionPosVal.Text = "0"
        ' 
        ' trackSectionPos
        ' 
        trackSectionPos.Location = New Point(12, 675)
        trackSectionPos.Maximum = 47
        trackSectionPos.Name = "trackSectionPos"
        trackSectionPos.Size = New Size(260, 45)
        trackSectionPos.TabIndex = 27
        trackSectionPos.TickStyle = TickStyle.None
        ' 
        ' lblTitleViewport
        ' 
        lblTitleViewport.AutoSize = True
        lblTitleViewport.Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold)
        lblTitleViewport.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleViewport.Location = New Point(16, 755)
        lblTitleViewport.Name = "lblTitleViewport"
        lblTitleViewport.Size = New Size(37, 19)
        lblTitleViewport.TabIndex = 28
        lblTitleViewport.Text = "视口"
        ' 
        ' chkTooltip
        ' 
        chkTooltip.AutoSize = True
        chkTooltip.Location = New Point(16, 783)
        chkTooltip.Name = "chkTooltip"
        chkTooltip.Size = New Size(147, 21)
        chkTooltip.TabIndex = 29
        chkTooltip.Text = "悬停显示体素数据提示"
        ' 
        ' btnAll
        ' 
        btnAll.FlatStyle = FlatStyle.Flat
        btnAll.Font = New Font("Microsoft YaHei UI", 8.0F)
        btnAll.Location = New Point(16, 809)
        btnAll.Name = "btnAll"
        btnAll.Size = New Size(62, 24)
        btnAll.TabIndex = 30
        btnAll.Text = "全选"
        ' 
        ' btnNone
        ' 
        btnNone.FlatStyle = FlatStyle.Flat
        btnNone.Font = New Font("Microsoft YaHei UI", 8.0F)
        btnNone.Location = New Point(84, 809)
        btnNone.Name = "btnNone"
        btnNone.Size = New Size(62, 24)
        btnNone.TabIndex = 31
        btnNone.Text = "清空"
        ' 
        ' clbTooltipFields
        ' 
        clbTooltipFields.BorderStyle = BorderStyle.FixedSingle
        clbTooltipFields.CheckOnClick = True
        clbTooltipFields.Font = New Font("Consolas", 8.0F)
        clbTooltipFields.HorizontalScrollbar = True
        clbTooltipFields.Location = New Point(12, 839)
        clbTooltipFields.Name = "clbTooltipFields"
        clbTooltipFields.Size = New Size(264, 167)
        clbTooltipFields.TabIndex = 32
        ' 
        ' chkDebug
        ' 
        chkDebug.AutoSize = True
        chkDebug.Location = New Point(12, 1012)
        chkDebug.Name = "chkDebug"
        chkDebug.Size = New Size(149, 21)
        chkDebug.TabIndex = 33
        chkDebug.Text = "显示 DirectX 调试信息"
        ' 
        ' PanelPlayerLeft
        ' 
        AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        AutoScroll = True
        BackColor = Color.White
        ClientSize = New Size(349, 1056)
        Controls.Add(lblTitleData)
        Controls.Add(btnLoad)
        Controls.Add(lblTitleScalar)
        Controls.Add(lblScalarHint)
        Controls.Add(cboField)
        Controls.Add(lblPaletteCaption)
        Controls.Add(cboPalette)
        Controls.Add(lblTitleRange)
        Controls.Add(cboRangeMode)
        Controls.Add(lblMin)
        Controls.Add(lblMax)
        Controls.Add(txtRangeMin)
        Controls.Add(txtRangeMax)
        Controls.Add(lblThresholdTitle)
        Controls.Add(lblThresholdVal)
        Controls.Add(trackThreshold)
        Controls.Add(lblTitleArrows)
        Controls.Add(chkArrows)
        Controls.Add(lblArrowDensity)
        Controls.Add(cboArrowDensity)
        Controls.Add(lblTitleSection)
        Controls.Add(lblSectionModeLabel)
        Controls.Add(cboSectionMode)
        Controls.Add(lblSectionAxisLabel)
        Controls.Add(cboSectionAxis)
        Controls.Add(lblPosition)
        Controls.Add(lblSectionPosVal)
        Controls.Add(trackSectionPos)
        Controls.Add(lblTitleViewport)
        Controls.Add(chkTooltip)
        Controls.Add(btnAll)
        Controls.Add(btnNone)
        Controls.Add(clbTooltipFields)
        Controls.Add(chkDebug)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Font = New Font("Microsoft YaHei UI", 9.0F)
        Name = "PanelPlayerLeft"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        Text = "CFD 控制面板"
        CType(trackThreshold, ISupportInitialize).EndInit()
        CType(trackSectionPos, ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents lblTitleData As Label
    Friend WithEvents btnLoad As Button
    Friend WithEvents lblTitleScalar As Label
    Friend WithEvents lblScalarHint As Label
    Friend WithEvents cboField As ComboBox
    Friend WithEvents lblPaletteCaption As Label
    Friend WithEvents cboPalette As ComboBox
    Friend WithEvents lblTitleRange As Label
    Friend WithEvents cboRangeMode As ComboBox
    Friend WithEvents lblMin As Label
    Friend WithEvents lblMax As Label
    Friend WithEvents txtRangeMin As TextBox
    Friend WithEvents txtRangeMax As TextBox
    Friend WithEvents lblThresholdTitle As Label
    Friend WithEvents lblThresholdVal As Label
    Friend WithEvents trackThreshold As TrackBar
    Friend WithEvents lblTitleArrows As Label
    Friend WithEvents chkArrows As CheckBox
    Friend WithEvents lblArrowDensity As Label
    Friend WithEvents cboArrowDensity As ComboBox
    Friend WithEvents lblTitleSection As Label
    Friend WithEvents lblSectionModeLabel As Label
    Friend WithEvents cboSectionMode As ComboBox
    Friend WithEvents lblSectionAxisLabel As Label
    Friend WithEvents cboSectionAxis As ComboBox
    Friend WithEvents lblPosition As Label
    Friend WithEvents lblSectionPosVal As Label
    Friend WithEvents trackSectionPos As TrackBar
    Friend WithEvents lblTitleViewport As Label
    Friend WithEvents chkTooltip As CheckBox
    Friend WithEvents btnAll As Button
    Friend WithEvents btnNone As Button
    Friend WithEvents clbTooltipFields As CheckedListBox
    Friend WithEvents chkDebug As CheckBox
End Class
