Option Strict On
Option Explicit On

Imports std = System.Math

Namespace CityBlender


    ''' <summary>
    ''' Level-1 of the procedural chain: recursive (wavefront-style) splitting of
    ''' the map into road strips and street blocks. Early splits carry wide
    ''' arterial roads ("主干路"); later splits are secondary streets.
    ''' </summary>
    Public NotInheritable Class RoadNetwork

        ''' <summary>An axis-aligned road strip.</summary>
        Public NotInheritable Class RoadStrip
            ''' <summary>Strip bounds; the road runs along the long axis.</summary>
            Public ReadOnly X0, Y0, X1, Y1 As Double
            Public ReadOnly Width As Double
            Public ReadOnly IsMain As Boolean

            Public Sub New(x0 As Double, y0 As Double, x1 As Double, y1 As Double, width As Double, isMain As Boolean)
                Me.X0 = x0 : Me.Y0 = y0 : Me.X1 = x1 : Me.Y1 = y1
                Me.Width = width : Me.IsMain = isMain
            End Sub
        End Class

        ''' <summary>A street block: free space between roads, already inset by the sidewalk.</summary>
        Public NotInheritable Class Block
            Public ReadOnly X0, Y0, X1, Y1 As Double

            Public Sub New(x0 As Double, y0 As Double, x1 As Double, y1 As Double)
                Me.X0 = x0 : Me.Y0 = y0 : Me.X1 = x1 : Me.Y1 = y1
            End Sub

            Public ReadOnly Property Width As Double
                Get
                    Return X1 - X0
                End Get
            End Property

            Public ReadOnly Property Depth As Double
                Get
                    Return Y1 - Y0
                End Get
            End Property
        End Class

        Public ReadOnly Roads As New List(Of RoadStrip)
        Public ReadOnly Blocks As New List(Of Block)

        ''' <summary>Split the square map recursively into blocks separated by roads.</summary>
        ''' <param name="maxDepth">Number of split generations; 0 = a single block, no roads.</param>
        ''' <param name="minBlock">Blocks smaller than this are no longer split (meters).</param>
        Public Sub Generate(rng As Random, mapSize As Double, maxDepth As Integer, minBlock As Double)
            Roads.Clear()
            Blocks.Clear()
            Recurse(rng, mapSize, 0.0, 0.0, mapSize, mapSize, maxDepth, minBlock)
        End Sub

        Private Sub Recurse(rng As Random, mapSize As Double,
                        x0 As Double, y0 As Double, x1 As Double, y1 As Double,
                        depth As Integer, minBlock As Double)
            Dim w = x1 - x0
            Dim h = y1 - y0

            ' stop splitting: emit a block inset by its sidewalk
            If depth <= 0 OrElse Math.Min(w, h) < minBlock * 1.7 Then
                Const sidewalk = 4.0
                Dim bx0 = x0 + If(x0 > 0.0, sidewalk, 0.5)
                Dim by0 = y0 + If(y0 > 0.0, sidewalk, 0.5)
                Dim bx1 = x1 - If(x1 < mapSize, sidewalk, 0.5)
                Dim by1 = y1 - If(y1 < mapSize, sidewalk, 0.5)
                If bx1 - bx0 >= 8.0 AndAlso by1 - by0 >= 8.0 Then
                    Blocks.Add(New Block(bx0, by0, bx1, by1))
                End If
                Return
            End If

            Dim isMain = depth >= 3        ' outermost splits = arterials
            Dim roadW = If(isMain, 20.0, 11.0)
            Dim vertical = w >= h

            ' mid split with jitter (the "random" of the street pattern)
            Dim t = 0.5 + (rng.NextDouble() - 0.5) * 0.34
            t = std.Clamp(t, 0.32, 0.68)

            If vertical Then
                Dim mid = x0 + w * t
                Dim rx0 = mid - roadW / 2.0, rx1 = mid + roadW / 2.0
                Roads.Add(New RoadStrip(rx0, y0, rx1, y1, roadW, isMain))
                Recurse(rng, mapSize, x0, y0, rx0, y1, depth - 1, minBlock)
                Recurse(rng, mapSize, rx1, y0, x1, y1, depth - 1, minBlock)
            Else
                Dim mid = y0 + h * t
                Dim ry0 = mid - roadW / 2.0, ry1 = mid + roadW / 2.0
                Roads.Add(New RoadStrip(x0, ry0, x1, ry1, roadW, isMain))
                Recurse(rng, mapSize, x0, y0, x1, ry0, depth - 1, minBlock)
                Recurse(rng, mapSize, x0, ry1, x1, y1, depth - 1, minBlock)
            End If
        End Sub

    End Class
End Namespace