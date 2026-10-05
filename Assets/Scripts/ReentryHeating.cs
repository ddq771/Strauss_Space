using System;
using UnityEngine;

/// <summary>
/// Aerodynamic heating and the re-entry glow, for the rocket and for
/// falling spent stages.
///
/// Heat flux at the stagnation point from the Sutton-Graves relation,
///     q = k·√(ρ/rₙ)·v³,  k = 1.7415e-4 (Earth air, SI), rₙ = half the body
///     diameter,
/// with ρ from the US Standard Atmosphere 1976 (the same model as the drag)
/// and v the speed through the air. The surface is a set of patches facing
/// all directions: each is heated by q·max(0, cos θ)^1.5, θ between its
/// normal and the oncoming flow (the windward side takes the heat, the
/// leeward side almost none), and cools by radiating, εσT⁴, with the
/// thermal mass of the skin behind it:
///     C·dT/dt = q·f(θ) − εσ(T⁴ − T_air⁴).
/// Patches inside the stage's heat shield (tiles, base shield) survive up
/// to its limit; bare structure breaks up above ~1,100 °C. Fly a shielded
/// vehicle with the shield into the flow and it survives; turn bare metal
/// into the flow and it burns up.
///
/// The glow - a white-hot cap on the leading side, a long orange/pink
/// plasma wake and an orange light - scales with the heat flux.
/// </summary>
public sealed class ReentryHeating : MonoBehaviour
{
    private const double SuttonGraves = 1.7415e-4;
    private const double Sigma = 5.670374e-8, Emissivity = .85;
    private const double HeatCapacity = 25000;     // J/(m²·K): skin plus structure behind it
    private const float VisibleFlux = 3e4f, FullFlux = 1.5e6f;   // W/m²: glow starts / full

    // Patch normals (rocket-local, +Y = nose): a Fibonacci sphere.
    private static readonly Vector3[] Normals = BuildNormals(48);

    // Set by the owner.
    public Func<Vector3> AirVelocity;           // m/s, world axes
    public Func<double> AltitudeMeters;
    public Func<Vector3> CenterWorld;
    public Func<float> LengthMeters, DiameterMeters;
    public Func<RocketPresets.HeatProtection> Protection;
    public Func<bool> Active;                   // false: no heating (on the pad, crashed)
    public Action<string> BurnedUp;

    private readonly double[] temperature = new double[Normals.Length];
    public double HeatFlux { get; private set; }            // W/m², stagnation point
    public double HottestShieldC { get; private set; } = 15;
    public double HottestBareC { get; private set; } = 15;
    public double ShieldLimitC { get; private set; }
    public double BareLimitC { get; private set; } = RocketPresets.HeatProtection.DefaultBareLimitC;
    public bool HasShield { get; private set; }
    /// <summary>How squarely the heat shield faces the flow: 1 head-on, 0 edge-on or away.</summary>
    public float ShieldFacing { get; private set; }
    public float Glow { get; private set; }
    public bool Burned { get; private set; }


    // The rocket's heating, wired to its flight model and staging.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToRocket()
    {
        foreach (var flight in FindObjectsByType<RocketFlightModel>(FindObjectsSortMode.None))
        {
            if (flight.GetComponent<ReentryHeating>() != null) continue;
            var heating = flight.gameObject.AddComponent<ReentryHeating>();
            var rocket = flight.GetComponent<Rocket>();
            var assembly = flight.GetComponent<RocketAssemblyController>();
            var body = flight.GetComponent<Rigidbody>();
            heating.AirVelocity = () => flight.GroundVelocity;
            heating.AltitudeMeters = () => flight.Altitude;
            heating.CenterWorld = () => body.worldCenterOfMass;
            heating.LengthMeters = () => Mathf.Max(1f, rocket.ActiveHeight);
            heating.DiameterMeters = () => Mathf.Max(.5f, rocket.ActiveDiameter);
            heating.Protection = () => assembly != null ? assembly.CurrentHeatProtection : default;
            heating.Active = () => rocket.Launched && !flight.Crashed;
            heating.BurnedUp = reason =>
            {
                flight.BurnUp(reason);
                if (assembly != null) assembly.ReportBurnUp(reason);
            };
        }
    }

    /// <summary>Heating for a falling spent stage: bare structure, burns up like the real ones.</summary>
    public static void AttachToDebris(DroppedStage stage, Rigidbody body, PlanetBody planet, float length, float diameter)
    {
        var heating = stage.gameObject.AddComponent<ReentryHeating>();
        heating.AirVelocity = () => body.linearVelocity / PlanetBody.WorldUnitsPerMeter;
        heating.AltitudeMeters = () => planet != null ? (body.worldCenterOfMass - planet.transform.position).magnitude / PlanetBody.WorldUnitsPerMeter - planet.Radius : 0;
        heating.CenterWorld = () => body.worldCenterOfMass;
        heating.LengthMeters = () => length;
        heating.DiameterMeters = () => diameter;
        heating.BurnedUp = _ => Destroy(stage.gameObject);
    }

    private static Vector3[] BuildNormals(int n)
    {
        var result = new Vector3[n];
        var golden = Mathf.PI * (3 - Mathf.Sqrt(5));
        for (var i = 0; i < n; i++)
        {
            var y = 1 - 2 * (i + .5f) / n;
            var r = Mathf.Sqrt(1 - y * y);
            result[i] = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
        }
        return result;
    }

    public void ResetHeat()
    {
        for (var i = 0; i < temperature.Length; i++) temperature[i] = 288.15;
        HeatFlux = 0; Glow = 0; Burned = false; HottestShieldC = HottestBareC = 15;
        UpdateVisuals(Vector3.zero);
    }

    private void Awake() { for (var i = 0; i < temperature.Length; i++) temperature[i] = 288.15; }

    private void FixedUpdate()
    {
        if (AirVelocity == null || Burned) return;
        if (Active != null && !Active()) { HeatFlux = 0; Glow = 0; return; }
        var dt = Time.fixedDeltaTime * TimeWarp.ClockMultiplier;
        var velocity = AirVelocity();
        var speed = (double)velocity.magnitude;
        var altitude = AltitudeMeters();
        var airK = StandardAtmosphere.TemperatureKelvin(altitude);
        var density = StandardAtmosphere.Density(StandardAtmosphere.Pressure(altitude), airK);
        var noseRadius = Math.Max(.3, DiameterMeters() * .5);
        HeatFlux = speed > 1 ? SuttonGraves * Math.Sqrt(density / noseRadius) * speed * speed * speed : 0;

        var protection = Protection != null ? Protection() : default;
        HasShield = protection.HasShield;
        ShieldLimitC = protection.shieldLimitC; BareLimitC = protection.BareLimit;
        var axis = protection.shieldAxis.sqrMagnitude > 0 ? protection.shieldAxis.normalized : Vector3.zero;
        var cosShield = Mathf.Cos(protection.shieldHalfAngle * Mathf.Deg2Rad);
        // The side meeting the air: the direction of motion through it, in
        // the vehicle's frame (patches facing it take the heat).
        var oncoming = speed > 1 ? transform.InverseTransformDirection(velocity / (float)speed) : Vector3.up;
        ShieldFacing = HasShield && axis != Vector3.zero ? Mathf.Clamp01(Vector3.Dot(axis, oncoming)) : 0;

        // Integrate each patch; sub-step so warp or a big flux can't overshoot.
        var steps = Math.Max(1, (int)Math.Ceiling(dt / .25));
        var h = dt / steps;
        double shieldMax = airK, bareMax = airK;
        string failure = null;
        for (var i = 0; i < Normals.Length; i++)
        {
            var c = Vector3.Dot(Normals[i], oncoming);
            var exposure = c > 0 ? Math.Pow(c, 1.5) : 0;
            var t = temperature[i];
            for (var s = 0; s < steps; s++)
            {
                var net = HeatFlux * exposure - Emissivity * Sigma * (t * t * t * t - airK * airK * airK * airK);
                t = Math.Max(airK * .5, t + net / HeatCapacity * h);
            }
            temperature[i] = t;
            var shielded = HasShield && axis != Vector3.zero && Vector3.Dot(Normals[i], axis) >= cosShield;
            var celsius = t - 273.15;
            if (shielded) shieldMax = Math.Max(shieldMax, t); else bareMax = Math.Max(bareMax, t);
            if (failure == null && celsius > (shielded ? protection.shieldLimitC : protection.BareLimit))
                failure = shielded ? "heat shield failed" : "bare structure exposed to the plasma";
        }
        HottestShieldC = shieldMax - 273.15; HottestBareC = bareMax - 273.15;
        Glow = Mathf.Clamp01(Mathf.Log10(Mathf.Max(1f, (float)HeatFlux) / VisibleFlux) / Mathf.Log10(FullFlux / VisibleFlux));
        if (failure != null && Active != null && Active())
        {
            Burned = true;
            BurnedUp?.Invoke(failure);
        }
    }

    private void LateUpdate()
    {
        UpdateVisuals(AirVelocity != null && !Burned ? AirVelocity() : Vector3.zero);
    }

    // --- Visuals ----------------------------------------------------------
    // A glowing copy of every hull mesh (the shock layer on the windward
    // side), a particle trail of hot gas left behind in the air, and an
    // orange light.
    private Material sheathMaterial;
    private readonly System.Collections.Generic.List<GameObject> sheaths = new System.Collections.Generic.List<GameObject>();
    private int sheathSourceCount = -1;
    private ParticleSystem trail;
    private Vector3 trailHead; private bool hasTrailHead;
    private Light glowLight;
    private static Texture2D softDot;

    private void UpdateVisuals(Vector3 velocity)
    {
        var glow = Glow;
        var visible = glow > .001f && velocity.sqrMagnitude > 1;
        if (!visible)
        {
            if (sheathMaterial != null) { SetSheaths(false); glowLight.enabled = false; }
            hasTrailHead = false;
            return;
        }
        if (sheathMaterial == null) Build();
        RefreshSheaths();
        SetSheaths(true);
        glowLight.enabled = true;

        var u = PlanetBody.WorldUnitsPerMeter;
        var motion = velocity.normalized;
        var length = LengthMeters(); var diameter = DiameterMeters();
        // Dull red at the edge of visibility, orange, then the pink-white
        // glare of the hottest entries.
        var color = Color.Lerp(new Color(1f, .28f, .06f), new Color(1f, .5f, .2f), Mathf.Clamp01(glow * 1.6f));
        color = Color.Lerp(color, new Color(1f, .52f, .78f), Mathf.Clamp01((glow - .55f) * 2.2f));
        sheathMaterial.SetColor("_Color", color);
        // Rises fast then levels off, so the hottest entries stay coloured
        // (pink-orange) instead of clipping to white.
        sheathMaterial.SetFloat("_Intensity", 1.6f * Mathf.Sqrt(glow));
        sheathMaterial.SetVector("_FlowDir", motion);
        sheathMaterial.SetFloat("_Inflate", (.3f + diameter * .06f * glow) * u);

        var center = CenterWorld();
        // Trail: hot gas shed from the windward face, left behind in the air
        // all along the path flown since the last frame (at 7 km/s that's
        // hundreds of metres a frame - emitting only at the vehicle would
        // leave a dotted line).
        var head = center + motion * (diameter * .4f * u);
        if (hasTrailHead)
        {
            var path = head - trailHead;
            var spacing = Mathf.Max(diameter * .5f * u, path.magnitude / 40f);
            var count = Mathf.Min(40, Mathf.CeilToInt(path.magnitude / spacing * glow));
            var emit = new ParticleSystem.EmitParams();
            for (var i = 0; i < count; i++)
            {
                emit.position = trailHead + path * ((i + UnityEngine.Random.value) / count) + UnityEngine.Random.insideUnitSphere * (diameter * .35f * u);
                emit.startSize = UnityEngine.Random.Range(1.4f, 2.8f) * diameter * u * (.5f + glow);
                emit.startColor = Color.Lerp(color, new Color(1f, .75f, .5f), UnityEngine.Random.value * .5f) * new Color(1, 1, 1, .35f + .5f * glow);
                emit.startLifetime = UnityEngine.Random.Range(.4f, 1.2f) * (.4f + glow);
                trail.Emit(emit, 1);
            }
        }
        trailHead = head; hasTrailHead = true;

        glowLight.color = color;
        glowLight.intensity = 6f * glow;
        glowLight.range = (length + 40) * 3f * u;
        glowLight.transform.position = center + motion * (diameter * u);
    }

    private void Build()
    {
        sheathMaterial = new Material(Shader.Find("Strauss Space/Reentry Plasma")) { name = "Re-entry plasma" };
        sheathMaterial.SetFloat("_Seed", UnityEngine.Random.value * 100);

        var trailObject = new GameObject("Re-entry trail");
        trailObject.transform.SetParent(transform, false);
        trail = trailObject.AddComponent<ParticleSystem>();
        trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = trail.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // left behind where it was shed
        main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.4f);
        main.startSpeed = 0;
        main.maxParticles = 2000;
        main.playOnAwake = true;
        var emission = trail.emission; emission.rateOverTime = 0;   // emitted by hand along the path
        var fade = trail.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, .45f, .2f), .4f), new GradientColorKey(new Color(.6f, .15f, .05f), 1) },
                         new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.6f, .3f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var size = trail.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 2.2f));
        var renderer = trail.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var particleMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = "Re-entry trail" };
        particleMaterial.mainTexture = SoftDot();
        particleMaterial.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
        renderer.sharedMaterial = particleMaterial;
        trail.Play();

        glowLight = new GameObject("Re-entry glow").AddComponent<Light>();
        glowLight.transform.SetParent(transform, false);
        glowLight.type = LightType.Point;
        glowLight.shadows = LightShadows.None;
    }

    private static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 64;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Soft dot" };
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x - n / 2f + .5f, y - n / 2f + .5f).magnitude / (n / 2f);
                var a = Mathf.Clamp01(1 - d); a *= a;
                softDot.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        softDot.Apply();
        return softDot;
    }

    // One glowing copy per hull mesh still attached (staging changes the
    // set: rebuilt when the number of meshes changes).
    private void RefreshSheaths()
    {
        var filters = GetComponentsInChildren<MeshFilter>();
        var count = 0;
        foreach (var f in filters) if (f.GetComponent<ReentrySheath>() == null && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled) count++;
        if (count == sheathSourceCount && sheaths.TrueForAll(g => g != null)) return;
        foreach (var g in sheaths) if (g != null) Destroy(g);
        sheaths.Clear();
        foreach (var f in filters)
        {
            var source = f.GetComponent<MeshRenderer>();
            if (f.GetComponent<ReentrySheath>() != null || source == null || !source.enabled || f.sharedMesh == null) continue;
            var g = new GameObject("Re-entry sheath");
            g.AddComponent<ReentrySheath>();
            g.transform.SetParent(f.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = sheathMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            sheaths.Add(g);
        }
        sheathSourceCount = count;
    }

    private void SetSheaths(bool on) { foreach (var g in sheaths) if (g != null && g.activeSelf != on) g.SetActive(on); }

    private void OnDestroy()
    {
        if (sheathMaterial != null) Destroy(sheathMaterial);
    }
}

/// <summary>Marks a re-entry glow mesh, so it isn't itself copied as hull.</summary>
public sealed class ReentrySheath : MonoBehaviour { }
