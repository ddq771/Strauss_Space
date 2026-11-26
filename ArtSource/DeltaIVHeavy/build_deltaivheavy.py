"""Delta IV Heavy, built to its real size: 72 m tall, three 5.1 m cores -
low detail. Orange foam insulation on the cores.

  * Three Common Booster Cores, 5.3 m apart, one big RS-68A nozzle each;
    the side cores end in nose cones.
  * Interstage, the 5 m Delta Cryogenic Second Stage, the 5 m fairing.

Stages, in separation order: Booster_Left, Booster_Right, CentreCore,
Fairing_A, Fairing_B, DCSS, Payload. Visual approximation, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=72.0
R=2.565
SIDE=5.3
CORE_TOP=43.0
DCSS_TOP=53.0

orange=material('D4_Foam',(.85,.45,.15),.05,.75)
white=material('D4_White',(.93,.93,.93),.1,.4)
dark=material('D4_Dark',(.12,.12,.13),.3,.6)
engine=material('D4_Engine',(.24,.22,.21),.8,.35)
gold=material('D4_PayloadFoil',(.85,.64,.24),.9,.3)

stage('Booster_Left','Side core: 1 RS-68A')
stage('Booster_Right','Side core: 1 RS-68A')
stage('CentreCore','Centre core: 1 RS-68A')
stage('Fairing_A','Fairing half')
stage('Fairing_B','Fairing half')
stage('DCSS','Delta Cryogenic Second Stage: 1 RL10B-2')
stage('Payload','Generic satellite')

def core(label):
    """One core built on the axis: nozzle, dark engine section, orange tank."""
    return [nozzle(label+'_RS68A',0,0,3.0,3.0,.5,1.22,engine,32),
            cap(label+'_Base',(R,3.0),dark,at_top=False),
            lathe(label+'_EngineSection',[(R,3.0),(R,5.0)],dark,48),
            lathe(label+'_Tank',[(R,5.0),(R,CORE_TOP)],orange,48)]

for name,x in (('Booster_Left',-SIDE),('Booster_Right',SIDE)):
    stage(name)
    parts=core(name)
    parts.append(lathe(name+'_Nose',[(R,CORE_TOP),(2.0,CORE_TOP+3.0),(1.0,CORE_TOP+5.0),(0,CORE_TOP+6.0)],white,48))
    place(parts,Matrix.Translation((x,0,0)))

stage('CentreCore')
core('Centre')
lathe('Interstage',[(R,CORE_TOP),(R,CORE_TOP+3.0),(2.5,CORE_TOP+4.0)],white,48)

stage('DCSS')
nozzle('RL10B2',0,0,CORE_TOP+3.5,2.4,.2,1.07,engine,24)
lathe('DCSS_Tank',[(2.5,CORE_TOP+4.0),(2.5,DCSS_TOP)],white,48)

stage('Payload')
lathe('Payload',[(1.6,DCSS_TOP+.5),(2.0,DCSS_TOP+1.5),(2.0,DCSS_TOP+7.0),(1.0,DCSS_TOP+8.0)],gold,32)

fairing=[(2.5,DCSS_TOP-2.0),(2.55,DCSS_TOP-1.0),(2.55,DCSS_TOP+10.0),(2.1,DCSS_TOP+14.0),(1.2,DCSS_TOP+17.0),(.3,HEIGHT-.2),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,32)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,32)

complete(HERE,'DeltaIVHeavy',HEIGHT,78,camera=(48,-82,0),label='Delta IV Heavy')
