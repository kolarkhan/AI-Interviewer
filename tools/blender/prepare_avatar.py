"""Blender asset-preparation script for the AI Interviewer avatar.

It does NOT create a production character. It audits and prepares a licensed rigged model you already have
(e.g. exported from Character Creator, Daz, Ready Player Me-style tools or a marketplace asset):

  1. reports meshes, triangle counts, shape keys and the humanoid bones Unity needs;
  2. optionally renames shape keys to the Oculus viseme / ARKit names the app maps (JSON mapping file);
  3. optionally decimates meshes WITHOUT shape keys (body, clothes) to hit a mobile triangle budget;
  4. exports an FBX with settings that import cleanly into Unity (Humanoid rig, blendshapes kept).

Usage (Blender 4.x; untested on this build machine because Blender is not installed):
    blender --background character.blend --python tools/blender/prepare_avatar.py -- \
        --out Interviewer.fbx [--rename mapping.json] [--decimate 0.5] [--report-only]

Example mapping.json: {"V_Open": "viseme_aa", "V_Explosive": "viseme_PP", "Blink_L": "eyeBlinkLeft"}
"""

import argparse
import json
import sys

import bpy

REQUIRED_BONE_HINTS = ["hips", "spine", "chest", "neck", "head", "shoulder", "upperarm", "forearm", "hand"]
RECOMMENDED_SHAPES = ["viseme_aa", "viseme_E", "viseme_I", "viseme_O", "viseme_U", "viseme_PP", "viseme_FF",
                      "viseme_SS", "jawOpen", "eyeBlinkLeft", "eyeBlinkRight", "mouthSmileLeft", "mouthSmileRight"]
MOBILE_TRIANGLE_BUDGET = 50_000


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default="Interviewer.fbx")
    p.add_argument("--rename", help="JSON file mapping existing shape key names to target names")
    p.add_argument("--decimate", type=float, default=None, help="ratio 0.1-1.0 for meshes without shape keys")
    p.add_argument("--report-only", action="store_true")
    return p.parse_args(argv)


def triangle_count(obj):
    mesh = obj.data
    return sum(max(0, len(poly.vertices) - 2) for poly in mesh.polygons)


def report():
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    total = 0
    shapes = set()
    print("\n=== AI Interviewer avatar report ===")
    for o in meshes:
        tris = triangle_count(o)
        total += tris
        keys = [k.name for k in o.data.shape_keys.key_blocks] if o.data.shape_keys else []
        shapes.update(keys)
        print(f"MESH {o.name:30s} tris={tris:7d} shape_keys={len(keys)}")
    print(f"Total triangles: {total:,} (mobile budget ~{MOBILE_TRIANGLE_BUDGET:,})")
    if total > MOBILE_TRIANGLE_BUDGET:
        print("WARN: above budget; consider --decimate for body/clothing meshes or a LOD.")
    if len(armatures) != 1:
        print(f"WARN: expected exactly one armature, found {len(armatures)}")
    for arm in armatures:
        names = [b.name.lower().replace("_", "").replace(".", "") for b in arm.data.bones]
        missing = [h for h in REQUIRED_BONE_HINTS if not any(h in n for n in names)]
        print(f"ARMATURE {arm.name}: {len(names)} bones; missing humanoid hints: {missing or 'none'}")
    missing_shapes = [s for s in RECOMMENDED_SHAPES if s not in shapes]
    print(f"Recommended shape keys missing: {missing_shapes or 'none'}")
    print("====================================\n")


def rename_shape_keys(mapping_path):
    with open(mapping_path, encoding="utf-8") as f:
        mapping = json.load(f)
    renamed = 0
    for o in bpy.data.objects:
        if o.type != "MESH" or not o.data.shape_keys:
            continue
        for key in o.data.shape_keys.key_blocks:
            if key.name in mapping:
                key.name = mapping[key.name]
                renamed += 1
    print(f"Renamed {renamed} shape keys")


def decimate(ratio):
    ratio = max(0.1, min(1.0, ratio))
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        if o.data.shape_keys:
            print(f"Skip decimate on {o.name}: has shape keys (decimation would destroy them)")
            continue
        mod = o.modifiers.new(name="AII_Decimate", type="DECIMATE")
        mod.ratio = ratio
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
        print(f"Decimated {o.name} to ratio {ratio}")


def export(path):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=False,          # modifiers cannot be applied to meshes with shape keys
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=True,
        bake_anim=False,                   # animations come from separate clips
        path_mode="COPY",
        embed_textures=True,
    )
    print(f"Exported {path}")


def main():
    args = parse_args()
    report()
    if args.report_only:
        return
    if args.rename:
        rename_shape_keys(args.rename)
    if args.decimate:
        decimate(args.decimate)
    export(args.out)
    report()


if __name__ == "__main__":
    main()
