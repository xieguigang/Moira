' ============================================================================
' FluidDynamics3D.vb - D3Q19 Lattice Boltzmann Method Engine
'
' Implements a complete 3D LBM solver using the D3Q19 lattice with:
'   - BGK (Bhatnagar-Gross-Krook) single-relaxation-time collision
'   - Pull-based streaming
'   - Full-way bounce-back for stationary walls
'   - Modified bounce-back for moving walls (impeller)
'   - Open/closed boundary conditions
'
' Based on the original 2D D2Q9 implementation by Jean Flaherty,
' extended to 3D with the D3Q19 lattice model.
' ============================================================================

Imports System.Threading.Tasks

Public Class FluidDynamics3D
    Inherits Simulation3D

    ' Temporary arrays for streaming (pull scheme)
    Private fTemp As Double()() = Nothing

    Sub New(nx As Integer, ny As Integer, nz As Integer)
        MyBase.New(nx, ny, nz)
        AllocateTempArrays()
    End Sub

    Private Sub AllocateTempArrays()
        ReDim fTemp(18)
        For i As Integer = 0 To 18
            ReDim fTemp(i)(NTotal - 1)
        Next
    End Sub

    Protected Overrides Sub close()
        Erase f
        Erase fTemp
        Erase m_rho
        Erase m_ux
        Erase m_uy
        Erase m_uz
        Erase m_speed2
        Erase m_barrier
        Erase m_wallUx
        Erase m_wallUy
        Erase m_wallUz
    End Sub

    ' ========================================================================
    '  RESET - Initialize equilibrium distribution
    ' ========================================================================
    Public Overrides Sub reset()
        Dim v0 As Double = 0.0  ' initial velocity = 0 (fluid at rest)

        For z As Integer = 0 To NZ - 1
            For y As Integer = 0 To NY - 1
                For x As Integer = 0 To NX - 1
                    Dim idx As Integer = MyBase.Idx(x, y, z)

                    If m_barrier(idx) Then
                        ' Barrier cells: zero distributions
                        For i As Integer = 0 To 18
                            f(i)(idx) = 0.0
                        Next
                        m_rho(idx) = 0.0
                        m_ux(idx) = 0.0
                        m_uy(idx) = 0.0
                        m_uz(idx) = 0.0
                        m_speed2(idx) = 0.0
                    Else
                        ' Fluid cells: equilibrium at rest
                        Dim rho0 As Double = 1.0
                        m_rho(idx) = rho0
                        m_ux(idx) = 0.0
                        m_uy(idx) = 0.0
                        m_uz(idx) = 0.0
                        m_speed2(idx) = 0.0

                        For i As Integer = 0 To 18
                            f(i)(idx) = Equilibrium(i, rho0, 0.0, 0.0, 0.0)
                        Next
                    End If
                Next
            Next
        Next

        Console.WriteLine($"[CFD3D] Initialized D3Q19 simulation: {NX}x{NY}x{NZ} = {NTotal} cells")
        Console.WriteLine($"[CFD3D] Viscosity = {viscosity}, Omega = {omega}")
    End Sub

    ' ========================================================================
    '  EQUILIBRIUM - Compute equilibrium distribution for direction i
    ' ========================================================================
    ''' <summary>
    ''' Compute the equilibrium distribution function for direction i.
    ''' f_i^eq = w_i * rho * [1 + 3*(c.u) + 9/2*(c.u)^2 - 3/2*(u.u)]
    ''' </summary>
    Private Function Equilibrium(i As Integer, rho As Double, ux As Double, uy As Double, uz As Double) As Double
        Dim cu As Double = Lattice3D.cx(i) * ux + Lattice3D.cy(i) * uy + Lattice3D.cz(i) * uz
        Dim u2 As Double = ux * ux + uy * uy + uz * uz
        Return Lattice3D.w(i) * rho * (1.0 + 3.0 * cu + 4.5 * cu * cu - 1.5 * u2)
    End Function

    ' ========================================================================
    '  ADVANCE - One simulation time step
    ' ========================================================================
    Public Overrides Sub advance()
        Collide()
        Stream()
        BounceBack()
        ComputeMacroscopic()
        ClampFields()
    End Sub

    ' ========================================================================
    '  COLLIDE - BGK collision operator (parallel over z-slices)
    ' ========================================================================
    Private Sub Collide()
        Dim om As Double = omega

        Parallel.For(0, NZ, Sub(z)
            For y As Integer = 0 To NY - 1
                For x As Integer = 0 To NX - 1
                                        Dim idx As Integer = MyBase.Idx(x, y, z)

                                        If m_barrier(idx) Then Return

                    ' Compute macroscopic quantities from current distributions
                    Dim rho As Double = 0.0
                    Dim ux As Double = 0.0
                    Dim uy As Double = 0.0
                    Dim uz As Double = 0.0

                    For i As Integer = 0 To 18
                        rho += f(i)(idx)
                        ux += Lattice3D.cx(i) * f(i)(idx)
                        uy += Lattice3D.cy(i) * f(i)(idx)
                        uz += Lattice3D.cz(i) * f(i)(idx)
                    Next

                    ' Avoid division by zero
                    If rho > 0.0001 Then
                        ux /= rho
                        uy /= rho
                        uz /= rho
                    Else
                        ux = 0.0
                        uy = 0.0
                        uz = 0.0
                        rho = 1.0
                    End If

                    ' BGK collision: f_i = f_i - omega * (f_i - f_i^eq)
                    For i As Integer = 0 To 18
                        Dim feq As Double = Equilibrium(i, rho, ux, uy, uz)
                        f(i)(idx) += om * (feq - f(i)(idx))
                    Next
                Next
            Next
        End Sub)
    End Sub

    ' ========================================================================
    '  STREAM - Pull-based streaming (parallel over z-slices)
    ' ========================================================================
    Private Sub Stream()
        ' Copy current state to temp
        For i As Integer = 0 To 18
            Array.Copy(f(i), fTemp(i), NTotal)
        Next

        ' Pull scheme: f_i(x) = fTemp_i(x - c_i)
        Parallel.For(0, NZ, Sub(z)
            For y As Integer = 0 To NY - 1
                For x As Integer = 0 To NX - 1
                                        Dim idx As Integer = MyBase.Idx(x, y, z)

                                        For i As Integer = 0 To 18
                        Dim sx As Integer = x - Lattice3D.cx(i)
                        Dim sy As Integer = y - Lattice3D.cy(i)
                        Dim sz As Integer = z - Lattice3D.cz(i)

                        ' Boundary handling: out-of-bounds cells keep their value
                        ' (will be overwritten by boundary conditions)
                        If sx >= 0 AndAlso sx < NX AndAlso
                           sy >= 0 AndAlso sy < NY AndAlso
                           sz >= 0 AndAlso sz < NZ Then
                                                f(i)(idx) = fTemp(i)(MyBase.Idx(sx, sy, sz))
                                            End If
                        ' else: f(i)(idx) retains its post-collision value
                    Next
                Next
            Next
        End Sub)
    End Sub

    ' ========================================================================
    '  BOUNCE-BACK - Reflect distributions at barrier cells
    ' ========================================================================
    Private Sub BounceBack()
        Parallel.For(0, NZ, Sub(z)
            For y As Integer = 0 To NY - 1
                For x As Integer = 0 To NX - 1
                                        Dim idx As Integer = MyBase.Idx(x, y, z)

                                        If Not m_barrier(idx) Then Return

                    ' Check if this barrier has a non-zero wall velocity (moving wall)
                    Dim hasWallVelocity As Boolean = (Math.Abs(m_wallUx(idx)) > 1.0E-12) OrElse
                                                     (Math.Abs(m_wallUy(idx)) > 1.0E-12) OrElse
                                                     (Math.Abs(m_wallUz(idx)) > 1.0E-12)

                    If hasWallVelocity Then
                        ' Modified bounce-back for moving walls:
                        ' f_opp(x) = f_i(x) + 2 * w_i * rho * 3 * (c_i . u_wall)
                        Dim rho0 As Double = 1.0
                        For i As Integer = 0 To 18
                            Dim oi As Integer = Lattice3D.opp(i)
                            Dim cu As Double = Lattice3D.cx(i) * m_wallUx(idx) +
                                              Lattice3D.cy(i) * m_wallUy(idx) +
                                              Lattice3D.cz(i) * m_wallUz(idx)
                            Dim correction As Double = 2.0 * Lattice3D.w(i) * rho0 * 3.0 * cu
                            f(oi)(idx) = f(i)(idx) + correction
                        Next
                    Else
                        ' Standard bounce-back for stationary walls:
                        ' f_opp(x) = f_i(x)
                        For i As Integer = 0 To 18
                            Dim oi As Integer = Lattice3D.opp(i)
                            f(oi)(idx) = f(i)(idx)
                        Next
                    End If
                Next
            Next
        End Sub)
    End Sub

    ' ========================================================================
    '  COMPUTE MACROSCOPIC - Calculate density and velocity from distributions
    ' ========================================================================
    Private Sub ComputeMacroscopic()
        Parallel.For(0, NZ, Sub(z)
            For y As Integer = 0 To NY - 1
                For x As Integer = 0 To NX - 1
                                        Dim idx As Integer = MyBase.Idx(x, y, z)

                                        If m_barrier(idx) Then
                        m_rho(idx) = 0.0
                        m_ux(idx) = 0.0
                        m_uy(idx) = 0.0
                        m_uz(idx) = 0.0
                        m_speed2(idx) = 0.0
                        Return
                    End If

                    Dim rho As Double = 0.0
                    Dim ux As Double = 0.0
                    Dim uy As Double = 0.0
                    Dim uz As Double = 0.0

                    For i As Integer = 0 To 18
                        rho += f(i)(idx)
                        ux += Lattice3D.cx(i) * f(i)(idx)
                        uy += Lattice3D.cy(i) * f(i)(idx)
                        uz += Lattice3D.cz(i) * f(i)(idx)
                    Next

                    If rho > 0.0001 Then
                        ux /= rho
                        uy /= rho
                        uz /= rho
                    Else
                        ux = 0.0
                        uy = 0.0
                        uz = 0.0
                        rho = 1.0
                    End If

                    m_rho(idx) = rho
                    m_ux(idx) = ux
                    m_uy(idx) = uy
                    m_uz(idx) = uz
                    m_speed2(idx) = ux * ux + uy * uy + uz * uz
                Next
            Next
        End Sub)
    End Sub

    ' ========================================================================
    '  CLAMP FIELDS - Prevent numerical blowup
    ' ========================================================================
    Private Sub ClampFields()
        Const maxVal As Double = 10.0
        Const minVal As Double = -10.0

        For i As Integer = 0 To 18
            ClampArray(f(i), minVal, maxVal)
        Next
        ClampArray(m_rho, -1.0, 2.0)
        ClampArray(m_ux, minVal, maxVal)
        ClampArray(m_uy, minVal, maxVal)
        ClampArray(m_uz, minVal, maxVal)
        ClampArray(m_speed2, 0.0, maxVal)
    End Sub

End Class
