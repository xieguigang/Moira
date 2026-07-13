Imports CFD_win32.RibbonLib.Controls
Imports Galaxy.Workbench
Imports Galaxy.Workbench.CommonDialogs

Module RibbonMenu

    Public ReadOnly Property Ribbon As RibbonItems

    Public Sub Setup(ribbon As RibbonItems)
        _Ribbon = ribbon

        AddHandler ribbonItems.ButtonAbout.ExecuteEvent, Sub() Call New SplashScreen() With {.ShowAbout = True}.Show()
        AddHandler ribbonItems.ButtonAppExit.ExecuteEvent, Sub() Call Me.Close()
        AddHandler ribbonItems.FileNew.ExecuteEvent, Sub() Call CreateNewSimulation()
        AddHandler ribbonItems.ButtonLicense.ExecuteEvent, Sub() Call InputDialog.Input(Of FormLicense)()
        AddHandler ribbonItems.Button3DModelTool.ExecuteEvent, Sub() Call CommonRuntime.ShowSingleDocument(Of frm3DModelTool)()
        AddHandler ribbonItems.ButtonCFDPlay.ExecuteEvent, Sub() Call CommonRuntime.ShowSingleDocument(Of frmCFDPlayer)()
    End Sub
End Module
