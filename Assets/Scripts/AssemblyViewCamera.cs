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
    // The surface frame the camera's view is defined in ("Kenya Surface
    // Frame", 0.001-scaled, so its units are metres): its pitch, yaw and
    // focus are all relative to it. The camera used to be its child, but a
    // child's position is stored in its parent's space - metres from the
    // launch site, millions of them once in orbit, where a float resolves
    // only ~0.5 m - and even a world-space placement was rounded to that,
    // which shook the view. So the camera is taken out of the hierarchy
    // (DetachFromFrame) and keeps the frame here: its pose is worked out in
    // the frame's space as before, then set in world space, near the origin
    // where it's precise.
    private Transform frame;
    // While following a launched rocket: the world-space point the camera
    // looks at (its centre of mass plus any panning), set each frame.
    private bool followingFlight;
    private bool rocketLaunched;   // set in ApplyPose: the map may centre on the Moon only in flight
    private Vector3 flightFocusWorld;
    private bool zoomInitialized;
    private Rocket trackedRocket;
    private GUIStyle rocketMarkerStyle;
    // Map view (zoomed out to the whole planet) orbits Earth's centre with
    // its own angles, in the planet's frame: mapPitch is latitude-like
    // (-89..89), mapYaw longitude-like. Kept separate from the close-up
    // yaw/pitch, which are relative to the launch site's local frame.
    private float mapYaw, mapPitch;
    private float previousOrbitalBlend;
    private const float MapStart = 2000000f, MapFull = 16000000f;
    // Zoomed out further still, the view hands over from Earth to the Sun:
    // the solar system view, where Earth's orbit (1 AU) fits on screen.
    private const float SolarStart = 2e9f, SolarFull = 1.5e11f, MaxDistance = 6e11f;

    public void SetPlanet(PlanetBody value) => planet = value;

    // Unity's built-in mouse events (OnMouseDown, OnMouseEnter...) cast a ray
    // from the mouse through every camera each frame. Nothing here uses them
    // - parts are picked by RocketAssemblyController.SelectAtRay - and with
    // this scene's clip planes (metres on the pad, hundreds of millions of
    // km in the solar system view) that ray maths breaks down and floods the
    // console with "Screen position out of view frustum". An event mask of
    // 0 takes each camera out of that pass entirely.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void DisableMouseEvents()
    {
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            camera.eventMask = 0;
    }

    // Called first thing (Awake): remembers the scene parent as the frame and
    // unparents the camera, keeping its world pose. Root objects are also
    // moved by FloatingOrigin, like the frame, so the two stay in step.
    private void DetachFromFrame()
    {
        if (frame != null || transform.parent == null) return;
        frame = transform.parent;
        transform.SetParent(null, true);
    }

    private void Awake() => DetachFromFrame();

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
        // Automated editor checks: keystrokes typed elsewhere mustn't move the camera.
        var acceptInput = !RocketAssemblyController.IgnorePilotInput;
        if (acceptInput && planet != null && Input.GetKeyDown(KeyCode.V))
            desiredDistance = desiredDistance > 1000000f ? 90f : 16000000f;
        if (acceptInput && Input.GetKeyDown(KeyCode.F))
        {
            desiredDistance = 90f;
            target = new Vector3(0,18,0);
            flightFocusOffset = Vector3.zero;
            yaw = 35f;
            pitch = 25f;
        }
        var inMap = OrbitalBlend(distance) > .5f;
        if (acceptInput && Input.GetMouseButton(1))
        {
            if (inMap)
            {
                mapYaw += Input.GetAxis("Mouse X") * 3f;
                mapPitch = Mathf.Clamp(mapPitch - Input.GetAxis("Mouse Y") * 3f, -89f, 89f);
            }
            else
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, -85f, 89f);
            }
        }
        if (acceptInput && !RocketAssemblyController.IsPointerOverPanel())
        {
            var wheel=Input.mouseScrollDelta.y;
            desiredDistance = Mathf.Clamp(desiredDistance * Mathf.Exp(-wheel * 0.2f),
                12f, planet != null ? MaxDistance : 160f);
            if (Input.GetMouseButton(2) && !inMap)
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
        if(rocket!=null && rocket.Launched && distance<1000000f && frame!=null)
        {
            // Follow the vehicle's centre of mass - the point it really turns
            // about - plus any panning the pilot has added (flightFocusOffset,
            // metres in the rocket's own axes, so it turns with the rocket).
            // The rocket's root is the middle of the whole original stack;
            // after staging that can lie below the remaining stage's engine,
            // and a camera centred there made the stage appear to swing round
            // its engine. The centre of mass moves to the stage still flying.
            var body=rocket.GetComponent<Rigidbody>();
            var centre=body!=null?body.worldCenterOfMass:rocket.transform.position;
            // The focus in world space, kept for ApplyPose to place the camera
            // from directly (see there); 'target' (the same point in the
            // surface frame) still serves everything else.
            flightFocusWorld=centre+rocket.transform.TransformVector(flightFocusOffset*PlanetBody.WorldUnitsPerMeter);
            followingFlight=true;
            target=frame.InverseTransformPoint(flightFocusWorld);
        }
        else followingFlight=false;
        ApplyPose();
    }

    private void DrawRocketMarker()
    {
        if (Event.current.type != EventType.Repaint || !GetComponent<Camera>().enabled) return;
        if (trackedRocket == null) trackedRocket = FindFirstObjectByType<Rocket>();
        if (trackedRocket == null || !trackedRocket.Launched) return;

        var camera = GetComponent<Camera>();
        var rocketPosition = trackedRocket.transform.position;
        var viewport = camera.WorldToViewportPoint(rocketPosition);
        if (float.IsNaN(viewport.x) || float.IsNaN(viewport.y) || float.IsNaN(viewport.z)) return;

        // Measure the visible rocket body, rather than using camera zoom as a proxy.
        var renderers = trackedRocket.GetComponentsInChildren<Renderer>();
        var hasBounds = false;
        var bounds = new Bounds(rocketPosition, Vector3.zero);
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        var sizePixels = 0f;
        if (hasBounds && viewport.z > 0f)
        {
            var diameter = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (camera.orthographic)
                sizePixels = diameter * camera.pixelHeight / (2f * camera.orthographicSize);
            else
                sizePixels = diameter * camera.pixelHeight /
                    (2f * viewport.z * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
        }
        var offScreen = viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f ||
                        viewport.y < 0f || viewport.y > 1f;
        if (!offScreen && sizePixels >= 32f) return;

        if (rocketMarkerStyle == null)
        {
            rocketMarkerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            rocketMarkerStyle.normal.textColor = new Color(1f, 0.28f, 0.25f);
        }

        // GUI coordinates start at the top left. Keep the marker inside the view.
        var x = viewport.z > 0f ? viewport.x * Screen.width : (1f - viewport.x) * Screen.width;
        var y = viewport.z > 0f ? (1f - viewport.y) * Screen.height : viewport.y * Screen.height;
        x = Mathf.Clamp(x, 26f, Screen.width - 26f);
        y = Mathf.Clamp(y, 20f, Screen.height - 20f);
        var oldColor = GUI.color;
        GUI.color = new Color(1f, 0.12f, 0.1f, 1f);
        // An outline keeps the point visible without covering the planet.
        GUI.DrawTexture(new Rect(x - 22f, y - 15f, 44f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 22f, y + 13f, 44f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 22f, y - 15f, 2f, 30f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + 20f, y - 15f, 2f, 30f), Texture2D.whiteTexture);

        var labelOnRight = x + 130f < Screen.width;
        var lineStart = new Vector2(x + (labelOnRight ? 22f : -22f), y - 15f);
        var lineEnd = new Vector2(x + (labelOnRight ? 62f : -62f), y - 34f);
        DrawMarkerLine(lineStart, lineEnd);
        GUI.color = oldColor;
        var labelX = labelOnRight ? lineEnd.x + 4f : lineEnd.x - 68f;
        GUI.Label(new Rect(labelX, lineEnd.y - 12f, 66f, 24f), "Rocket", rocketMarkerStyle);
    }

    private static void DrawMarkerLine(Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y, delta.magnitude, 2f), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
    }

    private void PanFocus(Vector2 mouseDelta)
    {
        if (mouseDelta.sqrMagnitude<.0001f || frame==null) return;
        var metresPerInput=Mathf.Clamp(desiredDistance*.015f,.25f,30f);
        var worldDelta=(transform.right*mouseDelta.x+transform.up*mouseDelta.y)*
            (metresPerInput*PlanetBody.WorldUnitsPerMeter);
        target+=frame.InverseTransformVector(worldDelta);
        var rocket=FindFirstObjectByType<Rocket>();
        if (rocket!=null && rocket.Launched)
        {
            flightFocusOffset+=rocket.transform.InverseTransformVector(worldDelta)/
                PlanetBody.WorldUnitsPerMeter;
            flightFocusOffset=Vector3.ClampMagnitude(flightFocusOffset,1000f);
        }
    }

    public float MapBlend => OrbitalBlend(distance);
    /// <summary>0 = centred on Earth, 1 = solar system view centred on the Sun.</summary>
    public float SolarBlend =>
        planet != null && frame != null && SolarSystem.Instance != null
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SolarStart, SolarFull, distance)) : 0f;

    // Map view turns with the stars, not with Earth: Earth spins underneath
    // it and the solar system view doesn't swing round once a day.
    private float InertialYawOffset =>
        SolarSystem.Instance != null ? (float)(SolarSystem.Instance.RotationAngleRad * Mathf.Rad2Deg) : 0f;

    // 0 = close-up camera around the site/rocket, 1 = map view around Earth,
    // eased so the hand-over between the two has no visible kink.
    private float OrbitalBlend(float atDistance) =>
        planet != null && frame != null
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MapStart, MapFull, atDistance)) : 0f;

    public void ApplyPose()
    {
        InitializeZoom();
        var orbitalBlend = OrbitalBlend(distance);
        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        var focus = target;
        var scale = frame != null ? frame.lossyScale.x : 1f;
        if (planet != null && frame != null)
        {

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
            rocketLaunched = trackedRocket != null && trackedRocket.Launched;
            if (trackedRocket != null && trackedRocket.Launched)
            {
                var flightModel = trackedRocket.GetComponent<RocketFlightModel>();
                if (flightModel != null)
                    groundProximity = Mathf.Max(groundProximity, (float)flightModel.Altitude);
            }

            // Sky colour and distance haze come from the Atmosphere
            // scattering shader at every altitude. This used to switch a
            // flat sky colour + linear fog on below 2 km and the starfield
            // above it, which snapped visibly on the way up.
            RenderSettings.fog = false;
            var camera = GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;

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
            // Never further than the far side of the planet from the map
            // camera (which sits at radius + distance from the centre).
            camera.farClipPlane = Mathf.Min(neededFarMeters, planet.Radius * 2.2f + distance * 1.1f) * scale;
            // Near the Moon its ground is what's close: size the view to its
            // horizon instead (from Earth's altitude the near clip would sit
            // hundreds of metres out and cut the rocket away).
            var moon = MoonBody.Instance;
            if (moon != null && moon.RocketNear && trackedRocket != null && trackedRocket.Launched)
            {
                var moonRadius = (float)MoonBody.RadiusMeters;
                var height = Mathf.Max(distance, (float)moon.RocketAltitude);
                var moonHorizon = Mathf.Sqrt(Mathf.Max(0f, (moonRadius + height) * (moonRadius + height) - moonRadius * moonRadius));
                var neededNearMoon = Mathf.Max(distance * 3f, (height + moonHorizon) * 1.5f);
                camera.farClipPlane = Mathf.Min(neededNearMoon, moonRadius * 2.2f + distance * 1.1f) * scale;
            }
            // Map view reaches out past the Moon's orbit (≤ ~407,000 km).
            if (OrbitalBlend(distance) > 0f)
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, (planet.Radius + distance + 4.15e8f) * scale);
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
        if (followingFlight && frame != null)
        {
            // Following a launched rocket: place the camera in world space,
            // straight from the rocket's centre of mass. Going through the
            // surface frame's local space (metres from the launch site, a
            // child of a 0.001-scaled frame) meant coordinates of millions
            // of metres once in orbit, where a float resolves only ~0.5 m -
            // the camera's offset from the rocket snapped about by that much
            // every frame, which read as violent shaking that grew the
            // further the rocket went. How: the orientation is the same as
            // before (the view's pitch/yaw rotation within the surface
            // frame, turned into world axes by the frame's rotation); the
            // position is the world focus point - near the origin, so
            // precise to millimetres - backed off along the view direction
            // by the zoom distance converted to world units (× the frame's
            // 0.001 scale).
            transform.rotation = frame.rotation * rotation;
            transform.position = flightFocusWorld - transform.rotation * Vector3.forward * (distance * scale);
        }
        else if (frame != null)
        {
            // The same pose as before, worked out in the surface frame's
            // space (metres) and turned into world space.
            transform.SetPositionAndRotation(frame.TransformPoint(focus - rotation * Vector3.forward * distance), frame.rotation * rotation);
        }
        else
        {
            transform.localPosition = focus - rotation * Vector3.forward * distance;
            transform.localRotation = rotation;
        }
        if (orbitalBlend > 0f)
        {
            // The map orbits Earth - or the Moon, while the rocket is inside
            // its sphere of influence (MoonBody.RocketNear), so a lunar orbit
            // or descent fills the map instead of being a speck by a distant
            // Earth. Its radius sets how far out the map camera sits.
            var moon = MoonBody.Instance;
            var atMoon = moon != null && moon.RocketNear && rocketLaunched;
            var center = atMoon ? moon.WorldPosition : planet.transform.position;
            var bodyRadius = atMoon ? (float)MoonBody.RadiusMeters : planet.Radius;
            // Entering map view: start the orbit directly above where the
            // close-up camera already is, so zooming out has no jump.
            if (previousOrbitalBlend <= 0f)
            {
                var up = (transform.position - center).normalized;
                mapPitch = Mathf.Asin(Mathf.Clamp(up.y, -1f, 1f)) * Mathf.Rad2Deg;
                mapYaw = Mathf.Atan2(-up.x, -up.z) * Mathf.Rad2Deg - InertialYawOffset;
            }
            var solar = SolarBlend;
            if (solar > 0f) center = Vector3.Lerp(center, SolarSystem.Instance.SunWorldPosition, solar);
            // Orbit Earth's centre at (radius + zoom distance), always looking
            // at the centre with the orbit's own "up" - no world-up LookAt,
            // so it can go over the poles without flipping.
            var orbit = Quaternion.Euler(mapPitch, mapYaw + InertialYawOffset, 0f);
            var mapPosition = center + orbit * Vector3.back *
                (bodyRadius * PlanetBody.WorldUnitsPerMeter + distance * scale);
            var mapRotation = Quaternion.LookRotation(center - mapPosition, orbit * Vector3.up);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, mapPosition, orbitalBlend),
                Quaternion.Slerp(transform.rotation, mapRotation, orbitalBlend));
            if (solar > 0f)
            {
                // Reach past the far side of Earth's orbit; keep the near clip
                // well in front of everything (nothing is closer than ~0.1 AU
                // to a camera this far out).
                var camera = GetComponent<Camera>();
                var au = (float)(SolarSystem.AstronomicalUnit * PlanetBody.WorldUnitsPerMeter);
                var toCentre = Vector3.Distance(transform.position, center);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, toCentre + au * 1.2f);
                camera.nearClipPlane = Mathf.Max(camera.nearClipPlane, Mathf.Min(toCentre * .05f, camera.farClipPlane * .0001f));
            }
        }
        previousOrbitalBlend = orbitalBlend;

        // Zoomed out, the near clip above stays a fraction of a kilometre
        // against a far clip of tens of thousands - on OpenGL's 24-bit depth
        // that resolves only ~100 km at orbital range, so the planet and its
        // atmosphere shells (38 km+ up) z-fought into a "dissolving" planet.
        // Nothing sits between the camera and the ground, so push the near
        // clip out toward the surface - capped by the focus distance so a
        // tracked rocket close to the camera is never clipped.
        if (planet != null && frame != null)
        {
            var camera = GetComponent<Camera>();
            var surfaceDistance = Vector3.Distance(transform.position, planet.transform.position) -
                planet.Radius * PlanetBody.WorldUnitsPerMeter;
            camera.nearClipPlane = Mathf.Max(camera.nearClipPlane,
                Mathf.Min(surfaceDistance * .5f, distance * .1f * scale));
        }
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

    /// <summary>Close in on the rocket - the part still flying - wherever it is.</summary>
    public void ShowRocket()
    {
        var rocket = FindFirstObjectByType<Rocket>();
        if (rocket == null || frame == null) return;
        // In flight the view centres on the vehicle's centre of mass (see the
        // follow code in Update), so no panning offset; on the pad, the
        // centre of the stack.
        flightFocusOffset = Vector3.zero;
        var body = rocket.GetComponent<Rigidbody>();
        var centre = rocket.Launched && body != null
            ? body.worldCenterOfMass
            : rocket.transform.TransformPoint(Vector3.up * ((rocket.ActiveBottom + rocket.ActiveTop) * .5f - rocket.TotalHeight * .5f) * PlanetBody.WorldUnitsPerMeter);
        target = frame.InverseTransformPoint(centre);
        desiredDistance = Mathf.Max(40f, rocket.ActiveHeight * 2.4f);
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
        target = frame != null ? frame.InverseTransformPoint(worldPosition) : worldPosition;
        distance = Mathf.Clamp(spanMeters * 2.6f, 12f, 3000f);
        desiredDistance = distance;
        ApplyPose();
    }

    private void OnGUI()
    {
        if (LaunchMenu.Open) return;   // the launch menu covers the scene
        var heading = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        heading.normal.textColor = Color.white;
        GUI.Label(new Rect(24, 18, 520, 36), "KENYA  /  ROCKET ASSEMBLY", heading);
        GUI.Label(new Rect(24, 53, 980, 25), SolarBlend > .5f
            ? "Solar system view   •   Right drag: rotate   •   Scroll: zoom (in to return to Earth)   •   V: back to site"
            : OrbitalBlend(distance) > .5f
            ? "Map view   •   Right drag: rotate Earth (any direction, over the poles)   •   Scroll: zoom   •   V: back to site"
            : "Right drag: orbit   •   Scroll: zoom   •   Hold mouse wheel: move focus freely   •   V: Earth / site   •   F: assembly view");
        if (planet != null)
        {
            if (GUI.Button(new Rect(24, 86, 145, 32), "Earth / Planet")) desiredDistance=16000000f;
            if (SolarSystem.Instance != null && GUI.Button(new Rect(338, 86, 145, 32), "Solar system")) desiredDistance=3e11f;
            if (GUI.Button(new Rect(495, 86, 145, 32), "Rocket")) ShowRocket();
            if (GUI.Button(new Rect(181, 86, 145, 32), "Assembly site"))
            {
                target=new Vector3(0,18,0);
                flightFocusOffset=Vector3.zero;
                desiredDistance=90f;
            }
        }
        DrawRocketMarker();
    }
}
