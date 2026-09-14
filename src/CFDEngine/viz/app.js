/**
 * CFDEngine 仿真结果查看器 —— 基于 VTK.js 的浏览器端可视化
 *
 * 设计要点：
 *  1. VTK.js v37 是纯 ESM 包，浏览器无法直接解析裸模块名，
 *     因此用 index.html 里的 importmap 把裸模块名映射到 CDN，
 *     让浏览器按原始单文件 ESM 逐个加载（保证 vtk 类注册表只有一份）。
 *     加载失败时在页面顶部给出本地 npm 兜底指引，而不是白屏。
 *  2. 不一次性加载整个时间序列：先读 frames.json 拿到帧清单，
 *     再按当前帧 fetch 单个 .vti（约 5.5 MB），内存里永远只有一帧。
 *  3. 流线由本文件自己积分（RK2）生成 vtkPolyData，
 *     不依赖 vtk.js 里 API 不稳定的 ImageStreamline filter，降低不确定性。
 */

/* ==================== 配置 ==================== */

const VTK_VERSION = '37.0.0';
// 经 index.html 的 importmap 映射到 CDN；改本地时把这里换成
// './node_modules/@kitware/vtk.js' 并同步修改 importmap。
const VTK_BASE = '@kitware/vtk.js';

// 帧目录：默认指向 demo 的输出目录；可用 ?frames=xxx 覆盖
const params = new URLSearchParams(location.search);
const FRAMES_DIR = params.get('frames') || '../test/bin/x64/Release/net10.0/frames';

const SLICING = { I: 0, J: 1, K: 2 };

/* ==================== DOM ==================== */

const $ = (id) => document.getElementById(id);

const dom = {
  errorBar: $('errorBar'), errorText: $('errorText'), errorClose: $('errorClose'),
  hudFrame: $('hudFrame'), hudTime: $('hudTime'), hudGrid: $('hudGrid'),
  hudField: $('hudField'), hudStatus: $('hudStatus'), hudBackend: $('hudBackend'),
  viewport: $('viewport'), loading: $('loading'),
  legendMin: $('legendMin'), legendMax: $('legendMax'),
  statFps: $('statFps'), statSize: $('statSize'), statVmax: $('statVmax'), statVavg: $('statVavg'),
  tlSlider: $('tlSlider'), tlTicks: $('tlTicks'),
  btnPlay: $('btnPlay'), btnPrev: $('btnPrev'), btnNext: $('btnNext'),
  fpsSel: $('fpsSel'), loopChk: $('loopChk'),
  panel: $('panel'), panelCollapse: $('panelCollapse'),
};

function showError(msg, isHtml = false) {
  if (isHtml) dom.errorText.innerHTML = msg; else dom.errorText.textContent = msg;
  dom.errorBar.classList.remove('hidden');
}
dom.errorClose.addEventListener('click', () => dom.errorBar.classList.add('hidden'));

function setStatus(text, kind = '') {
  dom.hudStatus.textContent = text;
  dom.hudStatus.className = 'status-chip' + (kind ? ' ' + kind : '');
}

/* ==================== 加载 VTK.js ==================== */

let V = null; // 模块集合

async function loadVtk() {
  const paths = {
    profiles: '/Rendering/OpenGL/Profiles/All',
    GenericRenderWindow: '/Rendering/Misc/GenericRenderWindow',
    XMLImageDataReader: '/IO/XML/XMLImageDataReader',
    Volume: '/Rendering/Core/Volume',
    VolumeMapper: '/Rendering/Core/VolumeMapper',
    ColorTransferFunction: '/Rendering/Core/ColorTransferFunction',
    PiecewiseFunction: '/Common/DataModel/PiecewiseFunction',
    ImageSlice: '/Rendering/Core/ImageSlice',
    ImageMapper: '/Rendering/Core/ImageMapper',
    PolyData: '/Common/DataModel/PolyData',
    DataArray: '/Common/Core/DataArray',
    // 注意：vtk.js 从 v37 起取消了独立的 Core/PolyDataMapper，
    // 统一使用 Rendering/Core/Mapper.js 的 vtkMapper（由 OpenGL 层按输入类型注册覆盖实现）
    Mapper: '/Rendering/Core/Mapper',
    Actor: '/Rendering/Core/Actor',
    CubeAxesActor: '/Rendering/Core/CubeAxesActor',
    OutlineFilter: '/Filters/General/OutlineFilter',
  };

  setStatus('加载 VTK.js…');

  const entries = await Promise.all(
    Object.entries(paths).map(async ([key, p]) => {
      const mod = await import(VTK_BASE + p);
      return [key, mod.default ?? mod];
    })
  );

  V = Object.fromEntries(entries);
}

/* ==================== 全局状态 ==================== */

const state = {
  frames: [],          // frames.json 的帧清单
  grid: { nx: 0, ny: 0, nz: 0 },
  index: 0,
  imageData: null,
  cache: new Map(),    // 轻量缓存：file -> ArrayBuffer（最多 4 帧）
  field: 'density',
  cmap: 'cyan',
  playing: false,
  timer: null,
  lastBytes: 0,
};

let renderWindow, renderer, apiRenderWindow, interactor;
let volumeActor, volumeMapper, ctf, ofun;
let sliceActors = { I: null, J: null, K: null };
let streamActor = null;
let boundsActor = null;

/* ==================== 初始化 ==================== */

async function main() {
  try {
    await loadVtk();
  } catch (e) {
    console.error(e);
    setStatus('VTK.js 加载失败', 'err');
    showError(
      '无法从 CDN 加载 VTK.js（' + (e && e.message ? e.message : e) + '）。' +
      '请在 <code>viz/</code> 目录执行 <code>npm i @kitware/vtk.js@' + VTK_VERSION + '</code>，' +
      '然后把 app.js 顶部的 <code>VTK_BASE</code> 改为 <code>./node_modules/@kitware/vtk.js</code>。',
      true
    );
    return;
  }

  try {
    setupRenderWindow();
  } catch (e) {
    console.error(e);
    setStatus('渲染器初始化失败', 'err');
    showError('WebGL 渲染器初始化失败：' + (e && e.message ? e.message : e));
    return;
  }

  try {
    await loadManifest();
  } catch (e) {
    console.error(e);
    setStatus('帧清单缺失', 'err');
    showError(
      '读不到 <code>' + FRAMES_DIR + '/frames.json</code>。' +
      '请先运行 demo 生成结果（<code>dotnet test.dll --size 64 --steps 50 --interval 5</code>），' +
      '或改用 <code>?frames=</code> 指定目录。',
      true
    );
    return;
  }

  bindControls();
  await gotoFrame(0);
  setStatus('就绪', 'ok');
}

function setupRenderWindow() {
  const grw = V.GenericRenderWindow.newInstance({
    background: [0.043, 0.059, 0.078],   // #0B0F14
    listenWindowResize: true,
  });
  grw.setContainer(dom.viewport);
  grw.resize();

  renderWindow = grw.getRenderWindow();
  renderer = grw.getRenderer();
  apiRenderWindow = grw.getApiSpecificRenderWindow();
  interactor = renderWindow.getInteractor();
  interactor.setDesiredUpdateRate(15);

  dom.hudBackend.textContent = (apiRenderWindow && apiRenderWindow.getClassName?.().includes('WebGPU'))
    ? 'WebGPU' : 'WebGL2';
}

async function loadManifest() {
  const url = `${FRAMES_DIR}/frames.json`;
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) throw new Error('HTTP ' + res.status + ' @ ' + url);

  const manifest = await res.json();
  state.frames = manifest.frames || [];
  state.grid = manifest.grid || { nx: 0, ny: 0, nz: 0 };

  if (!state.frames.length) throw new Error('frames.json 中没有帧');

  dom.hudGrid.textContent = `${state.grid.nx}×${state.grid.ny}×${state.grid.nz}`;
  dom.tlSlider.max = String(state.frames.length - 1);
  renderTicks();
  syncSliceRanges();
}

function renderTicks() {
  const n = state.frames.length;
  if (!n) return;
  const picks = 8;
  let html = '';
  for (let i = 0; i < picks; i++) {
    const idx = Math.round((i / (picks - 1)) * (n - 1));
    const f = state.frames[idx];
    html += `<span>${f ? f.step : idx}</span>`;
  }
  dom.tlTicks.innerHTML = html;
}

function syncSliceRanges() {
  for (const [axis, key] of [['I', 'sliceI'], ['J', 'sliceJ'], ['K', 'sliceK']]) {
    const max = state.grid[axis === 'I' ? 'nx' : axis === 'J' ? 'ny' : 'nz'] - 1;
    const el = $(key);
    if (!el) continue;
    el.max = String(Math.max(0, max));
    el.value = String(Math.floor(max / 2));
    $(key + 'Out').textContent = el.value;
  }
}

/* ==================== 帧加载 ==================== */

async function fetchFrameBuffer(file) {
  if (state.cache.has(file)) return state.cache.get(file);

  const res = await fetch(`${FRAMES_DIR}/${file}`, { cache: 'force-cache' });
  if (!res.ok) throw new Error('HTTP ' + res.status);
  const buf = await res.arrayBuffer();

  // 只保留最近 4 帧，避免内存随播放无限增长
  if (state.cache.size >= 4) state.cache.delete(state.cache.keys().next().value);
  state.cache.set(file, buf);
  return buf;
}

async function gotoFrame(i) {
  const n = state.frames.length;
  if (!n) return;
  state.index = ((i % n) + n) % n;

  const frame = state.frames[state.index];
  dom.loading.classList.remove('hidden');
  setStatus('加载帧 ' + frame.step + '…');

  try {
    const buf = await fetchFrameBuffer(frame.file);
    state.lastBytes = buf.byteLength;

    const reader = V.XMLImageDataReader.newInstance();
    reader.parseAsArrayBuffer(buf);
    const imageData = reader.getOutputData();
    if (!imageData) throw new Error('.vti 解析结果为空');

    state.imageData = imageData;
    addSpeedArray(imageData);
    buildScene();
    updateHud(frame);

    setStatus('就绪', 'ok');
  } catch (e) {
    console.error(e);
    setStatus('帧加载失败', 'err');
    showError('加载 <code>' + frame.file + '</code> 失败：' + (e && e.message ? e.message : e), true);
  } finally {
    dom.loading.classList.add('hidden');
  }
}

/** 由 velocity 三分量派生 speed 标量场（导出时为省体积没有落盘）。 */
function addSpeedArray(imageData) {
  if (imageData.getPointData().getArrayByName('speed')) return;

  const vel = imageData.getPointData().getArrayByName('velocity');
  if (!vel) return;

  const v = vel.getData();
  const n = v.length / 3;
  const speed = new Float32Array(n);
  let vmax = 0, vsum = 0;

  for (let i = 0; i < n; i++) {
    const x = v[3 * i], y = v[3 * i + 1], z = v[3 * i + 2];
    const s = Math.sqrt(x * x + y * y + z * z);
    speed[i] = s;
    if (s > vmax) vmax = s;
    vsum += s;
  }

  const da = V.DataArray.newInstance({ name: 'speed', values: speed, numberOfComponents: 1 });
  imageData.getPointData().addArray(da);

  dom.statVmax.textContent = vmax.toFixed(2);
  dom.statVavg.textContent = (vsum / Math.max(1, n)).toFixed(3);
}

function updateHud(frame) {
  dom.hudFrame.textContent = `${state.index + 1}/${state.frames.length} (step ${frame.step})`;
  dom.hudTime.textContent = Number(frame.time).toFixed(2);
  dom.hudField.textContent = state.field;
  dom.statSize.textContent = (state.lastBytes / 1048576).toFixed(1) + ' MB';
  dom.tlSlider.value = String(state.index);
}

/* ==================== 场景构建 ==================== */

function getArray(name) {
  const a = state.imageData.getPointData().getArrayByName(name);
  return a ? a.getData() : null;
}

function getMask() {
  const a = state.imageData.getPointData().getArrayByName('mask');
  return a ? a.getData() : null;
}

/** 只统计活动（非固体）体素的范围，避免固体区的 0 把色标压扁。 */
function activeRange(values, mask) {
  let mn = Infinity, mx = -Infinity;
  for (let i = 0; i < values.length; i++) {
    if (mask && mask[i] === 0) continue;
    const v = values[i];
    if (v < mn) mn = v;
    if (v > mx) mx = v;
  }
  if (!isFinite(mn) || !isFinite(mx)) { mn = 0; mx = 1; }
  if (mx - mn < 1e-9) mx = mn + 1e-6;
  return [mn, mx];
}

const COLOR_MAPS = {
  cyan: [[0.02, 0.05, 0.11], [0.03, 0.57, 0.70], [0.13, 0.83, 0.93], [0.96, 0.62, 0.04], [0.99, 0.90, 0.54]],
  amber: [[0.06, 0.04, 0.02], [0.55, 0.28, 0.02], [0.96, 0.62, 0.04], [1.0, 0.85, 0.45], [1.0, 0.99, 0.90]],
  viridis: [[0.10, 0.05, 0.20], [0.13, 0.36, 0.53], [0.13, 0.63, 0.53], [0.55, 0.83, 0.28], [0.99, 0.91, 0.15]],
};

function buildScene() {
  const imageData = state.imageData;

  // 清掉上一帧的 actor
  if (volumeActor) renderer.removeActor(volumeActor);
  for (const k of Object.keys(sliceActors)) {
    if (sliceActors[k]) { renderer.removeActor(sliceActors[k]); sliceActors[k] = null; }
  }
  if (streamActor) { renderer.removeActor(streamActor); streamActor = null; }
  if (boundsActor) { renderer.removeActor(boundsActor); boundsActor = null; }

  // 标量范围与传输函数
  const values = getArray(state.field) || getArray('density');
  const mask = getMask();
  const [mn, mx] = activeRange(values, mask);

  dom.legendMin.textContent = fmtNum(mn);
  dom.legendMax.textContent = fmtNum(mx);

  ctf = V.ColorTransferFunction.newInstance();
  ofun = V.PiecewiseFunction.newInstance();

  const stops = COLOR_MAPS[state.cmap] || COLOR_MAPS.cyan;
  const span = mx - mn;
  stops.forEach((c, i) => {
    const t = mn + (i / (stops.length - 1)) * span;
    ctf.addRGBPoint(t, c[0], c[1], c[2]);
  });

  const opacity = Number($('volOpacity').value) / 100;
  const lo = mn + (Number($('volLow').value) / 100) * span;
  const hi = mn + (Number($('volHigh').value) / 100) * span;
  ofun.addPoint(mn, 0);
  ofun.addPoint(lo, 0);
  ofun.addPoint(lo + 0.18 * (hi - lo), opacity * 0.32);
  ofun.addPoint(hi, opacity);
  ofun.addPoint(mx, opacity);

  // ---- 体积渲染 ----
  if ($('volEnabled').checked) {
    volumeMapper = V.VolumeMapper.newInstance();
    volumeMapper.setInputData(imageData);
    volumeActor = V.Volume.newInstance();
    volumeActor.setMapper(volumeMapper);

    const prop = volumeActor.getProperty();
    try {
      prop.setRGBTransferFunction(0, ctf);
      prop.setScalarOpacity(0, ofun);
    } catch (_) {
      prop.setRGBTransferFunction(ctf);
      prop.setScalarOpacity(ofun);
    }
    prop.setInterpolationTypeToLinear();
    prop.setShade(true);
    prop.setAmbient(0.32);
    prop.setDiffuse(0.72);
    prop.setSpecular(0.22);
    prop.setSpecularPower(20);

    renderer.addActor(volumeActor);
  }

  // ---- 正交切片 ----
  if ($('sliceEnabled').checked) {
    for (const axis of ['I', 'J', 'K']) {
      const m = V.ImageMapper.newInstance();
      m.setInputData(imageData);
      m.setSlicingMode(SLICING[axis]);
      m.setSlice(Number($('slice' + axis).value));

      const a = V.ImageSlice.newInstance();
      a.setMapper(m);
      const p = a.getProperty();
      try { p.setRGBTransferFunction(0, ctf); } catch (_) { p.setRGBTransferFunction(ctf); }
      try { p.setPiecewiseFunction(0, ofun); } catch (_) { p.setPiecewiseFunction(ofun); }
      p.setOpacity(0.92);

      sliceActors[axis] = a;
      renderer.addActor(a);
    }
  }

  // ---- 流线 / 箭头 ----
  if ($('streamEnabled').checked) {
    try {
      const poly = traceStreamlines();
      if (poly) {
        const m = V.Mapper.newInstance();
        m.setInputData(poly);
        m.setScalarVisibility(true);
        m.setColorModeToMapScalars();
        try { m.setUseLookupTableScalarRange(true); } catch (_) {}
        m.setLookupTable(ctf);

        streamActor = V.Actor.newInstance();
        streamActor.setMapper(m);
        streamActor.getProperty().setLineWidth(2.0);
        renderer.addActor(streamActor);
      }
    } catch (e) {
      console.warn('流线生成失败：', e);
    }
  }

  // ---- 包围盒边框 ----
  if ($('showBounds').checked) {
    try {
      const outline = V.OutlineFilter.newInstance();
      outline.setInputData(imageData);
      const om = V.Mapper.newInstance();
      om.setInputData(outline.getOutputData());
      boundsActor = V.Actor.newInstance();
      boundsActor.setMapper(om);
      boundsActor.getProperty().setColor(0.13, 0.83, 0.93);
      boundsActor.getProperty().setLineWidth(1.2);
      renderer.addActor(boundsActor);
    } catch (e) {
      console.warn('包围盒生成失败：', e);
    }
  }

  dom.viewport.querySelector('.vp-axes').style.display =
    $('showAxes').checked ? '' : 'none';

  if (!renderer.getActors().length) {
    renderer.resetCamera();
  }
  renderer.resetCameraClippingRange();
  renderWindow.render();
  measureFps();
}

function fmtNum(v) {
  const a = Math.abs(v);
  if (a >= 1000 || (a > 0 && a < 0.01)) return v.toExponential(1);
  return v.toFixed(a >= 10 ? 1 : 3);
}

/* ==================== 流线（自行积分，生成 vtkPolyData） ==================== */

/**
 * 在速度场上做 RK2 积分生成流线。
 * 自己实现而不依赖 vtk.js 的 ImageStreamline，是因为后者 API 在各版本间不稳定。
 */
function traceStreamlines() {
  const imageData = state.imageData;
  const vel = getArray('velocity');
  const mask = getMask();
  if (!vel) return null;

  const dims = imageData.getDimensions();          // [nx, ny, nz]
  const [nx, ny, nz] = dims;
  const nPlane = ny * nz;

  const seeds = Number($('seedDensity').value);
  const maxSteps = Number($('streamSteps').value);
  const stepLen = 0.55;                            // 网格单位
  const mode = document.querySelector('#streamSeg .seg-btn.active').dataset.mode;

  const sampleVel = (x, y, z) => {
    // 三线性采样速度场
    let i0 = Math.floor(x), j0 = Math.floor(y), k0 = Math.floor(z);
    i0 = Math.min(Math.max(i0, 0), nx - 2);
    j0 = Math.min(Math.max(j0, 0), ny - 2);
    k0 = Math.min(Math.max(k0, 0), nz - 2);
    const fx = Math.min(Math.max(x - i0, 0), 1);
    const fy = Math.min(Math.max(y - j0, 0), 1);
    const fz = Math.min(Math.max(z - k0, 0), 1);

    const out = [0, 0, 0];
    for (let c = 0; c < 3; c++) {
      const g = (ii, jj, kk) => vel[3 * (ii * nPlane + jj * nz + kk) + c];
      const c00 = g(i0, j0, k0) * (1 - fx) + g(i0 + 1, j0, k0) * fx;
      const c01 = g(i0, j0, k0 + 1) * (1 - fx) + g(i0 + 1, j0, k0 + 1) * fx;
      const c10 = g(i0, j0 + 1, k0) * (1 - fx) + g(i0 + 1, j0 + 1, k0) * fx;
      const c11 = g(i0, j0 + 1, k0 + 1) * (1 - fx) + g(i0 + 1, j0 + 1, k0 + 1) * fx;
      const c0 = c00 * (1 - fy) + c10 * fy;
      const c1 = c01 * (1 - fy) + c11 * fy;
      out[c] = c0 * (1 - fz) + c1 * fz;
    }
    return out;
  };

  const points = [];
  const lines = [];
  const speedAtPoint = [];

  for (let si = 1; si < seeds; si++) {
    for (let sj = 1; sj < seeds; sj++) {
      for (let sk = 1; sk < seeds; sk++) {
        let x = (si / seeds) * (nx - 1);
        let y = (sj / seeds) * (ny - 1);
        let z = (sk / seeds) * (nz - 1);

        const startIdx = points.length / 3;
        let count = 0;

        for (let s = 0; s < maxSteps; s++) {
          if (x < 0 || y < 0 || z < 0 || x > nx - 1 || y > ny - 1 || z > nz - 1) break;

          const ii = Math.round(x), jj = Math.round(y), kk = Math.round(z);
          if (mask && mask[ii * nPlane + jj * nz + kk] === 0) break;

          const v1 = sampleVel(x, y, z);
          const sp1 = Math.hypot(v1[0], v1[1], v1[2]);
          if (sp1 < 1e-4) break;

          // RK2 中点法
          const h = stepLen / sp1;
          const mx2 = x + 0.5 * h * v1[0];
          const my2 = y + 0.5 * h * v1[1];
          const mz2 = z + 0.5 * h * v1[2];
          const v2 = sampleVel(mx2, my2, mz2);
          const sp2 = Math.hypot(v2[0], v2[1], v2[2]);
          if (sp2 < 1e-4) break;

          points.push(x, y, z);
          speedAtPoint.push(sp1);
          count++;

          x += (stepLen / sp2) * v2[0];
          y += (stepLen / sp2) * v2[1];
          z += (stepLen / sp2) * v2[2];
        }

        if (count >= 2) {
          if (mode === 'arrow') {
            // 箭头模式：每段两端各画一个短线段（十字），近似矢量标记
            for (let s = 0; s + 1 < count; s += Math.max(1, Math.floor(maxSteps / 12))) {
              const a = startIdx + s;
              lines.push(2, a, a + 1);
            }
          } else {
            lines.push(count);
            for (let s = 0; s < count; s++) lines.push(startIdx + s);
          }
        }
      }
    }
  }

  if (!lines.length) return null;

  const poly = V.PolyData.newInstance();
  poly.getPoints().setData(Float32Array.from(points), 3);
  poly.getLines().setData(Uint32Array.from(lines));

  const da = V.DataArray.newInstance({
    name: 'speed',
    values: Float32Array.from(speedAtPoint),
    numberOfComponents: 1,
  });
  poly.getPointData().setScalars(da);

  return poly;
}

/* ==================== 控件绑定 ==================== */

let rebuildTimer = null;
function scheduleRebuild(delay = 60) {
  clearTimeout(rebuildTimer);
  rebuildTimer = setTimeout(() => { if (state.imageData) buildScene(); }, delay);
}

function bindValue(inputId, outId, fmt) {
  const el = $(inputId), out = $(outId);
  const apply = () => {
    out.textContent = fmt ? fmt(Number(el.value)) : el.value;
    out.classList.add('flash');
    setTimeout(() => out.classList.remove('flash'), 220);
  };
  el.addEventListener('input', apply);
  apply();
  return el;
}

function bindSegment(segId, attr, onChange) {
  const seg = $(segId);
  seg.addEventListener('click', (e) => {
    const btn = e.target.closest('.seg-btn');
    if (!btn) return;
    seg.querySelectorAll('.seg-btn').forEach((b) => b.classList.remove('active'));
    btn.classList.add('active');
    onChange(btn.dataset[attr]);
  });
}

function bindControls() {
  // 体绘制
  $('volEnabled').addEventListener('change', scheduleRebuild);
  bindSegment('fieldSeg', 'field', (f) => {
    state.field = f;
    dom.hudField.textContent = f;
    scheduleRebuild();
  });
  bindSegment('cmapSeg', 'cmap', (c) => { state.cmap = c; scheduleRebuild(); });

  for (const [id, out, fmt] of [
    ['volOpacity', 'volOpacityOut', (v) => (v / 100).toFixed(2)],
    ['volLow', 'volLowOut', (v) => (v / 100).toFixed(2)],
    ['volHigh', 'volHighOut', (v) => (v / 100).toFixed(2)],
  ]) {
    const el = bindValue(id, out, fmt);
    el.addEventListener('input', () => scheduleRebuild(0));
  }

  // 切片
  $('sliceEnabled').addEventListener('change', scheduleRebuild);
  for (const axis of ['I', 'J', 'K']) {
    const el = bindValue('slice' + axis, 'slice' + axis + 'Out');
    el.addEventListener('input', () => {
      if (sliceActors[axis]) {
        sliceActors[axis].getMapper().setSlice(Number(el.value));
        renderWindow.render();
      }
    });
  }

  // 流线
  $('streamEnabled').addEventListener('change', scheduleRebuild);
  bindSegment('streamSeg', 'mode', () => scheduleRebuild());
  bindValue('seedDensity', 'seedDensityOut').addEventListener('input', () => scheduleRebuild(160));
  bindValue('streamSteps', 'streamStepsOut').addEventListener('input', () => scheduleRebuild(160));

  // 显示
  $('showBounds').addEventListener('change', scheduleRebuild);
  $('showAxes').addEventListener('change', () => {
    dom.viewport.querySelector('.vp-axes').style.display =
      $('showAxes').checked ? '' : 'none';
  });

  // 面板折叠
  dom.panelCollapse.addEventListener('click', () => {
    dom.panel.classList.toggle('collapsed');
    dom.panelCollapse.textContent = dom.panel.classList.contains('collapsed') ? '›' : '‹';
    setTimeout(() => { apiRenderWindow && renderWindow.render(); }, 300);
  });

  // 时间轴
  dom.tlSlider.addEventListener('input', () => {
    stopPlay();
    gotoFrame(Number(dom.tlSlider.value));
  });
  dom.btnPrev.addEventListener('click', () => { stopPlay(); gotoFrame(state.index - 1); });
  dom.btnNext.addEventListener('click', () => { stopPlay(); gotoFrame(state.index + 1); });
  dom.btnPlay.addEventListener('click', () => (state.playing ? stopPlay() : startPlay()));

  window.addEventListener('resize', () => {
    // GenericRenderWindow 已监听 resize；这里补一次重绘
    setTimeout(() => renderWindow && renderWindow.render(), 120);
  });
}

/* ==================== 播放 ==================== */

function startPlay() {
  if (state.frames.length < 2) return;
  state.playing = true;
  dom.btnPlay.textContent = '❚❚';

  const fps = Number(dom.fpsSel.value) || 5;
  state.timer = setInterval(() => {
    const next = state.index + 1;
    if (next >= state.frames.length) {
      if (dom.loopChk.checked) gotoFrame(0);
      else { stopPlay(); return; }
    } else {
      gotoFrame(next);
    }
  }, 1000 / fps);
}

function stopPlay() {
  state.playing = false;
  dom.btnPlay.textContent = '▶';
  if (state.timer) { clearInterval(state.timer); state.timer = null; }
}

/* ==================== FPS ==================== */

let frameTimes = [];
function measureFps() {
  // 用一次渲染往返粗略估计刷新率
  const t0 = performance.now();
  requestAnimationFrame(() => {
    const dt = performance.now() - t0;
    frameTimes.push(dt);
    if (frameTimes.length > 20) frameTimes.shift();
    const avg = frameTimes.reduce((a, b) => a + b, 0) / frameTimes.length;
    dom.statFps.textContent = avg > 0 ? Math.min(999, Math.round(1000 / Math.max(avg, 1))) : '—';
  });
}

/* ==================== 启动 ==================== */

main();
