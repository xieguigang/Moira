Imports System.ComponentModel
Imports System.Numerics
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Moira.CFDDxCanvas.Rendering
Imports Font = Microsoft.VisualBasic.Imaging.Font
Imports Math = System.Math
Imports SolidBrush = Microsoft.VisualBasic.Imaging.SolidBrush

' /********************************************************************************/
'
'   WindTunnelCanvas.vb
'
'   风洞实时渲染画布 —— 参照 CDFDxCanvas/CFDCanvas 的组装模式：
'
'       UserControl + 内嵌 DxScene3DCanvas（DXApi Direct3D11 渲染管线）
'         - 障碍体素（不活动的三维体素模型本体）+ 流场点云：世界空间实例立方体（PointShape=Cube）
'         - 可视化类型互斥：热图（流体点云）/ 矢量图 / 流线图，由 Visualization 单一状态表达
'         - 矢量图 / 流线图：LineSegment 连线叠加层（ShowConnections）；固体模型本体常驻显示
'         - 色标条：复用 CDFDxCanvas Rendering.ColorbarOverlay（Render 事件 2D 叠加）
'
'   数据源是 Eulerian FVM 解经**笛卡尔重采样**后的不可变快照
'   （Simulation\EulerianFieldSnapshot.vb）。虽然 FVM 网格是非结构的，
'   但重采样后回到结构笛卡尔格，因此点云 stride 降采样 / 矢量箭头 /
'   流线三线性插值三条管线可以零改造复用。
'   逐帧 UpdatePointCloud / UpdateConnections 保持相机。
'
' /********************************************************************************/

''' <summary>
''' 风洞实时渲染画布（WinForms 控件）。
''' </summary>
Public Class WindTunnelCanvas : Inherits System.Windows.Forms.UserControl

#Region "显示选项"

    ''' <summary>显示的标量场。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Field As TunnelField = TunnelField.Speed

    ''' <summary>热图调色板。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Palette As ScalerPalette = ScalerPalette.Jet

    '''
    ''' 当前可视化类型：热图 / 矢量图 / 流线图（严格互斥，同一时刻只有一种生效）。
    '''
    '''   热图 = 流体体素按标量场 LUT 着色的点云；矢量图 = 速度矢量线段；
    '''   流线图 = 入口面种子 RK2 轨迹 + 光点粒子动画。
    '''   不活动的固体模型本体不属于可视化类型，三种模式下都常驻渲染。
    '''
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Visualization As VisualizationMode
        Get
            Return m_visualization
        End Get
        Set(value As VisualizationMode)
            If m_visualization = value Then Return

            Dim leavingStreamlines As Boolean = (m_visualization = VisualizationMode.Streamlines)
            m_visualization = value

            If leavingStreamlines AndAlso value <> VisualizationMode.Streamlines Then
                ' 离开流线图：清掉折线缓存与粒子动画
                Call InvalidatePathlines()
            End If

            Call RefreshField()
        End Set
    End Property

    Private m_visualization As VisualizationMode = VisualizationMode.Heatmap

    ''' <summary>
    ''' 【已弃用】速度箭头叠加层已并入矢量图（Visualization = Vectors）。
    ''' 读取等价于"当前是否为矢量图"；写入 True 切换到矢量图，False 回到热图。
    ''' </summary>
    <Obsolete("速度箭头已并入矢量图，请改用 Visualization = VisualizationMode.Vectors")>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ShowArrows As Boolean
        Get
            Return m_visualization = VisualizationMode.Vectors
        End Get
        Set(value As Boolean)
            If value Then
                Visualization = VisualizationMode.Vectors
            ElseIf m_visualization = VisualizationMode.Vectors Then
                Visualization = VisualizationMode.Heatmap
            End If
        End Set
    End Property

    ''' <summary>
    ''' 【已弃用】箭头采样步长已并入矢量图的 VectorStride。
    ''' </summary>
    <Obsolete("箭头采样步长已并入矢量图，请改用 VectorStride")>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ArrowStride As Integer
        Get
            Return VectorStride
        End Get
        Set(value As Integer)
            VectorStride = value
        End Set
    End Property

    ''' <summary>
    ''' 是否显示矢量图：箭头表示流体速度方向，
    ''' 箭头长度与颜色共同表示速度大小（与热图色标一致）。
    ''' 与热图、流线图互斥（写入 True 会把可视化类型切换为矢量图）。
    ''' </summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ShowVectors As Boolean
        Get
            Return m_visualization = VisualizationMode.Vectors
        End Get
        Set(value As Boolean)
            If value Then
                Visualization = VisualizationMode.Vectors
            ElseIf m_visualization = VisualizationMode.Vectors Then
                Visualization = VisualizationMode.Heatmap
            End If
        End Set
    End Property

    ''' <summary>矢量图采样步长（每轴，格点）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property VectorStride As Integer = 3

    ''' <summary>
    ''' 是否显示流线图：由入口面均匀注入种子、RK2 积分得到流体质点
    ''' 运动轨迹（稳态流动分析用），叠加沿线运动的光点粒子动画。
    ''' 与热图、矢量图互斥（写入 True 会把可视化类型切换为流线图）。
    ''' </summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ShowPathlines As Boolean
        Get
            Return m_visualization = VisualizationMode.Streamlines
        End Get
        Set(value As Boolean)
            If value Then
                Visualization = VisualizationMode.Streamlines
            ElseIf m_visualization = VisualizationMode.Streamlines Then
                Visualization = VisualizationMode.Heatmap
            End If
        End Set
    End Property

    ''' <summary>流线种子层采样步长（j/k 方向，格点）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property PathlineSeedStride As Integer
        Get
            Return m_pathSeedStride
        End Get
        Set(value As Integer)
            value = Math.Max(1, Math.Min(16, value))

            If m_pathSeedStride = value Then Return
            m_pathSeedStride = value
            Call InvalidatePathlines()
        End Set
    End Property

    Private m_pathSeedStride As Integer = 4

    ''' <summary>
    ''' 隐藏直线形态的流线：始终呈直线的来流运动轨迹不显示，
    ''' 只显示有曲线变化状态的流线（绕流 / 涡旋 / 剪切层等）。
    ''' </summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property HideStraightStreamlines As Boolean
        Get
            Return m_hideStraight
        End Get
        Set(value As Boolean)
            If m_hideStraight = value Then Return
            m_hideStraight = value

            ' 已追踪的流线立即按新阈值生效（无需重追踪）：
            ' 折线缓存按新过滤条件重建，粒子动画器随之重建
            If m_streamSet IsNot Nothing Then
                m_streamSet.MinCurvature = If(value, StreamlineSet.DefaultStraightness, 0.0F)
                m_pathLinesCache = Nothing

                If m_animator IsNot Nothing Then
                    m_animator = New StreamlineAnimator(m_streamSet, PathlineParticlesPerLine) With {
                        .CyclesPerSecond = PathlineCyclesPerSecond
                    }
                    m_particleBuf = New Single(Math.Max(1, m_animator.ParticleCount) * 8 - 1) {}
                End If

                If Not value Then
                    Call m_sceneCanvas.RequestRender()
                End If
            End If
        End Set
    End Property

    Private m_hideStraight As Boolean = True

    ''' <summary>是否启用流线光点粒子动画（关闭时只画静态流线折线）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property PathlinesAnimate As Boolean
        Get
            Return m_pathlinesAnimate
        End Get
        Set(value As Boolean)
            If m_pathlinesAnimate = value Then Return
            m_pathlinesAnimate = value

            If value AndAlso m_animator IsNot Nothing AndAlso m_backend IsNot Nothing Then
                m_animClock.Restart()
                m_pathTimer.Start()
            ElseIf Not value Then
                m_pathTimer.Stop()
                Call ClearParticleInstances()
            End If
        End Set
    End Property

    Private m_pathlinesAnimate As Boolean = True

    ''' <summary>每条流线布置的光点粒子数（1-8）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property PathlineParticlesPerLine As Integer
        Get
            Return m_pathParticlesPerLine
        End Get
        Set(value As Integer)
            value = Math.Max(1, Math.Min(8, value))

            If m_pathParticlesPerLine = value Then Return
            m_pathParticlesPerLine = value

            ' 已有折线时立即按新粒子数重建动画器
            If m_streamSet IsNot Nothing Then
                m_animator = New StreamlineAnimator(m_streamSet, value) With {
                    .CyclesPerSecond = PathlineCyclesPerSecond
                }
                m_particleBuf = New Single(Math.Max(1, m_animator.ParticleCount) * 8 - 1) {}
            End If
        End Set
    End Property

    Private m_pathParticlesPerLine As Integer = 2

    ''' <summary>粒子沿线速度（每秒走过折线全长的比例）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property PathlineCyclesPerSecond As Single
        Get
            Return m_pathCyclesPerSecond
        End Get
        Set(value As Single)
            value = Math.Max(0.005F, Math.Min(4.0F, value))

            If Math.Abs(m_pathCyclesPerSecond - value) < 0.0001F Then Return
            m_pathCyclesPerSecond = value

            If m_animator IsNot Nothing Then
                m_animator.CyclesPerSecond = value
            End If
        End Set
    End Property

    Private m_pathCyclesPerSecond As Single = 0.12F

    ''' <summary>是否启用截面。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SliceEnabled As Boolean = False

    ''' <summary>True = 仅显示截面层；False = 显示截面一侧（裁剪模式）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SliceOnly As Boolean = False

    ''' <summary>截面轴。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SliceAxis As SliceAxisKind = SliceAxisKind.X

    ''' <summary>截面位置（体素索引）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SlicePosition As Integer = 0

    ''' <summary>是否显示固体障碍体素（不活动的三维体素模型本体）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ShowSolids As Boolean = True

    '''
    ''' 裁剪远端横截面时，是否把不活动的三维体素模型本体（固体障碍体素）也一并裁剪隐藏。
    '''
    '''   False（默认）= 只裁剪活动的物理场流体体素，模型本体保持完整，
    '''   便于看清被剖开区域与模型的相对位置；True = 模型本体也按同一截面规则裁剪。
    '''
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property ClipSolidModelWithSection As Boolean
        Get
            Return m_clipSolidModel
        End Get
        Set(value As Boolean)
            If m_clipSolidModel = value Then Return
            m_clipSolidModel = value
            Call RefreshField()
        End Set
    End Property

    Private m_clipSolidModel As Boolean = False

    ''' <summary>流场点云采样上限（自动降采样，控制每帧传输量）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property MaxPoints As Integer = 160000

    ''' <summary>
    ''' 流场显示阈值（归一化 [0,1]，相对当前帧 [RangeMin, RangeMax]）。
    ''' 标量值低于该阈值的流体点不渲染（视觉上透明），
    ''' 用于剥掉低速背景流，看见模型表面的气流结构。
    ''' 阈值不影响色标值域的稳定性（值域始终按全体可见流体计算）。
    ''' </summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Threshold As Double = 0.0

    ''' <summary>左上角信息叠加文本（网格 / 路径 / 步数等统计）。</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property OverlayText As String = ""

#End Region

#Region "内部状态"

    Private WithEvents m_sceneCanvas As DxScene3DCanvas

    ''' <summary>当前显示的 FVM 解快照（后台线程发布，UI 线程只读）。</summary>
    Private m_snapshot As EulerianFieldSnapshot

    Private m_nx, m_ny, m_nz As Integer

    ''' <summary>域外掩膜：1 = 不在流体域内（渲染为固体灰），0 = 流体。</summary>
    Private m_outside As Byte()

    Private m_lut As Color()
    Private m_colorbar As New ColorbarOverlay
    Private m_sceneReady As Boolean

    ' 点云复用缓冲（避免每帧大数组 LOH 分配）
    Private m_buf As PointCloudPoint()
    Private m_count As Integer

    ' 全集质心（UpdatePointCloud 质心补偿，见 VoxelSceneBuilder 同款技巧）
    Private m_fullCx, m_fullCy, m_fullCz As Double

    ' 当前帧值域（诊断 / 色标用）
    Private m_rangeMin, m_rangeMax As Double

    ' ---- 矢量图 / 流线图 ----

    ''' <summary>Direct3D 渲染后端（外部实例缓冲 = 动画光点粒子通道）。</summary>
    Private m_backend As Direct3D11SceneRenderer

    ''' <summary>流线折线缓存（原始世界坐标；流场重置后失效重追踪）。</summary>
    Private m_streamSet As StreamlineSet

    ''' <summary>折线的质心补偿空间线段缓存（偏移未变时复用）。</summary>
    Private m_pathLinesCache As LineSegment()
    Private m_pathOffsetX As Double = Double.NaN
    Private m_pathOffsetY As Double = Double.NaN
    Private m_pathOffsetZ As Double = Double.NaN

    ''' <summary>流线光点粒子动画。</summary>
    Private m_animator As StreamlineAnimator
    Private m_particleBuf As Single()
    Private WithEvents m_pathTimer As New Timer With {.Interval = 33, .Enabled = False}
    Private ReadOnly m_animClock As New Stopwatch

#End Region

#Region "构造与场景配置"

    Public Sub New()
        m_sceneCanvas = New DxScene3DCanvas With {
            .Dock = DockStyle.Fill,
            .RenderMode = SceneRenderMode.PointCloud,
            .UseEmbeddedColor = True,
            .PointShape = ScenePointShape.Cube,
            .MultisampleCount = 4,
            .ShowConnections = True,
            .CubeEdge = New Vector3(0.95F, 0.95F, 0.95F),
            .BackgroundColor = Color.FromArgb(214, 222, 233)
        }
        m_sceneCanvas.Lighting.Ambient = 40
        m_sceneCanvas.ShowBackgroundGrid = True
        m_sceneCanvas.BackgroundGridColor = Color.White
        m_sceneCanvas.BackgroundGridCellSize = 32

        Call Controls.Add(m_sceneCanvas)

        AddHandler m_sceneCanvas.Render, AddressOf OnSceneRender
        AddHandler m_pathTimer.Tick, AddressOf OnPathTimerTick

        ' 流线动画粒子走渲染器的外部实例缓冲，以叠加层方式画在体素点云之上
        m_backend = TryCast(m_sceneCanvas.Renderer, Direct3D11SceneRenderer)

        If m_backend IsNot Nothing Then
            m_backend.ExternalCloudOverlay = True
        End If
    End Sub

    ''' <summary>内嵌的 3D 场景画布（相机控制等高级操作入口）。</summary>
    Public ReadOnly Property SceneCanvas As DxScene3DCanvas
        Get
            Return m_sceneCanvas
        End Get
    End Property

    ''' <summary>当前帧标量值域。</summary>
    Public ReadOnly Property RangeMin As Double
        Get
            Return m_rangeMin
        End Get
    End Property

    Public ReadOnly Property RangeMax As Double
        Get
            Return m_rangeMax
        End Get
    End Property

    ''' <summary>当前绑定的解快照（未绑定时为 Nothing）。</summary>
    Public ReadOnly Property Snapshot As EulerianFieldSnapshot
        Get
            Return m_snapshot
        End Get
    End Property

#End Region

#Region "数据绑定"

    ''' <summary>
    ''' 完整绑定一份 FVM 解快照：分配点缓冲、计算全集质心、重建场景
    ''' （换网格 / 重置算例时调用；相机自动 FitView）。
    ''' </summary>
    Public Sub BindSnapshot(snapshot As EulerianFieldSnapshot)
        If snapshot Is Nothing Then
            Throw New ArgumentNullException(NameOf(snapshot))
        End If

        m_snapshot = snapshot
        m_nx = snapshot.Nx
        m_ny = snapshot.Ny
        m_nz = snapshot.Nz
        m_outside = snapshot.Outside
        m_sceneReady = False

        ' ---- 点云降采样步长：控制每帧传输的点数 ----
        Dim total As Long = CLng(m_nx) * m_ny * m_nz
        Dim stride As Integer = 1

        While total \ (CLng(stride) * stride * stride) > MaxPoints
            stride += 1
        End While
        m_stride = stride

        ' ---- 全集质心（质心补偿基准）：所有"域内"格点 ----
        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
        Dim n As Integer = 0
        Dim plane As Integer = m_ny * m_nz

        For i As Integer = 0 To m_nx - 1 Step m_stride
            For j As Integer = 0 To m_ny - 1 Step m_stride
                For k As Integer = 0 To m_nz - 1 Step m_stride
                    If m_outside(i * plane + j * m_nz + k) = 0 Then
                        sx += i : sy += j : sz += k
                        n += 1
                    End If
                Next
            Next
        Next

        If n > 0 Then
            m_fullCx = sx / n : m_fullCy = sy / n : m_fullCz = sz / n
        End If

        ' ---- 点缓冲复用（容量上限）----
        Dim cap As Integer = CInt(total \ (CLng(m_stride) * m_stride * m_stride)) + m_nx * m_ny \ 4 + 16
        m_buf = New PointCloudPoint(cap - 1) {}

        ' ---- 矢量图 / 流线图状态重置（新流场，缓存全部失效）----
        Call InvalidatePathlines()

        Call RefreshField()
    End Sub

    ''' <summary>
    ''' 每帧更新：只换数据引用后刷新（结构格尺寸与缓冲保持不变）。
    ''' </summary>
    Public Sub UpdateSnapshot(snapshot As EulerianFieldSnapshot)
        If snapshot Is Nothing OrElse m_buf Is Nothing Then
            Return
        End If

        m_snapshot = snapshot
        m_nx = snapshot.Nx
        m_ny = snapshot.Ny
        m_nz = snapshot.Nz
        m_outside = snapshot.Outside

        Call RefreshField()
    End Sub

    Private m_stride As Integer = 1

#End Region

#Region "标量场与截面"

    ''' <summary>标量场类型。</summary>
    Public Enum TunnelField
        ''' <summary>速度幅值 |u|。</summary>
        Speed
        ''' <summary>x 方向速度分量（来流方向）。</summary>
        Ux
        ''' <summary>y 方向速度分量（竖直）。</summary>
        Uy
        ''' <summary>z 方向速度分量（展向）。</summary>
        Uz
        ''' <summary>表压 p = cs²·(ρ − 1)。</summary>
        Pressure
    End Enum

    ''' <summary>截面轴。</summary>
    Public Enum SliceAxisKind
        X
        Y
        Z
    End Enum

    Private Function SliceVisible(i As Integer, j As Integer, k As Integer) As Boolean
        If Not SliceEnabled Then
            Return True
        End If

        Dim c As Integer

        Select Case SliceAxis
            Case SliceAxisKind.Y : c = j
            Case SliceAxisKind.Z : c = k
            Case Else : c = i
        End Select

        If SliceOnly Then
            Return c = SlicePosition
        Else
            Return c <= SlicePosition
        End If
    End Function

    Private Function FieldLabel() As String
        Select Case Field
            Case TunnelField.Ux : Return "Ux (m/s 格子)"
            Case TunnelField.Uy : Return "Uy (m/s 格子)"
            Case TunnelField.Uz : Return "Uz (m/s 格子)"
            Case TunnelField.Pressure : Return "Pressure (cs²·Δρ)"
            Case Else : Return "Speed |u| (格子)"
        End Select
    End Function

#End Region

#Region "逐帧场景刷新"

    ''' <summary>
    ''' 从当前快照读取重采样后的场并刷新 3D 场景（每帧调用）。
    ''' </summary>
    Public Sub RefreshField()
        If m_snapshot Is Nothing OrElse m_buf Is Nothing Then
            Return
        End If

        Dim plane As Integer = m_ny * m_nz
        Dim dataP As Single() = m_snapshot.P
        Dim dataUx As Single() = m_snapshot.Ux
        Dim dataUy As Single() = m_snapshot.Uy
        Dim dataUz As Single() = m_snapshot.Uz

        If m_lut Is Nothing OrElse m_lut.Length = 0 Then
            m_lut = Designer.FromSchema(Palette, 256)
        End If

        ' 热图模式才生成流体体素点云（三种可视化类型严格互斥）
        Dim heatmap As Boolean = (m_visualization = VisualizationMode.Heatmap)

        ' 固体模型本体是否跟随远端横截面一起裁剪（默认 False = 保持完整）
        Dim clipSolid As Boolean = SliceEnabled AndAlso ClipSolidModelWithSection

        ' ---- Pass 1：可见流体格点的标量值域 + 渲染子集聚心（质心补偿）----
        Dim mn As Double = Double.PositiveInfinity
        Dim mx As Double = Double.NegativeInfinity
        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
        Dim nVis As Integer = 0

        For i As Integer = 0 To m_nx - 1 Step m_stride
            For j As Integer = 0 To m_ny - 1 Step m_stride
                For k As Integer = 0 To m_nz - 1 Step m_stride
                    Dim idx As Integer = i * plane + j * m_nz + k
                    Dim bt As Byte = m_outside(idx)

                    If bt = 1 Then
                        ' 域外（= 被障碍占据的区域）：三种模式下都常驻，默认不受截面裁剪。
                        ' Eulerian 网格只含流体域，因此"最近单元不存在"即等价于 LBM 时代的固体格点。
                        If Not ShowSolids Then
                            Continue For
                        End If
                        If clipSolid AndAlso Not SliceVisible(i, j, k) Then
                            Continue For
                        End If

                        sx += i : sy += j : sz += k
                        nVis += 1

                    Else
                        If Not SliceVisible(i, j, k) Then
                            Continue For
                        End If

                        Dim v As Double = ScalarAt(idx, dataP, dataUx, dataUy, dataUz)

                        If v < mn Then mn = v
                        If v > mx Then mx = v

                        ' 热图模式才把流体点画进点云，因此质心只在热图模式下计入流体
                        If heatmap Then
                            sx += i : sy += j : sz += k
                            nVis += 1
                        End If
                    End If
                Next
            Next
        Next

        If mn = Double.PositiveInfinity OrElse mx - mn < 1.0E-12 Then
            mn = 0 : mx = 1
        End If

        m_rangeMin = mn
        m_rangeMax = mx

        Dim ox As Double = m_fullCx - If(nVis > 0, sx / nVis, 0.0R)
        Dim oy As Double = m_fullCy - If(nVis > 0, sy / nVis, 0.0R)
        Dim oz As Double = m_fullCz - If(nVis > 0, sz / nVis, 0.0R)

        ' ---- Pass 2：填充点云缓冲（固体灰 + 流体 LUT 着色）----
        Dim p As Integer = 0
        Dim denom As Double = mx - mn
        Dim solidHtml As String = "#94A3B8"

        For i As Integer = 0 To m_nx - 1 Step m_stride
            For j As Integer = 0 To m_ny - 1 Step m_stride
                For k As Integer = 0 To m_nz - 1 Step m_stride
                    Dim idx As Integer = i * plane + j * m_nz + k
                    Dim bt As Byte = m_outside(idx)

                    If bt = 1 Then
                        If Not ShowSolids Then
                            Continue For
                        End If
                        If clipSolid AndAlso Not SliceVisible(i, j, k) Then
                            Continue For
                        End If

                        m_buf(p) = New PointCloudPoint(i + ox, j + oy, k + oz, 0.0R, solidHtml)
                        p += 1

                    ElseIf heatmap AndAlso SliceVisible(i, j, k) Then

                        Dim v As Double = ScalarAt(idx, dataP, dataUx, dataUy, dataUz)
                        Dim tc As Double = (v - mn) / denom

                        If tc.IsNaNImaginary Then
                            tc = 0
                        End If

                        If tc < 0.0R Then tc = 0.0R
                        If tc > 1.0R Then tc = 1.0R

                        ' 低于显示阈值的流体点不渲染（透明），露出模型表面气流
                        If tc < Threshold Then
                            Continue For
                        End If

                        Dim c As Color = m_lut(CInt(tc * (m_lut.Length - 1)))

                        m_buf(p) = New PointCloudPoint(i + ox, j + oy, k + oz, tc,
                                                       $"#{c.R:X2}{c.G:X2}{c.B:X2}")
                        p += 1
                    End If
                Next
            Next
        Next

        m_count = p

        ' ---- 线段图层：矢量图 / 流线图（互斥，旧的简单箭头叠加层已并入矢量图）----
        Dim lines As New List(Of LineSegment)

        If m_visualization = VisualizationMode.Vectors Then
            Dim vStride As Integer = Math.Max(1, VectorStride)

            ' 采样子集上的速度上限（直接读宿主数组，避免每帧全量扫描）
            Dim maxS2 As Double = 0.0

            For i As Integer = 0 To m_nx - 1 Step vStride
                For j As Integer = 0 To m_ny - 1 Step vStride
                    For k As Integer = 0 To m_nz - 1 Step vStride
                        Dim idx As Integer = i * plane + j * m_nz + k

                        If m_outside(idx) <> 0 Then
                            Continue For
                        End If

                        Dim uu As Double = dataUx(idx)
                        Dim vv As Double = dataUy(idx)
                        Dim ww As Double = dataUz(idx)
                        Dim s2 As Double = uu * uu + vv * vv + ww * ww

                        If s2 > maxS2 Then maxS2 = s2
                    Next
                Next
            Next

            Dim speedMax As Double = Math.Sqrt(Math.Max(maxS2, 0.000000000001))

            ' 矢量与箭头使用同一显示规则：切片过滤 + 低于显示阈值剔除
            Dim include As Func(Of Integer, Integer, Integer, Boolean) =
                Function(ii As Integer, jj As Integer, kk As Integer) As Boolean
                    If Not SliceVisible(ii, jj, kk) Then Return False

                    Dim vidx As Integer = ii * plane + jj * m_nz + kk
                    Dim vu As Double = dataUx(vidx)
                    Dim vv As Double = dataUy(vidx)
                    Dim vw As Double = dataUz(vidx)
                    Dim vtc As Double = Math.Sqrt(vu * vu + vv * vv + vw * vw) / speedMax

                    Return vtc >= Threshold
                End Function

            ' 矢量线段直接生成在质心补偿空间（与点云/箭头同一坐标空间）
            Dim vfield As New WindTunnelFlowField(
                m_nx, m_ny, m_nz, dataUx, dataUy, dataUz, m_outside,
                CSng(ox), CSng(oy), CSng(oz))
            Dim voptions As New VectorArrowOptions With {
                .Stride = vStride,
                .SpeedMax = speedMax,
                .Lut = m_lut,
                .Alpha = 235,
                .IncludePoint = include
            }

            Call lines.AddRange(FlowVectors.BuildArrows(vfield, voptions))
        End If

        ' ---- 流线图（入口面种子 + RK2 积分；折线缓存，仅在需要时重追踪）----
        If m_visualization = VisualizationMode.Streamlines Then
            If m_streamSet Is Nothing Then
                Dim sfield As New WindTunnelFlowField(m_nx, m_ny, m_nz, dataUx, dataUy, dataUz, m_outside)

                Try
                    m_streamSet = StreamlineTracer.Trace(sfield, New StreamlineOptions With {
                        .SeedStride = Math.Max(1, PathlineSeedStride)
                    })

                    ' 直线形态的来流轨迹不显示，只保留有曲线变化的流线
                    If m_streamSet IsNot Nothing Then
                        m_streamSet.MinCurvature = If(HideStraightStreamlines,
                            StreamlineSet.DefaultStraightness, 0.0F)
                    End If
                Catch ex As Exception
                    ' 追踪失败（如预算异常）：静默降级为不显示，避免打断渲染循环
                    m_streamSet = Nothing
                End Try

                m_animator = Nothing
                m_pathLinesCache = Nothing
            End If

            If m_streamSet IsNot Nothing Then
                ' 折线平移到质心补偿空间（偏移未变时复用上次结果，避免每帧重建）
                If m_pathLinesCache Is Nothing OrElse
                    Math.Abs(m_pathOffsetX - ox) > 0.000001 OrElse
                    Math.Abs(m_pathOffsetY - oy) > 0.000001 OrElse
                    Math.Abs(m_pathOffsetZ - oz) > 0.000001 Then

                    m_pathLinesCache = m_streamSet.BuildLines(m_lut, 210, ox, oy, oz)
                    m_pathOffsetX = ox
                    m_pathOffsetY = oy
                    m_pathOffsetZ = oz
                End If

                Call lines.AddRange(m_pathLinesCache)

                If m_animator Is Nothing Then
                    m_animator = New StreamlineAnimator(m_streamSet, PathlineParticlesPerLine) With {
                        .CyclesPerSecond = PathlineCyclesPerSecond
                    }
                    m_particleBuf = New Single(Math.Max(1, m_animator.ParticleCount) * 8 - 1) {}

                    If PathlinesAnimate AndAlso m_backend IsNot Nothing Then
                        m_animClock.Restart()
                        m_pathTimer.Start()
                    End If
                End If
            End If
        Else
            m_streamSet = Nothing
            m_animator = Nothing
            m_pathTimer.Stop()
        End If

        ' ---- 场景提交（首帧 FitView，后续帧相机保持）----
        If Not m_sceneReady Then
            Call m_sceneCanvas.LoadPointCloud(New PointSlice(m_buf, m_count))
            Call m_sceneCanvas.LoadConnections(lines)
            m_sceneReady = True
        Else
            Call m_sceneCanvas.UpdatePointCloud(New PointSlice(m_buf, m_count))
            Call m_sceneCanvas.UpdateConnections(lines)
        End If

        ' ---- 色标条 ----
        m_colorbar.Lut = m_lut
        m_colorbar.Title = $"{FieldLabel()}  [{mn:F4}, {mx:F4}]"
        m_colorbar.Visible = True

        Call m_sceneCanvas.RequestRender()
    End Sub

    ''' <summary>
    ''' 取第 idx 个格点的显示标量。
    ''' 压力语义已从 LBM 的格子压力 (ρ−1)/3 改为 FVM 的压力场 p（Pa 量级，
    ''' SIMPLE 稳态解可差一个任意常数），色标按当前帧 min/max 自适应。
    ''' </summary>
    Private Function ScalarAt(idx As Integer,
                              p As Single(), ux As Single(), uy As Single(), uz As Single()) As Double
        Select Case Field
            Case TunnelField.Ux : Return CDbl(ux(idx))
            Case TunnelField.Uy : Return CDbl(uy(idx))
            Case TunnelField.Uz : Return CDbl(uz(idx))
            Case TunnelField.Pressure : Return CDbl(p(idx))
            Case Else
                Dim vx As Double = ux(idx)
                Dim vy As Double = uy(idx)
                Dim vz As Double = uz(idx)
                Return Math.Sqrt(vx * vx + vy * vy + vz * vz)
        End Select
    End Function

    ''' <summary>清空场景（换网格 / 重新加载前调用）。</summary>
    Public Sub ClearScene()
        m_sceneReady = False
        m_snapshot = Nothing
        m_outside = Nothing
        m_buf = Nothing
        m_colorbar.Visible = False
        Call m_sceneCanvas.Scene.Clear()
    End Sub

    ''' <summary>
    ''' 流线缓存失效（流场重置 / 更换求解器后调用），
    ''' 下次 <see cref="RefreshField"/> 重新追踪流线。
    ''' </summary>
    Public Sub InvalidatePathlines()
        m_streamSet = Nothing
        m_animator = Nothing
        m_pathLinesCache = Nothing
        m_pathOffsetX = Double.NaN
        m_pathOffsetY = Double.NaN
        m_pathOffsetZ = Double.NaN
        m_pathTimer.Stop()
        Call ClearParticleInstances()
    End Sub

    Private Sub ClearParticleInstances()
        If m_backend IsNot Nothing Then
            Call m_backend.ClearInstances()
        End If
    End Sub

    ''' <summary>流线光点粒子动画节拍：推进相位 → 打包 → 上传外部实例缓冲。</summary>
    Private Sub OnPathTimerTick(sender As Object, e As EventArgs)
        If m_visualization <> VisualizationMode.Streamlines OrElse Not PathlinesAnimate OrElse
            m_animator Is Nothing OrElse m_particleBuf Is Nothing OrElse m_backend Is Nothing Then

            Call m_pathTimer.Stop()
            Return
        End If

        Dim dt As Double = m_animClock.Elapsed.TotalSeconds

        m_animClock.Restart()
        Call m_animator.Advance(dt)

        Dim count As Integer = m_animator.Pack(m_particleBuf, m_pathOffsetX, m_pathOffsetY, m_pathOffsetZ)

        If count > 0 Then
            Call m_backend.EnsureInstanceCapacity(m_animator.ParticleCount)
            Call m_backend.UploadInstances(m_particleBuf, count)
        End If

        Call m_sceneCanvas.RequestRender()
    End Sub

    Private ReadOnly m_overlayFont As New Font("Consolas", 9.0F)

    ''' <summary>Render 事件 2D 叠加：色标条 + 统计信息。</summary>
    Private Sub OnSceneRender(sender As Object, e As DxRenderEventArgs)
        Call m_colorbar.Draw(e.Graphics, e.Size)

        If Not String.IsNullOrEmpty(OverlayText) Then
            Using brush As New SolidBrush(Color.FromArgb(180, 30, 41, 59))
                Call e.Graphics.DrawString(OverlayText, m_overlayFont, brush, 12.0F, 12.0F)
            End Using
        End If
    End Sub

    ''' <summary>复用缓冲的点云切片枚举（避免每帧大数组分配）。</summary>
    Private NotInheritable Class PointSlice : Implements IEnumerable(Of PointCloudPoint)

        Private ReadOnly m_buf As PointCloudPoint()
        Private ReadOnly m_n As Integer

        Public Sub New(buf As PointCloudPoint(), n As Integer)
            m_buf = buf
            m_n = n
        End Sub

        Public Function GetEnumerator() As IEnumerator(Of PointCloudPoint) _
            Implements IEnumerable(Of PointCloudPoint).GetEnumerator

            Return New SliceEnumerator(m_buf, m_n)
        End Function

        Private Function GetEnumerator1() As IEnumerator Implements IEnumerable.GetEnumerator
            Return GetEnumerator()
        End Function

        Private NotInheritable Class SliceEnumerator : Implements IEnumerator(Of PointCloudPoint)

            Private ReadOnly m_buf As PointCloudPoint()
            Private m_i As Integer = -1
            Private m_n As Integer

            Public Sub New(buf As PointCloudPoint(), n As Integer)
                m_buf = buf
                m_n = n
            End Sub

            Public ReadOnly Property Current As PointCloudPoint Implements IEnumerator(Of PointCloudPoint).Current
                Get
                    Return m_buf(m_i)
                End Get
            End Property

            Private ReadOnly Property Current1 As Object Implements IEnumerator.Current
                Get
                    Return Current
                End Get
            End Property

            Public Function MoveNext() As Boolean Implements IEnumerator.MoveNext
                m_i += 1
                Return m_i < m_n
            End Function

            Public Sub Reset() Implements IEnumerator.Reset
                m_i = -1
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
            End Sub
        End Class
    End Class

#End Region

End Class
