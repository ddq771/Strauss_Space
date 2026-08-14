using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class RocketCameraController : MonoBehaviour
{
    [Header("Zoom in kilometres")]
    [SerializeField] private float distanceKm = 0.08f;
    [SerializeField] private float minimumDistanceKm = 0.035f;
    [SerializeField] private float localViewLimitKm = 50f;
    [SerializeField] private float scrollSensitivity = 0.18f;

    private Camera localCamera;
    private Camera worldCamera;
    private PlanetBody planet;
    private Rocket rocket;

    private void Awake()
    {
        localCamera = GetComponent<Camera>();
        planet = FindFirstObjectByType<PlanetBody>();
        rocket = FindFirstObjectByType<Rocket>();
        worldCamera = FindWorldCamera();
    }

    private void Start()
    {
        SwitchToLocalView();
    }

    private void Update()
    {
        if (planet == null || rocket == null)
        {
            return;
        }

        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f)
        {
            scroll = Input.GetAxis("Mouse ScrollWheel") * 10f;
        }
        if (Mathf.Abs(scroll) > 0.001f)
        {
            if (scroll > 0f && !localCamera.enabled)
            {
                SwitchToLocalView();
            }
            else if (localCamera.enabled)
            {
                distanceKm = Mathf.Clamp(
                    distanceKm * Mathf.Pow(1f - scrollSensitivity, scroll),
                    minimumDistanceKm,
                    localViewLimitKm * 1.1f);

                if (distanceKm > localViewLimitKm)
                {
                    SwitchToWorldView();
                }
            }
        }

        if (localCamera.enabled)
        {
            UpdateLocalCamera();
        }
    }

    public void SwitchToLocalView()
    {
        if (planet == null || rocket == null)
        {
            return;
        }

        worldCamera ??= FindWorldCamera();
        if (worldCamera != null)
        {
            worldCamera.enabled = false;
            worldCamera.tag = "Untagged";
        }

        localCamera.enabled = true;
        localCamera.tag = "MainCamera";
        UpdateLocalCamera();
    }

    public void SwitchToWorldView()
    {
        worldCamera ??= FindWorldCamera();
        if (worldCamera == null || planet == null)
        {
            return;
        }

        localCamera.enabled = false;
        localCamera.tag = "Untagged";
        worldCamera.enabled = true;
        worldCamera.tag = "MainCamera";
        worldCamera.fieldOfView = 45f;
        worldCamera.nearClipPlane = 1f;
        worldCamera.farClipPlane = 100_000f;
        worldCamera.transform.position = planet.transform.position + Vector3.back *
                                         (planet.Radius * PlanetBody.WorldUnitsPerMeter * 2.5f);
        worldCamera.transform.LookAt(planet.transform.position);
    }

    private void UpdateLocalCamera()
    {
        var outward = (rocket.transform.position - planet.transform.position).normalized;
        if (outward.sqrMagnitude < 0.001f)
        {
            outward = Vector3.up;
        }

        var east = Vector3.Cross(Vector3.up, outward).normalized;
        var offsetDirection = (east * 0.85f + outward * 0.5f).normalized;
        var target = rocket.transform.position + outward * 0.005f;
        var cameraPosition = rocket.transform.position +
                             offsetDirection * (distanceKm * 0.001f);

        localCamera.transform.position = cameraPosition;
        localCamera.transform.rotation = Quaternion.LookRotation(
            target - cameraPosition,
            outward);
        localCamera.fieldOfView = 55f;
        localCamera.nearClipPlane = 0.001f;
        localCamera.farClipPlane = 100f;
    }

    private static Camera FindWorldCamera()
    {
        var worldObject = GameObject.Find("World Camera");
        return worldObject == null ? null : worldObject.GetComponent<Camera>();
    }
}
