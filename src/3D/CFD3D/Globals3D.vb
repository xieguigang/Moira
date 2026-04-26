' ============================================================================
' Globals3D.vb - D3Q19 Lattice Boltzmann Method Constants
'
' D3Q19 lattice: 19 velocity directions in 3D space
' Used for 3D computational fluid dynamics simulation
' ============================================================================

Public Module Globals3D

    ' D3Q19 lattice weights
    ' Direction 0 (rest):           w = 1/3
    ' Directions 1-6 (face):        w = 1/18
    ' Directions 7-18 (edge):       w = 1/36
    Public Const w0 As Double = 1.0 / 3.0
    Public Const w1 As Double = 1.0 / 18.0
    Public Const w2 As Double = 1.0 / 36.0

    ' Speed of sound squared in lattice units: cs^2 = 1/3
    Public Const cs2 As Double = 1.0 / 3.0

    ' Number of lattice velocities
    Public Const Q As Integer = 19

End Module
