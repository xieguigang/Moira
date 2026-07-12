/* viewer3d.js — three.js voxel field rendering, velocity arrows, cross-section, picking */
(function (global) {
  'use strict';

  class Viewer3D {
    constructor(canvas) {
      this.canvas = canvas;
      this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
      this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
      this.renderer.localClippingEnabled = true;
      this.renderer.setClearColor(0xF4F6FA, 1);

      this.scene = new THREE.Scene();
      this.camera = new THREE.PerspectiveCamera(50, 1, 0.1, 8000);

      this.controls = new OrbitControls(this.camera, this.renderer.domElement);

      this.scene.add(new THREE.AmbientLight(0xffffff, 0.85));
      const dir = new THREE.DirectionalLight(0xffffff, 0.7);
      dir.position.set(1, 1.4, 0.8);
      this.scene.add(dir);

      this.raycaster = new THREE.Raycaster();
      this.pointer = new THREE.Vector2();

      this.voxelMesh = null;
      this.arrowMesh = null;
      this.arrowCenters = null;
      this.arrowStride = 2;
      this.highlightMesh = null;

      this.clipPlane = new THREE.Plane(new THREE.Vector3(-1, 0, 0), 0);
      this._up = new THREE.Vector3(0, 1, 0);
      this._dir = new THREE.Vector3();
      this._pos = new THREE.Vector3();
      this._scl = new THREE.Vector3();
      this._q = new THREE.Quaternion();
      this._m4 = new THREE.Matrix4();

      this._shown = null;        // Uint8Array of instance scales (1/0)
      this._thresholdActive = false;

      this.loader = null;

      this._resize();
      window.addEventListener('resize', () => this._resize());
      this._loop();
    }

    _resize() {
      const w = this.canvas.clientWidth || this.canvas.parentElement.clientWidth;
      const h = this.canvas.clientHeight || this.canvas.parentElement.clientHeight;
      if (!w || !h) return;
      this.renderer.setSize(w, h, false);
      this.camera.aspect = w / h;
      this.camera.updateProjectionMatrix();
    }

    _loop() {
      const tick = () => {
        this.controls.update();
        this.renderer.render(this.scene, this.camera);
        requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    }

    buildModel(loader) {
      this.loader = loader;
      const nx = loader.nx, ny = loader.ny, nz = loader.nz;
      const N = loader.N;
      const sp = loader.spacing, o = loader.origin;
      this.nx = nx; this.ny = ny; this.nz = nz; this.N = N;

      // voxel geometry sized by spacing
      const geo = new THREE.BoxGeometry(sp[0], sp[1], sp[2]);
      const mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
      const mesh = new THREE.InstancedMesh(geo, mat, N);
      mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(N * 3), 3);
      mesh.frustumCulled = false;

      this._shown = new Uint8Array(N).fill(1);
      const m = new THREE.Matrix4();
      this.basePos = new Float32Array(N * 3);
      for (let i = 0; i < nx; i++) {
        for (let j = 0; j < ny; j++) {
          for (let k = 0; k < nz; k++) {
            const idx = i * ny * nz + j * nz + k;
            const cx = o[0] + (i + 0.5) * sp[0];
            const cy = o[1] + (j + 0.5) * sp[1];
            const cz = o[2] + (k + 0.5) * sp[2];
            this.basePos[idx * 3] = cx;
            this.basePos[idx * 3 + 1] = cy;
            this.basePos[idx * 3 + 2] = cz;
            m.makeTranslation(cx, cy, cz);
            mesh.setMatrixAt(idx, m);
          }
        }
      }
      mesh.instanceMatrix.needsUpdate = true;
      this.scene.add(mesh);
      this.voxelMesh = mesh;

      // highlight box
      const hgeo = new THREE.BoxGeometry(sp[0] * 1.12, sp[1] * 1.12, sp[2] * 1.12);
      const hmat = new THREE.MeshBasicMaterial({ color: 0x111827, wireframe: true });
      this.highlightMesh = new THREE.Mesh(hgeo, hmat);
      this.highlightMesh.visible = false;
      this.scene.add(this.highlightMesh);

      // frame camera
      const mid = new THREE.Vector3(
        o[0] + nx * sp[0] / 2,
        o[1] + ny * sp[1] / 2,
        o[2] + nz * sp[2] / 2
      );
      const d = Math.max(nx * sp[0], ny * sp[1], nz * sp[2]) * 1.5;
      this.controls.target.copy(mid);
      this.camera.position.set(mid.x + d, mid.y + d * 0.6, mid.z + d);
      this.camera.near = 0.1;
      this.camera.far = d * 20;
      this.camera.updateProjectionMatrix();
      this.controls.update();
      this._resize();
    }

    get voxelMaterial() { return this.voxelMesh ? this.voxelMesh.material : null; }
    get arrowMaterial() { return this.arrowMesh ? this.arrowMesh.material : null; }

    /* ---- color / threshold update ---- */
    updateVoxels(frame, field, colormapName, range, threshold) {
      if (!this.voxelMesh) return;
      const arr = field === 'speed' ? frame.speed : frame[field];
      if (!arr) return;
      const lut = Colormaps.getLinLUT(colormapName);
      const [min, max] = range;
      const denom = (max - min) || 1;
      const col = this.voxelMesh.instanceColor.array;
      const mat = this.voxelMesh.instanceMatrix.array;

      const threshOn = threshold > 0;
      if (threshOn) this._thresholdActive = true;

      for (let idx = 0; idx < this.N; idx++) {
        const val = arr[idx];
        const t = (val - min) / denom;
        const tc = t < 0 ? 0 : t > 1 ? 1 : t;
        const li = (tc * 255) | 0;
        col[idx * 3] = lut[li * 3];
        col[idx * 3 + 1] = lut[li * 3 + 1];
        col[idx * 3 + 2] = lut[li * 3 + 2];

        if (threshOn) {
          const show = tc >= threshold ? 1 : 0;
          if (this._shown[idx] !== show) {
            this._shown[idx] = show;
            const b = idx * 16;
            mat[b] = show; mat[b + 5] = show; mat[b + 10] = show;
          }
        }
      }
      this.voxelMesh.instanceColor.needsUpdate = true;
      if (threshOn) this.voxelMesh.instanceMatrix.needsUpdate = true;
      else if (this._thresholdActive) {
        // reset all scales back to 1 once
        for (let idx = 0; idx < this.N; idx++) {
          if (this._shown[idx] === 0) {
            this._shown[idx] = 1;
            const b = idx * 16;
            mat[b] = 1; mat[b + 5] = 1; mat[b + 10] = 1;
          }
        }
        this.voxelMesh.instanceMatrix.needsUpdate = true;
        this._thresholdActive = false;
      }
    }

    /* ---- velocity arrows ---- */
    setArrowStride(stride) {
      if (stride === this.arrowStride && this.arrowMesh) return;
      this.arrowStride = stride;
      this._buildArrows();
    }

    _buildArrows() {
      if (!this.loader) return;
      const s = this.arrowStride;
      const nx = this.nx, ny = this.ny, nz = this.nz, sp = this.loader.spacing;
      const centers = [];
      for (let i = 0; i < nx; i += s)
        for (let j = 0; j < ny; j += s)
          for (let k = 0; k < nz; k += s) {
            const idx = i * ny * nz + j * nz + k;
            centers.push(this.basePos[idx * 3], this.basePos[idx * 3 + 1], this.basePos[idx * 3 + 2]);
          }
      this.arrowCenters = new Float32Array(centers);
      const count = centers.length / 3;

      if (this.arrowMesh) { this.scene.remove(this.arrowMesh); this.arrowMesh.geometry.dispose(); this.arrowMesh.material.dispose(); }
      const r = sp[0] * 0.12;
      const ageo = new THREE.ConeGeometry(r, 1, 6);
      ageo.translate(0, 0.5, 0); // base at origin, tip at +1
      const amat = new THREE.MeshLambertMaterial({ color: 0xffffff });
      const amesh = new THREE.InstancedMesh(ageo, amat, count);
      amesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(count * 3), 3);
      amesh.frustumCulled = false;
      amesh.visible = false;
      this.scene.add(amesh);
      this.arrowMesh = amesh;
      this.maxLen = sp[0] * 1.7;
    }

    updateArrows(frame, enabled, colormapName) {
      if (!this.arrowMesh) this._buildArrows();
      if (!this.arrowMesh) return;
      if (!enabled) { this.arrowMesh.visible = false; return; }
      const centers = this.arrowCenters;
      const count = centers.length / 3;
      const u = frame.u, v = frame.v, w = frame.w;
      const speedMax = this.loader.getRange('speed')[1] || 1;
      const lut = Colormaps.getLinLUT(colormapName);
      const stride = this.arrowStride, ny = this.ny, nz = this.nz;

      for (let a = 0; a < count; a++) {
        const cx = centers[a * 3], cy = centers[a * 3 + 1], cz = centers[a * 3 + 2];
        // recover idx from world pos
        const i = Math.round((cx - this.loader.origin[0]) / this.loader.spacing[0] - 0.5);
        const j = Math.round((cy - this.loader.origin[1]) / this.loader.spacing[1] - 0.5);
        const k = Math.round((cz - this.loader.origin[2]) / this.loader.spacing[2] - 0.5);
        const idx = i * ny * nz + j * nz + k;
        const uu = u[idx], vv = v[idx], ww = w[idx];
        const spd = Math.sqrt(uu * uu + vv * vv + ww * ww);
        if (spd < 1e-9) {
          this._m4.makeScale(0, 0, 0);
          this.arrowMesh.setMatrixAt(a, this._m4);
          continue;
        }
        this._dir.set(uu / spd, vv / spd, ww / spd);
        this._q.setFromUnitVectors(this._up, this._dir);
        const len = (spd / speedMax) * this.maxLen;
        this._pos.set(cx, cy, cz);
        this._scl.set(1, len, 1);
        this._m4.compose(this._pos, this._q, this._scl);
        this.arrowMesh.setMatrixAt(a, this._m4);

        const tc = spd / speedMax;
        const li = (tc > 1 ? 1 : tc) * 255 | 0;
        this.arrowMesh.instanceColor.array[a * 3] = lut[li * 3];
        this.arrowMesh.instanceColor.array[a * 3 + 1] = lut[li * 3 + 1];
        this.arrowMesh.instanceColor.array[a * 3 + 2] = lut[li * 3 + 2];
      }
      this.arrowMesh.instanceMatrix.needsUpdate = true;
      this.arrowMesh.instanceColor.needsUpdate = true;
      this.arrowMesh.visible = true;
    }

    setArrowsVisible(on) { if (this.arrowMesh) this.arrowMesh.visible = on; }

    /* ---- cross-section ---- */
    setCrossSection(axis, pos, enabled) {
      if (!this.loader) return;
      const mat = this.voxelMaterial;
      const amat = this.arrowMaterial;
      if (!enabled) {
        if (mat) mat.clippingPlanes = [];
        if (amat) amat.clippingPlanes = [];
        return;
      }
      const sp = this.loader.spacing, o = this.loader.origin;
      const axisIdx = axis === 'x' ? 0 : axis === 'y' ? 1 : 2;
      const cut = o[axisIdx] + (pos + 0.5) * sp[axisIdx];
      const nrm = new THREE.Vector3(axis === 'x' ? -1 : 0, axis === 'y' ? -1 : 0, axis === 'z' ? -1 : 0);
      this.clipPlane.normal.copy(nrm);
      this.clipPlane.constant = cut;
      if (mat) mat.clippingPlanes = [this.clipPlane];
      if (amat) amat.clippingPlanes = [this.clipPlane];
    }

    /* ---- picking ---- */
    pick(clientX, clientY) {
      if (!this.voxelMesh) return null;
      const rect = this.canvas.getBoundingClientRect();
      this.pointer.x = ((clientX - rect.left) / rect.width) * 2 - 1;
      this.pointer.y = -((clientY - rect.top) / rect.height) * 2 + 1;
      this.raycaster.setFromCamera(this.pointer, this.camera);
      const hits = this.raycaster.intersectObject(this.voxelMesh);
      if (hits.length && hits[0].instanceId !== undefined) {
        const idx = hits[0].instanceId;
        const [i, j, k] = this.loader.idxToIJK(idx);
        return { idx, i, j, k };
      }
      return null;
    }

    highlight(idx) {
      if (!this.highlightMesh) return;
      const cx = this.basePos[idx * 3], cy = this.basePos[idx * 3 + 1], cz = this.basePos[idx * 3 + 2];
      this.highlightMesh.position.set(cx, cy, cz);
      this.highlightMesh.visible = true;
    }

    /* 2D slice extraction for charts */
    extractSlice(field, frame, axis, pos) {
      const loader = this.loader;
      const arr = field === 'speed' ? frame.speed : frame[field];
      const nx = loader.nx, ny = loader.ny, nz = loader.nz;
      let w, h;
      if (axis === 'x') { w = ny; h = nz; }
      else if (axis === 'y') { w = nx; h = nz; }
      else { w = nx; h = ny; }
      const out = new Float32Array(w * h);
      for (let a = 0; a < w; a++) {
        for (let b = 0; b < h; b++) {
          let idx;
          if (axis === 'x') idx = pos * ny * nz + a * nz + b;
          else if (axis === 'y') idx = a * ny * nz + pos * nz + b;
          else idx = a * ny * nz + b * nz + pos;
          out[a + b * w] = arr ? arr[idx] : 0;
        }
      }
      return { data: out, w, h };
    }
  }

  global.Viewer3D = Viewer3D;
})(window);
