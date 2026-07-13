// 递归下载 three.js r0.160.0 的 build 与示例 jsm 模块到本地 vendor 目录。
// 自动解析 jsm 文件之间的相对依赖，保证完全离线可用。
import { writeFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { posix } from 'node:path';

const VERSION = '0.160.0';
const CDN = `https://cdn.jsdelivr.net/npm/three@${VERSION}`;
const OUT = 'g:/Moira/src/3d/vendor/three';

const entryPoints = [
  'examples/jsm/loaders/3MFLoader.js',
  'examples/jsm/loaders/OBJLoader.js',
  'examples/jsm/loaders/STLLoader.js',
  'examples/jsm/loaders/PLYLoader.js',
  'examples/jsm/loaders/ColladaLoader.js',
  'examples/jsm/loaders/GLTFLoader.js',
  'examples/jsm/loaders/TDSLoader.js',
  'examples/jsm/controls/OrbitControls.js',
];

const seen = new Set();
const queue = [];

function toLocalPath(cdnPath) {
  // cdnPath 形如 examples/jsm/loaders/3MFLoader.js 或 build/three.module.js
  const i = cdnPath.indexOf('examples/jsm/');
  if (i >= 0) return cdnPath.slice(i + 'examples/jsm/'.length); // 相对 jsm 根
  const b = cdnPath.indexOf('build/');
  if (b >= 0) return '../build/' + cdnPath.slice(b + 'build/'.length);
  return cdnPath;
}

async function fetchText(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
  return await res.text();
}

function extractSpecs(code) {
  const specs = [];
  const re = /(?:import|export)[^'"]*?from\s*['"]([^'"]+)['"]/g;
  let m;
  while ((m = re.exec(code))) specs.push(m[1]);
  // 动态 import
  const dy = /import\(\s*['"]([^'"]+)['"]\s*\)/g;
  while ((m = dy.exec(code))) specs.push(m[1]);
  return specs;
}

async function process(relPath, depth) {
  if (seen.has(relPath)) return;
  seen.add(relPath);

  const url = `${CDN}/${relPath}`;
  const code = await fetchText(url);

  // 保存到本地（保留 jsm 目录结构）
  const localRel = toLocalPath(relPath); // 相对 jsm 根 或 ../build/...
  const outPath = join(OUT, 'jsm', localRel);
  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, code, 'utf8');
  console.log(`[${depth}] saved ${relPath} -> ${localRel}`);

  // 解析依赖
  for (const spec of extractSpecs(code)) {
    if (spec === 'three') continue; // 由 importmap 解析
    let target;
    if (spec.startsWith('three/addons/')) {
      target = 'examples/jsm/' + spec.slice('three/addons/'.length);
    } else if (spec.startsWith('.')) {
      // 相对当前文件（使用 posix 规范，避免 Windows 反斜杠）
      const baseDir = dirname(relPath);
      target = posix.normalize(posix.join(baseDir, spec));
    } else {
      console.log(`  (跳过裸依赖 ${spec})`);
      continue;
    }
    queue.push(target);
  }
}

(async () => {
  // 1) 先下载 build/three.module.js
  const buildCode = await fetchText(`${CDN}/build/three.module.js`);
  await mkdir(`${OUT}/build`, { recursive: true });
  await writeFile(`${OUT}/build/three.module.js`, buildCode, 'utf8');
  console.log('[0] saved build/three.module.js');

  // 2) 处理 jsm 入口及其依赖（BFS）
  for (const e of entryPoints) queue.push(e);
  let d = 1;
  while (queue.length) {
    const batch = queue.splice(0, queue.length);
    for (const p of batch) await process(p, d);
    d++;
  }
  console.log(`\n完成。共下载 ${seen.size} 个 jsm 文件 + 1 个 build 文件。`);
})().catch((err) => {
  console.error('下载失败:', err);
  throw err;
});
