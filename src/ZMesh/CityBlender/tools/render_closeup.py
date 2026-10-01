#!/usr/bin/env python3
"""Close-up render of the city centre to verify urban features."""
import struct
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def read_stl(path):
    with open(path, "rb") as f:
        f.read(80)
        n = struct.unpack("<I", f.read(4))[0]
        raw = np.frombuffer(f.read(50 * n), dtype=np.uint8).reshape(n, 50)
    tris = raw[:, :48].copy().view(np.float32).reshape(n, 4, 3)[:, 1:, :]
    return np.asarray(tris, dtype=np.float64)


def inbox(tris, x0, x1, y0, y1, frac=0.2):
    cx = tris[:, :, 0].mean(axis=1)
    cy = tris[:, :, 1].mean(axis=1)
    m = (cx >= x0) & (cx <= x1) & (cy >= y0) & (cy <= y1)
    return tris[m]


base = "samples/city_seed42"
c0, c1, half = 500.0, 500.0, 260.0

ground = inbox(read_stl(f"{base}_ground.stl"), c0 - half, c0 + half, c1 - half, c1 + half)
roads = inbox(read_stl(f"{base}_roads.stl"), c0 - half, c0 + half, c1 - half, c1 + half)
blds = inbox(read_stl(f"{base}_buildings.stl"), c0 - half, c0 + half, c1 - half, c1 + half)
water = inbox(read_stl(f"{base}_water.stl"), c0 - half, c0 + half, c1 - half, c1 + half)

# lift thin slabs off the terrain to defeat the depth-buffer z-fighting
roads[:, :, 2] += 2.0
water[:, :, 2] += 1.0

fig = plt.figure(figsize=(14, 11), facecolor="#11131a")
ax = fig.add_subplot(111, projection="3d", facecolor="#11131a")

pc = Poly3DCollection(ground, facecolors="#5c6b4f", edgecolors="none", alpha=0.9)
ax.add_collection3d(pc)
pc = Poly3DCollection(water, facecolors="#2e6f9e", edgecolors="none", alpha=0.9)
ax.add_collection3d(pc)
pc = Poly3DCollection(roads, facecolors="#20242a", edgecolors="none", alpha=1.0)
ax.add_collection3d(pc)

zs = blds[:, :, 2].mean(axis=1)
h = (zs - zs.min()) / max(1e-9, zs.max() - zs.min())
pc = Poly3DCollection(blds, facecolors=plt.cm.turbo(0.15 + 0.8 * h), edgecolors="none", alpha=1.0)
ax.add_collection3d(pc)

ax.set_xlim(c0 - half, c0 + half)
ax.set_ylim(c1 - half, c1 + half)
ax.set_zlim(0, 150)
ax.set_box_aspect((1, 1, 0.42))
ax.view_init(38, -50)
ax.set_axis_off()
fig.suptitle("CityBlender centre close-up — roads (dark), buildings (height-coloured), water (blue)",
             color="#dfe3ea", fontsize=12)
fig.savefig(base + "_closeup.png", dpi=120, facecolor="#11131a", bbox_inches="tight")
print("triangles:", len(ground), len(roads), len(blds), len(water))
print("building z-range:", zs.min(), zs.max())
print("written", base + "_closeup.png")
