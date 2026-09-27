Imports CFD_win32.My
Imports Microsoft.VisualBasic.Drawing

Module Globals

    Public ReadOnly toolkit As New toolCFDParameters
    Public ReadOnly settings As Settings

    Sub New()
        settings = Settings.LoadSettings
        SkiaDriver.Register()
    End Sub

    Public Sub SetupBackendUI()
        ' AddHandler Ribbon.ButtonSimulationStart.ExecuteEvent, Sub() Call Current.start()
        ' AddHandler Ribbon.ButtonSimulationPause.ExecuteEvent, Sub() Call Current.pause()
    End Sub

End Module
