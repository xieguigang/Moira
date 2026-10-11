Option Strict On
Option Explicit On

' /********************************************************************************/
'
'   LatticeD3Q19.vb
'
'   D3Q19 格子模型 —— LBM 引擎的离散速度集与权重。
'
'   物理约定（与 TensorGrid 索引布局一致）：
'       i:x（面 stride = Ny*Nz） j:y（行 stride = Nz） k:z（自下而上）
'
'   离散速度 e_α（α = 0..18）：
'       0      : ( 0, 0, 0)                      w = 1/3
'       1..6   : ±x ±y ±z 主轴                    w = 1/18
'       7..18  : 平面对角（两分量 ±1）              w = 1/36
'
'   等温模型：cs² = 1/3（格子单位，dx = dt = 1）
'
' /********************************************************************************/

''' <summary>
''' D3Q19 格子常量。方向配对设计：α 与 α^1 奇偶相邻互为反向（0 除外）。
''' </summary>
Public Module LatticeD3Q19

    ''' <summary>离散速度数。</summary>
    Public Const Q As Integer = 19

    ''' <summary>格子声速平方 cs²（等温）。</summary>
    Public Const Cs2 As Double = 1.0R / 3.0R

    ''' <summary>x 方向速度分量 e_αx（+1/0/−1）。</summary>
    Public ReadOnly EX As Integer() = {
        0,
        1, -1, 0, 0, 0, 0,
        1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0
    }

    ''' <summary>y 方向速度分量 e_αy。</summary>
    Public ReadOnly EY As Integer() = {
        0,
        0, 0, 1, -1, 0, 0,
        1, -1, -1, 1, 0, 0, 0, 0, 1, -1, 1, -1
    }

    ''' <summary>z 方向速度分量 e_αz。</summary>
    Public ReadOnly EZ As Integer() = {
        0,
        0, 0, 0, 0, 1, -1,
        0, 0, 0, 0, 1, -1, -1, 1, 1, -1, -1, 1
    }

    ''' <summary>权重 w_α。</summary>
    Public ReadOnly W As Double() = {
        1.0R / 3.0R,
        1.0R / 18.0R, 1.0R / 18.0R, 1.0R / 18.0R, 1.0R / 18.0R, 1.0R / 18.0R, 1.0R / 18.0R,
        1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R,
        1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R, 1.0R / 36.0R
    }

    ''' <summary>反方向下标 opp(α)：e_opp(α) = −e_α。</summary>
    Public ReadOnly Opp As Integer() = {
        0,
        2, 1, 4, 3, 6, 5,
        8, 7, 10, 9, 12, 11, 14, 13, 16, 15, 18, 17
    }

End Module
