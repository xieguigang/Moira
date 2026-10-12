' /********************************************************************************/
'
'   CliToolLayout.vb
'
'   三个 CFD 工具窗体的公共外观与交互辅助（浅色工作台风格）：
'     · 配色 / 字体常量
'     · 参数行（标签 + 控件）与分组标题的小工厂
'     · 命令行预览框、日志框的统一样式
'     · 参数变更 → 刷新预览的批量事件接线、日志追加与限行
'
' /********************************************************************************/

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>CFD 工具窗体的公共布局与行为辅助。</summary>
Friend Module CliToolLayout

    ' ---- 配色（工程化浅色工作台） ----

    Friend ReadOnly ColorPrimary As Color = Color.FromArgb(&H0F, &H62, &HFE)
    Friend ReadOnly ColorPrimaryDark As Color = Color.FromArgb(&H2F, &H6F, &HDB)
    Friend ReadOnly ColorAccentSoft As Color = Color.FromArgb(&HE3, &HED, &HFF)
    Friend ReadOnly ColorBg As Color = Color.FromArgb(&HF5, &HF7, &HFA)
    Friend ReadOnly ColorBorder As Color = Color.FromArgb(&HE5, &HE7, &HEB)
    Friend ReadOnly ColorCard As Color = Color.White
    Friend ReadOnly ColorText As Color = Color.FromArgb(&H1F, &H29, &H37)
    Friend ReadOnly ColorTextDim As Color = Color.FromArgb(&H4B, &H55, &H63)
    Friend ReadOnly ColorOk As Color = Color.FromArgb(&H16, &HA3, &H4A)
    Friend ReadOnly ColorWarn As Color = Color.FromArgb(&HD9, &H77, &H06)
    Friend ReadOnly ColorError As Color = Color.FromArgb(&HDC, &H26, &H26)
    Friend ReadOnly ColorLogBg As Color = Color.FromArgb(&H1E, &H1E, &H1E)
    Friend ReadOnly ColorLogFg As Color = Color.FromArgb(&HD4, &HD4, &HD4)

    Friend Const CaptionWidth As Integer = 158
    Friend Const RowWidth As Integer = 720

    Private ReadOnly _fontMono As New Font("Consolas", 9.0F)
    Private ReadOnly _fontUIBold As New Font("Segoe UI", 9.0F, FontStyle.Bold)

    ''' <summary>等宽字体（命令行预览与日志区）。</summary>
    Friend ReadOnly Property FontMono As Font
        Get
            Return _fontMono
        End Get
    End Property

    ''' <summary>
    ''' 构造一行「标签 + 控件组」的参数行。控件按传入顺序自左向右排列。
    ''' </summary>
    Friend Function MakeRow(caption As String, ParamArray inputs As Control()) As Panel
        Dim row As New Panel() With {
            .Margin = New Padding(0, 1, 0, 1),
            .Padding = New Padding(0),
            .BackColor = Color.Transparent
        }

        Dim cap As New Label() With {
            .Text = caption,
            .AutoSize = False,
            .Location = New Point(0, 3),
            .Size = New Size(CaptionWidth - 10, 21),
            .ForeColor = ColorText,
            .TextAlign = ContentAlignment.MiddleLeft
        }

        row.Controls.Add(cap)

        Dim x As Integer = CaptionWidth
        Dim maxHeight As Integer = 23

        For Each input As Control In inputs
            If input.Width <= 0 Then input.Width = 150

            input.Location = New Point(x, 1)
            input.Anchor = AnchorStyles.Top Or AnchorStyles.Left

            If input.Height > maxHeight Then maxHeight = input.Height

            row.Controls.Add(input)
            x += input.Width + 6
        Next

        row.Height = maxHeight + 5
        row.Width = System.Math.Max(RowWidth, x + 8)

        Return row
    End Function

    ''' <summary>行内灰色提示文字。</summary>
    Friend Function MakeNote(text As String) As Label
        Return New Label() With {
            .Text = text,
            .AutoSize = False,
            .Size = New Size(RowWidth, 20),
            .Margin = New Padding(0, 0, 0, 2),
            .ForeColor = ColorTextDim,
            .TextAlign = ContentAlignment.MiddleLeft
        }
    End Function

    ''' <summary>主/次按钮（扁平化）。</summary>
    Friend Function MakeButton(text As String, primary As Boolean, Optional width As Integer = 108) As Button
        Dim button As New Button() With {
            .Text = text,
            .Size = New Size(width, 30),
            .FlatStyle = FlatStyle.Flat,
            .Font = _fontUIBold,
            .Cursor = Cursors.Hand
        }

        button.FlatAppearance.BorderSize = 0

        If primary Then
            button.BackColor = ColorPrimary
            button.ForeColor = Color.White
        Else
            button.BackColor = ColorBorder
            button.ForeColor = ColorText
        End If

        Return button
    End Function

    ''' <summary>文件/目录浏览按钮（"..."）。</summary>
    Friend Function MakeBrowseButton() As Button
        Dim button As New Button() With {
            .Text = "...",
            .Size = New Size(36, 25),
            .FlatStyle = FlatStyle.Flat,
            .Font = _fontUIBold
        }

        button.FlatAppearance.BorderSize = 0
        button.BackColor = ColorAccentSoft
        button.ForeColor = ColorPrimaryDark
        button.Cursor = Cursors.Hand

        Return button
    End Function

    ''' <summary>统一样式化命令行预览框。</summary>
    Friend Sub StyleCommandBox(box As TextBox)
        box.Multiline = True
        box.ReadOnly = True
        box.WordWrap = True
        box.ScrollBars = ScrollBars.Vertical
        box.BackColor = ColorBg
        box.ForeColor = ColorText
        box.Font = _fontMono
        box.BorderStyle = BorderStyle.FixedSingle
    End Sub

    ''' <summary>统一样式化深色日志框。</summary>
    Friend Sub StyleLogBox(box As TextBox)
        box.Multiline = True
        box.ReadOnly = True
        box.WordWrap = False
        box.ScrollBars = ScrollBars.Both
        box.BackColor = ColorLogBg
        box.ForeColor = ColorLogFg
        box.Font = _fontMono
        box.BorderStyle = BorderStyle.None
        box.HideSelection = False
    End Sub

    ''' <summary>
    ''' 批量接线：参数区内所有输入控件的变更事件 → 同一个刷新回调。
    ''' </summary>
    Friend Sub WirePreviewRefresh(container As Control, handler As EventHandler, ParamArray exclude As Control())
        For Each child As Control In container.Controls
            Dim skip As Boolean = (exclude IsNot Nothing) AndAlso exclude.Contains(child)

            If Not skip AndAlso TypeOf child Is TextBox Then
                AddHandler DirectCast(child, TextBox).TextChanged, handler
            ElseIf TypeOf child Is ComboBox Then
                AddHandler DirectCast(child, ComboBox).SelectedIndexChanged, handler
                AddHandler DirectCast(child, ComboBox).TextUpdate, handler
            ElseIf TypeOf child Is NumericUpDown Then
                AddHandler DirectCast(child, NumericUpDown).ValueChanged, handler
            ElseIf TypeOf child Is CheckBox Then
                AddHandler DirectCast(child, CheckBox).CheckedChanged, handler
            ElseIf TypeOf child Is ListBox Then
                AddHandler DirectCast(child, ListBox).SelectedIndexChanged, handler
            End If

            If child.HasChildren Then
                Call WirePreviewRefresh(child, handler)
            End If
        Next
    End Sub

    ''' <summary>
    ''' 向日志框追加一行（自动滚动 + 超量截断，保证长时间运行不拖垮 UI）。
    ''' </summary>
    Friend Sub AppendLogLine(box As TextBox, line As String)
        If box Is Nothing OrElse box.IsDisposed Then Return

        If box.InvokeRequired Then
            Try
                Call box.BeginInvoke(Sub() AppendLogLine(box, line))
            Catch ex As Exception
            End Try

            Return
        End If

        box.AppendText(line & Environment.NewLine)

        If box.Lines.Length > CliRunner.MaxLogLines Then
            Dim keepFrom As Integer = box.Lines.Length - CliRunner.MaxLogLines
            box.Lines = box.Lines.Skip(keepFrom).ToArray()
        End If

        box.SelectionStart = box.TextLength
        box.ScrollToCaret()
    End Sub
End Module
