"""Atlas V 431, built to its real size: 58.3 m tall - the Atlas V 401
model with three AJ-60A solid boosters (1.58 m x ~20 m) at 120 degrees.

  * Common Core Booster: 32.5 m x 3.81 m, the familiar bronze-orange
    insulation, with the RD-180's two nozzles on the boat-tail and the
    raceways down the side.
  * Interstage adapter (white) and the Centaur III upper stage: 3.05 m,
    stainless balloon tanks, one RL10C-1 hanging into the adapter.
  * 4-m Extended Payload Fairing (white) over the forward Centaur and the
    payload, in two halves (jettisoned ~T+3:30).

Stages, in separation order: SRB_1, SRB_2, SRB_3, Fairing_A, Fairing_B,
Booster, Centaur.
Visual approximation from published dimensions and photos, not
engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=58.3
R=1.905
C_R=1.525
F_R=2.1
CCB_BASE=2.9
CCB_TOP=CCB_BASE+32.5            # 35.4
ISA_TOP=CCB_TOP+3.2              # 38.6

bronze=material('Atlas_CCBInsulation',(.72,.43,.22),.05,.75)
white=material('Atlas_White',(.93,.93,.93),.08,.45)
steel=material('Atlas_CentaurSteel',(.72,.73,.75),.85,.3)
engine=material('Atlas_Engine',(.24,.22,.21),.85,.3)
dark=material('Atlas_Dark',(.10,.10,.11),.3,.6)
gold=material('Atlas_PayloadFoil',(.85,.64,.24),.9,.3)

stage('SRB_1','AJ-60A solid booster, jettisoned at ~1 min 40 s')
stage('SRB_2','AJ-60A solid booster, jettisoned at ~1 min 40 s')
stage('SRB_3','AJ-60A solid booster, jettisoned at ~1 min 40 s')
stage('Fairing_A','Payload fairing half, jettisoned at ~3 min 30 s')
stage('Fairing_B','Payload fairing half, jettisoned at ~3 min 30 s')
stage('Booster','Common Core Booster: 1 RD-180 (2 nozzles); separates at ~4 min 20 s')
stage('Centaur','Centaur III upper stage: 1 RL10C-1, with the payload')

# --- Common Core Booster -------------------------------------------------------------
stage('Booster')
for x in (-.75,.75):
    nozzle('RD180_Nozzle',x,0,CCB_BASE+.2,3.0,.4,.95,engine,40)
cap('CCB_BoatTail',(1.6,CCB_BASE),dark,at_top=False)
lathe('CCB_AftSkirt',[(1.6,CCB_BASE),(R,CCB_BASE+1.6)],dark)
lathe('CCB_Tank',[(R,CCB_BASE+1.6),(R,CCB_TOP)],bronze)
ring('CCB_Seam',R,CCB_BASE+20.5,.03,dark)
box('CCB_Raceway',(0,-R-.08,CCB_BASE+17.0),(.35,.16,29.0),bronze)
box('CCB_LO2Feed',(.9,-R-.1,CCB_BASE+9.0),(.25,.2,13.0),bronze)
# Interstage adapter: 3.81 m -> Centaur.
lathe('ISA',[(R,CCB_TOP),(R,CCB_TOP+1.2),(C_R+.1,ISA_TOP)],white)

# --- Solid boosters ----------------------------------------------------------------
# Each built upright on the axis (nozzle, white case, nose cone), then
# moved out 2.75 m (core radius + booster radius + a gap) and swung round
# to its place, 120 degrees apart.
SRB_R=.79
for k in range(3):
    stage('SRB_%d'%(k+1))
    parts=[nozzle('SRB_Nozzle',0,0,CCB_BASE+.6,1.6,.35,.7,dark,24),
           lathe('SRB_Case',[(SRB_R,CCB_BASE+.6),(SRB_R,CCB_BASE+17.4)],white,32),
           lathe('SRB_Nose',[(SRB_R,CCB_BASE+17.4),(.55,CCB_BASE+19.2),(0,CCB_BASE+20.4)],white,32)]
    place(parts,Matrix.Rotation(math.radians(90+120*k),4,'Z')@Matrix.Translation((R+SRB_R+.05,0,0)))

# --- Centaur ---------------------------------------------------------------------------
stage('Centaur')
nozzle('RL10C1',0,0,CCB_TOP+2.4,2.2,.22,.72,engine,40)
lathe('Centaur_AftBulkhead',[(.6,CCB_TOP+2.4),(1.2,CCB_TOP+2.9),(C_R,ISA_TOP)],steel)
lathe('Centaur_Tank',[(C_R,ISA_TOP),(C_R,ISA_TOP+9.6)],steel)
ring('Centaur_Band',C_R,ISA_TOP+3.0,.03,dark)
C_TOP=ISA_TOP+9.6               # 48.2
# Fairing boattail around the forward Centaur, payload adapter, payload.
lathe('Fairing_BoatTail',[(C_R+.05,ISA_TOP+4.5),(F_R,ISA_TOP+6.6),(F_R,C_TOP)],white)
lathe('PayloadAdapter',[(C_R,C_TOP),(.9,C_TOP+.9)],steel)
lathe('Payload',[(1.4,C_TOP+.9),(1.4,C_TOP+5.2),(.9,C_TOP+6.0)],gold)

# --- Fairing halves ---------------------------------------------------------------------
fairing=[(F_R,C_TOP),(F_R,C_TOP+4.6),(1.98,C_TOP+5.6),(1.7,C_TOP+6.8),(1.3,C_TOP+8.0),
         (.8,C_TOP+9.1),(.35,HEIGHT-.12),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,white,math.pi/2,3*math.pi/2,48)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,white,-math.pi/2,math.pi/2,48)

complete(HERE,'AtlasV431',HEIGHT,62,camera=(38,-66,0),label='Atlas V 431')
