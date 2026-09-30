' /********************************************************************************/
'
'   CudaTensorF.vb
'
'   CudaTensorF —— ITensorComputeF 的 CUDA GPU 实现后端（全 float32）
'
'   作用：
'       把 StableFluidsSolver 风洞求解器的热算子（Jacobi 扩散 / 压力泊松 /
'       半拉格朗日平流 / 固体掩膜置零）下放到 NVIDIA GPU 执行。
'
'   设计与定位：
'       * 继承标量兜底基类 TensorComputeFBase（与 Double 栈的 CudaTensor 同构），
'         只重写"GPU 有对应内核"的 CFD 原语；其余算子自动继承 CPU 实现，
'         保证任何算子都不会失效。
'       * 内核为内联 float32 CUDA C 源码（CFDKernelSource），经 NVRTC 即时编译。
'         注册必须发生在 CudaEngine.TryCreate 之前。
'       * 网格布局与 CFDEngine 严格一致：idx = (i * ny + j) * nz + k。
'       * 调用粒度为"每算子一次上传 / 一次回读"：Jacobi 迭代的 ping-pong
'         完全在显存内完成，主机侧每步只承担 O(算子数) 次传输。
'
'   用法（风洞测试）：
'       If CudaTensorF.TryRegister() Then
'           tunnel.Solver.UseCudaBackend = True    ' 热算子路由到 GPU
'       End If
'       ' 注册失败（无 NVIDIA 设备 / 驱动不匹配）时求解器自动走 CPU 路径
'
' /********************************************************************************/

Imports ILCudaRuntime = Microsoft.VisualBasic.Computing.ILCuda.Runtime
Imports ILCudaKernels = Microsoft.VisualBasic.Computing.ILCuda.Kernels
Imports Microsoft.VisualBasic.MachineLearning.TensorFlow
Imports tfCompute = Microsoft.VisualBasic.MachineLearning.TensorFlow.Compute

''' <summary>
''' <see cref="tfCompute.ITensorComputeF"/> 的 CUDA GPU 实现后端（float32）。
''' </summary>
Public Class CudaTensorF
    Inherits tfCompute.TensorComputeFBase

#Region "内核名称与 CUDA 源码"

    Private Const KJacobi As String = "moira_cfd_jacobi7"
    Private Const KAdvect As String = "moira_cfd_advect"
    Private Const KDiv As String = "moira_cfd_divergence"
    Private Const KGradSub As String = "moira_cfd_gradsub"
    Private Const KLaplacian As String = "moira_cfd_laplacian7"
    Private Const KZeroSolid As String = "moira_cfd_zero_solid"

    ''' <summary>
    ''' CFD float32 内核源码（经 NVRTC 编译）。
    ''' 网格布局：idx = (i * ny + j) * nz + k，与 VoxelShape / TensorF 一致。
    ''' </summary>
    Private Shared ReadOnly CFDKernelSource As String =
<cuda><![CDATA[
// 每格坐标分解：idx = (i*ny + j)*nz + k
static __device__ inline void decompose(int idx, int ny, int nz,
                                        int* i, int* j, int* k) {
    int plane = ny * nz;
    *i = idx / plane;
    int rem = idx - (*i) * plane;
    *j = rem / nz;
    *k = rem - (*j) * nz;
}

static __device__ inline int imin2(int a, int b) { return a < b ? a : b; }
static __device__ inline int imax2(int a, int b) { return a > b ? a : b; }

// 带掩膜的七点 Jacobi 迭代（一轮）：
//   流体内部单元: x_out = (alpha * sum(x_prev 6邻居) + rhs) / denom
//                 denom = beta（>0 时）或该格流体邻居数 nFluid（压力泊松）
//   固体单元    : 恒为 0
//   边界单元    : 零梯度（拷贝夹取到内部后的邻居值，模拟 Neumann 边界）
extern "C" __global__ void moira_cfd_jacobi7(
    const float* __restrict__ xPrev, const float* __restrict__ rhs,
    const unsigned char* mask, const unsigned char* nFluid, float* xOut,
    int nx, int ny, int nz, float alpha, float beta, int useMask, int divMode, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int i, j, k;
    decompose(idx, ny, nz, &i, &j, &k);

    if (useMask && mask[idx] == 0) { xOut[idx] = 0.0f; return; }

    bool interior = (i >= 1 && i <= nx - 2 && j >= 1 && j <= ny - 2 && k >= 1 && k <= nz - 2);
    if (interior) {
        float s = xPrev[idx - ny * nz] + xPrev[idx + ny * nz]
                + xPrev[idx - nz]       + xPrev[idx + nz]
                + xPrev[idx - 1]        + xPrev[idx + 1];
        float denom = (beta > 0.0f) ? beta
                    : ((divMode != 0) ? (float)nFluid[idx] : 6.0f);
        if (denom <= 0.0f) denom = 1.0f;
        xOut[idx] = (alpha * s + rhs[idx]) / denom;
    } else {
        int ci = imax2(1, imin2(i, nx - 2));
        int cj = imax2(1, imin2(j, ny - 2));
        int ck = imax2(1, imin2(k, nz - 2));
        xOut[idx] = xPrev[(ci * ny + cj) * nz + ck];
    }
}

// 半拉格朗日平流（纯 gather，完全并行）：
// 对每个流体格子沿速度场反向追踪 dt，在回溯位置做三线性插值采样源场。
// 坐标钳制到 [0.5, n-1.5]，固体单元输出恒为 0。
extern "C" __global__ void moira_cfd_advect(
    const float* __restrict__ src,
    const float* __restrict__ u, const float* __restrict__ v, const float* __restrict__ w,
    const unsigned char* mask, float* out,
    int nx, int ny, int nz, float dt, int useMask, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int i, j, k;
    decompose(idx, ny, nz, &i, &j, &k);

    if (useMask && mask[idx] == 0) { out[idx] = 0.0f; return; }

    float x = (float)i - dt * u[idx]; x = fminf(fmaxf(x, 0.5f), nx - 1.5f);
    float y = (float)j - dt * v[idx]; y = fminf(fmaxf(y, 0.5f), ny - 1.5f);
    float z = (float)k - dt * w[idx]; z = fminf(fmaxf(z, 0.5f), nz - 1.5f);

    int i0 = (int)floorf(x), j0 = (int)floorf(y), k0 = (int)floorf(z);
    float fx = x - i0, fy = y - j0, fz = z - k0;

    int i1 = imin2(i0 + 1, nx - 1), j1 = imin2(j0 + 1, ny - 1), k1 = imin2(k0 + 1, nz - 1);
    i0 = imax2(0, imin2(i0, nx - 1));
    j0 = imax2(0, imin2(j0, ny - 1));
    k0 = imax2(0, imin2(k0, nz - 1));

    int a00 = (i0 * ny + j0) * nz, a01 = (i0 * ny + j1) * nz;
    int a10 = (i1 * ny + j0) * nz, a11 = (i1 * ny + j1) * nz;

    float c000 = src[a00 + k0], c001 = src[a00 + k1];
    float c010 = src[a01 + k0], c011 = src[a01 + k1];
    float c100 = src[a10 + k0], c101 = src[a10 + k1];
    float c110 = src[a11 + k0], c111 = src[a11 + k1];

    float c00 = c000 * (1.0f - fx) + c100 * fx;
    float c01 = c001 * (1.0f - fx) + c101 * fx;
    float c10 = c010 * (1.0f - fx) + c110 * fx;
    float c11 = c011 * (1.0f - fx) + c111 * fx;

    float cc0 = c00 * (1.0f - fy) + c10 * fy;
    float cc1 = c01 * (1.0f - fy) + c11 * fy;

    out[idx] = cc0 * (1.0f - fz) + cc1 * fz;
}

// 散度 div = grad·u（中心差分，仅内部单元；固体 / 边界为 0）。
// 写出取负后的结果，直接作为压力泊松 Jacobi 的右端项 rhs = -div。
extern "C" __global__ void moira_cfd_divergence(
    const float* __restrict__ u, const float* __restrict__ v, const float* __restrict__ w,
    const unsigned char* mask, float* div,
    int nx, int ny, int nz, float invDt, int useMask, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int i, j, k;
    decompose(idx, ny, nz, &i, &j, &k);

    bool interior = (i >= 1 && i <= nx - 2 && j >= 1 && j <= ny - 2 && k >= 1 && k <= nz - 2);
    if (!interior || (useMask && mask[idx] == 0)) { div[idx] = 0.0f; return; }

    float sum = 0.5f * (u[idx + ny * nz] - u[idx - ny * nz])
              + 0.5f * (v[idx + nz]       - v[idx - nz])
              + 0.5f * (w[idx + 1]        - w[idx - 1]);

    div[idx] = -sum * invDt;
}

// 速度减去压力梯度（就地更新，仅内部单元）：
// 固体邻居用本格压力代替（零梯度），保证壁面无通量。
extern "C" __global__ void moira_cfd_gradsub(
    const float* __restrict__ p, const unsigned char* mask,
    float* u, float* v, float* w,
    int nx, int ny, int nz, float dt, int useMask, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int i, j, k;
    decompose(idx, ny, nz, &i, &j, &k);

    bool interior = (i >= 1 && i <= nx - 2 && j >= 1 && j <= ny - 2 && k >= 1 && k <= nz - 2);
    if (!interior) return;
    if (useMask && mask[idx] == 0) { u[idx] = 0.0f; v[idx] = 0.0f; w[idx] = 0.0f; return; }

    float pc  = p[idx];
    float pXp = (!useMask || mask[idx + ny * nz] != 0) ? p[idx + ny * nz] : pc;
    float pXm = (!useMask || mask[idx - ny * nz] != 0) ? p[idx - ny * nz] : pc;
    float pYp = (!useMask || mask[idx + nz] != 0)      ? p[idx + nz]      : pc;
    float pYm = (!useMask || mask[idx - nz] != 0)      ? p[idx - nz]      : pc;
    float pZp = (!useMask || mask[idx + 1] != 0)       ? p[idx + 1]       : pc;
    float pZm = (!useMask || mask[idx - 1] != 0)       ? p[idx - 1]       : pc;

    u[idx] -= dt * (pXp - pXm) * 0.5f;
    v[idx] -= dt * (pYp - pYm) * 0.5f;
    w[idx] -= dt * (pZp - pZm) * 0.5f;
}

// 七点拉普拉斯：out = sum(流体邻居) - nFluid * 中心（仅内部单元，固体 / 边界为 0）
extern "C" __global__ void moira_cfd_laplacian7(
    const float* __restrict__ x, const unsigned char* mask, const unsigned char* nFluid,
    float* out, int nx, int ny, int nz, int useMask, int divMode, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int i, j, k;
    decompose(idx, ny, nz, &i, &j, &k);

    bool interior = (i >= 1 && i <= nx - 2 && j >= 1 && j <= ny - 2 && k >= 1 && k <= nz - 2);
    if (!interior || (useMask && mask[idx] == 0)) { out[idx] = 0.0f; return; }

    float s = 0.0f;
    if (!useMask || mask[idx - ny * nz] != 0) s += x[idx - ny * nz];
    if (!useMask || mask[idx + ny * nz] != 0) s += x[idx + ny * nz];
    if (!useMask || mask[idx - nz] != 0)      s += x[idx - nz];
    if (!useMask || mask[idx + nz] != 0)      s += x[idx + nz];
    if (!useMask || mask[idx - 1] != 0)       s += x[idx - 1];
    if (!useMask || mask[idx + 1] != 0)       s += x[idx + 1];

    float div = (divMode != 0) ? (float)nFluid[idx] : 6.0f;
    out[idx] = s - div * x[idx];
}

// 按掩膜把固体单元置零（无滑移壁）
extern "C" __global__ void moira_cfd_zero_solid(
    const unsigned char* mask, float* f, int useMask, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx < n && useMask && mask[idx] == 0) f[idx] = 0.0f;
}
]]></cuda>.Value

#End Region

#Region "注册与生命周期"

    Private Shared _current As CudaTensorF
    Private Shared _sourceRegistered As Boolean
    Private Shared _regLock As New Object()

    ''' <summary>最近一次 <see cref="TryRegister"/> 失败的原因</summary>
    Public Shared Property LastError As String

    ''' <summary>当前已注册的 GPU 后端实例（未注册时为 Nothing）</summary>
    Public Shared ReadOnly Property Current As CudaTensorF
        Get
            Return _current
        End Get
    End Property

    Public Overrides ReadOnly Property Name As String = "CUDA-F32"

    ''' <summary>底层 CUDA 引擎</summary>
    Public ReadOnly Property Engine As ILCudaRuntime.CudaEngine

    ''' <summary>
    ''' 尝试初始化 CUDA 并把 <see cref="TensorF.computeKernelF"/> 切换为本 GPU 后端。
    ''' 设备不可用 / NVRTC 编译失败时返回 False，并且不改变当前后端。
    ''' 幂等：重复调用直接返回 True。
    ''' </summary>
    Public Shared Function TryRegister(Optional deviceOrdinal As Integer = -1) As Boolean
        If _current IsNot Nothing Then Return True

        SyncLock _regLock
            If _current IsNot Nothing Then Return True

            Try
                ' 内核源码必须注册于 CudaEngine.TryCreate 之前（NVRTC 一次性编译整个编译单元）
                If Not _sourceRegistered Then
                    Call ILCudaKernels.KernelSources.RegisterSource("moira_cfd_f32.cu", CFDKernelSource)
                    _sourceRegistered = True
                End If

                Dim options As New ILCudaRuntime.EngineOptions()
                If deviceOrdinal >= 0 Then options.DeviceOrdinal = deviceOrdinal

                Dim engine = ILCudaRuntime.CudaEngine.TryCreate(options)
                If engine Is Nothing Then
                    LastError = options.ErrorMessage
                    Return False
                End If

                Dim backend As New CudaTensorF(engine)

                SyncLock TensorF.SyncRoot
                    TensorF.computeKernelF = backend
                End SyncLock

                _current = backend
                LastError = Nothing
                Return True
            Catch ex As Exception
                LastError = ex.Message
                Return False
            End Try
        End SyncLock
    End Function

    ''' <summary>把单精度计算后端切回默认的 SIMD CPU 实现，并释放 GPU 资源</summary>
    Public Shared Sub Unregister()
        SyncLock _regLock
            If _current IsNot Nothing Then
                SyncLock TensorF.SyncRoot
                    TensorF.computeKernelF = tfCompute.SIMDTensorF.Default
                End SyncLock

                Call _current.Dispose()
                _current = Nothing
            End If
        End SyncLock
    End Sub

    Private Sub New(engine As ILCudaRuntime.CudaEngine)
        If engine Is Nothing Then Throw New ArgumentNullException(NameOf(engine))
        Me.Engine = engine
    End Sub

    ''' <summary>释放设备缓冲资源</summary>
    Public Sub Dispose()
        If _scratchA IsNot Nothing Then
            Call _scratchA.Dispose()
            _scratchA = Nothing
        End If
        If _scratchB IsNot Nothing Then
            Call _scratchB.Dispose()
            _scratchB = Nothing
        End If
        If _maskSlot.Buf IsNot Nothing Then
            Call _maskSlot.Buf.Dispose()
            _maskSlot.Buf = Nothing
        End If
        If _nFluidSlot.Buf IsNot Nothing Then
            Call _nFluidSlot.Buf.Dispose()
            _nFluidSlot.Buf = Nothing
        End If
    End Sub

#End Region

#Region "设备缓冲管理"

    Private ReadOnly _maskSlot As New ByteBufSlot()
    Private ReadOnly _nFluidSlot As New ByteBufSlot()

    Private _scratchA As ILCudaRuntime.DeviceBuffer(Of Single)
    Private _scratchB As ILCudaRuntime.DeviceBuffer(Of Single)
    Private _scratchN As Integer = -1

    ''' <summary>Byte 缓冲槽：按主机数组引用缓存（掩膜 / 邻居数在求解期间不变）</summary>
    Private NotInheritable Class ByteBufSlot
        Public Buf As ILCudaRuntime.DeviceBuffer(Of Byte)
        Public Key As Byte()
    End Class

    Private Function GetByteBuffer(slot As ByteBufSlot, data As Byte(), n As Integer) As ILCudaRuntime.DeviceBuffer(Of Byte)
        If slot.Buf IsNot Nothing AndAlso slot.Key Is data AndAlso slot.Buf.Count = n Then
            Return slot.Buf
        End If

        If slot.Buf IsNot Nothing Then
            Call slot.Buf.Dispose()
            slot.Buf = Nothing
        End If

        slot.Buf = New ILCudaRuntime.DeviceBuffer(Of Byte)(n)
        If data IsNot Nothing Then
            Call slot.Buf.Write(data)
        Else
            Call slot.Buf.Fill(CByte(0))
        End If
        slot.Key = data

        Return slot.Buf
    End Function

    ''' <summary>Jacobi ping-pong 工作缓冲（成对重建）</summary>
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
        Try
            Return _engine.GetKernel(name)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

#End Region

#Region "Jacobi 核心（显存内 ping-pong）"

    ''' <summary>
    ''' 在显存内跑完整的 Jacobi 迭代。
    ''' 返回持有结果的设备缓冲（可能是交换后的 scratch，须在下次复用前 Read）。
    ''' </summary>
    ''' <param name="dRhs">右端项（已上传）</param>
    ''' <param name="dInit">初始猜测（已上传）；Nothing 且 zeroInit 时全零开始</param>
    Private Function JacobiCore(dRhs As ILCudaRuntime.DeviceBuffer(Of Single),
                                dInit As ILCudaRuntime.DeviceBuffer(Of Single),
                                zeroInit As Boolean,
                                nx As Integer, ny As Integer, nz As Integer,
                                alpha As Single, beta As Single,
                                iterations As Integer,
                                maskBuf As ILCudaRuntime.DeviceBuffer(Of Byte),
                                nFluidBuf As ILCudaRuntime.DeviceBuffer(Of Byte),
                                useMask As Integer, divMode As Integer,
                                n As Integer) As ILCudaRuntime.DeviceBuffer(Of Single)

        Dim k = Kernel(KJacobi)
        If k Is Nothing Then Throw New InvalidOperationException("CUDA 内核 moira_cfd_jacobi7 不可用")

        Dim a = GetScratch(0, n)
        Dim b = GetScratch(1, n)

        If dInit IsNot Nothing Then
            Call dInit.CopyTo(a)
        ElseIf zeroInit Then
            Call a.Fill(0.0F)
        End If

        For iter = 1 To iterations
            Call k.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                               a, dRhs, maskBuf, nFluidBuf, b,
                               nx, ny, nz, alpha, beta, useMask, divMode, n)
            ' ping-pong：交换引用，下一轮 prev=a（刚算出的）、out=b
            Dim t = a
            a = b
            b = t
        Next

        Return a

    End Function

#End Region

#Region "ITensorComputeF CFD 原语（GPU 重写）"

    ''' <summary>
    ''' 带掩膜的七点 Jacobi 迭代（GPU）：整个迭代在显存内 ping-pong 完成，
    ''' 主机侧只有 rhs / initial 的一次上传与结果的一次回读。
    ''' </summary>
    Public Overrides Function JacobiStencil7(rhs As TensorF, mask As Byte(), nFluid As Byte(),
                                             alpha As Single, beta As Single,
                                             iterations As Integer,
                                             Optional initial As TensorF = Nothing) As TensorF
        Dim k = Kernel(KJacobi)
        If k Is Nothing OrElse iterations <= 0 Then
            Return MyBase.JacobiStencil7(rhs, mask, nFluid, alpha, beta, iterations, initial)
        End If

        Dim shp = rhs.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = rhs.Length
        Dim maskBuf = GetByteBuffer(_maskSlot, mask, n)
        Dim nFluidBuf = GetByteBuffer(_nFluidSlot, nFluid, n)
        Dim useMask = If(mask IsNot Nothing, 1, 0)
        Dim divMode = If(nFluid IsNot Nothing, 1, 0)

        Dim dInit As ILCudaRuntime.DeviceBuffer(Of Single) = Nothing

        Try
            Using dRhs As New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                Call dRhs.Write(rhs.Data)

                If initial IsNot Nothing Then
                    dInit = New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                    Call dInit.Write(initial.Data)
                End If

                Dim result = JacobiCore(dRhs, dInit, zeroInit:=False,
                                        nx, ny, nz, alpha, beta, iterations,
                                        maskBuf, nFluidBuf, useMask, divMode, n)

                Return TensorF.Wrap(result.Read(), shp)
            End Using
        Finally
            If dInit IsNot Nothing Then Call dInit.Dispose()
        End Try
    End Function

    ''' <summary>七点拉普拉斯 stencil（GPU）</summary>
    Public Overrides Function Laplacian7(src As TensorF, mask As Byte(), nFluid As Byte()) As TensorF
        Dim k = Kernel(KLaplacian)
        If k Is Nothing Then
            Return MyBase.Laplacian7(src, mask, nFluid)
        End If

        Dim shp = src.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = src.Length
        Dim maskBuf = GetByteBuffer(_maskSlot, mask, n)
        Dim nFluidBuf = GetByteBuffer(_nFluidSlot, nFluid, n)
        Dim useMask = If(mask IsNot Nothing, 1, 0)
        Dim divMode = If(nFluid IsNot Nothing, 1, 0)

        Using dX As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dOut As New ILCudaRuntime.DeviceBuffer(Of Single)(n)

            Call dX.Write(src.Data)
            Call k.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                               dX, maskBuf, nFluidBuf, dOut, nx, ny, nz, useMask, divMode, n)

            Return TensorF.Wrap(dOut.Read(), shp)
        End Using
    End Function

    ''' <summary>半拉格朗日三线性平流（GPU，纯 gather 完全并行）</summary>
    Public Overrides Function AdvectTrilinear(src As TensorF,
                                              u As TensorF, v As TensorF, w As TensorF,
                                              dt As Single, mask As Byte()) As TensorF
        Dim k = Kernel(KAdvect)
        If k Is Nothing Then
            Return MyBase.AdvectTrilinear(src, u, v, w, dt, mask)
        End If

        Dim shp = src.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = src.Length
        Dim maskBuf = GetByteBuffer(_maskSlot, mask, n)
        Dim useMask = If(mask IsNot Nothing, 1, 0)

        Using dSrc As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dU As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dV As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dW As New ILCudaRuntime.DeviceBuffer(Of Single)(n),
              dOut As New ILCudaRuntime.DeviceBuffer(Of Single)(n)

            Call dSrc.Write(src.Data)
            Call dU.Write(u.Data)
            Call dV.Write(v.Data)
            Call dW.Write(w.Data)

            Call k.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                               dSrc, dU, dV, dW, maskBuf, dOut, nx, ny, nz, dt, useMask, n)

            Return TensorF.Wrap(dOut.Read(), shp)
        End Using
    End Function

    ''' <summary>按掩膜把所有给定场的固体单元置零（GPU）</summary>
    Public Overrides Sub ApplyMask(mask As Byte(), ParamArray fields As TensorF())
        Dim k = Kernel(KZeroSolid)
        If k Is Nothing Then
            Call MyBase.ApplyMask(mask, fields)
            Return
        End If
        If mask Is Nothing OrElse fields Is Nothing Then Return

        Dim n = mask.Length
        Dim maskBuf = GetByteBuffer(_maskSlot, mask, n)

        For Each f In fields
            If f Is Nothing OrElse f.Length <> n Then Continue For

            Using dF As New ILCudaRuntime.DeviceBuffer(Of Single)(n)
                Call dF.Write(f.Data)
                Call k.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                                   maskBuf, dF, 1, n)
                Call Array.Copy(dF.Read(), f.Data, n)
            End Using
        Next
    End Sub

#End Region

#Region "求解器复合算子（主机进 / 主机出，迭代全程驻留显存）"

    ''' <summary>
    ''' GPU 压力投影：散度 → 泊松 Jacobi → 速度减压力梯度，
    ''' 三个阶段的数据全程驻留显存，主机侧每阶段仅一次传输。
    ''' 迭代结束后的边界条件与固体置零由调用方（StableFluidsSolver）在主机侧完成，
    ''' 与 CPU 路径保持一致的边界语义。
    ''' </summary>
    ''' <param name="u">速度场 X 分量（就地更新）</param>
    ''' <param name="v">速度场 Y 分量（就地更新）</param>
    ''' <param name="w">速度场 Z 分量（就地更新）</param>
    ''' <param name="pressure">压力场（写入泊松解）</param>
    ''' <param name="dt">时间步长</param>
    ''' <param name="mask">固体掩膜（0 = 固体）</param>
    ''' <param name="nFluid">每格流体邻居数（0..6）</param>
    ''' <param name="iterations">泊松 Jacobi 迭代次数</param>
    Public Sub GpuProject(u As TensorF, v As TensorF, w As TensorF, pressure As TensorF,
                          dt As Double, mask As Byte(), nFluid As Byte(),
                          iterations As Integer)

        Dim divKernel = Kernel(KDiv)
        Dim gradKernel = Kernel(KGradSub)
        If divKernel Is Nothing OrElse gradKernel Is Nothing OrElse Kernel(KJacobi) Is Nothing Then
            Throw New InvalidOperationException("CUDA CFD 内核不可用（NVRTC 编译失败或内核缺失）")
        End If

        Dim shp = u.Shape
        Dim nx = shp(0), ny = shp(1), nz = shp(2)
        Dim n = u.Length
        Dim maskBuf = GetByteBuffer(_maskSlot, mask, n)
        Dim nFluidBuf = GetByteBuffer(_nFluidSlot, nFluid, n)
        Dim useMask = If(mask IsNot Nothing, 1, 0)
        Dim divMode = If(nFluid IsNot Nothing, 1, 0)
        Dim invDt = CSng(1.0 / dt)

        Dim du As New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        Dim dv As New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        Dim dw As New ILCudaRuntime.DeviceBuffer(Of Single)(n)
        Dim dDiv As New ILCudaRuntime.DeviceBuffer(Of Single)(n)

        Try
            ' ---- 上传速度场 ----
            Call du.Write(u.Data)
            Call dv.Write(v.Data)
            Call dw.Write(w.Data)

            ' ---- Step 1: 散度（写出取负后的 div 作为泊松右端项）----
            Call divKernel.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                                  du, dv, dw, maskBuf, dDiv, nx, ny, nz, invDt, useMask, n)

            ' ---- Step 2: 压力泊松 Jacobi（alpha=1, beta=0 → 除数取流体邻居数；初始猜测全零）----
            Dim dP = JacobiCore(dDiv, Nothing, zeroInit:=True,
                                nx, ny, nz, 1.0F, 0.0F, iterations,
                                maskBuf, nFluidBuf, useMask, divMode, n)

            ' 回读压力场
            Call Array.Copy(dP.Read(), pressure.Data, n)

            ' ---- Step 3: 速度减去压力梯度（就地）----
            Call gradKernel.Launch(ILCudaRuntime.LaunchPlanner.For1D(n, 256),
                                   dP, maskBuf, du, dv, dw, nx, ny, nz, CSng(dt), useMask, n)

            ' ---- 回读速度场 ----
            Call Array.Copy(du.Read(), u.Data, n)
            Call Array.Copy(dv.Read(), v.Data, n)
            Call Array.Copy(dw.Read(), w.Data, n)
        Finally
            Call du.Dispose()
            Call dv.Dispose()
            Call dw.Dispose()
            Call dDiv.Dispose()
        End Try

    End Sub

#End Region

End Class
