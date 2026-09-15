"""Build a visual F-1-inspired game asset; Blender 3.4+, no external assets.
Run: blender --background --python ArtSource/F1/build_f1.py
Dimensions and pipe routing are artistic approximations, not engineering data.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / 'Assets' / 'Models' / 'F1'
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

steel = material('F1_BrushedSteel', (.38,.43,.46), .82, .32)
dark = material('F1_NozzleAlloy', (.105,.13,.15), .75, .4)
inside = material('F1_InnerBell', (.045,.052,.06), .55, .57)
silver = material('F1_PipeSteel', (.64,.69,.71), .85, .25)
bronze = material('F1_HeatTint', (.28,.18,.10), .72, .4)
black = material('F1_Gaskets', (.022,.027,.031), .15, .62)

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

# Bell shell is hollow, with a distinct throat and a rolled exit lip.
profile=[(1.88,0),(1.855,.12),(1.78,.40),(1.67,.75),(1.54,1.10),
         (1.39,1.45),(1.22,1.80),(1.04,2.13),(.85,2.44),(.67,2.73),
         (.51,3.00),(.425,3.22),(.435,3.40),(.50,3.57),(.57,3.76),
         (.595,3.98),(.595,4.14)]
bell=lathe('Bell_Outer',profile,dark)
inner=lathe('Bell_Inner',[(r-.037,z) for r,z in reversed(profile)],inside)
ring('Exit_RolledLip',1.865,.018,.034,silver,n=128)
ring('Nozzle_Extension_Joint',1.235,1.80,.065,steel)
for z,r in [(0.14,1.85),(.48,1.756),(.84,1.636),(1.19,1.50),(1.53,1.35)]:
    ring('Extension_ReinforcingBand',r,z,.019,steel)

# Longitudinal cooling jacket ribs; grouped into one mesh at export.
for i in range(144):
    a=2*math.pi*i/144
    coords=[((r+.012)*math.cos(a),(r+.012)*math.sin(a),z) for r,z in profile]
    tube('Cooling_Jacket_Rib',coords,.012,steel,2)

ring('Upper_Cooling_Manifold',.61,4.02,.09,silver)
cylinder('Injector_Housing',(0,0,4.12),(0,0,4.37),.65,steel,64)
ring('Injector_LowerFlange',.65,4.14,.045,silver)
ring('Injector_UpperFlange',.65,4.35,.045,silver)
bolts(.65,4.39,32)
cylinder('Thrust_Dome',(0,0,4.35),(0,0,4.62),.46,dark,48)
cylinder('Gimbal_Stem',(0,0,4.60),(0,0,5.12),.20,silver)
ring('Gimbal_Collar',.24,4.96,.075,steel)
cylinder('Mounting_Boss',(0,0,5.12),(0,0,5.45),.34,steel,48)
cylinder('Mounting_Plate',(0,0,5.44),(0,0,5.60),.47,silver,48)
bolts(.395,5.60,12)

# Asymmetrical pump group and exposed pipework establish the F-1 silhouette.
cylinder('Turbopump_Axle',(.73,-.14,4.60),(.73,.78,4.60),.29,dark,48)
for y,r in [(-.12,.46),(.25,.34),(.72,.40)]:
    cylinder('Turbopump_Casing',(.73,y-.10,4.60),(.73,y+.10,4.60),r,steel,48)
    ring('Pump_Flange',r,4.60,.04,silver,(.73,y),rotation=(math.pi/2,0,0))
for i in range(12):
    a=2*math.pi*i/12
    x,z=.73+.41*math.cos(a),4.60+.41*math.sin(a)
    cylinder('Pump_Bolt',(x,-.245,z),(x,-.20,z),.029,silver,6)

tube('Large_Feed_Duct',[(.72,.73,4.63),(1.20,.85,4.86),(1.34,.62,5.17),(1.32,.24,5.40)],.18,silver)
cylinder('Feed_Inlet_Flange',(1.32,.24,5.38),(1.32,.24,5.49),.25,steel)
bolts(.21,5.5,10,(1.32,.24))
tube('Pump_To_Chamber',[(.77,-.18,4.60),(1.11,-.53,4.24),(.96,-.72,3.97),(.51,-.29,4.01)],.125,silver)
tube('Main_Return_Line',[(-.43,.20,4.24),(-.89,.28,4.72),(-1.09,.17,5.10),(-1.09,-.23,5.39)],.145,steel)
cylinder('Second_Inlet_Flange',(-1.09,-.23,5.37),(-1.09,-.23,5.48),.215,silver)
cylinder('Gas_Generator',(.89,.45,3.51),(.89,.45,4.10),.18,bronze)
ring('Generator_Collar',.19,3.97,.037,silver,(.89,.45))
tube('Turbine_Exhaust_Downcomer',[(.94,.58,4.36),(1.25,.61,3.94),(1.25,.55,3.13),(1.39,.35,2.35),(1.25,.0,1.83)],.135,dark)
ring('Exhaust_Collector',1.26,1.85,.102,bronze)
for angle in [20,110,200,290]:
    a=math.radians(angle)
    xy=lambda r,z:(r*math.cos(a),r*math.sin(a),z)
    tube('Chamber_Supply_Branch',[xy(.58,4.14),xy(.77,3.87),xy(.69,3.49),xy(.58,3.11)],.054,silver)
    cylinder('Actuator_Brace',xy(.36,5.20),xy(.78,4.03),.045,steel)
    cylinder('Actuator_Sleeve',xy(.43,4.99),xy(.64,4.41),.081,silver)
for i in range(9):
    a=2*math.pi*i/9+.16
    coords=[((r+.08)*math.cos(a), (r+.08)*math.sin(a),z) for r,z in
            [(.67,4.30),(.78,4.03),(.63,3.66),(.54,3.28),(.67,2.88),(1.02,2.19),(1.25,1.90)]]
    tube('Instrumentation_Line',coords,.017,bronze if i%3==0 else silver)

# Bake modifiers and curves. Merge small parts by material to keep draw calls low.
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.object.convert(target='MESH')
meshes=[o for o in scene.objects if o.type=='MESH']
groups={}
for o in meshes: groups.setdefault(o.data.materials[0].name,[]).append(o)
parts=[]
for name,objs in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objs: obj.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.object.join()
    obj=bpy.context.object
    obj.name=name.replace('F1_','Geometry_')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # Normalize normals, including the closed rims and cylindrical caps.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Inner shell's normals must face the open cavity.
    if name=='F1_InnerBell':
        radial=sum(p.normal.x*p.center.x+p.normal.y*p.center.y for p in obj.data.polygons)
        if radial>0:
            for poly in obj.data.polygons: poly.flip()
    obj.location.z-=5.62
    parts.append(obj)

root=bpy.data.objects.new('F1_Engine',None)
scene.collection.objects.link(root)
root['description']='F-1 visual approximation for Strauss Space; not a measured replica'
root['units']='metres'
root['mount_axis']='Blender +Z / Unity +Y'
for obj in parts: obj.parent=root
mount=bpy.data.objects.new('MountPoint',None)
scene.collection.objects.link(mount)
mount.parent=root
exhaust=bpy.data.objects.new('ExhaustPoint',None)
scene.collection.objects.link(exhaust)
exhaust.parent=root
exhaust.location=(0,0,-5.62)
exhaust.rotation_euler=(math.pi,0,0)

bpy.ops.object.select_all(action='DESELECT')
for obj in parts+[root,mount,exhaust]: obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/'F1_Engine.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True,add_leaf_bones=False,bake_anim=False,
    mesh_smooth_type='FACE',path_mode='AUTO')

triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in parts)
corners=[o.matrix_world@Vector(c) for o in parts for c in o.bound_box]
stats={'triangles':triangles,'mesh_objects':len(parts),'materials':len(groups),
       'bounds_m':{a:round(max(v[i] for v in corners)-min(v[i] for v in corners),4)
                   for i,a in enumerate('xyz')},
       'note':'Approximate visual model; dimensions and details are not measured.'}
(HERE/'model_stats.json').write_text(json.dumps(stats,indent=2)+'\n')

# Product-style preview, excluded from the FBX export.
studio=bpy.data.collections.new('Preview_Studio')
scene.collection.children.link(studio)
def studio_move(obj):
    for collection in list(obj.users_collection): collection.objects.unlink(obj)
    studio.objects.link(obj)
    return obj
floor_mat=material('Preview_Floor',(.025,.038,.055),.15,.45)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-5.67))
floor=studio_move(bpy.context.object)
floor.name='Studio_Floor'
floor.data.materials.append(floor_mat)
def aim(obj,target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(9,-13,3.0))
cam=studio_move(bpy.context.object)
cam.name='Preview_Camera'
aim(cam,(0,0,-2.78))
cam.data.type='ORTHO'
cam.data.ortho_scale=7.5
scene.camera=cam
def light(name,position,power,size,color):
    data=bpy.data.lights.new(name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    data.color=color
    obj=bpy.data.objects.new(name,data)
    studio.objects.link(obj)
    obj.location=position
    aim(obj,(0,0,-2.5))
light('Key_Softbox',(3,-6,3),2200,5,(.78,.88,1))
light('Warm_Rim',(-4,2,1),3000,4,(1,.77,.52))
light('Front_Fill',(-4,-4,-1),1300,4,(.7,.84,1))
light('Top_Rim',(2,4,2),2600,3,(.8,.9,1))
scene.world.color=(.18,.18,.18)
scene.render.engine='CYCLES'
scene.cycles.samples=128
scene.cycles.use_denoising=False
scene.cycles.sample_clamp_indirect=2.0
scene.render.resolution_x=1100
scene.render.resolution_y=1300
scene.render.resolution_percentage=100
scene.view_settings.view_transform='Filmic'
scene.view_settings.look='Medium High Contrast'
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(HERE/'F1_Engine_preview.png')
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=9
            area.spaces.active.region_3d.view_location=(0,0,-2.8)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'F1_Engine.blend'))
bpy.ops.render.render(write_still=True)
print('F1_BUILD_COMPLETE',json.dumps(stats))
