"""Raptor 2 sea-level visual approximation; deliberately exposed pipework."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from engine_asset import *

profile=[(.65,0),(.648,.10),(.613,.32),(.553,.57),(.475,.83),
         (.386,1.09),(.29,1.34),(.207,1.54),(.179,1.67),(.207,1.79),(.255,1.93)]
bell(profile,dark,0,[(.648,.08),(.59,.42),(.252,1.91)])
for i in range(80):
    a=2*math.pi*i/80
    tube('Upper_Cooling_Channel',[((r+.006)*math.cos(a),(r+.006)*math.sin(a),z)
         for r,z in profile[5:]],.006,bronze,2)
cylinder('Copper_Chamber',(0,0,1.91),(0,0,2.18),.27,bronze,64)
ring('Injector_Flange',.285,2.15,.025,silver)
bolts(.28,2.20,20)
cylinder('Central_Powerhead',(0,0,2.17),(0,0,2.53),.25,steel,48)
pump('Fuel_Turbopump',(.35,.08,2.71),.205,.57,silver)
pump('Oxidizer_Turbopump',(-.34,.19,2.64),.24,.54,steel)
pump('Fuel_Preburner',(.29,-.24,2.40),.115,.33,bronze)
pump('Oxidizer_Preburner',(-.29,-.17,2.37),.12,.36,steel)
tube('Fuel_Hotgas_Duct',[(.30,-.24,2.43),(.52,-.25,2.55),(.57,-.10,2.80),(.35,.08,2.86)],.08,silver)
tube('Oxidizer_Hotgas_Duct',[(-.29,-.17,2.41),(-.55,-.17,2.55),(-.56,.09,2.80),(-.34,.19,2.84)],.095,silver)
tube('Oxidizer_Main_Inlet',[(-.34,.19,2.77),(-.49,.42,2.99),(-.28,.42,3.19)],.115,silver)
cylinder('LOX_Inlet_Flange',(-.28,.42,3.16),(-.28,.42,3.23),.16,steel)
tube('Methane_Main_Inlet',[(.35,.08,2.89),(.57,.24,3.03),(.43,.32,3.20)],.095,silver)
cylinder('Fuel_Inlet_Flange',(.43,.32,3.16),(.43,.32,3.23),.14,steel)
tube('Fuel_Chamber_Feed',[(.36,.02,2.69),(.56,-.04,2.40),(.44,-.22,2.16),(.26,-.13,2.03)],.073,silver)
tube('Oxidizer_Chamber_Feed',[(-.34,.17,2.64),(-.49,-.04,2.41),(-.36,-.25,2.22),(-.20,-.1,2.2)],.087,steel)
tube('Front_Manifold',[(-.44,-.20,2.64),(-.30,-.40,2.78),(.13,-.42,2.8),(.40,-.28,2.64)],.058,silver)
tube('Rear_Return_Loop',[(-.40,.31,2.44),(-.46,.55,2.73),(.0,.59,2.94),(.42,.38,2.72)],.062,steel)
cylinder('Thrust_Mount_Stem',(0,0,2.52),(0,0,3.19),.105,silver)
cylinder('Gimbal_Cap',(0,0,3.16),(0,0,3.29),.205,steel,48)
bolts(.167,3.30,12)
for sign in [-1,1]:
    cylinder('Gimbal_Piston',(sign*.44,0,2.06),(sign*.19,0,2.89),.028,silver)
    cylinder('Gimbal_Sleeve',(sign*.32,0,2.44),(sign*.19,0,2.9),.048,steel)
    box('Valve_Electronics',(sign*.53,-.11,2.48),(.11,.12,.20),black)
    for j in range(4):
        cylinder('Valve_Solenoid',(sign*.49,-.23,2.20+j*.11),
                 (sign*.61,-.23,2.20+j*.11),.034,steel,16)
    for i in range(8):
        tube('Fine_Pipe_Harness',[(sign*.12,-.13-i*.012,3.12),
             (sign*(.23+i*.035),-.31,2.93),(sign*(.36+i*.025),-.37,2.63),
             (sign*(.34+i*.016),-.32,2.31),(sign*.23,-.17,2.14)],
             .0065,black if i%3==0 else silver)
for i in range(8):
    a=2*math.pi*i/8
    tube('Chamber_Service_Line',[((r+.024)*math.cos(a),(r+.024)*math.sin(a),z)
         for r,z in [(.26,2.10),(.28,1.96),(.227,1.76),(.203,1.57),(.30,1.35)]],.009,silver)
complete(HERE,'Raptor2',3.318,4.3,camera=(5,-10,2.1))
