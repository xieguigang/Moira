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
''' 所有场量都基于单精度张量 <see cref="TensorF"/> 实现（Single / float32 存储）。
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

#Region "物理量场（TensorF 单精度张量对象）"

    ''' <summary>X 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property U As TensorF

    ''' <summary>Y 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property V As TensorF

    ''' <summary>Z 方向速度场，形状 (Nx, Ny, Nz)</summary>
    Public Property W As TensorF

    ''' <summary>压力场，形状 (Nx, Ny, Nz)</summary>
    Public Property Pressure As TensorF

    ''' <summary>密度/示踪剂场，形状 (Nx, Ny, Nz)</summary>
    Public Property Density As TensorF

#End Region

#Region "混合精度：双精度镜像（惰性构造）"

    ' ─────────────────────────────────────────────────────────────────────────
    '   精度分工（混合精度 CFD 引擎的核心契约）
    '
    '   Single（TensorF，本类的主存储）
    '       → GPU / CUDA 收益最高的迭代式椭圆求解：压力泊松 Jacobi、动量预估
    '         Jacobi、七点 Laplacian。Jacobi 对舍入不敏感，内迭代的截断误差会
    '         被外层 SIMPLE / 时间推进吸收，且单精度使显存带宽与占用减半。
    '
    '   Double（Tensor，惰性镜像 U64/V64/W64/P64/Density64）
    '       → CPU 上精度敏感的模块：MRF 桨盘源项、k-ε 湍流闭包（含 exp / pow）、
    '         PBM 群体平衡（破碎/聚并转移矩阵与归一化）、DO 传质与 kLa 闭包、
    '         以及所有比值型诊断统计量。这些量的误差会跨时间步累积。
    '
    '   镜像采用惰性构造：StableFluids 路径永不访问 U64 等属性，因此不会额外
    '   分配任何双精度数组 —— 对既有路径是零内存、零行为回归。
    ' ─────────────────────────────────────────────────────────────────────────

    Private _u64 As Tensor
    Private _v64 As Tensor
    Private _w64 As Tensor
    Private _p64 As Tensor
    Private _d64 As Tensor

    ''' <summary>
    ''' 镜像是否已整体物化。物化是<b>全有或全无</b>的：五个镜像必须同时存在，
    ''' 否则 SyncToSingle / SyncToDouble 会踩到 Nothing（半物化状态的 NRE）。
    ''' </summary>
    Private _mirrored As Boolean = False

    ''' <summary>一次性物化五个双精度镜像（幂等）。StableFluids 路径永不调用。</summary>
    Private Sub EnsureDoubleMirror()
        If _mirrored Then Return

        _u64 = U.ToTensor()
        _v64 = V.ToTensor()
        _w64 = W.ToTensor()
        _p64 = Pressure.ToTensor()
        _d64 = Density.ToTensor()
        _mirrored = True
    End Sub

    ''' <summary>X 方向速度场的双精度镜像（惰性构造；精度敏感模块用）。</summary>
    Public ReadOnly Property U64 As Tensor
        Get
            EnsureDoubleMirror()
            Return _u64
        End Get
    End Property

    ''' <summary>Y 方向速度场的双精度镜像（惰性构造；精度敏感模块用）。</summary>
    Public ReadOnly Property V64 As Tensor
        Get
            EnsureDoubleMirror()
            Return _v64
        End Get
    End Property

    ''' <summary>Z 方向速度场的双精度镜像（惰性构造；精度敏感模块用）。</summary>
    Public ReadOnly Property W64 As Tensor
        Get
            EnsureDoubleMirror()
            Return _w64
        End Get
    End Property

    ''' <summary>压力场的双精度镜像（惰性构造；精度敏感模块用）。</summary>
    Public ReadOnly Property P64 As Tensor
        Get
            EnsureDoubleMirror()
            Return _p64
        End Get
    End Property

    ''' <summary>密度/示踪剂场的双精度镜像（惰性构造；精度敏感模块用）。</summary>
    Public ReadOnly Property Density64 As Tensor
        Get
            EnsureDoubleMirror()
            Return _d64
        End Get
    End Property

    ''' <summary>双精度镜像是否已被物化（用于诊断与避免无意触发）。</summary>
    Public ReadOnly Property HasDoubleMirror As Boolean
        Get
            Return _mirrored
        End Get
    End Property

    ''' <summary>
    ''' 把 Single 主存储提升到 Double 镜像（模块边界一次性转换，O(5N)）。
    ''' 镜像尚未物化时顺带完成物化，之后为就地逐元素写入，不产生新数组。
    ''' </summary>
    Public Sub SyncToDouble()
        EnsureDoubleMirror()

        CopyF32ToF64(U, _u64)
        CopyF32ToF64(V, _v64)
        CopyF32ToF64(W, _w64)
        CopyF32ToF64(Pressure, _p64)
        CopyF32ToF64(Density, _d64)
    End Sub

    ''' <summary>
    ''' 把 Double 镜像回落写入 Single 主存储（模块边界一次性转换，O(5N)）。
    ''' 镜像未物化时为空操作（StableFluids 路径）。
    ''' </summary>
    Public Sub SyncToSingle()
        If Not _mirrored Then Return

        CopyF64ToF32(_u64, U)
        CopyF64ToF32(_v64, V)
        CopyF64ToF32(_w64, W)
        CopyF64ToF32(_p64, Pressure)
        CopyF64ToF32(_d64, Density)
    End Sub

    Private Shared Sub CopyF32ToF64(src As TensorF, dst As Tensor)
        Dim s = src.Data
        Dim d = dst.Data
        For i = 0 To d.Length - 1
            d(i) = s(i)
        Next
    End Sub

    Private Shared Sub CopyF64ToF32(src As Tensor, dst As TensorF)
        Dim s = src.Data
        Dim d = dst.Data
        For i = 0 To d.Length - 1
            d(i) = CSng(s(i))
        Next
    End Sub

#End Region

#Region "扩展标量场（双精度）"

    ''' <summary>
    ''' 扩展标量场（湍流 k / epsilon / 气含率 alpha_g / 溶解氧 DO / 湍流粘度 nut /
    ''' kLa / d32 / n0 / PBM 分组 αᵢ），名称 → Tensor（形状与标准场一致）。
    ''' 全部走双精度：这些量包含指数、幂律与矩阵归一化，误差会跨步累积。
    ''' VTI 快照记录器会把它们与五个标准场一并写出。
    ''' </summary>
    Public ReadOnly Property ExtraScalars As New Dictionary(Of String, Tensor)

    ''' <summary>按名读取扩展场（不存在时返回 Nothing）。</summary>
    Public Function GetExtra(name As String) As Tensor
        Dim t As Tensor = Nothing
        If ExtraScalars.TryGetValue(name, t) Then Return t
        Return Nothing
    End Function

    ''' <summary>安全读取 (i,j,k) 处的场值（越界返回 0）。</summary>
    Public Shared Function At(t As Tensor, nx As Integer, ny As Integer, nz As Integer,
                             i As Integer, j As Integer, k As Integer) As Double
        If i < 0 OrElse i >= nx OrElse j < 0 OrElse j >= ny OrElse k < 0 OrElse k >= nz Then
            Return 0.0
        End If
        Return t.Data(i * (ny * nz) + j * nz + k)
    End Function

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

        ' 使用单精度张量的工厂方法创建零张量
        ' 形状为 (Nx, Ny, Nz)，对应三维索引器 (i, j, k)
        Dim dims As Integer() = {Nx, Ny, Nz}
        Me.U = TensorF.Zeros(dims)
        Me.V = TensorF.Zeros(dims)
        Me.W = TensorF.Zeros(dims)
        Me.Pressure = TensorF.Zeros(dims)
        Me.Density = TensorF.Zeros(dims)
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
        copy.U = U.CloneT()
        copy.V = V.CloneT()
        copy.W = W.CloneT()
        copy.Pressure = Pressure.CloneT()
        copy.Density = Density.CloneT()
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


