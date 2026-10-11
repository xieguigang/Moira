Imports Moira.CFDEngine.Eulerian

''' <summary>
''' 网格生成选项。
''' </summary>
Public Class MeshGenerateOptions

    ''' <summary>预处理得到的流体计算域。</summary>
    Public Domain As GeometryPreprocessor.FluidDomain

    ''' <summary>目标网格尺度（最长边方向的目标单元尺寸，世界坐标单位）。</summary>
    Public Resolution As Double = 0.1

    ''' <summary>八叉树网格：True = 均匀细分（RefineToUniform），False = 仅边界自适应细分。</summary>
    Public UniformOctree As Boolean = True

    ''' <summary>八叉树网格：True = 对边界单元做平面切割（cut-cell 多面体），False = 保留完整六面体（IBM）。</summary>
    Public CutCells As Boolean = False

    ''' <summary>Delaunay 网格：体填充点数上限（防止计算域点云爆炸）。</summary>
    Public MaxFillPoints As Integer = 200000

    ''' <summary>结构化网格：Poisson 平滑迭代次数（0 = 不平滑）。</summary>
    Public PoissonIterations As Integer = 0

    ''' <summary>边界层网格：层数。</summary>
    Public BoundaryLayers As Integer = 5

    ''' <summary>边界层网格：第一层厚度（相对模型最大边长的比例）。</summary>
    Public FirstLayerThickness As Double = 0.002

    ''' <summary>边界层网格：层间增长率。</summary>
    Public GrowthRatio As Double = 1.2
End Class

''' <summary>
''' 中间网格单元：一个多面体单元 = 若干有序顶点环构成的面。
''' 面顶点绕序约定：从单元外部看为逆时针（即绕序隐含外法向）。
''' </summary>
Public Class RawCell

    ''' <summary>构成单元的面集合。</summary>
    Public Faces As New List(Of RawFace)

    ''' <summary>单元标记（生成器自定义，如 "fluid" / "wall-cut" / "prism-layer2"）。</summary>
    Public Tag As String = "fluid"

    Public Overrides Function ToString() As String
        Return $"cell({Faces.Count} faces, {Tag})"
    End Function
End Class

''' <summary>中间网格面：有序顶点索引环。</summary>
Public Class RawFace

    Public PointIndices As New List(Of Integer)

    Public Sub New()
    End Sub

    Public Sub New(indices As IEnumerable(Of Integer))
        PointIndices.AddRange(indices)
    End Sub
End Class

''' <summary>
''' 网格生成器统一接口：各算法体系（八叉树/Delaunay/结构化/边界层）
''' 产出统一的 <see cref="RawCellMesh"/>，由 FvmTopologyBuilder 转换为
''' OpenFOAM 风格的 <see cref="EulerianMesh"/>。
''' </summary>
Public Interface IMeshGenerator

    ''' <summary>生成器名称。</summary>
    ReadOnly Property Name As String

    ''' <summary>执行网格生成。</summary>
    Function Generate(options As MeshGenerateOptions) As RawCellMesh
End Interface

''' <summary>中间网格模型：点 + 多面体单元集合。</summary>
Public Class RawCellMesh

    Public Points As New List(Of EulerianVertex)
    Public Cells As New List(Of RawCell)

    ''' <summary>登记新顶点并返回其索引。</summary>
    Public Function AddVertex(p As EulerianVertex) As Integer
        Points.Add(p)
        Return Points.Count - 1
    End Function

    ''' <summary>添加一个单元。</summary>
    Public Function AddCell(tag As String, ParamArray faces As Integer()()) As RawCell
        Dim cell As New RawCell With {.Tag = tag}

        For Each f As Integer() In faces
            cell.Faces.Add(New RawFace(f))
        Next

        Cells.Add(cell)
        Return cell
    End Function

    ''' <summary>
    ''' 添加六面体单元。角点布局（与结构化网格一致）：
    ''' 底面 (0,0,0)(1,0,0)(1,1,0)(0,1,0)，顶面对应 [4..7]。
    ''' </summary>
    Public Function AddHex(tag As String, c As Integer()) As RawCell
        If c.Length <> 8 Then
            Throw New ArgumentException("hex 需要 8 个角点", NameOf(c))
        End If

        ' 面绕序：从单元外部看逆时针（外法向）
        Return AddCell(tag,
            {c(0), c(3), c(2), c(1)},   ' z-
            {c(4), c(5), c(6), c(7)},   ' z+
            {c(0), c(4), c(7), c(3)},   ' x-
            {c(1), c(2), c(6), c(5)},   ' x+
            {c(0), c(1), c(5), c(4)},   ' y-
            {c(3), c(7), c(6), c(2)})   ' y+
    End Function

    ''' <summary>
    ''' 添加正定向四面体单元（a,b,c,d 满足右手定则）。
    ''' </summary>
    Public Function AddTet(tag As String, a As Integer, b As Integer, c As Integer, d As Integer) As RawCell
        Return AddCell(tag,
            {a, c, b},
            {a, b, d},
            {a, d, c},
            {b, c, d})
    End Function

    ''' <summary>
    ''' 添加三棱柱单元：底面三角 (b0,b1,b2)（从 +n 方向看逆时针），顶面 = 底面 + n。
    ''' </summary>
    Public Function AddPrism(tag As String, b0 As Integer, b1 As Integer, b2 As Integer,
                             t0 As Integer, t1 As Integer, t2 As Integer) As RawCell
        Return AddCell(tag,
            {b0, b2, b1},
            {t0, t1, t2},
            {b0, b1, t1, t0},
            {b1, b2, t2, t1},
            {b2, b0, t0, t2})
    End Function
End Class
