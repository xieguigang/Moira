Imports System.IO
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' 边界层层推进网格生成器。
'''
''' 以模型表面三角网格为初始前沿，沿法向向外（流体侧）逐层推进，
''' 生成三棱柱（prism）边界层单元，用于捕捉壁面附近的强梯度流动：
'''   1. 由三角形面积加权平均计算顶点法向；
'''   2. 层推进法：第 k 层厚度 = firstThickness × growth^(k-1)；
'''   3. 法向碰撞处理：顶点每层推进量受相邻最短边长度限制，
'''      凹角处自动缩短步长以避免层间交叉。
''' </summary>
Public Class BoundaryLayerExtruder : Implements IMeshGenerator

    Public ReadOnly Property Name As String Implements IMeshGenerator.Name
        Get
            Return "boundary-layer"
        End Get
    End Property

    Public Function Generate(options As MeshGenerateOptions) As RawCellMesh Implements IMeshGenerator.Generate
        Dim domain As GeometryPreprocessor.FluidDomain = options.Domain

        If domain.Triangles.Count = 0 Then
            Throw New InvalidDataException("流体域不含三角面片")
        End If

        Dim mesh As New RawCellMesh
        Dim nv As Integer = domain.Vertices.Count

        ' 全局点表：layer 0 = 模型表面顶点
        Dim layerPoints As Integer()() = New Integer(options.BoundaryLayers)() {}
        layerPoints(0) = New Integer(nv - 1) {}

        For v As Integer = 0 To nv - 1
            layerPoints(0)(v) = mesh.AddVertex(domain.Vertices(v))
        Next

        ' ---- 1. 顶点法向（面积加权平均）----
        Dim normals As EulerianVertex() = New EulerianVertex(nv - 1) {}

        For Each tri As Integer() In domain.Triangles
            Dim a As EulerianVertex = domain.Vertices(tri(0))
            Dim b As EulerianVertex = domain.Vertices(tri(1))
            Dim c As EulerianVertex = domain.Vertices(tri(2))
            Dim n As EulerianVertex = EulerianVertex.Cross(b - a, c - a)

            For Each vi As Integer In tri
                normals(vi) = normals(vi) + n
            Next
        Next

        For v As Integer = 0 To nv - 1
            Dim l As Double = normals(v).Length

            If l > 1.0E-30 Then
                normals(v) = normals(v) * (1.0 / l)
            Else
                normals(v) = New EulerianVertex(0, 1, 0)
            End If
        Next

        ' 顶点邻接最短边长（法向碰撞限幅用）
        Dim minEdge As Double() = New Double(nv - 1) {}

        For v As Integer = 0 To nv - 1
            minEdge(v) = Double.MaxValue
        Next

        For Each tri As Integer() In domain.Triangles
            For e As Integer = 0 To 2
                Dim u As Integer = tri(e)
                Dim w As Integer = tri((e + 1) Mod 3)
                Dim len As Double = (domain.Vertices(u) - domain.Vertices(w)).Length

                minEdge(u) = std.Min(minEdge(u), len)
                minEdge(w) = std.Min(minEdge(w), len)
            Next
        Next

        ' ---- 2. 层推进 ----
        Dim modelExtent As Double = (domain.ModelMax - domain.ModelMin).Length
        Dim baseStep As Double = options.FirstLayerThickness * modelExtent

        For layer As Integer = 0 To options.BoundaryLayers - 1
            Dim layerStep As Double = baseStep * std.Pow(options.GrowthRatio, layer)

            ' 推进顶点（碰撞限幅）
            layerPoints(layer + 1) = New Integer(nv - 1) {}

            For v As Integer = 0 To nv - 1
                Dim limit As Double = 0.4 * minEdge(v)
                Dim s As Double = std.Min(layerStep, limit)
                Dim p As EulerianVertex = domain.Vertices(v) + normals(v) * s
                layerPoints(layer + 1)(v) = mesh.AddVertex(p)
            Next

            ' ---- 3. 该层的三棱柱单元（跳过退化三角形）----
            For Each tri As Integer() In domain.Triangles
                If tri(0) = tri(1) OrElse tri(1) = tri(2) OrElse tri(2) = tri(0) Then
                    Continue For
                End If

                Dim ta As EulerianVertex = domain.Vertices(tri(0))
                Dim tb As EulerianVertex = domain.Vertices(tri(1))
                Dim tc As EulerianVertex = domain.Vertices(tri(2))

                If EulerianVertex.Cross(tb - ta, tc - ta).Length < 1.0E-12 * modelExtent Then
                    Continue For
                End If

                Dim b0 As Integer = layerPoints(layer)(tri(0))
                Dim b1 As Integer = layerPoints(layer)(tri(1))
                Dim b2 As Integer = layerPoints(layer)(tri(2))
                Dim t0 As Integer = layerPoints(layer + 1)(tri(0))
                Dim t1 As Integer = layerPoints(layer + 1)(tri(1))
                Dim t2 As Integer = layerPoints(layer + 1)(tri(2))
                Call mesh.AddPrism($"prism-layer{layer + 1}", b0, b1, b2, t0, t1, t2)
            Next
        Next

        Return mesh
    End Function
End Class
