using System;
using UnityEngine;

/// <summary>
/// Earth's real orbit around the Sun: an ellipse of semi-major axis 1 AU
/// (149.6 million km, eccentricity 0.0167 - 147.1 million km at perihelion in
/// early January, 152.1 at aphelion in July), one revolution per sidereal
/// year (365.256 days), with Earth's axis tilted 23.44° to the orbit - which
/// is what moves the Sun north and south through the seasons.
///
/// The scene stays centred on Earth: at 1 AU from the origin, 32-bit float
/// positions would only resolve ~10 km, far too coarse for a rocket, so
/// (like KSP or Orbiter) the frame follows the body the player is near and
/// the Sun moves around it instead. Relative to Earth that is the same
/// motion. The Sun's direction drives the scene's directional light (and
/// with it the atmosphere and day/night), its distance scales sunlight by
/// the inverse square, and the sky draws its disc at the true angular size.
///
/// Earth also spins once per sidereal day (PlanetBody.RotationRate), so the
/// Sun rises and sets over the Earth-fixed scene and the stars wheel
/// overhead; over a year the two motions add up to the 24-hour solar day.
/// The scene starts on today's date, with Earth's rotation angle chosen so
/// the Sun starts over the same longitude the scene was lit from.
/// In map view Earth's spin axis is drawn; zoomed out to the solar system
/// view, the Sun (true size, true distance) and Earth's orbit are drawn too.
/// Rendering at 1 AU is fine - only the rocket's physics needs the
/// precision near the origin.
/// Sun's gravity on the rocket is left out: in Earth's free-falling frame it
/// only acts as a tide of ~10⁻⁶ m/s² near Earth.
/// </summary>
public sealed class SolarSystem : MonoBehaviour
{
    public const double AstronomicalUnit = 1.495978707e11;   // m
    private const double SunGravitationalParameter = 1.32712440018e20; // m³/s²
    private const double SunRadius = 6.957e8;                 // m
    // Earth's orbital elements (J2000, Meeus / Astronomical Almanac).
    private const double SemiMajorAxisAu = 1.00000261;
    private const double Eccentricity = 0.01671022;
    private const double PerihelionLongitudeDeg = 102.93735;
    private const double MeanAnomalyAtJ2000Deg = 357.52911;
    private const double MeanMotionDegPerDay = 0.98560028;   // 360° / 365.256 d
    private const double ObliquityDeg = 23.4393;
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Tooltip("Start on the real current date (UTC); otherwise on Start Date.")]
    [SerializeField] private bool startToday = true;
    [SerializeField] private string startDate = "2026-06-21";
    [Tooltip("Sun light intensity at exactly 1 AU; scaled by (1 AU / distance)².")]
    [SerializeField] private float intensityAtOneAu = 1.1f;

    private Light sun;
    private PlanetBody planet;
    private double earthRotationRad;       // Earth's rotation angle at startDateUtc
    private DateTime startDateUtc;
    private bool initialized;
    private LineRenderer axisLine, orbitLine;
    private GameObject sunSphere;
    private AssemblyViewCamera view;
    private const int OrbitSegments = 360;
    public static SolarSystem Instance { get; private set; }
    /// <summary>The Sun's position in the (Earth-centred, Earth-fixed) scene.</summary>
    public Vector3 SunWorldPosition { get; private set; }
    public double RotationAngleRad => RotationAngle;
    // Earth's rotation angle now (radians): the sidereal angle that turns
    // right ascension into longitude over the ground.
    private double RotationAngle =>
        earthRotationRad + (planet != null ? planet.RotationRate : 0) * (Date - startDateUtc).TotalSeconds;

    public DateTime Date { get; private set; }
    public double DistanceMeters { get; private set; }
    public double OrbitalSpeed { get; private set; }         // m/s, Earth around the Sun
    public double SubsolarLatitude { get; private set; }     // deg = the Sun's declination
    /// <summary>Sunlight strength at Earth's distance today (inverse square),
    /// before any atmosphere or shadow - what the atmosphere shader uses.</summary>
    public float SunIrradianceScale { get; private set; } = 1.1f;
    public double SunAngularDiameterDeg => 2 * Math.Asin(SunRadius / DistanceMeters) * Mathf.Rad2Deg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        var sunObject = GameObject.Find("Sun");
        if (sunObject != null && sunObject.GetComponent<SolarSystem>() == null) sunObject.AddComponent<SolarSystem>();
    }

    private void Start() => Initialize();

    // Also re-run after a script reload during Play mode: Unity restores the
    // old component's saved fields (so 'initialized' can come back true) but
    // not the date, nor anything a newer version of this script added.
    private bool NeedsInitialize =>
        !initialized || Date == default || axisLine == null || orbitLine == null || sunSphere == null;

    private void Initialize()
    {
        sun = GetComponent<Light>();
        planet = FindFirstObjectByType<PlanetBody>();
        Date = startToday ? DateTime.UtcNow
            : DateTime.TryParse(startDate, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.UtcNow;
        // Keep the scene's designed lighting at startup: pick the frozen Earth
        // rotation that puts today's Sun over the longitude it was lit from.
        if (planet != null && sun != null)
        {
            var toSun = -transform.forward;
            var sceneLongitude = Math.Atan2(toSun.z, toSun.x);
            SunEquatorial(Date, out var rightAscension, out _, out _);
            earthRotationRad = rightAscension - sceneLongitude;
        }
        startDateUtc = Date;
        view = FindFirstObjectByType<AssemblyViewCamera>();
        if (axisLine == null) axisLine = MakeLine("Earth Spin Axis", new Color(1f, .45f, .35f, .9f), false);
        if (orbitLine == null) orbitLine = MakeLine("Earth's Orbit", new Color(.45f, .75f, 1f, .8f), true);
        if (sunSphere == null) sunSphere = MakeSun();
        Instance = this;
        initialized = true;
        Apply();
    }

    private void Update()
    {
        if (NeedsInitialize) Initialize();
        // Game time, including physics warp (×2..×10, via timeScale) and
        // rails warp (×100, ×1000).
        Date = Date.AddSeconds(Time.deltaTime * TimeWarp.ClockMultiplier);
        Apply();
    }

    private void Apply()
    {
        SunEquatorial(Date, out var rightAscension, out var declination, out var distance);
        DistanceMeters = distance;
        SubsolarLatitude = declination * Mathf.Rad2Deg;
        OrbitalSpeed = Math.Sqrt(SunGravitationalParameter * (2 / distance - 1 / (SemiMajorAxisAu * AstronomicalUnit)));
        var angle = RotationAngle;
        var longitude = (rightAscension - angle) * Mathf.Rad2Deg;
        var toSun = planet != null
            ? planet.GetSurfaceNormal((float)SubsolarLatitude, (float)longitude)
            : Vector3.up;
        transform.rotation = Quaternion.LookRotation(-toSun);
        var au = distance / AstronomicalUnit;
        SunIrradianceScale = (float)(intensityAtOneAu / (au * au));
        Shader.SetGlobalVector("_SunWorldDir", toSun);
        UpdateSceneLighting(toSun);
        Shader.SetGlobalVector("_SolarDirection", new Vector4(toSun.x, toSun.y, toSun.z, (float)(SunAngularDiameterDeg * .5 * Mathf.Deg2Rad)));
        // Stars are fixed in inertial space: the sky turns with the same angle.
        Shader.SetGlobalFloat("_SkyAngle", (float)(angle % (2 * Math.PI)));
        UpdateAxisDisplay(angle, toSun, distance);
        // The sky's Sun disc is drawn in the Earth->Sun direction; from out in
        // the solar system view that's the wrong place, and the true-size
        // sphere shows the Sun instead.
        if (view != null && view.SolarBlend > .05f) Shader.SetGlobalVector("_SolarDirection", Vector4.zero);
    }

    // The Sun is the only light. Ambient light is the sky: sunlight
    // scattered by the air, so it's there by day near the ground and fades
    // through twilight to (almost) nothing at night, in the upper atmosphere
    // and in space. Direct sunlight reaching where the camera is looking is
    // reddened by the air it crosses (orange low suns) and is cut off in
    // Earth's shadow: below the horizon on the ground, inside the shadow
    // cylinder in orbit. Planet and Moon shaders light themselves from
    // _SunWorldDir and aren't affected by that cut-off.
    private void UpdateSceneLighting(Vector3 toSun)
    {
        if (sun == null || planet == null) return;
        if (view == null) view = FindFirstObjectByType<AssemblyViewCamera>();
        var camera = view != null ? view.GetComponent<Camera>() : Camera.main;
        var u = PlanetBody.WorldUnitsPerMeter;
        var centre = planet.transform.position;
        var mapView = view != null && view.MapBlend > .5f;
        var probe = camera != null ? camera.transform.position : centre + Vector3.up * planet.Radius * u;
        if (!mapView)
        {
            var rocket = FindFirstObjectByType<Rocket>();
            if (rocket != null) probe = rocket.transform.position;
        }
        var r = (probe - centre) / u;
        var altitude = r.magnitude - planet.Radius;
        var up = r.normalized;
        var sinElevation = Vector3.Dot(up, toSun);
        var air = Mathf.Exp(-Mathf.Max(0f, altitude) / 8000f);

        // Direct sunlight: Earth's shadow, then atmospheric extinction.
        var along = Vector3.Dot(r, toSun);
        var inShadow = altitude > 100000f
            ? along < 0 && (r - along * toSun).magnitude < planet.Radius
            : sinElevation < -.01f;
        var visible = mapView ? 1f : inShadow ? 0f : altitude > 100000f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.01f, .02f, sinElevation));
        var elevationDeg = Mathf.Asin(Mathf.Clamp(sinElevation, -1f, 1f)) * Mathf.Rad2Deg;
        var airmass = 1f / (Mathf.Max(sinElevation, 0f) + .50572f * Mathf.Pow(Mathf.Max(elevationDeg + 6.07995f, .5f), -1.6364f));
        var tau = new Vector3(.15f, .21f, .36f) * airmass * air;   // Rayleigh + haze, sea level
        var tint = mapView || altitude > 100000f ? Color.white
            : new Color(Mathf.Exp(-tau.x), Mathf.Exp(-tau.y), Mathf.Exp(-tau.z));
        var peak = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
        sun.color = peak > 0 ? tint / peak : Color.white;
        sun.intensity = SunIrradianceScale * visible * peak;

        // Skylight: only where there's air above and the Sun is up (or just
        // set - twilight).
        var daylight = mapView ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.12f, .3f, sinElevation)) * air;
        const float night = .006f;   // starlight / airglow
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.42f, .55f, .72f) * (.55f * daylight) + Color.white * night;
        RenderSettings.ambientEquatorColor = new Color(.40f, .44f, .47f) * (.45f * daylight) + Color.white * night;
        RenderSettings.ambientGroundColor = new Color(.22f, .20f, .17f) * (.40f * daylight) + Color.white * night * .5f;
        RenderSettings.reflectionIntensity = Mathf.Clamp01(daylight);
    }

    private static LineRenderer MakeLine(string name, Color color, bool loop)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = loop;
        line.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        return line;
    }

    private static GameObject MakeSun()
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Sun (true size)";
        Destroy(sphere.GetComponent<Collider>());
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, .93f, .78f) };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        sphere.transform.localScale = Vector3.one * (float)(2 * SunRadius * PlanetBody.WorldUnitsPerMeter);
        return sphere;
    }

    // Inertial equatorial vector (x toward the March equinox, z north) to
    // the Earth-fixed scene's axes at Earth's current rotation angle.
    public static Vector3 EquatorialToScene(double x, double y, double z, double rotation)
    {
        var lon = Math.Atan2(y, x) - rotation;
        var h = Math.Sqrt(x * x + y * y);
        return new Vector3((float)(h * Math.Cos(lon)), (float)z, (float)(h * Math.Sin(lon)));
    }

    // Earth's heliocentric position (m, inertial equatorial) at true anomaly nu.
    private static void EarthHeliocentric(double nu, out double x, out double y, out double z)
    {
        var a = SemiMajorAxisAu * AstronomicalUnit;
        var r = a * (1 - Eccentricity * Eccentricity) / (1 + Eccentricity * Math.Cos(nu));
        var lambda = nu + PerihelionLongitudeDeg * Mathf.Deg2Rad;
        var tilt = ObliquityDeg * Mathf.Deg2Rad;
        x = r * Math.Cos(lambda);
        y = r * Math.Sin(lambda) * Math.Cos(tilt);
        z = r * Math.Sin(lambda) * Math.Sin(tilt);
    }

    // Map view: Earth's spin axis through the poles. Always: the true-size
    // Sun at its true distance, and Earth's yearly orbit around it (visible
    // once zoomed out to the solar system view; beyond the far clip plane
    // otherwise).
    private void UpdateAxisDisplay(double angle, Vector3 toSun, double distance)
    {
        if (axisLine == null || orbitLine == null || sunSphere == null || planet == null) return;
        if (view == null) view = FindFirstObjectByType<AssemblyViewCamera>();
        var centre = planet.transform.position;
        SunWorldPosition = centre + toSun * (float)(distance * PlanetBody.WorldUnitsPerMeter);
        sunSphere.transform.position = SunWorldPosition;
        var camera = view != null ? view.GetComponent<Camera>() : Camera.main;
        var solar = view != null ? view.SolarBlend : 0f;

        // Earth's orbit, drawn through Earth's current position.
        if (solar > 0f)
        {
            var days = (Date - J2000).TotalDays;
            var meanAnomaly = (MeanAnomalyAtJ2000Deg + MeanMotionDegPerDay * days) * Mathf.Deg2Rad;
            EarthHeliocentric(TrueAnomaly(meanAnomaly), out var nx, out var ny, out var nz);
            orbitLine.positionCount = OrbitSegments;
            for (var i = 0; i < OrbitSegments; i++)
            {
                EarthHeliocentric(2 * Math.PI * i / OrbitSegments, out var x, out var y, out var z);
                orbitLine.SetPosition(i, centre + EquatorialToScene(x - nx, y - ny, z - nz, angle) * PlanetBody.WorldUnitsPerMeter);
            }
            // Perihelion (true anomaly 0, early January) and aphelion (early July).
            EarthHeliocentric(0, out var px, out var py, out var pz);
            EarthHeliocentric(Math.PI, out var ax, out var ay, out var az);
            perihelionPoint = centre + EquatorialToScene(px - nx, py - ny, pz - nz, angle) * PlanetBody.WorldUnitsPerMeter;
            aphelionPoint = centre + EquatorialToScene(ax - nx, ay - ny, az - nz, angle) * PlanetBody.WorldUnitsPerMeter;
            if (camera != null) orbitLine.widthMultiplier = Vector3.Distance(camera.transform.position, SunWorldPosition) * .0012f;
        }
        else orbitLine.positionCount = 0;

        var showAxis = view != null && view.MapBlend > .5f && solar < .5f;
        if (!showAxis) { axisLine.positionCount = 0; return; }
        var radius = planet.Radius * PlanetBody.WorldUnitsPerMeter;
        var up = planet.transform.up;
        axisLine.positionCount = 2;
        axisLine.SetPosition(0, centre - up * radius * 1.35f);
        axisLine.SetPosition(1, centre + up * radius * 1.35f);
        axisLine.widthMultiplier = Vector3.Distance(camera.transform.position, centre) * .0012f * 1.4f;
        axisLabel = centre + up * radius * 1.42f;
    }

    private static double TrueAnomaly(double meanAnomaly)
    {
        var E = meanAnomaly;
        for (var i = 0; i < 8; i++) E -= (E - Eccentricity * Math.Sin(E) - meanAnomaly) / (1 - Eccentricity * Math.Cos(E));
        return 2 * Math.Atan2(Math.Sqrt(1 + Eccentricity) * Math.Sin(E / 2), Math.Sqrt(1 - Eccentricity) * Math.Cos(E / 2));
    }
    private Vector3 axisLabel, perihelionPoint, aphelionPoint;

    private GUIStyle mapLabelStyle;
    private void MapLabel(Camera camera, Vector3 world, string text, Color color)
    {
        var vp = camera.WorldToViewportPoint(world);
        if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) return;
        mapLabelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        mapLabelStyle.normal.textColor = color;
        GUI.Label(new Rect(vp.x * Screen.width + 6, (1 - vp.y) * Screen.height - 10, 300, 22), text, mapLabelStyle);
    }

    private void OnDestroy()
    {
        if (axisLine != null) Destroy(axisLine.gameObject);
        if (orbitLine != null) Destroy(orbitLine.gameObject);
        if (sunSphere != null) Destroy(sunSphere);
    }

    /// <summary>
    /// The Sun's right ascension and declination (radians) and distance (m)
    /// as seen from Earth at a UTC date: Earth's position on its Kepler
    /// orbit, seen from the other side, rotated from the ecliptic into the
    /// equatorial frame by the axial tilt.
    /// </summary>
    public static void SunEquatorial(DateTime utc, out double rightAscension, out double declination, out double distance)
    {
        var days = (utc - J2000).TotalDays;
        var meanAnomaly = (MeanAnomalyAtJ2000Deg + MeanMotionDegPerDay * days) * Mathf.Deg2Rad;
        // Kepler's equation M = E - e·sin E, by Newton iteration.
        var E = meanAnomaly;
        for (var i = 0; i < 8; i++) E -= (E - Eccentricity * Math.Sin(E) - meanAnomaly) / (1 - Eccentricity * Math.Cos(E));
        var trueAnomaly = 2 * Math.Atan2(Math.Sqrt(1 + Eccentricity) * Math.Sin(E / 2), Math.Sqrt(1 - Eccentricity) * Math.Cos(E / 2));
        distance = SemiMajorAxisAu * AstronomicalUnit * (1 - Eccentricity * Math.Cos(E));
        // The Sun's ecliptic longitude as seen from Earth: Earth's own
        // heliocentric longitude (true anomaly + longitude of perihelion),
        // plus 180° - the Sun is on the far side of Earth's position.
        var lambda = trueAnomaly + (PerihelionLongitudeDeg + 180) * Mathf.Deg2Rad;
        var tilt = ObliquityDeg * Mathf.Deg2Rad;
        var x = Math.Cos(lambda);
        var y = Math.Sin(lambda) * Math.Cos(tilt);
        var z = Math.Sin(lambda) * Math.Sin(tilt);
        rightAscension = Math.Atan2(y, x);
        declination = Math.Asin(z);
    }

    private GUIStyle style;
    private void OnGUI()
    {
        if (LaunchMenu.Open) return;   // the launch menu covers the scene
        if (NeedsInitialize) return;
        if (view != null && Event.current.type == EventType.Repaint)
        {
            var camera = view.GetComponent<Camera>();
            // The axis label only while Earth fills a good part of the view.
            if (axisLine.positionCount > 0 && Vector3.Distance(camera.transform.position, planet.transform.position) < planet.Radius * PlanetBody.WorldUnitsPerMeter * 12)
                MapLabel(camera, axisLabel, "Spin axis · tilted 23.44° to the orbit", new Color(1f, .55f, .45f));
            if (view.SolarBlend > .3f)
            {
                MapLabel(camera, SunWorldPosition, "Sun", new Color(1f, .9f, .6f));
                MapLabel(camera, planet.transform.position, "Earth", new Color(.5f, .8f, 1f));
                var a = SemiMajorAxisAu * AstronomicalUnit / 1e9;
                MapLabel(camera, perihelionPoint, "◆ Perihelion " + (a * (1 - Eccentricity)).ToString("F2") + " million km (≈Jan 3)", new Color(1f, .75f, .45f));
                MapLabel(camera, aphelionPoint, "◆ Aphelion " + (a * (1 + Eccentricity)).ToString("F2") + " million km (≈Jul 4)", new Color(.55f, .75f, 1f));
            }
        }
        style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 12 };
        style.normal.textColor = new Color(1f, .93f, .75f);
        GUI.Label(new Rect(Screen.width - 524, 18, 500, 56),
            Date.ToString("yyyy-MM-dd HH:mm") + " UTC   ·   Earth–Sun " + (DistanceMeters / 1e9).ToString("F2") + " million km (" +
            (DistanceMeters / AstronomicalUnit).ToString("F4") + " AU)\nOrbital speed " + (OrbitalSpeed / 1000).ToString("F2") +
            " km/s   ·   Sun over " + Math.Abs(SubsolarLatitude).ToString("F1") + "°" + (SubsolarLatitude >= 0 ? "N" : "S") +
            (MoonBody.Instance != null ? "\nMoon " + (MoonBody.Instance.DistanceMeters / 1000).ToString("N0") + " km   ·   " +
                (MoonBody.Instance.IlluminatedFraction * 100).ToString("F0") + "% lit" : ""), style);
    }
}
