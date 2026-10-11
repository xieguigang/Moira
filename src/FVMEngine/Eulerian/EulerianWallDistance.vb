' /********************************************************************************/
'
'   EulerianWallDistance.vb
'
'   壁面距离场 —— k-ω SST 的混合函数 F1/F2 与壁面函数（对数律）的基础输入。
'
'   需求：
'       · 每个单元到最近壁面的距离 y；
'       · 每个壁面相邻单元的**壁面法向垂直距离** y_wall（y+ 与对数律都用它，
'         而不是到壁面形心的欧氏距离，否则斜置壁面会系统性高估 y+）；
'       · 单元是否与壁面相邻（决定是否施加壁面函数）。
'
'   算法（自包含，不依赖外部空间索引组件）：
'       1. 收集壁面面（patch 类型为 Wall 的边界面，含合成 patch defaultFaces）；
'       2. 以"平均单元尺寸"为边长建立均匀哈希网格，把壁面形心装箱；
'       3. 对每个单元按 Chebyshev 半径递增搜索箱壳，
'          当 'r·h > 当前最优距离' 时提前终止 —— 该判据保证最近邻搜索的正确性；
'       4. 垂直距离由壁面法向投影得到：y = |(x_P − x_f)·n̂_f|，
'          近零（切向掠过的面）时退化为欧氏距离；
'       5. 兜底：超出最大搜索半径的单元（如深腔内部）取包围盒对角线，
'          保证 F1/F2 混合函数不会因 y → 0 而产生除零。
'
'   混合精度契约：本文件全部为 double（一次性几何预处理，非热点）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

    ''' <summary>壁面距离场（单元 → 最近壁面距离 / 垂直壁距 / 是否壁面相邻）。</summary>
    Public Class EulerianWallDistance

#Region "数据"

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>参与"壁面"统计的边界面索引（patch 类型为 Wall）。</summary>
        Public ReadOnly Property WallFaces As Integer()

        ''' <summary>单元 → 到最近壁面形心的距离。</summary>
        Public ReadOnly Property Distance As Double()
            Get
                Return _distance
            End Get
        End Property

        ''' <summary>单元 → 最近壁面面索引（未找到时为 −1）。</summary>
        Public ReadOnly Property NearestFace As Integer()
            Get
                Return _nearest
            End Get
        End Property

        ''' <summary>单元 → 是否与壁面相邻（是某个壁面的 Owner）。</summary>
        Public ReadOnly Property IsWallAdjacent As Boolean()
            Get
                Return _wallAdjacent
            End Get
        End Property

        ''' <summary>单元 → 壁面法向垂直距离（非壁面相邻单元为该单元的最近壁距）。</summary>
        Public ReadOnly Property WallDistanceNormal As Double()
            Get
                Return _wallY
            End Get
        End Property

        ''' <summary>最大壁距（兜底值，用于限幅）。</summary>
        Public ReadOnly Property MaxDistance As Double
            Get
                Return _maxDistance
            End Get
        End Property

        ''' <summary>壁面相邻单元数。</summary>
        Public ReadOnly Property WallAdjacentCellCount As Integer
            Get
                Return _wallAdjacentCount
            End Get
        End Property

        Public ReadOnly Property CellCount As Integer
            Get
                Return _distance.Length
            End Get
        End Property

        Private ReadOnly _distance As Double()
        Private ReadOnly _nearest As Integer()
        Private ReadOnly _wallAdjacent As Boolean()
        Private ReadOnly _wallY As Double()
        Private ReadOnly _maxDistance As Double
        Private ReadOnly _wallAdjacentCount As Integer

#End Region

#Region "构造"

        Private Sub New(mesh As EulerianFvMesh,
                        wallFaces As Integer(),
                        distance As Double(),
                        nearest As Integer(),
                        wallAdjacent As Boolean(),
                        wallY As Double(),
                        maxDistance As Double,
                        wallAdjacentCount As Integer,
                        boundingDiagonal As Double)

            Me.Mesh = mesh
            Me.WallFaces = wallFaces
            Me._distance = distance
            Me._nearest = nearest
            Me._wallAdjacent = wallAdjacent
            Me._wallY = wallY
            Me._maxDistance = maxDistance
            Me._wallAdjacentCount = wallAdjacentCount
            Me.BoundingDiagonal = boundingDiagonal
        End Sub

        ''' <summary>网格包围盒对角线长度。</summary>
        Public ReadOnly Property BoundingDiagonal As Double

#End Region

#Region "构建"

        ''' <summary>
        ''' 构建壁面距离场。默认把 patch 类型为 <see cref="EulerianPatchType.Wall"/>
        ''' 的边界面视为壁面；可用 <paramref name="patchNames"/> 显式指定参与统计的 patch 名。
        ''' </summary>
        Public Shared Function Build(mesh As EulerianFvMesh,
                                     Optional patchNames As IEnumerable(Of String) = Nothing) As EulerianWallDistance

            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            Dim n As Integer = mesh.CellCount
            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre
            Dim cf As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.Cf

            ' ---- 1. 收集壁面 ----
            Dim explicitSet As HashSet(Of Integer) = Nothing

            If patchNames IsNot Nothing Then
                explicitSet = New HashSet(Of Integer)

                For Each nm As String In patchNames
                    Dim pi As Integer = mesh.FindPatch(nm)

                    If pi >= 0 Then
                        Call explicitSet.Add(pi)
                    End If
                Next
            End If

            Dim wallFaces As New List(Of Integer)

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim include As Boolean

                If explicitSet IsNot Nothing Then
                    include = explicitSet.Contains(pi)
                Else
                    include = geo.PatchType = EulerianPatchType.Wall
                End If

                If include Then
                    For Each f As Integer In geo.FaceIndices
                        wallFaces.Add(f)
                    Next
                End If
            Next

            Dim wallArr As Integer() = wallFaces.ToArray
            Dim distance(n - 1) As Double
            Dim nearest(n - 1) As Integer
            Dim wallAdjacent(n - 1) As Boolean
            Dim wallY(n - 1) As Double

            For c As Integer = 0 To n - 1
                nearest(c) = -1
            Next

            ' ---- 包围盒 ----
            Dim bmin As EulerianVertex = Nothing
            Dim bmax As EulerianVertex = Nothing

            Call mesh.GetBounds(bmin, bmax)

            Dim diag As Double = (bmax - bmin).Length
            Dim maxDist As Double = If(diag > 0, diag, 1.0)
            Dim wallCount As Integer = 0

            If wallArr.Length = 0 Then
                ' 无壁面（例如全滑移 / 周期域）：距离统一取对角线，避免 0 距离
                For c As Integer = 0 To n - 1
                    distance(c) = maxDist
                    wallY(c) = maxDist
                Next

                Return New EulerianWallDistance(mesh, wallArr, distance, nearest, wallAdjacent, wallY, maxDist, 0, diag)
            End If

            ' ---- 2. 均匀哈希网格（边长 = 2 倍平均单元尺度） ----
            Dim meanCell As Double = std.Pow(std.Max(mesh.TotalVolume, 1.0E-300) / std.Max(1, n), 1.0 / 3.0)
            Dim h As Double = std.Max(meanCell * 2.0, diag * 1.0E-4)

            Dim nx As Integer = std.Max(1, CInt(std.Floor((bmax.X - bmin.X) / h)) + 1)
            Dim ny As Integer = std.Max(1, CInt(std.Floor((bmax.Y - bmin.Y) / h)) + 1)
            Dim nz As Integer = std.Max(1, CInt(std.Floor((bmax.Z - bmin.Z) / h)) + 1)

            ' 限制总箱数，避免超大域 + 超细网格导致的内存爆炸
            Const MaxBins As Long = 4000000

            While CLng(nx) * CLng(ny) * CLng(nz) > MaxBins
                h *= 2.0
                nx = std.Max(1, CInt(std.Floor((bmax.X - bmin.X) / h)) + 1)
                ny = std.Max(1, CInt(std.Floor((bmax.Y - bmin.Y) / h)) + 1)
                nz = std.Max(1, CInt(std.Floor((bmax.Z - bmin.Z) / h)) + 1)
            End While

            Dim bins As New Dictionary(Of Long, List(Of Integer))(wallArr.Length * 2)

            For Each f As Integer In wallArr
                Dim ix, iy, iz As Integer
                Call BinOf(cf(f), bmin, h, nx, ny, nz, ix, iy, iz)

                Dim key As Long = BinKey(ix, iy, iz, ny, nz)
                Dim bucket As List(Of Integer) = Nothing

                If Not bins.TryGetValue(key, bucket) Then
                    bucket = New List(Of Integer)
                    bins(key) = bucket
                End If

                bucket.Add(f)
            Next

            ' ---- 3. 逐单元最近邻搜索 ----
            Dim maxRing As Integer = std.Max(nx, std.Max(ny, nz))
            Dim h2 As Double = h * h
            Dim foundMax As Double = 0

            For c As Integer = 0 To n - 1
                Dim p As Moira.CFDEngine.Eulerian.EulerianVertex = cc(c)
                Dim best As Double = Double.MaxValue
                Dim bestFace As Integer = -1

                Dim ix, iy, iz As Integer
                Call BinOf(p, bmin, h, nx, ny, nz, ix, iy, iz)

                For r As Integer = 0 To maxRing
                    ' 提前终止：本壳及更远壳内不可能有更近的面
                    If best < Double.MaxValue AndAlso CDbl(r) * h > best Then
                        Exit For
                    End If

                    Dim di0 As Integer = std.Max(-r, 0 - ix)
                    Dim di1 As Integer = std.Min(r, nx - 1 - ix)

                    For di As Integer = di0 To di1
                        Dim dj0 As Integer = std.Max(-r, 0 - iy)
                        Dim dj1 As Integer = std.Min(r, ny - 1 - iy)

                        For dj As Integer = dj0 To dj1
                            Dim dk0 As Integer = std.Max(-r, 0 - iz)
                            Dim dk1 As Integer = std.Min(r, nz - 1 - iz)

                            For dk As Integer = dk0 To dk1
                                If std.Max(std.Abs(di), std.Max(std.Abs(dj), std.Abs(dk))) <> r Then
                                    Continue For
                                End If

                                Dim bucket As List(Of Integer) = Nothing

                                If Not bins.TryGetValue(BinKey(ix + di, iy + dj, iz + dk, ny, nz), bucket) Then
                                    Continue For
                                End If

                                For Each f As Integer In bucket
                                    Dim ddx As Double = cf(f).X - p.X
                                    Dim ddy As Double = cf(f).Y - p.Y
                                    Dim ddz As Double = cf(f).Z - p.Z
                                    Dim dd As Double = ddx * ddx + ddy * ddy + ddz * ddz

                                    If dd < best Then
                                        best = dd
                                        bestFace = f
                                    End If
                                Next
                            Next
                        Next
                    Next
                Next

                If bestFace < 0 Then
                    ' 兜底：未在搜索范围内找到壁面
                    distance(c) = maxDist
                    nearest(c) = -1
                    wallY(c) = maxDist
                Else
                    Dim d As Double = std.Sqrt(best)

                    distance(c) = d
                    nearest(c) = bestFace
                    wallY(c) = d

                    If d > foundMax Then foundMax = d
                End If
            Next

            ' ---- 4. 壁面相邻单元的垂直壁距 ----
            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                If explicitSet IsNot Nothing Then
                    If Not explicitSet.Contains(pi) Then
                        Continue For
                    End If
                ElseIf geo.PatchType <> EulerianPatchType.Wall Then
                    Continue For
                End If

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k).Normalized()
                    Dim apex As Moira.CFDEngine.Eulerian.EulerianVertex = cc(own)
                    Dim dvec As Moira.CFDEngine.Eulerian.EulerianVertex = apex - cf(f)

                    Dim perp As Double = std.Abs(dvec.X * nrm.X + dvec.Y * nrm.Y + dvec.Z * nrm.Z)

                    If perp <= 0 Then
                        perp = dvec.Length
                    End If

                    If perp <= 0 Then
                        perp = std.Sqrt(h2) * 0.5
                    End If

                    wallAdjacent(own) = True

                    If wallY(own) <= 0 OrElse perp < wallY(own) Then
                        wallY(own) = perp
                    End If
                Next
            Next

            For c As Integer = 0 To n - 1
                If wallAdjacent(c) Then
                    wallCount += 1
                End If
            Next

            Return New EulerianWallDistance(mesh, wallArr, distance, nearest, wallAdjacent, wallY, maxDist, wallCount, diag)
        End Function

        Private Shared Sub BinOf(p As Moira.CFDEngine.Eulerian.EulerianVertex,
                                 bmin As Moira.CFDEngine.Eulerian.EulerianVertex,
                                 h As Double, nx As Integer, ny As Integer, nz As Integer,
                                 ByRef ix As Integer, ByRef iy As Integer, ByRef iz As Integer)

            ix = ClampBin(CInt(std.Floor((p.X - bmin.X) / h)), nx)
            iy = ClampBin(CInt(std.Floor((p.Y - bmin.Y) / h)), ny)
            iz = ClampBin(CInt(std.Floor((p.Z - bmin.Z) / h)), nz)
        End Sub

        Private Shared Function ClampBin(v As Integer, n As Integer) As Integer
            If v < 0 Then Return 0
            If v >= n Then Return n - 1
            Return v
        End Function

        Private Shared Function BinKey(ix As Integer, iy As Integer, iz As Integer,
                                       ny As Integer, nz As Integer) As Long

            Return (CLng(ix) * CLng(ny) + CLng(iy)) * CLng(nz) + CLng(iz)
        End Function

#End Region

        ''' <summary>诊断摘要。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                If _distance.Length = 0 Then Return "wall distance: empty"

                Dim dmin As Double = Double.MaxValue
                Dim dsum As Double = 0

                For c As Integer = 0 To _distance.Length - 1
                    Dim d As Double = _distance(c)

                    If d < dmin Then dmin = d
                    dsum += d
                Next

                Return $"wall distance: wallFaces={WallFaces.Length}, wallAdjacentCells={_wallAdjacentCount}/{_distance.Length}, " &
                       $"min={dmin.ToString("G6", CultureInfo.InvariantCulture)}, " &
                       $"mean={(dsum / _distance.Length).ToString("G6", CultureInfo.InvariantCulture)}, " &
                       $"max={_maxDistance.ToString("G6", CultureInfo.InvariantCulture)}"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"EulerianWallDistance(cells={_distance.Length}, wallFaces={WallFaces.Length})"
        End Function

    End Class

End Namespace
