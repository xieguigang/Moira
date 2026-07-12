// vtkParser.js —— 主线程与 Worker 的接口：解析 pvd / 协调 Worker / 提供按帧取数

import { t } from './i18n.js';

const FIELDS = ['pressure', 'density', 'u', 'v', 'w', 'speed'];

export function parsePVD(text) {
  const frameFiles = [];
  const timesteps = [];
  const re = /<DataSet[^>]*timestep="([^"]+)"[^>]*file="([^"]+)"[^>]*\/?>/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    timesteps.push(parseFloat(m[1]));
    frameFiles.push(m[2]);
  }
  return { frameFiles, timesteps };
}

/**
 * 从上传的文件列表加载数据集。
 * @param {FileList|File[]} fileList
 * @param {{onProgress?: (done:number,total:number)=>void}} opts
 * @returns {Promise<{nx,ny,nz,origin,spacing,timesteps,nFrames,fields,arrays}>}
 */
export function loadDatasetFromFiles(fileList, opts = {}) {
  const files = Array.from(fileList);
  const byName = new Map();
  for (const f of files) byName.set(f.name, f);

  // 查找 pvd（优先 animation.pvd）
  let pvdFile = byName.get('animation.pvd');
  if (!pvdFile) pvdFile = files.find((f) => f.name.toLowerCase().endsWith('.pvd'));
  if (!pvdFile) throw new Error(t('err.noPvd'));

  const pvdText = pvdFile.text ? '' : '';
  return pvdFile.text().then((txt) => {
    let { frameFiles, timesteps } = parsePVD(txt);
    if (!frameFiles.length) {
      // 退化：直接用所有 frame_*.vtk 并按名排序
      frameFiles = files.filter((f) => /^frame_.*\.vtk$/i.test(f.name)).map((f) => f.name);
      frameFiles.sort();
      timesteps = frameFiles.map((_, i) => i);
    }
    if (!frameFiles.length) throw new Error(t('err.noVtk'));

    const ordered = [];
    const resolvedTs = [];
    for (let i = 0; i < frameFiles.length; i++) {
      const f = byName.get(frameFiles[i]) || files.find((x) => x.name === frameFiles[i]);
      if (f) { ordered.push(f); resolvedTs.push(timesteps[i]); }
    }
    if (!ordered.length) throw new Error(t('err.noVtk'));

    return new Promise((resolve, reject) => {
      const worker = new Worker(new URL('./vtkParser.worker.js', import.meta.url), { type: 'module' });
      worker.onmessage = (ev) => {
        const d = ev.data;
        if (d.type === 'progress') {
          opts.onProgress && opts.onProgress(d.done, d.total);
        } else if (d.type === 'done') {
          worker.terminate();
          resolve({
            nx: d.meta.nx, ny: d.meta.ny, nz: d.meta.nz,
            origin: d.meta.origin, spacing: d.meta.spacing,
            timesteps: d.timesteps, nFrames: ordered.length,
            fields: FIELDS, arrays: d.arrays
          });
        } else if (d.type === 'error') {
          worker.terminate();
          reject(new Error(d.message));
        }
      };
      worker.onerror = (err) => { worker.terminate(); reject(new Error(err.message || 'worker error')); };
      worker.postMessage({ files: ordered, timesteps: resolvedTs });
    });
  });
}

// 取某帧某场量的 Float32Array 切片（长度 = N）
export function getFrameField(ds, field, frame) {
  const N = ds.nx * ds.ny * ds.nz;
  const arr = ds.arrays[field];
  return arr.subarray(frame * N, frame * N + N);
}

// 取某体素（线性索引 idx）某场量在所有帧的时间序列
export function getVoxelSeries(ds, field, idx) {
  const N = ds.nx * ds.ny * ds.nz;
  const arr = ds.arrays[field];
  const out = new Float32Array(ds.nFrames);
  for (let f = 0; f < ds.nFrames; f++) out[f] = arr[idx + f * N];
  return out;
}

// 线性索引 → (i,j,k)
export function idxToIJK(idx, nx, ny) {
  const i = idx % nx;
  const j = Math.floor(idx / nx) % ny;
  const k = Math.floor(idx / (nx * ny));
  return [i, j, k];
}

// (i,j,k) → 线性索引
export function ijkToIdx(i, j, k, nx, ny) {
  return i + j * nx + k * nx * ny;
}
