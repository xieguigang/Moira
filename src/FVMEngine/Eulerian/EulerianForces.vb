' /********************************************************************************/
'
'   EulerianForces.vb
'
'   后处理：气动力 / 力矩与无量纲系数（Cd / Cl / Cm）—— 风洞算例的核心输出。
'
'   力的两个来源（对指定的壁面 patch 积分）：
'       1. 压力力   F_p = −Σ_f p_f · S_f        （S_f 为自流体指向外部的面矢量）
'       2. 黏性力   F_v = +Σ_f τ_w · A_f        （τ_w 为壁面剪应力矢量）
'
'   壁面剪应力 τ_w 的估计与湍流壁面函数保持一致（对数律）：
'       uTau = Cmu^0.25·√k_P           （k_P 为壁面相邻单元的湍动能）
'       τ_w  = ρ·uTau²·t̂               （t̂ = unit(u_P − u_wall) 的切向单位向量）
'   湍流未启用（k 恒 0）时退化为黏性底层估计：
'       τ_w  = ρ·ν·|u_t|/y·t̂
'
'   无量纲系数（参考量由调用方给出，便于与试验/文献对比）：
'       Cd = F_x / (½ ρ_ref U_ref² A_ref)
'       Cl = F_z / (½ ρ_ref U_ref² A_ref)
'       Cm = M_z / (½ ρ_ref U_ref² A_ref L_ref)
'
'   混合精度契约：全部 double（面积分与比值，属精度敏感的非热点路径）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

    ''' <summary>气动力 / 力矩积分结果与无量纲系数。</summary>
    Public Class EulerianForceReport

        ''' <summary>X 方向合力（N）—— 通常为阻力方向（顺来流）。</summary>
        Public Property ForceX As Double

        ''' <summary>Y 方向合力（N）—— 通常为侧向力方向。</summary>
        Public Property ForceY As Double

        ''' <summary>Z 方向合力（N）—— 通常为升力方向。</summary>
        Public Property ForceZ As Double

        ''' <summary>绕 X 轴力矩（N·m）。</summary>
        Public Property MomentX As Double

        ''' <summary>绕 Y 轴力矩（N·m）。</summary>
        Public Property MomentY As Double

        ''' <summary>绕 Z 轴力矩（N·m）—— 通常为偏航力矩。</summary>
        Public Property MomentZ As Double

        ''' <summary>积分面积（m²）。</summary>
        Public Property Area As Double

        ''' <summary>参考面积（m²）。</summary>
        Public Property ReferenceArea As Double = 1.0

        ''' <summary>参考长度（m）。</summary>
        Public Property ReferenceLength As Double = 1.0

        ''' <summary>参考密度（kg/m³）。</summary>
        Public Property ReferenceDensity As Double = 1.225

        ''' <summary>参考速度（m/s）。</summary>
        Public Property ReferenceVelocity As Double = 1.0

        ''' <summary>参与积分的 patch 名。</summary>
        Public Property Patches As String = ""

        ''' <summary>动压 ½ρU²。</summary>
        Public ReadOnly Property DynamicPressure As Double
            Get
                Return 0.5 * ReferenceDensity * ReferenceVelocity * ReferenceVelocity
            End Get
        End Property

        ''' <summary>阻力系数。</summary>
        Public ReadOnly Property Cd As Double
            Get
                Return Normalise(ForceX)
            End Get
        End Property

        ''' <summary>升力系数（Z 向）。</summary>
        Public ReadOnly Property Cl As Double
            Get
                Return Normalise(ForceZ)
            End Get
        End Property

        ''' <summary>侧向力系数（Y 向）。</summary>
        Public ReadOnly Property Cs As Double
            Get
                Return Normalise(ForceY)
            End Get
        End Property

        ''' <summary>偏航力矩系数（绕 Z 轴）。</summary>
        Public ReadOnly Property Cm As Double
            Get
                Dim den As Double = DynamicPressure * ReferenceArea * ReferenceLength

                If std.Abs(den) <= 1.0E-30 Then Return 0

                Return MomentZ / den
            End Get
        End Property

        Private Function Normalise(f As Double) As Double
            Dim den As Double = DynamicPressure * ReferenceArea

            If std.Abs(den) <= 1.0E-30 Then Return 0

            Return f / den
        End Function

        Public Overrides Function ToString() As String
            Return $"forces[{Patches}]: F=({ForceX.ToString("G6", CultureInfo.InvariantCulture)}, " &
                   $"{ForceY.ToString("G6", CultureInfo.InvariantCulture)}, " &
                   $"{ForceZ.ToString("G6", CultureInfo.InvariantCulture)}) N, " &
                   $"Cd={Cd.ToString("F5", CultureInfo.InvariantCulture)}, " &
                   $"Cl={Cl.ToString("F5", CultureInfo.InvariantCulture)}, " &
                   $"Cm={Cm.ToString("F5", CultureInfo.InvariantCulture)}"
        End Function

    End Class

    ''' <summary>气动力积分。</summary>
    Public Module EulerianForces

        ''' <summary>
        ''' 对指定 patch 积分压力力与黏性力。
        ''' </summary>
        ''' <param name="mesh">网格</param>
        ''' <param name="fields">场集合（取 U / p / k）</param>
        ''' <param name="nuEffFace">逐面有效运动粘度 ν_eff（可取自湍流模型；Nothing 时用 nuMolecular）</param>
        ''' <param name="rhoFace">逐面密度</param>
        ''' <param name="nuMolecular">分子运动粘度（nuEffFace 为 Nothing 时使用）</param>
        ''' <param name="patchNames">参与积分的 patch 名（Nothing = 全部 Wall 类型 patch）</param>
        ''' <param name="referenceArea">参考面积</param>
        ''' <param name="referenceLength">参考长度</param>
        ''' <param name="referenceDensity">参考密度</param>
        ''' <param name="referenceVelocity">参考速度</param>
        ''' <param name="momentOrigin">力矩参考点（默认原点 = (0,0,0)）</param>
        Public Function Compute(mesh As EulerianFvMesh,
                                       fields As EulerianFieldSet,
                                       nuEffFace As Double(),
                                       rhoFace As Double(),
                                       nuMolecular As Double,
                                       Optional patchNames As IEnumerable(Of String) = Nothing,
                                       Optional referenceArea As Double = 1.0,
                                       Optional referenceLength As Double = 1.0,
                                       Optional referenceDensity As Double = 1.225,
                                       Optional referenceVelocity As Double = 1.0,
                                       Optional momentOrigin As Moira.CFDEngine.Eulerian.EulerianVertex = Nothing) As EulerianForceReport

            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))
            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Dim u As EulerianField = fields.U
            Dim p As EulerianField = fields.P
            Dim kField As EulerianField = fields.K

            Dim names As HashSet(Of Integer) = Nothing

            If patchNames IsNot Nothing Then
                names = New HashSet(Of Integer)

                For Each nm As String In patchNames
                    Dim pi As Integer = mesh.FindPatch(nm)

                    If pi >= 0 Then
                        Call names.Add(pi)
                    End If
                Next
            End If

            Dim origin As Moira.CFDEngine.Eulerian.EulerianVertex = momentOrigin

            Dim report As New EulerianForceReport With {
                .ReferenceArea = referenceArea,
                .ReferenceLength = referenceLength,
                .ReferenceDensity = referenceDensity,
                .ReferenceVelocity = referenceVelocity}

            Dim used As New List(Of String)
            Dim skippedFaces As Integer = 0

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                If names IsNot Nothing Then
                    If Not names.Contains(pi) Then
                        Continue For
                    End If
                ElseIf geo.PatchType <> EulerianPatchType.Wall Then
                    Continue For
                End If

                used.Add(geo.Name)

                Dim pfU As EulerianPatchField = u.Boundaries(pi)
                Dim pfP As EulerianPatchField = p.Boundaries(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim area As Double = geo.MagSf(k)

                    If area <= 0 Then
                        Continue For
                    End If

                    Dim sf As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k)
                    Dim pB As Double = pfP.Value(k)
                    Dim rho As Double = If(rhoFace IsNot Nothing AndAlso rhoFace.Length > f, rhoFace(f), referenceDensity)

                    ' ---- 压力力：−p·S_f ----
                    Dim fx As Double = -pB * sf.X
                    Dim fy As Double = -pB * sf.Y
                    Dim fz As Double = -pB * sf.Z

                    ' ---- 黏性力：τ_w·A ----
                    Dim ux As Double = pfU.Value(k, 0)
                    Dim uy As Double = If(u.Components >= 2, pfU.Value(k, 1), 0)
                    Dim uz As Double = If(u.Components >= 3, pfU.Value(k, 2), 0)

                    ' 壁面速度（运动壁面时非零）
                    Dim wbX As Double = 0
                    Dim wbY As Double = 0
                    Dim wbZ As Double = 0
                    Dim mw As MovingWallBc = TryCast(pfU.Condition, MovingWallBc)

                    If mw IsNot Nothing Then
                        Call mw.WallVelocity(k, wbX, wbY, wbZ)
                    End If

                    ' 切向相对速度
                    Dim tx As Double = ux - wbX
                    Dim ty As Double = uy - wbY
                    Dim tz As Double = uz - wbZ
                    Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = sf.Normalized()
                    Dim un As Double = tx * nrm.X + ty * nrm.Y + tz * nrm.Z

                    tx -= un * nrm.X
                    ty -= un * nrm.Y
                    tz -= un * nrm.Z

                    Dim tmag As Double = std.Sqrt(tx * tx + ty * ty + tz * tz)

                    If tmag > 1.0E-12 Then
                        Dim uTau As Double = EstimateUTau(kField, own)

                        If uTau <= 0 Then
                            ' 层流 / k 未启用：黏性底层估计 τ = ρν|u_t|/y
                            Dim nuLoc As Double = If(nuEffFace IsNot Nothing AndAlso nuEffFace.Length > f,
                                                     nuEffFace(f), nuMolecular)

                            uTau = std.Sqrt(nuLoc * tmag / std.Max(geo.Delta(k).Length, 1.0E-30))
                        End If

                        Dim tau As Double = rho * uTau * uTau

                        fx += tau * (tx / tmag) * area
                        fy += tau * (ty / tmag) * area
                        fz += tau * (tz / tmag) * area
                    End If

                    ' ★ 单点退化防护：切割单元网格上极个别边界面可能给出非有限贡献
                    '   （法向退化 / 边界外推压力为 NaN），若不加防护会污染整个积分结果，
                    '   表现为"流场正常但 Cd = NaN"。这里跳过该类面并计数。
                    If Not IsFinite3(fx, fy, fz) Then
                        skippedFaces += 1
                        Continue For
                    End If

                    report.ForceX += fx
                    report.ForceY += fy
                    report.ForceZ += fz

                    ' ---- 力矩 r × F ----
                    Dim r As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Cf(k) - origin

                    report.MomentX += r.Y * fz - r.Z * fy
                    report.MomentY += r.Z * fx - r.X * fz
                    report.MomentZ += r.X * fy - r.Y * fx

                    report.Area += area
                Next
            Next

            report.Patches = String.Join(",", used)

            Return report
        End Function

        ''' <summary>三个分量是否全部有限（NaN / Inf 防护）。</summary>
        Private Function IsFinite3(a As Double, b As Double, c As Double) As Boolean
            Return Not Double.IsNaN(a) AndAlso Not Double.IsInfinity(a) AndAlso
                   Not Double.IsNaN(b) AndAlso Not Double.IsInfinity(b) AndAlso
                   Not Double.IsNaN(c) AndAlso Not Double.IsInfinity(c)
        End Function

        ''' <summary>
        ''' 由壁面相邻单元的湍动能估计摩擦速度 uTau = Cmu^0.25·√k；
        ''' k 未启用（&lt;= 0 或 Nothing）时返回 0，由调用方退化到黏性底层估计。
        ''' </summary>
        Private Function EstimateUTau(kField As EulerianField, ownerCell As Integer) As Double
            If kField Is Nothing Then Return 0

            Dim k As Double = kField.Value(ownerCell)

            If k <= 0 OrElse Double.IsNaN(k) Then Return 0

            Return std.Pow(KECst.Cmu, 0.25) * std.Sqrt(k)
        End Function

    End Module

End Namespace
