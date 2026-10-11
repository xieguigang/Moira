Imports Moira.LBMEngine
Imports Moira.ZMesh
Imports Math = System.Math

' /********************************************************************************/
'
'   FormWindTunnel.vb
'
'   LBM 风洞测试模拟器主窗体：
'
'       - 内嵌 WindTunnelCanvas（DxScene3DCanvas / Direct3D11 实时渲染）
'       - Timer 逐帧步进 WindTunnelSolver（LBM float32，CUDA 设备驻留 / CPU 回退）
'       - 通过 ZMesh WindTunnelSceneBuilder 加载 STL/3MF 模型并体素化
'         （Standard / Sdf / Physical / SdfRaw 四条路径，Physical/SdfRaw 无精度限制）
'       - Demo：ZMesh/CityBlender/samples/city_seed42.stl
'
' /********************************************************************************/

Public Class FormWindTunnel

#Region "默认 Demo 资源"

    ''' <summary>默认加载的 demo 模型（city_seed42.stl）。</summary>
    Public Shared ReadOnly DefaultModelPath As String =
        "G:\Moira\src\ZMesh\CityBlender\samples\city_seed42.stl"

    ''' <summary>风洞粘度（格子单位；τ = ν/cs² + 1/2 ≈ 0.515，TRT 稳定）。</summary>
    Const DefaultViscosity As Double = 0.005

#End Region

#Region "窗体控件"

    Private WithEvents m_canvas As WindTunnelCanvas
    Private WithEvents m_timer As New Timer With {.Interval = 30}

    Private m_toolbar As New Panel With {.Dock = DockStyle.Top, .Height = 114, .Padding = New Padding(8, 6, 8, 2)}
    Private m_status As New StatusStrip
    Private m_lblStatus As New ToolStripStatusLabel With {.Text = "就绪。点击「加载模型」开始风洞测试。"}

    ' 第一行：动作
    Private m_btnLoad As New Button With {.Text = "加载模型", .Width = 88}
    Private m_btnRun As New Button With {.Text = "启动", .Width = 64, .Enabled = False}
    Private m_btnStep As New Button With {.Text = "单步", .Width = 56, .Enabled = False}
    Private m_btnReset As New Button With {.Text = "重置流场", .Width = 80, .Enabled = False}
    Private m_chkGpu As New CheckBox With {.Text = "CUDA 加速", .Checked = True, .AutoSize = True}
    Private m_chkArrows As New CheckBox With {.Text = "速度箭头", .AutoSize = True}
    Private m_chkSlice As New CheckBox With {.Text = "截面", .AutoSize = True}

    ' 第二行：参数
    Private m_cmbField As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 120}
    Private m_cmbVoxelizer As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 90}
    Private m_numResolution As New NumericUpDown With {.Minimum = 16, .Maximum = 160, .Value = 40, .Width = 64}
    Private m_numFreestream As New NumericUpDown With {.Minimum = 0.02D, .Maximum = 0.2D,
        .Increment = 0.01D, .Value = 0.1D, .DecimalPlaces = 2, .Width = 64}
    Private m_numStepsPerFrame As New NumericUpDown With {.Minimum = 1, .Maximum = 20, .Value = 2, .Width = 48}
    Private m_numVoxelSize As New NumericUpDown With {.Minimum = 0.1D, .Maximum = 10D,
        .Increment = 0.1D, .Value = 1.0D, .DecimalPlaces = 1, .Width = 56}

    ' 第三行：阈值（低于阈值的流体点透明，看见模型表面气流）
    Private m_thresholdBar As New TrackBar With {
        .Minimum = 0, .Maximum = 100, .TickFrequency = 10, .Width = 240,
        .AutoSize = False, .Height = 28}
    Private m_lblThreshold As New Label With {.Text = "0%", .AutoSize = True, .Margin = New Padding(6, 9, 0, 0)}

    ' 第四行：截面控制
    Private m_cmbSliceAxis As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 56, .Enabled = False}
    Private m_sliceBar As New TrackBar With {
        .Minimum = 0, .Maximum = 100, .TickFrequency = 10, .Width = 220,
        .Enabled = False, .AutoSize = False, .Height = 28}
    Private m_chkSliceOnly As New CheckBox With {.Text = "仅切片层", .AutoSize = True, .Enabled = False}

    Private m_running As Boolean
    Private m_lastMsPerFrame As Double

    Private m_scene As WindTunnelScene
    Private m_solver As WindTunnelSolver

#End Region

#Region "构造与 UI 构建"

    Public Sub New()
        InitializeComponent()
        BuildUi()

        Text = "LBM 风洞测试模拟器 (float32 CUDA / CPU)"
        ClientSize = New Size(1280, 760)
        StartPosition = FormStartPosition.CenterScreen

        AddHandler m_timer.Tick, AddressOf OnTimerTick
        AddHandler Shown, AddressOf OnFormShown
    End Sub

    Private Sub BuildUi()
        ' ---- 画布 ----
        m_canvas = New WindTunnelCanvas With {.Dock = DockStyle.Fill}
        Controls.Add(m_canvas)

        ' ---- 工具栏：三行流式布局 ----
        Dim row1 As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 36, .WrapContents = False}
        Dim row2 As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 36, .WrapContents = False}
        Dim rowT As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 32, .WrapContents = False}
        Dim row3 As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 34, .WrapContents = False}

        row1.Controls.AddRange(New Control() {
            m_btnLoad, m_btnRun, m_btnStep, m_btnReset,
            Sep(), m_chkGpu, m_chkArrows, m_chkSlice})

        Call AddLabel(row2, "显示场:")
        m_cmbField.Items.AddRange(New Object() {"速度幅值 |u|", "Ux (来流)", "Uy (竖直)", "Uz (展向)", "压力 cs²·Δρ"})
        m_cmbField.SelectedIndex = 0

        Call AddLabel(row2, "体素化:")
        m_cmbVoxelizer.Items.AddRange(New Object() {"Standard", "Sdf", "Physical", "SdfRaw"})
        m_cmbVoxelizer.SelectedIndex = 0

        Call AddLabel(row2, "分辨率:")
        Call AddLabel(row2, "来流 U∞:")
        Call AddLabel(row2, "步/帧:")
        Call AddLabel(row2, "voxelSize:")
        row2.Controls.AddRange(New Control() {
            m_cmbField, Sep(), m_cmbVoxelizer, Sep(),
            m_numResolution, Sep(), m_numFreestream, Sep(),
            m_numStepsPerFrame, Sep(), m_numVoxelSize})

        ' 阈值行：拖动滑块，低于阈值的流体点透明，露出模型表面气流
        Call AddLabel(rowT, "透明阈值:")
        rowT.Controls.AddRange(New Control() {m_thresholdBar, m_lblThreshold})

        m_cmbSliceAxis.Items.AddRange(New Object() {"X", "Y", "Z"})
        m_cmbSliceAxis.SelectedIndex = 0
        row3.Controls.AddRange(New Control() {
            m_cmbSliceAxis, m_sliceBar, m_chkSliceOnly})
        row3.Visible = False

        m_toolbar.Height = row1.Height + row2.Height + rowT.Height + row3.Height + 12
        m_toolbar.Controls.Add(row3)
        m_toolbar.Controls.Add(rowT)
        m_toolbar.Controls.Add(row2)
        m_toolbar.Controls.Add(row1)

        Controls.Add(m_toolbar)
        Controls.Add(m_status)
        m_status.Items.Add(m_lblStatus)

        ' ---- 事件 ----
        AddHandler m_btnLoad.Click, AddressOf OnLoadModel
        AddHandler m_btnRun.Click, AddressOf OnToggleRun
        AddHandler m_btnStep.Click, AddressOf OnStepOnce
        AddHandler m_btnReset.Click, AddressOf OnResetFlow

        AddHandler m_cmbField.SelectedIndexChanged,
            Sub()
                m_canvas.Field = CType(m_cmbField.SelectedIndex, WindTunnelCanvas.TunnelField)
                m_canvas.RefreshField()
            End Sub
        AddHandler m_chkArrows.CheckedChanged,
            Sub()
                m_canvas.ShowArrows = m_chkArrows.Checked
                m_canvas.RefreshField()
            End Sub
        AddHandler m_thresholdBar.ValueChanged,
            Sub()
                m_canvas.Threshold = m_thresholdBar.Value / 100.0R
                m_lblThreshold.Text = $"{m_thresholdBar.Value}%"
                m_canvas.RefreshField()
            End Sub
        AddHandler m_chkSlice.CheckedChanged, AddressOf OnToggleSlice
        AddHandler m_cmbSliceAxis.SelectedIndexChanged,
            Sub()
                m_canvas.SliceAxis = CType(m_cmbSliceAxis.SelectedIndex, WindTunnelCanvas.SliceAxisKind)
                m_canvas.SlicePosition = Math.Min(m_canvas.SlicePosition, SliceMax() - 1)
                m_canvas.RefreshField()
            End Sub
        AddHandler m_sliceBar.ValueChanged,
            Sub()
                m_canvas.SlicePosition = m_sliceBar.Value
                m_canvas.RefreshField()
            End Sub
        AddHandler m_chkSliceOnly.CheckedChanged,
            Sub()
                m_canvas.SliceOnly = m_chkSliceOnly.Checked
                m_canvas.RefreshField()
            End Sub
    End Sub

    Private Shared Function Sep() As Label
        Return New Label With {.Text = "   ", .AutoSize = True}
    End Function

    Private Shared Sub AddLabel(panel As FlowLayoutPanel, text As String)
        Call panel.Controls.Add(New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(6, 8, 0, 0)})
    End Sub

    Private Sub OnFormShown(sender As Object, e As EventArgs)
        ' Demo 模型存在时自动加载
        If IO.File.Exists(DefaultModelPath) Then
            Call LoadModel(DefaultModelPath)
        End If
    End Sub

#End Region

#Region "模型加载与求解器生命周期"

    Private Sub OnLoadModel(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {
            .Title = "选择 3D 模型文件 (STL / 3MF / OBJ ...)",
            .Filter = "3D 模型|*.stl;*.3mf;*.obj;*.dae;*.3ds|所有文件|*.*",
            .InitialDirectory = If(IO.File.Exists(DefaultModelPath),
                IO.Path.GetDirectoryName(DefaultModelPath), Environment.CurrentDirectory)
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Call LoadModel(dlg.FileName)
            End If
        End Using
    End Sub

    ''' <summary>加载模型 → 体素化 → 创建风洞求解器 → 绑定渲染画布。</summary>
    Private Sub LoadModel(path As String)
        Try
            Cursor = Cursors.WaitCursor
            m_timer.Stop()
            m_running = False
            m_btnRun.Text = "启动"

            Dim useGpu As Boolean = m_chkGpu.Checked
            Dim voxelizer As VoxelizerKind = CType(m_cmbVoxelizer.SelectedIndex, VoxelizerKind)
            Dim resolution As Integer = CInt(m_numResolution.Value)
            Dim voxelSize As Double = If(voxelizer = VoxelizerKind.Physical, CDbl(m_numVoxelSize.Value), 0.0R)

            m_lblStatus.Text = $"正在加载并体素化: {IO.Path.GetFileName(path)} ({voxelizer})..."

            ' ---- 1. 加载 + 体素化 + 放大定位 ----
            m_scene = WindTunnelSceneBuilder.BuildScene(
                path, resolution, voxelizer,
                domainScale:=2.0, groundClearance:=0,
                voxelSize:=voxelSize)

            ' ---- 2. 创建 LBM float32 风洞求解器 ----
            If m_solver IsNot Nothing Then
                Call m_solver.Dispose()
            End If

            m_solver = WindTunnelSolver.FromVoxelShape(
                m_scene.Domain,
                freestream:=CDbl(m_numFreestream.Value),
                viscosity:=DefaultViscosity,
                groundNoSlip:=True,
                model:=CollisionModel.TRT,
                useGpu:=useGpu)

            ' ---- 3. 初始化来流并绑定画布 ----
            Call m_solver.InitializeFlow()
            Call m_canvas.BindSolver(m_solver)

            m_sliceBar.Maximum = Math.Max(1, SliceMax() - 1)
            m_sliceBar.Value = m_sliceBar.Maximum \ 2

            m_btnRun.Enabled = True
            m_btnStep.Enabled = True
            m_btnReset.Enabled = True

            Call UpdateOverlay()
            m_lblStatus.Text = $"{m_scene.Describe()}   求解路径 = {If(m_solver.UseGpu, "CUDA float32", "CPU float32")}"

        Catch ex As Exception
            MessageBox.Show(Me, $"模型加载失败:{Environment.NewLine}{ex.Message}",
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error)
            m_lblStatus.Text = $"加载失败: {ex.Message}"
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Function SliceMax() As Integer
        If m_solver Is Nothing Then
            Return 100
        End If

        Select Case m_canvas.SliceAxis
            Case WindTunnelCanvas.SliceAxisKind.Y : Return m_solver.Ny
            Case WindTunnelCanvas.SliceAxisKind.Z : Return m_solver.Nz
            Case Else : Return m_solver.Nx
        End Select
    End Function

#End Region

#Region "仿真控制"

    Private Sub OnToggleRun(sender As Object, e As EventArgs)
        If m_solver Is Nothing Then
            Return
        End If

        m_running = Not m_running
        m_btnRun.Text = If(m_running, "暂停", "启动")

        If m_running Then
            m_timer.Start()
        Else
            m_timer.Stop()
        End If
    End Sub

    Private Sub OnStepOnce(sender As Object, e As EventArgs)
        If m_solver Is Nothing Then
            Return
        End If

        Call RunSteps(1)
        Call m_canvas.RefreshField()
        Call UpdateOverlay()
    End Sub

    Private Sub OnResetFlow(sender As Object, e As EventArgs)
        If m_solver Is Nothing Then
            Return
        End If

        m_timer.Stop()
        m_running = False
        m_btnRun.Text = "启动"

        Call m_solver.InitializeFlow()
        Call m_canvas.RefreshField()
        Call UpdateOverlay()
    End Sub

    Private Sub OnToggleSlice(sender As Object, e As EventArgs)
        Dim isOn As Boolean = m_chkSlice.Checked

        m_canvas.SliceEnabled = isOn
        m_cmbSliceAxis.Enabled = isOn
        m_sliceBar.Enabled = isOn
        m_chkSliceOnly.Enabled = isOn
        m_sliceBar.Maximum = Math.Max(1, SliceMax() - 1)

        Call m_canvas.RefreshField()
    End Sub

    Private Sub OnTimerTick(sender As Object, e As EventArgs)
        If m_solver Is Nothing OrElse Not m_running Then
            Return
        End If

        Dim steps As Integer = CInt(m_numStepsPerFrame.Value)
        Dim sw = Stopwatch.StartNew()

        Call RunSteps(steps)

        m_lastMsPerFrame = sw.Elapsed.TotalMilliseconds
        Call m_canvas.RefreshField()
        Call UpdateOverlay()
    End Sub

    Private Sub RunSteps(steps As Integer)
        For s As Integer = 1 To steps
            Call m_solver.Advance()
        Next
    End Sub

    ''' <summary>更新左上角统计叠加与状态栏。</summary>
    Private Sub UpdateOverlay()
        If m_solver Is Nothing Then
            m_canvas.OverlayText = ""
            Return
        End If

        Dim wakeI As Integer = Math.Min(m_solver.Nx - 2, CInt(m_solver.Nx * 0.7))
        Dim msPerStep As Double = If(CInt(m_numStepsPerFrame.Value) > 0,
                                     m_lastMsPerFrame / CInt(m_numStepsPerFrame.Value), 0.0R)

        m_canvas.OverlayText =
            $"grid = {m_solver.Nx}x{m_solver.Ny}x{m_solver.Nz}   " &
            $"path = {If(m_solver.UseGpu, "CUDA", "CPU")}   " &
            $"step = {m_solver.StepCount}{Environment.NewLine}" &
            $"max|u| = {m_solver.ComputeMaxSpeed():F4}   " &
            $"Δmass = {m_solver.ComputeMassDrift():E2}   " &
            $"wake = {m_solver.ComputeWakeDeficit(wakeI):F4}{Environment.NewLine}" &
            $"sim = {msPerStep:F2} ms/step   frame = {m_lastMsPerFrame:F1} ms"

        m_lblStatus.Text =
            $"step = {m_solver.StepCount}   max|u| = {m_solver.ComputeMaxSpeed():F4}   " &
            $"wake = {m_solver.ComputeWakeDeficit(wakeI):F4}   " &
            $"{msPerStep:F2} ms/step   {If(m_solver.UseGpu, "CUDA float32", "CPU float32")}"
    End Sub

#End Region

End Class
