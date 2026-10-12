Imports System.IO
Imports Microsoft.VisualBasic.ComponentModel.DataStructures
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' 切割平面去重键（法向三元组 + 平面到参考点的有符号距离）。
'''
''' 取代原先"法向 10bit + 距离 10bit 打包成单个 Long"的做法：原键把
''' 不同距离（甚至不同法向）的平面折叠成同一个键，导致本应裁剪的三角形被
''' 判为"重复平面"而跳过 —— 几何边界因而漏切割。此处用全精度量化键消除该类误判。
''' </summary>
Public Structure PlaneKey4 : Implements IEquatable(Of PlaneKey4)

    Public ReadOnly NX As Long
    Public ReadOnly NY As Long
    Public ReadOnly NZ As Long
    Public ReadOnly D As Long

    Sub New(nx As Long, ny As Long, nz As Long, d As Long)
        Me.NX = nx
        Me.NY = ny
        Me.NZ = nz
        Me.D = d
    End Sub

    Public Overloads Function Equals(other As PlaneKey4) As Boolean Implements IEquatable(Of PlaneKey4).Equals
        Return NX = other.NX AndAlso NY = other.NY AndAlso NZ = other.NZ AndAlso D = other.D
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        If TypeOf obj Is PlaneKey4 Then
            Return Equals(DirectCast(obj, PlaneKey4))
        End If

        Return False
    End Function

    Public Overrides Function GetHashCode() As Integer
        Dim h As Long = NX Xor (NY << 21) Xor (NY >> 5) Xor (NZ << 42) Xor (NZ >> 7) Xor (D << 11) Xor (D >> 13)
        Return CInt(h And &H7FFFFFFF) Xor CInt(h >> 32)
    End Function

    Public Overrides Function ToString() As String
        Return $"({NX}, {NY}, {NZ}; {D})"
    End Function
End Structure

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
        Dim gridPoints As New Dictionary(Of GridKey3, Integer)
        Dim leaves As List(Of TightOctreeCell(Of Integer)) = octree.Leaves
        Dim cellSize As Double = maxExtent / (1L << targetDepth)
        Dim eps As Double = cellSize * 0.001
        ' 顶点焊接基准：以域原点为参考、按 1e-4 单元尺度量化。
        ' 相邻八叉树单元各自计算出的共享角点在双精度下可能相差若干 ulp，
        ' 量化焊接把这种差异吸收掉（不焊接 ⇒ 相邻单元的公共面被判成两个边界面，
        ' 网格拓扑断裂）；容差取 1e-4 单元：比任何真实顶点间距小 4 个数量级，
        ' 又比双精度舍入误差大 6 个数量级，兼顾"不误合并"与"不漏焊接"。
        Dim weld As New PointWelding(domain.DomainMin, cellSize * 0.0001)

        For Each leaf As TightOctreeCell(Of Integer) In leaves
            Dim b As BoundingBox3D = leaf.Bounds
            Dim center As New EulerianVertex(
                (b.MinX + b.MaxX) * 0.5,
                (b.MinY + b.MaxY) * 0.5,
                (b.MinZ + b.MaxZ) * 0.5)

            If leaf.IsBoundaryCell Then
                If options.CutCells Then
                    Call emitCutCell(mesh, domain, leaf, gridPoints, weld, eps)
                Else
                    ' IBM 模式：完整六面体，中心在固体内则丢弃
                    If domain.IsSolid(center) Then
                        Continue For
                    End If

                    Call emitHex(mesh, gridPoints, weld, b, "wall")
                End If
            Else
                If domain.IsSolid(center) Then
                    Continue For
                End If

                Call emitHex(mesh, gridPoints, weld, b, "fluid")
            End If
        Next

        Return mesh
    End Function

    ''' <summary>
    ''' 顶点焊接器：按"相对域原点 + 固定物理容差"把空间坐标量化为去重键。
    ''' 所有相邻的六面体/切割单元共享同一张哈希表，保证相邻单元的公共面
    ''' 得到完全相同的顶点环，从而被 <see cref="FvmTopologyBuilder"/> 识别为内部面。
    ''' </summary>
    Private Structure PointWelding

        Private ReadOnly _ox, _oy, _oz As Double
        Private ReadOnly _inv As Double

        Sub New(origin As EulerianVertex, tolerance As Double)
            _ox = origin.X
            _oy = origin.Y
            _oz = origin.Z
            _inv = 1.0 / tolerance
        End Sub

        Public Function Key(x As Double, y As Double, z As Double) As GridKey3
            Return New GridKey3(
                CLng(std.Round((x - _ox) * _inv)),
                CLng(std.Round((y - _oy) * _inv)),
                CLng(std.Round((z - _oz) * _inv)))
        End Function
    End Structure

    ''' <summary>按量化键登记/复用共享顶点。</summary>
    Private Shared Function InternPoint(mesh As RawCellMesh,
                                        gridPoints As Dictionary(Of GridKey3, Integer),
                                        weld As PointWelding,
                                        p As EulerianVertex) As Integer

        Dim key As GridKey3 = weld.Key(p.X, p.Y, p.Z)

        If gridPoints.ContainsKey(key) Then
            Return gridPoints(key)
        End If

        Dim idx As Integer = mesh.AddVertex(p)
        gridPoints(key) = idx
        Return idx
    End Function

    Private Shared Sub emitHex(mesh As RawCellMesh,
                               gridPoints As Dictionary(Of GridKey3, Integer),
                               weld As PointWelding,
                               b As BoundingBox3D, tag As String)
        Dim c As Integer() = {
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MinX, b.MinY, b.MinZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MaxX, b.MinY, b.MinZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MaxX, b.MaxY, b.MinZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MinX, b.MaxY, b.MinZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MinX, b.MinY, b.MaxZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MaxX, b.MinY, b.MaxZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MaxX, b.MaxY, b.MaxZ)),
            InternPoint(mesh, gridPoints, weld, New EulerianVertex(b.MinX, b.MaxY, b.MaxZ))}
        Call mesh.AddHex(tag, c)
    End Sub

    ''' <summary>对边界叶子单元做切割单元裁剪。</summary>
    Private Shared Sub emitCutCell(mesh As RawCellMesh,
                                   domain As GeometryPreprocessor.FluidDomain,
                                   leaf As TightOctreeCell(Of Integer),
                                   gridPoints As Dictionary(Of GridKey3, Integer),
                                   weld As PointWelding,
                                   eps As Double)

        Dim b As BoundingBox3D = leaf.Bounds
        Dim center As New EulerianVertex((b.MinX + b.MaxX) * 0.5, (b.MinY + b.MaxY) * 0.5, (b.MinZ + b.MaxZ) * 0.5)

        ' 单元中心位于固体内部：整单元剔除。
        '
        ' 切割单元的正确语义是"切掉本单元落在固体内部的那一部分"。
        ' 若中心已经在固体内，保留的部分只是一层极薄的碎片壳体 —— 把它当作流体单元
        ' 输出会让整个障碍物的内部重新变成流体（模型等于没有被挖掉）。
        ' cut-cell 的标准做法是整单元丢弃（代价是固体侧出现半个单元的阶梯）。
        '
        ' 注意这也让裁剪不依赖模型的三角面片绕序：被保留下来的单元中心一定在流体侧，
        ' 因此"保留中心所在的一侧"（见下方 keep）对它们恒为正确解。
        If domain.IsSolid(center) Then
            Return
        End If

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
        Dim usedPlanes As New HashSet(Of PlaneKey4)
        Dim clipCount As Integer = 0
        ' 单元外接球半径：平面到单元中心的距离超过该值时，平面与该单元不可能相交
        Dim cellRadius As Double = 0.5 * New EulerianVertex(
            b.MaxX - b.MinX, b.MaxY - b.MinY, b.MaxZ - b.MinZ).Length
        ' 距离量化精度：1e-9 单元尺度（远高于任何真实平面间距，可避免误去重）
        Dim planeInv As Double = 1.0 / (eps * 1.0E-6)

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

            ' 仅当平面确实穿过本单元时才参与裁剪。
            ' 大三角形（如贯穿模型的细长面）的 AABB 会登记到大量与之无关的叶子上，
            ' 若直接用其无限平面裁剪，会把远处单元错误地切掉。
            If std.Abs(d) > cellRadius + eps Then
                Continue For
            End If

            Dim keep As Double = If(d <= 0, -1.0, 1.0)   ' 保留单元中心所在的一侧

            ' 平面去重（法向 + 有符号距离，全精度量化键）
            Dim planeKey As New PlaneKey4(
                CLng(std.Round(n.X * 1000000000.0)),
                CLng(std.Round(n.Y * 1000000000.0)),
                CLng(std.Round(n.Z * 1000000000.0)),
                CLng(std.Round(d * planeInv)))

            If usedPlanes.Contains(planeKey) Then
                Continue For
            End If

            Call usedPlanes.Add(planeKey)

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

        ' 输出多面体单元（局部点映射为全局点）。
        ' 关键：角点与切割交点**统一走全局焊接**。相邻切割单元沿共享棱计算的交点
        ' 若各自登记为新顶点，两个单元的公共面顶点索引不同，拓扑上会被判成两个
        ' 边界面（网格不连通，流场被壁面割裂）。焊接后它们才是真正的内部面。
        Dim cell As New RawCell With {.Tag = "wall-cut"}
        Dim localMap As New Dictionary(Of Integer, Integer)

        For i As Integer = 0 To lp.Count - 1
            localMap(i) = InternPoint(mesh, gridPoints, weld, lp(i))
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

                ' 注意：u / v 可能是本轮新增的交点索引（>= 原始 points.Count），
                ' 它们必然位于裁剪平面上（dist ≡ 0），因此只对原始顶点查 dist 表。
                Dim onU As Boolean = onPlane.Contains(u)
                Dim onV As Boolean = onPlane.Contains(v)

                If onU AndAlso onV AndAlso
                    (u >= dist.Length OrElse std.Abs(dist(u)) <= eps) AndAlso
                    (v >= dist.Length OrElse std.Abs(dist(v)) <= eps) Then

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
