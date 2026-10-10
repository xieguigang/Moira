Imports System.IO
Imports System.Text
Imports Microsoft.VisualBasic.ApplicationServices
Imports FluidBox.Simulation
Imports std = System.Math

Namespace My
    ' The following events are available for MyApplication:
    ' Startup: Raised when the application starts, before the startup form is created.
    ' Shutdown: Raised after all application forms are closed.
    ' UnhandledException: Raised if the application encounters an unhandled exception.
    ' StartupNextInstance: Raised when launching a single-instance application
    ' and the application is already active.
    ' NetworkAvailabilityChanged: Raised when the network connection is connected or
    ' disconnected.
    Partial Friend Class MyApplication

        ''' <summary>
        ''' the headless smoke test of the engine, it builds a box, steps the
        ''' solver a few times and writes what it measured into a log file:
        ''' ``FluidBox.exe --selftest --particles 200000 --out selftest.log``
        ''' </summary>
        Private Function TrySelfTest(args As System.Collections.ObjectModel.ReadOnlyCollection(Of String)) As Boolean
            If Not args.Contains("--selftest") Then
                Return False
            End If

            Dim particles As Integer = 200_000
            Dim out As String = "selftest.log"

            For i As Integer = 0 To args.Count - 1
                If args(i) = "--particles" AndAlso i + 1 < args.Count Then
                    particles = CInt(args(i + 1))
                ElseIf args(i) = "--out" AndAlso i + 1 < args.Count Then
                    out = args(i + 1)
                End If
            Next

            Dim log As New StringBuilder()

            Try
                Dim watch = Diagnostics.Stopwatch.StartNew()
                Dim sim As New FluidBoxSim(
                    boxSize:=100.0F,
                    fillFraction:=2.0F / 3.0F,
                    particleCount:=particles,
                    enableGpu:=True,
                    progress:=Sub(msg, fraction)
                                  Call log.AppendLine($"[init] {msg} ({CInt(fraction * 100)}%)")
                              End Sub)

                watch.Stop()

                Call log.AppendLine($"particles    : {sim.ParticleCount}")
                Call log.AppendLine($"spacing      : {sim.Spacing:F6}")
                Call log.AppendLine($"smoothing h  : {sim.SmoothingRadius:F6}")
                Call log.AppendLine($"liquid height: {sim.LiquidHeight:F4}")
                Call log.AppendLine($"backend      : {sim.BackendName}")
                Call log.AppendLine($"gpu enabled  : {sim.IsGpuEnabled}")
                Call log.AppendLine($"gpu error    : {sim.GpuError}")
                Call log.AppendLine($"init         : {watch.Elapsed.TotalMilliseconds:F0} ms")

                For i As Integer = 1 To 3
                    Call sim.StepOnce()
                    Call log.AppendLine($"step {i}      : {sim.LastStepMs:F0} ms, substeps={sim.LastSubSteps}, error={sim.LastError}")
                Next

                Dim mapper As New SpeedHeatMapper()
                Call mapper.EnsureCapacity(std.Min(sim.ParticleCount, 200_000))
                Call mapper.SetTransform(New Single() {1, 0, 0, 0, 1, 0, 0, 0, 1},
                                         sim.BoxSize / 2.0F, sim.BoxSize / 2.0F, sim.BoxSize / 2.0F)
                Call mapper.Build(sim.ReadSnapshot(), sim.ParticleCount, std.Min(sim.ParticleCount, 200_000))

                Call log.AppendLine($"mapped       : {mapper.Count} points, stride={mapper.Stride}")
                Call log.AppendLine($"speed range  : {mapper.MinSpeed:F4} .. {mapper.MaxSpeed:F4}")
                Call log.AppendLine($"palette      : {mapper.SchemeName()}, {mapper.Colors(256).Length} colors")
                Call log.AppendLine("SELFTEST OK")
            Catch ex As Exception
                Call log.AppendLine("SELFTEST FAILED")
                Call log.AppendLine(ex.ToString())
            End Try

            Call File.WriteAllText(out, log.ToString())

            Return True
        End Function

        Private Sub MyApplication_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            Try
                If TrySelfTest(Me.CommandLineArgs) Then
                    e.Cancel = True
                End If
            Catch ex As Exception
                Call File.WriteAllText("selftest.log", "SELFTEST CRASHED" & vbCrLf & ex.ToString())
                e.Cancel = True
            End Try
        End Sub

        Private Sub MyApplication_UnhandledException(sender As Object, e As UnhandledExceptionEventArgs) Handles Me.UnhandledException
            Try
                Call File.WriteAllText("fluidbox-crash.log", e.Exception.ToString())
            Catch
            End Try
        End Sub
    End Class
End Namespace
