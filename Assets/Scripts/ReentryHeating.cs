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
/// Each patch belongs to one of the stage's heat zones (RocketPresets.
/// HeatZone: tiles, RCC, a base shield) or to its bare structure, with
/// that material's limit, emissivity and heat capacity, and its own local
/// radius in the Sutton-Graves term (a nose cap heats more than a broad
/// belly). Fly the vehicle at the attitude it was built for and every zone
/// stays under its rating; turn a weaker area into the flow and it
/// overheats and the vehicle burns up.
///
/// ShowZones draws the zones over the hull (H): each its own colour, bare
/// structure striped, shading to yellow and red as a patch nears its limit.
///
/// The glow - a white-hot cap on the leading side, a long orange/pink
/// plasma wake and an orange light - scales with the heat flux.
/// </summary>
public sealed class ReentryHeating : MonoBehaviour
{
    private const double SuttonGraves = 1.7415e-4;
    private const double Sigma = 5.670374e-8, Emissivity = .85;
    // W/m²: glow starts / full. Ascent (≤ ~100 kW/m² near a booster's
    // cutoff) stays dark; orbital entries (~1 MW/m²) blaze.
    private const float VisibleFlux = 1.2e5f, FullFlux = 2e6f;
    public const int MaxZones = 8;

    // Patch normals (rocket-local, +Y = nose): a Fibonacci sphere, fine
    // enough to resolve a 30° nose cap.
    private static readonly Vector3[] Normals = BuildNormals(96);

    // Set by the owner.
    public Func<Vector3> AirVelocity;           // m/s, world axes
    public Func<double> AltitudeMeters;
    public Func<Vector3> CenterWorld;
    public Func<float> LengthMeters, DiameterMeters;
    public Func<RocketPresets.HeatProtection> Protection;
    public Func<bool> Active;                   // false: no heating (on the pad, crashed)
    public Action<string> BurnedUp;

    private readonly double[] temperature = new double[Normals.Length];
    private readonly int[] patchZone = new int[Normals.Length];         // index into Zone*; last = bare
    private readonly float[] patchHeat = new float[Normals.Length];     // fraction of its zone's limit
    public double HeatFlux { get; private set; }            // W/m², stagnation point
    // Per zone, then the bare structure last: name, hottest patch, rating.
    public string[] ZoneNames { get; private set; } = { "bare aluminium" };
    public double[] ZoneHottestC { get; private set; } = { 15 };
    public double[] ZoneLimitC { get; private set; } = { 500 };
    public bool HasShield { get; private set; }
    /// <summary>How closely the vehicle holds the attitude it was built to enter at: 1 exactly, 0 at 90° or more off.</summary>
    public float AttitudeMatch { get; private set; }
    /// <summary>Draw the heat zones and their temperatures over the hull.</summary>
    public bool ShowZones;
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
        HeatFlux = 0; Glow = 0; Burned = false;
        for (var i = 0; i < ZoneHottestC.Length; i++) ZoneHottestC[i] = 15;
        Array.Clear(patchHeat, 0, patchHeat.Length);
        UpdateVisuals(Vector3.zero);
    }

    private void Awake()
    {
        for (var i = 0; i < temperature.Length; i++) temperature[i] = 288.15;
        FloatingOrigin.Shifted += OnOriginShifted;
    }
    private void OnOriginShifted(Vector3 offset) => trailHead += offset;

    private void FixedUpdate()
    {
        if (AirVelocity == null || Burned) return;
        if (Active != null && !Active()) { HeatFlux = 0; Glow = 0; return; }
        // On rails (only ever above the air) there's no heating to follow -
        // and at ×1,000,000 a step would be 80,000 sub-steps per patch.
        if (TimeWarp.OnRails) { HeatFlux = 0; Glow = 0; return; }
        var dt = Time.fixedDeltaTime * TimeWarp.ClockMultiplier;
        var velocity = AirVelocity();
        var speed = (double)velocity.magnitude;
        var altitude = AltitudeMeters();
        var airK = StandardAtmosphere.TemperatureKelvin(altitude);
        var density = StandardAtmosphere.Density(StandardAtmosphere.Pressure(altitude), airK);
        var noseRadius = Math.Max(.3, DiameterMeters() * .5);
        HeatFlux = speed > 1 ? SuttonGraves * Math.Sqrt(density / noseRadius) * speed * speed * speed : 0;

        var protection = Protection != null ? Protection() : default;
        var zones = protection.zones ?? Array.Empty<RocketPresets.HeatZone>();
        var zoneCount = Mathf.Min(zones.Length, MaxZones);
        var bare = protection.Bare;
        HasShield = zoneCount > 0;
        if (ZoneNames.Length != zoneCount + 1 || (zoneCount > 0 && ZoneNames[0] != zones[0].name) || ZoneNames[zoneCount] != bare.name)
        {
            ZoneNames = new string[zoneCount + 1]; ZoneHottestC = new double[zoneCount + 1]; ZoneLimitC = new double[zoneCount + 1];
            for (var z = 0; z <= zoneCount; z++) { ZoneNames[z] = z < zoneCount ? zones[z].name : bare.name; ZoneHottestC[z] = 15; }
        }
        // Which zone each patch is in, for the stage flying now.
        var zoneAxes = new Vector3[zoneCount]; var zoneCos = new float[zoneCount];
        for (var z = 0; z < zoneCount; z++) { zoneAxes[z] = protection.ToLocal(zones[z].direction); zoneCos[z] = Mathf.Cos(zones[z].halfAngle * Mathf.Deg2Rad); }
        for (var i = 0; i < Normals.Length; i++)
        {
            var zone = zoneCount;
            for (var z = 0; z < zoneCount; z++) if (Vector3.Dot(Normals[i], zoneAxes[z]) >= zoneCos[z]) { zone = z; break; }
            patchZone[i] = zone;
        }
        for (var z = 0; z <= zoneCount; z++) ZoneLimitC[z] = z < zoneCount ? zones[z].limitC : bare.limitC;
        // The side meeting the air: the direction of motion through it, in
        // the vehicle's frame (patches facing it take the heat).
        var oncoming = speed > 1 ? transform.InverseTransformDirection(velocity / (float)speed) : Vector3.up;
        AttitudeMatch = Mathf.Clamp01(Vector3.Dot(protection.ToLocal(protection.EntryDirection), oncoming));

        // Integrate each patch; sub-step so warp or a big flux can't overshoot.
        var steps = Math.Max(1, (int)Math.Ceiling(dt / .25));
        var h = dt / steps;
        var hottest = new double[zoneCount + 1];
        for (var z = 0; z <= zoneCount; z++) hottest[z] = airK;
        string failure = null;
        for (var i = 0; i < Normals.Length; i++)
        {
            var c = Vector3.Dot(Normals[i], oncoming);
            var exposure = c > 0 ? Math.Pow(c, 1.5) : 0;
            var zone = patchZone[i];
            var material = zone < zoneCount ? zones[zone] : bare;
            var radius = material.localRadius > 0 ? material.localRadius : noseRadius;
            var flux = HeatFlux * Math.Sqrt(noseRadius / radius) * exposure;
            var emissivity = material.emissivity > 0 ? material.emissivity : Emissivity;
            var capacity = material.heatCapacity > 0 ? material.heatCapacity : 9000;
            var t = temperature[i];
            for (var s = 0; s < steps; s++)
            {
                var net = flux - emissivity * Sigma * (t * t * t * t - airK * airK * airK * airK);
                t = Math.Max(airK * .5, t + net / capacity * h);
            }
            temperature[i] = t;
            hottest[zone] = Math.Max(hottest[zone], t);
            var celsius = t - 273.15;
            patchHeat[i] = (float)(celsius / material.limitC);
            if (failure == null && celsius > material.limitC)
                failure = material.name + " overheated (" + celsius.ToString("N0") + " °C, rated " + material.limitC.ToString("N0") + " °C)";
        }
        for (var z = 0; z <= zoneCount; z++) ZoneHottestC[z] = hottest[z] - 273.15;
        lastProtection = protection;
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
        UpdateHeatMap();
    }

    // --- Heat-zone map (H) ------------------------------------------------
    // A tinted copy of every hull mesh: each zone its own colour with a
    // bright line at its edge, bare structure striped, and every patch
    // shading to yellow then red as it nears its rating.
    private RocketPresets.HeatProtection lastProtection;
    private Material heatMapMaterial;
    private readonly System.Collections.Generic.List<GameObject> heatMaps = new System.Collections.Generic.List<GameObject>();
    private int heatMapSourceCount = -1;
    private readonly Vector4[] patchDirs = new Vector4[Normals.Length];
    private readonly Vector4[] zoneAxisArray = new Vector4[MaxZones];
    private readonly Vector4[] zoneColorArray = new Vector4[MaxZones + 1];
    /// <summary>The colour each zone is drawn in (bare structure last), for the legend.</summary>
    public static Color ZoneColor(int zone, int zoneCount)
    {
        if (zone >= zoneCount) return new Color(.95f, .6f, .2f);   // bare: amber stripes
        var palette = new[] { new Color(.3f, .55f, 1f), new Color(.2f, .85f, .9f), new Color(.65f, .45f, 1f), new Color(.3f, .9f, .55f), new Color(.95f, .45f, .85f), new Color(.6f, .75f, 1f), new Color(.9f, .9f, .4f), new Color(.5f, .9f, .9f) };
        return palette[zone % palette.Length];
    }

    private void UpdateHeatMap()
    {
        var show = ShowZones && Protection != null;
        if (!show)
        {
            foreach (var g in heatMaps) if (g != null && g.activeSelf) g.SetActive(false);
            return;
        }
        if (heatMapMaterial == null)
        {
            var shader = Shader.Find("Strauss Space/Heat Zones");
            if (shader == null) return;
            heatMapMaterial = new Material(shader) { name = "Heat zones" };
        }
        // Zones for the stage flying now even before any heating.
        var protection = Protection();
        var zones = protection.zones ?? Array.Empty<RocketPresets.HeatZone>();
        var zoneCount = Mathf.Min(zones.Length, MaxZones);
        if (lastProtection.zones != protection.zones) ResetPatchesFor(protection, zoneCount);
        for (var z = 0; z < MaxZones; z++)
        {
            if (z < zoneCount)
            {
                var axis = transform.TransformDirection(protection.ToLocal(zones[z].direction));
                zoneAxisArray[z] = new Vector4(axis.x, axis.y, axis.z, Mathf.Cos(zones[z].halfAngle * Mathf.Deg2Rad));
            }
            else zoneAxisArray[z] = Vector4.zero;
        }
        for (var z = 0; z <= MaxZones; z++) zoneColorArray[z] = ZoneColor(z < zoneCount ? z : zoneCount, zoneCount);
        for (var i = 0; i < Normals.Length; i++)
        {
            var d = transform.TransformDirection(Normals[i]);
            patchDirs[i] = new Vector4(d.x, d.y, d.z, patchHeat[i]);
        }
        heatMapMaterial.SetVectorArray("_PatchDir", patchDirs);
        heatMapMaterial.SetVectorArray("_ZoneAxis", zoneAxisArray);
        heatMapMaterial.SetVectorArray("_ZoneColor", zoneColorArray);
        heatMapMaterial.SetFloat("_ZoneCount", zoneCount);
        RefreshCopies(heatMaps, ref heatMapSourceCount, heatMapMaterial, "Heat zones");
        foreach (var g in heatMaps) if (g != null && !g.activeSelf) g.SetActive(true);
    }

    // On the pad or after staging: which zone each patch is in, so the map
    // shows the right areas before the first heating step.
    private void ResetPatchesFor(RocketPresets.HeatProtection protection, int zoneCount)
    {
        lastProtection = protection;
        for (var i = 0; i < Normals.Length; i++)
        {
            var zone = zoneCount;
            for (var z = 0; z < zoneCount; z++)
                if (Vector3.Dot(Normals[i], protection.ToLocal(protection.zones[z].direction)) >= Mathf.Cos(protection.zones[z].halfAngle * Mathf.Deg2Rad)) { zone = z; break; }
            patchZone[i] = zone;
        }
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
    private void RefreshSheaths() => RefreshCopies(sheaths, ref sheathSourceCount, sheathMaterial, "Re-entry sheath");

    private void RefreshCopies(System.Collections.Generic.List<GameObject> copies, ref int sourceCount, Material material, string label)
    {
        var filters = GetComponentsInChildren<MeshFilter>(true);
        var count = 0;
        foreach (var f in filters) if (IsHull(f)) count++;
        if (count == sourceCount && copies.TrueForAll(g => g != null)) return;
        foreach (var g in copies) if (g != null) Destroy(g);
        copies.Clear();
        foreach (var f in filters)
        {
            if (!IsHull(f)) continue;
            var g = new GameObject(label);
            g.AddComponent<ReentrySheath>();
            g.transform.SetParent(f.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            copies.Add(g);
        }
        sourceCount = count;
    }

    // A visible hull mesh: not one of these overlays, and not an engine flame.
    private static bool IsHull(MeshFilter f)
    {
        if (f.GetComponent<ReentrySheath>() != null || f.sharedMesh == null || !f.gameObject.activeInHierarchy) return false;
        var r = f.GetComponent<MeshRenderer>();
        if (r == null || !r.enabled) return false;
        return !(r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.StartsWith("Strauss Space/Engine Plume"));
    }

    private void SetSheaths(bool on) { foreach (var g in sheaths) if (g != null && g.activeSelf != on) g.SetActive(on); }

    private void OnDestroy()
    {
        FloatingOrigin.Shifted -= OnOriginShifted;
        if (sheathMaterial != null) Destroy(sheathMaterial);
        if (heatMapMaterial != null) Destroy(heatMapMaterial);
    }
}

/// <summary>Marks a re-entry glow mesh, so it isn't itself copied as hull.</summary>
public sealed class ReentrySheath : MonoBehaviour { }
