// loaders.js
// 按扩展名分发到对应的 three.js Loader，统一 fetch 后 parse，返回 THREE.Object3D。
import * as THREE from 'three';
import { ThreeMFLoader } from 'three/addons/loaders/3MFLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { PLYLoader } from 'three/addons/loaders/PLYLoader.js';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { TDSLoader } from 'three/addons/loaders/TDSLoader.js';

// 每种扩展名需要的数据类型：'arraybuffer' | 'text'
const LOADER_REGISTRY = {
  '3mf':  { type: 'arraybuffer' },
  'obj':  { type: 'text' },
  'stl':  { type: 'arraybuffer' }, // STLLoader 可解析 ASCII / 二进制（传 ArrayBuffer 均可）
  'ply':  { type: 'arraybuffer' },
  'dae':  { type: 'text' },
  'glb':  { type: 'arraybuffer' },
  'gltf': { type: 'arraybuffer' },
  '3ds':  { type: 'arraybuffer' },
};

export function getSupportedExtensions() {
  return Object.keys(LOADER_REGISTRY);
}

export function getExtension(pathOrName) {
  const clean = pathOrName.split('?')[0].split('#')[0];
  const idx = clean.lastIndexOf('.');
  return idx >= 0 ? clean.slice(idx + 1).toLowerCase() : '';
}

export function isSupported(ext) {
  return Object.prototype.hasOwnProperty.call(LOADER_REGISTRY, ext);
}

// 默认材质（部分格式无材质，如 STL / 部分 OBJ / PLY）
function defaultMaterial() {
  return new THREE.MeshStandardMaterial({
    color: 0x9fb4d8,
    metalness: 0.15,
    roughness: 0.65,
    flatShading: false,
    side: THREE.DoubleSide,
  });
}

function pointsMaterial() {
  return new THREE.PointsMaterial({
    color: 0x22d3ee,
    size: 0.02,
    sizeAttenuation: true,
  });
}

// 将 loader 的解析结果规范化为 THREE.Object3D
function normalizeResult(ext, parsed, baseUrl) {
  switch (ext) {
    case 'stl': {
      // 返回 BufferGeometry
      const geo = parsed;
      geo.computeVertexNormals();
      return new THREE.Mesh(geo, defaultMaterial());
    }
    case 'ply': {
      const geo = parsed;
      if (geo.index || geo.attributes.normal || (geo.attributes.position && hasFaces(geo))) {
        // 有面 -> Mesh
        if (!geo.attributes.normal) geo.computeVertexNormals();
        return new THREE.Mesh(geo, defaultMaterial());
      }
      // 纯点云
      const mat = pointsMaterial();
      if (geo.attributes.color) mat.vertexColors = true;
      return new THREE.Points(geo, mat);
    }
    case 'glb':
    case 'gltf': {
      return parsed.scene || parsed.scenes?.[0] || new THREE.Group();
    }
    case '3mf':
    case 'dae': {
      return parsed.scene || parsed;
    }
    case 'obj':
    case '3ds': {
      // OBJLoader/TDSLoader 直接返回 Group
      // 为无材质的 mesh 补默认材质
      parsed.traverse((child) => {
        if (child.isMesh && (!child.material || isEmptyMaterial(child.material))) {
          child.material = defaultMaterial();
        }
      });
      return parsed;
    }
    default:
      return parsed;
  }
}

function isEmptyMaterial(mat) {
  if (Array.isArray(mat)) return mat.length === 0;
  return false;
}

function hasFaces(geo) {
  // PLY 无 index 时可能是点云；此处仅在存在 index 时判定为面
  return !!geo.index && geo.index.count > 0;
}

// 解析 ArrayBuffer/text -> Object3D
function parseByExt(ext, data, baseUrl) {
  switch (ext) {
    case '3mf':  return normalizeResult(ext, new ThreeMFLoader().parse(data), baseUrl);
    case 'obj':  return normalizeResult(ext, new OBJLoader().parse(data), baseUrl);
    case 'stl':  return normalizeResult(ext, new STLLoader().parse(data), baseUrl);
    case 'ply':  return normalizeResult(ext, new PLYLoader().parse(data), baseUrl);
    case 'dae':  return normalizeResult(ext, new ColladaLoader().parse(data, baseUrl), baseUrl);
    case '3ds':  return normalizeResult(ext, new TDSLoader().parse(data, baseUrl), baseUrl);
    case 'glb':
    case 'gltf': {
      // GLTFLoader.parse 是异步（回调）
      return new Promise((resolve, reject) => {
        new GLTFLoader().parse(data, baseUrl || '', (gltf) => {
          resolve(normalizeResult(ext, gltf, baseUrl));
        }, reject);
      });
    }
    default:
      throw new Error(`不支持的格式: .${ext}`);
  }
}

// 从相对路径 fetch 并解析
export async function loadFromUrl(url, onProgress) {
  const ext = getExtension(url);
  if (!isSupported(ext)) throw new Error(`不支持的格式: .${ext}`);
  const cfg = LOADER_REGISTRY[ext];
  const baseUrl = url.slice(0, url.lastIndexOf('/') + 1);

  onProgress?.(0.1, '请求模型文件…');
  const res = await fetch(url);
  if (!res.ok) throw new Error(`请求失败 ${res.status}: ${url}`);

  const data = cfg.type === 'text' ? await res.text() : await res.arrayBuffer();
  onProgress?.(0.6, '解析模型…');

  const obj = await parseByExt(ext, data, baseUrl);
  onProgress?.(1.0, '解析完成');
  return { object: obj, ext };
}

// 从本地 File 对象加载
export async function loadFromFile(file, onProgress) {
  const ext = getExtension(file.name);
  if (!isSupported(ext)) throw new Error(`不支持的格式: .${ext}`);
  const cfg = LOADER_REGISTRY[ext];

  onProgress?.(0.1, '读取本地文件…');
  const data = cfg.type === 'text' ? await file.text() : await file.arrayBuffer();
  onProgress?.(0.6, '解析模型…');

  // 本地文件无外部资源 base，gltf 外部 bin/纹理不支持；glb 自包含
  const obj = await parseByExt(ext, data, '');
  onProgress?.(1.0, '解析完成');
  return { object: obj, ext };
}
