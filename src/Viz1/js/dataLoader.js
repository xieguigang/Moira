/* dataLoader.js — folder upload, metadata parse, lazy frame load + LRU cache */
(function (global) {
  'use strict';

  const CACHE_CAP = 12; // max frames kept in memory

  class DataLoader {
    constructor() {
      this.fileMap = new Map();      // relativePath -> File
      this.metadata = null;
      this.framesMeta = [];          // [{step, time, file, File}]
      this.nx = this.ny = this.nz = 0;
      this.N = 0;
      this.origin = [0, 0, 0];
      this.spacing = [1, 1, 1];
      this.cache = new Map();        // index -> FrameData
      this.inflight = new Map();     // index -> Promise
      this.rangeCache = {};          // field -> {min, max}
    }

    /* ---- upload & parse ---- */
    async loadFiles(fileList) {
      this.fileMap.clear();
      for (const f of fileList) {
        const key = f.webkitRelativePath || f.name;
        this.fileMap.set(key, f);
      }
      // locate metadata.json
      let metaKey = null;
      for (const k of this.fileMap.keys()) {
        const lower = k.toLowerCase();
        if (lower.endsWith('metadata.json')) { metaKey = k; break; }
      }
      if (!metaKey) throw new Error('NO_METADATA');

      const metaFile = this.fileMap.get(metaKey);
      const metaText = await metaFile.text();
      const meta = JSON.parse(metaText);
      this.metadata = meta;

      const g = meta.Grid;
      this.nx = g.Nx; this.ny = g.Ny; this.nz = g.Nz;
      this.N = g.Nx * g.Ny * g.Nz;
      this.origin = g.Origin || [0, 0, 0];
      this.spacing = g.Spacing || [1, 1, 1];

      // resolve frame files relative to metadata directory
      const dir = metaKey.includes('/') ? metaKey.substring(0, metaKey.lastIndexOf('/')) : '';
      this.framesMeta = [];
      for (const fr of (meta.Frames || [])) {
        let full = dir ? dir + '/' + fr.File : fr.File;
        let file = this.fileMap.get(full) || this.fileMap.get(fr.File);
        this.framesMeta.push({ step: fr.Step, time: fr.Time, file: fr.File, File: file });
      }
      return meta;
    }

    get frameCount() { return this.framesMeta.length; }

    /* ---- frame access (lazy + LRU) ---- */
    getFrame(index) {
      if (index < 0 || index >= this.framesMeta.length) return Promise.resolve(null);
      if (this.cache.has(index)) {
        // refresh LRU order
        const v = this.cache.get(index);
        this.cache.delete(index); this.cache.set(index, v);
        return Promise.resolve(v);
      }
      if (this.inflight.has(index)) return this.inflight.get(index);

      const p = this._loadFrame(index).then((data) => {
        this.inflight.delete(index);
        this.cache.set(index, data);
        // evict
        while (this.cache.size > CACHE_CAP) {
          const oldest = this.cache.keys().next().value;
          this.cache.delete(oldest);
        }
        return data;
      }).catch((e) => { this.inflight.delete(index); throw e; });

      this.inflight.set(index, p);
      return p;
    }

    async _loadFrame(index) {
      const fm = this.framesMeta[index];
      if (!fm || !fm.File) return null;
      const text = await fm.File.text();
      const obj = JSON.parse(text);
      const f = obj.fields;
      const N = this.N;
      const toFA = (arr) => {
        const out = new Float32Array(N);
        for (let i = 0; i < N; i++) out[i] = arr[i];
        return out;
      };
      const pressure = toFA(f.pressure);
      const density = toFA(f.density);
      const u = toFA(f.u);
      const v = toFA(f.v);
      const w = toFA(f.w);
      // speed
      const speed = new Float32Array(N);
      for (let i = 0; i < N; i++) speed[i] = Math.sqrt(u[i] * u[i] + v[i] * v[i] + w[i] * w[i]);

      const data = {
        step: obj.step, time: obj.time, index,
        pressure, density, u, v, w, speed,
      };
      this._updateRange('pressure', pressure);
      this._updateRange('density', density);
      this._updateRange('speed', speed);
      return data;
    }

    _updateRange(field, arr) {
      let r = this.rangeCache[field];
      if (!r) { r = { min: Infinity, max: -Infinity }; this.rangeCache[field] = r; }
      for (let i = 0; i < arr.length; i++) {
        const x = arr[i];
        if (x < r.min) r.min = x;
        if (x > r.max) r.max = x;
      }
    }

    getRange(field) {
      const r = this.rangeCache[field];
      if (r && isFinite(r.min) && isFinite(r.max)) return [r.min, r.max];
      return [0, 1];
    }

    /* ---- voxel time series (iterates all frames) ---- */
    async getVoxelSeries(index, field, onProgress) {
      const n = this.framesMeta.length;
      const values = new Float32Array(n);
      const times = new Float32Array(n);
      for (let i = 0; i < n; i++) {
        const fd = await this.getFrame(i);
        const arr = field === 'speed' ? fd.speed : fd[field];
        values[i] = arr ? arr[index] : 0;
        times[i] = this.framesMeta[i].time;
        if (onProgress) onProgress(i + 1, n);
      }
      return { values, times };
    }

    /* index -> (i,j,k) given i*ny*nz + j*nz + k */
    idxToIJK(idx) {
      const nz = this.nz, ny = this.ny;
      const i = Math.floor(idx / (ny * nz));
      const rem = idx - i * ny * nz;
      const j = Math.floor(rem / nz);
      const k = rem - j * nz;
      return [i, j, k];
    }
  }

  global.DataLoader = DataLoader;
})(window);
