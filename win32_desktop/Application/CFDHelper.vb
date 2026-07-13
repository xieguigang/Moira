Imports System.ComponentModel
Imports CFD
Imports CFD_clr
Imports Microsoft.VisualBasic.ComponentModel.Triggers
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors
Imports Microsoft.VisualBasic.Net

Public Class CFDHelper

    <Category(CFDRender)> Public Property DrawFrameData As FrameTypes
    <Category(CFDRender)> Public Property Colors As ScalerPalette = ScalerPalette.FlexImaging
    <Category(CFDRender)> Public Property ColorLevels As Integer = 255
    <Category(CFDRender)> Public Property TrIQ As Double = 0.85
    <Category(CFDRender)> Public Property enableTrIQ As Boolean = True
    <Category(CFDRender)> Public Property TracerSpeedLevel As Double = 25

    <Category(CFDRender)>
    Public Property RefreshRate As Integer
        Get
            Return 1000 / timer.Interval
        End Get
        Set(value As Integer)
            timer.Interval = 1000 / value
        End Set
    End Property

    <Category(CFDConfig)>
    Public ReadOnly Property dimension As Size
        Get
            Return dims
        End Get
    End Property

    Const CFDServer As String = "Moria CFD Service"
    Const CFDRender As String = "Render"
    Const CFDConfig As String = "Configuration"

    <Category(CFDServer)> Public ReadOnly Property host As String
    <Category(CFDServer)> Public ReadOnly Property port As Integer
    <Category(CFDServer)> Public ReadOnly Property session_storage As String

    Dim timer As ITimer
    Dim dims As Size

    Public Sub New(timer As ITimer)
        Me.timer = timer
    End Sub

    Public Sub SetParameters(pars As SetupParameters)
        dims = New Size(pars.dims(0), pars.dims(1))
        _session_storage = pars.storagefile.GetFullPath
    End Sub

    Public Sub SetBackend(endpoint As IPEndPoint)
        _host = endpoint.ipAddress
        _port = endpoint.port
    End Sub
End Class

