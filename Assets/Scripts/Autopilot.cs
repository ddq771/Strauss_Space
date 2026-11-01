using System;
using UnityEngine;

/// <summary>
/// Flies the rocket by itself - to a low Earth parking orbit, to the highest
/// Earth orbit it can reach (for study), or on from low orbit to a celestial
/// body (the Moon for now) - through the same controls a pilot
/// has: it points the vehicle (SAS steers with the engines' gimbals under
/// thrust, RCS while coasting), sets the throttle, lets auto-staging run the
/// real staging timeline and uses rails time warp through the long coasts.
///
/// How it controls the vehicle: it never touches the physics directly. Each
/// physics step it computes the direction the nose should point and hands
/// it to RocketAssemblyController as AutopilotAttitude (a rotation); the
/// existing SAS then gimbals the engines (or fires RCS when coasting) to
/// turn the vehicle there. It sets the throttle through the same call the
/// pilot's keys use, lets auto-staging separate and light stages, and asks
/// TimeWarp for rails warp when there's a long coast.
///
/// How it knows where it is: every step it reads the rocket's position
/// (from Earth's centre, in metres, from the scene position and
/// FloatingOrigin) and its velocity relative to the stars (ground velocity
/// plus Earth's spin × position), and turns those into the orbit's low and
/// high points with the standard two-body formulas (Elements).
///
/// Ascent: straight up for 250 m to clear the pad, then a gravity turn
/// whose pitch above the horizon follows 90° − 65°·(h/70 km)^0.55 while the
/// air is thick - and is pulled back toward the airflow whenever it's more
/// than 2-10° off it (less allowed the higher the dynamic pressure), so a
/// finless vehicle isn't flipped. Above 35 km closed-loop guidance takes
/// over: horizontally it points along the "velocity still to gain" (the
/// 200 km circular orbit's velocity in the target plane minus the velocity
/// it has - this automatically removes any sideways motion out of the
/// plane); vertically it picks the pitch whose share of thrust gives the
/// vertical acceleration needed to arrive at 200 km with no vertical speed
/// left. It cuts off when the orbit's low point is there.
///
/// Low orbit: the ascent's 200 km parking orbit, where a destination can
/// then be picked.
///
/// Maximum orbit: before launch it predicts the highest circular orbit the
/// rocket can reach: it simulates this rocket's whole ascent to low orbit
/// (its real mass, payload, engines, staging and drag - SimulateAscent) to
/// find the Δv it will have left there, and searches from that. In flight it
/// parks in low orbit, measures the Δv really left, keeps a reserve, and
/// takes a Hohmann transfer to the highest circular orbit that affords: a
/// prograde burn to raise the high point, a coast half an orbit, a prograde
/// burn there to circularise. The ceiling is not the rocket but the Moon:
/// above ~200,000 km from Earth's centre its pull (and the Sun's) breaks a
/// circular orbit up (see StableOrbitRadius), so a rocket with Δv to spare
/// stops there and says how much it has left.
///
/// Moon: on the pad it searches the next day for a launch time (the launch
/// window) and fast-forwards the clock to it; the parking orbit's plane is
/// chosen to contain where the Moon will be on arrival. In orbit it walks
/// along the coming orbit in 20 s steps looking for the point from which a
/// transfer reaches the Moon's distance just as the Moon gets there, then
/// solves for the Δv that passes 150 km over the surface by repeatedly
/// integrating the trip under Earth's and the Moon's gravity (the same
/// EarthMoonDynamics the trajectory display and rails warp use) and
/// correcting the Δv by Newton's method. It then simulates the real,
/// minutes-long burn the same way and tunes it. Two course corrections on
/// the way trim the pass; at the closest point it brakes against its motion
/// relative to the Moon until the speed there is circular-orbit speed - or
/// as far as the propellant allows.
///
/// Any steering, throttle, SAS or ignition key hands control back to the pilot.
/// </summary>
[RequireComponent(typeof(RocketAssemblyController))]
public sealed class Autopilot : MonoBehaviour
{
    public enum Destination { None, LowOrbit, MaxOrbit, Moon, CustomOrbit }

    // What the mission is doing now - a simple state machine. FixedUpdate
    // runs Ascent() while step is Ascent and Maneuver() for every later
    // step. Maneuver() handles each burn the same way: on entering a step,
    // Plan() fills in when the burn starts, which way to steer (Steer) and
    // what ends it (Cutoff); then it coasts (warping) until TurnLead seconds
    // before, turns to the burn direction, burns until the cutoff test
    // passes, and Advance() moves step on to the next state.
    // Lower / LowerCircularize are the downward mirror of Raise / Circularize,
    // used only by a custom-height orbit below the present one.
    private enum Step { Idle, Window, Ascent, Insert, Raise, Circularize, Lower, LowerCircularize, Escape, Tli, Mcc1, Mcc2, Loi, Done, Failed }
    private enum Steer { Prograde, Retrograde, MoonRetrograde, Fixed, Tli }
    private enum Cutoff { ApoapsisAbove, PeriapsisAbove, ApoapsisBelow, PeriapsisBelow, Energy, DeltaV, MoonCircular }

    private const double ParkingAltitude = 200000;          // m

    // Custom orbit: the circular orbit height (m above the surface) the pilot
    // typed into the panel. The lowest allowed is 150 km - below that the
    // air drags an orbit down within days; the highest is the stability
    // ceiling StableOrbitRadius (minus Earth's radius), above which the Moon
    // breaks a circular orbit up.
    public double CustomAltitude = 1_000_000;

    // Max orbit with more Δv than the highest stable orbit needs: the pilot
    // is asked (a pop-up that pauses the game) whether to spend all of it and
    // escape Earth, or stay at the highest stable Earth orbit. Ask = not
    // answered yet this mission (reset by Engage); while the pop-up is open
    // 'askingEscape' is set and spareDeltaV / escapeSpeed hold its numbers.
    private enum EscapeChoice { Ask, Escape, Stay }
    private EscapeChoice escapeChoice;
    private bool askingEscape;
    private double spareDeltaV, escapeSpeed, stableTarget;
    private const double MinCustomAltitude = 150000;
    private double MaxCustomAltitude => StableOrbitRadius - (planet != null ? planet.Radius : 6_371_000);

    // The height the ascent aims for. How: normally the 200 km parking
    // orbit; for a custom orbit lower than that, the custom height itself -
    // the ascent guidance just steers to whatever 'target' radius it is
    // given, so going straight there saves a transfer back down. A custom
    // orbit above 200 km still parks at 200 km first, then raises with a
    // Hohmann transfer (the ascent is only tuned for low targets).
    private double AscentAltitude =>
        Target == Destination.CustomOrbit ? Math.Min(CustomAltitude, ParkingAltitude) : ParkingAltitude;
    // The highest circular Earth orbit that stays one (m from Earth's
    // centre). Found by simulating circular, prograde orbits for 180 days
    // under Earth + the Moon + the Sun (the forces this game applies), from
    // several starting Moon and Sun positions: at 150,000 km the radius
    // stays within ±1 %, at 200,000 km ±4 %, at 220,000 km ±10 %, at 250,000
    // km it swings 136,000-310,000 km, and anywhere above the Moon (550,000-
    // 750,000 km) it is thrown out to millions of km or passes within 2,000
    // km of the Moon. So ~52 % of the Moon's distance is the ceiling.
    private const double StableOrbitRadius = 200_000_000;
    private const double LunarPassAltitude = 150000;        // m above the Moon's surface
    private const double DeltaVReserve = .05;               // held back for finite-burn and steering losses
    private const double VerticalRise = 250;                // m before pitching over
    private const double ProfileTop = 70000;                // m: end of the open-loop pitch profile
    private const double ClosedLoopFrom = 35000;            // m: closed-loop guidance from here
    private const double TurnLead = 240;                    // s before a burn to turn toward it
    private const double TransferApogeeFactor = 1.25;       // translunar apogee / the Moon's distance
    private const float PitchRate = 1.5f;                   // deg/s, closed-loop pitch changes
    private const float AlignedDegrees = 6;                 // pointing error at which a burn may light

    private RocketAssemblyController assembly;
    private Rocket rocket;
    private RocketFlightModel flight;
    private TrajectoryDisplay trajectory;
    private PlanetBody planet;

    public Destination Target { get; private set; }
    public string Phase => step.ToString();
    public bool Engaged => step != Step.Idle && step != Step.Done && step != Step.Failed;
    public string Status { get; private set; } = "";
    public string Detail { get; private set; } = "";
    /// <summary>Lets the autopilot run rails / physics time warp through coasts.</summary>
    public bool AutoWarp = true;

    private Step step = Step.Idle;
    private double mu, radius;

    // Clock and inertial frame. The scene is fixed to the turning Earth, so
    // its axes rotate relative to the stars. Orbit planning needs axes that
    // don't: "I" is the scene's axes frozen at the moment the mission was
    // engaged ('origin'). The clock (Now) counts simulated seconds since then
    // from SolarSystem's date - the same clock that places the Moon - and
    // Earth has turned by Theta = spin rate × Now since, so a scene vector
    // becomes an I vector by turning it back by Theta about the spin (Y) axis.
    private DateTime origin;
    private double fallbackClock;
    private EarthMoonDynamics.MoonTrack moonTrack;
    private Double3 planeNormalI;            // target orbit plane (angular momentum direction)

    // Ascent.
    private bool closedLoop;
    private float gammaCommand = 90, gammaLatch;

    // The planned burn.
    private bool planned, burning;
    private double replanAt = -1;            // not planned yet: coast until then, then plan
    private double burnStart, burnDuration, cutoffValue, deltaVDone, deltaVPlanned;
    private Steer steer;
    private Cutoff cutoff;
    private Double3 fixedDirectionI;
    private double tliPrograde, tliNormal, tliRadial;
    private double orbitTarget;              // m, best-orbit altitude
    private bool warpedByAutopilot;
    private double lastWarpRequest = -1;

    // Unity calls this once after the scene loads: it adds an Autopilot to
    // every rocket (the object carrying RocketAssemblyController), so no
    // scene or prefab needs editing.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        foreach (var model in FindObjectsByType<RocketAssemblyController>(FindObjectsSortMode.None))
            if (model.GetComponent<Autopilot>() == null) model.gameObject.AddComponent<Autopilot>();
    }

    private void Awake()
    {
        assembly = GetComponent<RocketAssemblyController>();
        rocket = GetComponent<Rocket>();
        flight = GetComponent<RocketFlightModel>();
        trajectory = GetComponent<TrajectoryDisplay>();
        planet = FindFirstObjectByType<PlanetBody>();
    }

    // --- Engage / disengage --------------------------------------------------

    /// <summary>
    /// Flies to a destination. How: resets the clock and frame (origin = now),
    /// builds a table of the Moon's positions for the trip if needed, turns
    /// SAS and auto-staging on, then picks the starting state from where the
    /// rocket is: already in orbit (low point above 120 km) → straight to the
    /// destination's first burn; on the pad bound for the Moon → wait for the
    /// launch window; otherwise → launch (if needed) and fly the ascent.
    /// </summary>
    public void Engage(Destination destination)
    {
        if (planet == null || flight == null || flight.Crashed) return;
        if (destination == Destination.Moon && (MoonBody.Instance == null || SolarSystem.Instance == null))
        { Fail("No Moon in this scene."); return; }
        // A custom height outside 150 km .. the stability ceiling is refused
        // here, before anything is reset, so a typo can't start a flight.
        if (destination == Destination.CustomOrbit &&
            (CustomAltitude < MinCustomAltitude || CustomAltitude > MaxCustomAltitude))
        {
            Fail("height must be " + TrajectoryDisplay.Km(MinCustomAltitude) + " to " + TrajectoryDisplay.Km(MaxCustomAltitude) + ".");
            return;
        }
        Target = destination;
        mu = PlanetBody.UniversalGravitationalConstant * planet.Mass;
        radius = planet.Radius;
        origin = SolarSystem.Instance != null ? SolarSystem.Instance.DateAt(Time.time) : default;
        fallbackClock = 0;
        moonTrack = destination == Destination.Moon
            ? new EarthMoonDynamics.MoonTrack(origin, SolarSystem.Instance.RotationAngleAt(origin), 12 * 86400) : null;
        planned = burning = false; replanAt = -1; kickCount = -1; lateCorrections = 0; lateCorrection = false;
        escapeChoice = EscapeChoice.Ask; CloseEscapePrompt();
        predictedMax = destination == Destination.MaxOrbit || destination == Destination.CustomOrbit ? PredictMaxOrbit(out _) : -1;
        // Custom orbit from the pad: the pre-launch simulation already says
        // the highest circular orbit this rocket reaches (predictedMax, m
        // altitude; −1 = it can't simulate one). If the typed height is above
        // that, refuse now rather than strand the rocket half-way.
        if (destination == Destination.CustomOrbit && !rocket.Launched && predictedMax > 0 && CustomAltitude > predictedMax + 1000)
        {
            Fail("this rocket can reach at most ≈ " + TrajectoryDisplay.Km(predictedMax) + ".");
            return;
        }
        assembly.EngageSas();
        assembly.SetAutoStaging(true);

        // Already in orbit: carry on from there; otherwise fly the ascent.
        GetState(out var r, out var v);
        Elements(r, v, out var pe, out _, out _);
        if (rocket.Launched && pe - radius > 120000)
        {
            planeNormalI = Double3.Cross(ToI(r), ToI(v)).Normalized;
            if (destination == Destination.LowOrbit) { Finish("Already in orbit: " + OrbitText(r, v)); return; }
            if (destination == Destination.CustomOrbit) { Status = "Engaged"; StartCustomOrbit(); return; }
            step = destination == Destination.Moon ? Step.Tli : Step.Raise;
        }
        else if (!rocket.Launched && destination == Destination.Moon && (launchAt = Now + LaunchWindow(r)) > Now + 120)
        {
            step = Step.Window;
            Status = "Waiting for the launch window";
            return;
        }
        else if (!LiftOff(r, v)) return;
        Status = "Engaged";
    }

    // Starts the ascent: chooses the orbit plane (from the current motion if
    // already flying fast, otherwise from ChoosePlane), resets the pitch
    // program to vertical, drops any time warp and launches through the same
    // routine as the Launch button. False (and Failed) if the launch is
    // refused - e.g. thrust-to-weight under 1.
    private bool LiftOff(Double3 r, Double3 v)
    {
        planeNormalI = rocket.Launched && v.Length > 1000 ? Double3.Cross(ToI(r), ToI(v)).Normalized : ChoosePlane(r, Now);
        closedLoop = false; gammaCommand = 90;
        step = Step.Ascent;
        if (!rocket.Launched)
        {
            if (TimeWarp.Rate > 1) TimeWarp.Request(1);
            assembly.Launch();
            if (!rocket.Launched) { Fail(assembly.Notice); return false; }
        }
        Status = "Ascent";
        return true;
    }

    // Hands control back: clearing AutopilotSteering makes SAS hold whatever
    // attitude it latches next, and any warp the autopilot started is ended.
    public void Disengage(string reason = null)
    {
        if (step == Step.Idle) return;
        CloseEscapePrompt();   // unpauses the game if the escape question was open
        step = Step.Idle;
        assembly.AutopilotSteering = false;
        if (warpedByAutopilot && TimeWarp.Rate > 1) TimeWarp.Request(1);
        warpedByAutopilot = false;
        Status = reason ?? "Off";
        Detail = "";
    }

    // Mission can't continue: stop steering and say why (the engines are
    // left as they are so a pilot can take over).
    private void Fail(string reason)
    {
        step = Step.Failed;
        assembly.AutopilotSteering = false;
        Status = "Stopped: " + reason;
        Detail = "";
    }

    // Mission complete: engines off, steering released, warp ended.
    private void Finish(string text)
    {
        assembly.SetAutopilotThrottle(0);
        assembly.AutopilotSteering = false;
        step = Step.Done;
        Status = text;
        Detail = "";
        if (warpedByAutopilot && TimeWarp.Rate > 1) TimeWarp.Request(1);
        warpedByAutopilot = false;
    }

    // --- Clock and frames ----------------------------------------------------

    // Simulated seconds since engaging: SolarSystem's date at this frame /
    // physics step minus the date at origin (fallbackClock counts physics
    // steps if there's no SolarSystem).
    private double Now => SolarSystem.Instance != null ? (SolarSystem.Instance.DateAt(Time.time) - origin).TotalSeconds : fallbackClock;
    // How far Earth (and the scene) has turned since origin, radians.
    private double Theta => planet.RotationRate * Now;
    // Scene → I: undo Earth's turn since origin (rotate by +Theta about Y,
    // the same convention TimeWarp uses). I → scene: the opposite.
    private Double3 ToI(Double3 scene) => scene.RotateAboutY(Theta);
    private Double3 ToScene(Double3 inertial) => inertial.RotateAboutY(-Theta);

    /// <summary>
    /// Position (m from Earth's centre) and inertial velocity (m/s), scene
    /// axes. Position: the rocket's scene position minus Earth's centre (both
    /// in double precision via FloatingOrigin), divided by the scene's 0.001
    /// units per metre. Velocity: the ground-relative velocity the flight
    /// model reports plus the ground's own motion, ω × r (Earth's spin).
    /// </summary>
    private void GetState(out Double3 r, out Double3 v)
    {
        r = (new Double3(transform.position) - FloatingOrigin.PlanetCentre) / PlanetBody.WorldUnitsPerMeter;
        v = new Double3(flight.GroundVelocity) + Double3.Cross(new Double3(planet.SpinVector), r);
    }

    // The orbit's lowest and highest points (m from the centre) and its
    // specific energy, for an Earth orbit through (r, v). How: specific
    // energy ε = v²/2 − μ/r; angular momentum h = r × v; the eccentricity
    // vector e = (v × h)/μ − r/|r| (its length is the eccentricity); the
    // semi-latus rectum p = h²/μ; then the conic r(ν) = p/(1 + e·cos ν) is
    // lowest at ν = 0 (p/(1+e)) and highest at ν = 180° (p/(1−e)) - no high
    // point (infinite) when e ≥ 1, an escape path.
    private void Elements(Double3 r, Double3 v, out double periapsis, out double apoapsis, out double energy)
    {
        var rl = r.Length;
        energy = v.SqrLength / 2 - mu / rl;
        var h = Double3.Cross(r, v);
        var e = (Double3.Cross(v, h) / mu - r / rl).Length;
        var p = h.SqrLength / mu;
        periapsis = p / (1 + e);
        apoapsis = e < 1 ? p / (1 - e) : double.PositiveInfinity;
    }

    // Seconds until the high point. How: the semi-major axis a from the
    // energy (a = −μ/2ε) and the eccentricity as above; the eccentric
    // anomaly E from r = a(1 − e·cos E) (past the high point when moving
    // inward, r·v < 0, so E > 180°); Kepler's equation M = E − e·sin E turns
    // that into the mean anomaly, which grows at a steady √(μ/a³) rad/s; the
    // high point is at M = π, so time = (π − M) / √(μ/a³), wrapped forward.
    private double TimeToApoapsis(Double3 r, Double3 v)
    {
        var rl = r.Length;
        var energy = v.SqrLength / 2 - mu / rl;
        if (energy >= 0) return -1;
        var a = -mu / (2 * energy);
        var h = Double3.Cross(r, v);
        var e = (Double3.Cross(v, h) / mu - r / rl).Length;
        if (e < 1e-5) return 0;
        var E = Math.Acos(Math.Max(-1, Math.Min(1, (1 - rl / a) / e)));
        if (Double3.Dot(r, v) < 0) E = 2 * Math.PI - E;
        var M = E - e * Math.Sin(E);
        var toGo = Math.PI - M;
        if (toGo < 0) toGo += 2 * Math.PI;
        return toGo * Math.Sqrt(a * a * a / mu);
    }

    // Launch plane, as its normal (the direction of the orbit's angular
    // momentum) in the I frame, for a launch at time 'launch'. How: the pad's
    // position and Earth's spin axis are turned into I axes for that moment;
    // "due east" is spin × site, and the orbit through the site heading east
    // has normal site × east - the least inclined orbit, keeping all of
    // Earth's 463 m/s. For the Moon the normal is site × (Moon on arrival),
    // so the plane contains both; it's flipped if needed so the orbit still
    // runs eastward (normal on the same side as the eastward one).
    private Double3 ChoosePlane(Double3 r, double launch)
    {
        var siteI = r.RotateAboutY(planet.RotationRate * launch);
        var spinI = new Double3(planet.SpinVector).RotateAboutY(planet.RotationRate * launch);
        var eastward = Double3.Cross(siteI, Double3.Cross(spinI, siteI)).Normalized;
        if (Target != Destination.Moon) return eastward;
        var arrival = MoonOnArrival(launch, out _);
        var normal = Double3.Cross(siteI, arrival);
        if (normal.Length < 1e-3 * siteI.Length * arrival.Length) return eastward;
        normal = normal.Normalized;
        return Double3.Dot(normal, eastward) >= 0 ? normal : -normal;
    }

    // Where the Moon will be (inertial) when a flight launched at 'launch'
    // gets there. How: adds up the trip - ~540 s to orbit, one revolution
    // of the 200 km parking orbit (its period 2π√(r³/μ)), then the transfer
    // time from TransferToMoonDistance - and looks the Moon up in the
    // tabulated ephemeris (moonTrack) at that time. 'transferAngle' is how
    // far round Earth the transfer carries the vehicle.
    private Double3 MoonOnArrival(double launch, out double transferAngle)
    {
        var rp = radius + ParkingAltitude;
        var parkingPeriod = 2 * Math.PI * Math.Sqrt(rp * rp * rp / mu);
        var moonDistance = moonTrack.Position(launch + 3 * 86400).Length;
        TransferToMoonDistance(rp, moonDistance, out _, out var transferTime, out transferAngle);
        return moonTrack.Position(launch + 540 + parkingPeriod + transferTime);
    }

    // The launch window (s from now, within a day). How: tries every minute
    // of the next 26 hours; for each it builds the plane through the pad and
    // the Moon's arrival point (ChoosePlane) and scores it in m/s:
    //  * Earth's spin lost - the ground's eastward speed minus its share
    //    along the plane's direction of travel (0 for a due-east plane);
    //  * 300 m/s per radian between where the ascent ends (the pad turned
    //    ~20° downrange in the plane) and where the departure burn has to
    //    be (the Moon's arrival point turned back by the transfer angle) -
    //    so the burn falls at the orbit's low point, which matters when a
    //    core stage leaves an elliptical orbit.
    // The lowest score wins (ties keep the earlier time).
    private double LaunchWindow(Double3 r)
    {
        var spinRate = planet.RotationRate;
        double best = 0, bestCost = double.PositiveInfinity;
        for (var wait = 0.0; wait <= 26 * 3600; wait += 60)
        {
            var launch = Now + wait;
            var siteI = r.RotateAboutY(spinRate * launch);
            var spinI = new Double3(planet.SpinVector).RotateAboutY(spinRate * launch);
            var normal = ChoosePlane(r, launch);
            // Earth's spin lost by not launching due east.
            var groundSpeed = Double3.Cross(spinI, siteI);
            var along = Double3.Cross(normal, siteI.Normalized);
            var lost = groundSpeed.Length - Double3.Dot(groundSpeed, along);
            // Where the burn must be vs where the ascent ends (~20° downrange).
            var arrival = MoonOnArrival(launch, out var transferAngle);
            var insertion = Rotate(siteI.Normalized, normal, 20 * Mathf.Deg2Rad);
            var burnPoint = Rotate(arrival.Normalized, normal, -transferAngle);
            var phase = Math.Atan2(Double3.Dot(Double3.Cross(insertion, burnPoint), normal), Double3.Dot(insertion, burnPoint));
            var cost = lost + 300 * Math.Abs(phase);
            if (cost < bestCost - 1) { bestCost = cost; best = wait; }
        }
        return best;
    }

    // v turned by 'angle' about unit axis k, by Rodrigues' formula: the part
    // of v along k stays, the part across k turns in the plane with k × v.
    private static Double3 Rotate(Double3 v, Double3 k, double angle) =>
        v * Math.Cos(angle) + Double3.Cross(k, v) * Math.Sin(angle) + k * (Double3.Dot(k, v) * (1 - Math.Cos(angle)));

    // A transfer from a circular orbit of radius rp with its far point at
    // TransferApogeeFactor × the Moon's distance d: the speed it needs at
    // the start, the time to reach distance d, and how far round (rad). How:
    // the ellipse has low point rp and high point ra, so a = (rp + ra)/2 and
    // e = (ra − rp)/(ra + rp); the start speed is vis-viva, √(μ(2/rp − 1/a));
    // the eccentric anomaly at distance d comes from d = a(1 − e·cos E),
    // the time from Kepler's equation (E − e·sin E)·√(a³/μ), and the angle
    // round (true anomaly) from tan(ν/2) = √((1+e)/(1−e))·tan(E/2).
    private void TransferToMoonDistance(double rp, double d, out double speed, out double time, out double angle)
    {
        var ra = d * TransferApogeeFactor;
        var a = (rp + ra) / 2;
        var e = (ra - rp) / (ra + rp);
        speed = Math.Sqrt(mu * (2 / rp - 1 / a));
        var E = Math.Acos(Math.Max(-1, Math.Min(1, (1 - d / a) / e)));
        time = (E - e * Math.Sin(E)) * Math.Sqrt(a * a * a / mu);
        angle = 2 * Math.Atan(Math.Sqrt((1 + e) / (1 - e)) * Math.Tan(E / 2));
    }

    // --- Each frame: pilot override, warp, panel text ---------------------------

    private double launchAt;

    // Every frame: the parts that follow the frame clock rather than physics -
    // counting down to the launch window (warping the pad clock there and
    // launching at zero), noticing a crash or a return to the pad, handing
    // over to the pilot on any control key, managing time warp, and keeping
    // the panel's countdown text live while on rails (physics is paused then).
    private void Update()
    {
        if (step == Step.Idle) return;
        if (step == Step.Window)
        {
            if (PilotTookControl()) { Disengage("Off - pilot took control"); return; }
            var wait = launchAt - Now;
            Detail = "Liftoff in " + TrajectoryDisplay.Clock(Math.Max(0, wait)) + " (clock fast-forwarded)";
            if (AutoWarp)
            {
                var rate = wait > 20 ? 10f : 1f;
                foreach (var r in TimeWarp.Rates) if (r >= TimeWarp.RailsFrom && wait > 3 * r) rate = r;
                if (!Mathf.Approximately(TimeWarp.Rate, rate)) TimeWarp.Request(rate);
            }
            if (wait <= 0)
            {
                GetState(out var r, out var v);
                LiftOff(r, v);
            }
            return;
        }
        if (!rocket.Launched || flight.Crashed)
        {
            if (flight.Crashed && Engaged) Fail("vehicle lost.");
            else if (!rocket.Launched) Disengage();
            return;
        }
        if (!Engaged) return;
        if (PilotTookControl()) { Disengage("Off - pilot took control"); return; }
        ManageWarp();
        // Physics (and so the burn logic) is paused on rails: keep the countdown live.
        if (TimeWarp.OnRails && step != Step.Ascent)
            Detail = planned ? NextBurnText() + " in " + TrajectoryDisplay.Clock(Math.Max(0, burnStart - Now))
                             : NextBurnText() + " planned in " + TrajectoryDisplay.Clock(Math.Max(0, replanAt - Now));
    }

    // True when the pilot steers (the controller's pitch/yaw input), or
    // presses a throttle / ignition / SAS / re-entry key - read the same way
    // RocketAssemblyController reads them (focused window, no text field
    // active, test runs excluded).
    private bool PilotTookControl()
    {
        if (assembly.PilotSteering) return true;
        if (!Application.isFocused || RocketAssemblyController.IgnorePilotInput || GUIUtility.keyboardControl != 0) return false;
        return Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.R)
            || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
            || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    // Picks a time-warp rate from how long there is to wait. How: during the
    // ascent, a burn, or a due replanning it wants ×1; otherwise the wait is
    // the time until it must start turning toward the next burn (burnStart −
    // TurnLead) or until the next replanning. It then takes the fastest
    // rails rate r with wait > 3·r - i.e. at least ~3 s of real time left at
    // that speed, so even a slow frame can't jump past the event - or ×10
    // physics warp for the last stretch, when the vehicle must still be able
    // to turn. It only touches the rate when it started the warp itself or
    // wants a different one, and retries a refused rails request (e.g. below
    // 100 km) at most twice a second.
    private void ManageWarp()
    {
        if (!AutoWarp) return;
        float rate;
        if (step == Step.Ascent || burning || (!planned && replanAt <= Now)) rate = 1;
        else
        {
            // Time that can be skipped: up to the moment to start turning
            // toward the burn, or to the next replanning.
            var wait = planned ? burnStart - Now - TurnLead : replanAt - Now;
            // The fastest rails speed that still leaves ~3 s of real time to
            // go (a slow frame at ×1,000,000 covers days).
            rate = wait > 20 - (planned ? TurnLead : 0) ? 10 : 1;
            foreach (var r in TimeWarp.Rates) if (r >= TimeWarp.RailsFrom && wait > 3 * r) rate = r;
        }
        if (rate == 1 && !warpedByAutopilot) return;
        if (Mathf.Approximately(TimeWarp.Rate, rate)) return;
        // Don't hammer a refused request (below 100 km, etc.) every frame.
        if (Time.unscaledTime - lastWarpRequest < .5f && rate >= TimeWarp.RailsFrom) return;
        lastWarpRequest = Time.unscaledTime;
        if (TimeWarp.Request(rate)) warpedByAutopilot = rate > 1;
    }

    // --- Each physics step: guidance -----------------------------------------------

    // Every physics step: the guidance itself (steering and throttle). On
    // rails the body is moved along its orbit by TimeWarp and can't be
    // steered, so nothing is done then.
    private void FixedUpdate()
    {
        if (!Engaged || !rocket.Launched || flight.Crashed || planet == null) return;
        if (SolarSystem.Instance == null) fallbackClock += Time.fixedDeltaTime * TimeWarp.ClockMultiplier;
        if (TimeWarp.OnRails) return;   // moved along its orbit; nothing to steer
        switch (step)
        {
            case Step.Ascent: Ascent(); break;
            default: Maneuver(); break;
        }
    }

    // Asks SAS to point the nose along a direction (scene axes). How: the
    // rotation that swings the nose (the rocket's local up, transform.up)
    // onto the direction by the shortest turn, applied to the present
    // attitude - so roll is left as it is - is handed to the controller as
    // AutopilotAttitude; with AutopilotSteering set, SAS holds that instead
    // of an attitude it latched itself.
    private void Point(Double3 directionScene)
    {
        if (directionScene.SqrLength < 1e-12) return;
        var direction = directionScene.Normalized.ToVector3();
        assembly.AutopilotAttitude = Quaternion.FromToRotation(transform.up, direction) * transform.rotation;
        assembly.AutopilotSteering = true;
    }

    // Producing thrust right now: liquid engines lit or solid boosters
    // burning, and the flight model reporting a positive thrust.
    private bool Thrusting => (rocket.EngineEnabled || flight.SolidBurning) && flight.Thrust > 0;

    /// <summary>Nothing left to burn: this stage's liquid tanks spent (either
    /// one empty), no solid propellant, not mid-separation, and no further
    /// stage to light (the stage index is the last one).</summary>
    private bool OutOfPropellant =>
        LiquidSpent && flight.SolidPropellant <= 0 && !assembly.StagingInProgress &&
        assembly.StageIndex >= assembly.StageCount - 1;
    // Either tank empty stops the engines, whatever is left in the other.
    private bool LiquidSpent => flight.FuelRemaining <= 0 || flight.OxidizerRemaining <= 0;

    // Sets the throttle through the controller, as the pilot would. How:
    // does nothing while a stage is separating or waiting to light (staging
    // lights it itself); raises any non-zero setting to the engines' lowest
    // working throttle (below it the engine model can't run); and doesn't try
    // to relight with a tank empty. SetAutopilotThrottle then lights the
    // engines if they're off, or shuts them down at 0.
    private void SetThrottle(float throttle)
    {
        if (assembly.StagingInProgress) return;   // separating / waiting to light
        if (throttle > 0) throttle = Mathf.Max(throttle, assembly.MinimumThrottle);
        if (throttle > 0 && LiquidSpent) return;
        assembly.SetAutopilotThrottle(throttle);
    }

    // The ascent guidance, run every physics step. How, in order:
    //  1. state: position r, inertial velocity v, the local vertical 'up',
    //     vertical speed vz = v·up, and 'along' - the direction of travel in
    //     the target plane at this spot (plane normal × up);
    //  2. done? on the last stage with a safe orbit → engines off, Parked();
    //     a lower stage still burning in orbit → burn it out prograde;
    //     nothing left to burn short of orbit → Failed; high point at the
    //     target with only a short burn left at the top → coast and Insert;
    //  3. heading: the horizontal part of (circular velocity along the plane
    //     − v), the velocity still to gain;
    //  4. pitch γ above the horizon: 90° for the first 250 m, then the
    //     profile 90° − 65°·(h/70 km)^0.55, then (above 35 km) closed loop -
    //     see below;
    //  5. direction = heading·cos γ + up·sin γ, pulled toward the airflow if
    //     it's too far off it in thick air; Point() it, full throttle (the
    //     flight model's own throttle program still applies Max Q limits).
    private void Ascent()
    {
        GetState(out var r, out var v);
        var up = r.Normalized;
        var altitude = r.Length - radius;
        var vz = Double3.Dot(v, up);
        var horizontal = v - up * vz;
        var normal = ToScene(planeNormalI).Normalized;
        var along = Double3.Cross(normal, up).Normalized;
        var target = radius + AscentAltitude;
        var circular = Math.Sqrt(mu / target);

        Elements(r, v, out var pe, out var ap, out _);
        var lastStage = assembly.StageIndex >= assembly.StageCount - 1 && !assembly.StagingInProgress;
        var safeOrbit = pe - radius >= AscentAltitude - 10000 || (pe - radius > 150000 && ap - radius > AscentAltitude * 1.5);
        if (safeOrbit && lastStage)
        {
            SetThrottle(0);
            Parked();
            return;
        }
        // In orbit with a lower stage still burning (a core stage running to
        // its scheduled cutoff): spend it prograde - it's dropped anyway,
        // and every m/s it adds is one the upper stage keeps.
        if (safeOrbit)
        {
            Point(v);
            SetThrottle(1);
            Detail = "In orbit · burning out the " + (assembly.StageName(assembly.StageIndex) ?? "stage") + " prograde · Ap " +
                     TrajectoryDisplay.Km(ap - radius) + " · Pe " + TrajectoryDisplay.Km(pe - radius);
            return;
        }
        if (OutOfPropellant && !Thrusting)
        {
            Fail("out of propellant before orbit (low point " + TrajectoryDisplay.Km(pe - radius) + ").");
            return;
        }
        // High point at the target and only a short burn left at the top (the
        // last stage flying, as after a core stage burnt to depletion): coast
        // up there and finish the orbit with a prograde burn - cheaper than
        // holding altitude against gravity on a weak upper stage.
        Elements(r, v, out _, out _, out var energy);
        if (lastStage && ap - radius >= AscentAltitude - 5000 && altitude > 120000 &&
            Math.Sqrt(Math.Max(0, 2 * (energy + mu / ap))) >= .9 * Math.Sqrt(mu / ap))
        {
            SetThrottle(0);
            planned = burning = false; replanAt = -1;
            step = Step.Insert;
            return;
        }

        // Horizontal steering along the velocity still to gain: the circular
        // orbit's velocity (speed √(μ/r_target) along the plane) minus the
        // velocity it has, with its vertical part removed. Early on that's
        // mostly "go downrange"; as speed builds, any sideways velocity out of
        // the plane shows up in it and gets steered out.
        var toGain = along * circular - v;
        var toGainH = toGain - up * Double3.Dot(toGain, up);
        var heading = toGainH.Length > 1 ? toGainH.Normalized : along;

        // Pitch above the horizon (γ, degrees).
        double gamma;
        var thrustAccel = Thrusting ? flight.Thrust / flight.TotalMass : 0;
        if (altitude < VerticalRise) gamma = 90;
        else if (!closedLoop)
        {
            gamma = 90 - 65 * Math.Pow(Math.Min(1, altitude / ProfileTop), .55);
            if (altitude > ClosedLoopFrom) { closedLoop = true; gammaLatch = Mathf.Max(gammaCommand, 15); }
        }
        else gamma = gammaCommand;
        if (closedLoop)
        {
            var vh = horizontal.Length;
            // Net downward pull: gravity μ/r² less the "centrifugal" share
            // v_h²/r that horizontal speed gives back - zero at orbital speed.
            var gEff = mu / r.SqrLength - vh * vh / r.Length;
            var below = target - r.Length;
            // Vertical speed to have now: whichever is lower of coasting up
            // to the target height, and running down to zero there just as
            // orbital speed is reached. Time to go at the present thrust - but
            // a booster's 3-4 g won't last to orbit: an upper stage finishes
            // the job at nearer 1 g, so the estimate assumes no more than that.
            var timeToGo = thrustAccel > 0 ? Math.Max(30, (circular - vh) / Math.Min(thrustAccel, 12)) : 300;
            var vzWanted = below > 0 ? Math.Min(Math.Sqrt(2 * Math.Max(gEff, .5) * below), 2 * below / timeToGo) : below / 60;
            // Vertical acceleration wanted: enough to cancel the net pull,
            // plus closing the gap to the wanted vertical speed over ~25 s.
            // Thrust supplies a·sin γ of it, so sin γ = azWanted / a below.
            var azWanted = gEff + (vzWanted - vz) / 25;
            // Never steeper than at hand-over - unless it's sinking well short
            // of orbit (a low-thrust upper stage), when it may climb harder.
            var maxGamma = vz < 50 && altitude < .9 * AscentAltitude ? 60 : gammaLatch;
            if (thrustAccel > 0)
            {
                // Not below the horizon while still well short of the target height.
                var minSin = below > 5000 && vz < vzWanted + 100 ? 0 : -.25;
                gamma = Math.Asin(Math.Max(minSin, Math.Min(Math.Sin(maxGamma * Mathf.Deg2Rad), azWanted / thrustAccel))) * Mathf.Rad2Deg;
            }
            // Turn no faster than PitchRate (1.5°/s) toward that pitch, so
            // SAS can follow it smoothly.
            gamma = Mathf.MoveTowards(gammaCommand, (float)gamma, PitchRate * Time.fixedDeltaTime);
        }
        gammaCommand = (float)gamma;
        var g = gamma * Mathf.Deg2Rad;
        var direction = heading * Math.Cos(g) + up * Math.Sin(g);

        // Low in the air: stay within a few degrees of the airflow. How: the
        // airflow comes from the ground-relative velocity; the allowed angle
        // falls from 10° at 1 kPa of dynamic pressure to 2° at 15 kPa; if the
        // wanted direction is further off than that, it's swung back toward
        // the airflow (Slerp) until it sits exactly at the limit.
        var air = new Double3(flight.GroundVelocity);
        var q = flight.DynamicPressure;
        if (altitude >= VerticalRise && air.Length > 30 && q > 500)
        {
            var limit = Mathf.Lerp(10f, 2f, Mathf.InverseLerp(1000f, 15000f, (float)q));
            var flow = air.Normalized.ToVector3();
            var wanted = direction.Normalized.ToVector3();
            var off = Vector3.Angle(flow, wanted);
            if (off > limit) direction = new Double3(Vector3.Slerp(flow, wanted, limit / off));
        }
        Point(direction);
        SetThrottle(1);

        Status = "Ascent to " + TrajectoryDisplay.Km(AscentAltitude) + (Target == Destination.CustomOrbit && AscentAltitude >= CustomAltitude ? " orbit" : " parking orbit");
        Detail = (closedLoop ? "Closed-loop guidance" : altitude < VerticalRise ? "Vertical rise" : "Gravity turn") +
                 " · pitch " + gamma.ToString("F0") + "° · Ap " + TrajectoryDisplay.Km(ap - radius) + " · Pe " + TrajectoryDisplay.Km(Math.Max(0, pe - radius));
    }

    // In the parking orbit: Low orbit ends here; Max orbit goes on to raise
    // it; the Moon goes on to translunar injection. Clearing 'planned' makes
    // the next step plan its burn on the next physics step.
    private void Parked()
    {
        planned = burning = false; replanAt = -1;
        if (Target == Destination.LowOrbit)
        {
            GetState(out var r, out var v);
            Finish("In low Earth orbit: " + OrbitText(r, v) + " - pick a destination to go on");
            return;
        }
        if (Target == Destination.CustomOrbit) { StartCustomOrbit(); return; }
        step = Target == Destination.Moon ? Step.Tli : Step.Raise;
    }

    // Custom orbit, from any Earth orbit: decide whether there's anything to
    // do and which way. How: reads the present orbit's low and high points;
    // if both are already within max(5 km, 1 %) of the typed height the
    // orbit is it - done. Otherwise its mean radius (pe + ap)/2 against the
    // target radius picks Raise (prograde Hohmann up) or Lower (retrograde
    // Hohmann down); the step's planner works out the burns.
    private void StartCustomOrbit()
    {
        planned = burning = false; replanAt = -1;
        GetState(out var r, out var v);
        Elements(r, v, out var pe, out var ap, out _);
        var target = radius + CustomAltitude;
        var tolerance = Math.Max(5000, CustomAltitude * .01);
        if (Math.Abs(pe - target) <= tolerance && Math.Abs(ap - target) <= tolerance)
        {
            Finish("In a " + TrajectoryDisplay.Km(CustomAltitude) + " orbit: " + OrbitText(r, v));
            return;
        }
        step = (pe + ap) / 2 < target ? Step.Raise : Step.Lower;
    }

    // --- Burns -------------------------------------------------------------------

    // Every burn after the ascent, run every physics step. How:
    //  * not planned yet: if a replanning time is set and not reached,
    //    coast pointing prograde; otherwise Plan() the burn for this step;
    //  * planned, before burnStart: coast (prograde, or - within TurnLead
    //    seconds - already pointing along the burn direction so the turn is
    //    done in time); at burnStart, light once the nose is within 6° of the
    //    burn direction (or after 60 s of trying);
    //  * burning: keep pointing along the burn direction (it moves as the
    //    orbit turns), add up the Δv delivered (thrust/mass × step), and
    //    stop when CutoffReached() says the goal is met - then Advance().
    private void Maneuver()
    {
        GetState(out var r, out var v);
        if (!planned)
        {
            if (Now < replanAt)
            {
                Point(v);
                SetThrottle(0);
                Detail = NextBurnText() + " planned in " + TrajectoryDisplay.Clock(replanAt - Now);
                return;
            }
            replanAt = -1;
            if (!Plan()) return;
            planned = true;
        }
        var direction = BurnDirection(r, v);
        if (!burning)
        {
            var wait = burnStart - Now;
            if (wait <= TurnLead) Point(direction);
            else Point(v);
            SetThrottle(0);
            Detail = NextBurnText() + " in " + TrajectoryDisplay.Clock(Math.Max(0, wait));
            if (wait > 0) return;
            // Turned to the burn direction (or given up waiting): light.
            if (Vector3.Angle(transform.up, direction.ToVector3()) > AlignedDegrees && wait > -60) return;
            burning = true;
            deltaVDone = 0;
            if (warpedByAutopilot) { TimeWarp.Request(1); warpedByAutopilot = false; }
        }

        Point(direction);
        if (Thrusting) deltaVDone += flight.Thrust / flight.TotalMass * Time.fixedDeltaTime;
        if (CutoffReached(r, v))
        {
            SetThrottle(0);
            burning = false; planned = false;
            Advance();
            return;
        }
        if (OutOfPropellant && !Thrusting) { OutOfPropellantDuringBurn(r, v); return; }
        // Pointing well off (a staging flip, say): hold the engines until back on line.
        var off = Vector3.Angle(transform.up, direction.ToVector3());
        // Measured burns ease off at the end (as far as the engines throttle),
        // so a few m/s isn't overshot by a whole physics step's worth.
        var throttle = 1f;
        if (cutoff == Cutoff.DeltaV && Thrusting)
        {
            var fullAccel = flight.Thrust / flight.TotalMass / Math.Max(.01, rocket.Throttle);
            throttle = Mathf.Clamp01((float)((cutoffValue - deltaVDone) / (fullAccel * 1.0)));
        }
        SetThrottle(off > 25 ? 0 : Mathf.Max(throttle, .01f));
        Detail = NextBurnText() + " · burning, " + deltaVDone.ToString("N0") + " of ~" + deltaVPlanned.ToString("N0") + " m/s";
    }

    // Which way to push for this burn, worked out afresh each step:
    //  * Prograde: along the inertial velocity (raises the far side);
    //  * MoonRetrograde: against the velocity relative to the Moon (brakes
    //    into lunar orbit) - the Moon's velocity from its ephemeris;
    //  * Fixed: a direction fixed among the stars (course corrections),
    //    stored in the I frame and turned into today's scene axes;
    //  * Tli: the planned split along prograde / out-of-plane (h = r × v) /
    //    radial (up), re-applied to the orbit as it turns during the burn.
    private Double3 BurnDirection(Double3 r, Double3 v)
    {
        switch (steer)
        {
            case Steer.Retrograde:
                return -v;   // against the motion: lowers the far side
            case Steer.MoonRetrograde:
                MoonState(out _, out var vm);
                return -(v - vm);
            case Steer.Fixed:
                return ToScene(fixedDirectionI);
            case Steer.Tli:
                var h = Double3.Cross(r, v).Normalized;
                return v.Normalized * tliPrograde + h * tliNormal + r.Normalized * tliRadial;
            default:
                return v;
        }
    }

    // Whether the burn has done its job, checked every step against the
    // live orbit: high point up to the target (ApoapsisAbove), low point up
    // (PeriapsisAbove - see below), orbital energy up to the planned value
    // (Energy: fixes how far out the transfer goes), the planned Δv delivered
    // (DeltaV: course corrections), or - relative to the Moon - energy down
    // to a circular orbit's at this distance, ε ≤ −μ_Moon/(2r) (MoonCircular).
    private bool CutoffReached(Double3 r, Double3 v)
    {
        Elements(r, v, out var pe, out var ap, out var energy);
        switch (cutoff)
        {
            case Cutoff.ApoapsisAbove: return ap - radius >= cutoffValue;
            case Cutoff.PeriapsisAbove:
            {
                // A prograde burn can lift the low point no higher than where
                // the vehicle is now (past that, this point becomes the low
                // point and the burn only raises the far side): aim for the
                // lower of the target and the present altitude.
                var reachable = Math.Min(cutoffValue, r.Length - radius - 3000);
                if (pe - radius >= reachable - Math.Max(2000, cutoffValue * .01)) return true;
                // Sinking a little through the burn, the low point creeps up
                // on the present altitude without quite reaching it. Stop once
                // the horizontal speed is circular-orbit speed here, √(μ/r):
                // the orbit is then round at the height it's actually at.
                var up = r.Normalized;
                var horizontal = (v - up * Double3.Dot(v, up)).Length;
                return pe - radius > 120000 && horizontal >= Math.Sqrt(mu / r.Length);
            }
            case Cutoff.PeriapsisBelow: return pe - radius <= cutoffValue;
            case Cutoff.ApoapsisBelow:
            {
                // Mirror of PeriapsisAbove: a retrograde burn can bring the
                // high point no lower than where the vehicle is now (past
                // that, this point becomes the high point and the burn only
                // drops the far side), so aim for the higher of the target
                // and the present altitude. As there, stop also once the
                // horizontal speed has fallen to circular speed √(μ/r): the
                // orbit is then round at the height it is actually at.
                var reachable = Math.Max(cutoffValue, r.Length - radius + 3000);
                if (ap - radius <= reachable + Math.Max(2000, cutoffValue * .01)) return true;
                var up = r.Normalized;
                var horizontal = (v - up * Double3.Dot(v, up)).Length;
                return horizontal <= Math.Sqrt(mu / r.Length);
            }
            case Cutoff.Energy: return energy >= cutoffValue;
            case Cutoff.DeltaV: return deltaVDone >= cutoffValue;
            case Cutoff.MoonCircular:
                MoonState(out var rm, out var vm);
                var rel = r - rm; var relV = v - vm;
                return relV.SqrLength / 2 - MoonBody.GravitationalParameter / rel.Length <= -MoonBody.GravitationalParameter / (2 * rel.Length);
            default: return true;
        }
    }

    // The propellant ran out mid-burn: end the mission saying where it left
    // the vehicle. At the Moon, the sign of the energy relative to the Moon
    // (v²/2 − μ_Moon/r) tells captured (negative) from flying past.
    private void OutOfPropellantDuringBurn(Double3 r, Double3 v)
    {
        if (step == Step.Loi)
        {
            MoonState(out var rm, out var vm);
            var rel = r - rm; var relV = v - vm;
            var bound = relV.SqrLength / 2 - MoonBody.GravitationalParameter / rel.Length < 0;
            Finish(bound ? "Captured into an elliptical lunar orbit (out of propellant)" : "Out of propellant - flying past the Moon");
            return;
        }
        Elements(r, v, out var pe, out var ap, out var energy);
        // Specific energy ε ≥ 0 means unbound: the rocket leaves Earth for
        // good, and far away keeps the speed v∞ = √(2ε) (ε = v∞²/2 there).
        if (energy >= 0)
        {
            Finish("All Δv spent · escaping Earth, leaving at " + (Math.Sqrt(2 * energy) / 1000).ToString("F2") + " km/s");
            return;
        }
        Finish("Out of propellant · orbit " + TrajectoryDisplay.Km(pe - radius) + " × " + TrajectoryDisplay.Km(ap - radius));
    }

    // A burn finished: move the state machine on. Leaving 'planned' false
    // (done by the caller) makes Maneuver() plan the next step's burn.
    private void Advance()
    {
        switch (step)
        {
            case Step.Insert: Parked(); break;
            case Step.Raise: step = Step.Circularize; break;
            case Step.Lower: step = Step.LowerCircularize; break;
            case Step.Circularize:
            case Step.LowerCircularize:
            {
                GetState(out var r, out var v);
                Elements(r, v, out var pe, out var ap, out _);
                Finish((Target == Destination.CustomOrbit ? "In the requested " + TrajectoryDisplay.Km(CustomAltitude) + " orbit: " : "In the maximum orbit: ") +
                       TrajectoryDisplay.Km(pe - radius) + " × " + TrajectoryDisplay.Km(ap - radius));
                break;
            }
            case Step.Tli:
                if (kickBurn) { kicksDone++; break; }   // another departure burn to come
                step = Step.Mcc1; correctionFrom = Now; break;
            case Step.Mcc1: step = Step.Mcc2; break;
            case Step.Mcc2: step = Step.Loi; break;
            case Step.Loi:
            {
                GetState(out var r, out var v);
                MoonState(out var rm, out var vm);
                var rel = r - rm;
                Finish("In lunar orbit at ~" + TrajectoryDisplay.Km(rel.Length - MoonBody.RadiusMeters));
                break;
            }
        }
    }

    private string NextBurnText()
    {
        switch (step)
        {
            case Step.Raise: return "Transfer burn to " + TrajectoryDisplay.Km(orbitTarget);
            case Step.Insert: return "Orbit insertion";
            case Step.Circularize: case Step.LowerCircularize: return "Circularisation";
            case Step.Lower: return "Transfer burn down to " + TrajectoryDisplay.Km(orbitTarget);
            case Step.Escape: return "Escape burn (all Δv)";
            case Step.Tli: return "Translunar injection";
            case Step.Mcc1: case Step.Mcc2: return "Course correction";
            case Step.Loi: return "Lunar orbit insertion";
            default: return "Burn";
        }
    }

    // Plans the burn for the current step: reads the installed engines'
    // vacuum thrust and Isp (for burn times), then hands over to the step's
    // planner, which sets burnStart, steer, cutoff and cutoffValue. False =
    // nothing to fly yet (waiting to replan, step skipped, mission ended).
    private bool Plan()
    {
        GetState(out var r, out var v);
        assembly.InstalledEngines(out var thrust, out var isp);
        switch (step)
        {
            case Step.Raise: return PlanRaise(r, v, thrust, isp);
            case Step.Insert:
            {
                // Best orbit: round at the high point. Moon: just a safe low
                // point - injection from an ellipse's low point costs less.
                Elements(r, v, out _, out var ap, out _);
                var high = ap - radius;
                ApoapsisBurn(r, v, thrust, isp, Target == Destination.Moon ? Math.Min(high, ParkingAltitude) : high);
                Status = "Coasting to " + TrajectoryDisplay.Km(high) + " to finish the orbit";
                return true;
            }
            case Step.Circularize:
            {
                // A high transfer takes days to coast up, and the Moon and
                // Sun shift when it gets to the top by minutes to an hour -
                // TimeToApoapsis, worked out from the present Earth-only
                // ellipse, can't know that. So while the top is more than 3 h
                // away, don't commit: coast, and plan again 2 h before the
                // predicted top (Maneuver waits for replanAt, time-warping).
                // By then the ellipse fitted to the motion is the one it will
                // really follow over the top.
                var ta = TimeToApoapsis(r, v);
                if (ta > 3 * 3600)
                {
                    replanAt = Now + ta - 2 * 3600;
                    Status = "Coasting up to " + TrajectoryDisplay.Km(orbitTarget);
                    return false;
                }
                ApoapsisBurn(r, v, thrust, isp, orbitTarget);
                Status = "Raising orbit to " + TrajectoryDisplay.Km(orbitTarget);
                return true;
            }
            case Step.Lower: return PlanLower(r, v, thrust, isp);
            case Step.Escape:
            {
                // Escape: one prograde burn that never cuts off by itself -
                // a DeltaV cutoff of +∞ is never reached (and keeps the
                // throttle at full, as the ease-off divides by what's left).
                // It runs through every stage (auto-staging lights the next)
                // until the last is empty; OutOfPropellantDuringBurn then
                // ends the mission. Burning low and fast, from the parking
                // orbit, gets the most out of it (the Oberth effect: the
                // same Δv adds more energy, v·Δv, where v is greatest).
                deltaVPlanned = RemainingDeltaV();
                burnDuration = BurnTime(deltaVPlanned, thrust, isp);
                burnStart = Now + TurnLead;
                steer = Steer.Prograde; cutoff = Cutoff.DeltaV; cutoffValue = double.PositiveInfinity;
                Status = "Escaping Earth: burning all " + deltaVPlanned.ToString("N0") + " m/s prograde";
                return true;
            }
            case Step.LowerCircularize:
            {
                // Mirror of Circularize: the low point is where the transfer
                // ends, so the burn goes there; from a high orbit it's days
                // away and the Moon shifts its timing, so coast and plan
                // again 2 h before (the same replanAt wait as above).
                var tp = TimeToPeriapsis(r, v);
                if (tp > 3 * 3600)
                {
                    replanAt = Now + tp - 2 * 3600;
                    Status = "Coasting down to " + TrajectoryDisplay.Km(orbitTarget);
                    return false;
                }
                PeriapsisBurn(r, v, thrust, isp, orbitTarget);
                Status = "Lowering orbit to " + TrajectoryDisplay.Km(orbitTarget);
                return true;
            }
            case Step.Tli: return PlanTli(r, v, thrust, isp);
            case Step.Mcc1:
            case Step.Mcc2: return PlanCorrection(r, v, thrust, isp);
            case Step.Loi: return PlanLoi(r, v, thrust, isp);
            default: return false;
        }
    }

    // A prograde burn centred on the high point, until the low point is up
    // at 'lowPoint' (m altitude). How: the speed it has at the high point
    // comes from the energy (vis-viva: v² = 2(ε + μ/r_ap)); the speed it
    // needs there for an orbit from r_ap down to the wanted low point is
    // vis-viva again with a = (r_ap + r_low)/2; the difference is the Δv.
    // The burn is centred on the high point - it starts half its estimated
    // duration before - so its effect is spread evenly either side.
    private void ApoapsisBurn(Double3 r, Double3 v, double thrust, double isp, double lowPoint)
    {
        var ta = TimeToApoapsis(r, v);
        Elements(r, v, out _, out var ap, out var energy);
        var vAp = Math.Sqrt(Math.Max(0, 2 * (energy + mu / ap)));
        var rl = radius + lowPoint;
        // Speed at the high point for an orbit with that low point (vis-viva).
        var wanted = Math.Sqrt(mu * (2 / ap - 2 / (ap + rl)));
        deltaVPlanned = Math.Max(0, wanted - vAp);
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = Now + ta - burnDuration / 2;
        steer = Steer.Prograde; cutoff = Cutoff.PeriapsisAbove; cutoffValue = lowPoint;
    }

    // The mirror of ApoapsisBurn, for coming down: a retrograde burn centred
    // on the low point, until the high point is down at 'highPoint' (m
    // altitude). How: the speed at the low point now comes from the energy
    // (vis-viva v² = 2(ε + μ/r_pe)); the speed needed there for an orbit
    // from r_pe up to the wanted high point is vis-viva with
    // a = (r_pe + r_high)/2; Δv is how much slower that is. The burn starts
    // half its duration before the low point so it's spread evenly about it.
    private void PeriapsisBurn(Double3 r, Double3 v, double thrust, double isp, double highPoint)
    {
        var tp = TimeToPeriapsis(r, v);
        Elements(r, v, out var pe, out _, out var energy);
        var vPe = Math.Sqrt(Math.Max(0, 2 * (energy + mu / pe)));
        var rh = radius + highPoint;
        var wanted = Math.Sqrt(mu * (2 / pe - 2 / (pe + rh)));
        deltaVPlanned = Math.Max(0, vPe - wanted);
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = Now + tp - burnDuration / 2;
        steer = Steer.Retrograde; cutoff = Cutoff.ApoapsisBelow; cutoffValue = highPoint;
    }

    // Burn time (s) for a Δv with the installed engines, from the rocket
    // equation. How: the mass after the burn is m·e^(−Δv/ve) (ve = Isp·g0,
    // the exhaust speed), so the propellant used is m(1 − e^(−Δv/ve)), and
    // at the mass flow F/ve that takes m·ve/F·(1 − e^(−Δv/ve)) seconds.
    private double BurnTime(double deltaV, double thrust, double isp)
    {
        if (thrust <= 0 || isp <= 0) return 0;
        var ve = isp * EnginePerformance.G0;
        return flight.TotalMass * ve / thrust * (1 - Math.Exp(-Math.Abs(deltaV) / ve));
    }

    /// <summary>
    /// Vacuum Δv left (m/s): this stage and every one still to come. Only
    /// propellant the engines can actually burn counts (Usable). How: this
    /// stage by the rocket equation, Δv = Isp·g0·ln(m0/m1), with m0 the whole
    /// vehicle now and m1 that minus its usable propellant (solid boosters
    /// mixed in by mass-weighted Isp); then, for a preset, each later stage
    /// from its data the same way - its m0 being everything from it up (the
    /// stages above, fully loaded, plus the payload), its m1 that minus its
    /// own usable propellant, its Isp the thrust-weighted vacuum Isp of its
    /// engines - and the stages' Δv added up.
    /// </summary>
    public double RemainingDeltaV()
    {
        assembly.InstalledEngines(out _, out var isp);
        var liquid = Usable(flight.FuelRemaining, flight.OxidizerRemaining, Engine(i => assembly.GetParameters(i), assembly.SocketCount));
        double solid = flight.SolidPropellant;
        var solidMotor = flight.SolidBoosterCount > 0 && assembly.IsPreset ? EnginePerformance.Solid(RocketPresets.All[assembly.PresetIndex].solidBoosterId) : null;
        var stageIsp = solid > 0 && solidMotor != null ? (liquid * isp + solid * solidMotor.vacuumIsp) / Math.Max(1, liquid + solid) : isp;
        var m0 = flight.TotalMass;
        var m1 = m0 - liquid - solid;
        var total = m1 > 0 && m0 > m1 ? stageIsp * EnginePerformance.G0 * Math.Log(m0 / m1) : 0;
        if (!assembly.IsPreset || assembly.StagingInProgress) return total;
        return total + LaterStagesDeltaV(RocketPresets.All[assembly.PresetIndex], assembly.StageIndex + 1);
    }

    // Vacuum Δv (m/s) of a preset's stages from 'first' (1 = the first upper
    // stage) to the top, as they'll be when each lights: each lifts
    // everything above it. How, per stage k: m0 = StackMass(k) (the stage
    // and all above it, fully loaded, plus the payload), m1 = m0 minus its
    // usable propellant, Isp = its engines' combined vacuum Isp (StageIsp);
    // Δv = Isp·g0·ln(m0/m1), summed.
    private static double LaterStagesDeltaV(RocketPresets.Preset p, int first)
    {
        double total = 0;
        if (p.upperStages == null) return 0;
        for (var k = Math.Max(1, first); k <= p.upperStages.Length; k++)
        {
            var spec = p.upperStages[k - 1];
            var engines = Array.ConvertAll(spec.engines, EnginePerformance.Reference);
            var isp = StageIsp(engines);
            var above = StackMass(p, k);
            var end = above - Usable(spec.fuelMass, spec.oxidizerMass, Engine(i => engines[i], engines.Length));
            if (isp > 0 && end > 0) total += isp * EnginePerformance.G0 * Math.Log(above / end);
        }
        return total;
    }

    // Mass (kg) of a preset's upper stage k fully loaded with everything it
    // carries: the payload, plus each stage from k up (dry + propellant).
    private static double StackMass(RocketPresets.Preset p, int k)
    {
        double above = p.payloadMass;
        for (var j = k - 1; j < p.upperStages.Length; j++) above += p.StageDry(j + 1) + p.upperStages[j].fuelMass + p.upperStages[j].oxidizerMass;
        return above;
    }

    // Combined vacuum Isp (s) of a set of engines: total thrust / total mass
    // flow, with each engine's flow = thrust / Isp (so g0 cancels).
    private static double StageIsp(EngineParameters[] engines)
    {
        double thrust = 0, flow = 0;
        foreach (var e in engines) if (e != null && e.vacuumIsp > 0) { thrust += e.vacuumThrust; flow += e.vacuumThrust / e.vacuumIsp; }
        return flow > 0 ? thrust / flow : 0;
    }

    // The first engine with a mixture ratio (a stage's engines share one).
    private static EngineParameters Engine(Func<int, EngineParameters> engine, int count)
    {
        for (var i = 0; i < count; i++) { var e = engine(i); if (e != null && e.mixtureRatio > 0) return e; }
        return null;
    }

    // Propellant (kg) the engine can burn from these tanks. At a fixed
    // mixture ratio, whichever tank runs out first stops it; an engine with
    // propellant utilization (the J-2) burns at the tanks' own ratio as long
    // as that lies within its range, so only what's outside the range is left.
    private static double Usable(double fuel, double oxidizer, EngineParameters engine)
    {
        if (engine == null || engine.mixtureRatio <= 0) return fuel + oxidizer;
        double high = engine.mixtureRatio, low = engine.puMinimumMixture > 0 ? engine.puMinimumMixture : high;
        if (fuel <= 0 || oxidizer <= 0) return 0;
        var ratio = oxidizer / fuel;
        if (ratio > high) return fuel * (1 + high);         // fuel runs out first
        if (ratio < low) return oxidizer * (1 + low) / low;  // oxidizer runs out first
        return fuel + oxidizer;                              // both together
    }

    // Radius of the highest circular orbit a Hohmann transfer from circular
    // radius r1 reaches with 'budget' m/s, no higher than StableOrbitRadius.
    // How: a Hohmann transfer's total Δv is NOT steadily rising with the
    // target radius r2. It rises to a peak at r2 ≈ 15.58·r1 (~102,000 km from
    // a 200 km orbit, costing 0.536·v1 ≈ 4.2 km/s) and then FALLS again
    // toward (√2 − 1)·v1 for a very distant target - far out, the second
    // burn needed to stay there gets tiny. So:
    //  1. if the ceiling itself is affordable, that's the answer - even when
    //     some orbit in between would cost more;
    //  2. otherwise every radius from the peak up to the ceiling costs at
    //     least what the ceiling does (the falling side), i.e. too much, so
    //     the answer lies on the rising side, between r1 and the peak (or the
    //     ceiling, if that comes first) - where Δv does rise steadily, so a
    //     bisection works: try halfway between the highest known-affordable
    //     and lowest known-too-dear radius, keep the half that brackets the
    //     budget, 60 times (far below a metre).
    private double HighestCircular(double r1, double budget)
    {
        var ceiling = StableOrbitRadius;
        if (budget <= 0 || r1 >= ceiling) return r1;
        if (Hohmann(r1, ceiling) <= budget) return ceiling;
        double lo = r1, hi = Math.Min(ceiling, HohmannPeakRatio * r1);
        for (var i = 0; i < 60; i++)
        {
            var mid = (lo + hi) / 2;
            if (Hohmann(r1, mid) <= budget) lo = mid; else hi = mid;
        }
        return lo;
    }

    // r2/r1 at which a Hohmann transfer's total Δv peaks: the real root of
    // x³ − 15x² − 9x − 1 = 0 (from setting d(Δv)/d(r2) = 0).
    private const double HohmannPeakRatio = 15.58172;

    private double predictedMax = -1;
    /// <summary>Δv (m/s) the last pad prediction expects the ascent to use -
    /// from SimulateAscent, so it depends on this rocket's weight. −1 = none.</summary>
    public double PredictedAscentDeltaV { get; private set; } = -1;
    /// <summary>Why the last ascent simulation didn't make orbit ("" = it did).</summary>
    public string AscentSimNote { get; private set; } = "";

    /// <summary>
    /// The highest circular orbit (m altitude) this rocket should reach, from
    /// its Δv now. How: on the pad, SimulateAscent flies this rocket's ascent
    /// to the 200 km parking orbit in a quick simulation and returns the Δv
    /// it will have left there and the orbit it ends in - so a heavier
    /// payload (slower climb, more lost to gravity) shows up directly; in
    /// orbit it's simply the Δv measured now, from the present orbit's mean
    /// radius. After a 5% reserve and 30 m/s for steering, HighestCircular
    /// finds where that budget gets to. -1 if it can't reach orbit at all.
    /// </summary>
    public double PredictMaxOrbit(out double deltaV)
    {
        deltaV = 0;
        if (planet == null || flight == null) return -1;
        mu = PlanetBody.UniversalGravitationalConstant * planet.Mass;
        radius = planet.Radius;
        deltaV = RemainingDeltaV();
        GetState(out var r, out var v);
        double r1, budget;
        if (!rocket.Launched)
        {
            budget = SimulateAscentBest(out r1);
            PredictedAscentDeltaV = budget >= 0 ? simSpent : -1;
        }
        else
        {
            Elements(r, v, out var pe, out var ap, out _);
            if (pe - radius < 100000) return -1;   // not in orbit yet
            r1 = (pe + ap) / 2;
            budget = deltaV;
        }
        if (budget < 0) return -1;
        return HighestCircular(r1, budget * (1 - DeltaVReserve) - 30) - radius;
    }

    // --- Ascent simulation (the pad prediction) ------------------------------------

    private const double SimStep = .5;          // s per integration step
    private const double SimMaxSeconds = 4000;  // give up: no orbit by then

    // How much steeper than the autopilot's own pitch program each retry
    // flies: the factor scales the 65° the open-loop profile pitches over by
    // 70 km (1 = exactly Ascent()'s program).
    private static readonly double[] SimSteepness = { 1, .85, .7, .55, .4 };

    // The real flights spend a little more than the ideal simulation - the
    // vehicle wobbling round the commanded attitude, the engines swivelled
    // off-axis to steer, staging transients. Measured (ascent Δv flown vs
    // simulated): Falcon 9 15 t +2.2 %, Saturn V empty +0.6 %, 45.7 t
    // +3.1 %, 70 t +3.2 % - so the simulated ascent is charged 2.5 % more.
    private const double SimSteeringLoss = .025;
    private double simSpent;   // Δv (m/s) the last successful simulated ascent used, with that allowance

    /// <summary>
    /// The ascent simulation, made robust for heavy rockets. How: flies it
    /// first with the autopilot's exact pitch program; if that doesn't make
    /// orbit, again with the pitch-over cut to 85 %, 70 %, 55 % and 40 %
    /// (steeper climbs), and takes the first that does. Why: a heavy vehicle
    /// (Saturn V with the Apollo payload) doesn't follow the steering as
    /// crisply as the ideal point-mass here - in the real flight its path
    /// ends up steeper (71 km at first-stage cutoff against the ideal
    /// program's 42 km) - and its weak upper stage needs that height: flown
    /// exactly as commanded it sinks back. Compared with real autopilot
    /// flights the first try is within 1-2 % (Falcon 9 with 15 t: 8,900 vs
    /// 9,098 m/s used; empty Saturn V: 9,420 vs 9,479).
    /// </summary>
    private double SimulateAscentBest(out double orbitRadius)
    {
        orbitRadius = 0;
        var firstNote = "";
        foreach (var steepness in SimSteepness)
        {
            var left = SimulateAscent(out orbitRadius, steepness, out var spent);
            // Made orbit: charge the steering allowance against what's left -
            // a rocket that only just makes it (the Shuttle) counts as in
            // orbit with nothing to spare, not as failing.
            if (left >= 0) { simSpent = spent * (1 + SimSteeringLoss); return Math.Max(0, left - SimSteeringLoss * spent); }
            if (firstNote.Length == 0) firstNote = AscentSimNote;
        }
        AscentSimNote = firstNote;   // report why the autopilot's own program fails
        return -1;
    }

    /// <summary>
    /// Flies this rocket's ascent to the parking orbit in a fast simulation
    /// and returns the vacuum Δv (m/s) it will have left in orbit (−1 = it
    /// doesn't make orbit); 'orbitRadius' = the mean radius of the orbit it
    /// ends in (m). Weight enters everywhere: the acceleration is thrust ÷
    /// the real, falling mass, so a heavy rocket climbs slowly and spends
    /// longer holding itself up against gravity, and drag (a force) slows a
    /// light one more. How:
    ///  * the vehicle, as now on the pad: its mass (payload, all stages),
    ///    the first stage's installed engines (thrust from the engine model
    ///    at the outside pressure, so it grows as the air thins), its flight
    ///    program (throttle schedule, single-engine cutoffs, cutoff time),
    ///    solid boosters (their thrust curve) and timed drops; then each
    ///    upper stage from the preset (its engines, usable propellant,
    ///    separation and ignition delays, the stack mass it lifts);
    ///  * the flight, in the plane of motion (2-D, Earth-centred, not
    ///    turning): every SimStep seconds, gravity μ/r² toward the centre,
    ///    thrust along the pitch the autopilot would command (SimPitch - the
    ///    same vertical rise, gravity-turn profile, closed-loop law and
    ///    airflow limit as Ascent()), drag against the velocity relative to
    ///    the air (which turns with Earth: eastward at ω·r·cos latitude);
    ///  * the same end conditions as Ascent(): a safe orbit on the last
    ///    stage; a lower stage still burning in orbit spends itself prograde;
    ///    or high point at the target with a short burn left - finished by a
    ///    circularising burn there (its Δv from vis-viva, subtracted);
    ///  * what's left: the last burning stage's propellant by the rocket
    ///    equation at its present mass, plus the stages not yet lit;
    ///    'spent' = the Δv the engines delivered on the way (Σ thrust/mass ×
    ///    step, boosters included, plus the final burn).
    /// </summary>
    private double SimulateAscent(out double orbitRadius, double steepness, out double spent)
    {
        orbitRadius = 0;
        spent = 0;   // Δv delivered so far: thrust/mass summed over the steps (+ the final burn)
        AscentSimNote = "";
        var isPreset = assembly.IsPreset;
        var preset = isPreset ? RocketPresets.All[assembly.PresetIndex] : default;   // a struct: 'default' = none
        var stageCount = isPreset && preset.Staged ? preset.upperStages.Length + 1 : 1;
        var target = radius + ParkingAltitude;
        var padMass = flight.TotalMass;

        // Stage being flown and its propulsion.
        var k = 0;
        var mass = padMass;
        EngineParameters[] engines; int[] sockets;
        FirstStageEngines(out engines, out sockets);
        var propellant = (double)Usable(flight.FuelRemaining, flight.OxidizerRemaining, Engine(i => engines[i], engines.Length));
        var stageClock = 0.0;                  // s since this stage lit
        var cutoffAt = isPreset ? (double)preset.mecoSeconds : 0;
        var throttleProgram = isPreset ? preset.throttleProgram : null;
        var engineCutoffs = isPreset ? preset.engineCutoffs : null;
        var drops = isPreset ? preset.firstStageDrops : null;
        var dropsDone = 0;
        var solid = flight.SolidBoosterCount > 0 && isPreset ? EnginePerformance.Solid(preset.solidBoosterId) : null;
        var solidCount = solid != null ? flight.SolidBoosterCount : 0;
        double solidLeft = flight.SolidPropellant;
        var coastUntil = -1.0;                 // staging: unpowered until this time

        // 2-D state: Earth's centre at the origin, the pad at (0, R), east = +x.
        // Inertial velocity starts as the pad's eastward motion with Earth.
        var lat = rocket.LaunchLatitudeDegrees * Mathf.Deg2Rad;
        var airSpin = planet.RotationRate * Math.Cos(lat);   // rad/s: the air's eastward turn rate here
        double x = 0, y = radius, vx = airSpin * radius, vy = 0;
        var pitch = new SimPitch(steepness);

        for (var t = 0.0; t < SimMaxSeconds; t += SimStep)
        {
            var rl = Math.Sqrt(x * x + y * y);
            var altitude = rl - radius;
            if (t > 10 && altitude < 0) { AscentSimNote = "falls back to the ground"; return -1; }
            double ux = x / rl, uy = y / rl;          // up
            double ax = uy, ay = -ux;                 // along: eastward, square to up
            // The present orbit (vacuum ellipse): energy ε = v²/2 − μ/r,
            // angular momentum h = x·vy − y·vx, eccentricity from them,
            // e = √(1 + 2εh²/μ²), semi-latus rectum p = h²/μ, low and high
            // points p/(1 ± e).
            var speed2 = vx * vx + vy * vy;
            var energy = speed2 / 2 - mu / rl;
            var h = x * vy - y * vx;
            var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (mu * mu)));
            var pSemi = h * h / mu;
            var pe = pSemi / (1 + e);
            var ap = e < 1 ? pSemi / (1 - e) : double.PositiveInfinity;
            var lastStage = k == stageCount - 1;
            var safeOrbit = pe - radius >= ParkingAltitude - 10000 || (pe - radius > 150000 && ap - radius > ParkingAltitude * 1.5);
            if (safeOrbit && lastStage)
            {
                orbitRadius = (pe + ap) / 2;
                return StageDeltaV(engines, mass, propellant) + (isPreset ? LaterStagesDeltaV(preset, k + 1) : 0);
            }
            // High point at the target and a short burn left at the top:
            // coast up and round the orbit off there, Δv = circular speed
            // √(μ/r_ap) minus the speed it will have there, √(2(ε + μ/r_ap)).
            if (lastStage && ap - radius >= ParkingAltitude - 5000 && altitude > 120000 && !double.IsInfinity(ap))
            {
                var vAp = Math.Sqrt(Math.Max(0, 2 * (energy + mu / ap)));
                if (vAp >= .9 * Math.Sqrt(mu / ap))
                {
                    orbitRadius = ap;
                    spent += Math.Sqrt(mu / ap) - vAp;
                    var left = StageDeltaV(engines, mass, propellant) - (Math.Sqrt(mu / ap) - vAp);
                    if (left < 0) AscentSimNote = "short of the burn to round the orbit off by " + (-left).ToString("N0") + " m/s";
                    return left >= 0 ? left : -1;
                }
            }

            // Thrust now (N) and the propellant flows it burns (kg/s).
            var pressure = StandardAtmosphere.Pressure(Math.Max(0, altitude));
            double thrust = 0, liquidFlow = 0, solidFlow = 0;
            var powered = t >= coastUntil && (cutoffAt <= 0 || stageClock < cutoffAt || (lastStage && k > 0));
            if (powered && propellant > 0)
            {
                var throttle = Math.Min(1, ProgramThrottleAt(throttleProgram, stageClock));
                for (var i = 0; i < engines.Length; i++)
                {
                    if (engines[i] == null || EngineCutAt(engineCutoffs, sockets[i], stageClock)) continue;
                    var r = EnginePerformance.Evaluate(engines[i], throttle, pressure);
                    if (!r.valid) continue;
                    thrust += r.thrust; liquidFlow += r.massFlow;
                }
            }
            if (k == 0 && solidCount > 0 && solidLeft > 0)
            {
                thrust += solidCount * solid.Thrust(t, pressure);
                solidFlow = solidCount * solid.MassFlow(t);
            }
            if (t == 0 && thrust < padMass * mu / (radius * radius)) { AscentSimNote = "thrust below its weight"; return -1; }

            // This stage done (tanks dry, or its programmed cutoff passed on a
            // lower stage): the next separates and lights after its delays.
            // (On the first stage, strap-on solids still burning keep it going.)
            var liquidDone = propellant <= 0 || (cutoffAt > 0 && stageClock >= cutoffAt && !(lastStage && k > 0));
            var solidsDone = k > 0 || solidCount == 0 || solidLeft <= 0;
            var stageDone = t >= coastUntil && liquidDone && solidsDone;
            if (stageDone)
            {
                if (lastStage)   // nothing left, short of orbit
                {
                    AscentSimNote = "propellant runs out at " + TrajectoryDisplay.Km(altitude) + ", " +
                        (Math.Sqrt(mu / rl) - Math.Abs(vx * ax + vy * ay)).ToString("N0") + " m/s short of orbital speed";
                    return -1;
                }
                k++;
                var spec = preset.upperStages[k - 1];
                engines = Array.ConvertAll(spec.engines, EnginePerformance.Reference);
                sockets = new int[engines.Length];
                for (var i = 0; i < sockets.Length; i++) sockets[i] = i;
                mass = StackMass(preset, k);
                propellant = Usable(spec.fuelMass, spec.oxidizerMass, Engine(i => engines[i], engines.Length));
                cutoffAt = spec.burnSeconds; throttleProgram = null; engineCutoffs = spec.engineCutoffs;
                drops = spec.drops; dropsDone = 0; solidCount = 0;
                // Unpowered through separation and ignition; the new stage's
                // clock (for its cutoff, engine cutoffs and drops) starts at
                // its ignition - it only runs once coastUntil has passed.
                coastUntil = t + spec.separationDelay + spec.ignitionDelay;
                stageClock = 0;
                thrust = liquidFlow = solidFlow = 0;
            }

            // Where to push: the autopilot's pitch, or prograde once in a safe
            // orbit with a lower stage still burning.
            var airX = vx - ax * airSpin * rl; var airY = vy - ay * airSpin * rl;   // velocity through the air
            var airSpeed = Math.Sqrt(airX * airX + airY * airY);
            var density = StandardAtmosphere.Density(pressure, StandardAtmosphere.TemperatureKelvin(Math.Max(0, altitude)));
            double dirX, dirY;
            if (safeOrbit) { var vs = Math.Sqrt(speed2); dirX = vx / vs; dirY = vy / vs; }
            else pitch.Direction(this, ux, uy, ax, ay, vx, vy, rl, altitude, target, mass > 0 ? thrust / mass : 0,
                                 airX, airY, airSpeed, .5 * density * airSpeed * airSpeed, out dirX, out dirY);

            // Accelerations: gravity, thrust/mass, drag/mass (the vehicle's own
            // drag model; its force = deceleration × the pad mass it was
            // computed with), then one step (velocity first, then position).
            var g = mu / (rl * rl);
            var accX = -ux * g + dirX * thrust / mass;
            var accY = -uy * g + dirY * thrust / mass;
            if (airSpeed > .1)
            {
                var drag = flight.DragDecelerationAt(altitude, airSpeed) * padMass / mass;
                accX -= airX / airSpeed * drag; accY -= airY / airSpeed * drag;
            }
            spent += thrust / mass * SimStep;
            vx += accX * SimStep; vy += accY * SimStep;
            x += vx * SimStep; y += vy * SimStep;

            // Propellant and mass, the clocks, and anything dropped on schedule.
            var burnt = Math.Min(propellant, liquidFlow * SimStep);
            propellant -= burnt; mass -= burnt;
            if (solidCount > 0)
            {
                var solidBurnt = Math.Min(solidLeft, solidFlow * SimStep);
                solidLeft -= solidBurnt; mass -= solidBurnt;
                if (solidLeft <= 0 || t >= solid.BurnTime) { solidLeft = 0; }
            }
            if (t >= coastUntil) stageClock += SimStep;
            while (drops != null && dropsDone < drops.Length && stageClock >= drops[dropsDone].time)
            {
                var d = drops[dropsDone++];
                if (!(preset.fairingOff && RocketPresets.Preset.IsFairingDrop(d))) mass -= d.mass;
                if (d.solids && solidCount > 0) { mass -= solidCount * (solid.inertMass + solidLeft / solidCount); solidCount = 0; solidLeft = 0; }
            }
        }
        AscentSimNote = "no orbit within " + SimMaxSeconds.ToString("N0") + " s";
        return -1;
    }

    // The first stage's engines as installed (one per socket, null = empty)
    // and their socket numbers (for the flight program's engine cutoffs).
    private void FirstStageEngines(out EngineParameters[] engines, out int[] sockets)
    {
        engines = new EngineParameters[assembly.SocketCount];
        sockets = new int[assembly.SocketCount];
        for (var i = 0; i < engines.Length; i++) { engines[i] = assembly.GetParameters(i); sockets[i] = i; }
    }

    // Vacuum Δv (m/s) of 'propellant' kg burnt by these engines from 'mass'
    // kg: Isp·g0·ln(m / (m − propellant)).
    private static double StageDeltaV(EngineParameters[] engines, double mass, double propellant)
    {
        var isp = StageIsp(engines);
        var end = mass - propellant;
        return isp > 0 && end > 0 && propellant > 0 ? isp * EnginePerformance.G0 * Math.Log(mass / end) : 0;
    }

    // A flight program's throttle limit 'seconds' after the stage lit - the
    // same (time, limit) pair table, interpolated, as RocketFlightModel's
    // ProgramThrottle. 1 = no program.
    private static double ProgramThrottleAt(float[] program, double seconds)
    {
        if (program == null || program.Length < 2) return 1;
        if (seconds <= program[0]) return program[1];
        for (var i = 2; i + 1 < program.Length; i += 2)
            if (seconds <= program[i]) return Mathf.Lerp(program[i - 1], program[i + 1], (float)((seconds - program[i - 2]) / Math.Max(1e-6, program[i] - program[i - 2])));
        return program[^1];
    }

    // Whether the program has shut engine 'socket' down by 'seconds' after
    // the stage lit: (time, socket) pairs, as RocketFlightModel.EngineCutOff.
    private static bool EngineCutAt(float[] cutoffs, int socket, double seconds)
    {
        if (cutoffs == null) return false;
        for (var i = 0; i + 1 < cutoffs.Length; i += 2)
            if (Mathf.RoundToInt(cutoffs[i + 1]) == socket && seconds >= cutoffs[i]) return true;
        return false;
    }

    /// <summary>
    /// The ascent's pitch program for the simulation, in 2-D - the same laws
    /// as Ascent() (see there for the reasoning): straight up to VerticalRise;
    /// then γ = 90° − 65°·s·(h / 70 km)^0.55 (s = steepness, 1 = Ascent()'s
    /// own program); from ClosedLoopFrom the closed
    /// loop - the vertical acceleration wanted, a_z = g_eff + (v_z,wanted −
    /// v_z)/25 s, gives sin γ = a_z / (thrust/mass), within the same limits,
    /// turning at most PitchRate °/s. The push is then held within 2-10° of
    /// the airflow while the dynamic pressure is over 500 Pa. Keeps its own
    /// commanded pitch between steps, like Ascent()'s gammaCommand.
    /// </summary>
    private sealed class SimPitch
    {
        private bool closedLoop;
        private double command = 90, latch;
        private readonly double steepness;   // scales the open-loop pitch-over (1 = Ascent()'s)
        public SimPitch(double steepness) => this.steepness = steepness;

        public void Direction(Autopilot a, double ux, double uy, double ax, double ay, double vx, double vy, double rl,
                              double altitude, double target, double thrustAccel, double airX, double airY, double airSpeed,
                              double q, out double dirX, out double dirY)
        {
            var vz = vx * ux + vy * uy;
            var vhSigned = vx * ax + vy * ay;
            var vh = Math.Abs(vhSigned);
            var circular = Math.Sqrt(a.mu / target);
            // Heading: along the direction of travel while short of circular
            // speed, back against it if past it (as toGain does in Ascent).
            var heading = circular - vhSigned > 1 ? 1.0 : -1.0;
            double gamma;
            if (altitude < VerticalRise) gamma = 90;
            else if (!closedLoop)
            {
                gamma = 90 - 65 * steepness * Math.Pow(Math.Min(1, altitude / ProfileTop), .55);
                if (altitude > ClosedLoopFrom) { closedLoop = true; latch = Math.Max(command, 15); }
            }
            else gamma = command;
            if (closedLoop)
            {
                var gEff = a.mu / (rl * rl) - vh * vh / rl;
                var below = target - rl;
                var timeToGo = thrustAccel > 0 ? Math.Max(30, (circular - vh) / Math.Min(thrustAccel, 12)) : 300;
                var vzWanted = below > 0 ? Math.Min(Math.Sqrt(2 * Math.Max(gEff, .5) * below), 2 * below / timeToGo) : below / 60;
                var azWanted = gEff + (vzWanted - vz) / 25;
                var maxGamma = vz < 50 && altitude < .9 * ParkingAltitude ? 60 : latch;
                if (thrustAccel > 0)
                {
                    var minSin = below > 5000 && vz < vzWanted + 100 ? 0 : -.25;
                    gamma = Math.Asin(Math.Max(minSin, Math.Min(Math.Sin(maxGamma * Mathf.Deg2Rad), azWanted / thrustAccel))) * Mathf.Rad2Deg;
                }
                gamma = Mathf.MoveTowards((float)command, (float)gamma, (float)(PitchRate * SimStep));
            }
            command = gamma;
            var g = gamma * Mathf.Deg2Rad;
            dirX = ax * heading * Math.Cos(g) + ux * Math.Sin(g);
            dirY = ay * heading * Math.Cos(g) + uy * Math.Sin(g);
            // Airflow limit: if the push is more than 'limit' off the airflow,
            // swing it back to exactly 'limit' (the angle between two 2-D
            // directions from atan2; the result rotated that far from the flow).
            if (altitude >= VerticalRise && airSpeed > 30 && q > 500)
            {
                var limit = Mathf.Lerp(10f, 2f, Mathf.InverseLerp(1000f, 15000f, (float)q)) * Mathf.Deg2Rad;
                var flow = Math.Atan2(airY, airX);
                var off = Math.Atan2(dirY, dirX) - flow;
                while (off > Math.PI) off -= 2 * Math.PI;
                while (off < -Math.PI) off += 2 * Math.PI;
                if (Math.Abs(off) > limit)
                {
                    var angle = flow + Math.Sign(off) * limit;
                    dirX = Math.Cos(angle); dirY = Math.Sin(angle);
                }
            }
        }
    }

    // "low × high" altitudes of the present orbit, for the panel.
    private string OrbitText(Double3 r, Double3 v)
    {
        Elements(r, v, out var pe, out var ap, out _);
        return TrajectoryDisplay.Km(pe - radius) + " × " + TrajectoryDisplay.Km(ap - radius);
    }

    // Hohmann transfer Δv (both burns) between circular orbits r1 -> r2.
    // How: the transfer ellipse touches both (a = (r1 + r2)/2); at r1 the
    // speed must go from circular √(μ/r1) to the ellipse's √(μ/r1)·√(r2/a),
    // and at r2 from the ellipse's √(μ/r2)·√(r1/a) up to circular √(μ/r2).
    private double Hohmann(double r1, double r2)
    {
        var v1 = Math.Sqrt(mu / r1); var v2 = Math.Sqrt(mu / r2);
        var a = (r1 + r2) / 2;
        return v1 * (Math.Sqrt(r2 / a) - 1) + v2 * (1 - Math.Sqrt(r1 / a));
    }

    // Max orbit, first burn. How: measures the Δv left now (RemainingDeltaV),
    // keeps the reserve, finds the highest circular orbit it affords from the
    // present orbit's mean radius, and - if that's worth going to (20+ km
    // higher) - plans a prograde burn starting after the turn, cut off when
    // the high point reaches that height. Its Δv for the display is the
    // first Hohmann burn's, √(μ/r1)·(√(r2/a) − 1).
    private bool PlanRaise(Double3 r, Double3 v, double thrust, double isp)
    {
        Elements(r, v, out var pe, out var ap, out _);
        var r1 = (pe + ap) / 2;
        var budget = RemainingDeltaV() * (1 - DeltaVReserve) - 30;
        if (Target == Destination.CustomOrbit)
        {
            // Custom orbit: the target is the typed height, not the highest
            // affordable. Check the whole Hohmann transfer (both burns) fits
            // the Δv budget first, so it never strands half-way up.
            var r2 = radius + CustomAltitude;
            var need = Hohmann(r1, r2);
            if (need > budget)
            {
                Fail("not enough Δv for " + TrajectoryDisplay.Km(CustomAltitude) + ": needs " + need.ToString("N0") +
                     " m/s, has " + Math.Max(0, budget).ToString("N0") + " (after reserve). Max reachable ≈ " +
                     TrajectoryDisplay.Km(HighestCircular(r1, budget) - radius) + ".");
                return false;
            }
            orbitTarget = CustomAltitude;
            var at = (r1 + r2) / 2;
            // First Hohmann burn: circular √(μ/r1) up to the ellipse's √(μ/r1)·√(r2/a).
            deltaVPlanned = Math.Sqrt(mu / r1) * (Math.Sqrt(r2 / at) - 1);
            burnDuration = BurnTime(deltaVPlanned, thrust, isp);
            burnStart = Now + TurnLead;
            steer = Steer.Prograde; cutoff = Cutoff.ApoapsisAbove; cutoffValue = orbitTarget;
            Status = "Raising orbit to " + TrajectoryDisplay.Km(orbitTarget) + " · transfer " + need.ToString("N0") +
                     " m/s of " + RemainingDeltaV().ToString("N0") + " m/s left";
            return true;
        }
        var lo = HighestCircular(r1, budget);
        orbitTarget = lo - radius;
        // Capped by the Moon, not by Δv, with a useful amount (100+ m/s) to
        // spare: ask the pilot before committing. How: the spare is the
        // budget less the whole transfer's cost. The escape speed shown is
        // what all of the Δv left would give if spent here in one prograde
        // burn - from circular speed √(μ/r1) up to v = √(μ/r1) + Δv; energy
        // is conserved on the way out, so far from Earth the speed left
        // is v∞ = √(v² − 2μ/r1) (an instantaneous-burn estimate; a long
        // real burn gets a little less). Pausing: time warp is ended first
        // (rails warp moves the rocket outside physics), then Time.timeScale
        // = 0 stops FixedUpdate, so nothing flies until a button is pressed.
        // Returning false leaves the burn unplanned; once answered, the
        // next physics step plans again with the choice made.
        var spare = budget - Hohmann(r1, lo);
        if (lo >= StableOrbitRadius - 1 && spare > 100 && escapeChoice == EscapeChoice.Ask)
        {
            if (!askingEscape)
            {
                spareDeltaV = spare;
                var vOut = Math.Sqrt(mu / r1) + RemainingDeltaV();
                escapeSpeed = Math.Sqrt(Math.Max(0, vOut * vOut - 2 * mu / r1));
                stableTarget = orbitTarget;
                if (TimeWarp.Rate > 1) TimeWarp.Request(1);
                warpedByAutopilot = false;
                SetThrottle(0);
                askingEscape = true;
                Time.timeScale = 0;
                Status = "Waiting for your answer: escape Earth or stay in the highest stable orbit?";
            }
            return false;
        }
        if (escapeChoice == EscapeChoice.Escape) { step = Step.Escape; return Plan(); }
        if (orbitTarget - (r1 - radius) < 20000)
        {
            Finish("In orbit: " + TrajectoryDisplay.Km(pe - radius) + " × " + TrajectoryDisplay.Km(ap - radius) +
                   (budget < 100 ? " (no Δv left to go higher)" : " (maximum achievable)"));
            return false;
        }
        var a = (r1 + lo) / 2;
        deltaVPlanned = Math.Sqrt(mu / r1) * (Math.Sqrt(lo / a) - 1);
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = Now + TurnLead;
        steer = Steer.Prograde; cutoff = Cutoff.ApoapsisAbove; cutoffValue = orbitTarget;
        // At the ceiling the Δv isn't the limit: say how much is spare (the
        // budget less the whole transfer's cost, both burns).
        var limitedByMoon = lo >= StableOrbitRadius - 1;
        Status = "Raising orbit to " + TrajectoryDisplay.Km(orbitTarget) + (limitedByMoon
                     ? " - the highest stable Earth orbit (the Moon breaks up any higher); " + (budget - Hohmann(r1, lo)).ToString("N0") + " m/s to spare"
                     : " - the maximum with the " + RemainingDeltaV().ToString("N0") + " m/s measured in orbit") +
                 (predictedMax > 0 ? " (predicted before launch: " + TrajectoryDisplay.Km(predictedMax) + ")" : "");
        return true;
    }

    // Custom orbit lower than the present one, first burn. How: the mirror
    // of the custom branch of PlanRaise - from the present orbit's mean
    // radius r1 down to r2 = radius + the typed height, a Hohmann transfer
    // whose total Δv is the same as going up between the two (Hohmann(r2,
    // r1)) and is checked against the budget; the first burn slows from
    // circular √(μ/r1) to the transfer ellipse's √(μ/r1)·√(r2/a) at the
    // top, a = (r1 + r2)/2. It's flown retrograde after the turn and cut off
    // when the low point is down at the target (PeriapsisBelow).
    private bool PlanLower(Double3 r, Double3 v, double thrust, double isp)
    {
        Elements(r, v, out var pe, out var ap, out _);
        var r1 = (pe + ap) / 2;
        var r2 = radius + CustomAltitude;
        var budget = RemainingDeltaV() * (1 - DeltaVReserve) - 30;
        var need = Hohmann(r2, r1);
        if (need > budget)
        {
            Fail("not enough Δv to come down to " + TrajectoryDisplay.Km(CustomAltitude) + ": needs " + need.ToString("N0") +
                 " m/s, has " + Math.Max(0, budget).ToString("N0") + " (after reserve).");
            return false;
        }
        orbitTarget = CustomAltitude;
        var a = (r1 + r2) / 2;
        deltaVPlanned = Math.Sqrt(mu / r1) * (1 - Math.Sqrt(r2 / a));
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = Now + TurnLead;
        steer = Steer.Retrograde; cutoff = Cutoff.PeriapsisBelow; cutoffValue = orbitTarget;
        Status = "Lowering orbit to " + TrajectoryDisplay.Km(orbitTarget) + " · transfer " + need.ToString("N0") +
                 " m/s of " + RemainingDeltaV().ToString("N0") + " m/s left";
        return true;
    }

    // --- The Moon ------------------------------------------------------------------

    // The Moon's position and velocity now (scene axes, from Earth's
    // centre), from the tabulated ephemeris: looked up at Now in the I frame
    // and turned into today's scene axes. The table is rebuilt longer if the
    // mission has run within a day of its end.
    private void MoonState(out Double3 position, out Double3 velocity)
    {
        var t = Now;
        if (t > moonTrack.Span - 86400)
        {
            moonTrack = new EarthMoonDynamics.MoonTrack(origin, SolarSystem.Instance.RotationAngleAt(origin), t + 12 * 86400);
        }
        position = ToScene(moonTrack.Position(t));
        velocity = ToScene(moonTrack.Velocity(t));
    }

    // The closest point of a predicted path to the Moon: how far from its
    // centre (m), when (s since origin), and the position and velocity there
    // relative to the Moon (I frame).
    private struct Approach
    {
        public double distance, time;
        public Double3 relative, relativeVelocity;
    }

    // Moves a state (I frame) forward to time 'until' under Earth's and the
    // Moon's gravity: RK4 steps (EarthMoonDynamics.Step), each sized by
    // SuggestedStep to ~1% of the orbital period about the nearer body, the
    // last one cut short to land exactly on 'until'.
    private void Propagate(ref Double3 r, ref Double3 v, ref double t, double until)
    {
        while (t < until)
        {
            var h = Math.Min(EarthMoonDynamics.SuggestedStep(r, v, moonTrack.Position(t), moonTrack.Velocity(t), mu, radius), until - t);
            EarthMoonDynamics.Step(ref r, ref v, t, h, moonTrack, mu);
            t += h;
        }
    }

    // Closest approach to the Moon's centre from (r, v) at t, up to tMax.
    // How: steps the path forward as Propagate does, and after each step
    // measures the distance to the Moon's tabulated position at that time,
    // keeping the smallest seen. It stops early once the path has been near
    // the Moon (inside its sphere of influence) and is now well past it
    // (1.5× that distance away), if it falls back into Earth's air, or if it
    // leaves for deep space. The Moon is treated as a point (no impact), so
    // a path straight through it still measures how far off it is - which
    // keeps the miss distance smooth for the solvers below.
    private Approach ClosestApproach(Double3 r, Double3 v, double t, double tMax)
    {
        var best = new Approach { distance = double.PositiveInfinity, time = -1 };
        while (t < tMax)
        {
            var rm = moonTrack.Position(t);
            var h = Math.Min(EarthMoonDynamics.SuggestedStep(r, v, rm, moonTrack.Velocity(t), mu, radius), tMax - t);
            EarthMoonDynamics.Step(ref r, ref v, t, h, moonTrack, mu);
            t += h;
            rm = moonTrack.Position(t);
            var rel = r - rm;
            var d = rel.Length;
            if (d < best.distance)
                best = new Approach { distance = d, time = t, relative = rel, relativeVelocity = v - moonTrack.Velocity(t) };
            if (best.distance < MoonBody.SphereOfInfluence && d > 1.5 * MoonBody.SphereOfInfluence) break;   // gone past
            if (r.Length - radius < 100000 && Double3.Dot(r, v) < 0) break;                                      // back into the air
            if (r.Length > 2e9) break;
        }
        return best;
    }

    // Solves for the Δv (inertial) at tBurn that brings the closest
    // approach to the Moon to LunarPassAltitude. How (Newton's method on one
    // equation, miss = closest distance − wanted = 0, in three unknowns):
    //  1. predict the pass for the current Δv (ClosestApproach);
    //  2. nudge the Δv by 1 m/s along x, y and z in turn and predict again -
    //     the three changes in miss distance form its gradient g;
    //  3. the smallest Δv change that would zero the miss if the problem
    //     were linear is −miss·g/|g|² (it points along g, the direction that
    //     changes the miss fastest), capped at 'maxStep' m/s;
    //  4. try it; if the miss didn't shrink, halve the change and retry
    //     (up to 6 times) - a line search, since the real problem is curved;
    //  5. repeat until the pass is within 3 km of the target (≤14 rounds).
    private Double3 TargetMoonPass(Double3 rB, Double3 vB, double tBurn, Double3 guess, double tMax, double maxStep, out Approach pass)
    {
        var wanted = MoonBody.RadiusMeters + LunarPassAltitude;
        var dv = guess;
        pass = ClosestApproach(rB, vB + dv, tBurn, tMax);
        for (var iteration = 0; iteration < 14; iteration++)
        {
            var miss = pass.distance - wanted;
            if (Math.Abs(miss) < 3000) break;
            const double probe = 1.0;
            var gx = (ClosestApproach(rB, vB + dv + new Double3(probe, 0, 0), tBurn, tMax).distance - pass.distance) / probe;
            var gy = (ClosestApproach(rB, vB + dv + new Double3(0, probe, 0), tBurn, tMax).distance - pass.distance) / probe;
            var gz = (ClosestApproach(rB, vB + dv + new Double3(0, 0, probe), tBurn, tMax).distance - pass.distance) / probe;
            var gradient = new Double3(gx, gy, gz);
            if (gradient.SqrLength < 1e-9) break;
            var change = gradient * (-miss / gradient.SqrLength);
            if (change.Length > maxStep) change = change.Normalized * maxStep;
            var improved = false;
            for (var halving = 0; halving < 6 && !improved; halving++)
            {
                var trial = ClosestApproach(rB, vB + dv + change, tBurn, tMax);
                if (Math.Abs(trial.distance - wanted) < Math.Abs(miss)) { dv = dv + change; pass = trial; improved = true; }
                else change = change / 2;
            }
            if (!improved) break;
        }
        return dv;
    }

    // Translunar injection. A weak upper stage (the ICPS: 110 kN pushing
    // ~58 t) would need most of an orbit to burn it all at once, losing
    // much of the Δv to steering and gravity; like real missions it splits
    // the departure into burns at the low point - each raising the high
    // point and coming back round a longer orbit - then a final one.
    // kickCount = raising burns needed (−1 = not decided yet), kicksDone =
    // how many are flown, kickBurn = the burn in progress is one of them,
    // kickApoapsis = the high point (m altitude) each one aims for.
    private int kickCount = -1, kicksDone;
    private bool kickBurn;
    private double[] kickApoapsis;

    // Plans the translunar injection (or the next raising burn). How, in
    // order: decide how many burns the stage needs; if raising burns are in
    // progress, centre the next one on the coming low point; otherwise walk
    // the orbit to find when to leave (below), wait and replan if that's
    // hours away, plan the first raising burn if any, or - for the final
    // departure - solve the instant-kick Δv (TargetMoonPass), turn it into
    // the steering split and energy cutoff the burn will fly with, and tune
    // those against a simulation of the real burn (TuneFiniteBurn).
    private bool PlanTli(Double3 r, Double3 v, double thrust, double isp)
    {
        var rI = ToI(r); var vI = ToI(v);
        var rNow = rI; var vNow = vI;   // kept for simulating the real burn below
        var t0 = Now;
        Elements(rI, vI, out var lowest, out var highest, out _);
        var semiMajor = (lowest + highest) / 2;
        var period = 2 * Math.PI * Math.Sqrt(semiMajor * semiMajor * semiMajor / mu);
        // The search can look two weeks ahead, plus the transfer: make sure
        // the Moon's tabulated path reaches that far.
        if (moonTrack.Span < t0 + 22 * 86400)
            moonTrack = new EarthMoonDynamics.MoonTrack(origin, SolarSystem.Instance.RotationAngleAt(origin), t0 + 25 * 86400);
        var moonDistance = moonTrack.Position(t0 + 3 * 86400).Length;
        kickBurn = false;

        // How many burns: so that none takes more than ~7% of an orbit. How:
        // the whole departure's burn time (rocket equation, from the speed a
        // transfer needs here minus the speed it has) divided by 7% of the
        // orbital period, rounded up - each extra burn is a raising burn.
        if (kickCount < 0)
        {
            TransferToMoonDistance(rI.Length, moonDistance, out var speed0, out _, out _);
            var whole = BurnTime(speed0 - vI.Length, thrust, isp);
            kickCount = Mathf.Clamp((int)Math.Ceiling(whole / (.07 * period)) - 1, 0, 3);
            kicksDone = 0;
        }
        // Later raising burns: centred on the next low point.
        if (kicksDone > 0 && kicksDone < kickCount)
        {
            Kick(t0 + TimeToPeriapsis(rI, vI), thrust, isp);
            return true;
        }

        // When and where along the orbit to start: the point from which the
        // transfer meets the Moon's distance just as the Moon gets there -
        // and, in an elliptical orbit, low down, where the burn costs least.
        // Each point's cost is its Δv, plus ~500 m/s per radian of mistiming
        // (made up later by changing the transfer's speed), plus ~50 m/s per
        // degree the Moon's arrival point lies off the orbit's plane (turning
        // the plane is expensive), plus a little for every hour of waiting.
        // An orbit lined up at launch (the launch window) scores well on its
        // first revolution; any other waits - up to two weeks - for the Moon
        // to come round to its plane, as it does twice a month. After
        // raising burns, only around the coming low point is searched.
        double from, to;
        if (kicksDone > 0)
        {
            var low = t0 + TimeToPeriapsis(rI, vI);
            from = Math.Max(t0 + TurnLead + 60, low - 1200); to = low + 1200;
        }
        else { from = t0 + TurnLead + 60; to = from + Math.Max(1.2 * period, 15 * 86400); }
        // The walk itself: move the vehicle along its coasting path in 20 s
        // steps from 'from' to 'to'; at each point work out the transfer that
        // would start there (TransferToMoonDistance) and where the Moon will
        // be when it arrives, measure the angle round the orbit to that point
        // (atan2 of its components across and along the present direction),
        // and score it; keep the cheapest.
        var t = t0;
        Propagate(ref rI, ref vI, ref t, from);
        double bestCost = double.PositiveInfinity, bestT = t, bestSpeed = 0, transferTime = 0;
        Double3 bestR = rI, bestV = vI;
        while (t < to)
        {
            TransferToMoonDistance(rI.Length, moonDistance, out var speed, out var time, out var transferAngle);
            // Raising burns still to come delay the departure by their orbits.
            var delay = kicksDone == 0 ? PhasingDelay(rI.Length, vI.Length, speed) : 0;
            var normal = Double3.Cross(rI, vI).Normalized;
            var moonThen = moonTrack.Position(t + delay + time);
            var angle = Math.Atan2(Double3.Dot(Double3.Cross(rI.Normalized, moonThen.Normalized), normal), Double3.Dot(rI.Normalized, moonThen.Normalized));
            if (angle < 0) angle += 2 * Math.PI;
            // How far the Moon's arrival point lies out of the orbit's plane:
            // the angle whose sine is its direction's component along the
            // plane's normal.
            var offPlane = Math.Abs(Math.Asin(Math.Max(-1, Math.Min(1, Double3.Dot(moonThen.Normalized, normal)))));
            var cost = Math.Max(0, speed - vI.Length) + 500 * Math.Abs(angle - transferAngle) + 3000 * offPlane + .5 * (t - t0) / 3600;
            if (cost < bestCost) { bestCost = cost; bestT = t; bestR = rI; bestV = vI; bestSpeed = speed; transferTime = time; }
            Propagate(ref rI, ref vI, ref t, t + 20);
        }

        // A long wait for the Moon to line up: coast (on rails) and plan
        // again a few hours before, from where the vehicle really is then.
        if (bestT - t0 > 6 * 3600)
        {
            replanAt = bestT - 3 * 3600;
            Status = "Waiting " + TrajectoryDisplay.Clock(bestT - t0) + " in orbit for the Moon to line up with it";
            return false;
        }

        if (kickCount > 0 && kicksDone == 0)
        {
            var total = bestSpeed - bestV.Length;
            if (RemainingDeltaV() < total + 25)
            {
                Fail("not enough Δv to reach the Moon (" + RemainingDeltaV().ToString("N0") + " of ~" + total.ToString("N0") + " m/s).");
                return false;
            }
            // Each raising burn adds an equal share of the speed at the low
            // point; its target high point follows from vis-viva - the
            // semi-major axis for that speed at radius rp is a = 1/(2/rp −
            // v²/μ), and the high point is 2a − rp.
            kickApoapsis = new double[kickCount];
            var rp = bestR.Length;
            for (var i = 0; i < kickCount; i++)
            {
                var vp = bestV.Length + (i + 1) * total / (kickCount + 1);
                var a = 1 / (2 / rp - vp * vp / mu);
                kickApoapsis[i] = 2 * a - rp - radius;
            }
            Kick(bestT, thrust, isp);
            return true;
        }

        var guess = bestV.Normalized * (bestSpeed - bestV.Length);
        var dv = TargetMoonPass(bestR, bestV, bestT, guess, bestT + transferTime * 1.8, 150, out var pass);
        if (pass.distance - MoonBody.RadiusMeters > 20000000)
        {
            Fail("couldn't find a path to the Moon from this orbit.");
            return false;
        }
        deltaVPlanned = dv.Length;
        if (RemainingDeltaV() < deltaVPlanned + 25)
        {
            Fail("not enough Δv to reach the Moon (" + RemainingDeltaV().ToString("N0") + " of ~" + deltaVPlanned.ToString("N0") + " m/s).");
            return false;
        }
        // Flown as a finite burn, centred on the planned point, steered with
        // the same split of prograde / out-of-plane / radial as the solution
        // and cut off at the solution's orbital energy. How: the solved Δv's
        // direction is broken into its components along the velocity (v̂),
        // the orbit normal (ĥ = r × v) and the vertical (r̂) at the burn point
        // - BurnDirection re-applies those to the turning orbit each step;
        // the cutoff energy is the energy the orbit would have just after the
        // instant kick, (v + Δv)²/2 − μ/r.
        var vHat = bestV.Normalized; var hHat = Double3.Cross(bestR, bestV).Normalized; var rHat = bestR.Normalized;
        var dir = dv.Normalized;
        tliPrograde = Double3.Dot(dir, vHat); tliNormal = Double3.Dot(dir, hHat); tliRadial = Double3.Dot(dir, rHat);
        var after = bestV + dv;
        cutoffValue = after.SqrLength / 2 - mu / bestR.Length;
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = bestT - burnDuration / 2;
        steer = Steer.Tli; cutoff = Cutoff.Energy;

        // That was an instant kick; the real burn lasts minutes and sweeps
        // several degrees round the orbit, so flown as planned it misses by
        // thousands of km. Simulate the burn as it will be flown and tune its
        // cutoff energy and out-of-plane steering until the simulated pass
        // is at the target height.
        var rStart = rNow; var vStart = vNow; var tStart = t0;
        Propagate(ref rStart, ref vStart, ref tStart, burnStart);
        pass = TuneFiniteBurn(rStart, vStart, burnStart, thrust, isp, bestT + transferTime * 1.8);

        // Braking into lunar orbit at the pass: from the arrival speed
        // relative to the Moon down to circular there.
        var braking = pass.relativeVelocity.Length - Math.Sqrt(MoonBody.GravitationalParameter / pass.distance);
        var flybyOnly = RemainingDeltaV() - deltaVPlanned < braking;
        Status = "Translunar injection: " + deltaVPlanned.ToString("N0") + " m/s, arriving in ~" +
                 TrajectoryDisplay.Clock(pass.time - bestT) + " at " + TrajectoryDisplay.Km(pass.distance - MoonBody.RadiusMeters) +
                 (flybyOnly ? " - only enough Δv for a flyby (lunar orbit needs ~" + braking.ToString("N0") + " m/s more)" : "");
        return true;
    }

    // Simulates the translunar burn as the autopilot flies it, from (r, v)
    // at time t (inertial frame): full thrust along the Tli steering split
    // (prograde / out-of-plane / radial, re-aimed every second as the orbit
    // turns), mass falling at ṁ = F/(g0·Isp), under Earth's and the Moon's
    // gravity (RK4, 1 s steps), cut off the moment the orbital energy
    // reaches 'energyCut' (the last step shortened to land on it exactly).
    // Returns the closest approach to the Moon that follows.
    private Approach FiniteBurn(Double3 r, Double3 v, double t, double normal, double energyCut, double thrust, double isp, double tMax)
    {
        var mass = flight.TotalMass;
        var massFlow = thrust / (isp * EnginePerformance.G0);
        for (var i = 0; i < 7200; i++)
        {
            var energy = v.SqrLength / 2 - mu / r.Length;
            if (energy >= energyCut) break;
            // Steering direction for this step, as BurnDirection computes it.
            var hHat = Double3.Cross(r, v).Normalized;
            var push = (v.Normalized * tliPrograde + hHat * normal + r.Normalized * tliRadial).Normalized * (thrust / mass);
            // A whole step, then - if it overshoots the cutoff - the same step
            // shortened in proportion (energy rises almost linearly over 1 s).
            var r1 = r; var v1 = v;
            ThrustStep(ref r1, ref v1, t, 1, push);
            var after = v1.SqrLength / 2 - mu / r1.Length;
            var h = after > energyCut ? Math.Max(.01, (energyCut - energy) / (after - energy)) : 1;
            if (h < 1) { r1 = r; v1 = v; ThrustStep(ref r1, ref v1, t, h, push); }
            r = r1; v = v1; t += h;
            mass -= massFlow * h;
            if (mass <= 0) break;
        }
        return ClosestApproach(r, v, t, tMax);
    }

    // One RK4 step of h seconds under gravity (Earth + the Moon's and the
    // Sun's tidal pulls) plus a constant thrust acceleration 'push'. How: classic
    // fourth-order Runge-Kutta - the slope (velocity, acceleration) is
    // sampled at the start, twice at the midpoint and at the end, and the
    // step uses their weighted average (1, 2, 2, 1)/6; the Moon's position
    // for each sample comes from its table at that sample's time.
    private void ThrustStep(ref Double3 r, ref Double3 v, double t, double h, Double3 push)
    {
        Double3 A(Double3 rr, double tt) => EarthMoonDynamics.Acceleration(rr, moonTrack.Position(tt), moonTrack.SunPosition(tt), mu) + push;
        var k1r = v; var k1v = A(r, t);
        var k2r = v + k1v * (h / 2); var k2v = A(r + k1r * (h / 2), t + h / 2);
        var k3r = v + k2v * (h / 2); var k3v = A(r + k2r * (h / 2), t + h / 2);
        var k4r = v + k3v * h; var k4v = A(r + k3r * h, t + h);
        r = r + (k1r + k2r * 2 + k3r * 2 + k4r) * (h / 6);
        v = v + (k1v + k2v * 2 + k3v * 2 + k4v) * (h / 6);
    }

    // Tunes the finite burn's cutoff energy and out-of-plane steering
    // (cutoffValue, tliNormal) so the simulated pass is at LunarPassAltitude.
    // How: the same Newton-with-line-search as TargetMoonPass, but over two
    // controls, each measured in m/s of equivalent Δv so one step treats
    // them alike - energy: a change of Δv near the end of the burn changes
    // the energy by about v·Δv (d(v²/2) = v·dv), so 1 m/s ≈ 'speed' J/kg;
    // steering: the out-of-plane share times the whole burn's Δv. Each round
    // simulates the burn three times (as is, energy +1 m/s, steering +1 m/s)
    // to get the gradient of the miss, steps by −miss·g/|g|² (capped at
    // 50 m/s), halving until the miss shrinks; up to 12 rounds, stopping
    // within 3 km.
    private Approach TuneFiniteBurn(Double3 r, Double3 v, double t, double thrust, double isp, double tMax)
    {
        var wanted = MoonBody.RadiusMeters + LunarPassAltitude;
        var speed = v.Length + deltaVPlanned;              // m/s near the end of the burn
        var share = Math.Max(1, deltaVPlanned);           // m/s per unit of steering share
        double Miss(double energy, double normal, out Approach a)
        {
            a = FiniteBurn(r, v, t, normal, energy, thrust, isp, tMax);
            return a.distance - wanted;
        }
        var e = cutoffValue; var n = tliNormal;
        var miss = Miss(e, n, out var pass);
        for (var iteration = 0; iteration < 12 && Math.Abs(miss) > 3000; iteration++)
        {
            const double probe = 1;   // m/s
            var ge = (Miss(e + speed * probe, n, out _) - miss) / probe;
            var gn = (Miss(e, n + probe / share, out _) - miss) / probe;
            var gg = ge * ge + gn * gn;
            if (gg < 1e-9) break;
            var de = -miss * ge / gg; var dn = -miss * gn / gg;   // m/s
            var size = Math.Sqrt(de * de + dn * dn);
            if (size > 50) { de *= 50 / size; dn *= 50 / size; }
            var improved = false;
            for (var halving = 0; halving < 6 && !improved; halving++)
            {
                var trial = Miss(e + speed * de, n + dn / share, out var trialPass);
                if (Math.Abs(trial) < Math.Abs(miss)) { e += speed * de; n += dn / share; miss = trial; pass = trialPass; improved = true; }
                else { de /= 2; dn /= 2; }
            }
            if (!improved) break;
        }
        cutoffValue = e; tliNormal = n;
        return pass;
    }

    // A raising burn centred on 'centre': prograde until the high point is
    // up at its target. How: the Δv shown is the change of speed at the low
    // point between the present orbit and the raised one, each from
    // vis-viva at the low point with a = (low + high)/2; the burn starts
    // half its estimated duration before 'centre' and is cut off on the
    // live high point (Cutoff.ApoapsisAbove), not on the Δv.
    private void Kick(double centre, double thrust, double isp)
    {
        kickBurn = true;
        cutoffValue = kickApoapsis[kicksDone];
        // Δv at the low point: from the present orbit's speed there to the raised one's.
        GetState(out var r, out var v);
        Elements(r, v, out var pe, out var ap, out _);
        var now = Math.Sqrt(mu * (2 / pe - 2 / (pe + ap)));
        var raised = Math.Sqrt(mu * (2 / pe - 2 / (pe + radius + cutoffValue)));
        deltaVPlanned = Math.Max(0, raised - now);
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = centre - burnDuration / 2;
        steer = Steer.Prograde; cutoff = Cutoff.ApoapsisAbove;
        Status = "Raising the high point to " + TrajectoryDisplay.Km(cutoffValue) + " (departure burn " + (kicksDone + 1) + " of " + (kickCount + 1) + ")";
    }

    // Time spent on the raising burns' orbits before the final departure.
    // How: after raising burn i the speed at the low point is the starting
    // speed plus i shares of the total; that orbit's semi-major axis comes
    // from vis-viva (a = 1/(2/rp − v²/μ)) and its period from Kepler's third
    // law, 2π√(a³/μ); one revolution of each is added up.
    private double PhasingDelay(double rp, double vp, double speed)
    {
        double delay = 0;
        for (var i = 0; i < kickCount; i++)
        {
            var v = vp + (i + 1) * (speed - vp) / (kickCount + 1);
            var a = 1 / (2 / rp - v * v / mu);
            delay += 2 * Math.PI * Math.Sqrt(a * a * a / mu);
        }
        return delay;
    }

    // Seconds until the low point: as TimeToApoapsis, but the low point is
    // at mean anomaly 2π (= 0 of the next revolution), so (2π − M)/√(μ/a³).
    private double TimeToPeriapsis(Double3 r, Double3 v)
    {
        var rl = r.Length;
        var energy = v.SqrLength / 2 - mu / rl;
        if (energy >= 0) return 0;
        var a = -mu / (2 * energy);
        var e = (Double3.Cross(v, Double3.Cross(r, v)) / mu - r / rl).Length;
        if (e < 1e-5) return 0;
        var E = Math.Acos(Math.Max(-1, Math.Min(1, (1 - rl / a) / e)));
        if (Double3.Dot(r, v) < 0) E = 2 * Math.PI - E;
        var M = E - e * Math.Sin(E);
        return (2 * Math.PI - M) * Math.Sqrt(a * a * a / mu);
    }

    // Course corrections on the way out: the first a couple of hours after
    // injection, the second half a day before arrival; each skipped if the
    // pass is already close enough. How: predicts the present pass
    // (ClosestApproach over the next 8 days); if it's within tolerance (10 km
    // for the first, 25 km for the second), or the correction would come
    // too close to arrival, moves on. If the correction is still far off it
    // sets replanAt and coasts (warping) until a few minutes before, then
    // plans afresh from the state there: propagates to the burn time, solves
    // the Δv with TargetMoonPass starting from zero (≤30 m/s per Newton
    // step), and flies it in a direction fixed among the stars, cut off when
    // that much Δv has been delivered (Cutoff.DeltaV).
    private bool PlanCorrection(Double3 r, Double3 v, double thrust, double isp)
    {
        var rI = ToI(r); var vI = ToI(v);
        var t0 = Now;
        var current = ClosestApproach(rI, vI, t0, t0 + 8 * 86400);
        if (current.time < 0) { Fail("lost the Moon."); return false; }
        var passAltitude = current.distance - MoonBody.RadiusMeters;
        var tolerance = step == Step.Mcc1 ? 10000 : 25000;
        var when = step == Step.Mcc1 ? correctionFrom + 2 * 3600 : current.time - 12 * 3600;
        // A late correction (sent back here by PlanLoi): as soon as the turn
        // allows, and allowed down to half an hour before the pass.
        var minimumLead = 3 * 3600.0;
        if (lateCorrection) { when = t0 + TurnLead + 60; minimumLead = 1800; lateCorrection = false; }
        if (Math.Abs(passAltitude - LunarPassAltitude) < tolerance || current.time - Math.Max(when, t0) < minimumLead)
        {
            Advance();
            return false;
        }
        Status = "Coasting to the Moon · pass at " + TrajectoryDisplay.Km(passAltitude) + " in " + TrajectoryDisplay.Clock(current.time - t0);
        if (when - t0 > TurnLead + 600) { replanAt = when - TurnLead - 300; return false; }
        when = Math.Max(when, t0 + TurnLead);
        var rB = rI; var vB = vI; var tB = t0;
        Propagate(ref rB, ref vB, ref tB, when);
        var dv = TargetMoonPass(rB, vB, tB, new Double3(0, 0, 0), current.time + 2 * 86400, 30, out var pass);
        deltaVPlanned = dv.Length;
        if (deltaVPlanned < .5) { Advance(); return false; }
        fixedDirectionI = dv.Normalized;
        cutoffValue = deltaVPlanned;
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = when - burnDuration / 2;
        steer = Steer.Fixed; cutoff = Cutoff.DeltaV;
        Status = "Course correction " + deltaVPlanned.ToString("N1") + " m/s → pass at " + TrajectoryDisplay.Km(pass.distance - MoonBody.RadiusMeters);
        return true;
    }

    private double correctionFrom;   // when translunar injection ended
    private bool lateCorrection;      // the next correction is a late one, from PlanLoi
    private int lateCorrections;      // how many of those have been flown

    // Lunar orbit insertion. How: predicts the pass; misses and impacts end
    // the mission; while it's more than 3 h away it coasts and plans again
    // 2 h out (where the prediction is best). The braking Δv is the speed
    // relative to the Moon at the pass minus circular speed there,
    // √(μ_Moon/r); the burn is centred on the pass, steered against the
    // velocity relative to the Moon, and cut off when the orbit about the
    // Moon is circular (Cutoff.MoonCircular).
    private bool PlanLoi(Double3 r, Double3 v, double thrust, double isp)
    {
        var rI = ToI(r); var vI = ToI(v);
        var t0 = Now;
        var pass = ClosestApproach(rI, vI, t0, t0 + 8 * 86400);
        if (pass.time < 0 || pass.distance > MoonBody.SphereOfInfluence) { Finish("Missed the Moon"); return false; }
        // Headed into the surface (or skimming it): small errors in the last
        // correction can do that. With time left, fly one more correction
        // straight away (step back to Mcc2 with lateCorrection set, which
        // lets it plan as little as half an hour before the pass), then
        // plan this again; only out of time or tries does the mission stop.
        if (pass.distance < MoonBody.RadiusMeters + 50000)
        {
            if (lateCorrections < 2 && pass.time - t0 > TurnLead + 1800)
            {
                lateCorrections++; lateCorrection = true;
                step = Step.Mcc2;
                Status = "Pass too low (" + TrajectoryDisplay.Km(pass.distance - MoonBody.RadiusMeters) + ") - one more course correction";
                return false;
            }
            if (pass.distance < MoonBody.RadiusMeters) { Fail("on course to hit the Moon."); return false; }
        }
        Status = "Coasting to the Moon · pass at " + TrajectoryDisplay.Km(pass.distance - MoonBody.RadiusMeters) + " in " + TrajectoryDisplay.Clock(pass.time - t0);
        // Planned again an hour or two out, where the pass is known best.
        if (pass.time - t0 > 3 * 3600) { replanAt = pass.time - 2 * 3600; return false; }
        var circular = Math.Sqrt(MoonBody.GravitationalParameter / pass.distance);
        deltaVPlanned = pass.relativeVelocity.Length - circular;
        burnDuration = BurnTime(deltaVPlanned, thrust, isp);
        burnStart = pass.time - burnDuration / 2;
        steer = Steer.MoonRetrograde; cutoff = Cutoff.MoonCircular;
        Status = "To the Moon · pass at " + TrajectoryDisplay.Km(pass.distance - MoonBody.RadiusMeters) +
                 " · braking " + deltaVPlanned.ToString("N0") + " m/s (have " + RemainingDeltaV().ToString("N0") + ")";
        return true;
    }

    // --- Panel -----------------------------------------------------------------------

    private GUIStyle boxTitle, boxText;
    // What's in the height box, as typed (kept as text so a half-typed
    // number isn't rejected or reformatted while editing).
    private string heightText = "1000";
    private string prediction;
    private float nextPrediction;

    // The AUTOPILOT box, top right (Unity's immediate-mode GUI: redrawn every
    // frame; a button returns true on the frame it's clicked). How: a
    // GUILayout area holds the destination buttons (the active one tinted),
    // the max-orbit prediction (recomputed once a second while idle, since it
    // runs the rocket equation over every stage), the mission status and
    // detail lines, and the time-warp toggle.
    private void OnGUI()
    {
        if (LaunchMenu.Open || flight == null || flight.Crashed && step == Step.Idle) return;
        // On the pad only for a real rocket (a custom build's pad has the
        // assembly panels there).
        if (!rocket.Launched && !assembly.IsPreset) return;
        boxTitle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        boxText ??= new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        const float w = 300;
        var x = Screen.width - w - 16;
        var y = 84f;
        GUILayout.BeginArea(new Rect(x, y, w, 330), GUI.skin.box);   // 330: room for the custom-height row
        GUILayout.Label("AUTOPILOT" + (Engaged ? " · ON" : ""), boxTitle);
        GUILayout.Label("Orbit", boxText);
        GUILayout.BeginHorizontal();
        if (Button("Low orbit", Engaged && Target == Destination.LowOrbit)) Engage(Destination.LowOrbit);
        if (Button(new GUIContent("Max orbit", "For study: the highest circular orbit this rocket can reach."), Engaged && Target == Destination.MaxOrbit)) Engage(Destination.MaxOrbit);
        GUILayout.EndHorizontal();
        // Custom height: a text box (km) and a Go button. How: on Go the text
        // is parsed (invariant culture, so '.' is the decimal point; commas
        // and spaces stripped so "35,786" works), turned into metres and
        // stored in CustomAltitude; Engage then range-checks it. A bad number
        // shows a message instead of engaging.
        GUILayout.BeginHorizontal();
        GUILayout.Label("Height (km)", boxText, GUILayout.Width(72));
        heightText = GUILayout.TextField(heightText, 12, GUILayout.Width(110));
        if (Button(new GUIContent("Go", "Fly to a circular orbit at this height: launches to it (or from the pad via the 200 km parking orbit), or raises / lowers an existing orbit with a Hohmann transfer."),
                   Engaged && Target == Destination.CustomOrbit))
        {
            if (double.TryParse(heightText.Replace(",", "").Replace(" ", ""), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var km) && km > 0)
            {
                CustomAltitude = km * 1000;
                Engage(Destination.CustomOrbit);
            }
            else Status = "Type a height in km, e.g. 400 or 35786";
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Destination (via low orbit)", boxText);
        GUILayout.BeginHorizontal();
        if (Button("Moon", Engaged && Target == Destination.Moon)) Engage(Destination.Moon);
        if (Button("Off", !Engaged)) Disengage();
        GUILayout.EndHorizontal();
        if (!Engaged && Time.unscaledTime >= nextPrediction)
        {
            nextPrediction = Time.unscaledTime + 1;
            var max = PredictMaxOrbit(out var deltaV);
            prediction = "Δv " + deltaV.ToString("N0") + " m/s" +
                (!rocket.Launched && PredictedAscentDeltaV > 0 ? " (ascent ≈ " + PredictedAscentDeltaV.ToString("N0") + ")" : "") + " · " + (max < 0
                ? (rocket.Launched ? "max orbit: once in orbit" : "can't reach orbit" + (AscentSimNote.Length > 0 ? " (simulated: " + AscentSimNote + ")" : ""))
                : "max orbit ≈ " + TrajectoryDisplay.Km(max) + (max + radius >= StableOrbitRadius - 1 ? " (stability limit - the Moon)" : ""));
        }
        if (!Engaged && prediction != null) GUILayout.Label(prediction, boxText);
        if (!string.IsNullOrEmpty(Status)) GUILayout.Label(Status, boxText);
        if (Engaged && !string.IsNullOrEmpty(Detail)) GUILayout.Label(Detail, boxText);
        AutoWarp = GUILayout.Toggle(AutoWarp, " Time warp through coasts");
        if (!Engaged && step == Step.Idle)
            GUILayout.Label(rocket.Launched ? "Takes over from here." : "Launches now and flies the whole way.", boxText);
        GUILayout.EndArea();
        if (askingEscape) EscapePromptGUI();
    }

    // Closes the escape question and restarts time (timeScale back to 1;
    // the pilot can warp again from there).
    private void CloseEscapePrompt()
    {
        if (!askingEscape) return;
        askingEscape = false;
        Time.timeScale = 1;
    }

    private GUIStyle promptTitle, promptText;

    // The escape question, centred over a dimmed screen. How: drawn last in
    // OnGUI so it sits above the autopilot box; a full-screen
    // GUI.Box behind it swallows clicks meant for the view. The buttons
    // record the choice, close the pop-up (unpausing) and clear 'planned',
    // so the next physics step plans the Max-orbit burn again - now flying
    // the Escape step, or the normal raise to the stable ceiling.
    private void EscapePromptGUI()
    {
        promptTitle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        promptText ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, .6f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, GUIStyle.none);
        var w = Mathf.Min(520f, Screen.width - 32f);
        const float h = 290f;
        GUILayout.BeginArea(new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h), GUI.skin.window);
        GUILayout.Label("⚠ GAME PAUSED: ESCAPE EARTH?", promptTitle);
        GUILayout.Space(6);
        GUILayout.Label("This rocket has " + spareDeltaV.ToString("N0") + " m/s more than the highest stable Earth orbit (" +
                        TrajectoryDisplay.Km(stableTarget) + ") needs. Above that height the Moon pulls circular orbits apart, " +
                        "so the extra Δv can't buy a higher Earth orbit.", promptText);
        GUILayout.Space(4);
        GUILayout.Label("Escape: burn every m/s left, prograde, starting now. The rocket leaves Earth for good, at about " +
                        (escapeSpeed / 1000).ToString("F1") + " km/s once far away, with 0 Δv left.", promptText);
        GUILayout.Label("Stay: fly to the highest stable Earth orbit (" + TrajectoryDisplay.Km(stableTarget) + ") and keep the spare Δv.", promptText);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Escape Earth (burn it all)", GUILayout.Height(34))) { escapeChoice = EscapeChoice.Escape; CloseEscapePrompt(); planned = false; }
        if (GUILayout.Button("Stay in max Earth orbit", GUILayout.Height(34))) { escapeChoice = EscapeChoice.Stay; CloseEscapePrompt(); planned = false; }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private static bool Button(string text, bool selected) => Button(new GUIContent(text), selected);

    // A button tinted orange when 'selected' (the destination being flown):
    // GUI.backgroundColor tints the controls drawn after it, so it's set,
    // the button drawn, and the colour put back.
    private static bool Button(GUIContent text, bool selected)
    {
        var previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(1f, .72f, .3f);
        var pressed = GUILayout.Button(text);
        GUI.backgroundColor = previous;
        return pressed;
    }
}
