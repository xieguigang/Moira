// main.js
// three.js 场景搭建、模型加载/属性统计、体素化与导出流程编排。
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { loadFromUrl, loadFromFile, getExtension } from './loaders.js';
import { collectGeometry } from './geometry.js';
import { voxelizeMesh, voxelizePoints } from './voxelizer.js';
import { buildVoxelJson, downloadJson, suggestFilename } from './exporter.js';

// ---------------- DOM ----------------
const $ = (id) => document.getElementById(id);
const el = {
  canvas: $('glCanvas'),
  modelPath: $('modelPath'),
  loadBtn: $('loadBtn'),
  fileInput: $('fileInput'),
  statVertices: $('statVertices'),
  statFaces: $('statFaces'),
  statObjects: $('statObjects'),
  statPoints: $('statPoints'),
  statBBox: $('statBBox'),
  modelType: $('modelType'),
  resInput: $('resInput'),
  voxelizeBtn: $('voxelizeBtn'),
  downloadBtn: $('downloadBtn'),
  progressBar: $('progressBar'),
  progressText: $('progressText'),
  previewChk: $('previewChk'),
  voxelSummary: $('voxelSummary'),
  vsDims: $('vsDims'),
  vsSolid: $('vsSolid'),
  vsSize: $('vsSize'),
  vsTime: $('vsTime'),
  loadingOverlay: $('loadingOverlay'),
  loadingText: $('loadingText'),
  statusDot: $('statusDot'),
  statusText: $('statusText'),
  statusMeta: $('statusMeta'),
  resetViewBtn: $('resetViewBtn'),
  toggleGridBtn: $('toggleGridBtn'),
  toggleWireBtn: $('toggleWireBtn'),
  themeToggle: $('themeToggle'),
};

// ---------------- State ----------------
const state = {
  currentObject: null,   // 场景中的模型 Object3D
  geometryData: null,    // collectGeometry 结果
  voxelResult: null,     // 体素化结果
  sourceName: 'airplane1.3mf',
  sourceFormat: '3mf',
  wireframe: false,
};

// ---------------- Three.js ----------------
let scene, camera, renderer, controls, grid, modelGroup, voxelGroup;
let keyLight, fillLight, rimLight, hemiLight;

// 三维场景亮/暗配色（与 CSS 主题同步，切换时平滑过渡）
const SCENE_THEMES = {
  dark: {
    background: 0x0a0f1c, fog: 0x0a0f1c,
    grid1: 0x2b3b57, grid2: 0x1b2740,
    hemiSky: 0xbcd4ff, hemiGround: 0x1a2233,
    key: 0xffffff, keyI: 1.6,
    fill: 0x88bbff, fillI: 0.7,
    rim: 0x22d3ee, rimI: 0.5,
  },
  light: {
    background: 0xeef3f9, fog: 0xeef3f9,
    grid1: 0xb8c4d6, grid2: 0xd6deea,
    hemiSky: 0xffffff, hemiGround: 0x9aa7bd,
    key: 0xffffff, keyI: 1.25,
    fill: 0x6f9bff, fillI: 0.55,
    rim: 0x3b82f6, rimI: 0.4,
  },
};
let currentTheme = 'dark';
// 当前与目标调色板（Color 实例，逐帧 lerp）
const palette = {
  bg: new THREE.Color(), fog: new THREE.Color(),
  grid1: new THREE.Color(), grid2: new THREE.Color(),
  hemiSky: new THREE.Color(), hemiGround: new THREE.Color(),
  key: new THREE.Color(), fill: new THREE.Color(), rim: new THREE.Color(),
};
const paletteTarget = {
  bg: new THREE.Color(), fog: new THREE.Color(),
  grid1: new THREE.Color(), grid2: new THREE.Color(),
  hemiSky: new THREE.Color(), hemiGround: new THREE.Color(),
  key: new THREE.Color(), fill: new THREE.Color(), rim: new THREE.Color(),
};

function themePalette(theme) { return SCENE_THEMES[theme] || SCENE_THEMES.dark; }

// 设定目标调色板；immediate=true 时直接应用（无动画，用于初始化）
function setSceneThemeTarget(theme, immediate) {
  const t = themePalette(theme);
  paletteTarget.bg.setHex(t.background);
  paletteTarget.fog.setHex(t.fog);
  paletteTarget.grid1.setHex(t.grid1);
  paletteTarget.grid2.setHex(t.grid2);
  paletteTarget.hemiSky.setHex(t.hemiSky);
  paletteTarget.hemiGround.setHex(t.hemiGround);
  paletteTarget.key.setHex(t.key);
  paletteTarget.fill.setHex(t.fill);
  paletteTarget.rim.setHex(t.rim);
  // 灯光强度按主题设定（立即，不 lerp）
  if (keyLight) {
    keyLight.intensity = t.keyI;
    fillLight.intensity = t.fillI;
    rimLight.intensity = t.rimI;
  }

  // 网格地面重建（两色顶点，无法 lerp，故直接切换）
  if (grid) { scene.remove(grid); grid.geometry.dispose(); grid.material.dispose(); }
  grid = new THREE.GridHelper(40, 40, t.grid1, t.grid2);
  grid.material.transparent = true; grid.material.opacity = 0.55;
  scene.add(grid);
  if (immediate) {
    for (const k in palette) palette[k].copy(paletteTarget[k]);
    applyPalette();
  }
}

function applyPalette() {
  if (!scene) return;
  scene.background.copy(palette.bg);
  scene.fog.color.copy(palette.fog);
  if (hemiLight) { hemiLight.color.copy(palette.hemiSky); hemiLight.groundColor.copy(palette.hemiGround); }
  if (keyLight) keyLight.color.copy(palette.key);
  if (fillLight) fillLight.color.copy(palette.fill);
  if (rimLight) rimLight.color.copy(palette.rim);
}

function lerpPalette(dt) {
  const a = Math.min(1, dt * 3.2); // 约 0.45s 收敛
  for (const k in palette) palette[k].lerp(paletteTarget[k], a);
  applyPalette();
}

function initThree() {
  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0a0f1c);
  scene.fog = new THREE.Fog(0x0a0f1c, 50, 260);

  const { clientWidth: w, clientHeight: h } = el.canvas.parentElement;
  camera = new THREE.PerspectiveCamera(55, w / h, 0.01, 5000);
  camera.position.set(6, 5, 8);

  renderer = new THREE.WebGLRenderer({ canvas: el.canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(w, h, false);
  renderer.outputColorSpace = THREE.SRGBColorSpace;

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;

  // 光照（三点光）
  hemiLight = new THREE.HemisphereLight(0xbcd4ff, 0x1a2233, 0.9); scene.add(hemiLight);
  keyLight = new THREE.DirectionalLight(0xffffff, 1.6); keyLight.position.set(8, 12, 6); scene.add(keyLight);
  fillLight = new THREE.DirectionalLight(0x88bbff, 0.7); fillLight.position.set(-8, 4, -6); scene.add(fillLight);
  rimLight = new THREE.DirectionalLight(0x22d3ee, 0.5); rimLight.position.set(0, -6, -8); scene.add(rimLight);

  // 地面网格 + 坐标轴（网格由主题调色板创建）
  scene.add(new THREE.AxesHelper(3));

  modelGroup = new THREE.Group();
  voxelGroup = new THREE.Group();
  scene.add(modelGroup);
  scene.add(voxelGroup);

  // 应用初始主题（立即，无动画）
  setSceneThemeTarget(currentTheme, true);

  window.addEventListener('resize', onResize);
  animate();
}

function onResize() {
  const { clientWidth: w, clientHeight: h } = el.canvas.parentElement;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h, false);
}

const clock = new THREE.Clock();
function animate() {
  requestAnimationFrame(animate);
  const dt = clock.getDelta();
  lerpPalette(dt);
  controls.update();
  renderer.render(scene, camera);
}

// 将模型居中并缩放到合适大小，返回缩放/位移信息（用于坐标一致性）
function frameObject(object) {
  const box = new THREE.Box3().setFromObject(object);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z) || 1;

  const targetSize = 8;
  const scale = targetSize / maxDim;

  object.scale.setScalar(scale);
  object.position.sub(center.multiplyScalar(scale));

  // 相机取景
  const dist = targetSize * 1.8;
  camera.position.set(dist * 0.7, dist * 0.6, dist);
  controls.target.set(0, 0, 0);
  controls.update();
}

// ---------------- Status helpers ----------------
function setStatus(text, kind = '') {
  el.statusText.textContent = text;
  el.statusDot.className = 'status-dot' + (kind ? ' ' + kind : '');
}
function setMeta(text) { el.statusMeta.textContent = text || ''; }
function setProgress(p, msg) {
  el.progressBar.style.width = Math.round(p * 100) + '%';
  if (msg) el.progressText.textContent = msg;
}
function showLoading(show, text) {
  el.loadingOverlay.hidden = !show;
  if (text) el.loadingText.textContent = text;
}

// ---------------- Load flow ----------------
async function loadModel(source, isFile) {
  showLoading(true, '加载中…');
  setStatus('加载模型…', 'busy');
  setProgress(0, '开始加载');
  el.voxelizeBtn.disabled = true;
  el.downloadBtn.disabled = true;
  el.voxelSummary.hidden = true;
  state.voxelResult = null;
  clearGroup(voxelGroup);

  try {
    const onProg = (p, msg) => { setProgress(p * 0.9, msg); showLoading(true, msg); };
    const result = isFile
      ? await loadFromFile(source, onProg)
      : await loadFromUrl(source, onProg);

    const object = result.object;
    state.sourceFormat = result.ext;
    state.sourceName = isFile ? source.name : source;

    // 收集几何数据（世界坐标，居中前）
    // 先加入场景做居中，再基于居中后的世界矩阵收集几何用于体素化
    clearGroup(modelGroup);
    disposeObject(state.currentObject);
    modelGroup.add(object);
    state.currentObject = object;
    applyWireframe(object, state.wireframe);
    frameObject(object);

    setProgress(0.95, '统计属性…');
    const geo = collectGeometry(object);
    state.geometryData = geo;
    updateStats(geo, result.ext);

    el.voxelizeBtn.disabled = false;
    setProgress(1, '就绪');
    setStatus(`已加载 ${state.sourceName}`, 'ok');
    setMeta(`${fmt(geo.vertexCount)} 顶点 · ${fmt(geo.faceCount)} 面`);
  } catch (err) {
    console.error(err);
    setStatus(`加载失败: ${err.message}`, 'err');
    setProgress(0, '失败');
    updateStats(null);
  } finally {
    showLoading(false);
  }
}

function updateStats(geo, ext) {
  if (!geo) {
    el.statVertices.textContent = '—';
    el.statFaces.textContent = '—';
    el.statObjects.textContent = '—';
    el.statPoints.textContent = '—';
    el.statBBox.textContent = '—';
    el.modelType.textContent = '—';
    return;
  }
  el.statVertices.textContent = fmt(geo.vertexCount);
  el.statFaces.textContent = fmt(geo.faceCount);
  el.statObjects.textContent = fmt(geo.objectCount);
  el.statPoints.textContent = geo.pointCount ? fmt(geo.pointCount) : '0';
  el.modelType.textContent = (ext || '').toUpperCase() + (geo.isPointCloud ? ' · 点云' : ' · 网格');

  const s = geo.bbox.getSize(new THREE.Vector3());
  el.statBBox.textContent = `${s.x.toFixed(3)} × ${s.y.toFixed(3)} × ${s.z.toFixed(3)}`;
}

// ---------------- Voxelize flow ----------------
async function runVoxelize() {
  if (!state.geometryData) return;
  const resolution = clampInt(parseInt(el.resInput.value, 10) || 64, 8, 512);
  el.resInput.value = resolution;

  el.voxelizeBtn.disabled = true;
  el.downloadBtn.disabled = true;
  setStatus('体素化中…', 'busy');
  setProgress(0, '准备体素化');

  const geo = state.geometryData;
  const t0 = performance.now();

  // 让 UI 有机会刷新
  await nextFrame();

  try {
    const onProg = (p, msg) => setProgress(p, msg);
    let vox;
    if (geo.isPointCloud) {
      vox = voxelizePoints(geo.points, geo.bbox, resolution, onProg);
    } else {
      vox = voxelizeMesh(geo.triangles, geo.bbox, resolution, onProg);
    }
    const dt = performance.now() - t0;

    state.voxelResult = { vox, mode: geo.isPointCloud ? 'occupancy' : 'solid' };

    // 摘要
    el.voxelSummary.hidden = false;
    el.vsDims.textContent = vox.dims.join(' × ');
    el.vsSolid.textContent = `${fmt(vox.solidCount)} / ${fmt(vox.dims[0]*vox.dims[1]*vox.dims[2])}`;
    el.vsSize.textContent = vox.voxelSize[0].toFixed(4);
    el.vsTime.textContent = `${dt.toFixed(0)} ms`;

    // 预览
    if (el.previewChk.checked) {
      buildVoxelPreview(vox);
    } else {
      clearGroup(voxelGroup);
      modelGroup.visible = true;
    }

    el.downloadBtn.disabled = false;
    setStatus('体素化完成', 'ok');
    setMeta(`固体体素 ${fmt(vox.solidCount)} · ${dt.toFixed(0)} ms`);
  } catch (err) {
    console.error(err);
    setStatus(`体素化失败: ${err.message}`, 'err');
  } finally {
    el.voxelizeBtn.disabled = false;
  }
}

// 用 InstancedMesh 预览固体体素（仅渲染固体，与模型同坐标系对齐）
function buildVoxelPreview(vox) {
  clearGroup(voxelGroup);
  const [W, H, D] = vox.dims;
  const total = vox.solidCount;
  if (total === 0) return;
  // 预览上限，避免过多实例
  const cap = 200000;
  const stride = total > cap ? Math.ceil(total / cap) : 1;

  const [sx] = vox.voxelSize;
  const [ox, oy, oz] = vox.origin;

  // 与模型显示一致：模型经过 frameObject 的 scale/position
  const obj = state.currentObject;
  const objScale = obj.scale.x;
  const objPos = obj.position;

  const geoBox = new THREE.BoxGeometry(sx * objScale * 0.92, sx * objScale * 0.92, sx * objScale * 0.92);
  const mat = new THREE.MeshStandardMaterial({
    color: 0x22d3ee, metalness: 0.2, roughness: 0.5,
    transparent: true, opacity: 0.85,
  });

  const count = Math.ceil(total / stride);
  const inst = new THREE.InstancedMesh(geoBox, mat, count);
  const m = new THREE.Matrix4();
  const data = vox.data;
  let written = 0, seen = 0;

  for (let x = 0; x < W && written < count; x++) {
    for (let y = 0; y < H; y++) {
      const base = (x * H + y) * D;
      for (let z = 0; z < D; z++) {
        if (data[base + z] !== 1) continue;
        if (seen++ % stride !== 0) continue;
        // 体素中心世界坐标（模型原始坐标系）
        const wx = ox + (x + 0.5) * vox.voxelSize[0];
        const wy = oy + (y + 0.5) * vox.voxelSize[1];
        const wz = oz + (z + 0.5) * vox.voxelSize[2];
        // 应用模型显示变换
        m.makeTranslation(
          wx * objScale + objPos.x,
          wy * objScale + objPos.y,
          wz * objScale + objPos.z
        );
        inst.setMatrixAt(written++, m);
        if (written >= count) break;
      }
      if (written >= count) break;
    }
  }
  inst.count = written;
  inst.instanceMatrix.needsUpdate = true;
  voxelGroup.add(inst);

  // 预览时隐藏原模型以便观察
  modelGroup.visible = false;
}

// ---------------- Download ----------------
function runDownload() {
  if (!state.voxelResult) return;
  const { vox, mode } = state.voxelResult;
  const json = buildVoxelJson(vox, {
    sourceModel: state.sourceName,
    sourceFormat: state.sourceFormat,
    mode,
  });
  const name = suggestFilename(state.sourceName, vox.dims);
  downloadJson(json, name);
  setStatus(`已下载 ${name}`, 'ok');
}

// ---------------- Utils ----------------
function clearGroup(group) {
  if (!group) return;
  for (let i = group.children.length - 1; i >= 0; i--) {
    const c = group.children[i];
    group.remove(c);
    disposeObject(c);
  }
}
function disposeObject(obj) {
  if (!obj) return;
  obj.traverse?.((c) => {
    if (c.geometry) c.geometry.dispose?.();
    if (c.material) {
      const mats = Array.isArray(c.material) ? c.material : [c.material];
      mats.forEach((m) => m.dispose?.());
    }
  });
  if (obj.geometry) obj.geometry.dispose?.();
  if (obj.material) {
    const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
    mats.forEach((m) => m.dispose?.());
  }
}
function applyWireframe(object, on) {
  object.traverse((c) => {
    if (c.isMesh && c.material) {
      const mats = Array.isArray(c.material) ? c.material : [c.material];
      mats.forEach((m) => { if ('wireframe' in m) m.wireframe = on; });
    }
  });
}
function fmt(n) { return (n ?? 0).toLocaleString('en-US'); }
function clampInt(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
function nextFrame() { return new Promise((r) => requestAnimationFrame(() => r())); }

// ---------------- Events ----------------
el.loadBtn.addEventListener('click', () => {
  const path = el.modelPath.value.trim();
  if (path) loadModel(path, false);
});
el.modelPath.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') el.loadBtn.click();
});
el.fileInput.addEventListener('change', (e) => {
  const file = e.target.files?.[0];
  if (file) {
    el.modelPath.value = file.name;
    loadModel(file, true);
  }
  e.target.value = '';
});
el.voxelizeBtn.addEventListener('click', runVoxelize);
el.downloadBtn.addEventListener('click', runDownload);

document.querySelectorAll('.chip').forEach((chip) => {
  chip.addEventListener('click', () => {
    el.resInput.value = chip.dataset.res;
    document.querySelectorAll('.chip').forEach((c) => c.classList.remove('active'));
    chip.classList.add('active');
  });
});
el.previewChk.addEventListener('change', () => {
  if (!state.voxelResult) return;
  if (el.previewChk.checked) buildVoxelPreview(state.voxelResult.vox);
  else { clearGroup(voxelGroup); modelGroup.visible = true; }
});

el.resetViewBtn.addEventListener('click', () => {
  if (state.currentObject) frameObject(state.currentObject);
});
el.toggleGridBtn.addEventListener('click', () => {
  grid.visible = !grid.visible;
  el.toggleGridBtn.classList.toggle('active', !grid.visible);
});
el.toggleWireBtn.addEventListener('click', () => {
  state.wireframe = !state.wireframe;
  if (state.currentObject) applyWireframe(state.currentObject, state.wireframe);
  el.toggleWireBtn.classList.toggle('active', state.wireframe);
});

// ---------------- Boot ----------------
initThree();
setStatus('等待加载模型…');
// 自动加载默认测试模型
window.addEventListener('load', () => {
  loadModel(el.modelPath.value.trim() || 'airplane1.3mf', false);
});
