// vtkParser.js —— 主线程解析 pvd / vtk（不使用 Web Worker，逐帧让出以实时显示进度）

import { t } from './i18n.js';

const FIELDS = ['pressure', 'density', 'u', 'v', 'w', 'speed'];
const NUM_RE = /-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?/g;

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

function parseVTK(text) {
  const dim = text.match(/DIMENSIONS\s+(\d+)\s+(\d+)\s+(\d+)/);
  if (!dim) throw new Error('DIMENSIONS not found');
  const nx = +dim[1], ny = +dim[2], nz = +dim[3];
  const N = nx * ny * nz;

  const pIdx = text.indexOf('POINT_DATA');
  if (pIdx < 0) throw new Error('POINT_DATA not found');
  // 跳过 "POINT_DATA <count>" 所在的行，避免把 count 当作数据
  const nl = text.indexOf('\n', pIdx);
  const data = text.slice(nl + 1);
  // 去除 SCALARS 行尾的 numComponents（如 "double 1"），否则会被当作额外数据点
  const cleaned = data.replace(/SCALARS\s+\w+\s+double\s+1/g, 'SCALARS');

  const tokens = cleaned.match(NUM_RE);
  if (!tokens) throw new Error('no numeric data');

  const fields = {
    pressure: new Float32Array(N),
    density: new Float32Array(N),
    u: new Float32Array(N),
    v: new Float32Array(N),
    w: new Float32Array(N),
    speed: new Float32Array(N)
  };
  const seg = N; // 每个标量块的体素数
  for (let m = 0; m < tokens.length; m++) {
    const val = parseFloat(tokens[m]);
    if (m < seg) fields.pressure[m] = val;
    else if (m < 2 * seg) fields.density[m - seg] = val;
    else if (m < 3 * seg) fields.u[m - 2 * seg] = val;
    else if (m < 4 * seg) fields.v[m - 3 * seg] = val;
    else if (m < 5 * seg) fields.w[m - 4 * seg] = val;
    else if (m < 6 * seg) fields.speed[m - 5 * seg] = val;
    // m >= 6*seg 为 velocity 向量分量，已包含于 u/v/w，忽略
  }
  return { nx, ny, nz, origin: [0, 0, 0], spacing: [1, 1, 1], fields };
}

// 让出主线程，使浏览器能渲染进度 UI
const nextTick = () => new Promise((r) => setTimeout(r, 0));

/**
 * 从上传的文件列表加载数据集（主线程解析，逐帧报告进度）。
 * @param {FileList|File[]} fileList
 * @param {{onProgress?: (info:{phase:string,done:number,total:number})=>void}} opts
 * @returns {Promise<{nx,ny,nz,origin,spacing,timesteps,nFrames,fields,arrays}>}
 */
export async function loadDatasetFromFiles(fileList, opts = {}) {
  const onProgress = opts.onProgress || (() => {});
  const files = Array.from(fileList);
  const byName = new Map();
  for (const f of files) byName.set(f.name, f);

  // 查找 pvd（优先 animation.pvd）
  let pvdFile = byName.get('animation.pvd');
  if (!pvdFile) pvdFile = files.find((f) => f.name.toLowerCase().endsWith('.pvd'));
  if (!pvdFile) throw new Error(t('err.noPvd'));

  const txt = await pvdFile.text();
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

  const nFrames = ordered.length;
  const big = { pressure: null, density: null, u: null, v: null, w: null, speed: null };
  let meta = null;

  for (let f = 0; f < nFrames; f++) {
    const text = await ordered[f].text();
    let res;
    try {
      res = parseVTK(text);
    } catch (err) {
      throw new Error(t('err.parse', { n: f, msg: err.message || String(err) }));
    }
    if (!meta) meta = { nx: res.nx, ny: res.ny, nz: res.nz, origin: res.origin, spacing: res.spacing };
    const N = res.nx * res.ny * res.nz;
    if (f === 0) {
      for (const k in big) big[k] = new Float32Array(N * nFrames);
    }
    const base = f * N;
    big.pressure.set(res.fields.pressure, base);
    big.density.set(res.fields.density, base);
    big.u.set(res.fields.u, base);
    big.v.set(res.fields.v, base);
    big.w.set(res.fields.w, base);
    big.speed.set(res.fields.speed, base);

    // 报告本帧加载进度并让出主线程，确保进度条与文本实时刷新
    onProgress({ phase: 'load', done: f + 1, total: nFrames });
    await nextTick();
  }

  return {
    nx: meta.nx, ny: meta.ny, nz: meta.nz,
    origin: meta.origin, spacing: meta.spacing,
    timesteps: resolvedTs, nFrames,
    fields: FIELDS, arrays: big
  };
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
