' /********************************************************************************/
'
'   FvmCompute.vb
'
'   FvmCompute —— 混合精度 CFD 引擎的「单精度热点算子」服务
'
'   作用：
'       把 FVM（有限体积 / SIMPLE）求解器中 GPU 收益最高的迭代式椭圆求解下放到
'       float32 执行：
'           · 压力修正的 p' 泊松方程（默认 40 次 Jacobi 扫描）
'           · 动量预估方程（每方向 1 次 Jacobi 扫描）
'       二者共用同一个「逐面变系数七点 Jacobi」内核。
'
'   ★ 为什么不是 ITensorComputeF 的一个 Override
'       ITensorComputeF 现有原语（JacobiStencil7 / Laplacian7）是**等系数**七点
'       模板（alpha / beta 标量 + nFluid 作除数），而 FVM 的面系数
'       D_f = d̄_f·A/δ 与迎风对流系数 ρ·max(∓F,0) 逐格变化。ITensorComputeF 是
'       GCModeller 的跨项目公共契约，扩展它会波及 TensorFlow / SPHEngine /
'       MeshGraph。因此把变系数算子放在本类（CFDEngine 内部）实现，
'       做到 GCModeller 零改动，同时自带 GPU / CPU 双实现。
'
'   ★ 精度边界
'       Single（本类）：迭代式椭圆求解。Jacobi 对舍入不敏感，内迭代的截断误差
'                       会被外层 SIMPLE / 伪瞬态推进吸收。
'       Double（求解器）：MRF 桨盘源项、k-ε 闭包、PBM 转移矩阵、DO 传质与 kLa、
'                       诊断统计量 —— 含 exp / pow / 归一化与比值，误差跨步累积。
'
'   用法：
'       If FvmCompute.TryRegister() Then
'           solver.UseSingleBackend = True     ' 热点路由到 float32 + CUDA
'       End If
'       ' 无 NVIDIA 设备 / NVRTC 编译失败时自动回落 TensorF + SIMD CPU 实现
'
' /********************************************************************************/

Imports System.IO
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports ILCudaRuntime = Microsoft.VisualBasic.Computing.ILCuda.Runtime
Imports ILCudaKernels = Microsoft.VisualBasic.Computing.ILCuda.Kernels

''' <summary>
''' 混合精度 FVM 热点算子服务：逐面变系数七点 Jacobi 的 float32 实现，
''' GPU（NVRTC 内联 <c>moira_fvm_f32.cu</c>）优先，无 GPU 时回落 SIMD CPU。
''' </summary>
Public Class FvmCompute
    Implements IDisposable

#Region "内核名称与 CUDA 源码"

    ''' <summary>变系数七点 Jacobi 内核名。</summary>
    Private Const KJacobi As String = "moira_fvm_jacobi_var"

    ''' <summary>嵌入资源中的内核源码文件名（按后缀匹配清单资源名）。</summary>
    Private Const FvmKernelResourceName As String = "moira_fvm_f32.cu"

    ''' <summary><see cref="FvmKernelSource"/> 的懒加载缓存。</summary>
    Private Shared _kernelSource As String

    ''' <summary>
    ''' FVM float32 内核源码（经 NVRTC 编译）。
    ''' 从本程序集的嵌入资源 <c>Kernels\moira_fvm_f32.cu</c> 读取并缓存。
    ''' 网格布局：idx = (i * ny + j) * nz + k，与 VoxelShape / TensorF / TensorGrid 一致。
    ''' </summary>
    Public Shared ReadOnly Property FvmKernelSource As String
        Get
            If _kernelSource IsNot Nothing Then Return _kernelSource

            Dim asm As Reflection.Assembly = GetType(FvmCompute).Assembly
            Dim resourceName As String = Nothing

            For Each name As String In asm.GetManifestResourceNames()
                If name.EndsWith(FvmKernelResourceName, StringComparison.OrdinalIgnoreCase) Then
                    resourceName = name
                    Exit For
                End If
            Next

            If resourceName Is Nothing Then
                Throw New InvalidOperationException(
                    $"程序集 {asm.GetName().Name} 中未找到嵌入的 CUDA 内核资源 " &
                    $"'{FvmKernelResourceName}'（请确认 vbproj 中的 EmbeddedResource 声明）")
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

    Private Shared _current As FvmCompute
    Private Shared _sourceRegistered As Boolean
    Private Shared _regLock As New Object()
    Private Shared _lastError As String

    ''' <summary>最近一次 <see cref="TryRegister"/> 失败的原因。</summary>
    Public Shared Property LastError As String
        Get
            Return _lastError
        End Get
        Set(value As String)
            _lastError = value
        End Set
    End Property

    ''' <summary>当前已注册的 GPU 后端实例（未注册时为 Nothing）。</summary>
    Public Shared ReadOnly Property Current As FvmCompute
        Get
            Return _current
        End Get
    End Property

    ''' <summary>是否已成功注册 GPU 后端。</summary>
    Public Shared ReadOnly Property IsGpuAvailable As Boolean
        Get
            Return _current IsNot Nothing
        End Get
    End Property

    ''' <summary>底层 CUDA 引擎（CPU 回落模式下为 Nothing）。</summary>
    Public ReadOnly Property Engine As ILCudaRuntime.CudaEngine
        Get
            Return _engine
        End Get
    End Property

    Private ReadOnly _engine As ILCudaRuntime.CudaEngine

    ''' <summary>本实例是否走 GPU 路径。</summary>
    Public ReadOnly Property IsGpu As Boolean
        Get
            Return _engine IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' 尝试初始化 CUDA 并把 FVM 热点算子切换为 GPU 后端。
    ''' 设备不可用 / NVRTC 编译失败时返回 False，且不影响既有调用（自动 CPU 回落）。
    ''' 幂等：重复调用直接返回 True。
    ''' </summary>
    Public Shared Function TryRegister(Optional deviceOrdinal As Integer = -1) As Boolean
        If _current IsNot Nothing Then Return True

        SyncLock _regLock
            If _current IsNot Nothing Then Return True

            Try
                ' 内核源码必须注册于 CudaEngine.TryCreate 之前（NVRTC 一次性编译整个编译单元）
                If Not _sourceRegistered Then
                    Call ILCudaKernels.KernelSources.RegisterSource(FvmKernelResourceName, FvmKernelSource)
                    _sourceRegistered = True
                End If

                Dim options As New ILCudaRuntime.EngineOptions()
                If deviceOrdinal >= 0 Then options.DeviceOrdinal = deviceOrdinal

                Dim engine = ILCudaRuntime.CudaEngine.TryCreate(options)
                If engine Is Nothing Then
                    _lastError = options.ErrorMessage
                    Return False
                End If

                _current = New FvmCompute(engine)
                _lastError = Nothing
                Return True
            Catch ex As Exception
                _lastError = ex.Message
                Return False
            End Try
        End SyncLock
    End Function

    ''' <summary>释放 GPU 后端，后续调用自动回落 CPU 实现。</summary>
    Public Shared Sub Unregister()
        SyncLock _regLock
            If _current IsNot Nothing Then
                Call _current.Dispose()
                _current = Nothing
            End If
        End SyncLock
    End Sub

    ''' <summary>以 CPU（TensorF / float32）模式创建实例，用于强制走非 GPU 路径。</summary>
    Public Shared Function CreateCpuBackend() As FvmCompute
        Return New FvmCompute(Nothing)
    End Function

    Private Sub New(engine As ILCudaRuntime.CudaEngine)
        _engine = engine
    End Sub

    ''' <summary>释放设备缓冲资源。</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If _scratchA IsNot Nothing Then
            Call _scratchA.Dispose()
            _scratchA = Nothing
        End If
        If _scratchB IsNot Nothing Then
            Call _scratchB.Dispose()
            _scratchB = Nothing
        End If
        If _maskBuf IsNot Nothing Then
            Call _maskBuf.Dispose()
            _maskBuf = Nothing
        End If
        _maskKey = Nothing
        _disposed = True
    End Sub

    Private _disposed As Boolean

#End Region

#Region "设备缓冲管理"

    Private _maskBuf As ILCudaRuntime.DeviceBuffer(Of Byte)
    Private _maskKey As Byte()
    Private _scratchA As ILCudaRuntime.DeviceBuffer(Of Single)
    Private _scratchB As ILCudaRuntime.DeviceBuffer(Of Single)
    Private _scratchN As Integer = -1

    ''' <summary>掩膜缓冲：按主机数组引用缓存（掩膜在求解期间不变）。</summary>
    Private Function GetMaskBuffer(mask As Byte(), n As Integer) As ILCudaRuntime.DeviceBuffer(Of Byte)
        If _maskBuf IsNot Nothing AndAlso _maskKey Is mask AndAlso _maskBuf.Count = n Then
            Return _maskBuf
        End If

        If _maskBuf IsNot Nothing Then
            Call _maskBuf.Dispose()
            _maskBuf = Nothing
        End If

        _maskBuf = New ILCudaRuntime.DeviceBuffer(Of Byte)(n)
        If mask IsNot Nothing Then
            Call _maskBuf.Write(mask)
        Else
            Call _maskBuf.Fill(CByte(0))
        End If
        _maskKey = mask

        Return _maskBuf
    End Function

    ''' <summary>Jacobi ping-pong 工作缓冲（成对重建）。</summary>
    Private Function GetScratch(slot As Integer, n As Integer) As ILCudaRuntime.DeviceBuffer(Of Single)
        If _scratchA Is Nothing OrElse _scratchN <> n Then
            If _scratchA IsNot Nothing Then Call _scratchA.Dispose()
            If _scratchB IsNot Nothing Then Call _scratchB.Dispose()
            _scratchA = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
            _scratchB = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
            _scratchN = n
        End If
        Return If(slot = 0, _scratchA, _scratchB)
    End Function

    Private Function Kernel(name As String) As ILCudaRuntime.CudaKernel
        If _engine Is Nothing Then Return Nothing
        Try
            Return _engine.GetKernel(name)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

#End Region

#Region "变系数七点 Jacobi"

    ''' <summary>
    ''' 逐面变系数七点 Jacobi 迭代（float32）。
    ''' </summary>
    ''' <param name="rhs">右端项（每格常数项，含源项与 aP0·u_old）。</param>
    ''' <param name="aEast">East 面系数（i+1 邻居）。</param>
    ''' <param name="aWest">West 面系数（i-1 邻居）。</param>
    ''' <param name="aNorth">North 面系数（j+1 邻居）。</param>
    ''' <param name="aSouth">South 面系数（j-1 邻居）。</param>
    ''' <param name="aTop">Top 面系数（k+1 邻居）。</param>
    ''' <param name="aBottom">Bottom 面系数（k-1 邻居）。</param>
    ''' <param name="aDiag">对角系数 aP（含欠松弛 / 时间项）。</param>
    ''' <param name="mask">活动掩膜（0 = 固体 / 非活动，输出恒 0）；Nothing 表示全场参与。</param>
    ''' <param name="iterations">Jacobi 扫描次数（动量预估取 1，压力泊松取 PressureSweeps）。</param>
    ''' <param name="initial">初始猜测；Nothing 时从全零开始。</param>
    ''' <returns>迭代结果（float32）。</returns>
    ''' <remarks>
    ''' 迭代语义与 CPU 参考实现 <see cref="CpuJacobi"/> 逐位一致：
    ''' 越界邻居按 0 计（与 TensorGrid.ShiftX/Y/Z 的补零语义相同），不做 Neumann 夹取。
    ''' </remarks>
    Public Function VarJacobi7(rhs As TensorF,
                               aEast As TensorF, aWest As TensorF,
                               aNorth As TensorF, aSouth As TensorF,
                               aTop As TensorF, aBottom As TensorF,
                               aDiag As TensorF,
                               mask As Byte(),
                               iterations As Integer,
                               Optional initial As TensorF = Nothing) As TensorF
        If iterations <= 0 Then
            Throw New ArgumentOutOfRangeException(NameOf(iterations), "Jacobi 扫描次数必须为正")
        End If

        If IsGpu Then
            Dim gpu = GpuJacobi(rhs, aEast, aWest, aNorth, aSouth, aTop, aBottom, aDiag, mask, iterations, initial)
            If gpu IsNot Nothing Then Return gpu
        End If

        Return CpuJacobi(rhs, aEast, aWest, aNorth, aSouth, aTop, aBottom, aDiag, mask, iterations, initial)
    End Function

    ''' <summary>
    ''' 全局便捷入口：已注册 GPU 后端时走 GPU，否则直接走 CPU float32 实现。
    ''' 调用方无需持有 <see cref="FvmCompute"/> 实例。
    ''' </summary>
    Public Shared Function Jacobi(rhs As TensorF,
                                  aEast As TensorF, aWest As TensorF,
                                  aNorth As TensorF, aSouth As TensorF,
                                  aTop As TensorF, aBottom As TensorF,
                                  aDiag As TensorF,
                                  mask As Byte(),
                                  iterations As Integer,
                                  Optional initial As TensorF = Nothing) As TensorF
        If _current IsNot Nothing Then
            Return _current.VarJacobi7(rhs, aEast, aWest, aNorth, aSouth, aTop, aBottom, aDiag, mask, iterations, initial)
        End If
        Return CpuJacobi(rhs, aEast, aWest, aNorth, aSouth, aTop, aBottom, aDiag, mask, iterations, initial)
    End Function

    ''' <summary>
    ''' GPU 路径：整个迭代在显存内 ping-pong 完成，
    ''' 主机侧只承担 8 个系数场的一次上传与结果的一次回读。
    ''' </summary>
    Private Function GpuJacobi(rhs As TensorF,
                               aEast As TensorF, aWest As TensorF,
                               aNorth As TensorF, aSouth As TensorF,
                               aTop As TensorF, aBottom As TensorF,
                               aDiag As TensorF,
                               mask As Byte(),
                               iterations As Integer,
                               initial As TensorF) As TensorF
        Dim k = Kernel(KJacobi)
        If k Is Nothing Then Return Nothing

        Dim shp = rhs.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = rhs.Length
        Dim maskBuf = GetMaskBuffer(mask, n)
        Dim useMask = If(mask IsNot Nothing, 1, 0)

        Dim cur = GetScratch(0, n)
        Dim nxt = GetScratch(1, n)

        If initial IsNot Nothing Then
            Call cur.Write(initial.Data)
        Else
            Call cur.Fill(0.0F)
        End If

        Using dRhs As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dE As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dW As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dN As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dS As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dT As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dB As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dDiag As New ILCudaRuntime.DeviceBuffer(Of Single)(n)

            Call dRhs.Write(rhs.Data)
            Call dE.Write(aEast.Data)
            Call dW.Write(aWest.Data)
            Call dN.Write(aNorth.Data)
            Call dS.Write(aSouth.Data)
            Call dT.Write(aTop.Data)
            Call dB.Write(aBottom.Data)
            Call dDiag.Write(aDiag.Data)

            For iter = 1 To iterations
                Call k.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                              dRhs, dE, dW, dN, dS, dT, dB, dDiag, maskBuf, cur, nxt,
                              nx, ny, nz, useMask, n)
                ' ping-pong：本轮输出即下一轮输入
                Dim swap = cur
                cur = nxt
                nxt = swap
            Next
        End Using

        Return TensorF.Wrap(cur.Read(), shp)
    End Function

    ''' <summary>
    ''' CPU 参考实现（float32）：与 GPU 内核 <c>moira_fvm_jacobi_var</c> 逐位一致，
    ''' 用于无 GPU 环境以及对拍校验。
    ''' </summary>
    Public Shared Function CpuJacobi(rhs As TensorF,
                                     aEast As TensorF, aWest As TensorF,
                                     aNorth As TensorF, aSouth As TensorF,
                                     aTop As TensorF, aBottom As TensorF,
                                     aDiag As TensorF,
                                     mask As Byte(),
                                     iterations As Integer,
                                     Optional initial As TensorF = Nothing) As TensorF
        Dim shp = rhs.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = rhs.Length
        Dim plane = ny * nz

        Dim cur As Single()
        If initial IsNot Nothing Then
            cur = CType(initial.Data.Clone(), Single())
        Else
            cur = New Single(n - 1) {}
        End If
        Dim nxt = New Single(n - 1) {}

        Dim r = rhs.Data
        Dim cE = aEast.Data, cW = aWest.Data
        Dim cN = aNorth.Data, cS = aSouth.Data
        Dim cT = aTop.Data, cB = aBottom.Data
        Dim cD = aDiag.Data

        For iter = 1 To iterations
            For idx = 0 To n - 1
                If mask IsNot Nothing AndAlso mask(idx) = 0 Then
                    nxt(idx) = 0.0F
                    Continue For
                End If

                Dim i = idx \ plane
                Dim r0 = idx - i * plane
                Dim j = r0 \ nz
                Dim k = r0 - j * nz

                Dim acc = r(idx)
                If i + 1 < nx Then acc += cE(idx) * cur(idx + plane)
                If i - 1 >= 0 Then acc += cW(idx) * cur(idx - plane)
                If j + 1 < ny Then acc += cN(idx) * cur(idx + nz)
                If j - 1 >= 0 Then acc += cS(idx) * cur(idx - nz)
                If k + 1 < nz Then acc += cT(idx) * cur(idx + 1)
                If k - 1 >= 0 Then acc += cB(idx) * cur(idx - 1)

                Dim d = cD(idx)
                nxt(idx) = If(d > 1.0E-20F OrElse d < -1.0E-20F, acc / d, 0.0F)
            Next

            Dim swap = cur
            cur = nxt
            nxt = swap
        Next

        Return TensorF.Wrap(cur, shp)
    End Function

#End Region

End Class
