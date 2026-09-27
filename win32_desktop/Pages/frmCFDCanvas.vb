Imports System.Runtime.CompilerServices
Imports Microsoft.VisualBasic.ComponentModel.Ranges.Model
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Linq
Imports Microsoft.VisualStudio.WinForms.Docking
Imports image = System.Drawing.Image
Imports solidbrush = System.Drawing.SolidBrush

Public Class frmCFDCanvas

    Dim colors As solidbrush()
    Dim offset As New DoubleRange(0, 255)
    Dim drawLine As Boolean = False
    Dim model As image = Nothing

    ReadOnly grays As solidbrush() = Designer _
        .GetColors(ScalerPalette.Gray.Description, 30) _
        .Select(Function(c) New solidbrush(c)) _
        .ToArray
    ReadOnly grayOffset As New DoubleRange(0, 29)

    Private Sub resetCFD()

    End Sub

    Private Sub PictureBox1_MouseUp(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseUp
        drawLine = False
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseDown
        If e.Button = MouseButtons.Left Then
            drawLine = CheckDrawBarrier()
        End If
    End Sub

    Private Function CheckDrawBarrier() As Boolean
        Return Ribbon.CheckDrawBarrier.BooleanValue
    End Function

    Private Function GetCFDPosition() As Point

    End Function

    Private Sub ShowPointInformation()

    End Sub

    Private Sub PictureBox1_MouseMove(sender As Object, e As MouseEventArgs) Handles PictureBox1.MouseMove

    End Sub

    Private Sub frmCFDCanvas_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        AddHandler Ribbon.ButtonReset.ExecuteEvent, Sub() resetCFD()
        AddHandler Ribbon.ButtonClearBarrier.ExecuteEvent, Sub()
                                                               ' CFD.clearBarrier()
                                                           End Sub

        toolkit.Show(DockPanel)
        toolkit.DockState = DockState.DockLeft

        TabText = $"CFD Project - {Now.Year}{Now.Month.ToString.PadLeft(1, "0"c)}{Now.Day.ToString.PadLeft(1, "0"c)}-{App.ElapsedMilliseconds}"

        Call SetCurrent()
        Call ApplyVsTheme(ContextMenuStrip1)



    End Sub

    Private Sub frmCFDCanvas_Activated(sender As Object, e As EventArgs) Handles MyBase.Activated
        SetCurrent()
    End Sub

    Private Sub frmCFDCanvas_LostFocus(sender As Object, e As EventArgs) Handles MyBase.LostFocus
        ' ribbonItems.TabSimulationPage.ContextAvailable = ContextAvailability.NotAvailable
    End Sub

    Private Sub frmCFDCanvas_GotFocus(sender As Object, e As EventArgs) Handles MyBase.GotFocus
        SetCurrent()
    End Sub

    Private Sub SetCurrent()

    End Sub

End Class