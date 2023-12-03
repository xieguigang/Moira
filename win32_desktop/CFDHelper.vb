﻿Imports CFD
Imports CFD_clr
Imports Microsoft.VisualBasic.Imaging.Drawing2D.Colors

Public Class CFDHelper

    Public Property DrawFrameData As FrameTypes
    Public Property Colors As ScalerPalette = ScalerPalette.FlexImaging
    Public Property ColorLevels As Integer = 255
    Public Property TrIQ As Double = 0.85
    Public Property enableTrIQ As Boolean = True
    Public Property TracerSpeedLevel As Double = 25
    Public Property RefreshRate As Integer
        Get
            Return 1000 / timer.Interval
        End Get
        Set(value As Integer)
            timer.Interval = 1000 / value
        End Set
    End Property

    Public ReadOnly Property dimension As Size
        Get
            Return dims
        End Get
    End Property

    Dim timer As Timer
    Dim dims As Size

    Public Sub New(timer As Timer)
        Me.timer = timer
    End Sub

    Public Sub SetParameters(pars As SetupParameters)
        dims = New Size(pars.dims(0), pars.dims(1))
    End Sub
End Class

