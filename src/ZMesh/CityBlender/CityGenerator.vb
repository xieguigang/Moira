Option Strict On
Option Explicit On

''' <summary>Input parameters for one city generation run.</summary>
Public NotInheritable Class CityOptions

    ''' <summary>Random seed — identical seeds regenerate an identical city.</summary>
    Public Property Seed As Integer = 42

    ''' <summary>Map side length in meters (STL units are locked to 1 unit = 1 m).</summary>
    Public Property MapSize As Double = 1000.0

    ''' <summary>Terrain height-field resolution (cells per side).</summary>
    Public Property Cells As Integer = 192

    ''' <summary>Target fraction of the terrain below the water level (lakes/river).</summary>
    Public Property WaterPercentile As Double = 0.12

    ''' <summary>Recursion depth of the road-network split.</summary>
    Public Property RoadDepth As Integer = 4

    ''' <summary>Minimum block size before splitting stops (m).</summary>
    Public Property MinBlock As Double = 110.0

    ''' <summary>Level of detail: 1 = flat roofs (CFD default), 2 = gable roofs on bungalows.</summary>
    Public Property Lod As Integer = 1

    ''' <summary>Plant trees in plazas.</summary>
    Public Property Trees As Boolean = True

    ''' <summary>Number of civic plazas ("市民广场").</summary>
    Public Property PlazaCount As Integer = 2

    ''' <summary>Write one STL per region (ground/roads/buildings/water) in addition to the combined file.</summary>
    Public Property SplitRegions As Boolean = False

    Public Sub Validate()
        If MapSize < 200 OrElse MapSize > 5000 Then
            Throw New ArgumentOutOfRangeException(NameOf(MapSize), "map size must be within [200, 5000] m")
        End If
        If Cells < 64 OrElse Cells > 512 Then
            Throw New ArgumentOutOfRangeException(NameOf(Cells), "cells must be within [64, 512]")
        End If
        If RoadDepth < 0 OrElse RoadDepth > 5 Then
            Throw New ArgumentOutOfRangeException(NameOf(RoadDepth), "road depth must be within [0, 5]")
        End If
        If Lod <> 1 AndAlso Lod <> 2 Then
            Throw New ArgumentOutOfRangeException(NameOf(Lod), "lod must be 1 or 2")
        End If
    End Sub

End Class

''' <summary>Summary statistics of one generated city.</summary>
Public NotInheritable Class CityStats

    Public Seed As Integer
    Public MapSize As Double
    Public Triangles As Integer
    Public Blocks, BuiltBlocks As Integer
    Public Buildings, Skyscrapers, Towers, Bungalows As Integer
    Public Plazas, Trees As Integer
    Public BridgeSegments As Integer
    Public WaterCells As Integer
    Public RiverLength As Double
    Public MinHeight, MaxHeight, WaterLevel As Double

    ''' <summary>Highest absolute building-top Z in the city (wind-tunnel reference H).</summary>
    Public MaxBuildingZ As Double
    ''' <summary>Sum of building frontal areas for wind blowing along +X / +Y (conservative, overlaps counted).</summary>
    Public FrontalX, FrontalY As Double

    Public Overrides Function ToString() As String
        Return $"CityBlender report" & vbCrLf &
               $"  seed            : {Seed}" & vbCrLf &
               $"  map             : {MapSize:0} m x {MapSize:0} m" & vbCrLf &
               $"  terrain         : {MinHeight:0.0} .. {MaxHeight:0.0} m (water level {WaterLevel:0.0} m)" & vbCrLf &
               $"  water cells     : {WaterCells}" & vbCrLf &
               $"  river length    : {RiverLength:0} m" & vbCrLf &
               $"  blocks          : {BuiltBlocks} built / {Blocks} total" & vbCrLf &
               $"  buildings       : {Buildings} (skyscrapers {Skyscrapers}, towers {Towers}, bungalows {Bungalows})" & vbCrLf &
               $"  plazas / trees  : {Plazas} / {Trees}" & vbCrLf &
               $"  bridge segments : {BridgeSegments}" & vbCrLf &
               $"  triangles       : {Triangles}"
    End Function

End Class

''' <summary>
''' The CityBlender facade: procedural random city generation for wind-tunnel
''' CFD, following the pipeline
''' <para>road-network split → per-block Poisson-disk footprints → height-map
''' gradient → building extrusion (watertight boxes, meters, binary STL).</para>
''' </summary>
Public NotInheritable Class CityGenerator

    Private Sub New()
    End Sub

    Private Const FoundationSink As Double = 1.2
    Private Const RoadLift As Double = 0.18
    Private Const BridgeClearance As Double = 2.5

    ''' <summary>Generates a city and writes the (combined, and optionally per-region) binary STL file(s).</summary>
    ''' <returns>Statistics of the generated city.</returns>
    Public Shared Function Generate(options As CityOptions, outputPath As String) As CityStats
        If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
        If String.IsNullOrWhiteSpace(outputPath) Then Throw New ArgumentNullException(NameOf(outputPath))
        options.Validate()

        Dim rng As New Random(options.Seed)
        Dim noise As New Noise(options.Seed)
        Dim stats As New CityStats With {
            .Seed = options.Seed,
            .MapSize = options.MapSize
        }

        ' ---------------- level 0: terrain ----------------
        Dim terrain As New Terrain(options.MapSize, options.Cells)
        terrain.Generate(rng, noise, options.WaterPercentile)
        stats.WaterLevel = terrain.WaterLevel
        stats.MinHeight = terrain.MinHeight
        stats.MaxHeight = terrain.MaxHeight
        stats.WaterCells = terrain.CountWaterCells()
        stats.RiverLength = terrain.RiverLength

        ' ---------------- level 1: road network ----------------
        Dim net As New RoadNetwork()
        net.Generate(rng, options.MapSize, options.RoadDepth, options.MinBlock)
        stats.Blocks = net.Blocks.Count

        ' ---------------- level 2/3: blocks, plazas, buildings ----------------
        Dim meshGround As New MeshBuilder()
        Dim meshRoads As New MeshBuilder()
        Dim meshBuildings As New MeshBuilder()
        Dim meshWater As New MeshBuilder()

        Dim plazas = ChoosePlazas(rng, terrain, net, options.PlazaCount)

        For Each block In net.Blocks
            Dim wf = terrain.WaterFraction(block.X0, block.Y0, block.X1, block.Y1)
            If wf > 0.55 Then Continue For          ' mostly lake — leave it open

            If plazas.Contains(block) Then
                EmitPlaza(rng, terrain, block, options, meshBuildings, stats)
            Else
                EmitBlockBuildings(rng, noise, terrain, block, options, meshBuildings, stats)
            End If
            stats.BuiltBlocks += 1
        Next

        ' ---------------- meshes: roads / bridges ----------------
        EmitRoads(net, terrain, meshRoads, stats)

        ' ---------------- meshes: terrain & water ----------------
        EmitTerrain(terrain, meshGround)
        EmitWater(terrain, meshWater)

        ' ---------------- export ----------------
        Dim combined As New MeshBuilder()
        Append(combined, meshGround)
        Append(combined, meshRoads)
        Append(combined, meshBuildings)
        Append(combined, meshWater)

        StlWriter.Write(combined, outputPath,
                        $"CityBlender v1.0 seed={options.Seed} size={options.MapSize:0}m units=meters")

        If options.SplitRegions Then
            Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(outputPath))
            Dim stem = IO.Path.GetFileNameWithoutExtension(outputPath)
            StlWriter.Write(meshGround, IO.Path.Combine(dir, stem & "_ground.stl"), "CityBlender region: ground")
            StlWriter.Write(meshRoads, IO.Path.Combine(dir, stem & "_roads.stl"), "CityBlender region: roads")
            StlWriter.Write(meshBuildings, IO.Path.Combine(dir, stem & "_buildings.stl"), "CityBlender region: buildings")
            StlWriter.Write(meshWater, IO.Path.Combine(dir, stem & "_water.stl"), "CityBlender region: water")
        End If

        stats.Triangles = combined.TriangleCount
        Return stats
    End Function

    ' =====================================================================
    '  Height map: CBD (centre) high → suburbs low
    ' =====================================================================

    ''' <summary>Distance-to-centre height map, jittered by low-frequency noise.</summary>
    Private Shared Function HeightAt(noise As Noise, mapSize As Double, x As Double, y As Double) As Double
        Dim cx = mapSize / 2.0, cy = mapSize / 2.0
        Dim dx = x - cx, dy = y - cy
        Dim sigma = mapSize / 4.5
        Dim g = Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma))
        Dim n = noise.Fbm(x / 260.0 + 13.7, y / 260.0 + 71.3, 3)     ' 0..1 irregularity
        Return 120.0 * g * (0.65 + 0.7 * n)
    End Function

    ' =====================================================================
    '  Plazas
    ' =====================================================================

    Private Shared Function ChoosePlazas(rng As Random, terrain As Terrain, net As RoadNetwork, count As Integer) As HashSet(Of RoadNetwork.Block)
        Dim chosen As New HashSet(Of RoadNetwork.Block)
        If count <= 0 Then Return chosen

        Dim candidates = net.Blocks _
            .Where(Function(b) terrain.WaterFraction(b.X0, b.Y0, b.X1, b.Y1) < 0.10) _
            .OrderBy(Function(b) Math.Pow(b.X0 + b.X1 - terrain.MapSize, 2) + Math.Pow(b.Y0 + b.Y1 - terrain.MapSize, 2)) _
            .ToList()
        If candidates.Count = 0 Then
            ' fall back: any reasonably dry block
            candidates = net.Blocks _
                .Where(Function(b) terrain.WaterFraction(b.X0, b.Y0, b.X1, b.Y1) < 0.40) _
                .OrderBy(Function(b) Math.Pow(b.X0 + b.X1 - terrain.MapSize, 2) + Math.Pow(b.Y0 + b.Y1 - terrain.MapSize, 2)) _
                .ToList()
        End If
        If candidates.Count = 0 Then Return chosen

        ' pick randomly among the inner 40% closest to the centre
        Dim pool = Math.Max(1, CInt(candidates.Count * 0.4))
        Dim used As New HashSet(Of Integer)
        For k = 1 To count
            Dim idx As Integer
            Do
                idx = rng.Next(pool)
            Loop While used.Contains(idx) AndAlso used.Count < pool
            If used.Add(idx) Then chosen.Add(candidates(idx))
        Next
        Return chosen
    End Function

    Private Shared Sub EmitPlaza(rng As Random, terrain As Terrain, block As RoadNetwork.Block,
                                 options As CityOptions, mesh As MeshBuilder, stats As CityStats)
        Dim x0 = block.X0 + 6.0, y0 = block.Y0 + 6.0
        Dim x1 = block.X1 - 6.0, y1 = block.Y1 - 6.0
        If x1 - x0 < 24.0 OrElse y1 - y0 < 24.0 Then
            EmitBlockBuildings(rng, New Noise(options.Seed), terrain, block, options, mesh, stats)
            Return
        End If

        ' flagstone slab
        Dim zMin = MinCorner(terrain, x0, y0, x1, y1)
        mesh.AddBox(x0, y0, x1, y1, zMin - FoundationSink, zMin + 0.45)
        Dim slabTop = zMin + 0.45
        stats.Plazas += 1

        ' central fountain
        Dim cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0
        Dim fr = Math.Min(7.0, Math.Min(x1 - x0, y1 - y0) * 0.13)
        mesh.AddPrism(cx, cy, slabTop, slabTop + 1.3, fr, 20)
        mesh.AddPrism(cx, cy, slabTop + 1.0, slabTop + 1.15, fr - 1.0, 20)

        ' kiosk with gable roof
        Dim kx = x0 + 8.0, ky = y0 + 8.0
        If x1 - kx > 14.0 AndAlso y1 - ky > 14.0 Then
            mesh.AddBox(kx, ky, kx + 6.0, ky + 4.5, slabTop - 0.6, slabTop + 3.0)
            mesh.AddGableRoof(kx - 0.4, ky - 0.4, kx + 6.4, ky + 4.9, slabTop + 3.0, slabTop + 4.6)
        End If

        ' trees (deterministic dart throwing)
        If options.Trees Then
            Dim n = 8 + rng.Next(7)
            Dim placed As New List(Of Double())
            For t = 1 To n * 14
                If placed.Count >= n Then Exit For
                Dim tx = x0 + 5.0 + rng.NextDouble() * (x1 - x0 - 10.0)
                Dim ty = y0 + 5.0 + rng.NextDouble() * (y1 - y0 - 10.0)
                If Math.Sqrt((tx - cx) * (tx - cx) + (ty - cy) * (ty - cy)) < fr + 5.0 Then Continue For
                Dim ok = True
                For Each p In placed
                    If Math.Sqrt((tx - p(0)) ^ 2 + (ty - p(1)) ^ 2) < 9.0 Then ok = False : Exit For
                Next
                If ok Then
                    placed.Add({tx, ty})
                    mesh.AddPrism(tx, ty, slabTop, slabTop + 4.5, 0.55, 6)
                    mesh.AddPrism(tx, ty, slabTop + 3.2, slabTop + 8.0, 2.4, 7)
                    mesh.AddPrism(tx, ty, slabTop + 7.2, slabTop + 9.4, 1.3, 6)
                    stats.Trees += 1
                End If
            Next
        End If
    End Sub

    ' =====================================================================
    '  Level 2/3: Poisson-disk footprints + extrusion inside one block
    ' =====================================================================

    Private Shared Sub EmitBlockBuildings(rng As Random, noise As Noise, terrain As Terrain,
                                         block As RoadNetwork.Block, options As CityOptions,
                                         mesh As MeshBuilder, stats As CityStats)
        Dim margin = 20.0
        Dim w = block.X1 - block.X0 - 2.0 * margin
        Dim h = block.Y1 - block.Y0 - 2.0 * margin
        If w < 10.0 OrElse h < 10.0 Then Return

        Dim attempts = CInt(MathUtil.Clamp(w * h / (Math.PI * 10.0 * 10.0) * 25.0, 200.0, 3500.0))
        Dim accepted As New List(Of Double())   ' {x, y, radius}

        For attempt = 1 To attempts
            Dim x = block.X0 + margin + rng.NextDouble() * w
            Dim y = block.Y0 + margin + rng.NextDouble() * h

            ' desired height from the CBD height map with random scatter
            Dim baseH = HeightAt(noise, terrain.MapSize, x, y)
            Dim hgt = baseH * (0.45 + 0.75 * rng.NextDouble())
            If hgt < 7.0 Then hgt = 7.0

            Dim bw, bd, gap As Double
            If hgt >= 85.0 Then
                bw = 20.0 + rng.NextDouble() * 16.0
                bd = 20.0 + rng.NextDouble() * 16.0
                gap = 10.0
            ElseIf hgt >= 28.0 Then
                bw = 14.0 + rng.NextDouble() * 12.0
                bd = 14.0 + rng.NextDouble() * 12.0
                gap = 8.0
            Else
                bw = 9.0 + rng.NextDouble() * 7.0
                bd = 9.0 + rng.NextDouble() * 7.0
                gap = 6.0
            End If

            Dim radius = 0.5 * Math.Sqrt(bw * bw + bd * bd) + gap / 2.0

            ' variable-radius Poisson-disk rejection
            Dim ok = True
            For Each p In accepted
                Dim dx = x - p(0), dy = y - p(1)
                If Math.Sqrt(dx * dx + dy * dy) < radius + p(2) Then ok = False : Exit For
            Next
            If Not ok Then Continue For

            ' keep out of water and off steep slopes
            If terrain.IsWater(x, y) OrElse
               terrain.IsWater(x - bw / 2, y - bd / 2) OrElse terrain.IsWater(x + bw / 2, y + bd / 2) OrElse
               terrain.IsWater(x - bw / 2, y + bd / 2) OrElse terrain.IsWater(x + bw / 2, y - bd / 2) Then Continue For

            Dim zMin = MinCorner(terrain, x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2)
            Dim zMax = MaxCorner(terrain, x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2)
            If zMax - zMin > 22.0 Then Continue For          ' too steep for a slab foundation

            accepted.Add({x, y, radius})
            Dim z0 = zMin - FoundationSink

            ' wind-tunnel statistics: reference height + frontal areas
            If z0 + hgt > stats.MaxBuildingZ Then stats.MaxBuildingZ = z0 + hgt
            stats.FrontalX += bd * hgt
            stats.FrontalY += bw * hgt

            If hgt >= 85.0 Then
                ' skyscraper with optional setback crown
                If hgt > 100.0 Then
                    Dim mid = z0 + hgt * 0.66
                    mesh.AddBox(x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2, z0, mid)
                    Dim tw = bw * 0.62, td = bd * 0.62
                    mesh.AddBox(x - tw / 2, y - td / 2, x + tw / 2, y + td / 2, mid - 0.3, z0 + hgt)
                Else
                    mesh.AddBox(x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2, z0, z0 + hgt)
                End If
                stats.Skyscrapers += 1
            ElseIf hgt >= 28.0 Then
                mesh.AddBox(x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2, z0, z0 + hgt)
                stats.Towers += 1
            Else
                ' bungalow: LOD1 flat roof or LOD2 gable roof
                Dim wall = z0 + hgt * 0.72
                mesh.AddBox(x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2, z0, wall)
                If options.Lod >= 2 Then
                    mesh.AddGableRoof(x - bw / 2, y - bd / 2, x + bw / 2, y + bd / 2, wall, z0 + hgt)
                End If
                stats.Bungalows += 1
            End If
            stats.Buildings += 1
        Next
    End Sub

    ' =====================================================================
    '  Roads: terrain-following ribbons + bridges over water
    ' =====================================================================

    ''' <summary>
    ''' Terrain-following road ribbons. Each ribbon is a closed slab (top,
    ''' bottom, two side walls, two end caps → watertight). Cross-sections
    ''' over water become bridge decks sitting above the water level with
    ''' pier boxes every ~24 m.
    ''' </summary>
    Private Shared Sub EmitRoads(net As RoadNetwork, terrain As Terrain, mesh As MeshBuilder, stats As CityStats)
        Const stepLen As Double = 8.0
        Const deckThick As Double = 0.6

        For Each road In net.Roads
            Dim vertical = (road.Y1 - road.Y0) > (road.X1 - road.X0)
            Dim halfW = road.Width / 2.0 - 0.4
            Dim len = If(vertical, road.Y1 - road.Y0, road.X1 - road.X0)
            Dim nSteps = Math.Max(1, CInt(Math.Ceiling(len / stepLen)))

            ' cross-section centres and deck heights
            Dim c(nSteps) As Double, z(nSteps) As Double
            For k = 0 To nSteps
                Dim s = If(k = nSteps, len, k * stepLen)
                If vertical Then
                    c(k) = road.Y0 + s
                Else
                    c(k) = road.X0 + s
                End If
                Dim mid = If(vertical, (road.X0 + road.X1) / 2.0, (road.Y0 + road.Y1) / 2.0)
                Dim zc = If(vertical, terrain.SampleHeight(mid, c(k)), terrain.SampleHeight(c(k), mid))
                If zc < terrain.WaterLevel - 0.4 Then
                    z(k) = terrain.WaterLevel + BridgeClearance      ' bridge deck
                Else
                    z(k) = Math.Max(zc + RoadLift, terrain.WaterLevel + 0.5)
                End If
            Next

            Dim fixedAxis = If(vertical, (road.X0 + road.X1) / 2.0, (road.Y0 + road.Y1) / 2.0)

            For k = 1 To nSteps
                Dim s0 = c(k - 1), s1 = c(k)
                Dim z0 = z(k - 1), z1 = z(k)

                If vertical Then
                    Dim lx = fixedAxis - halfW, rx = fixedAxis + halfW
                    ' top
                    mesh.AddQuad(lx, s0, z0, rx, s0, z0, rx, s1, z1, lx, s1, z1)
                    ' bottom
                    mesh.AddQuad(lx, s1, z1 - deckThick, rx, s1, z1 - deckThick, rx, s0, z0 - deckThick, lx, s0, z0 - deckThick)
                    ' west wall (-x)
                    mesh.AddQuad(lx, s0, z0, lx, s1, z1, lx, s1, z1 - deckThick, lx, s0, z0 - deckThick)
                    ' east wall (+x)
                    mesh.AddQuad(rx, s0, z0, rx, s0, z0 - deckThick, rx, s1, z1 - deckThick, rx, s1, z1)
                Else
                    Dim ly = fixedAxis - halfW, ry = fixedAxis + halfW
                    ' top
                    mesh.AddQuad(s0, ly, z0, s1, ly, z1, s1, ry, z1, s0, ry, z0)
                    ' bottom
                    mesh.AddQuad(s1, ly, z1 - deckThick, s0, ly, z0 - deckThick, s0, ry, z0 - deckThick, s1, ry, z1 - deckThick)
                    ' south wall (-y)
                    mesh.AddQuad(s0, ly, z0, s0, ly, z0 - deckThick, s1, ly, z1 - deckThick, s1, ly, z1)
                    ' north wall (+y)
                    mesh.AddQuad(s0, ry, z0, s1, ry, z1, s1, ry, z1 - deckThick, s0, ry, z0 - deckThick)
                End If

                ' bridge piers
                If z(k) >= terrain.WaterLevel + BridgeClearance - 0.01 AndAlso
                   z(k - 1) >= terrain.WaterLevel + BridgeClearance - 0.01 Then
                    stats.BridgeSegments += 1
                    If k Mod 3 = 0 Then
                        If vertical Then
                            mesh.AddBox(fixedAxis - 1.6, c(k) - 1.6, fixedAxis + 1.6, c(k) + 1.6, -2.0, z(k) - deckThick)
                        Else
                            mesh.AddBox(c(k) - 1.6, fixedAxis - 1.6, c(k) + 1.6, fixedAxis + 1.6, -2.0, z(k) - deckThick)
                        End If
                    End If
                End If
            Next

            ' end caps (watertight closure)
            Dim sA = c(0), zA = z(0), zAb = z(0) - deckThick
            Dim sB = c(nSteps), zB = z(nSteps), zBb = z(nSteps) - deckThick
            If vertical Then
                Dim lx = fixedAxis - halfW, rx = fixedAxis + halfW
                mesh.AddQuad(lx, sA, zAb, rx, sA, zAb, rx, sA, zA, lx, sA, zA)
                mesh.AddQuad(rx, sB, zBb, lx, sB, zBb, lx, sB, zB, rx, sB, zB)
            Else
                Dim ly = fixedAxis - halfW, ry = fixedAxis + halfW
                mesh.AddQuad(sA, ry, zAb, sA, ly, zAb, sA, ly, zA, sA, ry, zA)
                mesh.AddQuad(sB, ly, zBb, sB, ry, zBb, sB, ry, zB, sB, ly, zB)
            End If
        Next
    End Sub

    ' =====================================================================
    '  Terrain & water meshes
    ' =====================================================================

    Private Shared Sub EmitTerrain(terrain As Terrain, mesh As MeshBuilder)
        Dim cs = terrain.CellSize
        Dim n = terrain.Cells

        ' surface
        For i = 0 To n - 1
            For j = 0 To n - 1
                Dim x0 = i * cs, y0 = j * cs
                Dim x1 = x0 + cs, y1 = y0 + cs
                Dim h00 = terrain.Heights(i, j), h10 = terrain.Heights(i + 1, j)
                Dim h01 = terrain.Heights(i, j + 1), h11 = terrain.Heights(i + 1, j + 1)
                mesh.AddTriangle(x0, y0, h00, x1, y0, h10, x1, y1, h11)
                mesh.AddTriangle(x0, y0, h00, x1, y1, h11, x0, y1, h01)
            Next
        Next

        ' skirt + bottom cap → closed (watertight) solid
        Dim size = terrain.MapSize
        Dim zb = -3.0
        For j = 0 To n - 1
            Dim y0 = j * cs, y1 = y0 + cs
            ' west edge (x = 0, outward -x)
            Dim h0 = terrain.Heights(0, j), h1 = terrain.Heights(0, j + 1)
            mesh.AddQuad(0.0, y0, h0, 0.0, y1, h1, 0.0, y1, zb, 0.0, y0, zb)
            ' east edge (x = size, outward +x)
            h0 = terrain.Heights(n, j) : h1 = terrain.Heights(n, j + 1)
            mesh.AddQuad(size, y0, zb, size, y1, zb, size, y1, h1, size, y0, h0)
        Next
        For i = 0 To n - 1
            Dim x0 = i * cs, x1 = x0 + cs
            ' south edge (y = 0, outward -y)
            Dim h0 = terrain.Heights(i, 0), h1 = terrain.Heights(i + 1, 0)
            mesh.AddQuad(x1, 0.0, zb, x1, 0.0, h1, x0, 0.0, h0, x0, 0.0, zb)
            ' north edge (y = size, outward +y)
            h0 = terrain.Heights(i, n) : h1 = terrain.Heights(i + 1, n)
            mesh.AddQuad(x0, size, zb, x0, size, h0, x1, size, h1, x1, size, zb)
        Next
        mesh.AddQuad(0.0, 0.0, zb, 0.0, size, zb, size, size, zb, size, 0.0, zb)
    End Sub

    Private Shared Sub EmitWater(terrain As Terrain, mesh As MeshBuilder)
        Dim cs = terrain.CellSize
        Dim n = terrain.Cells
        Dim wl = terrain.WaterLevel

        ' one surface quad per fully submerged cell (lakes, river surface)
        For i = 0 To n - 1
            For j = 0 To n - 1
                If terrain.Heights(i, j) < wl AndAlso terrain.Heights(i + 1, j) < wl AndAlso
                   terrain.Heights(i, j + 1) < wl AndAlso terrain.Heights(i + 1, j + 1) < wl Then
                    Dim x0 = i * cs, y0 = j * cs
                    mesh.AddQuad(x0, y0, wl, x0 + cs, y0, wl, x0 + cs, y0 + cs, wl, x0, y0 + cs, wl)
                End If
            Next
        Next
    End Sub

    ' =====================================================================
    '  Helpers
    ' =====================================================================

    Private Shared Function MinCorner(terrain As Terrain, x0 As Double, y0 As Double, x1 As Double, y1 As Double) As Double
        Return Math.Min(Math.Min(terrain.SampleHeight(x0, y0), terrain.SampleHeight(x1, y0)),
                        Math.Min(terrain.SampleHeight(x0, y1), terrain.SampleHeight(x1, y1)))
    End Function

    Private Shared Function MaxCorner(terrain As Terrain, x0 As Double, y0 As Double, x1 As Double, y1 As Double) As Double
        Return Math.Max(Math.Max(terrain.SampleHeight(x0, y0), terrain.SampleHeight(x1, y0)),
                        Math.Max(terrain.SampleHeight(x0, y1), terrain.SampleHeight(x1, y1)))
    End Function

    Private Shared Sub Append(target As MeshBuilder, source As MeshBuilder)
        For i = 0 To source.TriangleCount - 1
            Dim t = source.GetTriangle(i)
            target.AddTriangle(t(3), t(4), t(5), t(6), t(7), t(8), t(9), t(10), t(11))
        Next
    End Sub

End Class
