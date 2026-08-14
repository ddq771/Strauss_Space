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
        light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlanetScene.unity");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
