// voxelizer.js
// 网格实心体素化（Z 轴射线奇偶 + XY 列候选三角过滤）与点云占据体素化。
// 索引公式: index = (x * height + y) * depth + z
// 数值语义: 0 = 空/外部空间, 1 = 固体区域

/**
 * 根据包围盒与分辨率（最长边体素数）计算三轴维度与体素尺寸。
 */
export function computeGridDims(bbox, resolution) {
  const size = {
    x: Math.max(bbox.max.x - bbox.min.x, 1e-9),
    y: Math.max(bbox.max.y - bbox.min.y, 1e-9),
    z: Math.max(bbox.max.z - bbox.min.z, 1e-9),
  };
  const maxDim = Math.max(size.x, size.y, size.z);
  const voxel = maxDim / resolution; // 立方体素边长

  const width = Math.max(1, Math.ceil(size.x / voxel));
  const height = Math.max(1, Math.ceil(size.y / voxel));
  const depth = Math.max(1, Math.ceil(size.z / voxel));

  return {
    width, height, depth,
    voxelSize: [voxel, voxel, voxel],
    origin: [bbox.min.x, bbox.min.y, bbox.min.z],
  };
}

/**
 * 网格实心体素化。
 * @param {Float32Array} triangles  每三角形 9 float
 * @param {THREE.Box3} bbox
 * @param {number} resolution
 * @param {(p:number,msg:string)=>void} onProgress
 * @returns {{data:Uint8Array, dims:number[], voxelSize:number[], origin:number[], solidCount:number}}
 */
export function voxelizeMesh(triangles, bbox, resolution, onProgress) {
  const { width, height, depth, voxelSize, origin } = computeGridDims(bbox, resolution);
  const [dx, dy, dz] = voxelSize;
  const [ox, oy, oz] = origin;
  const data = new Uint8Array(width * height * depth);

  const triCount = triangles.length / 9;

  // 将三角形按其覆盖的 XY 列区间进行分桶，加速逐列求交。
  // 桶键: cx * height + cy
  const buckets = new Map();
  const addToBucket = (key, triIdx) => {
    let arr = buckets.get(key);
    if (!arr) { arr = []; buckets.set(key, arr); }
    arr.push(triIdx);
  };

  for (let t = 0; t < triCount; t++) {
    const o = t * 9;
    const x0 = triangles[o], y0 = triangles[o + 1];
    const x1 = triangles[o + 3], y1 = triangles[o + 4];
    const x2 = triangles[o + 6], y2 = triangles[o + 7];

    const minX = Math.min(x0, x1, x2), maxX = Math.max(x0, x1, x2);
    const minY = Math.min(y0, y1, y2), maxY = Math.max(y0, y1, y2);

    // 覆盖的列索引范围（列中心落入区间）
    let cxMin = Math.floor((minX - ox) / dx - 0.5);
    let cxMax = Math.ceil((maxX - ox) / dx + 0.5);
    let cyMin = Math.floor((minY - oy) / dy - 0.5);
    let cyMax = Math.ceil((maxY - oy) / dy + 0.5);
    cxMin = Math.max(0, cxMin); cxMax = Math.min(width - 1, cxMax);
    cyMin = Math.max(0, cyMin); cyMax = Math.min(height - 1, cyMax);

    for (let cx = cxMin; cx <= cxMax; cx++) {
      for (let cy = cyMin; cy <= cyMax; cy++) {
        addToBucket(cx * height + cy, t);
      }
    }
  }

  let solidCount = 0;
  const zHits = [];

  for (let x = 0; x < width; x++) {
    const wx = ox + (x + 0.5) * dx;
    for (let y = 0; y < height; y++) {
      const wy = oy + (y + 0.5) * dy;
      const bucket = buckets.get(x * height + y);
      if (!bucket || bucket.length === 0) continue;

      zHits.length = 0;
      for (let k = 0; k < bucket.length; k++) {
        const o = bucket[k] * 9;
        const z = rayZIntersect(triangles, o, wx, wy);
        if (z !== null) zHits.push(z);
      }
      if (zHits.length < 2) continue;
      zHits.sort((a, b) => a - b);

      // 对每个体素中心 z，统计其前方交点数量的奇偶
      const base = (x * height + y) * depth;
      for (let z = 0; z < depth; z++) {
        const wz = oz + (z + 0.5) * dz;
        // 统计 < wz 的交点数（用二分）
        const cnt = lowerBoundCount(zHits, wz);
        if (cnt & 1) {
          data[base + z] = 1;
          solidCount++;
        }
      }
    }
    if (onProgress && (x % Math.max(1, (width >> 5)) === 0)) {
      onProgress((x + 1) / width, `体素化 ${x + 1}/${width} 列`);
    }
  }

  onProgress?.(1, '体素化完成');
  return { data, dims: [width, height, depth], voxelSize, origin, solidCount };
}

/**
 * 点云占据体素化：每个点所在体素标记为 1。
 */
export function voxelizePoints(points, bbox, resolution, onProgress) {
  const { width, height, depth, voxelSize, origin } = computeGridDims(bbox, resolution);
  const [dx, dy, dz] = voxelSize;
  const [ox, oy, oz] = origin;
  const data = new Uint8Array(width * height * depth);
  const n = points.length / 3;
  let solidCount = 0;

  for (let i = 0; i < n; i++) {
    const px = points[i * 3], py = points[i * 3 + 1], pz = points[i * 3 + 2];
    let x = Math.floor((px - ox) / dx);
    let y = Math.floor((py - oy) / dy);
    let z = Math.floor((pz - oz) / dz);
    if (x >= width) x = width - 1; if (x < 0) x = 0;
    if (y >= height) y = height - 1; if (y < 0) y = 0;
    if (z >= depth) z = depth - 1; if (z < 0) z = 0;
    const idx = (x * height + y) * depth + z;
    if (data[idx] === 0) { data[idx] = 1; solidCount++; }

    if (onProgress && (i % Math.max(1, (n / 50 | 0)) === 0)) {
      onProgress((i + 1) / n, `体素化点云 ${i + 1}/${n}`);
    }
  }

  onProgress?.(1, '体素化完成');
  return { data, dims: [width, height, depth], voxelSize, origin, solidCount };
}

// 轴对齐射线（沿 +Z）与三角形求交，返回交点 z（若 (wx,wy) 在三角形 XY 投影内），否则 null。
function rayZIntersect(tri, o, wx, wy) {
  const ax = tri[o],     ay = tri[o + 1],  az = tri[o + 2];
  const bx = tri[o + 3], by = tri[o + 4],  bz = tri[o + 5];
  const cx = tri[o + 6], cy = tri[o + 7],  cz = tri[o + 8];

  // 重心坐标 (在 XY 平面)
  const v0x = bx - ax, v0y = by - ay;
  const v1x = cx - ax, v1y = cy - ay;
  const v2x = wx - ax, v2y = wy - ay;

  const den = v0x * v1y - v1x * v0y;
  if (Math.abs(den) < 1e-12) return null; // 退化/竖直三角形

  const inv = 1 / den;
  const u = (v2x * v1y - v1x * v2y) * inv;
  const v = (v0x * v2y - v2x * v0y) * inv;
  const w = 1 - u - v;

  const eps = -1e-9;
  if (u < eps || v < eps || w < eps) return null;

  return w * az + u * bz + v * cz;
}

// 统计 sorted 中 < value 的元素个数
function lowerBoundCount(sorted, value) {
  let lo = 0, hi = sorted.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (sorted[mid] < value) lo = mid + 1; else hi = mid;
  }
  return lo;
}
