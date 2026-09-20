using System;
using UnityEngine;

[Serializable]
public sealed class EngineParameters
{
    public float dryMass, vacuumThrust, vacuumIsp, exitDiameter, mixtureRatio;
    public int nozzleCount=1;
    public bool allowGimbal=true;
    public float minimumThrottle=1;
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
            case "F1":p=new EngineParameters{dryMass=8444.1f,vacuumThrust=7776381f,vacuumIsp=304.8f,mixtureRatio=2.270f,source="NASA F-1 nominal reference; nozzle area calibrated from paired thrust",dataStatus="Published performance / estimated exit area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,6770200,1);return p;
            case "RD180":p=new EngineParameters{dryMass=5480,vacuumThrust=4148213f,vacuumIsp=339,mixtureRatio=2.72f,nozzleCount=2,minimumThrottle=.47f,source="Glavkosmos RD-180; estimated O/F and calibrated exit area",dataStatus="Published performance / estimated O/F and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,3824594,2);return p;
            case "RS25":p=new EngineParameters{dryMass=3526.7f,vacuumThrust=2278824,vacuumIsp=452,mixtureRatio=6,fuel="LH2",minimumThrottle=.67f/1.09f,source="L3Harris RS-25 at 109% RPL; estimated throttle limit / calibrated area",dataStatus="Published reference / estimated limits and area"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,1859320,1);return p;
            case "Merlin1D":return new EngineParameters{dryMass=470,vacuumThrust=914000,vacuumIsp=311,exitDiameter=CalibratedDiameter(914000,845000,1),mixtureRatio=2.36f,minimumThrottle=.4f,source="Experimental preset anchored to SpaceX sea-level thrust; other inputs assumed",dataStatus="Estimated simulation preset"};
            case "Raptor2":return new EngineParameters{dryMass=1630,vacuumThrust=2394500,vacuumIsp=347,exitDiameter=1.3f,mixtureRatio=3.6f,minimumThrottle=.4f,fuel="Methane",source="Experimental DLR-style sea-level baseline; mass, Isp and throttle assumed",dataStatus="Estimated simulation preset"};
            // RD-107 (R-7/Vostok strap-on/core family). Real hardware has 4
            // main chambers plus 2 small vernier thrusters sharing one
            // turbopump; verniers are omitted and nozzleCount=4 covers only
            // the main chambers, the same simplification already used for
            // RD-180's twin chambers above. No imported model exists for
            // this one - see ProceduralEngine/EngineCatalogSetup.
            case "RD107":p=new EngineParameters{dryMass=1250,vacuumThrust=1019700,vacuumIsp=315.6f,mixtureRatio=2.6f,nozzleCount=4,source="Astronautix RD-107 reference; verniers omitted",dataStatus="Published performance / simplified nozzle count"};p.exitDiameter=CalibratedDiameter(p.vacuumThrust,838500,4);return p;
            default:return null;
        }
    }
    private static float CalibratedDiameter(double vacuum,double sea,int count)=>(float)Math.Sqrt(4*(vacuum-sea)/(101325*Math.PI*count));
}
