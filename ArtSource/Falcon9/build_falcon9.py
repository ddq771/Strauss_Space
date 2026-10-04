"""Falcon 9 Block 5 (fairing configuration), built to its real size: 70 m
tall, 3.66 m core, 5.2 m fairing.

  * Stage 1 (41 m with interstage): nine Merlin 1D in the octaweb, four
    folded carbon landing legs, raceway, black interstage with four
    titanium grid fins.
  * Stage 2 (13.4 m) with its Merlin Vacuum nozzle inside the interstage.
  * Payload fairing in two halves, around a generic satellite.

Stages, in separation order: FirstStage, the two fairing halves, SecondStage,
Payload. Visual approximation from published dimensions and photos, not
engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=70.0
R=1.83
FAIRING_R=2.6

white=material('F9_White',(.93,.93,.94),.1,.4)
black=material('F9_Interstage',(.035,.035,.04),.2,.5)
soot=material('F9_Octaweb',(.17,.16,.15),.5,.6)
carbon=material('F9_LegCarbon',(.05,.05,.055),.3,.45)
titanium=material('F9_GridFin',(.36,.36,.38),.85,.35)
merlin=material('F9_Merlin',(.24,.22,.21),.75,.35)
mvac=material('F9_MVac',(.10,.09,.09),.8,.3)
gold=material('F9_PayloadFoil',(.85,.64,.24),.9,.3)

stage('FirstStage','Booster: 9 Merlin 1D; separates at ~2.5 min, lands on its legs')
stage('Fairing_A','Fairing half, jettisoned at ~3.5 min')
stage('Fairing_B','Fairing half, jettisoned at ~3.5 min')
stage('SecondStage','Upper stage: 1 Merlin Vacuum')
stage('Payload','Generic satellite on its adapter')

# --- Stage 1 ----------------------------------------------------------------
stage('FirstStage')
STAGE1_TANK_TOP=35.9
INTERSTAGE_TOP=42.6
for i in range(9):
    if i==0: x=y=0
    else:
        a=math.radians((i-1)*45)
        x,y=1.22*math.cos(a),1.22*math.sin(a)
    nozzle('Merlin',x,y,1.35,1.25,.2,.46,merlin)
cap('Octaweb_Base',(1.72,1.35),soot,at_top=False)
lathe('Octaweb',[(1.72,1.35),(1.83,2.7)],soot)
lathe('Stage1_Tank',[(R,2.7),(R,STAGE1_TANK_TOP)],white)
lathe('Interstage',[(R,STAGE1_TANK_TOP),(R,INTERSTAGE_TOP)],black)
ring('Stage1_Seam',R,STAGE1_TANK_TOP,.02,black)
# Raceway running up the side.
box('Raceway',(0,-R-.06,19.3),(.45,.14,33.0),white)
# Folded landing legs, at 45° between the grid fins.
for i in range(4):
    a=math.radians(45+i*90)
    leg=[prism('Leg',[(R+.02,2.2),(R+.02,11.8),(R+.18,11.8),(R+.30,2.2)],.85,carbon),
         box('Leg_Foot',(R+.22,0,2.05),(.35,.9,.25),carbon)]
    place(leg,Matrix.Rotation(a,4,'Z'))
# Grid fins stowed upright against the top of the interstage.
for i in range(4):
    a=math.radians(i*90)
    fin=[box('GridFin',(R+.14,0,41.2),(.12,1.2,1.5),titanium),
         box('GridFin_Hinge',(R+.06,0,40.3),(.16,.5,.25),titanium)]
    for k in range(5):
        fin.append(box('GridFin_Bar',(R+.2,0,40.6+k*.3),(.02,1.2,.03),black))
    place(fin,Matrix.Rotation(a,4,'Z'))

# --- Stage 2 ----------------------------------------------------------------
stage('SecondStage')
STAGE2_TOP=56.0
# MVac nozzle hangs down inside the interstage (seen after separation).
nozzle('MVac',0,0,INTERSTAGE_TOP,5.0,.35,1.55,mvac,48)
cap('Stage2_Base',(R,INTERSTAGE_TOP),black,at_top=False)
lathe('Stage2_Tank',[(R,INTERSTAGE_TOP),(R,STAGE2_TOP)],white)
box('Stage2_Raceway',(0,-R-.05,49.3),(.3,.1,12.0),white)

# --- Payload ----------------------------------------------------------------
stage('Payload')
lathe('PayloadAdapter',[(1.5,STAGE2_TOP),(.95,57.2)],titanium)
lathe('Satellite',[(1.6,57.2),(1.6,63.6),(1.1,64.4)],gold)
cap('Satellite_Base',(1.6,57.2),gold,at_top=False)
cap('Satellite_Top',(1.1,64.4),gold,at_top=True)

# --- Fairing halves -----------------------------------------------------------
fairing=[(R,STAGE2_TOP),(FAIRING_R,57.3),(FAIRING_R,62.6),(2.56,63.8),(2.42,65.2),(2.14,66.6),
         (1.72,67.8),(1.18,68.9),(.62,69.6),(.22,69.92),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,48)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,48)

complete(HERE,'Falcon9',HEIGHT,76,camera=(48,-80,0),label='Falcon 9 Block 5')
