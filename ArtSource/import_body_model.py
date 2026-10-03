"""Converts a downloaded rocket model (.glb/.gltf/.fbx/.obj) into the same
body-FBX convention rocket_asset.complete() produces for the procedural
bodies - so RocketBodySetup / Rocket.SetBodyModel take it with no changes:

  * stood upright along +Z (Unity +Y), nose up
  * uniformly scaled so its height equals the preset's total height
    (engineHeight + bodyHeight + noseHeight in RocketPresets.cs)
  * centred on the origin (vertical centre of the stack), with an
    "OriginPoint" empty under a "<Key>_Body" root
  * textures embedded in the FBX

Usage:
  blender -b --python ArtSource/import_body_model.py -- \
      <source model> <Key> <height m> [--axis x|y|z] [--flip] \
      [--drop name1,name2] [--preview]

--axis   which source axis runs nose-to-tail (default: the longest one)
--flip   nose points the wrong way after orienting - turn it over
--drop   comma-separated substrings; objects whose names contain any are
         deleted (launch towers, ground stands, etc.)

Draco-compressed .glb files need decoding first: Ubuntu's Blender build
ships without the Draco library.
"""
import bpy
import sys
import math
import json
from pathlib import Path
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
src, key, height = Path(argv[0]).resolve(), argv[1], float(argv[2])
opts = argv[3:]
axis = opts[opts.index('--axis') + 1] if '--axis' in opts else None
flip = '--flip' in opts
drop = opts[opts.index('--drop') + 1].split(',') if '--drop' in opts else []
preview = '--preview' in opts

here = Path(__file__).resolve().parent
out_dir = here.parent / 'Assets/Models' / key
out_dir.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
ext = src.suffix.lower()
if ext in ('.glb', '.gltf'): bpy.ops.import_scene.gltf(filepath=str(src))
elif ext == '.fbx': bpy.ops.import_scene.fbx(filepath=str(src))
elif ext == '.obj': bpy.ops.wm.obj_import(filepath=str(src))
else: raise SystemExit('Unsupported model type: ' + ext)

for obj in list(scene.objects):
    if any(d and d.lower() in obj.name.lower() for d in drop):
        bpy.data.objects.remove(obj, do_unlink=True)

meshes = [o for o in scene.objects if o.type == 'MESH']
if not meshes: raise SystemExit('No meshes in ' + str(src))

# Bake every parent/instance transform into the mesh data, then drop the
# now-empty hierarchy so the only transform left is the one we build.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.make_single_user(object=True, obdata=True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(scene.objects):
    if o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)


def bounds():
    pts = [v.co for o in meshes for v in o.data.vertices]
    return (Vector([min(p[i] for p in pts) for i in range(3)]),
            Vector([max(p[i] for p in pts) for i in range(3)]))


def transform_all(m):
    for o in meshes: o.data.transform(m)


lo, hi = bounds()
size = hi - lo
if axis is None: axis = 'xyz'[max(range(3), key=lambda i: size[i])]
# Rotate the nose-to-tail axis onto +Z.
if axis == 'x': transform_all(Matrix.Rotation(math.radians(-90), 4, 'Y'))
elif axis == 'y': transform_all(Matrix.Rotation(math.radians(90), 4, 'X'))
if flip: transform_all(Matrix.Rotation(math.radians(180), 4, 'X'))

lo, hi = bounds()
scale = height / (hi.z - lo.z)
centre = (lo + hi) / 2
transform_all(Matrix.Scale(scale, 4) @ Matrix.Translation(-centre))
for o in meshes: o.data.update()

root = bpy.data.objects.new(key + '_Body', None)
scene.collection.objects.link(root)
root['description'] = key + ' imported from ' + src.name
root['units'] = 'metres'
for o in meshes:
    o.parent = root
origin = bpy.data.objects.new('OriginPoint', None)
scene.collection.objects.link(origin)
origin.parent = root

# Write textures out as real files next to the FBX. Unity's FBX importer
# (materialImportMode 2) resolves relative texture paths, but silently
# drops textures embedded inside the FBX - the first NASA Saturn V import
# came through untextured that way.
tex_dir = out_dir / 'Textures'
tex_dir.mkdir(exist_ok=True)
for img in bpy.data.images:
    if img.type != 'IMAGE' or not img.has_data and not img.packed_file: continue
    name = bpy.path.clean_name(img.name) + '.png'
    img.filepath_raw = str(tex_dir / name)
    img.file_format = 'PNG'
    img.save()
    if img.packed_file: img.unpack(method='REMOVE')
    img.filepath = str(tex_dir / name)

bpy.ops.object.select_all(action='DESELECT')
for o in meshes + [root, origin]: o.select_set(True)
bpy.context.view_layer.objects.active = root
asset = out_dir / (key + '_Body.fbx')
# Same axis/scale settings as rocket_asset.complete().
bpy.ops.export_scene.fbx(filepath=str(asset), use_selection=True,
    object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True, add_leaf_bones=False, bake_anim=False,
    mesh_smooth_type='FACE', path_mode='RELATIVE', embed_textures=False)

lo, hi = bounds()
stats = {'source': src.name, 'mesh_objects': len(meshes),
         'triangles': sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes),
         'bounds_m': dict(zip('xyz', [round(hi[i] - lo[i], 3) for i in range(3)])),
         'scale_applied': round(scale, 5), 'axis': axis, 'flip': flip}
print('IMPORT_BODY_COMPLETE', key, json.dumps(stats))

if preview:
    # Quick side-on Workbench render to check the nose points up.
    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = height * 1.1
    cam = bpy.data.objects.new('cam', cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0, -height * 2, 0)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.render.resolution_x, scene.render.resolution_y = 400, 800
    scene.render.filepath = str(Path(bpy.app.tempdir or '/tmp') / (key + '_import_preview.png'))
    if '--preview-out' in opts: scene.render.filepath = opts[opts.index('--preview-out') + 1]
    bpy.ops.render.render(write_still=True)
    print('PREVIEW', scene.render.filepath)
