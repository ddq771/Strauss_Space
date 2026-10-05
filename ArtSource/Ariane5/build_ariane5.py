"""Ariane 5 ECA, built to its real size: 53.0 m tall (long fairing).

  * EPC cryogenic main stage: 30.5 m x 5.4 m, white, with the Vulcain 2
    engine and its bell below the thrust frame.
  * Two EAP P241 solid boosters: 31.6 m x 3.06 m, white with dark joint
    bands, conical noses and big nozzles, attached either side of the EPC.
  * ESC-A cryogenic upper stage (HM7B engine) and the vehicle equipment
    bay, inside the stage adapter.
  * Payload fairing, 5.4 m, ogive nose, in two halves (jettisoned ~T+3:11).

Stages, in separation order: EAP_Left, EAP_Right, Fairing_A, Fairing_B,
EPC, ESC. Visual approximation from published dimensions and photos, not
engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=53.0
R=2.7
EAP_R=1.53
EAP_X=R+EAP_R+.3
EPC_BASE=3.6
EPC_TOP=EPC_BASE+30.5            # 34.1
ESC_TOP=EPC_TOP+5.6              # 39.7
VEB_TOP=ESC_TOP+1.1              # 40.8

white=material('A5_White',(.93,.93,.93),.08,.45)
band=material('A5_Band',(.12,.12,.13),.2,.6)
engine=material('A5_Engine',(.26,.24,.22),.85,.3)
grey=material('A5_Grey',(.55,.56,.58),.6,.4)
blue=material('A5_Logo',(.05,.18,.55),.1,.5)

stage('EAP_Left','EAP P241 solid booster, separates at ~2 min 20 s')
stage('EAP_Right','EAP P241 solid booster, separates at ~2 min 20 s')
stage('Fairing_A','Payload fairing half, jettisoned at ~3 min 11 s')
stage('Fairing_B','Payload fairing half, jettisoned at ~3 min 11 s')
stage('EPC','EPC main stage: 1 Vulcain 2; separates at ~8 min 50 s')
stage('ESC','ESC-A upper stage (1 HM7B) with the payload')

# --- EAP solid boosters ----------------------------------------------------------
for name,side in (('EAP_Left',-1),('EAP_Right',1)):
    stage(name)
    parts=[nozzle('EAP_Nozzle',0,0,3.4,3.4,.6,1.5,engine,40),
           cap('EAP_Base',(1.75,3.0),band,at_top=False),
           lathe('EAP_AftSkirt',[(1.75,3.0),(EAP_R,4.6)],white),
           lathe('EAP_Body',[(EAP_R,4.6),(EAP_R,27.3),(1.35,28.6),(1.0,29.9),(.55,31.0),(0,31.6)],white)]
    for z in (10.5,17.0,23.5):
        parts.append(lathe('EAP_JointBand',[(EAP_R+.02,z),(EAP_R+.02,z+.6)],band))
    parts.append(box('EAP_Attach',(-side*(EAP_R+.2),0,6.0),(.45,.3,.3),grey))
    parts.append(box('EAP_Attach',(-side*(EAP_R+.2),0,27.0),(.45,.3,.3),grey))
    place(parts,Matrix.Translation((side*EAP_X,0,0)))

# --- EPC main stage -----------------------------------------------------------------
stage('EPC')
nozzle('Vulcain2',0,0,EPC_BASE+.3,3.4,.55,1.05,engine,48)
cap('EPC_ThrustFrame',(R-.3,EPC_BASE),grey,at_top=False)
lathe('EPC_AftSkirt',[(R-.3,EPC_BASE),(R,EPC_BASE+1.4)],grey)
lathe('EPC_Tank',[(R,EPC_BASE+1.4),(R,EPC_TOP)],white)
ring('EPC_Seam',R,EPC_BASE+22.0,.03,band)
box('EPC_Logo',(0,-R-.02,EPC_BASE+18.0),(1.6,.04,5.5),blue)
box('EPC_Raceway',(1.2,-R-.08,EPC_BASE+15.0),(.25,.15,24.0),white)

# --- ESC-A + equipment bay ---------------------------------------------------------
stage('ESC')
nozzle('HM7B',0,0,EPC_TOP+1.6,1.6,.18,.5,engine,32)
lathe('ESC_Adapter',[(R,EPC_TOP),(R,ESC_TOP)],white)
ring('ESC_Seam',R,EPC_TOP+.05,.04,band)
lathe('VEB',[(R,ESC_TOP),(R,VEB_TOP)],grey)
lathe('Payload',[(1.8,VEB_TOP),(1.8,VEB_TOP+6.0),(1.2,VEB_TOP+7.0)],grey)

# --- Fairing halves ------------------------------------------------------------------
fairing=[(R,VEB_TOP),(R,VEB_TOP+7.8),(2.62,VEB_TOP+8.6),(2.4,VEB_TOP+9.5),(2.0,VEB_TOP+10.3),
         (1.45,VEB_TOP+11.0),(.85,VEB_TOP+11.6),(.3,HEIGHT-.08),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,48)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,48)

complete(HERE,'Ariane5',HEIGHT,58,camera=(36,-62,0),label='Ariane 5 ECA')
