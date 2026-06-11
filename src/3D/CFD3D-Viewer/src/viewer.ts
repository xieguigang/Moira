/**
 * viewer.ts - 3D CFD Visualization Engine
 *
 * Uses Three.js to render:
 *   - Cross-sectional slices with color-mapped scalar fields
 *   - Velocity vector arrows on slices
 *   - Barrier/obstacle geometry (tank walls, impeller, shaft)
 *   - Optional volume rendering
 */

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { ColorMap, jet, normalize, drawColorbar } from './color-map';
import type { Metadata, FrameData } from './data-loader';

export interface ViewerConfig {
  field: 'speed' | 'ux' | 'uy' | 'uz' | 'rho';
  sliceAxis: 'x' | 'y' | 'z';
  slicePosition: number;
  showArrows: boolean;
  showBarrier: boolean;
  arrowDensity: number;
  colorMap: ColorMap;
}

export class CFDViewer {
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private renderer: THREE.WebGLRenderer;
  private controls: OrbitControls;

  private metadata!: Metadata;
  private barrier!: Uint8Array;
  private currentFrame!: FrameData;

  // Three.js objects
  private sliceMesh: THREE.Mesh | null = null;
  private arrowGroup: THREE.Group;
  private barrierMesh: THREE.Mesh | null = null;
  private tankOutline: THREE.LineSegments | null = null;

  // Config
  private config: ViewerConfig = {
    field: 'speed',
    sliceAxis: 'z',
    slicePosition: 25,
    showArrows: true,
    showBarrier: true,
    arrowDensity: 3,
    colorMap: jet,
  };

  private container: HTMLElement;

  constructor(container: HTMLElement) {
    this.container = container;

    // Scene
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0a0e17);

    // Camera
    const aspect = container.clientWidth / container.clientHeight;
    this.camera = new THREE.PerspectiveCamera(50, aspect, 0.1, 1000);
    this.camera.position.set(60, 60, 80);
    this.camera.up.set(0, 0, 1); // Z is up

    // Renderer
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    this.renderer.setPixelRatio(window.devicePixelRatio);
    container.appendChild(this.renderer.domElement);

    // Controls
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.target.set(25, 25, 30);

    // Arrow group
    this.arrowGroup = new THREE.Group();
    this.scene.add(this.arrowGroup);

    // Lights
    const ambient = new THREE.AmbientLight(0x404060, 1.5);
    this.scene.add(ambient);
    const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
    dirLight.position.set(50, 50, 100);
    this.scene.add(dirLight);

    // Grid helper
    const grid = new THREE.GridHelper(100, 20, 0x1a2332, 0x111827);
    grid.rotation.x = Math.PI / 2;
    this.scene.add(grid);

    // Resize handler
    window.addEventListener('resize', () => this.onResize());

    // Start render loop
    this.animate();
  }

  /** Initialize with simulation data */
  init(metadata: Metadata, barrier: Uint8Array): void {
    this.metadata = metadata;
    this.barrier = barrier;

    const [nx, ny, nz] = metadata.dims;

    // Center the scene
    this.controls.target.set(nx / 2, ny / 2, nz / 2);
    this.camera.position.set(nx * 1.2, ny * 1.2, nz * 1.2);

    // Build barrier geometry
    this.buildBarrierMesh();

    // Build tank outline
    this.buildTankOutline();

    // Set default slice position to middle
    this.config.slicePosition = Math.floor(
      this.config.sliceAxis === 'x' ? nx / 2 :
        this.config.sliceAxis === 'y' ? ny / 2 : nz / 2
    );
  }

  /** Update to show a specific frame */
  setFrame(frame: FrameData): void {
    this.currentFrame = frame;
    this.updateSlice();
    this.updateArrows();
  }

  /** Update visualization config */
  setConfig(config: Partial<ViewerConfig>): void {
    Object.assign(this.config, config);
    this.updateSlice();
    this.updateArrows();
  }

  /** Get current field value range */
  getFieldRange(): [number, number] {
    if (!this.metadata || !this.currentFrame) return [0, 1];
    const ranges = this.metadata.ranges;
    const fieldKey = this.config.field === 'speed' ? 'speed2' : this.config.field;
    const range = ranges[fieldKey];
    if (!range) return [0, 1];
    if (this.config.field === 'speed') {
      return [0, Math.sqrt(Math.max(0, range[1]))];
    }
    return [range[0], range[1]];
  }

  /** Get max slice position for current axis */
  getMaxSlice(): number {
    if (!this.metadata) return 50;
    const [nx, ny, nz] = this.metadata.dims;
    return this.config.sliceAxis === 'x' ? nx - 1 :
      this.config.sliceAxis === 'y' ? ny - 1 : nz - 1;
  }

  // ========================================================================
  //  SLICE RENDERING
  // ========================================================================

  private updateSlice(): void {
    if (!this.metadata || !this.currentFrame) return;

    // Remove old slice
    if (this.sliceMesh) {
      this.scene.remove(this.sliceMesh);
      this.sliceMesh.geometry.dispose();
      (this.sliceMesh.material as THREE.Material).dispose();
      this.sliceMesh = null;
    }

    const [nx, ny, nz] = this.metadata.dims;
    const pos = this.config.slicePosition;
    const field = this.config.field;
    const cmap = this.config.colorMap;
    const [minVal, maxVal] = this.getFieldRange();

    let sliceW: number, sliceH: number;
    let vertices: number[] = [];
    let colors: number[] = [];
    let indices: number[] = [];

    if (this.config.sliceAxis === 'z') {
      // Horizontal slice (XY plane at z=pos)
      sliceW = nx;
      sliceH = ny;
      for (let y = 0; y < ny - 1; y++) {
        for (let x = 0; x < nx - 1; x++) {
          const i00 = x + y * nx + pos * nx * ny;
          const i10 = (x + 1) + y * nx + pos * nx * ny;
          const i01 = x + (y + 1) * nx + pos * nx * ny;
          const i11 = (x + 1) + (y + 1) * nx + pos * nx * ny;

          if (this.barrier[i00] || this.barrier[i10] || this.barrier[i01] || this.barrier[i11]) continue;

          const baseIdx = vertices.length / 3;
          vertices.push(x, y, pos, x + 1, y, pos, x, y + 1, pos, x + 1, y + 1, pos);

          const v00 = this.getFieldValue(i00);
          const v10 = this.getFieldValue(i10);
          const v01 = this.getFieldValue(i01);
          const v11 = this.getFieldValue(i11);

          const c00 = cmap.map(normalize(v00, minVal, maxVal));
          const c10 = cmap.map(normalize(v10, minVal, maxVal));
          const c01 = cmap.map(normalize(v01, minVal, maxVal));
          const c11 = cmap.map(normalize(v11, minVal, maxVal));

          colors.push(
            c00[0] / 255, c00[1] / 255, c00[2] / 255,
            c10[0] / 255, c10[1] / 255, c10[2] / 255,
            c01[0] / 255, c01[1] / 255, c01[2] / 255,
            c11[0] / 255, c11[1] / 255, c11[2] / 255,
          );

          indices.push(baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 1, baseIdx + 3, baseIdx + 2);
        }
      }
    } else if (this.config.sliceAxis === 'x') {
      // X slice (YZ plane at x=pos)
      sliceW = ny;
      sliceH = nz;
      for (let z = 0; z < nz - 1; z++) {
        for (let y = 0; y < ny - 1; y++) {
          const i00 = pos + y * nx + z * nx * ny;
          const i10 = pos + (y + 1) * nx + z * nx * ny;
          const i01 = pos + y * nx + (z + 1) * nx * ny;
          const i11 = pos + (y + 1) * nx + (z + 1) * nx * ny;

          if (this.barrier[i00] || this.barrier[i10] || this.barrier[i01] || this.barrier[i11]) continue;

          const baseIdx = vertices.length / 3;
          vertices.push(pos, y, z, pos, y + 1, z, pos, y, z + 1, pos, y + 1, z + 1);

          const v00 = this.getFieldValue(i00);
          const v10 = this.getFieldValue(i10);
          const v01 = this.getFieldValue(i01);
          const v11 = this.getFieldValue(i11);

          const c00 = cmap.map(normalize(v00, minVal, maxVal));
          const c10 = cmap.map(normalize(v10, minVal, maxVal));
          const c01 = cmap.map(normalize(v01, minVal, maxVal));
          const c11 = cmap.map(normalize(v11, minVal, maxVal));

          colors.push(
            c00[0] / 255, c00[1] / 255, c00[2] / 255,
            c10[0] / 255, c10[1] / 255, c10[2] / 255,
            c01[0] / 255, c01[1] / 255, c01[2] / 255,
            c11[0] / 255, c11[1] / 255, c11[2] / 255,
          );

          indices.push(baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 1, baseIdx + 3, baseIdx + 2);
        }
      }
    } else {
      // Y slice (XZ plane at y=pos)
      sliceW = nx;
      sliceH = nz;
      for (let z = 0; z < nz - 1; z++) {
        for (let x = 0; x < nx - 1; x++) {
          const i00 = x + pos * nx + z * nx * ny;
          const i10 = (x + 1) + pos * nx + z * nx * ny;
          const i01 = x + pos * nx + (z + 1) * nx * ny;
          const i11 = (x + 1) + pos * nx + (z + 1) * nx * ny;

          if (this.barrier[i00] || this.barrier[i10] || this.barrier[i01] || this.barrier[i11]) continue;

          const baseIdx = vertices.length / 3;
          vertices.push(x, pos, z, x + 1, pos, z, x, pos, z + 1, x + 1, pos, z + 1);

          const v00 = this.getFieldValue(i00);
          const v10 = this.getFieldValue(i10);
          const v01 = this.getFieldValue(i01);
          const v11 = this.getFieldValue(i11);

          const c00 = cmap.map(normalize(v00, minVal, maxVal));
          const c10 = cmap.map(normalize(v10, minVal, maxVal));
          const c01 = cmap.map(normalize(v01, minVal, maxVal));
          const c11 = cmap.map(normalize(v11, minVal, maxVal));

          colors.push(
            c00[0] / 255, c00[1] / 255, c00[2] / 255,
            c10[0] / 255, c10[1] / 255, c10[2] / 255,
            c01[0] / 255, c01[1] / 255, c01[2] / 255,
            c11[0] / 255, c11[1] / 255, c11[2] / 255,
          );

          indices.push(baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 1, baseIdx + 3, baseIdx + 2);
        }
      }
    }

    if (vertices.length === 0) return;

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
    geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
    geometry.setIndex(indices);
    geometry.computeVertexNormals();

    const material = new THREE.MeshBasicMaterial({
      vertexColors: true,
      side: THREE.DoubleSide,
      transparent: true,
      opacity: 0.9,
    });

    this.sliceMesh = new THREE.Mesh(geometry, material);
    this.scene.add(this.sliceMesh);
  }

  private getFieldValue(idx: number): number {
    if (!this.currentFrame) return 0;
    switch (this.config.field) {
      case 'speed': return Math.sqrt(Math.max(0, this.currentFrame.speed2[idx]));
      case 'ux': return this.currentFrame.ux[idx];
      case 'uy': return this.currentFrame.uy[idx];
      case 'uz': return this.currentFrame.uz[idx];
      case 'rho': return this.currentFrame.rho[idx];
      default: return 0;
    }
  }

  // ========================================================================
  //  VELOCITY ARROWS
  // ========================================================================

  private updateArrows(): void {
    // Clear old arrows
    while (this.arrowGroup.children.length > 0) {
      const child = this.arrowGroup.children[0];
      this.arrowGroup.remove(child);
      if (child instanceof THREE.ArrowHelper) {
        child.line.geometry.dispose();
        child.cone.geometry.dispose();
      }
    }

    if (!this.metadata || !this.currentFrame || !this.config.showArrows) return;

    const [nx, ny, nz] = this.metadata.dims;
    const step = this.config.arrowDensity;
    const pos = this.config.slicePosition;
    const arrowScale = 5; // Scale factor for arrow length

    // Find max speed for normalization
    let maxSpeed = 0;
    for (let i = 0; i < this.currentFrame.speed2.length; i++) {
      if (!this.barrier[i]) {
        const s = Math.sqrt(this.currentFrame.speed2[i]);
        if (s > maxSpeed) maxSpeed = s;
      }
    }
    if (maxSpeed < 1e-10) return;

    const arrowColor = new THREE.Color(0xffffff);
    const length_scale = 0.125;
    const width_scale = 0.05;

    if (this.config.sliceAxis === 'z') {
      for (let y = step; y < ny - step; y += step) {
        for (let x = step; x < nx - step; x += step) {
          const idx = x + y * nx + pos * nx * ny;
          if (this.barrier[idx]) continue;

          const ux = this.currentFrame.ux[idx];
          const uy = this.currentFrame.uy[idx];
          const uz = this.currentFrame.uz[idx];
          const speed = Math.sqrt(ux * ux + uy * uy + uz * uz);
          if (speed < maxSpeed * 0.01) continue;

          const dir = new THREE.Vector3(ux, uy, uz).normalize();
          const length = (speed / maxSpeed) * arrowScale;
          const origin = new THREE.Vector3(x, y, pos + 0.5);

          const arrow = new THREE.ArrowHelper(dir, origin, length, arrowColor, length * length_scale, length * width_scale);
          this.arrowGroup.add(arrow);
        }
      }
    } else if (this.config.sliceAxis === 'x') {
      for (let z = step; z < nz - step; z += step) {
        for (let y = step; y < ny - step; y += step) {
          const idx = pos + y * nx + z * nx * ny;
          if (this.barrier[idx]) continue;

          const ux = this.currentFrame.ux[idx];
          const uy = this.currentFrame.uy[idx];
          const uz = this.currentFrame.uz[idx];
          const speed = Math.sqrt(ux * ux + uy * uy + uz * uz);
          if (speed < maxSpeed * 0.01) continue;

          const dir = new THREE.Vector3(ux, uy, uz).normalize();
          const length = (speed / maxSpeed) * arrowScale;
          const origin = new THREE.Vector3(pos + 0.5, y, z);

          const arrow = new THREE.ArrowHelper(dir, origin, length, arrowColor, length * length_scale, length * width_scale);
          this.arrowGroup.add(arrow);
        }
      }
    } else {
      for (let z = step; z < nz - step; z += step) {
        for (let x = step; x < nx - step; x += step) {
          const idx = x + pos * nx + z * nx * ny;
          if (this.barrier[idx]) continue;

          const ux = this.currentFrame.ux[idx];
          const uy = this.currentFrame.uy[idx];
          const uz = this.currentFrame.uz[idx];
          const speed = Math.sqrt(ux * ux + uy * uy + uz * uz);
          if (speed < maxSpeed * 0.01) continue;

          const dir = new THREE.Vector3(ux, uy, uz).normalize();
          const length = (speed / maxSpeed) * arrowScale;
          const origin = new THREE.Vector3(x, pos + 0.5, z);

          const arrow = new THREE.ArrowHelper(dir, origin, length, arrowColor, length * length_scale, length * width_scale);
          this.arrowGroup.add(arrow);
        }
      }
    }
  }

  // ========================================================================
  //  BARRIER GEOMETRY
  // ========================================================================

  private buildBarrierMesh(): void {
    if (this.barrierMesh) {
      this.scene.remove(this.barrierMesh);
      this.barrierMesh.geometry.dispose();
      (this.barrierMesh.material as THREE.Material).dispose();
    }

    const [nx, ny, nz] = this.metadata.dims;
    const vertices: number[] = [];

    for (let z = 0; z < nz; z++) {
      for (let y = 0; y < ny; y++) {
        for (let x = 0; x < nx; x++) {
          const idx = x + y * nx + z * nx * ny;
          if (!this.barrier[idx]) continue;

          // Only add surface cells (at least one non-barrier neighbor)
          let isSurface = false;
          for (let dz = -1; dz <= 1 && !isSurface; dz++) {
            for (let dy = -1; dy <= 1 && !isSurface; dy++) {
              for (let dx = -1; dx <= 1 && !isSurface; dx++) {
                if (dx === 0 && dy === 0 && dz === 0) continue;
                const nx2 = x + dx, ny2 = y + dy, nz2 = z + dz;
                if (nx2 < 0 || nx2 >= nx || ny2 < 0 || ny2 >= ny || nz2 < 0 || nz2 >= nz) {
                  isSurface = true;
                } else if (!this.barrier[nx2 + ny2 * nx + nz2 * nx * ny]) {
                  isSurface = true;
                }
              }
            }
          }

          if (isSurface) {
            vertices.push(x, y, z);
          }
        }
      }
    }

    if (vertices.length === 0) return;

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));

    const material = new THREE.PointsMaterial({
      color: 0x64748b,
      size: 0.8,
      transparent: true,
      opacity: 0.6,
    });

    this.barrierMesh = new THREE.Points(geometry, material);
    this.barrierMesh.visible = this.config.showBarrier;
    this.scene.add(this.barrierMesh);
  }

  private buildTankOutline(): void {
    const [nx, ny, nz] = this.metadata.dims;
    const cx = nx / 2, cy = ny / 2;
    const radius = Math.min(nx, ny) / 2;

    // Draw cylinder outline
    const points: THREE.Vector3[] = [];
    const segments = 64;

    // Bottom circle
    for (let i = 0; i <= segments; i++) {
      const angle = (i / segments) * Math.PI * 2;
      points.push(new THREE.Vector3(
        cx + radius * Math.cos(angle),
        cy + radius * Math.sin(angle),
        0
      ));
    }
    // Top circle
    for (let i = 0; i <= segments; i++) {
      const angle = (i / segments) * Math.PI * 2;
      points.push(new THREE.Vector3(
        cx + radius * Math.cos(angle),
        cy + radius * Math.sin(angle),
        nz
      ));
    }
    // Vertical lines
    for (let i = 0; i < 8; i++) {
      const angle = (i / 8) * Math.PI * 2;
      points.push(new THREE.Vector3(cx + radius * Math.cos(angle), cy + radius * Math.sin(angle), 0));
      points.push(new THREE.Vector3(cx + radius * Math.cos(angle), cy + radius * Math.sin(angle), nz));
    }

    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    const material = new THREE.LineBasicMaterial({ color: 0x334155, transparent: true, opacity: 0.5 });
    this.tankOutline = new THREE.LineSegments(geometry, material);
    this.scene.add(this.tankOutline);
  }

  // ========================================================================
  //  RENDER LOOP
  // ========================================================================

  private animate = (): void => {
    requestAnimationFrame(this.animate);
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  };

  private onResize(): void {
    const w = this.container.clientWidth;
    const h = this.container.clientHeight;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
  }

  /** Update colorbar canvas */
  updateColorbar(canvas: HTMLCanvasElement, minLabel: HTMLElement, maxLabel: HTMLElement): void {
    const [min, max] = this.getFieldRange();
    drawColorbar(canvas, this.config.colorMap, min, max);
    minLabel.textContent = min.toFixed(4);
    maxLabel.textContent = max.toFixed(4);
  }

  dispose(): void {
    this.renderer.dispose();
    this.controls.dispose();
  }
}
