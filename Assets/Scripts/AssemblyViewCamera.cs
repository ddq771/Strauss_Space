using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Camera))]
public sealed class AssemblyViewCamera : MonoBehaviour
{
    [SerializeField] private Vector3 target = new Vector3(0f, 5f, 0f);
    [SerializeField] private float distance = 62f;
    [SerializeField] private float yaw = 35f;
    [SerializeField] private float pitch = 25f;
    [SerializeField] private PlanetBody planet;
    [SerializeField] private float zoomResponse = 5f;
    private float desiredDistance;
    private Renderer localGround;
    private Renderer assemblyPlatform;
    [SerializeField, HideInInspector] private List<Renderer> localSiteRenderers = new List<Renderer>();
    [SerializeField, HideInInspector] private bool localSiteHidden;
    private Vector3 flightFocusOffset;
    private bool zoomInitialized;

    public void SetPlanet(PlanetBody value) => planet = value;

    private void Start()
    {
        InitializeZoom();
        ApplyPose();
    }

    private void InitializeZoom()
    {
        if (zoomInitialized) return;
        desiredDistance = distance;
        var ground = GameObject.Find("Ground");
        if (ground != null) localGround = ground.GetComponent<Renderer>();
        var platform = GameObject.Find("Assembly Platform");
        if (platform != null) assemblyPlatform = platform.GetComponent<Renderer>();
        zoomInitialized = true;
    }

    private void LateUpdate()
    {
        InitializeZoom();
        // Also runs after a script reload while the obsolete scene is in Play Mode.
        if (planet == null && gameObject.scene.name == "RocketAssembly")
        {
            SceneManager.LoadScene("PlanetScene");
            return;
        }
        if (planet != null && Input.GetKeyDown(KeyCode.V))
            desiredDistance = desiredDistance > 1000000f ? 90f : 16000000f;
        if (Input.GetKeyDown(KeyCode.F))
        {
            desiredDistance = 90f;
            target = new Vector3(0,18,0);
            flightFocusOffset = Vector3.zero;
            yaw = 35f;
            pitch = 25f;
        }
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, -85f, 89f);
        }
        if (!RocketAssemblyController.IsPointerOverPanel())
        {
            var wheel=Input.mouseScrollDelta.y;
            desiredDistance = Mathf.Clamp(desiredDistance * Mathf.Exp(-wheel * 0.2f),
                12f, planet != null ? 20000000f : 160f);
            if (Input.GetMouseButton(2))
                PanFocus(new Vector2(Input.GetAxis("Mouse X"),Input.GetAxis("Mouse Y")));
        }
        // Interpolate in logarithmic space because the view spans metres to
        // planetary distances. This keeps both ends responsive without a jump.
        var blend = 1f-Mathf.Exp(-zoomResponse*Time.unscaledDeltaTime);
        distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(12f,distance)),
            Mathf.Log(Mathf.Max(12f,desiredDistance)),blend));
        if (Mathf.Abs(distance-desiredDistance) < Mathf.Max(.01f,desiredDistance*.0001f))
            distance=desiredDistance;
        var rocket=FindFirstObjectByType<Rocket>();
        if(rocket!=null && rocket.Launched && distance<1000000f && transform.parent!=null)
        {
            target=transform.parent.InverseTransformPoint(rocket.transform.position);
            target+=transform.parent.InverseTransformVector(
                rocket.transform.TransformVector(flightFocusOffset*PlanetBody.WorldUnitsPerMeter));
        }
        ApplyPose();
    }

    private void PanFocus(Vector2 mouseDelta)
    {
        if (mouseDelta.sqrMagnitude<.0001f || transform.parent==null) return;
        var metresPerInput=Mathf.Clamp(desiredDistance*.015f,.25f,30f);
        var worldDelta=(transform.right*mouseDelta.x+transform.up*mouseDelta.y)*
            (metresPerInput*PlanetBody.WorldUnitsPerMeter);
        target+=transform.parent.InverseTransformVector(worldDelta);
        var rocket=FindFirstObjectByType<Rocket>();
        if (rocket!=null && rocket.Launched)
        {
            flightFocusOffset+=rocket.transform.InverseTransformVector(worldDelta)/
                PlanetBody.WorldUnitsPerMeter;
            flightFocusOffset=Vector3.ClampMagnitude(flightFocusOffset,1000f);
        }
    }

    public void ApplyPose()
    {
        InitializeZoom();
        var orbitalBlend = planet != null ? Mathf.InverseLerp(2000000f, 16000000f, distance) : 0f;
        var rotation = Quaternion.Euler(Mathf.Lerp(pitch, 75f, orbitalBlend), yaw, 0f);
        var focus = target;
        var scale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        if (planet != null && transform.parent != null)
        {
            var center = transform.parent.InverseTransformPoint(planet.transform.position);
            focus = Vector3.Lerp(target, center, Mathf.InverseLerp(2000000f, 16000000f, distance));

            // distance is the camera's zoom/orbit distance from its focus
            // point (metres) - while tracking a launched rocket, the camera
            // stays zoomed in close to it, so distance alone stops
            // reflecting how far the view actually is from the ground once
            // the rocket climbs. Without this, a rocket at real space
            // altitude still renders with ground-level fog and a solid-color
            // sky, because the camera never zoomed itself out. Blend in the
            // rocket's own altitude so leaving the atmosphere reads as
            // leaving it even at a tight camera zoom.
            var groundProximity = distance;
            var trackedRocket = FindFirstObjectByType<Rocket>();
            if (trackedRocket != null && trackedRocket.Launched)
            {
                var flightModel = trackedRocket.GetComponent<RocketFlightModel>();
                if (flightModel != null)
                    groundProximity = Mathf.Max(groundProximity, (float)flightModel.Altitude);
            }

            RenderSettings.fog = groundProximity < 2000f;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.67f, .79f, .86f);
            RenderSettings.fogStartDistance = Mathf.Max(140f, groundProximity * 2f) * scale;
            RenderSettings.fogEndDistance = Mathf.Max(500f, groundProximity * 8f) * scale;
            var camera = GetComponent<Camera>();
            camera.clearFlags = groundProximity < 2000f ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

            // Far clip must reach whatever is actually visible: the ground
            // horizon from the current altitude while flying/orbiting close
            // in, or the whole planet once truly zoomed out to the orbital
            // view. The previous formula unconditionally floored it at
            // planet-scale (4x the radius) regardless of how close the
            // camera actually was, while the near clip stayed pinned to a
            // tiny value for close-up assembly work - together that produced
            // a near:far ratio in the hundreds of billions, far beyond what
            // a depth buffer can resolve. That read as glitchy, torn ground
            // once a tracked rocket climbed a few tens of km, and as the
            // planet disappearing outright by around 100 km, even though
            // nothing was actually being clipped out of view.
            var horizonMeters = Mathf.Sqrt(Mathf.Max(0f,
                (planet.Radius + groundProximity) * (planet.Radius + groundProximity) -
                planet.Radius * planet.Radius));
            var neededFarMeters = Mathf.Max(distance * 3f, (groundProximity + horizonMeters) * 1.5f);
            camera.farClipPlane = Mathf.Min(neededFarMeters, planet.Radius * 4f) * scale;
            camera.nearClipPlane = Mathf.Max(
                Mathf.Clamp(distance * .00001f, .05f, 20000f) * scale,
                camera.farClipPlane * .00001f);
        }
        // The curved local terrain is only a precision-friendly site patch.
        // Hide it before its finite circular edge can enter the frame; at this
        // distance the full planet surface is visually coincident with it.
        var undersideInspection = pitch < 0f && distance < 3000f;
        SetLocalSiteVisible(distance < 12000f);
        if (!localSiteHidden)
        {
            if (localGround != null) localGround.enabled = !undersideInspection;
            if (assemblyPlatform != null) assemblyPlatform.enabled = !undersideInspection;
        }
        transform.localPosition = focus - rotation * Vector3.forward * distance;
        transform.localRotation = rotation;
        if (planet != null && orbitalBlend > 0f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(planet.transform.position-transform.position, Vector3.up), orbitalBlend);
    }

    private void SetLocalSiteVisible(bool visible)
    {
        if (visible && !localSiteHidden) return;
        if (!visible)
        {
            var frame=GameObject.Find("Kenya Surface Frame");
            if (frame!=null)
                foreach (var renderer in frame.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled)
                    {
                        localSiteRenderers.Add(renderer);
                        renderer.enabled=false;
                    }
            localSiteHidden=true;
            return;
        }
        foreach (var renderer in localSiteRenderers)
            if (renderer!=null) renderer.enabled=true;
        localSiteRenderers.Clear();
        localSiteHidden=false;
    }

    public void ShowPlanet()
    {
        if (planet == null) return;
        distance = 16000000f;
        desiredDistance = distance;
        ApplyPose();
    }

    public void ShowAssembly()
    {
        target = new Vector3(0,18,0);
        flightFocusOffset=Vector3.zero;
        distance = 90f;
        desiredDistance = distance;
        ApplyPose();
    }

    public void FocusAssemblyPart(Vector3 worldPosition, float spanMeters)
    {
        target = transform.parent != null ? transform.parent.InverseTransformPoint(worldPosition) : worldPosition;
        distance = Mathf.Clamp(spanMeters * 2.6f, 12f, 3000f);
        desiredDistance = distance;
        ApplyPose();
    }

    private void OnGUI()
    {
        var heading = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        heading.normal.textColor = Color.white;
        GUI.Label(new Rect(24, 18, 520, 36), "KENYA  /  ROCKET ASSEMBLY", heading);
        GUI.Label(new Rect(24, 53, 980, 25), "Right drag: orbit   •   Scroll: zoom   •   Hold mouse wheel: move focus freely   •   V: Earth / site   •   F: assembly view");
        if (planet != null)
        {
            if (GUI.Button(new Rect(24, 86, 145, 32), "Earth / Planet")) desiredDistance=16000000f;
            if (GUI.Button(new Rect(181, 86, 145, 32), "Assembly site"))
            {
                target=new Vector3(0,18,0);
                flightFocusOffset=Vector3.zero;
                desiredDistance=90f;
            }
        }
    }
}
