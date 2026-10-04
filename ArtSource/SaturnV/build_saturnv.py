"""Saturn V (Apollo lunar configuration), built to its real size: 110.6 m
tall, 10.1 m first and second stages, 6.6 m third stage.

  * S-IC (42 m): five F-1 engines, four engine fairings and fins, the
    black-and-white roll pattern.
  * S-II (24.9 m with its interstage): the interstage ring drops separately,
    the conical adapter up to the third stage stays with the S-II.
  * S-IVB (6.6 m) with its J-2, APS modules and the Instrument Unit.
  * Spacecraft LM Adapter, Service Module, Command Module under its boost
    protective cover, and the Launch Escape System tower.

Stages, in separation order: SIC, Interstage, LES, SII, SIVB, SLA,
ServiceModule, CommandModule. Visual approximation from published
dimensions and photos, not engineering CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=110.6
R1=5.05   # S-IC / S-II
R3=3.3    # S-IVB / IU
RCSM=1.95

white=material('SaturnV_White',(.93,.93,.91),.1,.45)
black=material('SaturnV_Black',(.03,.03,.03),.2,.5)
engine=material('SaturnV_F1',(.20,.19,.18),.8,.35)
silver=material('SaturnV_Silver',(.74,.75,.76),.85,.3)
iu=material('SaturnV_IU',(.55,.55,.52),.5,.5)
red=material('SaturnV_LESRed',(.55,.08,.06),.2,.5)

stage('SIC','First stage: five F-1; separates at ~2.7 min')
stage('Interstage','S-IC/S-II interstage ring, dropped ~30 s after S-II ignition')
stage('LES','Launch Escape System, jettisoned after S-II ignition')
stage('SII','Second stage: five J-2; separates at ~9 min')
stage('SIVB','Third stage + Instrument Unit: one J-2, restarts for TLI')
stage('SLA','Spacecraft LM Adapter panels, open after TLI')
stage('ServiceModule','Apollo Service Module with the SPS engine')
stage('CommandModule','Apollo Command Module')

def quadrant_panels(name,r,z0,z1,quadrants,mat):
    """Black paint panels over chosen 90° quadrants (the roll pattern)."""
    for q in quadrants:
        lathe_arc(name,[(r+.02,z0),(r+.02,z1)],mat,math.radians(q*90),math.radians(q*90+90),24)

# --- S-IC -----------------------------------------------------------------
stage('SIC')
SIC_BASE=5.7
SIC_TOP=42.1
for i in range(5):
    if i==0: x=y=0
    else:
        a=math.radians(45+(i-1)*90)
        x,y=3.3*math.cos(a),3.3*math.sin(a)
    nozzle('F1',x,y,SIC_BASE+.1,5.6,.6,1.88,engine,40)
cap('SIC_HeatShield',(R1,SIC_BASE),black,at_top=False)
lathe('SIC_Body',[(R1,SIC_BASE),(R1,SIC_TOP)],white)
quadrant_panels('SIC_Roll',R1,SIC_BASE,9.5,[0,2],black)
lathe('SIC_Intertank',[(R1+.02,21.0),(R1+.02,23.6)],black)
quadrant_panels('SIC_Roll',R1,33.0,39.5,[1,3],black)
lathe('SIC_ForwardSkirt',[(R1+.02,40.8),(R1+.02,SIC_TOP)],black)
# Engine fairings over the outer F-1s, each carrying a fin (19.2 m across
# the fin tips).
for i in range(4):
    a=math.radians(45+i*90)
    fairing=lathe('EngineFairing',[(1.55,SIC_BASE-.2),(1.25,8.2),(.55,10.8),(0,11.4)],white)
    fin=prism('Fin',[(1.2,SIC_BASE-.1),(5.6,SIC_BASE+.3),(5.6,SIC_BASE+2.0),(1.2,SIC_BASE+4.4)],.22,black)
    place([fairing,fin],Matrix.Rotation(a,4,'Z')@Matrix.Translation((3.9,0,0)))

# --- Interstage -------------------------------------------------------------
stage('Interstage')
INTERSTAGE_TOP=47.7
lathe('Interstage',[(R1,SIC_TOP),(R1,INTERSTAGE_TOP)],white)
for i in range(8):
    a=math.radians(22.5+i*45)
    cylinder('UllageMotor',((R1+.12)*math.cos(a),(R1+.12)*math.sin(a),43.0),
             ((R1+.12)*math.cos(a),(R1+.12)*math.sin(a),46.4),.2,white,12)

# --- S-II -------------------------------------------------------------------
stage('SII')
SII_TOP=66.9
ADAPTER_TOP=71.6
for i in range(5):
    if i==0: x=y=0
    else:
        a=math.radians(45+(i-1)*90)
        x,y=2.2*math.cos(a),2.2*math.sin(a)
    nozzle('SII_J2',x,y,INTERSTAGE_TOP,3.0,.35,1.0,engine,32)
cap('SII_Base',(R1,INTERSTAGE_TOP),black,at_top=False)
lathe('SII_Body',[(R1,INTERSTAGE_TOP),(R1,SII_TOP)],white)
lathe('SII_ForwardSkirt',[(R1+.02,65.9),(R1+.02,SII_TOP)],black)
lathe('SII_SIVB_Adapter',[(R1,SII_TOP),(R3,ADAPTER_TOP)],white)

# --- S-IVB + Instrument Unit --------------------------------------------------
stage('SIVB')
SIVB_TOP=84.7
IU_TOP=85.6
nozzle('SIVB_J2',0,0,ADAPTER_TOP,3.2,.35,1.0,engine,32)
cap('SIVB_Base',(R3,ADAPTER_TOP),black,at_top=False)
lathe('SIVB_Body',[(R3,ADAPTER_TOP),(R3,SIVB_TOP)],white)
quadrant_panels('SIVB_AftSkirt',R3,ADAPTER_TOP,74.2,[0,2],black)
for side in (-1,1):
    box('SIVB_APS',(side*(R3+.35),0,73.2),(.7,1.6,2.2),white)
lathe('IU',[(R3+.01,SIVB_TOP),(R3+.01,IU_TOP)],iu)

# --- Spacecraft LM Adapter ----------------------------------------------------
stage('SLA')
SLA_TOP=94.2
lathe('SLA',[(R3,IU_TOP),(RCSM,SLA_TOP)],white)

# --- Service + Command Modules ------------------------------------------------
stage('ServiceModule')
SM_TOP=98.1
nozzle('SPS',0,0,SLA_TOP,2.8,.45,1.25,engine,32)
cap('SM_Base',(RCSM,SLA_TOP),silver,at_top=False)
lathe('SM',[(RCSM,SLA_TOP),(RCSM,SM_TOP)],silver)
for i in range(4):
    a=math.radians(45+i*90)
    box('SM_RCSQuad',((RCSM+.12)*math.cos(a),(RCSM+.12)*math.sin(a),97.2),(.3,.5,.6),silver,rotation=(0,0,a))
stage('CommandModule')
CM_TOP=101.6
lathe('CM_BoostCover',[(RCSM,SM_TOP),(RCSM,SM_TOP+.25),(.42,CM_TOP)],white)
cap('CM_Top',(.42,CM_TOP),white,at_top=True)

# --- Launch Escape System -----------------------------------------------------
stage('LES')
TOWER_TOP=105.5
for i in range(4):
    a0=math.radians(45+i*90)
    lo=(.62*math.cos(a0),.62*math.sin(a0),CM_TOP)
    hi=(.32*math.cos(a0),.32*math.sin(a0),TOWER_TOP)
    cylinder('LES_Truss',lo,hi,.04,red,8)
    a1=math.radians(45+(i+1)*90)
    cylinder('LES_Truss',lo,(.47*math.cos(a1),.47*math.sin(a1),(CM_TOP+TOWER_TOP)/2),.03,red,8)
lathe('LES_Motor',[(.33,TOWER_TOP),(.33,109.4),(.24,109.8)],white)
lathe('LES_Nose',[(.24,109.8),(.12,110.3),(0,HEIGHT)],black)
for i in range(4):
    a=math.radians(i*90)
    nozzle('LES_Nozzle',.2*math.cos(a),.2*math.sin(a),TOWER_TOP+.4,.5,.06,.13,engine,12)

complete(HERE,'SaturnV',HEIGHT,120,camera=(75,-125,0),label='Saturn V')
