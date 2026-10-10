Imports System.Drawing.Drawing2D
Imports FluidBox.Rendering
Imports FluidBox.Simulation
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports std = System.Math

''' <summary>
''' The fluid box simulator: a 3d box that is filled with ten million SPH
''' particles, coloured by their speed and shaken with the mouse.
''' </summary>
Public Class Form1

    ' ---- the palette of the shell, see the design guide of the app ----
    Private Shared ReadOnly Back0 As Color = Color.FromArgb(11, 15, 20)
    Private Shared ReadOnly Back1 As Color = Color.FromArgb(19, 26, 34)
    Private Shared ReadOnly Back2 As Color = Color.FromArgb(27, 36, 48)
    Private Shared ReadOnly Accent As Color = Color.FromArgb(45, 212, 191)
    Private Shared ReadOnly Accent2 As Color = Color.FromArgb(56, 189, 248)
    Private Shared ReadOnly Text0 As Color = Color.FromArgb(230, 237, 243)
    Private Shared ReadOnly Text1 As Color = Color.FromArgb(139, 152, 165)
    Private Shared ReadOnly Good As Color = Color.FromArgb(52, 211, 153)
    Private Shared ReadOnly Warn As Color = Color.FromArgb(251, 191, 36)

    Private Shared s_fontName As String = Nothing

    Private m_sim As FluidBoxSim
    Private m_mapper As SpeedHeatMapper
    Private m_packer As InstancePacker

    Private WithEvents m_canvas As FluidSceneCanvas

    Private WithEvents m_toolbar As Panel
    Private WithEvents m_body As Panel
    Private WithEvents m_stage As Panel
    Private WithEvents m_params As Panel
    Private WithEvents m_status As Panel
    Private WithEvents m_legend As Panel
    Private WithEvents m_legendBar As Panel
    Private WithEvents m_legendMin As Label
    Private WithEvents m_legendMax As Label
    Private WithEvents m_legendName As Label
    Private WithEvents m_hud As Label
    Private WithEvents m_loading As Panel
    Private WithEvents m_loadingBar As Panel
    Private WithEvents m_loadingText As Label
    Private WithEvents m_loadingPct As Label

    Private WithEvents m_btnRun As Button
    Private WithEvents m_btnStep As Button
    Private WithEvents m_btnReset As Button
    Private WithEvents m_btnFit As Button
    Private WithEvents m_paletteBox As ComboBox
    Private WithEvents m_sizeBar As TrackBar

    Private WithEvents m_lstBackend As Label
    Private WithEvents m_lstStep As Label
    Private WithEvents m_lstRender As Label
    Private WithEvents m_lstMemory As Label

    Private WithEvents m_flow As FlowLayoutPanel
    Private WithEvents m_loadingStack As Panel

    Private WithEvents m_gravityHost As Panel
    Private WithEvents m_gravityName As Label
    Private WithEvents m_gravityRead As Label
    Private WithEvents m_gravityBar As TrackBar

    Private WithEvents m_viscosityHost As Panel
    Private WithEvents m_viscosityName As Label
    Private WithEvents m_viscosityRead As Label
    Private WithEvents m_viscosityBar As TrackBar

    Private WithEvents m_dampingHost As Panel
    Private WithEvents m_dampingName As Label
    Private WithEvents m_dampingRead As Label
    Private WithEvents m_dampingBar As TrackBar

    Private WithEvents m_soundHost As Panel
    Private WithEvents m_soundName As Label
    Private WithEvents m_soundRead As Label
    Private WithEvents m_soundBar As TrackBar

    Private WithEvents m_shakeHost As Panel
    Private WithEvents m_shakeName As Label
    Private WithEvents m_shakeRead As Label
    Private WithEvents m_shakeBar As TrackBar

    Private WithEvents m_tiltHost As Panel
    Private WithEvents m_tiltName As Label
    Private WithEvents m_tiltRead As Label
    Private WithEvents m_tiltBar As TrackBar

    Private m_progress As Double = 0
    Private m_ready As Boolean = False
    Private m_frameClock As Stopwatch = Stopwatch.StartNew()
    Private m_frames As Integer = 0
    Private m_fps As Double = 0
    Private m_fpsClock As Stopwatch = Stopwatch.StartNew()
    Private m_particleCount As Integer = 200_000
    Private m_renderBudget As Integer = 200_000
    Private m_adaptive As Boolean = False
    Private WithEvents m_clock As Timer
    Private m_statusClock As Stopwatch = Stopwatch.StartNew()

    ' the built in frame benchmark: FluidBox.exe --bench 20 writes the frame
    ' times of the first twenty seconds into fluidbox-bench.log and exits
    Private m_bench As Boolean = False
    Private m_noPhysics As Boolean = False
    Private m_benchSeconds As Double = 0
    Private m_benchClock As Stopwatch = Nothing
    Private m_benchTicks As Long = 0
    Private m_benchWork As Double = 0
    Private m_benchWorkFrames As Long = 0
    Private m_benchMaxDraw As Double = 0
    Private m_benchDrawSum As Double = 0
    Private m_gpuLogged As Boolean = False

    Sub New()
        ' 此调用是设计器所必需的。
        InitializeComponent()
    End Sub

    Private Shared Function UiFont(size As Single, Optional bold As Boolean = False) As Font
        If s_fontName Is Nothing Then
            s_fontName = "Segoe UI"

            For Each family As FontFamily In FontFamily.Families
                If family.Name = "Noto Sans" Then
                    s_fontName = "Noto Sans"
                    Exit For
                End If
            Next
        End If

        Return New Font(s_fontName, size, If(bold, FontStyle.Bold, FontStyle.Regular))
    End Function

    ' /********************************************************************************/
    '  construction
    ' /********************************************************************************/

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Call ParseCommandLine()
        Call SetupControls()
        Call RelayoutOverlay()
        Call StartLoading()

        ' The animation must not be driven by Application.Idle: the winforms
        ' message pump blocks in GetMessage as soon as the queue runs dry, so
        ' the idle event only fires once per message. With nothing to repaint
        ' the loop then stalls at a few ticks per second. A timer posts a
        ' message of its own and keeps the loop at a steady rate.
        m_clock = New Timer() With {.Interval = 8}
        Call m_clock.Start()
    End Sub

    ''' <summary>
    ''' ``--particles N`` and ``--budget N`` override the defaults of the shell,
    ''' which is handy for a smoke test on a small machine
    ''' </summary>
    Private Sub ParseCommandLine()
        Dim args = My.Application.CommandLineArgs

        For i As Integer = 0 To args.Count - 1
            If args(i) = "--particles" AndAlso i + 1 < args.Count Then
                m_particleCount = CInt(args(i + 1))
            ElseIf args(i) = "--budget" AndAlso i + 1 < args.Count Then
                m_renderBudget = CInt(args(i + 1))
            ElseIf args(i) = "--bench" AndAlso i + 1 < args.Count Then
                m_bench = True
                m_benchSeconds = CDbl(args(i + 1))
            ElseIf args(i) = "--nophys" Then
                m_noPhysics = True
            End If
        Next

        If m_particleCount < 10_000 Then m_particleCount = 10_000
        If m_renderBudget < 10_000 Then m_renderBudget = 10_000

        m_renderBudget = std.Min(m_renderBudget, m_particleCount)
    End Sub

    ''' <summary>
    ''' fill the controls that are populated at runtime (the palette combo box
    ''' and the six parameter sliders) and apply their default values
    ''' </summary>
    Private Sub SetupControls()
        m_paletteBox.Items.AddRange(System.Enum.GetValues(GetType(ScalerPalette)).Cast(Of Object)().ToArray())
        m_paletteBox.SelectedIndex = 0
        Call InitSliders()
    End Sub

    Private Sub InitSliders()
        Call OnGravityChanged(Nothing, Nothing)
        Call OnViscosityChanged(Nothing, Nothing)
        Call OnDampingChanged(Nothing, Nothing)
        Call OnSoundChanged(Nothing, Nothing)
        Call OnShakeChanged(Nothing, Nothing)
        Call OnTiltChanged(Nothing, Nothing)
    End Sub

    Private Sub UpdateSliderRead(bar As TrackBar, read As Label, scale As Integer)
        Dim v As Double = bar.Value / std.Pow(10, scale)
        read.Text = v.ToString("F" & scale.ToString())
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        Call MyBase.OnShown(e)
        Call RelayoutOverlay()
    End Sub

    ' /********************************************************************************/
    '  the parameter sliders
    ' /********************************************************************************/

    Private Sub OnGravityChanged(sender As Object, e As EventArgs) Handles m_gravityBar.ValueChanged
        UpdateSliderRead(m_gravityBar, m_gravityRead, 2)
        OnGravity(m_gravityBar.Value / 100.0)
    End Sub

    Private Sub OnViscosityChanged(sender As Object, e As EventArgs) Handles m_viscosityBar.ValueChanged
        UpdateSliderRead(m_viscosityBar, m_viscosityRead, 1)
        OnViscosity(m_viscosityBar.Value / 10.0)
    End Sub

    Private Sub OnDampingChanged(sender As Object, e As EventArgs) Handles m_dampingBar.ValueChanged
        UpdateSliderRead(m_dampingBar, m_dampingRead, 2)
        OnDamping(m_dampingBar.Value / 100.0)
    End Sub

    Private Sub OnSoundChanged(sender As Object, e As EventArgs) Handles m_soundBar.ValueChanged
        UpdateSliderRead(m_soundBar, m_soundRead, 1)
        OnSoundSpeed(m_soundBar.Value / 10.0)
    End Sub

    Private Sub OnShakeChanged(sender As Object, e As EventArgs) Handles m_shakeBar.ValueChanged
        UpdateSliderRead(m_shakeBar, m_shakeRead, 2)
        OnShakeStrength(m_shakeBar.Value / 100.0)
    End Sub

    Private Sub OnTiltChanged(sender As Object, e As EventArgs) Handles m_tiltBar.ValueChanged
        UpdateSliderRead(m_tiltBar, m_tiltRead, 0)
        OnMaxTilt(m_tiltBar.Value * std.PI / 180.0)
    End Sub

    ' /********************************************************************************/
    '  the life cycle of the simulation
    ' /********************************************************************************/

    Private Sub StartLoading()
        m_ready = False
        m_loading.Visible = True
        Call m_loading.BringToFront()

        ' everything that the worker needs from the shell has to be read here:
        ' a winforms control may only be touched by the thread that created it
        Dim count As Integer = m_particleCount
        Dim palette As ScalerPalette = CurrentPalette()

        Call Task.Run(
            Sub()
                Try
                    Dim mapper As New SpeedHeatMapper() With {
                        .Palette = palette
                    }
                    Dim sim As New FluidBoxSim(
                        boxSize:=100.0F,
                        fillFraction:=2.0F / 3.0F,
                        particleCount:=count,
                        enableGpu:=True,
                        progress:=Sub(msg, fraction)
                                      Call BeginInvoke(Sub()
                                                           m_loadingText.Text = msg
                                                           m_loadingPct.Text = CInt(fraction * 100).ToString() & "%"
                                                           m_progress = fraction
                                                           Call m_loadingBar.Invalidate()
                                                       End Sub)
                                  End Sub)

                    Call BeginInvoke(Sub() FinishLoading(sim, mapper))
                Catch ex As Exception
                    Try
                        Call IO.File.WriteAllText("fluidbox-init.log", ex.ToString())
                    Catch
                    End Try

                    Call BeginInvoke(Sub()
                                         m_loadingText.Text = "初始化失败：" & ex.Message
                                         m_loadingPct.Text = "详情见 fluidbox-init.log"
                                     End Sub)
                End Try
            End Sub)
    End Sub

    Private Sub FinishLoading(sim As FluidBoxSim, mapper As SpeedHeatMapper)
        m_sim = sim
        m_mapper = mapper

        ' the cloud is packed on its own thread, the ui thread only hands the
        ' finished buffer to the gpu
        If m_packer IsNot Nothing Then Call m_packer.Stop()

        m_packer = New InstancePacker(mapper)
        Call m_packer.Resize(m_renderBudget)

        Call m_canvas.SetHostBudget(m_renderBudget)
        Call m_canvas.Attach(sim, mapper)
        Call m_canvas.AttachPacker(m_packer)
        Call m_packer.Start()

        m_legendName.Text = m_paletteBox.SelectedItem.ToString()

        m_ready = True
        m_loading.Visible = False

        If m_bench Then m_benchClock = Stopwatch.StartNew()

        ' the first cloud is packed by the worker, the idle loop picks it up
        Call m_canvas.InvalidateFrame()

        If m_noPhysics Then
            m_btnRun.Text = "▶  开始"
        Else
            Call m_sim.Start()
            m_btnRun.Text = "⏸  暂停"
        End If

        Call UpdateStatus()
    End Sub

    ''' <summary>
    ''' append one line to the trace log, it is only written when --bench is
    ''' given and exists to find out where a run stops
    ''' </summary>
    Private Shared Sub Trace(message As String)
        Try
            Call IO.File.AppendAllText("fluidbox-trace.log",
                                       $"{DateTime.Now:HH:mm:ss.fff} {message}" & vbCrLf)
        Catch
        End Try
    End Sub

    Private Function CurrentPalette() As ScalerPalette
        Return DirectCast(m_paletteBox.SelectedItem, ScalerPalette)
    End Function

    ' /********************************************************************************/
    '  the animation loop
    ' /********************************************************************************/

    Private Sub OnIdleTick(sender As Object, e As EventArgs) Handles m_clock.Tick
        If Not m_ready OrElse m_canvas Is Nothing Then Return

        Dim elapsed As Long = m_frameClock.ElapsedMilliseconds

        If elapsed < 16 Then Return

        m_frameClock.Restart()

        Call m_canvas.UpdateShake()
        Call m_canvas.PushFrame()

        m_frames += 1

        If m_fpsClock.ElapsedMilliseconds >= 500 Then
            m_fps = m_frames * 1000.0 / m_fpsClock.ElapsedMilliseconds
            m_frames = 0
            m_fpsClock.Restart()
        End If

        ' every label of the status bar repaints itself when its text changes,
        ' which is far too much work for a frame that lasts a few milliseconds
        If m_statusClock.ElapsedMilliseconds >= 200 Then
            m_statusClock.Restart()
            Call UpdateStatus()
        End If

        Call BenchSample()
    End Sub

    ''' <summary>
    ''' collect the frame times of the benchmark run and write them out when the
    ''' requested time is over
    ''' </summary>
    Private Sub BenchSample()
        If Not m_bench OrElse Not m_ready Then Return

        If m_benchClock Is Nothing Then m_benchClock = Stopwatch.StartNew()

        ' without the solver nothing would ever change, so the benchmark of the
        ' renderer asks for a new cloud on every tick: that is exactly what a
        ' drag of the box does
        If m_noPhysics Then Call m_canvas.InvalidateFrame()

        m_benchTicks += 1

        If m_benchTicks = 1 OrElse (m_benchTicks Mod 40) = 0 Then
            Call Trace($"tick {m_benchTicks} drawn={m_canvas.DrawnPoints} budget={m_canvas.RenderBudget} draw={m_canvas.DrawMs:F1} skipped={m_canvas.FrameSkipped}")
        End If

        If Not m_canvas.FrameSkipped Then
            m_benchWork += m_canvas.PackMs + m_canvas.UploadMs + m_canvas.DrawMs
            m_benchDrawSum += m_canvas.DrawMs
            m_benchWorkFrames += 1

            Call Trace($"frame {m_benchWorkFrames} pack={m_canvas.PackMs:F1} up={m_canvas.UploadMs:F1} draw={m_canvas.DrawMs:F1} budget={m_canvas.RenderBudget} pts={m_canvas.DrawnPoints}")

            If m_canvas.DrawMs > m_benchMaxDraw Then
                m_benchMaxDraw = m_canvas.DrawMs
            End If
        End If

        If m_benchClock.Elapsed.TotalSeconds < m_benchSeconds Then Return

        Dim elapsed As Double = m_benchClock.Elapsed.TotalSeconds
        Dim work As Double = If(m_benchWorkFrames > 0, m_benchWork / m_benchWorkFrames, 0)
        Dim log As New System.Text.StringBuilder()

        Call log.AppendLine("FluidBox frame benchmark")
        Call log.AppendLine($"particles    : {m_sim.ParticleCount}")
        Call log.AppendLine($"render budget: {m_renderBudget}")
        Call log.AppendLine($"backend      : {m_sim.BackendName} (gpu={m_sim.IsGpuEnabled})")
        Call log.AppendLine($"gpu pipeline : {m_canvas.IsGpuActive} {m_canvas.GpuFailure}")
        Call log.AppendLine($"seconds      : {elapsed:F1}")
        Call log.AppendLine($"loop rate    : {m_benchTicks / elapsed:F1} /s")
        Call log.AppendLine($"sim steps    : {m_sim.StepCount}")
        Call log.AppendLine($"packs done   : {If(m_packer Is Nothing, -1, m_packer.Generation)}")
        Call log.AppendLine($"requests     : {If(m_packer Is Nothing, -1, m_packer.Requests)}")
        Call log.AppendLine($"updated      : {m_benchWorkFrames}")
        Call log.AppendLine($"skipped      : {m_benchTicks - m_benchWorkFrames}")
        Call log.AppendLine($"work per upd : {work:F2} ms")
        Call log.AppendLine($"pack         : {m_canvas.PackMs:F2} ms")
        Call log.AppendLine($"upload       : {m_canvas.UploadMs:F2} ms")
        Call log.AppendLine($"draw         : {If(m_benchWorkFrames > 0, m_benchDrawSum / m_benchWorkFrames, 0):F2} ms avg / {m_benchMaxDraw:F2} ms max")
        Call log.AppendLine($"physics      : {m_sim.LastStepMs:F0} ms / {m_sim.LastSubSteps} substeps")

        Try
            Call IO.File.WriteAllText("fluidbox-bench.log", log.ToString())
        Catch
        End Try

        m_bench = False
        Call Close()
    End Sub

    Private Sub UpdateStatus()
        If m_sim Is Nothing Then Return

        Dim gpu As Boolean = m_sim.IsGpuEnabled

        m_lstBackend.Text = "后端 " & m_sim.BackendName
        m_lstBackend.ForeColor = If(gpu, Good, Warn)

        If Not gpu AndAlso m_sim.GpuError <> "" Then
            m_lstBackend.Text &= "  (" & m_sim.GpuError & ")"
        End If

        m_lstStep.Text = $"步 {m_sim.StepCount}  子步 {m_sim.LastSubSteps}  物理 {m_sim.LastStepMs:F0} ms"

        m_lstRender.Text = $"渲染 {m_fps:F1} fps  {m_canvas.DrawnPoints.ToString("N0")} 点" &
                           $"  pack {m_canvas.PackMs:F1} / up {m_canvas.UploadMs:F1} / draw {m_canvas.DrawMs:F1} ms"

        Dim hostMB As Double = m_sim.ParticleCount * 18 * 4 / 1024.0 / 1024.0
        Dim gpuMB As Double = m_renderBudget * 32 / 1024.0 / 1024.0

        m_lstMemory.Text = $"粒子 {m_sim.ParticleCount.ToString("N0")}  主机 {hostMB:F0} MB  实例缓冲 {gpuMB:F0} MB"

        m_legendMin.Text = m_mapper.MinSpeed.ToString("F2") & " m/s"
        m_legendMax.Text = m_mapper.MaxSpeed.ToString("F2") & " m/s"

        Dim fail As String = m_canvas.GpuFailure

        m_hud.Text = If(gpu, "CUDA GPU 求解", "CPU 并行求解")

        If m_canvas.FrameSkipped Then
            m_hud.Text &= "  ·  帧已跳过（画面无变化）"
        End If

        If m_canvas.BudgetAdapted Then
            m_hud.Text &= $"  ·  自适应 {m_canvas.RenderBudget.ToString("N0")} 点"
        End If

        If fail <> "" Then
            m_hud.Text &= "  ·  " & fail

            If Not m_gpuLogged Then
                m_gpuLogged = True

                Try
                    Call IO.File.WriteAllText("fluidbox-gpu.log",
                                              fail & vbCrLf & vbCrLf & m_canvas.RendererDescription)
                Catch
                End Try
            End If
        End If

        If m_sim.LastError <> "" Then
            m_hud.Text &= "  ·  " & m_sim.LastError
        End If
    End Sub

    ' /********************************************************************************/
    '  the events of the shell
    ' /********************************************************************************/

    Private Sub OnRunClick(sender As Object, e As EventArgs) Handles m_btnRun.Click
        If m_sim Is Nothing Then Return

        If m_sim.IsRunning Then
            Call m_sim.Stop()
            m_btnRun.Text = "▶  开始"
        Else
            Call m_sim.Start()
            m_btnRun.Text = "⏸  暂停"
        End If
    End Sub

    Private Sub OnStepClick(sender As Object, e As EventArgs) Handles m_btnStep.Click
        If m_sim Is Nothing Then Return

        Call m_sim.Stop()
        m_btnRun.Text = "▶  开始"
        Call m_sim.StepOnce()
        Call m_canvas.PushFrame()
        Call UpdateStatus()
    End Sub

    Private Sub OnResetClick(sender As Object, e As EventArgs) Handles m_btnReset.Click
        If m_sim IsNot Nothing Then Call m_sim.Stop()
        If m_packer IsNot Nothing Then Call m_packer.Stop()

        m_ready = False
        Call StartLoading()
    End Sub

    Private Sub OnFitClick(sender As Object, e As EventArgs) Handles m_btnFit.Click
        If m_canvas Is Nothing Then Return

        Call m_canvas.FitView()
    End Sub

    Private Sub OnPaletteChanged(sender As Object, e As EventArgs) Handles m_paletteBox.SelectedIndexChanged
        If m_mapper Is Nothing Then Return

        m_mapper.Palette = CurrentPalette()
        m_legendName.Text = m_paletteBox.SelectedItem.ToString()

        Call m_canvas.ApplyPalette()
        Call m_legendBar.Invalidate()
    End Sub

    Private Sub OnPointSizeChanged(sender As Object, e As EventArgs) Handles m_sizeBar.ValueChanged
        If m_canvas Is Nothing Then Return

        m_canvas.PointSize = m_sizeBar.Value
        Call m_canvas.InvalidateFrame()
    End Sub

    Private Sub OnGravity(v As Double)
        If m_sim IsNot Nothing Then m_sim.Engine.Gravity = CSng(v)
    End Sub

    Private Sub OnViscosity(v As Double)
        If m_sim IsNot Nothing Then
            m_sim.Engine.ViscosityStrength = CSng(v)
            Call m_sim.Engine.InvalidateParams()
        End If
    End Sub

    Private Sub OnDamping(v As Double)
        If m_sim IsNot Nothing Then m_sim.Engine.CollisionDamping = CSng(v)
    End Sub

    Private Sub OnSoundSpeed(v As Double)
        If m_sim IsNot Nothing Then
            m_sim.Engine.SoundSpeedFactor = CSng(v)
            Call m_sim.Engine.InvalidateParams()
        End If
    End Sub

    Private Sub OnShakeStrength(v As Double)
        If m_canvas IsNot Nothing Then m_canvas.Shake.ShakeStrength = CSng(v)
        If m_sim IsNot Nothing Then m_sim.ShakeStrength = CSng(v)
    End Sub

    Private Sub OnMaxTilt(v As Double)
        If m_canvas IsNot Nothing Then
            m_canvas.Shake.MaxTilt = CSng(v * std.PI / 180.0)
        End If
    End Sub

    Private Sub RelayoutOverlay()
        If m_legend Is Nothing OrElse m_stage Is Nothing Then Return

        m_legend.Location = New Point(m_stage.Width - m_legend.Width - 16,
                                      m_stage.Height - m_legend.Height - 16)
        m_hud.Location = New Point(16, 12)
    End Sub

    Private Sub OnStageResize(sender As Object, e As EventArgs) Handles m_stage.Resize
        Call RelayoutOverlay()
    End Sub

    Private Sub OnToolbarPaint(sender As Object, e As PaintEventArgs) Handles m_toolbar.Paint, m_status.Paint
        Dim host = DirectCast(sender, Panel)

        Using pen As New Pen(Color.FromArgb(45, 212, 191), 1)
            e.Graphics.DrawLine(pen, 0, 0, host.Width, 0)
        End Using
    End Sub

    Private Sub OnLegendPaint(sender As Object, e As PaintEventArgs) Handles m_legend.Paint
        Dim host = DirectCast(sender, Panel)

        Using pen As New Pen(Color.FromArgb(56, 189, 248), 1)
            e.Graphics.DrawRectangle(pen, 0, 0, host.Width - 1, host.Height - 1)
        End Using
    End Sub

    Private Sub OnLegendBarPaint(sender As Object, e As PaintEventArgs) Handles m_legendBar.Paint
        Dim host = DirectCast(sender, Panel)
        Dim colors As Color() = If(m_mapper Is Nothing, Nothing, m_mapper.Colors(256))

        If colors Is Nothing OrElse colors.Length = 0 Then
            e.Graphics.FillRectangle(New SolidBrush(Back2), 0, 0, host.Width, host.Height)
            Return
        End If

        Dim w As Double = host.Width / CDbl(colors.Length)

        For i As Integer = 0 To colors.Length - 1
            Dim x As Integer = CInt(i * w)
            Dim width As Integer = CInt((i + 1) * w) - x

            If width < 1 Then width = 1

            Using brush As New SolidBrush(colors(i))
                e.Graphics.FillRectangle(brush, x, 0, width, host.Height)
            End Using
        Next
    End Sub

    Private Sub OnLoadingPaint(sender As Object, e As PaintEventArgs) Handles m_loading.Paint
        Dim bar = DirectCast(sender, Panel).Controls(0)

        bar.Location = New Point((DirectCast(sender, Panel).Width - bar.Width) \ 2,
                                 (DirectCast(sender, Panel).Height - bar.Height) \ 2)
    End Sub

    Private Sub OnLoadingBarPaint(sender As Object, e As PaintEventArgs) Handles m_loadingBar.Paint
        Dim host = DirectCast(sender, Panel)
        Dim w As Integer = CInt(host.Width * m_progress)

        e.Graphics.SmoothingMode = SmoothingMode.None
        e.Graphics.FillRectangle(New SolidBrush(Back2), 0, 0, host.Width, host.Height)

        Using brush As New LinearGradientBrush(New Point(0, 0), New Point(host.Width, 0), Accent, Accent2)
            e.Graphics.FillRectangle(brush, 0, 0, std.Max(1, w), host.Height)
        End Using
    End Sub

    Private Sub OnLoadingResize(sender As Object, e As EventArgs) Handles m_loading.Resize
        m_loadingStack.Location = New Point((m_loading.Width - m_loadingStack.Width) \ 2,
                                            (m_loading.Height - m_loadingStack.Height) \ 2)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If m_clock IsNot Nothing Then Call m_clock.Stop()
        If m_sim IsNot Nothing Then Call m_sim.Stop()
        If m_packer IsNot Nothing Then Call m_packer.Stop()

        Call MyBase.OnFormClosing(e)
    End Sub
End Class
