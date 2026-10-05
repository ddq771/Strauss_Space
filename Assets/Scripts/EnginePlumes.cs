using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fire from the engines: a glowing exhaust plume behind every engine
/// that is firing, sized by that engine's thrust this step and its nozzle
/// exit, and opening out as the air thins (tight at sea level, a huge faint
/// bloom in vacuum). Colour follows the propellant:
///   RP-1 (kerosene)   bright orange-yellow, sooty, with Mach diamonds
///   LH2 (hydrogen)    almost invisible pale blue-violet, diamonds
///   Methane           blue-violet with a pink edge
///   MMH (hypergolic)  faint orange-pink
///   Solid boosters    blinding white-yellow, thick white smoke
/// Plus a smoke trail low in the atmosphere (solids, kerosene) and an
/// orange light that lights up the pad and the vehicle.
/// </summary>
public sealed class EnginePlumes : MonoBehaviour
{
    private RocketAssemblyController assembly;
    private RocketFlightModel flight;
    private Rocket rocket;
    private Material material;
    private Mesh cone;
    private readonly List<Transform> liquid = new List<Transform>();
    private readonly List<Material> liquidMaterials = new List<Material>();
    private readonly List<Transform> solids = new List<Transform>();
    private readonly List<Material> solidMaterials = new List<Material>();
    private ParticleSystem smoke;
    private Light glow;
    private Vector3 smokeHead; private bool hasSmokeHead;
    private static Texture2D softDot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        foreach (var f in FindObjectsByType<RocketFlightModel>(FindObjectsSortMode.None))
            if (f.GetComponent<EnginePlumes>() == null) f.gameObject.AddComponent<EnginePlumes>();
    }

    private void Awake()
    {
        assembly = GetComponent<RocketAssemblyController>();
        flight = GetComponent<RocketFlightModel>();
        rocket = GetComponent<Rocket>();
    }

    private void LateUpdate()
    {
        if (assembly == null || flight == null) return;
        if (material == null) Build();
        var u = PlanetBody.WorldUnitsPerMeter;
        var pressureRatio = Mathf.Clamp01((float)(flight.Pressure / 101325.0));
        var vacuum = 1 - pressureRatio;
        var firing = rocket.Launched && !flight.Crashed && Time.time - flight.LastStepTime < .25f;
        var fuel = flight.FuelType;
        Colors(fuel, out var core, out var edge, out var brightness, out var diamonds);
        float totalThrust = 0; Vector3 lightPoint = Vector3.zero; float lightWeight = 0;

        // --- Liquid engines: one plume per socket that produced thrust.
        var sockets = assembly.SocketCount;
        while (liquid.Count < sockets) liquid.Add(MakePlume("Engine plume", liquidMaterials));
        for (var i = 0; i < liquid.Count; i++)
        {
            var thrust = firing && i < sockets ? flight.SocketThrust[i] : 0f;
            var plume = liquid[i];
            if (thrust <= 1) { if (plume.gameObject.activeSelf) plume.gameObject.SetActive(false); continue; }
            var p = assembly.GetParameters(i);
            var exitRadius = p != null && p.exitDiameter > 0 ? p.exitDiameter * .5f : 1f;
            var position = assembly.GetSocketPosition(i);
            var direction = -assembly.GetThrustDirection(i);
            Shape(plume, liquidMaterials[i], position, direction, thrust, exitRadius, vacuum, core, edge, brightness, diamonds * pressureRatio, u);
            totalThrust += thrust; lightPoint += position * thrust; lightWeight += thrust;
        }

        // --- Solid boosters: a plume at the base of each one still attached.
        var boosters = SolidBases();
        var solidFiring = firing && flight.SolidBurning && flight.SolidThrust > 0;
        while (solids.Count < boosters.Count) solids.Add(MakePlume("Solid booster plume", solidMaterials));
        for (var i = 0; i < solids.Count; i++)
        {
            var plume = solids[i];
            if (!solidFiring || i >= boosters.Count) { if (plume.gameObject.activeSelf) plume.gameObject.SetActive(false); continue; }
            var thrust = (float)flight.SolidThrust / boosters.Count;
            Shape(plume, solidMaterials[i], boosters[i].position, -transform.up, thrust, boosters[i].radius, vacuum,
                new Color(1f, .97f, .85f), new Color(1f, .62f, .2f), 1.6f, .25f * pressureRatio, u);
            totalThrust += thrust; lightPoint += boosters[i].position * thrust; lightWeight += thrust;
        }

        // --- Light from the fire.
        if (totalThrust > 1)
        {
            glow.enabled = true;
            glow.transform.position = lightPoint / lightWeight - transform.up * (8 * u);
            glow.color = solidFiring ? new Color(1f, .8f, .55f) : Color.Lerp(edge, core, .5f);
            glow.intensity = Mathf.Clamp(Mathf.Sqrt(totalThrust / 1e6f) * 1.2f, .5f, 8f) * (.4f + .6f * pressureRatio);
            glow.range = (60 + Mathf.Sqrt(totalThrust / 1e5f) * 25) * u;
        }
        else glow.enabled = false;

        // --- Smoke: solids and kerosene leave a trail low in the atmosphere.
        var smoky = (solidFiring ? 1f : 0f) + (fuel == "RP-1" && totalThrust > 1 ? .45f : 0f);
        var density = smoky * pressureRatio;
        if (density > .02f && totalThrust > 1)
        {
            var head = (lightWeight > 0 ? lightPoint / lightWeight : transform.position) - transform.up * (10 * u);
            if (hasSmokeHead)
            {
                var path = head - smokeHead;
                var width = Mathf.Max(4f, rocket.ActiveDiameter * 1.6f + (solidFiring ? 6 : 0));
                var count = Mathf.Clamp(Mathf.CeilToInt(path.magnitude / (width * .5f * u)), 1, 30);
                var emit = new ParticleSystem.EmitParams();
                for (var k = 0; k < count; k++)
                {
                    emit.position = smokeHead + path * ((k + Random.value) / count) + Random.insideUnitSphere * (width * .3f * u);
                    emit.startSize = width * Random.Range(.8f, 1.6f) * u;
                    var shade = Random.Range(.72f, .95f);
                    emit.startColor = new Color(shade, shade, shade * .98f, Mathf.Clamp01(density) * Random.Range(.35f, .6f));
                    emit.startLifetime = Random.Range(4f, 9f);
                    emit.velocity = Random.insideUnitSphere * (2 * u);
                    smoke.Emit(emit, 1);
                }
            }
            smokeHead = head; hasSmokeHead = true;
        }
        else hasSmokeHead = false;
    }

    private void Shape(Transform plume, Material m, Vector3 position, Vector3 direction, float thrust, float exitRadius,
                       float vacuum, Color core, Color edge, float brightness, float diamonds, float u)
    {
        if (!plume.gameObject.activeSelf) plume.gameObject.SetActive(true);
        var mn = thrust / 1e6f;
        // Sea level: a tight flame a few nozzle lengths long. Vacuum: the
        // exhaust expands into a wide, long, faint bloom.
        var length = (8 + 22 * Mathf.Sqrt(mn)) * (1 + 2f * vacuum);
        var radius = exitRadius * (1.05f + 4.5f * vacuum * vacuum);
        plume.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.down, direction));
        plume.localScale = new Vector3(radius, length, radius) * u;
        m.SetColor("_CoreColor", core);
        m.SetColor("_EdgeColor", edge);
        m.SetFloat("_Intensity", brightness * (1 - .55f * vacuum));
        m.SetFloat("_Diamonds", diamonds);
        m.SetFloat("_DiamondCount", 5 + 3 * Mathf.Sqrt(mn));
    }

    private static void Colors(string fuel, out Color core, out Color edge, out float brightness, out float diamonds)
    {
        switch (fuel)
        {
            case "LH2": core = new Color(.85f, .82f, 1f); edge = new Color(.35f, .3f, .75f); brightness = .55f; diamonds = 1f; break;
            case "Methane": core = new Color(.75f, .8f, 1f); edge = new Color(.55f, .3f, .85f); brightness = .8f; diamonds = .8f; break;
            case "MMH": core = new Color(1f, .75f, .65f); edge = new Color(.8f, .35f, .4f); brightness = .5f; diamonds = 0; break;
            default: core = new Color(1f, .85f, .55f); edge = new Color(1f, .45f, .1f); brightness = .85f; diamonds = .7f; break;   // RP-1
        }
    }

    private struct SolidBase { public Vector3 position; public float radius; }
    private readonly List<SolidBase> solidBases = new List<SolidBase>();
    private List<SolidBase> SolidBases()
    {
        solidBases.Clear();
        if (flight.SolidBoosterCount <= 0) return solidBases;
        // The body model's booster groups still attached (named SRB / EAP).
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (!t.name.StartsWith("Stage_") || !(t.name.Contains("SRB") || t.name.Contains("EAP"))) continue;
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var local = transform.InverseTransformPoint(bounds.center);
            var bottom = transform.InverseTransformPoint(bounds.center) - Vector3.up * (Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(transform.up.x), Mathf.Abs(transform.up.y), Mathf.Abs(transform.up.z))) / transform.lossyScale.y);
            local.y = bottom.y;
            var width = Mathf.Min(bounds.size.x, bounds.size.z) / PlanetBody.WorldUnitsPerMeter;
            solidBases.Add(new SolidBase { position = transform.TransformPoint(local), radius = Mathf.Max(1f, width * .45f) });
        }
        if (solidBases.Count == 0)
            for (var i = 0; i < flight.SolidBoosterCount; i++)
            {
                var a = i * Mathf.PI * 2 / flight.SolidBoosterCount;
                var offset = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (rocket.ActiveDiameter * .5f + 2);
                solidBases.Add(new SolidBase { position = transform.TransformPoint(Vector3.up * rocket.ActiveBaseLocalY + offset * PlanetBody.WorldUnitsPerMeter), radius = 1.5f });
            }
        return solidBases;
    }

    private Transform MakePlume(string name, List<Material> materials)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = cone;
        var r = go.AddComponent<MeshRenderer>();
        var m = new Material(material);
        m.SetFloat("_Seed", Random.value * 100);
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        materials.Add(m);
        return go.transform;
    }

    private void Build()
    {
        material = new Material(Shader.Find("Strauss Space/Engine Plume")) { name = "Engine plume" };
        cone = BuildCone();

        var smokeObject = new GameObject("Exhaust smoke");
        smokeObject.transform.SetParent(transform, false);
        smoke = smokeObject.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0; main.maxParticles = 4000;
        var emission = smoke.emission; emission.rateOverTime = 0;
        var size = smoke.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 3f));
        var fade = smoke.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(.85f, .85f, .85f), 1) },
                         new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.5f, .4f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = smoke.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var smokeMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { name = "Exhaust smoke" };
        smokeMaterial.mainTexture = SoftDot();
        smokeMaterial.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
        renderer.sharedMaterial = smokeMaterial;
        smoke.Play();

        glow = new GameObject("Engine light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.shadows = LightShadows.None;
        glow.enabled = false;
    }

    // A cone hanging down from y = 0 (nozzle exit, radius 1) to y = -1 (the
    // tail), bulging a little then closing; uv.y = 0..1 along it.
    private static Mesh BuildCone()
    {
        const int rings = 24, sides = 24;
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
        for (var r = 0; r <= rings; r++)
        {
            var t = (float)r / rings;
            var radius = Mathf.Lerp(1f, .0f, Mathf.Pow(t, 1.6f)) * (1 + .25f * Mathf.Sin(Mathf.PI * Mathf.Min(1, t * 1.6f)));
            for (var s = 0; s <= sides; s++)
            {
                var a = (float)s / sides * Mathf.PI * 2;
                var n = new Vector3(Mathf.Cos(a), .3f, Mathf.Sin(a)).normalized;
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, -t, Mathf.Sin(a) * radius));
                normals.Add(n);
                uvs.Add(new Vector2((float)s / sides, t));
            }
        }
        for (var r = 0; r < rings; r++)
            for (var s = 0; s < sides; s++)
            {
                int a = r * (sides + 1) + s, b = a + sides + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = "Plume cone" };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(new Vector3(0, -.5f, 0), new Vector3(2.6f, 1.2f, 2.6f));
        return mesh;
    }

    private static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 64;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Soft smoke" };
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x - n / 2f + .5f, y - n / 2f + .5f).magnitude / (n / 2f);
                var a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a);
                softDot.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        softDot.Apply();
        return softDot;
    }
}
