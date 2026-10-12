' /********************************************************************************/
'
'   FormVoxelizer.Designer.vb
'
'   Voxelizer.exe（三维模型 → CFD 体素模型包）的图形化参数窗体布局。
'
' /********************************************************************************/

Imports System.Drawing
Imports System.Windows.Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormVoxelizer
    Inherits System.Windows.Forms.Form

    'Form 重写 Dispose，以清理组件列表。
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

    'Windows 窗体设计器所必需的
    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        tblRoot = New TableLayoutPanel()
        pnlHeader = New Panel()
        lblTitle = New Label()
        lblSub = New Label()
        flpParams = New FlowLayoutPanel()

        grpIO = New GroupBox()
        flpIO = New FlowLayoutPanel()
        txtModel = New TextBox()
        btnModel = New Button()
        txtOutput = New TextBox()
        btnOutput = New Button()

        grpVoxel = New GroupBox()
        flpVoxel = New FlowLayoutPanel()
        cboKind = New ComboBox()
        nudResolution = New NumericUpDown()
        nudVoxelSize = New NumericUpDown()
        nudSubSamples = New NumericUpDown()
        nudSdfResolution = New NumericUpDown()

        grpTool = New GroupBox()
        flpTool = New FlowLayoutPanel()
        txtExe = New TextBox()
        btnExe = New Button()

        pnlCommand = New Panel()
        lblCommandTitle = New Label()
        txtCommand = New TextBox()
        btnCopy = New Button()
        pnlActions = New Panel()
        btnRun = New Button()
        btnStop = New Button()
        btnOpenOutput = New Button()
        btnClose = New Button()
        pnlLog = New Panel()
        lblLogTitle = New Label()
        txtLog = New TextBox()
        pnlStatus = New Panel()
        lblStatus = New Label()
        lblElapsed = New Label()
        Timer1 = New Timer(components)

        Call tblRoot.SuspendLayout()
        Call pnlHeader.SuspendLayout()
        Call flpIO.SuspendLayout()
        Call flpVoxel.SuspendLayout()
        Call flpTool.SuspendLayout()
        Call pnlCommand.SuspendLayout()
        Call pnlActions.SuspendLayout()
        Call pnlLog.SuspendLayout()
        Call pnlStatus.SuspendLayout()
        Call SuspendLayout()

        ' 
        ' tblRoot
        ' 
        tblRoot.BackColor = CliToolLayout.ColorBg
        tblRoot.ColumnCount = 1
        tblRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
        tblRoot.Dock = DockStyle.Fill
        tblRoot.Location = New Point(0, 0)
        tblRoot.Name = "tblRoot"
        tblRoot.RowCount = 6
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 68.0!))
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 96.0!))
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 52.0!))
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 220.0!))
        tblRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0!))
        tblRoot.TabIndex = 0

        ' 
        ' pnlHeader
        ' 
        pnlHeader.BackColor = CliToolLayout.ColorCard
        pnlHeader.Controls.Add(lblTitle)
        pnlHeader.Controls.Add(lblSub)
        pnlHeader.Dock = DockStyle.Fill
        pnlHeader.Location = New Point(0, 0)
        pnlHeader.Name = "pnlHeader"
        pnlHeader.Size = New Size(920, 68)
        pnlHeader.TabIndex = 0
        ' 
        ' lblTitle
        ' 
        lblTitle.AutoSize = True
        lblTitle.Font = New Font("Segoe UI", 14.0!, FontStyle.Bold)
        lblTitle.ForeColor = CliToolLayout.ColorText
        lblTitle.Location = New Point(16, 12)
        lblTitle.Name = "lblTitle"
        lblTitle.Size = New Size(160, 25)
        lblTitle.TabIndex = 0
        lblTitle.Text = "Voxelizer"
        ' 
        ' lblSub
        ' 
        lblSub.AutoSize = True
        lblSub.ForeColor = CliToolLayout.ColorTextDim
        lblSub.Location = New Point(18, 42)
        lblSub.Name = "lblSub"
        lblSub.Size = New Size(420, 15)
        lblSub.TabIndex = 1
        lblSub.Text = "三维模型文件 → CFD 体素模型包（*.voxel.zip / GridInfo JSON）"

        ' 
        ' flpParams
        ' 
        flpParams.AutoScroll = True
        flpParams.BackColor = CliToolLayout.ColorCard
        flpParams.Dock = DockStyle.Fill
        flpParams.FlowDirection = FlowDirection.TopDown
        flpParams.Location = New Point(0, 68)
        flpParams.Name = "flpParams"
        flpParams.Padding = New Padding(12, 8, 12, 8)
        flpParams.Size = New Size(920, 298)
        flpParams.TabIndex = 1
        flpParams.WrapContents = False

        ' 
        ' grpIO
        ' 
        grpIO.Controls.Add(flpIO)
        grpIO.Location = New Point(12, 8)
        grpIO.Margin = New Padding(0, 2, 0, 6)
        grpIO.Name = "grpIO"
        grpIO.Size = New Size(CliToolLayout.RowWidth, 126)
        grpIO.TabIndex = 0
        grpIO.TabStop = False
        grpIO.Text = "输入 / 输出"
        ' 
        ' flpIO
        ' 
        flpIO.Dock = DockStyle.Fill
        flpIO.FlowDirection = FlowDirection.TopDown
        flpIO.Location = New Point(3, 19)
        flpIO.Name = "flpIO"
        flpIO.Padding = New Padding(6, 6, 6, 0)
        flpIO.Size = New Size(714, 104)
        flpIO.TabIndex = 0
        flpIO.WrapContents = False
        ' 
        ' txtModel
        ' 
        txtModel.Name = "txtModel"
        txtModel.Size = New Size(390, 23)
        txtModel.TabIndex = 0
        ' 
        ' btnModel
        ' 
        btnModel = CliToolLayout.MakeBrowseButton()
        btnModel.Name = "btnModel"
        btnModel.TabIndex = 1
        ' 
        ' txtOutput
        ' 
        txtOutput.Name = "txtOutput"
        txtOutput.Size = New Size(390, 23)
        txtOutput.TabIndex = 2
        ' 
        ' btnOutput
        ' 
        btnOutput = CliToolLayout.MakeBrowseButton()
        btnOutput.Name = "btnOutput"
        btnOutput.TabIndex = 3

        Call flpIO.Controls.Add(CliToolLayout.MakeRow("模型文件", txtModel, btnModel))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("输出体素包", txtOutput, btnOutput))
        Call flpIO.Controls.Add(CliToolLayout.MakeNote("    输出留空时默认 <模型>.voxel.zip；*.json 为兼容旧格式，高分辨率下可能内存不足"))

        ' 
        ' grpVoxel
        ' 
        grpVoxel.Controls.Add(flpVoxel)
        grpVoxel.Location = New Point(12, 142)
        grpVoxel.Margin = New Padding(0, 2, 0, 6)
        grpVoxel.Name = "grpVoxel"
        grpVoxel.Size = New Size(CliToolLayout.RowWidth, 216)
        grpVoxel.TabIndex = 1
        grpVoxel.TabStop = False
        grpVoxel.Text = "体素化参数"
        ' 
        ' flpVoxel
        ' 
        flpVoxel.Dock = DockStyle.Fill
        flpVoxel.FlowDirection = FlowDirection.TopDown
        flpVoxel.Location = New Point(3, 19)
        flpVoxel.Name = "flpVoxel"
        flpVoxel.Padding = New Padding(6, 6, 6, 0)
        flpVoxel.Size = New Size(714, 194)
        flpVoxel.TabIndex = 0
        flpVoxel.WrapContents = False
        ' 
        ' cboKind
        ' 
        cboKind.DropDownStyle = ComboBoxStyle.DropDownList
        cboKind.Items.AddRange(New Object() {
            "standard（列扫描射线投射，已并行 + SIMD）",
            "sdf（符号距离场，带亚采样抗锯齿）",
            "physical（物理体素尺寸直接体素化）",
            "sdfraw（SDF 连续场重采样，任意目标分辨率）"})
        cboKind.Name = "cboKind"
        cboKind.Size = New Size(430, 23)
        cboKind.TabIndex = 0
        ' 
        ' nudResolution
        ' 
        nudResolution.Increment = New Decimal(New Integer() {16, 0, 0, 0})
        nudResolution.Maximum = New Decimal(New Integer() {8192, 0, 0, 0})
        nudResolution.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudResolution.Name = "nudResolution"
        nudResolution.Size = New Size(110, 23)
        nudResolution.TabIndex = 1
        nudResolution.ThousandsSeparator = True
        nudResolution.Value = New Decimal(New Integer() {48, 0, 0, 0})
        ' 
        ' nudVoxelSize
        ' 
        nudVoxelSize.DecimalPlaces = 4
        nudVoxelSize.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudVoxelSize.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        nudVoxelSize.Name = "nudVoxelSize"
        nudVoxelSize.Size = New Size(110, 23)
        nudVoxelSize.TabIndex = 2
        nudVoxelSize.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudSubSamples
        ' 
        nudSubSamples.Maximum = New Decimal(New Integer() {16, 0, 0, 0})
        nudSubSamples.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudSubSamples.Name = "nudSubSamples"
        nudSubSamples.Size = New Size(110, 23)
        nudSubSamples.TabIndex = 3
        nudSubSamples.Value = New Decimal(New Integer() {2, 0, 0, 0})
        ' 
        ' nudSdfResolution
        ' 
        nudSdfResolution.Increment = New Decimal(New Integer() {32, 0, 0, 0})
        nudSdfResolution.Maximum = New Decimal(New Integer() {8192, 0, 0, 0})
        nudSdfResolution.Name = "nudSdfResolution"
        nudSdfResolution.Size = New Size(110, 23)
        nudSdfResolution.TabIndex = 4
        nudSdfResolution.ThousandsSeparator = True
        nudSdfResolution.Value = New Decimal(New Integer() {0, 0, 0, 0})

        Call flpVoxel.Controls.Add(CliToolLayout.MakeRow("体素化器 --voxelizer", cboKind))
        Call flpVoxel.Controls.Add(CliToolLayout.MakeRow("最长边分辨率", nudResolution))
        Call flpVoxel.Controls.Add(CliToolLayout.MakeRow("物理体素尺寸", nudVoxelSize))
        Call flpVoxel.Controls.Add(CliToolLayout.MakeRow("SDF 亚采样数", nudSubSamples))
        Call flpVoxel.Controls.Add(CliToolLayout.MakeRow("SDF 基准分辨率", nudSdfResolution))
        Call flpVoxel.Controls.Add(CliToolLayout.MakeNote("    --voxel-size > 0 时忽略 --resolution；sdfraw 只接受 --resolution；--sdf-resolution 为 0 时自动取 max(resolution,128)"))

        ' 
        ' grpTool
        ' 
        grpTool.Controls.Add(flpTool)
        grpTool.Location = New Point(12, 366)
        grpTool.Margin = New Padding(0, 2, 0, 6)
        grpTool.Name = "grpTool"
        grpTool.Size = New Size(CliToolLayout.RowWidth, 76)
        grpTool.TabIndex = 2
        grpTool.TabStop = False
        grpTool.Text = "工具程序"
        ' 
        ' flpTool
        ' 
        flpTool.Dock = DockStyle.Fill
        flpTool.FlowDirection = FlowDirection.TopDown
        flpTool.Location = New Point(3, 19)
        flpTool.Name = "flpTool"
        flpTool.Padding = New Padding(6, 6, 6, 0)
        flpTool.Size = New Size(714, 54)
        flpTool.TabIndex = 0
        flpTool.WrapContents = False
        ' 
        ' txtExe
        ' 
        txtExe.Name = "txtExe"
        txtExe.Size = New Size(430, 23)
        txtExe.TabIndex = 0
        ' 
        ' btnExe
        ' 
        btnExe = CliToolLayout.MakeBrowseButton()
        btnExe.Name = "btnExe"
        btnExe.TabIndex = 1

        Call flpTool.Controls.Add(CliToolLayout.MakeRow("Voxelizer.exe", txtExe, btnExe))

        Call flpParams.Controls.Add(grpIO)
        Call flpParams.Controls.Add(grpVoxel)
        Call flpParams.Controls.Add(grpTool)

        ' 
        ' pnlCommand
        ' 
        pnlCommand.BackColor = CliToolLayout.ColorCard
        pnlCommand.Controls.Add(txtCommand)
        pnlCommand.Controls.Add(btnCopy)
        pnlCommand.Controls.Add(lblCommandTitle)
        pnlCommand.Dock = DockStyle.Fill
        pnlCommand.Location = New Point(0, 366)
        pnlCommand.Name = "pnlCommand"
        pnlCommand.Size = New Size(920, 96)
        pnlCommand.TabIndex = 2
        ' 
        ' lblCommandTitle
        ' 
        lblCommandTitle.AutoSize = True
        lblCommandTitle.Font = New Font("Segoe UI", 9.0!, FontStyle.Bold)
        lblCommandTitle.ForeColor = CliToolLayout.ColorTextDim
        lblCommandTitle.Location = New Point(12, 6)
        lblCommandTitle.Name = "lblCommandTitle"
        lblCommandTitle.Size = New Size(80, 15)
        lblCommandTitle.TabIndex = 0
        lblCommandTitle.Text = "命令行预览"
        ' 
        ' txtCommand
        ' 
        CliToolLayout.StyleCommandBox(txtCommand)
        txtCommand.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        txtCommand.Location = New Point(12, 26)
        txtCommand.Name = "txtCommand"
        txtCommand.Size = New Size(762, 60)
        txtCommand.TabIndex = 1
        ' 
        ' btnCopy
        ' 
        btnCopy = CliToolLayout.MakeButton("复制", False, 72)
        btnCopy.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        btnCopy.Location = New Point(788, 26)
        btnCopy.Name = "btnCopy"
        btnCopy.TabIndex = 2

        ' 
        ' pnlActions
        ' 
        pnlActions.BackColor = CliToolLayout.ColorBg
        pnlActions.Controls.Add(btnRun)
        pnlActions.Controls.Add(btnStop)
        pnlActions.Controls.Add(btnOpenOutput)
        pnlActions.Controls.Add(btnClose)
        pnlActions.Dock = DockStyle.Fill
        pnlActions.Location = New Point(0, 462)
        pnlActions.Name = "pnlActions"
        pnlActions.Size = New Size(920, 52)
        pnlActions.TabIndex = 3
        ' 
        ' btnRun
        ' 
        btnRun = CliToolLayout.MakeButton("运行", True, 110)
        btnRun.Location = New Point(12, 11)
        btnRun.Name = "btnRun"
        btnRun.TabIndex = 0
        ' 
        ' btnStop
        ' 
        btnStop = CliToolLayout.MakeButton("停止", False, 110)
        btnStop.Enabled = False
        btnStop.Location = New Point(132, 11)
        btnStop.Name = "btnStop"
        btnStop.TabIndex = 1
        ' 
        ' btnOpenOutput
        ' 
        btnOpenOutput = CliToolLayout.MakeButton("打开输出目录", False, 130)
        btnOpenOutput.Location = New Point(252, 11)
        btnOpenOutput.Name = "btnOpenOutput"
        btnOpenOutput.TabIndex = 2
        ' 
        ' btnClose
        ' 
        btnClose = CliToolLayout.MakeButton("关闭", False, 90)
        btnClose.Location = New Point(392, 11)
        btnClose.Name = "btnClose"
        btnClose.TabIndex = 3

        ' 
        ' pnlLog
        ' 
        pnlLog.BackColor = CliToolLayout.ColorLogBg
        pnlLog.Controls.Add(txtLog)
        pnlLog.Controls.Add(lblLogTitle)
        pnlLog.Dock = DockStyle.Fill
        pnlLog.Location = New Point(0, 514)
        pnlLog.Name = "pnlLog"
        pnlLog.Size = New Size(920, 220)
        pnlLog.TabIndex = 4
        ' 
        ' lblLogTitle
        ' 
        lblLogTitle.AutoSize = True
        lblLogTitle.BackColor = CliToolLayout.ColorLogBg
        lblLogTitle.ForeColor = CliToolLayout.ColorLogFg
        lblLogTitle.Location = New Point(8, 4)
        lblLogTitle.Name = "lblLogTitle"
        lblLogTitle.Size = New Size(60, 15)
        lblLogTitle.TabIndex = 0
        lblLogTitle.Text = "输出日志"
        ' 
        ' txtLog
        ' 
        CliToolLayout.StyleLogBox(txtLog)
        txtLog.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        txtLog.Location = New Point(0, 22)
        txtLog.Name = "txtLog"
        txtLog.Size = New Size(920, 198)
        txtLog.TabIndex = 1

        ' 
        ' pnlStatus
        ' 
        pnlStatus.BackColor = CliToolLayout.ColorAccentSoft
        pnlStatus.Controls.Add(lblStatus)
        pnlStatus.Controls.Add(lblElapsed)
        pnlStatus.Dock = DockStyle.Fill
        pnlStatus.Location = New Point(0, 734)
        pnlStatus.Name = "pnlStatus"
        pnlStatus.Size = New Size(920, 26)
        pnlStatus.TabIndex = 5
        ' 
        ' lblStatus
        ' 
        lblStatus.AutoSize = True
        lblStatus.ForeColor = CliToolLayout.ColorText
        lblStatus.Location = New Point(10, 5)
        lblStatus.Name = "lblStatus"
        lblStatus.Size = New Size(40, 15)
        lblStatus.TabIndex = 0
        lblStatus.Text = "就绪"
        ' 
        ' lblElapsed
        ' 
        lblElapsed.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        lblElapsed.AutoSize = True
        lblElapsed.ForeColor = CliToolLayout.ColorTextDim
        lblElapsed.Location = New Point(830, 5)
        lblElapsed.Name = "lblElapsed"
        lblElapsed.Size = New Size(0, 15)
        lblElapsed.TabIndex = 1
        ' 
        ' Timer1
        ' 
        Timer1.Interval = 500

        Call tblRoot.Controls.Add(pnlHeader, 0, 0)
        Call tblRoot.Controls.Add(flpParams, 0, 1)
        Call tblRoot.Controls.Add(pnlCommand, 0, 2)
        Call tblRoot.Controls.Add(pnlActions, 0, 3)
        Call tblRoot.Controls.Add(pnlLog, 0, 4)
        Call tblRoot.Controls.Add(pnlStatus, 0, 5)

        ' 
        ' FormVoxelizer
        ' 
        AutoScaleDimensions = New SizeF(7.0!, 15.0!)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(920, 760)
        Controls.Add(tblRoot)
        MinimumSize = New Size(820, 620)
        Name = "FormVoxelizer"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Voxelizer —— 三维模型转 CFD 体素模型"

        Call tblRoot.ResumeLayout(False)
        Call pnlHeader.ResumeLayout(False)
        Call flpIO.ResumeLayout(False)
        Call flpVoxel.ResumeLayout(False)
        Call flpTool.ResumeLayout(False)
        Call pnlCommand.ResumeLayout(False)
        Call pnlActions.ResumeLayout(False)
        Call pnlLog.ResumeLayout(False)
        Call pnlStatus.ResumeLayout(False)
        Call ResumeLayout(False)
        Call PerformLayout()
    End Sub

    Friend WithEvents Timer1 As Timer
    Friend WithEvents btnModel As Button
    Friend WithEvents btnOutput As Button
    Friend WithEvents btnExe As Button
    Friend WithEvents btnRun As Button
    Friend WithEvents btnStop As Button
    Friend WithEvents btnOpenOutput As Button
    Friend WithEvents btnClose As Button
    Friend WithEvents btnCopy As Button

    Friend tblRoot As TableLayoutPanel
    Friend pnlHeader As Panel
    Friend lblTitle As Label
    Friend lblSub As Label
    Friend flpParams As FlowLayoutPanel

    Friend grpIO As GroupBox
    Friend flpIO As FlowLayoutPanel
    Friend txtModel As TextBox
    Friend txtOutput As TextBox

    Friend grpVoxel As GroupBox
    Friend flpVoxel As FlowLayoutPanel
    Friend cboKind As ComboBox
    Friend nudResolution As NumericUpDown
    Friend nudVoxelSize As NumericUpDown
    Friend nudSubSamples As NumericUpDown
    Friend nudSdfResolution As NumericUpDown

    Friend grpTool As GroupBox
    Friend flpTool As FlowLayoutPanel
    Friend txtExe As TextBox

    Friend pnlCommand As Panel
    Friend lblCommandTitle As Label
    Friend txtCommand As TextBox
    Friend pnlActions As Panel
    Friend pnlLog As Panel
    Friend lblLogTitle As Label
    Friend txtLog As TextBox
    Friend pnlStatus As Panel
    Friend lblStatus As Label
    Friend lblElapsed As Label
End Class
