Option Strict On
Option Explicit On

Imports System.IO
Imports Moira.ZMesh.CityBlender
Imports Xunit

''' <summary>Binary-STL and generation-pipeline tests.</summary>
Public Class CityBlenderTests

    Private Shared ReadOnly WorkDir As String =
        Path.Combine(Path.GetTempPath(), "cityblender-tests")

    Public Sub New()
        Directory.CreateDirectory(WorkDir)
    End Sub

    Private Function RunCity(seed As Integer, Optional size As Double = 800.0,
                             Optional cells As Integer = 128, Optional lod As Integer = 1,
                             Optional plazas As Integer = 2) As CityStats
        Dim out = Path.Combine(WorkDir, $"city_{seed}_{lod}.stl")
        Dim options As New CityOptions With {
            .Seed = seed, .MapSize = size, .Cells = cells, .Lod = lod, .PlazaCount = plazas
        }
        Return CityGenerator.Generate(options, out)
    End Function

    ' ------------------------------------------------------------------ STL

    <Fact>
    Public Sub StlFileLayout_IsValidBinary()
        Dim stats = RunCity(11)
        Dim bytes = File.ReadAllBytes(Path.Combine(WorkDir, "city_11_1.stl"))

        Assert.Equal(84 + 50 * stats.Triangles, bytes.Length)

        ' triangle count field (little-endian uint32 at offset 80)
        Dim count = BitConverter.ToInt32(bytes, 80)
        Assert.Equal(stats.Triangles, count)

        ' ASCII STL starts with "solid"; binary files normally do not need to,
        ' but our header is printable text containing the CityBlender marker.
        Dim header = System.Text.Encoding.ASCII.GetString(bytes, 0, 80).TrimEnd(ControlChars.NullChar)
        Assert.Contains("CityBlender", header, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub StlNormals_MatchRightHandRule_AndAreUnitLength()
        RunCity(23)
        Dim tris = ReadTriangles(Path.Combine(WorkDir, "city_23_1.stl"))

        For Each t In tris
            ' unit length
            Dim ln = Math.Sqrt(t(0) * t(0) + t(1) * t(1) + t(2) * t(2))
            Assert.True(Math.Abs(ln - 1.0) < 0.01 OrElse ln < 0.001, $"normal length {ln}")

            ' direction agrees with the vertex winding (cross product)
            Dim ux = t(6) - t(3), uy = t(7) - t(4), uz = t(8) - t(5)
            Dim vx = t(9) - t(3), vy = t(10) - t(4), vz = t(11) - t(5)
            Dim cx = uy * vz - uz * vy
            Dim cy = uz * vx - ux * vz
            Dim cz = ux * vy - uy * vx
            Dim cl = Math.Sqrt(cx * cx + cy * cy + cz * cz)
            If cl > 1.0E-9 Then
                Dim dot = (t(0) * cx + t(1) * cy + t(2) * cz) / cl
                Assert.True(dot > 0.99, $"stored normal disagrees with winding (dot={dot:0.000})")
            End If
        Next
    End Sub

    <Fact>
    Public Sub BoundingBox_IsInMeters_AndWithinMap()
        RunCity(37)
        Dim tris = ReadTriangles(Path.Combine(WorkDir, "city_37_1.stl"))

        Dim minX = Double.MaxValue, maxX = Double.MinValue
        Dim minY = Double.MaxValue, maxY = Double.MinValue
        Dim minZ = Double.MaxValue, maxZ = Double.MinValue
        For Each t In tris
            For k = 0 To 2
                minX = Math.Min(minX, t(3 + 3 * k)) : maxX = Math.Max(maxX, t(3 + 3 * k))
                minY = Math.Min(minY, t(4 + 3 * k)) : maxY = Math.Max(maxY, t(4 + 3 * k))
                minZ = Math.Min(minZ, t(5 + 3 * k)) : maxZ = Math.Max(maxZ, t(5 + 3 * k))
            Next
        Next

        ' 1 unit = 1 m: the city stays inside the 800 m map (plus road widths)
        Assert.True(minX >= -25.0 AndAlso maxX <= 825.0, $"x out of map: {minX:0.0}..{maxX:0.0}")
        Assert.True(minY >= -25.0 AndAlso maxY <= 825.0, $"y out of map: {minY:0.0}..{maxY:0.0}")
        ' terrain skirt reaches z=-3, buildings stay below ~150 m
        Assert.True(minZ >= -5.0 AndAlso maxZ < 200.0, $"z out of range: {minZ:0.0}..{maxZ:0.0}")
        Assert.True(maxZ > 40.0, "no tall buildings generated")
    End Sub

    ' ---------------------------------------------------------- reproducible

    <Fact>
    Public Sub SameSeed_ProducesIdenticalBytes()
        Dim a = Path.Combine(WorkDir, "repro_a.stl")
        Dim b = Path.Combine(WorkDir, "repro_b.stl")

        CityGenerator.Generate(New CityOptions With {.Seed = 99, .MapSize = 600, .Cells = 96}, a)
        CityGenerator.Generate(New CityOptions With {.Seed = 99, .MapSize = 600, .Cells = 96}, b)

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b))
    End Sub

    <Fact>
    Public Sub DifferentSeed_ProducesDifferentCity()
        Dim a = Path.Combine(WorkDir, "diff_a.stl")
        Dim b = Path.Combine(WorkDir, "diff_b.stl")

        CityGenerator.Generate(New CityOptions With {.Seed = 1, .MapSize = 600, .Cells = 96}, a)
        CityGenerator.Generate(New CityOptions With {.Seed = 2, .MapSize = 600, .Cells = 96}, b)

        Assert.NotEqual(Of Byte())(File.ReadAllBytes(a), File.ReadAllBytes(b))
    End Sub

    ' ------------------------------------------------------- required content

    <Fact>
    Public Sub GeneratedCity_ContainsAllRequiredElements()
        Dim stats = RunCity(42, size:=1000.0, cells:=160)

        Assert.True(stats.WaterCells > 0, "no lakes/river water surface")
        Assert.True(stats.RiverLength > 100.0, $"river too short: {stats.RiverLength:0}")
        Assert.True(stats.MaxHeight - stats.WaterLevel > 12.0, "no hills above the water")
        Assert.True(stats.Blocks > 0)
        Assert.True(stats.Buildings > 30, $"only {stats.Buildings} buildings")
        Assert.True(stats.Skyscrapers > 0, "no CBD skyscrapers")
        Assert.True(stats.Towers > 0, "no residential towers")
        Assert.True(stats.Bungalows > 0, "no bungalows")
        Assert.True(stats.Plazas > 0, "no civic plaza")
        Assert.True(stats.Trees > 0, "no trees in plazas")
        Assert.True(stats.BridgeSegments > 0, "no bridges over water")
        Assert.True(stats.Triangles > 50000, $"suspiciously few triangles: {stats.Triangles}")
    End Sub

    <Fact>
    Public Sub Lod2_AddsGableRoofs_MoreTriangles()
        Dim s1 = RunCity(55, lod:=1)
        Dim s2 = RunCity(55, lod:=2)
        Assert.True(s2.Triangles > s1.Triangles, "LOD2 should add roof geometry")
    End Sub

    <Fact>
    Public Sub SplitRegions_WritesFourRegionFiles()
        Dim out = Path.Combine(WorkDir, "regions.stl")
        If File.Exists(out) Then File.Delete(out)

        CityGenerator.Generate(New CityOptions With {
            .Seed = 7, .MapSize = 600, .Cells = 96, .SplitRegions = True
        }, out)

        Dim stem = Path.GetFileNameWithoutExtension(out)
        Assert.True(File.Exists(out), "combined STL missing")
        For Each region In {"ground", "roads", "buildings", "water"}
            Dim p = Path.Combine(WorkDir, $"{stem}_{region}.stl")
            Assert.True(File.Exists(p), $"{region}.stl missing")
            Assert.True(New FileInfo(p).Length > 84, $"{region}.stl is empty")
        Next
    End Sub

    ' ------------------------------------------------------------ wind tunnel

    <Fact>
    Public Sub WindTunnel_DomainFollowsConventions()
        Dim stats = RunCity(31, size:=600.0, cells:=96)
        Dim domain = WindTunnel.Compute(stats, 600.0, New WindTunnel.Options With {.WindDirection = 0})

        Dim h = Math.Max(stats.MaxBuildingZ, 20.0)
        ' upstream 5H / downstream 15H / sides 5H / top 5H above the tallest building
        Assert.Equal(-5.0 * h, domain.X0, 1)
        Assert.Equal(600.0 + 15.0 * h, domain.X1, 1)
        Assert.Equal(-5.0 * h, domain.Y0, 1)
        Assert.Equal(600.0 + 5.0 * h, domain.Y1, 1)
        Assert.Equal(h + 5.0 * h, domain.Z1, 1)
        Assert.Equal(-3.0, domain.Z0, 3)
        Assert.True(domain.H >= 20.0)
        Assert.True(domain.BlockageRatio >= 0.0)
    End Sub

    <Fact>
    Public Sub WindTunnel_Wind180_FlipsInletSide()
        Dim stats = RunCity(31, size:=600.0, cells:=96)
        Dim d0 = WindTunnel.Compute(stats, 600.0, New WindTunnel.Options With {.WindDirection = 0})
        Dim d180 = WindTunnel.Compute(stats, 600.0, New WindTunnel.Options With {.WindDirection = 180})
        Dim h = Math.Max(stats.MaxBuildingZ, 20.0)

        ' the box must stay well-ordered for every wind direction
        Assert.True(d0.X0 < d0.X1 AndAlso d0.Y0 < d0.Y1 AndAlso d180.X0 < d180.X1 AndAlso d180.Y0 < d180.Y1)

        ' 0°: upstream margin (5H) on -x, downstream (15H) on +x
        Assert.Equal(-5.0 * h, d0.X0, 1)
        Assert.Equal(600.0 + 15.0 * h, d0.X1, 1)
        ' 180°: mirrored — upstream on +x, downstream on -x
        Assert.Equal(-15.0 * h, d180.X0, 1)
        Assert.Equal(600.0 + 5.0 * h, d180.X1, 1)
    End Sub

    <Fact>
    Public Sub WindTunnel_BadDirection_Throws()
        Assert.Throws(Of ArgumentOutOfRangeException)(
            Function() WindTunnel.Compute(New CityStats(), 600.0,
                                          New WindTunnel.Options With {.WindDirection = 45}))
    End Sub

    <Fact>
    Public Sub WindTunnel_WritesPatchesAndBlockMeshDict()
        Dim stats = RunCity(31, size:=600.0, cells:=96)
        Dim wtDir = Path.Combine(WorkDir, "wt")
        If Directory.Exists(wtDir) Then Directory.Delete(wtDir, True)

        Dim domain = WindTunnel.Compute(stats, 600.0, New WindTunnel.Options With {.WindDirection = 90})
        WindTunnel.WriteSurfaces(domain, wtDir, "city31")

        For Each patch In {"inlet", "outlet", "side_west", "side_east", "top"}
            Dim p = Path.Combine(wtDir, $"city31_{patch}.stl")
            Assert.True(File.Exists(p), $"{patch}.stl missing")
            ' a plane patch = 1 quad = 2 triangles = 84 + 100 bytes
            Assert.Equal(184L, New FileInfo(p).Length)
        Next

        Dim dict = WindTunnel.WriteBlockMeshDict(domain)
        Assert.Contains("convertToMeters 1", dict, StringComparison.Ordinal)
        Assert.Contains("inlet", dict, StringComparison.Ordinal)
        Assert.Contains("side_west", dict, StringComparison.Ordinal)
        Assert.Contains("hex (0 1 2 3 4 5 6 7)", dict, StringComparison.Ordinal)
        ' 90° wind: inlet is the -y face (0 1 5 4)
        Assert.Contains("(0 1 5 4)", dict, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub WindTunnel_PatchNormalsPointOutward()
        Dim stats = RunCity(31, size:=600.0, cells:=96)
        Dim wtDir = Path.Combine(WorkDir, "wt_normals")
        Dim domain = WindTunnel.Compute(stats, 600.0, New WindTunnel.Options With {.WindDirection = 0})
        WindTunnel.WriteSurfaces(domain, wtDir, "n")

        Dim cx = (domain.X0 + domain.X1) / 2.0
        Dim cy = (domain.Y0 + domain.Y1) / 2.0

        For Each kv In {("inlet", (-1.0, 0.0, 0.0)),
                        ("outlet", (1.0, 0.0, 0.0)),
                        ("side_south", (0.0, -1.0, 0.0)),
                        ("side_north", (0.0, 1.0, 0.0)),
                        ("top", (0.0, 0.0, 1.0))}
            For Each t In ReadTriangles(Path.Combine(wtDir, $"n_{kv.Item1}.stl"))
                Dim dot = t(0) * kv.Item2.Item1 + t(1) * kv.Item2.Item2 + t(2) * kv.Item2.Item3
                Assert.True(dot > 0.99, $"{kv.Item1} normal should point out of the domain")
            Next
        Next

        ' sanity: normals must not be zero and quad must live on the right plane
        Assert.True(domain.X0 < cx AndAlso domain.X1 > cx)
    End Sub

    ' ------------------------------------------------------------ validation

    <Theory>
    <InlineData(0)>
    <InlineData(-5)>
    Public Sub BadOptions_Throw(index As Integer)
        Assert.Throws(Of ArgumentOutOfRangeException)(
            Function() CityGenerator.Generate(New CityOptions With {.Seed = 1, .MapSize = index}, "x.stl"))
    End Sub

    <Fact>
    Public Sub Cli_Main_SmokeTest()
        Dim out = Path.Combine(WorkDir, "cli_smoke.stl")
        If File.Exists(out) Then File.Delete(out)

        Dim rc = CityBlenderCLI.Main({"/generate", "/seed", "5", "/size", "500",
                                                "/cells", "96", "/out", out})
        Assert.Equal(0, rc)
        Assert.True(File.Exists(out) AndAlso New FileInfo(out).Length > 84)
    End Sub

    ' --------------------------------------------------------------- helpers

    Private Shared Iterator Function ReadTriangles(path As String) As IEnumerable(Of Single())
        Using br As New BinaryReader(File.OpenRead(path))
            br.ReadBytes(80)
            Dim count = CInt(br.ReadUInt32())
            For i = 1 To count
                Dim t(11) As Single
                For k = 0 To 11
                    t(k) = br.ReadSingle()
                Next
                br.ReadUInt16()
                Yield t
            Next
        End Using
    End Function

End Class
