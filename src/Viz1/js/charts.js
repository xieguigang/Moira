/* charts.js — echarts time series, 2D slice heatmap, colorbar */
(function (global) {
  'use strict';

  class Charts {
    constructor(tsContainer) {
      this.tsChart = echarts.init(tsContainer, null, { renderer: 'canvas' });
      this._lastSeries = null;
      this._lastLabel = '';
      window.addEventListener('resize', () => this.tsChart && this.tsChart.resize());
    }

    renderTimeSeries(times, values, fieldLabel) {
      this._lastSeries = { times: Array.from(times), values: Array.from(values) };
      this._lastLabel = fieldLabel;
      this._drawSeries();
    }

    _drawSeries() {
      if (!this._lastSeries) return;
      const s = this._lastSeries;
      const data = s.values.map((v, i) => [s.times[i], v]);
      this.tsChart.setOption({
        backgroundColor: 'transparent',
        grid: { left: 46, right: 14, top: 18, bottom: 28 },
        tooltip: {
          trigger: 'axis',
          backgroundColor: 'rgba(255,255,255,.95)',
          borderColor: '#e2e8f0', textStyle: { color: '#1E293B', fontSize: 12 },
          formatter: (p) => `${I18N.t('time')}: ${p[0].value[0].toFixed(3)}<br/>${this._lastLabel}: <b>${p[0].value[1].toFixed(4)}</b>`,
        },
        xAxis: {
          type: 'value', name: I18N.t('time'), nameTextStyle: { color: '#94a3b8', fontSize: 10 },
          axisLine: { lineStyle: { color: '#cbd5e1' } },
          axisLabel: { color: '#94a3b8', fontSize: 10 },
          splitLine: { show: false },
        },
        yAxis: {
          type: 'value', scale: true,
          axisLine: { lineStyle: { color: '#cbd5e1' } },
          axisLabel: { color: '#94a3b8', fontSize: 10 },
          splitLine: { lineStyle: { color: '#eef2f7' } },
        },
        series: [{
          type: 'line', data, showSymbol: false, smooth: true,
          lineStyle: { color: '#2563EB', width: 2 },
          areaStyle: {
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
              { offset: 0, color: 'rgba(37,99,235,.28)' },
              { offset: 1, color: 'rgba(37,99,235,.02)' },
            ]),
          },
        }],
      }, true);
    }

    /* redraw after language switch */
    refreshSeriesLang() { this._drawSeries(); }

    renderSlice(canvas, slice, lut) {
      const w = slice.w, h = slice.h;
      const off = document.createElement('canvas');
      off.width = w; off.height = h;
      const octx = off.getContext('2d');
      const img = octx.createImageData(w, h);
      const d = slice.data;
      for (let p = 0; p < w * h; p++) {
        let t = d[p]; // already normalized 0..1 (app normalizes)
        if (t < 0) t = 0; else if (t > 1) t = 1;
        const li = (t * 255) | 0;
        img.data[p * 4] = lut[li * 3];
        img.data[p * 4 + 1] = lut[li * 3 + 1];
        img.data[p * 4 + 2] = lut[li * 3 + 2];
        img.data[p * 4 + 3] = 255;
      }
      octx.putImageData(img, 0, 0);
      // draw scaled to canvas
      const cw = canvas.clientWidth || canvas.width;
      const ch = canvas.clientHeight || canvas.height;
      canvas.width = cw; canvas.height = ch;
      const ctx = canvas.getContext('2d');
      ctx.imageSmoothingEnabled = true;
      ctx.clearRect(0, 0, cw, ch);
      ctx.drawImage(off, 0, 0, w, h, 0, 0, cw, ch);
    }

    drawColorbar(canvas, lut) {
      const h = canvas.height, w = canvas.width;
      const ctx = canvas.getContext('2d');
      for (let y = 0; y < h; y++) {
        const t = 1 - y / (h - 1); // top = max
        const li = (t * 255) | 0;
        ctx.fillStyle = `rgb(${lut[li * 3]},${lut[li * 3 + 1]},${lut[li * 3 + 2]})`;
        ctx.fillRect(0, y, w, 1);
      }
    }
  }

  global.Charts = Charts;
})(window);
