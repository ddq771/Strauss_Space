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
    private RocketAssemblyController assembly;
    private Rocket rocket;
    private PlanetBody planet;
    [SerializeField] private bool crashed;
    private Vector3 previousPosition;
    private bool trackingImpact;
    private readonly System.Collections.Generic.List<Renderer> impactHidden=new System.Collections.Generic.List<Renderer>();
    public double HorizontalSpeed=>Math.Sqrt(Math.Max(0,Speed*Speed-VerticalSpeed*VerticalSpeed));
    public string Status {get;private set;}="Ready";
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
    public double Drag {get;private set;}
    public double Thrust {get;private set;}
    public double FuelFlow {get;private set;}
    public double OxidizerFlow {get;private set;}
    public double DryMass {get;private set;}
    public double TotalMass=>DryMass+fuelRemaining+oxidizerRemaining;
    public double LocalGravity=>planet!=null?planet.GetGravityAcceleration(transform.position).magnitude/PlanetBody.WorldUnitsPerMeter:0;
    public double TWR=>TotalMass>0 && LocalGravity>0?Thrust/(TotalMass*LocalGravity):0;
    public double Speed=>GetComponent<Rigidbody>().linearVelocity.magnitude/PlanetBody.WorldUnitsPerMeter;
    // Signed radial velocity: independent of the rocket's orientation.
    public double VerticalSpeed
    {
        get
        {
            if(planet==null)return 0;
            var radial=(transform.position-planet.transform.position).normalized;
            return Vector3.Dot(GetComponent<Rigidbody>().linearVelocity,radial)/PlanetBody.WorldUnitsPerMeter;
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
        DryMass=rocket.StructuralMass+(assembly.FrameInstalled?120+50*(assembly.SocketCount-1):0);
        foreach(var t in new[]{assembly.FuelTank,assembly.OxidizerTank})
            if(t!=null)DryMass+=15*(Math.PI*t.Diameter*t.Height+Math.PI*t.Diameter*t.Diameter*.5);
        for(var i=0;i<assembly.SocketCount;i++)
        {var p=assembly.GetParameters(i);if(p!=null)DryMass+=p.dryMass;}
        var body=GetComponent<Rigidbody>();body.mass=(float)Math.Max(.001,TotalMass);
        var weighted=Vector3.zero;
        var frameMass=assembly.FrameInstalled?120+50*(assembly.SocketCount-1):0;
        weighted+=Vector3.up*rocket.AssemblyMountLocalY*frameMass;
        foreach(var t in new[]{assembly.FuelTank,assembly.OxidizerTank})
        {
            if(t==null)continue;
            var shellMass=15*(Math.PI*t.Diameter*t.Height+Math.PI*t.Diameter*t.Diameter*.5);
            var liquid=t==assembly.FuelTank?fuelRemaining:oxidizerRemaining;
            var center=transform.InverseTransformPoint(t.transform.TransformPoint(Vector3.up*t.Height*.5f));
            weighted+=center*(float)(shellMass+liquid);
        }
        for(var i=0;i<assembly.SocketCount;i++)
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
    // Explicit isothermal Earth atmosphere approximation: pressure decays
    // exponentially with altitude (scale height 8500 m, sea-level 101325 Pa).
    // Called every physics step (not just while the engine is running) so
    // Pressure/Drag telemetry stays live while coasting or falling, too.
    private void UpdateAtmosphere()
    {
        Pressure=planet!=null?101325*Math.Exp(-Altitude/8500):0;
    }

    /// <summary>
    /// Applies aerodynamic drag opposing the rocket's velocity, scaled by air
    /// density (from Pressure via the ideal gas law at a fixed reference
    /// temperature) and speed squared. This is a deliberately simple model -
    /// a single drag coefficient and the body's own cross-section, no
    /// separate centre of pressure - tuned to give a felt, gradual slowdown
    /// through the thick lower atmosphere rather than to match a real
    /// vehicle's flight data.
    /// </summary>
    private void ApplyDrag(Rigidbody body)
    {
        UpdateAtmosphere();
        if(Pressure<=0){Drag=0;return;}
        var velocity=body.linearVelocity/PlanetBody.WorldUnitsPerMeter;
        var speed=velocity.magnitude;
        if(speed<0.05f){Drag=0;return;}
        // Ideal gas law at a fixed reference temperature (288.15 K, standard
        // sea-level) - consistent with the isothermal pressure model above.
        var density=Pressure/(287.05*288.15);
        var capsule=GetComponent<CapsuleCollider>();
        var radius=capsule!=null?capsule.radius/PlanetBody.WorldUnitsPerMeter:1.85f;
        var area=Math.PI*radius*radius;
        Drag=0.5*density*speed*speed*dragCoefficient*area;
        var direction=-velocity/speed;
        body.AddForce(direction*(float)(Drag*PlanetBody.WorldUnitsPerMeter),ForceMode.Force);
    }

    public bool Prepare(double throttle)
    {
        if(crashed){Thrust=0;FuelFlow=0;OxidizerFlow=0;Status="Impact — vehicle destroyed. Return to assembly to rebuild.";return false;}
        Bind();if(!tanksInitialized)Refill();UpdateMass();Thrust=0;FuelFlow=0;OxidizerFlow=0;
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
        Thrust=totalThrust;FuelFlow=totalFuelFlow;OxidizerFlow=totalOxidizerFlow;
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
        crashed=true;Thrust=0;FuelFlow=0;OxidizerFlow=0;
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
