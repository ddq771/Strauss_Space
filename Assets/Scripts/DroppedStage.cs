using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spent stage (or strap-on booster, SRB...) after separation: it keeps
/// the vehicle's velocity and spin at the moment it was let go, plus a small
/// separation push, then falls freely - gravity, the rotating-frame forces
/// of the Earth-fixed scene and a crude tumbling drag - until it hits the
/// ground or has been falling for half an hour, then it's removed.
/// It has no collider, so it can't knock into the vehicle it came off.
/// </summary>
public sealed class DroppedStage : MonoBehaviour
{
    private const float Lifetime = 1800f;   // simulated seconds
    private PlanetBody planet;
    private Rigidbody body;
    private float age;
    private float area;                     // m², tumbling cross-section
    public static readonly List<DroppedStage> All = new();

    /// <summary>Detaches 'parts' (keeping their world pose) into a new falling object.</summary>
    public static DroppedStage Create(string name, IEnumerable<Transform> parts, float mass, Rigidbody from, Vector3 separationVelocity, Vector3 extraSpin = default)
    {
        var holder = new GameObject(name);
        holder.transform.SetPositionAndRotation(from.worldCenterOfMass, from.rotation);
        var bounds = new Bounds(); var first = true;
        foreach (var part in parts)
        {
            if (part == null) continue;
            part.SetParent(holder.transform, true);
            foreach (var r in part.GetComponentsInChildren<Renderer>())
            { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
        }
        var dropped = holder.AddComponent<DroppedStage>();
        var body = holder.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = Mathf.Max(1f, mass);
        body.linearDamping = 0f; body.angularDamping = 0f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = from.linearVelocity + separationVelocity * PlanetBody.WorldUnitsPerMeter;
        body.angularVelocity = from.angularVelocity + extraSpin;
        // No collider to derive inertia from: a long cylinder-ish estimate.
        var length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        body.inertiaTensor = Vector3.one * Mathf.Max(1e-6f, body.mass * length * length / 12f);
        body.inertiaTensorRotation = Quaternion.identity;
        dropped.body = body;
        var size = bounds.size / PlanetBody.WorldUnitsPerMeter;
        dropped.area = Mathf.Max(1f, size.x * size.y * .5f);
        // Spent stages heat up too - and burn up if they come in fast enough.
        ReentryHeating.AttachToDebris(dropped, body, FindFirstObjectByType<PlanetBody>(),
            Mathf.Max(1f, Mathf.Max(size.x, size.y, size.z)), Mathf.Max(.5f, Mathf.Min(size.x, size.y, size.z)));
        All.Add(dropped);
        return dropped;
    }

    public static void ClearAll()
    {
        foreach (var d in All) if (d != null) Destroy(d.gameObject);
        All.Clear();
    }

    private void Awake() => planet = FindFirstObjectByType<PlanetBody>();
    private void OnDestroy() => All.Remove(this);

    private void FixedUpdate()
    {
        if (planet == null || body == null) return;
        age += Time.fixedDeltaTime * TimeWarp.ClockMultiplier;
        var centre = planet.transform.position;
        var u = PlanetBody.WorldUnitsPerMeter;
        var r = (body.worldCenterOfMass - centre) / u;
        var altitude = r.magnitude - planet.Radius;
        if (altitude < 0 || age > Lifetime) { Destroy(gameObject); return; }

        // Gravity (world units already) and the rotating frame's Coriolis /
        // centrifugal terms, as for the rocket.
        body.AddForce(planet.GetGravityAcceleration(body.worldCenterOfMass), ForceMode.Acceleration);
        var spin = planet.SpinVector;
        var v = body.linearVelocity / u;
        var frame = -2f * Vector3.Cross(spin, v) - Vector3.Cross(spin, Vector3.Cross(spin, r));
        body.AddForce(frame * u, ForceMode.Acceleration);

        // Tumbling drag (Cd ~1 on the side area) - a spent stage is no arrow.
        var speed = v.magnitude;
        if (speed > .1f)
        {
            var temperature = StandardAtmosphere.TemperatureKelvin(altitude);
            var density = StandardAtmosphere.Density(StandardAtmosphere.Pressure(altitude), temperature);
            var drag = (float)(.5 * density * speed * speed * area);
            body.AddForce(-v / speed * drag * u, ForceMode.Force);
        }
    }
}
