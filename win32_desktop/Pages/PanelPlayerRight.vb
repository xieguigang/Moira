Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports CDFDxCanvas
Imports CDFDxCanvas.Data
Imports Microsoft.VisualBasic.My.JavaScript
Imports Galaxy.Workbench.DockDocument
Imports Galaxy.Workbench

Public Class PanelPlayerRight

    Friend player As PageCFDPlayer

    Dim m_series As VoxelSeries
    Dim m_slice As Bitmap
    Dim m_selectedVoxel As Integer = -1

    ' 体素属性动态对象的类型缓存（避免每次刷新都 Reflection.Emit 新类型）
    Dim m_propDynamicType As Type
    Dim m_propNameMap As Dictionary(Of String, String)
    Dim m_propSignature As String = Nothing

    ' ---------------- 画布拾取事件转发 ----------------

    Friend Sub HandleVoxelPicked(e As VoxelPickEventArgs)
        m_selectedVoxel = e.VoxelIndex
        Call UpdateVoxelInfo()
        Call UpdatePropertyGrid()
        Call LoadSeries()
    End Sub

    Friend Sub HandleVoxelPickCleared()
        m_selectedVoxel = -1
        m_series = Nothing
        lblVoxelInfo.Text = "点击体素查看详情"
        lblSeriesHint.Text = ""
        pnlSeries.Invalidate()
        CommonRuntime.GetPropertyWindow.Clear()
    End Sub

    ' ---------------- 体素信息 ----------------

    Friend Sub UpdateVoxelInfo()
        If m_selectedVoxel < 0 Then Return

        Dim args = player.Canvas.GetPickInfo(m_selectedVoxel)

        If args Is Nothing Then Return

        lblVoxelInfo.Text =
            $"坐标: ({args.I}, {args.J}, {args.K})" & Environment.NewLine &
            $"{CFDCanvas.FieldLabel(player.Canvas.Field)}: {args.FieldValue:F4}" & Environment.NewLine &
            $"速度 (u,v,w): ({args.U:F3}, {args.V:F3}, {args.W:F3})" & Environment.NewLine &
            $"|V|: {args.Speed:F4}"
    End Sub

    ''' <summary>
    ''' 用 DynamicType.Create 构建动态对象并在 PropertyGrid 中显示
    ''' 选中体素的全部字段值（数据集实际字段，不论其是否被勾选用于 tooltip）。
    ''' </summary>
    ''' <remarks>
    ''' 动态类型会被缓存：只有字段集合发生变化时
    ''' 才重新 Reflection.Emit；逐帧刷新只是用缓存类型换一组属性值，
    ''' 避免播放时每帧都生成新的动态类型。
    ''' </remarks>
    Friend Sub UpdatePropertyGrid()
        If m_selectedVoxel < 0 OrElse Not player.Canvas.IsReady Then
            Call CommonRuntime.GetPropertyWindow.Clear()
            Return
        End If

        ' 数据集实际字段（无论勾选与否）
        Dim names As New List(Of String)()

        For Each name As String In player.Canvas.Dataset.FieldNames
            Call names.Add(name)
        Next

        If names.Count = 0 Then
            Call CommonRuntime.GetPropertyWindow.Clear()
            Return
        End If

        Dim values As Dictionary(Of String, Double) = player.Canvas.GetVoxelFields(m_selectedVoxel)

        ' ---- 字段集合变化时重建动态类型 ----
        Dim signature As String = String.Join("|", names)

        If m_propDynamicType Is Nothing OrElse m_propSignature <> signature Then
            Dim meta As New Dictionary(Of String, Object)()

            For Each name As String In names
                Dim v As Double = 0.0
                Call values.TryGetValue(name, v)
                meta(name) = v
            Next

            Dim obj As Object = DynamicType.Create(meta)

            m_propDynamicType = obj.GetType()

            ' 原始字段名（DisplayName 特性）→ 动态属性符号名
            m_propNameMap = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            For Each prop As Reflection.PropertyInfo In m_propDynamicType.GetProperties()
                Dim display As String = prop.Name

                For Each attr As Object In prop.GetCustomAttributes(False)
                    Dim displayName = TryCast(attr, DisplayNameAttribute)

                    If displayName IsNot Nothing AndAlso Not String.IsNullOrEmpty(displayName.DisplayName) Then
                        display = displayName.DisplayName
                        Exit For
                    End If
                Next

                m_propNameMap(display) = prop.Name
            Next

            m_propSignature = signature
        End If

        ' ---- 用缓存的动态类型 + 当前帧数值构建新实例 ----
        Dim pairs As New List(Of KeyValuePair(Of String, Object))(names.Count)

        For Each name As String In names
            Dim v As Double = 0.0
            Call values.TryGetValue(name, v)

            Dim symbol As String = Nothing

            If Not m_propNameMap.TryGetValue(name, symbol) Then
                symbol = name
            End If

            Call pairs.Add(New KeyValuePair(Of String, Object)(symbol, v))
        Next

        Call CommonRuntime.GetPropertyWindow.SetObject(JavaScriptObject.CreateDynamicObject(m_propDynamicType, pairs))
    End Sub

    ' ---------------- 时间序列加载与绘制 ----------------

    Private Sub LoadSeries()
        If m_selectedVoxel < 0 OrElse Not player.Canvas.IsReady Then Return

        Dim voxelIdx As Integer = m_selectedVoxel
        Dim field As String = player.Canvas.Field

        lblSeriesHint.Text = "时间序列计算中..."

        Call Task.Run(
            Sub()
                Try
                    Dim series = player.Canvas.GetVoxelSeries(voxelIdx, field)
                    BeginInvoke(
                        Sub()
                            If m_selectedVoxel = voxelIdx AndAlso field = player.Canvas.Field Then
                                m_series = series
                                lblSeriesHint.Text = $"已加载 {series.Times.Length} 个时间点"
                                pnlSeries.Invalidate()
                            End If
                        End Sub)
                Catch ex As Exception
                    BeginInvoke(Sub() lblSeriesHint.Text = $"时间序列加载失败: {ex.Message}")
                End Try
            End Sub)
    End Sub

    Friend Sub UpdateSlice()
        If Not player.Canvas.IsReady Then Return

        Dim axis As CfdAxis = player.Canvas.SectionAxis
        Dim pos As Integer = player.Canvas.SectionPosition

        Dim old As Bitmap = m_slice
        m_slice = player.Canvas.RenderSliceBitmap(axis, pos, picSlice.Width - 2, picSlice.Height - 2)

        If old IsNot Nothing Then
            Call old.Dispose()
        End If

        picSlice.Image = m_slice
    End Sub

    Private Sub DrawSeries(sender As Object, e As PaintEventArgs) Handles pnlSeries.Paint
        Dim g As Graphics = e.Graphics
        Call g.Clear(Color.White)

        Dim w As Single = pnlSeries.ClientSize.Width - 2
        Dim h As Single = pnlSeries.ClientSize.Height - 2

        If m_series Is Nothing OrElse m_series.Values.Length = 0 Then
            Using font As New Font("Microsoft YaHei UI", 8.5F)
                Using brush As New SolidBrush(Color.FromArgb(148, 163, 184))
                    Dim text As String = "点击体素查看时间序列"
                    Dim size As SizeF = g.MeasureString(text, font)
                    Call g.DrawString(text, font, brush, (w - size.Width) / 2, (h - size.Height) / 2)
                End Using
            End Using
            Return
        End If

        Dim n As Integer = m_series.Values.Length
        Dim padL As Single = 52.0F, padR As Single = 10.0F
        Dim padT As Single = 10.0F, padB As Single = 22.0F
        Dim plotW As Single = w - padL - padR
        Dim plotH As Single = h - padT - padB

        ' 值域
        Dim mn As Double = Double.PositiveInfinity, mx As Double = Double.NegativeInfinity
        For Each v As Double In m_series.Values
            If v < mn Then mn = v
            If v > mx Then mx = v
        Next
        If mx <= mn Then mx = mn + 0.0001

        Dim tMin As Double = m_series.Times(0)
        Dim tMax As Double = m_series.Times(n - 1)
        If tMax <= tMin Then tMax = tMin + 0.0001

        ' Y 轴刻度线
        Using gridPen As New Pen(Color.FromArgb(238, 242, 247), 1.0F)
            For i As Integer = 0 To 3
                Dim yy As Single = padT + plotH * i / 3.0F
                Call g.DrawLine(gridPen, padL, yy, padL + plotW, yy)

                Dim val As Double = mx - (mx - mn) * i / 3.0
                Using font As New Font("Microsoft YaHei UI", 7.5F)
                    Using brush As New SolidBrush(Color.FromArgb(148, 163, 184))
                        Call g.DrawString(val.ToString("G3"), font, brush, 2.0F, yy - 6.0F)
                    End Using
                End Using
            Next
        End Using

        If n >= 2 Then
            Dim points(n - 1) As PointF

            For i As Integer = 0 To n - 1
                Dim x As Single = padL + CSng((m_series.Times(i) - tMin) / (tMax - tMin) * plotW)
                Dim y As Single = padT + CSng((mx - m_series.Values(i)) / (mx - mn) * plotH)
                points(i) = New PointF(x, y)
            Next

            ' 渐变面积（折线下方到基线）
            Dim areaPoints(n + 1) As PointF

            For i As Integer = 0 To n - 1
                areaPoints(i) = points(i)
            Next
            areaPoints(n) = New PointF(points(n - 1).X, padT + plotH)
            areaPoints(n + 1) = New PointF(points(0).X, padT + plotH)

            Using area As New GraphicsPath
                Call area.AddPolygon(areaPoints)

                Using brush As New LinearGradientBrush(
                    New RectangleF(padL, padT, plotW, plotH),
                    Color.FromArgb(70, 37, 99, 235),
                    Color.FromArgb(5, 37, 99, 235),
                    LinearGradientMode.Vertical)
                    Call g.FillPath(brush, area)
                End Using
            End Using

            Using pen As New Pen(Color.FromArgb(37, 99, 235), 2.0F)
                pen.LineJoin = LineJoin.Round
                Call g.DrawLines(pen, points)
            End Using
        End If

        ' X 轴标注（时间范围）
        Using font As New Font("Microsoft YaHei UI", 7.5F)
            Using brush As New SolidBrush(Color.FromArgb(148, 163, 184))
                Call g.DrawString($"t = {tMin:F2}", font, brush, padL, padT + plotH + 2.0F)
                Dim endText As String = $"{tMax:F2}"
                Dim size As SizeF = g.MeasureString(endText, font)
                Call g.DrawString(endText, font, brush, padL + plotW - size.Width, padT + plotH + 2.0F)
            End Using
        End Using
    End Sub

    Private Sub pnlSeries_Resize(sender As Object, e As EventArgs) Handles pnlSeries.Resize
        pnlSeries.Invalidate()
    End Sub
End Class
