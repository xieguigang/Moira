' /********************************************************************************/
'
'   IncompressibleSolver.vb
'
'   顶层循环层 —— 不可压压力–速度耦合求解器（SIMPLE / PIMPLE，Rhie-Chow 去振荡）。
'
'   物理与方程（全为"动力"形式，p 的单位为 Pa）：
'       动量 :  ∂(ρu)/∂t + ∇·(ρ u u) − ∇·(μ_eff ∇u) = −∇p + S_body
'       连续 :  ∇·u = 0        （等价于 ∇·φ = 0，φ 为面体积通量）
'       其中 μ_eff = ρ(ν + νt)，S_body 含 MRF 科氏/离心、重力、表面张力。
'
'   ★ 压力–速度耦合（严格对齐 OpenFOAM simpleFoam/pimpleFoam 的算子流程）
'       1. 动量预测：  a_P u*_P + Σ a_N u*_N = b（不含压力梯度）+ (−∇p_old)·V
'       2. rAU = 1/a_P（用**欠松弛后**的对角 → 压力-速度耦合自动获得松弛）
'       3. HbyA = (b − Σ a_N u*_N)·rAU           ← 不含压力梯度（关键！）
'       4. phiHbyA = interpolate(HbyA)·S_f，且在"规定速度"的边界上用 u_b·S_f
'                    （constrainHbyA 语义；保证边界质量守恒离散精确）
'       5. 压力方程： ∇·(rAU ∇p) = ∇·phiHbyA
'       6. 通量修正： φ = phiHbyA − (rAU ∇p)·S_f     ← 让离散连续性精确成立
'       7. 速度修正： u = HbyA − rAU ∇p
'       8. 边界值刷新
'
'   ★ Rhie-Chow / 棋盘压力振荡
'       面通量从不使用 interpolate(u)·S_f，而是使用 interpolate(HbyA)·S_f，
'       再由压力方程修正 —— 这正是"动量插值"形式的 Rhie-Chow 去相关，
'       因此同位网格上不会出现棋盘压力振荡（docs\FVM_Eulerian.md §5.4）。
'
'   ★ 稳态（SIMPLE）与瞬态（PIMPLE）
'       · 稳态：<see cref="IncompressibleSolverOptions.Transient"/> = False，
'         可选"伪瞬态"对角项（<see cref="IncompressibleSolverOptions.PseudoTransientCourant"/>）
'         增强非正交多面体网格上的稳健性，收敛到同一稳态解。
'       · 瞬态：True，Euler 隐式时间项 + PIMPLE（外循环 × PISO 内修正）。
'
'   混合精度契约：
'       系数组装、边界、HbyA、通量修正、VOF、湍流闭包全部 double；
'       只有线性方程内迭代（动量平滑扫描 / 压力 PCG）下放到 GPU float32，
'       且收敛判定用回读后的 double 残差（见 EulerianFvmCompute）。
'
' /********************************************************************************/

Imports System.Globalization
Imports System.Text
Imports Moira.CFDEngine.Eulerian
Imports std = System.Math

Namespace Eulerian

#Region "配置"

    ''' <summary>不可压求解器配置。</summary>
    Public Class IncompressibleSolverOptions

        ''' <summary>密度（单相；VOF 启用时由混合物性覆盖）。</summary>
        Public Property Rho As Double = 1000.0

        ''' <summary>运动粘度 ν（单相；VOF 启用时由混合物性覆盖）。</summary>
        Public Property Nu As Double = 1.0E-6

        ''' <summary>True = 瞬态（PIMPLE）；False = 稳态（SIMPLE）。</summary>
        Public Property Transient As Boolean = False

        ''' <summary>时间步长（瞬态必需）。</summary>
        Public Property DeltaT As Double = 0.01

        ''' <summary>对流格式。</summary>
        Public Property Scheme As EulerianConvectionScheme = EulerianConvectionScheme.Upwind

        ''' <summary>速度欠松弛（SIMPLE 推荐 0.7）。</summary>
        Public Property RelaxU As Double = 0.7

        ''' <summary>
        ''' 压力场显式欠松弛（SIMPLE 教科书值 0.3）。
        ''' </summary>
        ''' <remarks>
        ''' ★ 不能取 1.0：虽然压力-速度耦合的主欠松弛由 rAU = 1/a_P 承担，
        '''   但在含强非正交面（八叉树切割单元，实测最大 88°）的网格上，
        '''   非正交显式修正与 Rhie-Chow 插值本身带误差，压力修正若全额施加
        '''   会形成正反馈 —— 实测 F1 风洞算例的压力场在约 20 步后指数发散
        '''   （逐次力采样 7e2 → 2e4 → 7e5 → 1e21 → 1e113 → NaN）。
        '''   取 0.3 后该反馈被阻尼，稳态收敛稍慢但稳健。
        '''   盒式正交网格上 0.3 与 1.0 的最终解一致，仅迭代数不同。
        ''' </remarks>
        Public Property RelaxP As Double = 0.3

        ''' <summary>动量预测的平滑扫描次数（OpenFOAM smoothSolver nSweeps 等价物）。</summary>
        Public Property MomentumSweeps As Integer = 2

        ''' <summary>压力方程最大迭代数。</summary>
        Public Property PressureMaxIterations As Integer = 200

        ''' <summary>压力方程容差（归一化残差）。</summary>
        Public Property PressureTolerance As Double = 1.0E-9

        ''' <summary>
        ''' 稳态伪瞬态 Courant 数（&gt; 0 时启用伪瞬态对角项；0 = 关闭）。
        ''' 非正交切割单元网格上强烈建议 0.3~1.0。
        ''' </summary>
        Public Property PseudoTransientCourant As Double = 0.5

        ''' <summary>PIMPLE 外循环次数（瞬态）。</summary>
        Public Property PimpleOuterIterations As Integer = 1

        ''' <summary>PISO 压力修正次数（瞬态，典型 2~3）。</summary>
        Public Property PisoCorrectors As Integer = 2

        ''' <summary>是否允许 GPU float32 后端。</summary>
        Public Property UseGpu As Boolean = True

        ''' <summary>湍流模型（Nothing = 层流）。</summary>
        Public Property Turbulence As EulerianTurbulenceModel

        ''' <summary>
        ''' 湍流方程求解上下文。
        ''' 默认已构造（调用方可在 <c>Initialize()</c> 之前直接改其 Rho/Nu/松弛等成员）；
        ''' <c>Initialize()</c> 会按 <see cref="Rho"/>/<see cref="Nu"/> 覆盖物性。
        ''' </summary>
        Public Property TurbulenceContext As New TurbulenceContext()

        ''' <summary>湍流方程求解间隔（每隔多少外迭代解一次；0 = 只解一次，1 = 每次）。</summary>
        Public Property TurbulenceInterval As Integer = 1

        ''' <summary>MRF 旋转区（Nothing = 无旋转区）。</summary>
        Public Property Mrf As EulerianMrfZone

        ''' <summary>VOF 两相模型（Nothing = 单相）。</summary>
        Public Property Vof As EulerianVofModel

        ''' <summary>重力加速度大小（0 = 关闭）。</summary>
        Public Property Gravity As Double = 0

        ''' <summary>重力方向（单位向量，默认 −Z）。</summary>
        Public Property GravityDirection As Moira.CFDEngine.Eulerian.EulerianVertex =
            New Moira.CFDEngine.Eulerian.EulerianVertex(0, 0, -1)

        ''' <summary>压力参考单元（全 Neumann 边界时使用）。</summary>
        Public Property ReferencePressureCell As Integer = 0

        ''' <summary>压力参考值（Pa）。</summary>
        Public Property ReferencePressure As Double = 0

        ''' <summary>各入口 patch 的速度（patch 名 → (u,v,w)）。</summary>
        Public ReadOnly Property Inlets As New Dictionary(Of String, Double())(StringComparer.OrdinalIgnoreCase)

        ''' <summary>入口湍流强度（用于自动推导 k / ε / ω）。</summary>
        Public Property TurbulenceIntensity As Double = 0.05

        ''' <summary>入口湍流长度尺度（m）。</summary>
        Public Property TurbulenceLengthScale As Double = 0.1

        ''' <summary>
        ''' 随 MRF 区一起旋转的壁面 patch 名（桨叶 / 桨轴）。
        ''' 这些 patch 的 U 边界用 MovingWallBc（u_b = Ω×r）；其余壁面为无滑移固壁。
        ''' </summary>
        Public ReadOnly Property RotatingWallPatches As New List(Of String)()

        ''' <summary>
        ''' 把入口速度（大小 + 方向分量）登记到 <see cref="Inlets"/>。
        ''' 方向会先单位化；默认沿 +X。
        ''' </summary>
        Public Sub SetInletSpeed(patchName As String, speed As Double,
                                 Optional dirX As Double = 1.0,
                                 Optional dirY As Double = 0.0,
                                 Optional dirZ As Double = 0.0)

            Dim d As New Moira.CFDEngine.Eulerian.EulerianVertex(dirX, dirY, dirZ)
            Dim len As Double = d.Length

            If len <= 1.0E-30 Then
                d = New Moira.CFDEngine.Eulerian.EulerianVertex(1, 0, 0)
            Else
                d = New Moira.CFDEngine.Eulerian.EulerianVertex(d.X / len, d.Y / len, d.Z / len)
            End If

            Inlets(patchName) = New Double() {d.X * speed, d.Y * speed, d.Z * speed}
        End Sub

        ''' <summary>直接登记入口速度矢量。</summary>
        Public Sub SetInletVelocity(patchName As String, u As Double, v As Double, w As Double)
            Inlets(patchName) = New Double() {u, v, w}
        End Sub

    End Class

#End Region

#Region "求解器"

    ''' <summary>SIMPLE / PIMPLE 不可压求解器。</summary>
    Public Class IncompressibleSolver

#Region "状态"

        ''' <summary>网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>场集合。</summary>
        Public ReadOnly Property Fields As EulerianFieldSet

        ''' <summary>配置。</summary>
        Public ReadOnly Property Options As IncompressibleSolverOptions

        ''' <summary>面体积通量 φ（u·S，出 Owner 为正）。</summary>
        Public ReadOnly Property Flux As Double()
            Get
                Return _flux
            End Get
        End Property

        ''' <summary>累计模拟时间。</summary>
        Public Property Time As Double = 0

        ''' <summary>已执行的外迭代数。</summary>
        Public Property IterationCount As Integer = 0

        ''' <summary>本轮的动量方程报告（逐分量）。</summary>
        Public ReadOnly Property MomentumReports As New List(Of EulerianSolverReport)()

        ''' <summary>本轮的压力方程报告。</summary>
        Public ReadOnly Property PressureReport As EulerianSolverReport
            Get
                Return _pressureReport
            End Get
        End Property

        Private _pressureReport As EulerianSolverReport

        ''' <summary>最近的连续性误差（max |Σ_f φ_f|·V_P）。</summary>
        Public ReadOnly Property ContinuityError As Double
            Get
                Return _continuity
            End Get
        End Property

        ''' <summary>归一化初始残差历史（每轮外迭代末尾追加）。</summary>
        Public ReadOnly Property ResidualHistory As New List(Of Double)()

        ''' <summary>逐面有效运动粘度 ν_eff = ν + νt（供后处理做黏性力积分）。</summary>
        Public ReadOnly Property NuEffFace As Double()
            Get
                Return _nuEffFace
            End Get
        End Property

        ''' <summary>逐面密度（供后处理做流量与黏性力积分）。</summary>
        Public ReadOnly Property RhoFaceArray As Double()
            Get
                Return _rhoFace
            End Get
        End Property

        ''' <summary>逐面质量通量 ρ·φ。</summary>
        Public ReadOnly Property MassFluxFace As Double()
            Get
                Return _rhoPhi
            End Get
        End Property

        Private ReadOnly _flux As Double()
        Private ReadOnly _phiHbyA As Double()
        Private ReadOnly _rauCell As Double()
        Private ReadOnly _rauFace As Double()
        Private ReadOnly _rhoCell As Double()
        Private ReadOnly _rhoFace As Double()
        Private ReadOnly _rhoPhi As Double()
        Private ReadOnly _muFace As Double()
        Private ReadOnly _nuEffFace As Double()
        Private ReadOnly _pGradX As Double()
        Private ReadOnly _pGradY As Double()
        Private ReadOnly _pGradZ As Double()
        Private ReadOnly _hbyA As EulerianField
        Private ReadOnly _bNoP As Double()()
        Private ReadOnly _uSolved As Double()()
        Private ReadOnly _uOld As Double()()
        Private ReadOnly _continuityBox As Double()
        Private _continuity As Double
        Private _turbulenceSolves As Integer

#End Region

#Region "构造与初始化"

        Public Sub New(mesh As EulerianFvMesh, fields As EulerianFieldSet,
                       Optional options As IncompressibleSolverOptions = Nothing)

            If mesh Is Nothing Then Throw New ArgumentNullException(NameOf(mesh))
            If fields Is Nothing Then Throw New ArgumentNullException(NameOf(fields))

            Me.Mesh = mesh
            Me.Fields = fields
            Me.Options = If(options, New IncompressibleSolverOptions())

            Dim n As Integer = mesh.CellCount
            Dim nF As Integer = mesh.FaceCount

            _flux = New Double(nF - 1) {}
            _phiHbyA = New Double(nF - 1) {}
            _rauCell = New Double(n - 1) {}
            _rauFace = New Double(nF - 1) {}
            _rhoCell = fields.Rho.Values
            _rhoFace = New Double(nF - 1) {}
            _rhoPhi = New Double(nF - 1) {}
            _muFace = New Double(nF - 1) {}
            _nuEffFace = New Double(nF - 1) {}
            _pGradX = New Double(n - 1) {}
            _pGradY = New Double(n - 1) {}
            _pGradZ = New Double(n - 1) {}
            _continuityBox = New Double(n - 1) {}

            _hbyA = New EulerianField(mesh, "HbyA", 3, EulerianFieldRole.Velocity)

            _bNoP = New Double(2)() {}
            _uSolved = New Double(2)() {}
            _uOld = New Double(2)() {}

            For c As Integer = 0 To 2
                _bNoP(c) = New Double(n - 1) {}
                _uSolved(c) = New Double(n - 1) {}
                _uOld(c) = New Double(n - 1) {}
            Next

            If Options.TurbulenceContext Is Nothing Then
                Options.TurbulenceContext = New TurbulenceContext()
            End If

            Options.TurbulenceContext.Rho = Options.Rho
            Options.TurbulenceContext.Nu = Options.Nu
        End Sub

        ''' <summary>
        ''' 初始化：装配默认边界条件、填充物性、构造初始面通量与湍流量场。
        ''' </summary>
        Public Sub Initialize(Optional inletTurbulence As Boolean = True)
            Dim mesh = Me.Mesh
            Dim fields = Me.Fields

            Call ConfigureDefaultBoundaries()

            Call UpdateProperties()

            ' 初始面通量（由当前速度场插值）
            Call EulerianOperators.FaceVolumetricFlux(fields.U, _flux)
            Call UpdateMassFlux()

            ' 湍流量初值
            If Options.Turbulence IsNot Nothing Then
                If inletTurbulence Then
                    Call InitializeTurbulenceFields()
                End If

                Call fields.UpdateBoundaries()
                Call Options.Turbulence.ComputeStrain(fields.U)
                Call Options.Turbulence.UpdateViscosity(fields, Options.TurbulenceContext)
            End If

            Call fields.UpdateBoundaries()
        End Sub

        ''' <summary>依 α / 单相参数刷新 ρ、ν 与面物性。</summary>
        Public Sub UpdateProperties()
            Dim fields = Me.Fields
            Dim n = Mesh.CellCount

            If Options.Vof IsNot Nothing Then
                Options.Vof.UpdateProperties(fields)
            Else
                Dim rho As Double() = _rhoCell
                Dim nu As Double() = fields.Mu.Values

                For c As Integer = 0 To n - 1
                    rho(c) = Options.Rho
                    nu(c) = Options.Nu
                Next
            End If

            Call UpdateFaceProperties()
        End Sub

        Private Sub UpdateFaceProperties()
            If Options.Vof IsNot Nothing Then
                Dim rf As Double() = Options.Vof.RhoFace()

                Array.Copy(rf, _rhoFace, _rhoFace.Length)
            Else
                For f As Integer = 0 To _rhoFace.Length - 1
                    _rhoFace(f) = Options.Rho
                Next
            End If
        End Sub

        Private Sub UpdateMassFlux()
            For f As Integer = 0 To _flux.Length - 1
                _rhoPhi(f) = _rhoFace(f) * _flux(f)
            Next
        End Sub

        ''' <summary>
        ''' 重新由当前速度场构造初始面通量（φ_f = u_f·S_f，边界面按 patch 边界条件取值），
        ''' 并刷新质量通量与边界压力通量数据。
        ''' </summary>
        ''' <remarks>
        ''' ★ 必须在**任何**改变速度场的初值/边界装配之后调用一次。
        '''   时序陷阱：<see cref="Initialize"/> 会在装配边界后立刻用当时的速度场构造 φ；
        '''   若调用方在此之后才把速度场设为远场值（或任何非零初值），
        '''   则 φ 就与速度场**不一致**——典型表现是入口注入质量而出口通量为 0，
        '''   离散连续性残差 Σ_f φ_f ≈ 单个单元的面通量量级（而非机器零），
        '''   随后压力修正会试图用这个虚假的巨大源项去"修正"，
        '''   rAU = 1/a_P 随之指数崩溃、SIMPLE 外迭代在 2~3 步内发散。
        '''   实测该缺陷使 city_seed42 算例第 1 步连续性误差达到 4.2e8
        '''   （而正确的量级应为 1e2 以下），并在第 3 步彻底发散。
        ''' </remarks>
        Public Sub RefreshInitialFlux()
            Call UpdateProperties()
            Call EulerianOperators.FaceVolumetricFlux(Fields.U, _flux)
            Call UpdateMassFlux()
        End Sub

        ''' <summary>
        ''' 统计"阶梯状障碍造成的死端单元"数量（**只诊断，不修改任何场**）。
        ''' </summary>
        ''' <remarks>
        ''' ★ 几何根源（实测）
        '''   均匀笛卡尔 / 八叉树网格在"阶梯状"障碍边界处会生成若干**死端单元**：
        '''   它们在流向（x）上只有入流面、没有出流面，x 向被壁面封死。
        '''   对这种单元，任何非零的流向速度都会在单元内**必然**累积质量——
        '''   它的 x 入流量没有任何 x 出流面可以平衡，只能靠 y/z 横向面导出。
        '''   实测 city_seed42（32.87 m 单元）：3.772% 的单元属于此类，
        '''   使均匀远场初值下的离散连续性残差达到 1.7e4/单元
        '''   （≈ 单个 x 面通量的 55%）。
        '''   把网格加密一倍只把该比例降到 2.008% —— **分辨率不能根治**，
        '''   这是"阶梯式障碍 + 单元中心型 FV 离散"的固有几何病态。
        '''
        ''' ★ 结论（已实测）：不能简单地把死端单元的流向速度置 0 来"修"它——
        '''   置 0 后上游内部面仍在输运通量，散度并不会下降
        '''   （实测空通道被误判后 div 从 0 变 1.875）。
        '''   正确做法需要改压力方程的退化单元处理（见测试报告的"未完成项"）。
        ''' </remarks>
        Public Function ScanDeadEndCells() As Integer
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim cnt As Integer = 0

            For c As Integer = 0 To n - 1
                Dim hasIn As Boolean = False
                Dim hasOut As Boolean = False
                Dim cf As Integer() = mesh.CellFaces(c)

                For k As Integer = 0 To cf.Length - 1
                    Dim f As Integer = cf(k)
                    Dim eF = mesh.Mesh.Faces(f)

                    If eF.Neighbour < 0 Then Continue For

                    Dim sgn As Double = If(eF.Owner = c, 1.0R, -1.0R)
                    Dim sx As Double = sgn * mesh.Sf(f).X

                    If sx < -1.0E-9 Then hasIn = True
                    If sx > 1.0E-9 Then hasOut = True
                Next

                If hasIn AndAlso Not hasOut Then cnt += 1
            Next

            _deadEndCellCount = cnt

            Return cnt
        End Function

        ''' <summary>最近一次 <see cref="ScanDeadEndCells"/> 得到的死端单元数。</summary>
        Private _deadEndCellCount As Integer

        ''' <summary>最近一次 <see cref="ScanDeadEndCells"/> 得到的死端单元数。</summary>
        Public ReadOnly Property DeadEndCellCount As Integer
            Get
                Return _deadEndCellCount
            End Get
        End Property

#End Region

#Region "默认边界条件"

        ''' <summary>
        ''' 按 patch 类型装配工程默认边界：入口定值 / 出口零梯度 / 壁面无滑移（或随 MRF 旋转）/
        ''' 对称面；压力：入口与壁面用定通量压力、出口用定值 0。
        ''' 调用方可在之后按需覆盖任意 patch。
        ''' </summary>
        Public Sub ConfigureDefaultBoundaries()
            Dim mesh = Me.Mesh
            Dim fields = Me.Fields

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)
                Dim nm As String = geo.Name

                ' ---------- 速度 ----------
                Select Case geo.PatchType
                    Case EulerianPatchType.Inlet
                        Dim vin As Double() = Nothing

                        If Options.Inlets.TryGetValue(nm, vin) Then
                            Call fields.U.SetFixedValue(nm, vin)
                        Else
                            Call fields.U.SetFixedValue(nm, New Double() {0, 0, 0})
                        End If

                    Case EulerianPatchType.Outlet
                        Call fields.U.SetZeroGradient(nm)

                    Case EulerianPatchType.Symmetry
                        Call fields.U.SetSymmetry(nm)

                    Case Else
                        ' 壁面：默认无滑移；若在旋转壁面清单中则用 Ω×r
                        Dim rotating As Boolean = False

                        For Each rn As String In Options.RotatingWallPatches
                            If String.Equals(rn, nm, StringComparison.OrdinalIgnoreCase) Then
                                rotating = True
                                Exit For
                            End If
                        Next

                        Dim pf As EulerianPatchField = fields.U.Boundaries(pi)

                        If rotating AndAlso Options.Mrf IsNot Nothing Then
                            pf.Condition = New MovingWallBc(pf, Options.Mrf.AxisPoint, Options.Mrf.AxisDirection, Options.Mrf.Omega)
                        Else
                            pf.Condition = New MovingWallBc(pf)
                        End If
                End Select

                ' ---------- 压力 ----------
                Dim pp As EulerianPatchField = fields.P.Boundaries(pi)

                Select Case geo.PatchType
                    Case EulerianPatchType.Outlet
                        pp.Condition = New FixedValueBc(pp, Options.ReferencePressure)

                    Case EulerianPatchType.Symmetry
                        pp.Condition = New SymmetryBc(pp)

                    Case Else
                        pp.Condition = New FixedFluxPressureBc(pp)
                End Select

                ' ---------- 湍流 ----------
                Call ConfigureTurbulencePatch(pi, geo)
            Next

            Call fields.UpdateBoundaries()
        End Sub

        Private Sub ConfigureTurbulencePatch(pi As Integer, geo As EulerianPatchGeometry)
            Dim fields = Me.Fields
            Dim ctx = Options.TurbulenceContext
            Dim nm As String = geo.Name

            Dim kIn, epsIn, omIn As Double

            Call InletTurbulenceValues(geo, kIn, epsIn, omIn)

            Select Case geo.PatchType
                Case EulerianPatchType.Inlet
                    Call fields.K.SetFixedValue(nm, kIn)
                    Call fields.Epsilon.SetFixedValue(nm, epsIn)
                    Call fields.Omega.SetFixedValue(nm, omIn)

                Case EulerianPatchType.Outlet
                    Call fields.K.SetZeroGradient(nm)
                    Call fields.Epsilon.SetZeroGradient(nm)
                    Call fields.Omega.SetZeroGradient(nm)

                Case EulerianPatchType.Symmetry
                    Call fields.K.SetSymmetry(nm)
                    Call fields.Epsilon.SetSymmetry(nm)
                    Call fields.Omega.SetSymmetry(nm)

                Case Else
                    ' 壁面：k 零梯度；ε/ω 为壁面函数（定值由模型每轮写入）
                    Call fields.K.SetZeroGradient(nm)

                    Dim pe As EulerianPatchField = fields.Epsilon.Boundaries(pi)
                    Dim po As EulerianPatchField = fields.Omega.Boundaries(pi)

                    pe.Condition = New WallFunctionBc(pe, WallFunctionMode.FixedValue)
                    po.Condition = New WallFunctionBc(po, WallFunctionMode.FixedValue)
            End Select
        End Sub

        Private Sub InletTurbulenceValues(geo As EulerianPatchGeometry,
                                          ByRef kOut As Double, ByRef epsOut As Double, ByRef omOut As Double)

            Dim vin As Double() = Nothing

            If Not Options.Inlets.TryGetValue(geo.Name, vin) Then
                kOut = 1.0E-6
                epsOut = 1.0E-6
                omOut = 1.0
                Return
            End If

            Dim speed As Double = std.Sqrt(vin(0) * vin(0) + vin(1) * vin(1) + vin(2) * vin(2))
            Dim i As Double = std.Max(Options.TurbulenceIntensity, 1.0E-6)
            Dim l As Double = std.Max(Options.TurbulenceLengthScale, 1.0E-6)
            Dim k As Double = 1.5 * (i * speed) * (i * speed)

            kOut = std.Max(k, 1.0E-12)
            epsOut = std.Max(std.Pow(KECst.Cmu, 0.75) * std.Pow(kOut, 1.5) / l, 1.0E-12)
            omOut = std.Max(std.Sqrt(kOut) / (std.Pow(KECst.Cmu, 0.25) * l), 1.0E-6)
        End Sub

        Private Sub InitializeTurbulenceFields()
            Dim fields = Me.Fields

            Dim kIn, epsIn, omIn As Double

            ' 以第一个入口的湍流量作为全场初值
            For pi As Integer = 0 To Mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = Mesh.Patches(pi)

                If geo.PatchType = EulerianPatchType.Inlet Then
                    Call InletTurbulenceValues(geo, kIn, epsIn, omIn)
                    Exit For
                End If
            Next

            If kIn <= 0 Then
                kIn = 1.0E-4
                epsIn = 0.1
                omIn = 1.0
            End If

            fields.K.FillValue(kIn)
            fields.Epsilon.FillValue(epsIn)
            fields.Omega.FillValue(omIn)
        End Sub

#End Region

#Region "外迭代"

        ''' <summary>执行一次 SIMPLE 外迭代（稳态）或一个时间步的 PIMPLE 外循环（瞬态）。</summary>
        Public Sub Iterate()
            Dim outer As Integer = If(Options.Transient, std.Max(1, Options.PimpleOuterIterations), 1)
            Dim inner As Integer = If(Options.Transient, std.Max(1, Options.PisoCorrectors), 1)

            If Options.Transient Then
                Call CaptureOldVelocity()
            End If

            For o As Integer = 1 To outer
                Call OuterIteration(inner, o = outer)
            Next

            IterationCount += 1

            If Options.Transient Then
                Time += Options.DeltaT
            End If
        End Sub

        Private Sub CaptureOldVelocity()
            Dim n As Integer = Mesh.CellCount

            For comp As Integer = 0 To 2
                Array.Copy(Fields.U.Values, comp * n, _uOld(comp), 0, n)
            Next
        End Sub

        Private Sub OuterIteration(correctors As Integer, lastOuter As Boolean)
            Dim mesh = Me.Mesh
            Dim fields = Me.Fields
            Dim n As Integer = mesh.CellCount
            Dim ctx = Options.TurbulenceContext

            ' ---------- 0. 物性与湍流 ----------
            Call UpdateProperties()

            Dim turb As EulerianTurbulenceModel = Options.Turbulence

            If turb IsNot Nothing Then
                Call turb.ComputeStrain(fields.U)
                Call turb.UpdateViscosity(fields, ctx)

                If Options.TurbulenceInterval > 0 AndAlso
                   (Options.TurbulenceInterval = 1 OrElse (_turbulenceSolves Mod Options.TurbulenceInterval) = 0) Then

                    ctx.Rho = Options.Rho
                    ctx.Nu = fields.Mu.VolumeWeightedAverage()
                    ctx.DeltaT = If(Options.Transient, Options.DeltaT, 0)

                    Call turb.SolveTransport(fields, _flux, ctx)
                    Call turb.UpdateViscosity(fields, ctx)
                End If

                _turbulenceSolves += 1
            Else
                fields.Nut.Zero()
            End If

            ' 有效粘度面值
            Dim nuEff As Double() = If(turb IsNot Nothing,
                                       turb.EffectiveNuFace(fields.Mu.Values, fields.Nut),
                                       EulerianOperators.FaceGamma(mesh, fields.Mu))

            Array.Copy(nuEff, _nuEffFace, _nuEffFace.Length)

            For f As Integer = 0 To _muFace.Length - 1
                _muFace(f) = _rhoFace(f) * _nuEffFace(f)
            Next

            ' ---------- 1. 动量预测 ----------
            Call MomentumPredictor()

            ' ---------- 2~8. 压力修正（PISO 内循环） ----------
            For corr As Integer = 1 To correctors
                Call PressureCorrection()
            Next

            ' ---------- 9. VOF 推进 ----------
            If Options.Vof IsNot Nothing AndAlso Options.Transient Then
                Call Options.Vof.Advect(_flux, Options.DeltaT, ctx)
                Call UpdateProperties()
            End If

            ' ---------- 10. 连续性诊断 ----------
            _continuity = EulerianOperators.MaxContinuityError(mesh, _flux)
            ResidualHistory.Add(NormalisedContinuity())
        End Sub

        Private Function NormalisedContinuity() As Double
            Dim n As Integer = Mesh.CellCount
            Dim vol As Double() = Mesh.CellVolume
            Dim totalFlux As Double = 0

            For f As Integer = 0 To _rhoPhi.Length - 1
                totalFlux += std.Abs(_rhoPhi(f))
            Next

            Dim den As Double = If(totalFlux > 0, totalFlux / std.Max(1, n), 1.0)

            Return _continuity / std.Max(den, 1.0E-30)
        End Function

#End Region

#Region "动量预测"

        Private Sub MomentumPredictor()
            Dim mesh = Me.Mesh
            Dim fields = Me.Fields
            Dim n As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume

            Call MomentumReports.Clear()

            ' 用"面积加权"的方式得到 φ 用于对流的**质量通量**（已经是最新一次压力修正后的 φ）
            Call UpdateMassFlux()

            ' 当前压力梯度
            Call EulerianOperators.CellGradient(fields.P, 0, _pGradX, _pGradY, _pGradZ)

            ' 表面张力源（三个分量共用一次计算）
            Dim sigmaSrc As Double()() = Nothing

            If Options.Vof IsNot Nothing AndAlso Options.Vof.SurfaceTension > 0 Then
                sigmaSrc = New Double(2)() {}

                For comp As Integer = 0 To 2
                    sigmaSrc(comp) = Options.Vof.SurfaceTensionSource(comp)
                Next
            End If

            ' 伪瞬态系数（仅稳态）
            Dim pseudo As Double() = Nothing

            If Not Options.Transient AndAlso Options.PseudoTransientCourant > 0 Then
                pseudo = BuildPseudoTransientCoefficient()
            End If

            Dim mtx As New List(Of FvMatrix)

            For comp As Integer = 0 To 2
                Dim m As New FvMatrix(mesh, "U" & comp.ToString(CultureInfo.InvariantCulture))

                m.Symmetric = False

                Dim uComp As Double() = fields.U.ToComponent(comp)

                If Options.Transient Then
                    Call EulerianOperators.FvmDdt(m, fields.U, _uOld(comp), comp, Options.DeltaT, _rhoCell)
                ElseIf pseudo IsNot Nothing Then
                    For c As Integer = 0 To n - 1
                        m.AddDiag(c, pseudo(c))
                        m.AddSource(c, pseudo(c) * uComp(c))
                    Next
                End If

                Call EulerianOperators.FvmDiv(m, _rhoPhi, fields.U, comp, Options.Scheme)
                ' div(ρuu) − div(μ∇u) = −∇p + S：FvmLaplacian 即 −div(μ∇u)
                Call EulerianOperators.FvmLaplacian(m, fields.U, comp, _muFace)

                ' 体力：MRF 科氏 / 离心 + 虚拟桨盘 + 重力 + 表面张力
                Call MrfModel.AddFrameSource(m, Options.Mrf, fields.U, comp, _rhoCell)
                Call MrfModel.AddDriveSource(m, Options.Mrf, fields.U, comp, _rhoCell)
                Call AddGravitySource(m, comp)

                If sigmaSrc IsNot Nothing Then
                    Dim s As Double() = sigmaSrc(comp)

                    For c As Integer = 0 To n - 1
                        If s(c) <> 0 Then
                            m.AddSource(c, vol(c) * s(c))
                        End If
                    Next
                End If

                ' 欠松弛（必须在保存 bNoP 之前：rAU 与 HbyA 都要用松弛后的系统）
                Call m.Relax(Options.RelaxU, uComp)

                ' 保存"不含压力梯度"的右端（HbyA 用）
                Array.Copy(m.Source, _bNoP(comp), n)

                ' 再把压力梯度加到预测步的右端：−∇p·V
                Dim g As Double() = If(comp = 0, _pGradX, If(comp = 1, _pGradY, _pGradZ))

                For c As Integer = 0 To n - 1
                    m.AddSource(c, -vol(c) * g(c))
                Next

                Dim x As Double() = uComp
                Dim rpt As EulerianSolverReport = EulerianLinearSolve.Solve(
                    m, x, EulerianLinearSolverKind.BiCGStab, 1.0E-10, 0.0,
                    std.Max(20, Options.PressureMaxIterations), Options.MomentumSweeps, Options.UseGpu)

                Call MomentumReports.Add(rpt)

                fields.U.SetFromComponent(comp, x)
                Array.Copy(x, _uSolved(comp), n)

                mtx.Add(m)
            Next

            Call fields.U.UpdateBoundaries()

            ' 对角元（三个分量的对角结构一致，取 X 分量；松弛已含其中）
            Dim diag As Double() = mtx(0).DiagonalArray()

            For c As Integer = 0 To n - 1
                Dim d As Double = diag(c)

                _rauCell(c) = If(std.Abs(d) > 1.0E-300, 1.0 / d, 0.0)
            Next

            Call InterpolateCellToFace(_rauCell, _rauFace)

            ' ---------- HbyA = (b − Σ a_N u*_N)·rAU ----------
            Dim h As Double() = New Double(n - 1) {}

            For comp As Integer = 0 To 2
                Call mtx(comp).H(_uSolved(comp), h)

                Dim off As Integer = comp * n

                For c As Integer = 0 To n - 1
                    _hbyA.Values(off + c) = _rauCell(c) * (_bNoP(comp)(c) - h(c))
                Next
            Next

            Call _hbyA.UpdateBoundaries()

            ' ---------- phiHbyA（constrainHbyA 语义：规定速度的边界用 u_b·S_f） ----------
            Call EulerianOperators.ConstrainedFaceFlux(_hbyA, fields.U, _phiHbyA)
        End Sub

        ''' <summary>稳态伪瞬态对角系数：dt_local = Co·V/(Σ|ρφ|/2)。</summary>
        Private Function BuildPseudoTransientCoefficient() As Double()
            Dim mesh = Me.Mesh
            Dim n As Integer = mesh.CellCount
            Dim vol As Double() = mesh.CellVolume
            Dim fluxSum As Double() = _continuityBox

            Array.Clear(fluxSum, 0, n)

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim f As Integer = mesh.InternalFaces(i)
                Dim a As Double = std.Abs(_rhoPhi(f))

                fluxSum(mesh.InternalOwner(i)) += a
                fluxSum(mesh.InternalNeighbour(i)) += a
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    fluxSum(geo.OwnerCells(k)) += std.Abs(_rhoPhi(geo.FaceIndices(k)))
                Next
            Next

            Dim coeff(n - 1) As Double
            Dim co As Double = Options.PseudoTransientCourant

            For c As Integer = 0 To n - 1
                Dim throughput As Double = 0.5 * fluxSum(c)

                If throughput <= 0 Then
                    ' 无对流（纯扩散区）：不施加伪瞬态项
                    coeff(c) = 0
                Else
                    ' 需要的对角系数是 ρ·V/Δt_local，其中局部时间步 Δt_local = Co·V/throughput
                    '   ⇒ ρ·V·throughput/(Co·V) = ρ·throughput/Co
                    coeff(c) = _rhoCell(c) * throughput / co
                End If
            Next

            Return coeff
        End Function

        Private Sub AddGravitySource(m As FvMatrix, component As Integer)
            If Options.Gravity <= 0 Then Return

            Dim mesh = Me.Mesh
            Dim vol As Double() = mesh.CellVolume
            Dim g As Moira.CFDEngine.Eulerian.EulerianVertex = Options.GravityDirection.Normalized()
            Dim gi As Double = Options.Gravity * (If(component = 0, g.X, If(component = 1, g.Y, g.Z)))

            If gi = 0 Then Return

            For c As Integer = 0 To mesh.CellCount - 1
                ' 浮力：−ρ g（与密度差相关；此处用逐单元 ρ 直接给出体积力）
                m.AddSource(c, -vol(c) * _rhoCell(c) * gi)
            Next
        End Sub

        Private Sub InterpolateCellToFace(cellValues As Double(), faceValues As Double())
            Dim mesh = Me.Mesh
            Dim w As Double() = mesh.InterpWeight

            For i As Integer = 0 To mesh.InternalFaceCount - 1
                Dim wi As Double = w(i)

                faceValues(mesh.InternalFaces(i)) =
                    wi * cellValues(mesh.InternalOwner(i)) + (1.0 - wi) * cellValues(mesh.InternalNeighbour(i))
            Next

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim geo As EulerianPatchGeometry = mesh.Patches(pi)

                For k As Integer = 0 To geo.FaceCount - 1
                    faceValues(geo.FaceIndices(k)) = cellValues(geo.OwnerCells(k))
                Next
            Next
        End Sub

#End Region

#Region "压力修正"

        Private Sub PressureCorrection()
            Dim mesh = Me.Mesh
            Dim fields = Me.Fields
            Dim n As Integer = mesh.CellCount

            ' ---------- 注入定通量压力边界所需的数据 ----------
            Dim phiB As Double() = New Double(_flux.Length - 1) {}

            For f As Integer = 0 To phiB.Length - 1
                phiB(f) = _flux(f)
            Next

            Call InjectFixedFluxPressure(_phiHbyA, phiB)

            ' ---------- 压力方程： ∇·(rAU ∇p) = ∇·phiHbyA ----------
            Dim mP As New FvMatrix(mesh, "p")

            mP.Symmetric = True

            ' 压力方程：+div(rAU∇p) = div(phiHbyA) ⇒ sign := -1（FvmLaplacian 默认是 −div）
            Call EulerianOperators.FvmLaplacian(mP, fields.P, 0, _rauFace, sign:=-1.0)

            Dim divPhi As Double() = New Double(n - 1) {}

            Call EulerianOperators.FvcDiv(mesh, _phiHbyA, divPhi)
            Call mP.AddSourceField(divPhi)

            ' 全 Neumann 边界 → 需要压力参考
            If NeedsPressureReference() Then
                Call mP.SetReference(std.Max(0, std.Min(n - 1, Options.ReferencePressureCell)), Options.ReferencePressure)
            End If

            Dim px As Double() = fields.P.ToComponent(0)
            Dim rpt As EulerianSolverReport = EulerianLinearSolve.Solve(
                mP, px, EulerianLinearSolverKind.Pcg, Options.PressureTolerance, 0.01,
                Options.PressureMaxIterations, 0, Options.UseGpu)

            _pressureReport = rpt

            ' 压力显式松弛（默认 1.0；耦合松弛已由 rAU 承担）
            If Options.RelaxP < 1.0 Then
                Dim cur As Double() = fields.P.Values

                For c As Integer = 0 To n - 1
                    cur(c) += Options.RelaxP * (px(c) - cur(c))
                Next
            Else
                Call fields.P.SetFromComponent(0, px)
            End If

            Call fields.P.UpdateBoundaries()

            ' ---------- 通量修正： φ = phiHbyA − (rAU ∇p)·S_f ----------
            Dim lapFlux As Double() = New Double(_flux.Length - 1) {}

            Call EulerianOperators.LaplacianFaceFlux(mesh, _rauFace, fields.P, 0, lapFlux)

            For f As Integer = 0 To _flux.Length - 1
                _flux(f) = _phiHbyA(f) - lapFlux(f)
            Next

            ' ---------- 速度修正： u = HbyA − rAU ∇p ----------
            Call EulerianOperators.CellGradient(fields.P, 0, _pGradX, _pGradY, _pGradZ)

            Dim ux As Double() = _hbyA.Values
            Dim uy As Double() = New Double(n - 1) {}
            Dim uz As Double() = New Double(n - 1) {}

            Array.Copy(_hbyA.Values, n, uy, 0, n)
            Array.Copy(_hbyA.Values, 2 * n, uz, 0, n)

            Dim u As Double() = fields.U.Values

            For c As Integer = 0 To n - 1
                Dim r As Double = _rauCell(c)

                u(c) = ux(c) - r * _pGradX(c)
                u(n + c) = uy(c) - r * _pGradY(c)
                u(2 * n + c) = uz(c) - r * _pGradZ(c)
            Next

            Call fields.U.UpdateBoundaries()

            ' φ 已更新且满足连续性；同步质量通量供下一轮动量装配使用
            Call UpdateMassFlux()
        End Sub

        Private Function NeedsPressureReference() As Boolean
            For pi As Integer = 0 To Mesh.PatchCount - 1
                Dim bc As EulerianBoundaryCondition = Fields.P.Boundaries(pi).Condition

                If TypeOf bc Is FixedValueBc Then
                    Return False
                End If
            Next

            Return True
        End Function

        ''' <summary>把 rAU / phiHbyA / 目标边界通量写入各 patch 的 FixedFluxPressureBc。</summary>
        Private Sub InjectFixedFluxPressure(phiHbyA As Double(), phiB As Double())
            Dim mesh = Me.Mesh

            For pi As Integer = 0 To mesh.PatchCount - 1
                Dim pf As EulerianPatchField = Fields.P.Boundaries(pi)
                Dim ffp As FixedFluxPressureBc = TryCast(pf.Condition, FixedFluxPressureBc)

                If ffp Is Nothing Then
                    Continue For
                End If

                Dim rauFace As Double() = New Double(ffp.Geometry.FaceCount - 1) {}

                For k As Integer = 0 To rauFace.Length - 1
                    rauFace(k) = _rauCell(ffp.Geometry.OwnerCells(k))
                Next

                Call ffp.SetFluxData(rauFace, phiHbyA, phiB)
                Call pf.Evaluate()
            Next
        End Sub

#End Region

#Region "驱动循环"

        ''' <summary>
        ''' 稳态求解：持续迭代直到归一化连续性残差低于 <paramref name="tolerance"/>
        ''' 或达到最大迭代数；<paramref name="progress"/> 每轮回调（可为 Nothing）。
        ''' </summary>
        Public Function Solve(Optional maxIterations As Integer = 1000,
                              Optional tolerance As Double = 1.0E-6,
                              Optional progress As Action(Of Integer, Double) = Nothing) As Boolean

            Dim converged As Boolean = False

            For i As Integer = 1 To std.Max(1, maxIterations)
                Call Iterate()

                Dim res As Double = ResidualHistory(ResidualHistory.Count - 1)

                If progress IsNot Nothing Then
                    Call progress(IterationCount, res)
                End If

                If res <= tolerance Then
                    converged = True
                    Exit For
                End If
            Next

            Return converged
        End Function

        ''' <summary>连续推进 <paramref name="steps"/> 个时间步（瞬态）。</summary>
        Public Sub Run(steps As Integer, Optional progress As Action(Of Integer, Double) = Nothing)
            For i As Integer = 1 To std.Max(0, steps)
                Call Iterate()

                If progress IsNot Nothing Then
                    Call progress(IterationCount, ResidualHistory(ResidualHistory.Count - 1))
                End If
            Next
        End Sub

#End Region

#Region "诊断"

        ''' <summary>关键诊断指标。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(512)
                Dim u As Double() = Fields.U.Values
                Dim umax As Double = 0

                For c As Integer = 0 To Mesh.CellCount - 1
                    Dim m2 As Double = u(c) * u(c) + u(Mesh.CellCount + c) * u(Mesh.CellCount + c) +
                                       u(2 * Mesh.CellCount + c) * u(2 * Mesh.CellCount + c)

                    If m2 > umax Then umax = m2
                Next

                sb.AppendLine($"iteration={IterationCount}, time={Time.ToString("G6", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  max|U| = {std.Sqrt(umax).ToString("G6", CultureInfo.InvariantCulture)} m/s")
                sb.AppendLine($"  continuity error = {_continuity.ToString("E3", CultureInfo.InvariantCulture)}")
                sb.AppendLine($"  normalised residual = {If(ResidualHistory.Count > 0, ResidualHistory(ResidualHistory.Count - 1), 0).ToString("E3", CultureInfo.InvariantCulture)}")

                If _pressureReport IsNot Nothing Then
                    sb.AppendLine($"  {_pressureReport.ToString()}")
                End If

                For Each r In MomentumReports
                    sb.AppendLine($"  {r.ToString()}")
                Next

                If Options.Vof IsNot Nothing Then
                    sb.AppendLine($"  VOF: liquid fraction={Options.Vof.LiquidFraction().ToString("F5", CultureInfo.InvariantCulture)}")
                End If

                If Options.Turbulence IsNot Nothing Then
                    sb.Append(Options.Turbulence.Diagnostics(Fields))
                End If

                Return sb.ToString()
            End Get
        End Property

#End Region

    End Class

#End Region

End Namespace
