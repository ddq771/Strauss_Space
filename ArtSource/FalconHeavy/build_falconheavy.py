"""Falcon Heavy, built to its real size: 70 m tall, 12.2 m wide - low
detail (the Falcon 9 model's shapes, simplified).

  * Three first-stage cores, 3.66 m, 4.1 m apart: the centre core carries
    the interstage, second stage and fairing; the side boosters end in
    white nose cones. Nine Merlin 1D nozzles on each, in an octaweb
    (centre + ring of 8, matching the preset's engine layout).
  * Falcon 9 second stage and 5.2 m fairing on the centre core.

Stages, in separation order: Booster_Left, Booster_Right, CentreCore,
Fairing_A, Fairing_B, SecondStage, Payload. Visual approximation, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=70.0
R=1.83
FAIRING_R=2.6
SIDE=4.1                       # side cores' axis, metres from the centre core's
TANK_TOP=35.9
INTERSTAGE_TOP=42.6

white=material('FH_White',(.93,.93,.94),.1,.4)
black=material('FH_Interstage',(.035,.035,.04),.2,.5)
soot=material('FH_Octaweb',(.17,.16,.15),.5,.6)
merlin=material('FH_Merlin',(.24,.22,.21),.75,.35)
mvac=material('FH_MVac',(.10,.09,.09),.8,.3)
gold=material('FH_PayloadFoil',(.85,.64,.24),.9,.3)

stage('Booster_Left','Side booster: 9 Merlin 1D; separates at ~2.5 min')
stage('Booster_Right','Side booster: 9 Merlin 1D; separates at ~2.5 min')
stage('CentreCore','Centre core: 9 Merlin 1D; MECO ~3 min')
stage('Fairing_A','Fairing half')
stage('Fairing_B','Fairing half')
stage('SecondStage','Upper stage: 1 Merlin Vacuum')
stage('Payload','Generic satellite')

def core(x, label):
    """One first-stage core at x: nine nozzles (centre + ring of 8 at
    1.22 m), octaweb skirt, white tank up to the interstage line. Each
    piece is built round the axis then moved x metres sideways."""
    parts=[]
    for i in range(9):
        a=math.radians((i-1)*45)
        nx,ny=(0,0) if i==0 else (1.22*math.cos(a),1.22*math.sin(a))
        parts.append(nozzle(label+'_Merlin',nx,ny,1.35,1.25,.2,.46,merlin))
    parts.append(cap(label+'_OctawebBase',(1.72,1.35),soot,at_top=False))
    parts.append(lathe(label+'_Octaweb',[(1.72,1.35),(R,2.7)],soot,48))
    parts.append(lathe(label+'_Tank',[(R,2.7),(R,TANK_TOP)],white,48))
    return parts

from mathutils import Matrix
for name,x in (('Booster_Left',-SIDE),('Booster_Right',SIDE)):
    stage(name)
    parts=core(0,name)
    # Side booster tops: a short black band, then the white nose cone.
    parts.append(lathe(name+'_Band',[(R,TANK_TOP),(R,TANK_TOP+3.0)],black,48))
    parts.append(lathe(name+'_Nose',[(R,TANK_TOP+3.0),(1.5,TANK_TOP+5.5),(.9,TANK_TOP+7.5),(.2,TANK_TOP+8.8),(0,TANK_TOP+9.0)],white,48))
    place(parts,Matrix.Translation((x,0,0)))
    # Struts tying it to the centre core (they go with the booster).
    box(name+'_Strut',(x/2,0,TANK_TOP-1),(abs(x)-2*R+.2,.35,.35),black)
    box(name+'_StrutLow',(x/2,0,4.0),(abs(x)-2*R+.2,.35,.35),black)

stage('CentreCore')
core(0,'Centre')
lathe('Centre_Interstage',[(R,TANK_TOP),(R,INTERSTAGE_TOP)],black,48)

stage('SecondStage')
S2_TOP=56.0
nozzle('MVac',0,0,INTERSTAGE_TOP,5.0,.35,1.55,mvac,48)
cap('S2_Base',(R,INTERSTAGE_TOP),black,at_top=False)
lathe('S2_Tank',[(R,INTERSTAGE_TOP),(R,S2_TOP)],white,48)

stage('Payload')
lathe('Payload',[(1.4,S2_TOP+.5),(1.6,S2_TOP+1.2),(1.6,S2_TOP+5.5),(.8,S2_TOP+6.5)],gold,32)

fairing=[(R,S2_TOP),(FAIRING_R,S2_TOP+1.0),(FAIRING_R,S2_TOP+6.5),(2.3,S2_TOP+9.0),(1.6,S2_TOP+11.5),(.6,HEIGHT-.3),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,32)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,32)

complete(HERE,'FalconHeavy',HEIGHT,76,camera=(48,-80,0),label='Falcon Heavy')
