using System;
using UnityEngine;

[Serializable]
public sealed class EngineParameters
{
    public float dryMass, vacuumThrust, vacuumIsp, exitDiameter, mixtureRatio;
    public int nozzleCount=1;
    public bool allowGimbal=true;
    public float minimumThrottle=1;
    // Thrust vector control: how far the engine can swivel (degrees) and how
    // fast its actuators move it (degrees per second).
    public float gimbalRange=10f, gimbalRate=15f;
    public string fuel="RP-1", optimization="Sea-level", dataStatus="Estimated", source;
    public EngineParameters Copy() => JsonUtility.FromJson<EngineParameters>(JsonUtility.ToJson(this));
    public bool Valid => Positive(dryMass)&&Positive(vacuumThrust)&&Positive(vacuumIsp)&&Positive(exitDiameter)&&Positive(mixtureRatio)&&nozzleCount>0&&nozzleCount<=8&&minimumThrottle>0&&minimumThrottle<=1;
    private static bool Positive(float x)=>x>0 && !float.IsNaN(x) && !float.IsInfinity(x);
}

public static class EnginePerformance
{
    public const double G0=9.80665;
    public struct Result
    {
        public double thrust, massFlow, fuelFlow, oxidizerFlow, isp, area;
        public bool valid;
    }
    public static Result Evaluate(EngineParameters p,double throttle,double pressure)
    {
        if(p==null || !p.Valid || double.IsNaN(pressure)||double.IsInfinity(pressure)||pressure<0)return default;
        var a=p.nozzleCount*Math.PI*p.exitDiameter*p.exitDiameter/4;
        if(throttle<=0)return new Result{valid=true,area=a};
        if(double.IsNaN(throttle)||double.IsInfinity(throttle)||throttle<p.minimumThrottle-1e-6 || throttle>1)return default;
        var flow=throttle*p.vacuumThrust/(G0*p.vacuumIsp);
        var thrust=throttle*p.vacuumThrust-pressure*a;
        if(thrust<=0)return new Result{area=a};
        return new Result{valid=true,area=a,thrust=thrust,massFlow=flow,isp=thrust/(G0*flow),fuelFlow=flow/(1+p.mixtureRatio),oxidizerFlow=flow*p.mixtureRatio/(1+p.mixtureRatio)};
    }
    public static EngineParameters Reference(string id)
    {
        EngineParameters p;
        switch(id)
        {
            // Gimbal figures: F-1 ±6° at ~5°/s (Saturn V hydraulic actuators);
            // RD-180 ±8°; RS-25 ±10.5°; Merlin 1D ±5°; Raptor ±15°. The R-7's
            // RD-107/108 main chambers are fixed - it steers with small
            // swivelling vernier chambers, here an equivalent ±2.5° whole-
            // engine tilt.
            case "F1":p=new EngineParameters{gimbalRange=6,gimbalRate=5,dryMass=8444.1f,vacuumThrust=7776381f,vacuumIsp=304.8f,mixtureRatio=2.270f,source="NASA F-1 nominal reference; nozzle area calibrated from paired thrust",dataStatus="Published performance / estimated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,6770200,1);return p;
            case "RD180":p=new EngineParameters{gimbalRange=8,gimbalRate=10,dryMass=5480,vacuumThrust=4148213f,vacuumIsp=339,mixtureRatio=2.72f,nozzleCount=2,minimumThrottle=.47f,source="Glavkosmos RD-180; estimated O/F and calibrated exit area",dataStatus="Published performance / estimated O/F and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,3824594,2);return p;
            case "RS25":p=new EngineParameters{gimbalRange=10.5f,gimbalRate=10,dryMass=3526.7f,vacuumThrust=2278824,vacuumIsp=452,mixtureRatio=6,fuel="LH2",minimumThrottle=.67f/1.09f,source="L3Harris RS-25 at 109% RPL; estimated throttle limit / calibrated area",dataStatus="Published reference / estimated limits and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,1859320,1);return p;
            case "Merlin1D":return new EngineParameters{gimbalRange=5,gimbalRate=15,dryMass=470,vacuumThrust=914000,vacuumIsp=311,exitDiameter=CalibratedDiameter(914000,845000,1),mixtureRatio=2.36f,minimumThrottle=.4f,source="Experimental preset anchored to SpaceX sea-level thrust; other inputs assumed",dataStatus="Estimated simulation preset"};
            case "Raptor2":return new EngineParameters{gimbalRange=15,gimbalRate=20,dryMass=1630,vacuumThrust=2394500,vacuumIsp=347,exitDiameter=1.3f,mixtureRatio=3.6f,minimumThrottle=.4f,fuel="Methane",source="Experimental DLR-style sea-level baseline; mass, Isp and throttle assumed",dataStatus="Estimated simulation preset"};
            // RD-107 (R-7/Vostok strap-on/core family). Real hardware has 4
            // main chambers plus 2 small vernier thrusters sharing one
            // turbopump; verniers are omitted and nozzleCount=4 covers only
            // the main chambers, the same simplification already used for
            // RD-180's twin chambers above. No imported model exists for
            // this one - see ProceduralEngine/EngineCatalogSetup.
            // Vostok-era 8D74K: 1,000 kN vacuum / 821 kN sea level.
            case "RD107":p=new EngineParameters{gimbalRange=2.5f,gimbalRate=15,dryMass=1155,vacuumThrust=1000000,vacuumIsp=313,mixtureRatio=2.47f,nozzleCount=4,source="RD-107 8D74K (Vostok-K strap-ons); verniers omitted",dataStatus="Published performance / simplified nozzle count"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,821000,4);return p;
            // RD-108 8D75K, the R-7 core (Block A) sustainer: 941 kN vacuum /
            // 745 kN sea level. Same four-chamber layout as RD-107, with four
            // verniers (omitted here). Used as a preset's core engine only -
            // it has no catalog model of its own.
            case "RD108":p=new EngineParameters{gimbalRange=2.5f,gimbalRate=15,dryMass=1278,vacuumThrust=941000,vacuumIsp=315,mixtureRatio=2.39f,nozzleCount=4,source="RD-108 8D75K (Vostok-K core); verniers omitted",dataStatus="Published performance / simplified nozzle count"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,745000,4);return p;
            // J-2: Saturn V S-II (×5) and S-IVB (×1) engine. 1,033 kN in
            // vacuum, 486 kN at sea level, Isp 421 s, LOX/LH2 at O/F 5.5,
            // ±7° gimbal; no throttling (restartable on the S-IVB).
            case "J2":p=new EngineParameters{gimbalRange=7,gimbalRate=8,dryMass=1438,vacuumThrust=1033100,vacuumIsp=421,mixtureRatio=5.5f,fuel="LH2",source="Rocketdyne J-2 (Saturn V S-II / S-IVB)",dataStatus="Published performance / calibrated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,486200,1);return p;
            // Upper-stage engines (no catalog model - their bodies carry them):
            // Merlin Vacuum: 981 kN, Isp 348 s, 3.3 m nozzle, ±5°.
            case "MerlinVac":return new EngineParameters{gimbalRange=5,gimbalRate=10,dryMass=490,vacuumThrust=981000,vacuumIsp=348,exitDiameter=3.3f,mixtureRatio=2.36f,minimumThrottle=.39f,source="SpaceX Merlin Vacuum (Falcon 9 second stage)",dataStatus="Published performance / estimated mass"};
            // Raptor Vacuum: ~2,530 kN, Isp ~380 s, 2.3 m nozzle, fixed (the
            // Ship steers with its sea-level Raptors).
            case "RaptorVac":return new EngineParameters{gimbalRange=0,gimbalRate=0,allowGimbal=false,dryMass=2100,vacuumThrust=2530000,vacuumIsp=380,exitDiameter=2.3f,mixtureRatio=3.6f,minimumThrottle=.4f,fuel="Methane",source="SpaceX Raptor Vacuum (Starship Ship)",dataStatus="Estimated simulation preset"};
            // RD-0109: Vostok-K Block E. 54.5 kN, Isp 323.5 s; steering nozzles
            // as an equivalent ±3°.
            case "RD0109":return new EngineParameters{gimbalRange=3,gimbalRate=10,dryMass=121,vacuumThrust=54500,vacuumIsp=323.5f,exitDiameter=.5f,mixtureRatio=2.5f,source="Kosberg RD-0109 (Vostok-K Block E)",dataStatus="Published performance / estimated geometry"};
            // Space Shuttle OMS (AJ10-190): 26.7 kN each, Isp 316 s, MMH /
            // N2O4, ±6° gimbal.
            case "OMS":return new EngineParameters{gimbalRange=6,gimbalRate=5,dryMass=118,vacuumThrust=26700,vacuumIsp=316,exitDiameter=1.17f,mixtureRatio=1.65f,fuel="MMH",source="Aerojet AJ10-190 (Shuttle Orbital Maneuvering System)",dataStatus="Published performance"};
            default:return null;
        }
    }
    /// <summary>
    /// A solid rocket motor: its own propellant grain, lit once and burned to
    /// depletion - no throttle, no shutdown. Thrust follows the grain's
    /// designed burn profile (thrustProfile, a fraction of peak vacuum thrust
    /// against seconds since ignition), minus back-pressure on the nozzle exit.
    /// </summary>
    public sealed class SolidMotor
    {
        public string title;
        public double peakVacuumThrust, vacuumIsp, exitArea, propellantMass, inertMass;
        public double[] profileTime, profileFraction;
        public string source;

        public double Fraction(double seconds)
        {
            if(seconds<=profileTime[0])return profileFraction[0];
            for(var i=1;i<profileTime.Length;i++)
                if(seconds<=profileTime[i])
                    return profileFraction[i-1]+(profileFraction[i]-profileFraction[i-1])*(seconds-profileTime[i-1])/(profileTime[i]-profileTime[i-1]);
            return profileFraction[^1];
        }
        public double BurnTime => profileTime[^1];
        public double MassFlow(double seconds) => Fraction(seconds)*peakVacuumThrust/(G0*vacuumIsp);
        public double Thrust(double seconds,double pressure) => Math.Max(0,Fraction(seconds)*peakVacuumThrust-pressure*exitArea);
    }

    public static SolidMotor Solid(string id)
    {
        switch(id)
        {
            // Space Shuttle SRB (RSRM): ~12.5 MN each at sea level at liftoff,
            // 503 t of PBAN propellant, 87 t inert, Isp 268.6 s vacuum, 3.8 m
            // nozzle exit. The grain is shaped to drop thrust to ~72% through
            // Max Q (~50-65 s), recover, then tail off at ~124 s.
            case "SRB":return new SolidMotor{title="Shuttle SRB",peakVacuumThrust=13650000,vacuumIsp=268.6,
                exitArea=Math.PI*3.8*3.8/4,propellantMass=503000,inertMass=87000,
                profileTime=new double[]{0,20,50,65,80,100,110,118,124},
                profileFraction=new[]{1.0,1.03,.74,.72,.82,.78,.6,.3,0},
                source="Space Shuttle RSRM; thrust profile approximated from published thrust-time curve"};
            default:return null;
        }
    }

    private static float CalibratedDiameter(double vacuum,double sea,int count)=>(float)Math.Sqrt(4*(vacuum-sea)/(101325*Math.PI*count));
}
