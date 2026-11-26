"""Saturn IB with an Apollo CSM, built to its real size: 68.06 m tall,
6.6 m - low detail.

  * S-IB (25.5 m): a central 2.67 m tank ringed by eight 1.78 m tanks
    (the Jupiter/Redstone tooling it was built from), eight H-1 nozzles
    (4 inboard, 4 outboard, matching the preset's layout), eight fins.
  * S-IVB-200 with the Instrument Unit, the tapered adapter (SLA), the
    Apollo CSM and the launch escape tower.

Stages, in separation order: SIB, LES, SIVB, Payload. Visual
approximation from published dimensions and photos, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *
from mathutils import Matrix

HEIGHT=68.06
R=3.3
SIB_TOP=25.5
SIVB_BASE=28.6
SIVB_TOP=46.4
IU_TOP=47.3
SLA_TOP=55.8
CSM_TOP=63.0

white=material('SIB_White',(.92,.92,.89),.08,.5)
black=material('SIB_Black',(.08,.08,.09),.2,.55)
engine=material('SIB_Engine',(.24,.22,.21),.8,.35)
silver=material('SIB_CSM',(.78,.79,.8),.85,.3)

stage('SIB','S-IB: 8 H-1')
stage('LES','Launch escape tower, jettisoned after S-IVB ignition')
stage('SIVB','S-IVB-200: 1 J-2, with the Instrument Unit')
stage('Payload','Apollo CSM in its adapter')

stage('SIB')
for i in range(4):
    a=math.radians(45+90*i)
    nozzle('H1_Inboard',.95*math.cos(a),.95*math.sin(a),2.6,2.4,.3,.6,engine,20)
    b=math.radians(90*i)
    nozzle('H1_Outboard',2.4*math.cos(b),2.4*math.sin(b),2.6,2.4,.3,.6,engine,20)
cap('SIB_Base',(R,2.6),black,at_top=False)
lathe('SIB_Thrust',[(R,2.6),(R,4.0)],black,48)
lathe('SIB_Centre',[(1.34,4.0),(1.34,SIB_TOP-1.0)],white,32)
for i in range(8):
    a=math.radians(22.5+45*i)
    t=[lathe('SIB_Tank',[(.89,4.0),(.89,SIB_TOP-2.5),(.5,SIB_TOP-1.2)],white if i%2 else black,24)]
    place(t,Matrix.Translation((2.4*math.cos(a),2.4*math.sin(a),0)))
lathe('SIB_Spider',[(R,SIB_TOP-1.0),(R,SIB_TOP)],white,48)
for i in range(8):
    fin=[prism('Fin',[(R,2.0),(R,6.5),(R+2.6,3.0),(R+2.6,1.5)],.25,black)]
    place(fin,Matrix.Rotation(math.radians(22.5+45*i),4,'Z'))
lathe('Interstage',[(R,SIB_TOP),(R,SIVB_BASE)],white,48)

stage('SIVB')
nozzle('J2',0,0,SIVB_BASE+.8,3.0,.4,1.0,engine,24)
lathe('SIVB_Tank',[(R,SIVB_BASE),(R,SIVB_TOP)],white,48)
lathe('IU',[(R,SIVB_TOP),(R,IU_TOP)],black,48)
lathe('SLA',[(R,IU_TOP),(1.95,SLA_TOP)],white,48)

stage('Payload')
lathe('SM',[(1.95,SLA_TOP),(1.95,SLA_TOP+4.0)],silver,48)
lathe('CM',[(1.95,SLA_TOP+4.0),(.5,CSM_TOP)],silver,48)

stage('LES')
lathe('BoostCover',[(1.95,SLA_TOP+4.0),(.5,CSM_TOP),(.3,CSM_TOP+.2)],white,32)
cylinder('LES_Tower',(0,0,CSM_TOP+.2),(0,0,CSM_TOP+3.0),.35,black,12)
lathe('LES_Motor',[(.45,CSM_TOP+3.0),(.45,HEIGHT-.8),(0,HEIGHT)],black,16)

complete(HERE,'SaturnIB',HEIGHT,74,camera=(45,-78,0),label='Saturn IB')
