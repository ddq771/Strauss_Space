"""Build a visual RD-180-inspired game asset; Blender 3.4+, no external assets.
Run: blender --background --python ArtSource/RD180/build_rd180.py
Dimensions and pipe routing are artistic approximations, not engineering data.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / 'Assets' / 'Models' / 'RD180'
OUT.mkdir(parents=True, exist_ok=True)
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

steel = material('RD180_BrushedSteel', (.38,.43,.46), .82, .32)
dark = material('RD180_NozzleAlloy', (.105,.13,.15), .75, .4)
inside = material('RD180_InnerBell', (.045,.052,.06), .55, .57)
silver = material('RD180_PipeSteel', (.64,.69,.71), .85, .25)
bronze = material('RD180_HeatTint', (.28,.18,.10), .72, .4)
black = material('RD180_Gaskets', (.022,.027,.031), .15, .62)

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
    curve.bevel_resolution=1
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

white=material('RD180_IvoryCoating',(.72,.73,.68),.25,.36)
green=material('RD180_OliveEquipment',(.105,.16,.045),.45,.32)

def bar(name,a,b,width,depth,mat):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cube_add(size=1,location=(a+b)/2)
    obj=bpy.context.object
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    obj.scale=(width,depth,(b-a).length)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bevel=obj.modifiers.new('Edge highlights','BEVEL')
    bevel.width=.012
    bevel.segments=2
    return finish(obj,name,mat)

# Two distinct hollow bells, with pale outer jackets and dark inner surfaces.
profile=[(.72,0),(.715,.06),(.685,.23),(.645,.42),(.593,.62),
         (.53,.83),(.46,1.02),(.38,1.21),(.295,1.40),(.22,1.57),
         (.19,1.68),(.22,1.79),(.285,1.93),(.30,2.05)]
for side,cx in [('L',-.82),('R',.82)]:
    start=len(parts)
    lathe('Bell_Outer_'+side,profile,white,128)
    lathe('Bell_Inner_'+side,[(r-.02,z) for r,z in reversed(profile)],inside,128)
    ring('Exit_Lip_'+side,.71,.012,.023,silver,n=128)
    for r,z in [(.64,.45),(.62,.51),(.295,1.41),(.30,2.02)]:
        ring('Jacket_Seam_'+side,r,z,.012,white)
    cylinder('Chamber_Head_'+side,(0,0,2.04),(0,0,2.18),.32,white,48)
    ring('Chamber_Flange_'+side,.323,2.07,.025,silver)
    bolts(.316,2.18,20)
    cylinder('Gimbal_'+side,(0,0,2.18),(0,0,2.38),.14,steel)
    ring('Gimbal_Ring_'+side,.145,2.30,.044,silver)
    # Fine external service lines following each nozzle jacket.
    for angle in [-130,-45,40,135]:
        a=math.radians(angle)
        tube('Bell_Service_Line_'+side,
             [((r+.033)*math.cos(a),(r+.033)*math.sin(a),z)
              for r,z in [(.31,2.12),(.31,1.93),(.24,1.66),(.4,1.21),(.61,.61)]],
             .012,silver)
    for obj in parts[start:]: obj.location.x+=cx

# Shared upper equipment block, with a central pump and cross-fed branches.
cylinder('Shared_Turbopump_Core',(0,.31,2.37),(0,.31,3.15),.28,white,64)
for z,r in [(2.39,.31),(2.53,.33),(2.75,.32),(3.05,.29),(3.15,.26)]:
    ring('Shared_Pump_Flange',r,z,.024,silver,(0,.31))
    bolts(r,z+.022,16,(0,.31))
cylinder('Pump_Upper_Cap',(0,.31,3.15),(0,.31,3.30),.23,white,48)
tube('Central_Supply_Elbow',[(0,.31,3.26),(0,.63,3.42),(.40,.69,3.40),(.54,.53,3.24)],.13,white)
cylinder('Upper_Inlet_Flange',(.54,.53,3.20),(.54,.53,3.29),.19,silver)

for sign in [-1,1]:
    cx=sign*.82
    tube('Shared_Pump_Chamber_Branch',[(sign*.18,.30,2.65),(sign*.54,.35,2.74),
         (cx,.17,2.63),(cx,0,2.14)],.12,white)
    tube('Upper_Crossover',[(0,.28,3.02),(sign*.52,.06,3.03),
         (sign*1.0,.08,2.72),(sign*.94,.05,2.24)],.092,white)
    tube('Lower_Return_Branch',[(cx,-.05,2.06),(cx,-.37,2.20),
         (sign*.43,-.46,2.40),(sign*.18,.02,2.49)],.077,white)
    # Visible olive control/actuator housings and polished piston rods.
    cylinder('Olive_Valve_Body',(sign*.47,-.47,2.51),(sign*.47,-.47,3.04),.115,green)
    cylinder('Olive_Upper_Sleeve',(sign*.47,-.47,3.02),(sign*.47,-.47,3.18),.084,green)
    ring('Valve_Seal',.116,2.69,.018,silver,(sign*.47,-.47))
    cylinder('Polished_Rod',(sign*.47,-.47,2.35),(sign*.47,-.47,2.52),.041,silver)
    tube('Valve_Curved_Return',[(sign*.47,-.47,2.52),(sign*.66,-.49,2.42),
         (sign*.7,-.41,2.18),(cx,-.14,2.07)],.038,green)
    cylinder('Side_Actuator',(sign*1.12,.05,2.23),(sign*.90,.04,2.88),.057,silver)
    cylinder('Side_Actuator_Sleeve',(sign*1.05,.05,2.44),(sign*.90,.04,2.89),.078,white)
    cylinder('Side_Accumulator',(sign*.86,.45,2.75),(sign*.86,.45,3.15),.105,green)
    ring('Accumulator_Collar',.109,2.80,.017,silver,(sign*.86,.45))
    # Dark support structure tying each chamber into the common mount.
    for y in [-.28,.32]:
        bar('Thrust_Frame',(cx,y,2.33),(sign*.39,y,3.49),.065,.065,dark)
    bar('Upper_Frame_Crossbar',(sign*.39,-.34,3.48),(sign*.39,.43,3.48),.095,.085,steel)
    cylinder('Mount_Boss',(sign*.39,0,3.46),(sign*.39,0,3.54),.16,silver)
    bolts(.12,3.55,8,(sign*.39,0))
    for i in range(5):
        y=-.58-i*.019
        tube('Instrumentation_Harness',[(sign*.46,y,3.12),(sign*(.67+i*.03),y,2.92),
             (sign*(.64+i*.03),y,2.52),(sign*.78,-.32,2.22)],.009,bronze if i%2 else silver)

bar('Common_Mount_Bridge',(-.42,0,3.48),(.42,0,3.48),.12,.12,dark)
tube('Rear_Large_Loop',[(-.84,.35,2.65),(-.88,.62,3.18),(-.53,.72,3.34),
     (-.26,.65,3.13),(0,.31,2.98)],.11,white)
tube('Forward_Distribution',[(-.78,-.12,2.63),(-.42,-.31,2.91),(0,-.32,2.98),
     (.42,-.31,2.91),(.78,-.12,2.63)],.075,white)

# Bake and consolidate by material. Each bell remains visually distinct.
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.object.convert(target='MESH')
groups={}
for obj in list(scene.objects):
    if obj.type=='MESH': groups.setdefault(obj.data.materials[0].name,[]).append(obj)
parts=[]
for name,objs in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objs: obj.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    if len(objs)>1: bpy.ops.object.join()
    obj=bpy.context.object
    obj.name=name.replace('RD180_','Geometry_')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    if name=='RD180_InnerBell':
        # Calculate radial direction relative to the nearest bell axis.
        score=0
        for p in obj.data.polygons:
            world=obj.matrix_world@p.center
            cx=-.82 if world.x<0 else .82
            score+=p.normal.x*(world.x-cx)+p.normal.y*world.y
        if score>0:
            for p in obj.data.polygons: p.flip()
    obj.location.z-=3.568
    parts.append(obj)

root=bpy.data.objects.new('RD180_Engine',None)
scene.collection.objects.link(root)
root['description']='RD-180 visual approximation; twin chambers and shared pump'
root['units']='metres'
for obj in parts: obj.parent=root
markers=[]
for name,pos in [('MountPoint',(0,0,0)),('ExhaustPoint_L',(-.82,0,-3.568)),
                 ('ExhaustPoint_R',(.82,0,-3.568))]:
    obj=bpy.data.objects.new(name,None)
    scene.collection.objects.link(obj)
    obj.parent=root
    obj.location=pos
    if name.startswith('Exhaust'): obj.rotation_euler=(math.pi,0,0)
    markers.append(obj)

bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for obj in parts+[root]+markers: obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'RD180_Engine.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True,add_leaf_bones=False,bake_anim=False,
    mesh_smooth_type='FACE',path_mode='AUTO')
triangles=sum(sum(len(p.vertices)-2 for p in obj.data.polygons) for obj in parts)
points=[obj.matrix_world@Vector(c) for obj in parts for c in obj.bound_box]
stats={'triangles':triangles,'mesh_objects':len(parts),'materials':len(groups),
       'bounds_m':{a:round(max(p[i] for p in points)-min(p[i] for p in points),4)
                   for i,a in enumerate('xyz')},
       'note':'Visual approximation; pipework and dimensions are not measured.'}
(HERE/'model_stats.json').write_text(json.dumps(stats,indent=2)+'\n')

studio=bpy.data.collections.new('Preview_Studio')
scene.collection.children.link(studio)
def studio_move(obj):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    studio.objects.link(obj)
    return obj
floor_mat=material('Preview_Floor',(.025,.038,.055),.15,.45)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-3.615))
floor=studio_move(bpy.context.object)
floor.name='Studio_Floor'
floor.data.materials.append(floor_mat)
def aim(obj,target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(5,-12,2.3))
cam=studio_move(bpy.context.object)
aim(cam,(0,0,-1.8))
cam.data.type='ORTHO'
cam.data.ortho_scale=5.3
scene.camera=cam
def light(name,pos,power,size,color):
    data=bpy.data.lights.new(name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    data.color=color
    obj=bpy.data.objects.new(name,data)
    studio.objects.link(obj)
    obj.location=pos
    aim(obj,(0,0,-1.8))
light('Key',(3,-5,3),1100,4,(.82,.9,1))
light('Warm_Rim',(-3,2,1),1400,3,(1,.8,.6))
light('Fill',(-4,-3,0),700,4,(.72,.85,1))
light('Top',(2,3,2),1200,3,(.8,.9,1))
scene.world.color=(.14,.14,.14)
scene.render.engine='CYCLES'
scene.cycles.samples=128
scene.cycles.use_denoising=False
scene.cycles.sample_clamp_indirect=2
scene.render.resolution_x=1300
scene.render.resolution_y=1200
scene.render.resolution_percentage=100
scene.view_settings.view_transform='Filmic'
scene.view_settings.look='Medium High Contrast'
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(HERE/'RD180_Engine_preview.png')
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=6
            area.spaces.active.region_3d.view_location=(0,0,-1.8)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'RD180_Engine.blend'))
bpy.ops.render.render(write_still=True)
print('RD180_BUILD_COMPLETE',json.dumps(stats))
