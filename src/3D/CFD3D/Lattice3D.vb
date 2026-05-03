' ============================================================================
' Lattice3D.vb - D3Q19 Lattice Definition
'
' Defines the 19 velocity vectors, weights, and opposite direction mappings
' for the D3Q19 lattice used in 3D Lattice Boltzmann simulations.
'
' Velocity layout:
'   0:  ( 0, 0, 0)  rest
'   1:  ( 1, 0, 0)  2:  (-1, 0, 0)   face x
'   3:  ( 0, 1, 0)  4:  ( 0,-1, 0)   face y
'   5:  ( 0, 0, 1)  6:  ( 0, 0,-1)   face z
'   7:  ( 1, 1, 0)  8:  (-1,-1, 0)   edge xy
'   9:  ( 1,-1, 0) 10:  (-1, 1, 0)   edge xy
'  11:  ( 1, 0, 1) 12:  (-1, 0,-1)   edge xz
'  13:  ( 1, 0,-1) 14:  (-1, 0, 1)   edge xz
'  15:  ( 0, 1, 1) 16:  ( 0,-1,-1)   edge yz
'  17:  ( 0, 1,-1) 18:  ( 0,-1, 1)   edge yz
' ============================================================================

Public Module Lattice3D

    ''' <summary>
    ''' Velocity vector components for each of the 19 directions.
    ''' cx(i), cy(i), cz(i) give the x, y, z components of direction i.
    ''' </summary>
    Public ReadOnly cx As Integer() = {
        0, 1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0
    }
    Public ReadOnly cy As Integer() = {
        0, 0, 0, 1, -1, 0, 0, 1, -1, -1, 1, 0, 0, 0, 0, 1, -1, 1, -1
    }
    Public ReadOnly cz As Integer() = {
        0, 0, 0, 0, 0, 1, -1, 0, 0, 0, 0, 1, -1, -1, 1, 1, -1, -1, 1
    }

    ''' <summary>
    ''' Lattice weights for each direction.
    ''' </summary>
    Public ReadOnly w As Double() = {
        Globals3D.w0,
        Globals3D.w1, Globals3D.w1, Globals3D.w1, Globals3D.w1, Globals3D.w1, Globals3D.w1,
        Globals3D.w2, Globals3D.w2, Globals3D.w2, Globals3D.w2,
        Globals3D.w2, Globals3D.w2, Globals3D.w2, Globals3D.w2,
        Globals3D.w2, Globals3D.w2, Globals3D.w2, Globals3D.w2
    }

    ''' <summary>
    ''' Opposite direction index for each direction.
    ''' opp(i) gives the index of the direction opposite to direction i.
    ''' </summary>
    Public ReadOnly opp As Integer() = {
        0,
        2, 1, 4, 3, 6, 5,
        8, 7, 10, 9,
        12, 11, 14, 13,
        16, 15, 18, 17
    }

    ''' <summary>
    ''' Compute the flat array index for a 3D grid coordinate.
    ''' Layout: index = x + y * NX + z * NX * NY
    ''' </summary>
    <Runtime.CompilerServices.Extension>
    Public Function Idx(x As Integer, y As Integer, z As Integer, NX As Integer, NY As Integer) As Integer
        Return x + y * NX + z * NX * NY
    End Function

End Module
