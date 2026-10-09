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
///
/// It is placed for each physics step's own moment (it sweeps through the
/// Earth-fixed scene at ~28 km/s), from Earth's centre in double precision
/// (FloatingOrigin), so a rocket can fly down to it and land: touching
/// down on the mean surface at up to 3 m/s down, 2 m/s across and 12° from
/// upright (Apollo's lunar module was built for 3 m/s and 1.2 m/s) it
/// stands there, carried round with the Moon, until its engines can lift
/// it; any harder and it crashes. Close to the surface a fine patch of
/// ground is drawn under the rocket - the sphere's mesh is faceted by
/// hundreds of metres.
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
    // Where the Moon's pull outweighs Earth's for a passing craft (Laplace).
    public const double SphereOfInfluence = 66.1e6;   // m
    // The Moon's spin (rad/s): one turn per sidereal month (tidally locked).
    private const double SpinRate = 481267.88123421 * Math.PI / 180 / (36525.0 * 86400);
    // Touchdown limits.
    public const double SafeVerticalSpeed = 3, SafeHorizontalSpeed = 2, SafeTiltDegrees = 12;

    // The rocket relative to the Moon (updated every physics step).
    public bool Landed { get; private set; }
    public bool RocketNear { get; private set; }            // inside the sphere of influence
    public double RocketAltitude { get; private set; }      // m, its base above the mean surface
    public double RocketVerticalSpeed { get; private set; } // m/s relative to the ground, + up
    public double RocketHorizontalSpeed { get; private set; }
    public double RocketLatitude { get; private set; }      // degrees, selenographic
    public double RocketLongitude { get; private set; }
    private float liftOffTime = -10;     // no touchdown again straight after lifting off
    private Vector3 landedOffset;        // world units, in the Moon's axes
    private Quaternion landedRotation;   // relative to the Moon's
    private RocketFlightModel flight;
    private RocketAssemblyController assembly;
    private GameObject patch;            // fine ground under the rocket
    private Vector3 patchCentre;         // object-space direction it's built around
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
        flight = rocket != null ? rocket.GetComponent<RocketFlightModel>() : null;
        assembly = rocket != null ? rocket.GetComponent<RocketAssemblyController>() : null;
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

    /// <summary>The Moon's centre relative to Earth's (m), in the scene's axes at Earth rotation angle 'rotation'.</summary>
    public static Double3 OffsetFromEarth(DateTime date, double rotation)
    {
        Position(date, out var dir, out var distance, out _, out _);
        return EclipticToSceneD(dir, rotation) * distance;
    }

    // Where the Moon is and how it's turned at a date (scene axes at that
    // date's Earth rotation): centre offset from Earth (m), orientation,
    // and its spin axis.
    private static void Pose(DateTime date, double rotation, out Double3 offset, out Quaternion orientation, out Vector3 spin)
    {
        Position(date, out var eclipticDir, out var distance, out var meanLongitude, out var node);
        offset = EclipticToSceneD(eclipticDir, rotation) * distance;
        // Tidal lock: near side (the mesh's +X, longitude 0 of the LRO map)
        // toward the MEAN Earth direction, spin axis per Cassini's laws.
        var nodeRad = node * Mathf.Deg2Rad;
        var tilt = CassiniTiltDeg * Mathf.Deg2Rad;
        var spinAxis = new Vector3d(-Math.Sin(tilt) * Math.Sin(nodeRad), Math.Sin(tilt) * Math.Cos(nodeRad), Math.Cos(tilt));
        var toMeanEarth = new Vector3d(-Math.Cos(meanLongitude), -Math.Sin(meanLongitude), 0);
        spin = EclipticToScene(spinAxis, rotation);
        var prime = Vector3.ProjectOnPlane(EclipticToScene(toMeanEarth, rotation), spin).normalized;
        orientation = Quaternion.LookRotation(Vector3.Cross(prime, spin), spin);
    }

    // Puts the Moon where it is at 'date', relative to Earth's centre in
    // double precision.
    private void Place(DateTime date)
    {
        Pose(date, SolarSystem.Instance.RotationAngleAt(date), out var offset, out var orientation, out _);
        transform.SetPositionAndRotation((FloatingOrigin.PlanetCentre + offset * PlanetBody.WorldUnitsPerMeter).ToVector3(), orientation);
    }

    /// <summary>
    /// Velocity (m/s, scene) of the Moon's ground at a world point: its
    /// centre's motion plus its turning - its own slow spin, and the whole
    /// scene turning with Earth beneath it (~127 m/s at the surface).
    /// </summary>
    private Vector3 GroundVelocity(Vector3 worldPoint, DateTime date)
    {
        var rotation = SolarSystem.Instance.RotationAngleAt(date);
        var centreVelocity = (OffsetFromEarth(date.AddSeconds(1), rotation + planet.RotationRate) - OffsetFromEarth(date.AddSeconds(-1), rotation - planet.RotationRate)) / 2;
        Pose(date, rotation, out _, out _, out var spin);
        // Angular velocities here follow PlanetBody.SpinVector's convention
        // (the scene's axes are mirrored from the ecliptic's, so a prograde
        // spin about the north axis points down it): the Moon's own spin,
        // less the scene's turning with Earth.
        var turning = -spin * (float)SpinRate - planet.SpinVector;
        var arm = (worldPoint - transform.position) / PlanetBody.WorldUnitsPerMeter;
        return centreVelocity.ToVector3() + Vector3.Cross(turning, arm);
    }

    private void LateUpdate()
    {
        var sol = SolarSystem.Instance;
        if (sol == null || planet == null) return;
        var date = sol.Date;
        var rotation = sol.RotationAngleRad;
        var centre = planet.transform.position;

        Position(date, out _, out var distance, out _, out _);
        DistanceMeters = distance;
        // Near the Moon, draw it at the moment the rocket is drawn at - it
        // moves ~28 km/s through the scene, so a frame's difference would
        // show the rocket hundreds of metres off the ground.
        Place(RocketNear && !Landed && rocketBody != null && !TimeWarp.OnRails ? sol.DateAt(RenderedPhysicsTime()) : date);
        var sceneDir = (transform.position - centre).normalized;
        if (Landed) Pin();
        UpdatePatch();

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
        var sol = SolarSystem.Instance;
        if (sol == null || planet == null) return;
        var date = sol.DateAt(Time.time);
        Place(date);
        if (rocket == null || rocketBody == null) return;
        if (!rocket.Launched || flight == null || flight.Crashed) { Landed = false; RocketNear = false; return; }

        // Where the rocket is relative to the Moon's ground.
        var u = PlanetBody.WorldUnitsPerMeter;
        var basePoint = rocket.transform.TransformPoint(Vector3.up * rocket.ActiveBaseLocalY);
        var toBase = basePoint - transform.position;
        var up = toBase.normalized;
        RocketAltitude = toBase.magnitude / u - RadiusMeters;
        RocketNear = (rocket.transform.position - transform.position).magnitude / u < SphereOfInfluence;
        var local = Quaternion.Inverse(transform.rotation) * up;
        RocketLatitude = Math.Asin(Mathf.Clamp(local.y, -1, 1)) * 180 / Math.PI;
        RocketLongitude = Math.Atan2(-local.z, local.x) * 180 / Math.PI;
        var ground = GroundVelocity(basePoint, date);
        var velocity = TimeWarp.TryGetRailsVelocity(out var rails) ? rails : rocketBody.linearVelocity / u;
        var relative = velocity - ground;
        RocketVerticalSpeed = Vector3.Dot(relative, up);
        RocketHorizontalSpeed = (relative - up * (float)RocketVerticalSpeed).magnitude;

        if (Landed) { Pin(); TryLiftOff(up, ground); return; }
        if (rocketBody.isKinematic) return;   // rails warp moves it

        // The Moon's pull on the rocket. The scene is centred on Earth, which
        // is itself falling toward the Moon, so what acts here is the
        // difference (the tide): GM·((r_m − r)/|r_m − r|³ − r_m/|r_m|³).
        var d = (Vector3d)((transform.position - rocketBody.worldCenterOfMass) / u);
        var rm = (Vector3d)((new Double3(transform.position) - FloatingOrigin.PlanetCentre).ToVector3() / u);
        var a = d * (GravitationalParameter / Math.Pow(d.Length, 3)) - rm * (GravitationalParameter / Math.Pow(rm.Length, 3));
        rocketBody.AddForce(a.ToVector3() * u, ForceMode.Acceleration);

        if (RocketAltitude <= 0 && Time.time - liftOffTime > 2f) Touchdown(basePoint, up, ground);
    }

    private void Touchdown(Vector3 basePoint, Vector3 up, Vector3 ground)
    {
        var surface = transform.position + up * (float)(RadiusMeters * PlanetBody.WorldUnitsPerMeter);
        var tilt = Vector3.Angle(rocket.transform.up, up);
        var down = -RocketVerticalSpeed;
        if (down > SafeVerticalSpeed || RocketHorizontalSpeed > SafeHorizontalSpeed || tilt > SafeTiltDegrees)
        {
            var why = down > SafeVerticalSpeed ? down.ToString("F1") + " m/s down" :
                      RocketHorizontalSpeed > SafeHorizontalSpeed ? RocketHorizontalSpeed.ToString("F1") + " m/s sideways" :
                      tilt.ToString("F0") + "° from upright";
            flight.Impact(surface, up, "Crashed on the Moon (" + why + ")");
            if (assembly != null) assembly.ReportMoon("CRASHED ON THE MOON", "Crashed on the Moon: " + why + " - the most a lander takes is " +
                SafeVerticalSpeed.ToString("F0") + " m/s down, " + SafeHorizontalSpeed.ToString("F0") + " m/s across, " + SafeTiltDegrees.ToString("F0") + "° tilt.");
            return;
        }
        // Standing on the ground: engines off, carried round with the Moon.
        rocket.transform.position += surface - basePoint;
        rocket.StopEngine();
        rocketBody.linearVelocity = Vector3.zero;
        rocketBody.angularVelocity = Vector3.zero;
        rocketBody.isKinematic = true;
        landedOffset = Quaternion.Inverse(transform.rotation) * (rocket.transform.position - transform.position);
        landedRotation = Quaternion.Inverse(transform.rotation) * rocket.transform.rotation;
        Landed = true;
        RocketAltitude = 0;
        if (assembly != null) assembly.ReportMoon("THE EAGLE HAS LANDED", "Landed on the Moon at " + Coordinates(RocketLatitude, RocketLongitude) +
            " - " + down.ToString("F1") + " m/s down, " + RocketHorizontalSpeed.ToString("F1") + " m/s across. Throttle up past lunar weight to lift off.");
    }

    // The time the rocket's rigidbody is drawn at this frame.
    private float RenderedPhysicsTime() =>
        rocketBody.interpolation == RigidbodyInterpolation.Interpolate ? Time.time - Time.fixedDeltaTime :
        rocketBody.interpolation == RigidbodyInterpolation.Extrapolate ? Time.time : Time.fixedTime;

    // Keeps a landed rocket on its spot as the Moon moves and turns.
    private void Pin()
    {
        var position = transform.position + transform.rotation * landedOffset;
        var orientation = transform.rotation * landedRotation;
        rocket.transform.SetPositionAndRotation(position, orientation);
        rocketBody.position = position;
        rocketBody.rotation = orientation;
    }

    // Lift off once the engines out-push the rocket's lunar weight.
    private void TryLiftOff(Vector3 up, Vector3 ground)
    {
        var weight = flight.TotalMass * SurfaceGravity;
        if (!(rocket.EngineEnabled || flight.SolidBurning) || flight.Thrust <= weight * 1.01) return;
        Landed = false;
        liftOffTime = Time.time;
        rocketBody.isKinematic = false;
        // Leaves with the ground's motion, just clear of it.
        rocket.transform.position += up * (.5f * PlanetBody.WorldUnitsPerMeter);
        rocketBody.position = rocket.transform.position;
        rocketBody.linearVelocity = (ground + up * .5f) * PlanetBody.WorldUnitsPerMeter;
        if (assembly != null) assembly.ReportMoon("LIFTOFF FROM THE MOON", "Lifted off from the Moon at " + Coordinates(RocketLatitude, RocketLongitude) + ".");
    }

    /// <summary>Releases a landed rocket (back to the assembly, or a new flight).</summary>
    public void ResetRocket() { Landed = false; RocketNear = false; }

    public static string Coordinates(double latitude, double longitude) =>
        Math.Abs(latitude).ToString("F2") + "° " + (latitude >= 0 ? "N" : "S") + ", " + Math.Abs(longitude).ToString("F2") + "° " + (longitude >= 0 ? "E" : "W");

    // --- Fine ground under the rocket ------------------------------------
    // The sphere is drawn from 128×64 facets, whose flat middles sit up to
    // ~500 m below the true surface - a landed rocket would seem to hover.
    // Within 150 km of the surface, a 240 km cap of ~1.9 km cells (sagging
    // < 0.3 m) on the true sphere is drawn under the rocket, rebuilt as it
    // travels; the coarse sphere stays below it everywhere.
    private const float PatchHalfAngleDeg = 4f;
    private const int PatchCells = 128;

    private void UpdatePatch()
    {
        var show = rocket != null && rocket.Launched && RocketNear && RocketAltitude < 150000;
        if (!show) { if (patch != null) patch.SetActive(false); return; }
        var direction = transform.InverseTransformDirection((rocket.transform.position - transform.position).normalized).normalized;
        if (patch == null)
        {
            patch = new GameObject("Moon Surface (near)", typeof(MeshFilter), typeof(MeshRenderer));
            patch.transform.SetParent(transform, false);
            var renderer = patch.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            patchCentre = Vector3.zero;
        }
        patch.SetActive(true);
        if (patchCentre != Vector3.zero && Vector3.Angle(patchCentre, direction) < PatchHalfAngleDeg * .5f) return;
        patchCentre = direction;
        var filter = patch.GetComponent<MeshFilter>();
        if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
        filter.sharedMesh = BuildPatch(direction);
    }

    // A gnomonic grid on the unit sphere (object space, radius 0.5) round
    // 'centre', with the sphere mesh's UVs (PlanetBody.BuildUvSphere).
    private static Mesh BuildPatch(Vector3 centre)
    {
        var e1 = Vector3.Cross(centre, Mathf.Abs(centre.y) < .9f ? Vector3.up : Vector3.right).normalized;
        var e2 = Vector3.Cross(centre, e1);
        var extent = Mathf.Tan(PatchHalfAngleDeg * Mathf.Deg2Rad);
        var n = PatchCells + 1;
        var vertices = new Vector3[n * n]; var normals = new Vector3[n * n]; var uvs = new Vector2[n * n];
        var centreU = Mathf.Repeat(Mathf.Atan2(centre.z, centre.x) / (2 * Mathf.PI), 1f) + .5f;
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                var a = (2f * i / PatchCells - 1) * extent; var b = (2f * j / PatchCells - 1) * extent;
                var dir = (centre + e1 * a + e2 * b).normalized;
                var k = j * n + i;
                vertices[k] = dir * .5f; normals[k] = dir;
                var u = Mathf.Repeat(Mathf.Atan2(dir.z, dir.x) / (2 * Mathf.PI), 1f) + .5f;
                u = centreU + Mathf.Repeat(u - centreU + .5f, 1f) - .5f;   // no seam across the patch
                uvs[k] = new Vector2(u, 1f - Mathf.Acos(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI);
            }
        var triangles = new int[PatchCells * PatchCells * 6];
        var t = 0;
        for (var j = 0; j < PatchCells; j++)
            for (var i = 0; i < PatchCells; i++)
            {
                var k = j * n + i;
                // Wound to face outward like the sphere's triangles.
                triangles[t++] = k; triangles[t++] = k + n; triangles[t++] = k + 1;
                triangles[t++] = k + 1; triangles[t++] = k + n; triangles[t++] = k + n + 1;
            }
        var mesh = new Mesh { name = "Moon surface patch", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
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

    // Ecliptic to the scene, in double precision (for positions).
    private static Double3 EclipticToSceneD(Vector3d v, double rotation)
    {
        var e = ObliquityDeg * Math.PI / 180;
        var y = v.y * Math.Cos(e) - v.z * Math.Sin(e);
        var z = v.y * Math.Sin(e) + v.z * Math.Cos(e);
        var lon = Math.Atan2(y, v.x) - rotation;
        var h = Math.Sqrt(v.x * v.x + y * y);
        return new Double3(h * Math.Cos(lon), z, h * Math.Sin(lon));
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
        if (LaunchMenu.Open) return;   // the launch menu covers the scene
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
