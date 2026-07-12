/* i18n.js — 中 / 英文多语言词典与切换 (lightweight, no dependency) */
(function (global) {
  "use strict";

  const DICT = {
    zh: {
      appTitle: "CFD 可视化",
      uploadBtn: "上传 demo 文件夹",
      emptyHint: "请点击右上角「上传 demo 文件夹」加载模拟结果",
      emptyHint2: "支持 metadata.json 与 frame_*.json 的文件夹",
      loading: "加载中…",
      secField: "标量场",
      fieldLabel: "标量场",
      colormapLabel: "调色板",
      secRange: "颜色值域",
      rangeAuto: "自动",
      rangeManual: "手动",
      rangeMin: "最小值",
      rangeMax: "最大值",
      thresholdLabel: "透明阈值",
      secArrows: "速度矢量箭头",
      arrowsToggle: "显示箭头",
      arrowsDensity: "箭头密度",
      densityHigh: "高 (2×2×2)",
      densityMid: "中 (3×3×3)",
      densityLow: "低 (4×4×4)",
      secSection: "横截面",
      sectionEnable: "启用",
      sectionAxis: "截面轴",
      axisX: "X 轴",
      axisY: "Y 轴",
      axisZ: "Z 轴",
      sectionPos: "位置",
      secVoxel: "选中体素",
      voxelEmpty: "点击体素查看详情",
      secTimeseries: "时间序列",
      secSlice: "2D 横截面",
      speed: "速度",
      frame: "帧",
      time: "时间",
      coord: "坐标 (i,j,k)",
      value: "当前值",
      velocity: "速度",
      loadingFrame: "正在加载帧",
      parsing: "解析帧",
      seriesProgress: "正在提取时间序列",
      noSelection: "尚未选中体素",
      rangeInvalid: "手动值域无效（最小值须小于最大值）",
      uploadErr:
        "未能从该文件夹读取 metadata.json，请确认包含了 demo/frames 数据",
      fieldPressure: "压力",
      fieldDensity: "密度",
      fieldSpeed: "速度",
    },
    en: {
      appTitle: "CFD Visualization",
      uploadBtn: "Upload demo folder",
      emptyHint: 'Click "Upload demo folder" at top-right to load results',
      emptyHint2: "Folder with metadata.json and frame_*.json supported",
      loading: "Loading…",
      secField: "Scalar Field",
      fieldLabel: "Scalar field",
      colormapLabel: "Colormap",
      secRange: "Color Range",
      rangeAuto: "Auto",
      rangeManual: "Manual",
      rangeMin: "Min",
      rangeMax: "Max",
      thresholdLabel: "Opacity threshold",
      secArrows: "Velocity Arrows",
      arrowsToggle: "Show arrows",
      arrowsDensity: "Arrow density",
      densityHigh: "High (2×2×2)",
      densityMid: "Medium (3×3×3)",
      densityLow: "Low (4×4×4)",
      secSection: "Cross-section",
      sectionEnable: "Enable",
      sectionAxis: "Axis",
      axisX: "X axis",
      axisY: "Y axis",
      axisZ: "Z axis",
      sectionPos: "Position",
      secVoxel: "Selected Voxel",
      voxelEmpty: "Click a voxel for details",
      secTimeseries: "Time Series",
      secSlice: "2D Cross-section",
      speed: "Speed",
      frame: "Frame",
      time: "Time",
      coord: "Coord (i,j,k)",
      value: "Value",
      velocity: "Velocity",
      loadingFrame: "Loading frame",
      parsing: "Parsing frame",
      seriesProgress: "Extracting time series",
      noSelection: "No voxel selected",
      rangeInvalid: "Invalid manual range (min must be < max)",
      uploadErr:
        "Could not read metadata.json from this folder. Make sure it contains the demo/frames data",
      fieldPressure: "Pressure",
      fieldDensity: "Density",
      fieldSpeed: "Speed",
    },
  };

  const I18N = {
    lang: localStorage.getItem("cdf_lang") || "zh",

    t(key) {
      const d = DICT[this.lang] || DICT.zh;
      return d[key] !== undefined
        ? d[key]
        : DICT.zh[key] !== undefined
          ? DICT.zh[key]
          : key;
    },

    setLang(lang) {
      if (!DICT[lang]) return;
      this.lang = lang;
      localStorage.setItem("cdf_lang", lang);
      this.apply();
    },

    toggle() {
      this.setLang(this.lang === "zh" ? "en" : "zh");
    },

    /* Translate every element carrying data-i18n / data-i18n-ph */
    apply() {
      document.documentElement.lang = this.lang;
      const all = document.querySelectorAll("[data-i18n]");
      all.forEach((el) => {
        const key = el.getAttribute("data-i18n");
        // keep inner SVG icons if present
        const svg = el.querySelector("svg");
        if (svg && el.childNodes.length > 1) {
          // element has text + svg (e.g., buttons): only update the trailing text node
          for (const node of el.childNodes) {
            if (node.nodeType === 3 && node.textContent.trim() !== "") {
              node.textContent = " " + this.t(key);
              break;
            }
          }
        } else {
          el.textContent = this.t(key);
        }
      });
      const opts = document.querySelectorAll("option[data-i18n]");
      opts.forEach((el) => {
        el.textContent = this.t(el.getAttribute("data-i18n"));
      });
      document.title = this.t("appTitle");
      if (typeof this.onChange === "function") this.onChange(this.lang);
    },
  };

  global.I18N = I18N;
})(window);
