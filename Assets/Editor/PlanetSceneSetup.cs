using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlanetSceneSetup
{
    [MenuItem("Shtraus Space/Create Planet Body")]
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

    [MenuItem("Shtraus Space/Create Rocket At Earth Surface")]
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

    [MenuItem("Shtraus Space/Place Rocket In Kenya")]
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
