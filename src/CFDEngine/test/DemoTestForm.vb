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
' /********************************************************************************/

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports CDFDxCanvas
Imports CDFDxCanvas.Data
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors

Public Class DemoTestForm
    Inherits Form

    Const DefaultDemoFolder As String = "G:\fermenter\src\demo\cfd"

    ' ---------------- 视口与数据 ----------------

    ReadOnly m_canvas As New CFDCanvas
    ReadOnly m_playTimer As New Timer With {.Enabled = False}
    Dim m_playing As Boolean = False
    Dim m_series As VoxelSeries
    Dim m_slice As Bitmap
    Dim m_selectedVoxel As Integer = -1

    ' ---------------- 控件 ----------------

    ReadOnly cboField As New ComboBox With {.Name = "cboField"}
    ReadOnly cboPalette As New ComboBox With {.Name = "cboPalette"}
    ReadOnly cboRangeMode As New ComboBox With {.Name = "cboRangeMode"}
    ReadOnly txtRangeMin As New TextBox With {.Name = "txtRangeMin"}
    ReadOnly txtRangeMax As New TextBox With {.Name = "txtRangeMax"}
    ReadOnly lblThresholdVal As New Label With {.Name = "lblThresholdVal"}
    ReadOnly trackThreshold As New TrackBar With {.Name = "trackThreshold"}
    ReadOnly chkArrows As New CheckBox With {.Name = "chkArrows"}
    ReadOnly cboArrowDensity As New ComboBox With {.Name = "cboArrowDensity"}
    ReadOnly cboSectionMode As New ComboBox With {.Name = "cboSectionMode"}
    ReadOnly cboSectionAxis As New ComboBox With {.Name = "cboSectionAxis"}
    ReadOnly lblSectionPosVal As New Label With {.Name = "lblSectionPosVal"}
    ReadOnly trackSectionPos As New TrackBar With {.Name = "trackSectionPos"}

    ReadOnly lblVoxelInfo As New Label With {.Name = "lblVoxelInfo"}
    ReadOnly pnlSeries As New Panel With {.Name = "pnlSeries"}
    ReadOnly lblSeriesHint As New Label With {.Name = "lblSeriesHint"}
    ReadOnly picSlice As New PictureBox With {.Name = "picSlice"}

    ReadOnly btnPlay As New Button With {.Name = "btnPlay"}
    ReadOnly cboSpeed As New ComboBox With {.Name = "cboSpeed"}
    ReadOnly trackFrame As New TrackBar With {.Name = "trackFrame"}
    ReadOnly lblFrame As New Label With {.Name = "lblFrame"}
    ReadOnly chkTooltip As New CheckBox With {.Name = "chkTooltip"}

    Public Sub New()
        Text = "CFD 可视化 · CFDCanvas 控件测试"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(1440, 860)
        MinimumSize = New Size(1100, 640)
        BackColor = Color.FromArgb(244, 246, 250)
        Font = New Font("Microsoft YaHei UI", 9.0F)

        Call BuildLayout()

        AddHandler m_playTimer.Tick, AddressOf OnPlayTick

        AddHandler Shown, AddressOf OnFormShown
        AddHandler m_canvas.DatasetLoaded, AddressOf OnDatasetLoaded
        AddHandler m_canvas.FrameChanged, AddressOf OnFrameChanged
        AddHandler m_canvas.VoxelPicked, AddressOf OnVoxelPicked
        AddHandler m_canvas.VoxelPickCleared, AddressOf OnVoxelPickCleared

        AddHandler pnlSeries.Paint, AddressOf DrawSeries
        AddHandler pnlSeries.Resize, Sub() pnlSeries.Invalidate()
    End Sub

    ' ---------------- 布局 ----------------

    Private Sub BuildLayout()
        ' 中央视口（先加入，占满剩余空间）
        m_canvas.Dock = DockStyle.Fill
        Controls.Add(m_canvas)

        ' 左侧控制面板
        Dim left As New Panel With {.Dock = DockStyle.Left, .Width = 292,
                                    .BackColor = Color.White, .AutoScroll = True}
        Call BuildLeftPanel(left)
        Controls.Add(left)

        ' 右侧信息面板
        Dim right As New Panel With {.Dock = DockStyle.Right, .Width = 330,
                                     .BackColor = Color.White, .AutoScroll = True}
        Call BuildRightPanel(right)
        Controls.Add(right)

        ' 底部时间轴
        Dim bottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 64,
                                      .BackColor = Color.White}
        Call BuildBottomBar(bottom)
        Controls.Add(bottom)
    End Sub

    Private Function AddTitle(parent As Control, text As String, y As Integer) As Integer
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Microsoft YaHei UI", 10.0F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(30, 41, 59),
            .Location = New Point(16, y), .AutoSize = True}
        parent.Controls.Add(lbl)
        Return y + 28
    End Function

    Private Function AddLabel(parent As Control, text As String, y As Integer) As Integer
        Dim lbl As New Label With {
            .Text = text, .ForeColor = Color.FromArgb(71, 85, 105),
            .Location = New Point(16, y), .AutoSize = True}
        parent.Controls.Add(lbl)
        Return y + 22
    End Function

    Private Sub StyleCombo(c As ComboBox)
        c.DropDownStyle = ComboBoxStyle.DropDownList
        c.FlatStyle = FlatStyle.Flat
        c.Font = New Font("Microsoft YaHei UI", 9.0F)
        c.BackColor = Color.FromArgb(248, 250, 252)
    End Sub

    Private Sub BuildLeftPanel(panel As Panel)
        Dim y As Integer = 14

        ' ---- 数据加载 ----
        y = AddTitle(panel, "数据", y)

        Dim btnLoad As New Button With {
            .Text = "加载数据文件夹", .Size = New Size(140, 32),
            .Location = New Point(16, y),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(37, 99, 235),
            .ForeColor = Color.White,
            .Font = New Font("Microsoft YaHei UI", 9.0F)}
        btnLoad.FlatAppearance.BorderSize = 0
        AddHandler btnLoad.Click, AddressOf OnLoadClick
        panel.Controls.Add(btnLoad)

        y += 44

        ' ---- 标量场 ----
        y = AddTitle(panel, "标量场", y)
        y = AddLabel(panel, "标量场（由 VTI 实际字段生成）", y)

        StyleCombo(cboField)
        cboField.DropDownWidth = 420
        cboField.SetBounds(16, y, 250, 26)
        AddHandler cboField.SelectedIndexChanged,
            Sub(s, e)
                If cboField.SelectedItem Is Nothing Then Return
                m_canvas.Field = CStr(cboField.SelectedItem)
            End Sub
        panel.Controls.Add(cboField)
        y += 34

        y = AddLabel(panel, "调色板", y)
        StyleCombo(cboPalette)
        For Each name As String In [Enum].GetNames(GetType(ScalerPalette))
            Call cboPalette.Items.Add(name)
        Next
        cboPalette.SelectedItem = "Jet"
        cboPalette.SetBounds(16, y, 250, 26)
        AddHandler cboPalette.SelectedIndexChanged,
            Sub(s, e) m_canvas.Palette = CType([Enum].Parse(GetType(ScalerPalette), CStr(cboPalette.SelectedItem)), ScalerPalette)
        panel.Controls.Add(cboPalette)
        y += 40

        ' ---- 颜色值域 ----
        y = AddTitle(panel, "颜色值域", y)
        StyleCombo(cboRangeMode)
        cboRangeMode.Items.AddRange(New Object() {"自动", "手动"})
        cboRangeMode.SelectedIndex = 0
        cboRangeMode.SetBounds(16, y, 250, 26)
        AddHandler cboRangeMode.SelectedIndexChanged,
            Sub(s, e)
                m_canvas.AutoRange = cboRangeMode.SelectedIndex = 0
                txtRangeMin.Visible = cboRangeMode.SelectedIndex = 1
                txtRangeMax.Visible = cboRangeMode.SelectedIndex = 1
            End Sub
        panel.Controls.Add(cboRangeMode)
        y += 34

        Dim lblMin As New Label With {.Text = "最小值", .ForeColor = Color.FromArgb(71, 85, 105),
                                      .Location = New Point(16, y), .AutoSize = True}
        panel.Controls.Add(lblMin)

        Dim lblMax As New Label With {.Text = "最大值", .ForeColor = Color.FromArgb(71, 85, 105),
                                      .Location = New Point(150, y), .AutoSize = True}
        panel.Controls.Add(lblMax)
        y += 20

        For Each box As TextBox In {txtRangeMin, txtRangeMax}
            box.BorderStyle = BorderStyle.FixedSingle
            box.Font = New Font("Microsoft YaHei UI", 9.0F)
        Next
        txtRangeMin.SetBounds(16, y, 115, 24)
        txtRangeMax.SetBounds(150, y, 115, 24)
        txtRangeMin.Visible = False
        txtRangeMax.Visible = False
        AddHandler txtRangeMin.TextChanged,
            Sub(s, e)
                Dim v As Double
                If Double.TryParse(txtRangeMin.Text, v) Then m_canvas.RangeMin = v
            End Sub
        AddHandler txtRangeMax.TextChanged,
            Sub(s, e)
                Dim v As Double
                If Double.TryParse(txtRangeMax.Text, v) Then m_canvas.RangeMax = v
            End Sub
        panel.Controls.Add(txtRangeMin)
        panel.Controls.Add(txtRangeMax)
        y += 36

        y = AddLabel(panel, "透明阈值", y)
        lblThresholdVal.Text = "0.00"
        lblThresholdVal.ForeColor = Color.FromArgb(14, 165, 233)
        lblThresholdVal.Location = New Point(220, y - 18)
        panel.Controls.Add(lblThresholdVal)

        trackThreshold.Minimum = 0
        trackThreshold.Maximum = 100
        trackThreshold.TickStyle = TickStyle.None
        trackThreshold.SetBounds(12, y - 8, 260, 30)
        AddHandler trackThreshold.ValueChanged,
            Sub(s, e)
                Dim t As Double = trackThreshold.Value / 100.0
                lblThresholdVal.Text = t.ToString("F2")
                m_canvas.Threshold = t
            End Sub
        panel.Controls.Add(trackThreshold)
        y += 38

        ' ---- 速度矢量箭头 ----
        y = AddTitle(panel, "速度矢量箭头", y)

        chkArrows.Text = "显示箭头"
        chkArrows.AutoSize = True
        chkArrows.Location = New Point(16, y)
        AddHandler chkArrows.CheckedChanged, Sub(s, e) m_canvas.ShowArrows = chkArrows.Checked
        panel.Controls.Add(chkArrows)
        y += 28

        y = AddLabel(panel, "箭头密度", y)
        StyleCombo(cboArrowDensity)
        cboArrowDensity.Items.AddRange(New Object() {"高 (2×2×2)", "中 (3×3×3)", "低 (4×4×4)"})
        cboArrowDensity.SelectedIndex = 0
        cboArrowDensity.SetBounds(16, y, 250, 26)
        AddHandler cboArrowDensity.SelectedIndexChanged,
            Sub(s, e) m_canvas.ArrowDensity = cboArrowDensity.SelectedIndex + 2
        panel.Controls.Add(cboArrowDensity)
        y += 40

        ' ---- 横截面 ----
        y = AddTitle(panel, "横截面", y)

        y = AddLabel(panel, "截面模式", y)
        StyleCombo(cboSectionMode)
        cboSectionMode.Items.AddRange(New Object() {"不启用", "启用（裁剪远侧）", "切片模式（单层）"})
        cboSectionMode.SelectedIndex = 0
        cboSectionMode.SetBounds(16, y, 250, 26)
        AddHandler cboSectionMode.SelectedIndexChanged, AddressOf ApplySectionMode
        panel.Controls.Add(cboSectionMode)
        y += 34

        y = AddLabel(panel, "截面轴", y)
        StyleCombo(cboSectionAxis)
        cboSectionAxis.Items.AddRange(New Object() {"X 轴", "Y 轴", "Z 轴"})
        cboSectionAxis.SelectedIndex = 0
        cboSectionAxis.SetBounds(16, y, 250, 26)
        AddHandler cboSectionAxis.SelectedIndexChanged,
            Sub(s, e)
                m_canvas.SectionAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
                UpdateSectionBounds()
            End Sub
        panel.Controls.Add(cboSectionAxis)
        y += 34

        y = AddLabel(panel, "位置", y)
        lblSectionPosVal.Text = "0"
        lblSectionPosVal.ForeColor = Color.FromArgb(14, 165, 233)
        lblSectionPosVal.Location = New Point(220, y - 18)
        panel.Controls.Add(lblSectionPosVal)

        trackSectionPos.Minimum = 0
        trackSectionPos.Maximum = 47
        trackSectionPos.TickStyle = TickStyle.None
        trackSectionPos.SetBounds(12, y - 8, 260, 30)
        AddHandler trackSectionPos.ValueChanged,
            Sub(s, e)
                lblSectionPosVal.Text = trackSectionPos.Value.ToString()
                m_canvas.SectionPosition = trackSectionPos.Value
            End Sub
        panel.Controls.Add(trackSectionPos)
        y += 40

        ' ---- 悬停提示 ----
        y = AddTitle(panel, "视口", y)

        chkTooltip.Text = "悬停显示体素数据提示"
        chkTooltip.AutoSize = True
        chkTooltip.Location = New Point(16, y)
        AddHandler chkTooltip.CheckedChanged, Sub(s, e) m_canvas.ShowHoverTooltip = chkTooltip.Checked
        panel.Controls.Add(chkTooltip)
    End Sub

    ''' <summary>把截面模式下拉映射到控件属性。</summary>
    Private Sub ApplySectionMode()
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

    Private Sub BuildRightPanel(panel As Panel)
        Dim y As Integer = 14

        y = AddTitle(panel, "选中体素", y)

        lblVoxelInfo.Text = "点击体素查看详情"
        lblVoxelInfo.ForeColor = Color.FromArgb(71, 85, 105)
        lblVoxelInfo.Font = New Font("Microsoft YaHei UI", 9.0F)
        lblVoxelInfo.Location = New Point(16, y)
        lblVoxelInfo.Size = New Size(296, 84)
        panel.Controls.Add(lblVoxelInfo)
        y += 94

        y = AddTitle(panel, "时间序列", y)

        pnlSeries.SetBounds(12, y, 300, 200)
        pnlSeries.BorderStyle = BorderStyle.FixedSingle
        pnlSeries.BackColor = Color.White
        panel.Controls.Add(pnlSeries)

        lblSeriesHint.Text = ""
        lblSeriesHint.ForeColor = Color.FromArgb(148, 163, 184)
        lblSeriesHint.Font = New Font("Microsoft YaHei UI", 8.0F)
        lblSeriesHint.Location = New Point(16, y + 202)
        panel.Controls.Add(lblSeriesHint)
        y += 226

        y = AddTitle(panel, "2D 横截面", y)

        picSlice.SetBounds(12, y, 300, 300)
        picSlice.BorderStyle = BorderStyle.FixedSingle
        picSlice.BackColor = Color.FromArgb(248, 250, 252)
        picSlice.SizeMode = PictureBoxSizeMode.StretchImage
        panel.Controls.Add(picSlice)
    End Sub

    Private Sub BuildBottomBar(panel As Panel)
        btnPlay.Text = "▶"
        btnPlay.Size = New Size(40, 40)
        btnPlay.Location = New Point(16, 12)
        btnPlay.FlatStyle = FlatStyle.Flat
        btnPlay.FlatAppearance.BorderSize = 0
        btnPlay.BackColor = Color.FromArgb(37, 99, 235)
        btnPlay.ForeColor = Color.White
        btnPlay.Font = New Font("Microsoft YaHei UI", 11.0F)
        btnPlay.Enabled = False

        Dim path As New GraphicsPath()
        path.AddEllipse(0, 0, btnPlay.Width - 1, btnPlay.Height - 1)
        btnPlay.Region = New Region(path)
        path.Dispose()

        AddHandler btnPlay.Click, AddressOf OnPlayClick
        panel.Controls.Add(btnPlay)

        Dim lblSpeed As New Label With {.Text = "速度", .ForeColor = Color.FromArgb(71, 85, 105),
                                        .Location = New Point(72, 22), .AutoSize = True}
        panel.Controls.Add(lblSpeed)

        StyleCombo(cboSpeed)
        cboSpeed.Items.AddRange(New Object() {"2 fps", "5 fps", "10 fps", "20 fps", "30 fps"})
        cboSpeed.SelectedIndex = 1
        cboSpeed.SetBounds(112, 18, 84, 26)
        AddHandler cboSpeed.SelectedIndexChanged,
            Sub(s, e)
                Dim fps As Integer() = {2, 5, 10, 20, 30}
                m_playTimer.Interval = CInt(1000 / fps(cboSpeed.SelectedIndex))
            End Sub
        panel.Controls.Add(cboSpeed)

        trackFrame.Minimum = 0
        trackFrame.Maximum = 0
        trackFrame.TickStyle = TickStyle.None
        trackFrame.Enabled = False
        AddHandler trackFrame.ValueChanged,
            Sub(s, e)
                If trackFrame.Focused Then m_canvas.ShowFrame(trackFrame.Value)
            End Sub

        lblFrame.Text = "— · —"
        lblFrame.ForeColor = Color.FromArgb(71, 85, 105)
        lblFrame.AutoSize = True
        lblFrame.Location = New Point(0, 24)

        panel.Controls.Add(trackFrame)
        panel.Controls.Add(lblFrame)

        AddHandler panel.Resize,
            Sub(s, e)
                trackFrame.SetBounds(210, 20, panel.Width - 400, 28)
                lblFrame.Location = New Point(panel.Width - lblFrame.PreferredWidth - 16, 24)
            End Sub
    End Sub

    ' ---------------- 数据加载 ----------------

    Private Sub OnFormShown(sender As Object, e As EventArgs)
        If IO.Directory.Exists(DefaultDemoFolder) Then
            Call LoadFolder(DefaultDemoFolder)
        End If
    End Sub

    Private Sub OnLoadClick(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog With {.ShowNewFolderButton = False}
            If IO.Directory.Exists(DefaultDemoFolder) Then
                dialog.SelectedPath = DefaultDemoFolder
            End If

            If dialog.ShowDialog(Me) = DialogResult.OK Then
                Call LoadFolder(dialog.SelectedPath)
            End If
        End Using
    End Sub

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
    End Sub

    Private Sub UpdateSectionBounds()
        Dim axis As CfdAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        Dim len As Integer = Math.Max(0, m_canvas.SectionLength(axis) - 1)

        trackSectionPos.Maximum = len
        trackSectionPos.Value = Math.Min(trackSectionPos.Value, len)
    End Sub

    ' ---------------- 播放 ----------------

    Private Sub OnPlayClick(sender As Object, e As EventArgs)
        If Not m_canvas.IsReady Then Return

        m_playing = Not m_playing
        btnPlay.Text = If(m_playing, "❚❚", "▶")

        If m_playing Then
            Call m_playTimer.Start()
        Else
            Call m_playTimer.Stop()
        End If
    End Sub

    Private Sub OnPlayTick(sender As Object, e As EventArgs)
        If Not m_canvas.IsReady Then Return

        Dim n As Integer = m_canvas.Dataset.FrameCount
        Dim nextFrame As Integer = (m_canvas.FrameIndex + 1) Mod n

        Call m_canvas.ShowFrame(nextFrame)
    End Sub

    ' ---------------- 视口事件 ----------------

    Private Sub OnDatasetLoaded(dataset As CfdDataset)
        If InvokeRequired Then
            BeginInvoke(Sub() OnDatasetLoaded(dataset))
            Return
        End If

        trackFrame.Maximum = Math.Max(0, dataset.FrameCount - 1)
        trackFrame.Value = 0
    End Sub

    Private Sub OnFrameChanged(frameIndex As Integer, time As Double)
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
    End Sub

    Private Sub OnVoxelPicked(e As VoxelPickEventArgs)
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPicked(e))
            Return
        End If

        m_selectedVoxel = e.VoxelIndex
        Call UpdateVoxelInfo()
        Call LoadSeries()
    End Sub

    Private Sub OnVoxelPickCleared()
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPickCleared())
            Return
        End If

        m_selectedVoxel = -1
        m_series = Nothing
        lblVoxelInfo.Text = "点击体素查看详情"
        lblSeriesHint.Text = ""
        pnlSeries.Invalidate()
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

    ' ---------------- 时间序列绘制（复刻 ECharts 折线 + 面积图） ----------------

    Private Sub DrawSeries(sender As Object, e As PaintEventArgs)
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

End Class
