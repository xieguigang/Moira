Imports System.Drawing.Drawing2D
Imports CDFDxCanvas
Imports CDFDxCanvas.Data
Imports Galaxy.Workbench
Imports Microsoft.VisualStudio.WinForms.Docking

Public Class PageCFDPlayer

    Dim m_playing As Boolean = False
    Dim panelLeft As PanelPlayerLeft
    Dim panelRight As PanelPlayerRight

    ''' <summary>暴露给左右面板访问的 CFD 画布。</summary>
    Friend ReadOnly Property Canvas As CFDCanvas
        Get
            Return m_canvas
        End Get
    End Property

    ' ---------------- 窗体与停靠面板初始化 ----------------

    Private Sub frmCFDPlayer_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        panelLeft = New PanelPlayerLeft With {.player = Me}
        panelRight = New PanelPlayerRight With {.player = Me}

        Call CommonRuntime.RegisterToolWindow(panelLeft, DockState.DockLeft)
        Call CommonRuntime.RegisterToolWindow(panelRight, DockState.DockRight)

        panelLeft.Width = 300
        panelRight.Width = 300

        ' btnPlay 圆形裁剪区域（需在控件创建后设置）
        Dim path As New GraphicsPath()
        path.AddEllipse(0, 0, btnPlay.Width - 1, btnPlay.Height - 1)
        btnPlay.Region = New Region(path)
        btnPlay.ForeColor = Color.White
        path.Dispose()
    End Sub

    ' ---------------- 播放控制事件 ----------------

    Private Sub OnPlayClick(sender As Object, e As EventArgs) Handles btnPlay.Click
        If Not m_canvas.IsReady Then Return

        m_playing = Not m_playing
        btnPlay.Text = If(m_playing, "❚❚", "▶")
        btnPlay.ForeColor = Color.White

        If m_playing Then
            Call m_playTimer.Start()
        Else
            Call m_playTimer.Stop()
        End If
    End Sub

    Private Sub cboSpeed_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboSpeed.SelectedIndexChanged
        Dim fps As Integer() = {2, 5, 10, 20, 30}
        m_playTimer.Interval = CInt(1000 / fps(cboSpeed.SelectedIndex))
    End Sub

    Private Sub trackFrame_ValueChanged(sender As Object, e As EventArgs) Handles trackFrame.ValueChanged
        If trackFrame.Focused Then m_canvas.ShowFrame(trackFrame.Value)
    End Sub

    Private Sub bottomPanel_Resize(sender As Object, e As EventArgs) Handles bottomPanel.Resize
        trackFrame.SetBounds(210, 20, bottomPanel.Width - 400, 28)
        lblFrame.Location = New Point(bottomPanel.Width - lblFrame.PreferredWidth - 16, 24)
    End Sub

    ' ---------------- 画布事件 ----------------

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

        Call panelRight.UpdateSlice()
        Call panelRight.UpdateVoxelInfo()
        Call panelRight.UpdatePropertyGrid()
    End Sub

    Private Sub OnVoxelPicked(e As VoxelPickEventArgs) Handles m_canvas.VoxelPicked
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPicked(e))
            Return
        End If

        Call panelRight.HandleVoxelPicked(e)
    End Sub

    Private Sub OnVoxelPickCleared() Handles m_canvas.VoxelPickCleared
        If InvokeRequired Then
            BeginInvoke(Sub() OnVoxelPickCleared())
            Return
        End If

        Call panelRight.HandleVoxelPickCleared()
    End Sub

    Private Sub OnPlayTick(sender As Object, e As EventArgs) Handles m_playTimer.Tick
        If Not m_canvas.IsReady Then Return

        Dim n As Integer = m_canvas.Dataset.FrameCount
        Dim nextFrame As Integer = (m_canvas.FrameIndex + 1) Mod n

        Call m_canvas.ShowFrame(nextFrame)
    End Sub

    ' ---------------- 数据加载协调 ----------------

    Friend Sub LoadFolder(folder As String)
        Text = $"CFD Player · 正在加载 {folder} ..."
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
            Text = "CFD Player"
            Call MessageBox.Show(Me, $"数据加载失败: {ex.Message}", "加载失败",
                                 MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim dataset = m_canvas.Dataset

        Text = $"CFD Player · 已加载 {dataset.FrameCount} 帧 " &
               $"({dataset.Nx}×{dataset.Ny}×{dataset.Nz}，" &
               $"{dataset.ActiveVoxels:N0} 活动体素 · {dataset.FieldNames.Length} 个标量场)"

        Call CommonRuntime.Success(Text)
        Call Globals.host.SetTitle(Text)

        Call panelLeft.PopulateFields(dataset.FieldNames)
        btnPlay.Enabled = True
        trackFrame.Enabled = True
        btnPlay.ForeColor = Color.White

        Call panelLeft.UpdateSectionBounds()
        Call panelLeft.PopulateTooltipFields(dataset.FieldNames)

        m_canvas.TooltipFields = Nothing
    End Sub

    Private Sub PageCFDPlayer_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If MessageBox.Show("Close current CFD re-play session window?", "Close Session", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) <> DialogResult.OK Then
            e.Cancel = True
        Else
            panelLeft.Close()
            panelRight.Close()
        End If
    End Sub
End Class
