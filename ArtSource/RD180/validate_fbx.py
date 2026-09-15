"""Validate the RD-180 asset by reimporting the exported FBX into Blender."""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

here=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(here.parents[1]/'Assets/Models/RD180/RD180_Engine.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
expected=json.loads((here/'model_stats.json').read_text())
assert len(meshes)==expected['mesh_objects']
points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
size=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
assert all(math.isfinite(c) for p in points for c in p)
for i,a in enumerate('xyz'):
    assert abs(size[i]-expected['bounds_m'][a])<.002,(a,size[i])
assert 3<size[0]<3.2 and 3.5<size[2]<3.7, size
for name in ['MountPoint','ExhaustPoint_L','ExhaustPoint_R']:
    assert bpy.data.objects.get(name) is not None,name
left=bpy.data.objects['ExhaustPoint_L'].matrix_world.translation
right=bpy.data.objects['ExhaustPoint_R'].matrix_world.translation
assert abs((right-left).length-1.64)<.002
assert all(o.data.materials for o in meshes)
inner=next(o for o in meshes if 'InnerBell' in o.name)
score=0
for p in inner.data.polygons:
    center=inner.matrix_world@p.center
    normal=inner.matrix_world.to_3x3()@p.normal
    cx=-.82 if center.x<0 else .82
    score+=normal.x*(center.x-cx)+normal.y*center.y
assert score<0,score
report={'fbx_roundtrip':'passed','mesh_objects':len(meshes),
        'bounds_metres_blender_xyz':size,'two_exhaust_markers':True,
        'inner_bell_normals':'inward','unity_editor_import':'not tested'}
(here/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report))
