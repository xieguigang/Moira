Imports System.Numerics
Imports Microsoft.VisualBasic.Drawing.DirectX
Imports Microsoft.VisualBasic.Drawing.DirectX.Scene3D
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Imaging.Drawing3D
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports Moira.CDFDxCanvas.Rendering
Imports Font = Microsoft.VisualBasic.Imaging.Font
Imports SolidBrush = Microsoft.VisualBasic.Imaging.SolidBrush

' /********************************************************************************/
'
'   WindTunnelCanvas.vb
'
'   风洞实时渲染画布 —— 参照 CDFDxCanvas/CFDCanvas 的组装模式：
'
'       UserControl + 内嵌 DxScene3DCanvas（DXApi Direct3D11 渲染管线）
'         - 障碍体素 + 流场点云：世界空间实例立方体（PointShape=Cube）
'         - 速度箭头：LineSegment 连线叠加层（ShowConnections）
'         - 色标条：复用 CDFDxCanvas Rendering.ColorbarOverlay（Render 事件 2D 叠加）
'
'   数据源不是离线 VTI（CfdDataset），而是实时 WindTunnelSolver 的矩量场
'   （TensorF 零拷贝视图）。逐帧 UpdatePointCloud / UpdateConnections 保持相机。
'
' /********************************************************************************/

''' <summary>
''' 风洞实时渲染画布（WinForms 控件）。
''' </summary>
Public Class WindTunnelCanvas : Inherits System.Windows.Forms.UserControl

#Region "显示选项"

    ''' <summary>显示的标量场。</summary>
    Public Property Field As TunnelField = TunnelField.Speed

    ''' <summary>热图调色板。</summary>
    Public Property Palette As ScalerPalette = ScalerPalette.Jet

    ''' <summary>是否显示速度箭头（LineSegment 叠加层）。</summary>
    Public Property ShowArrows As Boolean = False

    ''' <summary>箭头采样步长（每轴，格点）。</summary>
    Public Property ArrowStride As Integer = 3

    ''' <summary>是否启用截面。</summary>
    Public Property SliceEnabled As Boolean = False

    ''' <summary>True = 仅显示截面层；False = 显示截面一侧（裁剪模式）。</summary>
    Public Property SliceOnly As Boolean = False

    ''' <summary>截面轴。</summary>
    Public Property SliceAxis As SliceAxisKind = SliceAxisKind.X

    ''' <summary>截面位置（体素索引）。</summary>
    Public Property SlicePosition As Integer = 0

    ''' <summary>是否显示固体障碍体素。</summary>
    Public Property ShowSolids As Boolean = True

    ''' <summary>流场点云采样上限（自动降采样，控制每帧传输量）。</summary>
    Public Property MaxPoints As Integer = 160000

    ''' <summary>左上角信息叠加文本（网格 / 路径 / 步数等统计）。</summary>
    Public Property OverlayText As String = ""

#End Region

#Region "内部状态"

    Private WithEvents m_sceneCanvas As DxScene3DCanvas
    Private m_solver As WindTunnelSolver
    Private m_nx, m_ny, m_nz As Integer
    Private m_btype As Byte()

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

    ''' <summary>已绑定的求解器。</summary>
    Public ReadOnly Property Solver As WindTunnelSolver
        Get
            Return m_solver
        End Get
    End Property

#End Region

#Region "数据绑定"

    ''' <summary>
    ''' 绑定实时风洞求解器，构建初始场景（相机自动 FitView）。
    ''' </summary>
    Public Sub BindSolver(solver As WindTunnelSolver)
        m_solver = solver
        m_nx = solver.Nx
        m_ny = solver.Ny
        m_nz = solver.Nz
        m_btype = solver.Btype
        m_sceneReady = False

        ' ---- 流体点云降采样步长：控制每帧传输的点数 ----
        Dim total As Long = CLng(m_nx) * m_ny * m_nz
        Dim stride As Integer = 1

        While total \ (CLng(stride) * stride * stride) > MaxPoints
            stride += 1
        End While
        m_stride = stride

        ' ---- 全集质心（质心补偿基准）----
        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
        Dim n As Integer = 0
        Dim plane As Integer = m_ny * m_nz

        For i As Integer = 0 To m_nx - 1 Step m_stride
            For j As Integer = 0 To m_ny - 1 Step m_stride
                For k As Integer = 0 To m_nz - 1 Step m_stride
                    Dim bt As Byte = m_btype(i * plane + j * m_nz + k)

                    If bt = CudaLbmBackend.CellFluid OrElse bt = CudaLbmBackend.CellSolid Then
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
    ''' 从求解器读取当前矩量场并刷新 3D 场景（每帧调用）。
    ''' </summary>
    Public Sub RefreshField()
        If m_solver Is Nothing OrElse m_buf Is Nothing Then
            Return
        End If

        Call m_solver.EnsureMoments()

        Dim plane As Integer = m_ny * m_nz
        Dim dataRho As Single() = m_solver.RhoF.Data
        Dim dataUx As Single() = m_solver.UxF.Data
        Dim dataUy As Single() = m_solver.UyF.Data
        Dim dataUz As Single() = m_solver.UzF.Data

        If m_lut Is Nothing OrElse m_lut.Length = 0 Then
            m_lut = Designer.FromSchema(Palette, 256)
        End If

        ' ---- Pass 1：可见流体格点的标量值域 + 可见子集聚心（质心补偿）----
        Dim mn As Double = Double.PositiveInfinity
        Dim mx As Double = Double.NegativeInfinity
        Dim sx As Double = 0, sy As Double = 0, sz As Double = 0
        Dim nVis As Integer = 0

        For i As Integer = 0 To m_nx - 1 Step m_stride
            For j As Integer = 0 To m_ny - 1 Step m_stride
                For k As Integer = 0 To m_nz - 1 Step m_stride
                    Dim idx As Integer = i * plane + j * m_nz + k

                    If m_btype(idx) <> CudaLbmBackend.CellFluid Then
                        Continue For
                    End If
                    If Not SliceVisible(i, j, k) Then
                        Continue For
                    End If

                    Dim v As Double = ScalarAt(idx, dataRho, dataUx, dataUy, dataUz)

                    If v < mn Then mn = v
                    If v > mx Then mx = v

                    sx += i : sy += j : sz += k
                    nVis += 1
                Next
            Next
        Next

        If nVis = 0 OrElse mx - mn < 1.0E-12 Then
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
                    Dim bt As Byte = m_btype(idx)

                    If bt = CudaLbmBackend.CellSolid Then
                        If Not ShowSolids OrElse Not SliceVisible(i, j, k) Then
                            Continue For
                        End If

                        m_buf(p) = New PointCloudPoint(i + ox, j + oy, k + oz, 0.0R, solidHtml)
                        p += 1

                    ElseIf bt = CudaLbmBackend.CellFluid AndAlso SliceVisible(i, j, k) Then
                        Dim v As Double = ScalarAt(idx, dataRho, dataUx, dataUy, dataUz)
                        Dim tc As Double = (v - mn) / denom

                        If tc < 0.0R Then tc = 0.0R
                        If tc > 1.0R Then tc = 1.0R

                        Dim c As Color = m_lut(CInt(tc * (m_lut.Length - 1)))

                        m_buf(p) = New PointCloudPoint(i + ox, j + oy, k + oz, tc,
                                                       $"#{c.R:X2}{c.G:X2}{c.B:X2}")
                        p += 1
                    End If
                Next
            Next
        Next

        m_count = p

        ' ---- 速度箭头（LineSegment 连线叠加层）----
        Dim lines As New List(Of LineSegment)

        If ShowArrows Then
            Dim stride As Integer = Math.Max(1, ArrowStride)
            Dim speedMax As Double = Math.Max(0.000001R, m_rangeMax)
            Dim maxLen As Double = 1.7R

            For i As Integer = 0 To m_nx - 1 Step stride
                For j As Integer = 0 To m_ny - 1 Step stride
                    For k As Integer = 0 To m_nz - 1 Step stride
                        Dim idx As Integer = i * plane + j * m_nz + k

                        If m_btype(idx) <> CudaLbmBackend.CellFluid Then
                            Continue For
                        End If
                        If Not SliceVisible(i, j, k) Then
                            Continue For
                        End If

                        Dim uu As Double = dataUx(idx)
                        Dim vv As Double = dataUy(idx)
                        Dim ww As Double = dataUz(idx)
                        Dim spd As Double = Math.Sqrt(uu * uu + vv * vv + ww * ww)

                        If spd < 0.000000001R Then
                            Continue For
                        End If

                        Dim len As Double = (spd / speedMax) * maxLen
                        Dim tx As Double = i + (uu / spd) * len
                        Dim ty As Double = j + (vv / spd) * len
                        Dim tz As Double = k + (ww / spd) * len
                        Dim tc As Double = Math.Min(1.0R, spd / speedMax)
                        Dim ac As Color = m_lut(CInt(tc * (m_lut.Length - 1)))

                        Call lines.Add(New LineSegment(
                            New Point3D(i + ox, j + oy, k + oz),
                            New Point3D(tx + ox, ty + oy, tz + oz),
                            Color.FromArgb(235, ac)))
                    Next
                Next
            Next
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

    Private Function ScalarAt(idx As Integer,
                              rho As Single(), ux As Single(), uy As Single(), uz As Single()) As Double
        Select Case Field
            Case TunnelField.Ux : Return CDbl(ux(idx))
            Case TunnelField.Uy : Return CDbl(uy(idx))
            Case TunnelField.Uz : Return CDbl(uz(idx))
            Case TunnelField.Pressure : Return (CDbl(rho(idx)) - 1.0R) / 3.0R
            Case Else
                Dim vx As Double = ux(idx)
                Dim vy As Double = uy(idx)
                Dim vz As Double = uz(idx)
                Return Math.Sqrt(vx * vx + vy * vy + vz * vz)
        End Select
    End Function

    ''' <summary>清空场景（换求解器前调用）。</summary>
    Public Sub ClearScene()
        m_sceneReady = False
        m_solver = Nothing
        m_colorbar.Visible = False
        Call m_sceneCanvas.Scene.Clear()
    End Sub

    ''' <summary>Render 事件 2D 叠加：色标条 + 统计信息。</summary>
    Private Sub OnSceneRender(sender As Object, e As DxRenderEventArgs)
        Call m_colorbar.Draw(e.Graphics, e.Size)

        If Not String.IsNullOrEmpty(OverlayText) Then
            Using font As New Font("Consolas", 9.0F)
                Using brush As New SolidBrush(Color.FromArgb(180, 30, 41, 59))
                    Call e.Graphics.DrawString(OverlayText, font, brush, 12.0F, 12.0F)
                End Using
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
