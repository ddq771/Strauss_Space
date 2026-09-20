"""Starship (full stack) visual approximation: wide bare-steel body, blunt
rounded nose, aft flaps near the base, forward flaps near the nose, and a
darker "hot side" heat-shield band down one side."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=126.0
R=4.5

steel=material('Starship_Steel',(.72,.73,.75),.85,.28)
dark=material('Starship_HeatShield',(.09,.09,.1),.4,.55)
black=material('Starship_Black',(.03,.03,.035),.2,.4)

# Main body: constant-diameter steel tube (Booster + Ship combined, no
# separation modeled - see RocketPresets' doc comment on why).
lathe('Body_Main',[(R,6),(R,112)],steel)
cap('Base_Cap',(R*.85,0),black,at_top=False)

# Nose: Starship's nose is a blunt rounded cone, not a sharp ogive.
nose=[(R,112),(R*.97,116),(R*.85,119.5),(R*.65,122.5),(R*.35,124.8),(0,HEIGHT)]
lathe('Nose',nose,steel)

# Heat-shield band: a dark strip down one side (the windward face on reentry).
box('Heat_Shield_Belly',(R*.85,0,64),(1.6,.06,58),dark)

# Aft flaps: 2 large flaps near the base (booster grid fins simplified as
# a pair of flat rectangular fins here for silhouette recognition).
for i in range(2):
    a=math.radians(90+i*180)
    x,y=(R+2.2)*math.cos(a),(R+2.2)*math.sin(a)
    box('Aft_Flap',(x,y,14),(0.35,4.5,7),steel,rotation=(0,0,a))

# Forward flaps: 2 smaller flaps near the nose base.
for i in range(2):
    a=math.radians(45+i*180)
    x,y=(R+1.6)*math.cos(a),(R+1.6)*math.sin(a)
    box('Fwd_Flap',(x,y,108),(0.3,3.2,5),steel,rotation=(0,0,a))

# Engine skirt: dark flared section at the very base (visual only - the
# actual 39-engine cluster is RocketAssemblyController's own system).
lathe('Engine_Skirt',[(R*.85,0),(R,6)],black)

complete(HERE,'Starship',HEIGHT,135,camera=(85,-140,0),label='Starship')
