Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow.Compute

''' <summary>
''' 结构化网格 Poisson（椭圆型 PDE）平滑器 —— 基于 TensorF 算子。
'''
''' 对结构化网格的每个坐标分量场求解离散 Laplace 方程
''' ∇²x = 0（Thomas–Middlecoff 型控制的简化形式），边界节点钳制不动：
'''
''' - 场以 <see cref="TensorF"/>（形状 (ni+2, nj+2, nk+2)）承载，
'''   通过 <see cref="TensorF.computeKernelF"/> 可插拔后端执行七点 stencil 迭代，
'''   默认 SIMDTensorF（CPU SIMD），可切换 CUDA GPU 加速；
''' - 内核的最外层在迭代中恒不更新，天然充当填充层（ghost layer）；
''' - 真实边界节点在每轮外层迭代后恢复钳制值，从而实现 Dirichlet 边界。
''' </summary>
Public Class StructuredPoissonSmoother

    ''' <summary>
    ''' 平滑结构化网格节点坐标。
    ''' </summary>
    ''' <param name="coords">节点坐标 [ni*nj*nk]，索引 (i*nj + j)*nk + k。</param>
    ''' <param name="ni"></param><param name="nj"></param><param name="nk">三向节点数。</param>
    ''' <param name="iterations">外层迭代轮数（每轮内部 K 次 Jacobi）。</param>
    Public Shared Sub Smooth(coords As Double(), ni As Integer, nj As Integer, nk As Integer,
                             Optional iterations As Integer = 30)

        If ni < 3 OrElse nj < 3 OrElse nk < 3 OrElse iterations <= 0 Then
            Return
        End If

        Dim pi As Integer = ni + 2, pj As Integer = nj + 2, pk As Integer = nk + 2
        Dim padded As Single() = New Single(pi * pj * pk - 1) {}
        Dim boundary As Single() = New Single(ni * nj * nk - 1) {}
        Dim plane As Integer = pj * pk

        ' 装载初始场：边界值同时备份到 boundary
        For i As Integer = 0 To ni - 1
            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    Dim src As Integer = (i * nj + j) * nk + k
                    Dim dst As Integer = ((i + 1) * pj + (j + 1)) * pk + (k + 1)
                    padded(dst) = CSng(coords(src))
                    boundary(src) = CSng(coords(src))
                Next
            Next
        Next

        ' 填充层镜像边界值（仅被钳制节点的 stencil 读取，其值会被恢复步骤覆盖节点值，不影响收敛结果）
        Call FillGhost(padded, pi, pj, pk, ni, nj, nk, boundary)

        Dim kernel As ITensorComputeF = TensorF.computeKernelF

        For it As Integer = 1 To iterations
            Dim field As TensorF = TensorF.Wrap(padded, {pi, pj, pk})
            Dim solved As TensorF = kernel.JacobiStencil7(
                TensorF.Zeros({pi, pj, pk}), Nothing, Nothing,
                1.0F, 0, 10, field)
            Dim data As Single() = solved.Data

            ' 恢复钳制边界节点 + 刷新填充层
            For i As Integer = 0 To ni - 1
                For j As Integer = 0 To nj - 1
                    For k As Integer = 0 To nk - 1
                        Dim src As Integer = (i * nj + j) * nk + k
                        data(((i + 1) * pj + (j + 1)) * pk + (k + 1)) = boundary(src)
                    Next
                Next
            Next

            Call FillGhost(data, pi, pj, pk, ni, nj, nk, boundary)
            padded = data
        Next

        ' 写回
        For i As Integer = 0 To ni - 1
            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    Dim src As Integer = (i * nj + j) * nk + k
                    coords(src) = padded(((i + 1) * pj + (j + 1)) * pk + (k + 1))
                Next
            Next
        Next
    End Sub

    ''' <summary>将真实边界节点的值镜像到紧邻的填充层。</summary>
    Private Shared Sub FillGhost(padded As Single(), pi As Integer, pj As Integer, pk As Integer,
                                 ni As Integer, nj As Integer, nk As Integer,
                                 boundary As Single())
        Dim plane As Integer = pj * pk

        For i As Integer = 0 To ni - 1
            For j As Integer = 0 To nj - 1
                For k As Integer = 0 To nk - 1
                    Dim v As Single = boundary((i * nj + j) * nk + k)
                    Dim gi As Integer = i + 1, gj As Integer = j + 1, gk As Integer = k + 1

                    ' 六向镜像（角落位置会覆盖多次，取最后一次即可）
                    ' 扁平索引：idx = i * (pj*pk) + j * pk + k
                    If gi = 1 Then padded((gi - 1) * plane + gj * pk + gk) = v
                    If gi = ni Then padded((gi + 1) * plane + gj * pk + gk) = v
                    If gj = 1 Then padded(gi * plane + (gj - 1) * pk + gk) = v
                    If gj = nj Then padded(gi * plane + (gj + 1) * pk + gk) = v
                    If gk = 1 Then padded(gi * plane + gj * pk + (gk - 1)) = v
                    If gk = nk Then padded(gi * plane + gj * pk + (gk + 1)) = v
                Next
            Next
        Next
    End Sub
End Class
