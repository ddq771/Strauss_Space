"""ULA Vulcan Centaur VC2S, built to its real size: 61.6 m tall, 5.4 m -
low detail.

  * Booster: two BE-4 nozzles, the tan insulated tank, two GEM 63XL solids
    (1.6 m) on the y axis.
  * Interstage, Centaur V (5.4 m, two RL10C-X), 5.4 m fairing.

Stages, in separation order: SRB_Left, SRB_Right, Fairing_A, Fairing_B,
Booster, Centaur, Payload. Visual approximation, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=61.6
R=2.7
B_TOP=36.6
C_BASE=39.0
C_TOP=50.0
SRB_R=.8

tan=material('VC_Insulation',(.78,.5,.28),.05,.75)
white=material('VC_White',(.93,.93,.93),.08,.4)
dark=material('VC_Dark',(.12,.12,.13),.3,.6)
engine=material('VC_Engine',(.24,.22,.21),.8,.35)
gold=material('VC_PayloadFoil',(.85,.64,.24),.9,.3)

stage('SRB_Left','GEM 63XL solid, jettisoned at ~1 min 40 s')
stage('SRB_Right','GEM 63XL solid, jettisoned at ~1 min 40 s')
stage('Fairing_A','Fairing half')
stage('Fairing_B','Fairing half')
stage('Booster','Booster: 2 BE-4')
stage('Centaur','Centaur V: 2 RL10C-X')
stage('Payload','Generic satellite')

for name,y in (('SRB_Left',-(R+SRB_R+.05)),('SRB_Right',R+SRB_R+.05)):
    stage(name)
    parts=[nozzle(name+'_Nozzle',0,0,2.4,1.8,.35,.75,dark,24),
           lathe(name+'_Case',[(SRB_R,2.4),(SRB_R,22.0)],white,32),
           lathe(name+'_Nose',[(SRB_R,22.0),(.55,23.6),(0,24.4)],white,32)]
    place(parts,Matrix.Translation((0,y,0)))

stage('Booster')
for x in (-1.3,1.3):
    nozzle('BE4',x,0,3.6,3.2,.45,1.0,engine,32)
cap('Booster_Base',(R,3.6),dark,at_top=False)
lathe('Booster_Tank',[(R,3.6),(R,B_TOP)],tan,48)
lathe('Interstage',[(R,B_TOP),(R,C_BASE)],white,48)

stage('Centaur')
for x in (-.9,.9):
    nozzle('RL10CX',x,0,C_BASE+.6,2.2,.2,.8,engine,24)
lathe('Centaur_Tank',[(R,C_BASE),(R,C_TOP)],white,48)

stage('Payload')
lathe('Payload',[(1.6,C_TOP+.5),(2.0,C_TOP+1.5),(2.0,C_TOP+6.0),(1.0,C_TOP+7.0)],gold,32)

fairing=[(R,C_TOP-3.0),(R,C_TOP+4.0),(2.3,C_TOP+7.0),(1.4,C_TOP+9.5),(.3,HEIGHT-.2),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,32)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,32)

complete(HERE,'Vulcan',HEIGHT,68,camera=(42,-72,0),label='Vulcan Centaur')
