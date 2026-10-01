Option Strict On
Option Explicit On


Namespace CityBlender


    ''' <summary>
    ''' Deterministic 2-D value noise with fBm (fractional Brownian motion),
    ''' built on an integer lattice hash. Given the same seed the sequence is
    ''' fully reproducible on every platform — a "city seed" is enough to
    ''' regenerate an identical model.
    ''' </summary>
    Public NotInheritable Class Noise

        Private ReadOnly _seed As Long

        ''' <param name="seed">Any integer; identical seeds give identical noise.</param>
        Public Sub New(seed As Integer)
            _seed = CLng(seed)
        End Sub

        ''' <summary>
        ''' Single-octave lattice value noise in [0, 1). C1-continuous thanks to
        ''' smoothstep interpolation between the four lattice corners.
        ''' </summary>
        Public Function Sample(x As Double, y As Double) As Double
            Dim ix = CInt(Math.Floor(x))
            Dim iy = CInt(Math.Floor(y))

            Dim fx = x - ix
            Dim fy = y - iy
            fx = fx * fx * (3.0 - 2.0 * fx)
            fy = fy * fy * (3.0 - 2.0 * fy)

            Dim v00 = Hash(ix, iy)
            Dim v10 = Hash(ix + 1, iy)
            Dim v01 = Hash(ix, iy + 1)
            Dim v11 = Hash(ix + 1, iy + 1)

            Return (v00 * (1.0 - fx) + v10 * fx) * (1.0 - fy) +
               (v01 * (1.0 - fx) + v11 * fx) * fy
        End Function

        ''' <summary>
        ''' Fractional Brownian motion: octaves octaves of value noise with
        ''' doubling frequency and halving amplitude. Output in [0, 1).
        ''' </summary>
        Public Function Fbm(x As Double, y As Double, octaves As Integer) As Double
            If octaves < 1 Then octaves = 1
            Dim total = 0.0
            Dim amplitude = 1.0
            Dim frequency = 1.0
            Dim norm = 0.0

            For o = 1 To octaves
                total += amplitude * Sample(x * frequency, y * frequency)
                norm += amplitude
                amplitude *= 0.5
                frequency *= 2.0
            Next

            Return total / norm
        End Function

        ''' <summary>Integer lattice hash mapped to [0, 1).</summary>
        Private Function Hash(ix As Integer, iy As Integer) As Double
            ' All arithmetic in Int64 with explicit masking — no reliance on
            ' unchecked integer wrap-around, safe with Option Strict / overflow checks.
            Dim h As Long = (ix * 374761393L) Xor (iy * 668265263L) Xor (_seed * 1442695041L)
            h = h And &HFFFFFFFFL
            h = (h Xor (h >> 13)) * 1274126177L And &HFFFFFFFFL
            h = h Xor (h >> 16)
            Return (h And &HFFFFFFFFL) / 4294967295.0
        End Function

    End Class
End Namespace