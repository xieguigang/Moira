' /********************************************************************************/
'
'   CompressibleSolver.vb
'
'   可压缩 Euler/N-S 求解器 —— AUSM+ / HLLC 通量分裂 + MUSCL 重构 + 显式推进。
'
'   面向 F1 车身局部跨声速、飞机高速绕流、以及需要捕捉弱激波的建筑群风场。
'   （建筑群 / F1 车身大部分区域 Ma < 0.3，若不需要可压缩效应，
'     应优先使用 IncompressibleSolver —— 更快且更稳。）
'
'   守恒量（逐单元）： U = (ρ, ρu, ρv, ρw, E)
'   通量形式（有限体积）：
'       dU_P/dt = −(1/V_P)·Σ_f F_f·|S_f| + S
'       其中 F_f = F(U_L, U_R, n̂_f) 由通量分裂格式给出，n̂_f 自 Owner 指向 Neighbour。
'
'   已实现通量格式（<see cref="ICompressibleFluxScheme"/>）：
'       AusmPlusFlux   AUSM+（Liou 1996）：界面 Mach 数与压力分裂，
'                      接触间断分辨力好、无 carbuncle、对低马赫数也有合理耗散。
'       HllcFlux       Toro 的 HLLC：三波结构（左右激波 + 接触面），
'                      对接触间断与激波的分辨率优秀，鲁棒性最强。
'
'   重构与推进：
'       · MUSCL：原始变量（ρ, u, v, w, p）逐单元梯度 + Barth–Jespersen 限制器，
'         保证面重构值落在邻域上下界内（单调、无新极值）。
'       · 时间推进：显式 SSP-RK3；稳态用"局部时间步长"（cell-local CFL）加速收敛。
'         （隐式 LU-SGS / 双时间推进留作扩展点，见
'           <see cref="CompressibleOptions.SteadyState"/> 的注释。）
'
'   边界（<see cref="CompressibleBcKind"/>，通过虚拟单元 ghost 状态实现）：
'       WallSlip / WallNoSlipAdiabatic / InletSubsonic(总压总温) /
'       InletSupersonic / OutletSubsonic(静压) / OutletSupersonic / Symmetry
'
'   混合精度契约：
'       通量分裂、重构、限制器、边界 ghost 全部 double（含 sqrt/pow 与非规则访存，
'       精度敏感且不易向量化）；本求解器为显式推进，没有椭圆型线性求解，
'       因此**不需要** GPU 线性求解后端 —— 这符合"热点才下放"的原则。
'       若日后加入隐式推进，其线性求解可直接复用 EulerianFvmCompute。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

#Region "通量格式契约"

    ''' <summary>
    ''' 可压缩面通量格式契约：给定左右状态与面单位法向，返回 +n̂ 方向的通量
    ''' F = (F_ρ, F_ρu, F_ρv, F_ρw, F_E)。
    ''' </summary>
    Public Interface ICompressibleFluxScheme

        ''' <summary>格式名称。</summary>
        ReadOnly Property Name As String

        ''' <summary>
        ''' 计算面通量。
        ''' </summary>
        ''' <param name="gamma">比热比 γ</param>
        ''' <param name="leftState">左态原始量（ρ, u, v, w, p）</param>
        ''' <param name="rightState">右态原始量（ρ, u, v, w, p）</param>
        ''' <param name="nx">面单位法向 X（Owner → Neighbour）</param>
        ''' <param name="ny">面单位法向 Y</param>
        ''' <param name="nz">面单位法向 Z</param>
        ''' <param name="flux">输出通量，长度 5</param>
        Sub ComputeFlux(gamma As Double, leftState As Double(), rightState As Double(),
                        nx As Double, ny As Double, nz As Double, flux As Double())
    End Interface

    ''' <summary>守恒量索引。</summary>
    Public Module ConservedIdx
        Public Const Rho As Integer = 0
        Public Const RhoU As Integer = 1
        Public Const RhoV As Integer = 2
        Public Const RhoW As Integer = 3
        Public Const Energy As Integer = 4
        Public Const Count As Integer = 5
    End Module

    ''' <summary>
    ''' AUSM+（Liou 1996）：界面 Mach 数分裂 + 压力分裂。
    ''' </summary>
    Public Class AusmPlusFlux
        Implements ICompressibleFluxScheme

        Public ReadOnly Property Name As String Implements ICompressibleFluxScheme.Name
            Get
                Return "AUSM+"
            End Get
        End Property

        Public Sub ComputeFlux(gamma As Double, leftState As Double(), rightState As Double(),
                               nx As Double, ny As Double, nz As Double,
                               flux As Double()) Implements ICompressibleFluxScheme.ComputeFlux

            Dim rl As Double = std.Max(leftState(0), 1.0E-30)
            Dim ul As Double = leftState(1), vl As Double = leftState(2), wl As Double = leftState(3)
            Dim pl As Double = std.Max(leftState(4), 1.0E-30)

            Dim rr As Double = std.Max(rightState(0), 1.0E-30)
            Dim ur As Double = rightState(1), vr As Double = rightState(2), wr As Double = rightState(3)
            Dim pr As Double = std.Max(rightState(4), 1.0E-30)

            Dim al As Double = std.Sqrt(gamma * pl / rl)
            Dim ar As Double = std.Sqrt(gamma * pr / rr)

            Dim unl As Double = ul * nx + vl * ny + wl * nz
            Dim unr As Double = ur * nx + vr * ny + wr * nz

            Dim aHalf As Double = 0.5 * (al + ar)
            Dim ml As Double = unl / aHalf
            Dim mr As Double = unr / aHalf

            Dim mp As Double = SplitMachPlus(ml)
            Dim mm As Double = SplitMachMinus(mr)
            Dim mHalf As Double = mp + mm

            Dim pp As Double = SplitPressurePlus(ml)
            Dim pm As Double = SplitPressureMinus(mr)
            Dim pHalf As Double = pp * pl + pm * pr

            Dim mdot As Double = aHalf * mHalf * If(mHalf >= 0, rl, rr)

            Dim ux As Double, uy As Double, uz As Double, hUp As Double

            If mHalf >= 0 Then
                ux = ul : uy = vl : uz = wl
                hUp = ETotal(rl, ul, vl, wl, pl, gamma) + pl
            Else
                ux = ur : uy = vr : uz = wr
                hUp = ETotal(rr, ur, vr, wr, pr, gamma) + pr
            End If

            flux(0) = mdot
            flux(1) = mdot * ux + pHalf * nx
            flux(2) = mdot * uy + pHalf * ny
            flux(3) = mdot * uz + pHalf * nz
            flux(4) = mdot * hUp
        End Sub

        ''' <summary>总能量密度 E = p/(γ−1) + ½ρ|u|²。</summary>
        Public Shared Function ETotal(rho As Double, u As Double, v As Double, w As Double,
                                      p As Double, gamma As Double) As Double

            Return p / (gamma - 1.0) + 0.5 * rho * (u * u + v * v + w * w)
        End Function

        ''' <summary>M⁺ 四阶分裂。</summary>
        Public Shared Function SplitMachPlus(m As Double) As Double
            If std.Abs(m) >= 1.0 Then Return 0.5 * (m + std.Abs(m))

            Dim t As Double = m + 1.0

            Return 0.25 * t * t
        End Function

        ''' <summary>M⁻ 四阶分裂。</summary>
        Public Shared Function SplitMachMinus(m As Double) As Double
            If std.Abs(m) >= 1.0 Then Return 0.5 * (m - std.Abs(m))

            Dim t As Double = m - 1.0

            Return -0.25 * t * t
        End Function

        ''' <summary>P⁺ 五阶压力分裂。</summary>
        Public Shared Function SplitPressurePlus(m As Double) As Double
            If std.Abs(m) >= 1.0 Then Return If(m >= 0, 1.0, 0.0)

            Dim t As Double = m + 1.0

            Return 0.25 * t * t * (2.0 - m)
        End Function

        ''' <summary>P⁻ 五阶压力分裂。</summary>
        Public Shared Function SplitPressureMinus(m As Double) As Double
            If std.Abs(m) >= 1.0 Then Return If(m >= 0, 0.0, 1.0)

            Dim t As Double = m - 1.0

            Return 0.25 * t * t * (2.0 + m)
        End Function
    End Class

    ''' <summary>
    ''' HLLC（Toro）：三波结构近似黎曼解，鲁棒性最强。
    ''' </summary>
    Public Class HllcFlux
        Implements ICompressibleFluxScheme

        Public ReadOnly Property Name As String Implements ICompressibleFluxScheme.Name
            Get
                Return "HLLC"
            End Get
        End Property

        Public Sub ComputeFlux(gamma As Double, leftState As Double(), rightState As Double(),
                               nx As Double, ny As Double, nz As Double,
                               flux As Double()) Implements ICompressibleFluxScheme.ComputeFlux

            Dim rl As Double = std.Max(leftState(0), 1.0E-30)
            Dim ul As Double = leftState(1), vl As Double = leftState(2), wl As Double = leftState(3)
            Dim pl As Double = std.Max(leftState(4), 1.0E-30)

            Dim rr As Double = std.Max(rightState(0), 1.0E-30)
            Dim ur As Double = rightState(1), vr As Double = rightState(2), wr As Double = rightState(3)
            Dim pr As Double = std.Max(rightState(4), 1.0E-30)

            Dim al As Double = std.Sqrt(gamma * pl / rl)
            Dim ar As Double = std.Sqrt(gamma * pr / rr)

            Dim unl As Double = ul * nx + vl * ny + wl * nz
            Dim unr As Double = ur * nx + vr * ny + wr * nz

            Dim el As Double = AusmPlusFlux.ETotal(rl, ul, vl, wl, pl, gamma)
            Dim er As Double = AusmPlusFlux.ETotal(rr, ur, vr, wr, pr, gamma)

            ' ---- 波速估计（Toro 的 PVRS + 自适应 q） ----
            Dim rAvg As Double = 0.5 * (rl + rr)
            Dim aAvg As Double = 0.5 * (al + ar)
            Dim pPvrs As Double = 0.5 * (pl + pr) - 0.5 * (unr - unl) * rAvg * aAvg

            If pPvrs < 0 Then pPvrs = 0

            Dim ql As Double = If(pPvrs <= pl, 1.0, std.Sqrt(1.0 + (gamma + 1.0) / (2.0 * gamma) * (pPvrs / pl - 1.0)))
            Dim qr As Double = If(pPvrs <= pr, 1.0, std.Sqrt(1.0 + (gamma + 1.0) / (2.0 * gamma) * (pPvrs / pr - 1.0)))

            Dim sl As Double = unl - al * ql
            Dim sr As Double = unr + ar * qr

            Dim denom As Double = rl * (sl - unl) - rr * (sr - unr)
            Dim sm As Double

            If std.Abs(denom) < 1.0E-30 Then
                sm = 0.5 * (unl + unr)
            Else
                sm = (pr - pl + rl * unl * (sl - unl) - rr * unr * (sr - unr)) / denom
            End If

            ' ---- 采样 ----
            If sl >= 0 Then
                Call PhysicalFlux(rl, ul, vl, wl, pl, el, nx, ny, nz, flux)
            ElseIf sm >= 0 Then
                Call StarFlux(rl, ul, vl, wl, pl, el, unl, sl, sm, nx, ny, nz, flux)
            ElseIf sr >= 0 Then
                Call StarFlux(rr, ur, vr, wr, pr, er, unr, sr, sm, nx, ny, nz, flux)
            Else
                Call PhysicalFlux(rr, ur, vr, wr, pr, er, nx, ny, nz, flux)
            End If
        End Sub

        Private Shared Sub PhysicalFlux(rho As Double, u As Double, v As Double, w As Double,
                                        p As Double, e As Double,
                                        nx As Double, ny As Double, nz As Double, flux As Double())

            Dim un As Double = u * nx + v * ny + w * nz

            flux(0) = rho * un
            flux(1) = rho * u * un + p * nx
            flux(2) = rho * v * un + p * ny
            flux(3) = rho * w * un + p * nz
            flux(4) = (e + p) * un
        End Sub

        Private Shared Sub StarFlux(rho As Double, u As Double, v As Double, w As Double,
                                    p As Double, e As Double,
                                    un As Double, sK As Double, sm As Double,
                                    nx As Double, ny As Double, nz As Double, flux As Double())

            Dim dK As Double = sK - un
            Dim dM As Double = sK - sm

            If std.Abs(dM) < 1.0E-30 Then
                Call PhysicalFlux(rho, u, v, w, p, e, nx, ny, nz, flux)
                Return
            End If

            If std.Abs(dK) < 1.0E-30 Then
                Call PhysicalFlux(rho, u, v, w, p, e, nx, ny, nz, flux)
                Return
            End If

            Dim factor As Double = dK / dM
            Dim rhoStar As Double = rho * factor

            ' 星区速度：切向不变，法向变为 S_M
            Dim us As Double = u + (sm - un) * nx
            Dim vs As Double = v + (sm - un) * ny
            Dim ws As Double = w + (sm - un) * nz

            Dim eStar As Double = rhoStar * (e / rho + (sm - un) * (sm + p / (rho * dK)))

            ' F_K + S_K (U*_K − U_K)
            Dim fRho As Double = rho * un
            Dim fMomX As Double = rho * u * un + p * nx
            Dim fMomY As Double = rho * v * un + p * ny
            Dim fMomZ As Double = rho * w * un + p * nz
            Dim fEnergy As Double = (e + p) * un

            flux(0) = fRho + sK * (rhoStar - rho)
            flux(1) = fMomX + sK * (rhoStar * us - rho * u)
            flux(2) = fMomY + sK * (rhoStar * vs - rho * v)
            flux(3) = fMomZ + sK * (rhoStar * ws - rho * w)
            flux(4) = fEnergy + sK * (eStar - e)
        End Sub
    End Class

#End Region

#Region "边界与配置"

    ''' <summary>可压缩边界的物理类型（通过虚拟单元 ghost 状态实现）。</summary>
    Public Enum CompressibleBcKind

        ''' <summary>滑移壁面（无黏）：法向速度镜像。</summary>
        WallSlip = 0

        ''' <summary>绝热无滑移壁面：法向镜像 + 切向反号。</summary>
        WallNoSlipAdiabatic = 1

        ''' <summary>亚声速入口（总压 / 总温 + 来流方向）。</summary>
        InletSubsonic = 2

        ''' <summary>超声速入口（密度 / 温度 / 方向全部给定）。</summary>
        InletSupersonic = 3

        ''' <summary>亚声速出口（给定静压，其余外插）。</summary>
        OutletSubsonic = 4

        ''' <summary>超声速出口（全外插）。</summary>
        OutletSupersonic = 5

        ''' <summary>对称面（法向速度镜像，等价于滑移壁面但无压力力）。</summary>
        Symmetry = 6
    End Enum

    ''' <summary>可压缩边界规格。</summary>
    Public Class CompressiblePatchSpec

        Public Property Kind As CompressibleBcKind = CompressibleBcKind.WallSlip

        ''' <summary>总压（Pa，入口）。</summary>
        Public Property TotalPressure As Double = 101325.0

        ''' <summary>总温（K，入口）。</summary>
        Public Property TotalTemperature As Double = 300.0

        ''' <summary>来流方向（入口，任意长度，内部单位化）。</summary>
        Public Property Direction As Moira.CFDEngine.Eulerian.EulerianVertex =
            New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)

        ''' <summary>静压（Pa，亚声速出口）。</summary>
        Public Property StaticPressure As Double = 101325.0

        ''' <summary>入口马赫数（超声速入口）。</summary>
        Public Property Mach As Double = 2.0

        ''' <summary>壁面温度（K；&lt;= 0 表示绝热）。</summary>
        Public Property WallTemperature As Double = -1

    End Class

    ''' <summary>可压缩求解器配置。</summary>
    Public Class CompressibleOptions

        ''' <summary>比热比 γ。</summary>
        Public Property Gamma As Double = 1.4

        ''' <summary>气体常数 R（J/(kg·K)）。</summary>
        Public Property GasConstant As Double = 287.05

        ''' <summary>通量格式。</summary>
        Public Property Scheme As ICompressibleFluxScheme

        ''' <summary>CFL 数（显式推进；推荐 0.5~2.0 配 MUSCL）。</summary>
        Public Property Cfl As Double = 0.8

        ''' <summary>True = 稳态（局部时间步长 + 无时间精度要求）；False = 双曲型瞬态（全局时间步长）。</summary>
        Public Property SteadyState As Boolean = True

        ''' <summary>是否启用二阶 MUSCL 重构（False = 一阶迎风型通量）。</summary>
        Public Property SecondOrder As Boolean = True

        ''' <summary>固定时间步长（瞬态；&lt;= 0 时按 CFL 自动确定）。</summary>
        Public Property FixedDeltaT As Double = 0

        ''' <summary>各 patch 的边界规格（按 patch 名）；未指定时按类型给出保守默认。</summary>
        Public ReadOnly Property Patches As New Dictionary(Of String, CompressiblePatchSpec)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>由 Eulerian patch 类型自动推导的默认边界（未在 <see cref="Patches"/> 中显式指定时使用）。</summary>
        Public Property InletFallback As CompressibleBcKind = CompressibleBcKind.InletSubsonic
        Public Property OutletFallback As CompressibleBcKind = CompressibleBcKind.OutletSubsonic
        Public Property WallFallback As CompressibleBcKind = CompressibleBcKind.WallSlip

        ''' <summary>初始化时按远场条件填充全场（由 <see cref="CompressibleSolver.InitializeFromFreestream"/> 使用）。</summary>
        Public Property FreestreamPressure As Double = 101325.0
        Public Property FreestreamTemperature As Double = 300.0
        Public Property FreestreamMach As Double = 0.1
        Public Property FreestreamDirection As Moira.CFDEngine.Eulerian.EulerianVertex =
            New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)

    End Class

#End Region

#Region "求解器"

    ''' <summary>可压缩 Euler 求解器（显式 SSP-RK3 + MUSCL + AUSM+/HLLC）。</summary>
    Public Class CompressibleSolver

#Region "状态"

        Public ReadOnly Property Mesh As EulerianFvMesh
        Public ReadOnly Property Fields As EulerianFieldSet
        Public ReadOnly Property Options As CompressibleOptions

        ''' <summary>守恒量（逐单元），索引见 <see cref="ConservedIdx"/>。</summary>
        Public ReadOnly Property Conserved As Double()()

        ''' <summary>当前物理时间（瞬态）。</summary>
        Public Property Time As Double = 0

        ''' <summary>已执行的时间步数。</summary>
        Public Property StepCount As Integer = 0

        ''' <summary>密度方程残差历史（归一化 L2）。</summary>
        Public ReadOnly Property ResidualHistory As New List(Of Double)()

        Private ReadOnly _u As Double()()
        Private ReadOnly _uOld As Double()()
        Private ReadOnly _rhs As Double()()
        Private ReadOnly _dt As Double()
        Private ReadOnly _prim As Double()()
        Private ReadOnly _grad As Double()()()
        Private ReadOnly _limiter As Double()()
        Private ReadOnly _leafX As Double()
        Private ReadOnly _leafY As Double()
        Private ReadOnly _leafZ As Double()
        Private ReadOnly _residualBuf As Double()

        ''' <summary>最近一步的最大 Mach 数。</summary>
        Public Property MaxMach As Double = 0

#End Region

#Region "构造与初始化"

        Public Sub New(mesh As EulerianFvMesh, fields As EulerianFieldSet,
                       Optional options As CompressibleOptions = Nothing)

            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))
            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Me.Mesh = mesh
            Me.Fields = fields
            Me.Options = If(options, New CompressibleOptions())

            If Options.Scheme Is Nothing Then
                Options.Scheme = New AusmPlusFlux()
            End If

            Dim n As Integer = mesh.CellCount

            _u = AllocateConserved(n)
            _uOld = AllocateConserved(n)
            _rhs = AllocateConserved(n)
            Conserved = _u

            _dt = New Double(n - 1) {}
            _prim = New Double(n - 1)() {}

            For c As Integer = 0 To n - 1
                _prim(c) = New Double(4) {}
            Next

            Dim nv As Integer = If(Options.SecondOrder, 5, 1)

            _grad = New Double(nv - 1)()() {}

            For c As Integer = 0 To nv - 1
                _grad(c) = New Double(n - 1)() {}

                For i As Integer = 0 To n - 1
                    _grad(c)(i) = New Double(2) {}
                Next
            Next

            _limiter = New Double(nv - 1)() {}

            For c As Integer = 0 To nv - 1
                _limiter(c) = New Double(n - 1) {}
            Next

            Dim nf As Integer = mesh.FaceCount

            _leafX = New Double(nf - 1) {}
            _leafY = New Double(nf - 1) {}
            _leafZ = New Double(nf - 1) {}
            _residualBuf = New Double(n - 1) {}
        End Sub

        Private Shared Function AllocateConserved(n As Integer) As Double()()
            Dim a As Double()() = New Double(ConservedIdx.Count - 1)() {}

            For i As Integer = 0 To ConservedIdx.Count - 1
                a(i) = New Double(n - 1) {}
            Next

            Return a
        End Function

        ''' <summary>用远场条件填充全场（自由来流）。</summary>
        Public Sub InitializeFromFreestream()
            Dim gamma As Double = Options.Gamma
            Dim r As Double = Options.GasConstant

            Dim p0 As Double = Options.FreestreamPressure
            Dim t0 As Double = Options.FreestreamTemperature
            Dim rho0 As Double = p0 / (r * t0)
            Dim a0 As Double = std.Sqrt(gamma * p0 / rho0)
            Dim m As Double = Options.FreestreamMach
            Dim d As Moira.CFDEngine.Eulerian.EulerianVertex = Options.FreestreamDirection.Normalized()

            If d.Length <= 0 Then
                d = New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)
            End If

            Dim speed As Double = m * a0

            Call SetUniformState(rho0, d.X * speed, d.Y * speed, d.Z * speed, p0)
        End Sub

        ''' <summary>用给定的均匀状态填充全场。</summary>
        Public Sub SetUniformState(rho As Double, u As Double, v As Double, w As Double, p As Double)
            Dim n As Integer = Mesh.CellCount
            Dim e As Double = AusmPlusFlux.ETotal(rho, u, v, w, p, Options.Gamma)

            For c As Integer = 0 To n - 1
                _u(ConservedIdx.Rho)(c) = rho
                _u(ConservedIdx.RhoU)(c) = rho * u
                _u(ConservedIdx.RhoV)(c) = rho * v
                _u(ConservedIdx.RhoW)(c) = rho * w
                _u(ConservedIdx.Energy)(c) = e
            Next

            Call UpdatePrimitive()
            Call SyncFields()
        End Sub

#End Region

#Region "原始量 ↔ 守恒量"

        ''' <summary>从守恒量刷新原始量缓存（ρ/ρu/ρv/ρw/E → ρ,u,v,w,p）与派生的 a、Ma、T。</summary>
        Public Sub UpdatePrimitive()
            Dim n As Integer = Mesh.CellCount
            Dim gamma As Double = Options.Gamma
            Dim r As Double = Options.GasConstant
            Dim umax As Double = 0

            For c As Integer = 0 To n - 1
                Dim rho As Double = _u(ConservedIdx.Rho)(c)

                If rho <= 0 OrElse Double.IsNaN(rho) Then
                    rho = 1.0E-8
                    _u(ConservedIdx.Rho)(c) = rho
                End If

                Dim u As Double = _u(ConservedIdx.RhoU)(c) / rho
                Dim v As Double = _u(ConservedIdx.RhoV)(c) / rho
                Dim w As Double = _u(ConservedIdx.RhoW)(c) / rho

                Dim e As Double = _u(ConservedIdx.Energy)(c)
                Dim p As Double = (gamma - 1.0) * (e - 0.5 * rho * (u * u + v * v + w * w))

                If p <= 0 OrElse Double.IsNaN(p) Then
                    p = 1.0E-6
                    _u(ConservedIdx.Energy)(c) = p / (gamma - 1.0) + 0.5 * rho * (u * u + v * v + w * w)
                End If

                _prim(c)(0) = rho
                _prim(c)(1) = u
                _prim(c)(2) = v
                _prim(c)(3) = w
                _prim(c)(4) = p

                Dim a As Double = std.Sqrt(gamma * p / rho)
                Dim mach As Double = std.Sqrt(u * u + v * v + w * w) / std.Max(a, 1.0E-30)

                If mach > umax Then umax = mach
            Next

            MaxMach = umax
        End Sub

        ''' <summary>把原始量写回用户可见的场（U / p / ρ / T + Extra 中的 a、Ma）。</summary>
        Public Sub SyncFields()
            Dim n As Integer = Mesh.CellCount
            Dim fields = Me.Fields
            Dim gamma As Double = Options.Gamma
            Dim r As Double = Options.GasConstant

            Dim aF As EulerianField = fields.Scalar("a")
            Dim mF As EulerianField = fields.Scalar("Ma")

            For c As Integer = 0 To n - 1
                Dim rho As Double = _prim(c)(0)
                Dim u As Double = _prim(c)(1)
                Dim v As Double = _prim(c)(2)
                Dim w As Double = _prim(c)(3)
                Dim p As Double = _prim(c)(4)

                fields.U.SetValue(c, u, 0)
                fields.U.SetValue(c, v, 1)
                fields.U.SetValue(c, w, 2)

                fields.P.SetValue(c, p)
                fields.Rho.SetValue(c, rho)
                fields.T.SetValue(c, p / (r * rho))

                Dim a As Double = std.Sqrt(gamma * p / rho)

                aF.SetValue(c, a)
                mF.SetValue(c, std.Sqrt(u * u + v * v + w * w) / std.Max(a, 1.0E-30))
            Next

            Call fields.UpdateBoundaries()
        End Sub

#End Region

#Region "重构与限制器"

        ''' <summary>
        ''' 逐单元的原始变量梯度（最小二乘）+ Barth–Jespersen 限制器。
        ''' </summary>
        Private Sub BuildReconstruction()
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount

            If Not Options.SecondOrder Then Return

            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre

            For v As Integer = 0 To 4
                ' 依单元梯度（用最小二乘，避免 Gauss-Green 在边界单元上的偏差）
                Call LeastSquaresGradient(v)
            Next

            ' 限制器：把面重构值约束在"本单元与其邻居"的上下界内
            For v As Integer = 0 To 4
                For c As Integer = 0 To n - 1
                    _limiter(v)(c) = 1.0
                Next
            Next

            For c As Integer = 0 To n - 1
                Dim qmin(4) As Double
                Dim qmax(4) As Double

                For v As Integer = 0 To 4
                    qmin(v) = _prim(c)(v)
                    qmax(v) = _prim(c)(v)
                Next

                For e As Integer = mesh.RowPtr(c) To mesh.RowPtr(c + 1) - 1
                    Dim q As Integer = mesh.ColIdx(e)

                    If q = c Then Continue For

                    For v As Integer = 0 To 4
                        If _prim(q)(v) < qmin(v) Then qmin(v) = _prim(q)(v)
                        If _prim(q)(v) > qmax(v) Then qmax(v) = _prim(q)(v)
                    Next
                Next

                For e As Integer = mesh.RowPtr(c) To mesh.RowPtr(c + 1) - 1
                    Dim q As Integer = mesh.ColIdx(e)

                    If q = c Then Continue For

                    For v As Integer = 0 To 4
                        Dim d As Moira.CFDEngine.Eulerian.EulerianVertex = cc(q) - cc(c)
                        Dim dq As Double = _grad(v)(c)(0) * d.X + _grad(v)(c)(1) * d.Y + _grad(v)(c)(2) * d.Z
                        Dim base As Double = _prim(c)(v)

                        If std.Abs(dq) < 1.0E-30 Then Continue For

                        Dim ratio As Double

                        If dq > 0 Then
                            ratio = (qmax(v) - base) / dq
                        Else
                            ratio = (qmin(v) - base) / dq
                        End If

                        If ratio < _limiter(v)(c) Then
                            _limiter(v)(c) = std.Max(0.0, ratio)
                        End If
                    Next
                Next
            Next
        End Sub

        ''' <summary>原始变量 v（0=ρ,1=u,2=v,3=w,4=p）的最小二乘单元梯度。</summary>
        Private Sub LeastSquaresGradient(v As Integer)
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex() = mesh.CellCentre

            For c As Integer = 0 To n - 1
                Dim a11 As Double = 0, a12 As Double = 0, a13 As Double = 0
                Dim a22 As Double = 0, a23 As Double = 0, a33 As Double = 0
                Dim bx As Double = 0, by As Double = 0, bz As Double = 0
                Dim q0 As Double = _prim(c)(v)

                For e As Integer = mesh.RowPtr(c) To mesh.RowPtr(c + 1) - 1
                    Dim q As Integer = mesh.ColIdx(e)

                    If q = c Then Continue For

                    Dim d As Moira.CFDEngine.Eulerian.EulerianVertex = cc(q) - cc(c)
                    Dim dq As Double = _prim(q)(v) - q0

                    a11 += d.X * d.X
                    a12 += d.X * d.Y
                    a13 += d.X * d.Z
                    a22 += d.Y * d.Y
                    a23 += d.Y * d.Z
                    a33 += d.Z * d.Z

                    bx += d.X * dq
                    by += d.Y * dq
                    bz += d.Z * dq
                Next

                Dim tr As Double = a11 + a22 + a33

                If tr <= 1.0E-300 Then
                    _grad(v)(c)(0) = 0
                    _grad(v)(c)(1) = 0
                    _grad(v)(c)(2) = 0
                    Continue For
                End If

                Dim eps As Double = tr * 1.0E-10
                Dim r11 As Double = a11 + eps
                Dim r22 As Double = a22 + eps
                Dim r33 As Double = a33 + eps

                Dim det As Double = r11 * (r22 * r33 - a23 * a23) -
                                    a12 * (a12 * r33 - a23 * a13) +
                                    a13 * (a12 * a23 - r22 * a13)

                If std.Abs(det) < 1.0E-300 Then
                    _grad(v)(c)(0) = 0
                    _grad(v)(c)(1) = 0
                    _grad(v)(c)(2) = 0
                    Continue For
                End If

                Dim i11 As Double = (r22 * r33 - a23 * a23) / det
                Dim i22 As Double = (r11 * r33 - a13 * a13) / det
                Dim i33 As Double = (r11 * r22 - a12 * a12) / det
                Dim i12 As Double = (a13 * a23 - a12 * r33) / det
                Dim i13 As Double = (a12 * a23 - a13 * r22) / det
                Dim i23 As Double = (a13 * a12 - r11 * a23) / det

                _grad(v)(c)(0) = i11 * bx + i12 * by + i13 * bz
                _grad(v)(c)(1) = i12 * bx + i22 * by + i23 * bz
                _grad(v)(c)(2) = i13 * bx + i23 * by + i33 * bz
            Next
        End Sub

        ''' <summary>由单元 c 重构到面心 xf 的原始变量（写入 result，长度 5）。</summary>
        Private Sub Reconstruct(c As Integer, xf As Moira.CFDEngine.Eulerian.EulerianVertex, result As Double())
            If Not Options.SecondOrder Then
                For v As Integer = 0 To 4
                    result(v) = _prim(c)(v)
                Next

                Return
            End If

            Dim cc As Moira.CFDEngine.Eulerian.EulerianVertex = Me.Mesh.CellCentre(c)

            For v As Integer = 0 To 4
                Dim dq As Double = _grad(v)(c)(0) * (xf.X - cc.X) +
                                   _grad(v)(c)(1) * (xf.Y - cc.Y) +
                                   _grad(v)(c)(2) * (xf.Z - cc.Z)

                Dim q As Double = _prim(c)(v) + _limiter(v)(c) * dq

                If v = 0 OrElse v = 4 Then
                    q = std.Max(q, 1.0E-12)
                End If

                result(v) = q
            Next
        End Sub

        Private ReadOnly _leftState As Double() = New Double(4) {}
        Private ReadOnly _rightState As Double() = New Double(4) {}

#End Region

#Region "边界 ghost 状态"

        ''' <summary>取某 patch 的边界规格（未显式指定时按类型给出默认）。</summary>
        Public Function PatchSpec(geo As EulerianPatchGeometry) As CompressiblePatchSpec
            Dim spec As CompressiblePatchSpec = Nothing

            If Options.Patches.TryGetValue(geo.Name, spec) Then
                Return spec
            End If

            spec = New CompressiblePatchSpec()

            Select Case geo.PatchType
                Case EulerianPatchType.Inlet
                    spec.Kind = Options.InletFallback
                Case EulerianPatchType.Outlet
                    spec.Kind = Options.OutletFallback
                Case EulerianPatchType.Symmetry
                    spec.Kind = CompressibleBcKind.Symmetry
                Case Else
                    spec.Kind = Options.WallFallback
            End Select

            Return spec
        End Function

        ''' <summary>
        ''' 构造边界面 f 的 ghost 状态（原始量，写入 result，长度 5）；owner = P。
        ''' </summary>
        Private Sub BuildGhost(geo As EulerianPatchGeometry, k As Integer, spec As CompressiblePatchSpec, result As Double())

            Dim gamma As Double = Options.Gamma
            Dim rg As Double = Options.GasConstant
            Dim p As Integer = geo.OwnerCells(k)
            Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k).Normalized()

            Dim rho As Double = _prim(p)(0)
            Dim ux As Double = _prim(p)(1)
            Dim uy As Double = _prim(p)(2)
            Dim uz As Double = _prim(p)(3)
            Dim pr As Double = _prim(p)(4)

            Select Case spec.Kind

                Case CompressibleBcKind.WallSlip, CompressibleBcKind.Symmetry
                    ' 法向速度镜像（壁面不可穿透），切向不变
                    Dim un As Double = ux * nrm.X + uy * nrm.Y + uz * nrm.Z

                    result(0) = rho
                    result(1) = ux - 2.0 * un * nrm.X
                    result(2) = uy - 2.0 * un * nrm.Y
                    result(3) = uz - 2.0 * un * nrm.Z
                    result(4) = pr

                Case CompressibleBcKind.WallNoSlipAdiabatic
                    ' 全速度反号（无滑移）
                    result(0) = rho
                    result(1) = -ux
                    result(2) = -uy
                    result(3) = -uz
                    result(4) = pr

                Case CompressibleBcKind.InletSubsonic
                    ' 亚声速入口：外插静压，由总压总温等熵确定密度与速度大小
                    Dim p0 As Double = spec.TotalPressure
                    Dim t0 As Double = spec.TotalTemperature
                    Dim pIn As Double = std.Min(std.Max(pr, 1.0), p0 * 0.999999)

                    Dim tIn As Double = t0 * std.Pow(pIn / p0, (gamma - 1.0) / gamma)
                    Dim rhoIn As Double = pIn / (rg * std.Max(tIn, 1.0))
                    Dim cp As Double = gamma * rg / (gamma - 1.0)
                    Dim speedSq As Double = 2.0 * cp * (t0 - tIn)
                    Dim speed As Double = If(speedSq > 0, std.Sqrt(speedSq), 0)
                    Dim d As Moira.CFDEngine.Eulerian.EulerianVertex = spec.Direction.Normalized()

                    If d.Length <= 0 Then
                        d = New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)
                    End If

                    result(0) = rhoIn
                    result(1) = d.X * speed
                    result(2) = d.Y * speed
                    result(3) = d.Z * speed
                    result(4) = pIn

                Case CompressibleBcKind.InletSupersonic
                    ' 超声速入口：全部给定（等熵关系由总压总温与入口马赫数给出）
                    Dim d As Moira.CFDEngine.Eulerian.EulerianVertex = spec.Direction.Normalized()

                    If d.Length <= 0 Then
                        d = New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)
                    End If

                    Dim m As Double = std.Max(spec.Mach, 1.0E-6)
                    Dim factor As Double = 1.0 + 0.5 * (gamma - 1.0) * m * m
                    Dim pIn As Double = spec.TotalPressure / std.Pow(factor, gamma / (gamma - 1.0))
                    Dim tIn As Double = spec.TotalTemperature / factor
                    Dim rhoIn As Double = pIn / (rg * std.Max(tIn, 1.0))
                    Dim aIn As Double = std.Sqrt(gamma * pIn / rhoIn)
                    Dim speed As Double = m * aIn

                    result(0) = rhoIn
                    result(1) = d.X * speed
                    result(2) = d.Y * speed
                    result(3) = d.Z * speed
                    result(4) = pIn

                Case CompressibleBcKind.OutletSubsonic
                    ' 亚声速出口：给定静压，密度与速度外插
                    result(0) = rho
                    result(1) = ux
                    result(2) = uy
                    result(3) = uz
                    result(4) = spec.StaticPressure

                Case Else
                    ' 超声速出口：全外插
                    result(0) = rho
                    result(1) = ux
                    result(2) = uy
                    result(3) = uz
                    result(4) = pr
            End Select
        End Sub

#End Region

#Region "右端项（面通量求和）"

        Private Sub ComputeRhs()
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim gamma As Double = Options.Gamma
            Dim scheme As ICompressibleFluxScheme = Options.Scheme
            Dim flux(4) As Double

            For i As Integer = 0 To ConservedIdx.Count - 1
                Array.Clear(_rhs(i), 0, n)
            Next

            ' ---------- 内部面 ----------
            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim P As Integer = mesh.InternalOwner(i)
                Dim Q As Integer = mesh.InternalNeighbour(i)
                Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = mesh.Nf(f)
                Dim mag As Double = mesh.MagSf(f)

                Call Reconstruct(P, mesh.Cf(f), _leftState)
                Call Reconstruct(Q, mesh.Cf(f), _rightState)
                Call scheme.ComputeFlux(gamma, _leftState, _rightState, nrm.X, nrm.Y, nrm.Z, flux)

                For v As Integer = 0 To 4
                    Dim c As Double = flux(v) * mag

                    _rhs(v)(P) -= c
                    _rhs(v)(Q) += c
                Next
            Next

            ' ---------- 边界面 ----------
            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim spec As CompressiblePatchSpec = PatchSpec(geo)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim P As Integer = geo.OwnerCells(k)
                    Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = geo.Sf(k).Normalized()
                    Dim mag As Double = geo.MagSf(k)

                    Call Reconstruct(P, geo.Cf(k), _leftState)
                    Call BuildGhost(geo, k, spec, _rightState)

                    Call scheme.ComputeFlux(gamma, _leftState, _rightState, nrm.X, nrm.Y, nrm.Z, flux)

                    For v As Integer = 0 To 4
                        _rhs(v)(P) -= flux(v) * mag
                    Next
                Next
            Next

            ' ---------- 除以体积 ----------
            Dim vol As Double() = mesh.CellVolume

            For v As Integer = 0 To 4
                Dim r As Double() = _rhs(v)

                For c As Integer = 0 To n - 1
                    r(c) /= vol(c)
                Next
            Next
        End Sub

#End Region

#Region "时间推进"

        ''' <summary>按 CFL 计算逐单元时间步长（稳态用局部步长，瞬态用全局最小步长）。</summary>
        Private Function ComputeTimeSteps() As Double
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim gamma As Double = Options.Gamma
            Dim vol As Double() = mesh.CellVolume
            Dim spectral As Double() = _residualBuf

            Array.Clear(spectral, 0, n)

            ' 谱半径通量：Σ_f (|u_n| + a)·|S_f|
            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim P As Integer = mesh.InternalOwner(i)
                Dim Q As Integer = mesh.InternalNeighbour(i)
                Dim nrm As Moira.CFDEngine.Eulerian.EulerianVertex = mesh.Nf(f)
                Dim mag As Double = mesh.MagSf(f)

                Dim lam As Double = SpectralRadius(P, nrm, mag, gamma) + SpectralRadius(Q, nrm, mag, gamma)

                spectral(P) += 0.5 * lam
                spectral(Q) += 0.5 * lam
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim P As Integer = geo.OwnerCells(k)

                    spectral(P) += SpectralRadius(P, geo.Sf(k).Normalized(), geo.MagSf(k), gamma)
                Next
            Next

            Dim dtMin As Double = Double.MaxValue

            For c As Integer = 0 To n - 1
                Dim s As Double = spectral(c)
                Dim dt As Double = If(s > 0, Options.Cfl * vol(c) / s, Options.Cfl * 1.0E-6)

                _dt(c) = std.Max(dt, 1.0E-30)

                If _dt(c) < dtMin Then dtMin = _dt(c)
            Next

            Return dtMin
        End Function

        Private Function SpectralRadius(cell As Integer, nrm As Moira.CFDEngine.Eulerian.EulerianVertex,
                                        mag As Double, gamma As Double) As Double

            Dim rho As Double = _prim(cell)(0)
            Dim p As Double = _prim(cell)(4)
            Dim a As Double = std.Sqrt(gamma * p / std.Max(rho, 1.0E-30))
            Dim un As Double = _prim(cell)(1) * nrm.X + _prim(cell)(2) * nrm.Y + _prim(cell)(3) * nrm.Z

            Return (std.Abs(un) + a) * mag
        End Function

        ''' <summary>
        ''' 执行一个时间步（显式 SSP-RK3）。
        ''' 稳态时使用局部时间步长（每个单元按自身 CFL 前进），加速收敛到定常解。
        ''' </summary>
        Public Sub Advance()
            Call UpdatePrimitive()
            Call BuildReconstruction()

            Dim dtGlobal As Double = ComputeTimeSteps()

            If Not Options.SteadyState Then
                ' 瞬态：全局统一时间步（保证时间精度）；
                ' 显式给定 FixedDeltaT 时使用它，否则用 CFL 推出的全局最小步长。
                If Options.FixedDeltaT > 0 Then
                    dtGlobal = Options.FixedDeltaT
                End If

                For c As Integer = 0 To _dt.Length - 1
                    _dt(c) = dtGlobal
                Next
            End If

            ' ★ 注意：_u 是 Double()()（5 个守恒分量），_u.Length = 5 是**分量数**，
            '   不是单元数。单元数必须用 _u(v).Length / Mesh.CellCount，
            '   否则只更新前 5 个单元（曾导致 Sod 算例状态完全不演化）。
            Dim nCells As Integer = _u(0).Length

            ' ---- RK3 第一阶段 ----
            Call ComputeRhs()
            Call SaveState(_uOld)

            For v As Integer = 0 To 4
                For c As Integer = 0 To nCells - 1
                    _u(v)(c) = _uOld(v)(c) + _dt(c) * _rhs(v)(c)
                Next
            Next

            Call UpdatePrimitive()
            Call BuildReconstruction()

            ' ---- RK3 第二阶段 ----
            Call ComputeRhs()

            For v As Integer = 0 To 4
                For c As Integer = 0 To nCells - 1
                    _u(v)(c) = 0.75 * _uOld(v)(c) + 0.25 * (_u(v)(c) + _dt(c) * _rhs(v)(c))
                Next
            Next

            Call UpdatePrimitive()
            Call BuildReconstruction()

            ' ---- RK3 第三阶段 ----
            Call ComputeRhs()

            For v As Integer = 0 To 4
                For c As Integer = 0 To nCells - 1
                    _u(v)(c) = (1.0 / 3.0) * _uOld(v)(c) + (2.0 / 3.0) * (_u(v)(c) + _dt(c) * _rhs(v)(c))
                Next
            Next

            Call UpdatePrimitive()

            StepCount += 1

            If Not Options.SteadyState Then
                Time += dtGlobal
            End If

            ResidualHistory.Add(ResidualNorm())
        End Sub

        Private Sub SaveState(dest As Double()())
            For v As Integer = 0 To 4
                Array.Copy(_u(v), dest(v), _u(v).Length)
            Next
        End Sub

        ''' <summary>密度方程残差的归一化 L2 范数。</summary>
        Private Function ResidualNorm() As Double
            Dim n As Integer = Mesh.CellCount
            Dim r As Double() = _rhs(ConservedIdx.Rho)
            Dim s As Double = 0
            Dim ref As Double = 0

            For c As Integer = 0 To n - 1
                s += r(c) * r(c)
                ref += _u(ConservedIdx.Rho)(c) * _u(ConservedIdx.Rho)(c)
            Next

            Dim den As Double = std.Max(std.Sqrt(ref / std.Max(1, n)), 1.0E-30)

            Return std.Sqrt(s / std.Max(1, n)) / den
        End Function

        ''' <summary>推进 <paramref name="steps"/> 步。</summary>
        Public Sub Run(steps As Integer, Optional progress As Action(Of Integer, Double) = Nothing)
            For i As Integer = 1 To std.Max(0, steps)
                Call Advance()

                If progress IsNot Nothing AndAlso ResidualHistory.Count > 0 Then
                    Call progress(StepCount, ResidualHistory(ResidualHistory.Count - 1))
                End If
            Next

            Call SyncFields()
        End Sub

        ''' <summary>
        ''' 推进到稳态：残差下降 <paramref name="tolerance"/> 或达到 <paramref name="maxSteps"/>。
        ''' </summary>
        Public Function Solve(Optional maxSteps As Integer = 5000,
                              Optional tolerance As Double = 1.0E-6,
                              Optional progress As Action(Of Integer, Double) = Nothing) As Boolean

            Dim converged As Boolean = False

            For i As Integer = 1 To std.Max(1, maxSteps)
                Call Advance()

                Dim res As Double = ResidualHistory(ResidualHistory.Count - 1)

                If progress IsNot Nothing Then
                    Call progress(StepCount, res)
                End If

                If res <= tolerance Then
                    converged = True
                    Exit For
                End If
            Next

            Call SyncFields()

            Return converged
        End Function

#End Region

#Region "诊断"

        ''' <summary>气动诊断：极端 Mach 数与残差。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(256)

                sb.AppendLine($"compressible ({Options.Scheme.Name}), steps={StepCount}, " &
                              $"time={Time.ToString("G6", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  max Mach = {MaxMach.ToString("G6", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  density residual = {If(ResidualHistory.Count > 0, ResidualHistory(ResidualHistory.Count - 1), 0).ToString("E3", CultureInfo.InvariantCulture)}")

                Return sb.ToString()
            End Get
        End Property

#End Region

    End Class

#End Region

End Namespace
