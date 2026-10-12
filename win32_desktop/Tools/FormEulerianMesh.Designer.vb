' /********************************************************************************/
'
'   FormEulerianMesh.Designer.vb
'
'   EulerianMesh.exe（三维模型 → CFD Eulerian 网格包）的图形化参数窗体布局。
'
'   骨架与另外两个工具窗体完全一致：
'       标题区 / 参数区 / 命令行预览 / 运行控制 / 日志区 / 状态栏
'
' /********************************************************************************/

Imports System.Drawing
Imports System.Windows.Forms

<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormEulerianMesh
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
        txtInput = New TextBox()
        btnInput = New Button()
        txtOutput = New TextBox()
        btnOutput = New Button()

        grpMaxres = New GroupBox()
        flpMaxres = New FlowLayoutPanel()
        nudCells = New NumericUpDown()
        nudDepth = New NumericUpDown()
        nudDomainScale = New NumericUpDown()
        nudVoxelRatio = New NumericUpDown()
        nudMaxVoxels = New NumericUpDown()
        chkPlan = New CheckBox()

        grpConvert = New GroupBox()
        flpConvert = New FlowLayoutPanel()
        cboType = New ComboBox()
        nudResolution = New NumericUpDown()
        nudDomainScaleC = New NumericUpDown()
        nudPoisson = New NumericUpDown()
        chkCutCells = New CheckBox()
        chkNoSmooth = New CheckBox()

        grpCommon = New GroupBox()
        flpCommon = New FlowLayoutPanel()
        cboMode = New ComboBox()
        txtSolids = New TextBox()
        chkGpu = New CheckBox()

        grpPatch = New GroupBox()
        flpPatch = New FlowLayoutPanel()
        txtPatchName = New TextBox()
        cboPatchType = New ComboBox()
        cboPatchFace = New ComboBox()
        btnPatchAdd = New Button()
        lstPatches = New ListBox()
        btnPatchRemove = New Button()

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
        Call flpMaxres.SuspendLayout()
        Call flpConvert.SuspendLayout()
        Call flpCommon.SuspendLayout()
        Call flpPatch.SuspendLayout()
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
        lblTitle.Size = New Size(220, 25)
        lblTitle.TabIndex = 0
        lblTitle.Text = "Eulerian Mesh"
        ' 
        ' lblSub
        ' 
        lblSub.AutoSize = True
        lblSub.ForeColor = CliToolLayout.ColorTextDim
        lblSub.Location = New Point(18, 42)
        lblSub.Name = "lblSub"
        lblSub.Size = New Size(420, 15)
        lblSub.TabIndex = 1
        lblSub.Text = "三维模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）→ CFD Eulerian 网格包"

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
        grpIO.Size = New Size(CliToolLayout.RowWidth, 130)
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
        flpIO.Size = New Size(714, 108)
        flpIO.TabIndex = 0
        flpIO.WrapContents = False
        ' 
        ' cboCommand
        ' 
        cboCommand.DropDownStyle = ComboBoxStyle.DropDownList
        cboCommand.Items.AddRange(New Object() {"maxres（原始几何精度 + 最高可行分辨率）", "convert（自定义网格算法与尺度）"})
        cboCommand.Name = "cboCommand"
        cboCommand.Size = New Size(430, 23)
        cboCommand.TabIndex = 0
        ' 
        ' txtInput
        ' 
        txtInput.Name = "txtInput"
        txtInput.Size = New Size(390, 23)
        txtInput.TabIndex = 1
        ' 
        ' btnInput
        ' 
        btnInput = CliToolLayout.MakeBrowseButton()
        btnInput.Name = "btnInput"
        btnInput.TabIndex = 2
        ' 
        ' txtOutput
        ' 
        txtOutput.Name = "txtOutput"
        txtOutput.Size = New Size(390, 23)
        txtOutput.TabIndex = 3
        ' 
        ' btnOutput
        ' 
        btnOutput = CliToolLayout.MakeBrowseButton()
        btnOutput.Name = "btnOutput"
        btnOutput.TabIndex = 4

        Call flpIO.Controls.Add(CliToolLayout.MakeRow("子命令", cboCommand))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("输入模型", txtInput, btnInput))
        Call flpIO.Controls.Add(CliToolLayout.MakeRow("输出网格包", txtOutput, btnOutput))
        Call flpIO.Controls.Add(CliToolLayout.MakeNote("输出留空时由工具自动生成：<输入>.maxres.eulerian.zip / <输入>.eulerian.zip"))

        ' 
        ' grpMaxres
        ' 
        grpMaxres.Controls.Add(flpMaxres)
        grpMaxres.Location = New Point(12, 146)
        grpMaxres.Margin = New Padding(0, 2, 0, 6)
        grpMaxres.Name = "grpMaxres"
        grpMaxres.Size = New Size(CliToolLayout.RowWidth, 220)
        grpMaxres.TabIndex = 1
        grpMaxres.TabStop = False
        grpMaxres.Text = "maxres —— 一键最高精度"
        ' 
        ' flpMaxres
        ' 
        flpMaxres.Dock = DockStyle.Fill
        flpMaxres.FlowDirection = FlowDirection.TopDown
        flpMaxres.Location = New Point(3, 19)
        flpMaxres.Name = "flpMaxres"
        flpMaxres.Padding = New Padding(6, 6, 6, 0)
        flpMaxres.Size = New Size(714, 198)
        flpMaxres.TabIndex = 0
        flpMaxres.WrapContents = False
        ' 
        ' nudCells
        ' 
        nudCells.Increment = New Decimal(New Integer() {1000000, 0, 0, 0})
        nudCells.Maximum = New Decimal(New Integer() {1000000000, 0, 0, 0})
        nudCells.Minimum = New Decimal(New Integer() {8, 0, 0, 0})
        nudCells.Name = "nudCells"
        nudCells.Size = New Size(140, 23)
        nudCells.TabIndex = 0
        nudCells.ThousandsSeparator = True
        nudCells.Value = New Decimal(New Integer() {2500000, 0, 0, 0})
        ' 
        ' nudDepth
        ' 
        nudDepth.Maximum = New Decimal(New Integer() {18, 0, 0, 0})
        nudDepth.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDepth.Name = "nudDepth"
        nudDepth.Size = New Size(90, 23)
        nudDepth.TabIndex = 1
        nudDepth.Value = New Decimal(New Integer() {18, 0, 0, 0})
        ' 
        ' nudDomainScale
        ' 
        nudDomainScale.DecimalPlaces = 2
        nudDomainScale.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudDomainScale.Maximum = New Decimal(New Integer() {20, 0, 0, 0})
        nudDomainScale.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDomainScale.Name = "nudDomainScale"
        nudDomainScale.Size = New Size(90, 23)
        nudDomainScale.TabIndex = 2
        nudDomainScale.Value = New Decimal(New Integer() {2, 0, 0, 0})
        ' 
        ' nudVoxelRatio
        ' 
        nudVoxelRatio.DecimalPlaces = 3
        nudVoxelRatio.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        nudVoxelRatio.Maximum = New Decimal(New Integer() {4, 0, 0, 0})
        nudVoxelRatio.Minimum = New Decimal(New Integer() {1, 0, 0, 131072})
        nudVoxelRatio.Name = "nudVoxelRatio"
        nudVoxelRatio.Size = New Size(90, 23)
        nudVoxelRatio.TabIndex = 3
        nudVoxelRatio.Value = New Decimal(New Integer() {5, 0, 0, 65536})
        ' 
        ' nudMaxVoxels
        ' 
        nudMaxVoxels.Increment = New Decimal(New Integer() {1000000, 0, 0, 0})
        nudMaxVoxels.Maximum = New Decimal(New Integer() {1000000000, 0, 0, 0})
        nudMaxVoxels.Minimum = New Decimal(New Integer() {1024, 0, 0, 0})
        nudMaxVoxels.Name = "nudMaxVoxels"
        nudMaxVoxels.Size = New Size(150, 23)
        nudMaxVoxels.TabIndex = 4
        nudMaxVoxels.ThousandsSeparator = True
        nudMaxVoxels.Value = New Decimal(New Integer() {134217728, 0, 0, 0})
        ' 
        ' chkPlan
        ' 
        chkPlan.AutoSize = True
        chkPlan.Name = "chkPlan"
        chkPlan.Size = New Size(300, 19)
        chkPlan.TabIndex = 5
        chkPlan.Text = "仅输出尺度方案（--plan，不生成网格）"

        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("叶子预算 --cells", nudCells))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("深度上限 --max-depth", nudDepth))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("计算域放大 --domain-scale", nudDomainScale))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("分类场体素比例 --voxel-ratio", nudVoxelRatio))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("分类场体素上限 --max-voxels", nudMaxVoxels))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeRow("干跑规划", chkPlan))
        Call flpMaxres.Controls.Add(CliToolLayout.MakeNote("预算每跨一个 8 倍台阶，八叉树深度 +1（8^d ≤ cells）；体素比例越小分类越准、内存越多"))

        ' 
        ' grpConvert
        ' 
        grpConvert.Controls.Add(flpConvert)
        grpConvert.Location = New Point(12, 372)
        grpConvert.Margin = New Padding(0, 2, 0, 6)
        grpConvert.Name = "grpConvert"
        grpConvert.Size = New Size(CliToolLayout.RowWidth, 220)
        grpConvert.TabIndex = 2
        grpConvert.TabStop = False
        grpConvert.Text = "convert —— 自定义网格算法与尺度"
        ' 
        ' flpConvert
        ' 
        flpConvert.Dock = DockStyle.Fill
        flpConvert.FlowDirection = FlowDirection.TopDown
        flpConvert.Location = New Point(3, 19)
        flpConvert.Name = "flpConvert"
        flpConvert.Padding = New Padding(6, 6, 6, 0)
        flpConvert.Size = New Size(714, 198)
        flpConvert.TabIndex = 0
        flpConvert.WrapContents = False
        ' 
        ' cboType
        ' 
        cboType.DropDownStyle = ComboBoxStyle.DropDownList
        cboType.Items.AddRange(New Object() {"octree", "delaunay", "structured", "boundarylayer"})
        cboType.Name = "cboType"
        cboType.Size = New Size(160, 23)
        cboType.TabIndex = 0
        ' 
        ' nudResolution
        ' 
        nudResolution.DecimalPlaces = 4
        nudResolution.Increment = New Decimal(New Integer() {5, 0, 0, 196608})
        nudResolution.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        nudResolution.Minimum = New Decimal(New Integer() {1, 0, 0, 196608})
        nudResolution.Name = "nudResolution"
        nudResolution.Size = New Size(90, 23)
        nudResolution.TabIndex = 1
        nudResolution.Value = New Decimal(New Integer() {5, 0, 0, 131072})
        ' 
        ' nudDomainScaleC
        ' 
        nudDomainScaleC.DecimalPlaces = 2
        nudDomainScaleC.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        nudDomainScaleC.Maximum = New Decimal(New Integer() {20, 0, 0, 0})
        nudDomainScaleC.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        nudDomainScaleC.Name = "nudDomainScaleC"
        nudDomainScaleC.Size = New Size(90, 23)
        nudDomainScaleC.TabIndex = 2
        nudDomainScaleC.Value = New Decimal(New Integer() {2, 0, 0, 0})
        ' 
        ' nudPoisson
        ' 
        nudPoisson.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        nudPoisson.Name = "nudPoisson"
        nudPoisson.Size = New Size(90, 23)
        nudPoisson.TabIndex = 3
        nudPoisson.Value = New Decimal(New Integer() {0, 0, 0, 0})
        ' 
        ' chkCutCells
        ' 
        chkCutCells.AutoSize = True
        chkCutCells.Name = "chkCutCells"
        chkCutCells.Size = New Size(300, 19)
        chkCutCells.TabIndex = 4
        chkCutCells.Text = "启用切割单元（--cut-cells）"
        ' 
        ' chkNoSmooth
        ' 
        chkNoSmooth.AutoSize = True
        chkNoSmooth.Name = "chkNoSmooth"
        chkNoSmooth.Size = New Size(300, 19)
        chkNoSmooth.TabIndex = 5
        chkNoSmooth.Text = "关闭 Laplacian 平滑（--no-smooth）"

        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("网格算法 --type", cboType))
        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("网格尺度 --resolution", nudResolution))
        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("计算域放大 --domain-scale", nudDomainScaleC))
        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("Poisson 平滑轮数", nudPoisson))
        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("切割单元", chkCutCells))
        Call flpConvert.Controls.Add(CliToolLayout.MakeRow("平滑", chkNoSmooth))
        Call flpConvert.Controls.Add(CliToolLayout.MakeNote("--resolution 为相对模型对角线长度的比例：0.05 ≈ 20 格跨模型"))

        ' 
        ' grpCommon
        ' 
        grpCommon.Controls.Add(flpCommon)
        grpCommon.Location = New Point(12, 598)
        grpCommon.Margin = New Padding(0, 2, 0, 6)
        grpCommon.Name = "grpCommon"
        grpCommon.Size = New Size(CliToolLayout.RowWidth, 180)
        grpCommon.TabIndex = 3
        grpCommon.TabStop = False
        grpCommon.Text = "公共选项"
        ' 
        ' flpCommon
        ' 
        flpCommon.Dock = DockStyle.Fill
        flpCommon.FlowDirection = FlowDirection.TopDown
        flpCommon.Location = New Point(3, 19)
        flpCommon.Name = "flpCommon"
        flpCommon.Padding = New Padding(6, 6, 6, 0)
        flpCommon.Size = New Size(714, 158)
        flpCommon.TabIndex = 0
        flpCommon.WrapContents = False
        ' 
        ' cboMode
        ' 
        cboMode.DropDownStyle = ComboBoxStyle.DropDownList
        cboMode.Items.AddRange(New Object() {"external（外流绕流：风洞/汽车/建筑）", "internal（容器内流：发酵罐/管道）"})
        cboMode.Name = "cboMode"
        cboMode.Size = New Size(430, 23)
        cboMode.TabIndex = 0
        ' 
        ' txtSolids
        ' 
        txtSolids.AcceptsReturn = True
        txtSolids.Multiline = True
        txtSolids.Name = "txtSolids"
        txtSolids.ScrollBars = ScrollBars.Vertical
        txtSolids.Size = New Size(430, 62)
        txtSolids.TabIndex = 1
        txtSolids.WordWrap = False
        ' 
        ' chkGpu
        ' 
        chkGpu.AutoSize = True
        chkGpu.Name = "chkGpu"
        chkGpu.Size = New Size(300, 19)
        chkGpu.TabIndex = 2
        chkGpu.Text = "使用 CUDA GPU 张量后端（--gpu）"

        Call flpCommon.Controls.Add(CliToolLayout.MakeRow("计算域语义 --mode", cboMode))
        Call flpCommon.Controls.Add(CliToolLayout.MakeRow("内流次级实体", txtSolids))
        Call flpCommon.Controls.Add(CliToolLayout.MakeNote("    每行一个模型文件（挡板/桨叶/轴），仅 internal 模式生效"))
        Call flpCommon.Controls.Add(CliToolLayout.MakeRow("加速", chkGpu))

        ' 
        ' grpPatch
        ' 
        grpPatch.Controls.Add(flpPatch)
        grpPatch.Location = New Point(12, 784)
        grpPatch.Margin = New Padding(0, 2, 0, 6)
        grpPatch.Name = "grpPatch"
        grpPatch.Size = New Size(CliToolLayout.RowWidth, 200)
        grpPatch.TabIndex = 4
        grpPatch.TabStop = False
        grpPatch.Text = "边界 Patch 规则（--patch 名称:类型:盒面）"
        ' 
        ' flpPatch
        ' 
        flpPatch.Dock = DockStyle.Fill
        flpPatch.FlowDirection = FlowDirection.TopDown
        flpPatch.Location = New Point(3, 19)
        flpPatch.Name = "flpPatch"
        flpPatch.Padding = New Padding(6, 6, 6, 0)
        flpPatch.Size = New Size(714, 178)
        flpPatch.TabIndex = 0
        flpPatch.WrapContents = False
        ' 
        ' txtPatchName
        ' 
        txtPatchName.Name = "txtPatchName"
        txtPatchName.Size = New Size(110, 23)
        txtPatchName.TabIndex = 0
        ' 
        ' cboPatchType
        ' 
        cboPatchType.DropDownStyle = ComboBoxStyle.DropDownList
        cboPatchType.Items.AddRange(New Object() {"Wall", "Inlet", "Outlet", "Symmetry"})
        cboPatchType.Name = "cboPatchType"
        cboPatchType.Size = New Size(100, 23)
        cboPatchType.TabIndex = 1
        ' 
        ' cboPatchFace
        ' 
        cboPatchFace.DropDownStyle = ComboBoxStyle.DropDownList
        cboPatchFace.Items.AddRange(New Object() {"XMin", "XMax", "YMin", "YMax", "ZMin", "ZMax"})
        cboPatchFace.Name = "cboPatchFace"
        cboPatchFace.Size = New Size(90, 23)
        cboPatchFace.TabIndex = 2
        ' 
        ' btnPatchAdd
        ' 
        btnPatchAdd = CliToolLayout.MakeButton("添加", False, 72)
        btnPatchAdd.Name = "btnPatchAdd"
        btnPatchAdd.TabIndex = 3
        ' 
        ' lstPatches
        ' 
        lstPatches.Name = "lstPatches"
        lstPatches.Size = New Size(430, 76)
        lstPatches.TabIndex = 4
        ' 
        ' btnPatchRemove
        ' 
        btnPatchRemove = CliToolLayout.MakeButton("移除选中", False, 96)
        btnPatchRemove.Name = "btnPatchRemove"
        btnPatchRemove.TabIndex = 5

        Call flpPatch.Controls.Add(CliToolLayout.MakeRow("名称 / 类型 / 盒面", txtPatchName, cboPatchType, cboPatchFace, btnPatchAdd))
        Call flpPatch.Controls.Add(CliToolLayout.MakeRow("已添加规则", lstPatches, btnPatchRemove))
        Call flpPatch.Controls.Add(CliToolLayout.MakeNote("    例：ground : Wall : YMin —— 把计算域底面归为壁面 patch"))

        ' 
        ' grpTool
        ' 
        grpTool.Controls.Add(flpTool)
        grpTool.Location = New Point(12, 990)
        grpTool.Margin = New Padding(0, 2, 0, 6)
        grpTool.Name = "grpTool"
        grpTool.Size = New Size(CliToolLayout.RowWidth, 76)
        grpTool.TabIndex = 5
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

        Call flpTool.Controls.Add(CliToolLayout.MakeRow("EulerianMesh.exe", txtExe, btnExe))

        Call flpParams.Controls.Add(grpIO)
        Call flpParams.Controls.Add(grpMaxres)
        Call flpParams.Controls.Add(grpConvert)
        Call flpParams.Controls.Add(grpCommon)
        Call flpParams.Controls.Add(grpPatch)
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
        btnCopy.Size = New Size(72, 28)
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
        ' FormEulerianMesh
        ' 
        AutoScaleDimensions = New SizeF(7.0!, 15.0!)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(920, 760)
        Controls.Add(tblRoot)
        MinimumSize = New Size(820, 620)
        Name = "FormEulerianMesh"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Eulerian Mesh —— 三维模型转 CFD 网格"

        Call tblRoot.ResumeLayout(False)
        Call pnlHeader.ResumeLayout(False)
        Call flpIO.ResumeLayout(False)
        Call flpMaxres.ResumeLayout(False)
        Call flpConvert.ResumeLayout(False)
        Call flpCommon.ResumeLayout(False)
        Call flpPatch.ResumeLayout(False)
        Call flpTool.ResumeLayout(False)
        Call pnlCommand.ResumeLayout(False)
        Call pnlActions.ResumeLayout(False)
        Call pnlLog.ResumeLayout(False)
        Call pnlStatus.ResumeLayout(False)
        Call ResumeLayout(False)
        Call PerformLayout()
    End Sub

    Friend WithEvents Timer1 As Timer
    Friend WithEvents btnInput As Button
    Friend WithEvents btnOutput As Button
    Friend WithEvents btnExe As Button
    Friend WithEvents btnRun As Button
    Friend WithEvents btnStop As Button
    Friend WithEvents btnOpenOutput As Button
    Friend WithEvents btnClose As Button
    Friend WithEvents btnCopy As Button
    Friend WithEvents btnPatchAdd As Button
    Friend WithEvents btnPatchRemove As Button

    Friend tblRoot As TableLayoutPanel
    Friend pnlHeader As Panel
    Friend lblTitle As Label
    Friend lblSub As Label
    Friend flpParams As FlowLayoutPanel

    Friend grpIO As GroupBox
    Friend flpIO As FlowLayoutPanel
    Friend cboCommand As ComboBox
    Friend txtInput As TextBox
    Friend txtOutput As TextBox

    Friend grpMaxres As GroupBox
    Friend flpMaxres As FlowLayoutPanel
    Friend nudCells As NumericUpDown
    Friend nudDepth As NumericUpDown
    Friend nudDomainScale As NumericUpDown
    Friend nudVoxelRatio As NumericUpDown
    Friend nudMaxVoxels As NumericUpDown
    Friend chkPlan As CheckBox

    Friend grpConvert As GroupBox
    Friend flpConvert As FlowLayoutPanel
    Friend cboType As ComboBox
    Friend nudResolution As NumericUpDown
    Friend nudDomainScaleC As NumericUpDown
    Friend nudPoisson As NumericUpDown
    Friend chkCutCells As CheckBox
    Friend chkNoSmooth As CheckBox

    Friend grpCommon As GroupBox
    Friend flpCommon As FlowLayoutPanel
    Friend cboMode As ComboBox
    Friend txtSolids As TextBox
    Friend chkGpu As CheckBox

    Friend grpPatch As GroupBox
    Friend flpPatch As FlowLayoutPanel
    Friend txtPatchName As TextBox
    Friend cboPatchType As ComboBox
    Friend cboPatchFace As ComboBox
    Friend lstPatches As ListBox

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
