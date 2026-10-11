Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   TensorGrid.vb
'
'   结构化网格上的 Tensor 算子库 —— FVM 离散的积木。
'
'   ★ 核心模块①【FVM 骨架】的网格算子层（FVM.md §一 1.2 面通量三关键量）：
'     ShiftX/Y/Z = 邻位取值（owner/neighbour 面表的张量化等价物）
'     Ddx/Ddy/Ddz = 中心差分梯度（扩散通量 ∇φ·n）
'     被 MomentumPredictor / PressureCorrection / 各输运方程统一复用。
'
'   设计说明：
'       - 移位（邻居取值）是纯数据搬运，用 BCL Array.Copy 实现；
'       - 所有算术（加/减/乘/除/最大/最小）在调用方通过
'         tfMath 模块（Tensor.computeKernel 路由）完成 —— 切换
'         computeKernel 到 CUDA 后端即可整体迁移到 GPU。
'       - 布局与 Tensor.Item(i,j,k) 一致：idx = i*(Ny*Nz) + j*Nz + k。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.MachineLearning.TensorFlow

''' <summary>结构化三维网格上的移位与差分工具。</summary>
Public Module TensorGrid

    ''' <summary>把 Double 数据包装为 {nx,ny,nz} Tensor。</summary>
    Public Function Wrap(data As Double(), nx As Integer, ny As Integer, nz As Integer) As Tensor
        Return New Tensor(data, nx, ny, nz)
    End Function

    ''' <summary>元素全为常数 c 的 Tensor。</summary>
    Public Function Filled(nx As Integer, ny As Integer, nz As Integer, c As Double) As Tensor
        Dim t As New Tensor(nx, ny, nz)
        For i = 0 To t.Length - 1
            t.Data(i) = c
        Next
        Return t
    End Function

    ''' <summary>逐元素克隆。</summary>
    Public Function Clone(t As Tensor) As Tensor
        Dim r As New Tensor(t.Shape(0), t.Shape(1), t.Shape(2))
        Array.Copy(t.Data, r.Data, t.Length)
        Return r
    End Function

    ' ---------------- 沿 X（i，平面方向，整平面连续拷贝最快） ----------------

    ''' <summary>结果(i,j,k) = t(i+off,j,k)；越界补 0。off=+1 即“取 East 邻居”。</summary>
    Public Function ShiftX(t As Tensor, off As Integer) As Tensor
        Dim nx = t.Shape(0), ny = t.Shape(1), nz = t.Shape(2)
        Dim r As New Tensor(nx, ny, nz)
        Dim plane = ny * nz
        Dim src = t.Data, dst = r.Data
        If off > 0 Then
            For i = 0 To nx - off - 1
                Array.Copy(src, (i + off) * plane, dst, i * plane, plane)
            Next
        ElseIf off < 0 Then
            For i = -off To nx - 1
                Array.Copy(src, (i + off) * plane, dst, i * plane, plane)
            Next
        Else
            Array.Copy(src, dst, t.Length)
        End If
        Return r
    End Function

    ' ---------------- 沿 Y（j，行 stride = nz） ----------------

    ''' <summary>结果(i,j,k) = t(i,j+off,k)；越界补 0。</summary>
    Public Function ShiftY(t As Tensor, off As Integer) As Tensor
        Dim nx = t.Shape(0), ny = t.Shape(1), nz = t.Shape(2)
        Dim r As New Tensor(nx, ny, nz)
        Dim src = t.Data, dst = r.Data
        For i = 0 To nx - 1
            Dim base_ = i * ny * nz
            For j = 0 To ny - 1
                Dim jj = j + off
                If jj >= 0 AndAlso jj < ny Then
                    Array.Copy(src, base_ + jj * nz, dst, base_ + j * nz, nz)
                End If
            Next
        Next
        Return r
    End Function

    ' ---------------- 沿 Z（k，列内连续） ----------------

    ''' <summary>结果(i,j,k) = t(i,j,k+off)；越界补 0。</summary>
    Public Function ShiftZ(t As Tensor, off As Integer) As Tensor
        Dim nx = t.Shape(0), ny = t.Shape(1), nz = t.Shape(2)
        Dim r As New Tensor(nx, ny, nz)
        Dim src = t.Data, dst = r.Data
        For i = 0 To nx - 1
            For j = 0 To ny - 1
                Dim base_ = i * ny * nz + j * nz
                If off > 0 Then
                    For k = 0 To nz - off - 1
                        dst(base_ + k) = src(base_ + k + off)
                    Next
                ElseIf off < 0 Then
                    For k = -off To nz - 1
                        dst(base_ + k) = src(base_ + k + off)
                    Next
                Else
                    For k = 0 To nz - 1
                        dst(base_ + k) = src(base_ + k)
                    Next
                End If
            Next
        Next
        Return r
    End Function

    ' ---------------- 六邻居便捷访问 ----------------

    ''' <summary>East：取 (i+1,j,k)。</summary>
    Public Function East(t As Tensor) As Tensor
        Return ShiftX(t, +1)
    End Function

    ''' <summary>West：取 (i-1,j,k)。</summary>
    Public Function West(t As Tensor) As Tensor
        Return ShiftX(t, -1)
    End Function

    ''' <summary>North：取 (i,j+1,k)。</summary>
    Public Function North(t As Tensor) As Tensor
        Return ShiftY(t, +1)
    End Function

    ''' <summary>South：取 (i,j-1,k)。</summary>
    Public Function South(t As Tensor) As Tensor
        Return ShiftY(t, -1)
    End Function

    ''' <summary>Top：取 (i,j,k+1)。</summary>
    Public Function Top(t As Tensor) As Tensor
        Return ShiftZ(t, +1)
    End Function

    ''' <summary>Bottom：取 (i,j,k-1)。</summary>
    Public Function Bottom(t As Tensor) As Tensor
        Return ShiftZ(t, -1)
    End Function

    ' ---------------- 差分算子（调用方负责用 tfMath 做算术） ----------------

    ''' <summary>中心差分 ∂t/∂x（第一类边界零值）。</summary>
    Public Function Ddx(t As Tensor, dx As Double) As Tensor
        Dim nx = t.Shape(0)
        ' (East - West) / (2dx)，边界单侧
        Dim e = East(t).Data
        Dim w = West(t).Data
        Dim r As New Tensor(t.Shape(0), t.Shape(1), t.Shape(2))
        Dim dst = r.Data
        For i = 0 To t.Length - 1
            dst(i) = (e(i) - w(i)) / (2.0R * dx)
        Next
        ' X 边界处用单侧差分修正（East/West 被补零导致偏差）
        Dim ny = t.Shape(1), nz = t.Shape(2)
        Dim src = t.Data
        For j = 0 To ny - 1
            For k = 0 To nz - 1
                Dim i0 = j * nz + k
                dst(i0) = (src(nz + i0) - src(i0)) / dx
                Dim iL = (nx - 1) * ny * nz + i0
                dst(iL) = (src(iL) - src(iL - ny * nz)) / dx
            Next
        Next
        Return r
    End Function

    ''' <summary>中心差分 ∂t/∂y。</summary>
    Public Function Ddy(t As Tensor, dy As Double) As Tensor
        Dim e = North(t).Data
        Dim w = South(t).Data
        Dim r As New Tensor(t.Shape(0), t.Shape(1), t.Shape(2))
        Dim dst = r.Data
        For i = 0 To t.Length - 1
            dst(i) = (e(i) - w(i)) / (2.0R * dy)
        Next
        Return r
    End Function

    ''' <summary>中心差分 ∂t/∂z。</summary>
    Public Function Ddz(t As Tensor, dz As Double) As Tensor
        Dim e = Top(t).Data
        Dim w = Bottom(t).Data
        Dim r As New Tensor(t.Shape(0), t.Shape(1), t.Shape(2))
        Dim dst = r.Data
        For i = 0 To t.Length - 1
            dst(i) = (e(i) - w(i)) / (2.0R * dz)
        Next
        Return r
    End Function

    ' ---------------- 掩膜工具 ----------------

    ''' <summary>把 Boolean 掩膜转为 0/1 Tensor。</summary>
    Public Function MaskTensor(mask As Boolean(), nx As Integer, ny As Integer, nz As Integer) As Tensor
        Dim t As New Tensor(nx, ny, nz)
        For i = 0 To mask.Length - 1
            If mask(i) Then t.Data(i) = 1.0R
        Next
        Return t
    End Function

    ''' <summary>补掩膜（1-mask），用于出流层置零等。</summary>
    Public Function InverseMask(mask As Tensor) As Tensor
        Dim r As New Tensor(mask.Shape(0), mask.Shape(1), mask.Shape(2))
        For i = 0 To mask.Length - 1
            r.Data(i) = 1.0R - mask.Data(i)
        Next
        Return r
    End Function

End Module
