' /********************************************************************************/
'
'   EulerianDiagnostics.vb
'
'   后处理诊断：流量守恒、搅拌功率 / 功率数、探针采样、残差汇总。
'
'   工程意义：
'       · 进出口流量不平衡 → 立刻定位"边界通量设置错误"或"压力参考缺失"；
'       · 功率数 Np = P/(ρN³D⁵) 是搅拌罐放大设计的核心无量纲数
'         （Rushton 涡轮在水中的典型值 5~6；本实现用壁面剪应力的对数律估计
'          桨叶对流体做的功，因此在粗网格上会略偏离实验值，但趋势可靠）；
'       · 探针采样用于监控局部位点收敛（转速、功率、混合时间曲线）。
'
'   混合精度契约：全部 double（诊断统计量含比值与求和，属精度敏感的非热点路径）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

    ''' <summary>逐 patch 与总体的体积 / 质量流量。</summary>
    Public Class EulerianFlowRates

        ''' <summary>逐 patch 体积流量（出＝正，入＝负）。</summary>
        Public ReadOnly Property VolumetricPerPatch As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>逐 patch 质量流量。</summary>
        Public ReadOnly Property MassPerPatch As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>全部边界面的体积流量之和（闭合域应 ≈ 0）。</summary>
        Public Property NetVolumetricFlux As Double

        ''' <summary>全部边界面的质量流量之和。</summary>
        Public Property NetMassFlux As Double

        ''' <summary>入口（流入）体积流量绝对值之和。</summary>
        Public Property TotalInflow As Double

        ''' <summary>出口（流出）体积流量之和。</summary>
        Public Property TotalOutflow As Double

        ''' <summary>质量不平衡的相对量 |NetMass| / max(TotalIn, TotalOut)。</summary>
        Public ReadOnly Property MassImbalance As Double
            Get
                Dim den As Double = std.Max(std.Abs(TotalInflow), std.Abs(TotalOutflow))

                If den <= 1.0E-30 Then Return 0

                Return std.Abs(NetMassFlux) / den
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim sb As New StringBuilder(256)

            sb.AppendLine($"flow rates: net volumetric = {NetVolumetricFlux.ToString("E3", CultureInfo.InvariantCulture)} m3/s, " &
                          $"net mass = {NetMassFlux.ToString("E3", CultureInfo.InvariantCulture)} kg/s, " &
                          $"imbalance = {MassImbalance.ToString("E3", CultureInfo.InvariantCulture)}")

            For Each kv As KeyValuePair(Of String, Double) In VolumetricPerPatch
                sb.AppendLine($"  {kv.Key}: Q={kv.Value.ToString("E4", CultureInfo.InvariantCulture)} m3/s")
            Next

            Return sb.ToString()
        End Function

    End Class

    ''' <summary>后处理诊断工具。</summary>
    Public Module EulerianDiagnostics

#Region "流量"

        ''' <summary>按 patch 统计体积 / 质量流量（出为正）。</summary>
        Public Function FlowRates(mesh As EulerianFvMesh, flux As Double(),
                                         Optional rhoFace As Double() = Nothing) As EulerianFlowRates

            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Dim r As New EulerianFlowRates()

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim qv As Double = 0
                Dim qm As Double = 0

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim q As Double = flux(f)

                    qv += q

                    If rhoFace IsNot Nothing AndAlso rhoFace.Length > f Then
                        qm += rhoFace(f) * q
                    End If
                Next

                r.VolumetricPerPatch(geo.Name) = qv
                r.MassPerPatch(geo.Name) = qm

                r.NetVolumetricFlux += qv
                r.NetMassFlux += qm

                If qv < 0 Then
                    r.TotalInflow -= qv
                Else
                    r.TotalOutflow += qv
                End If
            Next

            Return r
        End Function

#End Region

#Region "搅拌功率"

        ''' <summary>
        ''' 搅拌功率估计（对流体的做功功率）：
        '''     P = Σ_{旋转壁面} ρ·uTau²·|u_t|·A_f
        ''' 其中 uTau 由壁面相邻单元的对数律给出（Cmu^0.25·√k），
        ''' |u_t| 为壁面切向相对速度（流体相对桨叶）。
        ''' </summary>
        ''' <param name="rotatingPatches">旋转壁面 patch 名（桨叶 / 桨轴）</param>
        Public Function ImpellerPower(mesh As EulerianFvMesh, fields As EulerianFieldSet,
                                             nuEffFace As Double(), rhoFace As Double(),
                                             nuMolecular As Double,
                                             rotatingPatches As IEnumerable(Of String)) As Double

            If mesh Is Nothing OrElse fields Is Nothing Then Return 0

            Dim names As New HashSet(Of Integer)

            If rotatingPatches IsNot Nothing Then
                For Each nm As String In rotatingPatches
                    Dim pi As Integer = mesh.FindPatch(nm)

                    If pi >= 0 Then
                        Call names.Add(pi)
                    End If
                Next
            End If

            If names.Count = 0 Then Return 0

            Dim power As Double = 0
            Dim kField As EulerianField = fields.K
            Dim u As EulerianField = fields.U

            For pi As Integer = 0 To mesh.PatchCount - 1
                If Not names.Contains(pi) Then
                    Continue For
                End If

                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim pfU As EulerianPatchField = u.Boundaries(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim f As Integer = geo.FaceIndices(k)
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim area As Double = geo.MagSf(k)

                    Dim wbX As Double = 0
                    Dim wbY As Double = 0
                    Dim wbZ As Double = 0
                    Dim mw As MovingWallBc = TryCast(pfU.Condition, MovingWallBc)

                    If mw IsNot Nothing Then
                        Call mw.WallVelocity(k, wbX, wbY, wbZ)
                    End If

                    Dim tx As Double = pfU.Value(k, 0) - wbX
                    Dim ty As Double = If(u.Components >= 2, pfU.Value(k, 1), 0) - wbY
                    Dim tz As Double = If(u.Components >= 3, pfU.Value(k, 2), 0) - wbZ

                    Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k).Normalized()
                    Dim un As Double = tx * nrm.X + ty * nrm.Y + tz * nrm.Z

                    tx -= un * nrm.X
                    ty -= un * nrm.Y
                    tz -= un * nrm.Z

                    Dim tmag As Double = std.Sqrt(tx * tx + ty * ty + tz * tz)

                    If tmag <= 1.0E-12 Then
                        Continue For
                    End If

                    Dim rho As Double = If(rhoFace IsNot Nothing AndAlso rhoFace.Length > f, rhoFace(f), 1000.0)
                    Dim uTau As Double = 0

                    If kField IsNot Nothing Then
                        Dim kv As Double = kField.Value(own)

                        If kv > 0 Then
                            uTau = std.Pow(KECst.Cmu, 0.25) * std.Sqrt(kv)
                        End If
                    End If

                    If uTau <= 0 Then
                        Dim nuLoc As Double = If(nuEffFace IsNot Nothing AndAlso nuEffFace.Length > f,
                                                 nuEffFace(f), nuMolecular)

                        uTau = std.Sqrt(nuLoc * tmag / std.Max(geo.Delta(k).Length, 1.0E-30))
                    End If

                    power += rho * uTau * uTau * tmag * area
                Next
            Next

            Return power
        End Function

        ''' <summary>功率数 Np = P/(ρ·N³·D⁵)，N 为转速（rev/s），D 为桨径（m）。</summary>
        Public Function PowerNumber(power As Double, rho As Double,
                                          revolutionsPerSecond As Double, impellerDiameter As Double) As Double

            Dim den As Double = rho * std.Pow(std.Max(revolutionsPerSecond, 1.0E-30), 3.0) *
                                std.Pow(std.Max(impellerDiameter, 1.0E-30), 5.0)

            If den <= 1.0E-30 Then Return 0

            Return power / den
        End Function

        ''' <summary>桨叶叶尖速度 v = ω·R（m/s）。</summary>
        Public Function TipSpeed(omega As Double, radius As Double) As Double
            Return std.Abs(omega) * radius
        End Function

#End Region

#Region "探针"

        ''' <summary>把探针坐标吸附到最近的单元心，返回单元编号（网格为空时返回 −1）。</summary>
        Public Function NearestCell(mesh As EulerianFvMesh, x As Double, y As Double, z As Double) As Integer
            If mesh Is Nothing OrElse mesh.CellCount = 0 Then Return -1

            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre
            Dim best As Integer = 0
            Dim bestD As Double = Double.MaxValue

            For c As Integer = 0 To mesh.CellCount - 1
                Dim dx As Double = cc(c).X - x
                Dim dy As Double = cc(c).Y - y
                Dim dz As Double = cc(c).Z - z
                Dim d As Double = dx * dx + dy * dy + dz * dz

                If d < bestD Then
                    bestD = d
                    best = c
                End If
            Next

            Return best
        End Function

        ''' <summary>探针采样（单分量）。</summary>
        Public Function Sample(field As EulerianField, cell As Integer, Optional component As Integer = 0) As Double
            If field Is Nothing OrElse cell < 0 Then Return 0

            Return field.Value(cell, component)
        End Function

#End Region

#Region "残差汇总"

        ''' <summary>把不可压求解器的残差历史压缩成"每 N 轮一次的摘要"（日志友好）。</summary>
        Public Function ResidualSummary(solver As IncompressibleSolver, Optional every As Integer = 10) As String
            If solver Is Nothing OrElse solver.ResidualHistory.Count = 0 Then Return ""

            Dim sb As New StringBuilder(256)
            Dim h As List(Of Double) = solver.ResidualHistory

            sb.AppendLine($"SIMPLE residuals (every {every} iterations, last={h.Count}):")

            Dim start As Integer = std.Max(0, h.Count - 1 - 4 * std.Max(1, every))

            For i As Integer = start To h.Count - 1
                If i = h.Count - 1 OrElse (h.Count - 1 - i) Mod std.Max(1, every) = 0 Then
                    sb.AppendLine($"  iter {i + 1}: residual = {h(i).ToString("E4", CultureInfo.InvariantCulture)}")
                End If
            Next

            sb.AppendLine($"  continuity error = {solver.ContinuityError.ToString("E4", CultureInfo.InvariantCulture)}")

            Return sb.ToString()
        End Function

#End Region

    End Module

End Namespace
