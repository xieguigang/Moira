// vtkParser.worker.js —— Web Worker：高效解析 .vtk (STRUCTURED_POINTS)
// 协议：主线程发送 { files: File[], timesteps: number[] }
//       回传 { type:'progress', done, total } / { type:'done', meta, timesteps, arrays } / { type:'error', message }

const NUM_RE = /-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?/g;

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

self.onmessage = async (e) => {
  const { files, timesteps } = e.data;
  const nFrames = files.length;
  const big = { pressure: null, density: null, u: null, v: null, w: null, speed: null };
  let meta = null;
  try {
    for (let f = 0; f < nFrames; f++) {
      const text = await files[f].text();
      const res = parseVTK(text);
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
      self.postMessage({ type: 'progress', done: f + 1, total: nFrames });
    }
    const transfer = Object.values(big).map((a) => a.buffer);
    self.postMessage({ type: 'done', meta, timesteps, arrays: big }, transfer);
  } catch (err) {
    self.postMessage({ type: 'error', message: String((err && err.message) || err) });
  }
};
