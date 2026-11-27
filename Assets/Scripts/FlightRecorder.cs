using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// STUDY: ==================== STUDY GUIDE: FlightRecorder ====================
// STUDY: WHAT: writes telemetry to a CSV in Assets/Data every sampleInterval
// STUDY: seconds of simulated flight (Begin when the clamp releases, WriteRow,
// STUDY: End on impact or return to assembly), with blank lines between stages.
// STUDY: Read-only: it changes nothing in the flight.

/// <summary>
/// Writes flight telemetry to a CSV file, one row every
/// <see cref="sampleInterval"/> seconds of simulated time from the moment
/// the launch clamp releases until the vehicle impacts or returns to
/// assembly. Columns follow Assets/Data/Example csv:
///
///   height        m     altitude above the planet surface
///   velocity      m/s   speed
///   acceleration  m/s²  rate of change of velocity (includes gravity)
///   drag          N     aerodynamic drag force
///   torque        N·m   net torque on the vehicle, from its angular
///                       acceleration and inertia (gimbal/thrust offset)
///   fuel          kg    fuel + oxidizer remaining, plus any solid
///                       boosters' propellant
///   mass          kg    total vehicle mass
///   time          s     game time since launch, read from the game's
///                       date clock (SolarSystem) - the clock rails warp,
///                       the Moon and the autopilot run on
///   temperature   °C    outside air temperature, US Standard Atmosphere
///                       1976 - the same model the drag uses
///   tilt          deg   angle between the rocket's axis and local vertical
///   G-force       g     felt acceleration (thrust + drag, not gravity):
///                       1 g sitting on the pad, 0 g coasting
///
/// Each time the vehicle enters a new stage (a staged real rocket's next
/// stage lights) the file gets <see cref="StageGapLines"/> blank lines, so
/// the stages read as separate blocks.
///
/// Files go to Assets/Data in the editor (next to the example), or
/// persistentDataPath/FlightLogs in a build.
/// </summary>
[RequireComponent(typeof(Rocket), typeof(RocketFlightModel))]
public sealed class FlightRecorder : MonoBehaviour
{
    public const string Header = "height,velocity,acceleration,drag,torque,fuel,mass,time,temperature,tilt,G-force";
    private const double StandardGravity = 9.80665;
    private const int StageGapLines = 3;

    [Tooltip("Seconds of simulated flight time between CSV rows.")]
    // 3 s of game time per row, on the game's date clock (see Clock), so
    // warp changes how fast rows arrive in real time, never their spacing
    // in game time.
    [SerializeField] private float sampleInterval = 3f;

    private Rocket rocket;
    private RocketFlightModel flight;
    private RocketAssemblyController assembly;
    private PlanetBody planet;
    private Rigidbody body;
    private StreamWriter writer;
    private double elapsed, nextSample;
    // The game date at liftoff (default = no SolarSystem in the scene), and
    // a fallback clock summing physics steps for that case - see Clock().
    private DateTime startDate;
    private double stepClock;
    private int recordedStage;
    private Vector3 previousVelocity, previousAngularVelocity;
    private Vector3 acceleration, angularAcceleration;

    public string CurrentFile { get; private set; }

    // The scene's Rocket object predates this component - attach it at
    // startup rather than requiring a scene edit.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        foreach (var model in FindObjectsByType<RocketFlightModel>(FindObjectsSortMode.None))
            if (model.GetComponent<FlightRecorder>() == null) model.gameObject.AddComponent<FlightRecorder>();
    }

    private void Awake()
    {
        rocket = GetComponent<Rocket>();
        flight = GetComponent<RocketFlightModel>();
        assembly = GetComponent<RocketAssemblyController>();
        body = GetComponent<Rigidbody>();
        planet = FindFirstObjectByType<PlanetBody>();
    }

    private void FixedUpdate()
    {
        var dt = Time.fixedDeltaTime;
        var velocity = flight.GroundVelocity;
        var angularVelocity = body.angularVelocity;
        acceleration = (velocity - previousVelocity) / dt;
        angularAcceleration = (angularVelocity - previousAngularVelocity) / dt;
        previousVelocity = velocity;
        previousAngularVelocity = angularVelocity;

        if (writer == null)
        {
            if (rocket.Launched && !flight.Crashed) Begin();
            return;
        }
        if (!rocket.Launched)
        {
            End();
            return;
        }

        stepClock += dt * TimeWarp.ClockMultiplier;   // fallback clock only
        // On rails the rocket isn't moved by physics steps but once per
        // rendered frame (TimeWarp.Update): its rows are taken in LateUpdate,
        // after that frame's move. Here: every physics step otherwise.
        if (TimeWarp.OnRails && !flight.Crashed) return;
        Sample(dt * 0.5);
    }

    // Rails warp: one sample opportunity per frame, after TimeWarp has moved
    // the rocket and SolarSystem has advanced the date (LateUpdate runs after
    // every script's Update), so position and clock belong to the same moment.
    private void LateUpdate()
    {
        if (writer == null || !rocket.Launched || flight.Crashed || !TimeWarp.OnRails) return;
        Sample(0);
    }

    /// <summary>
    /// Writes a row if the next 3 s mark has been reached. How: blank lines
    /// first if a new stage has lit; then the clock (Clock: game seconds
    /// since liftoff) is read; a crash writes a last row and closes the file;
    /// otherwise, once the clock is within 'tolerance' of nextSample, a row
    /// is written and nextSample moves on 3 s - or, if a single step jumped
    /// past several marks (high warp), to the next mark ahead, so there is
    /// at most one row per step and no backlog to catch up.
    /// tolerance: half a physics step for physics sampling (the step times
    /// land just either side of each mark), 0 on rails.
    /// </summary>
    private void Sample(double tolerance)
    {
        // A new stage: leave a gap in the file before its rows.
        var stage = assembly != null ? assembly.StageIndex : 0;
        if (stage != recordedStage)
        {
            recordedStage = stage;
            for (var i = 0; i < StageGapLines; i++) writer.WriteLine();
        }

        elapsed = Clock();
        // Log the impact itself as a final row, then stop - nothing
        // changes afterwards.
        if (flight.Crashed)
        {
            WriteRow();
            End();
        }
        else if (elapsed + tolerance >= nextSample)
        {
            WriteRow();
            nextSample += sampleInterval;
            if (nextSample <= elapsed) nextSample = (Math.Floor(elapsed / sampleInterval) + 1) * sampleInterval;
        }
    }

    /// <summary>
    /// Game seconds since liftoff. How: the game's date now minus the date
    /// at liftoff. SolarSystem advances its date every frame by the frame's
    /// time × the warp rate - the same amount rails warp moves the rocket -
    /// and DateAt(Time.time) gives it at this exact step (inside a physics
    /// step Time.time is that step's time). Summing physics steps instead
    /// lost time whenever rails warp moved the rocket without matching
    /// physics steps (a whole 48-minute transfer coast showed as ~20 s).
    /// Without a SolarSystem in the scene, the physics-step sum is used.
    /// </summary>
    private double Clock() =>
        SolarSystem.Instance != null && startDate != default
            ? (SolarSystem.Instance.DateAt(Time.time) - startDate).TotalSeconds
            : stepClock;

    private void Begin()
    {
        var folder = Application.isEditor
            ? Path.Combine(Application.dataPath, "Data")
            : Path.Combine(Application.persistentDataPath, "FlightLogs");
        Directory.CreateDirectory(folder);
        var vehicle = assembly != null && !string.IsNullOrEmpty(assembly.ActivePresetName) ? assembly.ActivePresetName : "Custom";
        // Fair-test flights get "reference" / "replay" in the name (the
        // Autopilot sets the tag just before liftoff; empty otherwise).
        var tag = string.IsNullOrEmpty(Autopilot.FlightTag) ? "" : Autopilot.FlightTag + " ";
        CurrentFile = Path.Combine(folder, "Flight " + vehicle + " " + tag + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + ".csv");
        writer = new StreamWriter(CurrentFile, false) { AutoFlush = true };
        writer.WriteLine(Header);
        elapsed = stepClock = 0;
        startDate = SolarSystem.Instance != null ? SolarSystem.Instance.DateAt(Time.time) : default;
        recordedStage = assembly != null ? assembly.StageIndex : 0;
        // The first physics step after release would otherwise read the
        // whole jump from rest as one step's acceleration.
        acceleration = Vector3.zero;
        angularAcceleration = Vector3.zero;
        WriteRow();
        nextSample = sampleInterval;
        Debug.Log("FLIGHT_RECORDER started: " + CurrentFile);
    }

    private void End()
    {
        if (writer == null) return;
        writer.Dispose();
        writer = null;
        Debug.Log("FLIGHT_RECORDER saved: " + CurrentFile);
    }

    private void OnDisable() => End();

    private void WriteRow()
    {
        var up = planet != null ? (transform.position - planet.transform.position).normalized : Vector3.up;
        var gravity = planet != null
            ? planet.GetGravityAcceleration(body.worldCenterOfMass) / PlanetBody.WorldUnitsPerMeter
            : Vector3.zero;
        // On rails the rocket is coasting in a vacuum: free fall, 0 g felt.
        var onRails = TimeWarp.OnRails && rocket.Launched;
        var felt = onRails ? Vector3.zero : body.isKinematic ? -gravity : acceleration - gravity;
        var altitude = flight.Altitude;
        var fields = new[]
        {
            altitude,
            flight.Speed,
            onRails ? gravity.magnitude : body.isKinematic ? 0 : acceleration.magnitude,
            flight.Drag,
            body.isKinematic ? 0 : Torque(),   // (0 on rails: attitude held)
            flight.FuelRemaining + flight.OxidizerRemaining + flight.SolidPropellant,
            flight.TotalMass,
            elapsed,
            StandardAtmosphere.TemperatureKelvin(altitude) - 273.15,
            Vector3.Angle(transform.up, up),
            felt.magnitude / StandardGravity,
        };
        writer.WriteLine(string.Join(",", Array.ConvertAll(fields, f => f.ToString("0.###", CultureInfo.InvariantCulture))));
    }

    // Euler's rotation equation in the body's principal axes:
    // τ = I·α + ω × (I·ω). Inertia is in kg·(world unit)², so convert to m².
    private double Torque()
    {
        var principal = body.rotation * body.inertiaTensorRotation;
        var inverse = Quaternion.Inverse(principal);
        var alpha = inverse * angularAcceleration;
        var omega = inverse * body.angularVelocity;
        var inertia = body.inertiaTensor;
        var torque = Vector3.Scale(inertia, alpha) + Vector3.Cross(omega, Vector3.Scale(inertia, omega));
        return torque.magnitude / (PlanetBody.WorldUnitsPerMeter * PlanetBody.WorldUnitsPerMeter);
    }
}
