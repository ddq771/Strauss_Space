using System;
using UnityEngine;

/// <summary>
/// Simulation speed. Up to ×10 the physics simply runs faster
/// (Time.timeScale). ×100 up to ×1,000,000 are "on rails", as in KSP: thousands of
/// physics steps a second would be far too slow (and Unity caps timeScale at
/// 100), so the rocket is taken off physics and moved along its exact
/// two-body orbit (universal-variable Kepler propagation) while Earth keeps
/// turning under it. That is only valid in a vacuum with the engines off,
/// so rails warp needs the rocket above 100 km and coasting; it drops back
/// to ×1 by itself before the orbit dips into the atmosphere. On the pad it
/// just fast-forwards the clock (Sun, day/night, seasons).
///
/// Out past 50,000 km, or near the Moon, a two-body orbit is no longer the
/// real path: there the rails integrate Earth's and the Moon's gravity
/// together (EarthMoonDynamics, the forces the physics applies), and drop
/// back to ×1 before the rocket comes within 10 km of the Moon's surface.
/// Landed on the Moon it only fast-forwards the clock, as on the pad.
/// </summary>
public sealed class TimeWarp : MonoBehaviour
{
    public static readonly float[] Rates = { 1, 2, 5, 10, 100, 1000, 10000, 100000, 1000000 };
    public const float RailsFrom = 100f;
    private const double AtmosphereTop = 100000;   // m
    // The rails step is the frame's whole time (Unity already caps
    // Time.deltaTime at Time.maximumDeltaTime): the simulation date, which
    // places the Moon and Sun, advances by exactly that too. Cutting the
    // step shorter left the rocket behind the clock after every slow frame.
    private const double MoonPathFrom = 50e6;     // m from Earth's centre
    private const double MoonStopAltitude = 10000; // m above the Moon

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
        if (MoonBody.Instance != null && MoonBody.Instance.Landed) { railsBodyActive = false; return; } // on the Moon: clock only
        if (!railsBodyActive) EnterRails();
        // Throttle up or anything else that needs physics: drop out of warp.
        var reason = flight.Crashed || rocket.EngineEnabled || flight.SolidBurning ? "Time warp stopped: engines lit." : null;
        if (reason != null) { Stop(reason); return; }
        Propagate(Time.deltaTime * Rate);
    }

    private void Stop(string reason)
    {
        SetRate(1);
        Message = reason;
    }

    // Moves the rocket dt seconds along its orbit. The orbit is inertial;
    // the scene is fixed to the turning Earth, so convert there and back.
    /// <summary>
    /// Points along the path the last rails steps covered (oldest first), for
    /// TrajectoryDisplay's flown-path trail, as star-fixed samples (see
    /// TrajectoryDisplay.PathSample) so the trail can draw them in any frame.
    /// At ×100,000 a frame spans whole orbits, so a trail of one point per
    /// frame would join points orbits apart with straight lines; these fill
    /// the path in. TrajectoryDisplay takes them (and clears the list) every frame.
    /// </summary>
    public static readonly System.Collections.Generic.List<TrajectoryDisplay.PathSample> RailsPath = new();
    // This frame's step: its start date and Earth's rotation angle then.
    private DateTime stepStart;
    private double stepTheta;
    // Points along the path per orbit (one every 4°), and at most this many per frame.
    private const int PathPointsPerOrbit = 90, MaxPathPoints = 720;

    // Adds the point at position r, t seconds into this frame's step, to
    // RailsPath. How: r is in axes fixed among the stars that matched the
    // scene's at the step's start, when Earth's rotation angle was
    // stepTheta; turning it by that angle gives the star-fixed axes the trail
    // stores (those of rotation angle 0). Earth's angle at the point is
    // stepTheta + spin × t, and the Moon's position comes from the ephemeris
    // at that moment.
    private void AddPathPoint(Vector3d r, double t, Double3 centre)
    {
        if (RailsPath.Count >= MaxPathPoints) return;
        var moon = SolarSystem.Instance != null && MoonBody.Instance != null ? MoonBody.OffsetFromEarth(stepStart.AddSeconds(t), 0) : default;
        RailsPath.Add(new TrajectoryDisplay.PathSample(r.RotateAboutY(stepTheta).ToDouble3(), stepTheta + planet.RotationRate * t, moon));
    }

    private void Propagate(double dt)
    {
        var centre = FloatingOrigin.PlanetCentre;
        // When this step began (for the trail's samples): the date one frame
        // ago and Earth's rotation angle then.
        if (SolarSystem.Instance != null)
        {
            stepStart = SolarSystem.Instance.DateAt(Time.time - Time.deltaTime);
            stepTheta = SolarSystem.Instance.RotationAngleAt(stepStart);
        }
        var r0 = new Vector3d((new Double3(transform.position) - centre) / PlanetBody.WorldUnitsPerMeter);
        var spin = new Vector3d(planet.SpinVector);
        var v0 = groundVelocity + Vector3d.Cross(spin, r0);
        var mu = PlanetBody.UniversalGravitationalConstant * planet.Mass;
        Vector3d r1, v1;
        string stop = null;
        var sol = SolarSystem.Instance;
        var moon = MoonBody.Instance;
        if (sol != null && moon != null && (r0.Length > MoonPathFrom || moon.RocketNear))
        {
            // Earth and Moon together. The clock may already have moved on
            // this frame; the Moon's path starts where the rocket's does.
            var start = sol.DateAt(Time.time - Time.deltaTime);
            // The Moon's path sampled finely enough for the step (every 30 s
            // at ×1000; coarser when a frame covers days at the top speeds).
            var track = new EarthMoonDynamics.MoonTrack(start, sol.RotationAngleAt(start), dt + 60, Math.Max(30, (dt + 60) / 400));
            var r = r0.ToDouble3(); var v = v0.ToDouble3();
            var t = 0.0;
            // Trail points: at most one per 1/90 of this path's local orbital
            // period, taken at the integrator's own steps.
            var nextPoint = 0.0;
            while (t < dt)
            {
                var h = Math.Min(EarthMoonDynamics.SuggestedStep(r, v, track.Position(t), track.Velocity(t), mu, planet.Radius), dt - t);
                EarthMoonDynamics.Step(ref r, ref v, t, h, track, mu);
                t += h;
                if (t >= nextPoint && t < dt)
                {
                    AddPathPoint(new Vector3d(r), t, centre);
                    nextPoint = t + 2 * Math.PI * Math.Sqrt(Math.Pow(r.Length, 3) / mu) / PathPointsPerOrbit;
                }
                if (EarthMoonDynamics.MoonAltitude(r, track.Position(t)) < MoonStopAltitude)
                { stop = "Time warp stopped: approaching the Moon's surface."; break; }
                if (r.Length - planet.Radius < AtmosphereTop && Double3.Dot(r, v) < 0)
                { stop = "Time warp stopped: entering the atmosphere."; break; }
            }
            dt = t;
            r1 = new Vector3d(r); v1 = new Vector3d(v);
        }
        else if (PeriapsisRadius(r0, v0, mu) < planet.Radius + AtmosphereTop)
        {
            // The orbit dips into the air: at the top speeds one frame can
            // span whole orbits, so step through and stop where it comes in.
            r1 = r0; v1 = v0;
            var t = 0.0;
            while (t < dt)
            {
                var h = Math.Min(20, dt - t);
                Kepler(r1, v1, h, mu, out r1, out v1);
                t += h;
                if (t < dt) AddPathPoint(r1, t, centre);
                if (r1.Length - planet.Radius < AtmosphereTop && Vector3d.Dot(r1, v1) < 0)
                { stop = "Time warp stopped: entering the atmosphere."; break; }
            }
            dt = t;
        }
        else
        {
            Kepler(r0, v0, dt, mu, out r1, out v1);
            // Trail points along the way: the step split into pieces of
            // 1/90 orbit (or 60 s on an escape path), each point found by
            // Kepler propagation from the step's start to that moment.
            var alpha = 2 / r0.Length - Vector3d.Dot(v0, v0) / mu;   // 1/a
            var piece = alpha > 0 ? 2 * Math.PI * Math.Sqrt(1 / (alpha * alpha * alpha) / mu) / PathPointsPerOrbit : 60;
            var pieces = (int)Math.Min(MaxPathPoints, Math.Ceiling(dt / piece));
            for (var k = 1; k < pieces; k++)
            {
                var t = dt * k / pieces;
                Kepler(r0, v0, t, mu, out var rk, out _);
                AddPathPoint(rk, t, centre);
            }
        }

        // Earth turned eastward by spin × dt meanwhile: in its frame, the
        // inertial result sits that much further west - and so does the
        // rocket's (inertially fixed) attitude.
        var turn = -planet.RotationRate * dt;
        r1 = r1.RotateAboutY(turn);
        v1 = v1.RotateAboutY(turn);
        var ground = v1 - Vector3d.Cross(spin, r1);

        if (stop == null && r1.Length - planet.Radius < AtmosphereTop && Vector3d.Dot(r1, v1) < 0)
            stop = "Time warp stopped: entering the atmosphere.";
        Place(r1, ground, turn, centre);
        if (stop != null) Stop(stop);
    }

    private void Place(Vector3d r, Vector3d ground, double turn, Double3 centre)
    {
        groundVelocity = ground;
        // From Earth's centre in double precision (FloatingOrigin).
        var position = (centre + r.ToDouble3() * PlanetBody.WorldUnitsPerMeter).ToVector3();
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
        // A closed orbit repeats every period: at the top warp speeds a frame
        // can span many, so propagate only the part of a revolution left.
        if (alpha > 0)
        {
            var period = 2 * Math.PI * Math.Sqrt(1 / (alpha * alpha * alpha) / mu);
            dt %= period;
        }
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

    private static double PeriapsisRadius(Vector3d r, Vector3d v, double mu)
    {
        var h = Vector3d.Cross(r, v);
        var hh = Vector3d.Dot(h, h);
        var e = Vector3d.Cross(v, h) * (1 / mu) - r * (1 / r.Length);
        return hh / mu / (1 + e.Length);
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
        public Vector3d(Double3 v) { x = v.x; y = v.y; z = v.z; }
        public Double3 ToDouble3() => new(x, y, z);
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
