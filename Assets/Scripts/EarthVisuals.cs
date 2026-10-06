using UnityEngine;

/// <summary>
/// Gives the planet its NASA imagery at runtime (EarthSurface.shader): Blue
/// Marble day map with a water mask for sunglint, Black Marble city lights,
/// and a cloud layer that drifts slowly eastward with the mid-latitude winds.
/// The clouds are drawn at CloudAltitudeKm by the atmosphere shell
/// (Atmosphere.shader) with their scattering, and cast shadows on the
/// ground (EarthSurface.shader); both read the shader globals set here.
/// </summary>
[RequireComponent(typeof(PlanetBody))]
public sealed class EarthVisuals : MonoBehaviour
{
    // Cloud drift: ~10 m/s average zonal wind is ~1/4,000,000 of the
    // circumference per second.
    private const float CloudDriftPerSecond = 2.5e-7f;
    // One layer standing in for all of them: low cumulus tops ~2 km, mid
    // cloud ~4-6 km, cirrus ~10 km.
    private const float CloudAltitudeKm = 4f;
    private Material material;
    private float cloudOffset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        var planet = FindFirstObjectByType<PlanetBody>();
        if (planet != null && planet.GetComponent<EarthVisuals>() == null) planet.gameObject.AddComponent<EarthVisuals>();
    }

    private void Start()
    {
        var renderer = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Strauss Space/Earth Surface");
        var day = Resources.Load<Texture2D>("Earth/earth_day_4k");
        if (renderer == null || shader == null || day == null) return;
        material = new Material(shader) { name = "Earth (NASA Blue/Black Marble)" };
        material.SetTexture("_DayTex", day);
        material.SetTexture("_NightTex", Resources.Load<Texture2D>("Earth/earth_night_4k"));
        var clouds = Resources.Load<Texture2D>("Earth/earth_clouds_2k");
        material.SetTexture("_CloudTex", clouds);
        renderer.sharedMaterial = material;
        Shader.SetGlobalTexture("_CloudTex", clouds);
        Shader.SetGlobalFloat("_CloudsEnabled", clouds != null ? 1 : 0);
    }

    private void Update()
    {
        if (material == null) return;
        cloudOffset = (cloudOffset - Time.deltaTime * TimeWarp.ClockMultiplier * CloudDriftPerSecond) % 1f;
        material.SetFloat("_CloudOffset", cloudOffset);
        var planet = GetComponent<PlanetBody>();
        Shader.SetGlobalFloat("_CloudOffset", cloudOffset);
        Shader.SetGlobalFloat("_CloudAltitude", CloudAltitudeKm * 1000f * PlanetBody.WorldUnitsPerMeter);
        Shader.SetGlobalFloat("_CloudShadowScale", CloudAltitudeKm * 1000f / Mathf.Max(1f, planet.Radius));
        Shader.SetGlobalMatrix("_PlanetRotation", Matrix4x4.Rotate(Quaternion.Inverse(transform.rotation)));
    }
}
