using System;
using UnityEngine;

[RequireComponent(typeof(RocketAssemblyController))]
public sealed class RocketFlightModel : MonoBehaviour
{
    public const float OxygenDensity=1141f;
    [SerializeField] private float fuelRemaining,oxidizerRemaining;
    [SerializeField] private bool tanksInitialized;
    [SerializeField] private string fuelType="RP-1";
    [SerializeField] private float initialFill=1;
    [Tooltip("Drag coefficient used to slow the rocket through the atmosphere. " +
             "Tuned for a felt, gradual slowdown on ascent/descent rather than " +
             "to match a real vehicle's measured Cd.")]
    [SerializeField] private float dragCoefficient=0.5f;
    // RocketPreset selection sets this per real vehicle (Starship's blunt
    // hull drags far more than Falcon 9's slender one); the sandbox default
    // above is used for anything built by hand.
    public void SetDragCoefficient(float value)=>dragCoefficient=Mathf.Max(0.01f,value);

    // Solid rocket boosters (a preset's strap-ons, e.g. the Shuttle's SRBs):
    // their own grain, separate from the liquid tanks; lit at liftoff and
    // burned to depletion along the motor's thrust profile, whatever the
    // liquid engines and throttle are doing. The spent casings stay attached
    // (no staging yet).
    [SerializeField] private string solidId;
    [SerializeField] private int solidCount;
    [SerializeField] private float solidPropellant;
    [SerializeField] private float solidBurnTime;
    private EnginePerformance.SolidMotor solidMotor;
    // A preset's real non-propellant mass (kg): first-stage structure and
    // engines plus the fully fuelled upper stages, payload and fairing - so
    // liftoff weight matches the real vehicle. 0 = sandbox estimate from
    // the parts (structure + tank shells + engines + frame).
    [SerializeField] private float presetDryMass;
    public void SetPresetDryMass(float kilograms){Bind();if(rocket.Launched)return;presetDryMass=Mathf.Max(0,kilograms);UpdateMass();}
    public int SolidBoosterCount=>solidCount;
    public float SolidPropellant=>solidPropellant;
    public bool SolidBurning=>solidCount>0 && solidPropellant>0 && rocket!=null && rocket.Launched && !crashed;
    public string SolidTitle=>Solid?.title;
    private EnginePerformance.SolidMotor Solid=>solidMotor??=string.IsNullOrEmpty(solidId)?null:EnginePerformance.Solid(solidId);
    public void SetSolidBoosters(string id,int count)
    {
        Bind();if(rocket.Launched)return;
        solidId=id;solidMotor=null;solidCount=Solid!=null?Mathf.Max(0,count):0;
        Refill();
    }
    private void UpdateSolidThrust()
    {
        // Before liftoff this reads the ignition thrust, so the TWR shown on
        // the pad includes the boosters.
        SolidThrust=solidCount>0 && solidPropellant>0 && Solid!=null ? solidCount*Solid.Thrust(solidBurnTime,Pressure) : 0;
    }
    private void BurnSolids(Rigidbody body,float dt)
    {
        if(!SolidBurning || Solid==null){SolidThrust=0;return;}
        var flow=solidCount*Solid.MassFlow(solidBurnTime);
        var used=Math.Min(solidPropellant,flow*dt);
        var fraction=flow>0?used/(flow*dt):0;
        UpdateSolidThrust();
        var direction=assembly.InstalledCount>0?assembly.ResultantThrustDirection:transform.up;
        var basePoint=transform.TransformPoint(Vector3.up*rocket.BodyBaseLocalY);
        body.AddForceAtPosition(direction*(float)(SolidThrust*fraction*PlanetBody.WorldUnitsPerMeter),basePoint,ForceMode.Force);
        solidPropellant=Mathf.Max(0,solidPropellant-(float)used);
        solidBurnTime+=dt;
        if(solidPropellant<=0)SolidThrust=0;
        UpdateMass();
    }
    private RocketAssemblyController assembly;
    private Rocket rocket;
    private PlanetBody planet;
    [SerializeField] private bool crashed;
    private Vector3 previousPosition;
    private bool trackingImpact;
    private readonly System.Collections.Generic.List<Renderer> impactHidden=new System.Collections.Generic.List<Renderer>();
    public double HorizontalSpeed=>Math.Sqrt(Math.Max(0,Speed*Speed-VerticalSpeed*VerticalSpeed));
    public string Status {get;private set;}="Ready";
    public bool Crashed=>crashed;
    public float FuelRemaining=>fuelRemaining;
    public float OxidizerRemaining=>oxidizerRemaining;
    public string FuelType=>fuelType;
    public float Fill=>initialFill;
    public float FuelDensity=>fuelType=="LH2"?70.85f:fuelType=="Methane"?422.6f:810f;
    // Height of the rocket root above the spherical planet surface, in metres.
    // Evaluated on demand so telemetry also updates while coasting.
    public double Altitude
    {
        get
        {
            if(planet==null)return 0;
            var position=transform.position;var center=planet.transform.position;
            double x=(double)position.x-center.x,y=(double)position.y-center.y,z=(double)position.z-center.z;
            return Math.Max(0,Math.Sqrt(x*x+y*y+z*z)/PlanetBody.WorldUnitsPerMeter-planet.Radius);
        }
    }
    public double Pressure {get;private set;}
    public double AirTemperature {get;private set;}=288.15;
    public double AirDensity {get;private set;}
    public double SpeedOfSound {get;private set;}=340.3;
    public double Mach=>SpeedOfSound>0?Speed/SpeedOfSound:0;
    // dragCoefficient scaled for the current Mach number (see MachDragFactor).
    public double CurrentDragCoefficient=>dragCoefficient*MachDragFactor(Mach);
    public double Drag {get;private set;}
    // Liquid engines' thrust (set by Prepare) plus any burning solid boosters.
    public double LiquidThrust {get;private set;}
    public double SolidThrust {get;private set;}
    public double Thrust=>LiquidThrust+SolidThrust;
    public double FuelFlow {get;private set;}
    public double OxidizerFlow {get;private set;}
    public double DryMass {get;private set;}
    public double TotalMass=>DryMass+fuelRemaining+oxidizerRemaining+solidPropellant;
    public double LocalGravity=>planet!=null?planet.GetGravityAcceleration(transform.position).magnitude/PlanetBody.WorldUnitsPerMeter:0;
    public double TWR=>TotalMass>0 && LocalGravity>0?Thrust/(TotalMass*LocalGravity):0;
    /// <summary>Velocity relative to the ground (m/s, world axes) - from the
    /// orbit propagator while time-warping on rails, when the body is kinematic.</summary>
    public Vector3 GroundVelocity=>TimeWarp.TryGetRailsVelocity(out var rails)?rails:GetComponent<Rigidbody>().linearVelocity/PlanetBody.WorldUnitsPerMeter;
    public double Speed=>GroundVelocity.magnitude;
    // Signed radial velocity: independent of the rocket's orientation.
    public double VerticalSpeed
    {
        get
        {
            if(planet==null)return 0;
            var radial=(transform.position-planet.transform.position).normalized;
            return Vector3.Dot(GroundVelocity,radial);
        }
    }
    private void Bind(){assembly??=GetComponent<RocketAssemblyController>();rocket??=GetComponent<Rocket>();planet??=FindFirstObjectByType<PlanetBody>();}
    private void OnEnable(){Bind();}
    public void Configure(string fuel,float fill)
    {
        Bind();if(rocket.Launched)return;fuelType=fuel;initialFill=Mathf.Clamp01(fill);
    }
    public void SetFuel(string fuel)
    {
        Bind();if(rocket.Launched)return;
        fuelType=fuel;Refill();
    }
    public void SetFill(float fraction){Bind();if(rocket.Launched)return;initialFill=Mathf.Clamp01(fraction);Refill();}
    public void Refill()
    {
        crashed=false;trackingImpact=false;
        foreach(var surface in impactHidden)if(surface!=null)surface.enabled=true;
        impactHidden.Clear();
        Bind();fuelRemaining=(assembly.FuelTank!=null?assembly.FuelTank.Capacity:0)*FuelDensity*initialFill;
        oxidizerRemaining=(assembly.OxidizerTank!=null?assembly.OxidizerTank.Capacity:0)*OxygenDensity*initialFill;
        solidPropellant=Solid!=null?(float)(solidCount*Solid.propellantMass):0;solidBurnTime=0;
        tanksInitialized=true;UpdateMass();
    }
    public void AssemblyChanged()
    {
        Bind();if(!rocket.Launched)Refill();else UpdateMass();
    }
    public void UpdateMass()
    {
        Bind();
        // Structural base excludes components. Tank/frame estimates are explicit simulator assumptions.
        if(presetDryMass>0)DryMass=presetDryMass;
        else
        {
            DryMass=rocket.StructuralMass+(assembly.FrameInstalled?120+50*(assembly.SocketCount-1):0);
            foreach(var t in new[]{assembly.FuelTank,assembly.OxidizerTank})
                if(t!=null)DryMass+=15*(Math.PI*t.Diameter*t.Height+Math.PI*t.Diameter*t.Diameter*.5);
            for(var i=0;i<assembly.SocketCount;i++)
            {var p=assembly.GetParameters(i);if(p!=null)DryMass+=p.dryMass;}
        }
        if(Solid!=null)DryMass+=solidCount*Solid.inertMass;
        var body=GetComponent<Rigidbody>();body.mass=(float)Math.Max(.001,TotalMass);
        var weighted=Vector3.zero;
        var parts=presetDryMass<=0; // preset dry mass sits at the body's centre (local origin)
        var frameMass=parts&&assembly.FrameInstalled?120+50*(assembly.SocketCount-1):0;
        weighted+=Vector3.up*rocket.AssemblyMountLocalY*frameMass;
        foreach(var t in new[]{assembly.FuelTank,assembly.OxidizerTank})
        {
            if(t==null)continue;
            var shellMass=parts?15*(Math.PI*t.Diameter*t.Height+Math.PI*t.Diameter*t.Diameter*.5):0;
            var liquid=t==assembly.FuelTank?fuelRemaining:oxidizerRemaining;
            var center=transform.InverseTransformPoint(t.transform.TransformPoint(Vector3.up*t.Height*.5f));
            weighted+=center*(float)(shellMass+liquid);
        }
        for(var i=0;parts && i<assembly.SocketCount;i++)
        {
            var p=assembly.GetParameters(i);if(p==null)continue;
            var entry=assembly.FindEngine(assembly.GetEngineId(i));
            var center=assembly.GetSocketPosition(i)-assembly.GetThrustDirection(i)*entry.height*.5f*PlanetBody.WorldUnitsPerMeter;
            weighted+=transform.InverseTransformPoint(center)*p.dryMass;
        }
        body.centerOfMass=weighted/(float)TotalMass;
        var capsule=GetComponent<CapsuleCollider>();
        if(capsule!=null)
        {
            capsule.contactOffset=.00002f;
            // Conservative cylindrical inertia approximation; geometry is in world length units.
            var r=capsule.radius;var h=capsule.height;var m=body.mass;
            body.inertiaTensor=new Vector3(m*(3*r*r+h*h)/12,m*r*r*.5f,m*(3*r*r+h*h)/12);
            body.inertiaTensorRotation=Quaternion.identity;
        }
    }
    // US Standard Atmosphere 1976 (StandardAtmosphere): pressure, real
    // local temperature, density and speed of sound at the current altitude.
    // Called every physics step (not just while the engine is running) so
    // Pressure/Drag telemetry stays live while coasting or falling, too -
    // and engine thrust uses this Pressure for nozzle back-pressure.
    private void UpdateAtmosphere()
    {
        if(planet==null){Pressure=0;AirDensity=0;return;}
        var altitude=Altitude;
        Pressure=StandardAtmosphere.Pressure(altitude);
        AirTemperature=StandardAtmosphere.TemperatureKelvin(altitude);
        AirDensity=StandardAtmosphere.Density(Pressure,AirTemperature);
        SpeedOfSound=StandardAtmosphere.SpeedOfSound(AirTemperature);
    }

    // How a rocket's drag coefficient changes with Mach number, relative to
    // its subsonic value: flat until ~0.6, a sharp rise through the
    // transonic region as shock waves form (peaking ~1.9x just past Mach 1),
    // then easing back off as the flow goes fully supersonic. A typical
    // slender-body launcher curve; points are linearly interpolated.
    private static readonly double[] MachPoints={0,0.6,0.8,0.9,0.95,1.0,1.05,1.1,1.2,1.5,2.0,3.0,5.0,10.0};
    private static readonly double[] MachFactors={1,1,1.08,1.25,1.5,1.75,1.9,1.9,1.82,1.6,1.35,1.15,1.0,0.95};
    public static double MachDragFactor(double mach)
    {
        if(mach<=MachPoints[0])return MachFactors[0];
        for(var i=1;i<MachPoints.Length;i++)
            if(mach<=MachPoints[i])
                return MachFactors[i-1]+(MachFactors[i]-MachFactors[i-1])*(mach-MachPoints[i-1])/(MachPoints[i]-MachPoints[i-1]);
        return MachFactors[^1];
    }

    /// <summary>
    /// Applies aerodynamic drag opposing the rocket's velocity:
    /// F = ½ρv²·Cd(M)·A, with ρ from the standard atmosphere at the real
    /// local temperature and Cd raised through the transonic region
    /// (MachDragFactor). Still a single coefficient on the body's own
    /// cross-section with no separate centre of pressure, so drag never
    /// turns the vehicle.
    /// </summary>
    private void ApplyDrag(Rigidbody body)
    {
        UpdateAtmosphere();
        if(AirDensity<=0){Drag=0;return;}
        var velocity=body.linearVelocity/PlanetBody.WorldUnitsPerMeter;
        var speed=velocity.magnitude;
        if(speed<0.05f){Drag=0;return;}
        Drag=0.5*AirDensity*speed*speed*dragCoefficient*MachDragFactor(speed/SpeedOfSound)*FrontalArea;
        var direction=-velocity/speed;
        body.AddForce(direction*(float)(Drag*PlanetBody.WorldUnitsPerMeter),ForceMode.Force);
    }

    // The scene is fixed to the turning Earth - a rotating frame - so free
    // flight needs the Coriolis (−2ω×v) and centrifugal (−ω×(ω×r))
    // accelerations. Together they give a launch the ground's eastward speed
    // (~463 m/s at the equator) and make orbits curve correctly as seen from
    // the ground. Velocities here are relative to the ground - and to the
    // air, which turns with Earth, so drag needs no correction.
    private void ApplyRotatingFrame(Rigidbody body)
    {
        if(planet==null || body.isKinematic)return;
        var spin=planet.SpinVector;
        if(spin==Vector3.zero)return;
        var r=(body.worldCenterOfMass-planet.transform.position)/PlanetBody.WorldUnitsPerMeter;
        var v=body.linearVelocity/PlanetBody.WorldUnitsPerMeter;
        var a=-2f*Vector3.Cross(spin,v)-Vector3.Cross(spin,Vector3.Cross(spin,r));
        body.AddForce(a*PlanetBody.WorldUnitsPerMeter,ForceMode.Acceleration);
    }

    /// <summary>Speed relative to the stars (ground speed plus Earth's rotation) - what orbits care about.</summary>
    public double OrbitalSpeed
    {
        get
        {
            if(planet==null)return Speed;
            var r=(transform.position-planet.transform.position)/PlanetBody.WorldUnitsPerMeter;
            return (GroundVelocity+Vector3.Cross(planet.SpinVector,r)).magnitude;
        }
    }

    // Cross-section the drag acts on: the body's own (collider) radius.
    private CapsuleCollider frontalCapsule;
    private double FrontalArea
    {
        get
        {
            // Cached: the trajectory predictor asks thousands of times a frame.
            if(frontalCapsule==null)frontalCapsule=GetComponent<CapsuleCollider>();
            var radius=frontalCapsule!=null?frontalCapsule.radius/PlanetBody.WorldUnitsPerMeter:1.85f;
            return Math.PI*radius*radius;
        }
    }

    /// <summary>
    /// Drag deceleration (m/s²) this vehicle would feel at the given altitude
    /// and airspeed with its current mass - the same model ApplyDrag uses, for
    /// predicting a coasting trajectory (TrajectoryDisplay).
    /// </summary>
    public double DragDecelerationAt(double altitude,double speed)
    {
        if(TotalMass<=0 || speed<=0 || altitude>200000)return 0;
        var temperature=StandardAtmosphere.TemperatureKelvin(altitude);
        var density=StandardAtmosphere.Density(StandardAtmosphere.Pressure(altitude),temperature);
        var mach=speed/StandardAtmosphere.SpeedOfSound(temperature);
        return 0.5*density*speed*speed*dragCoefficient*MachDragFactor(mach)*FrontalArea/TotalMass;
    }

    public bool Prepare(double throttle)
    {
        if(crashed){LiquidThrust=0;FuelFlow=0;OxidizerFlow=0;Status="Impact — vehicle destroyed. Return to assembly to rebuild.";return false;}
        Bind();if(!tanksInitialized)Refill();UpdateMass();LiquidThrust=0;FuelFlow=0;OxidizerFlow=0;
        UpdateSolidThrust();
        UpdateAtmosphere();
        if(assembly.InstalledCount==0){Status="Install an engine first.";return false;}
        if(assembly.FuelTank==null || assembly.OxidizerTank==null){Status="Both propellant tanks are required.";return false;}
        if(fuelRemaining<=0 || oxidizerRemaining<=0){Status="Propellant depleted.";return false;}
        double totalThrust=0,totalFuelFlow=0,totalOxidizerFlow=0;
        for(var i=0;i<assembly.SocketCount;i++)
        {
            var p=assembly.GetParameters(i);if(p==null)continue;
            if(p.fuel!=fuelType){Status="Fuel mismatch: engine requires "+p.fuel+". Choose matching engines or change tank fuel.";return false;}
            var r=EnginePerformance.Evaluate(p,throttle,Pressure);
            if(!r.valid){Status="Outside engine model range: check parameters, pressure and minimum throttle.";return false;}
            totalThrust+=r.thrust;totalFuelFlow+=r.fuelFlow;totalOxidizerFlow+=r.oxidizerFlow;
        }
        LiquidThrust=totalThrust;FuelFlow=totalFuelFlow;OxidizerFlow=totalOxidizerFlow;
        Status="Ready";return true;
    }
    public void Step(Rigidbody body,float throttle,float dt)
    {
        if(!Prepare(throttle)){rocket.StopEngine();return;}
        var fraction=Math.Min(1,Math.Min(fuelRemaining/Math.Max(1e-12,FuelFlow*dt),oxidizerRemaining/Math.Max(1e-12,OxidizerFlow*dt)));
        for(var i=0;i<assembly.SocketCount;i++)
        {
            var p=assembly.GetParameters(i);if(p==null)continue;
            var r=EnginePerformance.Evaluate(p,throttle,Pressure);
            // Scene lengths use kilometres; forces must use the same scale as gravity.
            body.AddForceAtPosition(assembly.GetThrustDirection(i)*(float)(r.thrust*fraction*PlanetBody.WorldUnitsPerMeter),assembly.GetSocketPosition(i),ForceMode.Force);
        }
        fuelRemaining=Mathf.Max(0,fuelRemaining-(float)(FuelFlow*dt*fraction));
        oxidizerRemaining=Mathf.Max(0,oxidizerRemaining-(float)(OxidizerFlow*dt*fraction));
        UpdateMass();
        if(fraction<1 || fuelRemaining<=0 || oxidizerRemaining<=0){Status="Propellant depleted.";rocket.StopEngine();}
    }

    private void FixedUpdate()
    {
        Bind();
        if(crashed || !rocket.Launched || planet==null){trackingImpact=false;return;}
        // Applied every physics step regardless of engine state, so drag
        // keeps slowing the rocket while coasting or falling, not just
        // during powered flight (Step() only runs with the engine firing).
        ApplyDrag(GetComponent<Rigidbody>());
        BurnSolids(GetComponent<Rigidbody>(),Time.fixedDeltaTime);
        ApplyRotatingFrame(GetComponent<Rigidbody>());
        var current=transform.position;
        if(!trackingImpact){previousPosition=current;trackingImpact=true;}
        var center=planet.transform.position;
        double radius=planet.Radius*PlanetBody.WorldUnitsPerMeter;
        var start=previousPosition-center;var delta=current-previousPosition;
        double a=Vector3.Dot(delta,delta),b=2d*Vector3.Dot(start,delta);
        double c=(double)start.x*start.x+(double)start.y*start.y+(double)start.z*start.z-radius*radius;
        double t=-1;
        if(c<=0)t=0;
        else if(a>0 && b<0)
        {
            var discriminant=b*b-4*a*c;
            if(discriminant>=0)t=(-b-Math.Sqrt(discriminant))/(2*a);
        }
        previousPosition=current;
        if(t<0 || t>1)return;
        var normal=(start+delta*(float)t).normalized;
        var point=center+normal*(float)radius;
        transform.position=point;
        var body=GetComponent<Rigidbody>();
        rocket.StopEngine();body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.isKinematic=true;
        crashed=true;LiquidThrust=0;SolidThrust=0;solidPropellant=0;FuelFlow=0;OxidizerFlow=0;
        foreach(var surface in GetComponentsInChildren<Renderer>())if(surface.enabled){impactHidden.Add(surface);surface.enabled=false;}
        // Artistic fireball volume scales with remaining fuel, not a blast model.
        float size=5f+4f*Mathf.Pow(Mathf.Max(0,fuelRemaining),1f/3f);
        fuelRemaining=0;oxidizerRemaining=0;UpdateMass();
        Status="Impact — vehicle destroyed. Return to assembly to rebuild.";
        StartCoroutine(ImpactEffect(point,normal,size));
        StartCoroutine(ImpactDebris(point,normal));
    }

    private System.Collections.IEnumerator ImpactDebris(Vector3 point,Vector3 normal)
    {
        const int count=64;
        var pieces=new GameObject[count];var positions=new Vector3[count];var velocities=new Vector3[count];
        var material=new Material(Shader.Find("Standard"));material.color=new Color(.3f,.32f,.34f);
        var elapsed=0f;
        for(var i=0;i<count;i++)
        {
            pieces[i]=GameObject.CreatePrimitive(PrimitiveType.Cube);pieces[i].name="Rocket debris";
            var collider=pieces[i].GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            pieces[i].GetComponent<Renderer>().sharedMaterial=material;
            pieces[i].transform.localScale=new Vector3(UnityEngine.Random.Range(.1f,.7f),UnityEngine.Random.Range(.1f,.5f),UnityEngine.Random.Range(.1f,1.2f))*PlanetBody.WorldUnitsPerMeter;
            positions[i]=normal;
            velocities[i]=(UnityEngine.Random.onUnitSphere+normal*1.3f)*UnityEngine.Random.Range(8f,35f);
        }
        while(elapsed<10f)
        {
            var dt=Time.deltaTime;elapsed+=dt;
            for(var i=0;i<count;i++)
            {
                velocities[i]-=normal*9.81f*dt;positions[i]+=velocities[i]*dt;
                var height=Vector3.Dot(positions[i],normal);
                if(height<.6f)
                {
                    positions[i]+=normal*(.6f-height);
                    var vertical=Vector3.Dot(velocities[i],normal);
                    if(vertical<0)velocities[i]=(velocities[i]-normal*vertical*1.3f)*.65f;
                }
                pieces[i].transform.position=point+positions[i]*PlanetBody.WorldUnitsPerMeter;
                pieces[i].transform.Rotate(new Vector3(73,111,47)*dt);
                if(elapsed>8f)pieces[i].transform.localScale*=Mathf.Exp(-3f*dt);
            }
            yield return null;
        }
        foreach(var piece in pieces)Destroy(piece);Destroy(material);
    }

    private System.Collections.IEnumerator ImpactEffect(Vector3 point,Vector3 normal,float diameter)
    {
        var fireball=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fireball.name="Impact fireball";
        var collider=fireball.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        var material=new Material(Shader.Find("Unlit/Color"));
        fireball.GetComponent<Renderer>().sharedMaterial=material;
        var elapsed=0f;
        while(elapsed<3f)
        {
            elapsed+=Time.deltaTime;var fraction=Mathf.Clamp01(elapsed/3f);
            var size=diameter*PlanetBody.WorldUnitsPerMeter*Mathf.Sin(Mathf.PI*fraction);
            fireball.transform.position=point+normal*size*.3f;
            fireball.transform.localScale=Vector3.one*size;
            material.color=Color.Lerp(new Color(1f,.85f,.15f),new Color(.15f,.07f,.025f),fraction);
            yield return null;
        }
        Destroy(fireball);Destroy(material);
    }
}
