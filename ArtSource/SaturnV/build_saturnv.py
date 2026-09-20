"""Saturn V visual approximation: wide white S-IC first stage with a black
roll-pattern band, stepping down through narrower upper stages, topped with
the Apollo/LES escape tower spike."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=111.0
R1=5.05   # S-IC / S-II diameter
R2=3.3    # S-IVB diameter

white=material('SaturnV_White',(.93,.93,.9),.1,.4)
black=material('SaturnV_Black',(.03,.03,.03),.2,.45)
silver=material('SaturnV_Silver',(.7,.7,.68),.6,.3)

# S-IC first stage.
lathe('Stage1',[(R1,4),(R1,45)],white)
cap('Stage1_Base',(R1*.92,0),black,at_top=False)
lathe('Stage1_Skirt',[(R1*.92,0),(R1,4)],black)
# Roll pattern: black band plus 4 vertical accent stripes for recognition.
box('Roll_Band',(0,0,38),(R1*2.02,R1*2.02,3),black)
for i in range(4):
    a=math.radians(i*90)
    x,y=R1*1.01*math.cos(a),R1*1.01*math.sin(a)
    box('Roll_Stripe',(x,y,25),(1.1,1.1,32),black,rotation=(0,0,a))

# S-II second stage: same diameter as S-IC, shorter.
lathe('Stage2',[(R1,45),(R1,66)],white)
lathe('Stage2_Taper',[(R1,66),(R2,72)],white)

# S-IVB third stage: narrower.
lathe('Stage3',[(R2,72),(R2,88)],white)
lathe('Stage3_Taper',[(R2,88),(R2*.55,93)],white)

# Instrument unit + Apollo/LES: a slender spike on top.
lathe('Apollo_Adapter',[(R2*.55,93),(1.05,97)],silver)
lathe('Capsule',[(1.05,97),(.85,100.5),(.35,103)],silver)
lathe('LES_Tower',[(.12,103),(.12,109),(0,HEIGHT)],black)

complete(HERE,'SaturnV',HEIGHT,120,camera=(75,-125,0),label='Saturn V')
