// moira_lbm_f32.cu
//
// LBM float32 CUDA kernels (D3Q19, fused per-step kernels, computed by NVRTC).
//
// Grid layout (consistent with TensorF / TensorGrid / VoxelShape):
//     idx = (i * ny + j) * nz + k
//         (i, j, k)  : i -> X (streamwise), j -> Y (vertical), k -> Z (spanwise)
// Distribution function storage is DIRECTION-MAJOR:
//     f[a * n + idx], a = 0..18 (D3Q19), idx = cell linear index
//
// Cell boundary type (unsigned char, shared with the VB host):
//     0 = fluid
//     1 = solid (no-slip halfway bounce-back, frozen to f = w)
//     2 = inlet (equilibrium distribution with freestream velocity, rho = 1)
//     3 = outlet (zero-gradient: copy collided distribution from the x-1 neighbour)
//     5 = moving solid (no-slip bounce-back + moving-wall momentum correction,
//         used for the lid-driven cavity top wall)

static __device__ const int lbEX[19] = {
    0,  1, -1,  0,  0,  0,  0,  1, -1,  1, -1,  1, -1,  1, -1,  0,  0,  0,  0
};
static __device__ const int lbEY[19] = {
    0,  0,  0,  1, -1,  0,  0,  1, -1, -1,  1,  0,  0,  0,  0,  1, -1,  1, -1
};
static __device__ const int lbEZ[19] = {
    0,  0,  0,  0,  0,  1, -1,  0,  0,  0,  0,  1, -1, -1,  1,  1, -1, -1,  1
};
static __device__ const float lbW[19] = {
    1.0f / 3.0f,
    1.0f / 18.0f, 1.0f / 18.0f, 1.0f / 18.0f, 1.0f / 18.0f, 1.0f / 18.0f, 1.0f / 18.0f,
    1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f,
    1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f,
    1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f, 1.0f / 36.0f
};
static __device__ const int lbOpp[19] = {
    0,  2,  1,  4,  3,  6,  5,  8,  7, 10,  9, 12, 11, 14, 13, 16, 15, 18, 17
};

// specular (free-slip) reflection maps: flip the EZ / EY component, keep the rest
static __device__ const int lbZMirror[19] = {
    0,  1,  2,  3,  4,  6,  5,  7,  8,  9, 10, 13, 14, 11, 12, 17, 18, 15, 16
};
static __device__ const int lbYMirror[19] = {
    0,  1,  2,  4,  3,  5,  6,  9, 10,  7,  8, 11, 12, 13, 14, 18, 17, 16, 15
};

static __device__ inline float lbfeq(int a, float rho, float ux, float uy, float uz) {
    // f_eq = w * rho * [1 + 3(e.u) + 4.5(e.u)^2 - 1.5|u|^2]
    float eu = (float)lbEX[a] * ux + (float)lbEY[a] * uy + (float)lbEZ[a] * uz;
    float u2 = ux * ux + uy * uy + uz * uz;
    return lbW[a] * rho * (1.0f + 3.0f * eu + 4.5f * eu * eu - 1.5f * u2);
}

// ---------------------------------------------------------------------------
// 碰撞内核（BGK / TRT）：f -> fc，逐格点融合全部 19 个方向的平衡态 + 松弛
//   omega : omega- = 1/tau
//   omegaP: omega+ (仅 TRT 使用，由宿主按 tau 与 MagicLambda=1/4 预计算)
//   trt   : 0 = BGK, 非 0 = TRT
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_lbm_collide(
    const float* __restrict__ f, float* __restrict__ fc,
    const float* __restrict__ rho,
    const float* __restrict__ ux, const float* __restrict__ uy, const float* __restrict__ uz,
    float omega, float omegaP, int trt, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    float r  = rho[idx];
    float vx = ux[idx], vy = uy[idx], vz = uz[idx];
    const float* fx = f + idx;
    float* fcx = fc + idx;

    if (trt == 0) {
        for (int a = 0; a < 19; ++a) {
            float fe = lbfeq(a, r, vx, vy, vz);
            fcx[a * n] = (1.0f - omega) * fx[a * n] + omega * fe;
        }
    } else {
        float cA = 1.0f - 0.5f * (omegaP + omega);
        float cB = 0.5f * (omega - omegaP);
        float cC = 0.5f * (omegaP + omega);
        float cD = 0.5f * (omegaP - omega);
        for (int a = 0; a < 19; ++a) {
            int o = lbOpp[a];
            float fe  = lbfeq(a, r, vx, vy, vz);
            float feo = lbfeq(o, r, vx, vy, vz);
            fcx[a * n] = cA * fx[a * n] + cB * fx[o * n] + cC * fe + cD * feo;
        }
    }
}

// ---------------------------------------------------------------------------
// 流传播内核：fc -> f（gather），融合 halfway bounce-back、specular 自由滑移、
// 入口平衡态 / 出口零梯度 / 固体冻结，并就地写出矩量 (rho, ux, uy, uz)。
//   uwall : 顶盖运动壁速度（仅 btype=5 的固体拉取时产生 6*w*ex*uwall*rho 修正）
//   uinx/y/z : 入口 (btype=2) 平衡态速度
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_lbm_stream(
    const float* __restrict__ fc, float* __restrict__ f,
    float* __restrict__ rho,
    float* __restrict__ ux, float* __restrict__ uy, float* __restrict__ uz,
    const unsigned char* __restrict__ btype,
    float uwall, float uinx, float uiny, float uinz,
    int nx, int ny, int nz, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    int plane = ny * nz;
    int i = idx / plane;
    int rem = idx - i * plane;
    int j = rem / nz;
    int k = rem - j * nz;

    unsigned char bt = btype[idx];

    // ---- 固体（含运动顶盖）：冻结在静止平衡 f = w ----
    if (bt == 1 || bt == 5) {
        f[idx] = 1.0f / 3.0f;
        for (int a = 1; a < 19; ++a) {
            f[a * n + idx] = (a <= 6) ? (1.0f / 18.0f) : (1.0f / 36.0f);
        }
        rho[idx] = 1.0f;
        ux[idx] = 0.0f; uy[idx] = 0.0f; uz[idx] = 0.0f;
        return;
    }

    // ---- 入口：定常平衡态分布 ----
    if (bt == 2) {
        for (int a = 0; a < 19; ++a) {
            f[a * n + idx] = lbfeq(a, 1.0f, uinx, uiny, uinz);
        }
        rho[idx] = 1.0f;
        ux[idx] = uinx; uy[idx] = uiny; uz[idx] = uinz;
        return;
    }

    // ---- 出口：零梯度（复制 x-1 邻居的碰撞后分布）----
    if (bt == 3) {
        int src = idx - plane;
        float r = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
        for (int a = 0; a < 19; ++a) {
            float v = fc[a * n + src];
            f[a * n + idx] = v;
            r  += v;
            mx += v * (float)lbEX[a];
            my += v * (float)lbEY[a];
            mz += v * (float)lbEZ[a];
        }
        float inv = 1.0f / (r > 1.0e-9f ? r : 1.0e-9f);
        rho[idx] = r;
        ux[idx] = mx * inv; uy[idx] = my * inv; uz[idx] = mz * inv;
        return;
    }

    // ---- 流体：流传播 + 边界合成 ----
    for (int a = 0; a < 19; ++a) {
        int si = i - lbEX[a];
        int sj = j - lbEY[a];
        int sk = k - lbEZ[a];
        float v;

        if (si < 0 || si >= nx || sj < 0 || sj >= ny || sk < 0 || sk >= nz) {
            // 越界：顶 / 底 / 前后侧面走 specular 镜面自由滑移；
            // x 向越界在合法边界配置（入口/出口平面为 btype=2/3）下不应出现，兜底置零。
            if (sk < 0 || sk >= nz) {
                v = fc[lbZMirror[a] * n + idx];
            } else if (sj < 0 || sj >= ny) {
                v = fc[lbYMirror[a] * n + idx];
            } else {
                v = 0.0f;
            }
        } else {
            int sidx = (si * ny + sj) * nz + sk;
            unsigned char sb = btype[sidx];
            if (sb == 1 || sb == 5) {
                // halfway bounce-back（源格点为固体）
                v = fc[lbOpp[a] * n + idx];
                if (sb == 5) {
                    // 运动壁修正：+6 * w * (e_a . u_w) * rho，u_w = (uwall, 0, 0)
                    v += 6.0f * lbW[a] * (float)lbEX[a] * uwall * rho[idx];
                }
            } else {
                v = fc[a * n + sidx];
            }
        }

        f[a * n + idx] = v;
    }

    // ---- 矩量：rho = sum f, rho*u = sum f*e ----
    float r = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int a = 0; a < 19; ++a) {
        float v = f[a * n + idx];
        r  += v;
        mx += v * (float)lbEX[a];
        my += v * (float)lbEY[a];
        mz += v * (float)lbEZ[a];
    }
    float inv = 1.0f / (r > 1.0e-9f ? r : 1.0e-9f);
    rho[idx] = r;
    ux[idx] = mx * inv;
    uy[idx] = my * inv;
    uz[idx] = mz * inv;
}

// ---------------------------------------------------------------------------
// 矩量内核：从 f 就地重算 (rho, ux, uy, uz)（初始化 / 宿主侧边界修补后调用）
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_lbm_moments(
    const float* __restrict__ f, float* __restrict__ rho,
    float* __restrict__ ux, float* __restrict__ uy, float* __restrict__ uz,
    const unsigned char* __restrict__ btype, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    unsigned char bt = btype[idx];
    if (bt == 1 || bt == 5) {
        rho[idx] = 1.0f;
        ux[idx] = 0.0f; uy[idx] = 0.0f; uz[idx] = 0.0f;
        return;
    }
    if (bt == 2) {
        // 入口矩量由宿主写入 uinx 等，这里保持不动
        return;
    }

    float r = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int a = 0; a < 19; ++a) {
        float v = f[a * n + idx];
        r  += v;
        mx += v * (float)lbEX[a];
        my += v * (float)lbEY[a];
        mz += v * (float)lbEZ[a];
    }
    float inv = 1.0f / (r > 1.0e-9f ? r : 1.0e-9f);
    rho[idx] = r;
    ux[idx] = mx * inv;
    uy[idx] = my * inv;
    uz[idx] = mz * inv;
}

// ---------------------------------------------------------------------------
// 初始化内核：全域 f = f_eq(rho = 1, u = uin)
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_lbm_init_eq(
    float* __restrict__ f,
    float uinx, float uiny, float uinz, int n)
{
    int idx = blockDim.x * blockIdx.x + threadIdx.x;
    if (idx >= n) return;

    for (int a = 0; a < 19; ++a) {
        f[a * n + idx] = lbfeq(a, 1.0f, uinx, uiny, uinz);
    }
}
