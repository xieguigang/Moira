#!/usr/bin/env python3
"""Render a CityBlender multi-region STL set to preview images."""
import struct, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def read_stl(path):
    with open(path, "rb") as f:
        header = f.read(80)
        n = struct.unpack("<I", f.read(4))[0]
        raw = np.frombuffer(f.read(50 * n), dtype=np.uint8).reshape(n, 50)
    # 12 floats (normal + 3 verts), then 2 attribute bytes
    tris = raw[:, :48].copy().view(np.float32).reshape(n, 4, 3)[:, 1:, :]  # (n,3,3)
    return np.asarray(tris, dtype=np.float64)


def main(base):
    regions = {
        "ground":    ("#6b7a5a", 0.75),   # terrain green-grey
        "roads":     ("#3a3d42", 1.0),    # asphalt
        "buildings": ("#b8bdc7", 0.9),    # concrete
        "water":     ("#2e6f9e", 0.85),   # lake/river blue
    }

    fig = plt.figure(figsize=(16, 9), facecolor="#11131a")
    for i, view in enumerate([(28, -55), (55, -90)]):
        ax = fig.add_subplot(1, 2, i + 1, projection="3d", facecolor="#11131a")
        for name, (color, _) in regions.items():
            try:
                tris = read_stl(f"{base}_{name}.stl")
            except FileNotFoundError:
                continue
            if name == "ground":
                # subsample the terrain to keep matplotlib responsive
                step = max(1, len(tris) // 40000)
                tris = tris[::step]
            if name == "buildings":
                # colour by height
                zs = tris[:, :, 2].mean(axis=1)
                h = (zs - zs.min()) / max(1e-9, zs.max() - zs.min())
                colors = plt.cm.viridis(0.15 + 0.75 * h)
                pc = Poly3DCollection(tris, facecolors=colors, edgecolors="none", alpha=0.98)
            else:
                pc = Poly3DCollection(tris, facecolors=color, edgecolors="none", alpha=0.9)
            ax.add_collection3d(pc)

        size = 1000.0
        ax.set_xlim(0, size); ax.set_ylim(0, size); ax.set_zlim(-5, 150)
        ax.set_box_aspect((1, 1, 0.28))
        ax.view_init(*view)
        ax.set_axis_off()
        ax.set_facecolor("#11131a")

    fig.suptitle("CityBlender — random city, seed 42 (1000 m, binary STL, units = meters)",
                 color="#dfe3ea", fontsize=13)
    out = base + "_preview.png"
    fig.savefig(out, dpi=110, facecolor="#11131a", bbox_inches="tight")
    print("written", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "samples/city_seed42")
