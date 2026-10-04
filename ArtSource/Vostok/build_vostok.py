"""Vostok-K (8K72K) launch vehicle, built to its real proportions (38.4 m):

  * Block A core: 28 m, narrowest (2.05 m) at the engine end, widening to
    2.95 m above the booster tips, then a short taper to the top.
  * Blocks B/V/G/D boosters: 19.6 m cones, 2.68 m at the base, leaning in
    so their pointed noses rest against the core; each with an air-rudder
    fin, four RD-107 chambers and two steering verniers.
  * Core RD-108: four main chambers and four verniers.
  * Block E upper stage (2.56 m) on an open truss interstage.
  * Vostok payload shroud with the capsule's window cut-outs.

Stages, in separation order: the four boosters (each its own stage so they
can fall away individually), the shroud, Block A, Block E, and the Vostok
spacecraft (descent sphere + instrument module) inside the shroud.

Painted like the preserved flight-pattern vehicles: light grey stages,
darker engine bays, white-grey shroud. Visual approximation from published
dimensions and photos, not engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=38.4

grey=material('Vostok_Grey',(.60,.62,.58),.25,.5)
shroud=material('Vostok_Shroud',(.80,.81,.78),.15,.45)
dark=material('Vostok_EngineBay',(.13,.13,.12),.45,.55)
steel=material('Vostok_Steel',(.42,.40,.37),.85,.3)
window=material('Vostok_Window',(.05,.06,.07),.2,.25)
ablator=material('Vostok_Ablator',(.30,.27,.22),.1,.7)

# Declared up front so the stage list is in separation order.
for block in ['B','V','G','D']:
    stage('Booster_'+block,'Strap-on Block '+block+', separates at ~2 min')
stage('Shroud','Payload fairing, jettisoned during the Block A burn')
stage('Core','Block A sustainer, burns from liftoff; drops after Block E ignites')
stage('BlockE','Upper stage, RD-0109; fires through the truss')
stage('Spacecraft','Vostok 3KA: 2.3 m descent sphere on its instrument module')

import rocket_asset

def nozzle(name,x,y,top,length,r_top,r_bottom,mat=steel):
    return rocket_asset.nozzle(name,x,y,top,length,r_top,r_bottom,mat)

# --- Block A core -----------------------------------------------------------
stage('Core')
CORE_BASE=2.0
lathe('Core_EngineBay',[(1.03,CORE_BASE),(1.03,3.2)],dark)
cap('Core_Base',(1.03,CORE_BASE),dark,at_top=False)
lathe('Core_Tank',[(1.03,3.2),(1.10,8),(1.20,13),(1.30,18.6),(1.47,20.2),(1.475,26.2),(1.36,27.3)],grey)
ring('Core_Seam',1.475,23.2,.03,dark)
ring('Core_Seam',1.30,13.5,.03,dark)
# RD-108: four main chambers in a square, four verniers outside them.
for i in range(4):
    a=math.radians(45+i*90)
    nozzle('Core_Main',.42*math.cos(a),.42*math.sin(a),CORE_BASE,1.5,.14,.30)
    a=math.radians(i*90)
    nozzle('Core_Vernier',.88*math.cos(a),.88*math.sin(a),CORE_BASE+.2,.8,.06,.14)

# --- Strap-on boosters ------------------------------------------------------
B_LEN=19.6
B_BASE_Z=1.6
B_R=1.34
# Base centre sits clear of the core's engine bay; the nose leans in to touch
# the core where it starts to widen.
base_r=1.03+B_R+.08
tip_r=1.32
tilt=math.atan2(base_r-tip_r,B_LEN)
for i,block in enumerate(['B','V','G','D']):
    stage('Booster_'+block)
    a=math.radians(45+i*90)
    parts=[
        lathe('Booster_Skirt',[(B_R,0),(B_R,1.4)],dark),
        cap('Booster_Base',(B_R,0),dark,at_top=False),
        lathe('Booster_Body',[(B_R,1.4),(1.30,3.5),(1.0,8),(.72,12),(.48,15.5),(.28,17.8),(.12,19.0),(0,B_LEN)],grey),
        # Air rudder fin on the outside of the skirt.
        prism('Booster_Fin',[(B_R-.05,.2),(B_R+.95,.35),(B_R+.95,1.25),(B_R-.05,2.9)],.08,grey),
    ]
    parts.append(ring('Booster_Seam',1.17,5.2,.025,dark))
    # RD-107: four main chambers, two verniers on the sides.
    for j in range(4):
        b=math.radians(45+j*90)
        parts.append(nozzle('Booster_Main',.42*math.cos(b),.42*math.sin(b),0,1.45,.14,.30))
    for side in (-1,1):
        parts.append(nozzle('Booster_Vernier',-.25,side*1.0,.1,.7,.06,.13))
    place(parts,Matrix.Translation((base_r*math.cos(a),base_r*math.sin(a),B_BASE_Z))@
          Matrix.Rotation(a,4,'Z')@Matrix.Rotation(-tilt,4,'Y'))

# --- Block E on its truss -----------------------------------------------------
TRUSS_BOTTOM,TRUSS_TOP=27.3,28.4
stage('Core')  # the truss stays with Block A when Block E fires through it
for i in range(12):
    a0=math.radians(i*30); a1=math.radians(i*30+30)
    lo=(1.30*math.cos(a0),1.30*math.sin(a0),TRUSS_BOTTOM)
    hi=(1.24*math.cos(a1),1.24*math.sin(a1),TRUSS_TOP)
    cylinder('Truss',lo,hi,.035,steel,8)
    hi2=(1.24*math.cos(a0-math.radians(30)),1.24*math.sin(a0-math.radians(30)),TRUSS_TOP)
    cylinder('Truss',lo,hi2,.035,steel,8)
ring('Truss_Ring',1.30,TRUSS_BOTTOM,.05,steel)
ring('Truss_Ring',1.24,TRUSS_TOP,.05,steel)
stage('BlockE')
nozzle('BlockE_Engine',0,0,TRUSS_TOP,1.0,.12,.32)
lathe('BlockE',[(1.28,TRUSS_TOP),(1.28,31.5)],grey)
cap('BlockE_Base',(1.28,TRUSS_TOP),dark,at_top=False)
ring('BlockE_Seam',1.28,30.0,.03,dark)

# --- Vostok spacecraft (inside the shroud) -------------------------------------
stage('Spacecraft')
lathe('InstrumentModule',[(.4,31.55),(1.2,32.3),(1.2,32.7),(.55,33.3)],steel)
bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=24,radius=1.15,location=(0,0,34.35))
finish(bpy.context.object,'DescentSphere',ablator)

# --- Vostok shroud ----------------------------------------------------------
stage('Shroud')
lathe('Shroud',[(1.28,31.5),(1.28,32.9),(1.18,34.0),(1.0,35.3),(.75,36.5),(.48,37.4),(.22,38.05),(0,HEIGHT)],shroud)
ring('Shroud_Seam',1.28,32.9,.03,dark)
# Cut-outs over the capsule's hatch and portholes.
for i,(z,h) in enumerate([(34.1,.9),(35.4,.6),(34.6,.5)]):
    a=math.radians(20+i*120)
    r=1.18-(z-34.0)*.14
    box('Shroud_Window',(r*math.cos(a),r*math.sin(a),z),(.09,.55,h),window,rotation=(0,0,a))

complete(HERE,'Vostok',HEIGHT,44,camera=(28,-44,0),label='Vostok-K')
