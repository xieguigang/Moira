' ============================================================================
' GeometryBuilder3D.vb - 3D Geometry Construction Utilities
'
' Provides helper methods for building common CFD geometries:
'   - Cylindrical tank
'   - Central shaft
'   - Rushton turbine impeller (4-blade)
'   - Baffles
' ============================================================================

Public Module GeometryBuilder3D

    ''' <summary>
    ''' Build a cylindrical tank barrier.
    ''' Cells outside the cylinder radius are marked as barriers.
    ''' </summary>
    Public Sub BuildCylindricalTank(
        cfd As FluidDynamics3D,
        centerX As Double, centerY As Double,
        radius As Double,
        Optional wallThickness As Integer = 1,
        Optional bottom As Integer = 0,
        Optional top As Integer = -1)

        Dim nzTop As Integer = If(top < 0, cfd.NZ - 1, top)

        For z As Integer = bottom To nzTop
            For y As Integer = 0 To cfd.NY - 1
                For x As Integer = 0 To cfd.NX - 1
                    Dim dx As Double = x - centerX
                    Dim dy As Double = y - centerY
                    Dim dist As Double = Math.Sqrt(dx * dx + dy * dy)

                    ' Mark cells outside the cylinder as barriers
                    If dist > radius Then
                        cfd.barrier(cfd.Idx(x, y, z)) = True
                    End If

                    ' Mark the bottom and top lids
                    If z = bottom OrElse z = nzTop Then
                        If dist <= radius Then
                            cfd.barrier(cfd.Idx(x, y, z)) = True
                        End If
                    End If
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' Build a central shaft (vertical cylinder along z-axis).
    ''' </summary>
    Public Sub BuildShaft(
        cfd As FluidDynamics3D,
        centerX As Double, centerY As Double,
        shaftRadius As Double,
        Optional zBottom As Integer = 0,
        Optional zTop As Integer = -1)

        Dim nzTop As Integer = If(zTop < 0, cfd.NZ - 1, zTop)

        For z As Integer = zBottom To nzTop
            For y As Integer = 0 To cfd.NY - 1
                For x As Integer = 0 To cfd.NX - 1
                    Dim dx As Double = x - centerX
                    Dim dy As Double = y - centerY
                    Dim dist As Double = Math.Sqrt(dx * dx + dy * dy)

                    If dist <= shaftRadius Then
                        cfd.barrier(cfd.Idx(x, y, z)) = True
                    End If
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' Build a Rushton turbine impeller with 4 flat blades.
    ''' The impeller rotates around the z-axis passing through (centerX, centerY).
    ''' </summary>
    ''' <param name="angle">Current rotation angle in radians</param>
    ''' <param name="angularVelocity">Angular velocity in radians per time step</param>
    Public Sub BuildImpeller(
        cfd As FluidDynamics3D,
        centerX As Double, centerY As Double,
        innerRadius As Double, outerRadius As Double,
        bladeThickness As Double,
        zPosition As Integer, zHeight As Integer,
        angle As Double,
        angularVelocity As Double)

        ' Disk (hub) at the impeller center
        For y As Integer = 0 To cfd.NY - 1
            For x As Integer = 0 To cfd.NX - 1
                Dim dx As Double = x - centerX
                Dim dy As Double = y - centerY
                Dim dist As Double = Math.Sqrt(dx * dx + dy * dy)

                If dist <= innerRadius Then
                    For dz As Integer = 0 To zHeight - 1
                        Dim z As Integer = zPosition + dz
                        If z >= 0 AndAlso z < cfd.NZ Then
                            cfd.barrier(cfd.Idx(x, y, z)) = True
                        End If
                    Next
                End If
            Next
        Next

        ' 4 blades at 90-degree intervals
        For blade As Integer = 0 To 3
            Dim bladeAngle As Double = angle + blade * Math.PI / 2.0
            Dim cosA As Double = Math.Cos(bladeAngle)
            Dim sinA As Double = Math.Sin(bladeAngle)

            For y As Integer = 0 To cfd.NY - 1
                For x As Integer = 0 To cfd.NX - 1
                    ' Transform to impeller-local coordinates
                    Dim dx As Double = x - centerX
                    Dim dy As Double = y - centerY

                    ' Project onto blade direction (along blade) and perpendicular
                    Dim along As Double = dx * cosA + dy * sinA
                    Dim perp As Double = -dx * sinA + dy * cosA

                    ' Check if cell is within the blade
                    If along >= innerRadius AndAlso along <= outerRadius AndAlso
                       Math.Abs(perp) <= bladeThickness Then

                        For dz As Integer = 0 To zHeight - 1
                            Dim z As Integer = zPosition + dz
                            If z >= 0 AndAlso z < cfd.NZ Then
                                Dim idx As Integer = cfd.Idx(x, y, z)
                                cfd.barrier(idx) = True

                                ' Set moving wall velocity for this blade cell
                                ' v = omega x r (cross product for rotation around z-axis)
                                ' v_x = -omega * (y - centerY)
                                ' v_y =  omega * (x - centerX)
                                ' v_z =  0
                                cfd.wallUx(idx) = -angularVelocity * dy
                                cfd.wallUy(idx) = angularVelocity * dx
                                cfd.wallUz(idx) = 0.0
                            End If
                        Next
                    End If
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' Build 4 baffles on the tank wall (standard fermenter configuration).
    ''' Baffles are flat plates attached to the tank wall, extending inward.
    ''' </summary>
    Public Sub BuildBaffles(
        cfd As FluidDynamics3D,
        centerX As Double, centerY As Double,
        tankRadius As Double,
        baffleWidth As Double,
        baffleThickness As Double,
        Optional zBottom As Integer = 0,
        Optional zTop As Integer = -1)

        Dim nzTop As Integer = If(zTop < 0, cfd.NZ - 1, zTop)

        ' 4 baffles at 0, 90, 180, 270 degrees
        For baffle As Integer = 0 To 3
            Dim angle As Double = baffle * Math.PI / 2.0
            Dim cosA As Double = Math.Cos(angle)
            Dim sinA As Double = Math.Sin(angle)

            For z As Integer = zBottom To nzTop
                For y As Integer = 0 To cfd.NY - 1
                    For x As Integer = 0 To cfd.NX - 1
                        Dim dx As Double = x - centerX
                        Dim dy As Double = y - centerY

                        ' Project onto baffle direction and perpendicular
                        Dim along As Double = dx * cosA + dy * sinA
                        Dim perp As Double = -dx * sinA + dy * cosA

                        ' Baffle extends from tank wall inward
                        If along >= (tankRadius - baffleWidth) AndAlso along <= tankRadius AndAlso
                           Math.Abs(perp) <= baffleThickness Then
                            cfd.barrier(cfd.Idx(x, y, z)) = True
                        End If
                    Next
                Next
            Next
        Next
    End Sub

    ''' <summary>
    ''' Clear all barrier and wall velocity data.
    ''' </summary>
    Public Sub ClearAll(cfd As FluidDynamics3D)
        For i As Integer = 0 To cfd.NTotal - 1
            cfd.barrier(i) = False
            cfd.wallUx(i) = 0.0
            cfd.wallUy(i) = 0.0
            cfd.wallUz(i) = 0.0
        Next
    End Sub

End Module
