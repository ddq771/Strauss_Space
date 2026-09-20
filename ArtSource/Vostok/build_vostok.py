"""Vostok (R-7) visual approximation: narrow core stage with a conical
payload shroud, and 4 strap-on boosters flaring outward at the base - the
distinctive silhouette that sets the R-7 family apart from a plain stack."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=39.0
CORE_R=1.5

metal=material('Vostok_OliveMetal',(.42,.44,.37),.55,.45)
dark=material('Vostok_DarkMetal',(.12,.12,.11),.4,.5)

# Core stage (Block A): slender cylinder, most of the height.
lathe('Core_Body',[(CORE_R,8),(CORE_R,29)],metal)
# Payload shroud: conical nose on top of the core.
lathe('Core_Shroud',[(CORE_R,29),(CORE_R*.9,32),(CORE_R*.5,35.5),(0,HEIGHT)],metal)
lathe('Core_Engine',[(CORE_R*.7,6),(CORE_R,8)],dark)
cap('Core_Base_Cap',(CORE_R*.7,6),dark,at_top=False)

# 4 strap-on boosters (Blocks B/V/G/D): tapered cylinders flaring outward,
# tilted so they're wider apart at the base than at the top - the classic
# "flower petal" R-7 look.
BOOSTER_TOP_R=0.85
BOOSTER_BASE_R=1.35
for i in range(4):
    a=math.radians(45+i*90)
    top=Vector((CORE_R*.55*math.cos(a),CORE_R*.55*math.sin(a),26))
    base=Vector((3.1*math.cos(a),3.1*math.sin(a),0))
    axis=(base-top)
    mid=(top+base)/2
    length=axis.length
    obj_top=cylinder('Booster',tuple(top),tuple(base),BOOSTER_TOP_R,metal,32,radius2=BOOSTER_BASE_R)
    # Small engine nozzle box at each booster's base - centred just above
    # z=0 so it doesn't push the model's overall bounds below the base.
    box('Booster_Engine',(base.x,base.y,0.25),(BOOSTER_BASE_R*.7,BOOSTER_BASE_R*.7,0.5),dark)

complete(HERE,'Vostok',HEIGHT,44,camera=(28,-44,0),label='Vostok')
