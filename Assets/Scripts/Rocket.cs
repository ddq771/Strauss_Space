using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public sealed class Rocket : MonoBehaviour
{
    private const float StandardGravity = 9.80665f;

    [Header("Base structure mass (kg)")]
    [Tooltip("Structural mass excluding separately modeled engines, tanks, frame and propellants.")]
    [Min(0.001f)]
    [SerializeField] private float dryMass = 10_000f;

    [Min(0f)]
    [SerializeField] private float fuelMass = 20_000f;

    [Header("Engine")]
    [Tooltip("Maximum engine thrust in newtons.")]
    [Min(0f)]
    [SerializeField] private float maxThrust = 500_000f;

    [Tooltip("Engine efficiency, measured in seconds.")]
    [Min(0.01f)]
    [SerializeField] private float specificImpulse = 300f;

    [Range(0f, 1f)]
    [SerializeField] private float throttle;

    [SerializeField] private bool engineEnabled;

    [Header("Launch Site")]
    [Tooltip("Latitude of the launch site in degrees. South is negative.")]
    [SerializeField] private float launchLatitudeDegrees = -3.2f;

    [Tooltip("Longitude of the launch site in degrees. East is positive.")]
    [SerializeField] private float launchLongitudeDegrees = 40.1f;

    [Header("Shape (metres)")]
    [Tooltip("Outer diameter of the cylindrical body. A real launch vehicle " +
             "is a slender tube, not a squat cylinder - keep this small " +
             "relative to the total height.")]
    [Min(0.1f)]
    [SerializeField] private float bodyDiameter = 3.7f;

    [Min(0.1f)]
    [SerializeField] private float bodyHeight = 35f;

    [Tooltip("Height of the tapered nose cone stacked on top of the body.")]
    [Min(0f)]
    [SerializeField] private float noseHeight = 8f;

    [Tooltip("Height of the tapered engine skirt at the base of the body.")]
    [Min(0f)]
    [SerializeField] private float engineHeight = 4f;

    [Range(8, 64)]
    [SerializeField] private int hullSegments = 24;

    [Tooltip("Body/nose color. Presets (RocketPresets) use this to tell " +
             "otherwise-identically-shaped hulls apart at a glance - " +
             "Starship's bare steel vs. Falcon 9's white, for instance.")]
    [SerializeField] private Color hullColor = new(0.85f, 0.86f, 0.88f);

    public float StructuralMass => dryMass;
    public bool Launched => launchClampReleased;
    public float DryMass => GetComponent<RocketFlightModel>() is RocketFlightModel flight ? (float)flight.DryMass : dryMass;
    public float FuelMass => GetComponent<RocketFlightModel>() is RocketFlightModel flight ? flight.FuelRemaining+flight.OxidizerRemaining : fuelMass;
    public float CurrentMass => DryMass + FuelMass;
    public float MaxThrust => maxThrust;
    public float SpecificImpulse => specificImpulse;
    public float Throttle => throttle;
    public bool EngineEnabled => engineEnabled;
    public float LaunchLatitudeDegrees => launchLatitudeDegrees;
    public float LaunchLongitudeDegrees => launchLongitudeDegrees;
    public float AssemblyMountLocalY =>
        (engineHeight - (bodyHeight + noseHeight + engineHeight) * .5f) * PlanetBody.WorldUnitsPerMeter;

    private Rigidbody body;
    [SerializeField, HideInInspector] private bool launchClampReleased;

    private Material hullMaterial;
    private Material engineMaterial;
    private float builtBodyDiameter = -1f;
    private float builtBodyHeight = -1f;
    private float builtNoseHeight = -1f;
    private float builtEngineHeight = -1f;
    private int builtHullSegments = -1;
    private GameObject importedBody;
    private string builtBodyModelKey;

    private void Reset()
    {
        ApplyMass();
        BuildVisual();
    }

    private void Awake()
    {
        ApplyMass();
        BuildVisual();
    }

    private void Start()
    {
        // Restore a deterministic launch configuration whenever the
        // simulation starts. This keeps the cylinder aligned with the local
        // Earth surface normal instead of inheriting a stale scene rotation.
        var planet = FindFirstObjectByType<PlanetBody>();
        if (planet != null)
        {
            PlaceAtLatitudeLongitude(
                planet,
                launchLatitudeDegrees,
                launchLongitudeDegrees,
                clearance: 25f);

            PlaceOnAssemblyMount();

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            EnsureLaunchPlatformCollider();
            SetLaunchClamp(true);
        }
    }

    private static void EnsureLaunchPlatformCollider()
    {
        var platform = GameObject.Find("Kenya Launch Platform");
        if (platform == null)
        {
            return;
        }

        // The primitive cylinder's default CapsuleCollider is much taller
        // than the visible, thin launch platform after scaling. Replace it
        // with a unit BoxCollider so the collider follows the mesh thickness.
        var capsule = platform.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.enabled = false;
        }

        var box = platform.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = platform.AddComponent<BoxCollider>();
        }

        box.center = Vector3.zero;
        box.size = new Vector3(1f, 2f, 1f);
        box.enabled = true;
    }

    /// <summary>
    /// Used by RocketPreset selection to give a real vehicle its own body
    /// size and color instead of the sandbox default shape. Rebuilds the
    /// hull immediately so the change is visible without waiting for the
    /// next OnValidate/Awake pass.
    /// </summary>
    public void ConfigureShape(float diameter,float height,float nose,float engineSkirt,Color color)
    {
        bodyDiameter=diameter;bodyHeight=height;noseHeight=nose;engineHeight=engineSkirt;hullColor=color;
        BuildVisual();
    }

    /// <summary>
    /// Swaps the generated nose/body/engine hull for an imported model from
    /// Assets/Resources/RocketBodies (built procedurally in Blender - see
    /// ArtSource/*/build_*.py - not a licensed/downloaded asset), or back to
    /// the generated hull when key is null/empty. The collider still comes
    /// from ConfigureShape's dimensions either way - only the visual swaps.
    /// </summary>
    public void SetBodyModel(string key)
    {
        if (builtBodyModelKey == key) return;
        builtBodyModelKey = key;

        if (importedBody != null)
        {
            if (Application.isPlaying) Destroy(importedBody);
            else DestroyImmediate(importedBody);
            importedBody = null;
        }

        var useGeneratedHull = string.IsNullOrEmpty(key);
        foreach (var partName in new[] { "Engine", "Body", "NoseCone" })
        {
            var part = transform.Find(partName);
            if (part != null) part.gameObject.SetActive(useGeneratedHull);
        }
        if (useGeneratedHull) return;

        var prefab = Resources.Load<GameObject>("RocketBodies/" + key);
        if (prefab == null)
        {
            Debug.LogWarning("No body model found for '" + key + "' - keeping the generated hull.", this);
            builtBodyModelKey = null;
            foreach (var partName in new[] { "Engine", "Body", "NoseCone" })
            {
                var part = transform.Find(partName);
                if (part != null) part.gameObject.SetActive(true);
            }
            return;
        }

        var wrapper = new GameObject("Imported Body");
        wrapper.transform.SetParent(transform, worldPositionStays: false);
        wrapper.transform.localPosition = Vector3.zero;
        wrapper.transform.localRotation = Quaternion.identity;
        // The model is authored in real metres (Blender export); the rest
        // of the scene is kilometres - same scale-down every other
        // generated/imported piece in this project applies.
        wrapper.transform.localScale = Vector3.one * PlanetBody.WorldUnitsPerMeter;
        importedBody = Instantiate(prefab, wrapper.transform, false);
        importedBody.name = key;
    }

    public void PlaceOnAssemblyMount()
    {
        var mount = GameObject.Find("Rocket Assembly Root");
        if (mount == null) return;
        var halfHeight = (bodyHeight + noseHeight + engineHeight) * .5f * PlanetBody.WorldUnitsPerMeter;
        transform.SetPositionAndRotation(mount.transform.position + mount.transform.up * halfHeight,
            mount.transform.rotation);
    }

    private void OnValidate()
    {
        ApplyMass();

#if UNITY_EDITOR
        // AddComponent (used by BuildVisual the first time a child part is
        // created) is not allowed to run inside OnValidate's callstack -
        // Unity logs "SendMessage cannot be called during ... OnValidate".
        // Defer to the next editor tick instead of doing it in Play Mode
        // every frame, where OnValidate isn't in play anyway.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += DelayedBuildVisual;
            return;
        }
#endif
        BuildVisual();
    }

#if UNITY_EDITOR
    private void DelayedBuildVisual()
    {
        if (this == null)
        {
            return;
        }

        BuildVisual();
    }
#endif

    private void FixedUpdate()
    {
        if (!IsFinite(transform.position) || !IsFinite(body.linearVelocity) ||
            !IsFinite(body.angularVelocity))
        {
            Debug.LogError("Rocket physics became invalid. Restoring the Kenya launch position.", this);
            var planet = FindFirstObjectByType<PlanetBody>();
            if (planet != null)
            {
                PlaceAtLatitudeLongitude(
                    planet,
                    launchLatitudeDegrees,
                    launchLongitudeDegrees,
                    clearance: 25f);
            }

            SetLaunchClamp(true);
            return;
        }

        var flight=GetComponent<RocketFlightModel>();
        if(flight!=null)
        {
            if(engineEnabled && throttle>0)flight.Step(body,throttle,Time.fixedDeltaTime);
            return;
        }
        if (!engineEnabled || throttle <= 0f || fuelMass <= 0f)
        {
            return;
        }

        var thrust = maxThrust * throttle;
        var assembly=GetComponent<RocketAssemblyController>();
        if(assembly!=null && assembly.enabled && assembly.InstalledCount==0)return;
        var direction=assembly!=null && assembly.enabled ? assembly.ResultantThrustDirection : transform.up;
        body.AddForce(direction * thrust * PlanetBody.WorldUnitsPerMeter, ForceMode.Force);

        // mdot = F / (Isp * g0), using SI units: kg/s.
        var fuelUsed = thrust / (specificImpulse * StandardGravity) * Time.fixedDeltaTime;
        fuelMass = Mathf.Max(0f, fuelMass - fuelUsed);
        ApplyMass();

        if (fuelMass <= 0f)
        {
            engineEnabled = false;
        }
    }

    public void StartEngine(float requestedThrottle = 1f)
    {
        var assembly=GetComponent<RocketAssemblyController>();
        if(assembly!=null && assembly.enabled && assembly.InstalledCount==0)return;
        throttle = Mathf.Clamp01(requestedThrottle);
        var flight=GetComponent<RocketFlightModel>();
        engineEnabled = throttle > 0f && (flight!=null?flight.Prepare(throttle):fuelMass>0f);
        if (engineEnabled && !launchClampReleased)
        {
            SetLaunchClamp(false);
        }
    }

    public void ReturnToAssembly()
    {
        StopEngine();SetLaunchClamp(true);
        GetComponent<RocketFlightModel>()?.Refill();
    }

    public void StopEngine()
    {
        engineEnabled = false;
        throttle = 0f;
    }

    public void SetThrottle(float requestedThrottle)
    {
        throttle = Mathf.Clamp01(requestedThrottle);
        if (throttle <= 0f)
        {
            engineEnabled = false;
        }
    }

    /// <summary>
    /// Places the rocket just above a PlanetBody surface and points it away
    /// from the planet center. The planet radius is treated as metres.
    /// </summary>
    public void PlaceOnPlanetSurface(PlanetBody planet, float clearance = 1f)
    {
        if (planet == null || !IsFinite(planet.transform.position) ||
            float.IsNaN(planet.Radius) || float.IsInfinity(planet.Radius))
        {
            Debug.LogError("Rocket cannot be placed because the planet has an invalid position or radius.", this);
            return;
        }

        var outward = (transform.position - planet.transform.position).normalized;
        if (!IsFinite(outward) || outward.sqrMagnitude < 0.001f)
        {
            outward = Vector3.up;
        }

        var surfaceDistance = (planet.Radius + Mathf.Max(0f, clearance)) * PlanetBody.WorldUnitsPerMeter;
        transform.position = planet.transform.position + outward * surfaceDistance;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
    }

    /// <summary>
    /// Places the rocket at a latitude/longitude on a spherical planet.
    /// Coordinates use degrees: north/east are positive, south/west negative.
    /// </summary>
    public void PlaceAtLatitudeLongitude(
        PlanetBody planet,
        float latitudeDegrees,
        float longitudeDegrees,
        float clearance = 1f)
    {
        if (planet == null || !IsFinite(planet.transform.position) ||
            float.IsNaN(planet.Radius) || float.IsInfinity(planet.Radius))
        {
            Debug.LogError("Rocket cannot be placed because the planet has an invalid position or radius.", this);
            return;
        }

        var outward = planet.GetSurfaceNormal(latitudeDegrees, longitudeDegrees);
        transform.position = planet.GetSurfacePosition(latitudeDegrees, longitudeDegrees, clearance);
        transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
    }

    private void ApplyMass()
    {
        body ??= GetComponent<Rigidbody>();
        dryMass = Mathf.Max(0.001f, dryMass);
        fuelMass = Mathf.Max(0f, fuelMass);
        maxThrust = Mathf.Max(0f, maxThrust);
        specificImpulse = Mathf.Max(0.01f, specificImpulse);
        throttle = Mathf.Clamp01(throttle);
        body.useGravity = false;
        var flight=GetComponent<RocketFlightModel>();
        body.mass = flight!=null && flight.TotalMass>0 ? (float)flight.TotalMass : dryMass+fuelMass;
    }

    /// <summary>
    /// Builds a slender nose-cone/body/engine hull from generated meshes so
    /// the rocket reads as a rocket instead of a bare primitive cylinder.
    /// The root object keeps the collider and physics; the visible shape
    /// lives on three child objects rebuilt whenever the dimensions change.
    /// </summary>
    private void BuildVisual()
    {
        bodyDiameter = Mathf.Max(0.1f, bodyDiameter);
        bodyHeight = Mathf.Max(0.1f, bodyHeight);
        noseHeight = Mathf.Max(0f, noseHeight);
        engineHeight = Mathf.Max(0f, engineHeight);
        hullSegments = Mathf.Clamp(hullSegments, 8, 64);

        // The hull sizing is authored directly in metre-scaled child meshes
        // below, not via non-uniform transform scale - make sure a stale
        // scale (e.g. from before this shape existed) doesn't double it up.
        transform.localScale = Vector3.one;

        // The root was historically created as a primitive Cylinder; its own
        // mesh would otherwise show through/behind the generated hull.
        var rootRenderer = GetComponent<MeshRenderer>();
        if (rootRenderer != null)
        {
            rootRenderer.enabled = false;
        }

        var capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = gameObject.AddComponent<CapsuleCollider>();
        }

        var totalHeight = engineHeight + bodyHeight + noseHeight;
        capsule.direction = 1; // Y axis.
        capsule.center = Vector3.zero;
        capsule.radius = (bodyDiameter * 0.5f) * PlanetBody.WorldUnitsPerMeter;
        capsule.height = totalHeight * PlanetBody.WorldUnitsPerMeter;

        hullMaterial ??= CreateUnlitStandardMaterial(hullColor, 0.6f);
        engineMaterial ??= CreateUnlitStandardMaterial(new Color(0.12f, 0.12f, 0.13f), 0.3f);
        hullMaterial.color = hullColor;

        if (builtBodyDiameter == bodyDiameter && builtBodyHeight == bodyHeight &&
            builtNoseHeight == noseHeight && builtEngineHeight == engineHeight &&
            builtHullSegments == hullSegments)
        {
            return;
        }

        var radius = (bodyDiameter * 0.5f) * PlanetBody.WorldUnitsPerMeter;
        var engineHeightUnits = engineHeight * PlanetBody.WorldUnitsPerMeter;
        var bodyHeightUnits = bodyHeight * PlanetBody.WorldUnitsPerMeter;
        var noseHeightUnits = noseHeight * PlanetBody.WorldUnitsPerMeter;

        // Centred pivot: the stack spans from -totalHeight/2 to +totalHeight/2
        // locally, matching the primitive cylinder pivot this replaces so
        // every existing surface-placement call site keeps working unchanged.
        var baseY = -(totalHeight * PlanetBody.WorldUnitsPerMeter) * 0.5f;

        // Narrow at the exposed bottom tip, flaring out to match the body's
        // full radius where the two sections join - not the other way
        // around, or there would be a visible step where they meet.
        BuildPart("Engine", engineMaterial,
            BuildFrustum(radius * 0.6f, radius, engineHeightUnits, hullSegments),
            baseY);
        baseY += engineHeightUnits;

        BuildPart("Body", hullMaterial,
            BuildFrustum(radius, radius, bodyHeightUnits, hullSegments),
            baseY);
        baseY += bodyHeightUnits;

        BuildPart("NoseCone", hullMaterial,
            BuildFrustum(radius, 0f, noseHeightUnits, hullSegments),
            baseY);

        builtBodyDiameter = bodyDiameter;
        builtBodyHeight = bodyHeight;
        builtNoseHeight = noseHeight;
        builtEngineHeight = engineHeight;
        builtHullSegments = hullSegments;
    }

    private void BuildPart(string childName, Material material, Mesh mesh, float localY)
    {
        var child = transform.Find(childName);
        GameObject childObject;
        if (child == null)
        {
            childObject = new GameObject(childName);
            childObject.transform.SetParent(transform, worldPositionStays: false);
            childObject.AddComponent<MeshFilter>();
            childObject.AddComponent<MeshRenderer>();
        }
        else
        {
            childObject = child.gameObject;
        }

        childObject.transform.localPosition = new Vector3(0f, localY, 0f);
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;

        var meshFilter = childObject.GetComponent<MeshFilter>();
        mesh.name = childName + "Hull";
        meshFilter.sharedMesh = mesh;

        var renderer = childObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
    }

    /// <summary>
    /// Not private: ProceduralEngine reuses this for the Vostok-family
    /// engine visual instead of duplicating the same Standard-shader setup.
    /// </summary>
    internal static Material CreateUnlitStandardMaterial(Color color, float smoothness)
    {
        var material = new Material(Shader.Find("Standard"))
        {
            color = color
        };
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", 0.1f);
        return material;
    }

    /// <summary>
    /// Builds a tapered tube (a cylinder when bottomRadius equals topRadius,
    /// a cone when topRadius is 0) from local Y 0 to Y height, capped on
    /// whichever end has a non-zero radius. Not private: ProceduralEngine
    /// reuses this for the Vostok-family engine bell/chamber shapes.
    /// </summary>
    internal static Mesh BuildFrustum(float bottomRadius, float topRadius, float height, int segments)
    {
        var mesh = new Mesh();
        var stride = segments + 1;
        var slope = (bottomRadius - topRadius) / Mathf.Max(height, 0.0001f);

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        for (var ring = 0; ring < 2; ring++)
        {
            var y = ring == 0 ? 0f : height;
            var ringRadius = ring == 0 ? bottomRadius : topRadius;
            for (var i = 0; i <= segments; i++)
            {
                var t = (float)i / segments;
                var angle = t * Mathf.PI * 2f;
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                vertices.Add(new Vector3(cos * ringRadius, y, sin * ringRadius));
                normals.Add(new Vector3(cos, slope, sin).normalized);
                uvs.Add(new Vector2(t, ring));
            }
        }

        for (var i = 0; i < segments; i++)
        {
            var a = i;
            var b = a + stride;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(a + 1);

            triangles.Add(a + 1);
            triangles.Add(b);
            triangles.Add(b + 1);
        }

        AddCap(vertices, normals, uvs, triangles, bottomRadius, y: 0f, segments, facingDown: true);
        AddCap(vertices, normals, uvs, triangles, topRadius, y: height, segments, facingDown: false);

        mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static void AddCap(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        float capRadius,
        float y,
        int segments,
        bool facingDown)
    {
        if (capRadius <= 0.0001f)
        {
            return;
        }

        var normal = facingDown ? Vector3.down : Vector3.up;
        var centerIndex = vertices.Count;
        vertices.Add(new Vector3(0f, y, 0f));
        normals.Add(normal);
        uvs.Add(new Vector2(0.5f, 0.5f));

        var ringStart = vertices.Count;
        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var angle = t * Mathf.PI * 2f;
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            vertices.Add(new Vector3(cos * capRadius, y, sin * capRadius));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
        }

        for (var i = 0; i < segments; i++)
        {
            triangles.Add(centerIndex);
            if (facingDown)
            {
                triangles.Add(ringStart + i + 1);
                triangles.Add(ringStart + i);
            }
            else
            {
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + i + 1);
            }
        }
    }

    private void SetLaunchClamp(bool clamped)
    {
        if (clamped)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }
        else
        {
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        launchClampReleased = !clamped;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
