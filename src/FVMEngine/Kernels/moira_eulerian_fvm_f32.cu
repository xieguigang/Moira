// ---------------------------------------------------------------------------
// moira_eulerian_fvm_f32.cu
//
// Eulerian（非结构多面体网格）FVM 求解器的 float32 CUDA 热内核
// （随 Moira.FVMEngine.dll 以嵌入资源分发，由 EulerianFvmCompute 在运行时
//   读取，经 ILCuda 的 NVRTC 即时编译）。
//
// ★ 精度定位（混合精度契约）
//     本文件只承担"内迭代线性求解"这一热点：
//       · CSR 稀疏矩阵–向量乘（SpMV）
//       · 向量 axpy / xpay / copy
//       · 部分和归约（点积、范数平方）
//       · 一次带阻尼的 Jacobi 扫描
//     全部 float32。
//     矩阵系数、右端项、边界条件、残差判定一律留在主机侧 double：
//       · 每次求解前把 values / b / x0 降为 float32 上传；
//       · 迭代在显存内完成（主机只参与每次归约的标量交换）；
//       · 结束后把 x 回读，并在 **double** 下重算归一化残差作为收敛依据。
//     因此 float32 只影响内迭代的收敛路径，不改变离散算子的精度。
//
// ★ 与 moira_fvm_f32.cu 的区别
//     moira_fvm_f32.cu 是**结构化七点模板**（idx = (i*ny+j)*nz+k，等/变系数）；
//     本文件是**CSR 非结构稀疏**模板（rowPtr / colIdx / values），
//     面向任意面数多面体、四面体、结构六面体与棱柱网格。
//
// ★ 网格布局约定（与 EulerianFvMesh 的 CSR 完全一致）
//     每行首元素固定为对角元（colIdx[rowPtr[i]] == i）。
//     -1（孤立方程，已在主机侧 Normalise 修正）不在此处处理。
//
// ★ 归约约定
//     dot_partial 每个 block 输出一个部分和到 partial[blockIdx.x]；
//     主机把 gridDim.x 个部分和以 double 累加，避免 float32 长链求和误差。
//     启动配置使用 LaunchPlanner.For1D(n, 256)：blockDim.x = 256，gridDim.x <= 4096。
// ---------------------------------------------------------------------------

#define MOIRA_EFVM_BLOCK 256

// ---------------------------------------------------------------------------
// CSR 稀疏矩阵–向量乘： y = A * x
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_spmv_csr(
    const int*   __restrict__ rowPtr,
    const int*   __restrict__ colIdx,
    const float* __restrict__ values,
    const float* __restrict__ x,
    float*       __restrict__ y,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        const int e0 = rowPtr[i];
        const int e1 = rowPtr[i + 1];
        float s = 0.0f;

        for (int e = e0; e < e1; ++e) {
            s += values[e] * x[colIdx[e]];
        }

        y[i] = s;
    }
}

// ---------------------------------------------------------------------------
// y += alpha * x
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_axpy(
    float alpha,
    const float* __restrict__ x,
    float*       __restrict__ y,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        y[i] += alpha * x[i];
    }
}

// ---------------------------------------------------------------------------
// y = x + alpha * y    （用于 p = z + beta*p 这类"主向量在右"的更新）
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_xpay(
    float alpha,
    const float* __restrict__ x,
    float*       __restrict__ y,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        y[i] = x[i] + alpha * y[i];
    }
}

// ---------------------------------------------------------------------------
// y = x
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_copy(
    const float* __restrict__ x,
    float*       __restrict__ y,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        y[i] = x[i];
    }
}

// ---------------------------------------------------------------------------
// 部分和归约： dot[i] = SUM(x[i] * y[i])
//   partial[blockIdx.x] = 本 block 的部分和
//   传 y = x 即可得到向量范数平方（|x|²）
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_dot_partial(
    const float* __restrict__ x,
    const float* __restrict__ y,
    float*       __restrict__ partial,
    int n)
{
    __shared__ float sdata[MOIRA_EFVM_BLOCK * 4];

    const int tid = threadIdx.x;
    const int stride = blockDim.x * gridDim.x;
    float acc = 0.0f;

    for (int i = blockIdx.x * blockDim.x + tid; i < n; i += stride) {
        acc += x[i] * y[i];
    }

    sdata[tid] = acc;
    __syncthreads();

    for (int s = blockDim.x >> 1; s > 0; s >>= 1) {
        if (tid < s) {
            sdata[tid] += sdata[tid + s];
        }
        __syncthreads();
    }

    if (tid == 0) {
        partial[blockIdx.x] = sdata[0];
    }
}

// ---------------------------------------------------------------------------
// 一次带阻尼的 Jacobi 扫描（作为 GPU 侧平滑器 / 快速动量预测）
//   xOut[i] = xPrev[i] + damping * ( (b[i] - SUM_{j != i} A[i,j]*xPrev[j]) * diagInv[i] - xPrev[i] )
// 对角元固定在每条 CSR 行的行首，循环中按 j == i 跳过。
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_jacobi_csr(
    const int*   __restrict__ rowPtr,
    const int*   __restrict__ colIdx,
    const float* __restrict__ values,
    const float* __restrict__ diagInv,
    const float* __restrict__ b,
    const float* __restrict__ xPrev,
    float*       __restrict__ xOut,
    float damping,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        const int e0 = rowPtr[i];
        const int e1 = rowPtr[i + 1];
        float s = b[i];

        for (int e = e0; e < e1; ++e) {
            const int j = colIdx[e];

            if (j != i) {
                s -= values[e] * xPrev[j];
            }
        }

        const float xNew = s * diagInv[i];
        const float x0 = xPrev[i];

        xOut[i] = x0 + damping * (xNew - x0);
    }
}

// ---------------------------------------------------------------------------
// 对角（Jacobi）预条件： y[i] = factor[i] * x[i]
//   factor 由主机侧以 double 计算 1/a_ii 后降为 float32 上传
//   （|a_ii| 过小的单元已在 FvMatrix.Normalise 中改为硬约束，factor 置 0）
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_efvm_diag_scale(
    const float* __restrict__ factor,
    const float* __restrict__ x,
    float*       __restrict__ y,
    int n)
{
    const int stride = blockDim.x * gridDim.x;

    for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += stride) {
        y[i] = factor[i] * x[i];
    }
}
