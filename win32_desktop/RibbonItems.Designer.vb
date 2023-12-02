'------------------------------------------------------------------------------
' <auto-generated>
'     This code was generated by a tool.
'     Runtime Version:
'
'     Changes to this file may cause incorrect behavior and will be lost if
'     the code is regenerated.
' </auto-generated>
'------------------------------------------------------------------------------

Imports System
Imports RibbonLib
Imports RibbonLib.Controls
Imports RibbonLib.Interop

Namespace RibbonLib.Controls
    Partial Class RibbonItems
        Private Class Cmd
            Public Const cmdFileOpen As UInteger = 8
            Public Const cmdButtonAbout As UInteger = 12
            Public Const cmdButtonAppExit As UInteger = 13
            Public Const cmdTabSimulationPage As UInteger = 15
            Public Const cmdGroupSimulation As UInteger = 16
            Public Const cmdPageSimulation As UInteger = 17
            Public Const cmdButtonSimulationStart As UInteger = 18
            Public Const cmdButtonSimulationPause As UInteger = 19
            Public Const cmdButtonSimulationStop As UInteger = 20
            Public Const cmdMenu2 As UInteger = 10
            Public Const cmdCheckShowTracer As UInteger = 9
            Public Const cmdCheckShowFlowLine As UInteger = 11
            Public Const cmdTabSimulationOperations As UInteger = 22
            Public Const cmdButtonClearBarrier As UInteger = 6
            Public Const cmdButtonReset As UInteger = 2
            Public Const cmdCheckDrawBarrier As UInteger = 7
            Public Const cmdTabApplicationMain As UInteger = 21
            Public Const cmdCommandGroup As UInteger = 4
            Public Const cmdGroupApp As UInteger = 14
        End Class

        ' ContextPopup CommandName

        Private _ribbon As Ribbon
        Public ReadOnly Property Ribbon As Ribbon
            Get
                Return _ribbon
            End Get
        End Property
        Private _FileOpen As RibbonButton
        Public ReadOnly Property FileOpen As RibbonButton
            Get
                Return _FileOpen
            End Get
        End Property
        Private _ButtonAbout As RibbonButton
        Public ReadOnly Property ButtonAbout As RibbonButton
            Get
                Return _ButtonAbout
            End Get
        End Property
        Private _ButtonAppExit As RibbonButton
        Public ReadOnly Property ButtonAppExit As RibbonButton
            Get
                Return _ButtonAppExit
            End Get
        End Property
        Private _TabSimulationPage As RibbonTabGroup
        Public ReadOnly Property TabSimulationPage As RibbonTabGroup
            Get
                Return _TabSimulationPage
            End Get
        End Property
        Private _GroupSimulation As RibbonTab
        Public ReadOnly Property GroupSimulation As RibbonTab
            Get
                Return _GroupSimulation
            End Get
        End Property
        Private _PageSimulation As RibbonGroup
        Public ReadOnly Property PageSimulation As RibbonGroup
            Get
                Return _PageSimulation
            End Get
        End Property
        Private _ButtonSimulationStart As RibbonButton
        Public ReadOnly Property ButtonSimulationStart As RibbonButton
            Get
                Return _ButtonSimulationStart
            End Get
        End Property
        Private _ButtonSimulationPause As RibbonButton
        Public ReadOnly Property ButtonSimulationPause As RibbonButton
            Get
                Return _ButtonSimulationPause
            End Get
        End Property
        Private _ButtonSimulationStop As RibbonButton
        Public ReadOnly Property ButtonSimulationStop As RibbonButton
            Get
                Return _ButtonSimulationStop
            End Get
        End Property
        Private _Menu2 As RibbonGroup
        Public ReadOnly Property Menu2 As RibbonGroup
            Get
                Return _Menu2
            End Get
        End Property
        Private _CheckShowTracer As RibbonToggleButton
        Public ReadOnly Property CheckShowTracer As RibbonToggleButton
            Get
                Return _CheckShowTracer
            End Get
        End Property
        Private _CheckShowFlowLine As RibbonToggleButton
        Public ReadOnly Property CheckShowFlowLine As RibbonToggleButton
            Get
                Return _CheckShowFlowLine
            End Get
        End Property
        Private _TabSimulationOperations As RibbonGroup
        Public ReadOnly Property TabSimulationOperations As RibbonGroup
            Get
                Return _TabSimulationOperations
            End Get
        End Property
        Private _ButtonClearBarrier As RibbonButton
        Public ReadOnly Property ButtonClearBarrier As RibbonButton
            Get
                Return _ButtonClearBarrier
            End Get
        End Property
        Private _ButtonReset As RibbonButton
        Public ReadOnly Property ButtonReset As RibbonButton
            Get
                Return _ButtonReset
            End Get
        End Property
        Private _CheckDrawBarrier As RibbonToggleButton
        Public ReadOnly Property CheckDrawBarrier As RibbonToggleButton
            Get
                Return _CheckDrawBarrier
            End Get
        End Property
        Private _TabApplicationMain As RibbonTab
        Public ReadOnly Property TabApplicationMain As RibbonTab
            Get
                Return _TabApplicationMain
            End Get
        End Property
        Private _CommandGroup As RibbonGroup
        Public ReadOnly Property CommandGroup As RibbonGroup
            Get
                Return _CommandGroup
            End Get
        End Property
        Private _GroupApp As RibbonGroup
        Public ReadOnly Property GroupApp As RibbonGroup
            Get
                Return _GroupApp
            End Get
        End Property

        Public Sub New(ByVal ribbon As Ribbon)
            If ribbon Is Nothing Then
                Throw New ArgumentNullException(NameOf(ribbon), "Parameter is Nothing")
            End If
            _ribbon = ribbon
            _FileOpen = New RibbonButton(_ribbon, Cmd.cmdFileOpen)
            _ButtonAbout = New RibbonButton(_ribbon, Cmd.cmdButtonAbout)
            _ButtonAppExit = New RibbonButton(_ribbon, Cmd.cmdButtonAppExit)
            _TabSimulationPage = New RibbonTabGroup(_ribbon, Cmd.cmdTabSimulationPage)
            _GroupSimulation = New RibbonTab(_ribbon, Cmd.cmdGroupSimulation)
            _PageSimulation = New RibbonGroup(_ribbon, Cmd.cmdPageSimulation)
            _ButtonSimulationStart = New RibbonButton(_ribbon, Cmd.cmdButtonSimulationStart)
            _ButtonSimulationPause = New RibbonButton(_ribbon, Cmd.cmdButtonSimulationPause)
            _ButtonSimulationStop = New RibbonButton(_ribbon, Cmd.cmdButtonSimulationStop)
            _Menu2 = New RibbonGroup(_ribbon, Cmd.cmdMenu2)
            _CheckShowTracer = New RibbonToggleButton(_ribbon, Cmd.cmdCheckShowTracer)
            _CheckShowFlowLine = New RibbonToggleButton(_ribbon, Cmd.cmdCheckShowFlowLine)
            _TabSimulationOperations = New RibbonGroup(_ribbon, Cmd.cmdTabSimulationOperations)
            _ButtonClearBarrier = New RibbonButton(_ribbon, Cmd.cmdButtonClearBarrier)
            _ButtonReset = New RibbonButton(_ribbon, Cmd.cmdButtonReset)
            _CheckDrawBarrier = New RibbonToggleButton(_ribbon, Cmd.cmdCheckDrawBarrier)
            _TabApplicationMain = New RibbonTab(_ribbon, Cmd.cmdTabApplicationMain)
            _CommandGroup = New RibbonGroup(_ribbon, Cmd.cmdCommandGroup)
            _GroupApp = New RibbonGroup(_ribbon, Cmd.cmdGroupApp)
        End Sub

    End Class
End Namespace
