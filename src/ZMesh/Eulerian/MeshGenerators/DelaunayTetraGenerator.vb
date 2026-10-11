Imports System.IO
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D.Math3D.Delaunay3D
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' Delaunay 非结构四面体网格生成器。
'''
''' 流程：
'''   1. 点集 = 模型表面顶点（容差缝合后）+ 计算域背景栅格填充点
'''      （间距 = Resolution，跳过固体内部的点，受 MaxFillPoints 上限保护）；
'''   2. <see cref="DelaunayTriangulation3D.Triangulate"/> 做 Bowyer-Watson 逐点插入四面体化；
'''   3. 质心落在固体内部的四面体丢弃，其余输出为流体四面体单元。
''' </summary>
Public Class DelaunayTetraGenerator : Implements IMeshGenerator

    Public ReadOnly Property Name As String Implements IMeshGenerator.Name
        Get
            Return "delaunay-tetra"
        End Get
    End Property

    Public Function Generate(options As MeshGenerateOptions) As RawCellMesh Implements IMeshGenerator.Generate
        Dim domain As GeometryPreprocessor.FluidDomain = options.Domain
        Dim points As New List(Of Point3D)
        Dim indexMap As New Dictionary(Of Integer, Integer)

        ' ---- 1a. 模型表面顶点 ----
        For v As Integer = 0 To domain.Vertices.Count - 1
            Dim p As EulerianVertex = domain.Vertices(v)
            indexMap(v) = points.Count
            points.Add(New Point3D(p.X, p.Y, p.Z))
        Next

        ' ---- 1b. 计算域背景栅格填充点（固体内部跳过）----
        Dim dmin As EulerianVertex = domain.DomainMin
        Dim dmax As EulerianVertex = domain.DomainMax
        Dim res As Double = options.Resolution
        Dim nxFill As Integer = CInt(std.Ceiling((dmax.X - dmin.X) / res)) - 1
        Dim nyFill As Integer = CInt(std.Ceiling((dmax.Y - dmin.Y) / res)) - 1
        Dim nzFill As Integer = CInt(std.Ceiling((dmax.Z - dmin.Z) / res)) - 1
        Dim fillCount As Long = CLng(nxFill) * nyFill * nzFill
        Dim jitter As New Random(2026)

        If fillCount > options.MaxFillPoints Then
            Throw New InvalidDataException(
                $"填充点数 {fillCount} 超过上限 {options.MaxFillPoints}，请增大 Resolution 或 MaxFillPoints")
        End If

        For i As Integer = 1 To nxFill
            For j As Integer = 1 To nyFill
                For k As Integer = 1 To nzFill
                    Dim x As Double = dmin.X + i * res + (jitter.NextDouble - 0.5) * res * 0.2
                    Dim y As Double = dmin.Y + j * res + (jitter.NextDouble - 0.5) * res * 0.2
                    Dim z As Double = dmin.Z + k * res + (jitter.NextDouble - 0.5) * res * 0.2

                    If domain.IsSolid(x, y, z) Then
                        Continue For
                    End If

                    points.Add(New Point3D(x, y, z))
                Next
            Next
        Next

        If points.Count < 8 Then
            Throw New InvalidDataException("点云数量过少，无法做四面体化")
        End If

        ' ---- 2. 三维 Delaunay 四面体化 ----
        Dim tets As List(Of TetraIndices) = DelaunayTriangulation3D.Triangulate(points)
        Dim mesh As New RawCellMesh

        ' 全局点表（含填充点）
        For Each p As Point3D In points
            Call mesh.AddVertex(New EulerianVertex(p.X, p.Y, p.Z))
        Next

        ' ---- 3. 固体内部四面体过滤 ----
        Dim dropped As Integer = 0

        For Each t As TetraIndices In tets
            Dim c As EulerianVertex = (mesh.Points(t.A) + mesh.Points(t.B) + mesh.Points(t.C) + mesh.Points(t.D)) * 0.25

            If domain.IsSolid(c) Then
                dropped += 1
                Continue For
            End If

            Call mesh.AddTet("fluid", t.A, t.B, t.C, t.D)
        Next

        Return mesh
    End Function
End Class
