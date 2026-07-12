// i18n.js —— 中英文案字典与切换逻辑
const dict = {
  zh: {
    'app.title': 'CFD 流体可视化',
    'app.upload': '上传数据文件夹',
    'app.langToggle': 'EN',
    'status.idle': '未加载数据',
    'status.loaded': '已加载 {frames} 帧 · {nx}×{ny}×{nz}',
    'status.parsing': '正在解析… {pct}%',
    'control.field': '显示场',
    'field.speed': '速度大小',
    'field.pressure': '压力',
    'field.density': '密度 / 示踪剂',
    'field.u': '速度 U (X)',
    'field.v': '速度 V (Y)',
    'field.w': '速度 W (Z)',
    'control.palette': '调色板',
    'palette.viridis': 'Viridis',
    'palette.plasma': 'Plasma',
    'palette.inferno': 'Inferno',
    'palette.turbo': 'Turbo',
    'palette.jet': 'Jet',
    'palette.coolwarm': 'Cool-Warm',
    'palette.grayscale': 'Grayscale',
    'control.range': '数值范围',
    'control.auto': '自动',
    'control.arrows': '速度矢量箭头',
    'control.arrowDensity': '箭头密度',
    'control.section': '横截面',
    'control.sectionAxis': '截面轴',
    'axis.x': 'X',
    'axis.y': 'Y',
    'axis.z': 'Z',
    'control.sectionPos': '截面位置',
    'control.sectionFlip': '翻转裁剪方向',
    'control.sectionArbitrary': '任意平面',
    'control.sectionNormal': '法向量 (nx, ny, nz)',
    'control.animation': '动画播放',
    'control.play': '播放',
    'control.pause': '暂停',
    'control.speed': '播放速度',
    'control.frame': '帧',
    'control.time': '时间',
    'voxel.title': '选中体素',
    'voxel.none': '点击体素网格以查看其时间序列',
    'voxel.coords': '坐标 (i, j, k)',
    'chart.title': '时间序列',
    'hint.upload': '请上传包含 animation.pvd 与 frame_*.vtk 的 data 文件夹以开始可视化',
    'fieldLabel.pressure': '压力',
    'fieldLabel.density': '密度',
    'fieldLabel.u': 'U',
    'fieldLabel.v': 'V',
    'fieldLabel.w': 'W',
    'fieldLabel.speed': '速度',
    'fieldLabel.velocity': '速度向量',
    'err.noPvd': '未找到 animation.pvd（或 .pvd）文件，请确认上传的是包含动画定义的 data 文件夹。',
    'err.noVtk': '未找到任何 frame_*.vtk 文件。',
    'err.parse': '解析第 {n} 帧时发生错误：{msg}',
    'tip.drag': '拖拽旋转 · 滚轮缩放 · 点击体素查看时间序列'
  },
  en: {
    'app.title': 'CFD Flow Visualizer',
    'app.upload': 'Upload Data Folder',
    'app.langToggle': '中',
    'status.idle': 'No data loaded',
    'status.loaded': 'Loaded {frames} frames · {nx}×{ny}×{nz}',
    'status.parsing': 'Parsing… {pct}%',
    'control.field': 'Display Field',
    'field.speed': 'Speed',
    'field.pressure': 'Pressure',
    'field.density': 'Density / Tracer',
    'field.u': 'Velocity U (X)',
    'field.v': 'Velocity V (Y)',
    'field.w': 'Velocity W (Z)',
    'control.palette': 'Color Palette',
    'palette.viridis': 'Viridis',
    'palette.plasma': 'Plasma',
    'palette.inferno': 'Inferno',
    'palette.turbo': 'Turbo',
    'palette.jet': 'Jet',
    'palette.coolwarm': 'Cool-Warm',
    'palette.grayscale': 'Grayscale',
    'control.range': 'Value Range',
    'control.auto': 'Auto',
    'control.arrows': 'Velocity Arrows',
    'control.arrowDensity': 'Arrow density',
    'control.section': 'Cross Section',
    'control.sectionAxis': 'Section axis',
    'axis.x': 'X',
    'axis.y': 'Y',
    'axis.z': 'Z',
    'control.sectionPos': 'Section position',
    'control.sectionFlip': 'Flip clip direction',
    'control.sectionArbitrary': 'Arbitrary plane',
    'control.sectionNormal': 'Normal (nx, ny, nz)',
    'control.animation': 'Animation',
    'control.play': 'Play',
    'control.pause': 'Pause',
    'control.speed': 'Speed',
    'control.frame': 'Frame',
    'control.time': 'Time',
    'voxel.title': 'Selected Voxel',
    'voxel.none': 'Click a voxel to view its time series',
    'voxel.coords': 'Coords (i, j, k)',
    'chart.title': 'Time Series',
    'chart.x': 'Time',
    'chart.y': 'Value',
    'chart.x': 'Time',
    'chart.y': 'Value',
    'hint.upload': 'Upload the data folder containing animation.pvd and frame_*.vtk to begin visualization',
    'fieldLabel.pressure': 'Pressure',
    'fieldLabel.density': 'Density',
    'fieldLabel.u': 'U',
    'fieldLabel.v': 'V',
    'fieldLabel.w': 'W',
    'fieldLabel.speed': 'Speed',
    'fieldLabel.velocity': 'Velocity',
    'err.noPvd': 'No animation.pvd (or .pvd) file found. Please upload the data folder that contains the animation definition.',
    'err.noVtk': 'No frame_*.vtk files found.',
    'err.parse': 'Error parsing frame {n}: {msg}',
    'tip.drag': 'Drag to rotate · Scroll to zoom · Click a voxel for time series'
  }
};

let current = localStorage.getItem('cdf-lang') || 'zh';
let onChange = null;

export function t(key, params) {
  let s = dict[current]?.[key] ?? dict.zh[key] ?? key;
  if (params) {
    for (const k in params) s = s.replace(new RegExp('\\{' + k + '\\}', 'g'), params[k]);
  }
  return s;
}

function apply(root = document) {
  root.querySelectorAll('[data-i18n]').forEach((el) => { el.textContent = t(el.dataset.i18n); });
  root.querySelectorAll('[data-i18n-title]').forEach((el) => { el.setAttribute('title', t(el.dataset.i18nTitle)); });
  document.documentElement.lang = current === 'zh' ? 'zh' : 'en';
}

export function setLang(lang) {
  current = lang;
  localStorage.setItem('cdf-lang', lang);
  apply();
  if (onChange) onChange(lang);
}

export function toggleLang() {
  setLang(current === 'zh' ? 'en' : 'zh');
}

export function getLang() { return current; }

export function initI18n() {
  apply();
  return { t, setLang, toggleLang, getLang, on: (fn) => { onChange = fn; } };
}
