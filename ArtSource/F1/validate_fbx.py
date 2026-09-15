"""Round-trip validation of the exported asset using Blender's FBX importer."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

here=Path(__file__).resolve().parent
asset=here.parents[1]/'Assets/Models/F1/F1_Engine.fbx'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(asset))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
assert len(meshes)==5, len(meshes)
assert bpy.data.objects.get('MountPoint') is not None
assert bpy.data.objects.get('ExhaustPoint') is not None
points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
sizes=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
assert all(math.isfinite(c) for p in points for c in p)
assert 3.7<sizes[0]<3.9 and 3.7<sizes[1]<3.9 and 5.5<sizes[2]<5.8, sizes
inner=next(o for o in meshes if 'InnerBell' in o.name)
assert sum(p.normal.x*p.center.x+p.normal.y*p.center.y for p in inner.data.polygons)<0
assert all(len(o.data.materials)>0 for o in meshes)
report={'fbx_roundtrip':'passed','mesh_objects':len(meshes),
        'bounds_metres_blender_xyz':sizes,'mount_and_exhaust_markers':True,
        'inner_bell_normals':'inward','unity_editor_import':'not tested'}
(here/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report))
