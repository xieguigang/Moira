Imports System.IO
Imports Microsoft.VisualBasic.ComponentModel.DataStructures
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' 笛卡尔八叉树网格生成器。
'''
''' 流程：
'''   1. 以计算域包围盒为大立方体，构建携带三角面片索引的紧八叉树
'''      （<see cref="TightOctree(Of T)"/>）；
'''   2. 均匀细分至目标网格尺度 <see cref="MeshGenerateOptions.Resolution"/>；
'''   3. 内部叶子单元（不与边界相交）：中心在固体内的丢弃，其余输出为流体六面体；
'''   4. 边界叶子单元：按相交三角面片的平面方程做半空间裁剪
'''      （<see cref="PolyhedronClipper"/>），输出 cut-cell 多面体；
'''      CutCells = False 时保留完整六面体（配合浸入边界法 IBM 使用）。
''' </summary>
Public Class OctreeCutCellGenerator : Implements IMeshGenerator

    Public ReadOnly Property Name As String Implements IMeshGenerator.Name
        Get
            Return "octree-cutcell"
        End Get
    End Property

    Public Function Generate(options As MeshGenerateOptions) As RawCellMesh Implements IMeshGenerator.Generate
        Dim domain As GeometryPreprocessor.FluidDomain = options.Domain

        If domain.Triangles.Count = 0 Then
            Throw New InvalidDataException("流体域不含三角面片")
        End If

        Dim ext As EulerianVertex = domain.DomainSize
        Dim maxExtent As Double = std.Max(std.Max(ext.X, ext.Y), ext.Z)

        If maxExtent <= 0 Then
            Throw New InvalidDataException("计算域尺寸非法")
        End If

        ' 目标深度：最细单元尺寸 <= resolution
        Dim targetDepth As Integer = CInt(std.Ceiling(std.Log(maxExtent / options.Resolution, 2)))

        If targetDepth < 1 Then
            targetDepth = 1
        End If

        If targetDepth > 18 Then
            Throw New InvalidDataException(
                $"目标网格尺度 {options.Resolution} 过细：需要八叉树深度 {targetDepth} > 18 上限，请增大 Resolution")
        End If

        ' ---- 1. 构建携带三角面片的紧八叉树 ----
        Dim bounds As New BoundingBox3D(
            domain.DomainMin.X, domain.DomainMin.Y, domain.DomainMin.Z,
            domain.DomainMax.X, domain.DomainMax.Y, domain.DomainMax.Z)
        Dim octree As New TightOctree(Of Integer)(bounds, targetDepth)

        For t As Integer = 0 To domain.Triangles.Count - 1
            Dim tri As Integer() = domain.Triangles(t)
            Dim a As EulerianVertex = domain.Vertices(tri(0))
            Dim b As EulerianVertex = domain.Vertices(tri(1))
            Dim c As EulerianVertex = domain.Vertices(tri(2))
            Dim tb As New BoundingBox3D(
                std.Min(a.X, std.Min(b.X, c.X)), std.Min(a.Y, std.Min(b.Y, c.Y)), std.Min(a.Z, std.Min(b.Z, c.Z)),
                std.Max(a.X, std.Max(b.X, c.X)), std.Max(a.Y, std.Max(b.Y, c.Y)), std.Max(a.Z, std.Max(b.Z, c.Z)))
            Call octree.Insert(tb, t)
        Next

        ' ---- 2. 均匀细分至目标尺度 ----
        Call octree.RefineToUniform(targetDepth)

        ' ---- 3/4. 叶子单元 → 网格单元 ----
        Dim mesh As New RawCellMesh
        Dim gridPoints As New Dictionary(Of Long, Integer)
        Dim leaves As List(Of TightOctreeCell(Of Integer)) = octree.Leaves
        Dim cellSize As Double = maxExtent / (1L << targetDepth)
        Dim eps As Double = cellSize * 0.001

        For Each leaf As TightOctreeCell(Of Integer) In leaves
            Dim b As BoundingBox3D = leaf.Bounds
            Dim center As New EulerianVertex(
                (b.MinX + b.MaxX) * 0.5,
                (b.MinY + b.MaxY) * 0.5,
                (b.MinZ + b.MaxZ) * 0.5)

            If leaf.IsBoundaryCell Then
                If options.CutCells Then
                    Call emitCutCell(mesh, domain, leaf, gridPoints, eps)
                Else
                    ' IBM 模式：完整六面体，中心在固体内则丢弃
                    If domain.IsSolid(center) Then
                        Continue For
                    End If

                    Call emitHex(mesh, gridPoints, b, "wall")
                End If
            Else
                If domain.IsSolid(center) Then
                    Continue For
                End If

                Call emitHex(mesh, gridPoints, b, "fluid")
            End If
        Next

        Return mesh
    End Function

    ''' <summary>按均匀网格索引登记共享角点（保证相邻单元的面顶点一致焊接）。</summary>
    Private Shared Function InternGridPoint(mesh As RawCellMesh,
                                            gridPoints As Dictionary(Of Long, Integer),
                                            x As Double, y As Double, z As Double) As Integer
        Dim key As Long = QuantPoint(x, y, z)

        If gridPoints.ContainsKey(key) Then
            Return gridPoints(key)
        End If

        Dim idx As Integer = mesh.AddVertex(New EulerianVertex(x, y, z))
        gridPoints(key) = idx
        Return idx
    End Function

    Private Shared Function QuantPoint(x As Double, y As Double, z As Double) As Long
        ' 以量化坐标位打包（坐标值先缩放以便 21bit 覆盖常见范围）
        Dim ux As Long = CLng(x * 4096.0) And &H1FFFFF
        Dim uy As Long = CLng(y * 4096.0) And &H1FFFFF
        Dim uz As Long = CLng(z * 4096.0) And &H1FFFFF
        Return ux Or (uy << 21) Or (uz << 42)
    End Function

    Private Shared Sub emitHex(mesh As RawCellMesh,
                               gridPoints As Dictionary(Of Long, Integer),
                               b As BoundingBox3D, tag As String)
        Dim c As Integer() = {
            InternGridPoint(mesh, gridPoints, b.MinX, b.MinY, b.MinZ),
            InternGridPoint(mesh, gridPoints, b.MaxX, b.MinY, b.MinZ),
            InternGridPoint(mesh, gridPoints, b.MaxX, b.MaxY, b.MinZ),
            InternGridPoint(mesh, gridPoints, b.MinX, b.MaxY, b.MinZ),
            InternGridPoint(mesh, gridPoints, b.MinX, b.MinY, b.MaxZ),
            InternGridPoint(mesh, gridPoints, b.MaxX, b.MinY, b.MaxZ),
            InternGridPoint(mesh, gridPoints, b.MaxX, b.MaxY, b.MaxZ),
            InternGridPoint(mesh, gridPoints, b.MinX, b.MaxY, b.MaxZ)}
        Call mesh.AddHex(tag, c)
    End Sub

    ''' <summary>对边界叶子单元做切割单元裁剪。</summary>
    Private Shared Sub emitCutCell(mesh As RawCellMesh,
                                   domain As GeometryPreprocessor.FluidDomain,
                                   leaf As TightOctreeCell(Of Integer),
                                   gridPoints As Dictionary(Of Long, Integer),
                                   eps As Double)

        Dim b As BoundingBox3D = leaf.Bounds

        ' 局部点表（8 角点）+ 面表（外法向绕序，与 AddHex 一致）
        Dim lp As New List(Of EulerianVertex) From {
            New EulerianVertex(b.MinX, b.MinY, b.MinZ), New EulerianVertex(b.MaxX, b.MinY, b.MinZ),
            New EulerianVertex(b.MaxX, b.MaxY, b.MinZ), New EulerianVertex(b.MinX, b.MaxY, b.MinZ),
            New EulerianVertex(b.MinX, b.MinY, b.MaxZ), New EulerianVertex(b.MaxX, b.MinY, b.MaxZ),
            New EulerianVertex(b.MaxX, b.MaxY, b.MaxZ), New EulerianVertex(b.MinX, b.MaxY, b.MaxZ)}
        Dim lf As New List(Of List(Of Integer)) From {
            New List(Of Integer) From {0, 3, 2, 1},
            New List(Of Integer) From {4, 5, 6, 7},
            New List(Of Integer) From {0, 4, 7, 3},
            New List(Of Integer) From {1, 2, 6, 5},
            New List(Of Integer) From {0, 1, 5, 4},
            New List(Of Integer) From {3, 7, 6, 2}}

        ' 依次以相交三角面片的平面裁剪（保留流体侧）
        Dim center As New EulerianVertex((b.MinX + b.MaxX) * 0.5, (b.MinY + b.MaxY) * 0.5, (b.MinZ + b.MaxZ) * 0.5)
        Dim usedPlanes As New List(Of Long)
        Dim clipCount As Integer = 0

        For Each item As TightOctreeItem(Of Integer) In leaf.Items
            Dim tri As Integer() = domain.Triangles(item.Value)
            Dim a As EulerianVertex = domain.Vertices(tri(0))
            Dim bv As EulerianVertex = domain.Vertices(tri(1))
            Dim cv As EulerianVertex = domain.Vertices(tri(2))

            Dim e1 As EulerianVertex = bv - a
            Dim e2 As EulerianVertex = cv - a
            Dim n As EulerianVertex = EulerianVertex.Cross(e1, e2)
            Dim nl As Double = n.Length

            If nl < 1.0E-30 Then
                Continue For
            End If

            n = n * (1.0 / nl)
            Dim d As Double = EulerianVertex.Dot(center - a, n)
            Dim keep As Double = If(d <= 0, -1.0, 1.0)   ' 保留单元中心所在的一侧

            ' 平面去重（法向 + 距离粗量化）
            Dim planeKey As Long =
                (CLng(n.X * 512.0) And &H3FF) Or
                ((CLng(n.Y * 512.0) And &H3FF) << 10) Or
                ((CLng((d + 1.0) * 4096.0) And &H3FF) << 20)

            If usedPlanes.Contains(planeKey) Then
                Continue For
            End If

            usedPlanes.Add(planeKey)

            If clipCount >= 64 Then
                Exit For
            End If

            Dim result As PolyhedronClip = PolyhedronClipper.Clip(lp, lf, a, n, keep, eps * 0.01)

            If result Is Nothing OrElse result.Faces.Count < 4 Then
                ' 完全被裁掉：单元在固体内部
                Return
            End If

            lp = result.Points
            lf = result.Faces
            clipCount += 1
        Next

        ' 输出多面体单元（局部点映射为全局点：角点走栅格焊接，交点新建）
        Dim cell As New RawCell With {.Tag = "wall-cut"}
        Dim localMap As New Dictionary(Of Integer, Integer)

        For i As Integer = 0 To lp.Count - 1
            localMap(i) = -1
        Next

        ' 角点（与原始 8 角点量化位置一致的点）走栅格焊接
        Dim corners As EulerianVertex() = {
            New EulerianVertex(b.MinX, b.MinY, b.MinZ), New EulerianVertex(b.MaxX, b.MinY, b.MinZ),
            New EulerianVertex(b.MaxX, b.MaxY, b.MinZ), New EulerianVertex(b.MinX, b.MaxY, b.MinZ),
            New EulerianVertex(b.MinX, b.MinY, b.MaxZ), New EulerianVertex(b.MaxX, b.MinY, b.MaxZ),
            New EulerianVertex(b.MaxX, b.MaxY, b.MaxZ), New EulerianVertex(b.MinX, b.MaxY, b.MaxZ)}

        For i As Integer = 0 To lp.Count - 1
            For k As Integer = 0 To 7
                Dim dv As EulerianVertex = lp(i) - corners(k)

                If std.Abs(dv.X) < eps AndAlso std.Abs(dv.Y) < eps AndAlso std.Abs(dv.Z) < eps Then
                    localMap(i) = InternGridPoint(mesh, gridPoints, lp(i).X, lp(i).Y, lp(i).Z)
                    Exit For
                End If
            Next
        Next

        For i As Integer = 0 To lp.Count - 1
            If localMap(i) < 0 Then
                localMap(i) = mesh.AddVertex(lp(i))
            End If
        Next

        For Each f As List(Of Integer) In lf
            If f.Count < 3 Then
                Continue For
            End If

            Dim rf As New RawFace

            For Each vi As Integer In f
                rf.PointIndices.Add(localMap(vi))
            Next

            cell.Faces.Add(rf)
        Next

        If cell.Faces.Count >= 4 Then
            mesh.Cells.Add(cell)
        End If
    End Sub
End Class

''' <summary>多面体裁剪结果。</summary>
Public Class PolyhedronClip

    ''' <summary>裁剪后的点表（坐标系与输入相同，可能追加了交点）。</summary>
    Public Points As List(Of EulerianVertex)

    ''' <summary>裁剪后的面表（顶点索引指向 <see cref="Points"/>）。</summary>
    Public Faces As List(Of List(Of Integer))
End Class

''' <summary>
''' 多面体半空间裁剪器（切割单元法核心）：
''' 以平面 (p0, n) 将多面体裁剪为保留侧（dot(x - p0, n)·keep &gt;= 0），
''' 并沿裁剪平面生成封盖面。
''' </summary>
Public Class PolyhedronClipper

    Public Shared Function Clip(points As List(Of EulerianVertex),
                                faces As List(Of List(Of Integer)),
                                p0 As EulerianVertex,
                                n As EulerianVertex,
                                keep As Double,
                                eps As Double) As PolyhedronClip

        Dim dist As Double() = New Double(points.Count - 1) {}

        For i As Integer = 0 To points.Count - 1
            dist(i) = EulerianVertex.Dot(points(i) - p0, n) * keep
        Next

        Dim newPoints As New List(Of EulerianVertex)(points)
        Dim newFaces As New List(Of List(Of Integer))
        ' 封盖有向边：from -> to（沿裁剪平面，方向继承面绕序）
        Dim capEdges As New Dictionary(Of Integer, Integer)
        Dim onPlane As New HashSet(Of Integer)

        For Each face As List(Of Integer) In faces
            Dim nv As Integer = face.Count

            If nv < 3 Then
                Continue For
            End If

            Dim clipped As New List(Of Integer)

            For j As Integer = 0 To nv - 1
                Dim u As Integer = face(j)
                Dim v As Integer = face((j + 1) Mod nv)
                Dim du As Double = dist(u)
                Dim dv As Double = dist(v)

                If du <= eps Then
                    onPlane.Add(u)
                    clipped.Add(u)
                ElseIf du > 0 Then
                    clipped.Add(u)
                End If

                ' 跨越平面：插入交点
                If (du > eps AndAlso dv < -eps) OrElse (du < -eps AndAlso dv > eps) Then
                    Dim t As Double = du / (du - dv)
                    Dim p As EulerianVertex = points(u) + (points(v) - points(u)) * t
                    Dim idx As Integer = newPoints.Count
                    newPoints.Add(p)
                    onPlane.Add(idx)
                    clipped.Add(idx)
                End If
            Next

            If clipped.Count < 3 Then
                Continue For
            End If

            ' 面环上相邻的共面点对 => 封盖边界有向边
            For j As Integer = 0 To clipped.Count - 1
                Dim u As Integer = clipped(j)
                Dim v As Integer = clipped((j + 1) Mod clipped.Count)

                If onPlane.Contains(u) AndAlso onPlane.Contains(v) AndAlso
                    std.Abs(dist(u)) <= eps AndAlso std.Abs(dist(v)) <= eps Then

                    capEdges(u) = v
                End If
            Next

            newFaces.Add(clipped)
        Next

        ' ---- 封盖面：沿平面有向边链成环 ----
        Dim visited As New HashSet(Of Integer)

        For Each startVertex In capEdges.Keys
            If visited.Contains(startVertex) Then
                Continue For
            End If

            Dim loop0 As New List(Of Integer)
            Dim cur As Integer = startVertex

            While capEdges.ContainsKey(cur) AndAlso Not visited.Contains(cur)
                visited.Add(cur)
                loop0.Add(cur)
                cur = capEdges(cur)

                If cur = startVertex Then
                    Exit While
                End If
            End While

            ' 环上顶点都在平面上时才是有效封盖
            If loop0.Count >= 3 AndAlso loop0.All(AddressOf onPlane.Contains) Then
                newFaces.Add(New List(Of Integer)(loop0))
            End If
        Next

        If newFaces.Count < 4 Then
            Return Nothing
        End If

        Return New PolyhedronClip With {.Points = newPoints, .Faces = newFaces}
    End Function
End Class
