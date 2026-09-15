"""Shared mesh/export helpers for procedural visual engine assets."""
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

def material(name, color, metallic, roughness):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    return m

steel = material('Engine_BrushedSteel', (.38,.43,.46), .82, .32)
dark = material('Engine_NozzleAlloy', (.105,.13,.15), .75, .4)
inside = material('Engine_InnerBell', (.045,.052,.06), .55, .57)
silver = material('Engine_PipeSteel', (.64,.69,.71), .85, .25)
bronze = material('Engine_HeatTint', (.28,.18,.10), .72, .4)
black = material('Engine_Gaskets', (.022,.027,.031), .15, .62)

def finish(obj, name, mat):
    obj.name = name
    obj.data.materials.append(mat)
    if obj.type == 'MESH':
        for p in obj.data.polygons: p.use_smooth = True
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

def ring(name, radius, z, tube, mat, center=(0,0), rotation=(0,0,0), n=72):
    bpy.ops.mesh.primitive_torus_add(major_segments=n,minor_segments=8,
        location=(center[0],center[1],z),rotation=rotation,
        major_radius=radius,minor_radius=tube)
    return finish(bpy.context.object,name,mat)

def cylinder(name, a, b, radius, mat, vertices=32):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,
        depth=(b-a).length,location=(a+b)/2)
    obj=bpy.context.object
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    bevel=obj.modifiers.new('Edge highlights','BEVEL')
    bevel.width=min(.012,radius*.12)
    bevel.segments=2
    return finish(obj,name,mat)

def tube(name, coords, radius, mat, resolution=4):
    curve=bpy.data.curves.new(name,'CURVE')
    curve.dimensions='3D'
    curve.resolution_u=resolution
    curve.bevel_depth=radius
    curve.bevel_resolution=2
    curve.use_fill_caps=True
    s=curve.splines.new('BEZIER')
    s.bezier_points.add(len(coords)-1)
    for p,co in zip(s.bezier_points,coords):
        p.co=co
        p.handle_left_type='AUTO'
        p.handle_right_type='AUTO'
    obj=bpy.data.objects.new(name,curve)
    scene.collection.objects.link(obj)
    return finish(obj,name,mat)

def bolts(radius,z,count=24,center=(0,0)):
    for i in range(count):
        a=2*math.pi*i/count
        x,y=center[0]+radius*math.cos(a),center[1]+radius*math.sin(a)
        cylinder('Fastener',(x,y,z-.018),(x,y,z+.018),.028,silver,6)

def box(name,location,scale,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    obj=bpy.context.object
    obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bevel=obj.modifiers.new('Edge highlights','BEVEL')
    bevel.width=.016
    bevel.segments=2
    return finish(obj,name,mat)

def bell(profile,mat,ribs=0,hoops=()):
    lathe('Nozzle_Outer',profile,mat,128)
    lathe('Nozzle_Inner',[(r-.018,z) for r,z in reversed(profile)],inside,128)
    ring('Exit_Lip',profile[0][0]-.006,profile[0][1]+.01,.023,silver,n=128)
    for r,z in hoops: ring('Jacket_Band',r,z,.018,steel)
    for i in range(ribs):
        a=2*math.pi*i/ribs
        tube('Cooling_Rib',[((r+.008)*math.cos(a),(r+.008)*math.sin(a),z)
                           for r,z in profile],.0065,steel,2)

def pump(name,center,radius,length,mat=steel):
    x,y,z=center
    cylinder(name,(x,y,z-length/2),(x,y,z+length/2),radius,mat,48)
    for h in [z-length*.45,z,z+length*.45]:
        ring(name+'_Flange',radius+.012,h,.021,silver,(x,y))
    bolts(radius,z+length/2,12,(x,y))

def complete(here,key,height,ortho,camera=(6,-11,2.5)):
    """Bake, export, render, then independently reimport and validate FBX."""
    out=here.parents[1]/'Assets/Models'/key
    out.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.convert(target='MESH')
    groups={}
    for obj in list(scene.objects):
        if obj.type=='MESH': groups.setdefault(obj.data.materials[0].name,[]).append(obj)
    meshes=[]
    for name,objs in groups.items():
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objs: obj.select_set(True)
        bpy.context.view_layer.objects.active=objs[0]
        if len(objs)>1: bpy.ops.object.join()
        obj=bpy.context.object
        obj.name=name.replace('Engine_','Geometry_')
        obj.data.materials[0].name=name.replace('Engine_',key+'_')
        bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        if 'InnerBell' in name:
            score=sum(p.normal.x*p.center.x+p.normal.y*p.center.y for p in obj.data.polygons)
            if score>0:
                for p in obj.data.polygons: p.flip()
        obj.location.z-=height
        meshes.append(obj)
    root=bpy.data.objects.new(key+'_Engine',None)
    scene.collection.objects.link(root)
    root['description']=key+' visual approximation; not measured engineering CAD'
    root['units']='metres'
    for obj in meshes: obj.parent=root
    markers=[]
    for name,position in [('MountPoint',(0,0,0)),('ExhaustPoint',(0,0,-height))]:
        obj=bpy.data.objects.new(name,None)
        scene.collection.objects.link(obj)
        obj.parent=root
        obj.location=position
        if name=='ExhaustPoint': obj.rotation_euler=(math.pi,0,0)
        markers.append(obj)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes+[root]+markers: obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    asset=out/(key+'_Engine.fbx')
    bpy.ops.export_scene.fbx(filepath=str(asset),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,add_leaf_bones=False,bake_anim=False,
        mesh_smooth_type='FACE',path_mode='AUTO')
    points=[obj.matrix_world@Vector(c) for obj in meshes for c in obj.bound_box]
    size=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    stats={'triangles':sum(sum(len(p.vertices)-2 for p in obj.data.polygons) for obj in meshes),
           'mesh_objects':len(meshes),'materials':len(groups),
           'bounds_m':dict(zip('xyz',[round(x,4) for x in size])),
           'note':'Visual approximation; proportions, piping and fittings are approximate.'}
    (here/'model_stats.json').write_text(json.dumps(stats,indent=2)+'\n')
    studio=bpy.data.collections.new('Preview_Studio')
    scene.collection.children.link(studio)
    def move(obj):
        for c in list(obj.users_collection): c.objects.unlink(obj)
        studio.objects.link(obj)
        return obj
    floor_mat=material('Preview_Floor',(.025,.038,.055),.12,.48)
    floor_z=min(p.z for p in points)-.018
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,floor_z))
    floor=move(bpy.context.object)
    floor.name='Studio_Floor'
    floor.data.materials.append(floor_mat)
    target=(0,0,-height/2)
    def aim(obj): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.camera_add(location=camera)
    cam=move(bpy.context.object)
    aim(cam)
    cam.data.type='ORTHO'
    cam.data.ortho_scale=ortho
    scene.camera=cam
    for name,pos,power,size,color in [
        ('Key',(3,-5,3),1300,4,(.82,.90,1)),
        ('Warm_Rim',(-3,2,1),1600,3,(1,.8,.6)),
        ('Fill',(-4,-3,0),850,4,(.72,.85,1)),
        ('Top',(2,3,2),1400,3,(.8,.9,1))]:
        data=bpy.data.lights.new(name,'AREA')
        data.energy=power
        data.shape='DISK'
        data.size=size
        data.color=color
        obj=bpy.data.objects.new(name,data)
        studio.objects.link(obj)
        obj.location=pos
        aim(obj)
    scene.world.color=(.14,.14,.14)
    scene.render.engine='CYCLES'
    scene.cycles.samples=128
    scene.cycles.use_denoising=False
    scene.cycles.sample_clamp_indirect=2
    scene.render.resolution_x=1000
    scene.render.resolution_y=1200
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Filmic'
    scene.view_settings.look='Medium High Contrast'
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(here/(key+'_Engine_preview.png'))
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    bpy.context.view_layer.objects.active=root
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=ortho*1.3
                area.spaces.active.region_3d.view_location=target
    bpy.ops.wm.save_as_mainfile(filepath=str(here/(key+'_Engine.blend')))
    bpy.ops.render.render(write_still=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(asset))
    imported=[o for o in scene.objects if o.type=='MESH']
    assert len(imported)==stats['mesh_objects']
    points=[o.matrix_world@Vector(c) for o in imported for c in o.bound_box]
    actual=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    for i,axis in enumerate('xyz'):
        assert abs(actual[i]-stats['bounds_m'][axis])<.002,(axis,actual)
    assert all(math.isfinite(v) for p in points for v in p)
    assert all(o.data.materials for o in imported)
    assert bpy.data.objects.get('MountPoint') is not None
    assert bpy.data.objects.get('ExhaustPoint') is not None
    inner_obj=next(o for o in imported if 'InnerBell' in o.name)
    assert sum(p.normal.x*p.center.x+p.normal.y*p.center.y for p in inner_obj.data.polygons)<0
    report={'fbx_roundtrip':'passed','mesh_objects':len(imported),'bounds_m':actual,
            'markers':'mount and exhaust verified','normals':'inner bell inward',
            'unity_editor_import':'not tested'}
    (here/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('ASSET_COMPLETE',key,json.dumps(stats),json.dumps(report))
