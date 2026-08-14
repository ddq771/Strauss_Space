using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlanetBody : MonoBehaviour
{
    // Physical properties remain SI units. The scene uses kilometres so
    // Earth-sized objects remain inside Unity physics' stable coordinate range.
    public const float WorldUnitsPerMeter = 0.001f;
    public const double UniversalGravitationalConstant = 6.67430e-11;

    [Min(0.01f)]
    [SerializeField] private float radius = 6_378_100f;

    [Min(0.001f)]
    [SerializeField] private float mass = 5.9722e24f;

    [Header("Gravity")]
    [SerializeField] private bool gravityEnabled = true;

    [Tooltip("Maximum distance at which this planet applies gravity. Set to 0 for no cutoff.")]
    [Min(0f)]
    [SerializeField] private float gravityInfluenceRadius = 10_000_000f;

    [SerializeField] private LayerMask affectedLayers = ~0;

    public float Radius => radius;
    public float Mass => mass;
    public double Gravity => UniversalGravitationalConstant * mass / ((double)radius * radius);
    public double SurfaceGravity => Gravity;

    private SphereCollider sphereCollider;
    private Rigidbody body;
    private readonly HashSet<Rigidbody> affectedBodies = new();

    private void Reset()
    {
        ApplyPhysicsProperties();
    }

    private void Awake()
    {
        ApplyPhysicsProperties();
    }

    private void OnValidate()
    {
        ApplyPhysicsProperties();
    }

    private void FixedUpdate()
    {
        if (!gravityEnabled)
        {
            return;
        }

        var searchRadius = gravityInfluenceRadius > 0f
            ? gravityInfluenceRadius * WorldUnitsPerMeter
            : float.MaxValue;
        var colliders = Physics.OverlapSphere(
            transform.position,
            searchRadius,
            affectedLayers,
            QueryTriggerInteraction.Ignore);

        affectedBodies.Clear();
        foreach (var collider in colliders)
        {
            var otherBody = collider.attachedRigidbody;
            if (otherBody == null || otherBody == body || otherBody.isKinematic || !affectedBodies.Add(otherBody))
            {
                continue;
            }

            otherBody.AddForce(
                GetGravityAcceleration(otherBody.worldCenterOfMass),
                ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// Calculates Newtonian gravitational acceleration toward this planet:
    /// a = G * M / r^2.
    /// </summary>
    public Vector3 GetGravityAcceleration(Vector3 worldPosition)
    {
        var offsetToCenter = transform.position - worldPosition;
        if (!IsFinite(offsetToCenter))
        {
            return Vector3.zero;
        }

        var distanceInMeters = System.Math.Max(
            (double)offsetToCenter.magnitude / WorldUnitsPerMeter,
            radius);
        var accelerationInMeters = UniversalGravitationalConstant * mass /
                                   (distanceInMeters * distanceInMeters);
        var accelerationInWorldUnits = accelerationInMeters * WorldUnitsPerMeter;
        var direction = offsetToCenter.sqrMagnitude > 0f ? offsetToCenter.normalized : Vector3.zero;
        return direction * (float)accelerationInWorldUnits;
    }

    private void ApplyPhysicsProperties()
    {
        sphereCollider ??= GetComponent<SphereCollider>();
        body ??= GetComponent<Rigidbody>();

        radius = Mathf.Max(0.01f, radius);
        mass = Mathf.Max(0.001f, mass);

        // Unity's primitive sphere has a local radius of 0.5. Scaling the
        // object keeps the visible mesh and the collider at the same radius.
        sphereCollider.radius = 0.5f;
        transform.localScale = Vector3.one * (radius * 2f * WorldUnitsPerMeter);
        body.mass = mass;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
