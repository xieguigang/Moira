' /********************************************************************************/
'
'   EulerianBoundaryCondition.vb
'
'   边界条件框架（基类 + 首批基础实现）—— 对应 docs\FVM_Eulerian.md §九。
'
'   设计（OpenFOAM fvPatchField 风格，但保持 VB 可读性）：
'       每个边界条件对象绑定到唯一的 (field, patch) 组合上，向方程组装层只暴露
'       四个"面系数数组 + 一个边界值数组"，因此方程组装器完全不需要 switch 具体类型：
'
'         对流项： F_f·φ_f = F_f · [ ValueInternalCoeff(i)·φ_P + ValueBoundaryCoeff(i) ]
'         扩散项： Γ_f (∇φ)_f·S_f = Γ_f · [ GradInternalCoeff(i)·φ_P + GradBoundaryCoeff(i) ]
'
'       两个系数对都带缓存，只有 <see cref="Update"/>（内部场变化后）才失效重算。
'       这样每轮外迭代只做一次 O(patch 面数) 的重算，而不是在面循环里反复分支。
'
'   默认（Dirichlet 一致）系数：
'         FixedValue 风格：ValueInternal = 0        ValueBoundary = φ_b
'                          GradInternal  = −Δ_f     GradBoundary  = Δ_f·φ_b
'       其中 Δ_f = |S_f|²/(S_f·d) 为正交扩散系数（见 EulerianFvMesh）。
'
'   混合精度契约：
'       系数组装是"非热点"→ 全部 double。GPU 只承接内迭代线性求解。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports std = System.Math

Namespace Eulerian

    ''' <summary>边界条件的物理类别。</summary>
    Public Enum EulerianBcKind

        ''' <summary>定值（Dirichlet）。</summary>
        FixedValue = 0

        ''' <summary>定梯度（Neumann）。</summary>
        FixedGradient = 1

        ''' <summary>零梯度（出流 / 绝热）。</summary>
        ZeroGradient = 2

        ''' <summary>对称面（法向分量为零）。</summary>
        Symmetry = 3

        ''' <summary>定通量压力（壁面上压力梯度由速度边界条件反推）。</summary>
        FixedFluxPressure = 4

        ''' <summary>开口（按通量方向在定值与零梯度之间切换）。</summary>
        InletOutlet = 5

        ''' <summary>湍流壁面函数（对数律）。</summary>
        WallFunction = 6

        ''' <summary>运动壁面（MRF 桨叶：u = Ω × r）。</summary>
        MovingWall = 7

        ''' <summary>总压–总温入口（可压缩）。</summary>
        TotalPressureTemperature = 8
    End Enum

    ''' <summary>
    ''' 边界条件基类：提供"面系数四元组"契约与缓存，派生类只需实现
    ''' <see cref="UpdateBoundaryValues"/> 并按需覆盖四个系数计算。
    ''' </summary>
    Public MustInherit Class EulerianBoundaryCondition

#Region "构造与身份"

        ''' <summary>所属的 (field, patch) 边界场。</summary>
        Public ReadOnly Property PatchField As EulerianPatchField

        ''' <summary>该 patch 的几何量。</summary>
        Public ReadOnly Property Geometry As EulerianPatchGeometry

        ''' <summary>边界条件类别。</summary>
        Public MustOverride ReadOnly Property Kind As EulerianBcKind

        ''' <summary>人类可读描述（日志 / 诊断用）。</summary>
        Public MustOverride ReadOnly Property Description As String

        ''' <summary>
        ''' 是否生效。置为 False 时 <see cref="Update"/> 不做任何事，
        ''' 系数退化为零（等价于"无边界贡献"），仅供调试与特殊耦合场景使用。
        ''' </summary>
        Public Property Enabled As Boolean = True

        ''' <summary>
        ''' 本边界条件是否"直接规定了边界速度"（Dirichlet 型速度边界）。
        '''
        ''' 语义（对应 OpenFOAM 的 <c>constrainHbyA</c>）：
        '''   · True：边界速度由边界条件给定，SIMPLE/PIMPLE 在构造面通量 phiHbyA 时
        '''           必须用边界速度 u_b 取代 HbyA 的边界插值，从而让面通量精确满足
        '''           质量守恒（入口流量 / 壁面零穿透 / 运动壁面）。
        '''   · False：边界速度未知（出流、零梯度），phiHbyA 的边界值由内部值零阶外插。
        ''' </summary>
        Public Overridable ReadOnly Property PrescribesVelocity As Boolean
            Get
                Return False
            End Get
        End Property

        Protected Sub New(patchField As EulerianPatchField)
            If patchField Is Nothing Then
                Throw New ArgumentNullException(NameOf(patchField))
            End If

            Me.PatchField = patchField
            Me.Geometry = patchField.Geometry
        End Sub

        ''' <summary>本 patch 的边界面数。</summary>
        Protected ReadOnly Property FaceCount As Integer
            Get
                Return Geometry.FaceCount
            End Get
        End Property

        ''' <summary>场的分量数。</summary>
        Protected ReadOnly Property Components As Integer
            Get
                Return PatchField.Components
            End Get
        End Property

#End Region

#Region "更新（模板方法）"

        ''' <summary>
        ''' 内部场变化后调用：失效系数缓存并重算边界面值。
        ''' 由 <see cref="EulerianBoundaryField.UpdateAll"/> 统一驱动。
        ''' </summary>
        Public Sub Update()
            If Not Enabled Then
                Return
            End If

            Call InvalidateCache()
            Me.UpdateBoundaryValues()
        End Sub

        ''' <summary>失效全部系数缓存（几何或参数改变后调用）。</summary>
        Public Sub InvalidateCache()
            _valueInternal = Nothing
            _valueBoundary = Nothing
            _gradInternal = Nothing
            _gradBoundary = Nothing
        End Sub

        ''' <summary>重算边界面值（派生类实现）。</summary>
        Protected MustOverride Sub UpdateBoundaryValues()

#End Region

#Region "取值辅助"

        ''' <summary>当前边界面值 φ_b。</summary>
        Protected Function BoundaryValue(faceIndex As Integer, Optional component As Integer = 0) As Double
            Return PatchField.Value(faceIndex, component)
        End Function

        ''' <summary>写边界面值 φ_b。</summary>
        Protected Sub SetBoundaryValue(faceIndex As Integer, value As Double, Optional component As Integer = 0)
            PatchField.SetValue(faceIndex, value, component)
        End Sub

        ''' <summary>该边界面 Owner 单元的内部值 φ_P。</summary>
        Protected Function OwnerValue(faceIndex As Integer, Optional component As Integer = 0) As Double
            Return PatchField.OwnerCellValue(faceIndex, component)
        End Function

        ''' <summary>面法向单位向量 n̂。</summary>
        Protected Function Normal(faceIndex As Integer) As Moira.CFDEngine.Eulerian.EulerianVertex
            Return Geometry.Sf(faceIndex).Normalized()
        End Function

        ''' <summary>本 patch 的正交扩散系数 Δ_f。</summary>
        Protected Function DeltaCoeffArray() As Double()
            Return Geometry.DeltaCoeff
        End Function

        ''' <summary>本 patch 的面面积 |S_f|。</summary>
        Protected Function MagSfArray() As Double()
            Return Geometry.MagSf
        End Function

        ''' <summary>本 patch 的面法向矢量 S_f。</summary>
        Protected Function SfArray() As Moira.CFDEngine.Eulerian.EulerianVertex()
            Return Geometry.Sf
        End Function

#End Region

#Region "面系数四元组（带缓存）"

        Private _valueInternal As Double()
        Private _valueBoundary As Double()
        Private _gradInternal As Double()
        Private _gradBoundary As Double()

        ''' <summary>
        ''' 对流项内部系数：φ_f = ValueInternalCoeff(i)·φ_P + ValueBoundaryCoeff(i)。
        ''' 长度 = FaceCount（对所有分量相同）。
        ''' </summary>
        Public Function ValueInternalCoeffs() As Double()
            If _valueInternal Is Nothing OrElse _valueInternal.Length <> FaceCount Then
                _valueInternal = Me.ComputeValueInternalCoeffs()
                ValidateCoeffLength(_valueInternal, NameOf(ValueInternalCoeffs))
            End If

            Return _valueInternal
        End Function

        ''' <summary>对流项边界系数：长度 = FaceCount × Components（分量主序）。</summary>
        Public Function ValueBoundaryCoeffs() As Double()
            Dim need As Integer = FaceCount * std.Max(1, Components)

            If _valueBoundary Is Nothing OrElse _valueBoundary.Length <> need Then
                _valueBoundary = Me.ComputeValueBoundaryCoeffs()
                ValidateCoeffLength(_valueBoundary, NameOf(ValueBoundaryCoeffs))
            End If

            Return _valueBoundary
        End Function

        ''' <summary>扩散项内部系数：长度 = FaceCount。</summary>
        Public Function GradientInternalCoeffs() As Double()
            If _gradInternal Is Nothing OrElse _gradInternal.Length <> FaceCount Then
                _gradInternal = Me.ComputeGradientInternalCoeffs()
                ValidateCoeffLength(_gradInternal, NameOf(GradientInternalCoeffs))
            End If

            Return _gradInternal
        End Function

        ''' <summary>扩散项边界系数：长度 = FaceCount × Components（分量主序）。</summary>
        Public Function GradientBoundaryCoeffs() As Double()
            Dim need As Integer = FaceCount * std.Max(1, Components)

            If _gradBoundary Is Nothing OrElse _gradBoundary.Length <> need Then
                _gradBoundary = Me.ComputeGradientBoundaryCoeffs()
                ValidateCoeffLength(_gradBoundary, NameOf(GradientBoundaryCoeffs))
            End If

            Return _gradBoundary
        End Function

        Private Sub ValidateCoeffLength(a As Double(), what As String)
            If a Is Nothing Then
                Throw New InvalidOperationException($"{Me.GetType().Name}.{what} 返回 Nothing")
            End If
        End Sub

        ''' <summary>
        ''' 默认对流内部系数：0（Dirichlet：φ_f = φ_b）。
        ''' </summary>
        Protected Overridable Function ComputeValueInternalCoeffs() As Double()
            Return New Double(FaceCount - 1) {}
        End Function

        ''' <summary>
        ''' 默认对流边界系数：取当前边界面值 φ_b（Dirichlet）。
        ''' </summary>
        Protected Overridable Function ComputeValueBoundaryCoeffs() As Double()
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

        ''' <summary>
        ''' 默认扩散内部系数：−Δ_f（Dirichlet 一致：Γ(∇φ·S)_f = Γ·Δ_f(φ_b − φ_P)）。
        ''' </summary>
        Protected Overridable Function ComputeGradientInternalCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim a(n - 1) As Double
            Dim dc As Double() = Geometry.DeltaCoeff

            For i As Integer = 0 To n - 1
                a(i) = -dc(i)
            Next

            Return a
        End Function

        ''' <summary>默认扩散边界系数：Δ_f·φ_b（Dirichlet 一致）。</summary>
        Protected Overridable Function ComputeGradientBoundaryCoeffs() As Double()
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

#End Region

        Public Overrides Function ToString() As String
            Return $"{Kind.ToString()}(patch='{Geometry.Name}', {Description})"
        End Function

    End Class

#Region "基础实现"

    ''' <summary>
    ''' 定值边界（Dirichlet）：φ_f = φ_b。
    ''' 这是基类默认系数的自然形态，因此不需要覆盖任何系数计算。
    ''' </summary>
    Public Class FixedValueBc
        Inherits EulerianBoundaryCondition

        Private ReadOnly _value As Double()

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.FixedValue
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return $"fixed value {DescribeValues()}"
            End Get
        End Property

        ''' <summary>定值边界直接规定边界速度。</summary>
        Public Overrides ReadOnly Property PrescribesVelocity As Boolean
            Get
                Return True
            End Get
        End Property

        ''' <summary>指定标量定值（作用于所有分量）。</summary>
        Public Sub New(patchField As EulerianPatchField, value As Double)
            MyBase.New(patchField)

            Dim c As Integer = std.Max(1, patchField.Components)

            _value = New Double(c - 1) {}

            For i As Integer = 0 To c - 1
                _value(i) = value
            Next
        End Sub

        ''' <summary>指定逐分量的定值（长度 = Components；也可只给分量值数组作向量定值）。</summary>
        Public Sub New(patchField As EulerianPatchField, values As Double())
            MyBase.New(patchField)

            If values Is Nothing OrElse values.Length = 0 Then
                Throw New ArgumentException("定值数组不能为空", NameOf(values))
            End If

            Dim c As Integer = std.Max(1, patchField.Components)

            If values.Length <> c Then
                Throw New ArgumentException($"定值数组长度 {values.Length} 与场的分量数 {c} 不一致", NameOf(values))
            End If

            _value = DirectCast(values.Clone(), Double())
        End Sub

        ''' <summary>第 comp 分量的定值。</summary>
        Public Function Value(component As Integer) As Double
            If _value.Length = 1 Then
                Return _value(0)
            End If

            Return _value(component)
        End Function

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            For comp As Integer = 0 To c - 1
                Dim v As Double = Value(comp)

                For i As Integer = 0 To n - 1
                    SetBoundaryValue(i, v, comp)
                Next
            Next
        End Sub

        Private Function DescribeValues() As String
            Dim parts As New List(Of String)

            For i As Integer = 0 To _value.Length - 1
                parts.Add(_value(i).ToString("G6", CultureInfo.InvariantCulture))
            Next

            Return "[" & String.Join(", ", parts) & "]"
        End Function
    End Class

    ''' <summary>
    ''' 零梯度边界（Neumann = 0）：φ_f = φ_P，扩散项无边界贡献。
    ''' 用于出口、绝热壁面、以及被固定通量替代的压力边界。
    ''' </summary>
    Public Class ZeroGradientBc
        Inherits EulerianBoundaryCondition

        Public Sub New(patchField As EulerianPatchField)
            MyBase.New(patchField)
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.ZeroGradient
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return "zero gradient"
            End Get
        End Property

        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            For comp As Integer = 0 To c - 1
                For i As Integer = 0 To n - 1
                    SetBoundaryValue(i, OwnerValue(i, comp), comp)
                Next
            Next
        End Sub

        ''' <summary>φ_f = φ_P。</summary>
        Protected Overrides Function ComputeValueInternalCoeffs() As Double()
            Dim a(FaceCount - 1) As Double

            For i As Integer = 0 To a.Length - 1
                a(i) = 1.0
            Next

            Return a
        End Function

        ''' <summary>无边界值贡献。</summary>
        Protected Overrides Function ComputeValueBoundaryCoeffs() As Double()
            Return New Double(FaceCount * std.Max(1, Components) - 1) {}
        End Function

        ''' <summary>无扩散边界贡献。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Return New Double(FaceCount - 1) {}
        End Function

        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Return New Double(FaceCount * std.Max(1, Components) - 1) {}
        End Function
    End Class

    ''' <summary>
    ''' 定梯度边界（Neumann）：∂φ/∂n = g。
    ''' 扩散项边界贡献 GradBoundary = g·|S_f|，内部系数为 0；边界面值按 g 外插。
    ''' </summary>
    Public Class FixedGradientBc
        Inherits EulerianBoundaryCondition

        Private ReadOnly _gradient As Double()

        Public Sub New(patchField As EulerianPatchField, gradient As Double)
            MyBase.New(patchField)

            Dim c As Integer = std.Max(1, patchField.Components)

            _gradient = New Double(c - 1) {}

            For i As Integer = 0 To c - 1
                _gradient(i) = gradient
            Next
        End Sub

        ''' <summary>指定逐分量梯度（长度 = Components）。</summary>
        Public Sub New(patchField As EulerianPatchField, gradients As Double())
            MyBase.New(patchField)

            If gradients Is Nothing OrElse gradients.Length = 0 Then
                Throw New ArgumentException("梯度数组不能为空", NameOf(gradients))
            End If

            _gradient = DirectCast(gradients.Clone(), Double())
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.FixedGradient
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return "fixed gradient"
            End Get
        End Property

        Public Function Gradient(component As Integer) As Double
            If _gradient.Length = 1 Then
                Return _gradient(0)
            End If

            Return _gradient(component)
        End Function

        ''' <summary>沿外法向的推进：φ_b = φ_P + g·|x_f − x_P|。</summary>
        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim delta As Moira.CFDEngine.Eulerian.EulerianVertex() = Geometry.Delta

            For comp As Integer = 0 To c - 1
                Dim g As Double = Gradient(comp)

                For i As Integer = 0 To n - 1
                    Dim dLen As Double = delta(i).Length
                    Dim val As Double = OwnerValue(i, comp)

                    If dLen > 1.0E-300 Then
                        val += g * dLen
                    End If

                    SetBoundaryValue(i, val, comp)
                Next
            Next
        End Sub

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

        ''' <summary>无隐式贡献（纯显式梯度）。</summary>
        Protected Overrides Function ComputeGradientInternalCoeffs() As Double()
            Return New Double(FaceCount - 1) {}
        End Function

        ''' <summary>Γ·(∇φ·S)_f = Γ·g·|S_f|。</summary>
        Protected Overrides Function ComputeGradientBoundaryCoeffs() As Double()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)
            Dim mag As Double() = Geometry.MagSf
            Dim a(n * c - 1) As Double

            For comp As Integer = 0 To c - 1
                Dim off As Integer = comp * n

                For i As Integer = 0 To n - 1
                    a(off + i) = Gradient(comp) * mag(i)
                Next
            Next

            Return a
        End Function
    End Class

    ''' <summary>
    ''' 对称面：法向通量为零、法向梯度为零。
    ''' 标量场等价于零梯度；矢量场由调用方保证"法向分量被置零"（见 EulerianField
    ''' 的 Symmetry 处理），本类只负责系数贡献。
    ''' </summary>
    Public Class SymmetryBc
        Inherits ZeroGradientBc

        Public Sub New(patchField As EulerianPatchField)
            MyBase.New(patchField)
        End Sub

        Public Overrides ReadOnly Property Kind As EulerianBcKind
            Get
                Return EulerianBcKind.Symmetry
            End Get
        End Property

        Public Overrides ReadOnly Property Description As String
            Get
                Return "symmetry plane"
            End Get
        End Property

        ''' <summary>对称面法向速度为零，等价于"规定了边界速度"。</summary>
        Public Overrides ReadOnly Property PrescribesVelocity As Boolean
            Get
                Return True
            End Get
        End Property

        ''' <summary>
        ''' 对称面：把边界面值取为内部值在切平面上的镜像（法向分量为零）。
        ''' </summary>
        Protected Overrides Sub UpdateBoundaryValues()
            Dim n As Integer = FaceCount
            Dim c As Integer = std.Max(1, Components)

            If c < 3 Then
                MyBase.UpdateBoundaryValues()
                Return
            End If

            For i As Integer = 0 To n - 1
                Dim nf As Moira.CFDEngine.Eulerian.EulerianVertex = Geometry.Sf(i).Normalized()
                Dim ux As Double = OwnerValue(i, 0)
                Dim uy As Double = OwnerValue(i, 1)
                Dim uz As Double = OwnerValue(i, 2)

                ' 去掉法向分量：u_b = u_P − (u_P·n̂)·n̂
                Dim un As Double = ux * nf.X + uy * nf.Y + uz * nf.Z

                SetBoundaryValue(i, ux - un * nf.X, 0)
                SetBoundaryValue(i, uy - un * nf.Y, 1)
                SetBoundaryValue(i, uz - un * nf.Z, 2)
            Next
        End Sub
    End Class

#End Region

End Namespace
