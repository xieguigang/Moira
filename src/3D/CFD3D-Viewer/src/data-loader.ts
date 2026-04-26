/**
 * data-loader.ts - Binary Data Loader for 3D CFD Simulation Results
 *
 * Loads the binary output format produced by the VB.NET CFD3D simulation:
 *   metadata.json  - Simulation metadata
 *   barrier.bin    - Barrier mask (1 byte per cell)
 *   frame_XXXX.bin - Frame data (4 doubles per cell: ux, uy, uz, rho)
 */

export interface Metadata {
  dims: number[];          // [NX, NY, NZ]
  totalCells: number;
  totalFrames: number;
  viscosity: number;
  snapshotInterval: number;
  totalIterations: number;
  description: string;
  fields: string[];
  ranges: Record<string, number[]>;  // { field: [min, max] }
}

export interface FrameData {
  ux: Float64Array;
  uy: Float64Array;
  uz: Float64Array;
  rho: Float64Array;
  speed2: Float64Array;
}

export interface SimulationData {
  metadata: Metadata;
  barrier: Uint8Array;
  frames: FrameData[];
}

/**
 * Load simulation data from a directory URL.
 * All paths are relative to the base URL.
 */
export async function loadSimulationData(baseUrl: string): Promise<SimulationData> {
  // 1. Load metadata
  const metaUrl = `${baseUrl}/metadata.json`;
  const metaResp = await fetch(metaUrl);
  if (!metaResp.ok) throw new Error(`Failed to load metadata from ${metaUrl}`);
  const metadata: Metadata = await metaResp.json();

  const [nx, ny, nz] = metadata.dims;
  const totalCells = nx * ny * nz;

  // 2. Load barrier
  const barrierUrl = `${baseUrl}/barrier.bin`;
  const barrierResp = await fetch(barrierUrl);
  if (!barrierResp.ok) throw new Error(`Failed to load barrier from ${barrierUrl}`);
  const barrierBuffer = await barrierResp.arrayBuffer();
  const barrier = new Uint8Array(barrierBuffer);

  // 3. Load frames
  const frames: FrameData[] = [];
  const frameCount = metadata.totalFrames;

  for (let i = 1; i <= frameCount; i++) {
    const frameUrl = `${baseUrl}/frame_${String(i).PadLeft(4, '0')}.bin`;
    const frameResp = await fetch(frameUrl);
    if (!frameResp.ok) throw new Error(`Failed to load frame ${i} from ${frameUrl}`);
    const frameBuffer = await frameResp.arrayBuffer();
    const doubles = new Float64Array(frameBuffer);

    // Each cell has 4 doubles: ux, uy, uz, rho
    const expectedSize = totalCells * 4;
    if (doubles.length !== expectedSize) {
      throw new Error(
        `Frame ${i} size mismatch: expected ${expectedSize} doubles, got ${doubles.length}`
      );
    }

    const ux = new Float64Array(totalCells);
    const uy = new Float64Array(totalCells);
    const uz = new Float64Array(totalCells);
    const rho = new Float64Array(totalCells);
    const speed2 = new Float64Array(totalCells);

    for (let c = 0; c < totalCells; c++) {
      const base = c * 4;
      ux[c] = doubles[base];
      uy[c] = doubles[base + 1];
      uz[c] = doubles[base + 2];
      rho[c] = doubles[base + 3];
      speed2[c] = ux[c] * ux[c] + uy[c] * uy[c] + uz[c] * uz[c];
    }

    frames.push({ ux, uy, uz, rho, speed2 });
  }

  return { metadata, barrier, frames };
}

/**
 * Generate demo data for testing when no real simulation data is available.
 * Creates a synthetic vortex flow pattern in a cylindrical tank.
 */
export function generateDemoData(): SimulationData {
  const nx = 50, ny = 50, nz = 60;
  const totalCells = nx * ny * nz;
  const centerX = nx / 2, centerY = ny / 2;
  const tankRadius = 22;

  // Build barrier
  const barrier = new Uint8Array(totalCells);
  for (let z = 0; z < nz; z++) {
    for (let y = 0; y < ny; y++) {
      for (let x = 0; x < nx; x++) {
        const idx = x + y * nx + z * nx * ny;
        const dx = x - centerX;
        const dy = y - centerY;
        const dist = Math.sqrt(dx * dx + dy * dy);

        if (dist > tankRadius || z === 0 || z === nz - 1) {
          barrier[idx] = 1;
        }
        // Shaft
        if (dist < 1.5 && z > 18) {
          barrier[idx] = 1;
        }
        // Impeller at z=18
        if (z >= 18 && z <= 19 && dist >= 2 && dist <= 14) {
          barrier[idx] = 1;
        }
      }
    }
  }

  // Generate 5 frames with evolving vortex
  const frames: FrameData[] = [];
  const numFrames = 5;

  for (let f = 0; f < numFrames; f++) {
    const phase = (f / numFrames) * Math.PI * 2;
    const ux = new Float64Array(totalCells);
    const uy = new Float64Array(totalCells);
    const uz = new Float64Array(totalCells);
    const rho = new Float64Array(totalCells);
    const speed2 = new Float64Array(totalCells);

    for (let z = 0; z < nz; z++) {
      for (let y = 0; y < ny; y++) {
        for (let x = 0; x < nx; x++) {
          const idx = x + y * nx + z * nx * ny;
          if (barrier[idx]) continue;

          const dx = x - centerX;
          const dy = y - centerY;
          const dist = Math.sqrt(dx * dx + dy * dy);
          const normDist = dist / tankRadius;

          // Tangential velocity (vortex)
          const tangentialSpeed = 0.08 * Math.sin(normDist * Math.PI) *
            (1 + 0.3 * Math.sin(phase + z * 0.1));
          const angle = Math.atan2(dy, dx);

          ux[idx] = -tangentialSpeed * Math.sin(angle);
          uy[idx] = tangentialSpeed * Math.cos(angle);

          // Axial circulation (up near center, down near walls)
          const axialSpeed = 0.03 * Math.cos(normDist * Math.PI * 2) *
            Math.sin(phase * 0.5 + z * 0.05);
          uz[idx] = axialSpeed;

          // Density perturbation
          rho[idx] = 1.0 + 0.01 * Math.sin(normDist * Math.PI * 3 + phase);

          speed2[idx] = ux[idx] * ux[idx] + uy[idx] * uy[idx] + uz[idx] * uz[idx];
        }
      }
    }

    frames.push({ ux, uy, uz, rho, speed2 });
  }

  // Compute ranges
  let speedMin = Infinity, speedMax = -Infinity;
  for (const frame of frames) {
    for (let i = 0; i < totalCells; i++) {
      if (!barrier[i]) {
        const s = Math.sqrt(frame.speed2[i]);
        if (s < speedMin) speedMin = s;
        if (s > speedMax) speedMax = s;
      }
    }
  }

  const metadata: Metadata = {
    dims: [nx, ny, nz],
    totalCells,
    totalFrames: numFrames,
    viscosity: 0.02,
    snapshotInterval: 100,
    totalIterations: 500,
    description: 'Demo: Synthetic vortex flow in cylindrical tank',
    fields: ['ux', 'uy', 'uz', 'rho', 'speed2'],
    ranges: {
      ux: [-0.1, 0.1],
      uy: [-0.1, 0.1],
      uz: [-0.05, 0.05],
      rho: [0.99, 1.01],
      speed2: [0, speedMax * speedMax],
    },
  };

  return { metadata, barrier, frames };
}
