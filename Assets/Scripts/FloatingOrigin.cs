using System;
using UnityEngine;

/// <summary>
/// Keeps the rocket near the world origin once it heads out toward the
/// Moon. World positions are single-precision floats in kilometres, so
/// 384,000 km out they resolve only ~30 m - a lander moving a few
/// centimetres a physics step would freeze or jitter. When the rocket gets
/// more than a set distance from the origin, every root object, particle,
/// line and trail is moved back by the rocket's offset; the total shift is
/// kept in double precision, and Earth's centre (from which the Moon and
/// everything else is placed) is set from it exactly, never accumulated
/// in floats.
///
/// During the ascent nothing moves (the launch pad is the origin, as
/// authored). Once the rocket is above the atmosphere (100 km) the world
/// re-centres whenever it gets 50 km from the origin - in low orbit that
/// matters as much as at the Moon: there the rocket is up to ~13,000 km
/// from the pad, where a float resolves only ~1 m, while it moves ~150 m a
/// physics step. That rounding drained the orbit's energy (the low point
/// sank a few km a day), and the thrust's point of application - with the
/// lever arm to the centre of mass known only to ~1 m on a 15-40 m stage -
/// gave random torques that shook the vehicle. 50 km out a float resolves
/// ~4 mm. Inside the Moon's sphere of influence it re-centres every 5 km
/// (a fraction of a millimetre). Back in the assembly the world returns
/// to its authored place.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class FloatingOrigin : MonoBehaviour
{
    // Re-centring starts once the rocket is this far from Earth's centre (km,
    // world units): above the atmosphere, so the pad and camera stay put for
    // the ascent. After that, the world is shifted whenever the rocket is
    // more than FarThreshold km from the origin (NearMoonThreshold near the Moon).
    private const double AboveAtmosphere = 100;  // km above the surface
    private const float FarThreshold = 50f, NearMoonThreshold = 5f;

    /// <summary>Moved by this much (world units): add it to any cached world position.</summary>
    public static event Action<Vector3> Shifted;
    /// <summary>Earth's centre in world units, in double precision.</summary>
    public static Double3 PlanetCentre => instance != null ? instance.planetAuthored - instance.shift : planetFallback;
    public static bool Active => instance != null && (instance.shift.x != 0 || instance.shift.y != 0 || instance.shift.z != 0);

    private static FloatingOrigin instance;
    private static Double3 planetFallback;
    private Rocket rocket;
    private PlanetBody planet;
    private Double3 planetAuthored;   // Earth's centre with no shift
    private Double3 shift;            // total the world has been moved by, negated
    // Where each scene root was before the first shift, to put it back exactly.
    private readonly System.Collections.Generic.Dictionary<Transform, Vector3> authored = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        var rocket = FindFirstObjectByType<Rocket>();
        if (rocket != null && rocket.GetComponent<FloatingOrigin>() == null) rocket.gameObject.AddComponent<FloatingOrigin>();
    }

    private void Awake()
    {
        instance = this;
        rocket = GetComponent<Rocket>();
        planet = FindFirstObjectByType<PlanetBody>();
        if (planet != null) planetAuthored = new Double3(planet.transform.position);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void FixedUpdate() => Check();
    private void LateUpdate() => Check();

    private void Check()
    {
        if (planet == null) return;
        if (!rocket.Launched)
        {
            // Back in the assembly: return everything to where it was authored.
            if (Active) ReturnHome();
            return;
        }
        var position = rocket.transform.position;
        // Distance from Earth's centre (double precision): until the rocket
        // first climbs above the atmosphere nothing is shifted; once shifting
        // has started (Active) it carries on wherever the rocket goes.
        var fromEarth = (new Double3(position) - PlanetCentre).Length;
        if (fromEarth < planet.Radius * PlanetBody.WorldUnitsPerMeter + AboveAtmosphere && !Active) return;
        var moon = MoonBody.Instance;
        var nearMoon = moon != null && (moon.transform.position - position).magnitude < MoonBody.SphereOfInfluence * PlanetBody.WorldUnitsPerMeter;
        var threshold = nearMoon ? NearMoonThreshold : FarThreshold;
        if (position.magnitude > threshold) Shift(-position);
    }

    private void Shift(Vector3 offset)
    {
        if (offset == Vector3.zero) return;
        var wasActive = Active;
        shift = shift - new Double3(offset);
        // Snap tiny residue to zero when returning home.
        if (Math.Abs(shift.x) < 1e-6 && Math.Abs(shift.y) < 1e-6 && Math.Abs(shift.z) < 1e-6) shift = default;

        var scene = gameObject.scene;
        if (!wasActive) { authored.Clear(); foreach (var root in scene.GetRootGameObjects()) authored[root.transform] = root.transform.position; }
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.transform;
            t.position += offset;
            var body = root.GetComponent<Rigidbody>();
            if (body != null) body.position = t.position;
        }
        // Earth from the exact double-precision total, not the float sum.
        planet.transform.position = PlanetCentre.ToVector3();
        var planetBody = planet.GetComponent<Rigidbody>();
        if (planetBody != null) planetBody.position = planet.transform.position;

        foreach (var system in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (system.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
            var particles = new ParticleSystem.Particle[system.particleCount];
            var count = system.GetParticles(particles);
            for (var i = 0; i < count; i++) particles[i].position += offset;
            system.SetParticles(particles, count);
        }
        foreach (var line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
        {
            if (!line.useWorldSpace || line.positionCount == 0) continue;
            var points = new Vector3[line.positionCount];
            line.GetPositions(points);
            for (var i = 0; i < points.Length; i++) points[i] += offset;
            line.SetPositions(points);
        }
        foreach (var trail in FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None))
        {
            var points = new Vector3[trail.positionCount];
            var count = trail.GetPositions(points);
            for (var i = 0; i < count; i++) points[i] += offset;
            trail.SetPositions(points);
        }
        Physics.SyncTransforms();
        Shifted?.Invoke(offset);
    }

    private void ReturnHome()
    {
        var offset = shift.ToVector3();
        Shift(offset);
        shift = default;
        // Exactly where they were authored (a float sum of shifts may be
        // off by tens of metres after a trip to the Moon).
        foreach (var pair in authored)
        {
            // The rocket was mid-flight then; the assembly places it itself.
            if (pair.Key == null || pair.Key == rocket.transform) continue;
            pair.Key.position = pair.Value;
            var body = pair.Key.GetComponent<Rigidbody>();
            if (body != null) body.position = pair.Value;
        }
        authored.Clear();
        planet.transform.position = planetAuthored.ToVector3();
        Physics.SyncTransforms();
    }
}

/// <summary>Double-precision vector for orbit and origin maths.</summary>
public readonly struct Double3
{
    public readonly double x, y, z;
    public Double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public Double3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public double Length => Math.Sqrt(x * x + y * y + z * z);
    public double SqrLength => x * x + y * y + z * z;
    public Double3 Normalized { get { var l = Length; return l > 0 ? this / l : this; } }
    public Vector3 ToVector3() => new((float)x, (float)y, (float)z);
    public static Double3 operator +(Double3 a, Double3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Double3 operator -(Double3 a, Double3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Double3 operator -(Double3 a) => new(-a.x, -a.y, -a.z);
    public static Double3 operator *(Double3 a, double s) => new(a.x * s, a.y * s, a.z * s);
    public static Double3 operator /(Double3 a, double s) => new(a.x / s, a.y / s, a.z / s);
    public static double Dot(Double3 a, Double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static Double3 Cross(Double3 a, Double3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    /// <summary>Adds 'angle' to the longitude (x toward z), as PlanetBody's frame does.</summary>
    public Double3 RotateAboutY(double angle) { var c = Math.Cos(angle); var s = Math.Sin(angle); return new(x * c - z * s, y, x * s + z * c); }
}
