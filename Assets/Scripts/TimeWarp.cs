using System;
using UnityEngine;

/// <summary>
/// Simulation speed. Up to ×10 the physics simply runs faster
/// (Time.timeScale). ×100 and ×1000 are "on rails", as in KSP: thousands of
/// physics steps a second would be far too slow (and Unity caps timeScale at
/// 100), so the rocket is taken off physics and moved along its exact
/// two-body orbit (universal-variable Kepler propagation) while Earth keeps
/// turning under it. That is only valid in a vacuum with the engines off,
/// so rails warp needs the rocket above 100 km and coasting; it drops back
/// to ×1 by itself before the orbit dips into the atmosphere. On the pad it
/// just fast-forwards the clock (Sun, day/night, seasons).
/// </summary>
public sealed class TimeWarp : MonoBehaviour
{
    public static readonly float[] Rates = { 1, 2, 5, 10, 100, 1000 };
    public const float RailsFrom = 100f;
    private const double AtmosphereTop = 100000;   // m
    // Longest single rails step - keeps a slow frame from skipping far ahead.
    private const float MaxRealStep = .1f;

    public static float Rate { get; private set; } = 1;
    public static bool OnRails => Rate >= RailsFrom;
    /// <summary>Why the last request was refused or warp stopped (shown in the flight panel).</summary>
    public static string Message { get; private set; }
    /// <summary>Simulated seconds per real second, for clocks that run off Time.deltaTime.</summary>
    public static float ClockMultiplier => OnRails ? Rate : 1f;

    private static TimeWarp instance;
    private Rocket rocket;
    private RocketFlightModel flight;
    private Rigidbody body;
    private PlanetBody planet;
    private bool railsBodyActive;
    private Vector3d groundVelocity;   // m/s, Earth-fixed, while on rails

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        Rate = 1; Message = null;
        foreach (var model in FindObjectsByType<RocketFlightModel>(FindObjectsSortMode.None))
            if (model.GetComponent<TimeWarp>() == null) model.gameObject.AddComponent<TimeWarp>();
    }

    private void Awake()
    {
        instance = this;
        rocket = GetComponent<Rocket>();
        flight = GetComponent<RocketFlightModel>();
        body = GetComponent<Rigidbody>();
        planet = FindFirstObjectByType<PlanetBody>();
    }

    /// <summary>The rocket's ground velocity (m/s) while on rails, when its body is kinematic.</summary>
    public static bool TryGetRailsVelocity(out Vector3 metresPerSecond)
    {
        metresPerSecond = default;
        if (instance == null || !instance.railsBodyActive) return false;
        metresPerSecond = instance.groundVelocity.ToVector3();
        return true;
    }

    /// <summary>Ask for a simulation speed; returns false (with Message) if not allowed now.</summary>
    public static bool Request(float rate)
    {
        Message = null;
        if (instance == null) { Time.timeScale = Mathf.Min(rate, RailsFrom - 1); return rate < RailsFrom; }
        return instance.SetRate(rate);
    }

    private bool SetRate(float rate)
    {
        if (rate >= RailsFrom && rocket.Launched)
        {
            var reason = RailsBlocker();
            if (reason != null) { Message = reason; return false; }
        }
        if (rate < RailsFrom) LeaveRails();
        Rate = rate;
        Time.timeScale = rate < RailsFrom ? rate : 1f;
        if (rate >= RailsFrom && rocket.Launched) EnterRails();
        return true;
    }

    private string RailsBlocker()
    {
        if (planet == null) return "No planet to orbit.";
        if (flight.Crashed) return "The vehicle has crashed.";
        if (rocket.EngineEnabled || flight.SolidBurning) return "Time warp above ×10 needs the engines off - cut the throttle (X) first.";
        if (flight.Altitude < AtmosphereTop) return "Time warp above ×10 needs altitude above 100 km (out of the atmosphere).";
        return null;
    }

    private void EnterRails()
    {
        if (railsBodyActive) return;
        groundVelocity = new Vector3d(body.linearVelocity / PlanetBody.WorldUnitsPerMeter);
        body.isKinematic = true;
        railsBodyActive = true;
    }

    private void LeaveRails()
    {
        if (!railsBodyActive) return;
        railsBodyActive = false;
        body.isKinematic = false;
        body.linearVelocity = groundVelocity.ToVector3() * PlanetBody.WorldUnitsPerMeter;
        body.angularVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (!OnRails) return;
        if (!rocket.Launched) { if (railsBodyActive) railsBodyActive = false; return; } // pad: clock only
        if (!railsBodyActive) EnterRails();
        // Throttle up or anything else that needs physics: drop out of warp.
        var reason = flight.Crashed || rocket.EngineEnabled || flight.SolidBurning ? "Time warp stopped: engines lit." : null;
        if (reason != null) { Stop(reason); return; }
        Propagate(Mathf.Min(Time.deltaTime, MaxRealStep) * Rate);
    }

    private void Stop(string reason)
    {
        SetRate(1);
        Message = reason;
    }

    // Moves the rocket dt seconds along its orbit. The orbit is inertial;
    // the scene is fixed to the turning Earth, so convert there and back.
    private void Propagate(double dt)
    {
        var centre = planet.transform.position;
        var r0 = new Vector3d((transform.position - centre) / PlanetBody.WorldUnitsPerMeter);
        var spin = new Vector3d(planet.SpinVector);
        var v0 = groundVelocity + Vector3d.Cross(spin, r0);
        var mu = PlanetBody.UniversalGravitationalConstant * planet.Mass;
        Kepler(r0, v0, dt, mu, out var r1, out var v1);

        // Earth turned eastward by spin × dt meanwhile: in its frame, the
        // inertial result sits that much further west - and so does the
        // rocket's (inertially fixed) attitude.
        var turn = -planet.RotationRate * dt;
        r1 = r1.RotateAboutY(turn);
        v1 = v1.RotateAboutY(turn);
        var ground = v1 - Vector3d.Cross(spin, r1);

        if (r1.Length - planet.Radius < AtmosphereTop && Vector3d.Dot(r1, v1) < 0)
        {
            // About to dip into the air: stop here and hand back to physics.
            Place(r1, ground, turn, centre);
            Stop("Time warp stopped: entering the atmosphere.");
            return;
        }
        Place(r1, ground, turn, centre);
    }

    private void Place(Vector3d r, Vector3d ground, double turn, Vector3 centre)
    {
        groundVelocity = ground;
        var position = centre + r.ToVector3() * PlanetBody.WorldUnitsPerMeter;
        body.position = position;
        transform.position = position;
        transform.rotation = Quaternion.AngleAxis((float)(-turn * Mathf.Rad2Deg), planet.transform.up) * transform.rotation;
        body.rotation = transform.rotation;
    }

    // Universal-variable Kepler propagation (Curtis, Algorithms 3.3/3.4):
    // exact two-body motion for any orbit - ellipse, parabola or hyperbola.
    private static void Kepler(Vector3d r0, Vector3d v0, double dt, double mu, out Vector3d r, out Vector3d v)
    {
        var r0m = r0.Length;
        var vr0 = Vector3d.Dot(r0, v0) / r0m;
        var alpha = 2 / r0m - Vector3d.Dot(v0, v0) / mu;
        var sqrtMu = Math.Sqrt(mu);
        var chi = sqrtMu * Math.Abs(alpha) * dt;
        for (var i = 0; i < 60; i++)
        {
            var z = alpha * chi * chi;
            Stumpff(z, out var c, out var s);
            var f = r0m * vr0 / sqrtMu * chi * chi * c + (1 - alpha * r0m) * chi * chi * chi * s + r0m * chi - sqrtMu * dt;
            var df = r0m * vr0 / sqrtMu * chi * (1 - alpha * chi * chi * s) + (1 - alpha * r0m) * chi * chi * c + r0m;
            var step = f / df;
            chi -= step;
            if (Math.Abs(step) < 1e-9) break;
        }
        var zf = alpha * chi * chi;
        Stumpff(zf, out var cf, out var sf);
        var fCoef = 1 - chi * chi / r0m * cf;
        var gCoef = dt - chi * chi * chi / sqrtMu * sf;
        r = r0 * fCoef + v0 * gCoef;
        var rm = r.Length;
        var fDot = sqrtMu / (rm * r0m) * (alpha * chi * chi * chi * sf - chi);
        var gDot = 1 - chi * chi / rm * cf;
        v = r0 * fDot + v0 * gDot;
    }

    private static void Stumpff(double z, out double c, out double s)
    {
        if (z > 1e-8) { var q = Math.Sqrt(z); s = (q - Math.Sin(q)) / (q * q * q); c = (1 - Math.Cos(q)) / z; }
        else if (z < -1e-8) { var q = Math.Sqrt(-z); s = (Math.Sinh(q) - q) / (q * q * q); c = (Math.Cosh(q) - 1) / -z; }
        else { s = 1.0 / 6; c = .5; }
    }

    private void OnDisable() { LeaveRails(); Rate = 1; Time.timeScale = 1; }

    // Double-precision vector for metre-scale orbit maths.
    private readonly struct Vector3d
    {
        public readonly double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public Vector3d(Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public Vector3 ToVector3() => new((float)x, (float)y, (float)z);
        // Adds 'angle' to the longitude (x toward z), as PlanetBody's frame does.
        public Vector3d RotateAboutY(double angle)
        { var c = Math.Cos(angle); var s = Math.Sin(angle); return new(x * c - z * s, y, x * s + z * c); }
        public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.x * s, a.y * s, a.z * s);
        public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3d Cross(Vector3d a, Vector3d b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }
}
