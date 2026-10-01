Option Strict On
Option Explicit On

''' <summary>Small math helpers used across the city generator.</summary>
Public Module MathUtil

    ''' <summary>Clamps a double into [min, max].</summary>
    Public Function Clamp(v As Double, min As Double, max As Double) As Double
        If v < min Then Return min
        If v > max Then Return max
        Return v
    End Function

    ''' <summary>Linear interpolation.</summary>
    Public Function Lerp(a As Double, b As Double, t As Double) As Double
        Return a + (b - a) * t
    End Function

    ''' <summary>Hermite smoothstep, 0 at t=0 and 1 at t=1.</summary>
    Public Function SmoothStep(t As Double) As Double
        t = Clamp(t, 0.0, 1.0)
        Return t * t * (3.0 - 2.0 * t)
    End Function

End Module
