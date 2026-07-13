// geometry.js
// 遍历 Object3D，应用世界矩阵，收集三角形/点数据与统计信息。
import * as THREE from 'three';

/**
 * @returns {{
 *   triangles: Float32Array,   // 每个三角形 9 个 float (v0,v1,v2)
 *   points: Float32Array,      // 每个点 3 个 float
 *   triCount: number,
 *   pointCount: number,
 *   vertexCount: number,
 *   faceCount: number,
 *   objectCount: number,
 *   isPointCloud: boolean,
 *   bbox: THREE.Box3
 * }}
 */
export function collectGeometry(root) {
  root.updateMatrixWorld(true);

  const meshes = [];
  const pointClouds = [];
  let objectCount = 0;
  let vertexCount = 0;

  root.traverse((child) => {
    if (child.isMesh && child.geometry) {
      meshes.push(child);
      objectCount++;
      vertexCount += getVertexCount(child.geometry);
    } else if (child.isPoints && child.geometry) {
      pointClouds.push(child);
      objectCount++;
      vertexCount += getVertexCount(child.geometry);
    }
  });

  const bbox = new THREE.Box3();
  bbox.makeEmpty();

  // ---- 收集三角形 ----
  let totalTris = 0;
  for (const m of meshes) totalTris += triCountOf(m.geometry);

  const triangles = new Float32Array(totalTris * 9);
  let ti = 0;
  let faceCount = 0;
  const vTmp = new THREE.Vector3();

  for (const m of meshes) {
    const geo = m.geometry;
    const pos = geo.attributes.position;
    if (!pos) continue;
    const mat = m.matrixWorld;
    const index = geo.index;

    const readVert = (vi) => {
      vTmp.set(pos.getX(vi), pos.getY(vi), pos.getZ(vi)).applyMatrix4(mat);
      bbox.expandByPoint(vTmp);
      return vTmp;
    };

    if (index) {
      for (let i = 0; i < index.count; i += 3) {
        const a = index.getX(i), b = index.getX(i + 1), c = index.getX(i + 2);
        writeTri(triangles, ti, readVert(a)); ti += 3;
        writeTri(triangles, ti, readVert(b)); ti += 3;
        writeTri(triangles, ti, readVert(c)); ti += 3;
        faceCount++;
      }
    } else {
      for (let i = 0; i < pos.count; i += 3) {
        writeTri(triangles, ti, readVert(i)); ti += 3;
        writeTri(triangles, ti, readVert(i + 1)); ti += 3;
        writeTri(triangles, ti, readVert(i + 2)); ti += 3;
        faceCount++;
      }
    }
  }

  // ---- 收集点云 ----
  let totalPoints = 0;
  for (const p of pointClouds) totalPoints += (p.geometry.attributes.position?.count || 0);
  const points = new Float32Array(totalPoints * 3);
  let pi = 0;
  for (const p of pointClouds) {
    const pos = p.geometry.attributes.position;
    if (!pos) continue;
    const mat = p.matrixWorld;
    for (let i = 0; i < pos.count; i++) {
      vTmp.set(pos.getX(i), pos.getY(i), pos.getZ(i)).applyMatrix4(mat);
      bbox.expandByPoint(vTmp);
      points[pi++] = vTmp.x;
      points[pi++] = vTmp.y;
      points[pi++] = vTmp.z;
    }
  }

  const isPointCloud = meshes.length === 0 && pointClouds.length > 0;

  if (bbox.isEmpty()) bbox.set(new THREE.Vector3(-1, -1, -1), new THREE.Vector3(1, 1, 1));

  return {
    triangles,
    points,
    triCount: totalTris,
    pointCount: totalPoints,
    vertexCount,
    faceCount,
    objectCount,
    isPointCloud,
    bbox,
  };
}

function writeTri(arr, offset, v) {
  arr[offset] = v.x;
  arr[offset + 1] = v.y;
  arr[offset + 2] = v.z;
}

function getVertexCount(geo) {
  return geo.attributes.position ? geo.attributes.position.count : 0;
}

function triCountOf(geo) {
  if (!geo.attributes.position) return 0;
  if (geo.index) return Math.floor(geo.index.count / 3);
  return Math.floor(geo.attributes.position.count / 3);
}
