"""Space Shuttle launch stack, built to its real size: 56.1 m tall.

  * External Tank: 46.9 m x 8.4 m, foam orange, with the intertank band,
    LOX feedline, cable tray and the orbiter/SRB attach struts.
  * Two Solid Rocket Boosters: 45.6 m x 3.71 m, with aft skirts, segment
    joints and nozzles.
  * Orbiter: 37.2 m long, 23.8 m wingspan, mounted belly-to-tank - double-
    delta wings, vertical tail, OMS pods, body flap, three main engines,
    black tiles on the belly and wing undersides, RCC nose cap, cockpit
    windows.

The stack is centred on the tank's axis. Stages, in separation order:
SRB_Left, SRB_Right, ExternalTank, Orbiter. Visual approximation from
published dimensions and photos, not engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=56.1
ET_R=4.2
SRB_R=1.855
SRB_X=ET_R+SRB_R+.3
ORB_Y=-(ET_R+1.0+2.6)   # orbiter centreline; belly faces the tank (+y)

foam=material('Shuttle_ETFoam',(.78,.40,.15),.0,.85)
intertank=material('Shuttle_Intertank',(.62,.31,.12),.1,.8)
srb_white=material('Shuttle_SRBWhite',(.90,.90,.89),.1,.5)
joint=material('Shuttle_SRBJoint',(.45,.45,.45),.4,.5)
orb_white=material('Shuttle_OrbiterWhite',(.93,.93,.92),.05,.55)
tiles=material('Shuttle_BlackTiles',(.04,.04,.045),.0,.85)
rcc=material('Shuttle_RCC',(.30,.30,.31),.1,.6)
glass=material('Shuttle_Windows',(.02,.03,.04),.3,.15)
engine=material('Shuttle_Engine',(.26,.24,.22),.85,.3)
strut=material('Shuttle_Strut',(.35,.35,.36),.7,.4)

stage('SRB_Left','Solid Rocket Booster, separates at ~2 min')
stage('SRB_Right','Solid Rocket Booster, separates at ~2 min')
stage('ExternalTank','External Tank, released after main engine cutoff (~8.5 min)')
stage('Orbiter','Orbiter: 3 RS-25 main engines + 2 OMS engines')

# --- Solid Rocket Boosters ----------------------------------------------------
for name,side in (('SRB_Left',-1),('SRB_Right',1)):
    stage(name)
    parts=[nozzle('SRB_Nozzle',0,0,3.6,3.6,.62,1.9,joint,40),
           cap('SRB_SkirtBase',(2.55,3.2),joint,at_top=False),
           lathe('SRB_AftSkirt',[(2.55,3.2),(2.15,5.0),(SRB_R,6.6)],srb_white),
           lathe('SRB_Body',[(SRB_R,6.6),(SRB_R,40.6),(1.6,41.8),(1.45,43.0),(1.05,44.2),(.5,45.1),(0,45.6)],srb_white)]
    for z in (13.5,20.5,27.5,34.5):
        parts.append(ring('SRB_Joint',SRB_R,z,.04,joint))
    place(parts,Matrix.Translation((side*SRB_X,0,0)))

# --- External Tank ------------------------------------------------------------
stage('ExternalTank')
ET_BASE=9.2
lathe('ET',[(2.2,ET_BASE),(3.4,9.6),(4.0,10.3),(ET_R,11.2),(ET_R,43.5),(4.0,46.0),(3.55,48.6),
            (2.9,51.0),(2.1,53.0),(1.25,54.6),(.55,55.6),(0,HEIGHT)],foam)
cap('ET_Base',(2.2,ET_BASE),foam,at_top=False)
lathe('ET_Intertank',[(ET_R+.03,37.0),(ET_R+.03,43.5)],intertank)
for z in (38.0,39.6,41.2,42.8): ring('ET_IntertankRib',ET_R+.04,z,.04,intertank)
# LOX feedline and cable tray down the orbiter side.
cylinder('ET_LOXFeedline',(1.5,-ET_R-.18,43.0),(1.5,-ET_R-.18,11.5),.22,foam,16)
box('ET_CableTray',(-1.5,-ET_R-.12,27.0),(.35,.25,31.0),foam)
# Orbiter attach struts (aft pair + forward bipod) and SRB attach struts.
for x,z in ((1.3,11.6),(-1.3,11.6),(0,36.2)):
    cylinder('ET_OrbiterStrut',(x,-ET_R,z),(x*.8,-ET_R-1.05,z),.12,strut,12)
for side in (-1,1):
    for z in (12.0,38.0):
        box('ET_SRBStrut',(side*(ET_R+.2),0,z),(.5,.3,.3),strut)

# --- Orbiter -------------------------------------------------------------------
stage('Orbiter')
AFT=8.4
fuselage=loft('Orbiter_Fuselage',[
    (AFT,   0,ORB_Y,     2.9, 2.7, 3.0),
    (10.5,  0,ORB_Y,     2.75,2.65,3.0),
    (14.0,  0,ORB_Y,     2.6, 2.6, 3.0),
    (33.0,  0,ORB_Y,     2.6, 2.6, 3.0),
    (35.5,  0,ORB_Y-.1,  2.6, 2.65,2.8),
    (38.5,  0,ORB_Y+.2,  2.35,2.35,2.6),
    (41.0,  0,ORB_Y+.6,  1.85,1.75,2.4),
    (43.0,  0,ORB_Y+.9,  1.25,1.1, 2.2),
    (44.4,  0,ORB_Y+1.05,.6,  .5,  2.0),
    (45.1,  0,ORB_Y+1.1, .1,  .08, 2.0)],orb_white,64)
paint_faces(fuselage,tiles,lambda c,n: c.y>ORB_Y+1.2 and c.z<44.0)
paint_faces(fuselage,rcc,lambda c,n: c.z>=44.0)
# Cockpit windows on the forward upper (away from the tank) side.
for x,z in ((-.9,39.2),(0,39.5),(.9,39.2)):
    box('Orbiter_Window',(x,ORB_Y-2.28,z),(.7,.08,.55),glass,rotation=(math.radians(-25),0,0))
# Double-delta wings, low on the fuselage near the belly.
WING_Y=ORB_Y+1.65
for side in (-1,1):
    outline=[(2.4,AFT),(2.4,33.0),(4.6,23.0),(11.9,11.6),(11.9,9.6)]
    wing=prism('Orbiter_Wing',[(side*x,z) for x,z in outline],.55,orb_white)
    wing.location=(0,WING_Y,0)
    bpy.context.view_layer.update()
    paint_faces(wing,tiles,lambda c,n: c.y>WING_Y+.1)
# Vertical tail, sticking out of the back (away from the tank).
tail=prism('Orbiter_Tail',[(0,AFT+.2),(0,17.0),(7.9,11.0),(7.9,8.3)],.42,orb_white)
place([tail],Matrix.Translation((0,ORB_Y-2.45,0))@Matrix.Rotation(-math.pi/2,4,'Z'))
# OMS pods on either side of the tail, each with its engine.
for side in (-1,1):
    pod=[lathe('OMS_Pod',[(.95,AFT),(1.0,10.0),(.85,12.5),(.4,14.0),(0,14.3)],orb_white),
         nozzle('OMS_Engine',0,0,AFT,1.0,.15,.4,engine,16)]
    place(pod,Matrix.Translation((side*1.75,ORB_Y-1.95,0)))
# Body flap under the engines.
box('Orbiter_BodyFlap',(0,ORB_Y+2.1,AFT-.55),(4.6,.35,1.3),tiles)
# Three RS-25s: one high, two low.
for x,dy in ((0,-1.35),(-1.35,.6),(1.35,.6)):
    nozzle('RS25',x,ORB_Y+dy,AFT+.2,3.1,.42,1.15,engine,40)

complete(HERE,'SpaceShuttle',HEIGHT,62,camera=(40,-70,0),label='Space Shuttle')
