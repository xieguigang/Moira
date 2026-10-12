' /********************************************************************************/
'
'   FormWindTunnel.Designer.vb
'
'   WindTunnel.exe（Eulerian FVM 风洞 / 发酵罐模拟）的图形化参数窗体布局。
'
' /********************************************************************************/

Imports System.Drawing
Imports System.Windows.Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormWindTunnel
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
        cboCommand = New ComboBox()
        txtMesh = New TextBox()
        btnMesh = New Button()
        txtOutDir = New TextBox()
        btnOutDir = New Button()
        cboFormat = New ComboBox()

        grpSolve = New GroupBox()
        flpSolve = New FlowLayoutPanel()
        nudSteps = New NumericUpDown()
        nudDt = New NumericUpDown()
        nudCfl = New NumericUpDown()
        nudRelaxU = New NumericUpDown()
        nudPIter = New NumericUpDown()
        nudInterval = New NumericUpDown()

        grpPhysics = New GroupBox()
        flpPhysics = New FlowLayoutPanel()
        cboPhysics = New ComboBox()
        cboTurbulence = New ComboBox()
        cboSolver = New ComboBox()
        cboScheme = New ComboBox()
        cboFlux = New ComboBox()

        grpFlow = New GroupBox()
        flpFlow = New FlowLayoutPanel()
        nudInletU = New NumericUpDown()
        nudDirX = New NumericUpDown()
        nudDirY = New NumericUpDown()
        nudDirZ = New NumericUpDown()
        nudRho = New NumericUpDown()
        nudNu = New NumericUpDown()
        nudP0 = New NumericUpDown()
        nudT0 = New NumericUpDown()
        nudMach = New NumericUpDown()
        nudTi = New NumericUpDown()
        nudTl = New NumericUpDown()
        nudRefArea = New NumericUpDown()
        nudRefLength = New NumericUpDown()

        grpOptions = New GroupBox()
        flpOptions = New FlowLayoutPanel()
        chkGpu = New CheckBox()
        chkReport = New CheckBox()
        chkVerbose = New CheckBox()
        chkTransient = New CheckBox()
        txtProbes = New TextBox()

        grpTank = New GroupBox()
        flpTank = New FlowLayoutPanel()
        nudDiameter = New NumericUpDown()
        nudHeight = New NumericUpDown()
        nudLevel = New NumericUpDown()
        nudRpm = New NumericUpDown()
        nudRes = New NumericUpDown()
        chkCutCells = New CheckBox()
        nudDrive = New NumericUpDown()
        txtGeoDir = New TextBox()
        btnGeoDir = New Button()
        txtMeshOut = New TextBox()
        btnMeshOut = New Button()

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
        Call flpSolve.SuspendLayout()
        Call flpPhysics.SuspendLayout()
        Call flpFlow.SuspendLayout()
        Call flpOptions.SuspendLayout()
        Call flpTank.SuspendLayout()
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
        lblTitle.Size = New Size(180, 25)
        lblTitle.TabIndex = 0
        lblTitle.Text = "WindTunnel"
        ' 
        ' lblSub
        ' 
        lblSub.AutoSize = True
        lblSub.ForeColor = CliToolLayout.ColorTextDim
        lblSub.Location = New Point(18, 42)
        lblSub.Name = "lblSub"
        lblSub.Size = New Size(520, 15)
        lblSub.TabIndex = 1
        lblSub.Text = "CFD 模型文件（*.eulerian.zip）→ 风洞绕流 / 发酵罐搅拌模拟（Eulerian FVM）"

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
        grpIO.Size = New Size(CliToolLayout.RowWidth, 158)
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
        flpIO.Size = New Size(714, 136)
        flpIO.TabIndex = 0
        flpIO.WrapContents = False
        ' 
        ' cboCommand
        ' 
        cboCommand.DropDownStyle = ComboBoxStyle.DropDownList
        cboCommand.Items.AddRange(New Object() {"windtunnel（风洞绕流：F1 / 飞机 / 建筑群）", "tank（发酵罐搅拌：内流 + MRF + VOF）"})
        cboCommand.Name = "cboCommand"
        cboCommand.Size = New Size(430, 23)
        cboCommand.TabIndex = 0
        ' 
        ' txtMesh
        ' 
        txtMesh.Name = "txtMesh"
        txtMesh.Size = New Size(390, 23)
        txtMesh.TabIndex = 1
        ' 
        ' btnMesh
        ' 
        btnMesh = CliToolLayout.MakeBrowseButton()
        btnMesh.Name = "btnMesh"
        btnMesh.TabIndex = 2
        ' 
        ' txtOutDir
        ' 
        txtOutDir.Name = "txtOutDir"
        txtOutDir.Size = New Size(390, 23)
        txtOutDir.TabIndex = 3
        ' 
        ' btnOutDir
        ' 
        btnOutDir = CliToolLayout.MakeBrowseButton()
        btnOutDir.Name = "btnOutDir"
        btnOutDir.TabIndex = 4
        ' 
        ' cboFormat
        ' 
        cboFormat.DropDownStyle = ComboBoxStyle.DropDownList
        cboFormat.Items.AddRange(New Object() {"vtu（非结构原生网格，推荐）", "vti（重采样到结构笛卡尔格）", "both（同时输出两套动画序列）"})
        cboFormat.Name = "cboFormat"
        cboFormat.Size = New Size(300, 23)
        cboFormat.TabIndex = 5

        Call flpIO.Controls.Add(CliToolLayout.MakeRow("子命令", cboCommand))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("CFD 网格包 --mesh", txtMesh, btnMesh))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("输出目录 --out", txtOutDir, btnOutDir))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("输出格式 --format", cboFormat))
        Call flpIO.Controls.Add(CliToolLayout.MakeNote("    windtunnel 必须提供 --mesh（由 EulerianMesh 生成）；tank 可留空，由工具现场生成网格"))

        ' 
        ' grpSolve
        ' 
        grpSolve.Controls.Add(flpSolve)
        grpSolve.Location = New Point(12, 172)
        grpSolve.Margin = New Padding(0, 2, 0, 6)
        grpSolve.Name = "grpSolve"
        grpSolve.Size = New Size(CliToolLayout.RowWidth, 216)
        grpSolve.TabIndex = 1
        grpSolve.TabStop = False
        grpSolve.Text = "求解控制"
        ' 
        ' flpSolve
        ' 
        flpSolve.Dock = DockStyle.Fill
        flpSolve.FlowDirection = FlowDirection.TopDown
        flpSolve.Location = New Point(3, 19)
        flpSolve.Name = "flpSolve"
        flpSolve.Padding = New Padding(6, 6, 6, 0)
        flpSolve.Size = New Size(714, 194)
        flpSolve.TabIndex = 0
        flpSolve.WrapContents = False
        ' 
        ' nudSteps
        ' 
        nudSteps.Increment = New Decimal(New Integer() {100, 0, 0, 0})
        nudSteps.Maximum = New Decimal(New Integer() {10000000, 0, 0, 0})
        nudSteps.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudSteps.Name = "nudSteps"
        nudSteps.Size = New Size(120, 23)
        nudSteps.TabIndex = 0
        nudSteps.ThousandsSeparator = True
        nudSteps.Value = New Decimal(New Integer() {1500, 0, 0, 0})
        ' 
        ' nudDt
        ' 
        nudDt.DecimalPlaces = 6
        nudDt.Increment = New Decimal(New Integer() {1, 0, 0, 196608})
        nudDt.Maximum = New Decimal(New Integer() {10000, 0, 0, 0})
        nudDt.Name = "nudDt"
        nudDt.Size = New Size(120, 23)
        nudDt.TabIndex = 1
        nudDt.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudCfl
        ' 
        nudCfl.DecimalPlaces = 2
        nudCfl.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudCfl.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudCfl.Minimum = New Decimal(New Integer() {1, 0, 0, 131072})
        nudCfl.Name = "nudCfl"
        nudCfl.Size = New Size(120, 23)
        nudCfl.TabIndex = 2
        nudCfl.Value = New Decimal(New Integer() {8, 0, 0, 65536})
        ' 
        ' nudRelaxU
        ' 
        nudRelaxU.DecimalPlaces = 2
        nudRelaxU.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudRelaxU.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudRelaxU.Minimum = New Decimal(New Integer() {1, 0, 0, 65536})
        nudRelaxU.Name = "nudRelaxU"
        nudRelaxU.Size = New Size(120, 23)
        nudRelaxU.TabIndex = 3
        nudRelaxU.Value = New Decimal(New Integer() {7, 0, 0, 65536})
        ' 
        ' nudPIter
        ' 
        nudPIter.Maximum = New Decimal(New Integer() {10000, 0, 0, 0})
        nudPIter.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudPIter.Name = "nudPIter"
        nudPIter.Size = New Size(120, 23)
        nudPIter.TabIndex = 4
        nudPIter.Value = New Decimal(New Integer() {200, 0, 0, 0})
        ' 
        ' nudInterval
        ' 
        nudInterval.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        nudInterval.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudInterval.Name = "nudInterval"
        nudInterval.Size = New Size(120, 23)
        nudInterval.TabIndex = 5
        nudInterval.Value = New Decimal(New Integer() {10, 0, 0, 0})

        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("总步数 --steps", nudSteps))
        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("时间步长 --dt", nudDt))
        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("CFL 数 --cfl", nudCfl))
        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("速度欠松弛 --relax-u", nudRelaxU))
        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("压力迭代 --p-iter", nudPIter))
        Call flpSolve.Controls.Add(CliToolLayout.MakeRow("快照间隔 --interval", nudInterval))
        Call flpSolve.Controls.Add(CliToolLayout.MakeNote("    --dt ≤ 0 时由工具按 CFL / 叶尖速度自动确定"))

        ' 
        ' grpPhysics
        ' 
        grpPhysics.Controls.Add(flpPhysics)
        grpPhysics.Location = New Point(12, 394)
        grpPhysics.Margin = New Padding(0, 2, 0, 6)
        grpPhysics.Name = "grpPhysics"
        grpPhysics.Size = New Size(CliToolLayout.RowWidth, 190)
        grpPhysics.TabIndex = 2
        grpPhysics.TabStop = False
        grpPhysics.Text = "物理与离散格式"
        ' 
        ' flpPhysics
        ' 
        flpPhysics.Dock = DockStyle.Fill
        flpPhysics.FlowDirection = FlowDirection.TopDown
        flpPhysics.Location = New Point(3, 19)
        flpPhysics.Name = "flpPhysics"
        flpPhysics.Padding = New Padding(6, 6, 6, 0)
        flpPhysics.Size = New Size(714, 168)
        flpPhysics.TabIndex = 0
        flpPhysics.WrapContents = False
        ' 
        ' cboPhysics
        ' 
        cboPhysics.DropDownStyle = ComboBoxStyle.DropDownList
        cboPhysics.Items.AddRange(New Object() {"incompressible", "compressible"})
        cboPhysics.Name = "cboPhysics"
        cboPhysics.Size = New Size(180, 23)
        cboPhysics.TabIndex = 0
        ' 
        ' cboTurbulence
        ' 
        cboTurbulence.DropDownStyle = ComboBoxStyle.DropDownList
        cboTurbulence.Items.AddRange(New Object() {"ke（k-ε）", "sst（k-ω SST）", "none（层流）"})
        cboTurbulence.Name = "cboTurbulence"
        cboTurbulence.Size = New Size(180, 23)
        cboTurbulence.TabIndex = 1
        ' 
        ' cboSolver
        ' 
        cboSolver.DropDownStyle = ComboBoxStyle.DropDownList
        cboSolver.Items.AddRange(New Object() {"simple", "pimple"})
        cboSolver.Name = "cboSolver"
        cboSolver.Size = New Size(180, 23)
        cboSolver.TabIndex = 2
        ' 
        ' cboScheme
        ' 
        cboScheme.DropDownStyle = ComboBoxStyle.DropDownList
        cboScheme.Items.AddRange(New Object() {"upwind", "central", "linear-upwind", "quick"})
        cboScheme.Name = "cboScheme"
        cboScheme.Size = New Size(180, 23)
        cboScheme.TabIndex = 3
        ' 
        ' cboFlux
        ' 
        cboFlux.DropDownStyle = ComboBoxStyle.DropDownList
        cboFlux.Items.AddRange(New Object() {"AUSM+", "HLLC"})
        cboFlux.Name = "cboFlux"
        cboFlux.Size = New Size(180, 23)
        cboFlux.TabIndex = 4

        Call flpPhysics.Controls.Add(CliToolLayout.MakeRow("物理模型 --physics", cboPhysics))
        Call flpPhysics.Controls.Add(CliToolLayout.MakeRow("湍流模型 --turbulence", cboTurbulence))
        Call flpPhysics.Controls.Add(CliToolLayout.MakeRow("求解器 --solver", cboSolver))
        Call flpPhysics.Controls.Add(CliToolLayout.MakeRow("对流格式 --scheme", cboScheme))
        Call flpPhysics.Controls.Add(CliToolLayout.MakeRow("通量格式 --flux", cboFlux))

        ' 
        ' grpFlow
        ' 
        grpFlow.Controls.Add(flpFlow)
        grpFlow.Location = New Point(12, 590)
        grpFlow.Margin = New Padding(0, 2, 0, 6)
        grpFlow.Name = "grpFlow"
        grpFlow.Size = New Size(CliToolLayout.RowWidth, 372)
        grpFlow.TabIndex = 3
        grpFlow.TabStop = False
        grpFlow.Text = "来流与物性（--inlet-u / --rho / --nu ...）"
        ' 
        ' flpFlow
        ' 
        flpFlow.Dock = DockStyle.Fill
        flpFlow.FlowDirection = FlowDirection.TopDown
        flpFlow.Location = New Point(3, 19)
        flpFlow.Name = "flpFlow"
        flpFlow.Padding = New Padding(6, 6, 6, 0)
        flpFlow.Size = New Size(714, 350)
        flpFlow.TabIndex = 0
        flpFlow.WrapContents = False
        ' 
        ' nudInletU
        ' 
        nudInletU.DecimalPlaces = 2
        nudInletU.Increment = New Decimal(New Integer() {5, 0, 0, 0})
        nudInletU.Maximum = New Decimal(New Integer() {10000, 0, 0, 0})
        nudInletU.Name = "nudInletU"
        nudInletU.Size = New Size(120, 23)
        nudInletU.TabIndex = 0
        nudInletU.Value = New Decimal(New Integer() {30, 0, 0, 0})
        ' 
        ' nudDirX / Y / Z
        ' 
        nudDirX.DecimalPlaces = 3
        nudDirX.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudDirX.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDirX.Minimum = New Decimal(-1)
        nudDirX.Name = "nudDirX"
        nudDirX.Size = New Size(80, 23)
        nudDirX.TabIndex = 1
        nudDirX.Value = New Decimal(New Integer() {1, 0, 0, 0})

        nudDirY.DecimalPlaces = 3
        nudDirY.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudDirY.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDirY.Minimum = New Decimal(-1)
        nudDirY.Name = "nudDirY"
        nudDirY.Size = New Size(80, 23)
        nudDirY.TabIndex = 2
        nudDirY.Value = New Decimal(New Integer() {0, 0, 0, 0})

        nudDirZ.DecimalPlaces = 3
        nudDirZ.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudDirZ.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDirZ.Minimum = New Decimal(-1)
        nudDirZ.Name = "nudDirZ"
        nudDirZ.Size = New Size(80, 23)
        nudDirZ.TabIndex = 3
        nudDirZ.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudRho
        ' 
        nudRho.DecimalPlaces = 4
        nudRho.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudRho.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        nudRho.Name = "nudRho"
        nudRho.Size = New Size(120, 23)
        nudRho.TabIndex = 4
        nudRho.Value = New Decimal(New Integer() {1225, 0, 0, 196608})
        ' 
        ' nudNu
        ' 
        nudNu.DecimalPlaces = 8
        nudNu.Increment = New Decimal(New Integer() {1, 0, 0, 393216})
        nudNu.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudNu.Name = "nudNu"
        nudNu.Size = New Size(120, 23)
        nudNu.TabIndex = 5
        nudNu.Value = New Decimal(New Integer() {15, 0, 0, 393216})
        ' 
        ' nudP0
        ' 
        nudP0.DecimalPlaces = 2
        nudP0.Increment = New Decimal(New Integer() {1000, 0, 0, 0})
        nudP0.Maximum = New Decimal(New Integer() {10000000, 0, 0, 0})
        nudP0.Name = "nudP0"
        nudP0.Size = New Size(120, 23)
        nudP0.TabIndex = 6
        nudP0.ThousandsSeparator = True
        nudP0.Value = New Decimal(New Integer() {101325, 0, 0, 0})
        ' 
        ' nudT0
        ' 
        nudT0.DecimalPlaces = 2
        nudT0.Maximum = New Decimal(New Integer() {10000, 0, 0, 0})
        nudT0.Name = "nudT0"
        nudT0.Size = New Size(120, 23)
        nudT0.TabIndex = 7
        nudT0.Value = New Decimal(New Integer() {28815, 0, 0, 131072})
        ' 
        ' nudMach
        ' 
        nudMach.DecimalPlaces = 3
        nudMach.Increment = New Decimal(New Integer() {1, 0, 0, 131072})
        nudMach.Maximum = New Decimal(New Integer() {20, 0, 0, 0})
        nudMach.Name = "nudMach"
        nudMach.Size = New Size(120, 23)
        nudMach.TabIndex = 8
        nudMach.Value = New Decimal(New Integer() {1, 0, 0, 65536})
        ' 
        ' nudTi
        ' 
        nudTi.DecimalPlaces = 4
        nudTi.Increment = New Decimal(New Integer() {1, 0, 0, 131072})
        nudTi.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudTi.Name = "nudTi"
        nudTi.Size = New Size(120, 23)
        nudTi.TabIndex = 9
        nudTi.Value = New Decimal(New Integer() {1, 0, 0, 131072})
        ' 
        ' nudTl
        ' 
        nudTl.DecimalPlaces = 4
        nudTl.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudTl.Maximum = New Decimal(New Integer() {10000, 0, 0, 0})
        nudTl.Name = "nudTl"
        nudTl.Size = New Size(120, 23)
        nudTl.TabIndex = 10
        nudTl.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudRefArea
        ' 
        nudRefArea.DecimalPlaces = 4
        nudRefArea.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudRefArea.Maximum = New Decimal(New Integer() {1000000, 0, 0, 0})
        nudRefArea.Name = "nudRefArea"
        nudRefArea.Size = New Size(120, 23)
        nudRefArea.TabIndex = 11
        nudRefArea.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudRefLength
        ' 
        nudRefLength.DecimalPlaces = 4
        nudRefLength.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudRefLength.Maximum = New Decimal(New Integer() {1000000, 0, 0, 0})
        nudRefLength.Name = "nudRefLength"
        nudRefLength.Size = New Size(120, 23)
        nudRefLength.TabIndex = 12
        nudRefLength.Value = New Decimal(New Integer() {0, 0, 0, 0})

        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("来流速度 --inlet-u", nudInletU))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("来流方向 --inlet-dir", nudDirX, nudDirY, nudDirZ))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("密度 --rho", nudRho))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("运动粘度 --nu", nudNu))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("参考压力 --p0", nudP0))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("总温 --t0", nudT0))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("马赫数 --mach", nudMach))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("湍流强度 --ti", nudTi))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("湍流长度尺度 --tl", nudTl))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("参考面积 --ref-area", nudRefArea))
        Call flpFlow.Controls.Add(CliToolLayout.MakeRow("参考长度 --ref-length", nudRefLength))
        Call flpFlow.Controls.Add(CliToolLayout.MakeNote("    --tl / --ref-area / --ref-length 为 0 时由工具按网格尺度自动确定"))

        ' 
        ' grpOptions
        ' 
        grpOptions.Controls.Add(flpOptions)
        grpOptions.Location = New Point(12, 968)
        grpOptions.Margin = New Padding(0, 2, 0, 6)
        grpOptions.Name = "grpOptions"
        grpOptions.Size = New Size(CliToolLayout.RowWidth, 232)
        grpOptions.TabIndex = 4
        grpOptions.TabStop = False
        grpOptions.Text = "开关与探针"
        ' 
        ' flpOptions
        ' 
        flpOptions.Dock = DockStyle.Fill
        flpOptions.FlowDirection = FlowDirection.TopDown
        flpOptions.Location = New Point(3, 19)
        flpOptions.Name = "flpOptions"
        flpOptions.Padding = New Padding(6, 6, 6, 0)
        flpOptions.Size = New Size(714, 210)
        flpOptions.TabIndex = 0
        flpOptions.WrapContents = False
        ' 
        ' chkGpu
        ' 
        chkGpu.AutoSize = True
        chkGpu.Checked = True
        chkGpu.CheckState = CheckState.Checked
        chkGpu.Name = "chkGpu"
        chkGpu.Size = New Size(420, 19)
        chkGpu.TabIndex = 0
        chkGpu.Text = "启用 CUDA float32 后端（--gpu，失败自动回落 CPU）"
        ' 
        ' chkReport
        ' 
        chkReport.AutoSize = True
        chkReport.Checked = True
        chkReport.CheckState = CheckState.Checked
        chkReport.Name = "chkReport"
        chkReport.Size = New Size(420, 19)
        chkReport.TabIndex = 1
        chkReport.Text = "输出气动力 / 搅拌功率报告（关闭 = --no-report）"
        ' 
        ' chkVerbose
        ' 
        chkVerbose.AutoSize = True
        chkVerbose.Name = "chkVerbose"
        chkVerbose.Size = New Size(420, 19)
        chkVerbose.TabIndex = 2
        chkVerbose.Text = "逐方程残差日志（--verbose）"
        ' 
        ' chkTransient
        ' 
        chkTransient.AutoSize = True
        chkTransient.Checked = True
        chkTransient.CheckState = CheckState.Checked
        chkTransient.Name = "chkTransient"
        chkTransient.Size = New Size(420, 19)
        chkTransient.TabIndex = 3
        chkTransient.Text = "瞬态时间推进（关闭 = --steady 稳态伪瞬态）"
        ' 
        ' txtProbes
        ' 
        txtProbes.AcceptsReturn = True
        txtProbes.Multiline = True
        txtProbes.Name = "txtProbes"
        txtProbes.ScrollBars = ScrollBars.Vertical
        txtProbes.Size = New Size(430, 62)
        txtProbes.TabIndex = 4
        txtProbes.WordWrap = False

        Call flpOptions.Controls.Add(CliToolLayout.MakeRow("加速", chkGpu))
        Call flpOptions.Controls.Add(CliToolLayout.MakeRow("报告", chkReport))
        Call flpOptions.Controls.Add(CliToolLayout.MakeRow("日志", chkVerbose))
        Call flpOptions.Controls.Add(CliToolLayout.MakeRow("时间推进", chkTransient))
        Call flpOptions.Controls.Add(CliToolLayout.MakeRow("探针点 --probe", txtProbes))
        Call flpOptions.Controls.Add(CliToolLayout.MakeNote("    每行一个探针，格式 x,y,z（可留空）"))

        ' 
        ' grpTank
        ' 
        grpTank.Controls.Add(flpTank)
        grpTank.Location = New Point(12, 1206)
        grpTank.Margin = New Padding(0, 2, 0, 6)
        grpTank.Name = "grpTank"
        grpTank.Size = New Size(CliToolLayout.RowWidth, 310)
        grpTank.TabIndex = 5
        grpTank.TabStop = False
        grpTank.Text = "发酵罐参数（仅 tank 子命令）"
        ' 
        ' flpTank
        ' 
        flpTank.Dock = DockStyle.Fill
        flpTank.FlowDirection = FlowDirection.TopDown
        flpTank.Location = New Point(3, 19)
        flpTank.Name = "flpTank"
        flpTank.Padding = New Padding(6, 6, 6, 0)
        flpTank.Size = New Size(714, 288)
        flpTank.TabIndex = 0
        flpTank.WrapContents = False
        ' 
        ' nudDiameter
        ' 
        nudDiameter.DecimalPlaces = 3
        nudDiameter.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudDiameter.Maximum = New Decimal(New Integer() {1000, 0, 0, 0})
        nudDiameter.Name = "nudDiameter"
        nudDiameter.Size = New Size(120, 23)
        nudDiameter.TabIndex = 0
        nudDiameter.Value = New Decimal(New Integer() {3, 0, 0, 65536})
        ' 
        ' nudHeight
        ' 
        nudHeight.DecimalPlaces = 3
        nudHeight.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudHeight.Maximum = New Decimal(New Integer() {1000, 0, 0, 0})
        nudHeight.Name = "nudHeight"
        nudHeight.Size = New Size(120, 23)
        nudHeight.TabIndex = 1
        nudHeight.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudLevel
        ' 
        nudLevel.DecimalPlaces = 3
        nudLevel.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudLevel.Maximum = New Decimal(New Integer() {1000, 0, 0, 0})
        nudLevel.Name = "nudLevel"
        nudLevel.Size = New Size(120, 23)
        nudLevel.TabIndex = 2
        nudLevel.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' nudRpm
        ' 
        nudRpm.DecimalPlaces = 1
        nudRpm.Increment = New Decimal(New Integer() {10, 0, 0, 0})
        nudRpm.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        nudRpm.Name = "nudRpm"
        nudRpm.Size = New Size(120, 23)
        nudRpm.TabIndex = 3
        nudRpm.Value = New Decimal(New Integer() {300, 0, 0, 0})
        ' 
        ' nudRes
        ' 
        nudRes.DecimalPlaces = 4
        nudRes.Increment = New Decimal(New Integer() {5, 0, 0, 196608})
        nudRes.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudRes.Minimum = New Decimal(New Integer() {1, 0, 0, 262144})
        nudRes.Name = "nudRes"
        nudRes.Size = New Size(120, 23)
        nudRes.TabIndex = 4
        nudRes.Value = New Decimal(New Integer() {4, 0, 0, 131072})
        ' 
        ' chkCutCells
        ' 
        chkCutCells.AutoSize = True
        chkCutCells.Checked = True
        chkCutCells.CheckState = CheckState.Checked
        chkCutCells.Name = "chkCutCells"
        chkCutCells.Size = New Size(420, 19)
        chkCutCells.TabIndex = 5
        chkCutCells.Text = "启用切割单元（--cut-cells）"
        ' 
        ' nudDrive
        ' 
        nudDrive.DecimalPlaces = 2
        nudDrive.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudDrive.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        nudDrive.Name = "nudDrive"
        nudDrive.Size = New Size(120, 23)
        nudDrive.TabIndex = 6
        nudDrive.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' txtGeoDir
        ' 
        txtGeoDir.Name = "txtGeoDir"
        txtGeoDir.Size = New Size(390, 23)
        txtGeoDir.TabIndex = 7
        ' 
        ' btnGeoDir
        ' 
        btnGeoDir = CliToolLayout.MakeBrowseButton()
        btnGeoDir.Name = "btnGeoDir"
        btnGeoDir.TabIndex = 8
        ' 
        ' txtMeshOut
        ' 
        txtMeshOut.Name = "txtMeshOut"
        txtMeshOut.Size = New Size(390, 23)
        txtMeshOut.TabIndex = 9
        ' 
        ' btnMeshOut
        ' 
        btnMeshOut = CliToolLayout.MakeBrowseButton()
        btnMeshOut.Name = "btnMeshOut"
        btnMeshOut.TabIndex = 10

        Call flpTank.Controls.Add(CliToolLayout.MakeRow("罐内径 -d", nudDiameter))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("罐高 -h", nudHeight))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("液面高度 --liquid-level", nudLevel))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("转速 --rpm", nudRpm))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("网格分辨率 -r", nudRes))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("切割单元", chkCutCells))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("桨盘驱动强度 --drive", nudDrive))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("几何输出目录", txtGeoDir, btnGeoDir))
        Call flpTank.Controls.Add(CliToolLayout.MakeRow("网格另存 --mesh-out", txtMeshOut, btnMeshOut))
        Call flpTank.Controls.Add(CliToolLayout.MakeNote("    罐高 ≤ 0 取 1.5·D；液面 ≤ 0 取 2/3·H；驱动强度 0 = 仅桨叶壁面驱动"))

        ' 
        ' grpTool
        ' 
        grpTool.Controls.Add(flpTool)
        grpTool.Location = New Point(12, 1522)
        grpTool.Margin = New Padding(0, 2, 0, 6)
        grpTool.Name = "grpTool"
        grpTool.Size = New Size(CliToolLayout.RowWidth, 76)
        grpTool.TabIndex = 6
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

        Call flpTool.Controls.Add(CliToolLayout.MakeRow("WindTunnel.exe", txtExe, btnExe))

        Call flpParams.Controls.Add(grpIO)
        Call flpParams.Controls.Add(grpSolve)
        Call flpParams.Controls.Add(grpPhysics)
        Call flpParams.Controls.Add(grpFlow)
        Call flpParams.Controls.Add(grpOptions)
        Call flpParams.Controls.Add(grpTank)
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
        ' FormWindTunnel
        ' 
        AutoScaleDimensions = New SizeF(7.0!, 15.0!)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(920, 760)
        Controls.Add(tblRoot)
        MinimumSize = New Size(820, 620)
        Name = "FormWindTunnel"
        StartPosition = FormStartPosition.CenterScreen
        Text = "WindTunnel —— CFD 风洞 / 发酵罐模拟"

        Call tblRoot.ResumeLayout(False)
        Call pnlHeader.ResumeLayout(False)
        Call flpIO.ResumeLayout(False)
        Call flpSolve.ResumeLayout(False)
        Call flpPhysics.ResumeLayout(False)
        Call flpFlow.ResumeLayout(False)
        Call flpOptions.ResumeLayout(False)
        Call flpTank.ResumeLayout(False)
        Call flpTool.ResumeLayout(False)
        Call pnlCommand.ResumeLayout(False)
        Call pnlActions.ResumeLayout(False)
        Call pnlLog.ResumeLayout(False)
        Call pnlStatus.ResumeLayout(False)
        Call ResumeLayout(False)
        Call PerformLayout()
    End Sub

    Friend WithEvents Timer1 As Timer
    Friend WithEvents btnMesh As Button
    Friend WithEvents btnOutDir As Button
    Friend WithEvents btnGeoDir As Button
    Friend WithEvents btnMeshOut As Button
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
    Friend cboCommand As ComboBox
    Friend txtMesh As TextBox
    Friend txtOutDir As TextBox
    Friend cboFormat As ComboBox

    Friend grpSolve As GroupBox
    Friend flpSolve As FlowLayoutPanel
    Friend nudSteps As NumericUpDown
    Friend nudDt As NumericUpDown
    Friend nudCfl As NumericUpDown
    Friend nudRelaxU As NumericUpDown
    Friend nudPIter As NumericUpDown
    Friend nudInterval As NumericUpDown

    Friend grpPhysics As GroupBox
    Friend flpPhysics As FlowLayoutPanel
    Friend cboPhysics As ComboBox
    Friend cboTurbulence As ComboBox
    Friend cboSolver As ComboBox
    Friend cboScheme As ComboBox
    Friend cboFlux As ComboBox

    Friend grpFlow As GroupBox
    Friend flpFlow As FlowLayoutPanel
    Friend nudInletU As NumericUpDown
    Friend nudDirX As NumericUpDown
    Friend nudDirY As NumericUpDown
    Friend nudDirZ As NumericUpDown
    Friend nudRho As NumericUpDown
    Friend nudNu As NumericUpDown
    Friend nudP0 As NumericUpDown
    Friend nudT0 As NumericUpDown
    Friend nudMach As NumericUpDown
    Friend nudTi As NumericUpDown
    Friend nudTl As NumericUpDown
    Friend nudRefArea As NumericUpDown
    Friend nudRefLength As NumericUpDown

    Friend grpOptions As GroupBox
    Friend flpOptions As FlowLayoutPanel
    Friend chkGpu As CheckBox
    Friend chkReport As CheckBox
    Friend chkVerbose As CheckBox
    Friend chkTransient As CheckBox
    Friend txtProbes As TextBox

    Friend grpTank As GroupBox
    Friend flpTank As FlowLayoutPanel
    Friend nudDiameter As NumericUpDown
    Friend nudHeight As NumericUpDown
    Friend nudLevel As NumericUpDown
    Friend nudRpm As NumericUpDown
    Friend nudRes As NumericUpDown
    Friend chkCutCells As CheckBox
    Friend nudDrive As NumericUpDown
    Friend txtGeoDir As TextBox
    Friend txtMeshOut As TextBox

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
