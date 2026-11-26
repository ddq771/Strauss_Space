"""Long March 8A (CZ-8A), built to its published size: 50.5 m tall -
low detail.

  * Core stage, 3.35 m, two YF-100 nozzles; two 2.25 m boosters with one
    YF-100 each and pointed nose cones, either side on the y axis (the
    preset's engine layout puts the booster engines there).
  * Second stage, 3.35 m, two YF-75H.
  * 4.2 m payload fairing in two halves.

Stages, in separation order: Booster_Left, Booster_Right, Core, Fairing_A,
Fairing_B, SecondStage, Payload. Visual approximation from published
dimensions and photos, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=50.5
R=1.675
B_R=1.125
F_R=2.1
CORE_TOP=27.0
S2_TOP=CORE_TOP+12.375

white=material('CZ8_White',(.94,.94,.94),.08,.45)
orange=material('CZ8_Insulation',(.85,.45,.15),.05,.7)
dark=material('CZ8_Dark',(.12,.12,.13),.3,.6)
engine=material('CZ8_Engine',(.24,.22,.21),.8,.35)
red=material('CZ8_Red',(.75,.1,.1),.1,.5)
gold=material('CZ8_PayloadFoil',(.85,.64,.24),.9,.3)

stage('Booster_Left','2.25 m booster: 1 YF-100')
stage('Booster_Right','2.25 m booster: 1 YF-100')
stage('Core','Core stage: 2 YF-100')
stage('Fairing_A','Fairing half')
stage('Fairing_B','Fairing half')
stage('SecondStage','Second stage: 2 YF-75H')
stage('Payload','Generic satellite')

for name,y in (('Booster_Left',-(R+B_R+.1)),('Booster_Right',R+B_R+.1)):
    stage(name)
    parts=[nozzle(name+'_YF100',0,0,1.4,1.4,.3,.65,engine,24),
           cap(name+'_Base',(B_R,1.4),dark,at_top=False),
           lathe(name+'_Case',[(B_R,1.4),(B_R,22.5)],white,32),
           lathe(name+'_Nose',[(B_R,22.5),(.8,24.8),(.3,26.2),(0,26.5)],white,32)]
    for k in range(4):
        parts.append(box(name+'_Fin',(B_R+.3,0,2.2),(.6,.08,1.6),dark,rotation=(0,0,0)))
        place([parts[-1]],Matrix.Rotation(math.radians(45+90*k),4,'Z'))
    place(parts,Matrix.Translation((0,y,0)))

stage('Core')
for x in (-.75,.75):
    nozzle('Core_YF100',x,0,1.4,1.4,.3,.65,engine,24)
cap('Core_Base',(R,1.4),dark,at_top=False)
lathe('Core_Tank',[(R,1.4),(R,CORE_TOP-3.0)],orange,48)
lathe('Core_Upper',[(R,CORE_TOP-3.0),(R,CORE_TOP)],white,48)
ring('Core_Band',R,14.0,.03,red)

stage('SecondStage')
for x in (-.6,.6):
    nozzle('YF75H',x,0,CORE_TOP+1.2,1.8,.2,.65,engine,24)
lathe('S2_Tank',[(R,CORE_TOP),(R,S2_TOP)],white,48)

stage('Payload')
lathe('Payload',[(1.2,S2_TOP+.4),(1.4,S2_TOP+1.0),(1.4,S2_TOP+4.5),(.6,S2_TOP+5.5)],gold,32)

fairing=[(R,S2_TOP-2.0),(F_R,S2_TOP-.5),(F_R,S2_TOP+5.0),(1.75,S2_TOP+7.5),(1.0,S2_TOP+9.5),(.3,HEIGHT-.2),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,32)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,32)

complete(HERE,'LongMarch8A',HEIGHT,56,camera=(34,-60,0),label='Long March 8A')
