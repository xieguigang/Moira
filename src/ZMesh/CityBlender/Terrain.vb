Option Strict On
Option Explicit On

Namespace CityBlender

    ''' <summary>
    ''' Height-field terrain with hills, lakes and a meandering river.
    ''' <para>
    ''' The base relief is fBm value noise shaped with a power curve (flat
    ''' lowlands, pronounced hills) plus a few explicit Gaussian mounds. The
    ''' water level is a percentile of all node heights, so low fBm areas flood
    ''' into lakes. Finally a river is walked from one map edge (meandering,
    ''' pulled towards the centre) and carved into the bed with a smooth
    ''' U-profile — the longest of four candidate walks wins.
    ''' </para>
    ''' </summary>
    Public NotInheritable Class Terrain

        Public ReadOnly MapSize As Double
        Public ReadOnly Cells As Integer
        ''' <summary>Node heights on a (Cells+1)×(Cells+1) grid; node (i,j) is at (i·CellSize, j·CellSize).</summary>
        Public ReadOnly Heights(,) As Double
        ''' <summary>Everything below this level is under water.</summary>
        Public WaterLevel As Double

        ''' <summary>Length in meters of the carved river polyline.</summary>
        Public ReadOnly Property RiverLength As Double
            Get
                Return _riverPoints.Count * RiverStep
            End Get
        End Property

        Private _riverPoints As New List(Of Double())
        Private Const RiverStep As Double = 6.0

        ''' <summary>Side length of one grid cell in meters.</summary>
        Public ReadOnly Property CellSize As Double

        Public Sub New(mapSize As Double, cells As Integer)
            If mapSize <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(mapSize))
            If cells < 8 Then Throw New ArgumentOutOfRangeException(NameOf(cells))
            Me.MapSize = mapSize
            Me.Cells = cells
            CellSize = mapSize / cells
            Heights = New Double(cells, cells) {}
        End Sub

        ''' <summary>Builds the terrain from the given (deterministic) RNG and noise source.</summary>
        ''' <param name="waterPercentile">Fraction of the map that should be below the water level.</param>
        Public Sub Generate(rng As Random, noise As Noise, waterPercentile As Double)
            waterPercentile = MathUtil.Clamp(waterPercentile, 0.02, 0.6)

            ' ---- base relief: shaped fBm, ~5 features across the map ----
            For i = 0 To Cells
                For j = 0 To Cells
                    Dim u = (i * CellSize / MapSize) * 5.0
                    Dim v = (j * CellSize / MapSize) * 5.0
                    Dim h01 = noise.Fbm(u, v, 5)
                    h01 = Math.Pow(h01, 1.7)          ' flatten lowlands, sharpen hills
                    Heights(i, j) = h01 * 58.0
                Next
            Next

            ' ---- explicit hills (gaussian mounds) ----
            Dim hillCount = 4 + rng.Next(3)
            For k = 1 To hillCount
                Dim hx = MapSize * (0.12 + 0.76 * rng.NextDouble())
                Dim hy = MapSize * (0.12 + 0.76 * rng.NextDouble())
                Dim amp = 14.0 + 34.0 * rng.NextDouble()
                Dim sigma = 55.0 + 110.0 * rng.NextDouble()
                Dim twoSigma2 = 2.0 * sigma * sigma
                For i = 0 To Cells
                    Dim dx = i * CellSize - hx
                    For j = 0 To Cells
                        Dim dy = j * CellSize - hy
                        Heights(i, j) += amp * Math.Exp(-(dx * dx + dy * dy) / twoSigma2)
                    Next
                Next
            Next

            ' ---- water level from percentile, lifted to keep beds above zero ----
            WaterLevel = PercentileHeight(waterPercentile)
            If WaterLevel < 4.0 Then
                Dim lift = 4.0 - WaterLevel
                WaterLevel = 4.0
                For i = 0 To Cells
                    For j = 0 To Cells
                        Heights(i, j) += lift
                    Next
                Next
            End If

            For i = 0 To Cells
                For j = 0 To Cells
                    If Heights(i, j) < 0.25 Then Heights(i, j) = 0.25
                Next
            Next

            ' ---- river: walk from all four edges, keep the longest ----
            Dim best As New List(Of Double())
            For side = 0 To 3
                Dim pts = WalkRiver(rng, noise, side)
                If pts.Count > best.Count Then best = pts
            Next
            _riverPoints = best
            CarveRiver(best, noise)

            ' ---- final water level: the river bed adds below-level area, so
            '      re-derive the percentile from the carved field. The drop is
            '      capped at 1 m so the carved bed (3.4 m under the old level)
            '      never runs dry. ----
            Dim finalLevel = PercentileHeight(waterPercentile)
            If finalLevel < WaterLevel - 1.0 Then finalLevel = WaterLevel - 1.0
            WaterLevel = finalLevel
        End Sub

        ''' <summary>Water level such that the given fraction of nodes lies below it.</summary>
        Private Function PercentileHeight(fraction As Double) As Double
            Dim all(Cells * (Cells + 2)) As Double
            Dim n = 0
            For i = 0 To Cells
                For j = 0 To Cells
                    all(n) = Heights(i, j) : n += 1
                Next
            Next
            Array.Sort(all, 0, n)
            Return all(CInt(Math.Floor(MathUtil.Clamp(fraction, 0.02, 0.6) * (n - 1))))
        End Function

        ''' <summary>Bilinear height sample at map coordinates (clamped to the map).</summary>
        Public Function SampleHeight(x As Double, y As Double) As Double
            Dim gx = MathUtil.Clamp(x / CellSize, 0.0, Cells - 0.000001)
            Dim gy = MathUtil.Clamp(y / CellSize, 0.0, Cells - 0.000001)
            Dim i = CInt(Math.Floor(gx))
            Dim j = CInt(Math.Floor(gy))
            Dim fx = gx - i
            Dim fy = gy - j
            Return MathUtil.Lerp(
            MathUtil.Lerp(Heights(i, j), Heights(i + 1, j), fx),
            MathUtil.Lerp(Heights(i, j + 1), Heights(i + 1, j + 1), fx),
            fy)
        End Function

        ''' <summary>True when the terrain at (x, y) is below the water level.</summary>
        Public Function IsWater(x As Double, y As Double) As Boolean
            Return SampleHeight(x, y) < WaterLevel
        End Function

        ''' <summary>Fraction of sampled nodes inside the rectangle that are under water.</summary>
        Public Function WaterFraction(x0 As Double, y0 As Double, x1 As Double, y1 As Double) As Double
            Dim steps = 6
            Dim under = 0
            Dim total = 0
            For a = 0 To steps
                For b = 0 To steps
                    Dim x = MathUtil.Lerp(x0, x1, a / steps)
                    Dim y = MathUtil.Lerp(y0, y1, b / steps)
                    If SampleHeight(x, y) < WaterLevel Then under += 1
                    total += 1
                Next
            Next
            Return under / CDbl(total)
        End Function

        Public ReadOnly Property MaxHeight As Double
            Get
                Dim m = Heights(0, 0)
                For i = 0 To Cells
                    For j = 0 To Cells
                        If Heights(i, j) > m Then m = Heights(i, j)
                    Next
                Next
                Return m
            End Get
        End Property

        Public ReadOnly Property MinHeight As Double
            Get
                Dim m = Heights(0, 0)
                For i = 0 To Cells
                    For j = 0 To Cells
                        If Heights(i, j) < m Then m = Heights(i, j)
                    Next
                Next
                Return m
            End Get
        End Property

        ''' <summary>Number of grid cells whose lowest node corner is under water.</summary>
        Public Function CountWaterCells() As Integer
            Dim count = 0
            For i = 0 To Cells - 1
                For j = 0 To Cells - 1
                    Dim m = Math.Min(Math.Min(Heights(i, j), Heights(i + 1, j)),
                                 Math.Min(Heights(i, j + 1), Heights(i + 1, j + 1)))
                    If m < WaterLevel Then count += 1
                Next
            Next
            Return count
        End Function

        ' =====================================================================
        '  River plumbing
        ' =====================================================================

        ''' <summary>Walks a meandering river polyline from one edge, stopping in a lake or off-map.</summary>
        Private Function WalkRiver(rng As Random, noise As Noise, side As Integer) As List(Of Double())
            Dim pts As New List(Of Double())
            Dim cx = MapSize / 2.0
            Dim cy = MapSize / 2.0

            Dim x As Double, y As Double, heading As Double
            Select Case side
                Case 0 : x = 0.0 : y = MapSize * (0.2 + 0.6 * rng.NextDouble()) : heading = 0.0
                Case 1 : x = MapSize : y = MapSize * (0.2 + 0.6 * rng.NextDouble()) : heading = Math.PI
                Case 2 : y = 0.0 : x = MapSize * (0.2 + 0.6 * rng.NextDouble()) : heading = Math.PI / 2.0
                Case Else : y = MapSize : x = MapSize * (0.2 + 0.6 * rng.NextDouble()) : heading = -Math.PI / 2.0
            End Select

            Dim wander = rng.NextDouble() * 2.0 * Math.PI

            For nsteps = 0 To CInt(MapSize / RiverStep) + 20
                If x < 0 OrElse x > MapSize OrElse y < 0 OrElse y > MapSize Then Exit For
                pts.Add({x, y})

                ' stop once we have merged into a lake (terrain below the water level)
                If pts.Count > 8 AndAlso SampleHeight(x, y) < WaterLevel - 0.5 Then Exit For

                ' meander + gentle pull towards the map centre
                wander += (noise.Sample(nsteps * 0.11, side * 3.7) - 0.5) * 0.9
                Dim toCentre = Math.Atan2(cy - y, cx - x)
                Dim delta = toCentre - heading
                While delta > Math.PI : delta -= 2.0 * Math.PI : End While
                While delta < -Math.PI : delta += 2.0 * Math.PI : End While
                heading += 0.05 * delta + 0.16 * Math.Sin(wander)

                x += RiverStep * Math.Cos(heading)
                y += RiverStep * Math.Sin(heading)
            Next

            Return pts
        End Function

        ''' <summary>Carves the river bed into the height field with a smooth U-profile.</summary>
        Private Sub CarveRiver(pts As List(Of Double()), noise As Noise)
            If pts.Count < 4 Then Return

            For Each p In pts
                Dim x = p(0), y = p(1)
                Dim w = 13.0 + 8.0 * noise.Sample(x / 90.0, y / 90.0)     ' half width, 13..21 m
                Dim reach = w * 1.5 + 2.0

                Dim iMin = Math.Max(0, CInt(Math.Floor((x - reach) / CellSize)))
                Dim iMax = Math.Min(Cells, CInt(Math.Ceiling((x + reach) / CellSize)))
                Dim jMin = Math.Max(0, CInt(Math.Floor((y - reach) / CellSize)))
                Dim jMax = Math.Min(Cells, CInt(Math.Ceiling((y + reach) / CellSize)))

                For i = iMin To iMax
                    For j = jMin To jMax
                        Dim dx = i * CellSize - x
                        Dim dy = j * CellSize - y
                        Dim d = Math.Sqrt(dx * dx + dy * dy)
                        Dim t = d / w
                        If t < 1.5 Then
                            Dim f = MathUtil.Clamp((1.5 - t) / 1.5, 0.0, 1.0)
                            Dim target = WaterLevel - 3.4 * MathUtil.SmoothStep(f)
                            If target < 0.25 Then target = 0.25
                            If target < Heights(i, j) Then Heights(i, j) = target
                        End If
                    Next
                Next
            Next
        End Sub

    End Class
End Namespace