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

    [Header("Mesh")]
    [Tooltip("Unity's built-in sphere primitive is a fixed, visibly faceted low-poly mesh. " +
             "This generates a smoother UV sphere instead, so the surface still reads as " +
             "round up close (e.g. the Kenya launch camera) and not just from orbit.")]
    [Range(8, 256)]
    [SerializeField] private int longitudeSegments = 96;

    [Range(4, 128)]
    [SerializeField] private int latitudeSegments = 48;

    public float Radius => radius;
    public float Mass => mass;
    public double Gravity => UniversalGravitationalConstant * mass / ((double)radius * radius);
    public double SurfaceGravity => Gravity;

    public Vector3 GetSurfaceNormal(float latitudeDegrees, float longitudeDegrees)
    {
        var latitude = latitudeDegrees * Mathf.Deg2Rad;
        var longitude = longitudeDegrees * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(latitude) * Mathf.Cos(longitude),
            Mathf.Sin(latitude),
            Mathf.Cos(latitude) * Mathf.Sin(longitude)).normalized;
    }

    public Vector3 GetSurfacePosition(
        float latitudeDegrees,
        float longitudeDegrees,
        float clearanceMeters = 0f)
    {
        var normal = GetSurfaceNormal(latitudeDegrees, longitudeDegrees);
        var distance = (radius + Mathf.Max(0f, clearanceMeters)) * WorldUnitsPerMeter;
        return transform.position + normal * distance;
    }

    private SphereCollider sphereCollider;
    private Rigidbody body;
    private MeshFilter meshFilter;
    private Mesh generatedMesh;
    private int generatedLongitudeSegments = -1;
    private int generatedLatitudeSegments = -1;
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
        body.useGravity = false;
        // Earth is the fixed reference body for this simulation. Its custom
        // gravity is calculated below; PhysX must not try to move a dynamic
        // Earth with a 5.97e24 kg mass, which can produce invalid AABBs.
        body.isKinematic = true;
        // The planet is represented visually and by custom gravity. A
        // 12,756-km PhysX collider is not needed for the launch scene and can
        // destabilize broad-phase bounds at this scale. The launch platform
        // supplies the physical surface for the rocket.
        sphereCollider.enabled = false;
        body.detectCollisions = false;
        // Do not assign the planet's real-world mass (up to ~6e24 kg) to the
        // PhysX Rigidbody: with no active collider to derive mass properties
        // from, PhysX's automatic center-of-mass/inertia-tensor computation
        // produces NaN, which surfaces as "Expanding invalid MinMaxAABB"
        // spam. This field is unused elsewhere; gravity uses the `mass`
        // field directly via GetGravityAcceleration.
        body.automaticCenterOfMass = false;
        body.automaticInertiaTensor = false;
        body.centerOfMass = Vector3.zero;
        body.inertiaTensor = Vector3.one;

        ApplySmoothMesh();
    }

    private void ApplySmoothMesh()
    {
        meshFilter ??= GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            return;
        }

        longitudeSegments = Mathf.Clamp(longitudeSegments, 8, 256);
        latitudeSegments = Mathf.Clamp(latitudeSegments, 4, 128);

        if (generatedMesh != null &&
            generatedLongitudeSegments == longitudeSegments &&
            generatedLatitudeSegments == latitudeSegments)
        {
            return;
        }

        generatedMesh = BuildUvSphere(longitudeSegments, latitudeSegments, radius: 0.5f);
        generatedMesh.name = $"PlanetSphere_{longitudeSegments}x{latitudeSegments}";
        generatedLongitudeSegments = longitudeSegments;
        generatedLatitudeSegments = latitudeSegments;
        meshFilter.sharedMesh = generatedMesh;
    }

    /// <summary>
    /// Builds an outward-facing UV sphere with Greenwich at texture U=0.5,
    /// matching GetSurfaceNormal's longitude convention. Unity's built-in
    /// sphere is a fixed, coarse mesh that shows visible flat facets once the
    /// planet is large and the camera gets close to the surface; this lets the
    /// resolution scale with how round the planet needs to look.
    /// </summary>
    private static Mesh BuildUvSphere(int longitudeSegments, int latitudeSegments, float radius)
    {
        var mesh = new Mesh();
        var vertexCount = (longitudeSegments + 1) * (latitudeSegments + 1);
        mesh.indexFormat = vertexCount > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        var vertices = new List<Vector3>(vertexCount);
        var normals = new List<Vector3>(vertexCount);
        var uvs = new List<Vector2>(vertexCount);

        for (var lat = 0; lat <= latitudeSegments; lat++)
        {
            var v = (float)lat / latitudeSegments;
            var theta = v * Mathf.PI; // 0 at the north pole, PI at the south pole.
            var sinTheta = Mathf.Sin(theta);
            var cosTheta = Mathf.Cos(theta);

            for (var lon = 0; lon <= longitudeSegments; lon++)
            {
                var u = (float)lon / longitudeSegments;
                var phi = u * Mathf.PI * 2f;
                var sinPhi = Mathf.Sin(phi);
                var cosPhi = Mathf.Cos(phi);

                var normal = new Vector3(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
                vertices.Add(normal * radius);
                normals.Add(normal);
                uvs.Add(new Vector2(u + .5f, 1f - v));
            }
        }

        var triangles = new List<int>(longitudeSegments * latitudeSegments * 6);
        var stride = longitudeSegments + 1;
        for (var lat = 0; lat < latitudeSegments; lat++)
        {
            for (var lon = 0; lon < longitudeSegments; lon++)
            {
                var a = lat * stride + lon;
                var b = a + stride;

                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(b);

                triangles.Add(a + 1);
                triangles.Add(b + 1);
                triangles.Add(b);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
