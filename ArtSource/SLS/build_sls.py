"""SLS Block 1 (Artemis I), built to its real size: 98.1 m tall.

  * Core stage: 64.6 m x 8.4 m, orange foam over the tanks, with the engine
    section and four RS-25s in a square, the forward skirt and the
    intertank band.
  * Two five-segment Solid Rocket Boosters: 54 m x 3.71 m, white, with aft
    skirts, segment joints and nose cones.
  * Launch Vehicle Stage Adapter (LVSA): 8.4 m -> 5 m cone, stays on the
    core stage.
  * Interim Cryogenic Propulsion Stage (ICPS, a Delta IV upper stage): 5 m,
    one RL10B-2 with its extendable nozzle.
  * Orion on its stage adapter: service module with folded solar-array
    fairings, the crew module under the Launch Abort System's ogive
    fairing and tower.

Stages, in separation order: SRB_Left, SRB_Right, LAS (jettisoned ~T+3:15),
Core, ICPS. Visual approximation from published dimensions and photos, not
engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=98.1
CORE_R=4.2
SRB_R=1.855
SRB_X=CORE_R+SRB_R+.35
CORE_BASE=3.6
CORE_TOP=CORE_BASE+64.6          # 68.2
LVSA_TOP=CORE_TOP+8.9            # 77.1

foam=material('SLS_CoreFoam',(.80,.42,.16),.0,.85)
intertank=material('SLS_Intertank',(.64,.33,.13),.1,.8)
white=material('SLS_White',(.92,.92,.91),.08,.45)
joint=material('SLS_Joint',(.42,.42,.42),.4,.5)
engine=material('SLS_Engine',(.25,.23,.21),.85,.3)
heat=material('SLS_EngineSection',(.18,.17,.16),.3,.7)
silver=material('SLS_Silver',(.62,.63,.65),.8,.35)
orion=material('SLS_OrionPanels',(.80,.80,.80),.3,.4)
black=material('SLS_Black',(.04,.04,.045),.1,.7)

stage('SRB_Left','Five-segment Solid Rocket Booster, separates at ~2 min 12 s')
stage('SRB_Right','Five-segment Solid Rocket Booster, separates at ~2 min 12 s')
stage('LAS','Launch Abort System tower and ogive fairing, jettisoned at ~3 min 15 s')
stage('Core','Core stage: 4 RS-25, with the LVSA; separates at ~8 min 10 s')
stage('ICPS','Interim Cryogenic Propulsion Stage (1 RL10B-2) with Orion')

# --- Solid Rocket Boosters ---------------------------------------------------
for name,side in (('SRB_Left',-1),('SRB_Right',1)):
    stage(name)
    parts=[nozzle('SRB_Nozzle',0,0,3.6,3.6,.65,1.95,joint,40),
           cap('SRB_SkirtBase',(2.6,3.2),joint,at_top=False),
           lathe('SRB_AftSkirt',[(2.6,3.2),(2.2,5.0),(SRB_R,6.8)],white),
           lathe('SRB_Body',[(SRB_R,6.8),(SRB_R,48.6),(1.62,49.9),(1.4,51.1),(1.0,52.4),(.5,53.4),(0,54.0)],white)]
    for z in (14.0,22.0,30.0,38.0):
        parts.append(ring('SRB_Joint',SRB_R,z,.045,joint))
    parts.append(box('SRB_Strut',(-side*(SRB_R+.2),0,8.0),(.5,.3,.3),silver))
    parts.append(box('SRB_Strut',(-side*(SRB_R+.2),0,46.0),(.5,.3,.3),silver))
    place(parts,Matrix.Translation((side*SRB_X,0,0)))

# --- Core stage ----------------------------------------------------------------
stage('Core')
# Four RS-25s in a square under the engine section.
for x,y in ((1.45,1.45),(-1.45,1.45),(1.45,-1.45),(-1.45,-1.45)):
    nozzle('RS25',x,y,CORE_BASE+.2,3.1,.42,1.15,engine,40)
cap('Core_HeatShield',(CORE_R,CORE_BASE),heat,at_top=False)
lathe('Core_EngineSection',[(CORE_R,CORE_BASE),(CORE_R,CORE_BASE+6.3)],heat)
lathe('Core_LH2Tank',[(CORE_R,CORE_BASE+6.3),(CORE_R,CORE_BASE+46.5)],foam)
lathe('Core_Intertank',[(CORE_R+.03,CORE_BASE+46.5),(CORE_R+.03,CORE_BASE+53.5)],intertank)
for k in range(5): ring('Core_IntertankRib',CORE_R+.05,CORE_BASE+47.3+k*1.4,.04,intertank)
lathe('Core_LOXTank',[(CORE_R,CORE_BASE+53.5),(CORE_R,CORE_TOP-3.0)],foam)
lathe('Core_ForwardSkirt',[(CORE_R,CORE_TOP-3.0),(CORE_R,CORE_TOP)],intertank)
# Feedlines and cable tray down the side.
cylinder('Core_LOXFeedline',(0,-CORE_R-.2,CORE_BASE+53.0),(0,-CORE_R-.2,CORE_BASE+6.5),.25,foam,16)
box('Core_CableTray',(1.6,-CORE_R-.12,CORE_BASE+30.0),(.35,.25,44.0),foam)
# LVSA: cone from 8.4 m to 5 m, stays with the core stage.
lathe('LVSA',[(CORE_R,CORE_TOP),(CORE_R-.1,CORE_TOP+1.0),(2.55,LVSA_TOP)],white)
ring('LVSA_Band',CORE_R-.05,CORE_TOP+1.0,.05,silver)

# --- ICPS + Orion -----------------------------------------------------------------
stage('ICPS')
ICPS_BASE=CORE_TOP+1.2          # hangs down inside the LVSA
ICPS_TOP=LVSA_TOP+5.6           # 82.7
nozzle('RL10B2',0,0,ICPS_BASE+2.8,2.9,.3,1.05,engine,40)
lathe('ICPS_LH2Tank',[(1.6,ICPS_BASE+2.8),(2.5,ICPS_BASE+4.5),(2.5,LVSA_TOP),(2.5,ICPS_TOP)],white)
cap('ICPS_Base',(1.6,ICPS_BASE+2.8),silver,at_top=False)
# Orion stage adapter.
OSA_TOP=ICPS_TOP+1.5
lathe('Orion_StageAdapter',[(2.5,ICPS_TOP),(2.55,OSA_TOP)],silver)
# European Service Module inside its three fairing panels.
ESM_TOP=OSA_TOP+4.8
lathe('Orion_SM_Fairings',[(2.6,OSA_TOP),(2.6,ESM_TOP)],orion)
for k in range(3):
    a=math.radians(k*120)
    box('SM_PanelSeam',(2.62*math.cos(a),2.62*math.sin(a),(OSA_TOP+ESM_TOP)/2),(.08,.08,4.8),black,rotation=(0,0,a))
# Crew module under the Launch Abort System's ogive fairing.
cap('CM_Shield',(2.5,ESM_TOP),silver,at_top=False)
stage('LAS')
OGIVE_TOP=ESM_TOP+5.3
lathe('LAS_Ogive',[(2.6,ESM_TOP),(2.55,ESM_TOP+1.0),(2.2,ESM_TOP+2.6),(1.5,ESM_TOP+4.0),(.85,OGIVE_TOP)],white)
lathe('LAS_Tower',[(.85,OGIVE_TOP),(.6,OGIVE_TOP+.5),(.55,OGIVE_TOP+3.8),(.42,HEIGHT-.6),(0,HEIGHT)],white)
# Abort motor nozzles canted out from the tower.
for k in range(4):
    a=math.radians(45+k*90)
    cylinder('LAS_AbortNozzle',(.5*math.cos(a),.5*math.sin(a),OGIVE_TOP+1.0),(.95*math.cos(a),.95*math.sin(a),OGIVE_TOP+.4),.16,black,12)
ring('LAS_Band',.6,OGIVE_TOP+3.0,.05,black)

complete(HERE,'SLS',HEIGHT,104,camera=(62,-110,0),label='SLS Block 1')
