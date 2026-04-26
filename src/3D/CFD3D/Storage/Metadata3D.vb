' ============================================================================
' Metadata3D.vb - Simulation Metadata for 3D CFD Results
' ============================================================================

Namespace Storage

    Public Class Metadata3D

        ''' <summary>Grid dimensions (NX, NY, NZ)</summary>
        Public Property dims As Integer()

        ''' <summary>Total number of cells</summary>
        Public Property totalCells As Integer

        ''' <summary>Number of saved frames</summary>
        Public Property totalFrames As Integer

        ''' <summary>Kinematic viscosity used in simulation</summary>
        Public Property viscosity As Double

        ''' <summary>Snapshot interval (iterations between frames)</summary>
        Public Property snapshotInterval As Integer

        ''' <summary>Total iterations performed</summary>
        Public Property totalIterations As Integer

        ''' <summary>Physical description of the simulation</summary>
        Public Property description As String

        ''' <summary>Data fields saved per frame (e.g., "ux", "uy", "uz", "rho")</summary>
        Public Property fields As String()

        ''' <summary>Value ranges for each field across all frames: {min, max}</summary>
        Public Property ranges As Dictionary(Of String, Double())

        Public Sub New()
            ranges = New Dictionary(Of String, Double())
        End Sub

    End Class

End Namespace
