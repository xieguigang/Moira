Option Strict On
Option Explicit On

Imports System.IO
Imports System.Text

Namespace CityBlender

    ''' <summary>
    ''' Wind-tunnel CFD domain built around a generated city, following the
    ''' wind-engineering conventions: upstream 5H, downstream 15H, sides and top
    ''' 5H each, where H is the highest building; the blockage ratio should stay
    ''' below 3%.
    ''' <para>
    ''' Produces boundary patch surfaces (inlet / outlet / side_west/east/south/
    ''' north / top) as individual binary STL files with normals pointing out of
    ''' the fluid domain, plus a ready-to-use OpenFOAM <c>blockMeshDict</c>.
    ''' </para>
    ''' </summary>
    Public NotInheritable Class WindTunnel

        ''' <summary>Domain sizing parameters; distances are multiples of H.</summary>
        Public NotInheritable Class Options
            ''' <summary>Wind heading in degrees: 0 = blowing towards +X, 90 = +Y, 180 = -X, 270 = -Y.</summary>
            Public Property WindDirection As Integer = 0
            Public Property UpstreamH As Double = 5.0
            Public Property DownstreamH As Double = 15.0
            Public Property SideH As Double = 5.0
            Public Property TopH As Double = 5.0

            Public Sub Validate()
                Dim d = ((WindDirection Mod 360) + 360) Mod 360
                If d Mod 90 <> 0 Then
                    Throw New ArgumentOutOfRangeException(NameOf(WindDirection),
                    "wind direction must be a multiple of 90 degrees (0/90/180/270)")
                End If
                If UpstreamH <= 0 OrElse DownstreamH <= 0 OrElse SideH <= 0 OrElse TopH <= 0 Then
                    Throw New ArgumentOutOfRangeException("multipliers", "all H multipliers must be positive")
                End If
            End Sub
        End Class

        ''' <summary>Computed domain box and derived quantities.</summary>
        Public NotInheritable Class Domain
            Public X0, Y0, Z0, X1, Y1, Z1 As Double
            Public H As Double
            Public FrontalArea, CrossSection, BlockageRatio As Double
            Public WindDirection As Integer

            Public ReadOnly Property Length As Double
                Get
                    Return X1 - X0
                End Get
            End Property

            Public ReadOnly Property Width As Double
                Get
                    Return Y1 - Y0
                End Get
            End Property

            Public ReadOnly Property Height As Double
                Get
                    Return Z1 - Z0
                End Get
            End Property
        End Class

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Computes the domain around the city. The city occupies
        ''' [0, size]×[0, size]; the box extends UpstreamH·H against the wind,
        ''' DownstreamH·H along it, SideH·H sideways and TopH·H above the
        ''' highest building top.
        ''' </summary>
        Public Shared Function Compute(city As CityStats, size As Double, options As Options) As Domain
            If city Is Nothing Then Throw New ArgumentNullException(NameOf(city))
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            options.Validate()

            ' reference height: highest building top, with a sane floor
            Dim h = Math.Max(city.MaxBuildingZ, 20.0)

            Dim d = New Domain With {.H = h, .WindDirection = ((options.WindDirection Mod 360) + 360) Mod 360}
            Dim dir = ((options.WindDirection Mod 360) + 360) Mod 360

            ' extents along the wind axis (upstream = where the wind comes FROM)
            Dim up = options.UpstreamH * h
            Dim down = options.DownstreamH * h
            Dim side = options.SideH * h

            If dir = 0 OrElse dir = 180 Then
                ' wind along X — the box is always X0 < X1; the upstream margin
                ' attaches to the side the wind comes FROM
                If dir = 0 Then
                    d.X0 = -up                       ' upstream (-x)
                    d.X1 = size + down               ' downstream (+x)
                Else
                    d.X0 = -down                     ' downstream (-x)
                    d.X1 = size + up                 ' upstream (+x)
                End If
                d.Y0 = -side
                d.Y1 = size + side
                d.FrontalArea = city.FrontalX
            Else
                ' wind along Y
                If dir = 90 Then
                    d.Y0 = -up                       ' upstream (-y)
                    d.Y1 = size + down              ' downstream (+y)
                Else
                    d.Y0 = -down                    ' downstream (-y)
                    d.Y1 = size + up                 ' upstream (+y)
                End If
                d.X0 = -side
                d.X1 = size + side
                d.FrontalArea = city.FrontalY
            End If

            d.Z0 = -3.0                            ' terrain skirt bottom
            d.Z1 = h + options.TopH * h            ' 5H of clearance above the tallest building

            ' blockage ratio: city frontal area over the inlet cross-section
            If dir = 0 OrElse dir = 180 Then
                d.CrossSection = d.Width * d.Height
            Else
                d.CrossSection = d.Length * d.Height
            End If
            d.BlockageRatio = If(d.CrossSection > 0.0, d.FrontalArea / d.CrossSection, 0.0)
            Return d
        End Function

        ''' <summary>
        ''' Writes the five boundary patches as individual binary STL files
        ''' (normals point out of the fluid domain) into <paramref name="directory"/>
        ''' as <c>{base}_inlet.stl</c> etc.
        ''' </summary>
        Public Shared Sub WriteSurfaces(d As Domain, outDirectory As String, baseName As String)
            Global.System.IO.Directory.CreateDirectory(outDirectory)

            Dim x0 = d.X0, y0 = d.Y0, x1 = d.X1, y1 = d.Y1, z0 = d.Z0, z1 = d.Z1

            ' box corners: bottom 0-3 counter-clockwise, top 4-7
            Dim v = {
            (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)
        }

            Dim dir = ((d.WindDirection Mod 360) + 360) Mod 360
            Dim inletIdx, outletIdx As Integer        ' face indices 0=-x, 1=+x, 2=-y, 3=+y

            Select Case dir
                Case 0 : inletIdx = 0 : outletIdx = 1                ' from -x towards +x
                Case 90 : inletIdx = 2 : outletIdx = 3                ' from -y towards +y
                Case 180 : inletIdx = 1 : outletIdx = 0               ' from +x towards -x
                Case Else : inletIdx = 3 : outletIdx = 2               ' 270: from +y towards -y
            End Select

            For face = 0 To 4
                ' 0=-x(west) 1=+x(east) 2=-y(south) 3=+y(north) 4=+z(top)
                Dim name As String

                Select Case face
                    Case 0 : name = "side_west"
                    Case 1 : name = "side_east"
                    Case 2 : name = "side_south"
                    Case 3 : name = "side_north"
                    Case Else : name = "top"
                End Select

                If face = inletIdx Then
                    name = "inlet"
                ElseIf face = outletIdx Then
                    name = "outlet"
                End If

                Dim quad() As (x As Double, y As Double, z As Double)
                Select Case face
                    Case 0 : quad = {v(0), v(4), v(7), v(3)}           ' -x, outward normal -x
                    Case 1 : quad = {v(1), v(2), v(6), v(5)}           ' +x
                    Case 2 : quad = {v(0), v(1), v(5), v(4)}           ' -y
                    Case 3 : quad = {v(3), v(7), v(6), v(2)}           ' +y
                    Case Else : quad = {v(4), v(5), v(6), v(7)}        ' +z
                End Select

                Dim mesh As New MeshBuilder()
                mesh.AddQuad(quad(0).x, quad(0).y, quad(0).z,
                         quad(1).x, quad(1).y, quad(1).z,
                         quad(2).x, quad(2).y, quad(2).z,
                         quad(3).x, quad(3).y, quad(3).z)
                StlWriter.Write(mesh, Path.Combine(outDirectory, $"{baseName}_{name}.stl"),
                            $"CityBlender wind-tunnel patch {name} wind={d.WindDirection}deg units=meters")
            Next
        End Sub

        ''' <summary>Writes a ready-to-use OpenFOAM blockMeshDict for the domain.</summary>
        Public Shared Function WriteBlockMeshDict(d As Domain) As String
            Dim sb As New StringBuilder()
            Dim f As New Global.System.Globalization.CultureInfo("en-US")

            sb.AppendLine("/*--------------------------------*- C++ -*----------------------------------*\")
            sb.AppendLine("| =========                 |                                                 |")
            sb.AppendLine("| \\      /  F ield         | CityBlender: auto-generated wind-tunnel domain |")
            sb.AppendLine("|  \\    /   O peration     | units: meters (1 unit = 1 m)                   |")
            sb.AppendLine("|   \\  /    A nd           |                                                 |")
            sb.AppendLine("|    \\/     M anipulation  |                                                 |")
            sb.AppendLine("\*---------------------------------------------------------------------------*/")
            sb.AppendLine("FoamFile")
            sb.AppendLine("{")
            sb.AppendLine("    version     2.0;")
            sb.AppendLine("    format      ascii;")
            sb.AppendLine("    class       dictionary;")
            sb.AppendLine("    object      blockMeshDict;")
            sb.AppendLine("}")
            sb.AppendLine("// * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * //")
            sb.AppendLine()
            sb.AppendLine($"convertToMeters 1;")
            sb.AppendLine()
            sb.AppendLine($"// H (max building top) = {d.H.ToString("0.0", f)} m; domain {d.Length.ToString("0", f)} x {d.Width.ToString("0", f)} x {d.Height.ToString("0", f)} m, blockage = {100.0 * d.BlockageRatio:0.00}%")
            sb.AppendLine("vertices")
            sb.AppendLine("(")
            Dim pts = {
            (d.X0, d.Y0, d.Z0), (d.X1, d.Y0, d.Z0), (d.X1, d.Y1, d.Z0), (d.X0, d.Y1, d.Z0),
            (d.X0, d.Y0, d.Z1), (d.X1, d.Y0, d.Z1), (d.X1, d.Y1, d.Z1), (d.X0, d.Y1, d.Z1)
        }
            For Each p In pts
                sb.AppendLine($"    ({p.Item1.ToString("0.###", f)} {p.Item2.ToString("0.###", f)} {p.Item3.ToString("0.###", f)})")
            Next
            sb.AppendLine(");")
            sb.AppendLine()

            ' suggested background cell size ~ H/4, snappyHexMesh will refine
            Dim cell = Math.Max(5.0, d.H / 4.0)
            Dim nx = Math.Max(4, CInt(Math.Round(d.Length / cell)))
            Dim ny = Math.Max(4, CInt(Math.Round(d.Width / cell)))
            Dim nz = Math.Max(4, CInt(Math.Round(d.Height / cell)))
            sb.AppendLine($"// background mesh ~ {cell:0} m cells ({nx}x{ny}x{nz}); adjust before running blockMesh")
            sb.AppendLine("blocks")
            sb.AppendLine("(")
            sb.AppendLine($"    hex (0 1 2 3 4 5 6 7) ({nx} {ny} {nz}) simpleGrading (1 1 1)")
            sb.AppendLine(");")
            sb.AppendLine()

            sb.AppendLine("boundary")
            sb.AppendLine("(")
            Select Case d.WindDirection
                Case 0
                    AppendPatch(sb, "inlet", "patch", "    (0 4 7 3)")
                    AppendPatch(sb, "outlet", "patch", "    (1 2 6 5)")
                    AppendPatch(sb, "side_south", "patch", "    (0 1 5 4)")
                    AppendPatch(sb, "side_north", "patch", "    (3 7 6 2)")
                Case 180
                    AppendPatch(sb, "inlet", "patch", "    (1 2 6 5)")
                    AppendPatch(sb, "outlet", "patch", "    (0 4 7 3)")
                    AppendPatch(sb, "side_south", "patch", "    (0 1 5 4)")
                    AppendPatch(sb, "side_north", "patch", "    (3 7 6 2)")
                Case 90
                    AppendPatch(sb, "inlet", "patch", "    (0 1 5 4)")
                    AppendPatch(sb, "outlet", "patch", "    (3 7 6 2)")
                    AppendPatch(sb, "side_west", "patch", "    (0 4 7 3)")
                    AppendPatch(sb, "side_east", "patch", "    (1 2 6 5)")
                Case Else     ' 270
                    AppendPatch(sb, "inlet", "patch", "    (3 7 6 2)")
                    AppendPatch(sb, "outlet", "patch", "    (0 1 5 4)")
                    AppendPatch(sb, "side_west", "patch", "    (0 4 7 3)")
                    AppendPatch(sb, "side_east", "patch", "    (1 2 6 5)")
            End Select
            AppendPatch(sb, "top", "patch", "    (4 5 6 7)")
            AppendPatch(sb, "bottom", "wall", "    (0 3 2 1)    // terrain/ground - snap the city onto this")
            sb.AppendLine(");")
            sb.AppendLine()
            sb.AppendLine("// ************************************************************************* //")
            Return sb.ToString()
        End Function

        Private Shared Sub AppendPatch(sb As StringBuilder, name As String, kind As String, face As String)
            sb.AppendLine($"    {name}")
            sb.AppendLine("    {")
            sb.AppendLine($"        type {kind};")
            sb.AppendLine($"        faces")
            sb.AppendLine("        (")
            sb.AppendLine(face)
            sb.AppendLine("        );")
            sb.AppendLine("    }")
        End Sub

    End Class
End Namespace