using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class PlanetBody : MonoBehaviour
{
    [Min(0.01f)]
    [SerializeField] private float radius = 5f;

    [Min(0.001f)]
    [SerializeField] private float mass = 1000f;

    public float Radius => radius;
    public float Mass => mass;

    private SphereCollider sphereCollider;
    private Rigidbody body;

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
