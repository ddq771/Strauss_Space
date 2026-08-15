using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class RocketCameraController : MonoBehaviour
{
    [Header("Zoom in kilometres")]
    [SerializeField] private float distanceKm = 0.08f;
    [SerializeField] private float minimumDistanceKm = 0.035f;
    [SerializeField] private float localViewLimitKm = 50f;
    [SerializeField] private float planetaryViewDistanceKm = 16_000f;
    [SerializeField] private float scrollSensitivity = 0.18f;
    [SerializeField] private KeyCode toggleViewKey = KeyCode.V;

    private Camera localCamera;
    private Camera worldCamera;
    private PlanetBody planet;
    private Rocket rocket;
    private bool alignLocalHorizonOnNextLocalView;

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

        if (Input.GetKeyDown(toggleViewKey))
        {
            ToggleRocketPlanetView();
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
                    planetaryViewDistanceKm);

                if (distanceKm >= planetaryViewDistanceKm)
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
        localCamera.clearFlags = CameraClearFlags.SolidColor;
        localCamera.backgroundColor = new Color(0.32f, 0.55f, 0.82f, 1f);
        alignLocalHorizonOnNextLocalView = true;
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
        worldCamera.clearFlags = CameraClearFlags.Skybox;
        GetPlanetaryCameraPose(out var position, out var rotation);
        worldCamera.transform.SetPositionAndRotation(position, rotation);
        worldCamera.fieldOfView = 45f;
        worldCamera.nearClipPlane = 1f;
        worldCamera.farClipPlane = 100_000f;
    }

    /// <summary>
    /// Jumps between the close rocket view and the full planetary view.
    /// This method can also be assigned directly to a Unity UI Button.
    /// </summary>
    public void ToggleRocketPlanetView()
    {
        if (localCamera != null && localCamera.enabled)
        {
            distanceKm = planetaryViewDistanceKm;
            SwitchToWorldView();
        }
        else
        {
            distanceKm = Mathf.Clamp(0.08f, minimumDistanceKm, localViewLimitKm);
            SwitchToLocalView();
        }
    }

    private void UpdateLocalCamera()
    {
        var outward = (rocket.transform.position - planet.transform.position).normalized;
        if (outward.sqrMagnitude < 0.001f)
        {
            outward = Vector3.up;
        }

        var east = Vector3.Cross(Vector3.up, outward);
        if (east.sqrMagnitude < 0.000001f)
        {
            east = Vector3.Cross(Vector3.forward, outward);
        }
        east.Normalize();

        // At close range the camera sits beside the rocket, not above it.
        // This produces a normal landscape view with the rocket centered.
        var closeDistanceKm = Mathf.Min(distanceKm, localViewLimitKm);
        var localPosition = rocket.transform.position +
                            east * closeDistanceKm;
        var localRotation = Quaternion.LookRotation(
            -east,
            outward);

        var transition = Mathf.InverseLerp(
            localViewLimitKm,
            planetaryViewDistanceKm,
            distanceKm);

        // In the ground/landscape range, always rebuild the rotation from
        // the local Earth frame. This guarantees a level horizon every time
        // local view is entered, even after switching cameras or zooming.
        if (transition <= 0.0001f)
        {
            localCamera.transform.position = localPosition;
            if (alignLocalHorizonOnNextLocalView)
            {
                localCamera.transform.rotation = localRotation;
                alignLocalHorizonOnNextLocalView = false;
            }
            localCamera.fieldOfView = 55f;
            localCamera.nearClipPlane = 0.001f;
            localCamera.farClipPlane = 100_000f;
            return;
        }

        var planetaryPosition = GetPlanetaryCameraPosition();
        var planetaryRotation = Quaternion.LookRotation(
            planet.transform.position - planetaryPosition,
            Vector3.up);

        var cameraPosition = Vector3.Lerp(localPosition, planetaryPosition, transition);
        var cameraRotation = Quaternion.Slerp(localRotation, planetaryRotation, transition);
        localCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        localCamera.fieldOfView = 55f;
        localCamera.nearClipPlane = 0.001f;
        localCamera.farClipPlane = 100_000f;
    }

    private Vector3 GetPlanetaryCameraPosition()
    {
        var outward = (rocket.transform.position - planet.transform.position).normalized;
        var east = Vector3.Cross(Vector3.up, outward);
        if (east.sqrMagnitude < 0.000001f)
        {
            east = Vector3.Cross(Vector3.forward, outward);
        }

        var viewDirection = (outward + east.normalized * 0.8f).normalized;
        return planet.transform.position +
               viewDirection * planetaryViewDistanceKm;
    }

    private void GetPlanetaryCameraPose(out Vector3 position, out Quaternion rotation)
    {
        position = GetPlanetaryCameraPosition();
        rotation = Quaternion.LookRotation(
            planet.transform.position - position,
            Vector3.up);
    }

    private static Camera FindWorldCamera()
    {
        var worldObject = GameObject.Find("World Camera");
        return worldObject == null ? null : worldObject.GetComponent<Camera>();
    }
}
