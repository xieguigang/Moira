' ============================================================================
' DataAdapter3D.vb - Data Access Wrapper for 3D CFD Simulation
'
' Provides convenient access to simulation data fields
' with both full-array and per-cell access methods.
' ============================================================================

Public Class DataAdapter3D

    Public ReadOnly CFD As FluidDynamics3D

    Public Property viscosity As Double
        Get
            Return CFD.viscosity
        End Get
        Set(value As Double)
            CFD.viscosity = value
        End Set
    End Property

    Sub New(cfd As FluidDynamics3D)
        Me.CFD = cfd
    End Sub

    ' --- Full array accessors ---
    Public Function GetSpeed() As Double()
        Return CFD.speed2
    End Function

    Public Function GetDensity() As Double()
        Return CFD.rho
    End Function

    Public Function GetXVel() As Double()
        Return CFD.ux
    End Function

    Public Function GetYVel() As Double()
        Return CFD.uy
    End Function

    Public Function GetZVel() As Double()
        Return CFD.uz
    End Function

    Public Function GetBarrier() As Boolean()
        Return CFD.barrier
    End Function

    ' --- Per-cell accessors ---
    Public Function GetSpeed(x As Integer, y As Integer, z As Integer) As Double
        Return CFD.speed2(CFD.Idx(x, y, z))
    End Function

    Public Function GetDensity(x As Integer, y As Integer, z As Integer) As Double
        Return CFD.rho(CFD.Idx(x, y, z))
    End Function

    Public Function GetXVel(x As Integer, y As Integer, z As Integer) As Double
        Return CFD.ux(CFD.Idx(x, y, z))
    End Function

    Public Function GetYVel(x As Integer, y As Integer, z As Integer) As Double
        Return CFD.uy(CFD.Idx(x, y, z))
    End Function

    Public Function GetZVel(x As Integer, y As Integer, z As Integer) As Double
        Return CFD.uz(CFD.Idx(x, y, z))
    End Function

    Public Function GetBarrier(x As Integer, y As Integer, z As Integer) As Boolean
        Return CFD.barrier(CFD.Idx(x, y, z))
    End Function

    Public Sub SetBarrier(x As Integer, y As Integer, z As Integer, flag As Boolean)
        CFD.barrier(CFD.Idx(x, y, z)) = flag
    End Sub

    Public Sub SetWallVelocity(x As Integer, y As Integer, z As Integer, wx As Double, wy As Double, wz As Double)
        Dim idx As Integer = CFD.Idx(x, y, z)
        CFD.wallUx(idx) = wx
        CFD.wallUy(idx) = wy
        CFD.wallUz(idx) = wz
    End Sub

End Class
