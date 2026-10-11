Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D.Math3D.Delaunay3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi
Imports std = System.Math

Module Program

    Function MakeCube(Optional n As Integer = 6) As List(Of Point3D)
        ' 单位立方体内的 n^3 网格点 + 少量随机抖动
        Dim pts As New List(Of Point3D)
        Dim rng As New Random(7)

        For i As Integer = 0 To n - 1
            For j As Integer = 0 To n - 1
                For k As Integer = 0 To n - 1
                    pts.Add(New Point3D(
                        i / (n - 1) + (rng.NextDouble - 0.5) * 0.1 / n,
                        j / (n - 1) + (rng.NextDouble - 0.5) * 0.1 / n,
                        k / (n - 1) + (rng.NextDouble - 0.5) * 0.1 / n))
                Next
            Next
        Next

        Return pts
    End Function

    Sub Main(args As String())
        Dim failures As Integer = 0

        ' ===== Test 1: 单位立方体 8 顶点 =====
        Dim cube As New List(Of Point3D) From {
            New Point3D(0, 0, 0), New Point3D(1, 0, 0), New Point3D(0, 1, 0), New Point3D(0, 0, 1),
            New Point3D(1, 1, 0), New Point3D(1, 0, 1), New Point3D(0, 1, 1), New Point3D(1, 1, 1)}
        Dim tets As List(Of TetraIndices) = DelaunayTriangulation3D.Triangulate(cube)
        Console.WriteLine($"[DBG] skipped={DelaunayTriangulation3D.LastRunSkipped}, alive={DelaunayTriangulation3D.LastRunAliveTets}/{DelaunayTriangulation3D.LastRunTotalTets}")

        ' 逐步加点定位首次出错
        DelaunayTriangulation3D.DebugValidateEachStep = True

        For k As Integer = 4 To 8
            Dim sub_tets As List(Of TetraIndices) = DelaunayTriangulation3D.Triangulate(cube.Take(k).ToList)

            For Each line As String In DelaunayTriangulation3D.LastDebugLog
                Console.WriteLine($"[DBG k={k}] {line}")
            Next
            Dim sub_vol As Double = sub_tets.Sum(Function(t)
                                                     Dim tet As New Tetrahedron(cube(t.A), cube(t.B), cube(t.C), cube(t.D))
                                                     Return tet.Volume
                                                 End Function)
            Console.WriteLine($"[DBG] k={k}: tets={sub_tets.Count}, vol={sub_vol.ToString("G6")}")
        Next
        ' 立方体有 6 种四面体剖分，每种恰好 5 或 6 个四面体（5T 或 6T 剖分）
        If tets.Count < 5 OrElse tets.Count > 6 Then
            Console.WriteLine($"[FAIL] cube tetra count = {tets.Count}, expect 5..6")
            failures += 1
        Else
            Console.WriteLine($"[PASS] cube tetra count = {tets.Count}")
        End If

        Dim vol As Double = tets.Sum(Function(t)
                                         Dim tet As New Tetrahedron(cube(t.A), cube(t.B), cube(t.C), cube(t.D))
                                         Return tet.Volume
                                     End Function)

        If std.Abs(vol - 1.0) > 0.0001 Then
            Console.WriteLine($"[FAIL] cube total volume = {vol}, expect 1")
            failures += 1
        Else
            Console.WriteLine($"[PASS] cube total volume = {vol.ToString("G6")}")
        End If

        Dim violations As Integer = DelaunayTestHelper.ValidateDelaunay(cube, tets)

        If violations > 0 Then
            Console.WriteLine($"[FAIL] cube Delaunay violations = {violations}")
            failures += 1
        Else
            Console.WriteLine("[PASS] cube Delaunay property holds")
        End If

        ' ===== Test 2: 3x3x3 抖动网格 =====
        Dim grid As List(Of Point3D) = MakeCube(3)
        Dim tets2 As List(Of TetraIndices) = DelaunayTriangulation3D.Triangulate(grid)
        Console.WriteLine($"[INFO] 3x3x3 jitter grid: {grid.Count} points -> {tets2.Count} tetra")

        Dim vol2 As Double = tets2.Sum(Function(t)
                                           Dim tet As New Tetrahedron(grid(t.A), grid(t.B), grid(t.C), grid(t.D))
                                           Return tet.Volume
                                       End Function)
        ' 域体积约 1.0（抖动不改变外边界近似）——体积和应接近包围盒体积
        If vol2 < 0.5 OrElse vol2 > 1.5 Then
            Console.WriteLine($"[FAIL] grid total volume = {vol2}")
            failures += 1
        Else
            Console.WriteLine($"[PASS] grid total volume = {vol2.ToString("G6")}")
        End If

        ' 每个四面体应为正定向且非退化
        Dim degen As Integer = 0

        For Each t As TetraIndices In tets2
            Dim tet As New Tetrahedron(grid(t.A), grid(t.B), grid(t.C), grid(t.D))

            If tet.SignedVolume6 <= 0 OrElse tet.Volume < 0.000000001 Then
                degen += 1
            End If
        Next

        If degen > 0 Then
            Console.WriteLine($"[FAIL] grid degenerate/misoriented tets = {degen}")
            failures += 1
        Else
            Console.WriteLine("[PASS] all grid tetrahedra positively oriented and non-degenerate")
        End If

        ' ===== Test 3: 边界三角形提取（封闭流形检查：每条边恰好被 2 个三角形共享）=====
        Dim boundary As List(Of Integer()) = DelaunayTriangulation3D.BoundaryTriangles(cube, tets)
        Dim edgeCount As New Dictionary(Of Long, Integer)

        For Each tri As Integer() In boundary
            Dim vs As Integer() = {tri(0), tri(1), tri(2)}

            For e As Integer = 0 To 2
                Dim a As Integer = vs(e), b As Integer = vs((e + 1) Mod 3)

                If a > b Then
                    Dim tmp As Integer = a : a = b : b = tmp
                End If

                Dim key As Long = CLng(a) * 1000000 + b

                If edgeCount.ContainsKey(key) Then
                    edgeCount(key) += 1
                Else
                    edgeCount(key) = 1
                End If
            Next
        Next

        Dim badEdges As Integer = edgeCount.Values.Where(Function(c) c <> 2).Count()

        If badEdges > 0 Then
            Console.WriteLine($"[FAIL] cube boundary mesh: {badEdges} edges not shared by exactly 2 triangles")
            failures += 1
        Else
            Console.WriteLine($"[PASS] cube boundary mesh is closed manifold ({boundary.Count} triangles)")
        End If

        ' ===== Test 4: 2D Voronoi/Delaunay 冒烟（重复点 + 少量点）=====
        Try
            Dim pts2d As New List(Of Microsoft.VisualBasic.Imaging.Math2D.Vector2D) From {
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0, 0),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 0),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0, 1),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 1),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 1)}
            Dim bounds As New Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi.Rectf(0, 0, 1, 1)
            Dim v As New Voronoi(pts2d, bounds)
            Console.WriteLine($"[PASS] Voronoi with duplicate point OK, edges={v.Edges.Count}")
        Catch ex As Exception
            Console.WriteLine($"[FAIL] Voronoi duplicate point threw: {ex.Message}")
            failures += 1
        End Try

        Try
            Dim singleSite As New List(Of Microsoft.VisualBasic.Imaging.Math2D.Vector2D) From {
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0.5, 0.5)}
            Dim bounds As New Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi.Rectf(0, 0, 1, 1)
            Dim v As New Voronoi(singleSite, bounds)
            Console.WriteLine($"[PASS] Voronoi with single site OK, edges={v.Edges.Count}")
        Catch ex As Exception
            Console.WriteLine($"[FAIL] Voronoi single site threw: {ex.Message}")
            failures += 1
        End Try

        ' ===== Test 5: 质量度量 =====
        Dim quality As DelaunayQualityStats = DelaunayTestHelper.ComputeQuality(cube, tets)
        Console.WriteLine($"[INFO] cube quality: {quality.ToString}")

        Console.WriteLine()
        Console.WriteLine($"==== {(If(failures = 0, "ALL TESTS PASSED", failures.ToString & " FAILURES"))} ====")

        If failures > 0 Then
            Environment.ExitCode = 1
        End If
    End Sub
End Module
