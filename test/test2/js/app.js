// app.js —— 整体编排：上传、状态管理与全部 UI 控件绑定

import { initI18n, t, toggleLang } from './i18n.js';
import { loadDatasetFromFiles, parsePVD } from './vtkParser.js';
import { Viewer } from './viewer.js';
import { drawLegend } from './palettes.js';
import { initChart, updateChart } from './chart.js';

const $ = (id) => document.getElementById(id);

const i18n = initI18n();
let viewer = null;
let ds = null;
let selectedInfo = null;
let chart = null;

function fmt(v) {
  if (v === 0) return '0';
  const a = Math.abs(v);
  if (a < 1e-3 || a >= 1e4) return v.toExponential(2);
  return String(+v.toPrecision(4));
}

function setStatus(text) { $('dataStatus').textContent = text; }

// ===================== 初始化 =====================
function init() {
  viewer = new Viewer($('viewport'));
  chart = initChart($('tsChart'));
  drawLegend($('legendCanvas'), 'viridis');

  viewer.onFrame = (frame, time) => {
    $('timeDisplay').textContent = (time ?? 0).toFixed(2);
    $('frameDisplay').textContent = frame;
    if (document.activeElement !== $('frameSlider')) $('frameSlider').value = frame;
    if (selectedInfo) refreshVoxelValues();
  };
  viewer.onPick = (info) => showVoxelPanel(info);

  bindControls();
  i18n.on(refreshDynamicUI);

  $('pickTip').textContent = t('tip.drag');
  $('pickTip').hidden = false;

  // 调试钩子 + autoload 演示模式（?autoload 从同源 data/ 直接加载）
  window.__cdf = { viewer, handleFiles, loadDatasetFromFiles };
  if (new URLSearchParams(location.search).has('autoload')) loadDemo();
}

async function loadDemo() {
  try {
    const pvdText = await (await fetch('data/animation.pvd')).text();
    const { frameFiles } = parsePVD(pvdText);
    const files = [];
    for (const name of frameFiles) {
      const blob = await (await fetch('data/' + name)).blob();
      files.push(new File([blob], name));
    }
    await handleFiles(files);
  } catch (e) {
    console.error('autoload failed', e);
  }
}

// ===================== 控件绑定 =====================
function bindControls() {
  $('folderInput').addEventListener('change', (e) => handleFiles(e.target.files));

  $('langToggle').addEventListener('click', () => toggleLang());

  $('fieldSelect').addEventListener('change', (e) => {
    viewer.setField(e.target.value);
    configureRange();
    updateLegendLabels();
    if (selectedInfo) refreshVoxelPanel();
  });

  $('paletteSelect').addEventListener('change', (e) => {
    viewer.setPalette(e.target.value);
    drawLegend($('legendCanvas'), e.target.value);
  });

  $('rangeAuto').addEventListener('click', () => {
    viewer.setRangeAuto();
    configureRange();
    updateLegendLabels();
  });
  $('rangeMin').addEventListener('input', onRangeInput);
  $('rangeMax').addEventListener('input', onRangeInput);

  $('arrowsToggle').addEventListener('change', (e) => viewer.setArrows(e.target.checked));
  $('arrowDensity').addEventListener('input', (e) => {
    $('arrowDensityVal').textContent = e.target.value;
    viewer.setArrowStep(parseInt(e.target.value, 10));
  });

  $('sectionToggle').addEventListener('change', (e) => viewer.setSection({ enabled: e.target.checked }));
  $('sectionPos').addEventListener('input', (e) => viewer.setSection({ pos: parseFloat(e.target.value) }));
  $('sectionFlip').addEventListener('change', (e) => viewer.setSection({ flip: e.target.checked }));
  document.querySelectorAll('#sectionAxis .seg-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('#sectionAxis .seg-btn').forEach((b) => b.classList.remove('active'));
      btn.classList.add('active');
      viewer.setSection({ axis: btn.dataset.axis });
    });
  });
  $('sectionArbitrary').addEventListener('change', (e) => {
    viewer.setSection({ arbitrary: e.target.checked });
    $('arbitraryFields').hidden = !e.target.checked;
  });
  ['normalX', 'normalY', 'normalZ'].forEach((id) => {
    $(id).addEventListener('input', () => {
      viewer.setSection({ normal: [parseFloat($('normalX').value) || 0, parseFloat($('normalY').value) || 0, parseFloat($('normalZ').value) || 0] });
    });
  });
  $('arbitraryPos').addEventListener('input', (e) => viewer.setSection({ arbPos: parseFloat(e.target.value) }));

  $('playBtn').addEventListener('click', () => updatePlayUI(viewer.togglePlay()));
  $('resetBtn').addEventListener('click', () => { viewer.setPlaying(false); viewer.setFrame(0); updatePlayUI(false); });
  $('speedSlider').addEventListener('input', (e) => {
    const v = parseFloat(e.target.value);
    viewer.setSpeed(v);
    $('speedVal').textContent = v.toFixed(1) + '×';
  });
  $('frameSlider').addEventListener('input', (e) => viewer.setFrame(parseInt(e.target.value, 10)));
}

function onRangeInput() {
  let mn = parseFloat($('rangeMin').value);
  let mx = parseFloat($('rangeMax').value);
  if (mn > mx) { [mn, mx] = [mx, mn]; }
  viewer.setRange(mn, mx);
  updateLegendLabels();
}

function updatePlayUI(playing) {
  $('playIcon').innerHTML = playing
    ? '<path d="M6 4h4v16H6zM14 4h4v16h-4z"/>'
    : '<path d="M8 5v14l11-7z"/>';
  $('playLabel').textContent = playing ? t('control.pause') : t('control.play');
}

// ===================== 数值范围滑块配置 =====================
function configureRange() {
  if (!ds) return;
  const s = viewer.fieldStats[viewer.field];
  const step = (s.max - s.min) / 1000 || 1e-6;
  for (const id of ['rangeMin', 'rangeMax']) {
    const el = $(id);
    el.min = s.min; el.max = s.max; el.step = step;
  }
  $('rangeMin').value = viewer.range.min;
  $('rangeMax').value = viewer.range.max;
}

function updateLegendLabels() {
  $('rangeMinLabel').textContent = fmt(viewer.range.min);
  $('rangeMaxLabel').textContent = fmt(viewer.range.max);
}

// ===================== 体素面板 =====================
function showVoxelPanel(info) {
  selectedInfo = info;
  $('voxelNone').hidden = true;
  $('voxelInfo').hidden = false;
  $('voxelCoords').textContent = `${info.i}, ${info.j}, ${info.k}`;
  refreshVoxelPanel();
}

function refreshVoxelPanel() {
  refreshVoxelValues();
  refreshVoxelChart();
}

function refreshVoxelValues() {
  if (!selectedInfo) return;
  const vals = viewer.getVoxelValues(selectedInfo.idx, viewer.currentFrame);
  const order = ['pressure', 'density', 'u', 'v', 'w', 'speed'];
  $('voxelValues').innerHTML = order.map((f) =>
    `<div class="vchip">${t('fieldLabel.' + f)}<b>${fmt(vals[f])}</b></div>`
  ).join('');
}

function refreshVoxelChart() {
  if (!selectedInfo) return;
  const field = viewer.field;
  const series = viewer.getVoxelSeries(field, selectedInfo.idx);
  updateChart(ds.timesteps, { [field]: series }, { [field]: t('fieldLabel.' + field) });
}

// ===================== 文件处理 =====================
async function handleFiles(files) {
  const overlay = $('loadingOverlay');
  overlay.hidden = false;
  setProgress(0, 1);
  $('loadingText').textContent = t('status.parsing', { pct: '0' });
  try {
    ds = await loadDatasetFromFiles(files, { onProgress: (done, total) => setProgress(done, total) });
  } catch (err) {
    overlay.hidden = true;
    alert(err.message || String(err));
    return;
  }
  viewer.setData(ds);
  viewer.setFrame(0);

  $('frameSlider').max = ds.nFrames - 1;
  $('frameSlider').value = 0;
  $('frameDisplay').textContent = 0;
  $('timeDisplay').textContent = ds.timesteps[0].toFixed(2);

  configureRange();
  updateLegendLabels();
  drawLegend($('legendCanvas'), viewer.palette);

  setStatus(t('status.loaded', { frames: ds.nFrames, nx: ds.nx, ny: ds.ny, nz: ds.nz }));
  $('emptyHint').style.display = 'none';
  overlay.hidden = true;
}

function setProgress(done, total) {
  const pct = total ? Math.round((done / total) * 100) : 0;
  $('progressBar').style.width = pct + '%';
  $('progressPct').textContent = pct + '%';
  $('loadingText').textContent = t('status.parsing', { pct });
}

// ===================== 语言切换刷新动态文案 =====================
function refreshDynamicUI() {
  if (ds) setStatus(t('status.loaded', { frames: ds.nFrames, nx: ds.nx, ny: ds.ny, nz: ds.nz }));
  $('pickTip').textContent = t('tip.drag');
  updateLegendLabels();
  if (chart) {
    chart.options.scales.x.title.text = t('chart.x');
    chart.options.scales.y.title.text = t('chart.y');
    chart.update('none');
  }
  if (selectedInfo) refreshVoxelPanel();
  updatePlayUI(viewer.playing);
}

init();
