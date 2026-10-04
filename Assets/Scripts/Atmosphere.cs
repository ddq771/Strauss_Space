using UnityEngine;

/// <summary>
/// Draws the planet's atmosphere as one physically based scattering shell
/// (see Shaders/Atmosphere.shader): Rayleigh + Mie single scattering through
/// an air density that falls off exponentially with altitude, using real
/// Earth values by default. Because density is continuous there are no
/// visible layer boundaries - the sky blends smoothly from blue at the
/// ground, through the darkening stratosphere, to black space - and the same
/// model produces the blue limb seen from orbit, red-orange sunrise/sunset
/// along the terminator, the glow around the sun and the haze over distant
/// ground.
///
/// This replaced an earlier set of three nested Fresnel glow shells
/// (troposphere/stratosphere/exosphere), whose hard edges showed as bands.
/// Visual only: RocketFlightModel's drag has its own density model.
/// </summary>
[RequireComponent(typeof(PlanetBody))]
public sealed class Atmosphere : MonoBehaviour
{
    private const string ShellName = "Atmosphere Scattering";
    private static readonly string[] LegacyShells = { "Atmosphere", "Troposphere", "Stratosphere", "Exosphere" };

    [Tooltip("Where scattering is cut off. Above ~100 km (the Kármán line) " +
             "the air is too thin to visibly scatter light.")]
    [SerializeField] private float atmosphereHeightKm = 100f;
    [Tooltip("Altitude over which air (molecule) density drops by a factor of e. Earth: ~8 km.")]
    [SerializeField] private float rayleighScaleHeightKm = 8f;
    [Tooltip("Altitude over which haze/aerosol density drops by a factor of e. Earth: ~1.2 km.")]
    [SerializeField] private float mieScaleHeightKm = 1.2f;
    [Tooltip("Sea-level Rayleigh scattering for red/green/blue light " +
             "(×10⁻⁶ per metre). Blue scatters ~6× more than red - why the sky is blue.")]
    [SerializeField] private Vector3 rayleighScattering = new(5.8f, 13.5f, 33.1f);
    [Tooltip("Sea-level ozone absorption for red/green/blue light (×10⁻⁶ per " +
             "metre). Absorbs orange-red, deepening the zenith blue and twilight.")]
    [SerializeField] private Vector3 ozoneAbsorption = new(0.65f, 1.881f, 0.085f);
    [Tooltip("Sea-level Mie (haze) scattering (×10⁻⁶ per metre). ~4 is a clear " +
             "day, ~20 noticeably hazy.")]
    [SerializeField] private float mieScattering = 4f;
    [Tooltip("How strongly haze scatters forward (toward the sun's direction). " +
             "Controls the size of the glow around the sun.")]
    [Range(0f, 0.99f)] [SerializeField] private float mieAnisotropy = 0.8f;
    [Tooltip("Sunlight strength for scattering, per unit of the Sun light's " +
             "intensity. ~11 keeps sky vs. sunlit ground at their real-world ratio.")]
    [SerializeField] private float sunIntensity = 11f;
    [Tooltip("Colour lift for the scattered light - single scattering shown " +
             "in Gamma colour space reads greyer than a real sky. 1 = none.")]
    [SerializeField] private float saturation = 1.3f;
    [Range(16, 256)] [SerializeField] private int longitudeSegments = 96;
    [Range(8, 128)] [SerializeField] private int latitudeSegments = 48;

    private PlanetBody planet;
    private Light sun;
    private Material material;
    private Mesh mesh;

    private void Reset() => BuildShell();
    private void Awake() => BuildShell();

    private void OnValidate()
    {
#if UNITY_EDITOR
        // AddComponent isn't allowed inside OnValidate's callstack - defer
        // to the next editor tick, same as Rocket.BuildVisual.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) BuildShell(); };
            return;
        }
#endif
        BuildShell();
    }

    private void BuildShell()
    {
        planet = GetComponent<PlanetBody>();
        foreach (var legacyName in LegacyShells)
        {
            var legacy = transform.Find(legacyName);
            if (legacy == null) continue;
            if (Application.isPlaying) Destroy(legacy.gameObject);
            else DestroyImmediate(legacy.gameObject);
        }

        var child = transform.Find(ShellName);
        if (child == null)
        {
            child = new GameObject(ShellName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            child.SetParent(transform, worldPositionStays: false);
        }
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        // Child of the planet, so it inherits its radius*2 scale; make the
        // geometry a little larger than the atmosphere top so the shader's
        // analytic sphere is always fully covered.
        child.localScale = Vector3.one * (1f + atmosphereHeightKm * 1000f / planet.Radius * 1.05f);

        if (mesh == null) mesh = PlanetBody.BuildUvSphere(longitudeSegments, latitudeSegments, radius: 0.5f);
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Strauss Space/Atmosphere");
        if (material == null || material.shader != shader)
            material = shader != null ? new Material(shader) { name = ShellName + " (generated)" } : null;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ApplyMaterialProperties();
    }

    private void Update() => ApplyMaterialProperties();

    private void ApplyMaterialProperties()
    {
        if (material == null || planet == null) return;
        // The shader stops each view ray at scene depth (haze over distant
        // ground, but not painted over the rocket in front of it).
        foreach (var camera in Camera.allCameras)
            camera.depthTextureMode |= DepthTextureMode.Depth;

        sun ??= GameObject.Find("Sun")?.GetComponent<Light>();
        // Light.forward is the direction light travels; the shader wants the
        // direction toward the sun.
        var toSun = sun != null ? -sun.transform.forward : Vector3.up;
        // Shader lengths are world units: convert km and per-metre values.
        var unitsPerKm = 1000f * PlanetBody.WorldUnitsPerMeter;
        var perUnit = 1e-6f / PlanetBody.WorldUnitsPerMeter;
        var radius = planet.Radius * PlanetBody.WorldUnitsPerMeter;
        material.SetVector("_PlanetCenter", transform.position);
        material.SetFloat("_PlanetRadius", radius);
        material.SetFloat("_AtmosphereRadius", radius + atmosphereHeightKm * unitsPerKm);
        material.SetVector("_RayleighScatter", rayleighScattering * perUnit);
        material.SetVector("_OzoneAbsorb", ozoneAbsorption * perUnit);
        material.SetFloat("_RayleighHeight", rayleighScaleHeightKm * unitsPerKm);
        material.SetFloat("_MieScatter", mieScattering * perUnit);
        material.SetFloat("_MieHeight", mieScaleHeightKm * unitsPerKm);
        material.SetFloat("_MieG", mieAnisotropy);
        material.SetFloat("_SunIntensity", sunIntensity * (sun != null ? sun.intensity : 1f));
        material.SetFloat("_Saturation", saturation);
        material.SetVector("_SunDirection", toSun);
    }
}
