// palettes.js —— 颜色调色板（t∈[0,1] → RGB）与图例渲染

const STOPS = {
  viridis: [
    [0.0, 68, 1, 84], [0.1, 72, 40, 120], [0.2, 62, 74, 137], [0.3, 49, 104, 142],
    [0.4, 38, 130, 142], [0.5, 31, 158, 137], [0.6, 53, 183, 121], [0.7, 110, 206, 88],
    [0.8, 181, 222, 43], [1.0, 253, 231, 37]
  ],
  plasma: [
    [0.0, 13, 8, 135], [0.25, 126, 3, 168], [0.5, 204, 71, 120], [0.75, 248, 149, 64], [1.0, 240, 249, 33]
  ],
  inferno: [
    [0.0, 0, 0, 4], [0.25, 87, 16, 110], [0.5, 188, 55, 84], [0.75, 249, 142, 9], [1.0, 252, 255, 164]
  ],
  turbo: [
    [0.0, 48, 18, 59], [0.125, 70, 107, 227], [0.25, 40, 176, 235], [0.375, 45, 225, 166],
    [0.5, 145, 255, 77], [0.625, 232, 240, 31], [0.75, 252, 168, 20], [0.875, 215, 69, 10], [1.0, 122, 4, 3]
  ],
  jet: [
    [0.0, 0, 0, 143], [0.125, 0, 0, 255], [0.25, 0, 255, 255], [0.375, 0, 255, 0],
    [0.5, 255, 255, 0], [0.625, 255, 128, 0], [0.75, 255, 0, 0], [0.875, 128, 0, 0], [1.0, 128, 0, 0]
  ],
  coolwarm: [
    [0.0, 59, 76, 192], [0.5, 221, 221, 221], [1.0, 180, 4, 38]
  ],
  grayscale: [
    [0.0, 20, 20, 20], [1.0, 240, 240, 240]
  ]
};

export const PALETTE_NAMES = Object.keys(STOPS);

function interp(stops, t) {
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  let i = 0;
  while (i < stops.length - 1 && t > stops[i + 1][0]) i++;
  const a = stops[i], b = stops[Math.min(i + 1, stops.length - 1)];
  const span = (b[0] - a[0]) || 1;
  const f = (t - a[0]) / span;
  return [
    Math.round(a[1] + (b[1] - a[1]) * f),
    Math.round(a[2] + (b[2] - a[2]) * f),
    Math.round(a[3] + (b[3] - a[3]) * f)
  ];
}

export function samplePalette(name, t) {
  const stops = STOPS[name] || STOPS.viridis;
  return interp(stops, t);
}

// 预计算 256 级查找表，返回 Uint8Array(steps*3)
export function buildLUT(name, steps = 256) {
  const stops = STOPS[name] || STOPS.viridis;
  const lut = new Uint8Array(steps * 3);
  for (let i = 0; i < steps; i++) {
    const [r, g, b] = interp(stops, i / (steps - 1));
    lut[i * 3] = r; lut[i * 3 + 1] = g; lut[i * 3 + 2] = b;
  }
  return lut;
}

// 将图例绘制到 canvas（水平渐变）
export function drawLegend(canvas, name, steps = 256) {
  const w = canvas.width, h = canvas.height;
  const ctx = canvas.getContext('2d');
  const img = ctx.createImageData(w, h);
  const lut = buildLUT(name, steps);
  for (let x = 0; x < w; x++) {
    const idx = Math.min(steps - 1, Math.floor((x / (w - 1)) * (steps - 1))) * 3;
    for (let y = 0; y < h; y++) {
      const p = (y * w + x) * 4;
      img.data[p] = lut[idx]; img.data[p + 1] = lut[idx + 1]; img.data[p + 2] = lut[idx + 2]; img.data[p + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
}
