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
        AddHandler ribbon.ButtonCFDPlay.ExecuteEvent, Sub() Call CommonRuntime.ShowSingleDocument(Of PageCFDPlayer)()
        ' CFD 工具箱：三个命令行工具（EulerianMesh / Voxelizer / WindTunnel）的图形化封装
        AddHandler ribbon.ButtonEulerianMesh.ExecuteEvent, Sub() Call ShowTool(Of FormEulerianMesh)()
        AddHandler ribbon.ButtonVoxelizer.ExecuteEvent, Sub() Call ShowTool(Of FormVoxelizer)()
        AddHandler ribbon.ButtonWindTunnel.ExecuteEvent, Sub() Call ShowTool(Of FormWindTunnel)()
    End Sub

    ''' <summary>已打开的工具窗体（单实例，键为窗体类型）。</summary>
    Private ReadOnly toolWindows As New Dictionary(Of Type, Form)()

    ''' <summary>
    ''' 以非模态方式单实例打开一个 CFD 工具窗体；已打开则直接激活。
    ''' </summary>
    Private Sub ShowTool(Of TForm As {Form, New})()
        Dim key As Type = GetType(TForm)
        Dim window As Form = Nothing

        If toolWindows.TryGetValue(key, window) AndAlso window IsNot Nothing AndAlso Not window.IsDisposed Then
            If window.WindowState = FormWindowState.Minimized Then
                window.WindowState = FormWindowState.Normal
            End If

            Call window.Activate()
            Return
        End If

        window = New TForm()
        toolWindows(key) = window

        AddHandler window.FormClosed, Sub() Call toolWindows.Remove(key)
        Call window.Show(DirectCast(CommonRuntime.AppHost, Form))
    End Sub

    Private Sub CreateNewSimulation()
        Dim wizard As New FormProjectWizard()

        If wizard.ShowDialog() = DialogResult.OK Then
            Using folder As New FolderBrowserDialog With {
                .ShowNewFolderButton = True
            }
                If folder.ShowDialog = DialogResult.OK Then

                End If
            End Using
        End If
    End Sub
End Module
