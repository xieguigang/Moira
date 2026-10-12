' /********************************************************************************/
'
'   VoxelSceneBuilder.vb
'
'   体素场景构建器 —— 帧 + 视图状态 → 点云 / 线段
'
'   作用：
'       把一帧 CFD 体数据按当前视图状态（标量场、值域、阈值、横截面、
'       可视化类型、拾取高亮）转换为 DxScene3DCanvas 可渲染的点云与线段集合。
'       热图颜色由调用方传入的 LUT 查表得到（LUT 由 Designer.FromSchema 生成）。
'
'   可视化类型互斥：
'       热图 / 矢量图 / 流线图三种类型由 VoxelViewOptions.Mode 单一状态表达，
'       只有热图会生成物理场体素点云；矢量图与流线图只画对应的线段图层。
'
'   不活动的固体模型本体：
'       metadata 掩膜 mask = 0 的体素（罐壁 / 挡板 / 车体等）以固定灰色常驻渲染，
'       三种可视化类型下都显示；只有 ClipSolidModelWithSection = True 时
'       才跟随远端横截面一起被裁剪。
'
'   质心补偿：
'       Scene.LoadPointCloud 每次都会把点云按其质心平移到原点。横截面与
'       阈值过滤会改变可见子集的质心，导致模型在屏幕上跳动。这里对传给
'       场景的坐标做补偿（+ C_full - C_visible），使传入集合的质心恒等于
'       全体活动体素的质心，从而渲染位置恒定为 世界坐标 - C_full。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Moira.CFDDxCanvas.Data

Namespace Rendering

    '''
    ''' 可视化类型（严格互斥：同一时刻只能开启一种）。
    '''
    '''   热图（体素模型热图点云）/ 矢量图 / 流线图 三者互斥，
    '''   由单一状态源（画布的 Visualization 属性、选项的 Mode）表达，
    '''   避免"互相置 False"式散落逻辑。不活动的固体模型本体不属于可视化类型，
    '''   在三种模式下都常驻渲染。
    '''
    Public Enum VisualizationMode
        ''' <summary>体素热图（默认）：物理场体素按标量值 LUT 着色。</summary>
        Heatmap = 0
        ''' <summary>矢量图：速度矢量线段（原"速度箭头"叠加层已并入本类型）。</summary>
        Vectors = 1
        ''' <summary>流线图：入口面种子 RK2 积分轨迹 + 沿线光点粒子动画。</summary>
        Streamlines = 2
    End Enum

    ''' <summary>
    ''' 视图状态快照（渲染参数的只读载体）。
    ''' </summary>
    Public Class VoxelViewOptions

        ''' <summary>当前显示的标量场名称（如 pressure / ph / gene_glc_fermenter[3]）。</summary>
        Public Property Field As String
        Public Property RangeMin As Double
        Public Property RangeMax As Double
        ''' <summary>透明阈值 0..1（归一化值 &lt; 阈值的体素被隐藏）。</summary>
        Public Property Threshold As Double

        Public Property SectionEnabled As Boolean
        ''' <summary>切片模式：只显示截面位置处的一层体素（而非裁掉远侧）。</summary>
        Public Property SliceOnly As Boolean
        Public Property SectionAxis As CfdAxis
        Public Property SectionPosition As Integer

        '''
        ''' 可视化类型（热图 / 矢量图 / 流线图，严格互斥）。
        '''
        '''   热图模式才生成物理场体素点云；矢量图与流线图模式下
        '''   只画对应的线段图层 + 常驻的固体模型本体。
        '''
        Public Property Mode As VisualizationMode = VisualizationMode.Heatmap

        '''
        ''' 是否显示不活动的三维体素模型本体（metadata 掩膜 mask = 0 的体素）。
        '''
        '''   三种可视化模式下都常驻显示，且不受显示阈值影响。
        '''
        Public Property ShowSolids As Boolean = True

        '''
        ''' 裁剪远端横截面时，是否把不活动的固体模型本体也一并裁剪隐藏。
        '''
        '''   False（默认）= 只裁剪活动的物理场体素，模型本体保持完整；
        '''   True = 模型本体也按同一截面规则裁剪。
        '''
        Public Property ClipSolidModelWithSection As Boolean = False

        '''
        ''' 固体模型本体的点数上限（超出时按格点步长确定性抽稀，控制每帧传输量）。
        '''
        Public Property MaxSolidPoints As Integer = 250000

        ''' <summary>矢量图采样步长（每轴，格点）。</summary>
        Public Property VectorStride As Integer = 3

        ''' <summary>流线种子层采样步长（j/k 方向，格点）。</summary>
        Public Property PathlineSeedStride As Integer = 4

        ''' <summary>
        ''' 隐藏直线形态的流线（始终呈直线的来流轨迹），
        ''' 只显示有曲线变化的运动轨迹。
        ''' </summary>
        Public Property HideStraightStreamlines As Boolean = True

        ''' <summary>
        ''' 已缓存的流线折线（帧未变时复用，避免逐帧重追踪）；
        ''' Nothing 时由构建器现场追踪。
        ''' </summary>
        Public Property CachedPathlines As StreamlineSet

        ''' <summary>选中的体素索引（引擎序），-1 = 无。</summary>
        Public Property SelectedVoxel As Integer = -1

        ''' <summary>热图 LUT（由 Designer.FromSchema 生成，256 级）。</summary>
        Public Property Lut As Color()

    End Class

    ''' <summary>
    ''' 一次构建的结果：点云、点索引 → 体素索引映射、线段（箭头 + 高亮框）。
    ''' </summary>
    Public Class VoxelSceneResult

        Public Property Points As PointCloudPoint()
        ''' <summary>Points 数组下标 → 体素引擎索引（拾取映射用）。</summary>
        Public Property PointVoxelMap As Integer()
        Public Property Lines As LineSegment()

        ''' <summary>
        ''' 流线折线集合（原始世界坐标，供动画光点粒子复用）；
        ''' 未启用流线图时为 Nothing。
        ''' </summary>
        Public Property Pathlines As StreamlineSet

        ''' <summary>
        ''' 线段的质心补偿偏移（与点云同一空间）；动画粒子打包时
        ''' 使用同一偏移保证与流线对齐。
        ''' </summary>
        Public Property LineOffsetX As Double
        Public Property LineOffsetY As Double
        Public Property LineOffsetZ As Double

    End Class

    Public Class VoxelSceneBuilder

        ''' <summary>不活动的固体模型本体的渲染颜色（与 WindTunnelCanvas 一致的灰）。</summary>
        Private Const SolidHtml As String = "#94A3B8"

        ' 可复用的可见性缓冲（按需扩容）
        Shared m_visibility As Boolean()
        Shared m_visibleList As Integer()
        Shared m_solidList As Integer()

        '''
        ''' 固体模型本体的抽稀步长：点数超过预算时按格点步长降采样
        ''' （与 WindTunnelCanvas 用 MaxPoints 反求 stride 的做法一致）。
        '''
        Private Shared Function SolidStride(count As Integer, budget As Integer) As Integer
            If budget <= 0 OrElse count <= budget Then
                Return 1
            End If

            Dim s As Integer = CInt(Math.Ceiling(Math.Pow(count / CDbl(budget), 1.0 / 3.0)))

            Return Math.Max(1, s)
        End Function

        ''' <summary>
        ''' 构建当前帧的点云与线段。
        ''' </summary>
        ''' <param name="dataset">数据集（提供几何与活动体素表）</param>
        ''' <param name="frame">当前帧</param>
        ''' <param name="options">视图状态</param>
        Public Shared Function Build(dataset As CfdDataset, frame As VtiFrameData, options As VoxelViewOptions) As VoxelSceneResult
            Dim nx = dataset.Nx, ny = dataset.Ny, nz = dataset.Nz
            Dim sp = dataset.Spacing, org = dataset.Origin
            Dim active = dataset.ActiveIndices
            Dim solids = dataset.SolidIndices
            Dim centroid = dataset.SceneCentroid

            ' 热图模式且标量场已加载时才生成物理场体素点云（三种可视化类型互斥）
            Dim heatmap As Boolean = (options.Mode = VisualizationMode.Heatmap)

            Dim values As Single() = FieldArray(frame, options.Field)

            ' 热图模式且标量场已加载时才生成物理场体素点云
            Dim fluidPoints As Boolean = heatmap AndAlso values IsNot Nothing

            Dim mn As Double = options.RangeMin
            Dim mx As Double = Math.Max(options.RangeMax, mn + 0.000000000001)
            Dim denom As Double = mx - mn
            Dim threshold As Double = options.Threshold

            ' ---- 截面裁剪参数 ----
            '   裁剪模式：保留 c <= cut 一侧（法向 -axis，与网页版一致）
            '   切片模式：只保留 |c - cut| <= 半格 的一层体素
            Dim cut As Double = 0.0
            Dim halfSpan As Double = 0.0
            Dim eps As Double = 0.0

            If options.SectionEnabled Then
                cut = SliceCut(dataset, options.SectionAxis, options.SectionPosition)
                halfSpan = AxisSpacing(dataset, options.SectionAxis) * 0.5
                eps = halfSpan * 0.1
            End If

            ' ---- 第一遍：物理场体素可见性 ----
            Dim n As Integer = If(active Is Nothing, 0, active.Length)
            Dim cap As Integer = Math.Max(1, n * 2)

            If m_visibility Is Nothing OrElse m_visibility.Length < n Then
                m_visibility = New Boolean(cap - 1) {}
            End If
            If m_visibleList Is Nothing OrElse m_visibleList.Length < n Then
                m_visibleList = New Integer(cap - 1) {}
            End If

            Dim visibleCount As Integer = 0
            Dim sx As Double = 0.0, sy As Double = 0.0, sz As Double = 0.0

            For t As Integer = 0 To n - 1
                Dim idx As Integer = active(t)
                Dim visible As Boolean = True

                If options.SectionEnabled Then
                    visible = SectionVisible(dataset, options, cut, halfSpan, eps, idx)
                End If

                If visible AndAlso threshold > 0 AndAlso values IsNot Nothing Then
                    Dim v As Double = values(idx)
                    Dim tc As Double = (v - mn) / denom
                    If tc < threshold Then
                        visible = False
                    End If
                End If

                m_visibility(t) = visible

                If visible Then
                    ' 只有热图模式才把物理场体素画进点云，
                    ' 因此质心累加也只在热图模式下计入（矢量/流线模式以模型本体为锚）
                    If fluidPoints Then
                        Dim cx As Double, cy As Double, cz As Double
                        Call VoxelCenter(dataset, idx, cx, cy, cz)
                        sx += cx : sy += cy : sz += cz
                    End If

                    m_visibleList(visibleCount) = t
                    visibleCount += 1
                End If
            Next

            ' ---- 不活动的固体模型本体（三种模式下常驻，默认不被截面裁剪）----
            Dim solidCount As Integer = 0
            Dim solidIdx As Integer() = solids

            If solidIdx Is Nothing Then
                solidIdx = Array.Empty(Of Integer)()
            End If

            ' 注意：局部变量不能与 SolidStride 仅大小写不同（VB 大小写不敏感会遮蔽函数）
            Dim solidStep As Integer = SolidStride(solidIdx.Length, options.MaxSolidPoints)

            If options.ShowSolids AndAlso solidIdx.Length > 0 Then
                Dim scap As Integer = Math.Max(1, solidIdx.Length)

                If m_solidList Is Nothing OrElse m_solidList.Length < solidIdx.Length Then
                    m_solidList = New Integer(scap - 1) {}
                End If

                ' 固体本体是否参与裁剪由 ClipSolidModelWithSection 决定
                Dim clipSolid As Boolean = options.SectionEnabled AndAlso options.ClipSolidModelWithSection

                For s As Integer = 0 To solidIdx.Length - 1
                    Dim idx As Integer = solidIdx(s)

                    If solidStep > 1 Then
                        Dim si As Integer, sj As Integer, sk As Integer
                        Call dataset.IdxToIJK(idx, si, sj, sk)

                        If (si Mod solidStep) <> 0 OrElse
                            (sj Mod solidStep) <> 0 OrElse
                            (sk Mod solidStep) <> 0 Then

                            Continue For
                        End If
                    End If

                    If clipSolid AndAlso Not SectionVisible(dataset, options, cut, halfSpan, eps, idx) Then
                        Continue For
                    End If

                    Dim cx As Double, cy As Double, cz As Double
                    Call VoxelCenter(dataset, idx, cx, cy, cz)
                    sx += cx : sy += cy : sz += cz

                    m_solidList(solidCount) = idx
                    solidCount += 1
                Next
            End If

            Dim fluidCount As Integer = If(fluidPoints, visibleCount, 0)
            Dim total As Integer = fluidCount + solidCount

            If total = 0 Then
                ' 全部被过滤（极端参数），退化为无点云
                Return New VoxelSceneResult With {
                    .Points = New PointCloudPoint() {},
                    .PointVoxelMap = New Integer() {},
                    .Lines = New LineSegment() {}
                }
            End If

            ' ---- 质心补偿量：使传入点集的质心恒为场景基准 ----
            Dim fx As Double = sx / total
            Dim fy As Double = sy / total
            Dim fz As Double = sz / total
            Dim ox As Double = centroid(0) - fx
            Dim oy As Double = centroid(1) - fy
            Dim oz As Double = centroid(2) - fz

            ' ---- 第二遍：填点云（物理场体素 + 固体模型本体）----
            Dim points(total - 1) As PointCloudPoint
            Dim pointMap(total - 1) As Integer
            Dim lut = options.Lut
            Dim lutN As Integer = lut.Length
            Dim p As Integer = 0

            If fluidPoints Then
                For q As Integer = 0 To visibleCount - 1
                    Dim t As Integer = m_visibleList(q)
                    Dim idx As Integer = active(t)

                    Dim cx As Double, cy As Double, cz As Double
                    Call VoxelCenter(dataset, idx, cx, cy, cz)

                    Dim v As Double = values(idx)
                    Dim tc As Double = (v - mn) / denom

                    If tc < 0.0 Then
                        tc = 0.0
                    ElseIf tc > 1.0 Then
                        tc = 1.0
                    End If

                    Dim color As Color = lut(CInt(tc * (lutN - 1)))

                    points(p) = New PointCloudPoint(cx + ox, cy + oy, cz + oz, tc, HtmlColor(color))
                    pointMap(p) = idx
                    p += 1
                Next
            End If

            For q As Integer = 0 To solidCount - 1
                Dim idx As Integer = m_solidList(q)

                Dim cx As Double, cy As Double, cz As Double
                Call VoxelCenter(dataset, idx, cx, cy, cz)

                ' 固体本体不是物理场体素：标量值 0，拾取映射 -1（不参与字段拾取）
                points(p) = New PointCloudPoint(cx + ox, cy + oy, cz + oz, 0.0, SolidHtml)
                pointMap(p) = -1
                p += 1
            Next

            ' ---- 线段：矢量图 / 流线图（互斥） + 拾取高亮框 ----
            Dim lines As New List(Of LineSegment)
            Dim pathlines As StreamlineSet = Nothing

            Select Case options.Mode
                Case VisualizationMode.Vectors
                    Call lines.AddRange(BuildVectors(dataset, frame, options, ox, oy, oz))

                Case VisualizationMode.Streamlines
                    pathlines = If(options.CachedPathlines, BuildPathlines(dataset, frame, options))

                    If pathlines IsNot Nothing Then
                        Call lines.AddRange(pathlines.BuildLines(options.Lut, 210, ox, oy, oz))
                    End If
            End Select

            If options.SelectedVoxel >= 0 Then
                Dim t As Integer = IndexOfActive(active, options.SelectedVoxel)

                If t >= 0 AndAlso m_visibility(t) Then
                    Call BuildHighlight(dataset, options.SelectedVoxel, ox, oy, oz, lines)
                End If
            End If

            Return New VoxelSceneResult With {
                .Points = points,
                .PointVoxelMap = pointMap,
                .Lines = lines.ToArray(),
                .Pathlines = pathlines,
                .LineOffsetX = ox,
                .LineOffsetY = oy,
                .LineOffsetZ = oz
            }
        End Function

        ''' <summary>选中体素的高亮线框盒（12 条棱，略大于体素）。</summary>
        Private Shared Sub BuildHighlight(dataset As CfdDataset, idx As Integer,
                                          ox As Double, oy As Double, oz As Double,
                                          lines As List(Of LineSegment))
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Dim sp = dataset.Spacing, org = dataset.Origin
            Dim cx As Double = org(0) + (i + 0.5) * sp(0)
            Dim cy As Double = org(1) + (j + 0.5) * sp(1)
            Dim cz As Double = org(2) + (k + 0.5) * sp(2)

            Dim hx As Double = sp(0) * 0.56
            Dim hy As Double = sp(1) * 0.56
            Dim hz As Double = sp(2) * 0.56

            ' 8 个角点（补偿空间）
            Dim corners(7) As Point3D
            Dim n As Integer = 0

            For Each s As Integer In {0, 1}
                For Each t As Integer In {0, 1}
                    For Each r As Integer In {0, 1}
                        corners(n) = New Point3D(
                            cx + If(s = 0, -hx, hx) + ox,
                            cy + If(t = 0, -hy, hy) + oy,
                            cz + If(r = 0, -hz, hz) + oz)
                        n += 1
                    Next
                Next
            Next

            Dim color As Color = Color.FromArgb(255, 17, 24, 39)

            ' 底面 / 顶面 / 竖棱（角点顺序: r 最内层）
            ' corners: 0..3 = z-, 4..7 = z+
            Call lines.Add(New LineSegment(corners(0), corners(1), color))
            Call lines.Add(New LineSegment(corners(1), corners(3), color))
            Call lines.Add(New LineSegment(corners(3), corners(2), color))
            Call lines.Add(New LineSegment(corners(2), corners(0), color))

            Call lines.Add(New LineSegment(corners(4), corners(5), color))
            Call lines.Add(New LineSegment(corners(5), corners(7), color))
            Call lines.Add(New LineSegment(corners(7), corners(6), color))
            Call lines.Add(New LineSegment(corners(6), corners(4), color))

            Call lines.Add(New LineSegment(corners(0), corners(4), color))
            Call lines.Add(New LineSegment(corners(1), corners(5), color))
            Call lines.Add(New LineSegment(corners(3), corners(7), color))
            Call lines.Add(New LineSegment(corners(2), corners(6), color))
        End Sub

        ''' <summary>Color → "#RRGGBB"（DxCanvas 嵌入色路径按 6 位 hex 解析）。</summary>
        Private Shared Function HtmlColor(c As Color) As String
            Return $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        End Function

        ''' <summary>读取帧内某标量场数组（按字段名；未加载时返回 Nothing）。</summary>
        Public Shared Function FieldArray(frame As VtiFrameData, field As String) As Single()
            Return frame.TryGet(field)
        End Function

        ''' <summary>体素中心世界坐标。</summary>
        Public Shared Sub VoxelCenter(dataset As CfdDataset, idx As Integer,
                                      ByRef cx As Double, ByRef cy As Double, ByRef cz As Double)
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Dim sp = dataset.Spacing, org = dataset.Origin
            cx = org(0) + (i + 0.5) * sp(0)
            cy = org(1) + (j + 0.5) * sp(1)
            cz = org(2) + (k + 0.5) * sp(2)
        End Sub

        ''' <summary>
        ''' 体素是否通过截面过滤：
        ''' 裁剪模式保留 c &lt;= cut 一侧；切片模式只保留 |c - cut| &lt;= 半格 的一层。
        ''' </summary>
        Private Shared Function SectionVisible(dataset As CfdDataset,
                                               options As VoxelViewOptions,
                                               cut As Double, halfSpan As Double, eps As Double,
                                               idx As Integer) As Boolean
            Dim c As Double = SliceCoord(dataset, options.SectionAxis, idx)

            If options.SliceOnly Then
                Return Math.Abs(c - cut) <= halfSpan + eps
            End If

            Return c <= cut
        End Function

        ''' <summary>某轴上的格子间距。</summary>
        Public Shared Function AxisSpacing(dataset As CfdDataset, axis As CfdAxis) As Double
            Select Case axis
                Case CfdAxis.X : Return dataset.Spacing(0)
                Case CfdAxis.Y : Return dataset.Spacing(1)
                Case Else : Return dataset.Spacing(2)
            End Select
        End Function

        ''' <summary>截面某轴上的切割坐标 cut = origin + (pos + 0.5) * spacing。</summary>
        Public Shared Function SliceCut(dataset As CfdDataset, axis As CfdAxis, pos As Integer) As Double
            Select Case axis
                Case CfdAxis.X : Return dataset.Origin(0) + (pos + 0.5) * dataset.Spacing(0)
                Case CfdAxis.Y : Return dataset.Origin(1) + (pos + 0.5) * dataset.Spacing(1)
                Case Else : Return dataset.Origin(2) + (pos + 0.5) * dataset.Spacing(2)
            End Select
        End Function

        ''' <summary>体素中心在某轴上的坐标。</summary>
        Public Shared Function SliceCoord(dataset As CfdDataset, axis As CfdAxis, idx As Integer) As Double
            Dim i, j, k As Integer
            Call dataset.IdxToIJK(idx, i, j, k)

            Select Case axis
                Case CfdAxis.X : Return dataset.Origin(0) + (i + 0.5) * dataset.Spacing(0)
                Case CfdAxis.Y : Return dataset.Origin(1) + (j + 0.5) * dataset.Spacing(1)
                Case Else : Return dataset.Origin(2) + (k + 0.5) * dataset.Spacing(2)
            End Select
        End Function

        ''' <summary>体素索引 → 活动表下标（二分查找；活动表按引擎序递增）。</summary>
        Private Shared Function IndexOfActive(active As Integer(), idx As Integer) As Integer
            Dim lo As Integer = 0, hi As Integer = active.Length - 1

            Do While lo <= hi
                Dim mid As Integer = (lo + hi) >> 1

                If active(mid) = idx Then
                    Return mid
                ElseIf active(mid) < idx Then
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            Loop

            Return -1
        End Function

        ''' <summary>
        ''' 矢量图 Pass：带箭头头部的线段矢量（长度∝速度、LUT 着色），
        ''' 与点云使用同一套截面过滤规则。
        ''' </summary>
        Private Shared Function BuildVectors(dataset As CfdDataset,
                                             frame As VtiFrameData,
                                             options As VoxelViewOptions,
                                             ox As Double, oy As Double, oz As Double) As LineSegment()
            If frame.U Is Nothing Then
                ' 该数据集没有速度场，画不出矢量
                Return New LineSegment() {}
            End If

            ' 线段生成在质心补偿空间：与点云 / 固体模型本体对齐
            Dim field As New DatasetFlowField(dataset, frame, ox, oy, oz)
            Dim options2 As New VectorArrowOptions With {
                .Stride = options.VectorStride,
                .Lut = options.Lut,
                .Alpha = 235
            }

            If dataset.HasRange("speed") Then
                options2.SpeedMax = dataset.GetRange("speed").Item2
            End If

            ' 矢量与点云使用同一套截面过滤规则（裁剪 / 切片），显示阈值不作用于矢量
            If options.SectionEnabled Then
                Dim cut As Double = SliceCut(dataset, options.SectionAxis, options.SectionPosition)
                Dim halfSpan As Double = AxisSpacing(dataset, options.SectionAxis) * 0.5
                Dim eps As Double = halfSpan * 0.1

                options2.IncludePoint =
                    Function(i As Integer, j As Integer, k As Integer) As Boolean
                        Dim idx As Integer = dataset.IJKToIdx(i, j, k)
                        Return SectionVisible(dataset, options, cut, halfSpan, eps, idx)
                    End Function
            End If

            Return FlowVectors.BuildArrows(field, options2)
        End Function

        ''' <summary>
        ''' 流线图 Pass：入口面种子 + RK2 积分（流线不受截面过滤影响，
        ''' 保持完整轨迹；调用方用返回的折线做光点粒子动画）。
        ''' </summary>
        Private Shared Function BuildPathlines(dataset As CfdDataset,
                                               frame As VtiFrameData,
                                               options As VoxelViewOptions) As StreamlineSet
            If frame.U Is Nothing Then
                Return Nothing
            End If

            ' 流线在原始世界坐标下追踪（始终完整，不受截面裁剪），
            ' 绘制时才由 BuildLines 平移到质心补偿空间
            Dim field As New DatasetFlowField(dataset, frame, 0.0, 0.0, 0.0)
            Dim opt As New StreamlineOptions With {
                .SeedStride = Math.Max(1, options.PathlineSeedStride)
            }

            If dataset.HasRange("speed") Then
                opt.SpeedMax = dataset.GetRange("speed").Item2
            End If

            Dim result As StreamlineSet = StreamlineTracer.Trace(field, opt)

            ' 直线形态的来流轨迹不显示，只保留有曲线变化的流线
            result.MinCurvature = If(options.HideStraightStreamlines,
                StreamlineSet.DefaultStraightness, 0.0F)

            Return result
        End Function

        ''' <summary>
        ''' IFlowFieldData 的离线数据集适配器：CfdDataset + VtiFrameData
        ''' → 速度场只读视图（体素中心坐标，CenterOffset = 0.5）。
        ''' </summary>
        Private NotInheritable Class DatasetFlowField : Implements IFlowFieldData

            ReadOnly m_ds As CfdDataset
            ReadOnly m_frame As VtiFrameData
            ReadOnly m_ox As Double
            ReadOnly m_oy As Double
            ReadOnly m_oz As Double

            '''
            ''' 构造流场只读视图。
            '''
            '''   ox/oy/oz = 质心补偿偏移，直接叠加到返回的原点上，
            '''   使矢量线段与点云 / 固体模型本体落在同一坐标空间。
            '''
            Public Sub New(ds As CfdDataset, frame As VtiFrameData,
                           ox As Double, oy As Double, oz As Double)
                m_ds = ds
                m_frame = frame
                m_ox = ox
                m_oy = oy
                m_oz = oz
            End Sub

            Public ReadOnly Property Nx As Integer Implements IFlowFieldData.Nx
                Get
                    Return m_ds.Nx
                End Get
            End Property

            Public ReadOnly Property Ny As Integer Implements IFlowFieldData.Ny
                Get
                    Return m_ds.Ny
                End Get
            End Property

            Public ReadOnly Property Nz As Integer Implements IFlowFieldData.Nz
                Get
                    Return m_ds.Nz
                End Get
            End Property

            Public ReadOnly Property OriginX As Single Implements IFlowFieldData.OriginX
                Get
                    Return CSng(m_ds.Origin(0) + m_ox)
                End Get
            End Property

            Public ReadOnly Property OriginY As Single Implements IFlowFieldData.OriginY
                Get
                    Return CSng(m_ds.Origin(1) + m_oy)
                End Get
            End Property

            Public ReadOnly Property OriginZ As Single Implements IFlowFieldData.OriginZ
                Get
                    Return CSng(m_ds.Origin(2) + m_oz)
                End Get
            End Property

            Public ReadOnly Property SpacingX As Single Implements IFlowFieldData.SpacingX
                Get
                    Return CSng(m_ds.Spacing(0))
                End Get
            End Property

            Public ReadOnly Property SpacingY As Single Implements IFlowFieldData.SpacingY
                Get
                    Return CSng(m_ds.Spacing(1))
                End Get
            End Property

            Public ReadOnly Property SpacingZ As Single Implements IFlowFieldData.SpacingZ
                Get
                    Return CSng(m_ds.Spacing(2))
                End Get
            End Property

            ''' <summary>体素中心坐标：格内偏移 0.5。</summary>
            Public ReadOnly Property CenterOffset As Single Implements IFlowFieldData.CenterOffset
                Get
                    Return 0.5F
                End Get
            End Property

            Public Function UxAt(idx As Integer) As Single Implements IFlowFieldData.UxAt
                Return If(m_frame.U IsNot Nothing, m_frame.U(idx), 0.0F)
            End Function

            Public Function UyAt(idx As Integer) As Single Implements IFlowFieldData.UyAt
                Return If(m_frame.V IsNot Nothing, m_frame.V(idx), 0.0F)
            End Function

            Public Function UzAt(idx As Integer) As Single Implements IFlowFieldData.UzAt
                Return If(m_frame.W IsNot Nothing, m_frame.W(idx), 0.0F)
            End Function

            Public Function IsActive(idx As Integer) As Boolean Implements IFlowFieldData.IsActive
                Return m_frame.Mask(idx)
            End Function

            Public Function IsSeedable(idx As Integer) As Boolean Implements IFlowFieldData.IsSeedable
                Return m_frame.Mask(idx)
            End Function

        End Class

    End Class

End Namespace
