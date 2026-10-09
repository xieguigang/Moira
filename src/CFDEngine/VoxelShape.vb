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

#Region "FVM 扩展掩膜（可选，StableFluids 路径为 Nothing）"

    ''' <summary>
    ''' 固体结构体素掩膜（挡板 / 桨叶 / 轴 / 罐壁）。
    ''' 仅 FVM（有限体积）路径使用；StableFluids 路径为 <c>Nothing</c>。
    ''' </summary>
    Public ReadOnly Property Solids As Boolean()

    ''' <summary>
    ''' 桨盘作用区掩膜（MRF 动量源加载区）。
    ''' 仅 FVM 路径使用；StableFluids 路径为 <c>Nothing</c>。
    ''' </summary>
    Public ReadOnly Property ImpellerZone As Boolean()

    ''' <summary>
    ''' 气体分布环掩膜（气相入口单元）。
    ''' 仅 FVM 路径使用；StableFluids 路径为 <c>Nothing</c>。
    ''' </summary>
    Public ReadOnly Property SpargerZone As Boolean()

    ''' <summary>X 方向体素数（= <see cref="Width"/>，与 FluidField.Nx 对齐的别名）。</summary>
    Public ReadOnly Property Nx As Integer
        Get
            Return Width
        End Get
    End Property

    ''' <summary>Y 方向体素数（= <see cref="Height"/>，与 FluidField.Ny 对齐的别名）。</summary>
    Public ReadOnly Property Ny As Integer
        Get
            Return Height
        End Get
    End Property

    ''' <summary>Z 方向体素数（= <see cref="Depth"/>，与 FluidField.Nz 对齐的别名）。</summary>
    Public ReadOnly Property Nz As Integer
        Get
            Return Depth
        End Get
    End Property

#End Region

#Region "构造函数"

    ''' <summary>
    ''' 创建体素空间模型。
    ''' </summary>
    ''' <param name="width">X 维度数（↔ Nx）</param>
    ''' <param name="height">Y 维度数（↔ Ny）</param>
    ''' <param name="depth">Z 维度数（↔ Nz）</param>
    ''' <param name="data">体素标记数组（长度须等于 width*height*depth）</param>
    Public Sub New(width As Integer, height As Integer, depth As Integer, data As Boolean(),
                   Optional solids As Boolean() = Nothing,
                   Optional impellerZone As Boolean() = Nothing,
                   Optional spargerZone As Boolean() = Nothing)
        If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))
        If data.Length <> width * height * depth Then
            Throw New ArgumentException("shape 数组长度必须等于 width*height*depth", NameOf(data))
        End If

        Dim n = width * height * depth
        Dim count = 0

        For Each b In data
            If b Then count += 1
        Next

        Me.Width = width
        Me.Height = height
        Me.Depth = depth
        Me.Shape = data
        Me.TotalActive = count

        ' ---- FVM 扩展掩膜（可选）----
        ' 三者在 StableFluids 路径下恒为 Nothing，因此不会带来任何内存或行为回归；
        ' FVM 路径下三者长度必须与主掩膜一致，否则立即抛出，避免后续越界。
        If solids IsNot Nothing AndAlso solids.Length <> n Then
            Throw New ArgumentException("solids 数组长度必须等于 width*height*depth", NameOf(solids))
        End If
        If impellerZone IsNot Nothing AndAlso impellerZone.Length <> n Then
            Throw New ArgumentException("impellerZone 数组长度必须等于 width*height*depth", NameOf(impellerZone))
        End If
        If spargerZone IsNot Nothing AndAlso spargerZone.Length <> n Then
            Throw New ArgumentException("spargerZone 数组长度必须等于 width*height*depth", NameOf(spargerZone))
        End If

        Me.Solids = solids
        Me.ImpellerZone = impellerZone
        Me.SpargerZone = spargerZone
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
    ''' 判断体素 (x, y, z) 是否为固体结构（FVM 路径）。
    ''' </summary>
    ''' <remarks>
    ''' <see cref="Solids"/> 为 <c>Nothing</c>（StableFluids 路径）时恒返回 False，
    ''' 该情形下固体语义由 <c>Not IsActive(...)</c> 表达。
    ''' </remarks>
    Public Function IsSolid(x As Integer, y As Integer, z As Integer) As Boolean
        If Solids Is Nothing Then Return False
        Return Solids((x * Height + y) * Depth + z)
    End Function

    ''' <summary>判断一维索引 idx 处体素是否为固体结构（FVM 路径）。</summary>
    Public Function IsSolid(idx As Integer) As Boolean
        If Solids Is Nothing Then Return False
        Return Solids(idx)
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

    ''' <summary>
    ''' 生成竖直（沿 Z 轴）放置的圆柱形体素模型。
    ''' 圆柱 = 半径 radius 的圆形截面沿 Z 轴从 bottomZ 拉伸到 topZ。
    ''' 空间中某体素属于圆柱，当且仅当：
    '''   - 其径向距离 sqrt((x-centerX)^2 + (y-centerY)^2) ≤ radius
    '''   - 且 bottomZ ≤ z ≤ topZ
    ''' </summary>
    ''' <param name="width">X 维度数</param>
    ''' <param name="height">Y 维度数</param>
    ''' <param name="depth">Z 维度数</param>
    ''' <param name="radius">圆柱半径（网格单位）</param>
    ''' <param name="centerX">圆心 X 位置（默认网格中心）</param>
    ''' <param name="centerY">圆心 Y 位置（默认网格中心）</param>
    ''' <param name="bottomZ">圆柱底部 Z（含，默认 0）</param>
    ''' <param name="topZ">圆柱顶部 Z（含，默认网格顶端 depth-1）</param>
    Public Shared Function Cylinder(width As Integer, height As Integer, depth As Integer,
                                    radius As Double,
                                    Optional centerX As Double = -1,
                                    Optional centerY As Double = -1,
                                    Optional bottomZ As Double = 0,
                                    Optional topZ As Double = -1) As VoxelShape

        If centerX < 0 Then centerX = (width - 1) * 0.5
        If centerY < 0 Then centerY = (height - 1) * 0.5
        If topZ < 0 Then topZ = depth - 1

        Dim n = width * height * depth
        Dim data(n - 1) As Boolean
        Dim r2 = radius * radius

        For x = 0 To width - 1
            Dim dx = x - centerX
            For y = 0 To height - 1
                Dim dy = y - centerY
                Dim radial2 = dx * dx + dy * dy
                If radial2 > r2 Then Continue For   ' 超出圆柱半径，必为空腔

                For z = 0 To depth - 1
                    If z < bottomZ OrElse z > topZ Then Continue For
                    data((x * height + y) * depth + z) = True
                Next
            Next
        Next

        Return New VoxelShape(width, height, depth, data)

    End Function

#End Region

End Class
