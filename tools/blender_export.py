"""
Blender → LD59 scene exporter (Phase 2).

Exports visible Blender objects as a game scene XML:
  - Mesh objects  → FBX files + Mesh3D entities
  - Point lights  → PointLight entities
  - Base-Color textures are copied to Content/images/
  - Content.mgcb is patched with new FBX / texture entries

Usage:
  1. Open Blender's Scripting workspace
  2. Paste or load this file
  3. Set GAME_CONTENT_DIR and OUTPUT_SCENE_NAME below
  4. Run (Alt+P)

Notes:
  - Meshes are exported at the origin in Y-up space; position/scale are
    written to the XML from the object's world transform in Blender.
  - Rotation is converted via change-of-basis (Blender Z-up → Game Y-up).
    For complex rotations, applying rotations in Blender before export is
    more reliable (Object > Apply > Rotation).
  - Multiple objects sharing the same mesh data will share one FBX file.
  - Light intensity/range values are approximate; tune them after export.
"""

import bpy
import os
import shutil
import math
import xml.etree.ElementTree as ET
from xml.dom import minidom
from mathutils import Matrix

# ─────────────────────────────────────────────────────────────────────────────
# CONFIGURATION  ← edit these
# ─────────────────────────────────────────────────────────────────────────────
GAME_CONTENT_DIR       = r"F:\Dev\LD59\ld59\Content"
OUTPUT_SCENE_NAME      = "my_scene"     # writes to files/scenes/<name>.xml
POSITION_SCALE         = 100.0         # Blender internal units are meters; multiply by 100 for cm-based game units
LIGHT_INTENSITY_SCALE  = 1.0           # fine-tune multiplier on top of calibrated base (1.0 = calibrated)
LIGHT_RANGE_SCALE      = 1.0           # fine-tune multiplier on top of calibrated base (1.0 = calibrated)

# Calibration point: a 39.7 W Blender point light was visually matched to
# Range=300, Intensity=1.0 in-game.  Change these if you re-calibrate.
_REF_WATTS     = 39.7
_REF_RANGE     = 500.0
_REF_INTENSITY = 1.0
# ─────────────────────────────────────────────────────────────────────────────

# Change-of-basis: Blender Z-up → MonoGame Y-up
#   game.X = blender.X,  game.Y = blender.Z,  game.Z = -blender.Y
_BTG = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))

# ── MGCB entry templates ──────────────────────────────────────────────────────
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
/processorParam:ColorKeyColor=255,0,255,255
/processorParam:ColorKeyEnabled=False
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyAlpha=False
/processorParam:ResizeToPowerOfTwo=False
/processorParam:MakeSquare=False
/processorParam:TextureFormat=Color
/build:{path}"""


# ─────────────────────────────────────────────────────────────────────────────
# Coordinate conversion helpers
# ─────────────────────────────────────────────────────────────────────────────

def _pos_game(loc):
    """Blender world location → MonoGame Y-up position.
    matrix_world.to_translation() is always in Blender internal meters;
    POSITION_SCALE converts to game world units (100 = 1 game unit per cm)."""
    s = POSITION_SCALE
    return (loc.x * s, loc.z * s, -loc.y * s)

def _scale_game(s):
    """Blender world scale → MonoGame scale tuple (swap Y/Z axes)."""
    return (s.x, s.z, s.y)

def _rot_game(world_matrix):
    """Blender world rotation → MonoGame XYZ euler angles (radians)."""
    R_b = world_matrix.to_3x3().normalized()
    R_g = _BTG @ R_b @ _BTG.transposed()
    e   = R_g.to_euler('XYZ')
    return (e.x, e.y, e.z)


# ─────────────────────────────────────────────────────────────────────────────
# XML helpers
# ─────────────────────────────────────────────────────────────────────────────

def _v3str(x, y, z):
    return f"{x:.6f},{y:.6f},{z:.6f}"

def _add_pos3d(parent, pos):
    p = ET.SubElement(parent, "Position3D")
    ET.SubElement(p, "X").text = f"{pos[0]:.4f}"
    ET.SubElement(p, "Y").text = f"{pos[1]:.4f}"
    ET.SubElement(p, "Z").text = f"{pos[2]:.4f}"

def _prop(parent, name, value, type_):
    ET.SubElement(parent, "Property", Name=name, Value=str(value), Type=type_)


# ─────────────────────────────────────────────────────────────────────────────
# Asset helpers
# ─────────────────────────────────────────────────────────────────────────────

def _get_base_color_texture(obj):
    """
    Return (abs_filepath, basename_without_ext) for the Principled BSDF
    Base Color texture of the first material, or (None, None).
    """
    if not obj.material_slots:
        return None, None
    mat = obj.material_slots[0].material
    if not mat or not mat.use_nodes:
        return None, None
    for node in mat.node_tree.nodes:
        if node.type != 'BSDF_PRINCIPLED':
            continue
        bc = node.inputs.get('Base Color')
        if not bc or not bc.links:
            continue
        tn = bc.links[0].from_node
        if tn.type == 'TEX_IMAGE' and tn.image:
            fp    = bpy.path.abspath(tn.image.filepath)
            bname = os.path.splitext(os.path.basename(fp))[0]
            return fp, bname
    return None, None

def _export_fbx(mesh_data, fbx_path):
    """
    Export mesh_data as an FBX at origin with Y-up axes.
    A temporary zero-transform object is created and removed.
    """
    bpy.ops.object.select_all(action='DESELECT')
    tmp = bpy.data.objects.new("__ld59_export_tmp__", mesh_data)
    bpy.context.scene.collection.objects.link(tmp)
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = tmp

    bpy.ops.export_scene.fbx(
        filepath        = fbx_path,
        use_selection   = True,
        axis_forward    = '-Z',
        axis_up         = 'Y',
        # Do NOT use bake_space_transform — MonoGame's FbxImporter reads the
        # axis declarations and applies the Z-up→Y-up conversion itself via
        # ParentBone.Transform. Baking it here would double-apply the rotation.
        bake_space_transform = False,
        apply_unit_scale     = True,
        apply_scale_options  = 'FBX_SCALE_NONE',
        mesh_smooth_type     = 'FACE',
        use_mesh_modifiers   = True,
        add_leaf_bones       = False,
        bake_anim            = False,
        path_mode            = 'COPY',
        embed_textures       = False,
    )

    bpy.context.scene.collection.objects.unlink(tmp)
    bpy.data.objects.remove(tmp)
    print(f"  FBX  → {fbx_path}")

# ─────────────────────────────────────────────────────────────────────────────
# Main export
# ─────────────────────────────────────────────────────────────────────────────

def export_scene():
    models_dir = os.path.join(GAME_CONTENT_DIR, "models")
    images_dir = os.path.join(GAME_CONTENT_DIR, "images")
    scenes_dir = os.path.join(GAME_CONTENT_DIR, "files", "scenes")
    for d in (models_dir, images_dir, scenes_dir):
        os.makedirs(d, exist_ok=True)

    root     = ET.Element("Scene")
    entities = ET.SubElement(root, "Entities")

    new_mgcb_models   = []   # content-relative paths  e.g. "models/chair.fbx"
    new_mgcb_textures = []   # content-relative paths  e.g. "images/wood.png"

    exported_meshes = {}   # mesh_data.name → "models/<safe_name>" content key

    for obj in sorted(bpy.context.scene.objects, key=lambda o: o.name):
        if not obj.visible_get():
            continue

        # ── Mesh ──────────────────────────────────────────────────────────────
        if obj.type == 'MESH':
            mesh_key  = obj.data.name
            safe_name = mesh_key.replace(" ", "_")

            # Export FBX once per unique mesh data block
            if mesh_key not in exported_meshes:
                fbx_rel  = f"models/{safe_name}.fbx"
                fbx_path = os.path.join(GAME_CONTENT_DIR, fbx_rel)
                _export_fbx(obj.data, fbx_path)
                new_mgcb_models.append(fbx_rel)
                exported_meshes[mesh_key] = f"models/{safe_name}"

            model_key = exported_meshes[mesh_key]   # no extension (Content.Load key)

            wm        = obj.matrix_world
            game_pos  = _pos_game(wm.to_translation())
            game_scl  = _scale_game(wm.to_scale())
            game_rot  = _rot_game(wm)

            ent  = ET.SubElement(entities, "Entity", Name=obj.name)
            _add_pos3d(ent, game_pos)

            comp = ET.SubElement(ent, "Component", Type="Mesh3D")
            _prop(comp, "ModelPath", model_key, "String")

            # Texture (Base Color from first material)
            tex_path, tex_base = _get_base_color_texture(obj)
            if tex_path and os.path.exists(tex_path):
                ext = os.path.splitext(tex_path)[1].lower()

                # Copy to images/ — this is what Mesh3DComponent loads via TexturePath
                dst = os.path.join(images_dir, tex_base + ext)
                if os.path.normcase(os.path.abspath(tex_path)) != \
                   os.path.normcase(os.path.abspath(dst)):
                    shutil.copy2(tex_path, dst)
                    print(f"  TEX  → {dst}")
                _prop(comp, "TexturePath", f"images/{tex_base}", "String")
                tex_rel = f"images/{tex_base}{ext}"
                if tex_rel not in new_mgcb_textures:
                    new_mgcb_textures.append(tex_rel)

                # Copy to models/ so the FBX's local texture reference resolves
                # during the MonoGame content build (embed_textures=False writes
                # just the bare filename next to the FBX).
                models_tex_dst = os.path.join(models_dir, tex_base + ext)
                if os.path.normcase(os.path.abspath(tex_path)) != \
                   os.path.normcase(os.path.abspath(models_tex_dst)):
                    shutil.copy2(tex_path, models_tex_dst)
                    print(f"  TEX  → {models_tex_dst}")
                models_tex_rel = f"models/{tex_base}{ext}"
                if models_tex_rel not in new_mgcb_textures:
                    new_mgcb_textures.append(models_tex_rel)

            _prop(comp, "Scale", _v3str(*game_scl), "Vector3")

            # Only write RotationEuler when non-trivial
            if any(abs(r) > 1e-4 for r in game_rot):
                _prop(comp, "RotationEuler", _v3str(*game_rot), "Vector3")

        # ── Point light ───────────────────────────────────────────────────────
        elif obj.type == 'LIGHT' and obj.data.type == 'POINT':
            light    = obj.data
            game_pos = _pos_game(obj.matrix_world.to_translation())
            color    = light.color

            # Range: effective reach scales with sqrt(power) — inverse-square law.
            range_val = _REF_RANGE * math.sqrt(max(light.energy, 0.001) / _REF_WATTS) * LIGHT_RANGE_SCALE
            range_val = min(max(range_val, 5.0), 500.0)
            # Intensity: linear with power — brighter light, brighter output.
            intensity = _REF_INTENSITY * (light.energy / _REF_WATTS) * LIGHT_INTENSITY_SCALE

            ent  = ET.SubElement(entities, "Entity", Name=obj.name)
            _add_pos3d(ent, game_pos)

            comp = ET.SubElement(ent, "Component", Type="PointLight")
            _prop(comp, "Range",     f"{range_val:.1f}", "Single")
            _prop(comp, "Intensity", f"{intensity:.3f}", "Single")
            _prop(comp, "Color",
                  f"{color.r:.4f},{color.g:.4f},{color.b:.4f},1.0", "Color")

    # ── Write scene XML ───────────────────────────────────────────────────────
    raw    = ET.tostring(root, encoding='unicode')
    pretty = minidom.parseString(raw).toprettyxml(indent="  ")
    pretty = '\n'.join(pretty.split('\n')[1:])   # drop <?xml ...?> declaration

    out_path = os.path.join(scenes_dir, OUTPUT_SCENE_NAME + ".xml")
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(pretty)
    print(f"  XML  → {out_path}")

    # ── Patch Content.mgcb ────────────────────────────────────────────────────
    _patch_mgcb(new_mgcb_models, new_mgcb_textures)
    print("Export complete.")


def _patch_mgcb(new_models, new_textures):
    mgcb_path = os.path.join(GAME_CONTENT_DIR, "Content.mgcb")
    if not os.path.exists(mgcb_path):
        print(f"WARNING: Content.mgcb not found at {mgcb_path}")
        return

    with open(mgcb_path, 'r', encoding='utf-8') as f:
        content = f.read()

    additions = []
    for p in new_models:
        if f"/build:{p}" not in content:
            additions.append(_MGCB_MODEL.format(path=p))
            print(f"  + MGCB model:   {p}")
    for p in new_textures:
        if f"/build:{p}" not in content:
            additions.append(_MGCB_TEXTURE.format(path=p))
            print(f"  + MGCB texture: {p}")

    if additions:
        content = content.rstrip('\n') + '\n\n' + '\n\n'.join(additions) + '\n'
        with open(mgcb_path, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Content.mgcb: {len(additions)} new entries added")
    else:
        print("Content.mgcb: already up to date")


# Run when executed as a Blender script
export_scene()
