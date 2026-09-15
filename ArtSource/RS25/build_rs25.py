"""RS-25 visual approximation. Run with Blender --background --python."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from engine_asset import *

profile=[(1.18,0),(1.17,.12),(1.11,.4),(1.01,.72),(.90,1.03),
         (.76,1.36),(.60,1.69),(.43,1.98),(.29,2.21),(.235,2.36),
         (.26,2.50),(.34,2.65),(.36,2.78)]
bell(profile,dark,96,[(1.16,.15),(1.10,.44),(.99,.77),(.87,1.11),(.74,1.4),(.59,1.72)])
for i in range(10):
    a=2*math.pi*i/10
    tube('Nozzle_External_Line',[((r+.045)*math.cos(a),(r+.045)*math.sin(a),z)
         for r,z in profile[:10]],.025,silver)
cylinder('Powerhead',(0,0,2.73),(0,0,3.19),.40,steel,64)
ring('Chamber_Manifold',.41,2.80,.055,silver)
pump('HP_Fuel_Pump',(.61,-.04,3.30),.25,.62)
pump('HP_Oxidizer_Pump',(-.52,.24,3.19),.23,.54)
pump('LP_Fuel_Pump',(.74,.10,3.83),.19,.31)
pump('LP_Oxidizer_Pump',(-.64,.31,3.70),.17,.31)
cylinder('Gimbal_Stem',(0,0,3.14),(0,0,4.0),.135,silver)
cylinder('Gimbal_Mount',(0,0,3.96),(0,0,4.15),.26,steel,48)
bolts(.22,4.16,12)
tube('Large_Hydrogen_Duct',[(.72,.1,3.90),(1.0,-.02,3.55),(.94,-.18,3.09),(.47,-.25,2.96)],.14,silver)
tube('Oxidizer_Sweep',[(-.65,.31,3.77),(-.9,.1,3.46),(-.70,-.18,3.15),(-.36,-.16,2.93)],.11,silver)
tube('Upper_Crossover',[(-.55,.35,3.63),(-.34,.52,3.90),(.29,.53,3.91),(.70,.25,3.72)],.09,steel)
tube('Chamber_Return',[(.31,0,2.86),(.46,-.42,3.14),(.37,-.47,3.54),(-.32,-.36,3.46),(-.5,.2,3.21)],.072,silver)
for x in [-.24,.24]:
    pump('Preburner',(x,.39,3.48),.115,.39,bronze)
    tube('Preburner_Duct',[(x,.39,3.51),(x,.20,3.64),(x,-.12,3.61),(x,-.22,3.09)],.055,steel)
box('Engine_Controller',(-.59,-.45,2.96),(.32,.23,.43),silver)
for i in range(4):
    for j in range(3):
        cylinder('Controller_Connector',(-.70+i*.065,-.58,2.83+j*.11),
                 (-.70+i*.065,-.595,2.83+j*.11),.019,black,12)
for sign in [-1,1]:
    cylinder('Gimbal_Brace',(sign*.62,.05,3.12),(sign*.11,.03,3.98),.043,steel)
    for i in range(6):
        tube('Control_Line',[(sign*.22,-.21-i*.018,3.95),(sign*(.38+i*.026),-.34,3.64),
             (sign*(.43+i*.026),-.36,3.24),(sign*.36,-.19,2.89)],.009,silver)
complete(HERE,'RS25',4.178,5.45)
