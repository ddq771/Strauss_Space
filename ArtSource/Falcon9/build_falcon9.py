"""Falcon 9 visual approximation: white slender body, black interstage band,
4 folded landing legs, 4 grid fins near the top of the first stage."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=87.0
R=1.85

white=material('Falcon9_White',(.92,.92,.93),.15,.35)
black=material('Falcon9_Black',(.03,.03,.035),.2,.4)
steel=material('Falcon9_Steel',(.55,.57,.6),.75,.3)

# Main body: cylindrical first+second stage, straight tube.
lathe('Body_Main',[(R,4),(R,66)],white)
cap('Base_Cap',(R*.7,0),black,at_top=False)

# Interstage: slightly narrower, black band between stages.
lathe('Interstage',[(R,66),(R*.94,66),(R*.94,70),(R,70)],black)

# Fairing: ogive nose from the interstage top to a rounded tip.
fairing=[(R,70),(R*.98,72),(R*.85,76),(R*.62,80),(R*.35,83.5),(R*.12,85.8),(0,HEIGHT)]
lathe('Fairing',fairing,white)

# Grid fins: 4 flat fins just below the interstage, folded flat against body.
for i in range(4):
    a=math.radians(45+i*90)
    x,y=(R+.55)*math.cos(a),(R+.55)*math.sin(a)
    box('Grid_Fin',(x,y,63),(0.05,1.1,1.4),steel,rotation=(0,0,a))

# Landing legs: 4 struts folded against the base, angled slightly outward.
for i in range(4):
    a=math.radians(45+i*90)
    x0,y0=R*.85*math.cos(a),R*.85*math.sin(a)
    x1,y1=(R+2.3)*math.cos(a),(R+2.3)*math.sin(a)
    cylinder('Landing_Leg',(x0,y0,7.5),(x1,y1,.3),.16,steel,16)
    box('Foot_Pad',(x1,y1,.15),(.55,.55,.12),black)

# Engine section: dark octaweb skirt at the very base (visual only - the
# actual engine cluster is RocketAssemblyController's own separate system).
lathe('Engine_Skirt',[(R*.7,0),(R,4)],black)

complete(HERE,'Falcon9',HEIGHT,95,camera=(60,-100,0),label='Falcon 9')
