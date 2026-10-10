Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Imaging.Physics
Imports std = System.Math

Namespace Simulation

    ''' <summary>
    ''' Maps the speed of every particle onto the heat map color table of
    ''' <see cref="Designer.FromSchema(ScalerPalette, Integer)"/> and packs the
    ''' result into the per instance buffer of the direct3d point cloud.
    ''' </summary>
    ''' <remarks>
    ''' The color itself never travels to the gpu: the instance carries a single
    ''' heat value in [0,1] and the pixel shader of the scene samples the 256
    ''' level palette texture with it. The very same
    ''' <see cref="Designer.FromSchema(ScalerPalette, Integer)"/> table is what
    ''' the ui paints into the color legend, so the legend and the particles can
    ''' not drift apart.
    ''' </remarks>
    Public Class SpeedHeatMapper

        ''' <summary>one instance is eight singles = 32 bytes</summary>
        Public Const FloatsPerPoint As Integer = 8

        ''' <summary>
        ''' the packing pass is deliberately kept off one core: the solver of the
        ''' box runs on its own thread and must not be starved by the renderer
        ''' </summary>
        Private Shared ReadOnly PackOptions As New ParallelOptions With {
            .MaxDegreeOfParallelism = std.Max(1, Environment.ProcessorCount - 1)
        }

        ''' <summary>the color table that is currently in use</summary>
        Public Property Palette As ScalerPalette = ScalerPalette.Jet

        ''' <summary>the lower end of the speed range of the legend</summary>
        Public ReadOnly Property MinSpeed As Single
            Get
                Return m_minSpeed
            End Get
        End Property

        ''' <summary>the upper end of the speed range of the legend</summary>
        Public ReadOnly Property MaxSpeed As Single
            Get
                Return m_maxSpeed
            End Get
        End Property

        ''' <summary>how many points the last <see cref="Build"/> has produced</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return m_count
            End Get
        End Property

        ''' <summary>the instance data of the last <see cref="Build"/></summary>
        Public ReadOnly Property Instances As Single()
            Get
                Return m_instances
            End Get
        End Property

        ''' <summary>how many particles the last build has skipped per drawn point</summary>
        Public ReadOnly Property Stride As Integer
            Get
                Return m_stride
            End Get
        End Property

        Private m_instances() As Single = New Single(-1) {}
        Private m_capacity As Integer = 0
        Private m_count As Integer = 0
        Private m_stride As Integer = 1
        Private m_minSpeed As Single = 0
        Private m_maxSpeed As Single = 1
        Private m_initialised As Boolean = False

        ''' <summary>
        ''' the color table of the current palette, the same call that the host
        ''' of this mapper uses for the legend
        ''' </summary>
        Public Function Colors(Optional levels As Integer = 256) As Color()
            Return Designer.FromSchema(Palette, levels)
        End Function

        ''' <summary>
        ''' the name of the color scheme of the scene canvas, the gpu palette
        ''' texture of the canvas is built from exactly this name
        ''' </summary>
        Public Function SchemeName() As String
            Return GetSchemeName(Palette)
        End Function

        ''' <summary>
        ''' the color table name of a <see cref="ScalerPalette"/> member, the
        ''' string is what <see cref="Designer.GetColors(String, Integer, Integer)"/>
        ''' and the scene canvas both accept
        ''' </summary>
        Public Shared Function GetSchemeName(palette As ScalerPalette) As String
            Select Case palette
                Case ScalerPalette.Jet : Return "jet"
                Case ScalerPalette.Hot : Return "hot"
                Case ScalerPalette.Cool : Return "cool"
                Case ScalerPalette.Gray : Return "grays"
                Case ScalerPalette.Autumn : Return "autumn"
                Case ScalerPalette.Spring : Return "spring"
                Case ScalerPalette.Summer : Return "summer"
                Case ScalerPalette.Winter : Return "winter"
                Case ScalerPalette.Rainbow : Return "rainbow"
                Case ScalerPalette.Typhoon : Return "typhoon"
                Case ScalerPalette.Icefire : Return "icefire"
                Case ScalerPalette.Seismic : Return "seismic"
                Case ScalerPalette.FlexImaging : Return "fleximaging"
                Case ScalerPalette.viridis : Return "viridis"
                Case ScalerPalette.magma : Return "viridis:magma"
                Case ScalerPalette.inferno : Return "viridis:inferno"
                Case ScalerPalette.plasma : Return "viridis:plasma"
                Case ScalerPalette.cividis : Return "viridis:cividis"
                Case ScalerPalette.turbo : Return "viridis:turbo"
                Case ScalerPalette.mako : Return "viridis:mako"
                Case ScalerPalette.rocket : Return "viridis:rocket"
                Case Else : Return "jet"
            End Select
        End Function

        ''' <summary>
        ''' grow the instance buffer so that it can hold <paramref name="points"/>
        ''' instances
        ''' </summary>
        ''' <remarks>
        ''' The buffer is kept between the frames, a growing animation never
        ''' reallocates it. It is always as large as the render budget because
        ''' the gpu buffer of the renderer is uploaded as a whole.
        ''' </remarks>
        Public Sub EnsureCapacity(points As Integer)
            If points <= 0 Then points = 1

            If m_capacity >= points AndAlso m_instances.Length >= points * FloatsPerPoint Then
                Return
            End If

            m_capacity = points
            m_instances = New Single(points * FloatsPerPoint - 1) {}
        End Sub

        ''' <summary>
        ''' the world transform of the box: the row major 3x3 rotation about the
        ''' center of the box and the center itself
        ''' </summary>
        ''' <param name="rotation">nine elements, row major 3x3</param>
        ''' <param name="originX">the x of the center of the box in the solver space</param>
        Public Sub SetTransform(rotation As Single(),
                                originX As Single, originY As Single, originZ As Single)
            If rotation Is Nothing OrElse rotation.Length < 9 Then
                m_identity = True
                m_r0 = 1 : m_r1 = 0 : m_r2 = 0
                m_r3 = 0 : m_r4 = 1 : m_r5 = 0
                m_r6 = 0 : m_r7 = 0 : m_r8 = 1
            Else
                m_identity = False
                m_r0 = rotation(0) : m_r1 = rotation(1) : m_r2 = rotation(2)
                m_r3 = rotation(3) : m_r4 = rotation(4) : m_r5 = rotation(5)
                m_r6 = rotation(6) : m_r7 = rotation(7) : m_r8 = rotation(8)
            End If

            m_ox = originX
            m_oy = originY
            m_oz = originZ
        End Sub

        Private m_identity As Boolean = True
        Private m_r0 As Single = 1
        Private m_r1 As Single = 0
        Private m_r2 As Single = 0
        Private m_r3 As Single = 0
        Private m_r4 As Single = 1
        Private m_r5 As Single = 0
        Private m_r6 As Single = 0
        Private m_r7 As Single = 0
        Private m_r8 As Single = 1
        Private m_ox As Single = 0
        Private m_oy As Single = 0
        Private m_oz As Single = 0

        ''' <summary>
        ''' sample the particles and pack them into the instance buffer
        ''' </summary>
        ''' <param name="state">the live state of the solver</param>
        ''' <param name="count">how many particles of the state are active</param>
        ''' <param name="budget">the largest number of points that may be drawn</param>
        ''' <summary>
        ''' sample the particles and pack them into the instance buffer
        ''' </summary>
        ''' <param name="state">the live state of the solver</param>
        ''' <param name="count">how many particles of the state are active</param>
        ''' <param name="budget">the largest number of points that may be drawn</param>
        Public Sub Build(state As SphState3D, count As Integer, budget As Integer)
            If budget < 1 Then budget = 1
            If m_instances.Length < budget * FloatsPerPoint Then
                Call EnsureCapacity(budget)
            End If

            Call BuildInto(state, count, budget, m_instances)
        End Sub

        ''' <summary>
        ''' pack the cloud into a buffer that the caller owns
        ''' </summary>
        ''' <remarks>
        ''' The background packer owns its buffers, so the mapper has to be able
        ''' to fill one that it did not allocate itself.
        ''' </remarks>
        Public Sub BuildInto(state As SphState3D, count As Integer, budget As Integer,
                             target As Single())

            If state Is Nothing OrElse count <= 0 OrElse target Is Nothing Then
                m_count = 0
                Return
            End If

            If budget < 1 Then budget = 1

            Dim stride As Integer = CInt(std.Ceiling(count / CDbl(budget)))
            If stride < 1 Then stride = 1

            Dim drawn As Integer = (count + stride - 1) \ stride

            If drawn > budget Then drawn = budget
            If target.Length < drawn * FloatsPerPoint Then
                drawn = target.Length \ FloatsPerPoint
            End If
            If drawn <= 0 Then
                m_count = 0
                Return
            End If

            Dim px = state.px, py = state.py, pz = state.pz
            Dim vx = state.vx, vy = state.vy, vz = state.vz

            Call UpdateSpeedRange(vx, vy, vz, count)

            Dim lo As Single = m_minSpeed
            Dim span As Single = m_maxSpeed - m_minSpeed
            If span < 0.000001F Then span = 0.000001F
            Dim inv As Single = 1.0F / span

            Dim drawnCount As Integer = drawn
            Dim strideUsed As Integer = stride
            Dim rotate As Boolean = Not m_identity
            Dim r0 = m_r0, r1 = m_r1, r2 = m_r2
            Dim r3 = m_r3, r4 = m_r4, r5 = m_r5
            Dim r6 = m_r6, r7 = m_r7, r8 = m_r8
            Dim ox = m_ox, oy = m_oy, oz = m_oz

            Call Parallel.For(0, drawnCount, PackOptions,
                Sub(k As Integer)
                    Dim i As Integer = k * strideUsed
                    If i >= count Then Return

                    Dim x As Single = px(i) - ox
                    Dim y As Single = py(i) - oy
                    Dim z As Single = pz(i) - oz

                    Dim o As Integer = k * FloatsPerPoint

                    If rotate Then
                        inst(o) = x * r0 + y * r1 + z * r2
                        inst(o + 1) = x * r3 + y * r4 + z * r5
                        inst(o + 2) = x * r6 + y * r7 + z * r8
                    Else
                        inst(o) = x
                        inst(o + 1) = y
                        inst(o + 2) = z
                    End If

                    Dim sx As Single = vx(i), sy As Single = vy(i), sz As Single = vz(i)
                    Dim speed As Single = CSng(std.Sqrt(sx * sx + sy * sy + sz * sz))
                    Dim t As Single = (speed - lo) * inv

                    If t < 0.0F Then t = 0.0F
                    If t > 1.0F Then t = 1.0F

                    ' the x of the normal slot is the per point size factor of
                    ' the palette mode of the point shader
                    inst(o + 3) = 1.0F
                    inst(o + 4) = 0.0F
                    inst(o + 5) = 0.0F
                    inst(o + 6) = t
                    inst(o + 7) = 0.0F
                End Sub)

            m_count = drawnCount
            m_stride = strideUsed
        End Sub

        ''' <summary>
        ''' estimate the speed range of the frame from a sample of the cloud and
        ''' smooth it across the frames so that the legend does not flicker
        ''' </summary>
        Private Sub UpdateSpeedRange(vx As Single(), vy As Single(), vz As Single(), count As Integer)
            Dim step1 As Integer = std.Max(1, count \ 65536)
            Dim lo As Single = Single.MaxValue
            Dim hi As Single = 0.0F
            Dim seen As Integer = 0

            Dim i As Integer = 0

            While i < count
                Dim sx As Single = vx(i), sy As Single = vy(i), sz As Single = vz(i)
                Dim speed As Single = CSng(std.Sqrt(sx * sx + sy * sy + sz * sz))

                If speed < lo Then lo = speed
                If speed > hi Then hi = speed

                seen += 1
                i += step1
            End While

            If seen = 0 Then Return

            If hi - lo < 0.05F Then hi = lo + 0.05F

            If Not m_initialised Then
                m_minSpeed = lo
                m_maxSpeed = hi
                m_initialised = True
                Return
            End If

            ' the eye reads a legend that jumps around as noise: the upper end
            ' follows a splash quickly but relaxes slowly
            m_minSpeed = CSng(m_minSpeed * 0.85F + lo * 0.15F)
            m_maxSpeed = CSng(m_maxSpeed * 0.92F + hi * 0.08F)

            If m_maxSpeed - m_minSpeed < 0.05F Then
                m_maxSpeed = m_minSpeed + 0.05F
            End If
        End Sub
    End Class
End Namespace
