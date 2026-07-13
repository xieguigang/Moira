Imports CFD_win32.RibbonLib.Controls
Imports Galaxy.Workbench
Imports Galaxy.Workbench.CommonDialogs

Module RibbonMenu

    Public ReadOnly Property Ribbon As RibbonItems

    Public Sub Setup(ribbon As RibbonItems)
        _Ribbon = ribbon

        AddHandler ribbon.ButtonAbout.ExecuteEvent, Sub() Call New SplashScreen() With {.ShowAbout = True}.Show()
        AddHandler ribbon.ButtonAppExit.ExecuteEvent, Sub() Call DirectCast(CommonRuntime.AppHost, Form).Close()
        AddHandler ribbon.FileNew.ExecuteEvent, Sub() Call CreateNewSimulation()
        AddHandler ribbon.ButtonLicense.ExecuteEvent, Sub() Call InputDialog.Input(Of FormLicense)()
        AddHandler ribbon.Button3DModelTool.ExecuteEvent, Sub() Call CommonRuntime.ShowSingleDocument(Of frm3DModelTool)()
        AddHandler ribbon.ButtonCFDPlay.ExecuteEvent, Sub() Call CommonRuntime.ShowSingleDocument(Of frmCFDPlayer)()
    End Sub

    Private Sub CreateNewSimulation()
        Dim wizard As New FormProjectWizard()

        If wizard.ShowDialog() = DialogResult.OK Then
            Using folder As New FolderBrowserDialog With {
                .ShowNewFolderButton = True
            }
                If folder.ShowDialog = DialogResult.OK Then
                    Dim pars = wizard.GetParameters(folder.SelectedPath)
                    Dim CFD As New frmCFDCanvas With {.setup = pars}

                    Call CommonRuntime.ShowDocument(CFD)
                End If
            End Using
        End If
    End Sub
End Module
