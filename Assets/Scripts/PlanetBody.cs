using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlanetBody : MonoBehaviour
{
    // Unity units are treated as metres and Rigidbody mass as kilograms.
    public const double UniversalGravitationalConstant = 6.67430e-11;

    [Min(0.01f)]
    [SerializeField] private float radius = 5f;

    [Min(0.001f)]
    [SerializeField] private float mass = 1000f;

    [Header("Gravity")]
    [SerializeField] private bool gravityEnabled = true;

    [Tooltip("Maximum distance at which this planet applies gravity. Set to 0 for no cutoff.")]
    [Min(0f)]
    [SerializeField] private float gravityInfluenceRadius = 100f;

    [SerializeField] private LayerMask affectedLayers = ~0;

    public float Radius => radius;
    public float Mass => mass;
    public double Gravity => UniversalGravitationalConstant * mass / (radius * radius);
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

        var searchRadius = gravityInfluenceRadius > 0f ? gravityInfluenceRadius : float.MaxValue;
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
        var distance = Mathf.Max(offsetToCenter.magnitude, radius);
        var acceleration = UniversalGravitationalConstant * mass / (distance * distance);
        return offsetToCenter.normalized * (float)acceleration;
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
        transform.localScale = Vector3.one * (radius * 2f);
        body.mass = mass;
    }
}
