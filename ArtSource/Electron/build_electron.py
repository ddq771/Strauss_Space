"""Rocket Lab Electron, built to its real size: 18 m tall, 1.2 m - low
detail. Black carbon-composite body.

  * First stage: nine Rutherford nozzles (centre + ring of 8), 11.5 m.
  * Second stage with one Rutherford Vacuum and the kick stage.
  * Fairing in two halves.

Stages, in separation order: FirstStage, Fairing_A, Fairing_B,
SecondStage, Payload. Visual approximation, not CAD.
"""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=18.0
R=.6
S1_TOP=12.1
S2_TOP=15.3

black=material('EL_Carbon',(.06,.06,.07),.3,.45)
white=material('EL_White',(.93,.93,.93),.1,.4)
engine=material('EL_Engine',(.28,.26,.25),.8,.35)
gold=material('EL_PayloadFoil',(.85,.64,.24),.9,.3)

stage('FirstStage','First stage: 9 Rutherford')
stage('Fairing_A','Fairing half')
stage('Fairing_B','Fairing half')
stage('SecondStage','Second stage: 1 Rutherford Vacuum')
stage('Payload','Kick stage and satellite')

stage('FirstStage')
for i in range(9):
    a=math.radians((i-1)*45)
    x,y=(0,0) if i==0 else (.4*math.cos(a),.4*math.sin(a))
    nozzle('Rutherford',x,y,.6,.6,.06,.12,engine,16)
cap('S1_Base',(R,.6),black,at_top=False)
lathe('S1_Body',[(R,.6),(R,S1_TOP)],black,32)
ring('S1_Band',R,8.0,.02,white)

stage('SecondStage')
nozzle('RutherfordVac',0,0,S1_TOP+.4,1.0,.08,.3,engine,16)
lathe('S2_Body',[(R,S1_TOP),(R,S2_TOP)],black,32)

stage('Payload')
lathe('KickStage',[(.5,S2_TOP),(.5,S2_TOP+.6),(.35,S2_TOP+1.2)],gold,16)

fairing=[(R,S2_TOP),(R,S2_TOP+1.0),(.5,S2_TOP+1.8),(.25,HEIGHT-.15),(0,HEIGHT)]
stage('Fairing_A'); lathe_arc('Fairing_A',fairing,black,math.pi/2,3*math.pi/2,16)
stage('Fairing_B'); lathe_arc('Fairing_B',fairing,black,-math.pi/2,math.pi/2,16)

complete(HERE,'Electron',HEIGHT,21,camera=(13,-23,0),label='Electron')
