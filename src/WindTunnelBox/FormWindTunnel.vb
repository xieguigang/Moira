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

#Region "窗体状态（控件声明见 FormWindTunnel.Designer.vb）"

    Private m_running As Boolean
    Private m_lastMsPerFrame As Double

    Private m_scene As WindTunnelScene
    Private m_solver As WindTunnelSolver

#End Region

#Region "构造与 UI 构建"

    Public Sub New()
        ' 此调用是设计器所必需的。
        InitializeComponent()
    End Sub

    Private Sub OnFormShown(sender As Object, e As EventArgs) Handles MyBase.Shown
        ' Demo 自动加载：优先探测预体素化产物（Voxelizer CLI，跳过 GUI 内体素化）：
        ' .voxel.zip 二进制体素包 → .voxel.json（旧格式）→ 原始模型
        Dim preVoxelZip As String = IO.Path.ChangeExtension(DefaultModelPath, ".voxel.zip")
        Dim preVoxelJson As String = IO.Path.ChangeExtension(DefaultModelPath, ".voxel.json")

        If IO.File.Exists(preVoxelZip) Then
            Call LoadModel(preVoxelZip)
        ElseIf IO.File.Exists(preVoxelJson) Then
            Call LoadModel(preVoxelJson)
        ElseIf IO.File.Exists(DefaultModelPath) Then
            Call LoadModel(DefaultModelPath)
        End If
    End Sub

#End Region

#Region "模型加载与求解器生命周期"

    Private Sub OnLoadModel(sender As Object, e As EventArgs) Handles m_btnLoad.Click
        Using dlg As New OpenFileDialog With {
            .Title = "选择 3D 模型文件 (STL / 3MF / OBJ ...) 或预体素化 json",
            .Filter = "3D 模型|*.stl;*.3mf;*.obj;*.dae;*.3ds|体素模型文件|*.voxel.zip;*.voxel.json;*.json|所有文件|*.*",
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
            Dim resolution As Integer = 512
            Dim voxelSize As Double = If(voxelizer = VoxelizerKind.Physical, CDbl(m_numVoxelSize.Value), 0.0R)

            ' ---- 1. 加载 + 体素化/直载 + 放大定位 ----
            Dim ext As String = IO.Path.GetExtension(path).ToLowerInvariant()

            If ext = ".json" OrElse ext = ".zip" Then
                ' 预体素化产物直载（Voxelizer CLI：.voxel.zip 二进制包 / .json 旧格式）
                m_lblStatus.Text = $"正在加载体素模型文件: {IO.Path.GetFileName(path)}..."
                m_scene = WindTunnelVoxelJson.BuildSceneFromJson(path, domainScale:=2.0, groundClearance:=0)
            Else
                m_lblStatus.Text = $"正在加载并体素化: {IO.Path.GetFileName(path)} ({voxelizer})..."
                m_scene = WindTunnelSceneBuilder.BuildScene(
                    path, resolution, voxelizer,
                    domainScale:=2.0, groundClearance:=0,
                    voxelSize:=voxelSize)
            End If

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

    Private Sub OnToggleRun(sender As Object, e As EventArgs) Handles m_btnRun.Click
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

    Private Sub OnStepOnce(sender As Object, e As EventArgs) Handles m_btnStep.Click
        If m_solver Is Nothing Then
            Return
        End If

        Call RunSteps(1)
        Call m_canvas.RefreshField()
        Call UpdateOverlay()
    End Sub

    Private Sub OnResetFlow(sender As Object, e As EventArgs) Handles m_btnReset.Click
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

    Private Sub OnToggleSlice(sender As Object, e As EventArgs) Handles m_chkSlice.CheckedChanged
        Dim isOn As Boolean = m_chkSlice.Checked

        m_canvas.SliceEnabled = isOn
        m_cmbSliceAxis.Enabled = isOn
        m_sliceBar.Enabled = isOn
        m_chkSliceOnly.Enabled = isOn
        m_sliceBar.Maximum = Math.Max(1, SliceMax() - 1)

        Call m_canvas.RefreshField()
    End Sub

    Private Sub OnFieldChanged(sender As Object, e As EventArgs) Handles m_cmbField.SelectedIndexChanged
        m_canvas.Field = CType(m_cmbField.SelectedIndex, WindTunnelCanvas.TunnelField)
        m_canvas.RefreshField()
    End Sub

    Private Sub OnArrowsChanged(sender As Object, e As EventArgs) Handles m_chkArrows.CheckedChanged
        m_canvas.ShowArrows = m_chkArrows.Checked
        m_canvas.RefreshField()
    End Sub

    Private Sub OnThresholdChanged(sender As Object, e As EventArgs) Handles m_thresholdBar.ValueChanged
        m_canvas.Threshold = m_thresholdBar.Value / 100.0R
        m_lblThreshold.Text = $"{m_thresholdBar.Value}%"
        m_canvas.RefreshField()
    End Sub

    Private Sub OnSliceAxisChanged(sender As Object, e As EventArgs) Handles m_cmbSliceAxis.SelectedIndexChanged
        m_canvas.SliceAxis = CType(m_cmbSliceAxis.SelectedIndex, WindTunnelCanvas.SliceAxisKind)
        m_canvas.SlicePosition = Math.Min(m_canvas.SlicePosition, SliceMax() - 1)
        m_canvas.RefreshField()
    End Sub

    Private Sub OnSlicePositionChanged(sender As Object, e As EventArgs) Handles m_sliceBar.ValueChanged
        m_canvas.SlicePosition = m_sliceBar.Value
        m_canvas.RefreshField()
    End Sub

    Private Sub OnSliceOnlyChanged(sender As Object, e As EventArgs) Handles m_chkSliceOnly.CheckedChanged
        m_canvas.SliceOnly = m_chkSliceOnly.Checked
        m_canvas.RefreshField()
    End Sub

    Private Sub OnTimerTick(sender As Object, e As EventArgs) Handles m_timer.Tick
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
