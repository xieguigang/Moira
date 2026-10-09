// ---------------------------------------------------------------------------
// moira_sph_f32.cu
//
// SPHEngine 的 3D SPH float32 CUDA 内核源码（随 SPHEngine.dll 以嵌入资源分发，
// 由 CudaSphBackend 在运行时读取，经 ILCuda 的 NVRTC 即时编译）。
//
// 算法与 physics-netcore5 的 CPU 后端 SphCpuCompute3D **逐一对应**：
//   - 邻居遍历：网格边长 = 平滑半径 h，扫描 3x3x3 = 27 个相邻格
//   - 密度：rho = W(0) + Σ (h-r)^2 * k2      近密度：rhoN = W(0) + Σ (h-r)^3 * k3
//   - 压力：p = K * (rho/rest - 1)（下限 pMin），近压力 pn = KN * (rhoN/restN)
//   - 受力（对称 / 动量守恒）：
//       a_i = -m * Σ_j (p_i/rho_i^2 + p_j/rho_j^2) * ∇_i W_ij
//   - 粘性：归一化核加权速度平滑（系数已按 dt 限幅，保证不过冲）
//
// 数据布局（与 SphState3D 完全一致，全部为 float32 连续数组）：
//   qx/qy/qz 预测位置, vx/vy/vz 速度, dens/densNear, press/pressNear, ax/ay/az
// 网格（与 UniformGrid3D 完全一致）：
//   cellStart[cellCount+1] 前缀和 + entries[n] 粒子索引
//   cell = (cx * gny + cy) * gnz + cz，坐标 = floor((x - origin) / cellSize) 并夹取
//
// 说明：内核使用 grid-stride 循环，因此 LaunchPlanner.For1D 的 block 上限
// （MaxBlocks1D = 4096）不会影响可处理规模。
// ---------------------------------------------------------------------------

static __device__ inline int sph_cell_coord(float x, float origin, float cellSize, int n) {
    int c = (int)floorf((x - origin) / cellSize);
    if (c < 0) c = 0;
    if (c >= n) c = n - 1;
    return c;
}

// ---------------------------------------------------------------------------
// 密度趟：density / near density
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_sph_density(
    const float* __restrict__ qx, const float* __restrict__ qy, const float* __restrict__ qz,
    const int* __restrict__ cellStart, const int* __restrict__ entries,
    int gnx, int gny, int gnz,
    float ox, float oy, float oz, float cellSize,
    float h, float k2, float k3, float selfD, float selfN,
    float* dens, float* densNear, int n)
{
    int stride = gridDim.x * blockDim.x;
    float h2 = h * h;

    for (int i = blockDim.x * blockIdx.x + threadIdx.x; i < n; i += stride) {
        float xi = qx[i], yi = qy[i], zi = qz[i];
        float d = selfD;
        float dn = selfN;

        int cx = sph_cell_coord(xi, ox, cellSize, gnx);
        int cy = sph_cell_coord(yi, oy, cellSize, gny);
        int cz = sph_cell_coord(zi, oz, cellSize, gnz);

        for (int ddx = -1; ddx <= 1; ++ddx) {
            int ax = cx + ddx;
            if (ax < 0 || ax >= gnx) continue;
            for (int ddy = -1; ddy <= 1; ++ddy) {
                int ay = cy + ddy;
                if (ay < 0 || ay >= gny) continue;
                for (int ddz = -1; ddz <= 1; ++ddz) {
                    int az = cz + ddz;
                    if (az < 0 || az >= gnz) continue;

                    int c = (ax * gny + ay) * gnz + az;
                    int s0 = cellStart[c];
                    int e0 = cellStart[c + 1];

                    for (int t = s0; t < e0; ++t) {
                        int j = entries[t];
                        if (j == i) continue;

                        float rx = qx[j] - xi;
                        float ry = qy[j] - yi;
                        float rz = qz[j] - zi;
                        float r2 = rx * rx + ry * ry + rz * rz;

                        if (r2 >= h2) continue;

                        float u = h - sqrtf(r2);
                        d  += u * u * k2;
                        dn += u * u * u * k3;
                    }
                }
            }
        }

        dens[i] = d;
        densNear[i] = dn;
    }
}

// ---------------------------------------------------------------------------
// 受力趟：对称压力力 + 归一化粘性 -> ax/ay/az
// ---------------------------------------------------------------------------
extern "C" __global__ void moira_sph_force(
    const float* __restrict__ qx, const float* __restrict__ qy, const float* __restrict__ qz,
    const float* __restrict__ vx, const float* __restrict__ vy, const float* __restrict__ vz,
    const float* __restrict__ dens, const float* __restrict__ densNear,
    const int* __restrict__ cellStart, const int* __restrict__ entries,
    int gnx, int gny, int gnz,
    float ox, float oy, float oz, float cellSize,
    float h, float restD, float restND, float vol,
    float minR, float maxR, float pMin,
    float K, float KN, float g2k, float g3k, float poly6, float visScale, float maxA,
    float* press, float* pressNear,
    float* ax, float* ay, float* az, int n)
{
    int stride = gridDim.x * blockDim.x;
    float h2 = h * h;

    for (int i = blockDim.x * blockIdx.x + threadIdx.x; i < n; i += stride) {
        float xi = qx[i], yi = qy[i], zi = qz[i];
        float vxi = vx[i], vyi = vy[i], vzi = vz[i];

        float rhoI  = fminf(fmaxf(dens[i]  / restD,  minR), maxR);
        float rhoNI = fminf(fmaxf(densNear[i] / restND, minR), maxR);

        float pI = K * (rhoI - 1.0f);
        if (pI < pMin) pI = pMin;
        // 近压力只作"抗团聚"修正：以静止近密度为参考并截断到非负
        float pnI = KN * (rhoNI - 1.0f);
        if (pnI < 0.0f) pnI = 0.0f;

        press[i] = pI;
        pressNear[i] = pnI;

        float cI  = pI  / (rhoI  * rhoI);
        float cnI = pnI / (rhoNI * rhoNI);

        float fx = 0.0f, fy = 0.0f, fz = 0.0f;
        float dvx = 0.0f, dvy = 0.0f, dvz = 0.0f;

        int cx = sph_cell_coord(xi, ox, cellSize, gnx);
        int cy = sph_cell_coord(yi, oy, cellSize, gny);
        int cz = sph_cell_coord(zi, oz, cellSize, gnz);

        for (int ddx = -1; ddx <= 1; ++ddx) {
            int axc = cx + ddx;
            if (axc < 0 || axc >= gnx) continue;
            for (int ddy = -1; ddy <= 1; ++ddy) {
                int ayc = cy + ddy;
                if (ayc < 0 || ayc >= gny) continue;
                for (int ddz = -1; ddz <= 1; ++ddz) {
                    int azc = cz + ddz;
                    if (azc < 0 || azc >= gnz) continue;

                    int c = (axc * gny + ayc) * gnz + azc;
                    int s0 = cellStart[c];
                    int e0 = cellStart[c + 1];

                    for (int t = s0; t < e0; ++t) {
                        int j = entries[t];
                        if (j == i) continue;

                        float rx = qx[j] - xi;
                        float ry = qy[j] - yi;
                        float rz = qz[j] - zi;
                        float r2 = rx * rx + ry * ry + rz * rz;

                        if (r2 >= h2) continue;

                        float r = sqrtf(r2);
                        if (r < 1.0e-7f) continue;

                        float inv = 1.0f / r;
                        float dx = rx * inv, dy = ry * inv, dz = rz * inv;
                        float u = h - r;

                        float rhoJ  = fminf(fmaxf(dens[j]  / restD,  minR), maxR);
                        float rhoNJ = fminf(fmaxf(densNear[j] / restND, minR), maxR);

                        float pJ = K * (rhoJ - 1.0f);
                        if (pJ < pMin) pJ = pMin;
                        float pnJ = KN * (rhoNJ - 1.0f);
                        if (pnJ < 0.0f) pnJ = 0.0f;

                        float coef  = vol * (cI  + pJ  / (rhoJ  * rhoJ));
                        float coefN = vol * (cnI + pnJ / (rhoNJ * rhoNJ));
                        float g2 = u * g2k;
                        float g3 = u * u * g3k;
                        float f = coef * g2 + coefN * g3;

                        fx -= f * dx;
                        fy -= f * dy;
                        fz -= f * dz;

                        if (visScale != 0.0f) {
                            float v = h2 - r2;
                            float wgt = v * v * v * poly6 / rhoJ;
                            dvx += (vx[j] - vxi) * wgt;
                            dvy += (vy[j] - vyi) * wgt;
                            dvz += (vz[j] - vzi) * wgt;
                        }
                    }
                }
            }
        }

        fx += visScale * dvx;
        fy += visScale * dvy;
        fz += visScale * dvz;

        if (maxA > 0.0f) {
            float m2 = fx * fx + fy * fy + fz * fz;
            if (m2 > maxA * maxA) {
                float s = maxA / sqrtf(m2);
                fx *= s; fy *= s; fz *= s;
            }
        }

        ax[i] = fx;
        ay[i] = fy;
        az[i] = fz;
    }
}
