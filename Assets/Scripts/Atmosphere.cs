using System;
using UnityEngine;

/// <summary>
/// Adds a translucent, Fresnel-based atmospheric glow shell around a
/// PlanetBody: brightest at the grazing-angle limb (matching real photos of
/// Earth from space), tinted toward the sun-facing side and dim on the night
/// side, and nearly invisible looking straight down through it so the
/// surface underneath stays clear. Built the same self-configuring way
/// PlanetBody builds its own mesh - Awake/Reset/OnValidate rebuild the shell
/// from these fields, so nothing needs to be hand-wired in the scene.
///
/// Rendered as several nested shells rather than one, loosely echoing the
/// real troposphere/stratosphere/exosphere split: a thin bright band close
/// to the ground, a mid layer, and a faint wide outer glow. This is a
/// game-feel choice, not an attempt at a physically graded atmosphere - it
/// gives the limb visible banding instead of one flat gradient. It has no
/// effect on flight - RocketFlightModel's drag uses a plain, continuous
/// exponential density falloff, independent of how many shells are drawn.
/// </summary>
[RequireComponent(typeof(PlanetBody))]
public sealed class Atmosphere : MonoBehaviour
{
    [Serializable]
    public struct Layer
    {
        public string name;
        [Range(0.001f, 0.2f)] public float thicknessFraction;
        public Color rimColor;
        public Color dayColor;
        public Color nightColor;
        [Range(0.5f, 8f)] public float rimPower;
        [Range(0f, 4f)] public float intensity;
    }

    [Tooltip("Nested shells, innermost first. Each is a full copy of the " +
             "Fresnel shell shader with its own thickness/color/falloff, so " +
             "the limb reads as bands rather than one flat gradient.")]
    [SerializeField]
    private Layer[] layers =
    {
        new()
        {
            name = "Troposphere", thicknessFraction = 0.006f,
            rimColor = new Color(0.85f, 0.92f, 1f), dayColor = new Color(0.55f, 0.72f, 1f),
            nightColor = new Color(0.05f, 0.06f, 0.1f), rimPower = 2f, intensity = 2.2f,
        },
        new()
        {
            name = "Stratosphere", thicknessFraction = 0.014f,
            rimColor = new Color(0.5f, 0.7f, 1f), dayColor = new Color(0.3f, 0.5f, 1f),
            nightColor = new Color(0.04f, 0.04f, 0.1f), rimPower = 3f, intensity = 1.3f,
        },
        new()
        {
            name = "Exosphere", thicknessFraction = 0.026f,
            rimColor = new Color(0.4f, 0.55f, 1f), dayColor = new Color(0.2f, 0.35f, 0.9f),
            nightColor = new Color(0.02f, 0.02f, 0.08f), rimPower = 5f, intensity = 0.7f,
        },
    };

    [Range(8, 256)]
    [SerializeField] private int longitudeSegments = 64;

    [Range(4, 128)]
    [SerializeField] private int latitudeSegments = 32;

    private Mesh generatedMesh;
    private Light sun;
    private LayerInstance[] instances = Array.Empty<LayerInstance>();

    private int builtLongitudeSegments = -1;
    private int builtLatitudeSegments = -1;

    private struct LayerInstance
    {
        public Transform transform;
        public MeshRenderer renderer;
        public Material material;
    }

    private void Reset()
    {
        BuildShell();
    }

    private void Awake()
    {
        BuildShell();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        // AddComponent (used by BuildShell the first time the shell child is
        // created) is not allowed to run inside OnValidate's callstack -
        // Unity logs "SendMessage cannot be called during ... OnValidate".
        // Same fix as Rocket.BuildVisual: defer to the next editor tick.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += DelayedBuildShell;
            return;
        }
#endif
        BuildShell();
    }

#if UNITY_EDITOR
    private void DelayedBuildShell()
    {
        if (this == null)
        {
            return;
        }

        BuildShell();
    }
#endif

    private void Update()
    {
        ApplyMaterialProperties();
    }

    private void BuildShell()
    {
        longitudeSegments = Mathf.Clamp(longitudeSegments, 8, 256);
        latitudeSegments = Mathf.Clamp(latitudeSegments, 4, 128);
        if (layers == null || layers.Length == 0)
        {
            layers = new[] { new Layer
            {
                name = "Atmosphere", thicknessFraction = 0.025f,
                rimColor = new Color(0.55f, 0.75f, 1f), dayColor = new Color(0.35f, 0.55f, 1f),
                nightColor = new Color(0.05f, 0.05f, 0.12f), rimPower = 3f, intensity = 1.6f,
            } };
        }

        // Migration from the single-shell version: an old child literally
        // named "Atmosphere" is only valid if a layer still uses that exact
        // name; otherwise it is a leftover single shell from before layers
        // existed and would linger as a stray, unmanaged extra glow.
        var legacy = transform.Find("Atmosphere");
        if (legacy != null && Array.TrueForAll(layers, l => l.name != "Atmosphere"))
        {
            if (Application.isPlaying) Destroy(legacy.gameObject);
            else DestroyImmediate(legacy.gameObject);
        }

        if (generatedMesh == null ||
            builtLongitudeSegments != longitudeSegments ||
            builtLatitudeSegments != latitudeSegments)
        {
            generatedMesh = PlanetBody.BuildUvSphere(longitudeSegments, latitudeSegments, radius: 0.5f);
            generatedMesh.name = $"AtmosphereShell_{longitudeSegments}x{latitudeSegments}";
            builtLongitudeSegments = longitudeSegments;
            builtLatitudeSegments = latitudeSegments;
        }

        var shader = Shader.Find("Strauss Space/Atmosphere");
        var built = new LayerInstance[layers.Length];
        for (var i = 0; i < layers.Length; i++)
        {
            ref var layer = ref layers[i];
            layer.thicknessFraction = Mathf.Clamp(layer.thicknessFraction, 0.001f, 0.2f);
            var childName = string.IsNullOrEmpty(layer.name) ? $"Atmosphere Layer {i}" : layer.name;

            var child = transform.Find(childName);
            GameObject shellObject;
            if (child == null)
            {
                shellObject = new GameObject(childName);
                shellObject.transform.SetParent(transform, worldPositionStays: false);
                shellObject.transform.localPosition = Vector3.zero;
                shellObject.transform.localRotation = Quaternion.identity;
                shellObject.AddComponent<MeshFilter>();
                shellObject.AddComponent<MeshRenderer>();
            }
            else
            {
                shellObject = child.gameObject;
            }

            // Each layer is a child of the Planet object, so it inherits
            // PlanetBody's own transform.localScale (radius*2*WorldUnitsPerMeter)
            // - only the extra thickness fraction needs to be applied here.
            shellObject.transform.localScale = Vector3.one * (1f + layer.thicknessFraction);

            var meshFilter = shellObject.GetComponent<MeshFilter>();
            var meshRenderer = shellObject.GetComponent<MeshRenderer>();
            meshFilter.sharedMesh = generatedMesh;

            var material = meshRenderer.sharedMaterial;
            if (material == null || material.shader != shader)
            {
                material = shader != null ? new Material(shader) { name = childName + " (generated)" } : null;
                meshRenderer.sharedMaterial = material;
            }

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            built[i] = new LayerInstance { transform = shellObject.transform, renderer = meshRenderer, material = material };
        }
        instances = built;

        ApplyMaterialProperties();
    }

    private void ApplyMaterialProperties()
    {
        if (instances == null || instances.Length == 0 || layers == null)
        {
            return;
        }

        sun ??= FindSun();
        // Light.transform.forward is the direction the light TRAVELS (away
        // from the sun); the shader wants the direction TOWARD the sun.
        var sunDirection = sun != null ? -sun.transform.forward : Vector3.up;
        var sunVector = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f);

        for (var i = 0; i < instances.Length && i < layers.Length; i++)
        {
            var material = instances[i].material;
            if (material == null)
            {
                continue;
            }

            var layer = layers[i];
            material.SetColor("_RimColor", layer.rimColor);
            material.SetColor("_DayColor", layer.dayColor);
            material.SetColor("_NightColor", layer.nightColor);
            material.SetFloat("_RimPower", layer.rimPower);
            material.SetFloat("_Intensity", layer.intensity);
            material.SetVector("_SunDirection", sunVector);
        }
    }

    private static Light FindSun()
    {
        var sunObject = GameObject.Find("Sun");
        return sunObject != null ? sunObject.GetComponent<Light>() : null;
    }
}
