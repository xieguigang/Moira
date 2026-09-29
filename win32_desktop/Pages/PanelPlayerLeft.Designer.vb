Imports Galaxy.Workbench.DockDocument

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PanelPlayerLeft
    Inherits ToolWindow


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
        components = New ComponentModel.Container()
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
        lblOpacityTitle = New Label()
        lblOpacityVal = New Label()
        trackOpacity = New TrackBar()
        CType(trackThreshold, ComponentModel.ISupportInitialize).BeginInit()
        CType(trackSectionPos, ComponentModel.ISupportInitialize).BeginInit()
        CType(trackOpacity, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        ' 
        ' lblTitleData
        ' 
        lblTitleData.AutoSize = True
        lblTitleData.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleData.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleData.Location = New Point(18, 9)
        lblTitleData.Name = "lblTitleData"
        lblTitleData.Size = New Size(37, 19)
        lblTitleData.TabIndex = 34
        lblTitleData.Text = "数据"
        ' 
        ' btnLoad
        ' 
        btnLoad.BackColor = Color.FromArgb(CByte(37), CByte(99), CByte(235))
        btnLoad.FlatAppearance.BorderSize = 0
        btnLoad.FlatStyle = FlatStyle.Flat
        btnLoad.Font = New Font("Microsoft YaHei UI", 9F)
        btnLoad.ForeColor = Color.White
        btnLoad.Location = New Point(18, 49)
        btnLoad.Name = "btnLoad"
        btnLoad.Size = New Size(140, 32)
        btnLoad.TabIndex = 35
        btnLoad.Text = "加载数据文件夹"
        btnLoad.UseVisualStyleBackColor = False
        ' 
        ' lblTitleScalar
        ' 
        lblTitleScalar.AutoSize = True
        lblTitleScalar.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleScalar.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleScalar.Location = New Point(18, 93)
        lblTitleScalar.Name = "lblTitleScalar"
        lblTitleScalar.Size = New Size(51, 19)
        lblTitleScalar.TabIndex = 36
        lblTitleScalar.Text = "标量场"
        ' 
        ' lblScalarHint
        ' 
        lblScalarHint.AutoSize = True
        lblScalarHint.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblScalarHint.Location = New Point(18, 121)
        lblScalarHint.Name = "lblScalarHint"
        lblScalarHint.Size = New Size(185, 15)
        lblScalarHint.TabIndex = 37
        lblScalarHint.Text = "标量场（由 VTI 实际字段生成）"
        ' 
        ' cboField
        ' 
        cboField.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboField.DropDownStyle = ComboBoxStyle.DropDownList
        cboField.DropDownWidth = 420
        cboField.FlatStyle = FlatStyle.Flat
        cboField.Font = New Font("Microsoft YaHei UI", 9F)
        cboField.Location = New Point(18, 143)
        cboField.Name = "cboField"
        cboField.Size = New Size(276, 25)
        cboField.TabIndex = 38
        ' 
        ' lblPaletteCaption
        ' 
        lblPaletteCaption.AutoSize = True
        lblPaletteCaption.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblPaletteCaption.Location = New Point(18, 177)
        lblPaletteCaption.Name = "lblPaletteCaption"
        lblPaletteCaption.Size = New Size(46, 15)
        lblPaletteCaption.TabIndex = 39
        lblPaletteCaption.Text = "调色板"
        ' 
        ' cboPalette
        ' 
        cboPalette.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboPalette.DropDownStyle = ComboBoxStyle.DropDownList
        cboPalette.FlatStyle = FlatStyle.Flat
        cboPalette.Font = New Font("Microsoft YaHei UI", 9F)
        cboPalette.Items.AddRange(New Object() {"Jet", "Autumn", "Cool", "Gray", "Hot", "Spring", "Summer", "Winter", "Red", "Green", "Blue", "ColorBrewer_OrRd", "ColorBrewer_PuBu", "ColorBrewer_BuPu", "ColorBrewer_Oranges", "ColorBrewer_BuGn", "ColorBrewer_YlOrBr", "ColorBrewer_YlGn", "ColorBrewer_RdPu", "ColorBrewer_YlGnBu", "ColorBrewer_Purples", "ColorBrewer_GnBu", "ColorBrewer_YlOrRd", "ColorBrewer_PuRd", "ColorBrewer_PuBuGn", "Rainbow", "FlexImaging", "Typhoon", "Icefire", "Seismic", "viridis", "magma", "inferno", "plasma", "cividis", "mako", "rocket", "turbo"})
        cboPalette.Location = New Point(18, 199)
        cboPalette.Name = "cboPalette"
        cboPalette.Size = New Size(276, 25)
        cboPalette.TabIndex = 40
        ' 
        ' lblTitleRange
        ' 
        lblTitleRange.AutoSize = True
        lblTitleRange.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleRange.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleRange.Location = New Point(18, 239)
        lblTitleRange.Name = "lblTitleRange"
        lblTitleRange.Size = New Size(65, 19)
        lblTitleRange.TabIndex = 41
        lblTitleRange.Text = "颜色值域"
        ' 
        ' cboRangeMode
        ' 
        cboRangeMode.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboRangeMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboRangeMode.FlatStyle = FlatStyle.Flat
        cboRangeMode.Font = New Font("Microsoft YaHei UI", 9F)
        cboRangeMode.Items.AddRange(New Object() {"自动", "手动"})
        cboRangeMode.Location = New Point(18, 267)
        cboRangeMode.Name = "cboRangeMode"
        cboRangeMode.Size = New Size(276, 25)
        cboRangeMode.TabIndex = 42
        ' 
        ' lblMin
        ' 
        lblMin.AutoSize = True
        lblMin.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblMin.Location = New Point(18, 307)
        lblMin.Name = "lblMin"
        lblMin.Size = New Size(46, 15)
        lblMin.TabIndex = 43
        lblMin.Text = "最小值"
        ' 
        ' lblMax
        ' 
        lblMax.AutoSize = True
        lblMax.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblMax.Location = New Point(157, 307)
        lblMax.Name = "lblMax"
        lblMax.Size = New Size(46, 15)
        lblMax.TabIndex = 44
        lblMax.Text = "最大值"
        ' 
        ' txtRangeMin
        ' 
        txtRangeMin.BorderStyle = BorderStyle.FixedSingle
        txtRangeMin.Font = New Font("Microsoft YaHei UI", 9F)
        txtRangeMin.Location = New Point(67, 304)
        txtRangeMin.Name = "txtRangeMin"
        txtRangeMin.Size = New Size(84, 23)
        txtRangeMin.TabIndex = 45
        txtRangeMin.Visible = False
        ' 
        ' txtRangeMax
        ' 
        txtRangeMax.BorderStyle = BorderStyle.FixedSingle
        txtRangeMax.Font = New Font("Microsoft YaHei UI", 9F)
        txtRangeMax.Location = New Point(209, 304)
        txtRangeMax.Name = "txtRangeMax"
        txtRangeMax.Size = New Size(85, 23)
        txtRangeMax.TabIndex = 46
        txtRangeMax.Visible = False
        ' 
        ' lblThresholdTitle
        ' 
        lblThresholdTitle.AutoSize = True
        lblThresholdTitle.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblThresholdTitle.Location = New Point(18, 385)
        lblThresholdTitle.Name = "lblThresholdTitle"
        lblThresholdTitle.Size = New Size(59, 15)
        lblThresholdTitle.TabIndex = 47
        lblThresholdTitle.Text = "透明阈值"
        ' 
        ' lblThresholdVal
        ' 
        lblThresholdVal.AutoSize = True
        lblThresholdVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblThresholdVal.Location = New Point(86, 385)
        lblThresholdVal.Name = "lblThresholdVal"
        lblThresholdVal.Size = New Size(28, 15)
        lblThresholdVal.TabIndex = 48
        lblThresholdVal.Text = "0.00"
        ' 
        ' trackThreshold
        ' 
        trackThreshold.Location = New Point(14, 349)
        trackThreshold.Maximum = 100
        trackThreshold.Name = "trackThreshold"
        trackThreshold.Size = New Size(280, 45)
        trackThreshold.TabIndex = 49
        trackThreshold.TickStyle = TickStyle.None
        ' 
        ' lblTitleArrows
        ' 
        lblTitleArrows.AutoSize = True
        lblTitleArrows.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleArrows.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleArrows.Location = New Point(14, 426)
        lblTitleArrows.Name = "lblTitleArrows"
        lblTitleArrows.Size = New Size(93, 19)
        lblTitleArrows.TabIndex = 50
        lblTitleArrows.Text = "速度矢量箭头"
        ' 
        ' chkArrows
        ' 
        chkArrows.AutoSize = True
        chkArrows.Location = New Point(18, 451)
        chkArrows.Name = "chkArrows"
        chkArrows.Size = New Size(78, 19)
        chkArrows.TabIndex = 51
        chkArrows.Text = "显示箭头"
        ' 
        ' lblArrowDensity
        ' 
        lblArrowDensity.AutoSize = True
        lblArrowDensity.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblArrowDensity.Location = New Point(18, 483)
        lblArrowDensity.Name = "lblArrowDensity"
        lblArrowDensity.Size = New Size(59, 15)
        lblArrowDensity.TabIndex = 52
        lblArrowDensity.Text = "箭头密度"
        ' 
        ' cboArrowDensity
        ' 
        cboArrowDensity.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboArrowDensity.DropDownStyle = ComboBoxStyle.DropDownList
        cboArrowDensity.FlatStyle = FlatStyle.Flat
        cboArrowDensity.Font = New Font("Microsoft YaHei UI", 9F)
        cboArrowDensity.Items.AddRange(New Object() {"高 (2×2×2)", "中 (3×3×3)", "低 (4×4×4)"})
        cboArrowDensity.Location = New Point(17, 503)
        cboArrowDensity.Name = "cboArrowDensity"
        cboArrowDensity.Size = New Size(277, 25)
        cboArrowDensity.TabIndex = 53
        ' 
        ' lblTitleSection
        ' 
        lblTitleSection.AutoSize = True
        lblTitleSection.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleSection.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleSection.Location = New Point(17, 546)
        lblTitleSection.Name = "lblTitleSection"
        lblTitleSection.Size = New Size(51, 19)
        lblTitleSection.TabIndex = 54
        lblTitleSection.Text = "横截面"
        ' 
        ' lblSectionModeLabel
        ' 
        lblSectionModeLabel.AutoSize = True
        lblSectionModeLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSectionModeLabel.Location = New Point(18, 575)
        lblSectionModeLabel.Name = "lblSectionModeLabel"
        lblSectionModeLabel.Size = New Size(59, 15)
        lblSectionModeLabel.TabIndex = 55
        lblSectionModeLabel.Text = "截面模式"
        ' 
        ' cboSectionMode
        ' 
        cboSectionMode.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSectionMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionMode.FlatStyle = FlatStyle.Flat
        cboSectionMode.Font = New Font("Microsoft YaHei UI", 9F)
        cboSectionMode.Items.AddRange(New Object() {"不启用", "启用（裁剪远侧）", "切片模式（单层）"})
        cboSectionMode.Location = New Point(18, 595)
        cboSectionMode.Name = "cboSectionMode"
        cboSectionMode.Size = New Size(276, 25)
        cboSectionMode.TabIndex = 56
        ' 
        ' lblSectionAxisLabel
        ' 
        lblSectionAxisLabel.AutoSize = True
        lblSectionAxisLabel.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblSectionAxisLabel.Location = New Point(18, 623)
        lblSectionAxisLabel.Name = "lblSectionAxisLabel"
        lblSectionAxisLabel.Size = New Size(46, 15)
        lblSectionAxisLabel.TabIndex = 57
        lblSectionAxisLabel.Text = "截面轴"
        ' 
        ' cboSectionAxis
        ' 
        cboSectionAxis.BackColor = Color.FromArgb(CByte(248), CByte(250), CByte(252))
        cboSectionAxis.DropDownStyle = ComboBoxStyle.DropDownList
        cboSectionAxis.FlatStyle = FlatStyle.Flat
        cboSectionAxis.Font = New Font("Microsoft YaHei UI", 9F)
        cboSectionAxis.Items.AddRange(New Object() {"X 轴", "Y 轴", "Z 轴"})
        cboSectionAxis.Location = New Point(17, 653)
        cboSectionAxis.Name = "cboSectionAxis"
        cboSectionAxis.Size = New Size(277, 25)
        cboSectionAxis.TabIndex = 58
        ' 
        ' lblPosition
        ' 
        lblPosition.AutoSize = True
        lblPosition.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblPosition.Location = New Point(20, 735)
        lblPosition.Name = "lblPosition"
        lblPosition.Size = New Size(33, 15)
        lblPosition.TabIndex = 59
        lblPosition.Text = "位置"
        ' 
        ' lblSectionPosVal
        ' 
        lblSectionPosVal.AutoSize = True
        lblSectionPosVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblSectionPosVal.Location = New Point(59, 735)
        lblSectionPosVal.Name = "lblSectionPosVal"
        lblSectionPosVal.Size = New Size(13, 15)
        lblSectionPosVal.TabIndex = 60
        lblSectionPosVal.Text = "0"
        ' 
        ' trackSectionPos
        ' 
        trackSectionPos.Location = New Point(14, 701)
        trackSectionPos.Maximum = 47
        trackSectionPos.Name = "trackSectionPos"
        trackSectionPos.Size = New Size(280, 45)
        trackSectionPos.TabIndex = 61
        trackSectionPos.TickStyle = TickStyle.None
        ' 
        ' lblTitleViewport
        ' 
        lblTitleViewport.AutoSize = True
        lblTitleViewport.Font = New Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        lblTitleViewport.ForeColor = Color.FromArgb(CByte(30), CByte(41), CByte(59))
        lblTitleViewport.Location = New Point(18, 774)
        lblTitleViewport.Name = "lblTitleViewport"
        lblTitleViewport.Size = New Size(37, 19)
        lblTitleViewport.TabIndex = 62
        lblTitleViewport.Text = "视口"
        ' 
        ' lblOpacityTitle
        ' 
        lblOpacityTitle.AutoSize = True
        lblOpacityTitle.ForeColor = Color.FromArgb(CByte(71), CByte(85), CByte(105))
        lblOpacityTitle.Location = New Point(18, 802)
        lblOpacityTitle.Name = "lblOpacityTitle"
        lblOpacityTitle.Size = New Size(73, 15)
        lblOpacityTitle.TabIndex = 68
        lblOpacityTitle.Text = "体素透明度"
        ' 
        ' lblOpacityVal
        ' 
        lblOpacityVal.AutoSize = True
        lblOpacityVal.ForeColor = Color.FromArgb(CByte(14), CByte(165), CByte(233))
        lblOpacityVal.Location = New Point(104, 802)
        lblOpacityVal.Name = "lblOpacityVal"
        lblOpacityVal.Size = New Size(28, 15)
        lblOpacityVal.TabIndex = 69
        lblOpacityVal.Text = "1.00"
        ' 
        ' trackOpacity
        ' 
        trackOpacity.Location = New Point(14, 820)
        trackOpacity.Maximum = 100
        trackOpacity.Minimum = 50
        trackOpacity.Name = "trackOpacity"
        trackOpacity.Size = New Size(280, 45)
        trackOpacity.TabIndex = 70
        trackOpacity.TickStyle = TickStyle.None
        trackOpacity.Value = 100
        ' 
        ' chkTooltip
        ' 
        chkTooltip.AutoSize = True
        chkTooltip.Location = New Point(18, 872)
        chkTooltip.Name = "chkTooltip"
        chkTooltip.Size = New Size(156, 19)
        chkTooltip.TabIndex = 63
        chkTooltip.Text = "悬停显示体素数据提示"
        ' 
        ' btnAll
        ' 
        btnAll.FlatStyle = FlatStyle.Flat
        btnAll.Font = New Font("Microsoft YaHei UI", 8F)
        btnAll.Location = New Point(18, 898)
        btnAll.Name = "btnAll"
        btnAll.Size = New Size(62, 24)
        btnAll.TabIndex = 64
        btnAll.Text = "全选"
        ' 
        ' btnNone
        ' 
        btnNone.FlatStyle = FlatStyle.Flat
        btnNone.Font = New Font("Microsoft YaHei UI", 8F)
        btnNone.Location = New Point(86, 898)
        btnNone.Name = "btnNone"
        btnNone.Size = New Size(62, 24)
        btnNone.TabIndex = 65
        btnNone.Text = "清空"
        ' 
        ' clbTooltipFields
        ' 
        clbTooltipFields.BorderStyle = BorderStyle.FixedSingle
        clbTooltipFields.CheckOnClick = True
        clbTooltipFields.Font = New Font("Consolas", 8F)
        clbTooltipFields.HorizontalScrollbar = True
        clbTooltipFields.Location = New Point(14, 927)
        clbTooltipFields.Name = "clbTooltipFields"
        clbTooltipFields.Size = New Size(280, 227)
        clbTooltipFields.TabIndex = 66
        ' 
        ' chkDebug
        ' 
        chkDebug.AutoSize = True
        chkDebug.Location = New Point(14, 1158)
        chkDebug.Name = "chkDebug"
        chkDebug.Size = New Size(148, 19)
        chkDebug.TabIndex = 67
        chkDebug.Text = "显示 DirectX 调试信息"
        ' 
        ' PanelPlayerLeft
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoScroll = True
        BackColor = Color.White
        ClientSize = New Size(309, 1189)
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
        Controls.Add(lblOpacityTitle)
        Controls.Add(lblOpacityVal)
        Controls.Add(trackOpacity)
        Controls.Add(chkTooltip)
        Controls.Add(btnAll)
        Controls.Add(btnNone)
        Controls.Add(clbTooltipFields)
        Controls.Add(chkDebug)
        DockAreas = Microsoft.VisualStudio.WinForms.Docking.DockAreas.Float Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockLeft Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockRight Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockTop Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.DockBottom Or Microsoft.VisualStudio.WinForms.Docking.DockAreas.Document
        DoubleBuffered = True
        Name = "PanelPlayerLeft"
        ShowHint = Microsoft.VisualStudio.WinForms.Docking.DockState.Unknown
        Text = "CFD 控制面板"
        CType(trackThreshold, ComponentModel.ISupportInitialize).EndInit()
        CType(trackSectionPos, ComponentModel.ISupportInitialize).EndInit()
        CType(trackOpacity, ComponentModel.ISupportInitialize).EndInit()
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
    Friend WithEvents lblOpacityTitle As Label
    Friend WithEvents lblOpacityVal As Label
    Friend WithEvents trackOpacity As TrackBar
End Class
