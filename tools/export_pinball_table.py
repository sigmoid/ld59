# Pinball Table Exporter for Blender
# Open in Blender's Text Editor (Scripting workspace) and press Run Script.
#
# ── Authoring conventions ────────────────────────────────────────────────────
#
#  wall_*      Mesh polyline (open chain of edges).
#              Author as a series of connected vertices/edges; no face needed.
#              Each distinct wall shape should be a separate object.
#
#  circle_*    Any mesh whose origin marks the bumper center.
#              Add → Mesh → Circle gives a clean visual ring.
#              Scale the object (S) to set the collision radius.
#              The world-space X extent of the object is used as the radius.
#
#  flipper_*   Exactly two vertices connected by one edge.
#              Vertex 0 (first added) = hinge point.
#              Vertex 1 = tip at the flipper's REST position.
#              Set these custom properties on the object:
#                activated_angle_offset  (float, degrees, e.g. -60 for left flipper)
#                activation_speed        (float, default 15)
#                return_speed            (float, default 10)
#                activation_key          (string, "LeftFlipper" or "RightFlipper")
#
#  PinballTable  A mesh plane whose local XY axes and scale define the table bounds.
#                UV (0,0) = plane's local (-X, -Y) corner.
#                UV (1,1) = plane's local (+X, +Y) corner.
#                Resize/rotate the plane to frame your table geometry.
#
# ── Configuration ────────────────────────────────────────────────────────────

REFERENCE_PLANE_NAME = "PinballTable"

# Output path. "//" means relative to the .blend file.
# Change to an absolute path or a path into your game's Content folder, e.g.:
#   OUTPUT_PATH = "C:/Dev/LD59/ld59/Content/files/pinball_table.json"
OUTPUT_PATH = "//pinball_table.json"

# ─────────────────────────────────────────────────────────────────────────────

import bpy
import json
import math


def walk_polyline(mesh):
    """
    Returns vertex indices ordered along a polyline chain.
    Starts from the endpoint with the lowest index (vertex 0 for a fresh edge,
    which is the hinge for flippers by convention).
    If the mesh is a closed loop, starts from vertex 0.
    """
    adj = {v.index: [] for v in mesh.vertices}
    for e in mesh.edges:
        a, b = e.vertices
        adj[a].append(b)
        adj[b].append(a)

    endpoints = [vi for vi, nbrs in adj.items() if len(nbrs) == 1]
    start = min(endpoints) if endpoints else 0

    visited = {start}
    order = [start]
    cur = start
    while True:
        nxt = [n for n in adj[cur] if n not in visited]
        if not nxt:
            break
        cur = nxt[0]
        order.append(cur)
        visited.add(cur)
    return order


def project_uv(world_pos, origin, right_vec, up_vec):
    """
    Projects a 3D world position onto the reference plane.
    Returns (u, v) where (0,0) is the plane's -X/-Y corner and (1,1) is +X/+Y.
    Points outside the plane will land outside [0,1] — that's fine.
    """
    local = world_pos - origin
    u = local.dot(right_vec) / right_vec.dot(right_vec)
    v = local.dot(up_vec)    / up_vec.dot(up_vec)
    return ((u + 1.0) / 2.0, (v + 1.0) / 2.0)


def export():
    plane = bpy.data.objects.get(REFERENCE_PLANE_NAME)
    if plane is None:
        raise ValueError(f"No object named '{REFERENCE_PLANE_NAME}' found. "
                         f"Create a mesh plane with that name to define the table bounds.")

    mat        = plane.matrix_world
    origin     = mat.translation.copy()
    right_vec  = mat.col[0].to_3d()   # local X axis, length = half-width
    up_vec     = mat.col[1].to_3d()   # local Y axis, length = half-height
    half_width = right_vec.length     # used to normalise circle radii

    out = {"version": 1, "walls": [], "circles": [], "flippers": [], "ramps": []}

    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or obj is plane:
            continue

        name  = obj.name
        mesh  = obj.data
        wv    = [obj.matrix_world @ v.co for v in mesh.vertices]  # world-space verts

        if name.startswith("wall_"):
            if len(mesh.vertices) < 2:
                print(f"[pinball] WARNING: {name} has <2 vertices, skipping")
                continue
            indices  = walk_polyline(mesh)
            verts_uv = [list(project_uv(wv[i], origin, right_vec, up_vec)) for i in indices]
            out["walls"].append({"name": name, "vertices": verts_uv})

        elif name.startswith("circle_"):
            center_uv   = list(project_uv(obj.matrix_world.translation.copy(),
                                          origin, right_vec, up_vec))
            world_radius = obj.matrix_world.col[0].to_3d().length
            # Normalise radius the same way positions are normalised:
            # full table width (UV 0→1) = 2 * half_width in world units
            radius_uv = world_radius / (2.0 * half_width)
            out["circles"].append({"name": name, "center": center_uv, "radius": radius_uv})

        elif name.startswith("flipper_"):
            if len(mesh.vertices) < 2:
                print(f"[pinball] WARNING: {name} needs 2 vertices, skipping")
                continue
            indices  = walk_polyline(mesh)
            hinge_uv = list(project_uv(wv[indices[0]], origin, right_vec, up_vec))
            tip_uv   = list(project_uv(wv[indices[1]], origin, right_vec, up_vec))

            # Show what custom properties are actually present so name mismatches
            # are immediately visible in the Blender console.
            obj_keys  = list(obj.keys())
            data_keys = list(obj.data.keys())
            print(f"[pinball] {name}  object props: {obj_keys}  mesh props: {data_keys}")

            def prop(key, default):
                val = obj.get(key) if obj.get(key) is not None else obj.data.get(key)
                if val is None:
                    print(f"[pinball]   '{key}' not found, using default {default!r}")
                    return default
                return val

            out["flippers"].append({
                "name":                   name,
                "hinge":                  hinge_uv,
                "tip":                    tip_uv,
                "activated_angle_offset": float(prop("activated_angle_offset", -60.0)),
                "activation_speed":       float(prop("activation_speed",        15.0)),
                "return_speed":           float(prop("return_speed",             10.0)),
                "activation_key":         str(  prop("activation_key",    "LeftFlipper")),
            })

        elif name.startswith("ramp_"):
            if len(mesh.vertices) < 2:
                print(f"[pinball] WARNING: {name} has <2 vertices, skipping")
                continue
            indices   = walk_polyline(mesh)
            table_z   = origin.z  # Z of reference plane = table surface
            points    = []
            for i in indices:
                wpos    = wv[i]
                u, v    = project_uv(wpos, origin, right_vec, up_vec)
                # Height above the table surface, normalised by the same factor as U
                height  = (wpos.z - table_z) / (2.0 * half_width)
                points.append([u, v, height])

            # entry_radius: check mesh data first, then object level
            er_val = obj.data.get("entry_radius")
            if er_val is None:
                er_val = obj.get("entry_radius")
            entry_radius = float(er_val) if er_val is not None else 0.05

            out["ramps"].append({
                "name":         name,
                "path":         points,
                "entry_radius": entry_radius,
            })

    path = bpy.path.abspath(OUTPUT_PATH)
    with open(path, "w") as f:
        json.dump(out, f, indent=2)

    print(f"[pinball] Exported to {path}")
    print(f"[pinball]   {len(out['walls'])} walls, "
          f"{len(out['circles'])} circles, "
          f"{len(out['flippers'])} flippers, "
          f"{len(out['ramps'])} ramps")


export()
