"""Space Shuttle visual approximation: the REAL side-mounted arrangement -
an orange external tank in the centre, the white orbiter mounted on its
side, and two white SRBs mounted on the other two sides - not a vertical
stack. This is what every other preset in this project is NOT (they're all
genuine single-stack rockets), so it gets a structurally different model
instead of reusing the stack approach."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from rocket_asset import *

HEIGHT=56.0
TANK_R=4.2

orange=material('SpaceShuttle_Tank',(.72,.42,.24),.1,.55)
white=material('SpaceShuttle_White',(.91,.91,.92),.15,.35)
black=material('SpaceShuttle_Black',(.03,.03,.035),.2,.45)
srb_orange=material('SpaceShuttle_SRB',(.85,.85,.86),.1,.4)

# External Tank: the tall orange centrepiece, rounded nose.
lathe('Tank_Body',[(TANK_R,6),(TANK_R,46)],orange)
lathe('Tank_Nose',[(TANK_R,46),(TANK_R*.85,49),(TANK_R*.55,51.5),(TANK_R*.2,53.5),(0,56)],orange)
cap('Tank_Base',(TANK_R*.85,0),black,at_top=False)
lathe('Tank_Skirt',[(TANK_R*.85,0),(TANK_R,6)],black)

# Orbiter: mounted on the +X side of the tank, roughly parallel, shorter,
# with a distinct nose and two small delta-wing stubs near its base.
ORB_X=TANK_R+2.1
orbiter=[(1.55,4),(1.55,32),(1.35,36),(1.0,39),(.55,41.5),(0,43.5)]
lathe('Orbiter_Body',[(r,z+6) for r,z in orbiter],white)
cap('Orbiter_Base',(1.55,10),black,at_top=False)
# Move the whole orbiter stack onto the tank's side; lathe builds around
# the world Z axis, so translate the just-built objects in X.
for obj in parts[-2:]:
    obj.location.x+=ORB_X
box('Orbiter_Wing',(ORB_X-.3,0,10),(1.7,4.4,.35),white)
box('Orbiter_Tail',(ORB_X-.1,0,15.5),(.18,.18,2.6),black)

# Two SRBs: mounted on the other two sides (+Y and -Y), tapered nose cones.
for sign in (1,-1):
    y=sign*(TANK_R+2.6)
    lathe('SRB_Body',[(1.35,3,) ,(1.35,44)],srb_orange)
    obj=parts[-1]
    obj.location.y=y
    lathe('SRB_Nose',[(1.35,44),(1.1,47),(.7,49.5),(0,52)],srb_orange)
    obj2=parts[-1]
    obj2.location.y=y
    cap('SRB_Base',(1.0,0),black,at_top=False)
    obj3=parts[-1]
    obj3.location.y=y
    box('SRB_AftSkirt',(0,y,1.5),(1.5,1.5,3),black)

complete(HERE,'SpaceShuttle',HEIGHT,58,camera=(0,-95,4),label='Space Shuttle')
