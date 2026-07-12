/* colormaps.js — scientific colormaps as control-point LUTs (0..255 RGB) */
(function (global) {
  'use strict';

  // control points: [t, r, g, b] with t in [0,1]
  const PALETTES = {
    viridis: [
      [0.0, 68, 1, 84], [0.25, 59, 82, 139], [0.5, 33, 145, 140],
      [0.75, 94, 201, 98], [1.0, 253, 231, 37],
    ],
    plasma: [
      [0.0, 13, 8, 135], [0.25, 126, 3, 168], [0.5, 204, 71, 120],
      [0.75, 248, 149, 64], [1.0, 240, 249, 33],
    ],
    turbo: [
      [0.0, 48, 18, 59], [0.125, 70, 107, 227], [0.25, 42, 176, 242],
      [0.375, 33, 201, 175], [0.5, 77, 213, 95], [0.625, 155, 223, 50],
      [0.75, 222, 216, 47], [0.875, 248, 170, 40], [1.0, 180, 55, 15],
    ],
    jet: [
      [0.0, 0, 0, 128], [0.125, 0, 0, 255], [0.375, 0, 255, 255],
      [0.625, 255, 255, 0], [0.875, 255, 0, 0], [1.0, 128, 0, 0],
    ],
    coolwarm: [
      [0.0, 59, 76, 192], [0.5, 221, 221, 221], [1.0, 180, 4, 38],
    ],
    inferno: [
      [0.0, 0, 0, 4], [0.25, 66, 10, 104], [0.5, 147, 38, 103],
      [0.75, 221, 81, 58], [1.0, 252, 255, 164],
    ],
    grayscale: [
      [0.0, 0, 0, 0], [1.0, 255, 255, 255],
    ],
  };

  const LUT_SIZE = 256;
  const lutCache = {};
  const linCache = {};

  function srgbToLinear(c) {
    c /= 255;
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  }

  function buildLUT(name) {
    if (lutCache[name]) return lutCache[name];
    const pts = PALETTES[name] || PALETTES.viridis;
    const lut = new Uint8Array(LUT_SIZE * 3);
    let seg = 0;
    for (let i = 0; i < LUT_SIZE; i++) {
      const t = i / (LUT_SIZE - 1);
      while (seg < pts.length - 2 && t > pts[seg + 1][0]) seg++;
      const a = pts[seg], b = pts[seg + 1];
      const span = b[0] - a[0] || 1;
      let f = (t - a[0]) / span;
      f = f < 0 ? 0 : f > 1 ? 1 : f;
      lut[i * 3] = Math.round(a[1] + (b[1] - a[1]) * f);
      lut[i * 3 + 1] = Math.round(a[2] + (b[2] - a[2]) * f);
      lut[i * 3 + 2] = Math.round(a[3] + (b[3] - a[3]) * f);
    }
    lutCache[name] = lut;
    return lut;
  }

  function buildLinLUT(name) {
    if (linCache[name]) return linCache[name];
    const s = buildLUT(name);
    const out = new Float32Array(LUT_SIZE * 3);
    for (let i = 0; i < LUT_SIZE * 3; i++) out[i] = srgbToLinear(s[i]);
    linCache[name] = out;
    return out;
  }

  const Colormaps = {
    names: Object.keys(PALETTES),
    getLUT(name) { return buildLUT(name); },
    getLinLUT(name) { return buildLinLUT(name); },
    /* sample to [r,g,b] 0..255 */
    sample(name, t) {
      const lut = buildLUT(name);
      if (t < 0) t = 0; else if (t > 1) t = 1;
      const idx = (t * (LUT_SIZE - 1)) | 0;
      return [lut[idx * 3], lut[idx * 3 + 1], lut[idx * 3 + 2]];
    },
  };

  global.Colormaps = Colormaps;
})(window);
