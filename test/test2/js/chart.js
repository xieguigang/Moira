// chart.js —— 基于 Chart.js 的时间序列折线图

const Chart = window.Chart;

let chart = null;
const SERIES_COLORS = ['#2563EB', '#10B981', '#F59E0B', '#EF4444', '#0EA5E9', '#8B5CF6'];

export function initChart(canvas) {
  const ctx = canvas.getContext('2d');
  chart = new Chart(ctx, {
    type: 'line',
    data: { labels: [], datasets: [] },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { labels: { font: { family: 'Roboto', size: 12 }, color: '#1F2937' } },
        tooltip: { titleFont: { family: 'Roboto' }, bodyFont: { family: 'Roboto' } }
      },
      scales: {
        x: { title: { display: true, text: 'Time', font: { family: 'Roboto' } }, ticks: { maxTicksLimit: 10, font: { family: 'Roboto', size: 11 }, color: '#6B7280' }, grid: { color: '#EEF1F6' } },
        y: { title: { display: true, text: 'Value', font: { family: 'Roboto' } }, ticks: { font: { family: 'Roboto', size: 11 }, color: '#6B7280' }, grid: { color: '#EEF1F6' } }
      },
      elements: { point: { radius: 0, hitRadius: 6 }, line: { borderWidth: 2, tension: 0.25 } }
    }
  });
  return chart;
}

// seriesByField: { fieldName: Float32Array }
export function updateChart(timesteps, seriesByField, fieldLabels) {
  if (!chart) return;
  chart.data.labels = timesteps.map((t) => t.toFixed(2));
  chart.data.datasets = Object.keys(seriesByField).map((f, idx) => ({
    label: fieldLabels[f] || f,
    data: Array.from(seriesByField[f]),
    borderColor: SERIES_COLORS[idx % SERIES_COLORS.length],
    backgroundColor: SERIES_COLORS[idx % SERIES_COLORS.length],
    fill: false
  }));
  chart.update();
}
