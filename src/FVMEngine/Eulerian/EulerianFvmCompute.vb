' /********************************************************************************/
'
'   EulerianFvmCompute.vb
'
'   混合精度 CFD 引擎的「float32 GPU 热点」后端（Eulerian 非结构网格版）。
'
'   职责：
'       把 Eulerian FVM 求解器中 GPU 收益最高的部分下放到 float32 + CUDA：
'           · CSR 稀疏矩阵–向量乘（SpMV）—— PCG / BiCGStab 的主要成本
'           · 向量 axpy / xpay / copy
'           · 部分和归约（点积、范数平方）
'           · 带阻尼的 Jacobi 扫描（GPU 侧平滑器 / 快速动量预测）
'       对应 doc\FVM_Eulerian.md §八 的"求解器层"，以及本工程 FVMEngine.vbproj 头部
'       声明的混合精度契约。
'
'   落地模式（与 CFDEngine.FvmCompute / LBMEngine.CudaLbmBackend 完全一致）：
'       1. KernelSources.RegisterSource(<.cu 源码>)   ← 必须在 CudaEngine.TryCreate 之前
'       2. CudaEngine.TryCreate(options)
'       3. DeviceBuffer(Of T) 申请设备缓冲
'       4. kernel.Launch(LaunchPlanner.For1D(n, 256), args...)
'       5. 任何失败 → TrySolve 返回 False → EulerianLinearSolve 自动回落 CPU double
'
'   ★ 精度与正确性边界（关键设计）
'       上传：values / b / x0 由 double 降为 float32（显存与带宽减半）
'       迭代：全部在显存内完成，主机只参与"每次归约的 4096 个部分和"
'       回读：结束后把 x 回读为 double，并在 **double 下重算归一化残差**
'             作为唯一的收敛判定依据 —— 绝不用 float32 残差做收敛判据。
'       因此 GPU 路径只影响内迭代的收敛路径，不改变外层 SIMPLE/PIMPLE 的物理精度。
'
'   ★ 与 CFDEngine.FvmCompute 的区别
'       FvmCompute 是**结构化七点变系数**模板（指数坐标分解）；
'       本类是**非结构 CSR** 模板，面向任意面数多面体 / 四面体 / 六面体 / 棱柱网格。
'
'   内核源码：Kernels\moira_eulerian_fvm_f32.cu（嵌入资源）
'
' /********************************************************************************/

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports ILCudaRuntime = Microsoft.VisualBasic.Computing.ILCuda.Runtime
Imports ILCudaKernels = Microsoft.VisualBasic.Computing.ILCuda.Kernels
Imports std = System.Math

Namespace Eulerian

    ''' <summary>
    ''' Eulerian FVM 的 float32 CUDA 迭代后端：实现 <see cref="IEulerianIterativeBackend"/>。
    ''' </summary>
    Public Class EulerianFvmCompute
        Implements IEulerianIterativeBackend
        Implements IDisposable

#Region "内核名称与源码"

        Private Const KResidualAsSpMv As String = "moira_efvm_spmv_csr"
        Private Const KAxpy As String = "moira_efvm_axpy"
        Private Const KXpay As String = "moira_efvm_xpay"
        Private Const KCopy As String = "moira_efvm_copy"
        Private Const KDot As String = "moira_efvm_dot_partial"
        Private Const KJacobi As String = "moira_efvm_jacobi_csr"
        Private Const KDiagScale As String = "moira_efvm_diag_scale"

        ''' <summary>嵌入资源中的内核源码文件名（按后缀匹配清单资源名）。</summary>
        Private Const KernelResourceName As String = "moira_eulerian_fvm_f32.cu"

        ''' <summary>Jacobi 平滑器的默认阻尼。</summary>
        Private Const JacobiDamping As Double = 0.7

        ''' <summary>
        ''' 低于此单元数时不启用 GPU（显存搬运与归约同步的开销会盖过收益），
        ''' 直接回落 CPU double 路径。
        ''' </summary>
        Public Const MinGpuCells As Integer = 8000

        Private Shared _kernelSource As String

        ''' <summary>内核源码（从本程序集的嵌入资源读取并缓存）。</summary>
        Public Shared ReadOnly Property KernelSource As String
            Get
                If _kernelSource IsNot Nothing Then Return _kernelSource

                Dim asm As Assembly = GetType(EulerianFvmCompute).Assembly
                Dim resourceName As String = Nothing

                For Each name As String In asm.GetManifestResourceNames()
                    If name.EndsWith(KernelResourceName, StringComparison.OrdinalIgnoreCase) Then
                        resourceName = name
                        Exit For
                    End If
                Next

                If resourceName Is Nothing Then
                    Throw New InvalidOperationException(
                        $"程序集 {asm.GetName().Name} 中未找到嵌入的 CUDA 内核资源 '{KernelResourceName}'" &
                        "（请确认 FVMEngine.vbproj 中的 EmbeddedResource 声明）")
                End If

                Using stream = asm.GetManifestResourceStream(resourceName)
                    If stream Is Nothing Then
                        Throw New InvalidOperationException($"嵌入资源 '{resourceName}' 无法读取")
                    End If

                    Using reader As New StreamReader(stream)
                        _kernelSource = reader.ReadToEnd()
                    End Using
                End Using

                Return _kernelSource
            End Get
        End Property

#End Region

#Region "注册与生命周期"

        Private Shared _current As EulerianFvmCompute
        Private Shared _sourceRegistered As Boolean
        Private Shared ReadOnly _regLock As New Object()
        Private Shared _lastError As String

        ''' <summary>最近一次注册失败的原因。</summary>
        Public Shared Property LastError As String
            Get
                Return _lastError
            End Get
            Set(value As String)
                _lastError = value
            End Set
        End Property

        ''' <summary>当前已注册的后端实例（未注册时为 Nothing）。</summary>
        Public Shared ReadOnly Property Current As EulerianFvmCompute
            Get
                Return _current
            End Get
        End Property

        ''' <summary>是否已成功注册并接管线性求解热点。</summary>
        Public Shared ReadOnly Property IsGpuAvailable As Boolean
            Get
                Return _current IsNot Nothing
            End Get
        End Property

        ''' <summary>
        ''' 尝试初始化 CUDA 并把 Eulerian 线性求解热点切换为 GPU float32。
        ''' 设备不可用 / NVRTC 编译失败时返回 False，求解自动走 CPU double。
        ''' 幂等：重复调用直接返回 True。
        ''' </summary>
        Public Shared Function TryRegister(Optional deviceOrdinal As Integer = -1) As Boolean
            If _current IsNot Nothing Then Return True

            SyncLock _regLock
                If _current IsNot Nothing Then Return True

                Try
                    ' 内核源码必须先注册（NVRTC 一次性编译整个编译单元）
                    If Not _sourceRegistered Then
                        Call ILCudaKernels.KernelSources.RegisterSource(KernelResourceName, KernelSource)
                        _sourceRegistered = True
                    End If

                    Dim options As New ILCudaRuntime.EngineOptions()

                    If deviceOrdinal >= 0 Then
                        options.DeviceOrdinal = deviceOrdinal
                    End If

                    Dim engine As ILCudaRuntime.CudaEngine = ILCudaRuntime.CudaEngine.TryCreate(options)

                    If engine Is Nothing Then
                        _lastError = options.ErrorMessage
                        Return False
                    End If

                    Dim backend As New EulerianFvmCompute(engine)

                    If Not backend.HasRequiredKernels() Then
                        _lastError = "one or more required kernels are missing: " & backend.MissingKernels()
                        Call backend.Dispose()
                        Return False
                    End If

                    _current = backend
                    _lastError = Nothing
                    EulerianLinearSolve.GpuBackend = backend

                    Return True
                Catch ex As Exception
                    _lastError = ex.Message
                    Return False
                End Try
            End SyncLock
        End Function

        ''' <summary>释放 GPU 后端，后续求解自动回落 CPU double。</summary>
        Public Shared Sub Unregister()
            SyncLock _regLock
                If _current IsNot Nothing Then
                    If ReferenceEquals(EulerianLinearSolve.GpuBackend, _current) Then
                        EulerianLinearSolve.GpuBackend = Nothing
                    End If

                    Call _current.Dispose()
                    _current = Nothing
                End If
            End SyncLock
        End Sub

        ''' <summary>后端名称（出现在求解报告里）。</summary>
        Public ReadOnly Property Name As String Implements IEulerianIterativeBackend.Name
            Get
                Return "cuda/f32-csr"
            End Get
        End Property

        ''' <summary>底层 CUDA 引擎。</summary>
        Public ReadOnly Property Engine As ILCudaRuntime.CudaEngine
            Get
                Return _engine
            End Get
        End Property

        Private ReadOnly _engine As ILCudaRuntime.CudaEngine

        Private ReadOnly _kSpMv As ILCudaRuntime.CudaKernel
        Private ReadOnly _kAxpy As ILCudaRuntime.CudaKernel
        Private ReadOnly _kXpay As ILCudaRuntime.CudaKernel
        Private ReadOnly _kCopy As ILCudaRuntime.CudaKernel
        Private ReadOnly _kDot As ILCudaRuntime.CudaKernel
        Private ReadOnly _kJacobi As ILCudaRuntime.CudaKernel
        Private ReadOnly _kDiagScale As ILCudaRuntime.CudaKernel

        Private Sub New(engine As ILCudaRuntime.CudaEngine)
            _engine = engine
            _kSpMv = engine.GetKernel(KResidualAsSpMv)
            _kAxpy = engine.GetKernel(KAxpy)
            _kXpay = engine.GetKernel(KXpay)
            _kCopy = engine.GetKernel(KCopy)
            _kDot = engine.GetKernel(KDot)
            _kJacobi = engine.GetKernel(KJacobi)
            _kDiagScale = engine.GetKernel(KDiagScale)
        End Sub

        Private Function HasRequiredKernels() As Boolean
            Return _kSpMv IsNot Nothing AndAlso _kAxpy IsNot Nothing AndAlso _kXpay IsNot Nothing AndAlso
                   _kCopy IsNot Nothing AndAlso _kDot IsNot Nothing AndAlso _kJacobi IsNot Nothing AndAlso
                   _kDiagScale IsNot Nothing
        End Function

        Private Function MissingKernels() As String
            Dim miss As New List(Of String)

            If _kSpMv Is Nothing Then miss.Add(KResidualAsSpMv)
            If _kAxpy Is Nothing Then miss.Add(KAxpy)
            If _kXpay Is Nothing Then miss.Add(KXpay)
            If _kCopy Is Nothing Then miss.Add(KCopy)
            If _kDot Is Nothing Then miss.Add(KDot)
            If _kJacobi Is Nothing Then miss.Add(KJacobi)
            If _kDiagScale Is Nothing Then miss.Add(KDiagScale)

            Return String.Join(", ", miss)
        End Function

        ''' <summary>设备描述（日志用）。</summary>
        Public Function DescribeDevice() As String
            If _engine Is Nothing Then Return "no CUDA engine"

            Try
                Return $"{Name}: {_engine.Image}"
            Catch ex As Exception
                Return $"{Name}: {ex.Message}"
            End Try
        End Function

#End Region

#Region "设备状态"

        ''' <summary>
        ''' 单个网格对应的常驻设备状态：
        ''' CSR 结构（一次上传、多次复用）+ 每次求解都要重填的数值缓冲。
        ''' 向量池：0=x, 1=r, 2=z, 3=p, 4=ap, 5=rhat, 6=v, 7=s, 8=t, 9=tmp
        ''' </summary>
        Private NotInheritable Class DeviceState
            Implements IDisposable

            Public Const VectorCount As Integer = 10
            Public Const VecX As Integer = 0
            Public Const VecR As Integer = 1
            Public Const VecZ As Integer = 2
            Public Const VecP As Integer = 3
            Public Const VecAp As Integer = 4
            Public Const VecRHat As Integer = 5
            Public Const VecV As Integer = 6
            Public Const VecS As Integer = 7
            Public Const VecT As Integer = 8
            Public Const VecTmp As Integer = 9

            Public ReadOnly N As Integer
            Public ReadOnly Nnz As Integer
            Public ReadOnly Config As ILCudaRuntime.LaunchConfig
            Public ReadOnly RowPtr As ILCudaRuntime.DeviceBuffer(Of Integer)
            Public ReadOnly Columns As ILCudaRuntime.DeviceBuffer(Of Integer)
            Public ReadOnly Values As ILCudaRuntime.DeviceBuffer(Of Single)
            Public ReadOnly DiagInv As ILCudaRuntime.DeviceBuffer(Of Single)
            Public ReadOnly Rhs As ILCudaRuntime.DeviceBuffer(Of Single)
            Public ReadOnly PartialSums As ILCudaRuntime.DeviceBuffer(Of Single)
            Public ReadOnly Vectors As ILCudaRuntime.DeviceBuffer(Of Single)()

            Public Sub New(n As Integer, nnz As Integer, config As ILCudaRuntime.LaunchConfig)
                Me.N = n
                Me.Nnz = nnz
                Me.Config = config

                RowPtr = New ILCudaRuntime.DeviceBuffer(Of Integer)(n + 1)
                Columns = New ILCudaRuntime.DeviceBuffer(Of Integer)(nnz)
                Values = New ILCudaRuntime.DeviceBuffer(Of Single)(nnz)
                DiagInv = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                Rhs = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                PartialSums = New ILCudaRuntime.DeviceBuffer(Of Single)(config.GridX)
                Vectors = New ILCudaRuntime.DeviceBuffer(Of Single)(VectorCount - 1) {}

                For i As Integer = 0 To VectorCount - 1
                    Vectors(i) = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                Next
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                Call SafeDispose(RowPtr)
                Call SafeDispose(Columns)
                Call SafeDispose(Values)
                Call SafeDispose(DiagInv)
                Call SafeDispose(Rhs)
                Call SafeDispose(PartialSums)

                If Vectors IsNot Nothing Then
                    For Each v In Vectors
                        Call SafeDispose(v)
                    Next
                End If
            End Sub

            Private Shared Sub SafeDispose(Of T As Structure)(b As ILCudaRuntime.DeviceBuffer(Of T))
                If b Is Nothing Then Return

                Try
                    b.Dispose()
                Catch
                    ' 释放失败不应影响上层（进程退出时驱动会回收）
                End Try
            End Sub
        End Class

        Private _state As DeviceState
        Private _stateRowPtrKey As Integer()
        Private _stateColIdxKey As Integer()

        ''' <summary>按 CSR 结构身份缓存设备状态（结构在求解期间不变，只重填数值）。</summary>
        Private Function EnsureState(m As FvMatrix) As DeviceState
            Dim mesh As EulerianFvMesh = m.Mesh
            Dim n As Integer = mesh.CellCount
            Dim nnz As Integer = mesh.NonZeroCount

            If _state IsNot Nothing AndAlso _state.N = n AndAlso _state.Nnz = nnz AndAlso
               ReferenceEquals(_stateRowPtrKey, mesh.RowPtr) AndAlso ReferenceEquals(_stateColIdxKey, mesh.ColIdx) Then

                Return _state
            End If

            If _state IsNot Nothing Then
                Call _state.Dispose()
                _state = Nothing
            End If

            Dim config As ILCudaRuntime.LaunchConfig = ILCudaRuntime.LaunchPlanner.For1D(n, 256)
            Dim state As New DeviceState(n, nnz, config)

            Call state.RowPtr.Write(mesh.RowPtr)
            Call state.Columns.Write(mesh.ColIdx)

            _state = state
            _stateRowPtrKey = mesh.RowPtr
            _stateColIdxKey = mesh.ColIdx

            Return state
        End Function

        ''' <summary>把 double 系数 / 右端项降为 float32 上传（每次外迭代调用一次）。</summary>
        Private Sub UploadCoefficients(m As FvMatrix, st As DeviceState)
            Dim src As Double() = m.Values
            Dim nnz As Integer = st.Nnz
            Dim vals(nnz - 1) As Single

            For i As Integer = 0 To nnz - 1
                vals(i) = CSng(src(i))
            Next

            Call st.Values.Write(vals)

            Dim de As Integer() = m.Mesh.DiagEntry
            Dim dinv(st.N - 1) As Single

            For c As Integer = 0 To st.N - 1
                Dim d As Double = src(de(c))

                ' 1/|d| 超过 float32 动态范围时置 0（该单元已被 Normalise 修正为硬约束）
                dinv(c) = If(std.Abs(d) > 1.0E-20, CSng(1.0 / d), 0.0F)
            Next

            Call st.DiagInv.Write(dinv)

            Dim b As Double() = m.Source
            Dim rhs(st.N - 1) As Single

            For c As Integer = 0 To st.N - 1
                rhs(c) = CSng(b(c))
            Next

            Call st.Rhs.Write(rhs)
        End Sub

        Private Shared Function ToSingleArray(data As Double()) As Single()
            Dim a(data.Length - 1) As Single

            For i As Integer = 0 To data.Length - 1
                a(i) = CSng(data(i))
            Next

            Return a
        End Function

#End Region

#Region "设备侧算子封装"

        Private Sub SpMv(st As DeviceState, xv As ILCudaRuntime.DeviceBuffer(Of Single),
                         yv As ILCudaRuntime.DeviceBuffer(Of Single))

            Call _kSpMv.Launch(st.Config, st.RowPtr, st.Columns, st.Values, xv, yv, st.N)
        End Sub

        Private Sub Axpy(st As DeviceState, alpha As Double,
                         xv As ILCudaRuntime.DeviceBuffer(Of Single),
                         yv As ILCudaRuntime.DeviceBuffer(Of Single))

            Call _kAxpy.Launch(st.Config, CSng(alpha), xv, yv, st.N)
        End Sub

        Private Sub Xpay(st As DeviceState, alpha As Double,
                         xv As ILCudaRuntime.DeviceBuffer(Of Single),
                         yv As ILCudaRuntime.DeviceBuffer(Of Single))

            Call _kXpay.Launch(st.Config, CSng(alpha), xv, yv, st.N)
        End Sub

        Private Sub Copy(st As DeviceState,
                         xv As ILCudaRuntime.DeviceBuffer(Of Single),
                         yv As ILCudaRuntime.DeviceBuffer(Of Single))

            Call _kCopy.Launch(st.Config, xv, yv, st.N)
        End Sub

        ''' <summary>
        ''' 部分和点积：返回 Σ x·y。
        ''' 内核输出 gridDim.x 个部分和；主机以 double 累加（避免 float32 长链求和误差）。
        ''' </summary>
        Private Function Dot(st As DeviceState,
                             xv As ILCudaRuntime.DeviceBuffer(Of Single),
                             yv As ILCudaRuntime.DeviceBuffer(Of Single)) As Double

            Call _kDot.Launch(st.Config, xv, yv, st.PartialSums, st.N)

            Dim parts As Single() = st.PartialSums.Read()
            Dim s As Double = 0

            For i As Integer = 0 To st.Config.GridX - 1
                s += parts(i)
            Next

            Return s
        End Function

#End Region

#Region "求解入口"

        ''' <summary>
        ''' 尝试用 GPU float32 完成本次线性求解。
        ''' 返回 False 表示调用方应回落到 CPU double（规模过小 / 数值异常 / 无内核）。
        ''' </summary>
        Public Function TrySolve(m As FvMatrix, ByRef x As Double(), kind As EulerianLinearSolverKind,
                                 tolerance As Double, relTol As Double, maxIterations As Integer,
                                 sweeps As Integer,
                                 ByRef report As EulerianSolverReport) As Boolean Implements IEulerianIterativeBackend.TrySolve

            If _engine Is Nothing Then Return False
            If m Is Nothing OrElse x Is Nothing Then Return False

            Dim n As Integer = m.CellCount

            If n <= 0 OrElse x.Length <> n Then Return False
            If n < MinGpuCells Then Return False

            Dim st As DeviceState = EnsureState(m)
            If st Is Nothing Then Return False

            Call UploadCoefficients(m, st)

            Dim rpt As New EulerianSolverReport With {
                .Equation = m.Name,
                .ExecutedOnGpu = True,
                .Preconditioner = "jacobi(diag)",
                .Solver = kind}

            ' 收敛判据一律在 double 下计算（权威值）
            Dim initial As Double = m.NormalisedResidual(x)
            rpt.InitialResidual = initial

            Dim xv = st.Vectors(DeviceState.VecX)
            Call xv.Write(ToSingleArray(x))

            ' ---- 平滑扫描模式：GPU 侧阻尼 Jacobi ----
            If sweeps > 0 Then
                Dim cur As Integer = DeviceState.VecX
                Dim nxt As Integer = DeviceState.VecTmp

                For s As Integer = 1 To sweeps
                    Call _kJacobi.Launch(st.Config, st.RowPtr, st.Columns, st.Values, st.DiagInv,
                                         st.Rhs, st.Vectors(cur), st.Vectors(nxt), CSng(JacobiDamping), st.N)

                    Dim swap As Integer = cur
                    cur = nxt
                    nxt = swap
                Next

                Call ReadBack(st.Vectors(cur), x)

                rpt.Solver = EulerianLinearSolverKind.Jacobi
                rpt.Iterations = sweeps
                rpt.FinalResidual = m.NormalisedResidual(x)
                rpt.Converged = rpt.FinalResidual <= rpt.InitialResidual
                rpt.Notes = $"smoother mapped to damped Jacobi (damping={JacobiDamping})"
                report = rpt

                Return True
            End If

            ' ---- Krylov 求解 ----
            Dim resolved As EulerianLinearSolverKind = kind

            If resolved = EulerianLinearSolverKind.Auto Then
                resolved = If(m.Symmetric, EulerianLinearSolverKind.Pcg, EulerianLinearSolverKind.BiCGStab)
            End If

            If resolved = EulerianLinearSolverKind.Pcg AndAlso Not m.Symmetric Then
                rpt.Notes = "PCG requested on non-symmetric matrix; switched to BiCGStab"
                resolved = EulerianLinearSolverKind.BiCGStab
            End If

            If resolved <> EulerianLinearSolverKind.Pcg AndAlso resolved <> EulerianLinearSolverKind.BiCGStab Then
                ' GS / SGS / Jacobi 的非扫描调用：退化为 Jacobi 迭代（残差仍在 double 下判定）
                resolved = EulerianLinearSolverKind.BiCGStab
                rpt.Notes = "smoother kind upgraded to BiCGStab on GPU"
            End If

            Dim iterations As Integer = If(maxIterations > 0, maxIterations, 1000)
            Dim den As Double = 1.0E-30 + m.SourceNorm()

            ' r = b − A·x
            Call SpMv(st, xv, st.Vectors(DeviceState.VecAp))
            Call Copy(st, st.Rhs, st.Vectors(DeviceState.VecR))
            Call Axpy(st, -1.0, st.Vectors(DeviceState.VecAp), st.Vectors(DeviceState.VecR))

            Dim usedIter As Integer
            Dim converged As Boolean

            If resolved = EulerianLinearSolverKind.Pcg Then
                usedIter = GpuPcg(st, iterations, tolerance * 0.5, relTol, initial, den, converged)
            Else
                usedIter = GpuBiCGStab(st, iterations, tolerance * 0.5, relTol, initial, den, converged)
            End If

            Call ReadBack(xv, x)

            rpt.Solver = resolved
            rpt.Iterations = usedIter

            ' ★ 权威残差：回读后在 double 下重算
            rpt.FinalResidual = m.NormalisedResidual(x)
            rpt.Converged = rpt.FinalResidual <= tolerance OrElse
                            (relTol > 0 AndAlso rpt.FinalResidual <= relTol * initial)

            ' ★★ float32 假收敛防护（关键正确性校验）
            '   病态系数矩阵（例如压力方程在 sealed/退化单元上 rAU 可达 1e-11，
            '   对角元与右端项相差 20+ 个量级）会让 GPU 侧 float32 的 pAp / rho
            '   因灾难性相消而失真，PCG 提前退出并**谎报收敛**——
            '   实测 city_seed42 压力方程：GPU 报 "1 it, residual 1.000 -> 1.000"
            '   （解完全没变），而同一矩阵 CPU double 需要 285+ 次迭代。
            '   若把这种"零进展"结果当作已求解交给外层 SIMPLE，
            '   压力修正量恒为 0，速度场被未修正的 Rhie-Chow 通量反复放大 → 发散。
            '   因此这里做**实质下降校验**：double 权威残差没有明显下降时，
            '   丢弃 GPU 结果并返回 False，由 EulerianLinearSolve 回落 CPU double。
            If Not rpt.Converged Then
                Dim madeProgress As Boolean =
                    rpt.FinalResidual <= 0.5 * initial OrElse
                    (initial <= tolerance AndAlso rpt.FinalResidual <= tolerance)

                If Not madeProgress Then
                    rpt.Notes = Combine2(rpt.Notes,
                        $"GPU rejected: no real residual progress in {usedIter} it " &
                        $"({initial:E3} -> {rpt.FinalResidual:E3}); falling back to CPU double")
                    rpt.ExecutedOnGpu = False
                    rpt.Solver = kind
                    report = rpt

                    Return False
                End If
            End If

            report = rpt

            Return True
        End Function

        Private Shared Function Combine2(a As String, b As String) As String
            If String.IsNullOrEmpty(a) Then Return If(b, "")
            If String.IsNullOrEmpty(b) Then Return a

            Return a & "; " & b
        End Function

        ''' <summary>把设备缓冲回写为 double 场数组。</summary>
        Private Shared Sub ReadBack(buf As ILCudaRuntime.DeviceBuffer(Of Single), x As Double())
            Dim data As Single() = buf.Read()

            For i As Integer = 0 To x.Length - 1
                x(i) = data(i)
            Next
        End Sub

        ''' <summary>归一化 float32 残差（仅用于内迭代停止判据，不是收敛判据）。</summary>
        Private Function RNorm(st As DeviceState, den As Double) As Double
            Dim d As Double = Dot(st, st.Vectors(DeviceState.VecR), st.Vectors(DeviceState.VecR))

            If d < 0 OrElse Double.IsNaN(d) Then Return Double.NaN

            Return std.Sqrt(d) / den
        End Function

        ''' <summary>GPU 上的 Jacobi 预条件 PCG。</summary>
        Private Function GpuPcg(st As DeviceState, maxIterations As Integer,
                                tolerance As Double, relTol As Double, initial As Double, den As Double,
                                ByRef converged As Boolean) As Integer

            Dim r = st.Vectors(DeviceState.VecR)
            Dim z = st.Vectors(DeviceState.VecZ)
            Dim p = st.Vectors(DeviceState.VecP)
            Dim ap = st.Vectors(DeviceState.VecAp)
            Dim xv = st.Vectors(DeviceState.VecX)

            ' z = M⁻¹ r（对角缩放）
            Call DiagScale(st, r, z)
            Call Copy(st, z, p)

            Dim rho As Double = Dot(st, r, z)
            Dim iter As Integer = 0
            Dim normR As Double = initial

            While iter < maxIterations
                iter += 1

                Call SpMv(st, p, ap)

                Dim pAp As Double = Dot(st, p, ap)

                If std.Abs(pAp) < 1.0E-30 OrElse Double.IsNaN(pAp) Then
                    Exit While
                End If

                Dim alpha As Double = rho / pAp

                Call Axpy(st, alpha, p, xv)
                Call Axpy(st, -alpha, ap, r)

                normR = RNorm(st, den)

                If Double.IsNaN(normR) Then
                    Exit While
                End If

                If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                    converged = True
                    Exit While
                End If

                Call DiagScale(st, r, z)

                Dim rhoNew As Double = Dot(st, r, z)

                If std.Abs(rho) < 1.0E-300 Then
                    Exit While
                End If

                Dim beta As Double = rhoNew / rho
                rho = rhoNew

                ' p = z + beta·p
                Call Xpay(st, beta, z, p)
            End While

            Return iter
        End Function

        ''' <summary>GPU 上的 Jacobi 预条件 BiCGStab。</summary>
        Private Function GpuBiCGStab(st As DeviceState, maxIterations As Integer,
                                     tolerance As Double, relTol As Double, initial As Double, den As Double,
                                     ByRef converged As Boolean) As Integer

            Dim r = st.Vectors(DeviceState.VecR)
            Dim rhat = st.Vectors(DeviceState.VecRHat)
            Dim p = st.Vectors(DeviceState.VecP)
            Dim v = st.Vectors(DeviceState.VecV)
            Dim s = st.Vectors(DeviceState.VecS)
            Dim t = st.Vectors(DeviceState.VecT)
            Dim z = st.Vectors(DeviceState.VecZ)
            Dim ap = st.Vectors(DeviceState.VecAp)
            Dim xv = st.Vectors(DeviceState.VecX)

            Call Copy(st, r, rhat)

            Dim rho As Double = 1.0
            Dim alpha As Double = 1.0
            Dim omega As Double = 1.0
            Dim iter As Integer = 0
            Dim normR As Double = initial

            Call Zero(st, p)
            Call Zero(st, v)

            While iter < maxIterations
                iter += 1

                Dim rhoNew As Double = Dot(st, rhat, r)

                If std.Abs(rhoNew) < 1.0E-30 OrElse Double.IsNaN(rhoNew) Then
                    Exit While
                End If

                Dim beta As Double = (rhoNew / rho) * (alpha / omega)
                rho = rhoNew

                ' p = r + beta*(p − omega·v)
                Call Axpy(st, -omega, v, p)
                Call Xpay(st, beta, r, p)

                ' phat = M⁻¹ p → z
                Call DiagScale(st, p, z)

                Call SpMv(st, z, v)

                Dim rHatV As Double = Dot(st, rhat, v)

                If std.Abs(rHatV) < 1.0E-30 OrElse Double.IsNaN(rHatV) Then
                    Exit While
                End If

                alpha = rho / rHatV

                ' s = r − alpha·v
                Call Copy(st, r, s)
                Call Axpy(st, -alpha, v, s)

                normR = BufNorm(st, s, den)

                If Double.IsNaN(normR) Then
                    Exit While
                End If

                If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                    Call Axpy(st, alpha, z, xv)
                    converged = True
                    Exit While
                End If

                Call DiagScale(st, s, t)
                Call SpMv(st, t, ap)

                Dim tT As Double = Dot(st, t, t)
                Dim ttAp As Double = Dot(st, t, ap)

                If std.Abs(tT) < 1.0E-30 OrElse Double.IsNaN(tT) Then
                    Exit While
                End If

                omega = ttAp / tT

                ' x += alpha·z + omega·t
                Call Axpy(st, alpha, z, xv)
                Call Axpy(st, omega, t, xv)

                ' r = s − omega·ap
                Call Copy(st, s, r)
                Call Axpy(st, -omega, ap, r)

                normR = RNorm(st, den)

                If Double.IsNaN(normR) Then
                    Exit While
                End If

                If normR <= tolerance OrElse (relTol > 0 AndAlso normR <= relTol * initial) Then
                    converged = True
                    Exit While
                End If

                If std.Abs(omega) < 1.0E-300 Then
                    Exit While
                End If
            End While

            Return iter
        End Function

        ''' <summary>对角（Jacobi）预条件：dst = invDiag ⊙ src。</summary>
        Private Sub DiagScale(st As DeviceState,
                              src As ILCudaRuntime.DeviceBuffer(Of Single),
                              dst As ILCudaRuntime.DeviceBuffer(Of Single))

            Call _kDiagScale.Launch(st.Config, st.DiagInv, src, dst, st.N)
        End Sub

        Private Sub Zero(st As DeviceState, buf As ILCudaRuntime.DeviceBuffer(Of Single))
            Call buf.Fill(0.0F)
        End Sub

        ''' <summary>任意缓冲的归一化范数（与 RNorm 的区别：不固定使用 r 缓冲）。</summary>
        Private Function BufNorm(st As DeviceState, buf As ILCudaRuntime.DeviceBuffer(Of Single), den As Double) As Double
            Dim d As Double = Dot(st, buf, buf)

            If d < 0 OrElse Double.IsNaN(d) Then Return Double.NaN

            Return std.Sqrt(d) / den
        End Function

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            If _state IsNot Nothing Then
                Call _state.Dispose()
                _state = Nothing
            End If
        End Sub

    End Class

End Namespace
