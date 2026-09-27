' /********************************************************************************/
'
'   CFDCanvas.vb
'
'   CFD 结果三维可视化视口控件（DirectX 加速）
'
'   作用：
'       基于 DxScene3DCanvas 的 3D 体渲染视口：体素点云热图着色、
'       速度矢量线段箭头、横截面过滤、体素拾取高亮、色标条叠加。
'       控件只负责视口本身，控制面板 / 信息面板 / 时间轴由宿主窗体
'       通过本控件公开的属性、方法与事件自行搭建（复刻 cfd-player.html
'       的数据可视化功能；热图颜色统一由 Designer.FromSchema 生成）。
'
' /********************************************************************************/

Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports CDFDxCanvas.Data
Imports CDFDxCanvas.Rendering
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Imaging.Drawing3D

''' <summary>
''' CFD 结果三维可视化视口控件。
''' </summary>
<ToolboxItem(True)>
<Description("CFD 结果三维可视化视口（DirectX 加速）")>
Partial Public Class CFDCanvas

    ' ---------------- 内部状态 ----------------

    Dim m_dataset As CfdDataset
    Dim m_currentFrame As VtiFrameData
    Dim m_frameIndex As Integer = -1
    Dim m_sceneLoaded As Boolean = False

    Dim m_field As String = "pressure"
    Dim m_palette As ScalerPalette = ScalerPalette.Jet
    Dim m_lut As Color()
    Dim m_autoRange As Boolean = True
    Dim m_rangeMin As Double = 0.0
    Dim m_rangeMax As Double = 1.0
    Dim m_threshold As Double = 0.0

    Dim m_showArrows As Boolean = False
    Dim m_arrowDensity As Integer = 2

    Dim m_sectionEnabled As Boolean = False
    Dim m_sectionSliceOnly As Boolean = False
    Dim m_sectionAxis As CfdAxis = CfdAxis.X
    Dim m_sectionPosition As Integer = 0

    Dim m_selectedVoxel As Integer = -1

    ' 拾取映射：当前点云下标 → 体素引擎索引
    Dim m_pointVoxelMap As Integer()

    ' 异步帧加载状态
    Dim m_loading As Boolean = False
    Dim m_pendingIndex As Integer = -1
    Dim m_appliedIndex As Integer = -1

    ''' <summary>是否已经在速率场缺失时补请求过一次速度场（避免无速度场的数据集死循环）。</summary>
    Dim m_velocityRequested As Boolean = False

    ' ---------------- 鼠标悬停提示 ----------------

    Dim m_showHoverTooltip As Boolean = False
    Dim m_hoverVoxel As Integer = -1
    Dim m_hoverX As Integer = 0
    Dim m_hoverY As Integer = 0

    ' ---------------- 点大小与调试叠加层 ----------------

    Dim m_autoPointSize As Boolean = True
    Dim m_pointFill As Single = 1.05F
    Dim m_tooltipFields As String()
    Dim m_showDebugInfo As Boolean = False

    ' FPS 统计（滚动窗口）
    ReadOnly m_renderClock As New Stopwatch
    Dim m_frameCount As Integer = 0
    Dim m_fps As Double = 0.0

    ''' <summary>鼠标当前位置（视口客户区坐标，调试叠加层显示用）。</summary>
    Dim m_mousePosX As Integer = -1
    Dim m_mousePosY As Integer = -1

    ''' <summary>
    ''' 是否在鼠标悬停到体素上时显示半透明数据提示框
    ''' （体素坐标 + VTI 中全部字段的数值）。
    ''' </summary>
    <Description("悬停显示体素数据提示框")>
    Public Property ShowHoverTooltip As Boolean
        Get
            Return m_showHoverTooltip
        End Get
        Set(value As Boolean)
            If m_showHoverTooltip = value Then Return
            m_showHoverTooltip = value

            If Not value AndAlso m_hoverVoxel >= 0 Then
                m_hoverVoxel = -1
                Call m_sceneCanvas.RequestRender()
            End If
        End Set
    End Property

    ' 属性变化防抖定时器（阈值 / 截面滑条拖动时的刷新节流）
    ReadOnly m_refreshTimer As New Timer With {.Interval = 80, .Enabled = False}

    ''' <summary>右上角色标条叠加层。</summary>
    ReadOnly m_colorbar As New ColorbarOverlay

    ' ---------------- 事件 ----------------

    ''' <summary>数据集加载完成。</summary>
    Public Event DatasetLoaded(dataset As CfdDataset)

    ''' <summary>当前显示帧已刷新。</summary>
    Public Event FrameChanged(frameIndex As Integer, time As Double)

    ''' <summary>用户点击拾取了一个可见体素。</summary>
    Public Event VoxelPicked(e As VoxelPickEventArgs)

    ''' <summary>拾取被清除（右键点击视口）。</summary>
    Public Event VoxelPickCleared()

    ' ---------------- 构造 ----------------

    Public Sub New()
        ' 此调用是设计器所必需的
        InitializeComponent()

        ' 体渲染固定使用点云模式 + 逐点嵌入颜色（热图 LUT 由本控件生成）
        m_sceneCanvas.RenderMode = SceneRenderMode.PointCloud
        m_sceneCanvas.UseEmbeddedColor = True
        m_sceneCanvas.MultisampleCount = 4

        AddHandler m_refreshTimer.Tick, AddressOf OnRefreshTimerTick
        AddHandler m_sceneCanvas.Render, AddressOf OnSceneRender
        AddHandler m_sceneCanvas.ViewChanged, AddressOf OnSceneViewChanged
    End Sub

    ' ---------------- 公开属性 ----------------

    ''' <summary>当前数据集（未加载时为 Nothing）。</summary>
    Public ReadOnly Property Dataset As CfdDataset
        Get
            Return m_dataset
        End Get
    End Property

    ''' <summary>是否已加载数据。</summary>
    Public ReadOnly Property IsReady As Boolean
        Get
            Return m_dataset IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' 当前显示的标量场（字段名，如 pressure / ph / gene_glc_fermenter[3]）。
    ''' 可选项见 <see cref="AvailableFields"/>。
    ''' </summary>
    <Description("显示的标量场")>
    Public Property Field As String
        Get
            Return m_field
        End Get
        Set(value As String)
            If String.Equals(m_field, value, StringComparison.OrdinalIgnoreCase) Then Return
            m_field = value
            Call EnsureFieldRange(m_field)
            Call EnsureFrameHasField(value)
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>数据集中可选的全部标量场名称。</summary>
    Public Function AvailableFields() As String()
        If m_dataset Is Nothing Then Return New String() {}
        Return m_dataset.FieldNames
    End Function

    ''' <summary>热图调色板（颜色由 Designer.FromSchema 生成）。</summary>
    <Description("热图调色板")>
    Public Property Palette As ScalerPalette
        Get
            Return m_palette
        End Get
        Set(value As ScalerPalette)
            If m_palette = value Then Return
            m_palette = value
            m_lut = Nothing
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>是否自动值域（随全局数据统计）。</summary>
    <Description("自动颜色值域")>
    Public Property AutoRange As Boolean
        Get
            Return m_autoRange
        End Get
        Set(value As Boolean)
            If m_autoRange = value Then Return
            m_autoRange = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>手动值域下界。</summary>
    Public Property RangeMin As Double
        Get
            Return m_rangeMin
        End Get
        Set(value As Double)
            m_rangeMin = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>手动值域上界。</summary>
    Public Property RangeMax As Double
        Get
            Return m_rangeMax
        End Get
        Set(value As Double)
            m_rangeMax = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>透明阈值 0..1（归一化值低于阈值者隐藏）。</summary>
    <Description("透明阈值 0-1")>
    Public Property Threshold As Double
        Get
            Return m_threshold
        End Get
        Set(value As Double)
            value = Math.Min(1.0, Math.Max(0.0, value))

            If Math.Abs(m_threshold - value) < 0.0001 Then Return
            m_threshold = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>是否显示速度矢量箭头。</summary>
    <Description("显示速度矢量箭头")>
    Public Property ShowArrows As Boolean
        Get
            Return m_showArrows
        End Get
        Set(value As Boolean)
            If m_showArrows = value Then Return
            m_showArrows = value

            If value AndAlso m_currentFrame IsNot Nothing AndAlso m_currentFrame.U Is Nothing Then
                ' 该帧还没加载速度场：重新请求一次，数据集会把缺失字段并入缓存帧
                If Not m_velocityRequested Then
                    m_velocityRequested = True
                    Call ShowFrame(m_frameIndex)
                    Return
                End If
            End If

            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>箭头采样密度（步长，2=高 3=中 4=低）。</summary>
    Public Property ArrowDensity As Integer
        Get
            Return m_arrowDensity
        End Get
        Set(value As Integer)
            value = Math.Min(8, Math.Max(1, value))

            If m_arrowDensity = value Then Return
            m_arrowDensity = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>是否启用横截面。</summary>
    <Description("启用横截面裁剪")>
    Public Property SectionEnabled As Boolean
        Get
            Return m_sectionEnabled
        End Get
        Set(value As Boolean)
            If m_sectionEnabled = value Then Return
            m_sectionEnabled = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>
    ''' 切片模式：启用横截面时只显示截面轴位置处的一层体素
    ''' （而不是把远侧裁掉），便于观察某一层的场数据。
    ''' </summary>
    <Description("横截面切片模式（只显示一层体素）")>
    Public Property SliceOnly As Boolean
        Get
            Return m_sectionSliceOnly
        End Get
        Set(value As Boolean)
            If m_sectionSliceOnly = value Then Return
            m_sectionSliceOnly = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>横截面轴。</summary>
    Public Property SectionAxis As CfdAxis
        Get
            Return m_sectionAxis
        End Get
        Set(value As CfdAxis)
            If m_sectionAxis = value Then Return
            m_sectionAxis = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>横截面位置（体素索引，0..N-1）。</summary>
    Public Property SectionPosition As Integer
        Get
            Return m_sectionPosition
        End Get
        Set(value As Integer)
            If m_sectionPosition = value Then Return
            m_sectionPosition = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>选中的体素（引擎索引，-1 = 无）。设置后视口绘制高亮框。</summary>
    Public Property SelectedVoxel As Integer
        Get
            Return m_selectedVoxel
        End Get
        Set(value As Integer)
            If m_selectedVoxel = value Then Return
            m_selectedVoxel = value
            Call ScheduleRefresh()
        End Set
    End Property

    ''' <summary>当前帧序号。设置后异步刷新视口。</summary>
    Public Property FrameIndex As Integer
        Get
            Return m_frameIndex
        End Get
        Set(value As Integer)
            Call ShowFrame(value)
        End Set
    End Property

    ''' <summary>
    ''' 点大小（像素）。设置后将关闭缩放自适应（<see cref="AutoPointSize"/>）。
    ''' </summary>
    <DefaultValue(6)>
    Public Property PointSize As Integer
        Get
            Return m_sceneCanvas.PointSize
        End Get
        Set(value As Integer)
            m_autoPointSize = False
            m_sceneCanvas.PointSize = value
            Call m_sceneCanvas.RequestRender()
        End Set
    End Property

    ''' <summary>
    ''' 缩放自适应点大小：让相邻体素方格始终相互衔接（不出现空隙）。
    ''' 默认开启；手动设置 <see cref="PointSize"/> 后自动关闭。
    ''' </summary>
    <Description("随缩放自适应体素点大小")>
    Public Property AutoPointSize As Boolean
        Get
            Return m_autoPointSize
        End Get
        Set(value As Boolean)
            If m_autoPointSize = value Then Return
            m_autoPointSize = value

            If value Then
                Call AdaptPointSize()
            End If
        End Set
    End Property

    ''' <summary>体素点方格相对体素间距的覆盖系数（1.0 = 恰好相接）。</summary>
    <DefaultValue(1.05F)>
    Public Property PointFill As Single
        Get
            Return m_pointFill
        End Get
        Set(value As Single)
            value = Math.Min(2.0F, Math.Max(0.5F, value))
            If Math.Abs(m_pointFill - value) < 0.001F Then Return
            m_pointFill = value
            Call AdaptPointSize()
        End Set
    End Property

    ''' <summary>
    ''' 悬停提示框中显示的字段清单（Nothing 或空 = 显示全部字段）。
    ''' </summary>
    Public Property TooltipFields As String()
        Get
            Return m_tooltipFields
        End Get
        Set(value As String())
            m_tooltipFields = value
            Call m_sceneCanvas.RequestRender()
        End Set
    End Property

    ''' <summary>
    ''' 是否在视口右下角显示三维图形引擎调试信息
    ''' （FPS / 鼠标位置 / 相机姿态 / FOV / 视距 / DirectX 设备信息等）。
    ''' </summary>
    <Description("显示 DirectX 调试信息叠加层")>
    Public Property ShowDebugInfo As Boolean
        Get
            Return m_showDebugInfo
        End Get
        Set(value As Boolean)
            If m_showDebugInfo = value Then Return
            m_showDebugInfo = value
            Call m_sceneCanvas.RequestRender()
        End Set
    End Property

    ''' <summary>3D 场景画布（DxCanvas 封装的 DirectX 渲染控件）。</summary>
    Public ReadOnly Property SceneCanvas As DxScene3DCanvas
        Get
            Return m_sceneCanvas
        End Get
    End Property

    ''' <summary>某截面轴上的体素数（宿主据此设置滑条上限）。</summary>
    Public Function SectionLength(axis As CfdAxis) As Integer
        If m_dataset Is Nothing Then Return 0

        Select Case axis
            Case CfdAxis.X : Return m_dataset.Nx
            Case CfdAxis.Y : Return m_dataset.Ny
            Case Else : Return m_dataset.Nz
        End Select
    End Function

    ''' <summary>当前生效的值域（自动时来自数据集全局统计）。</summary>
    Public Function CurrentRange() As Tuple(Of Double, Double)
        If m_autoRange AndAlso m_dataset IsNot Nothing Then
            Return m_dataset.GetRange(m_field)
        Else
            Dim mx As Double = Math.Max(m_rangeMax, m_rangeMin + 0.0000001)
            Return New Tuple(Of Double, Double)(m_rangeMin, mx)
        End If
    End Function

    ''' <summary>当前热图 LUT（256 级，宿主可用于自绘 2D 热图）。</summary>
    Public Function GetPaletteLut() As Color()
        If m_lut Is Nothing Then
            m_lut = Designer.FromSchema(m_palette, 256)
        End If
        Return m_lut
    End Function

    ' ---------------- 数据加载与帧刷新 ----------------

    ''' <summary>
    ''' 从文件夹加载数据集（metadata.json + *.vti），并显示第一帧。
    ''' 耗时操作，建议在后台线程调用后回到 UI 线程。
    ''' </summary>
    Public Sub LoadDataset(folder As String)
        Dim dataset As CfdDataset = CfdDataset.Load(folder)

        AddHandler dataset.RangeUpdated, AddressOf OnDatasetRangeUpdated

        m_field = dataset.DefaultField()

        m_dataset = dataset
        m_currentFrame = Nothing
        m_frameIndex = -1
        m_sceneLoaded = False
        m_selectedVoxel = -1
        m_pointVoxelMap = Nothing
        m_appliedIndex = -1
        m_loading = False
        m_pendingIndex = -1
        m_velocityRequested = False

        ' 预生成 LUT
        m_lut = Designer.FromSchema(m_palette, 256)

        RaiseEvent DatasetLoaded(dataset)

        Call ShowFrame(0)
        Call EnsureFieldRange(m_field)
    End Sub

    ''' <summary>
    ''' 当前视图真正需要用到的字段：显示标量场 + 速度场
    ''' （拾取信息与箭头都要用速度；速度场是懒加载的，随帧一起请求）。
    ''' </summary>
    Private Function WantedFields() As String()
        Dim list As New List(Of String)()

        If m_field IsNot Nothing Then list.Add(m_field)
        list.Add("velocity")

        Return list.ToArray()
    End Function

    ''' <summary>
    ''' 确保当前帧里已经加载了指定标量场；缺失时重新请求当前帧
    ''' （数据集会把缺失字段合并进缓存的帧对象）。
    ''' </summary>
    Private Sub EnsureFrameHasField(field As String)
        If field Is Nothing OrElse m_currentFrame Is Nothing Then Return
        If m_frameIndex < 0 Then Return
        If m_currentFrame.Fields.ContainsKey(field) Then Return

        Call ShowFrame(m_frameIndex)
    End Sub

    ''' <summary>
    ''' 后台计算某标量场的精确全局值域（已完成过则跳过）。
    ''' </summary>
    Public Sub EnsureFieldRange(field As String)
        If field Is Nothing OrElse m_dataset Is Nothing Then Return
        If m_dataset.HasRange(field) Then Return

        Dim dataset = m_dataset

        Call Task.Run(
            Sub()
                Try
                    Call dataset.ComputeGlobalRange(field)
                Catch ex As Exception
                    ' 值域统计失败不影响渲染（退回当前已知值域）
                End Try
            End Sub)
    End Sub

    ''' <summary>
    ''' 显示指定帧（异步加载，加载完成后自动刷新视口并触发 FrameChanged）。
    ''' 重复调用时总是显示最后一次请求的帧。
    ''' </summary>
    Public Sub ShowFrame(index As Integer)
        If m_dataset Is Nothing Then Return
        If index < 0 OrElse index >= m_dataset.FrameCount Then Return

        m_pendingIndex = index

        If m_loading Then Return

        m_loading = True
        Dim dataset = m_dataset
        Dim request As Integer = index
        Dim wanted As String() = WantedFields()

        Call Task.Run(Function() dataset.GetFrame(request, wanted)).ContinueWith(
            Sub(t)
                m_loading = False

                If Not IsHandleCreated OrElse IsDisposed Then Return

                Dim frame As VtiFrameData = Nothing

                If Not t.IsFaulted Then
                    frame = t.Result
                End If

                BeginInvoke(
                    Sub()
                        If IsDisposed Then Return

                        If frame IsNot Nothing AndAlso request = m_pendingIndex Then
                            Call ApplyFrame(request, frame)
                        ElseIf m_pendingIndex <> m_appliedIndex Then
                            ' 加载期间又有了新请求
                            Call ShowFrame(m_pendingIndex)
                        End If
                    End Sub)
            End Sub)
    End Sub

    ''' <summary>把加载好的帧应用到视口（UI 线程）。</summary>
    Private Sub ApplyFrame(index As Integer, frame As VtiFrameData)
        m_currentFrame = frame
        m_frameIndex = index
        m_appliedIndex = index

        Call RebuildScene()

        Dim meta = m_dataset.GetFrameMeta(index)
        RaiseEvent FrameChanged(index, If(meta IsNot Nothing, meta.Time, 0.0))
    End Sub

    ''' <summary>重建 3D 场景（点云 + 线段）。</summary>
    Private Sub RebuildScene()
        If m_dataset Is Nothing OrElse m_currentFrame Is Nothing Then Return

        ' 当前帧还没加载显示字段（懒加载）：先补载，等新帧应用后再重建
        If m_field IsNot Nothing AndAlso Not m_currentFrame.Fields.ContainsKey(m_field) Then
            Call EnsureFrameHasField(m_field)
            Return
        End If

        Dim range = CurrentRange()
        Dim options As New VoxelViewOptions With {
            .Field = m_field,
            .RangeMin = range.Item1,
            .RangeMax = range.Item2,
            .Threshold = m_threshold,
            .SectionEnabled = m_sectionEnabled,
            .SliceOnly = m_sectionSliceOnly,
            .SectionAxis = m_sectionAxis,
            .SectionPosition = m_sectionPosition,
            .ShowArrows = m_showArrows,
            .ArrowDensity = m_arrowDensity,
            .SelectedVoxel = m_selectedVoxel,
            .Lut = GetPaletteLut()
        }

        Dim result As VoxelSceneResult

        Try
            result = VoxelSceneBuilder.Build(m_dataset, m_currentFrame, options)
        Catch ex As Exception
            Return
        End Try

        If result.Points.Length = 0 Then
            ' 全部体素被阈值/截面过滤：清空场景而不是保留旧画面
            m_pointVoxelMap = Nothing

            If m_sceneLoaded Then
                Call m_sceneCanvas.UpdatePointCloud(New PointCloudPoint() {})
                Call m_sceneCanvas.UpdateConnections(New LineSegment() {})
            End If
            Return
        End If

        m_pointVoxelMap = result.PointVoxelMap

        ' 色标条标题：场名 + [min, max]
        m_colorbar.Lut = options.Lut
        m_colorbar.Visible = True
        m_colorbar.Title = $"{FieldLabel(m_field)}  [{range.Item1:F3}, {range.Item2:F3}]"

        If Not m_sceneLoaded Then
            ' 首次装载：LoadPointCloud 自动 FitView 对准网格中心
            Call m_sceneCanvas.LoadPointCloud(result.Points)
            Call m_sceneCanvas.LoadConnections(result.Lines)
            m_sceneLoaded = True
        Else
            ' 逐帧刷新：保持相机不动
            Call m_sceneCanvas.UpdatePointCloud(result.Points)
            Call m_sceneCanvas.UpdateConnections(result.Lines)
        End If

        Call m_sceneCanvas.RequestRender()

        ' 场景（相机）就绪后立即校准一次体素点大小
        Call AdaptPointSize()
    End Sub

    Private Sub OnRefreshTimerTick(sender As Object, e As EventArgs)
        Call m_refreshTimer.Stop()
        Call RebuildScene()
    End Sub

    ''' <summary>属性变化后的防抖刷新（80ms 内的连续变化合并为一次重建）。</summary>
    Private Sub ScheduleRefresh()
        If m_dataset Is Nothing OrElse m_currentFrame Is Nothing Then Return

        Call m_refreshTimer.Stop()
        Call m_refreshTimer.Start()
    End Sub

    ''' <summary>标量场显示名（已知场给出中文标注，其余原样返回）。</summary>
    Public Shared Function FieldLabel(field As String) As String
        If field Is Nothing Then Return ""

        Select Case field.ToLowerInvariant()
            Case "pressure" : Return "压力 Pressure"
            Case "density" : Return "密度 Density"
            Case "speed" : Return "速度幅值 |V|"
            Case "temperature_c" : Return "温度 Temperature (℃)"
            Case "ph" : Return "酸碱度 pH"
            Case "ionic_strength_M" : Return "离子强度 (M)"
            Case "do_mgl" : Return "溶解氧 DO (mg/L)"
            Case "cells_total" : Return "细胞总量"
            Case Else
                If field.StartsWith("cells_") Then Return "菌群 " & field.Substring(6)
                If field.StartsWith("conc_") Then Return "浓度 " & field.Substring(5).TrimEnd("e"c).Trim("_"c)
                If field.StartsWith("xfeed_") Then Return "交叉喂养 " & field.Substring(6).TrimEnd("e"c).Trim("_"c)
                If field.StartsWith("gene_") Then Return "基因 " & field
                Return field
        End Select
    End Function

    ' ---------------- 2D 切片与时间序列 ----------------

    ''' <summary>
    ''' 渲染当前帧某轴位置处的 2D 切片热图（复刻网页版 extractSlice + renderSlice）。
    ''' </summary>
    ''' <param name="axis">切片轴</param>
    ''' <param name="pos">切片位置（体素索引）</param>
    ''' <param name="width">目标位图宽</param>
    ''' <param name="height">目标位图高</param>
    Public Function RenderSliceBitmap(axis As CfdAxis, pos As Integer,
                                      width As Integer, height As Integer) As Bitmap
        If m_currentFrame Is Nothing Then
            Return New Bitmap(Math.Max(1, width), Math.Max(1, height))
        End If

        Dim values = VoxelSceneBuilder.FieldArray(m_currentFrame, m_field)
        Dim range = CurrentRange()
        Dim mn As Double = range.Item1
        Dim denom As Double = Math.Max(range.Item2 - range.Item1, 0.0000001)
        Dim lut = GetPaletteLut()
        Dim lutN As Integer = lut.Length

        Dim nx = m_dataset.Nx, ny = m_dataset.Ny, nz = m_dataset.Nz
        Dim plane As Integer = ny * nz

        Dim w As Integer, h As Integer

        Select Case axis
            Case CfdAxis.X : w = ny : h = nz
            Case CfdAxis.Y : w = nx : h = nz
            Case Else : w = nx : h = ny
        End Select

        ' ---- 生成切片源位图 ----
        Using source As New Bitmap(w, h)
            Dim rect As New Rectangle(0, 0, w, h)
            Dim data = source.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                       System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            Dim pixels(w * h - 1) As Integer

            For a As Integer = 0 To w - 1
                For b As Integer = 0 To h - 1
                    Dim idx As Integer

                    Select Case axis
                        Case CfdAxis.X : idx = pos * plane + a * nz + b
                        Case CfdAxis.Y : idx = a * plane + pos * nz + b
                        Case Else : idx = a * plane + b * nz + pos
                    End Select

                    Dim t As Double = (values(idx) - mn) / denom
                    If t < 0.0 Then
                        t = 0.0
                    ElseIf t > 1.0 Then
                        t = 1.0
                    End If

                    Dim c As Color = lut(CInt(t * (lutN - 1)))
                    ' 32bppArgb 内存为小端 ABGR
                    pixels(a + b * w) = &HFF000000 Or (c.R << 16) Or (c.G << 8) Or c.B
                Next
            Next

            Call Marshal.Copy(pixels, 0, data.Scan0, pixels.Length)
            Call source.UnlockBits(data)

            ' ---- 缩放绘制到目标位图 ----
            Dim target As New Bitmap(Math.Max(1, width), Math.Max(1, height))

            Using g As Graphics = Graphics.FromImage(target)
                g.InterpolationMode = Drawing2D.InterpolationMode.Bilinear
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
                Call g.DrawImage(source, 0, 0, target.Width, target.Height)
            End Using

            Return target
        End Using
    End Function

    ''' <summary>
    ''' 获取某体素在当前帧的详细信息（供宿主显示体素信息面板）。
    ''' </summary>
    ''' <param name="voxelIdx">体素引擎索引</param>
    Public Function GetPickInfo(voxelIdx As Integer) As VoxelPickEventArgs
        If m_dataset Is Nothing OrElse m_currentFrame Is Nothing Then Return Nothing

        Dim total As Integer = m_dataset.Nx * m_dataset.Ny * m_dataset.Nz
        If voxelIdx < 0 OrElse voxelIdx >= total Then Return Nothing

        Return BuildPickArgs(voxelIdx)
    End Function

    ''' <summary>
    ''' 提取某体素的时间序列（遍历全部帧；耗时操作，建议在后台线程调用）。
    ''' </summary>
    ''' <param name="idx">体素引擎索引</param>
    ''' <param name="field">标量场</param>
    Public Function GetVoxelSeries(idx As Integer, field As String) As VoxelSeries
        If m_dataset Is Nothing Then
            Return New VoxelSeries With {.Times = New Double() {}, .Values = New Double() {}}
        End If

        Return m_dataset.GetVoxelSeries(idx, field)
    End Function

    ' ---------------- 视口交互与叠加绘制 ----------------

    ''' <summary>
    ''' 视图变化（旋转/缩放/平移）：自适应点大小并刷新调试叠加层。
    ''' </summary>
    Private Sub OnSceneViewChanged(sender As Object, e As EventArgs)
        Call AdaptPointSize()

        If m_showDebugInfo Then
            Call m_sceneCanvas.RequestRender()
        End If
    End Sub

    ''' <summary>
    ''' 缩放自适应点大小：把相邻两个体素中心的投影间距实测出来，
    ''' 令点方格边长 ≈ 间距 × <see cref="PointFill"/>，
    ''' 从而无论放大多少倍，体素方格都恰好相互衔接不留空隙。
    ''' </summary>
    Private Sub AdaptPointSize()
        If Not m_autoPointSize Then Return
        If m_dataset Is Nothing OrElse m_currentFrame Is Nothing Then Return

        Dim active = m_dataset.ActiveIndices
        If active Is Nothing OrElse active.Length < 2 Then Return

        ' 取一个活动体素及其 +X 相邻体素作为测量参照
        Dim idxA As Integer = active(active.Length \ 2)
        Dim i, j, k As Integer
        Call m_dataset.IdxToIJK(idxA, i, j, k)

        Dim idxB As Integer = -1

        If i + 1 < m_dataset.Nx Then
            idxB = m_dataset.IJKToIdx(i + 1, j, k)
        ElseIf j + 1 < m_dataset.Ny Then
            idxB = m_dataset.IJKToIdx(i, j + 1, k)
        ElseIf k + 1 < m_dataset.Nz Then
            idxB = m_dataset.IJKToIdx(i, j, k + 1)
        End If

        If idxB < 0 Then Return

        Dim cA = m_dataset.VoxelCenter(idxA)
        Dim cB = m_dataset.VoxelCenter(idxB)

        Dim sA As PointF, sB As PointF

        If Not m_sceneCanvas.TryProjectPoint(New Point3D(cA(0), cA(1), cA(2)), sA) Then Return
        If Not m_sceneCanvas.TryProjectPoint(New Point3D(cB(0), cB(1), cB(2)), sB) Then Return

        Dim dist As Double = Math.Sqrt((sA.X - sB.X) * (sA.X - sB.X) + (sA.Y - sB.Y) * (sA.Y - sB.Y))

        If dist < 0.5 OrElse Double.IsNaN(dist) OrElse Double.IsInfinity(dist) Then Return

        Dim size As Integer = CInt(Math.Min(160.0, Math.Max(2.0, dist * m_pointFill)))

        If size <> m_sceneCanvas.PointSize Then
            m_sceneCanvas.PointSize = size
        End If
    End Sub

    ''' <summary>更新 FPS 统计（每 500ms 刷新一次读数）。</summary>
    Private Sub UpdateFps()
        If Not m_renderClock.IsRunning Then
            Call m_renderClock.Start()
            Return
        End If

        m_frameCount += 1
        Dim elapsed As Double = m_renderClock.Elapsed.TotalSeconds

        If elapsed >= 0.5 Then
            m_fps = m_frameCount / elapsed
            m_frameCount = 0
            Call m_renderClock.Restart()
        End If
    End Sub

    ''' <summary>
    ''' Render 事件：在 3D 场景之上绘制色标条、空状态提示与坐标轴提示。
    ''' </summary>
    Private Sub OnSceneRender(sender As Object, e As DxRenderEventArgs)
        Call UpdateFps()

        If Not IsReady OrElse Not m_colorbar.Visible Then
            Call DrawEmptyState(e)
        End If

        Call m_colorbar.Draw(e.Graphics, e.Size)
        Call DrawAxisHint(e)

        If m_showHoverTooltip AndAlso m_hoverVoxel >= 0 Then
            Call DrawHoverTooltip(e)
        End If

        If m_showDebugInfo Then
            Call DrawDebugInfo(e)
        End If
    End Sub

    ''' <summary>
    ''' 右下角半透明调试叠加层：FPS、鼠标位置、相机姿态、FOV、视距、
    ''' DirectX 设备与渲染管线信息。
    ''' </summary>
    Private Sub DrawDebugInfo(e As DxRenderEventArgs)
        Dim g = e.Graphics
        Dim cam = m_sceneCanvas.Controller.Camera
        Dim lines As New List(Of String)

        lines.Add($"FPS:        {m_fps:F1}")
        lines.Add($"鼠标:       ({m_mousePosX}, {m_mousePosY})")

        If cam IsNot Nothing Then
            lines.Add($"视角:       pitch={cam.AngleX:F1}° yaw={cam.AngleY:F1}° roll={cam.AngleZ:F1}°")
            lines.Add($"FOV:        {cam.FieldOfView:F0}   视距: {cam.ViewDistance:F1}")
        End If

        lines.Add($"分辨率:     {e.Size.Width}×{e.Size.Height}")
        lines.Add($"MSAA:       {m_sceneCanvas.MultisampleCount}x   点大小: {m_sceneCanvas.PointSize}px")

        Dim backend As String = "?"
        Dim device As String = "?"

        Try
            If m_sceneCanvas.Renderer IsNot Nothing Then
                backend = m_sceneCanvas.Renderer.GetType().Name
            End If
        Catch ex As Exception
        End Try

        Try
            If Not String.IsNullOrEmpty(m_sceneCanvas.DeviceDescription) Then
                device = m_sceneCanvas.DeviceDescription
            End If
        Catch ex As Exception
        End Try

        lines.Add($"渲染后端:   {backend}")
        lines.Add($"设备:       {device}")

        If m_pointVoxelMap IsNot Nothing Then
            lines.Add($"体素点数:   {m_pointVoxelMap.Length:N0}")
        End If

        Dim font As New Microsoft.VisualBasic.Imaging.Font("Consolas", 8.0F)
        Dim lineH As Single = font.Size * 1.5F
        Dim pad As Single = 8.0F

        Dim width As Single = 0.0F

        For Each line As String In lines
            Dim sz As SizeF = g.MeasureString(line, font)
            If sz.Width > width Then width = sz.Width
        Next

        Dim height As Single = lineH * lines.Count
        Dim x As Single = e.Size.Width - width - pad * 2.0F - 12.0F
        Dim y As Single = e.Size.Height - height - pad * 2.0F - 12.0F

        Using background As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(170, 15, 23, 42))
            Call g.FillRectangle(background, x, y, width + pad * 2.0F, height + pad * 1.5F)
        End Using

        Using textBrush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(220, 125, 211, 252))
            For i As Integer = 0 To lines.Count - 1
                Call g.DrawString(lines(i), font, textBrush, x + pad, y + pad * 0.75F + i * lineH)
            Next
        End Using
    End Sub

    Private Sub DrawEmptyState(e As DxRenderEventArgs)
        Dim text As String = If(IsReady, "正在加载体数据...", "请在宿主窗体中调用 LoadDataset(folder) 加载 CFD 数据")
        Dim font As New Microsoft.VisualBasic.Imaging.Font("Microsoft YaHei UI", 12.0F)

        Using brush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(148, 163, 184))
            Dim size As SizeF = e.Graphics.MeasureString(text, font)
            Call e.Graphics.DrawString(text, font, brush,
                                       (e.Size.Width - size.Width) / 2.0F,
                                       (e.Size.Height - size.Height) / 2.0F)
        End Using
    End Sub

    Private Sub DrawAxisHint(e As DxRenderEventArgs)
        Dim font As New Microsoft.VisualBasic.Imaging.Font("Microsoft YaHei UI", 8.0F)

        Using background As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(204, 255, 255, 255))
            Using textBrush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(71, 85, 105))
                Dim size As SizeF = e.Graphics.MeasureString("X · Y · Z", font)
                Dim x As Single = 16.0F
                Dim y As Single = e.Size.Height - size.Height - 20.0F

                Call e.Graphics.FillRectangle(background, x - 6.0F, y - 4.0F, size.Width + 12.0F, size.Height + 8.0F)
                Call e.Graphics.DrawString("X · Y · Z", font, textBrush, x, y)
            End Using
        End Using
    End Sub

    ' ---------------- 拾取 ----------------

    Dim m_downX As Integer, m_downY As Integer
    Dim m_dragged As Boolean = False
    Dim m_mouseDown As Boolean = False

    Private Sub OnCanvasMouseDown(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseDown
        m_downX = e.X
        m_downY = e.Y
        m_dragged = False
        m_mouseDown = True

        ' 按下期间（相机拖拽）隐藏悬停提示
        Call ClearHover()
    End Sub

    Private Sub OnCanvasMouseMove(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseMove
        m_mousePosX = e.X
        m_mousePosY = e.Y

        If m_mouseDown Then
            ' 只在按下期间累计拖拽位移，避免未按下时误判为拖拽
            If Math.Abs(e.X - m_downX) > 4 OrElse Math.Abs(e.Y - m_downY) > 4 Then
                m_dragged = True
            End If

            Call ClearHover()
        Else
            Call UpdateHover(e.X, e.Y)
        End If
    End Sub

    Private Sub OnCanvasMouseLeave(sender As Object, e As EventArgs) Handles m_sceneCanvas.MouseLeave
        Call ClearHover()
    End Sub

    ''' <summary>
    ''' 悬停提示需要展示 VTI 中的全部字段；当前帧是按需懒加载的，
    ''' 因此首次悬停时在后台把该帧的全部标量场补齐（只做一次），
    ''' 完成后在 UI 线程合并回当前帧并刷新提示框。
    ''' </summary>
    Private m_fullFieldsRequested As Boolean = False

    Private Sub EnsureFullFrameFields()
        If m_fullFieldsRequested Then Return
        If m_currentFrame Is Nothing OrElse m_dataset Is Nothing Then Return
        If m_currentFrame.Fields.Count >= m_dataset.FieldNames.Length Then Return
        If m_frameIndex < 0 Then Return

        m_fullFieldsRequested = True

        Dim dataset = m_dataset
        Dim request As Integer = m_frameIndex

        Call Task.Run(
            Sub()
                Try
                    ' wanted = Nothing → 加载该帧的全部标量场
                    Dim frame As VtiFrameData = dataset.GetFrame(request)

                    If Not IsHandleCreated OrElse IsDisposed Then Return

                    BeginInvoke(
                        Sub()
                            If IsDisposed OrElse m_currentFrame Is Nothing OrElse frame Is Nothing Then Return

                            ' UI 线程内合并，避免与渲染枚举并发
                            For Each kv In frame.Fields
                                m_currentFrame.Fields(kv.Key) = kv.Value
                            Next

                            If frame.Speed IsNot Nothing Then m_currentFrame.Speed = frame.Speed

                            If frame.U IsNot Nothing AndAlso m_currentFrame.U Is Nothing Then
                                m_currentFrame.U = frame.U
                                m_currentFrame.V = frame.V
                                m_currentFrame.W = frame.W
                            End If

                            Call m_sceneCanvas.RequestRender()
                        End Sub)
                Catch ex As Exception
                    ' 全字段加载失败不影响既有提示内容
                End Try
            End Sub)
    End Sub

    ''' <summary>清除悬停提示并重绘。</summary>
    Private Sub ClearHover()
        If m_hoverVoxel < 0 Then Return

        m_hoverVoxel = -1
        Call m_sceneCanvas.RequestRender()
    End Sub

    ''' <summary>
    ''' 体素命中判定半径：与当前点大小联动（点方格越大，判定范围越大），
    ''' 保证放大视图后悬停/拾取依然容易命中。
    ''' </summary>
    Private Function HitRadius() As Single
        Return Math.Max(8.0F, m_sceneCanvas.PointSize * 0.5F + 2.0F)
    End Function

    ''' <summary>
    ''' 鼠标移动时更新悬停的体素（HitTest 命中的点 → 体素索引）。
    ''' </summary>
    Private Sub UpdateHover(x As Integer, y As Integer)
        m_hoverX = x
        m_hoverY = y

        If Not m_showHoverTooltip OrElse m_dragged OrElse Not IsReady OrElse m_pointVoxelMap Is Nothing Then
            Call ClearHover()
            Return
        End If

        Dim voxel As Integer = -1
        ' 命中半径与点大小联动：点方格越大，命中判定范围越大
        Dim hit As SceneHitTest = m_sceneCanvas.HitTest(x, y, HitRadius())

        If hit.HasHit AndAlso hit.Kind = SceneHitKind.Point AndAlso
            hit.Index >= 0 AndAlso hit.Index < m_pointVoxelMap.Length Then
            voxel = m_pointVoxelMap(hit.Index)
        End If

        If voxel <> m_hoverVoxel Then
            m_hoverVoxel = voxel
            Call m_sceneCanvas.RequestRender()
        End If
    End Sub

    ''' <summary>
    ''' 绘制悬停体素的半透明数据提示框：
    ''' 体素坐标 + 当前帧 VTI 中全部已加载字段的数值（多列排布）。
    ''' </summary>
    Private Sub DrawHoverTooltip(e As DxRenderEventArgs)
        If m_currentFrame Is Nothing OrElse m_pointVoxelMap Is Nothing Then Return
        If m_hoverVoxel < 0 OrElse Array.IndexOf(m_pointVoxelMap, m_hoverVoxel) < 0 Then Return

        ' 该帧还没加载全部字段（懒加载）：后台补齐，完成后提示框自动变全
        Call EnsureFullFrameFields()

        Dim g = e.Graphics
        Dim viewport As Size = e.Size

        ' ---- 组装内容行（字段名, 值文本），按名称排序便于查找 ----
        Dim names As New List(Of String)(m_currentFrame.Fields.Keys)

        Call names.Sort(StringComparer.OrdinalIgnoreCase)

        ' 宿主配置了字段过滤清单时，只显示被勾选的字段
        If m_tooltipFields IsNot Nothing AndAlso m_tooltipFields.Length > 0 Then
            Dim allow As New HashSet(Of String)(m_tooltipFields, StringComparer.OrdinalIgnoreCase)

            Call names.RemoveAll(Function(n) Not allow.Contains(n))
        End If

        Dim items As New List(Of KeyValuePair(Of String, String))(names.Count)

        For Each name As String In names
            Dim arr As Single() = m_currentFrame.Fields(name)

            If m_hoverVoxel < arr.Length Then
                Call items.Add(New KeyValuePair(Of String, String)(name, FormatTooltipValue(arr(m_hoverVoxel))))
            End If
        Next

        ' ---- 排版参数 ----
        Dim titleFont As New Microsoft.VisualBasic.Imaging.Font("Microsoft YaHei UI", 9.0F)
        Dim itemFont As New Microsoft.VisualBasic.Imaging.Font("Consolas", 7.5F)
        Dim rowH As Single = itemFont.Size * 1.6F
        Dim pad As Single = 10.0F
        Dim nameValueGap As Single = 8.0F
        Dim colGap As Single = 18.0F

        Dim i, j, k As Integer
        Call m_dataset.IdxToIJK(m_hoverVoxel, i, j, k)
        Dim title As String = $"体素 ({i}, {j}, {k})   idx = {m_hoverVoxel}   帧 {m_frameIndex + 1}"
        Dim titleH As Single = g.MeasureString(title, titleFont).Height + 4.0F

        If items.Count = 0 Then
            Call items.Add(New KeyValuePair(Of String, String)("(无字段)", "0"))
        End If

        ' ---- 名字列宽与数值列宽 ----
        Dim nameW As Single = 0.0F, valueW As Single = 0.0F

        For Each it As KeyValuePair(Of String, String) In items
            Dim ns As SizeF = g.MeasureString(it.Key, itemFont)
            Dim vs As SizeF = g.MeasureString(it.Value, itemFont)

            If ns.Width > nameW Then nameW = ns.Width
            If vs.Width > valueW Then valueW = vs.Width
        Next

        ' ---- 分列：在视口高度内尽量少列 ----
        Dim availH As Single = viewport.Height - pad * 2.0F - titleH - 24.0F
        Dim rowsFit As Integer = Math.Max(1, CInt(Math.Floor(availH / rowH)))
        Dim cols As Integer = CInt(Math.Ceiling(items.Count / CDbl(rowsFit)))
        Dim colWidth As Single = nameW + nameValueGap + valueW + colGap
        Dim cardW As Single = pad * 2.0F + cols * colWidth - colGap
        Dim rows As Integer = CInt(Math.Ceiling(items.Count / CDbl(cols)))
        Dim cardH As Single = pad * 2.0F + titleH + rows * rowH

        ' ---- 位置：跟随鼠标，靠边翻转 ----
        Dim cx As Single = m_hoverX + 18.0F

        If cx + cardW > viewport.Width - 8.0F Then
            cx = m_hoverX - cardW - 18.0F
        End If

        Dim cy As Single = m_hoverY + 18.0F

        If cy + cardH > viewport.Height - 8.0F Then
            cy = m_hoverY - cardH - 18.0F
        End If

        cx = Math.Max(8.0F, cx)
        cy = Math.Max(8.0F, cy)

        ' ---- 半透明卡片背景 + 边框 ----
        Using background As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(226, 255, 255, 255))
            Call g.FillRectangle(background, cx, cy, cardW, cardH)
        End Using

        Using border As New Microsoft.VisualBasic.Imaging.Pen(Color.FromArgb(203, 213, 225), 1.0F)
            Call g.DrawRectangle(border, cx, cy, cardW, cardH)
        End Using

        ' ---- 标题（体素坐标，主色加粗效果用主色文字近似）----
        Using titleBrush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(37, 99, 235))
            Call g.DrawString(title, titleFont, titleBrush, cx + pad, cy + pad)
        End Using

        ' ---- 字段行（字段名灰、数值深色，构成富文本观感）----
        Dim top As Single = cy + pad + titleH

        For c As Integer = 0 To cols - 1
            Dim colX As Single = cx + pad + c * colWidth

            For r As Integer = 0 To rows - 1
                Dim index As Integer = c * rows + r

                If index >= items.Count Then Exit For

                Dim y As Single = top + r * rowH

                Using nameBrush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(100, 116, 139))
                    Call g.DrawString(items(index).Key, itemFont, nameBrush, colX, y)
                End Using

                Using valueBrush As New Microsoft.VisualBasic.Imaging.SolidBrush(Color.FromArgb(15, 23, 42))
                    Call g.DrawString(items(index).Value, itemFont, valueBrush, colX + nameW + nameValueGap, y)
                End Using
            Next
        Next
    End Sub

    ''' <summary>提示框中的数值格式：常规用 G6，极小/极大值用科学计数。</summary>
    Private Shared Function FormatTooltipValue(v As Single) As String
        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then
            Return v.ToString()
        End If

        Dim a As Double = Math.Abs(v)

        If a <> 0 AndAlso (a < 0.001 OrElse a >= 100000.0) Then
            Return v.ToString("E3")
        End If

        Return v.ToString("G6")
    End Function

    Private Sub OnCanvasMouseUp(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseUp
        m_mouseDown = False

        If m_dragged OrElse Not IsReady OrElse m_pointVoxelMap Is Nothing Then Return

        ' 右键点击清除拾取
        If e.Button = MouseButtons.Right Then
            If m_selectedVoxel >= 0 Then
                m_selectedVoxel = -1
                Call ScheduleRefresh()
                RaiseEvent VoxelPickCleared()
            End If
            Return
        End If

        If e.Button <> MouseButtons.Left Then Return

        Dim hit As SceneHitTest = m_sceneCanvas.HitTest(e.X, e.Y, HitRadius())

        If hit.HasHit AndAlso hit.Kind = SceneHitKind.Point AndAlso
            hit.Index >= 0 AndAlso hit.Index < m_pointVoxelMap.Length Then

            Dim voxelIdx As Integer = m_pointVoxelMap(hit.Index)

            m_selectedVoxel = voxelIdx
            Call ScheduleRefresh()

            RaiseEvent VoxelPicked(BuildPickArgs(voxelIdx))
        End If
    End Sub

    ''' <summary>构造拾取事件参数（含体素坐标与当前帧物理量）。</summary>
    Private Function BuildPickArgs(voxelIdx As Integer) As VoxelPickEventArgs
        Dim i, j, k As Integer
        Call m_dataset.IdxToIJK(voxelIdx, i, j, k)

        ' 速度场是懒加载的（或数据集里根本没有速度场）：
        ' 缺失时 u/v/w 取 0，避免 NullReferenceException
        Dim u As Double = 0.0, v As Double = 0.0, w As Double = 0.0

        If m_currentFrame.U IsNot Nothing AndAlso voxelIdx < m_currentFrame.U.Length Then
            u = m_currentFrame.U(voxelIdx)
            v = m_currentFrame.V(voxelIdx)
            w = m_currentFrame.W(voxelIdx)
        End If

        Return New VoxelPickEventArgs With {
            .VoxelIndex = voxelIdx,
            .I = i, .J = j, .K = k,
            .FieldValue = CfdDataset.GetFieldValue(m_currentFrame, m_field, voxelIdx),
            .U = u, .V = v, .W = w,
            .Speed = Math.Sqrt(u * u + v * v + w * w),
            .FrameIndex = m_frameIndex
        }
    End Function

    ''' <summary>数据集全局值域更新（可能来自后台线程）时刷新自动值域的视图。</summary>
    Private Sub OnDatasetRangeUpdated(field As String)
        If Not m_autoRange OrElse field <> m_field Then Return
        If Not IsHandleCreated OrElse IsDisposed Then Return

        Try
            BeginInvoke(
                Sub()
                    If Not IsDisposed Then
                        Call RebuildScene()
                    End If
                End Sub)
        Catch ex As ObjectDisposedException
            ' 控件正在销毁，忽略
        End Try
    End Sub

End Class
