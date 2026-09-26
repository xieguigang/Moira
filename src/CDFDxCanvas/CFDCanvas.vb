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

    Dim m_field As CfdField = CfdField.Pressure
    Dim m_palette As ScalerPalette = ScalerPalette.Jet
    Dim m_lut As Color()
    Dim m_autoRange As Boolean = True
    Dim m_rangeMin As Double = 0.0
    Dim m_rangeMax As Double = 1.0
    Dim m_threshold As Double = 0.0

    Dim m_showArrows As Boolean = False
    Dim m_arrowDensity As Integer = 2

    Dim m_sectionEnabled As Boolean = False
    Dim m_sectionAxis As CfdAxis = CfdAxis.X
    Dim m_sectionPosition As Integer = 0

    Dim m_selectedVoxel As Integer = -1

    ' 拾取映射：当前点云下标 → 体素引擎索引
    Dim m_pointVoxelMap As Integer()

    ' 异步帧加载状态
    Dim m_loading As Boolean = False
    Dim m_pendingIndex As Integer = -1
    Dim m_appliedIndex As Integer = -1

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

    ''' <summary>当前显示的标量场。</summary>
    <Description("显示的标量场")>
    Public Property Field As CfdField
        Get
            Return m_field
        End Get
        Set(value As CfdField)
            If m_field = value Then Return
            m_field = value
            Call ScheduleRefresh()
        End Set
    End Property

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

    ''' <summary>点大小（像素）。体渲染的视觉颗粒度。</summary>
    <DefaultValue(6)>
    Public Property PointSize As Integer
        Get
            Return m_sceneCanvas.PointSize
        End Get
        Set(value As Integer)
            m_sceneCanvas.PointSize = value
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

        ' 全量值域统计（遍历所有帧，帧数据走数据集内部缓存）
        Call dataset.ComputeGlobalRange()

        AddHandler dataset.RangeUpdated, AddressOf OnDatasetRangeUpdated

        m_dataset = dataset
        m_currentFrame = Nothing
        m_frameIndex = -1
        m_sceneLoaded = False
        m_selectedVoxel = -1
        m_pointVoxelMap = Nothing
        m_appliedIndex = -1
        m_loading = False
        m_pendingIndex = -1

        ' 预生成 LUT
        m_lut = Designer.FromSchema(m_palette, 256)

        RaiseEvent DatasetLoaded(dataset)

        Call ShowFrame(0)
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

        Call Task.Run(Function() dataset.GetFrame(request)).ContinueWith(
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

        Dim range = CurrentRange()
        Dim options As New VoxelViewOptions With {
            .Field = m_field,
            .RangeMin = range.Item1,
            .RangeMax = range.Item2,
            .Threshold = m_threshold,
            .SectionEnabled = m_sectionEnabled,
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

    ''' <summary>标量场显示名。</summary>
    Public Shared Function FieldLabel(field As CfdField) As String
        Select Case field
            Case CfdField.Pressure : Return "压力 Pressure"
            Case CfdField.Density : Return "密度 Density"
            Case CfdField.Speed : Return "速度幅值 |V|"
            Case Else : Return field.ToString
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
    Public Function GetVoxelSeries(idx As Integer, field As CfdField) As VoxelSeries
        If m_dataset Is Nothing Then
            Return New VoxelSeries With {.Times = New Double() {}, .Values = New Double() {}}
        End If

        Return m_dataset.GetVoxelSeries(idx, field)
    End Function

    ' ---------------- 视口交互与叠加绘制 ----------------

    ''' <summary>
    ''' Render 事件：在 3D 场景之上绘制色标条、空状态提示与坐标轴提示。
    ''' </summary>
    Private Sub OnSceneRender(sender As Object, e As DxRenderEventArgs)
        If Not IsReady OrElse Not m_colorbar.Visible Then
            Call DrawEmptyState(e)
        End If

        Call m_colorbar.Draw(e.Graphics, e.Size)
        Call DrawAxisHint(e)
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

    Private Sub OnCanvasMouseDown(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseDown
        m_downX = e.X
        m_downY = e.Y
        m_dragged = False
    End Sub

    Private Sub OnCanvasMouseMove(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseMove
        If Math.Abs(e.X - m_downX) > 4 OrElse Math.Abs(e.Y - m_downY) > 4 Then
            m_dragged = True
        End If
    End Sub

    Private Sub OnCanvasMouseUp(sender As Object, e As MouseEventArgs) Handles m_sceneCanvas.MouseUp
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

        Dim hit As SceneHitTest = m_sceneCanvas.HitTest(e.X, e.Y)

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

        Dim u As Double = m_currentFrame.U(voxelIdx)
        Dim v As Double = m_currentFrame.V(voxelIdx)
        Dim w As Double = m_currentFrame.W(voxelIdx)

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
    Private Sub OnDatasetRangeUpdated(field As CfdField)
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
