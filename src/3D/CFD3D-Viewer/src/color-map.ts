/**
 * color-map.ts - Scientific Color Maps for CFD Visualization
 *
 * Provides several perceptually uniform and diverging colormaps
 * commonly used in CFD and scientific visualization.
 */

export interface ColorMap {
  name: string;
  /** Map a normalized value [0, 1] to [r, g, b] in [0, 255] */
  map(t: number): [number, number, number];
}

/** Jet-like colormap (blue -> cyan -> green -> yellow -> red) */
export const jet: ColorMap = {
  name: 'Jet',
  map(t: number): [number, number, number] {
    t = Math.max(0, Math.min(1, t));
    let r: number, g: number, b: number;
    if (t < 0.25) {
      r = 0;
      g = Math.round(255 * (t / 0.25));
      b = 255;
    } else if (t < 0.5) {
      r = 0;
      g = 255;
      b = Math.round(255 * (1 - (t - 0.25) / 0.25));
    } else if (t < 0.75) {
      r = Math.round(255 * ((t - 0.5) / 0.25));
      g = 255;
      b = 0;
    } else {
      r = 255;
      g = Math.round(255 * (1 - (t - 0.75) / 0.25));
      b = 0;
    }
    return [r, g, b];
  },
};

/** Cool-Warm diverging colormap (blue -> white -> red) */
export const coolwarm: ColorMap = {
  name: 'CoolWarm',
  map(t: number): [number, number, number] {
    t = Math.max(0, Math.min(1, t));
    if (t < 0.5) {
      const s = t / 0.5;
      return [
        Math.round(59 + s * 196),
        Math.round(76 + s * 179),
        Math.round(192 + s * 63),
      ];
    } else {
      const s = (t - 0.5) / 0.5;
      return [
        Math.round(255),
        Math.round(255 - s * 179),
        Math.round(255 - s * 192),
      ];
    }
  },
};

/** Viridis-like colormap (dark purple -> blue -> teal -> yellow) */
export const viridis: ColorMap = {
  name: 'Viridis',
  map(t: number): [number, number, number] {
    t = Math.max(0, Math.min(1, t));
    // Simplified viridis approximation
    const r = Math.round(
      68 + t * (183 - 68) + t * t * (253 - 183) * (1 - t)
    );
    const g = Math.round(
      1 + t * (228 - 1) + t * t * (231 - 228) * (1 - t)
    );
    const b = Math.round(
      84 + t * (50 - 84) + t * t * (37 - 50) * (1 - t)
    );
    return [
      Math.max(0, Math.min(255, r)),
      Math.max(0, Math.min(255, g)),
      Math.max(0, Math.min(255, b)),
    ];
  },
};

/** Plasma colormap */
export const plasma: ColorMap = {
  name: 'Plasma',
  map(t: number): [number, number, number] {
    t = Math.max(0, Math.min(1, t));
    const r = Math.round(13 + t * 240 + t * t * 2);
    const g = Math.round(8 + t * 50 + t * t * 200);
    const b = Math.round(135 + t * 100 - t * t * 200);
    return [
      Math.max(0, Math.min(255, r)),
      Math.max(0, Math.min(255, g)),
      Math.max(0, Math.min(255, b)),
    ];
  },
};

/** Generate a canvas gradient for the colorbar UI */
export function drawColorbar(
  canvas: HTMLCanvasElement,
  cmap: ColorMap,
  minVal: number,
  maxVal: number
): void {
  const ctx = canvas.getContext('2d')!;
  const w = canvas.width;
  const h = canvas.height;

  for (let x = 0; x < w; x++) {
    const t = x / (w - 1);
    const [r, g, b] = cmap.map(t);
    ctx.fillStyle = `rgb(${r},${g},${b})`;
    ctx.fillRect(x, 0, 1, h);
  }
}

/** Map a value to a normalized [0, 1] range */
export function normalize(value: number, min: number, max: number): number {
  if (max === min) return 0.5;
  return (value - min) / (max - min);
}
