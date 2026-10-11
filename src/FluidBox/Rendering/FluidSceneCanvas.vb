Imports System.ComponentModel
Imports FluidBox.Simulation
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Font = Microsoft.VisualBasic.Imaging.Font
Imports IGraphics = Microsoft.VisualBasic.Imaging.IGraphics
Imports Pen = Microsoft.VisualBasic.Imaging.Pen
Imports SolidBrush = Microsoft.VisualBasic.Imaging.SolidBrush
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

        ''' <summary>
        ''' when true the canvas lowers the number of drawn points on its own to
        ''' keep one frame inside of the budget
        ''' </summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property AdaptiveBudget As Boolean = True

        ''' <summary>the frame budget of the adaptive mode in milliseconds</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property FrameBudgetMs As Double = 14.0

        ''' <summary>the smallest budget the adaptive mode falls back to</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property MinBudget As Integer = 200_000

        ''' <summary>has the adaptive mode changed the budget of the host?</summary>
        Public ReadOnly Property BudgetAdapted As Boolean
            Get
                Return m_budgetAdapted
            End Get
        End Property

        ''' <summary>the size of the container along x</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxWidth As Single = 100.0F
        ''' <summary>the size of the container along y</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxDepth As Single = 100.0F
        ''' <summary>
        ''' the size of the container along z, the axis of the gravity
        ''' </summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxHeight As Single = 100.0F

        ''' <summary>the color of the wire frame of the container</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxColor As Color = Color.FromArgb(150, 120, 220, 235)

        ''' <summary>the width in pixels of the overlay wire frame of the box</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BoxLineWidth As Single = 2.5F

        ''' <summary>draw the xyz axis gizmo in the lower left corner</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property ShowAxes As Boolean = True

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
        ''' the smallest change of the tilt that refreshes the ground grid, about
        ''' 0.4 degrees: every refresh raises Scene.Version and therefore drops
        ''' the whole cached geometry of the pipeline
        ''' </summary>
        Private Const LineTiltEpsilon As Single = 0.007F
        ''' <summary>two refreshes of the wire frame are at least this far apart</summary>
        Private Const LineIntervalMs As Long = 50

        Private ReadOnly m_shake As New BoxShakeController()
        Private m_sim As FluidBoxSim
        Private m_mapper As SpeedHeatMapper
        Private m_packer As InstancePacker
        Private m_backend As Direct3D11SceneRenderer
        Private m_lastGeneration As Long = -1
        Private m_drawn As Integer = 0
        Private m_packMs As Double = 0
        Private m_drawMs As Double = 0
        Private m_hx As Single = 50.0F
        Private m_hy As Single = 50.0F
        Private m_hz As Single = 50.0F
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

        Public Shared ReadOnly Property DefaultBackground As Color = Color.FromArgb(237, 250, 254)

        ''' <summary>the font of the labels of the axis gizmo, created once</summary>
        Private ReadOnly m_axisFont As New Font("Consolas", 9, FontStyle.Bold)

        Public Sub New()
            ' the overlay is drawn after the scene handler of the base class:
            ' the base class subscribed its own handler first, so the handlers
            ' run in that order and the overlay lands on top of the frame
            AddHandler Render, AddressOf OnOverlay
        End Sub

        ''' <summary>
        ''' configure the canvas for the given simulation
        ''' </summary>
        Public Sub Attach(sim As FluidBoxSim, mapper As SpeedHeatMapper)
            m_sim = sim
            m_mapper = mapper
            m_backend = TryCast(Renderer, Direct3D11SceneRenderer)

            BoxWidth = sim.BoxWidth
            BoxDepth = sim.BoxDepth
            BoxHeight = sim.BoxHeight
            m_hx = BoxWidth / 2.0F
            m_hy = BoxDepth / 2.0F
            m_hz = BoxHeight / 2.0F
            m_hostBudget = RenderBudget

            BackgroundColor = DefaultBackground
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
        ''' hand the background packer to the canvas
        ''' </summary>
        Public Sub AttachPacker(packer As InstancePacker)
            m_packer = packer
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
            If m_sim Is Nothing OrElse m_packer Is Nothing Then
                m_skipped = True
                Return False
            End If

            Dim steps As Long = m_sim.StepCount
            Dim tiltMoved As Boolean =
                std.Abs(m_shake.TiltX - m_cloudTiltX) > CloudTiltEpsilon OrElse
                std.Abs(m_shake.TiltY - m_cloudTiltY) > CloudTiltEpsilon
            Dim wanted As Boolean = m_dirty OrElse steps <> m_lastStep OrElse tiltMoved

            If wanted Then
                Dim state = m_sim.ReadSnapshot()
                Dim n As Integer = m_sim.ParticleCount

                If state IsNot Nothing AndAlso state.Count > 0 Then n = state.Count

                ' the tilt of the box changes while the user is dragging it, so
                ' the world transform of the cloud has to follow it
                Call m_packer.Request(state, n, m_shake.Rotation, m_hx, m_hy, m_hz)

                m_lastStep = steps
                m_cloudTiltX = m_shake.TiltX
                m_cloudTiltY = m_shake.TiltY
                m_dirty = False
            End If

            Dim data As Single() = Nothing
            Dim count As Integer = 0
            Dim generation As Long = 0

            If Not m_packer.TryTake(data, count, generation) Then
                ' the packer has not finished a cloud yet, ask again next frame
                If wanted Then m_dirty = True

                m_skipped = True
                Return False
            End If

            If generation = m_lastGeneration Then
                ' the gpu already holds exactly this cloud, but the packer is
                ' still working on the one that was asked for: keep the flag so
                ' that the next frame picks it up
                If wanted Then m_dirty = True

                m_skipped = True
                Return False
            End If

            m_lastGeneration = generation
            m_packMs = m_packer.PackMs
            m_skipped = False

            Call AdaptBudget()

            If m_backend IsNot Nothing Then
                Call m_backend.EnsureInstanceCapacity(RenderBudget)
                Call m_backend.UploadInstances(data, count)
            End If

            m_drawn = count

            Call UploadBoxLines(False)
            Call Invalidate()

            Return True
        End Function

        ''' <summary>
        ''' keep one frame inside of <see cref="FrameBudgetMs"/> by lowering the
        ''' number of drawn points, and give the points back when there is room
        ''' again
        ''' </summary>
        ''' <remarks>
        ''' The host sets the budget it wants, the adaptive mode only lowers it
        ''' when the measured frame really is too slow and never raises it above
        ''' the value of the host.
        ''' </remarks>
        Private Sub AdaptBudget()
            If Not AdaptiveBudget Then
                m_budgetAdapted = False
                Return
            End If

            ' a change of the budget reallocates the buffers of the packer, so
            ' it must not happen on every single frame
            If m_adaptClock.ElapsedMilliseconds < 250 Then Return

            m_adaptClock.Restart()

            ' the paint of this frame has not happened yet, so the last one is
            ' the freshest measurement there is
            Dim frame As Double = UploadMs + m_drawMs

            If frame > FrameBudgetMs AndAlso RenderBudget > MinBudget Then
                Dim next1 As Integer = CInt(RenderBudget * 0.75)

                If next1 < MinBudget Then next1 = MinBudget

                RenderBudget = next1
                m_budgetAdapted = True
                m_dirty = True

                If m_packer IsNot Nothing Then Call m_packer.Resize(RenderBudget)
            ElseIf frame < FrameBudgetMs * 0.5 AndAlso m_budgetAdapted Then
                Dim next1 As Integer = CInt(RenderBudget * 1.15)

                If next1 >= m_hostBudget Then
                    RenderBudget = m_hostBudget
                    m_budgetAdapted = False
                Else
                    RenderBudget = next1
                End If

                m_dirty = True

                If m_packer IsNot Nothing Then Call m_packer.Resize(RenderBudget)
            End If
        End Sub

        ''' <summary>
        ''' the budget that the host has asked for, the adaptive mode never
        ''' goes above it
        ''' </summary>
        Public Sub SetHostBudget(budget As Integer)
            m_hostBudget = If(budget < MinBudget, MinBudget, budget)
            RenderBudget = m_hostBudget
            m_budgetAdapted = False
            m_dirty = True
        End Sub

        Private m_hostBudget As Integer = 2_000_000
        Private m_budgetAdapted As Boolean = False
        Private m_adaptClock As Stopwatch = Stopwatch.StartNew()

        ''' <summary>
        ''' measure the whole paint of one frame: the upload of the instance
        ''' buffer, the draw call and the present
        ''' </summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim watch = Stopwatch.StartNew()

            Call MyBase.OnPaint(e)

            watch.Stop()
            m_drawMs = watch.Elapsed.TotalMilliseconds

            ' the instance buffer of this frame has reached the gpu, the packer
            ' may write into it again
            If m_packer IsNot Nothing Then
                Call m_packer.ReleaseTaken()
            End If
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
        ''' the ground grid of the container, rotated by the current tilt and
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

            ' the ground grid in the box local frame, rotated by the tilt of
            ' the box: the wire frame of the box itself is drawn by the screen
            ' space overlay (see OnOverlay) because the direct3d line list is
            ' always one pixel wide
            Call UpdateConnections(BuildGroundGrid(m_shake.Rotation, m_hx, m_hy, m_hz))
        End Sub

        ''' <summary>the transform of the box local frame into the scene frame</summary>
        Private Shared Function Transform(r As Single(),
                                          x As Single, y As Single, z As Single) As Point3D
            Return New Point3D(
                x * r(0) + y * r(1) + z * r(2),
                x * r(3) + y * r(4) + z * r(5),
                x * r(6) + y * r(7) + z * r(8))
        End Function

        ' /********************************************************************************/
        '  the screen space overlay
        ' /********************************************************************************/

        ''' <summary>
        ''' draw the wire frame of the box and the xyz axis gizmo on top of the
        ''' frame that the scene back end has just rendered
        ''' </summary>
        ''' <remarks>
        ''' The direct3d 11 pipeline submits its lines as a line list and the
        ''' hardware draws those one pixel wide, there is no wide line
        ''' primitive. The twelve edges of the box are therefore projected with
        ''' the very same camera formulas of the back end
        ''' (<see cref="TryProjectPoint"/>) and drawn as gdi lines of the width
        ''' of <see cref="BoxLineWidth"/>. The overlay is always on top of the
        ''' point cloud, which is invisible for a wire frame that surrounds the
        ''' cloud anyway.
        ''' </remarks>
        Private Sub OnOverlay(sender As Object, e As DxRenderEventArgs)
            Dim g As IGraphics = e.Graphics

            If g Is Nothing Then Return

            Call DrawBoxWire(g)

            If ShowAxes Then
                Call DrawAxisGizmo(g, e.Size)
            End If
        End Sub

        ''' <summary>
        ''' the twelve edges of the container, rotated by the tilt of the box
        ''' and projected onto the canvas
        ''' </summary>
        Private Sub DrawBoxWire(g As IGraphics)
            Dim r As Single() = m_shake.Rotation
            Dim sx As Single() = {-m_hx, m_hx, m_hx, -m_hx, -m_hx, m_hx, m_hx, -m_hx}
            Dim sy As Single() = {-m_hy, -m_hy, m_hy, m_hy, -m_hy, -m_hy, m_hy, m_hy}
            Dim sz As Single() = {-m_hz, -m_hz, -m_hz, -m_hz, m_hz, m_hz, m_hz, m_hz}
            Dim edges As Integer() = {0, 1, 1, 2, 2, 3, 3, 0,
                                      4, 5, 5, 6, 6, 7, 7, 4,
                                      0, 4, 1, 5, 2, 6, 3, 7}
            Dim screen(7) As PointF

            For i As Integer = 0 To 7
                If Not TryProjectPoint(Transform(r, sx(i), sy(i), sz(i)), screen(i)) Then
                    ' a corner behind the camera: the whole wire frame would be
                    ' nonsense, skip it
                    Return
                End If
            Next

            Using pen As New Pen(Color.FromArgb(200, BoxColor), BoxLineWidth)
                For i As Integer = 0 To 23 Step 2
                    Call g.DrawLine(pen, screen(edges(i)), screen(edges(i + 1)))
                Next
            End Using
        End Sub

        ''' <summary>
        ''' the xyz orientation gizmo in the lower left corner: three fixed
        ''' length lines that point along the world axes as the camera sees
        ''' them, labelled with the name of the axis
        ''' </summary>
        ''' <remarks>
        ''' The direction of each line is the screen space direction of the
        ''' world axis: the origin and a unit step along the axis are projected
        ''' and the difference of the two screen positions gives the direction.
        ''' The gizmo shows the world frame, it only turns when the camera
        ''' turns, never with the shake of the box.
        ''' </remarks>
        Private Sub DrawAxisGizmo(g As IGraphics, size As Size)
            Const length As Single = 46.0F
            Const margin As Single = 32.0F

            ' the worst case tip of an axis is one length away from the anchor
            ' in any direction, the margin keeps it inside of the canvas
            Dim anchor As New PointF(margin + length / 2.0F,
                                     size.Height - margin - length / 2.0F)
            Dim origin As PointF

            If Not TryProjectPoint(New Point3D(0, 0, 0), origin) Then Return

            Dim axes As Point3D() = {
                New Point3D(1, 0, 0),
                New Point3D(0, 1, 0),
                New Point3D(0, 0, 1)}
            Dim names As String() = {"X", "Y", "Z"}
            Dim colors As Color() = {
                Color.FromArgb(235, 235, 87, 87),
                Color.FromArgb(235, 92, 184, 92),
                Color.FromArgb(235, 66, 133, 244)}

            For i As Integer = 0 To 2
                Dim tip As PointF

                If Not TryProjectPoint(axes(i), tip) Then Continue For

                Dim dx As Single = tip.X - origin.X
                Dim dy As Single = tip.Y - origin.Y
                Dim len As Single = CSng(std.Sqrt(dx * dx + dy * dy))

                If len < 0.001F Then Continue For

                dx /= len
                dy /= len

                Dim end1 As New PointF(anchor.X, anchor.Y)
                Dim end2 As New PointF(anchor.X + dx * length,
                                       anchor.Y + dy * length)

                Using pen As New Pen(colors(i), 2.0F)
                    Call g.DrawLine(pen, end1, end2)
                End Using

                Using brush As New SolidBrush(colors(i))
                    Call g.DrawString(names(i), m_axisFont, brush,
                                      end2.X + dx * 8.0F - 4.0F,
                                      end2.Y + dy * 8.0F - 7.0F)
                End Using
            Next
        End Sub

        ''' <summary>
        ''' the grid that marks the ground under the box: parallel lines on the
        ''' plane of the bottom wall, wider than the footprint of the box
        ''' </summary>
        ''' <remarks>
        ''' The grid lives in the box local frame on the z = -hz plane (the z
        ''' axis of the solver is the axis of the gravity) and is rotated by
        ''' the very same tilt as the wire frame, so it follows the shake and
        ''' always shows where the box rests on the ground.
        ''' </summary>
        Private Function BuildGroundGrid(r As Single(),
                                         hx As Single, hy As Single, hz As Single) As List(Of LineSegment)

            Const cells As Integer = 24

            ' a square footprint that is wider than the box itself
            Dim ext As Single = std.Max(hx, hy) * 2.5F
            Dim z As Single = -hz
            Dim ink As Color = Color.FromArgb(70, 120, 135, 150)
            Dim lines As New List(Of LineSegment)(2 * cells + 2)

            For i As Integer = 0 To cells
                Dim t As Single = 2.0F * ext * i / cells - ext

                ' a line that runs along x at y = t
                lines.Add(New LineSegment(Transform(r, t, -ext, z), Transform(r, t, ext, z), ink))
                ' a line that runs along y at x = t
                lines.Add(New LineSegment(Transform(r, -ext, t, z), Transform(r, ext, t, z), ink))
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
