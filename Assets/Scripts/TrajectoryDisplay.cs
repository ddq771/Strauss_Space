using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the rocket's predicted coasting trajectory once it is flying (engines
/// off from now on), the path actually flown so far, and Ap / Pe / impact
/// markers with times.
///
/// The prediction switches between two methods along the path:
///  * In the atmosphere (below 100 km) it is integrated step by step (RK4,
///    0.5 s) with gravity and the same drag model the flight uses - density,
///    Mach-dependent Cd, the vehicle's current mass - so a low arc lands
///    where it really would, not where a vacuum orbit says.
///  * Above it, the exact two-body (Kepler) orbit is used: an ellipse, or a
///    hyperbola past escape speed - followed until the path re-enters the
///    atmosphere, where integration takes over again down to impact.
/// While burning, it is recomputed every frame from the current state.
///
/// Earth turns, and the scene is fixed to the ground, so the orbit maths
/// uses the inertial velocity (ground velocity + Earth's spin) and drag uses
/// the velocity relative to the turning air. A path that comes down is drawn
/// relative to the ground - each point shifted by how far Earth will have
/// turned by then - so the impact marker is where it really lands; a closed
/// orbit is drawn as its fixed ellipse (as at this moment), like KSP.
///
/// A path that reaches out past 50,000 km, or passes near the Moon, is
/// integrated under Earth's and the Moon's gravity together
/// (EarthMoonDynamics, the forces the flight applies), against the Moon's
/// real motion, for up to 10 days: it shows the closest approach to the
/// Moon (Moon Pe) and where it would hit the Moon. Inside the Moon's sphere
/// of influence the path is drawn relative to the Moon as it is now, so an
/// orbit round it or a descent to its surface reads as it would there.
/// </summary>
[RequireComponent(typeof(Rocket), typeof(RocketFlightModel))]
public sealed class TrajectoryDisplay : MonoBehaviour
{
    private const int ConicSamples = 360;
    private const double AtmosphereTop = 100000;   // m - above this, drag is negligible
    private const double StepSeconds = 0.5;
    private const int MaxSteps = 8000;             // ~67 min of atmospheric flight
    private const int MaxPhases = 4;
    private const float TrailInterval = 0.25f;
    private const int TrailMax = 8000;
    // Below this speed (just off the pad) the orbit is a degenerate sliver
    // that flickers around the launch site - don't predict until moving.
    private const double MinPredictSpeed = 50;
    // Line width as a fraction of camera distance: ~1.5 px at 1080p.
    private const float WidthPerDistance = .0012f;
    private const double MoonPathFrom = 50e6;               // m from Earth's centre
    private const double MoonPathSeconds = 10 * 86400;
    private const int MoonPathMaxSteps = 20000;

    private Rocket rocket;
    private RocketFlightModel flight;
    private Rigidbody body;
    private PlanetBody planet;
    private LineRenderer predicted, trail;
    private readonly List<Vector3> trailPoints = new();
    private readonly List<Vector3> points = new();
    private float nextTrailTime;
    private Camera viewCamera;
    private GUIStyle labelStyle;
    private double mu, radius, spinRate;
    private Vec spin;
    private Double3 centre;

    // Latest solution, also read by the flight panel.
    public bool HasOrbit { get; private set; }
    public double ApoapsisAltitude { get; private set; }   // m; +inf when escaping
    public double PeriapsisAltitude { get; private set; }  // m; negative = below the surface
    public bool Escaping { get; private set; }
    public bool InOrbit { get; private set; }               // closed orbit clear of the atmosphere
    public bool WillImpact { get; private set; }
    public double TimeToApoapsis { get; private set; } = -1; // s; -1 = already past / none ahead
    public double TimeToImpact { get; private set; } = -1;
    public double OrbitalPeriod { get; private set; } = -1;
    private Vector3 apWorld, peWorld, impactWorld;
    private bool showAp, showPe;

    // The Moon on the predicted path.
    public bool MoonEncounter { get; private set; }          // enters its sphere of influence
    public double MoonPeriapsisAltitude { get; private set; } // m above its surface, closest approach
    public double TimeToMoonPeriapsis { get; private set; } = -1;
    public bool WillImpactMoon { get; private set; }
    public double TimeToMoonImpact { get; private set; } = -1;
    private Vector3 moonPeWorld, moonImpactWorld;
    private EarthMoonDynamics.MoonTrack moonTrack;
    private Double3 moonNow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        foreach (var model in FindObjectsByType<RocketFlightModel>(FindObjectsSortMode.None))
            if (model.GetComponent<TrajectoryDisplay>() == null) model.gameObject.AddComponent<TrajectoryDisplay>();
    }

    private void Awake()
    {
        rocket = GetComponent<Rocket>();
        flight = GetComponent<RocketFlightModel>();
        body = GetComponent<Rigidbody>();
        planet = FindFirstObjectByType<PlanetBody>();
        predicted = MakeLine("Predicted Trajectory", new Color(.35f, .85f, 1f, .95f));
        trail = MakeLine("Flown Path", new Color(1f, .62f, .2f, .85f));
        FloatingOrigin.Shifted += OnOriginShifted;
    }

    // The flown path is kept in world positions: move it with the world.
    private void OnOriginShifted(Vector3 offset)
    {
        for (var i = 0; i < trailPoints.Count; i++) trailPoints[i] += offset;
    }

    private static LineRenderer MakeLine(string name, Color color)
    {
        // World-space lines on their own objects (not children of the moving,
        // rotating rocket), drawn after the atmosphere so haze doesn't hide them.
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
        line.startColor = line.endColor = color;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        return line;
    }

    private void OnDestroy()
    {
        FloatingOrigin.Shifted -= OnOriginShifted;
        if (predicted != null) Destroy(predicted.gameObject);
        if (trail != null) Destroy(trail.gameObject);
    }

    private void LateUpdate()
    {
        if (planet == null || !rocket.Launched)
        {
            Clear();
            if (trailPoints.Count > 0) { trailPoints.Clear(); trail.positionCount = 0; }
            return;
        }
        viewCamera ??= FindFirstObjectByType<AssemblyViewCamera>()?.GetComponent<Camera>() ?? Camera.main;

        RecordTrail();
        Solve();

        // Keep the lines about a pixel or two wide at any zoom, from the pad
        // to the map: scale with the camera's distance to the rocket only.
        if (viewCamera != null)
        {
            var width = Vector3.Distance(viewCamera.transform.position, transform.position) * WidthPerDistance;
            predicted.widthMultiplier = width;
            trail.widthMultiplier = width * .8f;
        }
    }

    private void Clear()
    {
        HasOrbit = false; WillImpact = false; showAp = showPe = false;
        MoonEncounter = false; WillImpactMoon = false; TimeToMoonPeriapsis = TimeToMoonImpact = -1;
        TimeToApoapsis = TimeToImpact = OrbitalPeriod = -1;
        predicted.positionCount = 0;
    }

    private void RecordTrail()
    {
        // On rails, first the points along the path rails warp covered this
        // frame (TimeWarp.RailsPath) - so at ×100,000, where a frame spans
        // orbits, the trail follows the curve instead of jumping across it.
        if (TimeWarp.RailsPath.Count > 0)
        {
            if (trailPoints.Count > 0)
            {
                trailPoints.AddRange(TimeWarp.RailsPath);
                // Over the limit: drop the oldest in one go (removing them one
                // at a time would shift the whole list for each).
                var excess = trailPoints.Count - (TrailMax - 1);
                if (excess > 0) trailPoints.RemoveRange(0, excess);
            }
            TimeWarp.RailsPath.Clear();
            nextTrailTime = 0;   // and the current position straight after
        }
        if (Time.time < nextTrailTime && trailPoints.Count > 0) return;
        if (trailPoints.Count == 0 && flight.Speed < 1) return;   // still on the pad
        nextTrailTime = Time.time + TrailInterval;
        if (trailPoints.Count >= TrailMax) trailPoints.RemoveAt(0);
        trailPoints.Add(transform.position);
        trail.positionCount = trailPoints.Count + 1;
        for (var i = 0; i < trailPoints.Count; i++) trail.SetPosition(i, trailPoints[i]);
        trail.SetPosition(trailPoints.Count, transform.position);
    }

    // --- Prediction -------------------------------------------------------------

    private void Solve()
    {
        // Metres, relative to the planet's centre, in double precision
        // (orbital radii are millions of metres).
        centre = FloatingOrigin.PlanetCentre;
        var rw = ((new Double3(transform.position) - centre) / PlanetBody.WorldUnitsPerMeter).ToVector3();
        var vw = flight.GroundVelocity;
        var r = new Vec(rw.x, rw.y, rw.z);
        var v = new Vec(vw.x, vw.y, vw.z);
        mu = PlanetBody.UniversalGravitationalConstant * planet.Mass;
        radius = planet.Radius;
        if (v.Length < MinPredictSpeed) { Clear(); return; }
        // Inertial velocity: what the rocket has relative to the stars.
        var sv = planet.SpinVector;
        spin = new Vec(sv.x, sv.y, sv.z);
        spinRate = planet.RotationRate;
        v = v + Vec.Cross(spin, r);

        // Vacuum orbit from the current state: Pe / escape / orbit status.
        var now = Conic.From(r, v, mu);
        HasOrbit = true;
        Escaping = now.e >= 1;
        PeriapsisAltitude = now.p / (1 + now.e) - radius;
        InOrbit = !Escaping && PeriapsisAltitude > AtmosphereTop;
        OrbitalPeriod = Escaping ? -1 : 2 * Math.PI * Math.Sqrt(Math.Pow(now.a, 3) / mu);
        showPe = PeriapsisAltitude > 0 && !(Escaping && now.nu0 > 0);
        peWorld = ToWorld(now.pHat * (now.p / (1 + now.e)));

        points.Clear();
        points.Add(transform.position);
        WillImpact = false; TimeToImpact = -1;
        MoonEncounter = false; WillImpactMoon = false; TimeToMoonPeriapsis = TimeToMoonImpact = -1;
        MoonPeriapsisAltitude = double.PositiveInfinity;
        // Out toward the Moon, or near it: Earth and Moon together.
        var moon = MoonBody.Instance; var sol = SolarSystem.Instance;
        var viaMoon = moon != null && sol != null &&
            (Escaping || now.a * (1 + now.e) > MoonPathFrom || r.Length > MoonPathFrom || moon.RocketNear);
        if (viaMoon)
        {
            moonTrack = new EarthMoonDynamics.MoonTrack(sol.Date, sol.RotationAngleRad, MoonPathSeconds, 1800);
            moonNow = moonTrack.Position(0);
        }
        var t = 0.0;
        var maxAlt = r.Length - radius; var maxAltT = 0.0; var maxAltPoint = r;
        var climbing = Vec.Dot(r, v) > 0;
        for (var phase = 0; phase < MaxPhases; phase++)
        {
            var alt = r.Length - radius;
            var done = alt < AtmosphereTop
                ? Integrate(ref r, ref v, ref t, ref maxAlt, ref maxAltT, ref maxAltPoint)
                : viaMoon ? FollowMoonPath(ref r, ref v, ref t)
                : FollowConic(ref r, ref v, ref t, ref maxAlt, ref maxAltT, ref maxAltPoint);
            if (done) break;
        }

        // Ap: the highest point ahead on the predicted path - or, for a closed
        // orbit clear of the air, the orbit's own apoapsis.
        if (Escaping || viaMoon) { ApoapsisAltitude = Escaping ? double.PositiveInfinity : now.p / (1 - now.e) - radius; showAp = false; TimeToApoapsis = -1; }
        else if (InOrbit)
        {
            ApoapsisAltitude = now.p / (1 - now.e) - radius;
            apWorld = ToWorld(now.pHat * (-now.p / (1 - now.e)));
            TimeToApoapsis = now.TimeTo(Math.PI, mu);
            showAp = true;
        }
        else
        {
            ApoapsisAltitude = maxAlt;
            apWorld = ToWorld(maxAltPoint, maxAltT);
            TimeToApoapsis = climbing && maxAltT > 0 ? maxAltT : -1;
            showAp = TimeToApoapsis > 0;
        }

        predicted.positionCount = points.Count;
        predicted.SetPositions(points.ToArray());
    }

    // RK4 through the atmosphere with gravity + drag. Returns true when the
    // prediction is finished (impact or out of steps), false on leaving the
    // atmosphere (hand over to the conic).
    private bool Integrate(ref Vec r, ref Vec v, ref double t, ref double maxAlt, ref double maxAltT, ref Vec maxAltPoint)
    {
        for (var step = 0; step < MaxSteps; step++)
        {
            var previous = r;
            Rk4(ref r, ref v, StepSeconds);
            t += StepSeconds;
            var alt = r.Length - radius;
            if (alt > maxAlt) { maxAlt = alt; maxAltT = t; maxAltPoint = r; }
            if (alt <= 0)
            {
                var prevAlt = previous.Length - radius;
                var f = prevAlt / Math.Max(1e-9, prevAlt - alt);
                var hit = previous + (r - previous) * f;
                var tHit = t - StepSeconds * (1 - f);
                points.Add(ToWorld(hit, tHit));
                impactWorld = ToWorld(hit, tHit);
                WillImpact = true;
                TimeToImpact = t - StepSeconds * (1 - f);
                return true;
            }
            if (step % 4 == 0) points.Add(ToWorld(r, t));
            if (alt >= AtmosphereTop && Vec.Dot(r, v) > 0) { points.Add(ToWorld(r, t)); return false; }
        }
        return true;
    }

    private void Rk4(ref Vec r, ref Vec v, double dt)
    {
        var k1r = v; var k1v = Accel(r, v);
        var k2r = v + k1v * (dt / 2); var k2v = Accel(r + k1r * (dt / 2), k2r);
        var k3r = v + k2v * (dt / 2); var k3v = Accel(r + k2r * (dt / 2), k3r);
        var k4r = v + k3v * dt; var k4v = Accel(r + k3r * dt, k4r);
        r = r + (k1r + k2r * 2 + k3r * 2 + k4r) * (dt / 6);
        v = v + (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6);
    }

    // Inertial acceleration: gravity, plus drag against the velocity
    // relative to the air (which turns with Earth).
    private Vec Accel(Vec r, Vec v)
    {
        var rl = r.Length;
        var gravity = r * (-mu / (rl * rl * rl));
        var air = v - Vec.Cross(spin, r);
        var speed = air.Length;
        var drag = flight.DragDecelerationAt(rl - radius, speed);
        return speed > 1e-6 ? gravity - air * (drag / speed) : gravity;
    }

    // Integrates under Earth's and the Moon's gravity (outside the air):
    // true when finished (Moon impact, too far, out of time), false on
    // coming back into Earth's atmosphere.
    private bool FollowMoonPath(ref Vec r, ref Vec v, ref double t)
    {
        var rd = new Double3(r.x, r.y, r.z); var vd = new Double3(v.x, v.y, v.z);
        var start = t;
        var lastDrawn = rd; var lastNear = false;
        for (var step = 0; step < MoonPathMaxSteps && t - start < MoonPathSeconds; step++)
        {
            var rm = moonTrack.Position(t);
            var h = EarthMoonDynamics.SuggestedStep(rd, vd, rm, moonTrack.Velocity(t), mu, radius);
            var previous = rd; var previousT = t;
            EarthMoonDynamics.Step(ref rd, ref vd, t, h, moonTrack, mu);
            t += h;
            rm = moonTrack.Position(t);
            var fromMoon = rd - rm;
            var near = fromMoon.Length < MoonBody.SphereOfInfluence;
            var moonAltitude = fromMoon.Length - MoonBody.RadiusMeters;
            if (near)
            {
                if (!MoonEncounter) MoonEncounter = true;
                if (moonAltitude < MoonPeriapsisAltitude)
                {
                    MoonPeriapsisAltitude = moonAltitude; TimeToMoonPeriapsis = t;
                    moonPeWorld = MoonWorld(fromMoon);
                }
            }
            if (moonAltitude <= 0)
            {
                // Down on the Moon: the crossing between the last two steps.
                var previousAltitude = (previous - moonTrack.Position(previousT)).Length - MoonBody.RadiusMeters;
                var f = previousAltitude / Math.Max(1e-9, previousAltitude - moonAltitude);
                var hitT = previousT + (t - previousT) * f;
                var hit = previous + (rd - previous) * f - moonTrack.Position(hitT);
                moonImpactWorld = MoonWorld(hit);
                points.Add(moonImpactWorld);
                WillImpactMoon = true; TimeToMoonImpact = hitT;
                TimeToMoonPeriapsis = -1;
                return true;
            }
            if (rd.Length - radius < AtmosphereTop && Double3.Dot(rd, vd) < 0)
            {
                r = new Vec(rd.x, rd.y, rd.z); v = new Vec(vd.x, vd.y, vd.z);
                points.Add(ToWorld(r, t));
                return false;
            }
            if (rd.Length > 2e9) break;
            // Inside the Moon's sphere: drawn round the Moon as it is now.
            if (near != lastNear || (rd - lastDrawn).Length > (near ? 20000 : 500000) || step % 50 == 0)
            {
                points.Add(near ? MoonWorld(fromMoon) : World(rd));
                lastDrawn = rd; lastNear = near;
            }
        }
        if (MoonEncounter && TimeToMoonPeriapsis >= 0 && double.IsInfinity(MoonPeriapsisAltitude)) TimeToMoonPeriapsis = -1;
        return true;
    }

    // A point (m) relative to the Moon's centre, drawn where the Moon is now.
    private Vector3 MoonWorld(Double3 fromMoon) => (centre + (moonNow + fromMoon) * PlanetBody.WorldUnitsPerMeter).ToVector3();
    // An inertial point (m from Earth's centre) as the scene is now.
    private Vector3 World(Double3 metres) => (centre + metres * PlanetBody.WorldUnitsPerMeter).ToVector3();

    // Follows the exact Kepler orbit from (r, v), outside the atmosphere.
    // Returns true when finished (closed orbit drawn, escape, or too far),
    // false on re-entering the atmosphere (r, v, t advanced to that point).
    private bool FollowConic(ref Vec r, ref Vec v, ref double t, ref double maxAlt, ref double maxAltT, ref Vec maxAltPoint)
    {
        var c = Conic.From(r, v, mu);
        if (c.hMag < 1e-3) return true;
        var entry = radius + AtmosphereTop;
        if (c.e >= 1)
        {
            // Hyperbola: sweep true anomaly out toward the asymptote.
            var nuEnd = Math.Max(c.nu0, Math.Acos(-1 / c.e) * .98);
            for (var i = 1; i <= ConicSamples; i++)
            {
                var nu = c.nu0 + (nuEnd - c.nu0) * i / ConicSamples;
                var dist = c.p / (1 + c.e * Math.Cos(nu));
                if (dist <= 0 || dist > radius * 30) break;
                points.Add(ToWorld((c.pHat * Math.Cos(nu) + c.qHat * Math.Sin(nu)) * dist, t));
            }
            return true;
        }
        // Ellipse: sample evenly in eccentric anomaly E (even in true anomaly
        // would leave a near-vertical launch's thin ellipse almost unsampled
        // around its top), one revolution from here.
        var e0 = c.EccentricAnomaly(c.nu0);
        var prevE = e0;
        // An orbit that stays clear of the air is drawn as its fixed ellipse;
        // one that comes back down follows the ground as it turns.
        var closed = c.a * (1 - c.e) > entry;
        for (var i = 1; i <= ConicSamples; i++)
        {
            var E = e0 + 2 * Math.PI * i / ConicSamples;
            var pos = c.Position(E);
            var dist = pos.Length;
            if (dist > radius * 30) return true;
            var alt = dist - radius;
            if (alt > maxAlt) { maxAlt = alt; maxAltT = t + c.TimeBetween(e0, E, mu); maxAltPoint = pos; }
            if (dist < entry)
            {
                // Re-entering the air: find the crossing, carry on integrating.
                var lo = prevE; var hi = E;
                for (var k = 0; k < 30; k++)
                {
                    var mid = (lo + hi) / 2;
                    if (c.Position(mid).Length > entry) lo = mid; else hi = mid;
                }
                r = c.Position(hi); v = c.Velocity(hi, mu);
                t += c.TimeBetween(e0, hi, mu);
                points.Add(ToWorld(r, t));
                return false;
            }
            points.Add(ToWorld(pos, closed ? t : t + c.TimeBetween(e0, E, mu)));
            prevE = E;
        }
        return true;
    }

    // Inertial point (m from the centre) at 'seconds' from now, to where it
    // is over the ground in the Earth-fixed scene: Earth will have turned
    // eastward by spin × time, so the point sits that much further west.
    // (Planet's north axis is world +Y, as PlanetBody assumes.)
    private Vector3 ToWorld(Vec metres, double seconds = 0)
    {
        var turn = -spinRate * seconds;
        var c = Math.Cos(turn); var s = Math.Sin(turn);
        var x = metres.x * c - metres.z * s;
        var z = metres.x * s + metres.z * c;
        return (centre + new Double3(x, metres.y, z) * PlanetBody.WorldUnitsPerMeter).ToVector3();
    }

    // --- Markers --------------------------------------------------------------

    private void OnGUI()
    {
        if (LaunchMenu.Open) return;   // the launch menu covers the scene
        if (!HasOrbit || viewCamera == null || Event.current.type != EventType.Repaint) return;
        labelStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
        var cyan = new Color(.4f, .9f, 1f);
        if (showAp) Marker(apWorld, "Ap " + Km(ApoapsisAltitude) + (TimeToApoapsis > 0 ? "  T−" + Clock(TimeToApoapsis) : ""), cyan);
        if (showPe) Marker(peWorld, "Pe " + Km(PeriapsisAltitude), cyan);
        if (WillImpact) Marker(impactWorld, "Impact  T−" + Clock(TimeToImpact), new Color(1f, .35f, .3f));
        var grey = new Color(.85f, .85f, .95f);
        if (MoonEncounter && !WillImpactMoon && TimeToMoonPeriapsis > 0)
            Marker(moonPeWorld, "Moon Pe " + Km(MoonPeriapsisAltitude) + "  T−" + Clock(TimeToMoonPeriapsis), grey);
        if (WillImpactMoon) Marker(moonImpactWorld, "Moon impact  T−" + Clock(TimeToMoonImpact), new Color(1f, .45f, .35f));
    }

    private void Marker(Vector3 world, string text, Color color)
    {
        var vp = viewCamera.WorldToViewportPoint(world);
        if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) return;
        var x = vp.x * Screen.width; var y = (1 - vp.y) * Screen.height;
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x - 3, y - 3, 6, 6), Texture2D.whiteTexture);
        labelStyle.normal.textColor = color;
        GUI.Label(new Rect(x + 7, y - 10, 220, 22), text, labelStyle);
        GUI.color = old;
    }

    public static string Km(double metres) =>
        double.IsInfinity(metres) ? "escape" : (metres / 1000).ToString("N0") + " km";

    public static string Clock(double seconds)
    {
        if (seconds < 0) return "—";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    // --- Orbit maths ----------------------------------------------------------

    // A two-body orbit from a state vector: plane basis (P toward periapsis,
    // Q 90° ahead), eccentricity, semi-latus rectum p, semi-major axis a,
    // and the true anomaly nu0 of the starting point.
    private readonly struct Conic
    {
        public readonly Vec pHat, qHat;
        public readonly double e, p, a, hMag, nu0;

        private Conic(Vec pHat, Vec qHat, double e, double p, double a, double hMag, double nu0)
        { this.pHat = pHat; this.qHat = qHat; this.e = e; this.p = p; this.a = a; this.hMag = hMag; this.nu0 = nu0; }

        public static Conic From(Vec r, Vec v, double mu)
        {
            var rMag = r.Length;
            var h = Vec.Cross(r, v);
            var hMag = h.Length;
            if (hMag < 1e-9) return new Conic(r / rMag, r / rMag, 1, 0, double.PositiveInfinity, 0, 0);
            var eVec = Vec.Cross(v, h) / mu - r / rMag;
            var e = eVec.Length;
            var p = hMag * hMag / mu;
            var pHat = e > 1e-6 ? eVec / e : r / rMag;   // near-circular: measure from here
            var qHat = Vec.Cross(h / hMag, pHat);
            var nu0 = Math.Atan2(Vec.Dot(r, qHat), Vec.Dot(r, pHat));
            return new Conic(pHat, qHat, e, p, p / (1 - e * e), hMag, nu0);
        }

        private double B => a * Math.Sqrt(Math.Max(0, 1 - e * e));
        public double EccentricAnomaly(double nu) =>
            2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
        public Vec Position(double E) => pHat * (a * (Math.Cos(E) - e)) + qHat * (B * Math.Sin(E));
        public Vec Velocity(double E, double mu)
        {
            var r = a * (1 - e * Math.Cos(E));
            var k = Math.Sqrt(mu * a) / r;
            return pHat * (-k * Math.Sin(E)) + qHat * (k * Math.Sqrt(Math.Max(0, 1 - e * e)) * Math.Cos(E));
        }
        // Kepler's equation: time from eccentric anomaly E0 forward to E1.
        public double TimeBetween(double e0, double e1, double mu) =>
            ((e1 - e * Math.Sin(e1)) - (e0 - e * Math.Sin(e0))) * Math.Sqrt(a * a * a / mu);
        public double TimeTo(double targetNu, double mu)
        {
            var e0 = EccentricAnomaly(nu0);
            var e1 = EccentricAnomaly(targetNu);
            while (e1 <= e0) e1 += 2 * Math.PI;
            return TimeBetween(e0, e1, mu);
        }
    }

    // Minimal double-precision vector for the orbit maths.
    private readonly struct Vec
    {
        public readonly double x, y, z;
        public Vec(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public static Vec operator +(Vec a, Vec b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec operator -(Vec a, Vec b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec operator *(Vec a, double s) => new(a.x * s, a.y * s, a.z * s);
        public static Vec operator /(Vec a, double s) => new(a.x / s, a.y / s, a.z / s);
        public static double Dot(Vec a, Vec b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vec Cross(Vec a, Vec b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }
}
