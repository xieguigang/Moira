Imports std = System.Math

Namespace Eulerian

    ''' <summary>
    ''' 网格节点坐标（Double 精度三维点）。
    ''' </summary>
    Public Structure EulerianVertex

        Public X As Double
        Public Y As Double
        Public Z As Double

        Sub New(x As Double, y As Double, z As Double)
            Me.X = x
            Me.Y = y
            Me.Z = z
        End Sub

        Public Shared Operator +(a As EulerianVertex, b As EulerianVertex) As EulerianVertex
            Return New EulerianVertex(a.X + b.X, a.Y + b.Y, a.Z + b.Z)
        End Operator

        Public Shared Operator -(a As EulerianVertex, b As EulerianVertex) As EulerianVertex
            Return New EulerianVertex(a.X - b.X, a.Y - b.Y, a.Z - b.Z)
        End Operator

        Public Shared Operator *(a As EulerianVertex, s As Double) As EulerianVertex
            Return New EulerianVertex(a.X * s, a.Y * s, a.Z * s)
        End Operator

        ''' <summary>向量点积。</summary>
        Public Shared Function Dot(a As EulerianVertex, b As EulerianVertex) As Double
            Return a.X * b.X + a.Y * b.Y + a.Z * b.Z
        End Function

        ''' <summary>向量叉积。</summary>
        Public Shared Function Cross(a As EulerianVertex, b As EulerianVertex) As EulerianVertex
            Return New EulerianVertex(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X)
        End Function

        Public ReadOnly Property Length As Double
            Get
                Return std.Sqrt(X * X + Y * Y + Z * Z)
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"({X.ToString("G6")}, {Y.ToString("G6")}, {Z.ToString("G6")})"
        End Function
    End Structure

    ''' <summary>
    ''' 网格面：多边形面片（顶点索引按外法向逆时针有序排列），
    ''' 附 Owner/Neighbour 单元索引。边界面 Neighbour = -1。
    ''' </summary>
    Public Class EulerianFace

        ''' <summary>构成面的顶点索引（有序，决定面法向）。</summary>
        Public PointIndices As New List(Of Integer)

        ''' <summary>主单元索引。</summary>
        Public Owner As Integer

        ''' <summary>邻单元索引；边界面为 -1。</summary>
        Public Neighbour As Integer = -1

        Public ReadOnly Property IsBoundary As Boolean
            Get
                Return Neighbour < 0
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"face(p={PointIndices.Count}, owner={Owner}, neighbour={Neighbour})"
        End Function
    End Class

    ''' <summary>
    ''' Eulerian（FVM）网格数据模型：OpenFOAM 风格的非邻接列表结构。
    '''
    ''' 核心组成：
    ''' - <see cref="Points"/>：节点坐标；
    ''' - <see cref="Faces"/>：面（多边形，含 Owner/Neighbour 单元索引）；
    ''' - <see cref="CellFaces"/>：单元 → 面索引表（非邻接列表）；
    ''' - <see cref="Patches"/>：边界面片分类；
    ''' - 预计算几何量（<see cref="ComputeGeometry"/>）：面法向量/面积/形心、
    '''   单元体积/形心，供 FVM 通量迭代直接使用。
    ''' </summary>
    Public Class EulerianMesh

        ''' <summary>网格节点。</summary>
        Public Points As New List(Of EulerianVertex)

        ''' <summary>网格面（Owner/Neighbour 拓扑）。</summary>
        Public Faces As New List(Of EulerianFace)

        ''' <summary>每个单元的面索引表（非邻接列表；内部面在两个单元的表中各出现一次）。</summary>
        Public CellFaces As New List(Of Integer())

        ''' <summary>边界面片集合。</summary>
        Public Patches As New List(Of EulerianPatch)

        ' ---- 预计算几何量（ComputeGeometry 填充） ----

        ''' <summary>面法向单位向量（自 Owner 指向 Neighbour；边界面指向外部）。</summary>
        Public FaceNormals As EulerianVertex()
        ''' <summary>面面积。</summary>
        Public FaceAreas As Double()
        ''' <summary>面形心。</summary>
        Public FaceCentroids As EulerianVertex()
        ''' <summary>单元体积。</summary>
        Public CellVolumes As Double()
        ''' <summary>单元形心。</summary>
        Public CellCentroids As EulerianVertex()

        Public ReadOnly Property PointCount As Integer
            Get
                Return Points.Count
            End Get
        End Property

        Public ReadOnly Property FaceCount As Integer
            Get
                Return Faces.Count
            End Get
        End Property

        Public ReadOnly Property CellCount As Integer
            Get
                Return CellFaces.Count
            End Get
        End Property

        ''' <summary>边界面数（Neighbour = -1）。</summary>
        Public ReadOnly Property BoundaryFaceCount As Integer
            Get
                Dim n As Integer = 0

                For Each f As EulerianFace In Faces
                    If f.IsBoundary Then
                        n += 1
                    End If
                Next

                Return n
            End Get
        End Property

        Public ReadOnly Property TotalVolume As Double
            Get
                If CellVolumes Is Nothing Then
                    Return 0
                End If

                Dim v As Double = 0

                For Each x As Double In CellVolumes
                    v += x
                Next

                Return v
            End Get
        End Property

        ''' <summary>网格包围盒（min, max）。</summary>
        Public Sub GetBounds(ByRef min As EulerianVertex, ByRef max As EulerianVertex)
            If Points.Count = 0 Then
                min = New EulerianVertex : max = New EulerianVertex
                Return
            End If

            min = Points(0)
            max = Points(0)

            For Each p As EulerianVertex In Points
                min = New EulerianVertex(std.Min(min.X, p.X), std.Min(min.Y, p.Y), std.Min(min.Z, p.Z))
                max = New EulerianVertex(std.Max(max.X, p.X), std.Max(max.Y, p.Y), std.Max(max.Z, p.Z))
            Next
        End Sub

        ''' <summary>
        ''' 预计算全部几何量：面形心/面积/单位法向、单元体积/形心。
        ''' 法向统一为"自 Owner 指向 Neighbour"（边界面自 Owner 指向单元外部），
        ''' 必要时会翻转面的顶点绕序以保持一致。
        ''' </summary>
        Public Sub ComputeGeometry()
            Dim nf As Integer = Faces.Count
            FaceNormals = New EulerianVertex(nf - 1) {}
            FaceAreas = New Double(nf - 1) {}
            FaceCentroids = New EulerianVertex(nf - 1) {}

            ' 1. 面几何（Newell 方法，适用于任意平面多边形）
            For i As Integer = 0 To nf - 1
                Dim f As EulerianFace = Faces(i)
                Dim nv As Integer = f.PointIndices.Count

                If nv < 3 Then
                    Throw New System.IO.InvalidDataException($"face #{i} has only {nv} points")
                End If

                Dim nx As Double = 0, ny As Double = 0, nz As Double = 0
                Dim cx As Double = 0, cy As Double = 0, cz As Double = 0

                For j As Integer = 0 To nv - 1
                    Dim p As EulerianVertex = Points(f.PointIndices(j))
                    Dim q As EulerianVertex = Points(f.PointIndices((j + 1) Mod nv))

                    ' Newell: n += (p - c) × (q - c) 的简化形式 n += p × q（累积后除以 2 得面积向量）
                    nx += (p.Y - q.Y) * (p.Z + q.Z)
                    ny += (p.Z - q.Z) * (p.X + q.X)
                    nz += (p.X - q.X) * (p.Y + q.Y)
                    cx += p.X : cy += p.Y : cz += p.Z
                Next

                Dim twiceArea As Double = std.Sqrt(nx * nx + ny * ny + nz * nz)
                FaceAreas(i) = 0.5 * twiceArea
                FaceCentroids(i) = New EulerianVertex(cx / nv, cy / nv, cz / nv)

                If twiceArea > 0 Then
                    FaceNormals(i) = New EulerianVertex(nx / twiceArea, ny / twiceArea, nz / twiceArea)
                Else
                    FaceNormals(i) = New EulerianVertex
                End If
            Next

            ' 2. 单元形心初值（面形心平均），用于确定法向朝向
            Dim nc As Integer = CellFaces.Count
            CellCentroids = New EulerianVertex(nc - 1) {}

            For c As Integer = 0 To nc - 1
                Dim sx As Double = 0, sy As Double = 0, sz As Double = 0

                For Each fi As Integer In CellFaces(c)
                    sx += FaceCentroids(fi).X : sy += FaceCentroids(fi).Y : sz += FaceCentroids(fi).Z
                Next

                Dim m As Integer = CellFaces(c).Length

                If m > 0 Then
                    CellCentroids(c) = New EulerianVertex(sx / m, sy / m, sz / m)
                End If
            Next

            ' 3. 法向定向：内部面 Owner→Neighbour，边界面自 Owner 指向外部
            For i As Integer = 0 To nf - 1
                Dim f As EulerianFace = Faces(i)
                Dim dir As EulerianVertex

                If f.IsBoundary OrElse f.Neighbour >= nc Then
                    dir = FaceCentroids(i) - CellCentroids(f.Owner)
                Else
                    dir = CellCentroids(f.Neighbour) - CellCentroids(f.Owner)
                End If

                If EulerianVertex.Dot(FaceNormals(i), dir) < 0 Then
                    ' 翻转法向并同步翻转顶点绕序
                    FaceNormals(i) = FaceNormals(i) * -1.0
                    f.PointIndices.Reverse()
                End If
            Next

            ' 4. 单元体积与形心（散度定理）
            '
            '    V = (1/3) Σ_f (c_f · n̂_f) A_f
            '    centroid = (1/(4V)) Σ_f (c_f · n̂_f) A_f c_f
            CellVolumes = New Double(nc - 1) {}

            For c As Integer = 0 To nc - 1
                Dim vol6 As Double = 0
                Dim wx As Double = 0, wy As Double = 0, wz As Double = 0

                For Each fi As Integer In CellFaces(c)
                    Dim cf As EulerianVertex = FaceCentroids(fi)
                    Dim nf2 As EulerianVertex = FaceNormals(fi)
                    Dim d As Double = EulerianVertex.Dot(cf, nf2) * FaceAreas(fi)

                    vol6 += d
                    wx += d * cf.X : wy += d * cf.Y : wz += d * cf.Z
                Next

                Dim vol As Double = std.Abs(vol6) / 3.0
                CellVolumes(c) = vol

                If vol > 0 Then
                    ' 质心 = ∫x dV / V = (1/4·Σ d·c) / (vol6/3)
                    CellCentroids(c) = New EulerianVertex(3.0 * wx / (4 * vol6), 3.0 * wy / (4 * vol6), 3.0 * wz / (4 * vol6))
                End If
            Next
        End Sub

        Public Overrides Function ToString() As String
            Return $"EulerianMesh(points={PointCount}, faces={FaceCount}, cells={CellCount}, boundary={BoundaryFaceCount})"
        End Function
    End Class
End Namespace
