using System;
using UnityEngine;

// STUDY: ==================== STUDY GUIDE: EnginePerformance ====================
// STUDY: WHAT: engine numbers. EngineParameters = one engine's data (vacuum thrust,
// STUDY: Isp, sea-level values, mixture ratio, throttle range). Evaluate() gives
// STUDY: thrust, Isp and mass flow at a throttle and outside air pressure (thrust
// STUDY: falls by pressure x nozzle exit area as the air thickens). Reference(id)
// STUDY: holds the published data for each real engine (Merlin, Raptor, F-1, J-2,
// STUDY: RS-25, RD-107...). SolidMotor: thrust curve of each solid booster.
// STUDY: USED BY: RocketFlightModel.Step (thrust each step), RocketAssemblyController
// STUDY: (delta-v, panel), Autopilot (burn times), RocketPresets.

[Serializable]
public sealed class EngineParameters
{
    public float dryMass, vacuumThrust, vacuumIsp, exitDiameter, mixtureRatio;
    public int nozzleCount=1;
    public bool allowGimbal=true;
    public float minimumThrottle=1;
    // Propellant utilization (PU): the lowest oxidizer/fuel ratio the engine
    // can shift down to (0 = runs only at mixtureRatio). The J-2's PU valve
    // moved between 5.5 and 4.5 during the burn so both tanks of the S-II /
    // S-IVB ran dry together, whatever ratio they had been loaded at.
    public float puMinimumMixture;
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
    /// <summary>
    /// One liquid engine at a throttle setting and ambient pressure (Pa).
    /// The propellant flow is set by the vacuum rating, ṁ = throttle·F_vac/(g₀·Isp_vac);
    /// the air pressing on the nozzle exit takes p·A off the thrust, so the
    /// same flow pushes harder in vacuum than at sea level (and the
    /// effective Isp, F/(g₀·ṁ), rises with altitude). Not valid below the
    /// engine's minimum throttle, or where back-pressure cancels the thrust.
    /// </summary>
    public static Result Evaluate(EngineParameters p,double throttle,double pressure)
    {
        if(p==null || !p.Valid || double.IsNaN(pressure)||double.IsInfinity(pressure)||pressure<0)return default;
        var a=p.nozzleCount*Math.PI*p.exitDiameter*p.exitDiameter/4;
        if(throttle<=0)return new Result{valid=true,area=a};
        if(double.IsNaN(throttle)||double.IsInfinity(throttle)||throttle<p.minimumThrottle-1e-6 || throttle>1)return default;
        var flow=throttle*p.vacuumThrust/(G0*p.vacuumIsp);
        var thrust=throttle*p.vacuumThrust-pressure*a;
        if(thrust<=0)return new Result{area=a};
        return new Result
        {
            valid=true,area=a,thrust=thrust,massFlow=flow,
            isp=thrust/(G0*flow),
            fuelFlow=flow/(1+p.mixtureRatio),
            oxidizerFlow=flow*p.mixtureRatio/(1+p.mixtureRatio),
        };
    }
    /// <summary>
    /// The oxidizer/fuel ratio the engine burns at with these amounts left:
    /// its rated mixtureRatio, or - with a propellant utilization range - the
    /// tanks' own remaining ratio, kept within puMinimumMixture..mixtureRatio,
    /// so whatever was loaded runs out together. (Simplification: total flow,
    /// thrust and Isp stay at the rated figures; the real J-2 gave ~20% less
    /// thrust at 4.5 for a few seconds more Isp.)
    /// </summary>
    public static double BurnMixture(EngineParameters p,double fuel,double oxidizer)
    {
        if(p==null)return 0;
        if(p.puMinimumMixture<=0 || fuel<=0)return p.mixtureRatio;
        return Math.Max(p.puMinimumMixture,Math.Min(p.mixtureRatio,oxidizer/fuel));
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
            case "J2":p=new EngineParameters{puMinimumMixture=4.5f,gimbalRange=7,gimbalRate=8,dryMass=1438,vacuumThrust=1033100,vacuumIsp=421,mixtureRatio=5.5f,fuel="LH2",source="Rocketdyne J-2 (Saturn V S-II / S-IVB)",dataStatus="Published performance / calibrated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,486200,1);return p;
            // Upper-stage engines (no catalog model - their bodies carry them):
            // Merlin Vacuum: 981 kN, Isp 348 s, 3.3 m nozzle, ±5°.
            case "MerlinVac":return new EngineParameters{gimbalRange=5,gimbalRate=10,dryMass=490,vacuumThrust=981000,vacuumIsp=348,exitDiameter=3.3f,mixtureRatio=2.36f,minimumThrottle=.39f,source="SpaceX Merlin Vacuum (Falcon 9 second stage)",dataStatus="Published performance / estimated mass"};
            // Raptor Vacuum: ~2,530 kN, Isp ~380 s, 2.3 m nozzle, fixed (the
            // Ship steers with its sea-level Raptors).
            case "RaptorVac":return new EngineParameters{gimbalRange=0,gimbalRate=0,allowGimbal=false,dryMass=2100,vacuumThrust=2530000,vacuumIsp=380,exitDiameter=2.3f,mixtureRatio=3.6f,minimumThrottle=.4f,fuel="Methane",source="SpaceX Raptor Vacuum (Starship Ship)",dataStatus="Estimated simulation preset"};
            // Aerojet Rocketdyne RL10B-2 (SLS ICPS, Delta IV upper stage):
            // 110.1 kN, Isp 465.5 s with its extendable carbon nozzle.
            case "RL10B2":return new EngineParameters{gimbalRange=4,gimbalRate=8,dryMass=301,vacuumThrust=110100,vacuumIsp=465.5f,exitDiameter=2.15f,mixtureRatio=5.88f,fuel="LH2",source="Aerojet Rocketdyne RL10B-2 (SLS ICPS)",dataStatus="Published performance"};
            // RL10C-1 (Atlas V Centaur): 101.8 kN, Isp 449.7 s.
            case "RL10C1":return new EngineParameters{gimbalRange=4,gimbalRate=8,dryMass=190,vacuumThrust=101800,vacuumIsp=449.7f,exitDiameter=1.45f,mixtureRatio=5.88f,fuel="LH2",source="Aerojet Rocketdyne RL10C-1 (Atlas V Centaur III)",dataStatus="Published performance"};
            // Safran Vulcain 2 (Ariane 5 EPC): 1,390 kN vacuum / 960 kN at
            // sea level, Isp 431 s, not throttled in flight.
            case "Vulcain2":p=new EngineParameters{gimbalRange=6,gimbalRate=10,dryMass=2100,vacuumThrust=1390000,vacuumIsp=431,mixtureRatio=6.1f,fuel="LH2",source="Safran Vulcain 2 (Ariane 5 ECA main stage)",dataStatus="Published performance / calibrated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,960000,1);return p;
            // ArianeGroup HM7B (Ariane 5 ESC-A): 67 kN, Isp 446 s.
            case "HM7B":return new EngineParameters{gimbalRange=4,gimbalRate=6,dryMass=165,vacuumThrust=67000,vacuumIsp=446,exitDiameter=.99f,mixtureRatio=5.0f,fuel="LH2",source="ArianeGroup HM7B (Ariane 5 ESC-A)",dataStatus="Published performance"};
            // RD-0109: Vostok-K Block E. 54.5 kN, Isp 323.5 s; steering nozzles
            // as an equivalent ±3°.
            case "RD0109":return new EngineParameters{gimbalRange=3,gimbalRate=10,dryMass=121,vacuumThrust=54500,vacuumIsp=323.5f,exitDiameter=.5f,mixtureRatio=2.5f,source="Kosberg RD-0109 (Vostok-K Block E)",dataStatus="Published performance / estimated geometry"};
            // Space Shuttle OMS (AJ10-190): 26.7 kN each, Isp 316 s, MMH /
            // N2O4, ±6° gimbal.
            case "OMS":return new EngineParameters{gimbalRange=6,gimbalRate=5,dryMass=118,vacuumThrust=26700,vacuumIsp=316,exitDiameter=1.17f,mixtureRatio=1.65f,fuel="MMH",source="Aerojet AJ10-190 (Shuttle Orbital Maneuvering System)",dataStatus="Published performance"};
            // Rocket Lab Rutherford (Electron first stage, ×9): electric-pump
            // kerosene engine. 311 s at sea level (published); ~24 kN vacuum
            // and 333 s are estimates consistent with Electron's ~200 kN at
            // liftoff (190 kN published, 224 kN peak).
            case "Rutherford":p=new EngineParameters{gimbalRange=5,gimbalRate=15,dryMass=35,vacuumThrust=24000,vacuumIsp=333,mixtureRatio=2.4f,source="Rocket Lab Rutherford (Electron first stage); vacuum figures estimated",dataStatus="Published sea-level Isp / estimated vacuum"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,22400,1);return p;
            // Rutherford Vacuum (Electron second stage): 25.8 kN, Isp 343 s.
            case "RutherfordVac":return new EngineParameters{gimbalRange=5,gimbalRate=15,dryMass=40,vacuumThrust=25800,vacuumIsp=343,exitDiameter=.6f,mixtureRatio=2.4f,source="Rocket Lab Rutherford Vacuum (Electron second stage)",dataStatus="Published performance / estimated geometry"};
            // Aerojet Rocketdyne RS-68A (Delta IV common booster core): the
            // largest hydrogen engine flown, 3,137 kN at sea level / 3,560 kN
            // vacuum, Isp 362 / 412 s, throttles to ~57 %.
            case "RS68A":p=new EngineParameters{gimbalRange=6,gimbalRate=10,dryMass=6747,vacuumThrust=3560000,vacuumIsp=412,mixtureRatio=6f,fuel="LH2",minimumThrottle=.57f,source="Aerojet Rocketdyne RS-68A (Delta IV Heavy)",dataStatus="Published performance / calibrated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,3137000,1);return p;
            // Rocketdyne H-1 (Saturn IB S-IB, ×8): 205,000 lbf = 912 kN at sea
            // level, Isp 263 / 296 s - so ~1,027 kN in vacuum.
            case "H1":p=new EngineParameters{gimbalRange=8,gimbalRate=8,dryMass=1000,vacuumThrust=1027000,vacuumIsp=296,mixtureRatio=2.23f,source="Rocketdyne H-1 (Saturn IB first stage)",dataStatus="Published performance / calibrated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,912000,1);return p;
            // Blue Origin BE-4 (Vulcan first stage, ×2): methane/LOX, 2,446 kN
            // at sea level (4,893 kN for the pair, published); ~2,750 kN and
            // 340 s in vacuum are estimates (302 s at sea level).
            case "BE4":p=new EngineParameters{gimbalRange=6,gimbalRate=10,dryMass=2500,vacuumThrust=2750000,vacuumIsp=340,mixtureRatio=3.6f,fuel="Methane",minimumThrottle=.4f,source="Blue Origin BE-4 (Vulcan); vacuum figures estimated",dataStatus="Published sea-level thrust / estimated Isp and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,2446000,1);return p;
            // RL10C-X (Vulcan's Centaur V, ×2): 106 kN, Isp 453.8 s.
            case "RL10CX":return new EngineParameters{gimbalRange=4,gimbalRate=8,dryMass=190,vacuumThrust=106000,vacuumIsp=453.8f,exitDiameter=1.6f,mixtureRatio=5.88f,fuel="LH2",source="Aerojet Rocketdyne RL10C-X (Vulcan Centaur V)",dataStatus="Published performance"};
            // YF-100 (Long March 5/6/7/8 kerosene engine): 1,200 kN at sea
            // level / 1,340 kN vacuum, Isp 300 / 335 s.
            case "YF100":p=new EngineParameters{gimbalRange=8,gimbalRate=10,dryMass=1900,vacuumThrust=1340000,vacuumIsp=335,mixtureRatio=2.6f,minimumThrottle=.65f,source="AALPT YF-100 (Long March 8A core and boosters)",dataStatus="Published performance / estimated mass and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,1200000,1);return p;
            // YF-75H (Long March 8A second stage, ×2): uprated YF-75D,
            // ~100 kN each, Isp 442.6 s.
            case "YF75H":return new EngineParameters{gimbalRange=4,gimbalRate=8,dryMass=550,vacuumThrust=100000,vacuumIsp=442.6f,exitDiameter=1.4f,mixtureRatio=6f,fuel="LH2",source="AALPT YF-75H (Long March 8A second stage)",dataStatus="Published performance / estimated mass and geometry"};
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
            // SLS five-segment booster: ~16 MN peak vacuum thrust, 631 t of
            // PBAN, 95 t inert, Isp 269 s, 3.87 m nozzle exit; burns ~126 s
            // (separation T+2:12).
            case "SLS_SRB":return new SolidMotor{title="SLS 5-segment SRB",peakVacuumThrust=16000000,vacuumIsp=269,
                exitArea=Math.PI*3.87*3.87/4,propellantMass=631000,inertMass=95000,
                profileTime=new double[]{0,10,25,45,60,80,100,115,124,128},
                profileFraction=new[]{.95,1.0,.97,.80,.78,.88,.82,.6,.25,0},
                source="Northrop Grumman five-segment RSRMV; thrust profile approximated"};
            // Ariane 5 EAP (P241): 240 t of HTPB each, 33 t inert, Isp
            // 274.5 s, ~6.6 MN peak; burns ~130 s (separation T+2:20).
            case "P241":return new SolidMotor{title="Ariane 5 EAP P241",peakVacuumThrust=6600000,vacuumIsp=274.5,
                exitArea=Math.PI*2.99*2.99/4,propellantMass=240000,inertMass=33000,
                profileTime=new double[]{0,5,20,40,60,80,100,115,125,135},
                profileFraction=new[]{.85,1.0,.98,.82,.78,.80,.73,.5,.3,0},
                source="Ariane 5 EAP / P241 booster; thrust profile approximated"};
            // Atlas V AJ-60A: 42.6 t of HTPB each, 4.1 t inert, Isp 279.3 s,
            // burns ~94 s. The peak (1,643 kN vacuum, close to the
            // published 1,688 kN maximum) is set so the profile's area
            // gives the grain's total impulse: 42,630 kg × 279.3 s × g0.
            case "AJ60A":return new SolidMotor{title="Atlas V AJ-60A",peakVacuumThrust=1643000,vacuumIsp=279.3,
                exitArea=Math.PI*1.4*1.4/4,propellantMass=42630,inertMass=4067,
                profileTime=new double[]{0,5,15,40,60,80,88,94},
                profileFraction=new[]{1.0,1.0,.95,.82,.74,.6,.4,0},
                source="Aerojet Rocketdyne AJ-60A; thrust profile approximated"};
            // Northrop Grumman GEM 63XL (Vulcan): 47.9 t of HTPB each, 5.5 t
            // inert, Isp ~279 s, burns 87.3 s, 2,061 kN maximum. The AJ-60A's
            // profile shape over the shorter burn; peak 1,987 kN gives the
            // grain's total impulse (47,853 kg × 279.3 s × g0).
            case "GEM63XL":return new SolidMotor{title="GEM 63XL",peakVacuumThrust=1987000,vacuumIsp=279.3,
                exitArea=Math.PI*1.6*1.6/4,propellantMass=47853,inertMass=5547,
                profileTime=new double[]{0,4.6,13.9,37.1,55.7,74.3,81.7,87.3},
                profileFraction=new[]{1.0,1.0,.95,.82,.74,.6,.4,0},
                source="Northrop Grumman GEM 63XL; thrust profile approximated"};
            default:return null;
        }
    }

    // Exit diameter from a published sea-level / vacuum thrust pair: the
    // difference is one atmosphere on the exit area, F_vac − F_SL = p₀·A.
    private static float CalibratedDiameter(double vacuum,double sea,int count)=>(float)Math.Sqrt(4*(vacuum-sea)/(101325*Math.PI*count));
}
