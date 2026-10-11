Imports System.IO
Imports Microsoft.VisualBasic.ComponentModel.DataStructures
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi
Imports Microsoft.VisualBasic.Imaging.Drawing3D.Math3D.Delaunay3D
Imports Moira.CFDEngine.Eulerian
Imports Moira.ZMesh
Imports LxPoint3D = Microsoft.VisualBasic.Imaging.Drawing3D.Point3D
Imports IOPath = System.IO.Path
Imports std = System.Math

Module Program

    Private failures As Integer = 0

    Sub Check(name As String, condition As Boolean, detail As String)
        If condition Then
            Console.WriteLine($"[PASS] {name} {detail}")
        Else
            Console.WriteLine($"[FAIL] {name} {detail}")
            failures += 1
        End If
    End Sub

    ''' <summary>写一个单位立方体 ASCII STL 测试模型。</summary>
    Function WriteCubeStl(path As String) As Double
        Using w As New StreamWriter(path)
            w.WriteLine("solid cube")

            ' 12 个三角形（绕序外法向）
            Dim tris As Integer()() = {
                New Integer() {0, 2, 1}, New Integer() {0, 3, 2},
                New Integer() {4, 5, 6}, New Integer() {4, 6, 7},
                New Integer() {0, 1, 5}, New Integer() {0, 5, 4},
                New Integer() {1, 2, 6}, New Integer() {1, 6, 5},
                New Integer() {2, 3, 7}, New Integer() {2, 7, 6},
                New Integer() {3, 0, 4}, New Integer() {3, 4, 7}}
            Dim v As Double()() = {
                New Double() {0, 0, 0}, New Double() {1, 0, 0},
                New Double() {1, 1, 0}, New Double() {0, 1, 0},
                New Double() {0, 0, 1}, New Double() {1, 0, 1},
                New Double() {1, 1, 1}, New Double() {0, 1, 1}}

            For Each t As Integer() In tris
                w.WriteLine("  facet normal 0 0 0")
                w.WriteLine("    outer loop")

                For Each vi As Integer In t
                    w.WriteLine($"      vertex {v(vi)(0)} {v(vi)(1)} {v(vi)(2)}")
                Next

                w.WriteLine("    endloop")
                w.WriteLine("  endfacet")
            Next

            w.WriteLine("endsolid cube")
        End Using

        Return 1.0
    End Function

    Sub Main(args As String())
        Console.WriteLine("==== ZMesh Eulerian SelfTest ====")

        Call TestOctree()
        Call TestTightOctree()
        Call TestDelaunay3D()
        Call TestVoronoi2D()
        Call TestEulerianMeshGeometry()
        Call TestTopologyBuilder()
        Call TestMeshPackage()
        Call TestPoissonSmoother()
        Call TestEndToEnd()

        Console.WriteLine()
        Console.WriteLine($"==== {(If(failures = 0, "ALL TESTS PASSED", failures.ToString & " FAILURES"))} ====")

        If failures > 0 Then
            Environment.ExitCode = 1
        End If
    End Sub

    ' ---------------- Octree（Core 库） ----------------

    Sub TestOctree()
        Dim bounds As New BoundingBox3D(-1, -1, -1, 1, 1, 1)
        Dim tree As New Octree(Of Integer)(bounds, capacity:=4, maxDepth:=6)
        Dim n As Integer = 200
        Dim rng As New Random(3)

        For i As Integer = 0 To n - 1
            Call tree.Insert(rng.NextDouble * 2 - 1, rng.NextDouble * 2 - 1, rng.NextDouble * 2 - 1, i)
        Next

        Check("Octree.Count", tree.Count = n, $"count={tree.Count}")
        Check("Octree.subdivided", Not tree.IsLeaf, "root is internal node")

        ' 区域查询：左下角小盒
        Dim box As New BoundingBox3D(-1, -1, -1, 0, 0, 0)
        Dim hits As List(Of OctreePoint(Of Integer)) = tree.Query(box)
        Dim all As List(Of OctreePoint(Of Integer)) = tree.Query(bounds)

        Check("Octree.Query", all.Count = n, $"query all={all.Count}")

        ' 最近邻
        Dim dist As Double = 0
        Dim nearest As OctreePoint(Of Integer) = tree.Nearest(0.9, 0.9, 0.9, dist)

        ' 暴力验证
        Dim bestDist As Double = Double.MaxValue
        Dim bestId As Integer = -1

        For Each p In all
            Dim dx As Double = p.X - 0.9, dy As Double = p.Y - 0.9, dz As Double = p.Z - 0.9
            Dim d As Double = std.Sqrt(dx * dx + dy * dy + dz * dz)

            If d < bestDist Then
                bestDist = d
                bestId = p.Value
            End If
        Next

        Check("Octree.Nearest", dist < Double.PositiveInfinity AndAlso nearest.Value = bestId,
              $"octree=#{nearest.Value} brute=#{bestId}")
    End Sub

    Sub TestTightOctree()
        Dim bounds As New BoundingBox3D(0, 0, 0, 4, 4, 4)
        Dim tree As New TightOctree(Of Integer)(bounds, maxDepth:=4)

        ' 一个穿过 (2,*,*) 的三角形边界（xz 平面三角）
        Call tree.Insert(New BoundingBox3D(1.5, 0, 0, 2.5, 4, 4), 7)
        Call tree.RefineToUniform(2)

        Dim leaves As List(Of TightOctreeCell(Of Integer)) = tree.Leaves

        Check("TightOctree.Leaves", leaves.Count = 64, $"leaves={leaves.Count} (uniform depth2)")

        Dim boundaryCount As Integer = leaves.Where(Function(l) l.IsBoundaryCell).Count()
        Check("TightOctree.boundary cells", boundaryCount > 0 AndAlso boundaryCount < leaves.Count, $"boundary={boundaryCount}")

        ' 边界自适应：minCellSize=1 时不再细分到 size<1
        Dim tree2 As New TightOctree(Of Integer)(bounds, maxDepth:=6)
        Call tree2.Insert(New BoundingBox3D(1.5, 0, 0, 2.5, 4, 4), 1)
        Call tree2.RefineBoundaryCells(1.0)
        Dim leaves2 As List(Of TightOctreeCell(Of Integer)) = tree2.Leaves
        Dim minSize As Double = Double.MaxValue
        Dim badLeaf As String = ""

        For Each l As TightOctreeCell(Of Integer) In leaves2
            Dim s As Double = std.Max(std.Max(l.Bounds.SizeX, l.Bounds.SizeY), l.Bounds.SizeZ)

            If s < minSize Then
                minSize = s

                If s < 0.9 Then
                    badLeaf = l.ToString
                End If
            End If
        Next

        Check("TightOctree.RefineBoundaryCells", std.Abs(minSize - 1.0) < 0.001, $"minCellSize={minSize} {badLeaf}")
    End Sub

    ' ---------------- 3D Delaunay ----------------

    Sub TestDelaunay3D()
        Dim cube As New List(Of LxPoint3D) From {
            New LxPoint3D(0, 0, 0), New LxPoint3D(1, 0, 0), New LxPoint3D(0, 1, 0), New LxPoint3D(0, 0, 1),
            New LxPoint3D(1, 1, 0), New LxPoint3D(1, 0, 1), New LxPoint3D(0, 1, 1), New LxPoint3D(1, 1, 1)}
        Dim tets As List(Of TetraIndices) = DelaunayTriangulation3D.Triangulate(cube)

        Dim vol As Double = tets.Sum(Function(t)
                                         Dim tet As New Tetrahedron(cube(t.A), cube(t.B), cube(t.C), cube(t.D))
                                         Return tet.Volume
                                     End Function)

        Check("Delaunay3D.cube count", tets.Count >= 5 AndAlso tets.Count <= 12, $"tets={tets.Count}")
        Check("Delaunay3D.cube volume", std.Abs(vol - 1.0) < 0.0001, $"vol={vol.ToString("G6")}")

        Dim violations As Integer = DelaunayTestHelper.ValidateDelaunay(cube, tets)
        Check("Delaunay3D.empty sphere", violations = 0, $"violations={violations}")

        Dim boundary As List(Of Integer()) = DelaunayTriangulation3D.BoundaryTriangles(cube, tets)
        Dim edgeCount As New Dictionary(Of Long, Integer)

        For Each tri As Integer() In boundary
            Dim vs As Integer() = {tri(0), tri(1), tri(2)}

            For e As Integer = 0 To 2
                Dim a As Integer = vs(e), b As Integer = vs((e + 1) Mod 3)
                Dim key As Long = FvmTopologyBuilder.DelaunayFaceKey(a, b, 1000000 + std.Max(a, b))

                If edgeCount.ContainsKey(key) Then
                    edgeCount(key) += 1
                Else
                    edgeCount(key) = 1
                End If
            Next
        Next

        Dim badEdges As Integer = 0

        ' 简化：直接数每条无序边出现次数
        edgeCount.Clear()

        For Each tri As Integer() In boundary
            Dim vs As Integer() = {tri(0), tri(1), tri(2)}

            For e As Integer = 0 To 2
                Dim a As Integer = vs(e), b As Integer = vs((e + 1) Mod 3)
                Dim key As Long = FvmTopologyBuilder.DelaunayFaceKey(a, b, 100000)

                If edgeCount.ContainsKey(key) Then
                    edgeCount(key) += 1
                Else
                    edgeCount(key) = 1
                End If
            Next
        Next

        badEdges = edgeCount.Values.Where(Function(c) c <> 2).Count()
        Check("Delaunay3D.closed boundary", badEdges = 0, $"triangles={boundary.Count}")
    End Sub

    ' ---------------- 2D Delaunay（Voronoi） ----------------

    Sub TestVoronoi2D()
        Try
            Dim pts As New List(Of Microsoft.VisualBasic.Imaging.Math2D.Vector2D) From {
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0, 0),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 0),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0, 1),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 1),
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(1, 1)}
            Dim bounds As New Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi.Rectf(0, 0, 1, 1)
            Dim v As New Voronoi(pts, bounds)
            Check("Voronoi2D.duplicate tolerance", True, $"edges={v.Edges.Count}")
        Catch ex As Exception
            Check("Voronoi2D.duplicate tolerance", False, ex.Message)
        End Try

        Try
            Dim singleSite As New List(Of Microsoft.VisualBasic.Imaging.Math2D.Vector2D) From {
                New Microsoft.VisualBasic.Imaging.Math2D.Vector2D(0.5, 0.5)}
            Dim bounds As New Microsoft.VisualBasic.Imaging.Drawing2D.Math2D.DelaunayVoronoi.Rectf(0, 0, 1, 1)
            Dim v As New Voronoi(singleSite, bounds)
            Check("Voronoi2D.single site", True, $"edges={v.Edges.Count}")
        Catch ex As Exception
            Check("Voronoi2D.single site", False, ex.Message)
        End Try
    End Sub

    ' ---------------- EulerianMesh 几何 ----------------

    Sub TestEulerianMeshGeometry()
        ' 单位立方体网格（通过 RawCellMesh）
        Dim raw As New RawCellMesh
        Dim ids As Integer() = New Integer(7) {}
        Dim corners As Double()() = {
            New Double() {0, 0, 0}, New Double() {1, 0, 0}, New Double() {1, 1, 0}, New Double() {0, 1, 0},
            New Double() {0, 0, 1}, New Double() {1, 0, 1}, New Double() {1, 1, 1}, New Double() {0, 1, 1}}

        For i As Integer = 0 To 7
            ids(i) = raw.AddVertex(New EulerianVertex(corners(i)(0), corners(i)(1), corners(i)(2)))
        Next

        Call raw.AddHex("fluid", ids)
        Dim mesh As EulerianMesh = FvmTopologyBuilder.Build(raw)

        Check("EulerianMesh.volume", std.Abs(mesh.TotalVolume - 1.0) < 0.0001, $"vol={mesh.TotalVolume.ToString("G6")}")
        Check("EulerianMesh.faces", mesh.FaceCount = 6 AndAlso mesh.BoundaryFaceCount = 6,
              $"faces={mesh.FaceCount}, boundary={mesh.BoundaryFaceCount}")
        Check("EulerianMesh.centroid",
              std.Abs(mesh.CellCentroids(0).X - 0.5) < 0.001 AndAlso
              std.Abs(mesh.CellCentroids(0).Y - 0.5) < 0.001 AndAlso
              std.Abs(mesh.CellCentroids(0).Z - 0.5) < 0.001,
              $"centroid={mesh.CellCentroids(0).ToString}")
    End Sub

    ' ---------------- FvmTopologyBuilder ----------------

    Sub TestTopologyBuilder()
        ' 两个相邻六面体：1 个内部面 + 10 个边界面
        Dim raw As New RawCellMesh
        Dim a As Integer() = New Integer(7) {}
        Dim b As Integer() = New Integer(7) {}
        Dim corners As Double()() = {
            New Double() {0, 0, 0}, New Double() {1, 0, 0}, New Double() {1, 1, 0}, New Double() {0, 1, 0},
            New Double() {0, 0, 1}, New Double() {1, 0, 1}, New Double() {1, 1, 1}, New Double() {0, 1, 1}}

        For i As Integer = 0 To 7
            a(i) = raw.AddVertex(New EulerianVertex(corners(i)(0), corners(i)(1), corners(i)(2)))
        Next

        For i As Integer = 0 To 7
            Dim p As Double() = corners(i)

            If i = 0 OrElse i = 3 OrElse i = 4 OrElse i = 7 Then
                ' 共享面（x=1）上的角点复用 hex A 的顶点索引
                b(i) = a(i)
            Else
                b(i) = raw.AddVertex(New EulerianVertex(p(0) + 1, p(1), p(2)))
            End If
        Next

        Call raw.AddHex("fluid", a)
        Call raw.AddHex("fluid", b)
        Dim mesh As EulerianMesh = FvmTopologyBuilder.Build(raw)

        Check("TopologyBuilder.internal face", mesh.FaceCount = 11 AndAlso mesh.BoundaryFaceCount = 10,
              $"faces={mesh.FaceCount}, boundary={mesh.BoundaryFaceCount}")
        Check("TopologyBuilder.volume", std.Abs(mesh.TotalVolume - 2.0) < 0.0001, $"vol={mesh.TotalVolume.ToString("G6")}")

        ' 内部面 Owner/Neighbour 一致性
        Dim internalFaces As Integer = 0
        Dim consistent As Boolean = True

        For Each f As EulerianFace In mesh.Faces
            If Not f.IsBoundary Then
                internalFaces += 1

                ' 法向应自 Owner 指向 Neighbour
                Dim dir As EulerianVertex = mesh.CellCentroids(f.Neighbour) - mesh.CellCentroids(f.Owner)

                If EulerianVertex.Dot(mesh.FaceNormals(mesh.Faces.IndexOf(f)), dir) <= 0 Then
                    consistent = False
                End If
            End If
        Next

        Check("TopologyBuilder.orientation", internalFaces = 1 AndAlso consistent,
              $"internal={internalFaces}, normalOK={consistent}")
    End Sub

    ' ---------------- zip 包读写 ----------------

    Sub TestMeshPackage()
        Dim raw As New RawCellMesh
        Dim a As Integer() = New Integer(7) {}
        Dim corners As Double()() = {
            New Double() {0, 0, 0}, New Double() {1, 0, 0}, New Double() {1, 1, 0}, New Double() {0, 1, 0},
            New Double() {0, 0, 1}, New Double() {1, 0, 1}, New Double() {1, 1, 1}, New Double() {0, 1, 1}}

        For i As Integer = 0 To 7
            a(i) = raw.AddVertex(New EulerianVertex(corners(i)(0), corners(i)(1), corners(i)(2)))
        Next

        Call raw.AddHex("fluid", a)
        Dim mesh As EulerianMesh = FvmTopologyBuilder.Build(raw)
        Dim patch As New EulerianPatch With {.Name = "ground", .Type = EulerianPatchType.Wall}
        patch.FaceIndices.AddRange(Enumerable.Range(0, mesh.FaceCount).Where(Function(i) mesh.Faces(i).IsBoundary))
        mesh.Patches.Add(patch)

        Dim path As String = IOPath.Combine(IOPath.GetTempPath(), "eulerian_selftest.zip")
        Call EulerianMeshPackage.Save(mesh, path, "selftest")

        Dim loaded As EulerianMesh = EulerianMeshPackage.Load(path)

        Check("MeshPackage.roundtrip points", loaded.PointCount = mesh.PointCount, $"{loaded.PointCount}")
        Check("MeshPackage.roundtrip faces", loaded.FaceCount = mesh.FaceCount, $"{loaded.FaceCount}")
        Check("MeshPackage.roundtrip volume", std.Abs(loaded.TotalVolume - mesh.TotalVolume) < 0.0001,
              $"vol={loaded.TotalVolume.ToString("G6")}")
        Check("MeshPackage.roundtrip patches",
              loaded.Patches.Count = 1 AndAlso loaded.Patches(0).Name = "ground" AndAlso
              loaded.Patches(0).FaceIndices.Count = patch.FaceIndices.Count,
              $"{loaded.Patches.FirstOrDefault?.ToString}")

        Dim info As EulerianMeshInfo = EulerianMeshPackage.ReadMetadata(path)
        Check("MeshPackage.metadata", info.PointCount = mesh.PointCount AndAlso info.CellCount = mesh.CellCount,
              $"points={info.PointCount}, cells={info.CellCount}")

        Call File.Delete(path)
    End Sub

    ' ---------------- Poisson 平滑 ----------------

    Sub TestPoissonSmoother()
        ' 4x4x4 网格：线性函数 x 坐标是 Laplace 方程的解，平滑后应保持不变（除数值误差）
        Dim ni As Integer = 4, nj As Integer = 4, nk As Integer = 4
        Dim coords As Double() = New Double(ni * nj * nk - 1) {}

        For i As Integer = 0 To ni - 1
            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    coords((i * nj + j) * nk + k) = i
                Next
            Next
        Next

        Call StructuredPoissonSmoother.Smooth(coords, ni, nj, nk, 10)

        Dim maxErr As Double = 0

        For i As Integer = 0 To ni - 1
            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    maxErr = std.Max(maxErr, std.Abs(coords((i * nj + j) * nk + k) - i))
                Next
            Next
        Next

        Check("Poisson.linear preserved", maxErr < 0.001, $"maxErr={maxErr.ToString("G3")}")
    End Sub

    ' ---------------- 端到端流水线 ----------------

    Sub TestEndToEnd()
        Dim stl As String = IOPath.Combine(IOPath.GetTempPath(), "eulerian_cube.stl")
        Call WriteCubeStl(stl)

        Try
            ' 八叉树（IBM 模式，低分辨率）
            Dim report As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                stl, Nothing, EulerianPipeline.MeshType.Octree,
                resolution:=0.34, domainScale:=1.6, cutCells:=False, smoothLaplacian:=False)
            Dim m As EulerianMesh = report.Mesh

            ' 固体立方体被挖除：流体体积应小于域包围盒体积且大于 0
            Dim boxVol As Double = domain_size(m)

            Check("E2E.octree build", m.CellCount > 0 AndAlso m.TotalVolume > 0 AndAlso m.TotalVolume < boxVol,
                  $"{report}, boxVol={boxVol.ToString("G3")}, fluidVol={m.TotalVolume.ToString("G3")}")
        Catch ex As Exception
            Check("E2E.octree build", False, ex.Message)
        End Try

        Try
            ' 结构化网格
            Dim report2 As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                stl, Nothing, EulerianPipeline.MeshType.Structured,
                resolution:=0.34, domainScale:=1.6, smoothLaplacian:=False)
            Check("E2E.structured build", report2.Mesh.CellCount > 0, report2.ToString)
        Catch ex As Exception
            Check("E2E.structured build", False, ex.Message)
        End Try

        ' zip 落盘回读
        Try
            Dim zip As String = IOPath.Combine(IOPath.GetTempPath(), "eulerian_e2e.zip")
            Dim report3 As EulerianPipeline.PipelineReport = EulerianPipeline.Convert(
                stl, zip, EulerianPipeline.MeshType.Octree,
                resolution:=0.34, domainScale:=1.6, smoothLaplacian:=False)
            Dim loaded As EulerianMesh = EulerianMeshPackage.Load(zip)

            Check("E2E.zip roundtrip",
                  loaded.CellCount = report3.Mesh.CellCount AndAlso loaded.Patches.Count > 0,
                  $"cells={loaded.CellCount}, patches={loaded.Patches.Count}")

            Call File.Delete(zip)
        Catch ex As Exception
            Check("E2E.zip roundtrip", False, ex.Message)
        End Try
    End Sub

    Function domain_size(m As EulerianMesh) As Double
        Dim bmin, bmax As EulerianVertex
        Call m.GetBounds(bmin, bmax)
        Return (bmax - bmin).X * (bmax - bmin).Y * (bmax - bmin).Z
    End Function
End Module
