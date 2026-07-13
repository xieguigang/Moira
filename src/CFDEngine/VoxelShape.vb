Imports std = System.Math

' /********************************************************************************/
'
'   VoxelShape.vb
'
'   三维体素空间模型 —— CFD 计算空间的真相源
'
'   作用：
'       用一维逻辑向量 Boolean() 表示一个三维布尔数组 boolean(,,)，
'       从而定义 CFD 模拟的三维计算空间：
'         - Width, Height, Depth 标记三维数组的三个维度（X, Y, Z）
'         - Shape(idx) = True   表示对应体素属于模拟环境空间（活动体素）
'         - Shape(idx) = False  表示空腔（不属于模拟计算空间，求解器中视为固体障碍物）
'
'   索引约定（与现有 Tensor 布局、JSON 扁平数组顺序严格一致）：
'       Index(x, y, z) = (x * HEIGHT + y) * DEPTH + z
'       即 Width↔Nx, Height↔Ny, Depth↔Nz，等价于 i*Ny*Nz + j*Nz + k。
'
'   使用方式：
'       - FullBox(nx, ny, nz)        生成填满的长方体（等价于旧版 nx×ny×nz）
'       - Capsule(...)                生成竖直（沿 Z 轴）放置的胶囊形体素模型
'       生成的 VoxelShape 可直接加载进 FermentationTank / FluidField，
'       完成非规则 CFD 计算空间的定义。
'
' /********************************************************************************/

''' <summary>
''' 三维体素空间模型 —— 用一维 Boolean() 表示三维布尔数组，标记每个体素
''' 是否属于模拟计算空间。作为整个引擎计算空间的 "真相源"。
''' </summary>
Public Class VoxelShape

#Region "维度与数据"

    ''' <summary>X 方向体素数（↔ FluidField.Nx）</summary>
    Public ReadOnly Property Width As Integer

    ''' <summary>Y 方向体素数（↔ FluidField.Ny）</summary>
    Public ReadOnly Property Height As Integer

    ''' <summary>Z 方向体素数（↔ FluidField.Nz）</summary>
    Public ReadOnly Property Depth As Integer

    ''' <summary>
    ''' 一维体素标记数组，长度 = Width * Height * Depth。
    ''' True = 该体素属于模拟计算空间；False = 空腔（固体障碍物）。
    ''' </summary>
    Public ReadOnly Property Shape As Boolean()

    ''' <summary>活动（属于模拟空间）体素总数。</summary>
    Public ReadOnly Property TotalActive As Integer

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建体素空间模型。
    ''' </summary>
    ''' <param name="width">X 维度数（↔ Nx）</param>
    ''' <param name="height">Y 维度数（↔ Ny）</param>
    ''' <param name="depth">Z 维度数（↔ Nz）</param>
    ''' <param name="data">体素标记数组（长度须等于 width*height*depth）</param>
    Public Sub New(width As Integer, height As Integer, depth As Integer, data As Boolean())
        If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))
        If data.Length <> width * height * depth Then
            Throw New ArgumentException("shape 数组长度必须等于 width*height*depth", NameOf(data))
        End If
        Me.Width = width
        Me.Height = height
        Me.Depth = depth
        Me.Shape = data
        Dim count = 0
        For Each b In data
            If b Then count += 1
        Next
        Me.TotalActive = count
    End Sub

#End Region

#Region "索引与查询"

    ''' <summary>
    ''' 计算三维坐标 (x, y, z) 在一维数组中的索引。
    ''' 约定：Index = (x * Height + y) * Depth + z。
    ''' 等价于 Width↔Nx, Height↔Ny, Depth↔Nz 时的 i*Ny*Nz + j*Nz + k，
    ''' 与现有 Tensor 数据布局、JSON 扁平数组顺序严格一致。
    ''' </summary>
    Public Function Index(x As Integer, y As Integer, z As Integer) As Integer
        Return (x * Height + y) * Depth + z
    End Function

    ''' <summary>
    ''' 判断体素 (x, y, z) 是否属于模拟计算空间（活动体素）。
    ''' </summary>
    Public Function IsActive(x As Integer, y As Integer, z As Integer) As Boolean
        Return Shape((x * Height + y) * Depth + z)
    End Function

    ''' <summary>
    ''' 判断一维索引 idx 处体素是否活动。
    ''' </summary>
    Public Function IsActive(idx As Integer) As Boolean
        Return Shape(idx)
    End Function

    ''' <summary>
    ''' 由体素标记派生固体掩膜（True = 空腔 / 固体障碍物）。
    ''' 供求解器使用：求解器逐体素跳过固体单元并施加无滑移壁面。
    ''' </summary>
    Public Function ToSolidMask() As Boolean()
        Dim m(Shape.Length - 1) As Boolean
        For i = 0 To Shape.Length - 1
            m(i) = Not Shape(i)
        Next
        Return m
    End Function

#End Region

#Region "工厂方法"

    ''' <summary>
    ''' 创建填满的全 true 长方体体素模型（等价于旧版 nx×ny×nz 长方体空间）。
    ''' </summary>
    Public Shared Function FullBox(nx As Integer, ny As Integer, nz As Integer) As VoxelShape
        Dim n = nx * ny * nz
        Dim data(n - 1) As Boolean
        For i = 0 To n - 1
            data(i) = True
        Next
        Return New VoxelShape(nx, ny, nz, data)
    End Function

    ''' <summary>
    ''' 生成竖直（沿 Z 轴）放置的胶囊形三维体素模型。
    ''' 胶囊 = 圆柱段（半径 radius，半高 cylHalfHeight，沿 Z 轴）+ 两端半球（半径 radius）。
    ''' 圆柱段中心高度由 centerZ 指定；半球分别位于圆柱段两端外侧。
    ''' 空间中某体素属于胶囊，当且仅当其到胶囊几何表面的 "有符号距离" ≤ 0：
    '''   - 圆柱段部分（|z - centerZ| ≤ cylHalfHeight）：径向距离 ≤ radius
    '''   - 半球端帽部分（|z - centerZ| &gt; cylHalfHeight）：到端帽球心
    '''     (centerX, centerY, centerZ ± cylHalfHeight) 的距离 ≤ radius
    ''' </summary>
    ''' <param name="width">X 维度数</param>
    ''' <param name="height">Y 维度数</param>
    ''' <param name="depth">Z 维度数</param>
    ''' <param name="radius">胶囊半径（网格单位）</param>
    ''' <param name="cylHalfHeight">圆柱段半高（网格单位，不含两端半球）</param>
    ''' <param name="centerX">胶囊轴 X 位置（默认网格中心）</param>
    ''' <param name="centerY">胶囊轴 Y 位置（默认网格中心）</param>
    ''' <param name="centerZ">胶囊圆柱段中心 Z 位置（默认网格中心）</param>
    Public Shared Function Capsule(width As Integer, height As Integer, depth As Integer,
                                   radius As Double, cylHalfHeight As Double,
                                   Optional centerX As Double = -1,
                                   Optional centerY As Double = -1,
                                   Optional centerZ As Double = -1) As VoxelShape

        If centerX < 0 Then centerX = (width - 1) * 0.5
        If centerY < 0 Then centerY = (height - 1) * 0.5
        If centerZ < 0 Then centerZ = (depth - 1) * 0.5

        Dim n = width * height * depth
        Dim data(n - 1) As Boolean
        Dim r2 = radius * radius

        For x = 0 To width - 1
            Dim dx = x - centerX
            For y = 0 To height - 1
                Dim dy = y - centerY
                Dim radial2 = dx * dx + dy * dy
                If radial2 > r2 Then Continue For   ' 超出胶囊最大半径，必为空腔

                For z = 0 To depth - 1
                    Dim dz = z - centerZ
                    Dim inside As Boolean
                    If std.Abs(dz) <= cylHalfHeight Then
                        ' 圆柱段部分：径向距离 ≤ radius 即在内部
                        inside = radial2 <= r2
                    Else
                        ' 半球端帽：距端帽球心 (±cylHalfHeight) 的距离 ≤ radius
                        Dim dzCap = dz - std.Sign(dz) * cylHalfHeight
                        inside = radial2 + dzCap * dzCap <= r2
                    End If
                    If inside Then
                        data((x * height + y) * depth + z) = True
                    End If
                Next
            Next
        Next

        Return New VoxelShape(width, height, depth, data)

    End Function

#End Region

End Class
