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
/// Earth does not spin in this project, so only the yearly motion shows:
/// the subsolar point drifts once around the planet per year. The scene
/// starts on today's date, with Earth's (frozen) rotation chosen so the Sun
/// starts over the same longitude the scene was lit from.
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
    private double earthRotationRad;       // frozen Earth rotation (sidereal angle)
    private bool initialized;

    public DateTime Date { get; private set; }
    public double DistanceMeters { get; private set; }
    public double OrbitalSpeed { get; private set; }         // m/s, Earth around the Sun
    public double SubsolarLatitude { get; private set; }     // deg = the Sun's declination
    public double SunAngularDiameterDeg => 2 * Math.Asin(SunRadius / DistanceMeters) * Mathf.Rad2Deg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        var sunObject = GameObject.Find("Sun");
        if (sunObject != null && sunObject.GetComponent<SolarSystem>() == null) sunObject.AddComponent<SolarSystem>();
    }

    private void Start()
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
        initialized = true;
        Apply();
    }

    private void Update()
    {
        if (!initialized) return;
        // Game time, including the flight's ×2..×10 simulation speed.
        Date = Date.AddSeconds(Time.deltaTime);
        Apply();
    }

    private void Apply()
    {
        SunEquatorial(Date, out var rightAscension, out var declination, out var distance);
        DistanceMeters = distance;
        SubsolarLatitude = declination * Mathf.Rad2Deg;
        OrbitalSpeed = Math.Sqrt(SunGravitationalParameter * (2 / distance - 1 / (SemiMajorAxisAu * AstronomicalUnit)));
        var longitude = (rightAscension - earthRotationRad) * Mathf.Rad2Deg;
        var toSun = planet != null
            ? planet.GetSurfaceNormal((float)SubsolarLatitude, (float)longitude)
            : Vector3.up;
        transform.rotation = Quaternion.LookRotation(-toSun);
        if (sun != null)
        {
            var au = distance / AstronomicalUnit;
            sun.intensity = (float)(intensityAtOneAu / (au * au));
        }
        Shader.SetGlobalVector("_SolarDirection", new Vector4(toSun.x, toSun.y, toSun.z, (float)(SunAngularDiameterDeg * .5 * Mathf.Deg2Rad)));
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
        if (!initialized) return;
        style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 12 };
        style.normal.textColor = new Color(1f, .93f, .75f);
        GUI.Label(new Rect(Screen.width - 524, 18, 500, 40),
            Date.ToString("yyyy-MM-dd HH:mm") + " UTC   ·   Earth–Sun " + (DistanceMeters / 1e9).ToString("F2") + " million km (" +
            (DistanceMeters / AstronomicalUnit).ToString("F4") + " AU)\nOrbital speed " + (OrbitalSpeed / 1000).ToString("F2") +
            " km/s   ·   Sun over " + Math.Abs(SubsolarLatitude).ToString("F1") + "°" + (SubsolarLatitude >= 0 ? "N" : "S"), style);
    }
}
