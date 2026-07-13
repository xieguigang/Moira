Imports System.IO
Imports System.Text.Json
Imports std = System.Math

' /********************************************************************************/
'
'   VoxelModelLoader.vb
'
'   体素模型加载器 —— 解析三维体素模型 JSON 并生成 CFD 计算空间
'
'   作用：
'       解析 "Moira.CFD.VoxelGrid/v1" schema 的体素 JSON 文件（例如
'       airplane1_28x9x32_voxels.json），将其还原为引擎的 VoxelShape 计算空间。
'
'   JSON 语义约定（valueSemantics）：
'       0 = fluid/exterior space （流体 / 外部空间，参与 CFD 计算）
'       1 = solid region         （固体区域，CFD 中作为固体障碍物）
'
'   与引擎的映射（关键：需反转）：
'       VoxelShape.Shape(idx) = True  → 活动流体体素（参与计算）
'       VoxelShape.Shape(idx) = False → 固体障碍物（空腔）
'       因此：active = (data(idx) <> SolidValue)，即 JSON 值 1 → False（固体），0 → True（流体）。
'
'   索引约定（与 JSON indexFormula 完全一致）：
'       index = (x * height + y) * depth + z
'       等价于 VoxelShape.Index(x, y, z)。
'
'   放大计算空间与模型定位（风洞需求）：
'       BuildDomain 生成一个比模型 grid 更大的计算空间（尺寸 = 模型尺寸 × domainScale），
'       把模型固体体素放入该空间中：X / Z 方向居中，Y（竖直/up）方向使模型
'       最低固体体素位于指定的离地高度 groundClearance；空间其余体素均为流体。
'       用于模拟贴地行驶（groundClearance≈0）或空中飞行（groundClearance 较大）的风洞测试。
'
' /********************************************************************************/

''' <summary>
''' 已解析的体素模型 —— 持有原始尺寸的 VoxelShape（计算空间真相源）与元数据。
''' </summary>
Public Class VoxelModel

    ''' <summary>模型原始尺寸下的体素空间（True = 流体，False = 固体障碍）。</summary>
    Public Property Shape As VoxelShape

    ''' <summary>X 方向体素数（JSON grid.width）</summary>
    Public Property Width As Integer

    ''' <summary>Y 方向体素数（JSON grid.height）</summary>
    Public Property Height As Integer

    ''' <summary>Z 方向体素数（JSON grid.depth）</summary>
    Public Property Depth As Integer

    ''' <summary>体素物理尺寸 [dx, dy, dz]（若 JSON 提供 bounds.voxelSize）。</summary>
    Public Property VoxelSize As Double()

    ''' <summary>源模型名称（JSON sourceModel）。</summary>
    Public Property SourceModel As String

    ''' <summary>固体体素总数（JSON solidVoxelCount，或由 data 统计）。</summary>
    Public Property SolidVoxelCount As Integer

    ''' <summary>
    ''' 固体体素包围盒（网格坐标，含端点）。若无固体体素则各分量为 -1。
    ''' 用于把模型放入放大空间时的居中与离地定位。
    ''' </summary>
    Public Property SolidBounds As (minX As Integer, minY As Integer, minZ As Integer,
                                    maxX As Integer, maxY As Integer, maxZ As Integer)

End Class

''' <summary>
''' 体素模型加载器 —— 从 JSON 解析 VoxelModel，并可构建放大 / 定位后的计算空间。
''' </summary>
Public Class VoxelModelLoader

#Region "加载与解析"

    ''' <summary>
    ''' 从体素 JSON 文件加载 VoxelModel。
    ''' </summary>
    ''' <param name="path">JSON 文件路径</param>
    ''' <param name="solidValue">表示固体的取值（默认 1，来自 valueSemantics）</param>
    Public Shared Function Load(path As String, Optional solidValue As Integer = 1) As VoxelModel

        If String.IsNullOrEmpty(path) Then
            Throw New ArgumentException("体素模型文件路径不能为空", NameOf(path))
        End If
        If Not File.Exists(path) Then
            Throw New FileNotFoundException($"找不到体素模型文件：{path}", path)
        End If

        Dim json As String = File.ReadAllText(path)

        Using doc As JsonDocument = JsonDocument.Parse(json)
            Dim root = doc.RootElement

            ' ---- 读取 grid 维度 ----
            Dim gridEl As JsonElement
            If Not root.TryGetProperty("grid", gridEl) Then
                Throw New InvalidDataException($"体素模型 JSON 缺少 'grid' 段：{path}")
            End If

            Dim width = gridEl.GetProperty("width").GetInt32()
            Dim height = gridEl.GetProperty("height").GetInt32()
            Dim depth = gridEl.GetProperty("depth").GetInt32()

            If width <= 0 OrElse height <= 0 OrElse depth <= 0 Then
                Throw New InvalidDataException(
                    $"体素模型 grid 维度非法 (width={width}, height={height}, depth={depth})：{path}")
            End If

            ' ---- 读取 data 数组 ----
            Dim dataEl As JsonElement
            If Not root.TryGetProperty("data", dataEl) OrElse dataEl.ValueKind <> JsonValueKind.Array Then
                Throw New InvalidDataException($"体素模型 JSON 缺少数组 'data' 段：{path}")
            End If

            Dim expected = width * height * depth
            Dim data(expected - 1) As Integer
            Dim n As Integer = 0
            For Each item As JsonElement In dataEl.EnumerateArray()
                If n >= expected Then
                    Throw New InvalidDataException(
                        $"体素模型 data 长度超过 width*height*depth={expected}：{path}")
                End If
                data(n) = item.GetInt32()
                n += 1
            Next
            If n <> expected Then
                Throw New InvalidDataException(
                    $"体素模型 data 长度 {n} 与 width*height*depth={expected} 不一致：{path}")
            End If

            ' ---- 反转映射为 VoxelShape（值=solidValue → 固体 False，其它 → 流体 True）----
            Dim shape = FromVoxelArray(width, height, depth, data, solidValue)

            ' ---- 统计固体体素数与包围盒 ----
            Dim solidCount As Integer = 0
            Dim minX = Integer.MaxValue, minY = Integer.MaxValue, minZ = Integer.MaxValue
            Dim maxX = -1, maxY = -1, maxZ = -1
            For x = 0 To width - 1
                For y = 0 To height - 1
                    For z = 0 To depth - 1
                        Dim idx = (x * height + y) * depth + z
                        If data(idx) = solidValue Then
                            solidCount += 1
                            If x < minX Then minX = x
                            If y < minY Then minY = y
                            If z < minZ Then minZ = z
                            If x > maxX Then maxX = x
                            If y > maxY Then maxY = y
                            If z > maxZ Then maxZ = z
                        End If
                    Next
                Next
            Next
            If solidCount = 0 Then
                minX = -1 : minY = -1 : minZ = -1
            End If

            ' ---- 读取可选元数据 ----
            Dim sourceModel As String = Nothing
            Dim srcEl As JsonElement
            If root.TryGetProperty("sourceModel", srcEl) AndAlso srcEl.ValueKind = JsonValueKind.String Then
                sourceModel = srcEl.GetString()
            End If

            Dim voxelSize As Double() = Nothing
            Dim boundsEl As JsonElement
            If root.TryGetProperty("bounds", boundsEl) Then
                Dim vsEl As JsonElement
                If boundsEl.TryGetProperty("voxelSize", vsEl) AndAlso vsEl.ValueKind = JsonValueKind.Array Then
                    Dim list As New List(Of Double)
                    For Each v As JsonElement In vsEl.EnumerateArray()
                        list.Add(v.GetDouble())
                    Next
                    voxelSize = list.ToArray()
                End If
            End If

            Return New VoxelModel With {
                .Shape = shape,
                .Width = width,
                .Height = height,
                .Depth = depth,
                .VoxelSize = voxelSize,
                .SourceModel = sourceModel,
                .SolidVoxelCount = solidCount,
                .SolidBounds = (minX, minY, minZ, maxX, maxY, maxZ)
            }
        End Using

    End Function

    ''' <summary>
    ''' 由 0/1 体素数组构造 VoxelShape（反转映射）。
    ''' active = (data(idx) &lt;&gt; solidValue)，即固体值 → False（障碍），其它 → True（流体）。
    ''' </summary>
    Public Shared Function FromVoxelArray(width As Integer, height As Integer, depth As Integer,
                                          data As Integer(), Optional solidValue As Integer = 1) As VoxelShape
        If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))
        Dim expected = width * height * depth
        If data.Length <> expected Then
            Throw New ArgumentException(
                $"体素数组长度 {data.Length} 与 width*height*depth={expected} 不一致", NameOf(data))
        End If

        Dim mask(expected - 1) As Boolean
        For i = 0 To expected - 1
            mask(i) = (data(i) <> solidValue)   ' 固体 → False（障碍）；流体/外部 → True（活动）
        Next
        Return New VoxelShape(width, height, depth, mask)
    End Function

#End Region

#Region "放大计算空间与模型定位"

    ''' <summary>
    ''' 生成放大并定位后的计算空间 VoxelShape（风洞域）。
    '''
    ''' 尺寸 = (Width*domainScale, Height*domainScale, Depth*domainScale)。
    ''' 空间先全部置为流体（True），再把模型固体体素按偏移写入（False）：
    '''   - X / Z 方向：模型居中
    '''   - Y（竖直 / up）方向：使模型最低固体体素位于 groundClearance（离地高度）
    ''' 用于模拟贴地行驶（groundClearance≈0）或空中飞行（groundClearance 较大）。
    ''' </summary>
    ''' <param name="model">已加载的体素模型</param>
    ''' <param name="domainScale">计算空间相对模型 grid 的放大倍数（默认 2）</param>
    ''' <param name="groundClearance">模型最低固体体素距底面 (j=0) 的高度（默认 0，贴地）</param>
    Public Shared Function BuildDomain(model As VoxelModel,
                                       Optional domainScale As Double = 2.0,
                                       Optional groundClearance As Integer = 0) As VoxelShape

        If model Is Nothing Then Throw New ArgumentNullException(NameOf(model))
        If domainScale < 1.0 Then
            Throw New ArgumentException("domainScale 必须 >= 1.0", NameOf(domainScale))
        End If
        If groundClearance < 0 Then
            Throw New ArgumentException("groundClearance 不能为负", NameOf(groundClearance))
        End If

        ' 放大后的计算空间尺寸（向上取整，至少与模型一样大）
        Dim dnx = std.Max(model.Width, CInt(std.Ceiling(model.Width * domainScale)))
        Dim dny = std.Max(model.Height, CInt(std.Ceiling(model.Height * domainScale)))
        Dim dnz = std.Max(model.Depth, CInt(std.Ceiling(model.Depth * domainScale)))

        Dim total = dnx * dny * dnz
        Dim mask(total - 1) As Boolean
        ' 先全部置为流体（活动区）
        For i = 0 To total - 1
            mask(i) = True
        Next

        ' 模型固体体素包围盒（若无固体则不放置任何障碍）
        Dim b = model.SolidBounds
        If b.minX < 0 Then
            Return New VoxelShape(dnx, dny, dnz, mask)
        End If

        ' ---- 计算偏移量 ----
        ' X / Z 居中：把模型整体（0..Width-1）放入放大空间中央
        Dim xOffset = (dnx - model.Width) \ 2
        Dim zOffset = (dnz - model.Depth) \ 2
        ' Y 方向：模型最低固体体素 b.minY 抬到 groundClearance
        Dim yOffset = groundClearance - b.minY

        ' ---- 把模型固体体素写入放大空间（对应位置 False = 障碍）----
        Dim srcShape = model.Shape
        For x = 0 To model.Width - 1
            Dim tx = x + xOffset
            If tx < 0 OrElse tx >= dnx Then Continue For
            For y = 0 To model.Height - 1
                Dim ty = y + yOffset
                If ty < 0 OrElse ty >= dny Then Continue For
                For z = 0 To model.Depth - 1
                    ' 模型体素为固体（IsActive=False）时，写入放大空间为障碍
                    If Not srcShape.IsActive(x, y, z) Then
                        Dim tz = z + zOffset
                        If tz < 0 OrElse tz >= dnz Then Continue For
                        mask((tx * dny + ty) * dnz + tz) = False
                    End If
                Next
            Next
        Next

        Return New VoxelShape(dnx, dny, dnz, mask)

    End Function

#End Region

End Class
