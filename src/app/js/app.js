/* app.js — orchestration: upload, playback, UI events, picking, charts */
(function () {
  'use strict';

  const $ = (id) => document.getElementById(id);

  const state = {
    field: 'pressure',
    colormap: 'viridis',
    rangeMode: 'auto',
    manualMin: 0,
    manualMax: 1,
    threshold: 0,
    arrowsOn: false,
    arrowStride: 2,
    section: { axis: 'x', pos: 24, enabled: false },
    current: 0,
    playing: false,
    fps: 5,
    ready: false,
    selected: null,
  };

  const FIELD_OPTS = [
    { value: 'pressure', key: 'fieldPressure' },
    { value: 'density', key: 'fieldDensity' },
    { value: 'speed', key: 'fieldSpeed' },
  ];

  const loader = new DataLoader();
  const viewer = new Viewer3D($('three-canvas'));
  const charts = new Charts($('ts-chart'));

  /* ---------- helpers ---------- */
  function fieldLabel(f) {
    return f === 'pressure' ? I18N.t('fieldPressure') : f === 'density' ? I18N.t('fieldDensity') : I18N.t('fieldSpeed');
  }
  function currentRange() {
    if (state.rangeMode === 'manual') {
      const mn = parseFloat($('range-min').value);
      const mx = parseFloat($('range-max').value);
      if (isFinite(mn) && isFinite(mx) && mn < mx) return [mn, mx];
      alert(I18N.t('rangeInvalid'));
      return loader.getRange(state.field);
    }
    return loader.getRange(state.field);
  }
  function getArr(frame, f) { return f === 'speed' ? frame.speed : frame[f]; }

  function showLoading(text) {
    $('loading-text').textContent = text || I18N.t('loading');
    $('loading').classList.remove('hidden');
  }
  function hideLoading() { $('loading').classList.add('hidden'); }

  /* ---------- populate selects ---------- */
  function refreshSelectOptions() {
    const fs = $('field-select'); fs.innerHTML = '';
    FIELD_OPTS.forEach((o) => {
      const el = document.createElement('option');
      el.value = o.value; el.textContent = I18N.t(o.key);
      if (o.value === state.field) el.selected = true;
      fs.appendChild(el);
    });
    const cs = $('colormap-select'); cs.innerHTML = '';
    Colormaps.names.forEach((n) => {
      const el = document.createElement('option');
      el.value = n; el.textContent = n.charAt(0).toUpperCase() + n.slice(1);
      if (n === state.colormap) el.selected = true;
      cs.appendChild(el);
    });
  }

  function refreshColorbar(range) {
    $('colorbar-title').textContent = `${fieldLabel(state.field)}  [${range[0].toFixed(3)}, ${range[1].toFixed(3)}]`;
    charts.drawColorbar($('colorbar'), Colormaps.getLUT(state.colormap));
  }

  /* ---------- frame rendering ---------- */
  async function showFrame(index, fromPlayback) {
    if (!state.ready) return;
    if (index < 0 || index >= loader.frameCount) return;
    state.current = index;
    const frame = await loader.getFrame(index);
    if (!frame) return;
    const range = currentRange();
    viewer.updateVoxels(frame, state.field, state.colormap, range, state.threshold);

    if (state.arrowsOn) viewer.updateArrows(frame, true, state.colormap);
    else viewer.setArrowsVisible(false);

    // 2D slice (always reflects section axis/pos of current frame)
    const slice = viewer.extractSlice(state.field, frame, state.section.axis, state.section.pos);
    const [mn, mx] = range; const denom = (mx - mn) || 1;
    const norm = new Float32Array(slice.w * slice.h);
    for (let p = 0; p < norm.length; p++) {
      let t = (slice.data[p] - mn) / denom; t = t < 0 ? 0 : t > 1 ? 1 : t;
      norm[p] = t;
    }
    charts.renderSlice($('slice-canvas'), { data: norm, w: slice.w, h: slice.h }, Colormaps.getLUT(state.colormap));

    refreshColorbar(range);
    renderVoxelInfo(frame);

    // timeline labels
    $('frame-label').textContent = `${I18N.t('frame')} ${index + 1} / ${loader.frameCount}`;
    $('time-label').textContent = `t = ${frame.time.toFixed(3)}`;
    $('frame-slider').value = index;

    if (fromPlayback) loader.getFrame(index + 1); // prefetch
  }

  function refreshCurrentFrame() { return showFrame(state.current, false); }

  function renderVoxelInfo(frame) {
    const box = $('voxel-info');
    if (state.selected == null) { box.textContent = I18N.t('voxelEmpty'); return; }
    const [i, j, k] = loader.idxToIJK(state.selected);
    const arr = getArr(frame, state.field);
    const val = arr ? arr[state.selected] : 0;
    const u = frame.u[state.selected], v = frame.v[state.selected], w = frame.w[state.selected];
    const spd = Math.sqrt(u * u + v * v + w * w);
    box.innerHTML =
      `<div class="kv"><span class="k">${I18N.t('coord')}</span><span class="v">(${i}, ${j}, ${k})</span></div>` +
      `<div class="kv"><span class="k">${I18N.t('value')} (${fieldLabel(state.field)})</span><span class="v">${val.toFixed(4)}</span></div>` +
      `<div class="kv"><span class="k">${I18N.t('velocity')}</span><span class="v">(${u.toFixed(3)}, ${v.toFixed(3)}, ${w.toFixed(3)})</span></div>` +
      `<div class="kv"><span class="k">|V|</span><span class="v">${spd.toFixed(4)}</span></div>`;
  }

  /* ---------- time series ---------- */
  async function loadSeries() {
    if (state.selected == null) return;
    $('ts-progress').classList.remove('hidden');
    const { values, times } = await loader.getVoxelSeries(state.selected, state.field, (i, n) => {
      $('ts-progress').textContent = `${I18N.t('seriesProgress')} ${i}/${n}`;
    });
    charts.renderTimeSeries(times, values, fieldLabel(state.field));
    $('ts-progress').classList.add('hidden');
  }

  /* ---------- playback ---------- */
  function setPlayIcon(playing) {
    $('play-icon').innerHTML = playing
      ? '<path d="M6 5h4v14H6zM14 5h4v14h-4z"/>'
      : '<path d="M8 5v14l11-7z"/>';
  }
  function loopStep() {
    if (!state.playing) return;
    let next = state.current + 1;
    if (next >= loader.frameCount) next = 0;
    showFrame(next, true).then(() => {
      if (state.playing) setTimeout(loopStep, 1000 / state.fps);
    }).catch((e) => { console.error(e); if (state.playing) setTimeout(loopStep, 1000 / state.fps); });
  }
  function startPlay() { if (!state.ready) return; state.playing = true; setPlayIcon(true); loopStep(); }
  function stopPlay() { state.playing = false; setPlayIcon(false); }

  /* ---------- upload ---------- */
  async function loadData(files) {
    showLoading(I18N.t('loading'));
    try {
      const meta = await loader.loadFiles(files);
      viewer.buildModel(loader);
      // section position bounds
      updateSectionPosBounds();
      state.current = 0;
      state.ready = true;
      // enable UI
      $('empty-state').classList.add('hidden');
      $('colorbar-wrap').classList.remove('hidden');
      $('play-btn').disabled = false;
      $('frame-slider').disabled = false;
      $('frame-slider').max = loader.frameCount - 1;
      // default manual range boxes
      const r = loader.getRange(state.field);
      $('range-min').value = r[0].toFixed(4);
      $('range-max').value = r[1].toFixed(4);
      state.section.pos = Math.floor(loader.nx / 2);
      $('section-pos').value = state.section.pos;
      hideLoading();
      await refreshCurrentFrame();
      console.info('CFD data loaded:', meta.Grid, 'frames:', loader.frameCount);
    } catch (e) {
      hideLoading();
      console.error(e);
      alert(I18N.t('uploadErr'));
    }
  }

  function updateSectionPosBounds() {
    const len = state.section.axis === 'x' ? loader.nx : state.section.axis === 'y' ? loader.ny : loader.nz;
    $('section-pos').max = len - 1;
  }

  /* ---------- events ---------- */
  function bindEvents() {
    $('upload-btn').addEventListener('click', () => $('folder-input').click());
    $('folder-input').addEventListener('change', (e) => {
      if (e.target.files && e.target.files.length) loadData(e.target.files);
    });

    $('lang-toggle').addEventListener('click', () => I18N.toggle());

    $('field-select').addEventListener('change', (e) => {
      state.field = e.target.value;
      $('colorbar-title').textContent = fieldLabel(state.field);
      refreshCurrentFrame();
      if (state.selected != null) loadSeries();
    });
    $('colormap-select').addEventListener('change', (e) => {
      state.colormap = e.target.value;
      refreshCurrentFrame();
    });
    $('range-mode').addEventListener('change', (e) => {
      state.rangeMode = e.target.value;
      $('manual-range').classList.toggle('hidden', state.rangeMode !== 'manual');
      refreshCurrentFrame();
    });
    $('range-min').addEventListener('change', refreshCurrentFrame);
    $('range-max').addEventListener('change', refreshCurrentFrame);

    $('threshold-slider').addEventListener('input', (e) => {
      state.threshold = parseFloat(e.target.value);
      $('threshold-val').textContent = state.threshold.toFixed(2);
      refreshCurrentFrame();
    });

    $('arrows-toggle').addEventListener('change', (e) => {
      state.arrowsOn = e.target.checked;
      if (state.arrowsOn) refreshCurrentFrame();
      else viewer.setArrowsVisible(false);
    });
    $('arrow-density').addEventListener('change', (e) => {
      state.arrowStride = parseInt(e.target.value, 10);
      viewer.setArrowStride(state.arrowStride);
      if (state.arrowsOn) refreshCurrentFrame();
    });

    $('section-toggle').addEventListener('change', (e) => {
      state.section.enabled = e.target.checked;
      viewer.setCrossSection(state.section.axis, state.section.pos, state.section.enabled);
    });
    $('section-axis').addEventListener('change', (e) => {
      state.section.axis = e.target.value;
      updateSectionPosBounds();
      viewer.setCrossSection(state.section.axis, state.section.pos, state.section.enabled);
      refreshCurrentFrame();
    });
    $('section-pos').addEventListener('input', (e) => {
      state.section.pos = parseInt(e.target.value, 10);
      $('section-pos-val').textContent = state.section.pos;
      viewer.setCrossSection(state.section.axis, state.section.pos, state.section.enabled);
      refreshCurrentFrame();
    });

    $('play-btn').addEventListener('click', () => { state.playing ? stopPlay() : startPlay(); });
    $('speed-select').addEventListener('change', (e) => { state.fps = parseInt(e.target.value, 10); });
    $('frame-slider').addEventListener('input', (e) => {
      state.current = parseInt(e.target.value, 10);
      refreshCurrentFrame();
    });

    // picking (ignore drags that were camera rotations)
    let _downX = 0, _downY = 0, _moved = false;
    $('three-canvas').addEventListener('mousedown', (e) => { _downX = e.clientX; _downY = e.clientY; _moved = false; });
    $('three-canvas').addEventListener('mousemove', (e) => {
      if (Math.abs(e.clientX - _downX) > 4 || Math.abs(e.clientY - _downY) > 4) _moved = true;
    });
    $('three-canvas').addEventListener('click', (e) => {
      if (!state.ready || _moved) return;
      const hit = viewer.pick(e.clientX, e.clientY);
      if (hit) {
        state.selected = hit.idx;
        viewer.highlight(hit.idx);
        loader.getFrame(state.current).then((frame) => { if (frame) renderVoxelInfo(frame); });
        loadSeries();
      }
    });

    // language change hook
    I18N.onChange = () => {
      refreshSelectOptions();
      refreshColorbar(currentRange());
      if (state.selected != null) loader.getFrame(state.current).then((f) => { if (f) renderVoxelInfo(f); });
      charts.refreshSeriesLang();
    };
  }

  /* ---------- init ---------- */
  function init() {
    refreshSelectOptions();
    bindEvents();
    I18N.apply();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
