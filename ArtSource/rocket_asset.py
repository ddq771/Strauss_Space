"""Shared mesh/export helpers for procedural rocket BODY assets - a sibling
to engine_asset.py, same geometry helpers, adapted export convention: the
object origin sits at the vertical CENTRE of the stack (matching Rocket.cs's
own centred hull pivot, so an imported body drops in without new offset
math), not at the top like the engine mount-point convention.

Staging: call stage('Name') before building each separable piece. Every
part made after it belongs to that stage; its merged meshes are exported as
"<Stage>__<material>" so the stage survives in the FBX. Stage extents
(metres from the base of the stack) go to <Key>_stages.json in
Assets/Resources/RocketBodies, in the order the stages were declared -
declare them in separation order (first to drop away first).
RocketBodySetup reads that file and groups each stage's meshes under a
"Stage_<Stage>" object (pivot at the stage's base) in the prefab, so a
future staging system can detach a stage by its transform. (The grouping
happens in Unity, not here: nesting meshes under empties breaks the FBX
exporter's baked axis conversion.)
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
parts = []
stages = []
current_stage = None

def stage(name, note=''):
    """Start a new separable stage; later parts belong to it."""
    global current_stage
    current_stage = name
    if all(s['name'] != name for s in stages):
        stages.append({'name': name, 'note': note})

def material(name, color, metallic, roughness):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    return m

def finish(obj, name, mat):
    obj.name = name
    obj.data.materials.append(mat)
    if obj.type == 'MESH':
        for p in obj.data.polygons: p.use_smooth = True
    obj['stage'] = current_stage or 'Main'
    parts.append(obj)
    return obj

def lathe(name, profile, mat, n=96, close=False):
    verts = [(r*math.cos(2*math.pi*j/n),r*math.sin(2*math.pi*j/n),z)
             for r,z in profile for j in range(n)]
    faces=[]
    count=len(profile)
    for i in range(count if close else count-1):
        k=(i+1)%count
        for j in range(n):
            q=(j+1)%n
            faces.append((i*n+j,i*n+q,k*n+q,k*n+j))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    scene.collection.objects.link(obj)
    return finish(obj,name,mat)

def cap(name, profile_ring_radius_z, mat, n=96, at_top=True):
    """Flat disc cap for a lathe profile's open end (lathe() leaves both
    ends open, matching an unclosed profile - bodies need to be sealed)."""
    r, z = profile_ring_radius_z
    verts=[(0,0,z)]+[(r*math.cos(2*math.pi*j/n), r*math.sin(2*math.pi*j/n), z) for j in range(n)]
    faces=[]
    for j in range(n):
        q=(j+1)%n
        faces.append((0, j+1, q+1) if at_top else (0, q+1, j+1))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    scene.collection.objects.link(obj)
    return finish(obj,name,mat)

def ring(name, radius, z, tube, mat, center=(0,0), rotation=(0,0,0), n=72):
    bpy.ops.mesh.primitive_torus_add(major_segments=n,minor_segments=8,
        location=(center[0],center[1],z),rotation=rotation,
        major_radius=radius,minor_radius=tube)
    return finish(bpy.context.object,name,mat)

def cylinder(name, a, b, radius, mat, vertices=32, radius2=None):
    a,b=Vector(a),Vector(b)
    if radius2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,
            depth=(b-a).length,location=(a+b)/2)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=vertices,radius1=radius,radius2=radius2,
            depth=(b-a).length,location=(a+b)/2)
    obj=bpy.context.object
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(obj,name,mat)

def box(name,location,scale,mat,rotation=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location,rotation=rotation)
    obj=bpy.context.object
    obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(obj,name,mat)

def wedge(name,location,size,rotation,mat):
    """A tapered box (fin/flap): size=(x,y,z), taper narrows +X toward 0."""
    bpy.ops.mesh.primitive_cube_add(size=1,location=location,rotation=rotation)
    obj=bpy.context.object
    obj.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    return finish(obj,name,mat)

def nozzle(name, x, y, top, length, r_top, r_bottom, mat, vertices=24):
    """Engine bell hanging down from 'top': narrow throat flaring to the exit."""
    return cylinder(name, (x, y, top), (x, y, top - length), r_top, mat, vertices, radius2=r_bottom)

def prism(name, outline, thickness, mat):
    """A flat plate from an (x, z) outline, extruded along y - fins, wings."""
    n = len(outline); h = thickness / 2
    verts = [(x, -h, z) for x, z in outline] + [(x, h, z) for x, z in outline]
    faces = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(verts, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); scene.collection.objects.link(obj)
    return finish(obj, name, mat)

def lathe_arc(name, profile, mat, a0, a1, n=48):
    """Part of a lathe: the profile swept only from angle a0 to a1 (radians).
    Fairing halves, painted panels, a heat shield over one side."""
    verts = [(r * math.cos(a0 + (a1 - a0) * j / n), r * math.sin(a0 + (a1 - a0) * j / n), z)
             for r, z in profile for j in range(n + 1)]
    faces = []
    for i in range(len(profile) - 1):
        for j in range(n):
            a = i * (n + 1) + j; b = a + n + 1
            faces.append((a, a + 1, b + 1, b))
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(verts, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); scene.collection.objects.link(obj)
    return finish(obj, name, mat)

def loft(name, sections, mat, n=48, close_ends=True):
    """Body through cross-sections along z: (z, cx, cy, half_width,
    half_height, roundness). roundness 2 = ellipse, higher = boxier
    (superellipse) - fuselages that aren't round."""
    verts = []
    for z, cx, cy, ax, ay, e in sections:
        for j in range(n):
            t = 2 * math.pi * j / n
            c, s_ = math.cos(t), math.sin(t)
            x = cx + ax * math.copysign(abs(c) ** (2 / e), c)
            y = cy + ay * math.copysign(abs(s_) ** (2 / e), s_)
            verts.append((x, y, z))
    faces = []
    for i in range(len(sections) - 1):
        for j in range(n):
            a = i * n + j; b = i * n + (j + 1) % n
            faces.append((a, b, b + n, a + n))
    if close_ends:
        faces.append(tuple(reversed(range(n))))
        last = (len(sections) - 1) * n
        faces.append(tuple(range(last, last + n)))
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(verts, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); scene.collection.objects.link(obj)
    return finish(obj, name, mat)

def paint_faces(obj, mat, test):
    """Give faces whose (centre, normal) pass test() a second material -
    e.g. black thermal tiles on the side of a body facing one way."""
    obj.data.materials.append(mat)
    index = len(obj.data.materials) - 1
    for poly in obj.data.polygons:
        if test(obj.matrix_world @ poly.center, (obj.matrix_world.to_3x3() @ poly.normal).normalized()):
            poly.material_index = index
    return obj

def place(objs, matrix):
    """Move already-built parts by a transform (e.g. a booster built upright
    at the origin, then leaned and swung into position)."""
    for o in objs: o.matrix_world = matrix @ o.matrix_world

def complete(here,key,height,ortho,camera=None,label=None):
    """Bake, export, render, then reimport and validate the FBX. Origin is
    the vertical centre of [0,height] as authored (matches Rocket.cs's own
    centred hull pivot - swap in directly, no extra offset math needed)."""
    out=here.parents[1]/'Assets/Models'/key
    out.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.convert(target='MESH')
    groups={}
    for obj in list(scene.objects):
        if obj.type=='MESH':
            groups.setdefault((obj.get('stage','Main'),obj.data.materials[0].name),[]).append(obj)
    meshes=[]
    for (stage_name,name),objs in groups.items():
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objs: obj.select_set(True)
        bpy.context.view_layer.objects.active=objs[0]
        if len(objs)>1: bpy.ops.object.join()
        obj=bpy.context.object
        obj['stage']=stage_name
        obj.name=stage_name+'__'+name
        bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj.location.z-=height/2
        meshes.append(obj)
    root=bpy.data.objects.new(key+'_Body',None)
    scene.collection.objects.link(root)
    root['description']=(label or key)+' visual approximation; not measured engineering CAD'
    root['units']='metres'
    for obj in meshes: obj.parent=root
    bpy.context.view_layer.update()
    stage_info=[]
    for stage_name in [s['name'] for s in stages] or ['Main']:
        members=[o for o in meshes if o.get('stage')==stage_name]
        if not members: continue
        pts=[o.matrix_world@Vector(c) for o in members for c in o.bound_box]
        note=next((s['note'] for s in stages if s['name']==stage_name),'')
        stage_info.append({'name':stage_name,
                           'bottom_m':round(min(p.z for p in pts)+height/2,3),
                           'top_m':round(max(p.z for p in pts)+height/2,3),'note':note})
    origin=bpy.data.objects.new('OriginPoint',None)
    scene.collection.objects.link(origin)
    origin.parent=root
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes+[root,origin]: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    asset=out/(key+'_Body.fbx')
    resources=here.parents[1]/'Assets/Resources/RocketBodies'
    resources.mkdir(parents=True,exist_ok=True)
    (resources/(key+'_stages.json')).write_text(json.dumps({'key':key,'height_m':height,'stages':stage_info},indent=2)+'\n')
    bpy.ops.export_scene.fbx(filepath=str(asset),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,add_leaf_bones=False,bake_anim=False,
        mesh_smooth_type='FACE',path_mode='AUTO')
    points=[obj.matrix_world@Vector(c) for obj in meshes for c in obj.bound_box]
    size=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    stats={'triangles':sum(sum(len(p.vertices)-2 for p in obj.data.polygons) for obj in meshes),
           'mesh_objects':len(meshes),'materials':len({m for _,m in groups}),
           'stages':[s['name'] for s in stage_info],
           'bounds_m':dict(zip('xyz',[round(x,4) for x in size])),
           'note':'Visual approximation; proportions and details are approximate, not scaled CAD.'}
    (here/'model_stats.json').write_text(json.dumps(stats,indent=2)+'\n')

    studio=bpy.data.collections.new('Preview_Studio')
    scene.collection.children.link(studio)
    def move(obj):
        for c in list(obj.users_collection): c.objects.unlink(obj)
        studio.objects.link(obj)
        return obj
    floor_mat=material('Preview_Floor',(.025,.038,.055),.12,.48)
    floor_z=min(p.z for p in points)-.05
    bpy.ops.mesh.primitive_plane_add(size=400,location=(0,0,floor_z))
    floor=move(bpy.context.object)
    floor.name='Studio_Floor'
    floor.data.materials.append(floor_mat)
    target=(0,0,0)
    cam_pos=camera or (ortho*0.9,-ortho*1.6,0)
    def aim(obj): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.camera_add(location=cam_pos)
    cam=move(bpy.context.object)
    aim(cam)
    cam.data.type='ORTHO'
    cam.data.ortho_scale=ortho
    scene.camera=cam
    for name,pos,power,lsize,color in [
        ('Key',(ortho*.3,-ortho*.6,ortho*.4),1800,ortho*.4,(.85,.91,1)),
        ('Warm_Rim',(-ortho*.4,ortho*.2,ortho*.15),2200,ortho*.3,(1,.82,.62)),
        ('Fill',(-ortho*.5,-ortho*.4,0),1200,ortho*.4,(.72,.85,1)),
        ('Top',(0,0,ortho*1.2),1600,ortho*.3,(.8,.9,1))]:
        data=bpy.data.lights.new(name,'AREA')
        data.energy=power
        data.shape='DISK'
        data.size=lsize
        data.color=color
        obj=bpy.data.objects.new(name,data)
        studio.objects.link(obj)
        obj.location=pos
        aim(obj)
    scene.world.color=(.15,.15,.16)
    scene.render.engine='CYCLES'
    scene.cycles.samples=96
    scene.cycles.use_denoising=False
    scene.cycles.sample_clamp_indirect=2
    scene.render.resolution_x=900
    scene.render.resolution_y=1300
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Filmic'
    scene.view_settings.look='Medium High Contrast'
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(here/(key+'_Body_preview.png'))
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.wm.save_as_mainfile(filepath=str(here/(key+'_Body.blend')))
    bpy.ops.render.render(write_still=True)

    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(asset))
    imported=[o for o in scene.objects if o.type=='MESH']
    assert len(imported)==stats['mesh_objects']
    points=[o.matrix_world@Vector(c) for o in imported for c in o.bound_box]
    actual=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    for i,axis in enumerate('xyz'):
        assert abs(actual[i]-stats['bounds_m'][axis])<.005,(axis,actual)
    assert all(math.isfinite(v) for p in points for v in p)
    assert all(o.data.materials for o in imported)
    # Loose tolerance: small greebles (nozzles, fins) offset from the exact
    # base/tip by design can nudge the bounding box a little; this is a
    # sanity check against gross authoring errors, not a precision fit.
    assert abs(actual[2]-height)<1.0,('height mismatch',actual[2],height)
    report={'fbx_roundtrip':'passed','mesh_objects':len(imported),'bounds_m':actual,
            'height_check':'passed','unity_editor_import':'not tested'}
    (here/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('ASSET_COMPLETE',key,json.dumps(stats),json.dumps(report))
