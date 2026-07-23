"""
Create a flat plane populated with blue-noise (Poisson-disk) points and
triangulate it (constrained Delaunay).

Run inside Blender 4.0:
    - Open the Scripting workspace, load this file, press "Run Script", OR
    - blender --python blender_blue_noise_plane.py

Tweak SIZE_X / SIZE_Y and DENSITY below.

No external dependencies (no scipy/numpy required) -- blue noise is generated
with Bridson's algorithm and triangulation uses mathutils.geometry.delaunay_2d_cdt.
"""

import math
import random

import bpy
import bmesh
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt

# ---------------------------------------------------------------------------
# Parameters -- edit these
# ---------------------------------------------------------------------------
SIZE_X = 10.0          # plane width  (Blender units)
SIZE_Y = 10.0          # plane depth  (Blender units)
DENSITY = 4.0          # approx. points per square unit (higher = more points)
SEED = 0               # RNG seed for reproducible layouts
OBJECT_NAME = "BlueNoisePlane"
K_CANDIDATES = 30      # Bridson rejection samples per active point (30 is standard)


# ---------------------------------------------------------------------------
# Blue noise: Bridson's Poisson-disk sampling
# ---------------------------------------------------------------------------
def poisson_disk_sample(width, height, radius, k=30, rng=None):
    """Return a list of (x, y) points, no two closer than `radius`.

    Uses a background grid so it runs in roughly O(n)."""
    if rng is None:
        rng = random.Random()

    cell = radius / math.sqrt(2.0)
    grid_w = max(1, int(math.ceil(width / cell)))
    grid_h = max(1, int(math.ceil(height / cell)))
    grid = [[-1] * grid_w for _ in range(grid_h)]

    points = []
    active = []

    def grid_coords(p):
        return int(p[0] / cell), int(p[1] / cell)

    def fits(p):
        gx, gy = grid_coords(p)
        for iy in range(max(0, gy - 2), min(grid_h, gy + 3)):
            for ix in range(max(0, gx - 2), min(grid_w, gx + 3)):
                idx = grid[iy][ix]
                if idx != -1:
                    q = points[idx]
                    if (p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 < radius * radius:
                        return False
        return True

    # Seed with one random point.
    first = (rng.uniform(0, width), rng.uniform(0, height))
    points.append(first)
    active.append(0)
    gx, gy = grid_coords(first)
    grid[gy][gx] = 0

    while active:
        i = rng.randrange(len(active))
        base = points[active[i]]
        found = False
        for _ in range(k):
            ang = rng.uniform(0, 2 * math.pi)
            rad = rng.uniform(radius, 2 * radius)
            p = (base[0] + math.cos(ang) * rad, base[1] + math.sin(ang) * rad)
            if 0 <= p[0] < width and 0 <= p[1] < height and fits(p):
                points.append(p)
                idx = len(points) - 1
                active.append(idx)
                cx, cy = grid_coords(p)
                grid[cy][cx] = idx
                found = True
                break
        if not found:
            active.pop(i)

    return points


# ---------------------------------------------------------------------------
# Build the mesh
# ---------------------------------------------------------------------------
def build_plane(size_x, size_y, density, seed=0, name="BlueNoisePlane"):
    rng = random.Random(seed)

    # Convert "points per square unit" into a Poisson-disk radius.
    # For a Poisson-disk pattern the achieved point count is ~ area / (c * r^2);
    # r = 1/sqrt(density) lands close to the requested density in practice.
    density = max(density, 1e-6)
    radius = 1.0 / math.sqrt(density)

    raw = poisson_disk_sample(size_x, size_y, radius, k=K_CANDIDATES, rng=rng)

    # Guarantee the four corners so the triangulated hull is a clean rectangle.
    corners = [(0.0, 0.0), (size_x, 0.0), (size_x, size_y), (0.0, size_y)]
    for c in corners:
        if all((c[0] - p[0]) ** 2 + (c[1] - p[1]) ** 2 > (radius * 0.25) ** 2
               for p in raw):
            raw.append(c)

    # Center the plane on the origin.
    verts_2d = [Vector((x - size_x / 2.0, y - size_y / 2.0)) for (x, y) in raw]

    # Constrained Delaunay triangulation of the point cloud.
    # output_type 0 -> triangulated convex hull of the inputs.
    out_verts, _out_edges, out_faces, _ov, _oe, _of = delaunay_2d_cdt(
        verts_2d, [], [], 0, 1e-5
    )

    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    bm_verts = [bm.verts.new((v.x, v.y, 0.0)) for v in out_verts]
    bm.verts.ensure_lookup_table()

    for face in out_faces:
        try:
            bm.faces.new([bm_verts[i] for i in face])
        except ValueError:
            # Duplicate face -- skip.
            pass

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    # Select and make active.
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    print("[blue_noise_plane] {} points, {} triangles".format(
        len(out_verts), len(out_faces)))
    return obj


if __name__ == "__main__":
    build_plane(SIZE_X, SIZE_Y, DENSITY, seed=SEED, name=OBJECT_NAME)
