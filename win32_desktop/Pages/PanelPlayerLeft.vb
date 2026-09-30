Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Moira.CDFDxCanvas.Data

Public Class PanelPlayerLeft

    Friend player As PageCFDPlayer

    ' ---------------- 数据加载 ----------------

    Private Sub OnLoadClick(sender As Object, e As EventArgs) Handles btnLoad.Click
        Using dialog As New FolderBrowserDialog With {.ShowNewFolderButton = False}
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                Call player.LoadFolder(dialog.SelectedPath)
            End If
        End Using
    End Sub

    ''' <summary>数据集加载完成后由 PageCFDPlayer 调用：填充标量场下拉。</summary>
    Friend Sub PopulateFields(fieldNames As String())
        cboField.Items.Clear()

        For Each name As String In fieldNames
            Call cboField.Items.Add(name)
        Next

        If player.Canvas.Dataset.HasField(player.Canvas.Field) Then
            cboField.SelectedItem = player.Canvas.Field
        ElseIf cboField.Items.Count > 0 Then
            cboField.SelectedIndex = 0
        End If
    End Sub

    ''' <summary>数据集加载完成后由 PageCFDPlayer 调用：填充 tooltip 字段清单。</summary>
    Friend Sub PopulateTooltipFields(fieldNames As String())
        RemoveHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck

        clbTooltipFields.BeginUpdate()
        clbTooltipFields.Items.Clear()

        For Each name As String In fieldNames
            Call clbTooltipFields.Items.Add(name, isChecked:=True)
        Next

        clbTooltipFields.EndUpdate()

        AddHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck
    End Sub

    ' ---------------- 标量场 / 调色板 / 值域 ----------------

    Private Sub cboField_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboField.SelectedIndexChanged
        If cboField.SelectedItem Is Nothing Then Return
        player.Canvas.Field = CStr(cboField.SelectedItem)
    End Sub

    Private Sub cboPalette_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboPalette.SelectedIndexChanged
        player.Canvas.Palette = CType([Enum].Parse(GetType(ScalerPalette), CStr(cboPalette.SelectedItem)), ScalerPalette)
    End Sub

    Private Sub cboRangeMode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboRangeMode.SelectedIndexChanged
        player.Canvas.AutoRange = cboRangeMode.SelectedIndex = 0
        txtRangeMin.Visible = cboRangeMode.SelectedIndex = 1
        txtRangeMax.Visible = cboRangeMode.SelectedIndex = 1
    End Sub

    Private Sub txtRangeMin_TextChanged(sender As Object, e As EventArgs) Handles txtRangeMin.TextChanged
        Dim v As Double
        If Double.TryParse(txtRangeMin.Text, v) Then player.Canvas.RangeMin = v
    End Sub

    Private Sub txtRangeMax_TextChanged(sender As Object, e As EventArgs) Handles txtRangeMax.TextChanged
        Dim v As Double
        If Double.TryParse(txtRangeMax.Text, v) Then player.Canvas.RangeMax = v
    End Sub

    ' ---------------- 阈值 / 箭头 / 截面 ----------------

    Private Sub trackThreshold_ValueChanged(sender As Object, e As EventArgs) Handles trackThreshold.ValueChanged
        Dim t As Double = trackThreshold.Value / 100.0
        lblThresholdVal.Text = t.ToString("F2")
        player.Canvas.Threshold = t
    End Sub

    Private Sub chkArrows_CheckedChanged(sender As Object, e As EventArgs) Handles chkArrows.CheckedChanged
        player.Canvas.ShowArrows = chkArrows.Checked
    End Sub

    Private Sub cboArrowDensity_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboArrowDensity.SelectedIndexChanged
        player.Canvas.ArrowDensity = cboArrowDensity.SelectedIndex + 2
    End Sub

    ''' <summary>把截面模式下拉映射到控件属性。</summary>
    Private Sub ApplySectionMode(sender As Object, e As EventArgs) Handles cboSectionMode.SelectedIndexChanged
        Select Case cboSectionMode.SelectedIndex
            Case 1
                player.Canvas.SectionEnabled = True
                player.Canvas.SliceOnly = False
            Case 2
                ' 切片模式：先切到启用态再打开切片开关，保证状态按序生效
                player.Canvas.SectionEnabled = True
                player.Canvas.SliceOnly = True
            Case Else
                player.Canvas.SliceOnly = False
                player.Canvas.SectionEnabled = False
        End Select
    End Sub

    Private Sub cboSectionAxis_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboSectionAxis.SelectedIndexChanged
        player.Canvas.SectionAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        UpdateSectionBounds()
    End Sub

    Private Sub trackSectionPos_ValueChanged(sender As Object, e As EventArgs) Handles trackSectionPos.ValueChanged
        lblSectionPosVal.Text = trackSectionPos.Value.ToString()
        player.Canvas.SectionPosition = trackSectionPos.Value
    End Sub

    Friend Sub UpdateSectionBounds()
        Dim axis As CfdAxis = CType(cboSectionAxis.SelectedIndex, CfdAxis)
        Dim len As Integer = Math.Max(0, player.Canvas.SectionLength(axis) - 1)

        trackSectionPos.Maximum = len
        trackSectionPos.Value = Math.Min(trackSectionPos.Value, len)
    End Sub

    ' ---------------- 视口 / 调试 ----------------

    ''' <summary>体素透明度滑动条：0.5（半透明）~ 1.0（不透明）。</summary>
    Private Sub trackOpacity_ValueChanged(sender As Object, e As EventArgs) Handles trackOpacity.ValueChanged
        Dim opacity As Single = CSng(trackOpacity.Value / 100.0)

        If player IsNot Nothing Then
            lblOpacityVal.Text = opacity.ToString("F2")
            player.Canvas.VoxelOpacity = opacity
        End If
    End Sub

    Private Sub chkTooltip_CheckedChanged(sender As Object, e As EventArgs) Handles chkTooltip.CheckedChanged
        player.Canvas.ShowHoverTooltip = chkTooltip.Checked
    End Sub

    Private Sub chkDebug_CheckedChanged(sender As Object, e As EventArgs) Handles chkDebug.CheckedChanged
        player.Canvas.ShowDebugInfo = chkDebug.Checked
    End Sub

    ' ---------------- tooltip 字段清单 ----------------

    Private Sub btnAll_Click(sender As Object, e As EventArgs) Handles btnAll.Click
        Call SetAllTooltipFields(True)
    End Sub

    Private Sub btnNone_Click(sender As Object, e As EventArgs) Handles btnNone.Click
        Call SetAllTooltipFields(False)
    End Sub

    ''' <summary>全选 / 清空 tooltip 字段清单。</summary>
    Private Sub SetAllTooltipFields(checked As Boolean)
        RemoveHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck

        For i As Integer = 0 To clbTooltipFields.Items.Count - 1
            clbTooltipFields.SetItemChecked(i, checked)
        Next

        AddHandler clbTooltipFields.ItemCheck, AddressOf OnTooltipFieldCheck
        Call ApplyTooltipFields()
    End Sub

    ''' <summary>把勾选的字段清单同步到画布（全部勾选 = 显示全部，传 Nothing）。</summary>
    Private Sub ApplyTooltipFields()
        Dim checked As New List(Of String)()

        For i As Integer = 0 To clbTooltipFields.Items.Count - 1
            If clbTooltipFields.GetItemChecked(i) Then
                Call checked.Add(CStr(clbTooltipFields.Items(i)))
            End If
        Next

        If checked.Count = clbTooltipFields.Items.Count OrElse checked.Count = 0 Then
            player.Canvas.TooltipFields = Nothing
        Else
            player.Canvas.TooltipFields = checked.ToArray()
        End If
    End Sub

    Private Sub OnTooltipFieldCheck(sender As Object, e As ItemCheckEventArgs) Handles clbTooltipFields.ItemCheck
        BeginInvoke(Sub() ApplyTooltipFields())
    End Sub
End Class
