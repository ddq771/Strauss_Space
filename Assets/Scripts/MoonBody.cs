using System;
using UnityEngine;

/// <summary>
/// The Moon, where it really is on the current date.
///
/// Position: Meeus' lunar series (Astronomical Algorithms, ch. 47 - the
/// main periodic terms), which beyond the basic ellipse (mean distance
/// 384,400 km, eccentricity ~0.055, 5.1° to Earth's orbit, one revolution per
/// 27.32-day sidereal month) carries the Sun's perturbations - evection,
/// variation, the annual equation - so distance (~356,500 to 406,700 km) and
/// direction come out to within about 0.1° and a few hundred km.
///
/// Tidally locked: the near side keeps facing the mean direction of Earth,
/// so the real libration shows (the true orbit runs ahead of and behind that
/// mean by up to ~7.9°), with the spin axis tilted 1.54° from the ecliptic
/// pole on the side opposite the orbit's pole (Cassini's laws).
///
/// Drawn three ways: the true-size textured sphere (NASA LRO colour map) at
/// its true distance, lit by the scene's Sun so phases are real - visible in
/// map view and beyond; from inside the near-Earth view, where the far clip
/// plane can't reach 384,000 km, the sky draws its disc instead, at the
/// right size, place and phase. Its gravity pulls on the rocket too.
/// </summary>
public sealed class MoonBody : MonoBehaviour
{
    // NASA Moon fact sheet: mean radius 1,737.4 km, GM 4,902.8 km³/s² (mass
    // 7.346e22 kg), surface gravity 1.62 m/s², escape speed 2.38 km/s,
    // sidereal month 27.3217 d, synodic month 29.53 d, mean distance
    // 384,400 km, eccentricity 0.0549, inclination 5.145° to the ecliptic,
    // spin axis 1.54° from the ecliptic pole.
    public const double RadiusMeters = 1737400;
    public const double GravitationalParameter = 4.9028e12;   // m³/s²
    public static double SurfaceGravity => GravitationalParameter / (RadiusMeters * RadiusMeters);   // 1.62 m/s²
    private const double ObliquityDeg = 23.4393;
    private const double CassiniTiltDeg = 1.543;
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static MoonBody Instance { get; private set; }
    public double DistanceMeters { get; private set; }
    public double IlluminatedFraction { get; private set; }
    public Vector3 WorldPosition => transform.position;

    private PlanetBody planet;
    private Rigidbody rocketBody;
    private Rocket rocket;
    private LineRenderer orbitLine;
    private const int OrbitSamples = 240;
    private double lastOrbitDays = double.NaN;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<MoonBody>() != null || FindFirstObjectByType<PlanetBody>() == null) return;
        var moon = new GameObject("Moon");
        moon.AddComponent<MeshFilter>().sharedMesh = PlanetBody.BuildUvSphere(128, 64, 0.5f);
        var renderer = moon.AddComponent<MeshRenderer>();
        // Lit by the Sun alone (MoonSurface.shader) - the scene's ambient
        // light is for daylight at the pad and would wash the phases out.
        var material = new Material(Shader.Find("Strauss Space/Moon Surface"));
        material.mainTexture = Resources.Load<Texture2D>("Moon/moon_lroc_color_4k") ?? Resources.Load<Texture2D>("Moon/moon_lroc_color_2k");
        material.SetTexture("_SlopeTex", Resources.Load<Texture2D>("Moon/moon_slope_normal_4k"));
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moon.transform.localScale = Vector3.one * (float)(2 * RadiusMeters * PlanetBody.WorldUnitsPerMeter);
        moon.AddComponent<MoonBody>();
    }

    private void Awake()
    {
        Instance = this;
        planet = FindFirstObjectByType<PlanetBody>();
        rocket = FindFirstObjectByType<Rocket>();
        rocketBody = rocket != null ? rocket.GetComponent<Rigidbody>() : null;
        orbitLine = new GameObject("Moon's Orbit").AddComponent<LineRenderer>();
        orbitLine.useWorldSpace = true;
        orbitLine.loop = true;
        orbitLine.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
        orbitLine.startColor = orbitLine.endColor = new Color(.75f, .75f, .8f, .6f);
        orbitLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbitLine.positionCount = 0;
        var mainTexture = GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        if (mainTexture != null) Shader.SetGlobalTexture("_MoonTex", mainTexture);
    }

    private void OnDestroy()
    {
        if (orbitLine != null) Destroy(orbitLine.gameObject);
    }

    private void LateUpdate()
    {
        var sol = SolarSystem.Instance;
        if (sol == null || planet == null) return;
        var date = sol.Date;
        var rotation = sol.RotationAngleRad;
        var centre = planet.transform.position;

        Position(date, out var eclipticDir, out var distance, out var meanLongitude, out var node);
        DistanceMeters = distance;
        var sceneDir = EclipticToScene(eclipticDir, rotation);
        transform.position = centre + sceneDir * (float)(distance * PlanetBody.WorldUnitsPerMeter);

        // Tidal lock: near side (the mesh's +X, longitude 0 of the LRO map)
        // toward the MEAN Earth direction, spin axis per Cassini's laws.
        var nodeRad = node * Mathf.Deg2Rad;
        var tilt = CassiniTiltDeg * Mathf.Deg2Rad;
        var spinAxis = new Vector3d(-Math.Sin(tilt) * Math.Sin(nodeRad), Math.Sin(tilt) * Math.Cos(nodeRad), Math.Cos(tilt));
        var toMeanEarth = new Vector3d(-Math.Cos(meanLongitude), -Math.Sin(meanLongitude), 0);
        var sceneSpin = EclipticToScene(spinAxis, rotation);
        var scenePrime = Vector3.ProjectOnPlane(EclipticToScene(toMeanEarth, rotation), sceneSpin).normalized;
        transform.rotation = Quaternion.LookRotation(Vector3.Cross(scenePrime, sceneSpin), sceneSpin);

        // Phase: fraction of the disc lit, from the Sun-Moon-Earth angle.
        var sunDir = SolarSystem.Instance.transform.forward * -1f;
        Shader.SetGlobalVector("_SunFallback", sunDir);
        var phaseAngle = Vector3.Angle(sunDir, -sceneDir) * Mathf.Deg2Rad;   // at the Moon, Sun vs Earth
        IlluminatedFraction = (1 + Math.Cos(phaseAngle)) / 2;

        // For the sky's disc (seen from the near-Earth view) and its texture.
        Shader.SetGlobalVector("_MoonPosition", new Vector4(transform.position.x, transform.position.y, transform.position.z,
            (float)(RadiusMeters * PlanetBody.WorldUnitsPerMeter)));
        Shader.SetGlobalMatrix("_MoonWorldToLocal", Matrix4x4.Rotate(Quaternion.Inverse(transform.rotation)));
        var view = FindFirstObjectByType<AssemblyViewCamera>();
        // Once the camera's far clip reaches the real sphere (map view), the
        // sky's stand-in disc would double it up.
        Shader.SetGlobalFloat("_MoonSkyDisc", view != null && view.MapBlend > .5f ? 0f : 1f);

        UpdateOrbitLine(date, rotation, centre, view);
    }

    private void FixedUpdate()
    {
        // The Moon's pull on the rocket. The scene is centred on Earth, which
        // is itself falling toward the Moon, so what acts here is the
        // difference (the tide): GM·((r_m − r)/|r_m − r|³ − r_m/|r_m|³).
        if (rocket == null || rocketBody == null || rocketBody.isKinematic || !rocket.Launched || planet == null) return;
        var centre = planet.transform.position;
        var rm = (Vector3d)((transform.position - centre) / PlanetBody.WorldUnitsPerMeter);
        var r = (Vector3d)((rocketBody.worldCenterOfMass - centre) / PlanetBody.WorldUnitsPerMeter);
        var d = rm - r;
        var a = d * (GravitationalParameter / Math.Pow(d.Length, 3)) - rm * (GravitationalParameter / Math.Pow(rm.Length, 3));
        rocketBody.AddForce(a.ToVector3() * PlanetBody.WorldUnitsPerMeter, ForceMode.Acceleration);
    }

    private void UpdateOrbitLine(DateTime date, double rotation, Vector3 centre, AssemblyViewCamera view)
    {
        if (view == null || view.MapBlend < .5f) { orbitLine.positionCount = 0; return; }
        // One sidereal month ahead, sampled through the full series - the
        // real (perturbed, slightly non-closing) path, not an idealised ellipse.
        orbitLine.positionCount = OrbitSamples;
        perigeeKm = double.MaxValue; apogeeKm = 0;
        for (var i = 0; i < OrbitSamples; i++)
        {
            Position(date.AddDays(27.321661 * i / OrbitSamples), out var dir, out var dist, out _, out _);
            var point = centre + EclipticToScene(dir, rotation) * (float)(dist * PlanetBody.WorldUnitsPerMeter);
            orbitLine.SetPosition(i, point);
            // Closest and farthest points of this month's path (they shift
            // from month to month: the Sun stretches the orbit, perigee
            // ranges ~356,400-370,400 km and apogee ~404,000-406,700 km).
            if (dist / 1000 < perigeeKm) { perigeeKm = dist / 1000; perigeePoint = point; }
            if (dist / 1000 > apogeeKm) { apogeeKm = dist / 1000; apogeePoint = point; }
        }
        var camera = view.GetComponent<Camera>();
        orbitLine.widthMultiplier = Vector3.Distance(camera.transform.position, centre) * .0012f;
    }

    /// <summary>
    /// Geocentric ecliptic direction (unit) and distance (m) of the Moon, plus
    /// its mean longitude (rad) and the longitude of its orbit's ascending
    /// node (deg), from Meeus' main periodic terms.
    /// </summary>
    public static void Position(DateTime utc, out Vector3d direction, out double distance, out double meanLongitude, out double node)
    {
        var T = (utc - J2000).TotalDays / 36525.0;
        double Deg(double x) => (x % 360 + 360) % 360 * Math.PI / 180;
        var L = Deg(218.3164477 + 481267.88123421 * T);
        var D = Deg(297.8501921 + 445267.1114034 * T);
        var M = Deg(357.5291092 + 35999.0502909 * T);
        var Mp = Deg(134.9633964 + 477198.8675055 * T);
        var F = Deg(93.2720950 + 483202.0175233 * T);
        node = 125.0445479 - 1934.1362891 * T;

        var lon = L + Math.PI / 180 * (
            6.288774 * Math.Sin(Mp) + 1.274027 * Math.Sin(2 * D - Mp) + 0.658314 * Math.Sin(2 * D)
            + 0.213618 * Math.Sin(2 * Mp) - 0.185116 * Math.Sin(M) - 0.114332 * Math.Sin(2 * F)
            + 0.058793 * Math.Sin(2 * D - 2 * Mp) + 0.057066 * Math.Sin(2 * D - M - Mp) + 0.053322 * Math.Sin(2 * D + Mp)
            + 0.045758 * Math.Sin(2 * D - M) - 0.040923 * Math.Sin(M - Mp) - 0.034720 * Math.Sin(D)
            - 0.030383 * Math.Sin(M + Mp));
        var lat = Math.PI / 180 * (
            5.128122 * Math.Sin(F) + 0.280602 * Math.Sin(Mp + F) + 0.277693 * Math.Sin(Mp - F)
            + 0.173237 * Math.Sin(2 * D - F) + 0.055413 * Math.Sin(2 * D - Mp + F) + 0.046271 * Math.Sin(2 * D - Mp - F));
        distance = 1000 * (385000.56 - 20905.355 * Math.Cos(Mp) - 3699.111 * Math.Cos(2 * D - Mp) - 2955.968 * Math.Cos(2 * D)
            - 569.925 * Math.Cos(2 * Mp) + 48.888 * Math.Cos(M) - 3.149 * Math.Cos(2 * F) + 246.158 * Math.Cos(2 * D - 2 * Mp)
            - 152.138 * Math.Cos(2 * D - M - Mp) - 170.733 * Math.Cos(2 * D + Mp) - 204.586 * Math.Cos(2 * D - M)
            - 129.620 * Math.Cos(M - Mp) + 108.743 * Math.Cos(D) + 104.755 * Math.Cos(M + Mp));
        direction = new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
        meanLongitude = L;
    }

    // Ecliptic (x toward the March equinox, z ecliptic north) to the
    // Earth-fixed scene at Earth's current rotation angle.
    private static Vector3 EclipticToScene(Vector3d v, double rotation)
    {
        var e = ObliquityDeg * Math.PI / 180;
        var y = v.y * Math.Cos(e) - v.z * Math.Sin(e);
        var z = v.y * Math.Sin(e) + v.z * Math.Cos(e);
        return SolarSystem.EquatorialToScene(v.x, y, z, rotation);
    }

    private GUIStyle labelStyle;
    private double perigeeKm, apogeeKm;
    private Vector3 perigeePoint, apogeePoint;
    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        var view = FindFirstObjectByType<AssemblyViewCamera>();
        if (view == null || view.MapBlend < .5f) return;
        var camera = view.GetComponent<Camera>();
        labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        if (view.SolarBlend > .3f) return;   // next to Earth's label at that scale
        Label(camera, transform.position, "Moon · " + (DistanceMeters / 1000).ToString("N0") + " km · " +
            (IlluminatedFraction * 100).ToString("F0") + "% lit · g 1.62 m/s²", new Color(.85f, .85f, .9f));
        if (orbitLine != null && orbitLine.positionCount > 0 && view.SolarBlend < .3f)
        {
            Label(camera, perigeePoint, "◆ Perigee " + perigeeKm.ToString("N0") + " km", new Color(1f, .75f, .45f));
            Label(camera, apogeePoint, "◆ Apogee " + apogeeKm.ToString("N0") + " km", new Color(.55f, .75f, 1f));
        }
    }

    private void Label(Camera camera, Vector3 world, string text, Color color)
    {
        var vp = camera.WorldToViewportPoint(world);
        if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) return;
        labelStyle.normal.textColor = color;
        GUI.Label(new Rect(vp.x * Screen.width - 6, (1 - vp.y) * Screen.height - 10, 360, 22), text, labelStyle);
    }

    public readonly struct Vector3d
    {
        public readonly double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public Vector3 ToVector3() => new((float)x, (float)y, (float)z);
        public static explicit operator Vector3d(Vector3 v) => new(v.x, v.y, v.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.x * s, a.y * s, a.z * s);
    }
}
