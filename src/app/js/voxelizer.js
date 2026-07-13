// voxelizer.js
// 泛洪填充体素化：表面采样标记 + BFS 泛洪区分内外。
// 索引公式: index = (x * height + y) * depth + z
// 数值语义: 0 = 空/外部空间, 1 = 固体区域

/**
 * 根据模型几何自动计算网格维度与体素尺寸。
 * 分辨率基于三角形数量自动确定，无需手动设置降采样参数。
 * 体素尺寸 = 模型最长边 / 分辨率，分辨率 = sqrt(三角形数) * 3.5 并夹在 [128, 768]。
 * 网格外扩 1 个体素作为边距，保证泛洪填充有外部起点。
 */
export function autoComputeGridDims(bbox, triangles) {
  const size = {
    x: Math.max(bbox.max.x - bbox.min.x, 1e-9),
    y: Math.max(bbox.max.y - bbox.min.y, 1e-9),
    z: Math.max(bbox.max.z - bbox.min.z, 1e-9),
  };
  const maxDim = Math.max(size.x, size.y, size.z);
  const triCount = Math.max(1, triangles.length / 9);

  // 分辨率基于模型复杂度自动确定
  let resolution = Math.min(768, Math.max(128, Math.ceil(Math.sqrt(triCount) * 3.5)));

  let voxelSize = maxDim / resolution;
  let width  = Math.max(1, Math.ceil(size.x / voxelSize)) + 2;
  let height = Math.max(1, Math.ceil(size.y / voxelSize)) + 2;
  let depth  = Math.max(1, Math.ceil(size.z / voxelSize)) + 2;

  // 总 voxel 数上限 ≈ 64M，防止浏览器内存溢出
  const MAX_VOXELS = 64_000_000;
  while (width * height * depth > MAX_VOXELS && resolution > 64) {
    resolution = Math.max(64, Math.floor(resolution * 0.8));
    voxelSize = maxDim / resolution;
    width  = Math.max(3, Math.ceil(size.x / voxelSize) + 2);
    height = Math.max(3, Math.ceil(size.y / voxelSize) + 2);
    depth  = Math.max(3, Math.ceil(size.z / voxelSize) + 2);
  }

  return {
    width, height, depth,
    voxelSize: [voxelSize, voxelSize, voxelSize],
    origin: [bbox.min.x - voxelSize, bbox.min.y - voxelSize, bbox.min.z - voxelSize],
  };
}

/**
 * 网格泛洪填充体素化（用于 CFD 计算）。
 *
 * 算法流程：
 *   1. 对每个三角形在表面均匀采样，将命中的体素标记为 1（表面/固体）。
 *   2. 从 (0,0,0) 出发做 6-邻接 BFS 泛洪，被访问到的空体素标记为 2（外部已访问）。
 *   3. 最终：2→0（外部空间），其余→1（内部固体+表面）。
 *
 * @param {Float32Array} triangles - 每三角形 9 float (3顶点 × 3坐标)
 * @param {THREE.Box3} bbox - 模型世界坐标包围盒
 * @param {(p:number, msg:string)=>void} onProgress - 进度回调
 * @returns {{data: Uint8Array, dims: number[], voxelSize: number[], origin: number[], solidCount: number}}
 */
export function voxelizeMesh(triangles, bbox, onProgress) {
  const dims = autoComputeGridDims(bbox, triangles);
  return voxelizeMeshFloodFill(triangles, bbox, dims, onProgress);
}

/**
 * 使用给定的网格维度（由 autoComputeGridDims 产生）做泛洪填充体素化。
 */
export function voxelizeMeshFloodFill(triangles, bbox, dims, onProgress) {
  const { width: W, height: H, depth: D, voxelSize, origin } = dims;
  const [dx, dy, dz] = voxelSize;
  const [ox, oy, oz] = origin;
  const total = W * H * D;
  const data = new Uint8Array(total);
  const triCount = Math.max(1, triangles.length / 9);

  onProgress?.(0, '采样模型表面…');

  // ========== 阶段 1：三角形表面采样，标记表面体素 ==========
  // 采样密度 ≈ 4 个点 / 体素截面积，确保相邻三角形表面在水密网格上不留缝
  const sampleDensity = 4.0 / (dx * dy);

  for (let t = 0; t < triCount; t++) {
    const bo = t * 9;
    const v0x = triangles[bo],     v0y = triangles[bo + 1], v0z = triangles[bo + 2];
    const v1x = triangles[bo + 3], v1y = triangles[bo + 4], v1z = triangles[bo + 5];
    const v2x = triangles[bo + 6], v2y = triangles[bo + 7], v2z = triangles[bo + 8];

    // 边向量
    const e1x = v1x - v0x, e1y = v1y - v0y, e1z = v1z - v0z;
    const e2x = v2x - v0x, e2y = v2y - v0y, e2z = v2z - v0z;

    // 三角形面积
    const nx = e1y * e2z - e1z * e2y;
    const ny = e1z * e2x - e1x * e2z;
    const nz = e1x * e2y - e1y * e2x;
    const area = 0.5 * Math.sqrt(nx * nx + ny * ny + nz * nz);

    const nSamples = Math.max(1, Math.ceil(area * sampleDensity));
    const rows = Math.ceil(Math.sqrt(nSamples * 2));

    // 在三角形内部均匀采样 barycentric (u, v)，u ≥ 0, v ≥ 0, u+v ≤ 1
    for (let i = 0; i <= rows; i++) {
      for (let j = 0; j <= rows - i; j++) {
        const u = i / rows;
        const v = j / rows;

        const px = v0x + u * e1x + v * e2x;
        const py = v0y + u * e1y + v * e2y;
        const pz = v0z + u * e1z + v * e2z;

        const ix = Math.floor((px - ox) / dx);
        const iy = Math.floor((py - oy) / dy);
        const iz = Math.floor((pz - oz) / dz);

        if (ix >= 0 && ix < W && iy >= 0 && iy < H && iz >= 0 && iz < D) {
          data[(ix * H + iy) * D + iz] = 1;
        }
      }
    }

    if (t % Math.max(1, Math.floor(triCount / 40)) === 0) {
      onProgress?.(0.05 + (t / triCount) * 0.35, `表面采样 ${Math.round(t / triCount * 100)}%`);
    }
  }

  onProgress?.(0.4, '泛洪填充识别内部区域…');

  // ========== 阶段 2：BFS 泛洪填充外部 ==========
  // 使用普通数组 + head 指针（避免 shift O(n)），最大 frontier ≈ O(n²) 可接受
  const HxD = H * D;
  const queue = [];
  let head = 0;

  // (0,0,0) 位于 padding 区，必定是模型外部
  if (data[0] === 0) {
    data[0] = 2;
    queue.push(0);
  }

  let lastReport = 0;
  const reportInterval = Math.max(1, Math.floor(total * 0.02));

  while (head < queue.length) {
    const idx = queue[head++];
    const z = idx % D;
    const rem = (idx / D) | 0;
    const y = rem % H;
    const x = (rem / H) | 0;

    // 6-邻接
    if (x > 0)     { const n = idx - HxD;   if (data[n] === 0) { data[n] = 2; queue.push(n); } }
    if (x < W - 1) { const n = idx + HxD;   if (data[n] === 0) { data[n] = 2; queue.push(n); } }
    if (y > 0)     { const n = idx - D;     if (data[n] === 0) { data[n] = 2; queue.push(n); } }
    if (y < H - 1) { const n = idx + D;     if (data[n] === 0) { data[n] = 2; queue.push(n); } }
    if (z > 0)     { const n = idx - 1;     if (data[n] === 0) { data[n] = 2; queue.push(n); } }
    if (z < D - 1) { const n = idx + 1;     if (data[n] === 0) { data[n] = 2; queue.push(n); } }

    if (head - lastReport > reportInterval) {
      lastReport = head;
      // 进度 40% → 50%
      onProgress?.(0.4 + Math.min(0.1, (head / total) * 0.1), `泛洪填充 ${Math.round(head / total * 100)}%`);
    }
  }

  onProgress?.(0.55, '整理结果…');

  // ========== 阶段 3：结果整理 ==========
  // 2 → 0（外部空）, 0 → 1（内部固体）, 1 → 1（表面固体）
  let solidCount = 0;
  for (let i = 0; i < total; i++) {
    if (data[i] === 2) {
      data[i] = 0;
    } else {
      data[i] = 1;
      solidCount++;
    }
  }

  onProgress?.(1, '体素化完成');

  return {
    data,
    dims: [W, H, D],
    voxelSize,
    origin,
    solidCount,
  };
}

/**
 * 点云占据体素化：每个点所在体素标记为 1。
 * 点云直接落在格子上，无需泛洪填充。
 */
export function voxelizePoints(points, bbox, onProgress) {
  const triCount = Math.max(1, points.length / 3);
  const dims = autoComputeGridDims(bbox, new Float32Array(Math.min(9, points.length)));
  const { width: W, height: H, depth: D, voxelSize, origin } = dims;
  const [dx, dy, dz] = voxelSize;
  const [ox, oy, oz] = origin;
  const data = new Uint8Array(W * H * D);
  const n = points.length / 3;
  let solidCount = 0;

  for (let i = 0; i < n; i++) {
    const px = points[i * 3], py = points[i * 3 + 1], pz = points[i * 3 + 2];
    let x = Math.floor((px - ox) / dx);
    let y = Math.floor((py - oy) / dy);
    let z = Math.floor((pz - oz) / dz);
    if (x >= W) x = W - 1; if (x < 0) x = 0;
    if (y >= H) y = H - 1; if (y < 0) y = 0;
    if (z >= D) z = D - 1; if (z < 0) z = 0;
    const idx = (x * H + y) * D + z;
    if (data[idx] === 0) { data[idx] = 1; solidCount++; }

    if (onProgress && (i % Math.max(1, (n / 50 | 0)) === 0)) {
      onProgress((i + 1) / n, `体素化点云 ${i + 1}/${n}`);
    }
  }

  onProgress?.(1, '体素化完成');
  return { data, dims: [W, H, D], voxelSize, origin, solidCount };
}
