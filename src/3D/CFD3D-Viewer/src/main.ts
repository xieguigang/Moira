/**
 * main.ts - Application Entry Point
 *
 * Wires up the UI controls with the CFDViewer and data loader.
 * Supports loading simulation data from a directory or generating demo data.
 */

import { CFDViewer } from './viewer';
import { loadSimulationData, generateDemoData } from './data-loader';
import { jet, coolwarm, viridis, plasma, drawColorbar } from './color-map';
import type { ColorMap } from './color-map';

// DOM elements
const viewport = document.getElementById('viewport')!;
const loading = document.getElementById('loading')!;
const frameInfo = document.getElementById('frame-info')!;
const overlayFrame = document.getElementById('overlay-frame')!;
const overlayTotal = document.getElementById('overlay-total')!;
const toast = document.getElementById('toast')!;

const dataPathInput = document.getElementById('data-path') as HTMLInputElement;
const loadBtn = document.getElementById('btn-load') as HTMLButtonElement;
const demoBtn = document.getElementById('btn-demo') as HTMLButtonElement;

const fieldSelect = document.getElementById('field-select') as HTMLSelectElement;
const sliceAxisSelect = document.getElementById('slice-axis') as HTMLSelectElement;
const sliceSlider = document.getElementById('slice-pos') as HTMLInputElement;
const sliceValue = document.getElementById('slice-label') as HTMLSpanElement;
const frameSlider = document.getElementById('frame-slider') as HTMLInputElement;
const frameValue = document.getElementById('frame-label') as HTMLSpanElement;
const playBtn = document.getElementById('btn-play') as HTMLButtonElement;

const arrowDensitySelect = document.getElementById('arrow-density') as HTMLSelectElement;
const showArrowsCheck = document.getElementById('show-arrows') as HTMLInputElement;
const showBarrierCheck = document.getElementById('show-barrier') as HTMLInputElement;
const showVolumeCheck = document.getElementById('show-volume') as HTMLInputElement;

const colormapSelect = document.getElementById('colormap') as HTMLSelectElement;
const colormapPanel = document.getElementById('colormap-panel')!;
const colorbarCanvas = document.getElementById('colorbar') as HTMLCanvasElement;
const cbarMin = document.getElementById('cbar-min')!;
const cbarMax = document.getElementById('cbar-max')!;

// State
let viewer: CFDViewer | null = null;
let simData = await import('./data-loader').then(m => null); // placeholder
let currentFrameIndex = 0;
let isPlaying = false;
let playInterval: number | null = null;

const colorMaps: Record<string, ColorMap> = { jet, coolwarm, viridis, plasma };

// Initialize viewer
viewer = new CFDViewer(viewport);

// ========================================================================
//  UI EVENT HANDLERS
// ========================================================================

loadBtn.addEventListener('click', async () => {
  const path = "/results"; // dataPathInput.value.trim();
  if (!path) {
    showToast('请输入数据目录路径', 'error');
    return;
  }
  try {
    loading.style.display = 'flex';
    frameInfo.style.display = 'none';
    const data = await loadSimulationData(path);
    simData = data;
    onDataLoaded();
  } catch (err) {
    showToast(`加载数据失败: ${err}`, 'error');
    loading.style.display = 'flex';
  }
});

demoBtn.addEventListener('click', () => {
  loading.style.display = 'none';
  simData = generateDemoData();
  onDataLoaded();
  showToast('已加载演示数据', 'info');
});

fieldSelect.addEventListener('change', () => {
  if (!viewer || !simData) return;
  viewer.setConfig({ field: fieldSelect.value as any });
  viewer.updateColorbar(colorbarCanvas, cbarMin, cbarMax);
});

sliceAxisSelect.addEventListener('change', () => {
  if (!viewer || !simData) return;
  const axis = sliceAxisSelect.value as 'x' | 'y' | 'z';
  const maxSlice = viewer.getMaxSlice();
  sliceSlider.max = maxSlice.toString();
  sliceSlider.value = Math.floor(maxSlice / 2).toString();
  sliceValue.textContent = sliceSlider.value;
  viewer.setConfig({ sliceAxis: axis, slicePosition: parseInt(sliceSlider.value) });
});

sliceSlider.addEventListener('input', () => {
  if (!viewer) return;
  const pos = parseInt(sliceSlider.value);
  sliceValue.textContent = pos.toString();
  viewer.setConfig({ slicePosition: pos });
});

frameSlider.addEventListener('input', () => {
  if (!viewer || !simData) return;
  currentFrameIndex = parseInt(frameSlider.value);
  frameValue.textContent = currentFrameIndex.toString();
  overlayFrame.textContent = currentFrameIndex.toString();
  viewer.setFrame(simData.frames[currentFrameIndex]);
});

playBtn.addEventListener('click', () => {
  if (!simData || simData.frames.length <= 1) return;
  isPlaying = !isPlaying;
  playBtn.textContent = isPlaying ? '⏸ 暂停' : '▶ 播放';

  if (isPlaying) {
    playInterval = window.setInterval(() => {
      currentFrameIndex = (currentFrameIndex + 1) % simData.frames.length;
      frameSlider.value = currentFrameIndex.toString();
      frameValue.textContent = currentFrameIndex.toString();
      overlayFrame.textContent = currentFrameIndex.toString();
      viewer!.setFrame(simData.frames[currentFrameIndex]);
    }, 200);
  } else {
    if (playInterval !== null) {
      clearInterval(playInterval);
      playInterval = null;
    }
  }
});

arrowDensitySelect.addEventListener('change', () => {
  if (!viewer) return;
  viewer.setConfig({ arrowDensity: parseInt(arrowDensitySelect.value) });
});

showArrowsCheck.addEventListener('change', () => {
  if (!viewer) return;
  viewer.setConfig({ showArrows: showArrowsCheck.checked });
});

showBarrierCheck.addEventListener('change', () => {
  if (!viewer) return;
  viewer.setConfig({ showBarrier: showBarrierCheck.checked });
});

colormapSelect.addEventListener('change', () => {
  if (!viewer) return;
  const cmap = colorMaps[colormapSelect.value] || jet;
  viewer.setConfig({ colorMap: cmap });
  viewer.updateColorbar(colorbarCanvas, cbarMin, cbarMax);
});

// ========================================================================
//  DATA LOADED CALLBACK
// ========================================================================

function onDataLoaded(): void {
  if (!viewer || !simData) return;

  loading.style.display = 'none';
  frameInfo.style.display = 'block';

  viewer.init(simData.metadata, simData.barrier);

  // Setup frame slider
  const totalFrames = simData.frames.length;
  frameSlider.max = (totalFrames - 1).toString();
  frameSlider.value = '0';
  frameValue.textContent = '0';
  overlayFrame.textContent = '0';
  overlayTotal.textContent = totalFrames.toString();

  // Setup slice slider
  const maxSlice = viewer.getMaxSlice();
  sliceSlider.max = maxSlice.toString();
  sliceSlider.value = Math.floor(maxSlice / 2).toString();
  sliceValue.textContent = sliceSlider.value;

  // Show first frame
  currentFrameIndex = 0;
  viewer.setFrame(simData.frames[0]);

  // Show colormap panel
  colormapPanel.style.display = 'block';
  viewer.updateColorbar(colorbarCanvas, cbarMin, cbarMax);

  // Update metadata display
  const meta = simData.metadata;
  const metaInfo = document.getElementById('meta-info')!;
  metaInfo.innerHTML = `
    <div class="meta-row"><span>网格尺寸</span><span>${meta.dims.join(' × ')}</span></div>
    <div class="meta-row"><span>总单元数</span><span>${meta.totalCells.toLocaleString()}</span></div>
    <div class="meta-row"><span>帧数</span><span>${meta.totalFrames}</span></div>
    <div class="meta-row"><span>粘度</span><span>${meta.viscosity}</span></div>
    <div class="meta-row"><span>总迭代</span><span>${meta.totalIterations.toLocaleString()}</span></div>
    <div class="meta-row"><span>描述</span><span>${meta.description}</span></div>
  `;

  showToast(`已加载 ${totalFrames} 帧数据`, 'success');
}

// ========================================================================
//  TOAST NOTIFICATIONS
// ========================================================================

function showToast(message: string, type: 'info' | 'success' | 'error' = 'info'): void {
  toast.textContent = message;
  toast.className = `toast toast-${type} show`;
  setTimeout(() => {
    toast.className = 'toast';
  }, 3000);
}
