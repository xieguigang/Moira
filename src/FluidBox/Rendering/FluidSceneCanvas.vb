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

        ''' <summary>the wall clock time of the last upload in milliseconds</summary>
        Public ReadOnly Property UploadMs As Double
            Get
                Return m_uploadMs
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

        Private ReadOnly m_shake As New BoxShakeController()
        Private m_sim As FluidBoxSim
        Private m_mapper As SpeedHeatMapper
        Private m_backend As Direct3D11SceneRenderer
        Private m_drawn As Integer = 0
        Private m_uploadMs As Double = 0
        Private m_shaking As Boolean = False
        Private m_orbiting As Boolean = False
        Private m_tiltX As Single = Single.MinValue
        Private m_tiltY As Single = Single.MinValue
        Private m_lastFrame As Stopwatch = Stopwatch.StartNew()

        ''' <summary>
        ''' configure the canvas for the given simulation
        ''' </summary>
        Public Sub Attach(sim As FluidBoxSim, mapper As SpeedHeatMapper)
            m_sim = sim
            m_mapper = mapper
            m_backend = TryCast(Renderer, Direct3D11SceneRenderer)

            BoxSize = sim.BoxSize

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
        End Sub

        ''' <summary>
        ''' the palette has been changed by the user
        ''' </summary>
        Public Sub ApplyPalette()
            If m_mapper Is Nothing Then Return

            ColorScheme = m_mapper.SchemeName()
            Call Invalidate()
        End Sub

        ''' <summary>
        ''' rebuild the point cloud of the current snapshot and push it onto the
        ''' gpu, then request a repaint
        ''' </summary>
        Public Sub PushFrame()
            If m_sim Is Nothing OrElse m_mapper Is Nothing Then Return

            Dim watch = Stopwatch.StartNew()
            Dim state = m_sim.ReadSnapshot()
            Dim n As Integer = m_sim.ParticleCount

            If state IsNot Nothing AndAlso state.Count > 0 Then n = state.Count

            Call m_mapper.Build(state, n, RenderBudget)

            If m_backend IsNot Nothing Then
                Call m_backend.EnsureInstanceCapacity(RenderBudget)
                Call m_backend.UploadInstances(m_mapper.Instances, m_mapper.Count)
            End If

            m_drawn = m_mapper.Count

            Call UploadBoxLines(False)

            watch.Stop()
            m_uploadMs = watch.Elapsed.TotalMilliseconds

            Call Invalidate()
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
        Private Sub UploadBoxLines(force As Boolean)
            If Not force AndAlso
               std.Abs(m_shake.TiltX - m_tiltX) < 0.0015F AndAlso
               std.Abs(m_shake.TiltY - m_tiltY) < 0.0015F Then

                Return
            End If

            m_tiltX = m_shake.TiltX
            m_tiltY = m_shake.TiltY

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
