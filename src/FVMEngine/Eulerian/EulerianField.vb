' /********************************************************************************/
'
'   EulerianField.vb
'
'   场变量层 —— cell-centered 几何场（GeometricField 的等价物）。
'
'   对应 docs\FVM_Eulerian.md §三：
'       · internalField：cell-centered 存储，本工程用 double() 平铺；
'       · boundaryField：见 EulerianBoundaryField.vb；
'       · 派生的面值/点值不进本类，由离散算子层（EulerianOperators）按需插值。
'
'   数值布局（重要）—— 分量主序：
'       Values(component * CellCount + cell)
'   理由：
'       ① 求解 U（3 分量）时逐方向组装/求解，分量主序让每个方向都是连续内存遍历；
'       ② 与 GPU 端的"每次上传一个标量数组做稀疏求解"天然吻合；
'       ③ 与 CSR 的 cell 索引一一对应，无需跨步。
'       成本：矢量化的"逐单元取 u,v,w"需要三次跨步访问 —— 这类访问只出现在
'       非热点的源项/后处理里（见 docs\FVM_Eulerian.md 的混合精度分层）。
'
'   混合精度契约：
'       Values 恒为 double。转 float32 只发生在 <see cref="ToSingleComponent"/>
'       出口（GPU 上传）与 <see cref="SetFromSingleComponent"/> 入口（GPU 回读）。
'       统计量（Sum / MaxAbs / L2Norm）走 Microsoft.VisualBasic.Math.SIMD 的
'       SIMD 归约算子（SimdReduce），即"CPU 侧的 SIMD 加速"。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports Microsoft.VisualBasic.Math.SIMD
Imports std = System.Math

Namespace Eulerian

    ''' <summary>
    ''' 场在求解器中的角色。用于：
    '''   · 依据角色挑选默认边界条件（见 EulerianFieldFactory）；
    '''   · 诊断输出时决定打印哪些统计量与单位。
    ''' </summary>
    Public Enum EulerianFieldRole

        ''' <summary>普通标量（示踪剂 / 用户自定义）。</summary>
        Scalar = 0

        ''' <summary>速度（3 分量）。</summary>
        Velocity = 1

        ''' <summary>压力（Pa 或运动学压力）。</summary>
        Pressure = 2

        ''' <summary>湍动能 k。</summary>
        TurbulentKineticEnergy = 3

        ''' <summary>湍流耗散率 ε。</summary>
        TurbulentDissipation = 4

        ''' <summary>比耗散率 ω。</summary>
        TurbulentSpecificDissipation = 5

        ''' <summary>湍流粘度 νt。</summary>
        TurbulentViscosity = 6

        ''' <summary>相分数 α（VOF）。</summary>
        PhaseFraction = 7

        ''' <summary>密度 ρ。</summary>
        Density = 8

        ''' <summary>温度 T。</summary>
        Temperature = 9
    End Enum

    ''' <summary>
    ''' cell-centered 几何场：内部值（单元）+ 边界值（patch）。
    ''' </summary>
    Public Class EulerianField

#Region "构造与身份"

        ''' <summary>所属 FVM 网格（只读）。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>场名（用于日志、快照字段名）。</summary>
        Public ReadOnly Property Name As String

        ''' <summary>分量数：1 = 标量，3 = 矢量。</summary>
        Public ReadOnly Property Components As Integer

        ''' <summary>场角色（决定默认边界条件与诊断方式）。</summary>
        Public Property Role As EulerianFieldRole = EulerianFieldRole.Scalar

        ''' <summary>
        ''' 内部值（单元值），分量主序：Values(component * CellCount + cell)。
        ''' 长度恒为 <see cref="Size"/>。直接暴露数组引用，供离散算子热循环读写。
        ''' </summary>
        Public Property Values As Double()

        ''' <summary>逐 patch 的边界场。</summary>
        Public ReadOnly Property Boundaries As EulerianBoundaryField

        Public Sub New(mesh As EulerianFvMesh,
                       name As String,
                       Optional components As Integer = 1,
                       Optional role As EulerianFieldRole = EulerianFieldRole.Scalar)

            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            If components < 1 Then
                Throw New ArgumentOutOfRangeException(NameOf(components), "分量数必须 ≥ 1")
            End If

            Me.Mesh = mesh
            Me.Name = If(name, "field")
            Me.Components = components
            Me.Role = role
            Me.Values = New Double(mesh.CellCount * components - 1) {}
            Me.Boundaries = New EulerianBoundaryField(Me)
        End Sub

        ''' <summary>单元数。</summary>
        Public ReadOnly Property CellCount As Integer
            Get
                Return Mesh.CellCount
            End Get
        End Property

        ''' <summary>内部值数组长度（= CellCount × Components）。</summary>
        Public ReadOnly Property Size As Integer
            Get
                Return Mesh.CellCount * Components
            End Get
        End Property

        ''' <summary>是否为矢量场。</summary>
        Public ReadOnly Property IsVector As Boolean
            Get
                Return Components >= 3
            End Get
        End Property

        ''' <summary>分量 comp 在 <see cref="Values"/> 中的起始偏移。</summary>
        Public Function Offset(component As Integer) As Integer
            Return component * Mesh.CellCount
        End Function

        ''' <summary>分量名前缀（U → u,v,w；其它 → name_0, name_1, ...）。</summary>
        Public Function ComponentName(component As Integer) As String
            If Components >= 3 Then
                Select Case component
                    Case 0
                        Return "x"
                    Case 1
                        Return "y"
                    Case Else
                        Return "z"
                End Select
            End If

            If Components = 1 Then
                Return Name
            End If

            Return Name & "_" & component.ToString(CultureInfo.InvariantCulture)
        End Function

#End Region

#Region "单元值读写"

        ''' <summary>读单元 cell 的分量 comp。</summary>
        Public Function Value(cell As Integer, Optional component As Integer = 0) As Double
            Return Values(component * Mesh.CellCount + cell)
        End Function

        ''' <summary>写单元 cell 的分量 comp。</summary>
        Public Sub SetValue(cell As Integer, value As Double, Optional component As Integer = 0)
            Values(component * Mesh.CellCount + cell) = value
        End Sub

        ''' <summary>累加到单元 cell 的分量 comp。</summary>
        Public Sub AddValue(cell As Integer, delta As Double, Optional component As Integer = 0)
            Dim idx As Integer = component * Mesh.CellCount + cell
            Values(idx) += delta
        End Sub

        ''' <summary>全部内部分量填为常数。</summary>
        Public Sub FillValue(value As Double)
            For i As Integer = 0 To Values.Length - 1
                Values(i) = value
            Next
        End Sub

        ''' <summary>全部内部分量清零。</summary>
        Public Sub Zero()
            Array.Clear(Values, 0, Values.Length)
        End Sub

        ''' <summary>把另一个同构场的内部值整体拷入（要求尺寸一致）。</summary>
        Public Sub Assign(src As EulerianField)
            If src Is Nothing Then
                Throw New ArgumentNullException(NameOf(src))
            End If

            If src.Size <> Size Then
                Throw New ArgumentException($"场 '{src.Name}' 尺寸 {src.Size} 与 '{Name}' 尺寸 {Size} 不一致", NameOf(src))
            End If

            Array.Copy(src.Values, Values, Values.Length)
        End Sub

        ''' <summary>深拷贝（含边界场配置引用，不含边界值）。</summary>
        Public Function Clone() As EulerianField
            Dim f As New EulerianField(Mesh, Name, Components, Role)
            Array.Copy(Values, f.Values, Values.Length)
            f.Boundaries.UpdateAll()
            Return f
        End Function

        ''' <summary>深拷贝并复制边界值。</summary>
        Public Function CloneWithBoundary() As EulerianField
            Dim f As EulerianField = Clone()

            For p As Integer = 0 To Boundaries.Count - 1
                Dim src As EulerianPatchField = Boundaries(p)
                Dim dst As EulerianPatchField = f.Boundaries(p)
                Array.Copy(src.Values, dst.Values, src.Values.Length)
                dst.Condition = src.Condition
            Next

            Return f
        End Function

#End Region

#Region "边界"

        ''' <summary>全量刷新边界值（内部场更新后调用）。</summary>
        Public Sub UpdateBoundaries()
            Boundaries.UpdateAll()
        End Sub

        ''' <summary>取某 patch 的边界条件（未绑定返回 Nothing）。</summary>
        Public Function BoundaryCondition(patchName As String) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = Boundaries.Patch(patchName)
            Return If(pf Is Nothing, Nothing, pf.Condition)
        End Function

        ''' <summary>绑定定值边界（Dirichlet，标量值作用于所有分量）。</summary>
        Public Function SetFixedValue(patchName As String, value As Double) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = RequirePatch(patchName)
            Dim bc As New FixedValueBc(pf, value)
            pf.Condition = bc
            Call pf.Evaluate()
            Return bc
        End Function

        ''' <summary>绑定逐分量定值边界（Dirichlet）。</summary>
        Public Function SetFixedValue(patchName As String, values As Double()) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = RequirePatch(patchName)
            Dim bc As New FixedValueBc(pf, values)
            pf.Condition = bc
            Call pf.Evaluate()
            Return bc
        End Function

        ''' <summary>绑定零梯度边界（Neumann = 0）。</summary>
        Public Function SetZeroGradient(patchName As String) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = RequirePatch(patchName)
            Dim bc As New ZeroGradientBc(pf)
            pf.Condition = bc
            Call pf.Evaluate()
            Return bc
        End Function

        ''' <summary>绑定定梯度边界（Neumann = g）。</summary>
        Public Function SetFixedGradient(patchName As String, gradient As Double) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = RequirePatch(patchName)
            Dim bc As New FixedGradientBc(pf, gradient)
            pf.Condition = bc
            Call pf.Evaluate()
            Return bc
        End Function

        ''' <summary>绑定对称面边界。</summary>
        Public Function SetSymmetry(patchName As String) As EulerianBoundaryCondition
            Dim pf As EulerianPatchField = RequirePatch(patchName)
            Dim bc As New SymmetryBc(pf)
            pf.Condition = bc
            Call pf.Evaluate()
            Return bc
        End Function

        Private Function RequirePatch(patchName As String) As EulerianPatchField
            Dim pf As EulerianPatchField = Boundaries.Patch(patchName)

            If pf Is Nothing Then
                Throw New KeyNotFoundException($"场 '{Name}' 上找不到 patch '{patchName}'；现有 patch：{Mesh.PatchNames()}")
            End If

            Return pf
        End Function

#End Region

#Region "统计（CPU SIMD 归约）"

        ''' <summary>全部值之和（SIMD 归约）。</summary>
        Public Function Sum() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.Sum(Values)
        End Function

        ''' <summary>全部值绝对值之和。</summary>
        Public Function SumAbs() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.L1Norm(Values)
        End Function

        ''' <summary>全部值的 L2 范数。</summary>
        Public Function L2Norm() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.L2Norm(Values)
        End Function

        ''' <summary>绝对值最大值。</summary>
        Public Function MaxAbs() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.MaxAbs(Values)
        End Function

        ''' <summary>最小值。</summary>
        Public Function MinValue() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.Min(Values)
        End Function

        ''' <summary>最大值。</summary>
        Public Function MaxValue() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.Max(Values)
        End Function

        ''' <summary>均值。</summary>
        Public Function Mean() As Double
            If Values.Length = 0 Then Return 0
            Return SimdReduce.Mean(Values)
        End Function

        ''' <summary>
        ''' 单个分量的绝对值最大值（逐分量限制器、SST 混合函数等需要）。
        ''' </summary>
        Public Function MaxAbsComponent(component As Integer) As Double
            Dim off As Integer = component * Mesh.CellCount
            Dim m As Double = 0

            For i As Integer = 0 To Mesh.CellCount - 1
                Dim v As Double = std.Abs(Values(off + i))

                If v > m Then m = v
            Next

            Return m
        End Function

        ''' <summary>单个分量的最小值。</summary>
        Public Function MinComponent(component As Integer) As Double
            Dim off As Integer = component * Mesh.CellCount
            Dim m As Double = Double.MaxValue

            For i As Integer = 0 To Mesh.CellCount - 1
                Dim v As Double = Values(off + i)

                If v < m Then m = v
            Next

            Return If(m = Double.MaxValue, 0, m)
        End Function

        ''' <summary>单个分量的最大值。</summary>
        Public Function MaxComponent(component As Integer) As Double
            Dim off As Integer = component * Mesh.CellCount
            Dim m As Double = Double.MinValue

            For i As Integer = 0 To Mesh.CellCount - 1
                Dim v As Double = Values(off + i)

                If v > m Then m = v
            Next

            Return If(m = Double.MinValue, 0, m)
        End Function

        ''' <summary>矢量场的模长（仅 Components ≥ 3 时有意义）。</summary>
        Public Function Magnitude() As Double()
            If Components < 3 Then
                Throw New InvalidOperationException($"场 '{Name}' 不是矢量场（Components={Components}），无法取模长")
            End If

            Return EulerianVectorMath.VectorMagnitude(Values, Mesh.CellCount)
        End Function

        ''' <summary>加权平均（如按体积加权求平均浓度）。</summary>
        Public Function VolumeWeightedAverage(Optional component As Integer = 0) As Double
            Dim v As Double() = Mesh.CellVolumeArray()
            Dim off As Integer = component * Mesh.CellCount
            Dim num As Double = 0
            Dim den As Double = 0

            For i As Integer = 0 To Mesh.CellCount - 1
                Dim w As Double = v(i)

                num += w * Values(off + i)
                den += w
            Next

            If den <= 0 Then Return 0

            Return num / den
        End Function

#End Region

#Region "混合精度转换（double ↔ float32）"

        ''' <summary>整个内部场降为 float32（GPU / float32 算子入口）。</summary>
        Public Function ToSingle() As Single()
            Dim a(Values.Length - 1) As Single

            For i As Integer = 0 To Values.Length - 1
                a(i) = CSng(Values(i))
            Next

            Return a
        End Function

        ''' <summary>用 float32 数据整体覆盖内部场（GPU 回读）。</summary>
        Public Sub SetFromSingle(data As Single())
            If data Is Nothing OrElse data.Length <> Values.Length Then
                Throw New ArgumentException($"float32 数据长度必须为 {Values.Length}", NameOf(data))
            End If

            For i As Integer = 0 To Values.Length - 1
                Values(i) = data(i)
            Next
        End Sub

        ''' <summary>
        ''' 取单个分量为 float32 数组（长度 = CellCount）。
        ''' 这是"上传到 GPU 做稀疏线性求解"的标准出口。
        ''' </summary>
        Public Function ToSingleComponent(component As Integer) As Single()
            Dim off As Integer = component * Mesh.CellCount
            Dim n As Integer = Mesh.CellCount
            Dim a(n - 1) As Single

            For i As Integer = 0 To n - 1
                a(i) = CSng(Values(off + i))
            Next

            Return a
        End Function

        ''' <summary>
        ''' 用 float32 的分量数据写回单个分量（长度 = CellCount）。
        ''' 这是"从 GPU 回读解向量"的标准入口。
        ''' </summary>
        Public Sub SetFromSingleComponent(component As Integer, data As Single())
            Dim n As Integer = Mesh.CellCount

            If data Is Nothing OrElse data.Length <> n Then
                Throw New ArgumentException($"float32 分量数据长度必须为 {n}", NameOf(data))
            End If

            Dim off As Integer = component * n

            For i As Integer = 0 To n - 1
                Values(off + i) = data(i)
            Next
        End Sub

        ''' <summary>取单个分量的 double 数组拷贝（长度 = CellCount）。</summary>
        Public Function ToComponent(component As Integer) As Double()
            Dim n As Integer = Mesh.CellCount
            Dim a(n - 1) As Double

            Array.Copy(Values, component * n, a, 0, n)
            Return a
        End Function

        ''' <summary>用 double 的分量数据写回单个分量（长度 = CellCount）。</summary>
        Public Sub SetFromComponent(component As Integer, data As Double())
            Dim n As Integer = Mesh.CellCount

            If data Is Nothing OrElse data.Length <> n Then
                Throw New ArgumentException($"分量数据长度必须为 {n}", NameOf(data))
            End If

            Array.Copy(data, 0, Values, component * n, n)
        End Sub

#End Region

        ''' <summary>单行摘要（日志用）。</summary>
        Public Function Summary() As String
            Return $"{Name}({Components} comp): min={MinValue().ToString("G6", CultureInfo.InvariantCulture)}, " &
                   $"max={MaxValue().ToString("G6", CultureInfo.InvariantCulture)}, " &
                   $"mean={Mean().ToString("G6", CultureInfo.InvariantCulture)}"
        End Function

        Public Overrides Function ToString() As String
            Return $"EulerianField({Name}, {Components} comp, {Mesh.CellCount} cells, {Role.ToString()})"
        End Function

    End Class

    ''' <summary>
    ''' 一个算例所需的场集合：不可压/可压缩共用的标准场 + 扩展标量场字典。
    ''' </summary>
    Public Class EulerianFieldSet

#Region "构造"

        ''' <summary>所属网格。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>速度 U（3 分量）。</summary>
        Public ReadOnly Property U As EulerianField
            Get
                Return _U
            End Get
        End Property

        ''' <summary>压力 p。</summary>
        Public ReadOnly Property P As EulerianField
            Get
                Return _P
            End Get
        End Property

        ''' <summary>温度 T（可压缩 / 传热）。</summary>
        Public ReadOnly Property T As EulerianField
            Get
                Return _T
            End Get
        End Property

        ''' <summary>湍动能 k。</summary>
        Public ReadOnly Property K As EulerianField
            Get
                Return _K
            End Get
        End Property

        ''' <summary>湍流耗散率 ε。</summary>
        Public ReadOnly Property Epsilon As EulerianField
            Get
                Return _Epsilon
            End Get
        End Property

        ''' <summary>比耗散率 ω（SST）。</summary>
        Public ReadOnly Property Omega As EulerianField
            Get
                Return _Omega
            End Get
        End Property

        ''' <summary>湍流粘度 νt（派生量）。</summary>
        Public ReadOnly Property Nut As EulerianField
            Get
                Return _Nut
            End Get
        End Property

        ''' <summary>相分数 α（VOF）。</summary>
        Public ReadOnly Property Alpha As EulerianField
            Get
                Return _Alpha
            End Get
        End Property

        ''' <summary>混合密度 ρ。</summary>
        Public ReadOnly Property Rho As EulerianField
            Get
                Return _Rho
            End Get
        End Property

        ''' <summary>混合动力粘度 μ。</summary>
        Public ReadOnly Property Mu As EulerianField
            Get
                Return _Mu
            End Get
        End Property

        ''' <summary>扩展标量场（示踪剂 / 物种 / 自定义）。</summary>
        Public ReadOnly Property Extra As Dictionary(Of String, EulerianField)

        Public Sub New(mesh As EulerianFvMesh)
            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            Me.Mesh = mesh

            _U = New EulerianField(mesh, "U", 3, EulerianFieldRole.Velocity)
            _P = New EulerianField(mesh, "p", 1, EulerianFieldRole.Pressure)
            _T = New EulerianField(mesh, "T", 1, EulerianFieldRole.Temperature)
            _K = New EulerianField(mesh, "k", 1, EulerianFieldRole.TurbulentKineticEnergy)
            _Epsilon = New EulerianField(mesh, "epsilon", 1, EulerianFieldRole.TurbulentDissipation)
            _Omega = New EulerianField(mesh, "omega", 1, EulerianFieldRole.TurbulentSpecificDissipation)
            _Nut = New EulerianField(mesh, "nut", 1, EulerianFieldRole.TurbulentViscosity)
            _Alpha = New EulerianField(mesh, "alpha", 1, EulerianFieldRole.PhaseFraction)
            _Rho = New EulerianField(mesh, "rho", 1, EulerianFieldRole.Density)
            _Mu = New EulerianField(mesh, "mu", 1, EulerianFieldRole.Scalar)

            Me.Extra = New Dictionary(Of String, EulerianField)(StringComparer.OrdinalIgnoreCase)
        End Sub

        Private ReadOnly _U As EulerianField
        Private ReadOnly _P As EulerianField
        Private ReadOnly _T As EulerianField
        Private ReadOnly _K As EulerianField
        Private ReadOnly _Epsilon As EulerianField
        Private ReadOnly _Omega As EulerianField
        Private ReadOnly _Nut As EulerianField
        Private ReadOnly _Alpha As EulerianField
        Private ReadOnly _Rho As EulerianField
        Private ReadOnly _Mu As EulerianField

#End Region

#Region "访问"

        ''' <summary>注册（或取回）一个扩展标量场。</summary>
        Public Function Scalar(name As String) As EulerianField
            Dim f As EulerianField = Nothing

            If Extra.TryGetValue(name, f) Then
                Return f
            End If

            f = New EulerianField(Mesh, name, 1, EulerianFieldRole.Scalar)
            Extra(name) = f
            Return f
        End Function

        ''' <summary>全部场（用于批量边界刷新与快照导出）。</summary>
        Public Function AllFields() As List(Of EulerianField)
            Dim list As New List(Of EulerianField) From {
                _U, _P, _T, _K, _Epsilon, _Omega, _Nut, _Alpha, _Rho, _Mu}

            For Each kv As KeyValuePair(Of String, EulerianField) In Extra
                list.Add(kv.Value)
            Next

            Return list
        End Function

        ''' <summary>按名称取场（含别名字段）；未找到返回 Nothing。</summary>
        Public Function Find(name As String) As EulerianField
            For Each f As EulerianField In AllFields()
                If String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) Then
                    Return f
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>刷新全部场的边界值。</summary>
        Public Sub UpdateBoundaries()
            For Each f As EulerianField In AllFields()
                f.UpdateBoundaries()
            Next
        End Sub

        ''' <summary>失效全部场的边界系数缓存。</summary>
        Public Sub InvalidateBoundaryCoefficients()
            For Each f As EulerianField In AllFields()
                f.Boundaries.InvalidateAll()
            Next
        End Sub

        ''' <summary>全部场摘要（多行）。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(512)

                For Each f As EulerianField In AllFields()
                    sb.AppendLine($"  {f.Summary()}")
                Next

                Return sb.ToString()
            End Get
        End Property

#End Region

    End Class

End Namespace
