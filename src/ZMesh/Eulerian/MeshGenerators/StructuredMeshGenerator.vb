Imports System.IO
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

''' <summary>
''' 结构化网格生成器（TFI 超限插值 + Poisson 椭圆方程平滑）。
'''
''' 对计算域六面体块做代数网格生成：
'''   1. TFI（超限插值）：以块角点的三线性插值生成内部节点（块边为直线时 TFI 退化为三线性插值）；
'''   2. 可选 Poisson 平滑（<see cref="StructuredPoissonSmoother"/>，TensorF 七点 stencil 迭代）；
'''   3. 中心在固体内部的六面体单元丢弃，其余输出为流体单元。
''' </summary>
Public Class StructuredMeshGenerator : Implements IMeshGenerator

    ''' <summary>节点数上限（防止分辨率过高导致内存耗尽）。</summary>
    Public Const MaxNodes As Long = 8000000

    Public ReadOnly Property Name As String Implements IMeshGenerator.Name
        Get
            Return "structured-tfi"
        End Get
    End Property

    Public Function Generate(options As MeshGenerateOptions) As RawCellMesh Implements IMeshGenerator.Generate
        Dim domain As GeometryPreprocessor.FluidDomain = options.Domain
        Dim ext As EulerianVertex = domain.DomainSize
        Dim res As Double = options.Resolution

        Dim ni As Integer = CInt(std.Ceiling(ext.X / res)) + 1
        Dim nj As Integer = CInt(std.Ceiling(ext.Y / res)) + 1
        Dim nk As Integer = CInt(std.Ceiling(ext.Z / res)) + 1

        If CLng(ni) * nj * nk > MaxNodes Then
            Throw New InvalidDataException(
                $"结构化网格节点数 {ni}x{nj}x{nk} 超过上限 {MaxNodes}，请增大 Resolution")
        End If

        Dim dmin As EulerianVertex = domain.DomainMin
        Dim dmax As EulerianVertex = domain.DomainMax

        ' ---- 1. TFI：三线性插值生成节点 ----
        Dim nodes As Double() = New Double(ni * nj * nk * 3 - 1) {}

        For i As Integer = 0 To ni - 1
            Dim u As Double = If(ni = 1, 0, CDbl(i) / (ni - 1))

            For j As Integer = 0 To nj - 1
                Dim v As Double = If(nj = 1, 0, CDbl(j) / (nj - 1))

                For k As Integer = 0 To nk - 1
                    Dim w As Double = If(nk = 1, 0, CDbl(k) / (nk - 1))
                    Dim dst As Integer = (i * nj + j) * nk + k
                    nodes(dst * 3 + 0) = dmin.X + u * (dmax.X - dmin.X)
                    nodes(dst * 3 + 1) = dmin.Y + v * (dmax.Y - dmin.Y)
                    nodes(dst * 3 + 2) = dmin.Z + w * (dmax.Z - dmin.Z)
                Next
            Next
        Next

        ' ---- 2. Poisson 平滑（TensorF 迭代）----
        If options.PoissonIterations > 0 Then
            ' 三个坐标分量分别平滑（边界钳制）
            Dim cx As Double() = New Double(ni * nj * nk - 1) {}
            Dim cy As Double() = New Double(ni * nj * nk - 1) {}
            Dim cz As Double() = New Double(ni * nj * nk - 1) {}

            For n As Integer = 0 To ni * nj * nk - 1
                cx(n) = nodes(n * 3 + 0)
                cy(n) = nodes(n * 3 + 1)
                cz(n) = nodes(n * 3 + 2)
            Next

            Call StructuredPoissonSmoother.Smooth(cx, ni, nj, nk, options.PoissonIterations)
            Call StructuredPoissonSmoother.Smooth(cy, ni, nj, nk, options.PoissonIterations)
            Call StructuredPoissonSmoother.Smooth(cz, ni, nj, nk, options.PoissonIterations)

            For n As Integer = 0 To ni * nj * nk - 1
                nodes(n * 3 + 0) = cx(n)
                nodes(n * 3 + 1) = cy(n)
                nodes(n * 3 + 2) = cz(n)
            Next
        End If

        ' ---- 3. 六面体单元（固体中心丢弃）----
        Dim mesh As New RawCellMesh
        Dim pid As Integer()() = New Integer(ni - 1)() {}

        For i As Integer = 0 To ni - 1
            pid(i) = New Integer(nj * nk - 1) {}

            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    Dim src As Integer = (i * nj + j) * nk + k
                    pid(i)(j * nk + k) = mesh.AddVertex(
                        New EulerianVertex(nodes(src * 3), nodes(src * 3 + 1), nodes(src * 3 + 2)))
                Next
            Next
        Next

        For i As Integer = 0 To ni - 2
            For j As Integer = 0 To nj - 2
                For k As Integer = 0 To nk - 2
                    Dim c As Integer() = {
                        pid(i)(j * nk + k),
                        pid(i + 1)(j * nk + k),
                        pid(i + 1)((j + 1) * nk + k),
                        pid(i)((j + 1) * nk + k),
                        pid(i)(j * nk + k + 1),
                        pid(i + 1)(j * nk + k + 1),
                        pid(i + 1)((j + 1) * nk + k + 1),
                        pid(i)((j + 1) * nk + k + 1)}

                    Dim center As EulerianVertex =
                        (mesh.Points(c(0)) + mesh.Points(c(1)) + mesh.Points(c(2)) + mesh.Points(c(3)) +
                         mesh.Points(c(4)) + mesh.Points(c(5)) + mesh.Points(c(6)) + mesh.Points(c(7))) * 0.125

                    If domain.IsSolid(center) Then
                        Continue For
                    End If

                    Call mesh.AddHex("fluid", c)
                Next
            Next
        Next

        Return mesh
    End Function
End Class
