' /********************************************************************************/
'
'   EulerianVectorMath.vb
'
'   Eulerian（FVM）求解器内部的三维向量运算辅助。
'
'   为什么需要这一层：
'       CFDEngine.Eulerian.EulerianVertex 是一个来自外部程序集的只读几何结构
'       （仅提供 X/Y/Z、+、−、×标量、Dot、Cross、Length），无法在其上添成员。
'       FVM 离散还需要一批"向量代数"操作（单位化、按分量加权、面积向量、三点法向等），
'       因此在本工程内以扩展方法 + 静态函数的形式提供。
'
'   混合精度契约：全部为 double（几何与系数组装属于非热点）。
'
' /********************************************************************************/

Imports System
Imports System.Runtime.CompilerServices
Imports std = System.Math

Namespace Eulerian

    ''' <summary>Eulerian 求解器专用的三维向量运算（对 CFDEngine 的 EulerianVertex 扩展）。</summary>
    Public Module EulerianVectorMath

#Region "扩展方法"

        ''' <summary>单位化；零向量返回零向量。</summary>
        <Extension>
        Public Function Normalized(v As Moira.CFDEngine.Eulerian.EulerianVertex) As Moira.CFDEngine.Eulerian.EulerianVertex
            Dim l As Double = v.Length

            If l <= 1.0E-300 Then
                Return New Moira.CFDEngine.Eulerian.EulerianVertex()
            End If

            Return New Moira.CFDEngine.Eulerian.EulerianVertex(v.X / l, v.Y / l, v.Z / l)
        End Function

        ''' <summary>向量模长（与 Length 等价，提供与标量语境一致的命名）。</summary>
        <Extension>
        Public Function Magnitude(v As Moira.CFDEngine.Eulerian.EulerianVertex) As Double
            Return v.Length
        End Function

        ''' <summary>点积。</summary>
        <Extension>
        Public Function DotWith(a As Moira.CFDEngine.Eulerian.EulerianVertex,
                                b As Moira.CFDEngine.Eulerian.EulerianVertex) As Double

            Return a.X * b.X + a.Y * b.Y + a.Z * b.Z
        End Function

        ''' <summary>叉积。</summary>
        <Extension>
        Public Function CrossWith(a As Moira.CFDEngine.Eulerian.EulerianVertex,
                                  b As Moira.CFDEngine.Eulerian.EulerianVertex) As Moira.CFDEngine.Eulerian.EulerianVertex

            Return New Moira.CFDEngine.Eulerian.EulerianVertex(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X)
        End Function

#End Region

#Region "静态工具"

        ''' <summary>按分量线性组合 a·wa + b·wb。</summary>
        Public Function Linear(a As Moira.CFDEngine.Eulerian.EulerianVertex, wa As Double,
                               b As Moira.CFDEngine.Eulerian.EulerianVertex, wb As Double) As Moira.CFDEngine.Eulerian.EulerianVertex

            Return New Moira.CFDEngine.Eulerian.EulerianVertex(
                a.X * wa + b.X * wb,
                a.Y * wa + b.Y * wb,
                a.Z * wa + b.Z * wb)
        End Function

        ''' <summary>取 X 分量。</summary>
        Public Function X(v As Moira.CFDEngine.Eulerian.EulerianVertex) As Double
            Return v.X
        End Function

        ''' <summary>取 Y 分量。</summary>
        Public Function Y(v As Moira.CFDEngine.Eulerian.EulerianVertex) As Double
            Return v.Y
        End Function

        ''' <summary>取 Z 分量。</summary>
        Public Function Z(v As Moira.CFDEngine.Eulerian.EulerianVertex) As Double
            Return v.Z
        End Function

        ''' <summary>按分量索引取值（0=X, 1=Y, 2=Z）。</summary>
        Public Function Component(v As Moira.CFDEngine.Eulerian.EulerianVertex, index As Integer) As Double
            Select Case index
                Case 0
                    Return v.X
                Case 1
                    Return v.Y
                Case Else
                    Return v.Z
            End Select
        End Function

        ''' <summary>由分量数组（长度 ≥ 3）构造向量。</summary>
        Public Function FromComponents(values As Double(), Optional offset As Integer = 0) As Moira.CFDEngine.Eulerian.EulerianVertex
            Return New Moira.CFDEngine.Eulerian.EulerianVertex(values(offset), values(offset + 1), values(offset + 2))
        End Function

        ''' <summary>三角形 (a, b, c) 的面积向量（= 2×面积，法向由右手定则给出）。</summary>
        Public Function TriangleAreaVector(a As Moira.CFDEngine.Eulerian.EulerianVertex,
                                           b As Moira.CFDEngine.Eulerian.EulerianVertex,
                                           c As Moira.CFDEngine.Eulerian.EulerianVertex) As Moira.CFDEngine.Eulerian.EulerianVertex

            Return (b - a).CrossWith(c - a)
        End Function

        ''' <summary>
        ''' 多边形（按 <paramref name="loop0"/> 绕序）的面积向量：
        ''' Newell 公式的向量形式，法向由绕序决定。
        ''' </summary>
        Public Function PolygonAreaVector(loop0 As IList(Of Moira.CFDEngine.Eulerian.EulerianVertex)) As Moira.CFDEngine.Eulerian.EulerianVertex
            Dim nx As Double = 0
            Dim ny As Double = 0
            Dim nz As Double = 0
            Dim n As Integer = loop0.Count

            For i As Integer = 0 To n - 1
                Dim p As Moira.CFDEngine.Eulerian.EulerianVertex = loop0(i)
                Dim q As Moira.CFDEngine.Eulerian.EulerianVertex = loop0((i + 1) Mod n)

                nx += (p.Y - q.Y) * (p.Z + q.Z)
                ny += (p.Z - q.Z) * (p.X + q.X)
                nz += (p.X - q.X) * (p.Y + q.Y)
            Next

            Return New Moira.CFDEngine.Eulerian.EulerianVertex(0.5 * nx, 0.5 * ny, 0.5 * nz)
        End Function

        ''' <summary>两点距离。</summary>
        Public Function Distance(a As Moira.CFDEngine.Eulerian.EulerianVertex,
                                 b As Moira.CFDEngine.Eulerian.EulerianVertex) As Double

            Return (b - a).Length
        End Function

        ''' <summary>把 x ∈ [lo, hi] 钳制。</summary>
        Public Function Clamp(x As Double, lo As Double, hi As Double) As Double
            If x < lo Then Return lo
            If x > hi Then Return hi
            Return x
        End Function

        ''' <summary>对 3 维矢量做模长（长度 = 单元数，分量主序输入）。</summary>
        Public Function VectorMagnitude(components As Double(), cellCount As Integer) As Double()
            Dim r(cellCount - 1) As Double
            Dim ox As Integer = 0
            Dim oy As Integer = cellCount
            Dim oz As Integer = 2 * cellCount

            For i As Integer = 0 To cellCount - 1
                Dim x As Double = components(ox + i)
                Dim y As Double = components(oy + i)
                Dim z As Double = components(oz + i)

                r(i) = std.Sqrt(x * x + y * y + z * z)
            Next

            Return r
        End Function

#End Region

    End Module

End Namespace
