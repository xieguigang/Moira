' ============================================================================
' Simulation3D.vb - Base Class for 3D LBM Simulation
'
' Provides the fundamental 3D grid structure, array management,
' and abstract interface for LBM simulation subclasses.
' Uses flat 1D arrays with manual 3D indexing for performance.
' ============================================================================

Public MustInherit Class Simulation3D
    Implements IDisposable

    Private disposedValue As Boolean

    ' --- Grid dimensions ---
    Public ReadOnly Property NX As Integer
    Public ReadOnly Property NY As Integer
    Public ReadOnly Property NZ As Integer
    ''' <summary>Total number of cells in the grid</summary>
    Public ReadOnly Property NTotal As Integer

    ' --- Fluid properties ---
    ''' <summary>Kinematic viscosity in lattice units</summary>
    Public Property viscosity As Double = 0.02

    ''' <summary>Relaxation parameter omega = 1 / (3*nu + 0.5)</summary>
    Public ReadOnly Property omega As Double
        Get
            Return 1.0 / (3.0 * viscosity + 0.5)
        End Get
    End Property

    ' --- Distribution functions (19 directions, flat arrays) ---
    ''' <summary>Distribution functions f[direction, flat_index]</summary>
    Protected f As Double()() = Nothing

    ' --- Macroscopic fields (flat arrays) ---
    Protected m_rho As Double() = Nothing
    Protected m_ux As Double() = Nothing
    Protected m_uy As Double() = Nothing
    Protected m_uz As Double() = Nothing
    Protected m_speed2 As Double() = Nothing

    ' --- Barrier mask ---
    ''' <summary>True at cells that are solid barriers</summary>
    Protected m_barrier As Boolean() = Nothing

    ' --- Moving wall velocity (for impeller) ---
    ''' <summary>Wall velocity x-component at each cell</summary>
    Protected m_wallUx As Double() = Nothing
    ''' <summary>Wall velocity y-component at each cell</summary>
    Protected m_wallUy As Double() = Nothing
    ''' <summary>Wall velocity z-component at each cell</summary>
    Protected m_wallUz As Double() = Nothing

    Sub New(nx As Integer, ny As Integer, nz As Integer)
        Me.NX = nx
        Me.NY = ny
        Me.NZ = nz
        NTotal = nx * ny * nz

        AllocateArrays()
    End Sub

    ''' <summary>
    ''' Allocate all working arrays.
    ''' </summary>
    Protected Overridable Sub AllocateArrays()
        ReDim f(18)
        For i As Integer = 0 To 18
            ReDim f(i)(NTotal - 1)
        Next

        ReDim m_rho(NTotal - 1)
        ReDim m_ux(NTotal - 1)
        ReDim m_uy(NTotal - 1)
        ReDim m_uz(NTotal - 1)
        ReDim m_speed2(NTotal - 1)
        ReDim m_barrier(NTotal - 1)
        ReDim m_wallUx(NTotal - 1)
        ReDim m_wallUy(NTotal - 1)
        ReDim m_wallUz(NTotal - 1)
    End Sub

    ' --- Indexing helpers ---
    Protected Function Idx(x As Integer, y As Integer, z As Integer) As Integer
        Return x + y * NX + z * NX * NY
    End Function

    ' --- Public accessors ---
    Public ReadOnly Property rho As Double()
        Get
            Return m_rho
        End Get
    End Property

    Public ReadOnly Property ux As Double()
        Get
            Return m_ux
        End Get
    End Property

    Public ReadOnly Property uy As Double()
        Get
            Return m_uy
        End Get
    End Property

    Public ReadOnly Property uz As Double()
        Get
            Return m_uz
        End Get
    End Property

    Public ReadOnly Property speed2 As Double()
        Get
            Return m_speed2
        End Get
    End Property

    Public ReadOnly Property barrier As Boolean()
        Get
            Return m_barrier
        End Get
    End Property

    Public Property wallUx As Double()
        Get
            Return m_wallUx
        End Get
        Set(value As Double())
            m_wallUx = value
        End Set
    End Property

    Public Property wallUy As Double()
        Get
            Return m_wallUy
        End Get
        Set(value As Double())
            m_wallUy = value
        End Set
    End Property

    Public Property wallUz As Double()
        Get
            Return m_wallUz
        End Get
        Set(value As Double())
            m_wallUz = value
        End Set
    End Property

    ' --- Abstract methods ---
    Public MustOverride Sub reset()
    Public MustOverride Sub advance()
    Protected MustOverride Sub close()

    ' --- Utility ---
    ''' <summary>
    ''' Clamp all values in a flat array to [-maxVal, +maxVal].
    ''' </summary>
    Protected Shared Sub ClampArray(arr As Double(), minVal As Double, maxVal As Double)
        For i As Integer = 0 To arr.Length - 1
            If arr(i) > maxVal Then arr(i) = maxVal
            If arr(i) < minVal Then arr(i) = minVal
        Next
    End Sub

    ''' <summary>
    ''' Check for NaN values in distribution functions.
    ''' </summary>
    Public Function CheckNaN() As Boolean
        For i As Integer = 0 To 18
            For j As Integer = 0 To f(i).Length - 1
                If Double.IsNaN(f(i)(j)) Then
                    Return False
                End If
            Next
        Next
        Return True
    End Function

    ' --- IDisposable ---
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                close()
            End If
            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class
