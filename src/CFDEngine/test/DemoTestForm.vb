' /********************************************************************************/
'
'   DemoTestForm.vb
'
'   CFDCanvas 控件的临时测试窗体（复刻 cfd-player.html 的界面布局）
'
'   作用：
'       左侧控制面板（标量场 / 调色板 / 值域 / 阈值 / 箭头 / 截面）
'       + 中间 CFDCanvas 3D 视口 + 右侧信息面板（体素信息 / 时间序列 /
'       2D 切片热图）+ 底部时间轴（播放 / fps / 帧滑条）。
'       默认加载 G:\fermenter\src\demo\cfd 的 demo 数据。
'       本窗体仅用于开发期验证，可随时删除。
'
'   ★ 标量场下拉由数据集里实际导出的字段动态生成
'     （字段可配置的 VTI 导出器可以输出压力以外的温度 / pH / 细胞数 /
'      代谢物浓度 / 逐基因表达量等任意标量场）
'
'   ★ 全部 UI 布局（控件创建 / 属性 / 停靠）已迁移至
'     DemoTestForm.Designer.vb 的 InitializeComponent；控件变量以
'     WithEvents 声明在模块级，事件统一通过 Handles 关键字绑定。
'
' /********************************************************************************/

Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports CDFDxCanvas
Imports CDFDxCanvas.Data
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.My.JavaScript

Public Class DemoTestForm

    Const DefaultDemoFolder As String = "G:\fermenter\src\demo\cfd"

    ' ---------------- 视口与数据 ----------------

    Dim m_playing As Boolean = False
    Dim m_series As VoxelSeries
    Dim m_slice As Bitmap
    Dim m_selectedVoxel As Integer = -1

    ' 体素属性动态对象的类型缓存（避免每次刷新都 Reflection.Emit 新类型）
    Dim m_propDynamicType As Type
    Dim m_propNameMap As Dictionary(Of String, String)
    Dim m_propSignature As String = Nothing

    ' ---------------- 控件事件（Handles 绑定） ----------------

    Private Sub OnFormShown(sender As Object, e As EventArgs) Handles MyBase.Shown
        If IO.Directory.Exists(DefaultDemoFolder) Then
            Call LoadFolder(DefaultDemoFolder)
        End If
    End Sub

    Private Sub OnLoadClick(sender As Object, e As EventArgs) Handles btnLoad.Click
        Using dialog As New FolderBrowserDialog With {.ShowNewFolderButton = False}
            If IO.Directory.Exists(DefaultDemoFolder) Then
                dialog.SelectedPath = DefaultDemoFolder
            End If

            If dialog.ShowDialog(Me) = DialogResult.OK Then
                Call LoadFolder(dialog.SelectedPath)
            End If
        End Using
    End Sub

    Private Sub cboField_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboField.SelectedIndexChanged
        If cboField.SelectedItem Is Nothing Then Return
        m_canvas.Field = CStr(cboField.SelectedItem)
    End Sub

    Private Sub cboPalette_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboPalette.SelectedIndexChanged
        m_canvas.Palette = CType([Enum].Parse(GetType(ScalerPalette), CStr(cboPalette.SelectedItem)), ScalerPalette)
    End Sub

    Private Sub cboRangeMode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboRangeMode.SelectedIndexChanged
        m_canvas.AutoRange = cboRangeMode.SelectedIndex = 0
        If txtRangeMin IsNot Nothing Then txtRangeMin.Visible = cboRangeMode.SelectedIndex = 1
        If txtRangeMax IsNot Nothing Then txtRangeMax.Visible = cboRangeMode.SelectedIndex = 1
    End Sub

    Private Sub txtRangeMin_TextChanged(sender As Object, e As EventArgs) Handles txtRangeMin.TextChanged
        Dim v As Double
        If Double.TryParse(txtRangeMin.Text, v) Then m_canvas.RangeMin = v
    End Sub

    Private Sub txtRangeMax_TextChanged(sender As Object, e As EventArgs) Handles txtRangeMax.TextChanged
        Dim v As Double
        If Double.TryParse(txtRangeMax.Text, v) Then m_canvas.RangeMax = v
    End Sub

    Private Sub trackThreshold_ValueChanged(sender As Object, e As EventArgs) Handles trackThreshold.ValueChanged
        Dim t As Double = trackThreshold.Value / 100.0
        lblThresholdVal.Text = t.ToString("F2")
        m_canvas.Threshold = t
    End Sub

    Private Sub chkArrows_CheckedChanged(sender As Object, e As EventArgs) Handles chkArrows.CheckedChanged
        m_canvas.ShowArrows = chkArrows.Checked
    End Sub

    Private Sub cboArrowDensity_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboArrowDensity.SelectedIndexChanged
        m_canvas.ArrowDensity = cboArrowDensity.SelectedIndex + 2
    End Sub

    ''' <summary>把截面模式下拉映射到控件属性。</summary>
    Private Sub ApplySectionMode(sender As Object, e As EventArgs) Handles cboSectionMode.SelectedIndexChanged
        Select Case cboSectionMode.SelectedIndex
            Case 1
                m_canvas.SectionEnabled = True
                m_canvas.SliceOnly = False
            Case 2
                ' 切片模式：先切到启用态再打开切片开关，保证状态按序生效
                m_canvas.SectionEnabled = True
                m_canvas.SliceOnly = True
            Case Else
                m_canvas.SliceOnly = False
                m_canvas.SectionEnabled = False
        End Select
    End Sub

    Private Sub cboSectionAxis_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboSectionAxis.SelectedIndexChanged
        m_canvas.SectionAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        UpdateSectionBounds()
    End Sub

    Private Sub trackSectionPos_ValueChanged(sender As Object, e As EventArgs) Handles trackSectionPos.ValueChanged
        lblSectionPosVal.Text = trackSectionPos.Value.ToString()
        m_canvas.SectionPosition = trackSectionPos.Value
    End Sub

    Private Sub chkTooltip_CheckedChanged(sender As Object, e As EventArgs) Handles chkTooltip.CheckedChanged
        m_canvas.ShowHoverTooltip = chkTooltip.Checked
    End Sub

    Private Sub btnAll_Click(sender As Object, e As EventArgs) Handles btnAll.Click
        Call SetAllTooltipFields(True)
    End Sub

    Private Sub btnNone_Click(sender As Object, e As EventArgs) Handles btnNone.Click
        Call SetAllTooltipFields(False)
    End Sub

    Private Sub chkDebug_CheckedChanged(sender As Object, e As EventArgs) Handles chkDebug.CheckedChanged
        m_canvas.ShowDebugInfo = chkDebug.Checked
    End Sub

    Private Sub cboSpeed_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboSpeed.SelectedIndexChanged
        Dim fps As Integer() = {2, 5, 10, 20, 30}
        m_playTimer.Interval = CInt(1000 / fps(cboSpeed.SelectedIndex))
    End Sub

    Private Sub trackFrame_ValueChanged(sender As Object, e As EventArgs) Handles trackFrame.ValueChanged
        If trackFrame.Focused Then m_canvas.ShowFrame(trackFrame.Value)
    End Sub

    Private Sub bottomPanel_Resize(sender As Object, e As EventArgs) Handles bottomPanel.Resize
        If trackFrame IsNot Nothing Then
            trackFrame.SetBounds(210, 20, bottomPanel.Width - 400, 28)
            lblFrame.Location = New Point(bottomPanel.Width - lblFrame.PreferredWidth - 16, 24)
        End If
    End Sub

    ' ---------------- 数据加载 ----------------

    Private Sub LoadFolder(folder As String)
        Text = $"CFD 可视化 · 正在加载 {folder} ..."
        Cursor = Cursors.WaitCursor

        Call Task.Run(
            Sub()
                Try
                    Call m_canvas.LoadDataset(folder)
                    BeginInvoke(Sub() OnLoadDone(Nothing))
                Catch ex As Exception
                    BeginInvoke(Sub() OnLoadDone(ex))
                End Try
            End Sub)
    End Sub

    Private Sub OnLoadDone(ex As Exception)
        Cursor = Cursors.Default

        If ex IsNot Nothing Then
            Text = "CFD 可视化 · CFDCanvas 控件测试"
            Call MessageBox.Show(Me, $"数据加载失败: {ex.Message}", "加载失败",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim dataset = m_canvas.Dataset

        Text = $"CFD 可视化 · 已加载 {dataset.FrameCount} 帧 " &
               $"({dataset.Nx}×{dataset.Ny}×{dataset.Nz}，" &
               $"{dataset.ActiveVoxels:N0} 活动体素 · {dataset.FieldNames.Length} 个标量场)"

        ' 标量场下拉：取自 VTI 中实际导出的字段
        cboField.Items.Clear()

        For Each name As String In dataset.FieldNames
            Call cboField.Items.Add(name)
        Next

        If dataset.HasField(m_canvas.Field) Then
            cboField.SelectedItem = m_canvas.Field
        ElseIf cboField.Items.Count > 0 Then
            cboField.SelectedIndex = 0
        End If

        btnPlay.Enabled = True
        trackFrame.Enabled = True
        Call UpdateSectionBounds()

        ' tooltip 字段清单：数据集实际字段，默认全选（= 显示全部）
        RemoveHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck

        clbTooltipFields.BeginUpdate()
        clbTooltipFields.Items.Clear()

        For Each name As String In dataset.FieldNames
            Call clbTooltipFields.Items.Add(name, isChecked:=True)
        Next

        clbTooltipFields.EndUpdate()

        AddHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck
        m_canvas.TooltipFields = Nothing
    End Sub

    Private Sub UpdateSectionBounds()
        Dim axis As CfdAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        Dim len As Integer = Math.Max(0, m_canvas.SectionLength(axis) - 1)

        If trackSectionPos IsNot Nothing Then
            trackSectionPos.Maximum = len
            trackSectionPos.Value = Math.Min(trackSectionPos.Value, len)
        End If
    End Sub

    ' ---------------- 播放 ----------------

    Private Sub OnPlayClick(sender As Object, e As EventArgs) Handles btnPlay.Click
        If Not m_canvas.IsReady Then Return

        m_playing = Not m_playing
        btnPlay.Text = If(m_playing, "❚❚", "▶")

        If m_playing Then
            Call m_playTimer.Start()
        Else
            Call m_playTimer.Stop()
        End If
    End Sub

    Private Sub OnPlayTick(sender As Object, e As EventArgs) Handles m_playTimer.Tick
        If Not m_canvas.IsReady Then Return

        Dim n As Integer = m_canvas.Dataset.FrameCount
        Dim nextFrame As Integer = (m_canvas.FrameIndex + 1) Mod n

        Call m_canvas.ShowFrame(nextFrame)
    End Sub

    ' ---------------- 视口事件 ----------------

    Private Sub OnDatasetLoaded(dataset As CfdDataset) Handles m_canvas.DatasetLoaded
        If InvokeRequired Then
            BeginInvoke(Sub() OnDatasetLoaded(dataset))
            Return
        End If

        trackFrame.Maximum = Math.Max(0, dataset.FrameCount - 1)
        trackFrame.Value = 0
    End Sub

    Private Sub OnFrameChanged(frameIndex As Integer, time As Double) Handles m_canvas.FrameChanged
        If InvokeRequired Then
            BeginInvoke(Sub() OnFrameChanged(frameIndex, time))
            Return
        End If

        If Not trackFrame.Focused Then
            trackFrame.Value = frameIndex
        End If

        Dim n As Integer = If(m_canvas.Dataset IsNot Nothing, m_canvas.Dataset.FrameCount, 0)
        lblFrame.Text = $"帧 {frameIndex + 1} / {n} · t = {time:F3}"

        Call UpdateSlice()
        Call UpdateVoxelInfo()
        Call UpdatePropertyGrid()
    End Sub

    Private Sub OnVoxelPicked(e As VoxelPickEventArgs) Handles m_canvas.VoxelPicked
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPicked(e))
            Return
        End If

        m_selectedVoxel = e.VoxelIndex
        Call UpdateVoxelInfo()
        Call UpdatePropertyGrid()
        Call LoadSeries()
    End Sub

    Private Sub OnVoxelPickCleared() Handles m_canvas.VoxelPickCleared
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPickCleared())
            Return
        End If

        m_selectedVoxel = -1
        m_series = Nothing
        lblVoxelInfo.Text = "点击体素查看详情"
        lblSeriesHint.Text = ""
        pnlSeries.Invalidate()
        pgVoxel.SelectedObject = Nothing
    End Sub

    Private Sub UpdateVoxelInfo()
        If m_selectedVoxel < 0 Then Return

        Dim args = m_canvas.GetPickInfo(m_selectedVoxel)

        If args Is Nothing Then Return

        lblVoxelInfo.Text =
            $"坐标: ({args.I}, {args.J}, {args.K})" & Environment.NewLine &
            $"{CFDCanvas.FieldLabel(m_canvas.Field)}: {args.FieldValue:F4}" & Environment.NewLine &
            $"速度 (u,v,w): ({args.U:F3}, {args.V:F3}, {args.W:F3})" & Environment.NewLine &
            $"|V|: {args.Speed:F4}"
    End Sub

    ''' <summary>
    ''' 用 DynamicType.Create 构建动态对象并在 PropertyGrid 中显示
    ''' 选中体素的全部字段值（clbTooltipFields 列举的所有字段，
    ''' 不论其是否被勾选用于 tooltip）。
    ''' </summary>
    ''' <remarks>
    ''' 动态类型会被缓存：只有 clbTooltipFields 的字段集合发生变化时
    ''' 才重新 Reflection.Emit；逐帧刷新只是用缓存类型换一组属性值，
    ''' 避免播放时每帧都生成新的动态类型。
    ''' </remarks>
    Private Sub UpdatePropertyGrid()
        If m_selectedVoxel < 0 OrElse Not m_canvas.IsReady Then
            pgVoxel.SelectedObject = Nothing
            Return
        End If

        ' clbTooltipFields 列举的全部字段（无论勾选与否）
        Dim names As New List(Of String)()

        For i As Integer = 0 To clbTooltipFields.Items.Count - 1
            Call names.Add(CStr(clbTooltipFields.Items(i)))
        Next

        If names.Count = 0 Then
            pgVoxel.SelectedObject = Nothing
            Return
        End If

        Dim values As Dictionary(Of String, Double) = m_canvas.GetVoxelFields(m_selectedVoxel)

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

        pgVoxel.SelectedObject = JavaScriptObject.CreateDynamicObject(m_propDynamicType, pairs)
    End Sub

    Private Sub LoadSeries()
        If m_selectedVoxel < 0 OrElse Not m_canvas.IsReady Then Return

        Dim voxelIdx As Integer = m_selectedVoxel
        Dim field As String = m_canvas.Field

        lblSeriesHint.Text = "时间序列计算中..."

        Call Task.Run(
            Sub()
                Try
                    Dim series = m_canvas.GetVoxelSeries(voxelIdx, field)
                    BeginInvoke(
                        Sub()
                            If m_selectedVoxel = voxelIdx AndAlso field = m_canvas.Field Then
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

    Private Sub UpdateSlice()
        If Not m_canvas.IsReady Then Return

        Dim axis As CfdAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        Dim pos As Integer = trackSectionPos.Value

        Dim old As Bitmap = m_slice
        m_slice = m_canvas.RenderSliceBitmap(axis, pos, picSlice.Width - 2, picSlice.Height - 2)

        If old IsNot Nothing Then
            Call old.Dispose()
        End If

        picSlice.Image = m_slice
    End Sub

    ' ---------------- tooltip 字段清单 ----------------

    ''' <summary>全选 / 清空 tooltip 字段清单。</summary>
    Private Sub SetAllTooltipFields(checked As Boolean)
        ' 批量设置期间挂起 ItemCheck 联动
        RemoveHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck

        For i As Integer = 0 To clbTooltipFields.Items.Count - 1
            clbTooltipFields.SetItemChecked(i, checked)
        Next

        AddHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck
        Call ApplyTooltipFields()
    End Sub

    ''' <summary>把勾选的字段清单同步到控件（全部勾选 = 显示全部，传 Nothing）。</summary>
    Private Sub ApplyTooltipFields()
        Dim checked As New List(Of String)()

        For i As Integer = 0 To clbTooltipFields.Items.Count - 1
            If clbTooltipFields.GetItemChecked(i) Then
                Call checked.Add(CStr(clbTooltipFields.Items(i)))
            End If
        Next

        If checked.Count = clbTooltipFields.Items.Count OrElse checked.Count = 0 Then
            m_canvas.TooltipFields = Nothing
        Else
            m_canvas.TooltipFields = checked.ToArray()
        End If
    End Sub

    Private Sub OnTooltipFieldCheck(sender As Object, e As ItemCheckEventArgs) Handles clbTooltipFields.ItemCheck
        BeginInvoke(Sub() ApplyTooltipFields())
    End Sub

    ' ---------------- 时间序列绘制（复刻 ECharts 折线 + 面积图） ----------------

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
