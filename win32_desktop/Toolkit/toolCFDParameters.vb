Imports Microsoft.VisualStudio.WinForms.Docking

Public Class toolCFDParameters

    Dim callback As frmCFDCanvas

    Public ReadOnly Property pars As CFDHelper

    Public Sub SetTarget(callback As frmCFDCanvas)
        Dim helper As New CFDHelper(callback.timer1)

        helper.SetBackend(callback.CFD.EndPoint)
        helper.SetParameters(callback.setup)

        _pars = helper

        PropertyGrid1.SelectedObject = helper
        PropertyGrid1.Refresh()

        Me.callback = callback
    End Sub

    Private Sub toolCFDParameters_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        e.Cancel = True
        DockState = DockState.Hidden
    End Sub

    Private Sub PropertyGrid1_PropertyValueChanged(s As Object, e As PropertyValueChangedEventArgs) Handles PropertyGrid1.PropertyValueChanged
        Select Case e.ChangedItem.Label
            Case NameOf(CFDHelper.DrawFrameData)
                ' do nothing
            Case NameOf(CFDHelper.Colors), NameOf(CFDHelper.ColorLevels)
                Call callback.UpdatePalette()
        End Select

        e.ChangedItem.Select()
    End Sub

    Private Sub toolCFDParameters_Load(sender As Object, e As EventArgs) Handles Me.Load
        TabText = "Simulator Parameters"
    End Sub
End Class