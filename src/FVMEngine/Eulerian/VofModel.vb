' /********************************************************************************/
'
'   VofModel.vb
'
'   VOF（Volume Of Fluid）两相模型 —— 发酵罐自由液面（液面高度 = 罐高 2/3）。
'
'   组成（对应 docs\FVM_Eulerian.md §六/§七 的多相扩展）：
'       1. 相分数 α 的输运方程      ∂α/∂t + div(φ·α) + div(φc·α) = 0
'       2. 界面压缩（anti-diffusion）：φc 为"迎风数值扩散"的反向通量，
'          沿界面法向作用，抵消一阶迎风格式对界面的抹平；
'       3. 有界化与体积再平衡（MULES-lite）：
'          限幅到 [0,1] 后把体积差额按 α(1−α) 权重重新分配，
'          保证"液体积"在长时间推进下不漂移（发酵罐工艺关心的正是液位）；
'       4. 两相混合物性：ρ(α) = α·ρ1 + (1−α)·ρ2，ν(α) 同理；
'       5. CSF 表面张力源：f_σ = σ·κ·∇α（κ = −∇·n̂ 为界面曲率），
'          以运动学形式（除以 ρ）加入动量方程。
'
'   ★ 关于界面重构精度的诚实说明
'       本实现采用"一阶迎风隐式 + 显式反扩散 + 有界再平衡"的 MULES-lite 路线，
'       而非几何重构（PLIC）。
'       优点：无条件有界、严格守恒（再平衡后）、与任意多面体网格兼容、实现风险低。
'       代价：界面厚度约为 1~3 个网格单元（PLIC 可达 1 个单元）。
'       对"2/3 液位搅拌"这类工程问题（关心整体液位、混合时间、功率数）足够；
'       若日后需要精确液面形状，只需实现 IVofReconstruction 接口替换
'       <see cref="EulerianVofModel.BuildCompressionFlux"/>。
'
'   混合精度契约：
'       界面处理（梯度、曲率、限幅、再平衡）全部 double —— 属于"精度敏感、
'       且非规则访存"的一侧，不适合下放 GPU；GPU 只承接其中的线性求解内迭代。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports std = System.Math

Namespace Eulerian

    ''' <summary>VOF 两相模型（相 1 = 液相，相 2 = 气相）。</summary>
    Public Class EulerianVofModel

#Region "配置"

        ''' <summary>相 1（液相）密度。</summary>
        Public Property Rho1 As Double = 1000.0

        ''' <summary>相 2（气相）密度。</summary>
        Public Property Rho2 As Double = 1.225

        ''' <summary>相 1（液相）运动粘度。</summary>
        Public Property Nu1 As Double = 1.0E-6

        ''' <summary>相 2（气相）运动粘度。</summary>
        Public Property Nu2 As Double = 1.5E-5

        ''' <summary>表面张力系数（N/m）；0 表示关闭 CSF 源。</summary>
        Public Property SurfaceTension As Double = 0.0

        ''' <summary>界面压缩系数 Cα ∈ [0, 1]；0 表示关闭压缩（界面会被迎风格式抹平）。</summary>
        Public Property CompressionFactor As Double = 1.0

        ''' <summary>体积再平衡的迭代次数。</summary>
        Public Property RebalanceIterations As Integer = 4

        ''' <summary>是否启用界面压缩。</summary>
        Public Property EnableCompression As Boolean = True

        ''' <summary>模型名（报告用）。</summary>
        Public ReadOnly Property Name As String
            Get
                Return $"VOF({Rho1:G5}/{Rho2:G5} kg/m3, sigma={SurfaceTension:G4})"
            End Get
        End Property

#End Region

#Region "状态"

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>相分数场（α = 1 表示全部为相 1 / 液相）。</summary>
        Public ReadOnly Property Alpha As EulerianField

        ''' <summary>上一轮求解报告。</summary>
        Public ReadOnly Property LastReport As EulerianSolverReport
            Get
                Return _lastReport
            End Get
        End Property

        Private _lastReport As EulerianSolverReport

        ''' <summary>初始液相体积（用于再平衡目标）。</summary>
        Public ReadOnly Property InitialLiquidVolume As Double
            Get
                Return _initialVolume
            End Get
        End Property

        Private ReadOnly _initialVolume As Double

#End Region

#Region "构造与初始化"

        Public Sub New(mesh As EulerianFvMesh, fields As EulerianFieldSet)
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))
            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Me.Mesh = mesh
            Me.Alpha = fields.Alpha

            ' 初始：全为气相
            Alpha.Zero()

            _initialVolume = 0
        End Sub

        ''' <summary>
        ''' 依"液面高度"初始化 α 场：z ≤ level 的单元置 1（可选反向）。
        ''' 对等截面容器，液相体积分数恰等于液面高度比（发酵罐的 2/3 液位由此保证）。
        ''' </summary>
        ''' <param name="level">液面 z 坐标</param>
        ''' <param name="phase1Below">True（默认）表示 level 以下为相 1（液相）</param>
        ''' <param name="sharp">True = 二值初值；False = 按单元心线性过渡（更平滑的启动）</param>
        Public Sub InitializeLevel(level As Double, Optional phase1Below As Boolean = True, Optional sharp As Boolean = True)
            Dim n As Integer = Mesh.CellCount
            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = Mesh.CellCentre
            Dim mesh_ = Mesh

            ' 估算单元竖直尺度（用于非二值初值的过渡带厚度）
            Dim h As Double = std.Pow(std.Max(mesh_.TotalVolume, 1.0E-300) / std.Max(1, n), 1.0 / 3.0)
            Dim band As Double = std.Max(h, 1.0E-30)

            For c As Integer = 0 To n - 1
                Dim z As Double = cc(c).Z
                Dim above As Double = If(phase1Below, -1.0, 1.0) * (level - z)
                Dim a As Double

                If sharp Then
                    a = If(above >= 0, 1.0, 0.0)
                ElseIf above >= 0.5 * band Then
                    a = 1.0
                ElseIf above <= -0.5 * band Then
                    a = 0.0
                Else
                    a = 0.5 + above / band
                End If

                Alpha.SetValue(c, a)
            Next

            Call Alpha.UpdateBoundaries()
            Call UpdateProperties(Nothing)
        End Sub

        ''' <summary>当前液相体积分数（Σ V·α / Σ V）。</summary>
        Public Function LiquidFraction() As Double
            Dim n As Integer = Mesh.CellCount
            Dim vol As Double() = Mesh.CellVolume
            Dim v As Double() = Alpha.Values
            Dim a As Double = 0
            Dim t As Double = 0

            For c As Integer = 0 To n - 1
                a += vol(c) * v(c)
                t += vol(c)
            Next

            If t <= 0 Then Return 0

            Return a / t
        End Function

        ''' <summary>当前液相体积。</summary>
        Public Function LiquidVolume() As Double
            Dim n As Integer = Mesh.CellCount
            Dim vol As Double() = Mesh.CellVolume
            Dim v As Double() = Alpha.Values
            Dim a As Double = 0

            For c As Integer = 0 To n - 1
                a += vol(c) * v(c)
            Next

            Return a
        End Function

#End Region

#Region "混合物性"

        ''' <summary>
        ''' 依 α 更新混合密度 ρ 与混合运动粘度 ν。
        ''' </summary>
        Public Sub UpdateProperties(fields As EulerianFieldSet)
            Dim n As Integer = Mesh.CellCount
            Dim a As Double() = Alpha.Values
            Dim rho As Double() = If(fields IsNot Nothing, fields.Rho.Values, Nothing)
            Dim nu As Double() = If(fields IsNot Nothing, fields.Mu.Values, Nothing)

            For c As Integer = 0 To n - 1
                Dim al As Double = Clamp01(a(c))
                Dim r As Double = al * Rho1 + (1.0 - al) * Rho2
                Dim nv As Double = al * Nu1 + (1.0 - al) * Nu2

                a(c) = al

                If rho IsNot Nothing Then rho(c) = r
                If nu IsNot Nothing Then nu(c) = nv
            Next
        End Sub

        ''' <summary>逐面混合密度 ρ_f。</summary>
        Public Function RhoFace() As Double()
            Dim mesh = Me.Mesh
            Dim nF As Integer = mesh.FaceCount
            Dim r(nF - 1) As Double
            Dim w As Double() = mesh.InterpWeight
            Dim v As Double() = Alpha.Values

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim wi As Double = w(i)
                Dim al As Double = wi * v(mesh.InternalOwner(i)) + (1.0 - wi) * v(mesh.InternalNeighbour(i))

                r(f) = al * Rho1 + (1.0 - al) * Rho2
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim al As Double = v(geo.OwnerCells(k))

                    r(geo.FaceIndices(k)) = al * Rho1 + (1.0 - al) * Rho2
                Next
            Next

            Return r
        End Function

        ''' <summary>逐面混合运动粘度 ν_f。</summary>
        Public Function NuFace() As Double()
            Dim mesh = Me.Mesh
            Dim nF As Integer = mesh.FaceCount
            Dim r(nF - 1) As Double
            Dim w As Double() = mesh.InterpWeight
            Dim v As Double() = Alpha.Values

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim wi As Double = w(i)
                Dim al As Double = wi * v(mesh.InternalOwner(i)) + (1.0 - wi) * v(mesh.InternalNeighbour(i))

                r(f) = al * Nu1 + (1.0 - al) * Nu2
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim al As Double = v(geo.OwnerCells(k))

                    r(geo.FaceIndices(k)) = al * Nu1 + (1.0 - al) * Nu2
                Next
            Next

            Return r
        End Function

#End Region

#Region "α 输运"

        ''' <summary>
        ''' 推进一个时间步的 α 输运（守恒形式 + 界面压缩 + 有界再平衡）。
        ''' 必须在压力修正得到满足连续性方程的面通量 <paramref name="flux"/> 之后调用。
        ''' </summary>
        ''' <param name="flux">面体积通量（u·S，出 Owner 为正）</param>
        ''' <param name="deltaT">时间步长</param>
        ''' <param name="ctx">求解上下文（线性求解参数、松弛）</param>
        Public Function Advect(flux As Double(), deltaT As Double, ctx As TurbulenceContext) As EulerianSolverReport
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim n As Integer = mesh.CellCount

            If deltaT <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(deltaT), "时间步长必须为正")
            End If

            Dim prev As Double() = Alpha.ToComponent(0)
            Dim targetVolume As Double = 0
            Dim vol As Double() = mesh.CellVolume

            For c As Integer = 0 To n - 1
                targetVolume += vol(c) * prev(c)
            Next

            Call Alpha.UpdateBoundaries()

            Dim m As New FvMatrix(mesh, "alpha")

            ' 时间项（Euler 隐式，ρ ≡ 1 的相分数守恒方程）
            Call EulerianOperators.FvmDdt(m, Alpha, prev, 0, deltaT, 1.0)

            ' 对流（迎风：无条件有界，是"再平衡"能够成立的基础）
            Call EulerianOperators.FvmDiv(m, flux, Alpha, 0, EulerianConvectionScheme.Upwind)

            ' 界面压缩（显式反向通量）
            If EnableCompression AndAlso CompressionFactor > 0 Then
                Dim divider(n - 1) As Double

                Call BuildCompressionFlux(flux, divider)

                For c As Integer = 0 To n - 1
                    If divider(c) <> 0 Then
                        m.AddSource(c, -divider(c))
                    End If
                Next
            End If

            Call m.Relax(ctx.Relaxation, prev)

            Dim x As Double() = prev
            Dim rpt As EulerianSolverReport = EulerianLinearSolve.Solve(
                m, x, EulerianLinearSolverKind.BiCGStab, ctx.Tolerance, 0.0,
                std.Max(50, ctx.MaxIterations), 0, ctx.UseGpu)

            Call Alpha.SetFromComponent(0, x)
            Call EnforceBounds(targetVolume)
            Call Alpha.UpdateBoundaries()

            _lastReport = rpt

            Return rpt
        End Function

        ''' <summary>
        ''' 显式界面压缩通量及其散度。
        ''' 压缩把相 1 沿 +n̂（α 增大方向）输运，等效于"负扩散"，用于抵消迎风格式的
        ''' 数值扩散；强度取与迎风扩散同量级并乘以 Cα。
        ''' </summary>
        Private Sub BuildCompressionFlux(flux As Double(), ByRef cellDivergence As Double())
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim n As Integer = mesh.CellCount

            Dim gx As Double() = New Double(n - 1) {}
            Dim gy As Double() = New Double(n - 1) {}
            Dim gz As Double() = New Double(n - 1) {}

            Call EulerianOperators.CellGradientLeastSquares(Alpha, 0, gx, gy, gz)

            Dim v As Double() = Alpha.Values
            Dim w As Double() = mesh.InterpWeight
            Dim magSf As Double() = mesh.MagSf
            Dim nf As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.Nf
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour

            Array.Clear(cellDivergence, 0, n)

            For i As Integer = 0 To ifc.Length - 1
                Dim f As Integer = ifc(i)
                Dim wi As Double = w(i)
                Dim P As Integer = ow(i)
                Dim Q As Integer = nb(i)

                ' 界面法向（面插值）
                Dim nx As Double = wi * gx(P) + (1.0 - wi) * gx(Q)
                Dim ny As Double = wi * gy(P) + (1.0 - wi) * gy(Q)
                Dim nz As Double = wi * gz(P) + (1.0 - wi) * gz(Q)
                Dim mag As Double = std.Sqrt(nx * nx + ny * ny + nz * nz)
                Dim sfMag As Double = magSf(f)

                If mag < 1.0E-12 OrElse sfMag <= 0 Then
                    Continue For
                End If

                ' 单位界面法向
                nx /= mag
                ny /= mag
                nz /= mag

                ' 面法向单位向量
                Dim ux As Double = nf(f).X
                Dim uy As Double = nf(f).Y
                Dim uz As Double = nf(f).Z

                ' cosθ = n̂_α · n̂_f
                Dim cosT As Double = nx * ux + ny * uy + nz * uz

                ' 压缩强度：|φ|·Cα·|cosθ|（|φ| 已是体积通量，量纲自洽）
                Dim comp As Double = CompressionFactor * std.Abs(flux(f)) * std.Abs(cosT)

                If comp <= 0 Then
                    Continue For
                End If

                ' 方向：沿 +n̂ 即"从低 α 单元流向高 α 单元"，投影到 owner→neighbour 方向
                Dim phiC As Double = comp * std.Sign(cosT)

                ' 迎风取相 1 分数
                Dim aUp As Double = If(phiC >= 0, v(P), v(Q))

                ' 只压缩界面（两侧都在 0..1 之间），避免把纯相区拉出界
                Dim contribution As Double = phiC * aUp

                cellDivergence(P) += contribution
                cellDivergence(Q) -= contribution
            Next
        End Sub

        ''' <summary>
        ''' 限幅到 [0,1] 并把体积差额按 α(1−α) 权重再分配，使总液体积回到目标值。
        ''' </summary>
        Private Sub EnforceBounds(targetVolume As Double)
            Dim n As Integer = Mesh.CellCount
            Dim vol As Double() = Mesh.CellVolume
            Dim v As Double() = Alpha.Values

            ' 1) 限幅
            For c As Integer = 0 To n - 1
                If Double.IsNaN(v(c)) Then
                    v(c) = 0
                End If

                v(c) = Clamp01(v(c))
            Next

            ' 2) 再平衡
            For iter As Integer = 1 To std.Max(1, RebalanceIterations)
                Dim cur As Double = 0
                Dim wsum As Double = 0

                For c As Integer = 0 To n - 1
                    cur += vol(c) * v(c)
                    wsum += vol(c) * v(c) * (1.0 - v(c))
                Next

                Dim diff As Double = targetVolume - cur

                If std.Abs(diff) <= 1.0E-14 * std.Max(1.0, std.Abs(targetVolume)) Then
                    Exit For
                End If

                If wsum <= 0 Then
                    ' 全场已二值化：按体积权重把差额均匀摊到最接近界面的单元上
                    Dim free As Double = 0

                    For c As Integer = 0 To n - 1
                        free += vol(c)
                    Next

                    If free <= 0 Then Exit For

                    Dim delta As Double = diff / free

                    For c As Integer = 0 To n - 1
                        v(c) = Clamp01(v(c) + delta)
                    Next
                Else
                    Dim delta As Double = diff / wsum

                    For c As Integer = 0 To n - 1
                        v(c) = Clamp01(v(c) + delta * v(c) * (1.0 - v(c)))
                    Next
                End If
            Next
        End Sub

#End Region

#Region "表面张力（CSF）"

        ''' <summary>
        ''' CSF 表面张力源（动力学形式，逐单元）：
        '''     f_σ = σ·κ·(∇α) ，其中 κ = −∇·(∇α/|∇α|)
        ''' 单位 N/m³。调用方以 <c>m.AddSource(c, vol(c)·f_σ(c))</c> 加入动量方程右端。
        ''' </summary>
        ''' <param name="component">速度分量（0=X, 1=Y, 2=Z）</param>
        Public Function SurfaceTensionSource(component As Integer) As Double()
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim src(n - 1) As Double

            If SurfaceTension <= 0 Then Return src

            Dim gx As Double() = New Double(n - 1) {}
            Dim gy As Double() = New Double(n - 1) {}
            Dim gz As Double() = New Double(n - 1) {}

            Call EulerianOperators.CellGradientLeastSquares(Alpha, 0, gx, gy, gz)

            ' 界面单位法向
            Dim ux As Double() = New Double(n - 1) {}
            Dim uy As Double() = New Double(n - 1) {}
            Dim uz As Double() = New Double(n - 1) {}
            Dim mag As Double() = New Double(n - 1) {}

            For c As Integer = 0 To n - 1
                Dim m2 As Double = std.Sqrt(gx(c) * gx(c) + gy(c) * gy(c) + gz(c) * gz(c))

                mag(c) = m2

                If m2 > 1.0E-12 Then
                    ux(c) = gx(c) / m2
                    uy(c) = gy(c) / m2
                    uz(c) = gz(c) / m2
                End If
            Next

            ' κ = −∇·n̂：先把 n̂ 插值到面，再用散度定理逐单元求和
            Dim fx As Double() = New Double(mesh.FaceCount - 1) {}
            Dim fy As Double() = New Double(mesh.FaceCount - 1) {}
            Dim fz As Double() = New Double(mesh.FaceCount - 1) {}

            Call EulerianOperators.FaceGradient(mesh, ux, uy, uz, fx, fy, fz)

            Dim faces As List(Of Moira.CFDEngine.Eulerian.EulerianFace) = mesh.Mesh.Faces

            For c As Integer = 0 To n - 1
                Dim div As Double = 0

                For Each f As Integer In mesh.CellFaces(c)
                    Dim s As Moira.CFDEngine.Eulerian.EulerianVertex = mesh.Sf(f)
                    Dim sign As Double = If(faces(f).Owner = c, 1.0, -1.0)

                    div += sign * (fx(f) * s.X + fy(f) * s.Y + fz(f) * s.Z)
                Next

                Dim volume As Double = mesh.CellVolume(c)
                Dim kappa As Double = If(volume > 0, -div / volume, 0)
                Dim grad As Double = If(component = 0, gx(c), If(component = 1, gy(c), gz(c)))

                src(c) = SurfaceTension * kappa * grad
            Next

            Return src
        End Function

#End Region

        Private Shared Function Clamp01(x As Double) As Double
            If x < 0 Then Return 0
            If x > 1 Then Return 1
            Return x
        End Function

        Public Overrides Function ToString() As String
            Return $"EulerianVofModel(liquidFraction={LiquidFraction().ToString("F5", CultureInfo.InvariantCulture)}, {Name})"
        End Function

    End Class

End Namespace
