using UnityEngine;

/// <summary>
/// Gives the planet its NASA imagery at runtime (EarthSurface.shader): Blue
/// Marble day map with a water mask for sunglint, Black Marble city lights,
/// and a cloud layer that drifts slowly eastward with the mid-latitude winds.
/// </summary>
[RequireComponent(typeof(PlanetBody))]
public sealed class EarthVisuals : MonoBehaviour
{
    // Cloud drift: ~10 m/s average zonal wind is ~1/4,000,000 of the
    // circumference per second.
    private const float CloudDriftPerSecond = 2.5e-7f;
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
        material.SetTexture("_CloudTex", Resources.Load<Texture2D>("Earth/earth_clouds_2k"));
        renderer.sharedMaterial = material;
    }

    private void Update()
    {
        if (material == null) return;
        cloudOffset = (cloudOffset - Time.deltaTime * TimeWarp.ClockMultiplier * CloudDriftPerSecond) % 1f;
        material.SetFloat("_CloudOffset", cloudOffset);
    }
}
