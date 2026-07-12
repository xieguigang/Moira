// viewer.js —— three.js 场景：体素/箭头 InstancedMesh、裁剪截面、拾取与动画

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { buildLUT } from './palettes.js';
import { getFrameField, getVoxelSeries, idxToIJK, ijkToIdx } from './vtkParser.js';

const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);

export class Viewer {
  constructor(container) {
    this.container = container;
    this.scene = new THREE.Scene();
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.localClippingEnabled = true;
    container.appendChild(this.renderer.domElement);

    this.camera = new THREE.PerspectiveCamera(50, 1, 0.1, 5000);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;

    this.scene.add(new THREE.AmbientLight(0xffffff, 0.85));
    const dir = new THREE.DirectionalLight(0xffffff, 0.7);
    dir.position.set(1, 1.4, 0.8);
    this.scene.add(dir);

    this.raycaster = new THREE.Raycaster();
    this.pointer = new THREE.Vector2();

    this.ds = null;
    this.field = 'speed';
    this.palette = 'viridis';
    this.lut = buildLUT(this.palette);
    this.range = { min: 0, max: 1, auto: true };
    this.fieldStats = {};
    this.currentFrame = 0;
    this.playing = false;
    this.speed = 1.5;
    this._frameAcc = 0;

    this.arrowsVisible = false;
    this.arrowStep = 2;
    this.arrowVoxels = [];

    this.section = { enabled: false, axis: 'x', pos: 0.5, flip: false, arbitrary: false, normal: [1, 0, 0], arbPos: 0 };
    this.clipPlane = new THREE.Plane(new THREE.Vector3(-1, 0, 0), 0);

    this.voxelMesh = null;
    this.arrowMesh = null;
    this.sliceMesh = null;
    this.highlight = null;
    this.selectedIdx = -1;

    this.onFrame = null;   // (frame, time) => {}
    this.onPick = null;    // ({idx,i,j,k}) => {}

    this._clock = new THREE.Clock();
    this._pointerDown = null;

    this._bindPointer();
    this._onResize = () => this.resize();
    window.addEventListener('resize', this._onResize);
    this.resize();
    this._animate();
  }

  resize() {
    const w = this.container.clientWidth, h = this.container.clientHeight;
    if (!w || !h) return;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h, false);
  }

  // ---------- 数据加载 ----------
  setData(ds) {
    this.clearScene();
    this.ds = ds;
    const { nx, ny, nz } = ds;
    this.offset = [nx / 2, ny / 2, nz / 2];
    const N = nx * ny * nz;

    // 预计算各场全局 min/max
    this.fieldStats = {};
    for (const f of ds.fields) {
      const a = ds.arrays[f];
      let mn = Infinity, mx = -Infinity;
      for (let i = 0; i < a.length; i++) { const v = a[i]; if (v < mn) mn = v; if (v > mx) mx = v; }
      this.fieldStats[f] = { min: mn, max: mx };
    }
    this.range = { min: this.fieldStats[this.field].min, max: this.fieldStats[this.field].max, auto: true };

    // 体素 InstancedMesh
    const geo = new THREE.BoxGeometry(0.9, 0.9, 0.9);
    const mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
    mat.clippingPlanes = this.section.enabled ? [this.clipPlane] : [];
    const mesh = new THREE.InstancedMesh(geo, mat, N);
    mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(N * 3), 3);
    const dummy = new THREE.Object3D();
    const [cx, cy, cz] = this.offset;
    for (let idx = 0; idx < N; idx++) {
      const [i, j, k] = idxToIJK(idx, nx, ny);
      dummy.position.set(i + 0.5 - cx, j + 0.5 - cy, k + 0.5 - cz);
      dummy.updateMatrix();
      mesh.setMatrixAt(idx, dummy.matrix);
    }
    mesh.frustumCulled = false;
    this.scene.add(mesh);
    this.voxelMesh = mesh;

    // 高亮线框
    const hg = new THREE.EdgesGeometry(new THREE.BoxGeometry(1.04, 1.04, 1.04));
    this.highlight = new THREE.LineSegments(hg, new THREE.LineBasicMaterial({ color: 0xF59E0B, linewidth: 2 }));
    this.highlight.visible = false;
    this.scene.add(this.highlight);

    // 箭头（按需构建）
    this._buildArrowSkeleton();

    // 坐标轴 / 地面网格
    const maxDim = Math.max(nx, ny, nz);
    const grid = new THREE.GridHelper(maxDim * 1.4, 14, 0xcdd5e0, 0xe4e9f0);
    grid.position.y = -cz - 0.5;
    grid.material.opacity = 0.5; grid.material.transparent = true;
    this._grid = grid;
    this.scene.add(grid);

    this.currentFrame = 0;
    this._frameAcc = 0;
    const d = maxDim * 1.8;
    this.camera.position.set(d * 0.8, d * 0.6, d * 0.85);
    this.controls.target.set(0, 0, 0);
    this.controls.update();

    this.updateColors();
    this._applySection();
  }

  clearScene() {
    for (const m of [this.voxelMesh, this.arrowMesh, this.sliceMesh, this.highlight, this._grid]) {
      if (!m) continue;
      this.scene.remove(m);
      m.geometry && m.geometry.dispose();
      if (m.material) (Array.isArray(m.material) ? m.material : [m.material]).forEach((x) => x.dispose && x.dispose());
    }
    this.voxelMesh = this.arrowMesh = this.sliceMesh = this.highlight = this._grid = null;
    this.selectedIdx = -1;
  }

  // ---------- 着色 ----------
  setField(field) {
    this.field = field;
    if (!this.ds) return;
    if (this.range.auto) {
      const s = this.fieldStats[field];
      this.range.min = s.min; this.range.max = s.max;
    }
    this.updateColors();
    this._refreshArrows();
  }

  setPalette(name) {
    this.palette = name;
    this.lut = buildLUT(name);
    if (!this.ds) return;
    this.updateColors();
    this._refreshArrows();
  }

  setRange(min, max) {
    this.range.min = min; this.range.max = max; this.range.auto = false;
    if (!this.ds) return;
    this.updateColors();
    this._refreshArrows();
  }

  setRangeAuto() {
    const s = this.fieldStats[this.field];
    this.range.min = s.min; this.range.max = s.max; this.range.auto = true;
    if (!this.ds) return;
    this.updateColors();
    this._refreshArrows();
  }

  updateColors() {
    if (!this.voxelMesh || !this.ds) return;
    const N = this.ds.nx * this.ds.ny * this.ds.nz;
    const slice = getFrameField(this.ds, this.field, this.currentFrame);
    const { min, max } = this.range;
    const span = (max - min) || 1;
    const lut = this.lut;
    const col = this.voxelMesh.instanceColor.array;
    for (let idx = 0; idx < N; idx++) {
      const t = clamp((slice[idx] - min) / span, 0, 1);
      const ti = (t * 255) | 0;
      const li = ti * 3;
      col[idx * 3] = lut[li] / 255;
      col[idx * 3 + 1] = lut[li + 1] / 255;
      col[idx * 3 + 2] = lut[li + 2] / 255;
    }
    this.voxelMesh.instanceColor.needsUpdate = true;
    if (this.section.enabled) this._updateSlice();
  }

  // ---------- 帧 / 动画 ----------
  updateFrame(frame) {
    this.currentFrame = frame;
    this._frameAcc = frame;
    this.updateColors();
    if (this.arrowsVisible) this._refreshArrows();
    if (this.onFrame) this.onFrame(frame, this.ds.timesteps[frame]);
  }

  setFrame(frame) { if (!this.ds) return; this.updateFrame(clamp(frame | 0, 0, this.ds.nFrames - 1)); }

  setPlaying(p) {
    this.playing = p;
    if (p) this._frameAcc = this.currentFrame;
  }
  togglePlay() { this.setPlaying(!this.playing); return this.playing; }
  setSpeed(s) { this.speed = s; }

  _animate = () => {
    requestAnimationFrame(this._animate);
    const dt = this._clock.getDelta();
    if (this.playing && this.ds) {
      this._frameAcc += dt * this.speed;
      let f = Math.floor(this._frameAcc) % this.ds.nFrames;
      if (f < 0) f += this.ds.nFrames;
      if (f !== this.currentFrame) this.updateFrame(f);
    }
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  };

  // ---------- 箭头 ----------
  _buildArrowSkeleton() {
    const { nx, ny, nz } = this.ds;
    const step = this.arrowStep;
    this.arrowVoxels = [];
    for (let k = 0; k < nz; k += step)
      for (let j = 0; j < ny; j += step)
        for (let i = 0; i < nx; i += step)
          this.arrowVoxels.push(ijkToIdx(i, j, k, nx, ny));

    const shaft = new THREE.CylinderGeometry(0.05, 0.05, 0.7, 6); shaft.translate(0, 0.35, 0);
    const head = new THREE.ConeGeometry(0.15, 0.3, 8); head.translate(0, 0.85, 0);
    const geo = mergeGeometries([shaft, head]);
    const mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
    mat.clippingPlanes = this.section.enabled ? [this.clipPlane] : [];
    const mesh = new THREE.InstancedMesh(geo, mat, this.arrowVoxels.length);
    mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(this.arrowVoxels.length * 3), 3);
    mesh.frustumCulled = false;
    mesh.visible = this.arrowsVisible;
    this.scene.add(mesh);
    this.arrowMesh = mesh;
  }

  setArrows(visible) {
    this.arrowsVisible = visible;
    if (this.arrowMesh) { this.arrowMesh.visible = visible; if (visible) this._refreshArrows(); }
  }

  setArrowStep(step) {
    this.arrowStep = step;
    if (this.ds) { this._buildArrowSkeleton(); this.arrowMesh.visible = this.arrowsVisible; if (this.arrowsVisible) this._refreshArrows(); }
  }

  _refreshArrows() {
    if (!this.arrowMesh || !this.ds) return;
    const N = this.ds.nx * this.ds.ny * this.ds.nz;
    const u = getFrameField(this.ds, 'u', this.currentFrame);
    const v = getFrameField(this.ds, 'v', this.currentFrame);
    const w = getFrameField(this.ds, 'w', this.currentFrame);
    const ref = this.fieldStats.speed.max || 1;
    const lut = this.lut;
    const { min, max } = this.range;
    const span = (max - min) || 1;
    const up = new THREE.Vector3(0, 1, 0);
    const dir = new THREE.Vector3();
    const q = new THREE.Quaternion();
    const dummy = new THREE.Object3D();
    const col = this.arrowMesh.instanceColor.array;
    const [cx, cy, cz] = this.offset;
    for (let a = 0; a < this.arrowVoxels.length; a++) {
      const idx = this.arrowVoxels[a];
      const uu = u[idx], vv = v[idx], ww = w[idx];
      const sp = Math.sqrt(uu * uu + vv * vv + ww * ww);
      if (sp < 1e-6) {
        dummy.scale.set(0, 0, 0); dummy.position.set(0, -9999, 0); dummy.updateMatrix();
        this.arrowMesh.setMatrixAt(a, dummy.matrix);
        continue;
      }
      dir.set(uu, vv, ww).multiplyScalar(1 / sp);
      const len = clamp(sp / ref, 0, 1) * 0.9 + 0.12;
      q.setFromUnitVectors(up, dir);
      const [i, j, k] = idxToIJK(idx, this.ds.nx, this.ds.ny);
      dummy.position.set(i + 0.5 - cx, j + 0.5 - cy, k + 0.5 - cz);
      dummy.quaternion.copy(q);
      dummy.scale.set(0.55, len, 0.55);
      dummy.updateMatrix();
      this.arrowMesh.setMatrixAt(a, dummy.matrix);

      const t = clamp((sp - min) / span, 0, 1);
      const li = ((t * 255) | 0) * 3;
      col[a * 3] = lut[li] / 255; col[a * 3 + 1] = lut[li + 1] / 255; col[a * 3 + 2] = lut[li + 2] / 255;
    }
    this.arrowMesh.instanceMatrix.needsUpdate = true;
    this.arrowMesh.instanceColor.needsUpdate = true;
  }

  // ---------- 横截面 ----------
  setSection(cfg) { Object.assign(this.section, cfg); if (this.ds) this._applySection(); }

  _applySection() {
    const s = this.section;
    const planes = s.enabled ? [this.clipPlane] : [];
    if (this.voxelMesh) this.voxelMesh.material.clippingPlanes = planes;
    if (this.arrowMesh) this.arrowMesh.material.clippingPlanes = planes;
    if (!s.enabled) { if (this.sliceMesh) this.sliceMesh.visible = false; return; }

    const { nx, ny, nz } = this.ds;
    const [cx, cy, cz] = this.offset;
    let n, c;
    if (s.arbitrary) {
      const nv = new THREE.Vector3(s.normal[0], s.normal[1], s.normal[2]);
      if (nv.lengthSq() < 1e-9) nv.set(1, 0, 0);
      nv.normalize();
      const maxHalf = Math.max(cx, cy, cz);
      const d = s.arbPos * maxHalf;
      n = nv.clone();
      if (s.flip) { n.negate(); c = d; }   // 保留 nv·x <= d
      else { c = -d; }                      // 保留 nv·x >= d
    } else {
      const dim = s.axis === 'x' ? nx : s.axis === 'y' ? ny : nz;
      const center = s.axis === 'x' ? cx : s.axis === 'y' ? cy : cz;
      const posW = s.pos * dim - center;
      if (s.flip) { n = new THREE.Vector3(s.axis === 'x' ? 1 : 0, s.axis === 'y' ? 1 : 0, s.axis === 'z' ? 1 : 0); c = -posW; }
      else { n = new THREE.Vector3(s.axis === 'x' ? -1 : 0, s.axis === 'y' ? -1 : 0, s.axis === 'z' ? -1 : 0); c = posW; }
    }
    this.clipPlane.normal.copy(n);
    this.clipPlane.constant = c;
    this._updateSlice();
  }

  _updateSlice() {
    if (!this.ds) return;
    const s = this.section;
    if (s.arbitrary) { if (this.sliceMesh) this.sliceMesh.visible = false; return; }
    const { nx, ny, nz } = this.ds;
    const [cx, cy, cz] = this.offset;
    const slice = getFrameField(this.ds, this.field, this.currentFrame);
    const { min, max } = this.range;
    const span = (max - min) || 1;
    const lut = this.lut;

    let wDim, hDim, axisIdx, pos;
    if (s.axis === 'x') { wDim = nz; hDim = ny; axisIdx = Math.round(s.pos * nx); pos = s.pos * nx - cx; }
    else if (s.axis === 'y') { wDim = nx; hDim = nz; axisIdx = Math.round(s.pos * ny); pos = s.pos * ny - cy; }
    else { wDim = nx; hDim = ny; axisIdx = Math.round(s.pos * nz); pos = s.pos * nz - cz; }
    axisIdx = clamp(axisIdx, 0, (s.axis === 'x' ? nx : s.axis === 'y' ? ny : nz) - 1);

    const data = new Uint8Array(wDim * hDim * 4);
    for (let r = 0; r < hDim; r++) {
      for (let cI = 0; cI < wDim; cI++) {
        let i, j, k;
        if (s.axis === 'x') { i = axisIdx; j = r; k = cI; }
        else if (s.axis === 'y') { i = cI; j = axisIdx; k = r; }
        else { i = cI; j = r; k = axisIdx; }
        const idx = ijkToIdx(i, j, k, nx, ny);
        const t = clamp((slice[idx] - min) / span, 0, 1);
        const li = ((t * 255) | 0) * 3;
        const p = (r * wDim + cI) * 4;
        data[p] = lut[li]; data[p + 1] = lut[li + 1]; data[p + 2] = lut[li + 2]; data[p + 3] = 255;
      }
    }
    const tex = new THREE.DataTexture(data, wDim, hDim, THREE.RGBAFormat);
    tex.needsUpdate = true;

    if (this.sliceMesh) { this.scene.remove(this.sliceMesh); this.sliceMesh.geometry.dispose(); this.sliceMesh.material.map.dispose(); this.sliceMesh.material.dispose(); }
    const geo = new THREE.PlaneGeometry(wDim, hDim);
    let rot;
    if (s.axis === 'x') { rot = new THREE.Euler(0, Math.PI / 2, 0); geo.translate(0, 0, 0); }
    else if (s.axis === 'y') { rot = new THREE.Euler(-Math.PI / 2, 0, 0); }
    else { rot = new THREE.Euler(0, 0, 0); }
    const mat = new THREE.MeshBasicMaterial({ map: tex, side: THREE.DoubleSide });
    const m = new THREE.Mesh(geo, mat);
    m.rotation.copy(rot);
    if (s.axis === 'x') m.position.set(pos, 0, 0);
    else if (s.axis === 'y') m.position.set(0, pos, 0);
    else m.position.set(0, 0, pos);
    m.renderOrder = 1;
    this.scene.add(m);
    this.sliceMesh = m;
  }

  // ---------- 拾取 ----------
  _bindPointer() {
    const dom = this.renderer.domElement;
    dom.addEventListener('pointerdown', (e) => { this._pointerDown = { x: e.clientX, y: e.clientY }; });
    dom.addEventListener('pointerup', (e) => {
      if (!this._pointerDown) return;
      const dx = e.clientX - this._pointerDown.x, dy = e.clientY - this._pointerDown.y;
      this._pointerDown = null;
      if (Math.hypot(dx, dy) > 5) return; // 拖拽，不拾取
      this._pick(e);
    });
  }

  _pick(e) {
    if (!this.voxelMesh) return;
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.pointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
    this.pointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
    this.raycaster.setFromCamera(this.pointer, this.camera);
    const hit = this.raycaster.intersectObject(this.voxelMesh, false)[0];
    if (hit && hit.instanceId != null) {
      const idx = hit.instanceId;
      this.selectedIdx = idx;
      const [i, j, k] = idxToIJK(idx, this.ds.nx, this.ds.ny);
      const [cx, cy, cz] = this.offset;
      this.highlight.position.set(i + 0.5 - cx, j + 0.5 - cy, k + 0.5 - cz);
      this.highlight.visible = true;
      if (this.onPick) this.onPick({ idx, i, j, k });
    }
  }

  // ---------- 对外取数 ----------
  getVoxelSeries(field, idx) { return getVoxelSeries(this.ds, field, idx); }
  getVoxelValues(idx, frame) {
    const N = this.ds.nx * this.ds.ny * this.ds.nz;
    const a = this.ds.arrays;
    const at = (f) => (k) => a[k][idx + frame * N];
    return {
      pressure: a.pressure[idx + frame * N],
      density: a.density[idx + frame * N],
      u: a.u[idx + frame * N],
      v: a.v[idx + frame * N],
      w: a.w[idx + frame * N],
      speed: a.speed[idx + frame * N]
    };
  }
}
