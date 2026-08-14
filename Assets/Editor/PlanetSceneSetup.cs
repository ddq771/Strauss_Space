using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlanetSceneSetup
{
    [MenuItem("Strauss Space/Create Planet Body")]
    public static void CreatePlanetInCurrentScene()
    {
        if (GameObject.Find("Planet") != null)
        {
            Selection.activeGameObject = GameObject.Find("Planet");
            return;
        }

        var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "Planet";
        planet.AddComponent<PlanetBody>();

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 2f, -14f);
        camera.transform.LookAt(planet.transform);

        var lightObject = new GameObject("Sun");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        // Earth baseline: 23.44 degree axial tilt. The exact Sun direction
        // in a real simulation would also depend on date, time, and location.
        light.transform.rotation = Quaternion.Euler(23.44f, -30f, 0f);

        Selection.activeGameObject = planet;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Strauss Space/Create Rocket At Earth Surface")]
    public static void CreateRocketAtEarthSurface()
    {
        var planet = GameObject.Find("Planet");
        if (planet == null)
        {
            EditorUtility.DisplayDialog("Rocket", "Create the Planet first, then create the rocket.", "OK");
            return;
        }

        var existingRocket = GameObject.Find("Rocket");
        if (existingRocket != null)
        {
            Selection.activeGameObject = existingRocket;
            return;
        }

        var rocket = CreateRocketObject();
        var planetBody = planet.GetComponent<PlanetBody>();
        rocket.transform.position = planet.transform.position +
                                    Vector3.up * ((planetBody.Radius + 20f) * PlanetBody.WorldUnitsPerMeter);
        rocket.GetComponent<Rocket>().PlaceOnPlanetSurface(planetBody, 20f);

        Selection.activeGameObject = rocket;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Strauss Space/Place Rocket In Kenya")]
    public static void PlaceRocketInKenya()
    {
        var planet = GameObject.Find("Planet");
        var rocket = GameObject.Find("Rocket");
        if (planet == null || rocket == null)
        {
            EditorUtility.DisplayDialog("Kenya Launch Site", "Create the Planet and Rocket first.", "OK");
            return;
        }

        var planetBody = planet.GetComponent<PlanetBody>();
        rocket.GetComponent<Rocket>().PlaceAtLatitudeLongitude(
            planetBody,
            latitudeDegrees: -3.2f,
            longitudeDegrees: 40.1f,
            clearance: 20f);

        Selection.activeGameObject = rocket;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Strauss Space/Create Kenya Launch View")]
    public static void CreateKenyaLaunchView()
    {
        var planet = GameObject.Find("Planet");
        var rocket = GameObject.Find("Rocket");
        if (planet == null || rocket == null)
        {
            EditorUtility.DisplayDialog("Kenya Launch View", "Create the Planet and Rocket first.", "OK");
            return;
        }

        var planetBody = planet.GetComponent<PlanetBody>();
        const float latitude = -3.2f;
        const float longitude = 40.1f;
        var normal = planetBody.GetSurfaceNormal(latitude, longitude);
        var surface = planetBody.GetSurfacePosition(latitude, longitude);

        var platform = GameObject.Find("Kenya Launch Platform");
        if (platform == null)
        {
            platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Kenya Launch Platform";
            // 100 m diameter and 10 m thick, with 1 Unity unit = 1 km.
            platform.transform.localScale = new Vector3(0.05f, 0.005f, 0.05f);
        }

        platform.transform.position = surface + normal * (5f * PlanetBody.WorldUnitsPerMeter);
        platform.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);

        var launchCameraObject = GameObject.Find("Kenya Launch Camera");
        if (launchCameraObject == null)
        {
            launchCameraObject = new GameObject("Kenya Launch Camera");
            launchCameraObject.AddComponent<Camera>();
        }

        var east = Vector3.Cross(Vector3.up, normal).normalized;
        var cameraPosition = surface + normal * 0.08f + east * 0.18f;
        launchCameraObject.transform.position = cameraPosition;
        var target = rocket.transform.position + normal * 0.015f;
        launchCameraObject.transform.rotation = Quaternion.LookRotation(target - cameraPosition, normal);
        var launchCamera = launchCameraObject.GetComponent<Camera>();
        launchCamera.fieldOfView = 45f;

        var currentMainCamera = GameObject.FindWithTag("MainCamera");
        if (currentMainCamera != null && currentMainCamera != launchCameraObject)
        {
            currentMainCamera.tag = "Untagged";
        }
        launchCameraObject.tag = "MainCamera";

        Selection.activeGameObject = platform;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "Planet";
        planet.AddComponent<PlanetBody>();

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 2f, -14f);
        camera.transform.LookAt(planet.transform);

        var lightObject = new GameObject("Sun");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(23.44f, -30f, 0f);

        var rocket = CreateRocketObject();
        rocket.GetComponent<Rocket>().PlaceAtLatitudeLongitude(
            planet.GetComponent<PlanetBody>(),
            latitudeDegrees: -3.2f,
            longitudeDegrees: 40.1f,
            clearance: 20f);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlanetScene.unity");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    private static GameObject CreateRocketObject()
    {
        var rocket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rocket.name = "Rocket";
        rocket.transform.localScale = new Vector3(10f, 15f, 10f) * PlanetBody.WorldUnitsPerMeter;
        rocket.AddComponent<Rocket>();
        return rocket;
    }
}
