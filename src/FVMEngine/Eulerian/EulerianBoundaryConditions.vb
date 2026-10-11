' /********************************************************************************/
'
'   EulerianBoundaryConditions.vb
'
'   边界条件实现（续 EulerianBoundaryCondition.vb 的基类与基础实现）。
'
'   本文件覆盖 CFD 工程中最关键的几类边界：
'
'     InletOutletBc           开口：按面通量方向在"定值（入流）"与"零梯度（出流）"间切换。
'                             用于风洞出流、压力出口、以及未知方向的自由边界。
'     FixedFluxPressureBc     定通量压力（★ SIMPLE/PIMPLE 正确性的关键）：
'                             壁面/入口上压力未知，但**通量已知**，于是把压力梯度写成
'                                 (∇p·S)_f = (phiHbyA_f − phi_b) / rAU_f
'                             代入 p' 方程后，pEqn.flux() 恰好等于 phiHbyA_f − phi_b，
'                             因此速度修正式 phi = phiHbyA − pEqn.flux() 精确给出
'                             预先规定的通量 phi_b —— 质量守恒不靠迭代慢慢逼近，而是
'                             离散意义下严格成立（对应 OpenFOAM 的 fixedFluxPressure）。
'     MovingWallBc            运动壁面：u_b = Ω × (x_f − x_axis)。
'                             Ω = 0 时退化为无滑移固壁；非零时即 MRF 搅拌桨叶。
'     WallFunctionBc          壁面函数承载者：k 用零梯度、ε/ω/νt 用"逐面定值"，
'                             值由湍流模型每轮按对数律写入。
'     TotalPressureTemperatureBc  总压–总温入口（可压缩），
'                             p_b = p0 − ½ρ|u_b|²，T_b = T0 − |u|²/(2c_p)。
'
'   混合精度契约：全部 double（边界贡献属于"非热点"）。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports std = System.Math

Namespace Eulerian

#Region "开口（inlet-outlet）"

    ''' <summary>
    ''' 开口边界：面通量为正（出流）时按零梯度外插；为负（入流）时按给定值。
    ''' 用于风洞出流与非定常自由边界。
    ''' </summary>
    Public Class InletOutletBc
        Inherits EulerianBoundaryCondition

        Private ReadOnly _inletValue As Double()
        Private _flux As Double()
        Private _hasFlux As Boolean

        ''' <summary>指定入流定值（标量作用于所有分量）。</summary>
        Public Sub New(patchField As EulerianPatchField, inletValue As Double)
            MyBase.New(patchField)

            Dim c As Integer = std.Max(1, patchField.Components)

            _inletValue = New Double(c - 1) {}

            For i As Integer = 0 To c - 1
                _inletValue(i) = inletValue
            Next
        End Sub

        ''' <summary>指定逐分量入流定值。</summary>
        Public Sub New(patchField As EulerianPatchField, inletValue As Double())
            MyBase.New(patchField)

            If inletValue Is Nothing OrElse inletValue.Length = 0 Then
                Throw New ArgumentException("入流定值不能为空", NameOf(inletValue))
            End If

            _inletValue = DirectCast(inletValue.Clone(), Double())
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.InletOutlet
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return "inlet-outlet (switch on face flux sign)"
            End Get
        End Property

        ''' <summary>
        ''' 由求解器每轮设置本 patch 的面通量（长度 = FaceCount，出流为正）。
        ''' 未设置时按零梯度处理（保守选择）。
        ''' </summary>
        Public Sub SetFaceFlux(flux As Double())
            If flux Is Nothing Then
                _hasFlux = False
                Return
            End If

            Dim n As Integer = FaceCount

            If flux.Length < n Then
                Throw New ArgumentException($"面通量长度必须 ≥ {n}", NameOf(flux))
            End If

            _flux = flux
            _hasFlux = True
        End Sub

        ''' <summary>第 faceIndex 面是否处于出流状态。</summary>
        Public Function IsOutflow(faceIndex As Integer) As Boolean
            If Not _hasFlux OrElse _flux Is Nothing Then
                Return True
            End If

            Return _flux(Geometry.FaceIndices(faceIndex)) >= 0
        End Function

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            For comp As Integer = 0 To c - 1
                Dim inlet As Double = If(_inletValue.Length = 1, _inletValue(0), _inletValue(comp))

                For i As Integer = 0 To n - 1
                    If IsOutflow(i) Then
                        SetBoundaryValue(i, OwnerValue(i, comp), comp)
                    Else
                        SetBoundaryValue(i, inlet, comp)
                    End If
                Next
            Next
        End Sub

        ''' <summary>入流：定值（vi = 0）；出流：零梯度（vi = 1）。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = If(IsOutflow(i), 1.0, 0.0)
            Next

            Return a
        End Function

        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim a(n * c - 1) As Double

            For comp As Integer = 0 To c - 1
                Dim inlet As Double = If(_inletValue.Length = 1, _inletValue(0), _inletValue(comp))
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = If(IsOutflow(i), 0.0, inlet)
                Next
            Next

            Return a
        End Function

        ''' <summary>出流按零梯度（无隐式贡献）；入流按 Dirichlet（−Δ_f）。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = If(IsOutflow(i), 0.0, -dc(i))
            Next

            Return a
        End Function

        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n * c - 1) As Double

            For comp As Integer = 0 To c - 1
                Dim inlet As Double = If(_inletValue.Length = 1, _inletValue(0), _inletValue(comp))
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = If(IsOutflow(i), 0.0, dc(i) * inlet)
                Next
            Next

            Return a
        End Function
    End Class

#End Region

#Region "定通量压力"

    ''' <summary>
    ''' 定通量压力边界：压力值未知、但边界面通量由速度边界条件给定。
    ''' 压力梯度由动量预测通量与目标通量之差反推（详见文件头说明）。
    ''' </summary>
    Public Class FixedFluxPressureBc
        Inherits EulerianBoundaryCondition

        Private _rau As Double()
        Private _phiHbyA As Double()
        Private _phiB As Double()

        Public Sub New(patchField As EulerianPatchField)
            MyBase.New(patchField)
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.FixedFluxPressure
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return "fixed flux pressure (pressure gradient from prescribed boundary flux)"
            End Get
        End Property

        ''' <summary>
        ''' 由求解器每轮设置：
        ''' <paramref name="rau"/> = 动量对角倒数（逐面，长度 = FaceCount）；
        ''' <paramref name="phiHbyA"/> = 动量插值通量（逐面，全局面索引）；
        ''' <paramref name="phiB"/> = 目标边界通量（逐面，全局面索引，出流为正）。
        ''' </summary>
        Public Sub SetFluxData(rau As Double(), phiHbyA As Double(), phiB As Double())
            If rau Is Nothing OrElse phiHbyA Is Nothing OrElse phiB Is Nothing Then
                _rau = Nothing
                _phiHbyA = Nothing
                _phiB = Nothing
                Return
            End If

            If rau.Length < FaceCount Then
                Throw New ArgumentException($"rAU 长度必须 ≥ {FaceCount}", NameOf(rau))
            End If

            _rau = rau
            _phiHbyA = phiHbyA
            _phiB = phiB
        End Sub

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            For comp As Integer = 0 To c - 1
                For i As Integer = 0 To n - 1
                    SetBoundaryValue(i, OwnerValue(i, comp), comp)
                Next
            Next
        End Sub

        ''' <summary>压力按零阶外插（φ_f = φ_P）。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Return Ones(FaceCount)
        End Function

        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Return New Double(FaceCount * std.Max(1, Components) - 1) {}
        End Function

        ''' <summary>无隐式贡献（梯度由通量差显式给出）。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Return New Double(FaceCount - 1) {}
        End Function

        ''' <summary>(∇p·S)_f = (phiHbyA_f − phi_b) / rAU_f。</summary>
        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim a(n * c - 1) As Double

            If _rau Is Nothing Then
                ' 未注入通量数据：退化为零梯度（等价于"压力不影响边界通量"）
                Return a
            End If

            For i As Integer = 0 To n - 1
                Dim f As Integer = Geometry.FaceIndices(i)
                Dim r As Double = _rau(i)

                If std.Abs(r) <= 1.0E-300 Then
                    Continue For
                End If

                Dim v As Double = (_phiHbyA(f) - _phiB(f)) / r

                If v = 0 Then
                    Continue For
                End If

                For comp As Integer = 0 To c - 1
                    a(comp * n + i) = v
                Next
            Next

            Return a
        End Function

        Private Shared Function Ones(n As Integer) As Double()
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = 1.0
            Next

            Return a
        End Function
    End Class

#End Region

#Region "运动壁面"

    ''' <summary>
    ''' 运动壁面：u_b = Ω × (x_f − x_axis)。
    ''' Ω = 0 时等价于无滑移固壁；非零时用于 MRF 搅拌桨叶。
    ''' </summary>
    Public Class MovingWallBc
        Inherits EulerianBoundaryCondition

        Private ReadOnly _axis As Moira.CFDEngine.Eulerian.EulerianVertex
        Private ReadOnly _omega As Moira.CFDEngine.Eulerian.EulerianVertex

        ''' <summary>绕指定轴以角速度 ω（rad/s）转动。</summary>
        ''' <param name="axisPoint">轴上一点</param>
        ''' <param name="axisDirection">轴方向（内部自动单位化）</param>
        ''' <param name="omega">角速度（rad/s；0 表示无滑移固壁）</param>
        Public Sub New(patchField As EulerianPatchField,
                       axisPoint As Moira.CFDEngine.Eulerian.EulerianVertex,
                       axisDirection As Moira.CFDEngine.Eulerian.EulerianVertex,
                       omega As Double)

            MyBase.New(patchField)

            _axis = axisPoint
            _omega = axisDirection.Normalized() * omega
        End Sub

        ''' <summary>无滑移固壁（Ω ≡ 0）。</summary>
        Public Sub New(patchField As EulerianPatchField)
            Me.New(patchField,
                   New Moira.CFDEngine.Eulerian.EulerianVertex(),
                   New Moira.CFDEngine.Eulerian.EulerianVertex(0, 0, 1),
                   0)
        End Sub

        ''' <summary>角速度矢量 Ω。</summary>
        Public ReadOnly Property Omega As Moira.CFDEngine.Eulerian.EulerianVertex
            Get
                Return _omega
            End Get
        End Property

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.MovingWall
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return $"moving wall (omega=|{_omega.Length.ToString("G4", CultureInfo.InvariantCulture)}|)"
            End Get
        End Property

        ''' <summary>运动壁面直接规定边界速度。</summary>
        Public Overrides ReadOnly Property PrescribesVelocity As Boolean
            Get
                Return True
            End Get
        End Property

        ''' <summary>第 i 个边界面上的壁面速度。</summary>
        Public Sub WallVelocity(faceIndex As Integer, ByRef u As Double, ByRef v As Double, ByRef w As Double)
            Dim r As Moira.CFDEngine.Eulerian.EulerianVertex = Geometry.Cf(faceIndex) - _axis
            Dim om As Moira.CFDEngine.Eulerian.EulerianVertex = _omega

            ' ω × r
            u = om.Y * r.Z - om.Z * r.Y
            v = om.Z * r.X - om.X * r.Z
            w = om.X * r.Y - om.Y * r.X
        End Sub

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            For i As Integer = 0 To n - 1
                Dim u, v, w As Double

                Call WallVelocity(i, u, v, w)

                Select Case c
                    Case 1
                        SetBoundaryValue(i, 0.0, 0)
                    Case 2
                        SetBoundaryValue(i, u, 0)
                        SetBoundaryValue(i, v, 1)
                    Case Else
                        SetBoundaryValue(i, u, 0)
                        SetBoundaryValue(i, v, 1)
                        SetBoundaryValue(i, w, 2)
                End Select
            Next
        End Sub

        ''' <summary>定值分量（vi = 0，全部走边界值）。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Dim a(FaceCount - 1) As Double

            Return a
        End Function

        ''' <summary>边界速度 u_b。</summary>
        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim a(n * c - 1) As Double

            For i As Integer = 0 To n - 1
                Dim u, v, w As Double

                Call WallVelocity(i, u, v, w)

                If c >= 1 Then a(i) = u
                If c >= 2 Then a(n + i) = v
                If c >= 3 Then a(2 * n + i) = w
            Next

            Return a
        End Function

        ''' <summary>Dirichlet 一致：−Δ_f。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = -dc(i)
            Next

            Return a
        End Function

        ''' <summary>Dirichlet 一致：Δ_f·u_b。</summary>
        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n * c - 1) As Double

            For i As Integer = 0 To n - 1
                Dim u, v, w As Double

                Call WallVelocity(i, u, v, w)

                If c >= 1 Then a(i) = dc(i) * u
                If c >= 2 Then a(n + i) = dc(i) * v
                If c >= 3 Then a(2 * n + i) = dc(i) * w
            Next

            Return a
        End Function
    End Class

#End Region

#Region "壁面函数"

    ''' <summary>壁面函数的取值模式。</summary>
    Public Enum WallFunctionMode

        ''' <summary>零梯度（邻壁单元值外插）：k 的标准处理。</summary>
        ZeroGradient = 0

        ''' <summary>逐面定值：由湍流模型每轮按对数律写入（ε / ω / νt）。</summary>
        FixedValue = 1
    End Enum

    ''' <summary>
    ''' 壁面函数承载边界：k 通常用 <see cref="WallFunctionMode.ZeroGradient"/>，
    ''' ε / ω / νt 用 <see cref="WallFunctionMode.FixedValue"/>，
    ''' 具体数值由 <see cref="EulerianTurbulenceModel"/> 每轮按对数律写入。
    ''' </summary>
    Public Class WallFunctionBc
        Inherits EulerianBoundaryCondition

        Private _mode As WallFunctionMode = WallFunctionMode.ZeroGradient
        Private _values As Double()
        Private _hasValues As Boolean

        Public Sub New(patchField As EulerianPatchField, Optional mode As WallFunctionMode = WallFunctionMode.ZeroGradient)
            MyBase.New(patchField)

            _mode = mode
        End Sub

        ''' <summary>取值模式（可在运行期切换）。</summary>
        Public Property Mode As WallFunctionMode
            Get
                Return _mode
            End Get
            Set(value As WallFunctionMode)
                If _mode <> value Then
                    _mode = value
                    Call InvalidateCache()
                End If
            End Set
        End Property

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.WallFunction
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return $"wall function ({_mode.ToString()})"
            End Get
        End Property

        ''' <summary>
        ''' 湍流模型写入逐面取值（长度 = FaceCount × Components，分量主序）。
        ''' 同时把模式切换为 <see cref="WallFunctionMode.FixedValue"/>。
        ''' </summary>
        Public Sub SetValues(values As Double())
            Dim need As Integer = FaceCount * std.Max(1, Components)

            If values Is Nothing OrElse values.Length < need Then
                Throw New ArgumentException($"壁面函数取值长度必须 ≥ {need}", NameOf(values))
            End If

            _values = values
            _hasValues = True
            _mode = WallFunctionMode.FixedValue
        End Sub

        ''' <summary>是否已由湍流模型写入取值。</summary>
        Public ReadOnly Property HasValues As Boolean
            Get
                Return _hasValues
            End Get
        End Property

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            If _mode = WallFunctionMode.ZeroGradient OrElse Not _hasValues Then
                For comp As Integer = 0 To c - 1
                    For i As Integer = 0 To n - 1
                        SetBoundaryValue(i, OwnerValue(i, comp), comp)
                    Next
                Next

                Return
            End If

            For comp As Integer = 0 To c - 1
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    SetBoundaryValue(i, _values(off + i), comp)
                Next
            Next
        End Sub

        ''' <summary>零梯度 → 1；定值 → 0。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim a(n - 1) As Double
            Dim isZeroGradient As Boolean = _mode = WallFunctionMode.ZeroGradient OrElse Not _hasValues

            For i As Integer = 0 To n - 1
                a(i) = If(isZeroGradient, 1.0, 0.0)
            Next

            Return a
        End Function

        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim a(n * c - 1) As Double

            If _mode = WallFunctionMode.ZeroGradient OrElse Not _hasValues Then
                Return a
            End If

            For comp As Integer = 0 To c - 1
                Array.Copy(_values, comp * n, a, comp * n, n)
            Next

            Return a
        End Function

        ''' <summary>零梯度 → 0；定值 → −Δ_f。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n - 1) As Double
            Dim isZeroGradient As Boolean = _mode = WallFunctionMode.ZeroGradient OrElse Not _hasValues

            If isZeroGradient Then
                Return a
            End If

            For i As Integer = 0 To n - 1
                a(i) = -dc(i)
            Next

            Return a
        End Function

        ''' <summary>零梯度 → 0；定值 → Δ_f·φ_b。</summary>
        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n * c - 1) As Double

            If _mode = WallFunctionMode.ZeroGradient OrElse Not _hasValues Then
                Return a
            End If

            For comp As Integer = 0 To c - 1
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = dc(i) * _values(off + i)
                Next
            Next

            Return a
        End Function
    End Class

#End Region

#Region "总压–总温入口（可压缩）"

    ''' <summary>
    ''' 总压–总温入口：p_b = p0 − ½ρ|u_b|²；T_b = T0 − |u|²/(2c_p)。
    ''' 用于可压缩风洞入口（F1 车身局部跨声速、飞机高速绕流）。
    ''' </summary>
    Public Class TotalPressureTemperatureBc
        Inherits EulerianBoundaryCondition

        ''' <summary>总压 p0。</summary>
        Public Property TotalPressure As Double = 101325.0

        ''' <summary>总温 T0。</summary>
        Public Property TotalTemperature As Double = 300.0

        ''' <summary>比热比 γ。</summary>
        Public Property Gamma As Double = 1.4

        ''' <summary>气体常数 R。</summary>
        Public Property GasConstant As Double = 287.05

        ''' <summary>密度（不可压近似；可压缩时由求解器每轮更新）。</summary>
        Public Property Density As Double = 1.225

        ''' <summary>速度场引用（取边界速度模长）。未设置时按静止流处理。</summary>
        Public Property VelocityField As EulerianField

        Public Sub New(patchField As EulerianPatchField)
            MyBase.New(patchField)
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.TotalPressureTemperature
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return $"total p/T (p0={TotalPressure.ToString("G5", CultureInfo.InvariantCulture)}, " &
                       $"T0={TotalTemperature.ToString("G5", CultureInfo.InvariantCulture)})"
            End Get
        End Property

        ''' <summary>定压比热 c_p = γR/(γ−1)。</summary>
        Public ReadOnly Property SpecificHeat As Double
            Get
                Return Gamma * GasConstant / (Gamma - 1.0)
            End Get
        End Property

        Private Function SpeedAt(faceIndex As Integer) As Double
            If VelocityField Is Nothing Then Return 0

            Dim pf As EulerianPatchField = VelocityField.Boundaries.Patch(Geometry.Name)

            If pf Is Nothing Then Return 0

            Dim u As Double = pf.Value(faceIndex, 0)
            Dim v As Double = If(VelocityField.Components >= 2, pf.Value(faceIndex, 1), 0)
            Dim w As Double = If(VelocityField.Components >= 3, pf.Value(faceIndex, 2), 0)

            Return std.Sqrt(u * u + v * v + w * w)
        End Function

        ''' <summary>按场的角色给出边界值。</summary>
        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount

            For i As Integer = 0 To n - 1
                Dim speed As Double = SpeedAt(i)

                Select Case PatchField.OwnerField.Role
                    Case EulerianFieldRole.Pressure
                        SetBoundaryValue(i, TotalPressure - 0.5 * Density * speed * speed, 0)
                    Case EulerianFieldRole.Temperature
                        SetBoundaryValue(i, TotalTemperature - speed * speed / (2.0 * SpecificHeat), 0)
                    Case EulerianFieldRole.Velocity
                        ' 速度分量由外部显式设定（见 SetVelocity）
                        SetBoundaryValue(i, 0.0, 0)
                        SetBoundaryValue(i, 0.0, 1)
                        SetBoundaryValue(i, 0.0, 2)
                    Case Else
                        SetBoundaryValue(i, OwnerValue(i, 0), 0)
                End Select
            Next
        End Sub

        ''' <summary>显式设置入口速度（仅速度场使用）。</summary>
        Public Sub SetVelocity(u As Double, v As Double, w As Double)
            Dim n As Integer = FaceCount

            For i As Integer = 0 To n - 1
                SetBoundaryValue(i, u, 0)
                SetBoundaryValue(i, v, 1)
                SetBoundaryValue(i, w, 2)
            Next

            Call InvalidateCache()
        End Sub

        ''' <summary>压力/温度用 Dirichlet；速度用给定值（vi = 0）。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Return New Double(FaceCount - 1) {}
        End Function

        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim a(n * c - 1) As Double

            For comp As Integer = 0 To c - 1
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = PatchField.Value(i, comp)
                Next
            Next

            Return a
        End Function

        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n - 1) As Double

            For i As Integer = 0 To n - 1
                a(i) = -dc(i)
            Next

            Return a
        End Function

        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim dc As Double() = Geometry.DeltaCoeff
            Dim a(n * c - 1) As Double

            For comp As Integer = 0 To c - 1
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = dc(i) * PatchField.Value(i, comp)
                Next
            Next

            Return a
        End Function
    End Class

#End Region

End Namespace
