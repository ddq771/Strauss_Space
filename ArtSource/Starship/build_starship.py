"""Starship (Flight 5 configuration), built to its real size: 121.3 m tall,
9 m diameter, all stainless steel.

  * Super Heavy (71 m including the hot-staging ring): 33 Raptors (3 centre,
    10 inner, 20 outer in the skirt), two chines, four grid fins, vented
    hot-staging ring on top.
  * Ship (50.3 m): 3 sea-level Raptors + 3 Raptor Vacuums, black heat-shield
    tiles over the windward half, two forward and two aft flaps.

Stages, in separation order: SuperHeavy, Ship. Visual approximation from
published dimensions and photos, not engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=121.3
R=4.5

steel=material('Starship_Steel',(.70,.71,.73),.92,.32)
scorched=material('Starship_Scorched',(.30,.29,.28),.85,.45)
tiles=material('Starship_Tiles',(.035,.035,.04),.1,.75)
raptor=material('Starship_Raptor',(.22,.21,.20),.8,.35)
vent=material('Starship_Vent',(.05,.05,.05),.5,.5)

stage('SuperHeavy','Booster: 33 Raptors; hot-stages at ~2.7 min and returns to the tower')
stage('Ship','Upper stage: 3 sea-level + 3 vacuum Raptors')

# --- Super Heavy ------------------------------------------------------------
stage('SuperHeavy')
SKIRT_BOTTOM=0.9
BOOSTER_TOP=69.2
RING_TOP=71.0
rings=[(3,.85,0),(10,2.15,18),(20,3.75,9)]
for count,radius,offset in rings:
    for i in range(count):
        a=math.radians(offset+i*360/count)
        nozzle('Raptor',radius*math.cos(a),radius*math.sin(a),2.1 if radius>3 else 1.8,1.75,.26,.62,raptor)
cap('Booster_Base',(R*.97,SKIRT_BOTTOM+1.2),scorched,at_top=False)
lathe('Booster_Skirt',[(R,SKIRT_BOTTOM),(R,4.6)],scorched)
lathe('Booster_Tank',[(R,4.6),(R,BOOSTER_TOP)],steel)
for z in (12,24,36,48,60): ring('Booster_Weld',R,z,.025,scorched)
# Chines down two sides.
for side in (-1,1):
    box('Chine',(side*(R+.18),0,34.0),(.36,.7,56.0),steel)
# Grid fins near the top, folded down.
for i in range(4):
    a=math.radians(45+i*90)
    fin=[box('GridFin',(R+.5,0,65.2),(.55,4.2,3.0),scorched),
         box('GridFin_Mount',(R+.15,0,65.2),(.3,1.2,1.0),steel)]
    for k in range(6):
        fin.append(box('GridFin_Bar',(R+.79,0,63.95+k*.5),(.03,4.2,.05),vent))
    place(fin,Matrix.Rotation(a,4,'Z'))
# Hot-staging ring: open-vented section the Ship's engines fire through.
lathe('HotStage_Ring',[(R,BOOSTER_TOP),(R,RING_TOP)],scorched)
for i in range(18):
    a=math.radians(i*20)
    box('HotStage_Vent',((R+.01)*math.cos(a),(R+.01)*math.sin(a),70.1),(.05,.9,1.3),vent,rotation=(0,0,a))

# --- Ship -------------------------------------------------------------------
stage('Ship')
SHIP_BASE=RING_TOP
BODY_TOP=103.0
for i in range(3):
    a=math.radians(60+i*120)
    nozzle('Raptor_SL',.95*math.cos(a),.95*math.sin(a),SHIP_BASE+1.9,1.7,.26,.62,raptor)
    a=math.radians(i*120)
    nozzle('Raptor_Vac',3.0*math.cos(a),3.0*math.sin(a),SHIP_BASE+2.6,2.5,.3,1.15,raptor,32)
cap('Ship_Base',(R*.98,SHIP_BASE+.2),scorched,at_top=False)
nose=[(R,BODY_TOP),(4.42,106),(4.2,109),(3.82,112),(3.24,115),(2.42,117.6),(1.42,119.6),(.62,120.8),(0,HEIGHT)]
lathe('Ship_Body',[(R,SHIP_BASE+.2),(R,BODY_TOP)],steel)
lathe('Ship_Nose',nose,steel)
# Heat-shield tiles over the windward (-x) half, slightly proud of the steel.
shield=[(R+.03,SHIP_BASE+.6),(R+.03,BODY_TOP)]+[(r+.03,z) for r,z in nose[1:-1]]+[(.03,HEIGHT-.05)]
lathe_arc('HeatShield',shield,tiles,math.pi/2,3*math.pi/2,64)
# Flaps hinge on the sides (±y), at the edge of the heat shield.
for side in (-1,1):
    flaps=[prism('AftFlap',[(0,0),(3.1,.8),(3.1,6.4),(0,8.6)],.35,tiles),
           prism('ForwardFlap',[(0,0),(2.2,1.0),(2.0,5.2),(0,6.6)],.3,tiles)]
    flaps[0].location=(R,0,SHIP_BASE+.8)
    # Forward flaps lean in to follow the nose taper (~9°).
    flaps[1].location=(4.27,0,108.4)
    flaps[1].rotation_euler=(0,-.155,0)
    bpy.context.view_layer.update()
    place(flaps,Matrix.Rotation(side*math.pi/2,4,'Z'))

complete(HERE,'Starship',HEIGHT,130,camera=(80,-130,0),label='Starship')
