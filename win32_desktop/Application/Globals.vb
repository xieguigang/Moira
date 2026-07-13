Imports BackgroundHost
Imports CFD_clr
Imports CFD_win32.My
Imports Galaxy.Workbench
Imports Microsoft.VisualBasic.Drawing
Imports Microsoft.VisualBasic.Net

Module Globals

    Public ReadOnly toolkit As New toolCFDParameters
    Public ReadOnly settings As Settings

    Sub New()
        settings = Settings.LoadSettings
        SkiaDriver.Register()
    End Sub

    Public Function CreateService() As CFDTcpProtocols
        Dim port As Integer = RscriptHelper.CreateCFDServer(await:=1500, log:=AddressOf CommonRuntime.StatusMessage)
        Dim client As New CFDTcpProtocols(New IPEndPoint("127.0.0.1", port))
        Return client
    End Function

    Public Current As CFDTcpProtocols

    Public Sub SetupBackendUI()
        AddHandler Ribbon.ButtonSimulationStart.ExecuteEvent, Sub() Call Current.start()
        AddHandler Ribbon.ButtonSimulationPause.ExecuteEvent, Sub() Call Current.pause()
    End Sub

End Module
