Option Strict On
Option Explicit On

Imports System.Reflection
Imports Microsoft.VisualBasic.CommandLine.Reflection

''' <summary>
''' CityBlender command-line interface.
''' <para>
'''   CityBlender.Cli /generate /seed 42 /size 1000 /out city.stl
''' </para>
''' Units are meters; output is a watertight binary STL.
''' </summary>
Public Module CityBlenderCLI

    Public Function Main(args As String()) As Integer
        If args.Length = 0 Then
            ShowHelp()
            Return 0
        End If

        Dim cmd = args(0).ToLowerInvariant()
        If cmd = "/help" OrElse cmd = "--help" OrElse cmd = "-h" OrElse cmd = "/?" Then
            ShowHelp()
            Return 0
        End If

        Dim parsed = ParseSwitches(args)

        For Each m In GetType(CityBlenderCLI).GetMethods(BindingFlags.Public Or BindingFlags.Static)
            Dim attr = m.GetCustomAttribute(Of ExportAPIAttribute)()
            If attr IsNot Nothing AndAlso attr.Name.ToLowerInvariant() = cmd Then
                m.Invoke(Nothing, {parsed})
                Return 0
            End If
        Next

        Console.Error.WriteLine($"unknown command '{args(0)}' — try /help")
        Return 1
    End Function

    ''' <summary>Turns "/key value /flag" arguments into a lookup dictionary.</summary>
    Private Function ParseSwitches(args As String()) As Dictionary(Of String, String)
        ' switches that never take a value
        Dim boolFlags = {"split", "notrees", "windtunnel"}

        Dim d As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim i = 1
        While i < args.Length
            Dim key = args(i).TrimStart("/"c, "-"c).ToLowerInvariant()
            If boolFlags.Contains(key) OrElse i + 1 >= args.Length Then
                d(key) = "true"
                i += 1
            Else
                ' value switches always consume the next token — this keeps
                ' absolute paths like /out /tmp/city.stl working
                d(key) = args(i + 1)
                i += 2
            End If
        End While
        Return d
    End Function

    Private Function GetInt(d As Dictionary(Of String, String), key As String, def As Integer) As Integer
        Dim v As Integer
        If d.TryGetValue(key, Nothing) AndAlso Integer.TryParse(d(key), v) Then Return v
        Return def
    End Function

    Private Function GetDouble(d As Dictionary(Of String, String), key As String, def As Double) As Double
        Dim v As Double
        If d.TryGetValue(key, Nothing) AndAlso Double.TryParse(d(key), v) Then Return v
        Return def
    End Function

    Private Function GetString(d As Dictionary(Of String, String), key As String, def As String) As String
        Dim v As String = Nothing
        If d.TryGetValue(key, v) Then Return v
        Return def
    End Function

    ''' <summary>Generate a random city and export binary STL.</summary>
    <ExportAPI("/generate")>
    <Usage("/generate [/seed <int>] [/size <m>] [/cells <int>] [/water <0.02..0.6>] [/depth <0..5>] " &
           "[/lod <1|2>] [/plazas <int>] [/notrees] [/split] " &
           "[/windtunnel [/wind <0|90|180|270>] [/upstream <H>] [/downstream <H>] [/side <H>] [/top <H>]] /out <file.stl>")>
    Public Sub Generate(args As Dictionary(Of String, String))
        Dim out = GetString(args, "out", "city.stl")

        Dim options As New CityOptions With {
            .Seed = GetInt(args, "seed", 42),
            .MapSize = GetDouble(args, "size", 1000.0),
            .Cells = GetInt(args, "cells", 192),
            .WaterPercentile = GetDouble(args, "water", 0.12),
            .RoadDepth = GetInt(args, "depth", 4),
            .MinBlock = GetDouble(args, "minblock", 110.0),
            .Lod = GetInt(args, "lod", 1),
            .PlazaCount = GetInt(args, "plazas", 2),
            .Trees = Not args.ContainsKey("notrees"),
            .SplitRegions = args.ContainsKey("split")
        }

        Console.WriteLine($"CityBlender — random city generator for wind-tunnel CFD")
        Console.WriteLine($"  seed={options.Seed}, map={options.MapSize:0} m, lod={options.Lod}, " &
                          $"water={options.WaterPercentile:0.00}, roads depth={options.RoadDepth}")

        Dim stats = CityGenerator.Generate(options, out)

        Console.WriteLine(stats.ToString())
        Console.WriteLine($"  written          : {IO.Path.GetFullPath(out)}")
        If options.SplitRegions Then
            Console.WriteLine($"  regions (snappyHexMesh-ready): {IO.Path.GetFileNameWithoutExtension(out)}_ground/roads/buildings/water.stl")
        End If

        If args.ContainsKey("windtunnel") Then
            EmitWindTunnel(stats, options, args, out)
        End If
    End Sub

    ''' <summary>Builds the CFD domain around the freshly generated city.</summary>
    Private Sub EmitWindTunnel(stats As CityStats, options As CityOptions,
                               args As Dictionary(Of String, String), out As String)
        Dim wtOptions As New WindTunnel.Options With {
            .WindDirection = GetInt(args, "wind", 0),
            .UpstreamH = GetDouble(args, "upstream", 5.0),
            .DownstreamH = GetDouble(args, "downstream", 15.0),
            .SideH = GetDouble(args, "side", 5.0),
            .TopH = GetDouble(args, "top", 5.0)
        }

        Dim domain = WindTunnel.Compute(stats, options.MapSize, wtOptions)

        Console.WriteLine($"  wind tunnel      : wind {domain.WindDirection} deg, H = {domain.H:0.0} m")
        Console.WriteLine($"  domain           : x [{domain.X0:0} .. {domain.X1:0}] m, y [{domain.Y0:0} .. {domain.Y1:0}] m, z [{domain.Z0:0} .. {domain.Z1:0}] m")
        Console.WriteLine($"                     length x width x height = {domain.Length:0} x {domain.Width:0} x {domain.Height:0} m")
        Console.WriteLine($"  frontal area     : {domain.FrontalArea:0} m2, blockage ratio {100.0 * domain.BlockageRatio:0.00}% (conservative, overlaps counted)")
        If domain.BlockageRatio > 0.03 Then
            Console.WriteLine("  WARNING          : blockage ratio > 3% — widen the domain (/side, /top) or shrink the city")
        End If

        Dim dir = IO.Path.GetDirectoryName(IO.Path.GetFullPath(out))
        Dim baseName = IO.Path.GetFileNameWithoutExtension(out)
        WindTunnel.WriteSurfaces(domain, dir, baseName)
        IO.File.WriteAllText(IO.Path.Combine(dir, baseName & "_blockMeshDict"),
                             WindTunnel.WriteBlockMeshDict(domain))
        Console.WriteLine($"  patches          : {baseName}_inlet/outlet/side_*/top.stl + {baseName}_blockMeshDict")
    End Sub

    Private Sub ShowHelp()
        Console.WriteLine("CityBlender v1.0 — random 3-D city model generator (binary STL, meters, CFD-ready)")
        Console.WriteLine()
        Console.WriteLine("Commands:")
        For Each m In GetType(CityBlenderCLI).GetMethods(BindingFlags.Public Or BindingFlags.Static)
            Dim api = m.GetCustomAttribute(Of ExportAPIAttribute)()
            Dim usage = m.GetCustomAttribute(Of UsageAttribute)()
            If api IsNot Nothing Then
                Console.WriteLine($"  {api.Name}")
                If usage IsNot Nothing Then
                    Console.WriteLine($"      {usage.UsageInfo}")
                End If
            End If
        Next
        Console.WriteLine()
        Console.WriteLine("Example:")
        Console.WriteLine("  CityBlender.Cli /generate /seed 7 /size 1200 /split /out /tmp/city7.stl")
    End Sub

End Module
