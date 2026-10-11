' /********************************************************************************/
'
'   EulerianLinearSolver.vb
'
'   求解器层 —— 非结构稀疏线性系统的迭代求解（docs\FVM_Eulerian.md §八）。
'
'   已实现：
'       PCG        预条件共轭梯度（对称正定，用于压力 Poisson 与纯扩散方程）
'       BiCGStab   稳定双共轭梯度（非对称，用于动量与标量输运方程）
'       GaussSeidel 前向 / 反向（平滑器；也在"动量预测只扫几次"时使用）
'       对称 GS    正反两趟（仅对对称矩阵有效，用作多重网格风格平滑器）
'       Jacobi     带阻尼的 Jacobi（最稳健的兜底平滑器）
'
'   预条件子：
'       Diagonal   Jacobi 预条件（总是可用）
'       Ilu0       零填充不完全 LU（对压力 Poisson 有数量级的加速）
'       —— 组装阶段若发现零主元/数值爆炸，Ilu0 会自动降级为 Diagonal，
'          并在报告里标记（生产环境可据此判断网格质量）。
'
'   收敛判据（docs\FVM_Eulerian.md §8.2）：
'       归一化残差  = |A·x − b| / (tiny + |b|)
'       收敛条件    = 归一化残差 ≤ tolerance  或  相对起始残差下降 ≥ 1/relTol
'       报告同时给出 Iterations / InitialResidual / FinalResidual / Converged。
'
'   混合精度契约：
'       本文件是 **double CPU 参考实现**（正确的兜底路径 + 自测对照基准）。
'       GPU float32 路径通过 <see cref="EulerianLinearSolve.GpuBackend"/> 注入
'       （见 EulerianFvmCompute.vb）；GPU 迭代结束后必须回读并在 double 下重算
'       残差，再决定是否收敛 —— 绝不能用 float32 残差做收敛判定。
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports Microsoft.VisualBasic.Math.SIMD
Imports std = System.Math

Namespace Eulerian

    ''' <summary>线性求解器种类。</summary>
    Public Enum EulerianLinearSolverKind

        ''' <summary>自动：对称矩阵用 PCG，非对称用 BiCGStab。</summary>
        Auto = 0

        ''' <summary>预条件共轭梯度（要求对称正定）。</summary>
        Pcg = 1

        ''' <summary>稳定双共轭梯度。</summary>
        BiCGStab = 2

        ''' <summary>前向 Gauss-Seidel 迭代。</summary>
        GaussSeidel = 3

        ''' <summary>对称 Gauss-Seidel（正反两趟，仅对称矩阵）。</summary>
        SymmetricGaussSeidel = 4

        ''' <summary>阻尼 Jacobi。</summary>
        Jacobi = 5
    End Enum

    ''' <summary>线性求解报告。</summary>
    Public Class EulerianSolverReport

        ''' <summary>方程名。</summary>
        Public Property Equation As String = ""

        ''' <summary>实际使用的求解器。</summary>
        Public Property Solver As EulerianLinearSolverKind = EulerianLinearSolverKind.Auto

        ''' <summary>执行迭代数。</summary>
        Public Property Iterations As Integer = 0

        ''' <summary>迭代前的归一化起始残差。</summary>
        Public Property InitialResidual As Double = 0

        ''' <summary>迭代后的归一化残差。</summary>
        Public Property FinalResidual As Double = 0

        ''' <summary>是否达到收敛判据。</summary>
        Public Property Converged As Boolean = False

        ''' <summary>耗时（毫秒）。</summary>
        Public Property ElapsedMs As Double = 0

        ''' <summary>实际生效的预条件子名称。</summary>
        Public Property Preconditioner As String = "none"

        ''' <summary>是否由 GPU float32 后端完成。</summary>
        Public Property ExecutedOnGpu As Boolean = False

        ''' <summary>附加说明（例如预条件子降级原因、对角元修正数量）。</summary>
        Public Property Notes As String = ""

        ''' <summary>残差下降量级（起始/最终）。</summary>
        Public ReadOnly Property ResidualDrop As Double
            Get
                If FinalResidual <= 0 Then Return Double.PositiveInfinity
                If InitialResidual <= 0 Then Return 1
                Return InitialResidual / FinalResidual
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim where_ As String = If(ExecutedOnGpu, "GPU/f32", "CPU/f64")

            Return $"{Equation}: {Solver.ToString()}/{Preconditioner} [{where_}] {Iterations} it, " &
                   $"residual {InitialResidual.ToString("E3", CultureInfo.InvariantCulture)} -> " &
                   $"{FinalResidual.ToString("E3", CultureInfo.InvariantCulture)} " &
                   $"(drop {ResidualDrop.ToString("E1", CultureInfo.InvariantCulture)}x) " &
                   $"{(If(Converged, "converged", "NOT converged"))}, {ElapsedMs.ToString("F1", CultureInfo.InvariantCulture)} ms"
        End Function
    End Class

    ''' <summary>
    ''' 外部迭代后端契约（用于把内迭代线性求解下放到 GPU float32）。
    ''' </summary>
    Public Interface IEulerianIterativeBackend

        ''' <summary>后端名称（出现在报告里）。</summary>
        ReadOnly Property Name As String

        ''' <summary>
        ''' 尝试用该后端求解；返回 True 表示成功接管（报告已填充），
        ''' 返回 False 表示调用方应回落到 CPU double 路径。
        ''' </summary>
        ''' <param name="sweeps">&gt; 0 时只做该次数的平滑扫描（后端可用 Jacobi 实现）</param>
        Function TrySolve(m As FvMatrix, ByRef x As Double(), kind As EulerianLinearSolverKind,
                          tolerance As Double, relTol As Double, maxIterations As Integer,
                          sweeps As Integer,
                          ByRef report As EulerianSolverReport) As Boolean
    End Interface

    ''' <summary>线性求解辅助（预条件子 + 各类迭代格式 + 分派入口）。</summary>
    Public Class EulerianLinearSolve

        Private Sub New()
        End Sub

        ''' <summary>可选的 GPU float32 迭代后端（由 EulerianFvmCompute 注册）。</summary>
        Public Shared Property GpuBackend As IEulerianIterativeBackend

#Region "预条件子"

        ''' <summary>Jacobi（对角）预条件子。</summary>
        Public NotInheritable Class DiagonalPreconditioner
            Implements IDisposable

            Private ReadOnly _invDiag As Double()

            Public Sub New(m As FvMatrix)
                Dim n As Integer = m.CellCount
                Dim de As Integer() = m.Mesh.DiagEntry
                Dim v As Double() = m.Values

                _invDiag = New Double(n - 1) {}

                For c As Integer = 0 To n - 1
                    Dim d As Double = v(de(c))

                    If std.Abs(d) < 1.0E-300 OrElse Double.IsNaN(d) Then
                        _invDiag(c) = 1.0
                    Else
                        _invDiag(c) = 1.0 / d
                    End If
                Next
            End Sub

            ''' <summary>z = M⁻¹ r（对角缩放）。</summary>
            Public Sub Apply(r As Double(), z As Double())
                For i As Integer = 0 To _invDiag.Length - 1
                    z(i) = _invDiag(i) * r(i)
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
            End Sub
        End Class

        ''' <summary>
        ''' 零填充不完全 LU（ILU(0)）预条件子。
        ''' 分解在与矩阵完全相同的稀疏结构上进行；出现零主元或非有限值时自动
        ''' 降级为对角预条件并记录原因。
        ''' </summary>
        Public NotInheritable Class Ilu0Preconditioner
            Implements IDisposable

            Private ReadOnly _rowPtr As Integer()
            Private ReadOnly _colIdx As Integer()
            Private ReadOnly _lu As Double()
            Private ReadOnly _diagEntry As Integer()
            Private ReadOnly _fallback As DiagonalPreconditioner

            ''' <summary>是否已降级为对角预条件。</summary>
            Public ReadOnly Property Degraded As Boolean

            ''' <summary>降级原因（未降级时为空）。</summary>
            Public ReadOnly Property DegradeReason As String = ""

            Public Sub New(m As FvMatrix)
                Dim n As Integer = m.CellCount

                _rowPtr = m.Mesh.RowPtr
                _colIdx = m.Mesh.ColIdx
                _diagEntry = m.Mesh.DiagEntry
                _lu = DirectCast(m.Values.Clone(), Double())

                Dim reason As String = Nothing

                If Not Factorise(n, _lu, _rowPtr, _colIdx, _diagEntry, reason) Then
                    Degraded = True
                    DegradeReason = reason
                    _fallback = New DiagonalPreconditioner(m)
                End If
            End Sub

            Public Sub Apply(r As Double(), z As Double())
                If Degraded Then
                    _fallback.Apply(r, z)
                    Return
                End If

                ' 前代（L y = r）
                For i As Integer = 0 To r.Length - 1
                    Dim s As Double = r(i)

                    For e As Integer = _rowPtr(i) To _diagEntry(i) - 1
                        s -= _lu(e) * z(_colIdx(e))
                    Next

                    z(i) = s
                Next

                ' 回代（U x = y）
                For i As Integer = r.Length - 1 To 0 Step -1
                    Dim s As Double = z(i)
                    Dim e1 As Integer = _rowPtr(i + 1) - 1

                    For e As Integer = _diagEntry(i) + 1 To e1
                        s -= _lu(e) * z(_colIdx(e))
                    Next

                    Dim d As Double = _lu(_diagEntry(i))

                    z(i) = If(std.Abs(d) > 1.0E-300, s / d, s)
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                If _fallback IsNot Nothing Then
                    _fallback.Dispose()
                End If
            End Sub

            ''' <summary>就地 ILU(0) 分解（行主序，零填充）。</summary>
            Private Shared Function Factorise(n As Integer, lu As Double(), rowPtr As Integer(),
                                              colIdx As Integer(), diagEntry As Integer(),
                                              ByRef reason As String) As Boolean

                For i As Integer = 0 To n - 1
                    Dim d As Integer = diagEntry(i)

                    For e As Integer = rowPtr(i) To d - 1
                        Dim k As Integer = colIdx(e)
                        Dim dk As Integer = diagEntry(k)
                        Dim akk As Double = lu(dk)

                        If std.Abs(akk) < 1.0E-300 Then
                            reason = $"zero pivot at row {k}"
                            Return False
                        End If

                        lu(e) = lu(e) / akk

                        If Not IsFinite(lu(e)) Then
                            reason = $"non-finite multiplier at row {i}"
                            Return False
                        End If

                        Dim factor As Double = lu(e)

                        For f As Integer = dk + 1 To rowPtr(k + 1) - 1
                            Dim j As Integer = colIdx(f)

                            ' 在行 i 中查找列 j（行很短，线性查找足够快）
                            For g As Integer = e + 1 To rowPtr(i + 1) - 1
                                If colIdx(g) = j Then
                                    lu(g) -= factor * lu(f)
                                    Exit For
                                End If

                                If colIdx(g) > j Then
                                    Exit For
                                End If
                            Next
                        Next
                    Next

                    ' FVM 矩阵的对角元恒为正；ILU0 若得到非正主元说明分解已被破坏
                    ' （舍入/非对称结构），此时必须降级为对角预条件，否则 PCG 会发散。
                    If std.Abs(lu(d)) < 1.0E-300 OrElse Not IsFinite(lu(d)) OrElse lu(d) <= 0 Then
                        reason = $"non-positive pivot at row {i} (value={lu(d).ToString("E3", CultureInfo.InvariantCulture)})"
                        Return False
                    End If
                Next

                Return True
            End Function

            Private Shared Function IsFinite(v As Double) As Boolean
                Return Not Double.IsNaN(v) AndAlso Not Double.IsInfinity(v)
            End Function
        End Class

#End Region

#Region "分派入口"

        ''' <summary>
        ''' 求解 A·x = b（就地更新 <paramref name="x"/>），返回完整报告。
        ''' </summary>
        ''' <param name="m">方程（CSR + 源项）</param>
        ''' <param name="x">解向量（长度 = CellCount；作为初始猜测，求解后就地更新）</param>
        ''' <param name="kind">求解器种类</param>
        ''' <param name="tolerance">归一化残差绝对容差</param>
        ''' <param name="relTol">相对容差：相对起始残差允许的最大比例（≤0 表示不使用）</param>
        ''' <param name="maxIterations">最大迭代数（0 表示按键值自动取值）</param>
        ''' <param name="sweeps">若 &gt; 0，则只做该次数的平滑扫描（用于动量预测），不做收敛判定</param>
        ''' <param name="useGpu">是否允许下放到 GPU float32 后端</param>
        ''' <param name="useIlu">是否使用 ILU0 预条件（默认 True；奇异时自动降级）</param>
        Public Shared Function Solve(m As FvMatrix, ByRef x As Double(),
                                    Optional kind As EulerianLinearSolverKind = EulerianLinearSolverKind.Auto,
                                    Optional tolerance As Double = 1.0E-9,
                                    Optional relTol As Double = 0.01,
                                    Optional maxIterations As Integer = 0,
                                    Optional sweeps As Integer = 0,
                                    Optional useGpu As Boolean = True,
                                    Optional useIlu As Boolean = True) As EulerianSolverReport

            If m Is Nothing Then Throw New ArgumentNullException(NameOf(m))

            Dim n As Integer = m.CellCount

            If x Is Nothing Then
                x = New Double(n - 1) {}
            ElseIf x.Length <> n Then
                Throw New ArgumentException($"解向量长度必须为 {n}（收到 {x.Length}）", NameOf(x))
            End If

            Dim sw As New Stopwatch
            sw.Start()

            Dim normalisedFixed As Integer = m.Normalise()

            Dim report As New EulerianSolverReport With {
                .Equation = m.Name,
                .Solver = kind,
                .Notes = If(normalisedFixed > 0, $"{normalisedFixed} cells normalised (zero/NaN diagonal)", "")}

            ' ---- GPU float32 路径（若已注册且允许） ----
            Dim backend As IEulerianIterativeBackend = GpuBackend

            If useGpu AndAlso backend IsNot Nothing Then
                Dim gpuReport As EulerianSolverReport = Nothing

                Try
                    If backend.TrySolve(m, x, kind, tolerance, relTol, maxIterations, sweeps, gpuReport) AndAlso gpuReport IsNot Nothing Then
                        gpuReport.Equation = If(String.IsNullOrEmpty(gpuReport.Equation), m.Name, gpuReport.Equation)
                        gpuReport.Notes = Combine(report.Notes, gpuReport.Notes)
                        gpuReport.ElapsedMs = sw.Elapsed.TotalMilliseconds
                        Return gpuReport
                    End If
                Catch ex As Exception
                    ' GPU 失败不得影响求解：记录后回落 CPU double
                    report.Notes = Combine(report.Notes, "GPU backend failed: " & ex.Message)
                End Try
            End If

            ' ---- 平滑扫描模式（动量预测等只需几次扫描） ----
            If sweeps > 0 Then
                Dim sm As EulerianLinearSolverKind = If(kind = EulerianLinearSolverKind.Auto, EulerianLinearSolverKind.GaussSeidel, kind)

                report.InitialResidual = m.NormalisedResidual(x)

                Call SmoothSweeps(m, x, sm, sweeps)

                report.Solver = sm
                report.Iterations = sweeps
                report.FinalResidual = m.NormalisedResidual(x)
                report.Converged = report.FinalResidual <= report.InitialResidual
                report.Preconditioner = "none"
                report.ElapsedMs = sw.Elapsed.TotalMilliseconds
                Return report
            End If

            ' ---- CPU double 路径 ----
            Dim resolved As EulerianLinearSolverKind = kind

            If resolved = EulerianLinearSolverKind.Auto Then
                resolved = If(m.Symmetric, EulerianLinearSolverKind.Pcg, EulerianLinearSolverKind.BiCGStab)
            End If

            Dim iterations As Integer = If(maxIterations > 0, maxIterations, DefaultIterations(resolved))
            Dim precond As IDisposable = Nothing

            Try
                Dim applyPrecond As Action(Of Double(), Double())

                ' ★ 对称矩阵（压力 Poisson / PCG）必须用**对称**预条件子。
                '   ILU0 的 L·U 不对称，套在 PCG 上会破坏 Krylov 子空间的正交性，
                '   表现为"残差停滞不降"（而非发散），极难定位。
                '   对称情形改用对角预条件（DIC 的零填充退化解）；非对称的动量/输运
                '   方程走 ILU0 + BiCGStab，那里非对称预条件是合法的。
                If useIlu AndAlso Not m.Symmetric Then
                    Dim ilu As New Ilu0Preconditioner(m)

                    precond = ilu
                    report.Preconditioner = If(ilu.Degraded, "ilu0->diagonal", "ilu0")

                    If ilu.Degraded Then
                        report.Notes = Combine(report.Notes, "ILU0 degraded: " & ilu.DegradeReason)
                    End If

                    applyPrecond = AddressOf ilu.Apply
                Else
                    Dim diag As New DiagonalPreconditioner(m)

                    precond = diag
                    report.Preconditioner = "diagonal"
                    applyPrecond = AddressOf diag.Apply
                End If

                Dim result As (iterations As Integer, initial As Double, finalResidual As Double, converged As Boolean)

                Select Case resolved
                    Case EulerianLinearSolverKind.Pcg
                        result = KrylovDriver(m, x, tolerance, relTol, iterations, applyPrecond, usePcg:=True)
                    Case EulerianLinearSolverKind.BiCGStab
                        result = KrylovDriver(m, x, tolerance, relTol, iterations, applyPrecond, usePcg:=False)
                    Case Else
                        ' 纯平滑器（GS / SGS / Jacobi）：固定迭代次数
                        Call SmoothSweeps(m, x, resolved, iterations)
                        Dim ir As Double = m.NormalisedResidual(x)
                        result = (iterations, ir, ir, False)
                        report.Preconditioner = "none"
                End Select

                report.Solver = resolved
                report.Iterations = result.iterations
                report.InitialResidual = result.initial
                report.FinalResidual = result.finalResidual
                report.Converged = result.converged
            Finally
                If precond IsNot Nothing Then
                    precond.Dispose()
                End If
            End Try

            report.ElapsedMs = sw.Elapsed.TotalMilliseconds
            Return report
        End Function

        Private Shared Function DefaultIterations(kind As EulerianLinearSolverKind) As Integer
            Select Case kind
                Case EulerianLinearSolverKind.GaussSeidel, EulerianLinearSolverKind.SymmetricGaussSeidel, EulerianLinearSolverKind.Jacobi
                    Return 50
                Case Else
                    Return 1000
            End Select
        End Function

        Private Shared Function Combine(a As String, b As String) As String
            If String.IsNullOrEmpty(a) Then Return If(b, "")
            If String.IsNullOrEmpty(b) Then Return a

            Return a & "; " & b
        End Function

#End Region

#Region "Krylov 驱动（PCG / BiCGStab）"

        ''' <summary>
        ''' 二维点积（标量循环）。
        ''' </summary>
        ''' <remarks>
        ''' 刻意不使用 SIMDIntrinsics.DotFma / AxpyInPlace：它们在大数组上走 AVX 分支，
        ''' 而 Krylov 迭代对"归约结果的一处漏项"极度敏感（会让 alpha/beta 错位，
        ''' 表现为残差持续增长而非发散到 NaN，极难定位）。这里用可逐位审计的标量循环
        ''' 保证正确性；真正的性能热点已经由 GPU float32 后端承担。
        ''' 统计量（范数/最值）仍走 SimdReduce（其尾块处理已在本工程实测验证）。
        ''' </remarks>
        Private Shared Function PlainDot(a As Double(), b As Double()) As Double
            Dim s As Double = 0
            Dim n As Integer = std.Min(a.Length, b.Length)

            For i As Integer = 0 To n - 1
                s += a(i) * b(i)
            Next

            Return s
        End Function

        ''' <summary>y += alpha·x（标量循环）。</summary>
        Private Shared Sub PlainAxpy(alpha As Double, x As Double(), y As Double())
            Dim n As Integer = std.Min(x.Length, y.Length)

            For i As Integer = 0 To n - 1
                y(i) += alpha * x(i)
            Next
        End Sub

        Private Shared Function KrylovDriver(m As FvMatrix, x As Double(),
                                            tolerance As Double, relTol As Double, maxIterations As Integer,
                                            precond As Action(Of Double(), Double()),
                                            usePcg As Boolean) As (iterations As Integer, initial As Double, finalResidual As Double, converged As Boolean)

            Dim n As Integer = m.CellCount
            Dim r(n - 1) As Double
            Dim z(n - 1) As Double
            Dim p(n - 1) As Double
            Dim ap(n - 1) As Double

            Dim den As Double = 1.0E-30 + m.SourceNorm()

            ' r = b − A·x
            Call m.ResidualVector(x, r)

            Dim initial As Double = SimdReduce.L2Norm(r) / den
            Dim normR As Double = initial
            Dim converged As Boolean = normR <= tolerance

            If converged OrElse n = 0 Then
                Return (0, initial, normR, converged)
            End If

            If usePcg Then
                Call precond(r, z)

                Array.Copy(z, p, n)

                Dim rho As Double = PlainDot(r, z)
                Dim iter As Integer = 0

                While iter < maxIterations
                    iter += 1

                    ' Ap = A·p
                    Call m.Multiply(p, ap)

                    Dim pAp As Double = PlainDot(p, ap)

                    If std.Abs(pAp) < 1.0E-300 Then
                        Exit While
                    End If

                    Dim alpha As Double = rho / pAp

                    ' x += alpha·p ; r -= alpha·Ap
                    Call PlainAxpy(alpha, p, x)
                    Call PlainAxpy(-alpha, ap, r)

                    normR = SimdReduce.L2Norm(r) / den

                    If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                        converged = True
                        Exit While
                    End If

                    Call precond(r, z)

                    Dim rhoNew As Double = PlainDot(r, z)
                    Dim beta As Double = rhoNew / rho

                    rho = rhoNew

                    ' p = z + beta·p
                    For i As Integer = 0 To n - 1
                        p(i) = z(i) + beta * p(i)
                    Next
                End While

                Return (iter, initial, normR, converged)
            Else
                ' ---- BiCGStab ----
                Dim rHat(n - 1) As Double
                Dim v(n - 1) As Double
                Dim s(n - 1) As Double
                Dim t(n - 1) As Double

                Array.Copy(r, rHat, n)

                Dim rho As Double = 1.0
                Dim alpha As Double = 1.0
                Dim omega As Double = 1.0
                Dim iter As Integer = 0

                For i As Integer = 0 To n - 1
                    p(i) = 0
                    v(i) = 0
                Next

                While iter < maxIterations
                    iter += 1

                    Dim rhoNew As Double = PlainDot(rHat, r)

                    If std.Abs(rhoNew) < 1.0E-300 Then
                        Exit While
                    End If

                    Dim beta As Double = (rhoNew / rho) * (alpha / omega)

                    rho = rhoNew

                    ' p = r + beta*(p − omega*v)
                    For i As Integer = 0 To n - 1
                        p(i) = r(i) + beta * (p(i) - omega * v(i))
                    Next

                    ' 预条件 p → phat（复用 z 缓冲）
                    Call precond(p, z)
                    Call m.Multiply(z, v)

                    Dim rHatV As Double = PlainDot(rHat, v)

                    If std.Abs(rHatV) < 1.0E-300 Then
                        Exit While
                    End If

                    alpha = rho / rHatV

                    ' s = r − alpha*v
                    For i As Integer = 0 To n - 1
                        s(i) = r(i) - alpha * v(i)
                    Next

                    normR = SimdReduce.L2Norm(s) / den

                    If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                        Call PlainAxpy(alpha, z, x)
                        converged = True
                        Exit While
                    End If

                    Call precond(s, t)
                    Call m.Multiply(t, ap)

                    Dim tT As Double = PlainDot(t, t)
                    Dim ttAp As Double = PlainDot(t, ap)

                    If std.Abs(tT) < 1.0E-300 Then
                        Exit While
                    End If

                    omega = ttAp / tT

                    ' x += alpha*z + omega*t ; r = s − omega*ap
                    For i As Integer = 0 To n - 1
                        x(i) += alpha * z(i) + omega * t(i)
                        r(i) = s(i) - omega * ap(i)
                    Next

                    normR = SimdReduce.L2Norm(r) / den

                    If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                        converged = True
                        Exit While
                    End If

                    If std.Abs(omega) < 1.0E-300 Then
                        Exit While
                    End If
                End While

                Return (iter, initial, normR, converged)
            End If
        End Function

#End Region

#Region "平滑器"

        ''' <summary>按种类执行若干次平滑扫描。</summary>
        Public Shared Sub SmoothSweeps(m As FvMatrix, x As Double(), kind As EulerianLinearSolverKind, sweeps As Integer)
            Select Case kind
                Case EulerianLinearSolverKind.Jacobi
                    Call JacobiSweeps(m, x, sweeps)
                Case EulerianLinearSolverKind.SymmetricGaussSeidel
                    Call SymmetricGaussSeidelSweeps(m, x, sweeps)
                Case Else
                    Call GaussSeidelSweeps(m, x, sweeps)
            End Select
        End Sub

        ''' <summary>前向 Gauss-Seidel 扫描。</summary>
        Public Shared Sub GaussSeidelSweeps(m As FvMatrix, x As Double(), sweeps As Integer)
            Dim rp As Integer() = m.Mesh.RowPtr
            Dim ci As Integer() = m.Mesh.ColIdx
            Dim de As Integer() = m.Mesh.DiagEntry
            Dim a As Double() = m.Values
            Dim b As Double() = m.Source
            Dim n As Integer = m.CellCount

            For s As Integer = 1 To sweeps
                For c As Integer = 0 To n - 1
                    Dim sum As Double = b(c)
                    Dim d As Integer = de(c)

                    ' 对角元固定在行首
                    For e As Integer = rp(c) To d - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    For e As Integer = d + 1 To rp(c + 1) - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    Dim ad As Double = a(d)

                    If std.Abs(ad) > 1.0E-300 Then
                        x(c) = sum / ad
                    End If
                Next
            Next
        End Sub

        ''' <summary>
        ''' 对称 Gauss-Seidel 扫描（正向后反向）。
        ''' 仅当矩阵对称时反向扫描才等价于 Aᵀ 上的前向扫描。
        ''' </summary>
        Public Shared Sub SymmetricGaussSeidelSweeps(m As FvMatrix, x As Double(), sweeps As Integer)
            Dim rp As Integer() = m.Mesh.RowPtr
            Dim ci As Integer() = m.Mesh.ColIdx
            Dim de As Integer() = m.Mesh.DiagEntry
            Dim a As Double() = m.Values
            Dim b As Double() = m.Source
            Dim n As Integer = m.CellCount

            For s As Integer = 1 To sweeps
                For c As Integer = 0 To n - 1
                    Dim sum As Double = b(c)
                    Dim d As Integer = de(c)

                    For e As Integer = rp(c) To d - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    For e As Integer = d + 1 To rp(c + 1) - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    Dim ad As Double = a(d)

                    If std.Abs(ad) > 1.0E-300 Then
                        x(c) = sum / ad
                    End If
                Next

                For c As Integer = n - 1 To 0 Step -1
                    Dim sum As Double = b(c)
                    Dim d As Integer = de(c)

                    For e As Integer = rp(c) To d - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    For e As Integer = d + 1 To rp(c + 1) - 1
                        sum -= a(e) * x(ci(e))
                    Next

                    Dim ad As Double = a(d)

                    If std.Abs(ad) > 1.0E-300 Then
                        x(c) = sum / ad
                    End If
                Next
            Next
        End Sub

        ''' <summary>阻尼 Jacobi 扫描。</summary>
        Public Shared Sub JacobiSweeps(m As FvMatrix, x As Double(), sweeps As Integer, Optional damping As Double = 0.7)
            Dim rp As Integer() = m.Mesh.RowPtr
            Dim ci As Integer() = m.Mesh.ColIdx
            Dim de As Integer() = m.Mesh.DiagEntry
            Dim a As Double() = m.Values
            Dim b As Double() = m.Source
            Dim n As Integer = m.CellCount
            Dim xOld(n - 1) As Double

            For s As Integer = 1 To sweeps
                Array.Copy(x, xOld, n)

                For c As Integer = 0 To n - 1
                    Dim sum As Double = b(c)
                    Dim d As Integer = de(c)

                    For e As Integer = rp(c) To d - 1
                        sum -= a(e) * xOld(ci(e))
                    Next

                    For e As Integer = d + 1 To rp(c + 1) - 1
                        sum -= a(e) * xOld(ci(e))
                    Next

                    Dim ad As Double = a(d)

                    If std.Abs(ad) > 1.0E-300 Then
                        Dim xNew As Double = sum / ad

                        x(c) = xOld(c) + damping * (xNew - xOld(c))
                    End If
                Next
            Next
        End Sub

#End Region

    End Class

End Namespace
