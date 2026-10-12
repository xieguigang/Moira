' /********************************************************************************/
'
'   FvMatrix.vb
'
'   代数系统层 —— 有限体积离散得到的稀疏线性方程组（OpenFOAM fvMatrix 的等价物）。
'
'   对应 docs\FVM_Eulerian.md §4.4：
'       对每个单元 P 得到    a_P·φ_P + Σ_N a_N·φ_N = b_P
'
'   存储布局：直接复用 <see cref="EulerianFvMesh"/> 预处理好的 CSR 结构。
'         · 每行首元素 = 对角元 a_P（列 = P 自身）；
'         · 其余元素 = 内部面贡献 a_N（列 = 邻元）；
'         · 内部面 f 的系数分别落在 Owner 行的 OwnerEntry(f) 与
'           Neighbour 行的 NeighbourEntry(f) 上；
'         · 矩阵结构（RowPtr / ColIdx）在整个求解过程中不变，
'           每次外迭代只 Reset 数值 —— 这正是 GPU 端"一次上传结构、
'           多轮显存内迭代"的前提。
'
'   边界条件的对角与源项贡献由边界条件自己通过
'   <see cref="AddDiag"/> / <see cref="AddSource"/> 累加进来（见 EulerianOperators）。
'
'   混合精度契约：
'       Values / Source 恒为 double（系数组装的正确性依赖双精度的量级跨度，
'       例如扩散系数 Δ_f 与体积 V_P 相差 10 个数量级时）。float32 只用于
'       内迭代线性求解（EulerianFvmCompute），且必须"回读后在 double 下重算残差"。
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
    ''' 稀疏线性方程组 a_P·φ_P + Σ_N a_N·φ_N = b_P（CSR 存储，结构由网格固定）。
    ''' </summary>
    Public Class FvMatrix

#Region "构造"

        ''' <summary>所属 FVM 网格（提供 CSR 结构与几何量）。</summary>
        Public ReadOnly Property Mesh As EulerianFvMesh

        ''' <summary>方程名（日志 / 诊断用，如 "Ux"、"p"、"k"）。</summary>
        Public Property Name As String

        ''' <summary>
        ''' 系数矩阵是否对称（决定线性求解器能否用 PCG）。
        ''' 纯扩散方程对称；含对流则非对称。
        ''' </summary>
        Public Property Symmetric As Boolean = False

        Private ReadOnly _values As Double()
        Private ReadOnly _source As Double()

        Public Sub New(mesh As EulerianFvMesh, Optional name As String = "equation")
            If mesh Is Nothing Then
                Throw New ArgumentNullException(NameOf(mesh))
            End If

            Me.Mesh = mesh
            Me.Name = name
            Me._values = New Double(mesh.NonZeroCount - 1) {}
            Me._source = New Double(mesh.CellCount - 1) {}
        End Sub

#End Region

#Region "数据视图"

        ''' <summary>CSR 系数数组（长度 = <see cref="NonZeroCount"/>；每行首元素为对角元）。</summary>
        Public ReadOnly Property Values As Double()
            Get
                Return _values
            End Get
        End Property

        ''' <summary>右端项 b（长度 = <see cref="CellCount"/>）。</summary>
        Public ReadOnly Property Source As Double()
            Get
                Return _source
            End Get
        End Property

        ''' <summary>单元数（= 方程数）。</summary>
        Public ReadOnly Property CellCount As Integer
            Get
                Return Mesh.CellCount
            End Get
        End Property

        ''' <summary>非零元数（含对角元）。</summary>
        Public ReadOnly Property NonZeroCount As Integer
            Get
                Return Mesh.NonZeroCount
            End Get
        End Property

        ''' <summary>系数与源项全部清零（保留 CSR 结构）。</summary>
        Public Sub Reset()
            Array.Clear(_values, 0, _values.Length)
            Array.Clear(_source, 0, _source.Length)
            Symmetric = False
        End Sub

#End Region

#Region "对角元"

        ''' <summary>单元 cell 的对角系数 a_P。</summary>
        Public Function Diag(cell As Integer) As Double
            Return _values(Mesh.DiagEntry(cell))
        End Function

        ''' <summary>写单元 cell 的对角系数。</summary>
        Public Sub SetDiag(cell As Integer, value As Double)
            _values(Mesh.DiagEntry(cell)) = value
        End Sub

        ''' <summary>累加单元 cell 的对角系数。</summary>
        Public Sub AddDiag(cell As Integer, value As Double)
            _values(Mesh.DiagEntry(cell)) += value
        End Sub

        ''' <summary>提取对角系数数组（长度 = CellCount）。</summary>
        Public Function DiagonalArray() As Double()
            Dim n As Integer = Mesh.CellCount
            Dim a(n - 1) As Double
            Dim de As Integer() = Mesh.DiagEntry

            For c As Integer = 0 To n - 1
                a(c) = _values(de(c))
            Next

            Return a
        End Function

#End Region

#Region "内部面非对角元"

        ''' <summary>内部面 face 落在 Owner 行（列 = Neighbour）的系数 a_N。</summary>
        Public Function OwnerCoeff(face As Integer) As Double
            Dim e As Integer = Mesh.OwnerEntry(face)

            If e < 0 Then Return 0

            Return _values(e)
        End Function

        ''' <summary>内部面 face 落在 Neighbour 行（列 = Owner）的系数。</summary>
        Public Function NeighbourCoeff(face As Integer) As Double
            Dim e As Integer = Mesh.NeighbourEntry(face)

            If e < 0 Then Return 0

            Return _values(e)
        End Function

        ''' <summary>累加内部面 face 在 Owner 行的非对角系数。</summary>
        Public Sub AddOwnerCoeff(face As Integer, value As Double)
            _values(Mesh.OwnerEntry(face)) += value
        End Sub

        ''' <summary>累加内部面 face 在 Neighbour 行的非对角系数。</summary>
        Public Sub AddNeighbourCoeff(face As Integer, value As Double)
            _values(Mesh.NeighbourEntry(face)) += value
        End Sub

        ''' <summary>
        ''' 对称累加一个内部面的系数：Owner 行的 (P,N) 项与 Neighbour 行的 (N,P) 项。
        ''' </summary>
        Public Sub AddFaceCoefficients(face As Integer, ownerRowValue As Double, neighbourRowValue As Double)
            _values(Mesh.OwnerEntry(face)) += ownerRowValue
            _values(Mesh.NeighbourEntry(face)) += neighbourRowValue
        End Sub

#End Region

#Region "源项"

        ''' <summary>累加单元 cell 的源项 b_P。</summary>
        Public Sub AddSource(cell As Integer, value As Double)
            _source(cell) += value
        End Sub

        ''' <summary>读单元 cell 的源项。</summary>
        Public Function SourceAt(cell As Integer) As Double
            Return _source(cell)
        End Function

        ''' <summary>累加整场源项（按单元顺序）。</summary>
        Public Sub AddSourceField(sourcePerCell As Double())
            If sourcePerCell Is Nothing Then Return

            Dim n As Integer = std.Min(sourcePerCell.Length, _source.Length)

            For c As Integer = 0 To n - 1
                _source(c) += sourcePerCell(c)
            Next
        End Sub

#End Region

#Region "矩阵运算"

        ''' <summary>稀疏矩阵–向量乘：y = A·x（含对角元）。</summary>
        Public Sub Multiply(x As Double(), y As Double())
            Dim rp As Integer() = Mesh.RowPtr
            Dim ci As Integer() = Mesh.ColIdx
            Dim n As Integer = Mesh.CellCount

            For c As Integer = 0 To n - 1
                Dim s As Double = 0
                Dim e0 As Integer = rp(c)
                Dim e1 As Integer = rp(c + 1) - 1

                For e As Integer = e0 To e1
                    s += _values(e) * x(ci(e))
                Next

                y(c) = s
            Next
        End Sub

        ''' <summary>
        ''' H 算子：对角以外部分的贡献 H(P) = Σ_N a_N·φ_N（用给定场值）。
        ''' 用于 SIMPLE / PIMPLE 的动量插值 HbyA = (H + b)/aP。
        ''' </summary>
        Public Sub H(fieldValues As Double(), result As Double())
            Dim rp As Integer() = Mesh.RowPtr
            Dim ci As Integer() = Mesh.ColIdx
            Dim de As Integer() = Mesh.DiagEntry
            Dim n As Integer = Mesh.CellCount

            For c As Integer = 0 To n - 1
                Dim s As Double = 0
                Dim e0 As Integer = rp(c)
                Dim e1 As Integer = rp(c + 1) - 1
                Dim d As Integer = de(c)

                For e As Integer = e0 To e1
                    If e <> d Then
                        s += _values(e) * fieldValues(ci(e))
                    End If
                Next

                result(c) = s
            Next
        End Sub

        ''' <summary>
        ''' 残差向量 r = A·x − b（长度 = CellCount）。
        ''' 标准残差 r = b − A·x。
        ''' </summary>
        ''' <remarks>
        ''' ★ 约定必须与 Krylov 驱动一致：CG/BiCGStab 的搜索方向 p 与更新
        '''   x ← x + α·p、r ← r − α·A·p 都以「r = b − A·x」为前提。
        '''   若返回 A·x − b（符号相反），迭代会收敛到 −A⁻¹b，
        '''   表现为「残差降得很低但解整体反号」——极难定位，此处务必保持标准约定。
        ''' </remarks>
        Public Sub ResidualVector(x As Double(), r As Double())
            Dim rp As Integer() = Mesh.RowPtr
            Dim ci As Integer() = Mesh.ColIdx
            Dim n As Integer = Mesh.CellCount

            For c As Integer = 0 To n - 1
                Dim s As Double = 0
                Dim e0 As Integer = rp(c)
                Dim e1 As Integer = rp(c + 1) - 1

                For e As Integer = e0 To e1
                    s += _values(e) * x(ci(e))
                Next

                r(c) = _source(c) - s
            Next
        End Sub

        ''' <summary>残差 r = b − A·x 的 L2 范数（SIMD 归约）。</summary>
        Public Function ResidualNorm(x As Double()) As Double
            Dim n As Integer = Mesh.CellCount
            Dim r(n - 1) As Double

            Call ResidualVector(x, r)
            Return SimdReduce.L2Norm(r)
        End Function

        ''' <summary>右端项的 L2 范数（归一化残差的分母）。</summary>
        Public Function SourceNorm() As Double
            If _source.Length = 0 Then Return 0
            Return SimdReduce.L2Norm(_source)
        End Function

        ''' <summary>
        ''' 归一化残差：|A·x − b| / (normFactor + |b|)。
        ''' 对应 docs\FVM_Eulerian.md §8.2 的"初始残差 + 起始残差归一化"约定。
        ''' </summary>
        Public Function NormalisedResidual(x As Double(), Optional normFactor As Double = 1.0E-30) As Double
            Dim raw As Double = ResidualNorm(x)
            Dim den As Double = normFactor + SourceNorm()

            If den <= 0 Then Return raw

            Return raw / den
        End Function

#End Region

#Region "欠松弛与参考值"

        ''' <summary>
        ''' 隐式欠松弛（OpenFOAM fvMatrix::relax 语义）：
        '''     a_P ← a_P/α ， b_P ← b_P + (a_P/α − a_P_old)·φ_P
        ''' 保证松弛后的解 `φ_P = (H + b)/(a_P/α)` 等于 α·φ_P^new + (1−α)·φ_P^old。
        ''' </summary>
        ''' <param name="alpha">松弛因子（0 &lt; α ≤ 1；SIMPLE 稳态推荐速度 0.7、压力 0.3）。</param>
        ''' <param name="current">当前场值（分量数组，长度 = CellCount）。</param>
        Public Sub Relax(alpha As Double, current As Double())
            If alpha >= 1.0 Then Return

            If alpha <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(alpha), $"松弛因子必须为正（收到 {alpha}）")
            End If

            Dim de As Integer() = Mesh.DiagEntry

            For c As Integer = 0 To Mesh.CellCount - 1
                Dim e As Integer = de(c)
                Dim aOld As Double = _values(e)
                Dim aNew As Double = aOld / alpha

                _values(e) = aNew
                _source(c) += (aNew - aOld) * current(c)
            Next
        End Sub

        ''' <summary>
        ''' 固定参考值（压力方程在封闭域内需要一个基准）：
        ''' 把单元 cell 的方程替换为硬约束 φ_cell = value。
        ''' </summary>
        Public Sub SetReference(cell As Integer, value As Double)
            Dim rp As Integer() = Mesh.RowPtr
            Dim de As Integer() = Mesh.DiagEntry
            Dim d As Integer = de(cell)
            Dim diag As Double = _values(d)

            ' 行内非对角项清零（硬约束）
            For e As Integer = rp(cell) To rp(cell + 1) - 1
                If e <> d Then
                    _values(e) = 0
                End If
            Next

            _source(cell) = If(diag <> 0, diag * value, value)
        End Sub

#End Region

#Region "一致性与诊断"

        Private _zeroDiagCount As Integer

        ''' <summary>上一条对角元被修正的单元数（<see cref="Normalise"/> 的副产品）。</summary>
        Public ReadOnly Property ZeroDiagonalCount As Integer
            Get
                Return _zeroDiagCount
            End Get
        End Property

        ''' <summary>
        ''' 对角元一致性修正：把 a_P == 0 / NaN 的孤立单元（例如完全被壁面包围、
        ''' 且没有源项贡献的多面体切割单元）替换为 φ_P = 0 的硬约束，避免求解器
        ''' 出现 0/0。返回被修正的单元数。
        ''' </summary>
        Public Function Normalise() As Integer
            Dim de As Integer() = Mesh.DiagEntry
            Dim fixed As Integer = 0

            ' ★ 先清理**全部**系数与源项中的 NaN / Inf。
            '   非结构网格（尤其八叉树切割单元）上，个别退化面会产生
            '   Δ_f = Inf 或 k_f 修正为 NaN；若不清理，一次 SpMV 就会把 NaN
            '   扩散到全场（表现为"第 1 步就 NaN"，且无法定位到具体单元）。
            '   这里把非法非对角元置 0（等价于删掉该耦合），把非法源项置 0。
            For e As Integer = 0 To _values.Length - 1
                Dim v As Double = _values(e)

                If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then
                    _values(e) = 0
                End If
            Next

            For c As Integer = 0 To Mesh.CellCount - 1
                Dim s As Double = _source(c)

                If Double.IsNaN(s) OrElse Double.IsInfinity(s) Then
                    _source(c) = 0
                End If

                Dim v As Double = _values(de(c))

                If Double.IsNaN(v) OrElse Double.IsInfinity(v) Then
                    _values(de(c)) = 1.0
                    _source(c) = 0
                    fixed += 1
                ElseIf std.Abs(v) < 1.0E-300 Then
                    ' 退化：对角给 1，源项保持不变（等价于 a_P·φ_P = b_P 的可解形式）
                    _values(de(c)) = 1.0
                    fixed += 1
                End If
            Next

            _zeroDiagCount = fixed
            Return fixed
        End Function

        ''' <summary>矩阵摘要（多行）。</summary>
        Public ReadOnly Property Diagnostics As String
            Get
                Dim sb As New StringBuilder(256)

                sb.AppendLine($"fvMatrix '{Name}': n={Mesh.CellCount}, nnz={Mesh.NonZeroCount}, symmetric={Symmetric}")
                sb.AppendLine($"  |a_P| in [{MinDiag().ToString("G6", CultureInfo.InvariantCulture)}, " &
                              $"{MaxDiag().ToString("G6", CultureInfo.InvariantCulture)}]")
                sb.AppendLine($"  |b| (L2) = {SourceNorm().ToString("G6", CultureInfo.InvariantCulture)}")

                If _zeroDiagCount > 0 Then
                    sb.AppendLine($"  warning: {_zeroDiagCount} cells had zero/NaN diagonal and were normalised")
                End If

                Return sb.ToString()
            End Get
        End Property

        ''' <summary>对角元绝对值最小者。</summary>
        Public Function MinDiag() As Double
            Dim de As Integer() = Mesh.DiagEntry
            Dim m As Double = Double.MaxValue

            For c As Integer = 0 To Mesh.CellCount - 1
                Dim v As Double = std.Abs(_values(de(c)))

                If v < m Then m = v
            Next

            Return If(m = Double.MaxValue, 0, m)
        End Function

        ''' <summary>对角元绝对值最大者。</summary>
        Public Function MaxDiag() As Double
            Dim de As Integer() = Mesh.DiagEntry
            Dim m As Double = 0

            For c As Integer = 0 To Mesh.CellCount - 1
                Dim v As Double = std.Abs(_values(de(c)))

                If v > m Then m = v
            Next

            Return m
        End Function

        Public Overrides Function ToString() As String
            Return $"FvMatrix({Name}, n={Mesh.CellCount}, nnz={Mesh.NonZeroCount})"
        End Function

#End Region

    End Class

End Namespace
