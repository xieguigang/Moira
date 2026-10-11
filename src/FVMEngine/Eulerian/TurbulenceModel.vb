' /********************************************************************************/
'
'   TurbulenceModel.vb
'
'   湍流模型层 —— RANS 两方程模型（docs\FVM_Eulerian.md §七）。
'
'   实现：
'       LaminarModel     层流（νt ≡ 0），用于低 Re 验证与湍流模型的对照组。
'       KEpsilonModel    标准高 Re k-ε + 对数律壁面函数。
'                        Cmu=0.09, C1=1.44, C2=1.92, σk=1.0, σε=1.3
'       KOmegaSSTModel   Menter k-ω SST（F1/F2 混合 + SST 粘度限制器 +
'                        雷诺应力/生产限制器 + ω 壁面函数）。
'                        适合 F1 后翼/扩散器与建筑侧风分离这类逆压梯度流动。
'
'   ★ 方程形式（全为"运动学"形式，即 ρ 已并入通量；k/ε/ω 为比质量量）
'       k : div(φu, k)  − div((ν + νt/σk)·∇k)   = Pk − ε
'       ε : div(φu, ε)  − div((ν + νt/σε)·∇ε)   = C1 (ε/k) Pk − C2 ε²/k
'       ω : div(φu, ω)  − div((ν + σω·νt)·∇ω)   = γ S² − β ω² + 2(1−F1)σω2·(∇k·∇ω)/ω
'       其中 φ 为体积通量（u·S），Pk = 2νt·SijSij，S² = 2SijSij。
'
'   ★ 离散约定（与 EulerianOperators 严格一致）
'       调用方写作  FvmDiv(m, flux, φ) + FvmLaplacian(m, φ, Γ) = source
'       即算子已经把"−div(Γ∇φ)"的负号含在 FvmLaplacian 内部（见该算子文档）。
'       source 与隐式汇项必须乘上单元体积 V_P（体积分形式）。
'
'   ★ 壁面处理（高 Re 壁面函数）
'       壁面相邻单元（y = WallDistanceNormal）：
'         y+ = Cmu^0.25·√k·y/ν
'         对数层：ε_w = Cmu^0.75 k^1.5/(κ y)，Pk|_w = Cmu^0.5·k·|u_P|/y
'         黏性底层（y+ < 11.06）：ε_w = 2νk/y²，Pk|_w = 0
'       k 的壁面边界为零梯度；ε/ω 的壁面边界为逐面定值（写入 WallFunctionBc）。
'
'   混合精度契约：
'       湍流模型的闭包（exp / pow / 比值 / 混合函数）全部 double —— 属于
'       "精度敏感、非热点"的一侧；只有其中的线性方程内迭代下放到 GPU float32。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports std = System.Math

Namespace Eulerian

    ''' <summary>湍流模型的求解上下文（物性、时间步、离散格式与线性求解参数）。</summary>
    Public Class TurbulenceContext

        ''' <summary>密度（仅用于时间项与诊断；输运方程本身为运动学形式）。</summary>
        Public Property Rho As Double = 1000.0

        ''' <summary>运动粘度 ν = μ/ρ。</summary>
        Public Property Nu As Double = 1.0E-6

        ''' <summary>伪/真实时间步长（&gt; 0 时加入 Euler 隐式时间项）。</summary>
        Public Property DeltaT As Double = 0

        ''' <summary>对流格式。</summary>
        Public Property Scheme As EulerianConvectionScheme = EulerianConvectionScheme.Upwind

        ''' <summary>湍流方程的欠松弛因子。</summary>
        Public Property Relaxation As Double = 0.7

        ''' <summary>线性求解最大迭代数。</summary>
        Public Property MaxIterations As Integer = 200

        ''' <summary>线性求解容差（归一化残差）。</summary>
        Public Property Tolerance As Double = 1.0E-9

        ''' <summary>是否允许使用 GPU float32 后端。</summary>
        Public Property UseGpu As Boolean = True

        ''' <summary>线性求解器种类。</summary>
        Public Property LinearSolver As EulerianLinearSolverKind = EulerianLinearSolverKind.Auto

        ''' <summary>von Kármán 常数。</summary>
        Public Property Kappa As Double = 0.41

        ''' <summary>对数律常数 E（光滑壁面 ≈ 9.8）。</summary>
        Public Property ELog As Double = 9.8

        ''' <summary>对数层判据 y+ 阈值（光滑壁面 ≈ 11.06）。</summary>
        Public Property YPlusLam As Double = 11.06

        ''' <summary>是否启用壁面函数（关闭时按"低 Re 直解"处理：壁面 k/ε/ω 全零梯度）。</summary>
        Public Property UseWallFunctions As Boolean = True

        ''' <summary>k 的下限。</summary>
        Public Property KMin As Double = 1.0E-12

        ''' <summary>ε 的下限。</summary>
        Public Property EpsilonMin As Double = 1.0E-12

        ''' <summary>ω 的下限。</summary>
        Public Property OmegaMin As Double = 1.0E-6

    End Class

    ''' <summary>
    ''' 湍流模型基类：提供共享的应变/生产项计算、通用标量输运求解与壁面邻域访问。
    ''' </summary>
    Public MustInherit Class EulerianTurbulenceModel

#Region "构造与身份"

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>壁面距离场（F1/F2 混合与壁面函数的基础）。</summary>
        Public ReadOnly Property WallDistance As EulerianWallDistance

        ''' <summary>模型名（报告与日志）。</summary>
        Public MustOverride ReadOnly Property Name As String

        ''' <summary>上一轮各个输运方程的求解报告（诊断用）。</summary>
        Public ReadOnly Property LastReports As List(Of EulerianSolverReport)

        Protected Sub New(mesh As EulerianFvMesh, wallDistance As EulerianWallDistance)
            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))

            Me.Mesh = mesh
            Me.WallDistance = If(wallDistance, EulerianWallDistance.Build(mesh))
            Me.LastReports = New List(Of EulerianSolverReport)
        End Sub

#End Region

#Region "应变与生产"

        Private _s11 As Double()
        Private _s22 As Double()
        Private _s33 As Double()
        Private _s12 As Double()
        Private _s13 As Double()
        Private _s23 As Double()
        Private _sSquared As Double()

        ''' <summary>上一次计算的应变不变量 S² = 2·Sij·Sij（未计算时返回全零）。</summary>
        Public ReadOnly Property StrainSquared As Double()
            Get
                If _sSquared Is Nothing Then
                    EnsureStrainBuffers(Mesh.CellCount)
                End If

                Return _sSquared
            End Get
        End Property

        ''' <summary>
        ''' 由速度场计算对称应变分量与 S² = 2·Sij·Sij。
        ''' 结果缓存于内部，供各模型复用（每轮外迭代调用一次）。
        ''' </summary>
        Public Sub ComputeStrain(u As EulerianField)
            Dim n As Integer = Mesh.CellCount

            EnsureStrainBuffers(n)
            Array.Clear(_s11, 0, n) : Array.Clear(_s22, 0, n) : Array.Clear(_s33, 0, n)
            Array.Clear(_s12, 0, n) : Array.Clear(_s13, 0, n) : Array.Clear(_s23, 0, n)
            Array.Clear(_sSquared, 0, n)

            Dim ux As Double() = New Double(n - 1) {}
            Dim uy As Double() = New Double(n - 1) {}
            Dim uz As Double() = New Double(n - 1) {}

            Call EulerianOperators.CellGradientLeastSquares(u, 0, ux, uy, uz)

            Dim vx As Double() = Nothing
            Dim vy As Double() = Nothing
            Dim vz As Double() = Nothing

            If u.Components >= 2 Then
                vx = New Double(n - 1) {}
                vy = New Double(n - 1) {}
                vz = New Double(n - 1) {}
                Call EulerianOperators.CellGradientLeastSquares(u, 1, vx, vy, vz)
            End If

            Dim wx As Double() = Nothing
            Dim wy As Double() = Nothing
            Dim wz As Double() = Nothing

            If u.Components >= 3 Then
                wx = New Double(n - 1) {}
                wy = New Double(n - 1) {}
                wz = New Double(n - 1) {}
                Call EulerianOperators.CellGradientLeastSquares(u, 2, wx, wy, wz)
            End If

            For c As Integer = 0 To n - 1
                Dim s11 As Double = ux(c)
                Dim s22 As Double = If(vx IsNot Nothing, vy(c), 0)
                Dim s33 As Double = If(wx IsNot Nothing, wz(c), 0)
                Dim s12 As Double = 0.5 * (If(vx IsNot Nothing, uy(c) + vx(c), 0))
                Dim s13 As Double = 0.5 * (If(wx IsNot Nothing, uz(c) + wx(c), 0))
                Dim s23 As Double = 0.5 * (If(wx IsNot Nothing, If(vx IsNot Nothing, vz(c) + wy(c), 0), 0))

                _s11(c) = s11
                _s22(c) = s22
                _s33(c) = s33
                _s12(c) = s12
                _s13(c) = s13
                _s23(c) = s23

                _sSquared(c) = 2.0 * (s11 * s11 + s22 * s22 + s33 * s33 +
                                      2.0 * (s12 * s12 + s13 * s13 + s23 * s23))
            Next
        End Sub

        Private Sub EnsureStrainBuffers(n As Integer)
            If _s11 IsNot Nothing AndAlso _s11.Length = n Then Return

            _s11 = New Double(n - 1) {}
            _s22 = New Double(n - 1) {}
            _s33 = New Double(n - 1) {}
            _s12 = New Double(n - 1) {}
            _s13 = New Double(n - 1) {}
            _s23 = New Double(n - 1) {}
            _sSquared = New Double(n - 1) {}
        End Sub

        ''' <summary>
        ''' 生产项 Pk = 2νt·SijSij（运动学），带可选的 SST 生产限制器。
        ''' </summary>
        Protected Sub ComputeProduction(nut As EulerianField, production As Double(),
                                        Optional limiter As Double = 0)

            Dim n As Integer = Mesh.CellCount

            For c As Integer = 0 To n - 1
                Dim p As Double = nut.Value(c) * _sSquared(c)

                If limiter > 0 Then
                    Dim cap As Double = limiter

                    If p > cap Then p = cap
                End If

                production(c) = p
            Next
        End Sub

#End Region

#Region "壁面邻域"

        ''' <summary>壁面相邻单元数量。</summary>
        Public ReadOnly Property WallAdjacentCellCount As Integer
            Get
                Return WallDistance.WallAdjacentCellCount
            End Get
        End Property

        ''' <summary>单元 c 的壁面函数取值（y、y+、uTau、ε_w、Pk_w）。</summary>
        Protected Structure WallCellData
            Public Y As Double
            Public YPlus As Double
            Public UTau As Double
            Public EpsilonWall As Double
            Public OmegaWall As Double
            Public ProductionWall As Double
            Public InLogLayer As Boolean
        End Structure

        ''' <summary>
        ''' 计算壁面相邻单元的壁面函数量。
        ''' </summary>
        ''' <param name="kField">k 场</param>
        ''' <param name="uField">速度场（取邻壁单元速度模长）</param>
        ''' <param name="ctx">上下文</param>
        ''' <param name="data">输出（长度 = CellCount，只有壁面相邻单元被填充）</param>
        Protected Sub ComputeWallCellData(kField As EulerianField, uField As EulerianField,
                                         ctx As TurbulenceContext, data As WallCellData())

            Dim n As Integer = Mesh.CellCount
            Dim nu As Double = std.Max(ctx.Nu, 1.0E-30)
            Dim c25 As Double = std.Pow(KECst.Cmu, 0.25)
            Dim c75 As Double = std.Pow(KECst.Cmu, 0.75)
            Dim kappa As Double = std.Max(ctx.Kappa, 1.0E-6)

            For c As Integer = 0 To n - 1
                If Not WallDistance.IsWallAdjacent(c) Then
                    data(c).Y = 0
                    Continue For
                End If

                Dim y As Double = std.Max(WallDistance.WallDistanceNormal(c), 1.0E-30)
                Dim k As Double = std.Max(kField.Value(c), ctx.KMin)
                Dim uTau As Double = c25 * std.Sqrt(k)
                Dim yPlus As Double = uTau * y / nu
                Dim inLog As Boolean = yPlus > ctx.YPlusLam

                data(c).Y = y
                data(c).YPlus = yPlus
                data(c).UTau = uTau
                data(c).InLogLayer = inLog

                If inLog Then
                    data(c).EpsilonWall = c75 * std.Pow(k, 1.5) / (kappa * y)
                    data(c).OmegaWall = std.Sqrt(k) / (c25 * kappa * y)
                    data(c).ProductionWall = std.Sqrt(KECst.Cmu) * k * VelocityMagnitude(uField, c) / y
                Else
                    data(c).EpsilonWall = 2.0 * nu * k / (y * y)
                    data(c).OmegaWall = 6.0 * nu / (SSTCst.Beta1 * y * y)
                    data(c).ProductionWall = 0
                End If
            Next
        End Sub

        ''' <summary>邻壁单元的速度模长。</summary>
        Protected Shared Function VelocityMagnitude(u As EulerianField, cell As Integer) As Double
            If u Is Nothing Then Return 0

            Dim ux As Double = u.Value(cell, 0)
            Dim uy As Double = If(u.Components >= 2, u.Value(cell, 1), 0)
            Dim uz As Double = If(u.Components >= 3, u.Value(cell, 2), 0)

            Return std.Sqrt(ux * ux + uy * uy + uz * uz)
        End Function

#End Region

#Region "通用标量输运求解"

        ''' <summary>
        ''' 求解一个湍流标量方程：
        '''     div(φu,φ) − div(Γ∇φ) + Sp·φ = Su
        ''' 其中 <paramref name="implicitSink"/>（≥0，已含体积因子）加到对角，
        ''' <paramref name="explicitSource"/>（已含体积因子）加到右端。
        ''' </summary>
        Protected Function SolveScalarField(phi As EulerianField, flux As Double(), gammaFace As Double(),
                                            implicitSink As Double(), explicitSource As Double(),
                                            ctx As TurbulenceContext) As EulerianSolverReport

            Dim prev As Double() = phi.ToComponent(0)

            Call phi.UpdateBoundaries()

            Dim m As New FvMatrix(Mesh, phi.Name)

            If ctx.DeltaT > 0 Then
                Call EulerianOperators.FvmDdt(m, phi, prev, 0, ctx.DeltaT, ctx.Rho)
            End If

            Call EulerianOperators.FvmDiv(m, flux, phi, 0, ctx.Scheme)
            ' div(φu,φ) − div(Γ∇φ)：FvmLaplacian 本身即 −div(Γ∇φ)（见其文档）
            Call EulerianOperators.FvmLaplacian(m, phi, 0, gammaFace)
            Call EulerianOperators.FvmSp(m, implicitSink, phi, 0)
            Call EulerianOperators.FvcSource(m, explicitSource)
            Call m.Relax(ctx.Relaxation, prev)

            Dim x As Double() = prev
            Dim rpt As EulerianSolverReport = EulerianLinearSolve.Solve(
                m, x, ctx.LinearSolver, ctx.Tolerance, 0.0, ctx.MaxIterations, 0, ctx.UseGpu)

            Call phi.SetFromComponent(0, x)
            Call phi.UpdateBoundaries()

            Return rpt
        End Function

        ''' <summary>
        ''' 有效运动粘度面值：ν_eff,f = ν_f + νt_f（长度 = FaceCount）。
        ''' 用于动量方程的扩散项与湍流方程的 Γ。
        ''' </summary>
        Public Function EffectiveNuFace(nuCell As Double(), nut As EulerianField) As Double()
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim nF As Integer = mesh.FaceCount
            Dim r(nF - 1) As Double
            Dim w As Double() = mesh.InterpWeight
            Dim ifc As Integer() = mesh.InternalFaces
            Dim ow As Integer() = mesh.InternalOwner
            Dim nb As Integer() = mesh.InternalNeighbour
            Dim nt As Double() = nut.Values

            For i As Integer = 0 To ifc.Length - 1
                Dim wi As Double = w(i)
                Dim nuF As Double = wi * nuCell(ow(i)) + (1.0 - wi) * nuCell(nb(i))
                Dim ntF As Double = wi * nt(ow(i)) + (1.0 - wi) * nt(nb(i))

                r(ifc(i)) = nuF + ntF
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    Dim own As Integer = geo.OwnerCells(k)

                    r(geo.FaceIndices(k)) = nuCell(own) + nt(own)
                Next
            Next

            Return r
        End Function

        ''' <summary>把湍流标量限幅到物理下限（收敛稳健性关键）。</summary>
        Protected Shared Sub BoundField(phi As EulerianField, lower As Double)
            Dim v As Double() = phi.Values

            For i As Integer = 0 To v.Length - 1
                If Double.IsNaN(v(i)) OrElse v(i) < lower Then
                    v(i) = lower
                End If
            Next
        End Sub

        ''' <summary>诊断摘要（模型名 + 各方程报告）。</summary>
        Public Overridable Function Diagnostics(fields As EulerianFieldSet) As String
            Dim sb As New StringBuilder(256)

            sb.AppendLine($"turbulence model: {Name}")

            For Each r As EulerianSolverReport In LastReports
                sb.AppendLine($"  {r.ToString()}")
            Next

            Return sb.ToString()
        End Function

#End Region

#Region "抽象接口"

        ''' <summary>用当前湍流量更新 νt 场。</summary>
        Public MustOverride Sub UpdateViscosity(fields As EulerianFieldSet, ctx As TurbulenceContext)

        ''' <summary>求解湍流量输运方程（就地更新 k 与 ε/ω）。</summary>
        Public MustOverride Sub SolveTransport(fields As EulerianFieldSet, flux As Double(), ctx As TurbulenceContext)

#End Region

    End Class

#Region "常数表"

    ''' <summary>标准 k-ε 常数。</summary>
    Public Module KECst
        Public Const Cmu As Double = 0.09
        Public Const C1 As Double = 1.44
        Public Const C2 As Double = 1.92
        Public Const SigmaK As Double = 1.0
        Public Const SigmaE As Double = 1.3

        ''' <summary>
        ''' 湍流粘度比上限 νt/ν。工业 CFD 的标准稳健化手段：
        ''' 掐断"ε 被过度耗散 → νt = Cμk²/ε 失控"的正反馈。
        ''' </summary>
        Public Const MaxNutRatio As Double = 1.0E5
    End Module

    ''' <summary>k-ω SST 常数（Menter 1994 / 2003）。</summary>
    Public Module SSTCst
        Public Const A1 As Double = 0.31
        Public Const BetaStar As Double = 0.09
        Public Const Kappa As Double = 0.41
        ' 内层（k-ω）
        Public Const SigmaK1 As Double = 0.85
        Public Const SigmaOmega1 As Double = 0.5
        Public Const Beta1 As Double = 0.075
        Public Const Gamma1 As Double = 0.5555555555555556    ' 5/9
        ' 外层（k-ε）
        Public Const SigmaK2 As Double = 1.0
        Public Const SigmaOmega2 As Double = 0.856
        Public Const Beta2 As Double = 0.0828
        Public Const Gamma2 As Double = 0.44
    End Module

#End Region

#Region "层流"

    ''' <summary>层流模型：νt ≡ 0，不做任何湍流输运求解。</summary>
    Public Class LaminarTurbulenceModel
        Inherits EulerianTurbulenceModel

        Public Sub New(mesh As EulerianFvMesh, Optional wallDistance As EulerianWallDistance = Nothing)
            MyBase.New(mesh, wallDistance)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return "laminar"
            End Get
        End Property

        Public Overrides Sub UpdateViscosity(fields As EulerianFieldSet, ctx As TurbulenceContext)
            fields.Nut.Zero()
        End Sub

        Public Overrides Sub SolveTransport(fields As EulerianFieldSet, flux As Double(), ctx As TurbulenceContext)
            LastReports.Clear()
        End Sub
    End Class

#End Region

#Region "标准 k-ε"

    ''' <summary>标准高 Re k-ε 模型（含对数律壁面函数）。</summary>
    Public Class KEpsilonModel
        Inherits EulerianTurbulenceModel

        Public Sub New(mesh As EulerianFvMesh, Optional wallDistance As EulerianWallDistance = Nothing)
            MyBase.New(mesh, wallDistance)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return "standard k-epsilon (wall functions)"
            End Get
        End Property

        Private _production As Double()
        Private _wallData As WallCellData()
        Private _gammaK As Double()
        Private _gammaE As Double()
        Private _sink As Double()
        Private _source As Double()

        Private Sub EnsureScratch(n As Integer)
            If _production IsNot Nothing AndAlso _production.Length = n Then Return

            _production = New Double(n - 1) {}
            _wallData = New WallCellData(n - 1) {}
            _sink = New Double(n - 1) {}
            _source = New Double(n - 1) {}
        End Sub

        ''' <summary>νt = Cmu·k²/ε。</summary>
        Public Overrides Sub UpdateViscosity(fields As EulerianFieldSet, ctx As TurbulenceContext)
            Dim n As Integer = Mesh.CellCount

            Call BoundField(fields.K, ctx.KMin)
            Call BoundField(fields.Epsilon, ctx.EpsilonMin)

            Dim k As Double() = fields.K.Values
            Dim e As Double() = fields.Epsilon.Values
            Dim nt As Double() = fields.Nut.Values

            For c As Integer = 0 To n - 1
                Dim eps As Double = std.Max(e(c), ctx.EpsilonMin)
                Dim v As Double = KECst.Cmu * k(c) * k(c) / eps

                If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then
                    v = 0
                End If

                ' ★ 湍流粘度比限幅 νt ≤ MaxNutRatio·ν（工业 CFD 的标准稳健化手段）
                '   k-ε 在粗网格 / 强非正交切割单元上会出现 ε 被过度耗散、
                '   νt 失控增长（k²/ε 发散）的正反馈，最终把动量与压力方程一起带崩。
                '   限幅掐断这个正反馈：粗网格上只损失少量精度，换来的是不发散。
                Dim nuMax As Double = KECst.MaxNutRatio * std.Max(ctx.Nu, 1.0E-30)

                If v > nuMax Then
                    v = nuMax
                End If

                nt(c) = v
            Next
        End Sub

        ''' <summary>求解 k 与 ε 方程。</summary>
        Public Overrides Sub SolveTransport(fields As EulerianFieldSet, flux As Double(), ctx As TurbulenceContext)
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume

            Call EnsureScratch(n)
            Call LastReports.Clear()

            ' ---- 生产项（含壁面函数的对数律替换） ----
            Call ComputeProduction(fields.Nut, _production)

            If ctx.UseWallFunctions Then
                Call ComputeWallCellData(fields.K, fields.U, ctx, _wallData)

                For c As Integer = 0 To n - 1
                    If _wallData(c).Y > 0 Then
                        _production(c) = _wallData(c).ProductionWall
                    End If
                Next
            End If

            ' ---- k 方程：div(φu,k) − div((ν + νt/σk)∇k) + (ε/k)·k = Pk ----
            _gammaK = EulerianOperators.FaceGamma(mesh, ScaledViscosity(fields.Nut, 1.0 / KECst.SigmaK, ctx))

            Dim kField As EulerianField = fields.K
            Dim eField As EulerianField = fields.Epsilon

            For c As Integer = 0 To n - 1
                Dim kk As Double = std.Max(kField.Value(c), ctx.KMin)
                Dim ee As Double = std.Max(eField.Value(c), ctx.EpsilonMin)

                _sink(c) = vol(c) * ee / kk
                _source(c) = vol(c) * _production(c)
            Next

            Call LastReports.Add(SolveScalarField(kField, flux, _gammaK, _sink, _source, ctx))
            Call BoundField(kField, ctx.KMin)

            ' ---- ε 方程：div(φu,ε) − div((ν + νt/σε)∇ε) + C2(ε/k)·ε = C1(ε/k)·Pk ----
            _gammaE = EulerianOperators.FaceGamma(mesh, ScaledViscosity(fields.Nut, 1.0 / KECst.SigmaE, ctx))

            If ctx.UseWallFunctions Then
                Call ApplyEpsilonWallBoundary(eField, ctx)
            End If

            For c As Integer = 0 To n - 1
                Dim kk As Double = std.Max(kField.Value(c), ctx.KMin)
                Dim ee As Double = std.Max(eField.Value(c), ctx.EpsilonMin)
                Dim ratio As Double = ee / kk

                _sink(c) = vol(c) * KECst.C2 * ratio
                _source(c) = vol(c) * KECst.C1 * ratio * _production(c)
            Next

            Call LastReports.Add(SolveScalarField(eField, flux, _gammaE, _sink, _source, ctx))
            Call BoundField(eField, ctx.EpsilonMin)
        End Sub

        ''' <summary>把 νt 按系数缩放后的场（用于 Γ = ν + νt/σ）。</summary>
        Private Function ScaledViscosity(nut As EulerianField, factor As Double, ctx As TurbulenceContext) As EulerianField
            Dim n As Integer = Mesh.CellCount
            Dim f As New EulerianField(Mesh, "nuEff_tmp", 1, EulerianFieldRole.TurbulentViscosity)
            Dim nt As Double() = nut.Values
            Dim v As Double() = f.Values

            For c As Integer = 0 To n - 1
                v(c) = ctx.Nu + nt(c) * factor
            Next

            Return f
        End Function

        ''' <summary>
        ''' 把壁面 ε 值写入 WallFunctionBc（逐面定值），并覆盖邻壁单元的 ε（对数律约束）。
        ''' </summary>
        Private Sub ApplyEpsilonWallBoundary(eField As EulerianField, ctx As TurbulenceContext)
            Dim mesh As EulerianFvMesh = Me.Mesh

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim pf As EulerianPatchField = eField.Boundaries(pi)
                Dim wf As WallFunctionBc = TryCast(pf.Condition, WallFunctionBc)

                If wf Is Nothing Then
                    Continue For
                End If

                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim n As Integer = geo.FaceCount
                Dim vals(n - 1) As Double

                For k As Integer = 0 To n - 1
                    Dim own As Integer = geo.OwnerCells(k)

                    vals(k) = std.Max(_wallData(own).EpsilonWall, ctx.EpsilonMin)
                Next

                Call wf.SetValues(vals)
                Call pf.Evaluate()
            Next
        End Sub
    End Class

#End Region

#Region "k-ω SST"

    ''' <summary>Menter k-ω SST 模型（F1/F2 混合 + SST 粘度限制器 + ω 壁面函数）。</summary>
    Public Class KOmegaSSTModel
        Inherits EulerianTurbulenceModel

        Public Sub New(mesh As EulerianFvMesh, Optional wallDistance As EulerianWallDistance = Nothing)
            MyBase.New(mesh, wallDistance)
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return "k-omega SST (Menter, wall functions)"
            End Get
        End Property

        Private _production As Double()
        Private _wallData As WallCellData()
        Private _f1 As Double()
        Private _gamma As Double()
        Private _sink As Double()
        Private _source As Double()
        Private _scratch As Double()

        Private Sub EnsureScratch(n As Integer)
            If _production IsNot Nothing AndAlso _production.Length = n Then Return

            _production = New Double(n - 1) {}
            _wallData = New WallCellData(n - 1) {}
            _f1 = New Double(n - 1) {}
            _gamma = New Double(n - 1) {}
            _sink = New Double(n - 1) {}
            _source = New Double(n - 1) {}
            _scratch = New Double(n - 1) {}
        End Sub

        ''' <summary>
        ''' νt = a1·k / max(a1·ω, S·F2)，S = √(2SijSij)。
        ''' </summary>
        Public Overrides Sub UpdateViscosity(fields As EulerianFieldSet, ctx As TurbulenceContext)
            Dim n As Integer = Mesh.CellCount
            Dim y As Double() = WallDistance.WallDistanceNormal()
            Dim k As Double() = fields.K.Values
            Dim w As Double() = fields.Omega.Values
            Dim nt As Double() = fields.Nut.Values
            Dim s2 As Double() = StrainSquared
            Dim nu As Double = std.Max(ctx.Nu, 1.0E-30)

            For c As Integer = 0 To n - 1
                Dim kk As Double = std.Max(k(c), ctx.KMin)
                Dim ww As Double = std.Max(w(c), ctx.OmegaMin)
                Dim yy As Double = std.Max(y(c), 1.0E-30)

                ' F2 = tanh(Φ2²)
                Dim a As Double = 2.0 * std.Sqrt(kk) / (SSTCst.BetaStar * ww * yy)
                Dim b As Double = 500.0 * nu / (yy * yy * ww)
                Dim phi2 As Double = std.Max(a, b)
                Dim t2 As Double = std.Tanh(phi2 * phi2)
                Dim s As Double = std.Sqrt(std.Max(s2(c), 0.0))

                Dim denom As Double = std.Max(SSTCst.A1 * ww, s * t2)
                Dim v As Double = If(denom > 0, SSTCst.A1 * kk / denom, 0)

                If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then
                    v = 0
                End If

                nt(c) = v
            Next
        End Sub

        ''' <summary>求解 k 与 ω 方程。</summary>
        Public Overrides Sub SolveTransport(fields As EulerianFieldSet, flux As Double(), ctx As TurbulenceContext)
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume
            Dim nu As Double = std.Max(ctx.Nu, 1.0E-30)

            Call EnsureScratch(n)
            Call LastReports.Clear()
            Call BoundField(fields.K, ctx.KMin)
            Call BoundField(fields.Omega, ctx.OmegaMin)

            Dim kField As EulerianField = fields.K
            Dim wField As EulerianField = fields.Omega

            ' ---- 混合函数 F1 与 γ 系数 ----
            Call ComputeBlending(kField, wField, ctx, nu)

            ' ---- 生产项（带 SST 生产限制器 Pk ≤ 10 β* k ω） ----
            Call ComputeProduction(fields.Nut, _production)

            For c As Integer = 0 To n - 1
                Dim cap As Double = 10.0 * SSTCst.BetaStar * std.Max(kField.Value(c), ctx.KMin) *
                                    std.Max(wField.Value(c), ctx.OmegaMin)

                If _production(c) > cap Then _production(c) = cap
            Next

            ' ---- k 方程：div − div((ν + σk νt)∇k) + (β* ω)·k = Pk ----
            _scratch = New Double(n - 1) {}

            For c As Integer = 0 To n - 1
                _scratch(c) = nu + SigmaK(c) * fields.Nut.Value(c)
            Next

            _gammaKCache = EulerianOperators.FaceGamma(mesh, WrapLike(mesh, _scratch, "nuK_face"))

            For c As Integer = 0 To n - 1
                _sink(c) = vol(c) * SSTCst.BetaStar * std.Max(wField.Value(c), ctx.OmegaMin)
                _source(c) = vol(c) * _production(c)
            Next

            Call LastReports.Add(SolveScalarField(kField, flux, _gammaKCache, _sink, _source, ctx))
            Call BoundField(kField, ctx.KMin)

            ' ---- ω 方程 ----
            For c As Integer = 0 To n - 1
                _scratch(c) = nu + SigmaOmega(c) * fields.Nut.Value(c)
            Next

            _gammaOmegaCache = EulerianOperators.FaceGamma(mesh, WrapLike(mesh, _scratch, "nuOmega_face"))

            If ctx.UseWallFunctions Then
                Call ComputeWallCellData(kField, fields.U, ctx, _wallData)
                Call ApplyOmegaWallBoundary(wField, ctx)
            End If

            ' 交叉扩散项 2(1−F1)σω2 (∇k·∇ω)/ω
            Dim gkx As Double() = New Double(n - 1) {}
            Dim gky As Double() = New Double(n - 1) {}
            Dim gkz As Double() = New Double(n - 1) {}
            Dim gox As Double() = New Double(n - 1) {}
            Dim goy As Double() = New Double(n - 1) {}
            Dim goz As Double() = New Double(n - 1) {}

            Call EulerianOperators.CellGradientLeastSquares(kField, 0, gkx, gky, gkz)
            Call EulerianOperators.CellGradientLeastSquares(wField, 0, gox, goy, goz)

            For c As Integer = 0 To n - 1
                Dim ww As Double = std.Max(wField.Value(c), ctx.OmegaMin)
                Dim cross As Double = 2.0 * (1.0 - _f1(c)) * SSTCst.SigmaOmega2 *
                                      (gkx(c) * gox(c) + gky(c) * goy(c) + gkz(c) * goz(c)) / ww

                _sink(c) = vol(c) * Beta(c) * ww
                _source(c) = vol(c) * (_gamma(c) * std.Max(StrainSquared(c), 0.0) + std.Max(cross, 0.0))
            Next

            Call LastReports.Add(SolveScalarField(wField, flux, _gammaOmegaCache, _sink, _source, ctx))
            Call BoundField(wField, ctx.OmegaMin)
        End Sub

        Private _gammaKCache As Double()
        Private _gammaOmegaCache As Double()

        ''' <summary>把逐单元数组包装为临时场以便调用 FaceGamma（内部工具）。</summary>
        Private Function WrapLike(mesh As EulerianFvMesh, data As Double(), name As String) As EulerianField
            Dim f As New EulerianField(mesh, name, 1, EulerianFieldRole.TurbulentViscosity)

            Array.Copy(data, f.Values, data.Length)
            Return f
        End Function

        Private Function SigmaK(c As Integer) As Double
            Return _f1(c) * SSTCst.SigmaK1 + (1.0 - _f1(c)) * SSTCst.SigmaK2
        End Function

        Private Function SigmaOmega(c As Integer) As Double
            Return _f1(c) * SSTCst.SigmaOmega1 + (1.0 - _f1(c)) * SSTCst.SigmaOmega2
        End Function

        Private Function Beta(c As Integer) As Double
            Return _f1(c) * SSTCst.Beta1 + (1.0 - _f1(c)) * SSTCst.Beta2
        End Function

        ''' <summary>
        ''' 计算 F1（内外层混合函数）与对应的 γ 系数。
        ''' γ = β/β* − σω κ²/√β*
        ''' </summary>
        Private Sub ComputeBlending(kField As EulerianField, wField As EulerianField,
                                    ctx As TurbulenceContext, nu As Double)

            Dim n As Integer = kField.CellCount
            Dim y As Double() = WallDistance.WallDistanceNormal()
            Dim k As Double() = kField.Values
            Dim w As Double() = wField.Values

            Dim gkx As Double() = New Double(n - 1) {}
            Dim gky As Double() = New Double(n - 1) {}
            Dim gkz As Double() = New Double(n - 1) {}
            Dim gox As Double() = New Double(n - 1) {}
            Dim goy As Double() = New Double(n - 1) {}
            Dim goz As Double() = New Double(n - 1) {}

            Call EulerianOperators.CellGradientLeastSquares(kField, 0, gkx, gky, gkz)
            Call EulerianOperators.CellGradientLeastSquares(wField, 0, gox, goy, goz)

            Dim sqrtBetaStar As Double = std.Sqrt(SSTCst.BetaStar)
            Dim kappa2 As Double = SSTCst.Kappa * SSTCst.Kappa

            For c As Integer = 0 To n - 1
                Dim kk As Double = std.Max(k(c), ctx.KMin)
                Dim ww As Double = std.Max(w(c), ctx.OmegaMin)
                Dim yy As Double = std.Max(y(c), 1.0E-30)

                Dim cd As Double = 2.0 * SSTCst.SigmaOmega2 *
                                   (gkx(c) * gox(c) + gky(c) * goy(c) + gkz(c) * goz(c)) / ww

                If cd < 1.0E-10 Then cd = 1.0E-10

                Dim a As Double = std.Sqrt(kk) / (SSTCst.BetaStar * ww * yy)
                Dim b As Double = 500.0 * nu / (yy * yy * ww)
                Dim cc As Double = 4.0 * SSTCst.SigmaOmega2 * kk / (cd * yy * yy)
                Dim phi1 As Double = std.Min(std.Max(a, b), cc)
                Dim f1 As Double = std.Tanh(std.Pow(phi1, 4))

                _f1(c) = f1

                Dim b1 As Double = Beta(c)
                Dim s1 As Double = SigmaOmega(c)
                Dim g1 As Double = b1 / SSTCst.BetaStar - s1 * kappa2 / sqrtBetaStar

                _gamma(c) = g1
            Next
        End Sub

        ''' <summary>ω 壁面邻域：写入 ω_wall = √(ω_vis² + ω_log²)。</summary>
        Private Sub ApplyOmegaWallBoundary(wField As EulerianField, ctx As TurbulenceContext)
            Dim mesh As EulerianFvMesh = Me.Mesh
            Dim nu As Double = std.Max(ctx.Nu, 1.0E-30)

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim pf As EulerianPatchField = wField.Boundaries(pi)
                Dim wf As WallFunctionBc = TryCast(pf.Condition, WallFunctionBc)

                If wf Is Nothing Then
                    Continue For
                End If

                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim n As Integer = geo.FaceCount
                Dim vals(n - 1) As Double

                For k As Integer = 0 To n - 1
                    Dim own As Integer = geo.OwnerCells(k)
                    Dim y As Double = std.Max(WallDistance.WallDistanceNormal(own), 1.0E-30)
                    Dim omegaVis As Double = 6.0 * nu / (SSTCst.Beta1 * y * y)
                    Dim omegaWall As Double = _wallData(own).OmegaWall

                    ' 边界值取对数律（低 Re 直解时可与黏性支并联）
                    Dim v As Double = omegaWall

                    If Not ctx.UseWallFunctions Then
                        v = omegaVis
                    End If

                    vals(k) = std.Max(v, ctx.OmegaMin)
                Next

                Call wf.SetValues(vals)
                Call pf.Evaluate()
            Next
        End Sub

    End Class

#End Region

End Namespace
