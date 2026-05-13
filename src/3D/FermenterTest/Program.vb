' ============================================================================
' Program.vb - Fermenter CFD Simulation Test
'
' Builds a 3D fermenter geometry with a rotating Rushton turbine impeller
' and runs a Lattice Boltzmann Method (D3Q19) simulation of the fluid flow.
'
' Geometry:
'   - Cylindrical tank (approximated on Cartesian grid)
'   - Central shaft from top to impeller
'   - 4-blade Rushton turbine impeller at ~30% tank height
'   - 4 wall baffles
'
' The impeller rotates, driving fluid circulation inside the tank.
' Results are saved as binary files for visualization in the companion
' TypeScript frontend (CFD3D-Viewer).
'
' Usage:
'   dotnet run -- [output_dir] [iterations] [snapshot_interval]
'
' Default: output_dir = "./results", iterations = 2000, snapshot_interval = 100
' ============================================================================

Imports CFD3D
Imports CFD3D.Storage

Public Module Program

    ' ========================================================================
    '  SIMULATION PARAMETERS
    ' ========================================================================

    ' Grid dimensions (keep moderate for testing; increase for production)
    Const NX As Integer = 50   ' x-axis (horizontal)
    Const NY As Integer = 50   ' y-axis (horizontal)
    Const NZ As Integer = 60   ' z-axis (vertical, 0=bottom)

    ' Tank geometry (in lattice units)
    Const TANK_RADIUS As Double = 22.0       ' Tank inner radius
    Const SHAFT_RADIUS As Double = 1.5        ' Central shaft radius
    Const IMPELLER_Z As Integer = 18          ' Impeller vertical position (from bottom)
    Const IMPELLER_HEIGHT As Integer = 2      ' Impeller blade thickness (z-direction)
    Const IMPELLER_INNER_R As Double = 2.0    ' Hub radius
    Const IMPELLER_OUTER_R As Double = 14.0   ' Blade tip radius
    Const BLADE_THICKNESS As Double = 1.0     ' Blade half-thickness

    ' Baffle geometry
    Const BAFFLE_WIDTH As Double = 4.0        ' Baffle extends inward from wall
    Const BAFFLE_THICKNESS As Double = 1.0    ' Baffle half-thickness

    ' Physics
    Const VISCOSITY As Double = 0.02          ' Kinematic viscosity (lattice units)
    Const ANGULAR_VELOCITY As Double = 0.008  ' Impeller angular velocity (rad/step)

    ' Simulation control
    Const DEFAULT_ITERATIONS As Integer = 2000
    Const DEFAULT_SNAPSHOT As Integer = 100

    ' ========================================================================
    '  MAIN ENTRY POINT
    ' ========================================================================

    Public Sub Main(args As String())

        ' Parse command-line arguments
        Dim outputDir As String = "Z:/results"
        Dim maxIterations As Integer = If(args.Length > 1, CInt(args(1)), DEFAULT_ITERATIONS)
        Dim snapshotInterval As Integer = If(args.Length > 2, CInt(args(2)), DEFAULT_SNAPSHOT)

        Console.WriteLine("============================================================")
        Console.WriteLine("  3D Fermenter CFD Simulation - D3Q19 Lattice Boltzmann")
        Console.WriteLine("============================================================")
        Console.WriteLine($"  Grid:       {NX} x {NY} x {NZ} = {NX * NY * NZ} cells")
        Console.WriteLine($"  Tank R:     {TANK_RADIUS}")
        Console.WriteLine($"  Impeller:   z={IMPELLER_Z}, r=[{IMPELLER_INNER_R}, {IMPELLER_OUTER_R}]")
        Console.WriteLine($"  Viscosity:  {VISCOSITY}")
        Console.WriteLine($"  Omega:      {ANGULAR_VELOCITY} rad/step")
        Console.WriteLine($"  Iterations: {maxIterations}")
        Console.WriteLine($"  Snapshot:   every {snapshotInterval} steps")
        Console.WriteLine($"  Output:     {outputDir}")
        Console.WriteLine("============================================================")

        ' --- Create simulation ---
        Dim cfd As New FluidDynamics3D(NX, NY, NZ)
        cfd.viscosity = VISCOSITY

        ' --- Build geometry ---
        Dim centerX As Double = NX / 2.0
        Dim centerY As Double = NY / 2.0
        Dim impellerAngle As Double = 0.0

        BuildGeometry(cfd, centerX, centerY, impellerAngle)

        ' --- Initialize ---
        cfd.reset()

        ' --- Create frame writer ---
        Dim writer As New FrameWriter3D(
            outputDirectory:=outputDir,
            nx:=NX, ny:=NY, nz:=NZ,
            visc:=VISCOSITY,
            desc:="3D Fermenter with rotating Rushton turbine impeller (D3Q19 LBM)"
        )

        ' Save initial barrier geometry
        writer.SaveBarrier(cfd.barrier)

        ' --- Run simulation loop ---
        Dim t0 As Date = Now
        Dim frameNum As Integer = 0
        Dim reportInterval As Integer = Math.Max(1, maxIterations \ 20)

        Console.WriteLine()
        Console.WriteLine("[Simulation] Starting...")

        For [step] As Integer = 1 To maxIterations

            ' Update impeller rotation
            impellerAngle += ANGULAR_VELOCITY

            ' Rebuild geometry with new impeller angle
            BuildGeometry(cfd, centerX, centerY, impellerAngle)

            ' Advance simulation one time step
            cfd.advance()

            ' Save snapshot
            If [step] Mod snapshotInterval = 0 Then
                frameNum += 1
                writer.AddFrame(cfd.ux, cfd.uy, cfd.uz, cfd.rho, cfd.speed2)
            End If

            ' Progress report
            If [step] Mod reportInterval = 0 Then
                Dim elapsed As Double = (Now - t0).TotalSeconds
                Dim speed As Double = [step] / elapsed
                Dim remaining As Double = (maxIterations - [step]) / speed
                Dim pct As Double = [step] / maxIterations * 100.0

                Console.WriteLine($"  [{[step]}/{maxIterations}] {pct:F1}%  " &
                                  $"{speed:F0} it/s  " &
                                  $"ETA: {FormatTime(remaining)}")
            End If

            ' NaN check (every 500 steps)
            If [step] Mod 500 = 0 Then
                If Not cfd.CheckNaN() Then
                    Console.WriteLine($"  [WARNING] NaN detected at step {[step]}!")
                End If
            End If
        Next

        ' --- Finalize ---
        writer.FinalizeMetadata(maxIterations, snapshotInterval)
        writer.Dispose()

        Dim totalTime As Double = (Now - t0).TotalSeconds
        Console.WriteLine()
        Console.WriteLine("============================================================")
        Console.WriteLine($"  Simulation complete!")
        Console.WriteLine($"  Total time:  {FormatTime(totalTime)}")
        Console.WriteLine($"  Frames saved: {frameNum}")
        Console.WriteLine($"  Output dir:  {IO.Path.GetFullPath(outputDir)}")
        Console.WriteLine("============================================================")

        cfd.Dispose()
    End Sub

    ' ========================================================================
    '  BUILD GEOMETRY - Construct the fermenter barrier field
    ' ========================================================================

    Private Sub BuildGeometry(cfd As FluidDynamics3D, centerX As Double, centerY As Double, angle As Double)

        ' Clear all barriers and wall velocities
        GeometryBuilder3D.ClearAll(cfd)

        ' 1. Cylindrical tank walls
        GeometryBuilder3D.BuildCylindricalTank(
            cfd:=cfd,
            centerX:=centerX, centerY:=centerY,
            radius:=TANK_RADIUS
        )

        ' 2. Central shaft (from impeller to top)
        GeometryBuilder3D.BuildShaft(
            cfd:=cfd,
            centerX:=centerX, centerY:=centerY,
            shaftRadius:=SHAFT_RADIUS,
            zBottom:=IMPELLER_Z,
            zTop:=cfd.NZ - 1
        )

        ' 3. Rotating impeller (4-blade Rushton turbine)
        GeometryBuilder3D.BuildImpeller(
            cfd:=cfd,
            centerX:=centerX, centerY:=centerY,
            innerRadius:=IMPELLER_INNER_R,
            outerRadius:=IMPELLER_OUTER_R,
            bladeThickness:=BLADE_THICKNESS,
            zPosition:=IMPELLER_Z,
            zHeight:=IMPELLER_HEIGHT,
            angle:=angle,
            angularVelocity:=ANGULAR_VELOCITY
        )

        ' 4. Wall baffles (4 baffles at 90-degree intervals)
        GeometryBuilder3D.BuildBaffles(
            cfd:=cfd,
            centerX:=centerX, centerY:=centerY,
            tankRadius:=TANK_RADIUS,
            baffleWidth:=BAFFLE_WIDTH,
            baffleThickness:=BAFFLE_THICKNESS
        )

    End Sub

    ' ========================================================================
    '  UTILITY
    ' ========================================================================

    Private Function FormatTime(seconds As Double) As String
        If seconds < 60 Then
            Return $"{seconds:F0}s"
        ElseIf seconds < 3600 Then
            Return $"{Int(seconds \ 60)}m {Int(seconds Mod 60)}s"
        Else
            Return $"{Int(seconds \ 3600)}h {Int((seconds Mod 3600) \ 60)}m"
        End If
    End Function

End Module
