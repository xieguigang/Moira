Imports Galaxy.Workbench
Imports Microsoft.VisualStudio.WinForms.Docking

Public Class PageCFDPlayer

    Dim panelLeft As PanelPlayerLeft
    Dim panelRight As PanelPlayerRight

    Private Sub frmCFDPlayer_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        panelLeft = New PanelPlayerLeft With {.player = Me}
        panelRight = New PanelPlayerRight With {.player = Me}

        Call CommonRuntime.RegisterToolWindow(panelLeft, DockState.DockLeft)
        Call CommonRuntime.RegisterToolWindow(panelRight, DockState.DockRight)
    End Sub
End Class