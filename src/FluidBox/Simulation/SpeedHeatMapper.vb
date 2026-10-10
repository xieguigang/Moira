Imports System.Drawing
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
        ''' sample the particles and pack them into the instance buffer
        ''' </summary>
        ''' <param name="state">the live state of the solver</param>
        ''' <param name="count">how many particles of the state are active</param>
        ''' <param name="budget">the largest number of points that may be drawn</param>
        ''' <param name="originX">the x of the center of the box, it is subtracted so that the box sits on the origin of the scene</param>
        ''' <param name="originY">the y of the center of the box</param>
        ''' <param name="originZ">the z of the center of the box</param>
        Public Sub Build(state As SphState3D, count As Integer, budget As Integer,
                         originX As Single, originY As Single, originZ As Single)

            If state Is Nothing OrElse count <= 0 Then
                m_count = 0
                Return
            End If

            If budget < 1 Then budget = 1

            Dim stride As Integer = CInt(std.Ceiling(count / CDbl(budget)))
            If stride < 1 Then stride = 1

            Dim drawn As Integer = (count + stride - 1) \ stride
            If drawn > budget Then drawn = budget

            Call EnsureCapacity(budget)

            Dim px = state.px, py = state.py, pz = state.pz
            Dim vx = state.vx, vy = state.vy, vz = state.vz
            Dim inst = m_instances

            Call UpdateSpeedRange(vx, vy, vz, count)

            Dim lo As Single = m_minSpeed
            Dim span As Single = m_maxSpeed - m_minSpeed
            If span < 0.000001F Then span = 0.000001F
            Dim inv As Single = 1.0F / span

            Dim drawnCount As Integer = drawn
            Dim strideUsed As Integer = stride

            Call Parallel.For(0, drawnCount,
                Sub(k As Integer)
                    Dim i As Integer = k * strideUsed
                    If i >= count Then Return

                    Dim sx As Single = vx(i), sy As Single = vy(i), sz As Single = vz(i)
                    Dim speed As Single = CSng(std.Sqrt(sx * sx + sy * sy + sz * sz))
                    Dim t As Single = (speed - lo) * inv

                    If t < 0.0F Then t = 0.0F
                    If t > 1.0F Then t = 1.0F

                    Dim o As Integer = k * FloatsPerPoint

                    inst(o) = px(i) - originX
                    inst(o + 1) = py(i) - originY
                    inst(o + 2) = pz(i) - originZ
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
