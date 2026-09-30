// ---------------------------------------------------------------------------
// moira_cfd_f32.cu
//
// CFDEngine 的 CFD float32 CUDA 内核源码（随 CFDEngine.dll 以嵌入资源分发，
// 由 CudaTensorF.CFDKernelSource 在运行时读取，经 ILCuda 的 NVRTC 即时编译）。
//
// 网格布局约定（与 CFDEngine / TensorF / VoxelShape 严格一致）：
//     idx = (i * ny + j) * nz + k
//         (i±1, j, k) -> idx ± ny*nz
//         (i, j±1, k) -> idx ± nz
//         (i, j, k±1) -> idx ± 1
// ---------------------------------------------------------------------------

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
