"""
Blender → LD59 walking-sim level exporter.

Exports the whole visible Blender scene as a ready-to-walk level in one run:
  - Mesh objects        → FBX + Mesh3D entities (visual geometry; pickable/occluding)
  - Objects marked      → Interactable3D components (see custom properties below)
  - Walkable meshes      → a navmesh source OBJ, baked to a navmesh via NavMeshBaker
  - Point / Sun lights  → PointLight / DirectionalLight entities
  - PlayerStart          → spawn point
  - Writes scene XML + Scene3DAsset (Mode=Walk) + .scene3d and patches Content.mgcb

Meter-scaled (SceneScale = 1), unlike blender_export.py which is cm-scaled for the viewer.

Usage:
  1. Blender Scripting workspace → paste/open this file
  2. Set GAME_CONTENT_DIR / REPO_DIR / OUTPUT_SCENE_NAME below
  3. Run (Alt+P). Then rebuild the game and open <name>.scene3d in the File Explorer.

Custom properties (Object Properties → Custom Properties):
  interact_action   str   marks the object interactable; the dispatcher's verb (e.g. "show-text")
  interact_prompt   str   crosshair hint (default "interact")
  interact_target   str   what the action acts on (file path / glyph id / entity name)
  interact_message  str   payload text (for "show-text", the text shown)
  no_collide        1     exclude this mesh from the navmesh (props you can't walk on)
  player_start      1     use this object as the spawn (or just name an object "PlayerStart")
  start_hidden      1     entity starts invisible (revealed by a trigger / puzzle outcome)
Powergrid puzzle panel (the object becomes an interactable puzzle screen):
  puzzle_level      str   the powergrid scene, e.g. "files/scenes/powergrid/level-001.xml"
  puzzle_outcomes   str   which-solution -> effect DSL (see PuzzlePanelComponent), e.g.
                          "Gate=Sun=>reveal:Bridge | Gate=Moon=>reveal:Pit | =>show-text::Solved!"
  puzzle_size       float world width of the panel in meters (default 2.0)
  puzzle_yoffset    float panel centre height above the object origin (default 1.0)
  puzzle_yaw        float panel facing in game degrees (0 = +Z); omit to use the object's rotation
On lights:  intensity (float), range (float, point only), shadows (1/0, sun only)

Notes:
  - Model geometry is exported at origin in Y-up; position/rotation/scale go to the XML from
    the object's Blender world transform (Z-up → Y-up change of basis).
  - The navmesh is written in the SAME game-space conversion so visuals and navmesh align.
    Do the marker-cube check on your first export (a named cube; confirm its base meets the
    floor in-game) to catch any axis/scale mismatch.
"""

import bpy
import os
import math
import shutil
import subprocess
import xml.etree.ElementTree as ET
from xml.dom import minidom
from mathutils import Matrix, Vector

# ─────────────────────────────────────────────────────────────────────────────
# CONFIGURATION  ← edit these
# ─────────────────────────────────────────────────────────────────────────────
GAME_CONTENT_DIR  = r"F:\Dev\LD59\ld59\Content"
REPO_DIR          = r"F:\Dev\LD59"          # repo root (holds NavMeshBaker/)
OUTPUT_SCENE_NAME = "my_level"              # writes files/scenes/<name>.xml etc.
AMBIENT           = "60,60,70"             # base light "r,g,b" (0-255)
RUN_BAKER         = True                    # attempt to bake the navmesh via dotnet

# Navmesh agent parameters (meters / degrees), passed to NavMeshBaker.
NAV_RADIUS = 0.35
NAV_HEIGHT = 1.8
NAV_STEP   = 0.3
NAV_SLOPE  = 45.0
NAV_CELL   = 0.15
# ─────────────────────────────────────────────────────────────────────────────

# Change-of-basis: Blender Z-up → MonoGame Y-up  (game.X = bl.X, game.Y = bl.Z, game.Z = -bl.Y)
_BTG = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))

_MGCB_MODEL = """\
#begin {path}
/importer:FbxImporter
/processor:ModelProcessor
/processorParam:ColorKeyColor=0,0,0,0
/processorParam:ColorKeyEnabled=True
/processorParam:DefaultEffect=BasicEffect
/processorParam:GenerateMipmaps=True
/processorParam:GenerateTangentFrames=False
/processorParam:PremultiplyTextureAlpha=True
/processorParam:PremultiplyVertexColors=True
/processorParam:ResizeTexturesToPowerOfTwo=False
/processorParam:RotationX=0
/processorParam:RotationY=0
/processorParam:RotationZ=0
/processorParam:Scale=1
/processorParam:SwapWindingOrder=False
/processorParam:TextureFormat=Color
/build:{path}"""

_MGCB_TEXTURE = """\
#begin {path}
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyEnabled=False
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyAlpha=False
/processorParam:ResizeToPowerOfTwo=False
/processorParam:MakeSquare=False
/processorParam:TextureFormat=Color
/build:{path}"""

_MGCB_COPY = """\
#begin {path}
/copy:{path}"""


# ── coordinate helpers ────────────────────────────────────────────────────────
def _pos_game(loc):
    return (loc.x, loc.z, -loc.y)

def _scale_game(s):
    return (s.x, s.z, s.y)

def _rot_game(world_matrix):
    # Use decompose() to recover a clean rotation quaternion. to_3x3().normalized() only works
    # for uniform positive scale -- under non-uniform or negative (mirror) scale it folds the
    # shear/flip into the "rotation" and mis-orients the object. decompose() separates rotation
    # from scale properly (the sign of a mirror lands in scale, not rotation). Conjugate by _BTG
    # to move the rotation into the game's Y-up frame, then read XYZ euler (matches the game's
    # CreateRotationX*Y*Z order).
    quat = world_matrix.decompose()[1]
    R_g = _BTG @ quat.to_matrix() @ _BTG.transposed()
    return tuple(R_g.to_euler('XYZ'))

def _vert_game(co):
    return (co.x, co.z, -co.y)


# ── XML helpers ───────────────────────────────────────────────────────────────
def _v3(x, y, z):
    return f"{x:.6f},{y:.6f},{z:.6f}"

def _add_pos3d(parent, pos):
    p = ET.SubElement(parent, "Position3D")
    ET.SubElement(p, "X").text = f"{pos[0]:.4f}"
    ET.SubElement(p, "Y").text = f"{pos[1]:.4f}"
    ET.SubElement(p, "Z").text = f"{pos[2]:.4f}"

def _prop(parent, name, value, type_):
    ET.SubElement(parent, "Property", Name=name, Value=str(value), Type=type_)

def _write_pretty(root, path):
    raw = ET.tostring(root, encoding='unicode')
    pretty = minidom.parseString(raw).toprettyxml(indent="  ")
    pretty = '\n'.join(pretty.split('\n')[1:])  # drop the <?xml?> line
    with open(path, 'w', encoding='utf-8') as f:
        f.write(pretty)


# ── object property helpers ───────────────────────────────────────────────────
def _interact_props(obj):
    """Return an interaction dict if the object is marked interactable, else None."""
    action = obj.get("interact_action")
    if action is None and not obj.get("interact"):
        return None
    return {
        "prompt":  str(obj.get("interact_prompt", "interact")),
        "action":  str(action if action is not None else "show-text"),
        "target":  str(obj.get("interact_target", "")),
        "message": str(obj.get("interact_message", "")),
    }

def _is_player_start(obj):
    return obj.name == "PlayerStart" or bool(obj.get("player_start"))

def _has_armature(obj):
    return any(m.type == 'ARMATURE' for m in obj.modifiers)

def _base_color_texture(obj):
    if not obj.material_slots:
        return None, None
    mat = obj.material_slots[0].material
    if not mat or not mat.use_nodes:
        return None, None
    for node in mat.node_tree.nodes:
        if node.type != 'BSDF_PRINCIPLED':
            continue
        bc = node.inputs.get('Base Color')
        if bc and bc.links:
            tn = bc.links[0].from_node
            if tn.type == 'TEX_IMAGE' and tn.image:
                fp = bpy.path.abspath(tn.image.filepath)
                return fp, os.path.splitext(os.path.basename(fp))[0]
    return None, None


def _export_fbx(obj, fbx_path):
    """Export obj as an FBX at origin in Y-up space (modifiers/poses baked in)."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(depsgraph)
    baked = bpy.data.meshes.new_from_object(eval_obj, depsgraph=depsgraph)

    # Bake the Blender-Z-up -> game-Y-up change of basis straight into the vertex data as a
    # -90° rotation about X: (x,y,z) -> (x, z, -y). This is the same _BTG / _vert_game basis
    # the navmesh uses, so the visual mesh and navmesh line up exactly.
    #
    # We do the rotation by hand rather than via the FBX exporter's axis_up='Y' +
    # bake_space_transform because bake_space_transform ALSO bakes Blender's meters->cm unit
    # scale into the geometry (blowing it up 100x), and apply_unit_scale=False does not stop
    # that. MonoGame ignores the FBX's declared axis + UnitScaleFactor and just reads raw
    # vertex coords, so we hand it Y-up meters directly and export with native axes (no
    # conversion added) and bake_space_transform=False (which keeps 1:1 meter scale).
    baked.transform(Matrix.Rotation(-math.pi / 2, 4, 'X'))

    bpy.ops.object.select_all(action='DESELECT')
    tmp = bpy.data.objects.new("__ld59_walk_tmp__", baked)
    bpy.context.scene.collection.objects.link(tmp)
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = tmp

    bpy.ops.export_scene.fbx(
        filepath=fbx_path, use_selection=True,
        axis_forward='-Y', axis_up='Z',       # native axes: don't let Blender add a conversion
        bake_space_transform=False,           # keep 1:1 meter scale (bake drags in the 100x)
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode='STRIP', embed_textures=False,
    )

    bpy.context.scene.collection.objects.unlink(tmp)
    bpy.data.objects.remove(tmp)
    bpy.data.meshes.remove(baked)
    print(f"  FBX  → {fbx_path}")


def _gather_walkable(obj, verts_out, faces_out):
    """Append obj's world-space triangles (in game coords) to the navmesh soup."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(depsgraph)
    mesh = eval_obj.to_mesh()
    mesh.calc_loop_triangles()
    wm = obj.matrix_world
    # A negative-determinant world transform (negative/mirror scale) flips triangle winding,
    # so faces that should point up end up pointing down. Recast's slope filter then rejects
    # them as ceilings and the surface silently drops out of the navmesh. Flip the winding
    # back so the baked normals stay up regardless of how the object was scaled in Blender.
    flip = wm.to_3x3().determinant() < 0
    if flip:
        print(f"  (winding flip: {obj.name} has negative scale)")
    base = len(verts_out)
    for v in mesh.vertices:
        verts_out.append(_vert_game(wm @ v.co))
    for tri in mesh.loop_triangles:
        a, b, c = tri.vertices
        if flip:
            a, c = c, a
        faces_out.append((base + a, base + b, base + c))
    eval_obj.to_mesh_clear()


def _write_obj(path, verts, faces):
    with open(path, 'w', encoding='utf-8') as f:
        f.write("# walkable geometry for NavMeshBaker (game-space, Y-up)\n")
        for (x, y, z) in verts:
            f.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")
        for (a, b, c) in faces:
            f.write(f"f {a + 1} {b + 1} {c + 1}\n")
    print(f"  NAV  → {path}  ({len(verts)} verts, {len(faces)} tris)")


# ─────────────────────────────────────────────────────────────────────────────
# Main export
# ─────────────────────────────────────────────────────────────────────────────
def export_level():
    models_dir = os.path.join(GAME_CONTENT_DIR, "models")
    images_dir = os.path.join(GAME_CONTENT_DIR, "images")
    scenes_dir = os.path.join(GAME_CONTENT_DIR, "files", "scenes")
    root_dir   = os.path.join(GAME_CONTENT_DIR, "files", "root")
    for d in (models_dir, images_dir, scenes_dir, root_dir):
        os.makedirs(d, exist_ok=True)

    root = ET.Element("Scene")
    entities = ET.SubElement(root, "Entities")

    new_models, new_textures = [], []
    exported = {}                 # mesh data name → "models/<safe>"
    nav_verts, nav_faces = [], []
    player_start_written = False

    for obj in sorted(bpy.context.scene.objects, key=lambda o: o.name):
        if not obj.visible_get():
            continue

        # ── Spawn ────────────────────────────────────────────────────────────
        if _is_player_start(obj):
            ent = ET.SubElement(entities, "Entity", Name="PlayerStart")
            _add_pos3d(ent, _pos_game(obj.matrix_world.to_translation()))
            player_start_written = True
            print(f"  SPAWN {obj.name}")
            continue

        # ── Mesh ─────────────────────────────────────────────────────────────
        if obj.type == 'MESH':
            if _has_armature(obj):
                key, safe = f"__posed__{obj.name}", obj.name.replace(" ", "_")
            else:
                key, safe = obj.data.name, obj.data.name.replace(" ", "_")

            if key not in exported:
                fbx_rel = f"models/{safe}.fbx"
                _export_fbx(obj, os.path.join(GAME_CONTENT_DIR, fbx_rel))
                new_models.append(fbx_rel)
                exported[key] = f"models/{safe}"
            model_key = exported[key]

            wm = obj.matrix_world
            ent = ET.SubElement(entities, "Entity", Name=obj.name)
            if obj.get("start_hidden"):
                ent.set("Visible", "false")   # revealed later by a trigger/puzzle outcome
            _add_pos3d(ent, _pos_game(wm.to_translation()))

            comp = ET.SubElement(ent, "Component", Type="Mesh3D")
            _prop(comp, "ModelPath", model_key, "String")

            tex_path, tex_base = _base_color_texture(obj)
            if tex_path and os.path.exists(tex_path):
                ext = os.path.splitext(tex_path)[1].lower()
                dst = os.path.join(images_dir, tex_base + ext)
                if os.path.normcase(os.path.abspath(tex_path)) != os.path.normcase(os.path.abspath(dst)):
                    shutil.copy2(tex_path, dst)
                _prop(comp, "TexturePath", f"images/{tex_base}", "String")
                tex_rel = f"images/{tex_base}{ext}"
                if tex_rel not in new_textures:
                    new_textures.append(tex_rel)

            _prop(comp, "Scale", _v3(*_scale_game(wm.to_scale())), "Vector3")
            rot = _rot_game(wm)
            if any(abs(r) > 1e-4 for r in rot):
                _prop(comp, "RotationEuler", _v3(*rot), "Vector3")

            interact = _interact_props(obj)
            if interact:
                ic = ET.SubElement(ent, "Component", Type="Interactable3D")
                _prop(ic, "PromptText", interact["prompt"], "String")
                _prop(ic, "Action", interact["action"], "String")
                if interact["target"]:
                    _prop(ic, "Target", interact["target"], "String")
                if interact["message"]:
                    _prop(ic, "Message", interact["message"], "String")
                print(f"  INTERACT {obj.name}  action={interact['action']}")

            # ── Powergrid puzzle panel ───────────────────────────────────────────
            puzzle_level = obj.get("puzzle_level")
            if puzzle_level:
                if not interact:  # puzzle objects are always interactable (hover + E to open)
                    ic = ET.SubElement(ent, "Component", Type="Interactable3D")
                    _prop(ic, "PromptText", str(obj.get("interact_prompt", "solve the puzzle")), "String")
                pc = ET.SubElement(ent, "Component", Type="PuzzlePanel")
                _prop(pc, "LevelPath", str(puzzle_level), "String")
                if obj.get("puzzle_outcomes"):
                    _prop(pc, "Outcomes", str(obj.get("puzzle_outcomes")), "String")
                if obj.get("puzzle_size") is not None:
                    _prop(pc, "PanelSize", f"{float(obj.get('puzzle_size')):.3f}", "Single")
                if obj.get("puzzle_yoffset") is not None:
                    _prop(pc, "PanelYOffset", f"{float(obj.get('puzzle_yoffset')):.3f}", "Single")
                # Panel facing (world-mounted): explicit puzzle_yaw (game degrees), else derive
                # from the object's rotation so rotating it in Blender aims the panel.
                if obj.get("puzzle_yaw") is not None:
                    _prop(pc, "PanelYaw", f"{float(obj.get('puzzle_yaw')):.2f}", "Single")
                else:
                    _prop(pc, "PanelYaw", f"{math.degrees(_rot_game(obj.matrix_world)[1]):.2f}", "Single")
                print(f"  PUZZLE {obj.name}  level={puzzle_level}")

            # Puzzle objects and props flagged no_collide are excluded from the navmesh.
            if not obj.get("no_collide") and not puzzle_level:
                _gather_walkable(obj, nav_verts, nav_faces)
                print(f"  MESH  {obj.name}  → navmesh")
            else:
                reason = "puzzle panel" if puzzle_level else "no_collide"
                print(f"  MESH  {obj.name}  → SKIPPED ({reason})")

        # ── Sun (directional) light ───────────────────────────────────────────
        elif obj.type == 'LIGHT' and obj.data.type == 'SUN':
            photon = obj.matrix_world.to_3x3() @ Vector((0.0, 0.0, -1.0))
            photon.normalize()
            toward = (-photon.x, -photon.z, photon.y)
            color = obj.data.color
            ent = ET.SubElement(entities, "Entity", Name=obj.name)
            _add_pos3d(ent, _pos_game(obj.matrix_world.to_translation()))
            comp = ET.SubElement(ent, "Component", Type="DirectionalLight")
            _prop(comp, "Direction", _v3(*toward), "Vector3")
            _prop(comp, "Color", f"{color.r:.4f},{color.g:.4f},{color.b:.4f},1.0", "Color")
            _prop(comp, "Intensity", f"{float(obj.get('intensity', 1.0)):.3f}", "Single")
            _prop(comp, "CastShadows", "True" if obj.get("shadows", True) else "False", "Boolean")
            print(f"  SUN  {obj.name}")

        # ── Point light ───────────────────────────────────────────────────────
        elif obj.type == 'LIGHT' and obj.data.type == 'POINT':
            color = obj.data.color
            ent = ET.SubElement(entities, "Entity", Name=obj.name)
            _add_pos3d(ent, _pos_game(obj.matrix_world.to_translation()))
            comp = ET.SubElement(ent, "Component", Type="PointLight")
            _prop(comp, "Range", f"{float(obj.get('range', 30.0)):.1f}", "Single")
            _prop(comp, "Intensity", f"{float(obj.get('intensity', 2.0)):.3f}", "Single")
            _prop(comp, "Color", f"{color.r:.4f},{color.g:.4f},{color.b:.4f},1.0", "Color")
            print(f"  POINT {obj.name}")

    if not player_start_written:
        print("  WARNING: no PlayerStart found (name an object 'PlayerStart' or set player_start=1). "
              "The game falls back to the first navmesh triangle.")

    # ── write scene, asset, .scene3d ──────────────────────────────────────────
    scene_rel = f"files/scenes/{OUTPUT_SCENE_NAME}.xml"
    asset_rel = f"files/scenes/{OUTPUT_SCENE_NAME}_asset.xml"
    nav_rel   = f"files/scenes/{OUTPUT_SCENE_NAME}_navmesh.obj"
    scene3d_rel = f"files/root/{OUTPUT_SCENE_NAME}.scene3d"

    _write_pretty(root, os.path.join(GAME_CONTENT_DIR, scene_rel))
    print(f"  XML  → {scene_rel}")

    asset = ET.Element("Scene3DAsset")
    ET.SubElement(asset, "ScenePath").text = scene_rel
    ET.SubElement(asset, "Title").text = OUTPUT_SCENE_NAME
    ET.SubElement(asset, "Mode").text = "Walk"
    ET.SubElement(asset, "NavMeshPath").text = nav_rel
    ET.SubElement(asset, "Ambient").text = AMBIENT
    _write_pretty(asset, os.path.join(GAME_CONTENT_DIR, asset_rel))
    print(f"  ASSET→ {asset_rel}")

    with open(os.path.join(GAME_CONTENT_DIR, scene3d_rel), 'w', encoding='utf-8') as f:
        f.write(asset_rel + "\n")
    print(f"  SCN3D→ {scene3d_rel}")

    # ── navmesh source + bake ─────────────────────────────────────────────────
    nav_src = os.path.join(scenes_dir, f"{OUTPUT_SCENE_NAME}_navmesh_src.obj")
    _write_obj(nav_src, nav_verts, nav_faces)
    _bake_navmesh(nav_src, os.path.join(GAME_CONTENT_DIR, nav_rel))

    # ── patch Content.mgcb ────────────────────────────────────────────────────
    _patch_mgcb(new_models, new_textures, [scene_rel, asset_rel, nav_rel, scene3d_rel])
    print("Export complete. Rebuild the game and open "
          f"{OUTPUT_SCENE_NAME}.scene3d in the File Explorer.")


def _bake_navmesh(src, out):
    cmd = ["dotnet", "run", "--project", os.path.join(REPO_DIR, "NavMeshBaker"), "--",
           src, out, "--radius", str(NAV_RADIUS), "--height", str(NAV_HEIGHT),
           "--step", str(NAV_STEP), "--slope", str(NAV_SLOPE), "--cell", str(NAV_CELL),
           "--validate"]
    if not RUN_BAKER:
        print("  (RUN_BAKER off) bake manually:\n    " + " ".join(cmd))
        return
    try:
        subprocess.run(cmd, cwd=REPO_DIR, check=True)
        print("  NAV  baked.")
    except Exception as e:
        print(f"  Baker did not run ({e}). Bake manually:\n    " + " ".join(cmd))


def _patch_mgcb(models, textures, copies):
    mgcb = os.path.join(GAME_CONTENT_DIR, "Content.mgcb")
    if not os.path.exists(mgcb):
        print(f"  WARNING: Content.mgcb not found at {mgcb}")
        return
    with open(mgcb, 'r', encoding='utf-8') as f:
        content = f.read()

    additions = []
    for p in models:
        if f"/build:{p}" not in content:
            additions.append(_MGCB_MODEL.format(path=p))
    for p in textures:
        if f"/build:{p}" not in content:
            additions.append(_MGCB_TEXTURE.format(path=p))
    for p in copies:
        if f"/copy:{p}" not in content and f"/build:{p}" not in content:
            additions.append(_MGCB_COPY.format(path=p))

    if additions:
        content = content.rstrip('\n') + '\n\n' + '\n\n'.join(additions) + '\n'
        with open(mgcb, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"  MGCB: {len(additions)} new entries")
    else:
        print("  MGCB: up to date")


export_level()
