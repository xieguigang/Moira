Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Drawing
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports FluidBox.Simulation
Imports std = System.Math

Namespace Rendering

    ''' <summary>
    ''' The 3d view of the fluid box: a wire frame container plus the point
    ''' cloud of the particles.
    ''' </summary>
    ''' <remarks>
    ''' The mouse is taken over completely:
    ''' <list type="bullet">
    ''' <item>the left button shakes the box (it never orbits the camera),</item>
    ''' <item>the right button orbits the camera through the very same orbit
    ''' controller that the base class uses for the left button,</item>
    ''' <item>the wheel zooms.</item>
    ''' </list>
    '''
    ''' The particles are not part of the <see cref="Scene"/>: they are uploaded
    ''' into the external instance buffer of the direct3d back end once per
    ''' frame. Putting them into the scene instead would raise
    ''' <see cref="Scene.Version"/> on every frame and drop the whole cached
    ''' geometry of the pipeline.
    ''' </remarks>
    Public Class FluidSceneCanvas : Inherits DxScene3DCanvas

        ''' <summary>the largest number of points that one frame may draw</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property RenderBudget As Integer = 2_000_000

        ''' <summary>the edge length of the container</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxSize As Single = 100.0F

        ''' <summary>the color of the wire frame of the container</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxColor As Color = Color.FromArgb(150, 120, 220, 235)

        ''' <summary>the controller of the shake of the box</summary>
        Public ReadOnly Property Shake As BoxShakeController
            Get
                Return m_shake
            End Get
        End Property

        ''' <summary>how many points the last frame has uploaded</summary>
        Public ReadOnly Property DrawnPoints As Integer
            Get
                Return m_drawn
            End Get
        End Property

        ''' <summary>
        ''' the cpu time of the last rebuild of the instance data in milliseconds
        ''' </summary>
        Public ReadOnly Property PackMs As Double
            Get
                Return m_packMs
            End Get
        End Property

        ''' <summary>
        ''' the time the gpu back end spent on the upload of the instance buffer
        ''' in milliseconds, zero when the back end does not report it
        ''' </summary>
        Public ReadOnly Property UploadMs As Double
            Get
                Return If(m_backend Is Nothing, 0.0, m_backend.LastUploadMs)
            End Get
        End Property

        ''' <summary>
        ''' the whole paint of the last frame: the upload, the draw call and the
        ''' present
        ''' </summary>
        Public ReadOnly Property DrawMs As Double
            Get
                Return m_drawMs
            End Get
        End Property

        ''' <summary>
        ''' true while the last call of <see cref="PushFrame"/> had nothing to
        ''' do: the gpu still holds exactly the cloud that is on the screen
        ''' </summary>
        Public ReadOnly Property FrameSkipped As Boolean
            Get
                Return m_skipped
            End Get
        End Property

        ''' <summary>is the direct3d point pipeline in use?</summary>
        Public ReadOnly Property IsGpuActive As Boolean
            Get
                Return IsGpuPipelineActive
            End Get
        End Property

        ''' <summary>why the gpu pipeline is not in use, empty when it is</summary>
        Public ReadOnly Property GpuFailure As String
            Get
                Dim gpu = TryCast(Renderer, Direct3D11SceneRenderer)

                If gpu Is Nothing Then Return "the direct3d back end is not installed"
                If gpu.IsGpuAvailable Then Return ""

                Return gpu.LastError
            End Get
        End Property

        ''' <summary>
        ''' the smallest change of the tilt that makes the cloud worth a rebuild,
        ''' about 0.05 degrees
        ''' </summary>
        Private Const CloudTiltEpsilon As Single = 0.0008F
        ''' <summary>
        ''' the smallest change of the tilt that refreshes the wire frame, about
        ''' 0.4 degrees: every refresh raises Scene.Version and therefore drops
        ''' the whole cached geometry of the pipeline
        ''' </summary>
        Private Const LineTiltEpsilon As Single = 0.007F
        ''' <summary>two refreshes of the wire frame are at least this far apart</summary>
        Private Const LineIntervalMs As Long = 50

        Private ReadOnly m_shake As New BoxShakeController()
        Private m_sim As FluidBoxSim
        Private m_mapper As SpeedHeatMapper
        Private m_backend As Direct3D11SceneRenderer
        Private m_drawn As Integer = 0
        Private m_packMs As Double = 0
        Private m_drawMs As Double = 0
        Private m_half As Single = 50.0F
        Private m_shaking As Boolean = False
        Private m_orbiting As Boolean = False

        ' the tilt that the cloud on the gpu was built with
        Private m_cloudTiltX As Single = Single.MinValue
        Private m_cloudTiltY As Single = Single.MinValue
        ' the tilt that the wire frame of the scene was built with
        Private m_tiltX As Single = Single.MinValue
        Private m_tiltY As Single = Single.MinValue
        Private m_lineClock As Stopwatch = Stopwatch.StartNew()
        Private m_lastFrame As Stopwatch = Stopwatch.StartNew()

        ' the dirty flag of the frame: the solver step that is on the gpu and
        ' a flag that the host raises when a setting has changed
        Private m_lastStep As Long = -1
        Private m_dirty As Boolean = True
        Private m_skipped As Boolean = False

        ''' <summary>
        ''' configure the canvas for the given simulation
        ''' </summary>
        Public Sub Attach(sim As FluidBoxSim, mapper As SpeedHeatMapper)
            m_sim = sim
            m_mapper = mapper
            m_backend = TryCast(Renderer, Direct3D11SceneRenderer)

            BoxSize = sim.BoxSize
            m_half = sim.BoxSize / 2.0F

            BackgroundColor = Color.FromArgb(11, 15, 20)
            RenderMode = SceneRenderMode.PointCloud
            PointShape = ScenePointShape.Square
            PointSize = 2
            PointAlpha = 235
            MultisampleCount = 1
            ShowGround = False
            ShowConnections = True
            ShowDebugOverlay = False
            EnableKeyboardShortcuts = False
            VSync = False

            ' the palette texture of the gpu is built from this name, it is the
            ' same table that the mapper gives to the legend
            ColorScheme = mapper.SchemeName()

            Call UploadBoxLines(True)
            Call FitView()
        End Sub

        ''' <summary>
        ''' the palette has been changed by the user
        ''' </summary>
        Public Sub ApplyPalette()
            If m_mapper Is Nothing Then Return

            ColorScheme = m_mapper.SchemeName()
            m_dirty = True
            Call Invalidate()
        End Sub

        ''' <summary>
        ''' tell the canvas that something the cloud depends on has changed (the
        ''' palette, the render budget, the point size), the next
        ''' <see cref="PushFrame"/> then rebuilds the cloud even when the solver
        ''' and the tilt did not move
        ''' </summary>
        Public Sub InvalidateFrame()
            m_dirty = True
        End Sub

        ''' <summary>
        ''' rebuild the point cloud of the current snapshot and push it onto the
        ''' gpu, then request a repaint
        ''' </summary>
        ''' <remarks>
        ''' The solver needs seconds for one step while the display refreshes
        ''' sixty times per second, so the vast majority of the frames would
        ''' pack and upload exactly the same cloud again. The frame is therefore
        ''' skipped unless the solver has stepped, the tilt has moved or the
        ''' host has raised <see cref="InvalidateFrame"/>.
        ''' </remarks>
        ''' <returns>true when the cloud has been rebuilt, false when the frame
        ''' was skipped because nothing has changed</returns>
        Public Function PushFrame() As Boolean
            If m_sim Is Nothing OrElse m_mapper Is Nothing Then
                m_skipped = True
                Return False
            End If

            Dim steps As Long = m_sim.StepCount
            Dim tiltMoved As Boolean =
                std.Abs(m_shake.TiltX - m_cloudTiltX) > CloudTiltEpsilon OrElse
                std.Abs(m_shake.TiltY - m_cloudTiltY) > CloudTiltEpsilon

            If Not m_dirty AndAlso steps = m_lastStep AndAlso Not tiltMoved Then
                ' the gpu still holds exactly the cloud that is on the screen
                m_skipped = True
                Return False
            End If

            m_lastStep = steps
            m_dirty = False
            m_cloudTiltX = m_shake.TiltX
            m_cloudTiltY = m_shake.TiltY
            m_skipped = False

            Dim state = m_sim.ReadSnapshot()
            Dim n As Integer = m_sim.ParticleCount

            If state IsNot Nothing AndAlso state.Count > 0 Then n = state.Count

            Dim pack = Stopwatch.StartNew()

            ' the tilt of the box changes while the user is dragging it, so the
            ' world transform of the cloud has to be refreshed with it
            Call m_mapper.SetTransform(m_shake.Rotation, m_half, m_half, m_half)
            Call m_mapper.Build(state, n, RenderBudget)

            pack.Stop()
            m_packMs = pack.Elapsed.TotalMilliseconds

            If m_backend IsNot Nothing Then
                Call m_backend.EnsureInstanceCapacity(RenderBudget)
                Call m_backend.UploadInstances(m_mapper.Instances, m_mapper.Count)
            End If

            m_drawn = m_mapper.Count

            Call UploadBoxLines(False)
            Call Invalidate()

            Return True
        End Function

        ''' <summary>
        ''' measure the whole paint of one frame: the upload of the instance
        ''' buffer, the draw call and the present
        ''' </summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim watch = Stopwatch.StartNew()

            Call MyBase.OnPaint(e)

            watch.Stop()
            m_drawMs = watch.Elapsed.TotalMilliseconds
        End Sub

        ''' <summary>
        ''' advance the shake and apply it to the solver
        ''' </summary>
        Public Sub UpdateShake()
            Dim now As Long = m_lastFrame.ElapsedMilliseconds
            Dim dt As Single = now / 1000.0F

            m_lastFrame.Restart()
            If dt > 0.1F Then dt = 0.1F

            Call m_shake.Update(dt)

            If m_sim IsNot Nothing Then
                Call m_sim.ApplyShake(m_shake.Acceleration, m_shake.GravityDirection)
            End If
        End Sub

        ''' <summary>
        ''' the twelve edges of the container, rotated by the current tilt and
        ''' centered on the origin of the scene
        ''' </summary>
        ''' <remarks>
        ''' Every refresh calls <c>Scene.LoadLineSegments</c>, which raises
        ''' <c>Scene.Version</c>; the cache key of
        ''' <c>D3D11ScenePipeline.GeometryOf</c> contains that version, so a
        ''' refresh drops the whole cached geometry (the palette texture and
        ''' every small buffer of the pipeline) and rebuilds it. While the user
        ''' is dragging that would happen sixty times per second, so a refresh
        ''' needs a visible change of the tilt and a minimum interval.
        ''' </remarks>
        Private Sub UploadBoxLines(force As Boolean)
            If Not force Then
                If std.Abs(m_shake.TiltX - m_tiltX) < LineTiltEpsilon AndAlso
                   std.Abs(m_shake.TiltY - m_tiltY) < LineTiltEpsilon Then

                    Return
                End If

                If m_lineClock.ElapsedMilliseconds < LineIntervalMs Then
                    Return
                End If
            End If

            m_tiltX = m_shake.TiltX
            m_tiltY = m_shake.TiltY
            m_lineClock.Restart()

            Call UpdateConnections(BuildBoxEdges(m_shake.Rotation, BoxSize / 2.0F))
        End Sub

        ''' <summary>
        ''' the eight corners of the container rotated by the tilt of the box
        ''' </summary>
        Private Function BuildBoxEdges(r As Single(), half As Single) As List(Of LineSegment)
            Dim corners(7) As Point3D
            Dim sx As Single() = {-half, half, half, -half, -half, half, half, -half}
            Dim sy As Single() = {-half, -half, half, half, -half, -half, half, half}
            Dim sz As Single() = {-half, -half, -half, -half, half, half, half, half}

            For i As Integer = 0 To 7
                Dim x As Single = sx(i), y As Single = sy(i), z As Single = sz(i)

                corners(i) = New Point3D(
                    x * r(0) + y * r(1) + z * r(2),
                    x * r(3) + y * r(4) + z * r(5),
                    x * r(6) + y * r(7) + z * r(8))
            Next

            Dim edges As Integer() = {0, 1, 1, 2, 2, 3, 3, 0,
                                      4, 5, 5, 6, 6, 7, 7, 4,
                                      0, 4, 1, 5, 2, 6, 3, 7}
            Dim lines As New List(Of LineSegment)(12)

            For i As Integer = 0 To 23 Step 2
                lines.Add(New LineSegment(corners(edges(i)), corners(edges(i + 1)), BoxColor))
            Next

            Return lines
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            Select Case e.Button
                Case MouseButtons.Left
                    m_shaking = True
                    m_orbiting = False
                    Cursor = Cursors.SizeAll
                    Call m_shake.BeginDrag(e.X, e.Y)
                    Call Focus()

                Case MouseButtons.Right
                    m_orbiting = True
                    m_shaking = False
                    Cursor = Cursors.NoMove2D
                    ' the orbit controller rotates on the left button, the right
                    ' button is the pan of the base class and is free here
                    Call Controller.MouseDown(SceneMouseButton.Left, e.X, e.Y)
                    Call Focus()

                Case Else
                    Call MyBase.OnMouseDown(e)
            End Select
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            If m_shaking Then
                Call m_shake.Drag(e.X, e.Y)
            ElseIf m_orbiting Then
                Call Controller.MouseMove(e.X, e.Y)
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            Select Case e.Button
                Case MouseButtons.Left
                    If m_shaking Then
                        m_shaking = False
                        Cursor = Cursors.Default
                        Call m_shake.EndDrag()
                    End If

                Case MouseButtons.Right
                    If m_orbiting Then
                        m_orbiting = False
                        Cursor = Cursors.Default
                        Call Controller.MouseUp(SceneMouseButton.Left)
                    End If

                Case Else
                    Call MyBase.OnMouseUp(e)
            End Select
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            Call Controller.MouseWheel(e.Delta)
        End Sub
    End Class
End Namespace
