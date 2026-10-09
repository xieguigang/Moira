// ---------------------------------------------------------------------------
// moira_fvm_f32.cu
//
// CFDEngine 的 FVM（有限体积 / SIMPLE）float32 CUDA 内核源码
// （随 CFDEngine.dll 以嵌入资源分发，由 FvmCompute.FvmKernelSource 在运行时
//   读取，经 ILCuda 的 NVRTC 即时编译）。
//
// ★ 与 moira_cfd_f32.cu（StableFluids 路径）的区别
//      moira_cfd_f32.cu   —— 等系数七点模板（alpha / beta 标量 + nFluid 除数）
//      moira_fvm_f32.cu   —— 逐面变系数七点模板（aE/aW/aN/aS/aT/aB/aDiag 逐格）
//   后者是 FVM 压力泊松（SIMPLE 的 p' 方程）与动量预估方程的必需形态：
//   面系数 D_f = d̄_f·A/δ 与迎风对流系数 ρ·max(∓F,0) 都是空间变化的。
//
//   精度定位：Jacobi 属于迭代式椭圆求解，对舍入不敏感 —— 内迭代的截断误差
//   会被外层 SIMPLE / 伪瞬态推进吸收，因此整条链路走 float32 是安全的，
//   且显存带宽与占用相比 float64 减半。
//
// 网格布局约定（与 CFDEngine / TensorF / VoxelShape / TensorGrid 严格一致）：
//     idx = (i * ny + j) * nz + k
//         (i±1, j, k) -> idx ± ny*nz
//         (i, j±1, k) -> idx ± nz
//         (i, j, k±1) -> idx ± 1
//
// 边界约定（★ 必须与 TensorGrid.ShiftX/Y/Z 的 CPU 语义逐位一致）：
//     越界邻居按 0 计（补零），不做 Neumann 夹取。
//     mask == 0（固体 / 非活动体素）恒输出 0。
// ---------------------------------------------------------------------------

// 每格坐标分解：idx = (i*ny + j)*nz + k
static __device__ inline void fvm_decompose(int idx, int ny, int nz,
                                            int* i, int* j, int* k) {
    int plane = ny * nz;
    *i = idx / plane;
    int rem  = idx - (*i) * plane;
    *j = rem / nz;
    *k = rem - (*j) * nz;
}

// ---------------------------------------------------------------------------
// 逐面变系数七点 Jacobi（一轮）
//
//   x_out[idx] = ( rhs[idx]
//                + aE[idx] * x_prev[idx + ny*nz]   (i+1 < nx)
//                + aW[idx] * x_prev[idx - ny*nz]   (i-1 >= 0)
//                + aN[idx] * x_prev[idx + nz]      (j+1 < ny)
//                + aS[idx] * x_prev[idx - nz]      (j-1 >= 0)
//                + aT[idx] * x_prev[idx + 1]       (k+1 < nz)
//                + aB[idx] * x_prev[idx - 1]       (k-1 >= 0)
//               ) / aDiag[idx]
//
// 两个入口都映射到这一个内核：
//   · 动量预估（SIMPLE 第 1 步）：iterations = 1，initial = 上一步速度
//   · 压力修正 p' 泊松（SIMPLE 第 2 步）：iterations = 40，initial = 全零
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_fvm_jacobi_var(
    const float* __restrict__ rhs,
    const float* __restrict__ aE,
    const float* __restrict__ aW,
    const float* __restrict__ aN,
    const float* __restrict__ aS,
    const float* __restrict__ aT,
    const float* __restrict__ aB,
    const float* __restrict__ aDiag,
    const unsigned char* __restrict__ mask,
    const float* __restrict__ xPrev,
    float* __restrict__ xOut,
    int nx, int ny, int nz, int useMask, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    if (useMask && mask[idx] == 0) { xOut[idx] = 0.0f; return; }

    int i, j, k;
    fvm_decompose(idx, ny, nz, &i, &j, &k);

    int plane = ny * nz;
    float s = rhs[idx];

    if (i + 1 < nx) s += aE[idx] * xPrev[idx + plane];
    if (i - 1 >= 0) s += aW[idx] * xPrev[idx - plane];
    if (j + 1 < ny) s += aN[idx] * xPrev[idx + nz];
    if (j - 1 >= 0) s += aS[idx] * xPrev[idx - nz];
    if (k + 1 < nz) s += aT[idx] * xPrev[idx + 1];
    if (k - 1 >= 0) s += aB[idx] * xPrev[idx - 1];

    float d = aDiag[idx];
    // 对角退化（全固体 / 系数全零）时输出 0，避免除零把 NaN 扩散到全场
    xOut[idx] = (d > 1.0e-20f || d < -1.0e-20f) ? (s / d) : 0.0f;
}
