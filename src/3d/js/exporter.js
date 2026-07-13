// exporter.js
// 组装 CFD 体素 JSON 并提供 Blob 下载。

/**
 * 构建 CFD 体素网格 JSON 对象。
 * @param {object} vox voxelize 返回值 { data:Uint8Array, dims, voxelSize, origin, solidCount }
 * @param {object} meta { sourceModel, sourceFormat, mode }
 */
export function buildVoxelJson(vox, meta) {
  const [width, height, depth] = vox.dims;
  const [ox, oy, oz] = vox.origin;
  const [sx, sy, sz] = vox.voxelSize;
  const total = width * height * depth;

  // Uint8Array -> 普通数组（integer）
  const data = Array.from(vox.data);

  const max = [ox + sx * width, oy + sy * height, oz + sz * depth];

  return {
    schema: 'Moira.CFD.VoxelGrid/v1',
    generatedAt: new Date().toISOString(),
    sourceModel: meta.sourceModel || 'unknown',
    sourceFormat: meta.sourceFormat || '',
    mode: meta.mode || 'solid', // 'solid' (网格实心) | 'occupancy' (点云占据)
    grid: { width, height, depth, voxelCount: total },
    bounds: {
      min: [ox, oy, oz],
      max,
      size: [sx * width, sy * height, sz * depth],
      voxelSize: [sx, sy, sz],
    },
    coordinateSystem: 'right-handed, Y-up (three.js world)',
    indexOrder: 'x-major, then y, then z',
    indexFormula: 'index = (x * height + y) * depth + z',
    encoding: 'raw',
    dataType: 'int32',
    valueSemantics: {
      '0': 'fluid/exterior space (no computation data)',
      '1': 'solid region (treated as solid boundary in CFD)',
    },
    solidVoxelCount: vox.solidCount,
    fluidVoxelCount: total - vox.solidCount,
    data,
  };
}

/**
 * 触发浏览器下载 JSON 文件。
 */
export function downloadJson(jsonObj, filename) {
  const text = JSON.stringify(jsonObj);
  const blob = new Blob([text], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function suggestFilename(sourceModel, dims) {
  const base = (sourceModel || 'model').replace(/\.[^.]+$/, '').replace(/[^\w\-]+/g, '_');
  const d = dims ? `_${dims[0]}x${dims[1]}x${dims[2]}` : '';
  return `${base}${d}_voxels.json`;
}
