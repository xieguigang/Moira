' /********************************************************************************/
'
'   GeometryPreprocessor.vb
'
'   Eulerian 网格流水线的几何预处理：
'       三维模型文件 --ModelLoader.LoadModel--> SceneModel
'                   --> 三角面片抽取 + 容差顶点缝合
'                   --> 体素化固体场（快速点分类）
'                   --> 流体计算域（包围盒放大 + 地面净空）
'
' /********************************************************************************/

Imports System.IO
Imports Microsoft.VisualBasic.Imaging.Landscape.Data
Imports LxVoxel = Microsoft.VisualBasic.Imaging.Landscape.Voxelization.VoxelModel
Imports LxVoxelizer = Microsoft.VisualBasic.Imaging.Landscape.Voxelization.Voxelizer
Imports LxPoint3D = Microsoft.VisualBasic.Imaging.Drawing3D.Point3D
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' 几何预处理器：将三维模型文件转换为 Eulerian 网格生成所需的
''' 流体域描述 <see cref="FluidDomain"/>。
''' </summary>
Public Class GeometryPreprocessor

    ''' <summary>
    ''' 基于体素场的固体/流体分类器（Landscape 体素化结果，True = 固体）。
    ''' </summary>
    Public Class SolidField

        Private ReadOnly _solid As LxVoxel
        Private ReadOnly _inv As Double

        Public Sub New(solid As LxVoxel)
            _solid = solid
            _inv = 1.0 / solid.VoxelSize
        End Sub

        Public ReadOnly Property VoxelSize As Double
            Get
                Return _solid.VoxelSize
            End Get
        End Property

        Public ReadOnly Property MinX As Double
            Get
                Return _solid.MinX
            End Get
        End Property

        Public ReadOnly Property MinY As Double
            Get
                Return _solid.MinY
            End Get
        End Property

        Public ReadOnly Property MinZ As Double
            Get
                Return _solid.MinZ
            End Get
        End Property

        ''' <summary>世界坐标点是否位于固体内部（体素网格外的点视为流体）。</summary>
        Public Function IsSolid(x As Double, y As Double, z As Double) As Boolean
            Dim i As Integer = CInt((x - _solid.MinX) * _inv)
            Dim j As Integer = CInt((y - _solid.MinY) * _inv)
            Dim k As Integer = CInt((z - _solid.MinZ) * _inv)

            If i < 0 OrElse j < 0 OrElse k < 0 OrElse
                i >= _solid.Width OrElse j >= _solid.Height OrElse k >= _solid.Depth Then
                Return False
            End If

            Return _solid.Cell(i, j, k)
        End Function
    End Class

    ''' <summary>
    ''' 流体计算域：预处理结果，供各网格生成器消费。
    ''' </summary>
    Public Class FluidDomain

        ''' <summary>缝合后的顶点表。</summary>
        Public Vertices As New List(Of EulerianVertex)

        ''' <summary>三角面片（顶点索引三元组，绕序继承模型文件）。</summary>
        Public Triangles As New List(Of Integer())

        ''' <summary>模型包围盒（原始模型坐标）。</summary>
        Public ModelMin, ModelMax As EulerianVertex

        ''' <summary>流体计算域包围盒（放大后的外层风场域）。</summary>
        Public DomainMin, DomainMax As EulerianVertex

        ''' <summary>固体/流体分类场。</summary>
        Public Solid As SolidField

        ''' <summary>源模型文件路径。</summary>
        Public SourceFile As String

        ''' <summary>容差缝合合并的重复顶点数。</summary>
        Public MergedVertices As Integer

        ''' <summary>域中心与三向尺寸。</summary>
        Public ReadOnly Property DomainSize As EulerianVertex
            Get
                Return DomainMax - DomainMin
            End Get
        End Property

        ''' <summary>点是否在固体内部（基于体素场）。</summary>
        Public Function IsSolid(p As EulerianVertex) As Boolean
            Return Solid.IsSolid(p.X, p.Y, p.Z)
        End Function

        Public Overrides Function ToString() As String
            Return $"FluidDomain(verts={Vertices.Count}, tris={Triangles.Count}, domain={DomainMin.ToString}..{DomainMax.ToString})"
        End Function
    End Class

    ''' <summary>
    ''' 从三维模型文件构建流体计算域。
    ''' </summary>
    ''' <param name="filePath">模型文件（STL/OBJ/glTF/GLB/DAE/3DS/3MF）。</param>
    ''' <param name="domainScale">计算域相对模型包围盒的放大倍数（&gt;= 1）。</param>
    ''' <param name="groundClearance">地面净空（模型底部与计算域底面的间距比例，相对模型高度）。</param>
    ''' <param name="voxelResolution">固体分类场的体素分辨率（最长边）。</param>
    ''' <param name="stitchTolerance">容差缝合容差（相对模型最大边长的比例，0 关闭缝合）。</param>
    Public Shared Function FromModelFile(filePath As String,
                                         Optional domainScale As Double = 2.0,
                                         Optional groundClearance As Double = 0.1,
                                         Optional voxelResolution As Integer = 128,
                                         Optional stitchTolerance As Double = 0.0001) As FluidDomain

        If Not File.Exists(filePath) Then
            Throw New FileNotFoundException($"找不到模型文件：{filePath}", filePath)
        End If

        Dim scene As SceneModel

        If ModelLoader.DetectFormat(filePath) = ModelFormat._3MF Then
            scene = Load3mfScene(filePath)
        Else
            scene = filePath.LoadModel
        End If

        If scene Is Nothing OrElse scene.Surfaces Is Nothing OrElse scene.Surfaces.Length = 0 Then
            Throw New InvalidDataException($"模型加载失败或不含几何表面：{filePath}")
        End If

        Dim domain As New FluidDomain With {.SourceFile = filePath}

        ' ---- 1. 三角面片抽取 + 容差顶点缝合 ----
        Call ExtractTriangles(scene, domain, stitchTolerance)

        ' ---- 2. 模型包围盒 ----
        Dim bmin As New EulerianVertex(Double.MaxValue, Double.MaxValue, Double.MaxValue)
        Dim bmax As New EulerianVertex(Double.MinValue, Double.MinValue, Double.MinValue)

        For Each p As EulerianVertex In domain.Vertices
            bmin = New EulerianVertex(std.Min(bmin.X, p.X), std.Min(bmin.Y, p.Y), std.Min(bmin.Z, p.Z))
            bmax = New EulerianVertex(std.Max(bmax.X, p.X), std.Max(bmax.Y, p.Y), std.Max(bmax.Z, p.Z))
        Next

        domain.ModelMin = bmin
        domain.ModelMax = bmax

        ' ---- 3. 计算域：绕中心放大 + 地面净空 ----
        Dim size As EulerianVertex = bmax - bmin
        Dim center As EulerianVertex = (bmin + bmax) * 0.5
        Dim half As EulerianVertex = New EulerianVertex(
            size.X * domainScale * 0.5,
            size.Y * domainScale * 0.5,
            size.Z * domainScale * 0.5)
        Dim ground As Double = size.Y * groundClearance

        domain.DomainMin = New EulerianVertex(center.X - half.X, bmin.Y - ground, center.Z - half.Z)
        domain.DomainMax = New EulerianVertex(center.X + half.X, bmax.Y + size.Y * 0.5, center.Z + half.Z)

        ' ---- 4. 固体分类场（Landscape 体素化，True = 固体）----
        Dim lx As LxVoxel = LxVoxelizer.Voxelize(scene, voxelResolution)

        If lx Is Nothing OrElse lx.Shape Is Nothing Then
            Throw New InvalidDataException($"模型体素化失败：{filePath}")
        End If

        domain.Solid = New SolidField(lx)
        Return domain
    End Function

    ''' <summary>从场景抽取三角面片并做容差缝合。</summary>
    Private Shared Sub ExtractTriangles(scene As SceneModel, domain As FluidDomain, stitchTolerance As Double)
        ' 顶点哈希缝合表：量化坐标 -> 顶点索引
        Dim quant As New Dictionary(Of Long, Integer)

        For Each surface As Surface In scene.Surfaces
            If surface.vertices Is Nothing OrElse surface.vertices.Length < 3 Then
                Continue For
            End If

            Dim pts As List(Of LxPoint3D) = surface.vertices.Select(Function(v) v.PointData).ToList

            ' 多边形按三角扇分解（模型文件以三角形为主）：p0, pt, pt+1
            Dim i0 As Integer = InternVertex(domain, quant, pts(0).X, pts(0).Y, pts(0).Z, stitchTolerance)

            For t As Integer = 1 To pts.Count - 2
                Dim p1 As LxPoint3D = pts(t)
                Dim p2 As LxPoint3D = pts(t + 1)
                domain.Triangles.Add({
                    i0,
                    InternVertex(domain, quant, p1.X, p1.Y, p1.Z, stitchTolerance),
                    InternVertex(domain, quant, p2.X, p2.Y, p2.Z, stitchTolerance)})
            Next
        Next
    End Sub

    ''' <summary>登记顶点（容差缝合：量化栅格内已存在的点复用索引）。</summary>
    Private Shared Function InternVertex(domain As FluidDomain,
                                         quant As Dictionary(Of Long, Integer),
                                         x As Double, y As Double, z As Double,
                                         stitchTolerance As Double) As Integer

        If stitchTolerance > 0 Then
            Dim key As Long = QuantKey(x, y, z, stitchTolerance)

            If quant.ContainsKey(key) Then
                domain.MergedVertices += 1
                Return quant(key)
            End If

            Dim idx As Integer = domain.Vertices.Count
            domain.Vertices.Add(New EulerianVertex(x, y, z))
            quant(key) = idx
            Return idx
        Else
            domain.Vertices.Add(New EulerianVertex(x, y, z))
            Return domain.Vertices.Count - 1
        End If
    End Function

    ''' <summary>坐标量化为哈希键（容差缝合的近似最近查找）。</summary>
    Private Shared Function QuantKey(x As Double, y As Double, z As Double, tol As Double) As Long
        Dim s As Double = 1.0 / tol
        Dim ux As Long = CLng(std.Round(x * s)) And &H1FFFFF
        Dim uy As Long = CLng(std.Round(y * s)) And &H1FFFFF
        Dim uz As Long = CLng(std.Round(z * s)) And &H1FFFFF
        Return ux Or (uy << 21) Or (uz << 42)
    End Function
End Class
