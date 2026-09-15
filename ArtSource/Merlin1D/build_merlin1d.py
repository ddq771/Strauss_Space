"""Merlin 1D sea-level visual approximation for Unity."""
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
from engine_asset import *

profile=[(.46,0),(.457,.09),(.426,.30),(.38,.56),(.323,.80),
         (.254,1.05),(.185,1.28),(.133,1.45),(.13,1.56),(.167,1.68),(.18,1.83)]
bell(profile,dark,72,[(.456,.08),(.37,.60),(.25,1.07),(.18,1.82)])
cylinder('Chamber_Jacket',(0,0,1.79),(0,0,2.05),.20,bronze,48)
ring('Injector_Flange',.216,2.05,.027,silver)
bolts(.207,2.08,16)
cylinder('Gimbal_Shaft',(0,0,2.05),(0,0,2.53),.085,silver)
cylinder('Gimbal_Head',(0,0,2.49),(0,0,2.77),.14,steel)
cylinder('Mounting_Plate',(0,0,2.76),(0,0,2.86),.235,steel,48)
bolts(.186,2.87,12)
pump('Single_Turbopump',(.28,.03,2.28),.18,.39,silver)
cylinder('Pump_Transverse_Housing',(.28,-.12,2.28),(.28,.27,2.28),.21,steel,48)
ring('Pump_End_Flange',.21,2.28,.024,silver,(.28,-.12),rotation=(math.pi/2,0,0))
pump('Gas_Generator',(-.23,.15,2.09),.085,.32,steel)
tube('Fuel_Inlet',[(.27,.1,2.35),(.48,.1,2.58),(.43,.09,2.75)],.075,silver)
cylinder('Fuel_Inlet_Flange',(.43,.09,2.72),(.43,.09,2.78),.114,steel)
tube('Oxidizer_Inlet',[(-.06,.05,2.04),(-.24,.31,2.31),(-.27,.28,2.64),(-.12,.21,2.72)],.095,silver)
ring('Oxidizer_Flange',.125,2.72,.020,steel,(-.12,.21))
tube('Pump_Chamber_Line',[(.29,-.12,2.27),(.34,-.24,2.02),(.20,-.16,1.86)],.055,silver)
tube('Return_Pipe',[(-.14,0,1.91),(-.28,-.1,2.12),(-.13,-.23,2.42),(.17,-.18,2.48)],.036,steel)
# A visible side exhaust is a distinguishing feature of this approximation.
tube('Turbine_Exhaust_Bend',[(-.23,.15,2.14),(-.43,.11,1.97),(-.47,.08,1.66),(-.48,.07,1.42)],.075,dark)
side=lathe('Turbine_Exhaust_Outlet',[(.09,1.11),(.076,1.40),(.063,1.40),(.076,1.11)],dark,48,True)
side.location=(-.48,.07,0)
for sign in [-1,1]:
    cylinder('Thrust_Brace',(sign*.19,.04,1.98),(sign*.105,.03,2.73),.023,steel)
    cylinder('Actuator_Sleeve',(sign*.21,-.08,2.10),(sign*.16,-.07,2.49),.041,silver)
    for i in range(4):
        tube('Control_Wire',[(sign*.12,-.12-i*.012,2.76),(sign*(.21+i*.02),-.24,2.46),
             (sign*(.23+i*.02),-.20,2.13),(sign*.16,-.08,1.91)],.006,black)
box('Control_Module',(.32,-.17,2.0),(.15,.10,.20),black)
for i in range(6):
    a=2*math.pi*i/6
    tube('Jacket_Service_Line',[((r+.018)*math.cos(a),(r+.018)*math.sin(a),z)
         for r,z in [(.18,1.82),(.146,1.5),(.26,1.06),(.39,.54)]],.009,silver)
complete(HERE,'Merlin1D',2.888,3.75,camera=(4,-9,1.8))
