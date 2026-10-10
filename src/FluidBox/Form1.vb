Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Diagnostics
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports FluidBox.Rendering
Imports FluidBox.Simulation
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

    Private m_canvas As FluidSceneCanvas
    Private m_sim As FluidBoxSim
    Private m_mapper As SpeedHeatMapper

    Private m_toolbar As Panel
    Private m_body As Panel
    Private m_stage As Panel
    Private m_params As Panel
    Private m_status As Panel
    Private m_legend As Panel
    Private m_legendBar As Panel
    Private m_legendMin As Label
    Private m_legendMax As Label
    Private m_legendName As Label
    Private m_hud As Label
    Private m_loading As Panel
    Private m_loadingBar As Panel
    Private m_loadingText As Label
    Private m_loadingPct As Label

    Private m_btnRun As Button
    Private m_btnStep As Button
    Private m_btnReset As Button
    Private m_btnFit As Button
    Private m_countBox As ComboBox
    Private m_budgetBox As ComboBox
    Private m_paletteBox As ComboBox
    Private m_sizeBar As TrackBar

    Private m_lstBackend As Label
    Private m_lstStep As Label
    Private m_lstRender As Label
    Private m_lstMemory As Label

    Private m_progress As Double = 0
    Private m_ready As Boolean = False
    Private m_frameClock As Stopwatch = Stopwatch.StartNew()
    Private m_frames As Integer = 0
    Private m_fps As Double = 0
    Private m_fpsClock As Stopwatch = Stopwatch.StartNew()
    Private m_particleCount As Integer = 10_000_000
    Private m_renderBudget As Integer = 2_000_000

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
        Call BuildShell()
        Call StartLoading()

        ' the animation is driven whenever the message queue runs dry, the
        ' handler throttles itself to the refresh rate of the display
        AddHandler Application.Idle, AddressOf OnIdleTick
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
            End If
        Next

        If m_particleCount < 10_000 Then m_particleCount = 10_000
        If m_renderBudget < 10_000 Then m_renderBudget = 10_000

        m_renderBudget = std.Min(m_renderBudget, m_particleCount)
    End Sub

    Private Sub BuildShell()
        BackColor = Back0
        ForeColor = Text0
        Font = UiFont(9.0F)
        Text = "Fluid Box · 3D SPH 液体盒子模拟器"
        StartPosition = FormStartPosition.CenterScreen
        WindowState = FormWindowState.Maximized

        ' ---- the toolbar ----
        m_toolbar = New Panel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .BackColor = Back1,
            .Padding = New Padding(12, 0, 12, 0)
        }
        AddHandler m_toolbar.Paint, AddressOf OnToolbarPaint

        m_btnRun = MakeButton("▶  开始", 108)
        m_btnStep = MakeButton("单步", 78)
        m_btnReset = MakeButton("重置", 78)
        m_btnFit = MakeButton("视角复位", 96)

        AddHandler m_btnRun.Click, AddressOf OnRunClick
        AddHandler m_btnStep.Click, AddressOf OnStepClick
        AddHandler m_btnReset.Click, AddressOf OnResetClick
        AddHandler m_btnFit.Click, AddressOf OnFitClick

        m_countBox = MakeCombo("粒子数", New String() {"100 万", "200 万", "500 万", "1000 万"}, 3)
        m_budgetBox = MakeCombo("渲染点数", New String() {"50 万", "100 万", "200 万", "500 万", "1000 万"}, 2)
        m_paletteBox = MakeCombo("色表", New String() {
            "Jet", "viridis", "turbo", "inferno", "magma", "plasma", "Hot", "Cool", "Seismic", "Icefire"}, 0)

        AddHandler m_countBox.SelectedIndexChanged, AddressOf OnCountChanged
        AddHandler m_budgetBox.SelectedIndexChanged, AddressOf OnBudgetChanged
        AddHandler m_paletteBox.SelectedIndexChanged, AddressOf OnPaletteChanged

        m_sizeBar = New TrackBar With {
            .Minimum = 1, .Maximum = 6, .Value = 2,
            .Width = 96, .Height = 28,
            .BackColor = Back1
        }
        AddHandler m_sizeBar.ValueChanged, AddressOf OnPointSizeChanged

        Call LayoutToolbar()

        ' ---- the viewport ----
        m_body = New Panel With {.Dock = DockStyle.Fill, .BackColor = Back0}
        m_params = New Panel With {
            .Dock = DockStyle.Right,
            .Width = 272,
            .BackColor = Back1,
            .Padding = New Padding(14, 12, 14, 12),
            .AutoScroll = True
        }
        m_stage = New Panel With {.Dock = DockStyle.Fill, .BackColor = Back0}

        m_canvas = New FluidSceneCanvas() With {
            .Dock = DockStyle.Fill,
            .BackColor = Back0,
            .Margin = New Padding(0)
        }

        m_legend = New Panel With {
            .Width = 236, .Height = 96,
            .BackColor = Back1,
            .Padding = New Padding(12, 10, 12, 8)
        }
        AddHandler m_legend.Paint, AddressOf OnLegendPaint

        m_legendBar = New Panel With {.Height = 16, .Dock = DockStyle.Top}
        AddHandler m_legendBar.Paint, AddressOf OnLegendBarPaint

        m_legendMin = New Label With {
            .Dock = DockStyle.Top, .Height = 18,
            .ForeColor = Text1, .Font = UiFont(8.5F),
            .Text = "0.00 m/s"
        }
        m_legendMax = New Label With {
            .Dock = DockStyle.Top, .Height = 18,
            .ForeColor = Text0, .Font = UiFont(8.5F, True),
            .Text = "0.00 m/s", .TextAlign = ContentAlignment.TopRight
        }
        m_legendName = New Label With {
            .Dock = DockStyle.Bottom, .Height = 18,
            .ForeColor = Accent, .Font = UiFont(8.5F),
            .Text = "Jet"
        }

        m_hud = New Label With {
            .Width = 360, .Height = 22,
            .ForeColor = Text1,
            .Font = UiFont(8.5F),
            .BackColor = Back1,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Padding = New Padding(8, 0, 0, 0),
            .Text = ""
        }

        Call m_legend.Controls.Add(m_legendName)
        Call m_legend.Controls.Add(m_legendMax)
        Call m_legend.Controls.Add(m_legendMin)
        Call m_legend.Controls.Add(m_legendBar)

        Call m_stage.Controls.Add(m_canvas)
        Call m_stage.Controls.Add(m_hud)
        Call m_stage.Controls.Add(m_legend)
        Call m_legend.BringToFront()

        ' ---- the status bar ----
        m_status = New Panel With {.Dock = DockStyle.Bottom, .Height = 30, .BackColor = Back1}
        AddHandler m_status.Paint, AddressOf OnToolbarPaint

        m_lstBackend = MakeStatusLabel(300)
        m_lstStep = MakeStatusLabel(260)
        m_lstRender = MakeStatusLabel(280)
        m_lstMemory = MakeStatusLabel(320)

        m_lstBackend.Dock = DockStyle.Left
        m_lstStep.Dock = DockStyle.Left
        m_lstRender.Dock = DockStyle.Left
        m_lstMemory.Dock = DockStyle.Fill

        Call m_status.Controls.Add(m_lstMemory)
        Call m_status.Controls.Add(m_lstRender)
        Call m_status.Controls.Add(m_lstStep)
        Call m_status.Controls.Add(m_lstBackend)

        Call m_body.Controls.Add(m_stage)
        Call m_body.Controls.Add(m_params)
        Call Controls.Add(m_body)
        Call Controls.Add(m_status)
        Call Controls.Add(m_toolbar)

        Call BuildParams()
        Call BuildLoading()

        AddHandler m_stage.Resize, Sub(s, ev) RelayoutOverlay()
        Call RelayoutOverlay()
    End Sub

    Private Sub LayoutToolbar()
        m_toolbar.Controls.Clear()

        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Back1,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Padding = New Padding(0, 11, 0, 0)
        }

        Call flow.Controls.Add(m_btnRun)
        Call flow.Controls.Add(MakeGap(8))
        Call flow.Controls.Add(m_btnStep)
        Call flow.Controls.Add(MakeGap(8))
        Call flow.Controls.Add(m_btnReset)
        Call flow.Controls.Add(MakeGap(8))
        Call flow.Controls.Add(m_btnFit)
        Call flow.Controls.Add(MakeGap(20))
        Call flow.Controls.Add(MakeCaption("粒子数"))
        Call flow.Controls.Add(m_countBox)
        Call flow.Controls.Add(MakeGap(14))
        Call flow.Controls.Add(MakeCaption("渲染点数"))
        Call flow.Controls.Add(m_budgetBox)
        Call flow.Controls.Add(MakeGap(14))
        Call flow.Controls.Add(MakeCaption("色表"))
        Call flow.Controls.Add(m_paletteBox)
        Call flow.Controls.Add(MakeGap(14))
        Call flow.Controls.Add(MakeCaption("点大小"))
        Call flow.Controls.Add(m_sizeBar)

        Call m_toolbar.Controls.Add(flow)
    End Sub

    Private Function MakeGap(w As Integer) As Control
        Return New Panel With {.Width = w, .Height = 1, .BackColor = Back1}
    End Function

    Private Function MakeCaption(text As String) As Control
        Return New Label With {
            .Text = text,
            .AutoSize = True,
            .ForeColor = Text1,
            .Font = UiFont(9.0F),
            .Margin = New Padding(0, 5, 0, 0),
            .Padding = New Padding(0)
        }
    End Function

    Private Function MakeButton(text As String, width As Integer) As Button
        Dim btn As New Button With {
            .Text = text,
            .Width = width,
            .Height = 28,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Back2,
            .ForeColor = Text0,
            .Font = UiFont(9.0F, True),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(0)
        }

        btn.FlatAppearance.BorderSize = 1
        btn.FlatAppearance.BorderColor = Accent
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59)
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(45, 212, 191)

        Return btn
    End Function

    Private Function MakeCombo(caption As String, items As String(), selected As Integer) As ComboBox
        Dim box As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Width = If(items.Length > 4, 118, 96),
            .Height = 26,
            .BackColor = Back2,
            .ForeColor = Text0,
            .Font = UiFont(9.0F),
            .FlatStyle = FlatStyle.Flat,
            .Margin = New Padding(0, 0, 0, 0)
        }

        Call box.Items.AddRange(items.Cast(Of Object).ToArray())
        box.SelectedIndex = selected

        Return box
    End Function

    Private Function MakeStatusLabel(width As Integer) As Label
        Return New Label With {
            .Width = width,
            .ForeColor = Text1,
            .Font = New Font("Consolas", 8.5F),
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoSize = False,
            .Padding = New Padding(12, 0, 0, 0)
        }
    End Function

    ' /********************************************************************************/
    '  the parameter drawer
    ' /********************************************************************************/

    Private Sub BuildParams()
        Dim title As New Label With {
            .Text = "物理参数",
            .Dock = DockStyle.Top,
            .Height = 26,
            .ForeColor = Text0,
            .Font = UiFont(11.0F, True)
        }

        Call m_params.Controls.Add(title)

        Call AddSlider("重力 (m/s²)", 0, 300, 98, 2, AddressOf OnGravity)
        Call AddSlider("粘性强度", 0, 100, 30, 1, AddressOf OnViscosity)
        Call AddSlider("碰撞阻尼", 0, 100, 40, 2, AddressOf OnDamping)
        Call AddSlider("声速因子", 10, 100, 30, 1, AddressOf OnSoundSpeed)
        Call AddSlider("晃动强度", 0, 300, 100, 2, AddressOf OnShakeStrength)
        Call AddSlider("最大倾角 (°)", 3, 60, 26, 0, AddressOf OnMaxTilt)

        Dim hint As New Label With {
            .Dock = DockStyle.Top,
            .Height = 78,
            .ForeColor = Text1,
            .Font = UiFont(8.0F),
            .Text = "左键拖拽 = 晃动盒子（惯性 + 倾斜）" & vbCrLf &
                    "右键拖拽 = 环绕视角" & vbCrLf &
                    "滚轮 = 缩放" & vbCrLf &
                    "物理在后台线程推进，画面始终显示最新一帧。"
        }

        Call m_params.Controls.Add(hint)
        Call m_params.Controls.Add(New Panel With {.Dock = DockStyle.Top, .Height = 14})
    End Sub

    Private Sub AddSlider(caption As String, min As Integer, max As Integer,
                          value As Integer, scale As Integer, handler As Action(Of Double))
        Dim host As New Panel With {.Dock = DockStyle.Top, .Height = 52, .BackColor = Back1}

        Dim name As New Label With {
            .Text = caption,
            .Dock = DockStyle.Top,
            .Height = 18,
            .ForeColor = Text1,
            .Font = UiFont(8.5F)
        }
        Dim read As New Label With {
            .Text = "",
            .Dock = DockStyle.Right,
            .Width = 58,
            .Height = 18,
            .ForeColor = Accent,
            .Font = UiFont(8.5F, True),
            .TextAlign = ContentAlignment.MiddleRight
        }
        Dim bar As New TrackBar With {
            .Minimum = min, .Maximum = max, .Value = value,
            .Dock = DockStyle.Top, .Height = 30,
            .BackColor = Back1
        }

        Dim update As Action =
            Sub()
                Dim v As Double = bar.Value / std.Pow(10, scale)
                read.Text = v.ToString("F" & scale.ToString())
                Call handler(v)
            End Sub

        bar.Tag = update
        AddHandler bar.ValueChanged, Sub(s, ev) DirectCast(bar.Tag, Action).Invoke()

        Call host.Controls.Add(bar)
        Call host.Controls.Add(read)
        Call host.Controls.Add(name)
        Call m_params.Controls.Add(host)
        Call host.BringToFront()

        Call update()
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

    ' /********************************************************************************/
    '  the loading overlay
    ' /********************************************************************************/

    Private Sub BuildLoading()
        m_loading = New Panel With {.BackColor = Back0, .Dock = DockStyle.Fill}
        AddHandler m_loading.Paint, AddressOf OnLoadingPaint

        m_loadingText = New Label With {
            .Text = "正在生成液体盒子 …",
            .AutoSize = False,
            .Width = 520, .Height = 24,
            .ForeColor = Text0,
            .Font = UiFont(12.0F, True),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        m_loadingPct = New Label With {
            .Text = "0%",
            .AutoSize = False,
            .Width = 520, .Height = 20,
            .ForeColor = Accent,
            .Font = UiFont(9.5F),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        m_loadingBar = New Panel With {.Width = 420, .Height = 8, .BackColor = Back2}
        AddHandler m_loadingBar.Paint, AddressOf OnLoadingBarPaint

        Dim stack As New Panel With {.Width = 560, .Height = 120}

        m_loadingText.Location = New Point(20, 8)
        m_loadingBar.Location = New Point(70, 48)
        m_loadingPct.Location = New Point(20, 66)

        Call stack.Controls.Add(m_loadingPct)
        Call stack.Controls.Add(m_loadingBar)
        Call stack.Controls.Add(m_loadingText)

        AddHandler m_loading.Resize, Sub(s, ev)
                                         stack.Location = New Point(
                                             (m_loading.Width - stack.Width) \ 2,
                                             (m_loading.Height - stack.Height) \ 2)
                                     End Sub

        Call m_loading.Controls.Add(stack)
        Call Controls.Add(m_loading)
        Call m_loading.BringToFront()
    End Sub

    Private Sub OnLoadingPaint(sender As Object, e As PaintEventArgs)
        Dim bar = DirectCast(sender, Panel).Controls(0)

        bar.Location = New Point((DirectCast(sender, Panel).Width - bar.Width) \ 2,
                                 (DirectCast(sender, Panel).Height - bar.Height) \ 2)
    End Sub

    Private Sub OnLoadingBarPaint(sender As Object, e As PaintEventArgs)
        Dim host = DirectCast(sender, Panel)
        Dim w As Integer = CInt(host.Width * m_progress)

        e.Graphics.SmoothingMode = SmoothingMode.None
        e.Graphics.FillRectangle(New SolidBrush(Back2), 0, 0, host.Width, host.Height)

        Using brush As New LinearGradientBrush(New Point(0, 0), New Point(host.Width, 0), Accent, Accent2)
            e.Graphics.FillRectangle(brush, 0, 0, std.Max(1, w), host.Height)
        End Using
    End Sub

    ' /********************************************************************************/
    '  the life cycle of the simulation
    ' /********************************************************************************/

    Private Sub StartLoading()
        m_ready = False
        m_loading.Visible = True
        Call m_loading.BringToFront()

        Dim count As Integer = m_particleCount

        Call Task.Run(
            Sub()
                Try
                    Dim mapper As New SpeedHeatMapper() With {
                        .Palette = CurrentPalette()
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
                    Call BeginInvoke(Sub()
                                         m_loadingText.Text = "初始化失败：" & ex.Message
                                         m_loadingPct.Text = ""
                                     End Sub)
                End Try
            End Sub)
    End Sub

    Private Sub FinishLoading(sim As FluidBoxSim, mapper As SpeedHeatMapper)
        m_sim = sim
        m_mapper = mapper

        Call mapper.SetTransform(m_canvas.Shake.Rotation,
                                 sim.BoxSize / 2.0F, sim.BoxSize / 2.0F, sim.BoxSize / 2.0F)
        mapper.EnsureCapacity(m_renderBudget)

        m_canvas.RenderBudget = m_renderBudget
        Call m_canvas.Attach(sim, mapper)

        m_legendName.Text = m_paletteBox.SelectedItem.ToString()

        m_ready = True
        m_loading.Visible = False

        Call m_canvas.PushFrame()
        Call m_sim.Start()
        Call UpdateStatus()

        m_btnRun.Text = "⏸  暂停"
    End Sub

    Private Function CurrentPalette() As ScalerPalette
        Select Case m_paletteBox.SelectedItem.ToString()
            Case "viridis" : Return ScalerPalette.viridis
            Case "turbo" : Return ScalerPalette.turbo
            Case "inferno" : Return ScalerPalette.inferno
            Case "magma" : Return ScalerPalette.magma
            Case "plasma" : Return ScalerPalette.plasma
            Case "Hot" : Return ScalerPalette.Hot
            Case "Cool" : Return ScalerPalette.Cool
            Case "Seismic" : Return ScalerPalette.Seismic
            Case "Icefire" : Return ScalerPalette.Icefire
            Case Else : Return ScalerPalette.Jet
        End Select
    End Function

    ' /********************************************************************************/
    '  the animation loop
    ' /********************************************************************************/

    Private Sub OnIdleTick(sender As Object, e As EventArgs)
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

        Call UpdateStatus()
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

        m_lstRender.Text = $"渲染 {m_fps:F1} fps  {m_canvas.DrawnPoints.ToString("N0")} 点  {m_canvas.UploadMs:F0} ms"

        Dim hostMB As Double = m_sim.ParticleCount * 18 * 4 / 1024.0 / 1024.0
        Dim gpuMB As Double = m_renderBudget * 32 / 1024.0 / 1024.0

        m_lstMemory.Text = $"粒子 {m_sim.ParticleCount.ToString("N0")}  主机 {hostMB:F0} MB  实例缓冲 {gpuMB:F0} MB"

        m_legendMin.Text = m_mapper.MinSpeed.ToString("F2") & " m/s"
        m_legendMax.Text = m_mapper.MaxSpeed.ToString("F2") & " m/s"

        Dim fail As String = m_canvas.GpuFailure

        m_hud.Text = If(gpu, "CUDA GPU 求解", "CPU 并行求解") &
                     If(fail = "", "", "  ·  " & fail)

        If m_sim.LastError <> "" Then
            m_hud.Text &= "  ·  " & m_sim.LastError
        End If
    End Sub

    ' /********************************************************************************/
    '  the events of the shell
    ' /********************************************************************************/

    Private Sub OnRunClick(sender As Object, e As EventArgs)
        If m_sim Is Nothing Then Return

        If m_sim.IsRunning Then
            Call m_sim.Stop()
            m_btnRun.Text = "▶  开始"
        Else
            Call m_sim.Start()
            m_btnRun.Text = "⏸  暂停"
        End If
    End Sub

    Private Sub OnStepClick(sender As Object, e As EventArgs)
        If m_sim Is Nothing Then Return

        Call m_sim.Stop()
        m_btnRun.Text = "▶  开始"
        Call m_sim.StepOnce()
        Call m_canvas.PushFrame()
        Call UpdateStatus()
    End Sub

    Private Sub OnResetClick(sender As Object, e As EventArgs)
        If m_sim IsNot Nothing Then Call m_sim.Stop()

        m_ready = False
        Call StartLoading()
    End Sub

    Private Sub OnFitClick(sender As Object, e As EventArgs)
        If m_canvas Is Nothing Then Return

        Call m_canvas.FitView()
    End Sub

    Private Sub OnCountChanged(sender As Object, e As EventArgs)
        Select Case m_countBox.SelectedItem.ToString()
            Case "100 万" : m_particleCount = 1_000_000
            Case "200 万" : m_particleCount = 2_000_000
            Case "500 万" : m_particleCount = 5_000_000
            Case Else : m_particleCount = 10_000_000
        End Select
    End Sub

    Private Sub OnBudgetChanged(sender As Object, e As EventArgs)
        Select Case m_budgetBox.SelectedItem.ToString()
            Case "50 万" : m_renderBudget = 500_000
            Case "100 万" : m_renderBudget = 1_000_000
            Case "200 万" : m_renderBudget = 2_000_000
            Case "500 万" : m_renderBudget = 5_000_000
            Case Else : m_renderBudget = 10_000_000
        End Select

        If Not m_ready OrElse m_mapper Is Nothing Then Return

        Call m_mapper.EnsureCapacity(m_renderBudget)
        m_canvas.RenderBudget = m_renderBudget
    End Sub

    Private Sub OnPaletteChanged(sender As Object, e As EventArgs)
        If m_mapper Is Nothing Then Return

        m_mapper.Palette = CurrentPalette()
        m_legendName.Text = m_paletteBox.SelectedItem.ToString()

        Call m_canvas.ApplyPalette()
        Call m_legendBar.Invalidate()
    End Sub

    Private Sub OnPointSizeChanged(sender As Object, e As EventArgs)
        If m_canvas Is Nothing Then Return

        m_canvas.PointSize = m_sizeBar.Value
    End Sub

    Private Sub RelayoutOverlay()
        If m_legend Is Nothing OrElse m_stage Is Nothing Then Return

        m_legend.Location = New Point(m_stage.Width - m_legend.Width - 16,
                                      m_stage.Height - m_legend.Height - 16)
        m_hud.Location = New Point(16, 12)
    End Sub

    Private Sub OnToolbarPaint(sender As Object, e As PaintEventArgs)
        Dim host = DirectCast(sender, Panel)

        Using pen As New Pen(Color.FromArgb(45, 212, 191), 1)
            e.Graphics.DrawLine(pen, 0, 0, host.Width, 0)
        End Using
    End Sub

    Private Sub OnLegendPaint(sender As Object, e As PaintEventArgs)
        Dim host = DirectCast(sender, Panel)

        Using pen As New Pen(Color.FromArgb(56, 189, 248), 1)
            e.Graphics.DrawRectangle(pen, 0, 0, host.Width - 1, host.Height - 1)
        End Using
    End Sub

    Private Sub OnLegendBarPaint(sender As Object, e As PaintEventArgs)
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

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If m_sim IsNot Nothing Then Call m_sim.Stop()
        Call MyBase.OnFormClosing(e)
    End Sub
End Class
