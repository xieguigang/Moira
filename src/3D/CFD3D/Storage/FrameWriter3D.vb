' ============================================================================
' FrameWriter3D.vb - Binary Frame Writer for 3D CFD Simulation Results
'
' Output format:
'   output_dir/
'     metadata.json    - Simulation metadata (JSON)
'     barrier.bin      - Barrier mask (1 byte per cell)
'     frame_0001.bin   - Frame data (4 doubles per cell: ux, uy, uz, rho)
'     frame_0002.bin
'     ...
'
' Binary layout per frame (little-endian):
'   For each cell (NX*NY*NZ cells, row-major order x+y*NX+z*NX*NY):
'     ux:  Double (8 bytes)
'     uy:  Double (8 bytes)
'     uz:  Double (8 bytes)
'     rho: Double (8 bytes)
'   Total: NTotal * 32 bytes per frame
' ============================================================================

Imports System.IO
Imports System.Text.Json

Namespace Storage

    Public Class FrameWriter3D
        Implements IDisposable

        Private ReadOnly outputDir As String
        Private ReadOnly metadata As New Metadata3D()
        Private frameCount As Integer = 0
        Private disposedValue As Boolean

        ' Running min/max for each field
        Private rangeUx As Double() = {Double.MaxValue, Double.MinValue}
        Private rangeUy As Double() = {Double.MaxValue, Double.MinValue}
        Private rangeUz As Double() = {Double.MaxValue, Double.MinValue}
        Private rangeRho As Double() = {Double.MaxValue, Double.MinValue}
        Private rangeSpeed As Double() = {Double.MaxValue, Double.MinValue}

        ''' <summary>
        ''' Create a frame writer that saves results to the specified directory.
        ''' </summary>
        Sub New(outputDirectory As String, nx As Integer, ny As Integer, nz As Integer,
                visc As Double, Optional desc As String = "")

            outputDir = outputDirectory
            Directory.CreateDirectory(outputDir)

            metadata.dims = {nx, ny, nz}
            metadata.totalCells = nx * ny * nz
            metadata.viscosity = visc
            metadata.description = desc
            metadata.fields = {"ux", "uy", "uz", "rho", "speed2"}
        End Sub

        ''' <summary>
        ''' Save the barrier mask to a binary file.
        ''' </summary>
        Public Sub SaveBarrier(barrier As Boolean())
            Dim path As String = $"{outputDir}/barrier.bin"
            Dim bytes(barrier.Length - 1) As Byte

            For i As Integer = 0 To barrier.Length - 1
                bytes(i) = If(barrier(i), CByte(1), CByte(0))
            Next

            File.WriteAllBytes(path, bytes)
            Console.WriteLine($"[FrameWriter3D] Saved barrier: {path} ({bytes.Length} bytes)")
        End Sub

        ''' <summary>
        ''' Save one simulation frame (velocity + density fields).
        ''' </summary>
        Public Sub AddFrame(
            ux As Double(), uy As Double(), uz As Double(),
            rho As Double(), speed2 As Double())

            frameCount += 1
            Dim path As String = $"{outputDir}/frame_{frameCount:D4}.bin"

            Using fs As New FileStream(path, FileMode.Create, FileAccess.Write)
                Using bw As New BinaryWriter(fs)
                    For i As Integer = 0 To ux.Length - 1
                        bw.Write(ux(i))
                        bw.Write(uy(i))
                        bw.Write(uz(i))
                        bw.Write(rho(i))
                    Next
                End Using
            End Using

            ' Update running ranges
            UpdateRange(rangeUx, ux)
            UpdateRange(rangeUy, uy)
            UpdateRange(rangeUz, uz)
            UpdateRange(rangeRho, rho)
            UpdateRange(rangeSpeed, speed2)

            Console.WriteLine($"[FrameWriter3D] Saved frame {frameCount}: {path}")
        End Sub

        ''' <summary>
        ''' Set simulation parameters and save final metadata.
        ''' Call this after all frames have been written.
        ''' </summary>
        Public Sub FinalizeMetadata(totalIterations As Integer, snapshotInterval As Integer)
            metadata.totalFrames = frameCount
            metadata.totalIterations = totalIterations
            metadata.snapshotInterval = snapshotInterval

            metadata.ranges = New Dictionary(Of String, Double()) From {
                {"ux", rangeUx},
                {"uy", rangeUy},
                {"uz", rangeUz},
                {"rho", rangeRho},
                {"speed2", rangeSpeed}
            }

            Dim jsonOptions As New JsonSerializerOptions With {
                .WriteIndented = True
            }
            Dim json As String = JsonSerializer.Serialize(metadata, jsonOptions)
            Dim metaPath As String = Path.Combine(outputDir, "metadata.json")
            File.WriteAllText(metaPath, json)

            Console.WriteLine($"[FrameWriter3D] Saved metadata: {metaPath}")
            Console.WriteLine($"[FrameWriter3D] Total frames: {frameCount}, Total iterations: {totalIterations}")
        End Sub

        Private Sub UpdateRange(ByRef range As Double(), data As Double())
            For i As Integer = 0 To data.Length - 1
                If data(i) < range(0) Then range(0) = data(i)
                If data(i) > range(1) Then range(1) = data(i)
            Next
        End Sub

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' Metadata should be finalized by caller
                End If
                disposedValue = True
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub

    End Class

End Namespace
