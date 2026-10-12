Imports Moira.CFDDxCanvas.Rendering
Imports Moira.CFDEngine.Eulerian
Imports Moira.FVMEngine.Eulerian
Imports Math = System.Math

' /********************************************************************************/
'
'   FormWindTunnel.vb
'
'   FVM 风洞测试模拟器主窗体（原 LBM 引擎已移除）：
'
'       - 内嵌 WindTunnelCanvas（DxScene3DCanvas / Direct3D11 实时渲染）
'       - 加载 src\EulerianMesh 命令行工具预生成的 *.eulerian.zip 网格包
'         （网格生成慢且吃内存，放在命令行预生成、GUI 只加载，是生产的正确分工）
'       - 求解在后台 Task（Simulation\WindTunnelSimulationRunner），
'         逐块生成"重采样后的笛卡尔快照"并发布；UI 定时器只负责取最新快照渲染
'       - 三维可视化：热图点云 / 矢量箭头 / 流线，按重采样后的结构格渲染
'
' /********************************************************************************/

Public Class FormWindTunnel

#Region "窗体状态（控件声明见 FormWindTunnel.Designer.vb）"

    ''' <summary>后台求解编排器（在 UI 线程构造，事件自动封送回 UI）。</summary>
    Private m_runner As WindTunnelSimulationRunner

    Private m_running As Boolean
    Private m_loading As Boolean
    Private m_lastMsPerFrame As Double
    Private m_lastStepCount As Integer
    Private m_sw As Diagnostics.Stopwatch

#End Region

#Region "生命周期"

    Private Sub FormWindTunnel_Load(sender As Object, e As EventArgs) Handles Me.Load
        m_runner = New WindTunnelSimulationRunner()

        AddHandler m_runner.Finished, AddressOf OnRunnerFinished

        ' 来流速度：LBM 时代是格子单位（0.01~0.02），FVM 改为真实 m/s
        m_numFreestream.DecimalPlaces = 1
        m_numFreestream.Minimum = 0.1D
        m_numFreestream.Maximum = 200.0D
        m_numFreestream.Increment = 1.0D
        m_numFreestream.Value = 30.0D

        m_lblFreestream.Text = "来流(m/s):"

        m_lblStatus.Text = "就绪。点击「加载模型」选择 *.eulerian.zip 网格包。"
        m_progress.Visible = False
    End Sub

    Private Sub FormWindTunnel_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If m_runner IsNot Nothing Then
            m_runner.Dispose()
        End If
    End Sub

#End Region

#Region "网格加载"

    Private Sub OnLoadModel(sender As Object, e As EventArgs) Handles m_btnLoad.Click
        If m_loading Then Return

        Using dlg As New OpenFileDialog With {
            .Title = "选择 Eulerian 网格包 (*.eulerian.zip)",
            .Filter = "Eulerian 网格包|*.eulerian.zip|所有文件|*.*"
        }
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Call LoadModelAsync(dlg.FileName)
            End If
        End Using
    End Sub

    ''' <summary>
    ''' 加载网格包（后台执行，避免大网格解压 + 几何计算卡住 UI）。
    ''' </summary>
    Private Async Sub LoadModelAsync(path As String)
        Try
            m_loading = True
            Cursor = Cursors.WaitCursor

            Call StopSolve()

            m_timer.Stop()
            m_running = False
            m_btnRun.Text = "启动"
            m_btnRun.Enabled = False
            m_btnStep.Enabled = False
            m_btnReset.Enabled = False
            m_progress.Visible = False

            m_lblStatus.Text = $"正在加载网格包: {IO.Path.GetFileName(path)} ..."

            If m_runner Is Nothing Then
                m_runner = New WindTunnelSimulationRunner()
                AddHandler m_runner.Finished, AddressOf OnRunnerFinished
            End If

            Dim info As String = Await Task.Run(Function() m_runner.LoadMesh(path))

            Call m_canvas.ClearScene()

            ' 用当前参数建立算例，得到第 0 步快照并绑定画布
            Call ApplyOptions()

            Dim snap As EulerianFieldSnapshot = m_runner.LatestSnapshot

            If snap IsNot Nothing Then
                Call m_canvas.BindSnapshot(snap)
            End If

            m_sliceBar.Maximum = Math.Max(1, SliceMax() - 1)
            m_sliceBar.Value = m_sliceBar.Maximum \ 2

            m_btnRun.Enabled = True
            m_btnStep.Enabled = True
            m_btnReset.Enabled = True

            m_lblStatus.Text = info.Replace(vbCrLf, "   ").TrimEnd()

        Catch ex As Exception
            Call ShowError("网格加载失败", ex)

            m_lblStatus.Text = $"加载失败: {ex.Message}"

        Finally
            m_loading = False
            Cursor = Cursors.Default
        End Try
    End Sub

    ''' <summary>按当前 UI 参数（重新）构建算例。</summary>
    Private Sub ApplyOptions()
        If m_runner Is Nothing OrElse m_runner.Mesh Is Nothing Then Return

        Dim opts As EulerianWindTunnelOptions = BuildOptions()
        Dim interval As Integer = Math.Max(1, CInt(m_numStepsPerFrame.Value))

        Call m_runner.Configure(opts, interval)
    End Sub

    ''' <summary>把 UI 控件值映射为 FVM 求解参数。</summary>
    Private Function BuildOptions() As EulerianWindTunnelOptions
        Dim turb As WindTunnelTurbulence

        Select Case m_cmbTurb.SelectedIndex
            Case 0 : turb = WindTunnelTurbulence.Laminar
            Case 2 : turb = WindTunnelTurbulence.KOmegaSST
            Case Else : turb = WindTunnelTurbulence.KEpsilon
        End Select

        Dim speed As Double = CDbl(m_numFreestream.Value)

        Return New EulerianWindTunnelOptions With {
            .InletSpeed = speed,
            .InletDirection = New EulerianVertex(1.0R, 0.0R, 0.0R),
            .Density = CDbl(m_numDensity.Value),
            .KinematicViscosity = CDbl(m_numViscosity.Value),
            .Turbulence = turb,
            .TurbulenceIntensity = CDbl(m_numTi.Value) / 100.0R,
            .Compressible = m_chkCompressible.Checked,
            .Mach = CDbl(m_numMach.Value),
            .UseGpu = m_chkGpu.Checked,
            .ReferenceArea = Math.Max(1.0E-6, CDbl(m_numRefArea.Value)),
            .PressureMaxIterations = 200
        }
    End Function

    Private Function SliceMax() As Integer
        If m_runner Is Nothing OrElse m_runner.Sampler Is Nothing Then
            Return 100
        End If

        Select Case m_canvas.SliceAxis
            Case WindTunnelCanvas.SliceAxisKind.Y : Return m_runner.Sampler.Ny
            Case WindTunnelCanvas.SliceAxisKind.Z : Return m_runner.Sampler.Nz
            Case Else : Return m_runner.Sampler.Nx
        End Select
    End Function

#End Region

#Region "仿真控制"

    Private Sub OnToggleRun(sender As Object, e As EventArgs) Handles m_btnRun.Click
        If m_runner Is Nothing OrElse m_runner.CaseObject Is Nothing Then
            Return
        End If

        If m_running Then
            Call PauseSolve()
        Else
            Call StartSolve()
        End If
    End Sub

    Private Sub StartSolve()
        Dim total As Integer = CInt(m_numTotalSteps.Value)
        Dim done As Integer = m_runner.DoneSteps

        If done >= total Then
            ' 已经跑满：自动重置后再跑，避免"点了没反应"
            Call ApplyOptions()
            done = 0
        End If

        ' 参数可能在暂停期间被改动 —— 只有从 0 步开始时才重建算例
        If done = 0 Then
            Call ApplyOptions()

            Dim snap0 As EulerianFieldSnapshot = m_runner.LatestSnapshot

            If snap0 IsNot Nothing Then
                Call m_canvas.BindSnapshot(snap0)
            End If
        End If

        m_progress.Visible = True
        m_progress.Minimum = 0
        m_progress.Maximum = Math.Max(1, total)
        m_progress.Value = Math.Min(done, total)

        m_sw = Diagnostics.Stopwatch.StartNew()
        m_lastStepCount = done

        m_running = True
        m_btnRun.Text = "暂停"
        m_btnStop.Enabled = True

        m_timer.Start()
        m_runner.Start(total - done)
    End Sub

    Private Sub PauseSolve()
        Call StopSolve()
        m_lblStatus.Text = $"已暂停（已完成 {m_runner.DoneSteps} 步）"
    End Sub

    Private Sub StopSolve()
        If m_runner IsNot Nothing Then
            m_runner.Stop()
        End If

        m_timer.Stop()
        m_running = False
        m_btnRun.Text = "启动"
        m_btnStop.Enabled = False
    End Sub

    Private Sub OnStop(sender As Object, e As EventArgs) Handles m_btnStop.Click
        Call StopSolve()
    End Sub

    Private Sub OnStepOnce(sender As Object, e As EventArgs) Handles m_btnStep.Click
        If m_runner Is Nothing OrElse m_runner.CaseObject Is Nothing Then
            Return
        End If
        If m_runner.IsRunning Then Return

        m_sw = Diagnostics.Stopwatch.StartNew()

        m_runner.StepOnce()

        m_lastMsPerFrame = m_sw.Elapsed.TotalMilliseconds
        m_lastStepCount = m_runner.DoneSteps

        Call RenderLatest()
        Call UpdateOverlay()
    End Sub

    Private Sub OnResetFlow(sender As Object, e As EventArgs) Handles m_btnReset.Click
        If m_runner Is Nothing OrElse m_runner.Mesh Is Nothing Then
            Return
        End If

        Try
            Cursor = Cursors.WaitCursor

            Call StopSolve()
            Call m_canvas.InvalidatePathlines()
            Call ApplyOptions()

            Dim snap As EulerianFieldSnapshot = m_runner.LatestSnapshot

            If snap IsNot Nothing Then
                Call m_canvas.BindSnapshot(snap)
            End If

            m_progress.Visible = False
            m_progress.Value = 0

            Call UpdateOverlay()
            m_lblStatus.Text = "已重置：用当前参数重建算例。"

        Catch ex As Exception
            Call ShowError("重置失败", ex)

        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub OnRunnerFinished(success As Boolean, message As String)
        Call StopSolve()
        Call RenderLatest()
        Call UpdateOverlay()

        m_lblStatus.Text = message
    End Sub

    Private Sub OnCompressibleChanged(sender As Object, e As EventArgs) Handles m_chkCompressible.CheckedChanged
        m_numMach.Enabled = m_chkCompressible.Checked
    End Sub

#End Region

#Region "渲染节拍"

    Private Sub OnTimerTick(sender As Object, e As EventArgs) Handles m_timer.Tick
        If Not m_running Then Return

        Dim snap As EulerianFieldSnapshot = m_runner.LatestSnapshot

        If snap IsNot Nothing AndAlso snap.StepIndex <> m_lastStepCount Then
            Dim steps As Integer = snap.StepIndex - m_lastStepCount

            m_lastStepCount = snap.StepIndex
            m_lastMsPerFrame = If(steps > 0, m_sw.Elapsed.TotalMilliseconds / steps, 0.0R)
            m_sw.Restart()

            Call RenderLatest()
        End If

        Call UpdateOverlay()
    End Sub

    ''' <summary>把最新快照送到画布（结构格尺寸未变时走轻量更新）。</summary>
    Private Sub RenderLatest()
        Dim snap As EulerianFieldSnapshot = m_runner.LatestSnapshot

        If snap Is Nothing Then Return

        If m_canvas.Snapshot Is Nothing OrElse
           m_canvas.Snapshot.Nx <> snap.Nx OrElse
           m_canvas.Snapshot.Ny <> snap.Ny OrElse
           m_canvas.Snapshot.Nz <> snap.Nz Then

            Call m_canvas.BindSnapshot(snap)
        Else
            Call m_canvas.UpdateSnapshot(snap)
        End If
    End Sub

    ''' <summary>更新画布叠加文本、进度条与状态栏。</summary>
    Private Sub UpdateOverlay()
        Dim snap As EulerianFieldSnapshot = If(m_runner IsNot Nothing, m_runner.LatestSnapshot, Nothing)

        If snap Is Nothing Then
            m_canvas.OverlayText = ""
            Return
        End If

        Dim ci As Globalization.CultureInfo = Globalization.CultureInfo.InvariantCulture
        Dim msPerStep As Double = m_lastMsPerFrame
        Dim total As Integer = Math.Max(1, CInt(m_numTotalSteps.Value))

        m_canvas.OverlayText =
            $"grid = {snap.Nx}x{snap.Ny}x{snap.Nz}   " &
            $"cells = {If(m_runner.Mesh IsNot Nothing, m_runner.Mesh.CellCount, 0)}   " &
            $"step = {snap.StepIndex}{Environment.NewLine}" &
            $"res = {snap.Residual.ToString("E2", ci)}   " &
            $"cont = {snap.ContinuityError.ToString("E2", ci)}   " &
            $"|u|max = {snap.MaxSpeed.ToString("F3", ci)}{Environment.NewLine}" &
            $"Cd = {snap.Cd.ToString("F4", ci)}   " &
            $"Cl = {snap.Cl.ToString("F4", ci)}   " &
            $"Cm = {snap.Cm.ToString("F4", ci)}{Environment.NewLine}" &
            $"{msPerStep:F1} ms/step"

        m_lblStatus.Text =
            $"step = {snap.StepIndex}/{total}   res = {snap.Residual.ToString("E2", ci)}   " &
            $"|u|max = {snap.MaxSpeed.ToString("F3", ci)}   " &
            $"Cd = {snap.Cd.ToString("F4", ci)}   {snap.Summary}"

        If m_progress.Visible Then
            m_progress.Value = Math.Min(m_progress.Maximum, snap.StepIndex)
        End If
    End Sub

#End Region

#Region "可视化交互（纯 UI 转发，无数值语义）"

    Private Sub OnToggleSlice(sender As Object, e As EventArgs) Handles m_chkSlice.CheckedChanged
        Dim isOn As Boolean = m_chkSlice.Checked

        m_canvas.SliceEnabled = isOn
        m_cmbSliceAxis.Enabled = isOn
        m_sliceBar.Enabled = isOn
        m_chkSliceOnly.Enabled = isOn
        m_chkClipSolid.Enabled = isOn
        m_row3.Visible = isOn
        m_sliceBar.Maximum = Math.Max(1, SliceMax() - 1)

        Call m_canvas.RefreshField()
    End Sub

    Private Sub OnFieldChanged(sender As Object, e As EventArgs) Handles m_cmbField.SelectedIndexChanged
        m_canvas.Field = CType(m_cmbField.SelectedIndex, WindTunnelCanvas.TunnelField)
        m_canvas.RefreshField()
    End Sub

    '''
    ''' 可视化类型切换：热图 / 矢量图 / 流线图（严格互斥，同一时刻只有一种）。
    '''
    Private Sub OnVisualizationChanged(sender As Object, e As EventArgs) Handles m_cmbViz.SelectedIndexChanged
        m_canvas.Visualization = CType(m_cmbViz.SelectedIndex, VisualizationMode)
        Call m_canvas.RefreshField()
    End Sub

    '''
    ''' 裁剪远端横截面时，是否把域外（障碍）本体一并裁剪（默认不裁剪）。
    '''
    Private Sub OnClipSolidModelChanged(sender As Object, e As EventArgs) Handles m_chkClipSolid.CheckedChanged
        m_canvas.ClipSolidModelWithSection = m_chkClipSolid.Checked
        Call m_canvas.RefreshField()
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

#End Region

#Region "错误处理"

    Private Sub ShowError(title As String, ex As Exception)
        Dim sb As New Text.StringBuilder(256)

        sb.AppendLine(ex.Message)

        If m_runner IsNot Nothing AndAlso m_runner.Mesh IsNot Nothing Then
            sb.AppendLine($"cells={m_runner.Mesh.CellCount}")
        End If

        sb.AppendLine()
        sb.AppendLine(ex.StackTrace)

        MessageBox.Show(Me, sb.ToString(), title, MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

#End Region

End Class
