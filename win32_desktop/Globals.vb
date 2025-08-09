Imports BackgroundHost
Imports CFD_clr
Imports CFD_win32.My
Imports CFD_win32.RibbonLib.Controls
Imports Microsoft.VisualBasic.Drawing
Imports Microsoft.VisualBasic.Net
Imports WeifenLuo.WinFormsUI.Docking

Module Globals

    Public ribbonItems As RibbonItems
    Public main As FormMain
    Public dockPanel As DockPanel
    Public ReadOnly toolkit As New toolCFDParameters
    Public ReadOnly settings As Settings

    Sub New()
        settings = Settings.LoadSettings
        SkiaDriver.Register()
    End Sub

    Public Sub Message(str As String)
        main.Invoke(Sub() main.ToolStripStatusLabel2.Text = str)
    End Sub

    Public Function CreateService() As CFDTcpProtocols
        Dim port As Integer = RscriptHelper.CreateCFDServer(await:=1500, log:=AddressOf Message)
        Dim client As New CFDTcpProtocols(New IPEndPoint("127.0.0.1", port))
        Return client
    End Function

    Public current As CFDTcpProtocols

    Public Sub SetupBackendUI()
        AddHandler ribbonItems.ButtonSimulationStart.ExecuteEvent, Sub() Call current.start()
        AddHandler ribbonItems.ButtonSimulationPause.ExecuteEvent, Sub() Call current.pause()

    End Sub

End Module
