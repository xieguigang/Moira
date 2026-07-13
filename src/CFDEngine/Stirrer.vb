Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports std = System.Math

' /********************************************************************************/
'
'   Stirrer.vb
'
'   搅拌器（叶轮）模型
'
'   作用：
'       模拟发酵罐内的旋转搅拌器。搅拌器是一个 "运动固体"，
'       它占据网格中的某些体素，并强制这些体素的速度等于搅拌器表面的速度。
'       这样流体就会被搅拌器 "带着转"，产生搅拌效果。
'
'   几何模型：
'       本引擎采用最简单的 "圆盘叶轮 (disk impeller)" 模型：
'         - 旋转轴沿 Z 方向（垂直），位于 (CenterX, CenterY)
'         - 叶轮是一个薄圆盘：半径 Radius，厚度 Height，中心高度 ZCenter
'         - 占据的体素 = { (i,j,k) : (i-cx)² + (j-cy)² ≤ R² 且 |k - zc| ≤ H/2 }
'
'   速度模型：
'       搅拌器以角速度 ω 绕 Z 轴旋转。
'       在搅拌器占据的体素 (i,j,k) 处，搅拌器表面的线速度为：
'         v = ω × r
'       其中 r 是从旋转轴到体素的向量 = (i - cx, j - cy, 0)
'       ω 向量 = (0, 0, ω)
'       所以：
'         U = -ω * (j - cy)     （切向，X 分量）
'         V =  ω * (i - cx)     （切向，Y 分量）
'         W =  AxialVelocity    （轴向泵送，可选，模拟轴向流）
'
'   扩展思路（不在本基础引擎实现，但可作为教学讨论）：
'       - 搅拌轴：从罐顶到叶轮的细圆柱，也强制速度为 0 或随轴旋转
'       - 挡板 (baffles)：罐壁上的固定挡板，防止整体刚体旋转
'       - 多层叶轮：多个不同高度的叶轮
'       - 涡轮叶片 (Rushton turbine)：更复杂的几何
'
' /********************************************************************************/

''' <summary>
''' 搅拌器 —— 旋转的圆盘叶轮，作为流体的驱动源。
''' </summary>
Public Class Stirrer

#Region "几何参数（网格坐标）"

    ''' <summary>旋转轴在 X 方向的位置（网格坐标）</summary>
    Public Property CenterX As Double

    ''' <summary>旋转轴在 Y 方向的位置（网格坐标）</summary>
    Public Property CenterY As Double

    ''' <summary>叶轮中心高度（Z 方向网格坐标）</summary>
    Public Property ZCenter As Double

    ''' <summary>叶轮半径（网格单位）</summary>
    Public Property Radius As Double

    ''' <summary>叶轮厚度（Z 方向网格单位）</summary>
    Public Property Height As Double

#End Region

#Region "运动参数"

    ''' <summary>
    ''' 角速度 ω（弧度/时间单位）。
    ''' 正值表示从 +Z 方向看下去逆时针旋转。
    ''' </summary>
    Public Property AngularVelocity As Double

    ''' <summary>
    ''' 轴向泵送速度（网格单位/时间）。
    ''' 非零值模拟轴向流叶轮（如桨叶向下/向上泵送流体）。
    ''' 0 表示纯切向搅拌（如 Rushton 涡轮的近似）。
    ''' </summary>
    Public Property AxialVelocity As Double = 0.0

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建搅拌器。
    ''' </summary>
    ''' <param name="centerX">旋转轴 X 位置</param>
    ''' <param name="centerY">旋转轴 Y 位置</param>
    ''' <param name="zCenter">叶轮中心高度</param>
    ''' <param name="radius">叶轮半径</param>
    ''' <param name="height">叶轮厚度</param>
    ''' <param name="angularVelocity">角速度 (rad/时间)</param>
    Public Sub New(centerX As Double, centerY As Double, zCenter As Double,
                   radius As Double, height As Double,
                   angularVelocity As Double)
        Me.CenterX = centerX
        Me.CenterY = centerY
        Me.ZCenter = zCenter
        Me.Radius = radius
        Me.Height = height
        Me.AngularVelocity = angularVelocity
    End Sub

#End Region

#Region "几何查询"

    ''' <summary>
    ''' 判断体素 (i,j,k) 是否在搅拌器内部。
    ''' 用体素中心点 (i, j, k) 到旋转轴的距离判断。
    ''' </summary>
    Public Function IsInside(i As Integer, j As Integer, k As Integer) As Boolean

        ' 径向距离（到旋转轴）
        Dim dx = i - CenterX
        Dim dy = j - CenterY
        Dim r2 = dx * dx + dy * dy

        ' 高度判断
        Dim dz = std.Abs(k - ZCenter)

        Return r2 <= Radius * Radius AndAlso dz <= Height * 0.5

    End Function

    ''' <summary>
    ''' 计算搅拌器在体素 (i,j,k) 处的表面速度。
    ''' v = ω × r
    ''' </summary>
    Public Sub GetSurfaceVelocity(i As Integer, j As Integer, k As Integer,
                                  ByRef uOut As Double, ByRef vOut As Double, ByRef wOut As Double)

        Dim dx = i - CenterX
        Dim dy = j - CenterY

        ' 切向速度：v = ω × r，ω = (0,0,ω)，r = (dx, dy, 0)
        ' v = (0,0,ω) × (dx,dy,0) = (-ω*dy, ω*dx, 0)
        uOut = -AngularVelocity * dy
        vOut = AngularVelocity * dx
        wOut = AxialVelocity

    End Sub

#End Region

#Region "应用到流体场"

    ''' <summary>
    ''' 把搅拌器边界条件应用到流体场：
    ''' 遍历所有体素，把搅拌器内部的体素速度强制设为搅拌器表面速度。
    '''
    ''' 这相当于把搅拌器当作 "强制速度源"。
    ''' 在每个时间步的末尾调用，确保搅拌器始终驱动流体。
    ''' </summary>
    Public Sub ApplyToField(field As FluidField)

        Dim nx = field.Nx
        Dim ny = field.Ny
        Dim nz = field.Nz

        ' 优化：只遍历搅拌器可能占据的范围，而不是整个网格
        Dim rMax = CInt(std.Ceiling(Radius)) + 1
        Dim iMin = CInt(std.Max(0, std.Floor(CenterX - rMax)))
        Dim iMax = CInt(std.Min(nx - 1, std.Ceiling(CenterX + rMax)))
        Dim jMin = CInt(std.Max(0, std.Floor(CenterY - rMax)))
        Dim jMax = CInt(std.Min(ny - 1, std.Ceiling(CenterY + rMax)))
        Dim kMin = CInt(std.Max(0, std.Floor(ZCenter - Height * 0.5)))
        Dim kMax = CInt(std.Min(nz - 1, std.Ceiling(ZCenter + Height * 0.5)))

        For i = iMin To iMax
            For j = jMin To jMax
                For k = kMin To kMax
                    If IsInside(i, j, k) Then
                        Dim su, sv, sw As Double
                        GetSurfaceVelocity(i, j, k, su, sv, sw)
                        field.U(i, j, k) = su
                        field.V(i, j, k) = sv
                        field.W(i, j, k) = sw
                    End If
                Next
            Next
        Next

    End Sub

    ''' <summary>
    ''' 把搅拌器边界条件直接应用到三个独立的速度 Tensor（u, v, w）。
    ''' 用于求解器内部双缓冲阶段，避免构造完整 FluidField。
    ''' </summary>
    Public Sub ApplyToFieldInternal(uTensor As Tensor, vTensor As Tensor, wTensor As Tensor)

        Dim nx = uTensor.Shape(0)
        Dim ny = uTensor.Shape(1)
        Dim nz = uTensor.Shape(2)

        Dim rMax = CInt(std.Ceiling(Radius)) + 1
        Dim iMin = CInt(std.Max(0, std.Floor(CenterX - rMax)))
        Dim iMax = CInt(std.Min(nx - 1, std.Ceiling(CenterX + rMax)))
        Dim jMin = CInt(std.Max(0, std.Floor(CenterY - rMax)))
        Dim jMax = CInt(std.Min(ny - 1, std.Ceiling(CenterY + rMax)))
        Dim kMin = CInt(std.Max(0, std.Floor(ZCenter - Height * 0.5)))
        Dim kMax = CInt(std.Min(nz - 1, std.Ceiling(ZCenter + Height * 0.5)))

        For i = iMin To iMax
            For j = jMin To jMax
                For k = kMin To kMax
                    If IsInside(i, j, k) Then
                        Dim su, sv, sw As Double
                        GetSurfaceVelocity(i, j, k, su, sv, sw)
                        uTensor(i, j, k) = su
                        vTensor(i, j, k) = sv
                        wTensor(i, j, k) = sw
                    End If
                Next
            Next
        Next

    End Sub

    ''' <summary>
    ''' 在搅拌器内部注入示踪剂（密度），用于可视化搅拌混合效果。
    ''' 通常在模拟开始时调用一次。
    ''' </summary>
    ''' <param name="field">流体场</param>
    ''' <param name="amount">注入的密度值</param>
    Public Sub InjectDye(field As FluidField, amount As Double)

        Dim nx = field.Nx
        Dim ny = field.Ny
        Dim nz = field.Nz

        Dim rMax = CInt(std.Ceiling(Radius)) + 1
        Dim iMin = CInt(std.Max(0, std.Floor(CenterX - rMax)))
        Dim iMax = CInt(std.Min(nx - 1, std.Ceiling(CenterX + rMax)))
        Dim jMin = CInt(std.Max(0, std.Floor(CenterY - rMax)))
        Dim jMax = CInt(std.Min(ny - 1, std.Ceiling(CenterY + rMax)))
        Dim kMin = CInt(std.Max(0, std.Floor(ZCenter - Height * 0.5)))
        Dim kMax = CInt(std.Min(nz - 1, std.Ceiling(ZCenter + Height * 0.5)))

        For i = iMin To iMax
            For j = jMin To jMax
                For k = kMin To kMax
                    If IsInside(i, j, k) Then
                        field.Density(i, j, k) = amount
                    End If
                Next
            Next
        Next

    End Sub

#End Region

End Class


