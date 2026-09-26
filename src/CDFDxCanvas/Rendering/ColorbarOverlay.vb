' /********************************************************************************/
'
'   ColorbarOverlay.vb
'
'   色标条叠加层 —— 在 DxScene3DCanvas 的 Render 事件中绘制的 2D 覆盖物
'
'   作用：
'       复刻 cfd-player.html 右上角的色标条卡片：半透明白色卡片 +
'       垂直渐变色条（上=最大值，下=最小值）+ 场名与 [min, max] 标题。
'       颜色来自 Designer.FromSchema 生成的 LUT。
'
' /********************************************************************************/

Imports Microsoft.VisualBasic.Imaging
Imports Font = Microsoft.VisualBasic.Imaging.Font
Imports Pen = Microsoft.VisualBasic.Imaging.Pen
Imports SolidBrush = Microsoft.VisualBasic.Imaging.SolidBrush

Namespace Rendering

    ''' <summary>
    ''' 视口右上角的色标条叠加层（GDI+ 绘制，随每帧 Render 事件重绘）。
    ''' </summary>
    Public Class ColorbarOverlay

        ''' <summary>色标条标题（场名 + 值域区间）。</summary>
        Public Property Title As String

        ''' <summary>是否可见（数据加载后显示）。</summary>
        Public Property Visible As Boolean = False

        ''' <summary>热图 LUT（256 级，Designer.FromSchema 生成）。</summary>
        Public Property Lut As Color()

        ' 视觉参数（对齐网页版 colorbar-wrap / colorbar 22x220）
        Const BarWidth As Single = 22.0F
        Const BarHeight As Single = 220.0F
        Const Padding As Single = 8.0F
        Const MarginRight As Single = 16.0F
        Const MarginTop As Single = 16.0F

        ReadOnly m_titleFont As New Font("Microsoft YaHei UI", 8.5F)

        ''' <summary>
        ''' 在 Render 事件提供的画布右上角绘制色标条。
        ''' </summary>
        Public Sub Draw(g As IGraphics, viewport As Size)
            If Not Visible OrElse Lut Is Nothing OrElse Lut.Length = 0 Then
                Return
            End If

            Dim title As String = If(Me.Title, "")
            Dim titleSize As SizeF = g.MeasureString(title, m_titleFont)
            Dim titleHeight As Single = If(String.IsNullOrEmpty(title), 0.0F, titleSize.Height + 4.0F)

            Dim cardW As Single = BarWidth + Padding * 2.0F
            Dim cardH As Single = titleHeight + BarHeight + Padding * 2.0F
            Dim cardX As Single = viewport.Width - cardW - MarginRight
            Dim cardY As Single = MarginTop

            ' ---- 半透明白色卡片背景 + 细边框 ----
            Using background As New SolidBrush(Color.FromArgb(217, 255, 255, 255))
                Call g.FillRectangle(background, cardX, cardY, cardW, cardH)
            End Using

            Using border As New Pen(Color.FromArgb(226, 232, 240), 1.0F)
                Call g.DrawRectangle(border, cardX, cardY, cardW, cardH)
            End Using

            Dim barX As Single = cardX + Padding
            Dim barY As Single = cardY + Padding + titleHeight

            ' ---- 垂直渐变色条：上 = max，下 = min ----
            Dim n As Integer = Lut.Length
            Dim rowH As Single = BarHeight / n

            For y As Integer = 0 To n - 1
                ' y=0 是画面最上端 → 对应 t=1（最大值）
                Dim t As Double = 1.0 - CDbl(y) / (n - 1)
                Dim li As Integer = CInt(t * (n - 1))
                Dim c As Color = Lut(li)

                Using brush As New SolidBrush(Color.FromArgb(c.R, c.G, c.B))
                    Call g.FillRectangle(brush, barX, barY + y * rowH, BarWidth, rowH + 0.75F)
                End Using
            Next

            Using border As New Pen(Color.FromArgb(148, 163, 184), 1.0F)
                Call g.DrawRectangle(border, barX, barY, BarWidth, BarHeight)
            End Using

            ' ---- 标题 ----
            If Not String.IsNullOrEmpty(title) Then
                Using textBrush As New SolidBrush(Color.FromArgb(71, 85, 105))
                    Call g.DrawString(title, m_titleFont, textBrush,
                                      cardX + Padding + Math.Max(0.0F, (cardW - Padding * 2.0F - titleSize.Width) / 2.0F),
                                      cardY + Padding)
                End Using
            End If
        End Sub

    End Class

End Namespace
