using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class Rocket : MonoBehaviour
{
    private const float StandardGravity = 9.80665f;

    [Header("Mass (kg)")]
    [Min(0.001f)]
    [SerializeField] private float dryMass = 10_000f;

    [Min(0f)]
    [SerializeField] private float fuelMass = 20_000f;

    [Header("Engine")]
    [Tooltip("Maximum engine thrust in newtons.")]
    [Min(0f)]
    [SerializeField] private float maxThrust = 500_000f;

    [Tooltip("Engine efficiency, measured in seconds.")]
    [Min(0.01f)]
    [SerializeField] private float specificImpulse = 300f;

    [Range(0f, 1f)]
    [SerializeField] private float throttle;

    [SerializeField] private bool engineEnabled;

    public float DryMass => dryMass;
    public float FuelMass => fuelMass;
    public float CurrentMass => dryMass + fuelMass;
    public float MaxThrust => maxThrust;
    public float SpecificImpulse => specificImpulse;
    public float Throttle => throttle;
    public bool EngineEnabled => engineEnabled;

    private Rigidbody body;

    private void Reset()
    {
        ApplyMass();
    }

    private void Awake()
    {
        ApplyMass();
    }

    private void OnValidate()
    {
        ApplyMass();
    }

    private void FixedUpdate()
    {
        if (!engineEnabled || throttle <= 0f || fuelMass <= 0f)
        {
            return;
        }

        var thrust = maxThrust * throttle;
        body.AddForce(transform.up * thrust, ForceMode.Force);

        // mdot = F / (Isp * g0), using SI units: kg/s.
        var fuelUsed = thrust / (specificImpulse * StandardGravity) * Time.fixedDeltaTime;
        fuelMass = Mathf.Max(0f, fuelMass - fuelUsed);
        ApplyMass();

        if (fuelMass <= 0f)
        {
            engineEnabled = false;
        }
    }

    public void StartEngine(float requestedThrottle = 1f)
    {
        throttle = Mathf.Clamp01(requestedThrottle);
        engineEnabled = throttle > 0f && fuelMass > 0f;
    }

    public void StopEngine()
    {
        engineEnabled = false;
        throttle = 0f;
    }

    public void SetThrottle(float requestedThrottle)
    {
        throttle = Mathf.Clamp01(requestedThrottle);
        if (throttle <= 0f)
        {
            engineEnabled = false;
        }
    }

    /// <summary>
    /// Places the rocket just above a PlanetBody surface and points it away
    /// from the planet center. The planet radius is treated as metres.
    /// </summary>
    public void PlaceOnPlanetSurface(PlanetBody planet, float clearance = 1f)
    {
        var outward = (transform.position - planet.transform.position).normalized;
        if (outward.sqrMagnitude < 0.001f)
        {
            outward = Vector3.up;
        }

        transform.position = planet.transform.position + outward * (planet.Radius + clearance);
        transform.rotation = Quaternion.FromToRotation(Vector3.up, outward);
    }

    private void ApplyMass()
    {
        body ??= GetComponent<Rigidbody>();
        dryMass = Mathf.Max(0.001f, dryMass);
        fuelMass = Mathf.Max(0f, fuelMass);
        maxThrust = Mathf.Max(0f, maxThrust);
        specificImpulse = Mathf.Max(0.01f, specificImpulse);
        throttle = Mathf.Clamp01(throttle);
        body.mass = CurrentMass;
    }
}
