Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports std = System.Math

' /********************************************************************************/
'
'   FluidField.vb
'
'   流体场数据结构
'
'   作用：
'       使用 Tensor 对象存储 CFD 模拟中所有三维网格物理量：
'         - 速度场 U, V, W （三个方向分量，每个都是 Nx×Ny×Nz 的 Tensor）
'         - 压力场 Pressure
'         - 密度/示踪剂场 Density （被动标量，用于可视化混合效果）
'
'   设计说明：
'       本引擎基于 "交错网格 (staggered grid)" 的简化版本——所有物理量都存储在
'       同一个网格的格子中心 (co-located grid)。这种做法在教学上更直观，
'       虽然在专业 CFD 中交错网格能更好地避免棋盘格压力振荡，
'       但对于原理学习而言，同位网格已经足够，且代码更易理解。
'
'   网格坐标约定：
'       (i, j, k) 中：
'         i ∈ [0, Nx-1]  对应 X 方向（水平）
'         j ∈ [0, Ny-1]  对应 Y 方向（水平）
'         k ∈ [0, Nz-1]  对应 Z 方向（垂直，通常为发酵罐的高度方向）
'       Tensor 的三维索引器 Item(row, col, depth) 直接对应 (i, j, k)。
'
' /********************************************************************************/

''' <summary>
''' 单个体素（网格单元）的查询结果，包含该位置所有基础物理量。
''' 用于对外提供 "每一个体素方格中的速度、压力、密度等基础信息"。
''' </summary>
Public Structure VoxelData

    ''' <summary>X 方向速度分量</summary>
    Public U As Double

    ''' <summary>Y 方向速度分量</summary>
    Public V As Double

    ''' <summary>Z 方向速度分量</summary>
    Public W As Double

    ''' <summary>压力</summary>
    Public Pressure As Double

    ''' <summary>密度/示踪剂浓度</summary>
    Public Density As Double

    ''' <summary>速度大小（标量）</summary>
    Public ReadOnly Property Speed As Double
        Get
            Return std.Sqrt(U * U + V * V + W * W)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"U={U:F4}, V={V:F4}, W={W:F4}, |v|={Speed:F4}, P={Pressure:F4}, D={Density:F4}"
    End Function

End Structure

''' <summary>
''' 流体场 —— 存储三维网格上所有物理量。
''' 所有场量都基于 Tensor 对象实现。
''' </summary>
Public Class FluidField

#Region "网格尺寸"

    ''' <summary>X 方向格子数</summary>
    Public ReadOnly Property Nx As Integer

    ''' <summary>Y 方向格子数</summary>
    Public ReadOnly Property Ny As Integer

    ''' <summary>Z 方向格子数</summary>
    Public ReadOnly Property Nz As Integer

    ''' <summary>格子总数</summary>
    Public ReadOnly Property TotalVoxels As Integer
        Get
            Return Nx * Ny * Nz
        End Get
    End Property

    ''' <summary>
    ''' 三维体素空间模型（计算空间的真相源）。
    ''' True = 活动体素（属于模拟空间）；False = 空腔（求解器中视为固体障碍物）。
    ''' 长方体旧路径内部构造全 true 的 VoxelShape.FullBox，故永不为 Nothing。
    ''' </summary>
    Public Property Shape As VoxelShape

#End Region

#Region "物理量场（Tensor 对象）"

    ''' <summary>X 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property U As Tensor

    ''' <summary>Y 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property V As Tensor

    ''' <summary>Z 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property W As Tensor

    ''' <summary>压力场，形状 (Nx, Ny, Nz)</summary>
    Public Property Pressure As Tensor

    ''' <summary>密度/示踪剂场，形状 (Nx, Ny, Nz)</summary>
    Public Property Density As Tensor

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建指定网格尺寸的流体场，所有物理量初始化为零。
    ''' 内部用全 true 的长方体体素模型（等价于旧版 nx×ny×nz 长方体空间）。
    ''' </summary>
    ''' <param name="nx">X 方向格子数</param>
    ''' <param name="ny">Y 方向格子数</param>
    ''' <param name="nz">Z 方向格子数</param>
    Public Sub New(nx As Integer, ny As Integer, nz As Integer)
        Me.New(VoxelShape.FullBox(nx, ny, nz))
    End Sub

    ''' <summary>
    ''' 用指定的三维体素空间模型创建流体场，所有物理量初始化为零。
    ''' 体素模型的 width/height/depth 对应网格的 Nx/Ny/Nz。
    ''' </summary>
    ''' <param name="voxelShape">三维体素空间模型（定义计算空间形状）</param>
    Public Sub New(voxelShape As VoxelShape)
        Me.Shape = voxelShape
        Me.Nx = voxelShape.Width
        Me.Ny = voxelShape.Height
        Me.Nz = voxelShape.Depth

        ' 使用 Tensor 的工厂方法创建零张量
        ' 形状为 (Nx, Ny, Nz)，对应三维索引器 (i, j, k)
        Dim dims As Integer() = {Nx, Ny, Nz}
        Me.U = Tensor.Zeros(dims)
        Me.V = Tensor.Zeros(dims)
        Me.W = Tensor.Zeros(dims)
        Me.Pressure = Tensor.Zeros(dims)
        Me.Density = Tensor.Zeros(dims)
    End Sub

#End Region

#Region "索引访问"

    ''' <summary>
    ''' 获取指定体素的速度向量。
    ''' </summary>
    Public Function GetVelocity(i As Integer, j As Integer, k As Integer) As (u As Double, v As Double, w As Double)
        Return (U(i, j, k), V(i, j, k), W(i, j, k))
    End Function

    ''' <summary>
    ''' 设置指定体素的速度向量。
    ''' </summary>
    Public Sub SetVelocity(i As Integer, j As Integer, k As Integer, uVal As Double, vVal As Double, wVal As Double)
        U(i, j, k) = uVal
        V(i, j, k) = vVal
        W(i, j, k) = wVal
    End Sub

    ''' <summary>
    ''' 获取指定体素的全部基础物理量（速度、压力、密度）。
    ''' 这是引擎对外暴露 "每一个体素方格中的基础信息" 的主要接口。
    ''' </summary>
    Public Function GetVoxel(i As Integer, j As Integer, k As Integer) As VoxelData
        Return New VoxelData With {
            .U = U(i, j, k),
            .V = V(i, j, k),
            .W = W(i, j, k),
            .Pressure = Pressure(i, j, k),
            .Density = Density(i, j, k)
        }
    End Function

    ''' <summary>
    ''' 判断索引是否在网格内部（不含边界）。
    ''' </summary>
    Public Function IsInterior(i As Integer, j As Integer, k As Integer) As Boolean
        Return i > 0 AndAlso i < Nx - 1 AndAlso
               j > 0 AndAlso j < Ny - 1 AndAlso
               k > 0 AndAlso k < Nz - 1
    End Function

    ''' <summary>
    ''' 判断索引是否在网格范围内（含边界）。
    ''' </summary>
    Public Function IsValid(i As Integer, j As Integer, k As Integer) As Boolean
        Return i >= 0 AndAlso i < Nx AndAlso
               j >= 0 AndAlso j < Ny AndAlso
               k >= 0 AndAlso k < Nz
    End Function

    ''' <summary>
    ''' 判断体素 (i, j, k) 是否属于模拟计算空间（活动体素）。
    ''' 空腔体素（Shape = False）在求解器中视为固体障碍物。
    ''' </summary>
    Public Function IsActive(i As Integer, j As Integer, k As Integer) As Boolean
        Return Shape IsNot Nothing AndAlso Shape.IsActive(i, j, k)
    End Function

#End Region

#Region "整体操作"

    ''' <summary>
    ''' 将所有场清零。
    ''' </summary>
    Public Sub Clear()
        Array.Clear(U.Data, 0, U.Length)
        Array.Clear(V.Data, 0, V.Length)
        Array.Clear(W.Data, 0, W.Length)
        Array.Clear(Pressure.Data, 0, Pressure.Length)
        Array.Clear(Density.Data, 0, Density.Length)
    End Sub

    ''' <summary>
    ''' 创建当前流体场的深拷贝（用于保存快照或双缓冲）。
    ''' 体素模型（几何形状）为不可变真相源，直接共享引用，无需深拷贝。
    ''' </summary>
    Public Function Clone() As FluidField
        Dim copy As New FluidField(Shape)
        copy.U = CType(U.Clone(), Tensor)
        copy.V = CType(V.Clone(), Tensor)
        copy.W = CType(W.Clone(), Tensor)
        copy.Pressure = CType(Pressure.Clone(), Tensor)
        copy.Density = CType(Density.Clone(), Tensor)
        Return copy
    End Function

    ''' <summary>
    ''' 把另一个同尺寸场的数据复制到本场（逐元素覆盖）。
    ''' </summary>
    Public Sub CopyFrom(other As FluidField)
        Array.Copy(other.U.Data, U.Data, U.Length)
        Array.Copy(other.V.Data, V.Data, V.Length)
        Array.Copy(other.W.Data, W.Data, W.Length)
        Array.Copy(other.Pressure.Data, Pressure.Data, Pressure.Length)
        Array.Copy(other.Density.Data, Density.Data, Density.Length)
    End Sub

#End Region

End Class


