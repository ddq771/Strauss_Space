using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AssemblySceneSetup
{
    public const string ScenePath = "Assets/Scenes/PlanetScene.unity";

    [InitializeOnLoadMethod]
    private static void QueueCraneClearanceFix()
    {
        EditorApplication.delayCall += () =>
        {
            var beam=GameObject.Find("Crane Beam");
            if(beam==null || (beam.scene.name!="PlanetScene" && beam.scene.name!="RocketAssembly"))return;
            // Migrate only the old pose, preserving deliberately edited layouts.
            if(Vector3.Distance(beam.transform.localPosition,new Vector3(-8,28,0))>.01f)return;
            beam.transform.localPosition=new Vector3(-18,28,10);
            beam.transform.localScale=new Vector3(.65f,.65f,25);
            var cable=GameObject.Find("Crane Cable");
            if(cable!=null)cable.transform.localPosition=new Vector3(-18,25,18);
            var hook=GameObject.Find("Crane Hook");
            if(hook!=null)hook.transform.localPosition=new Vector3(-18,22.2f,18);
            if(!EditorApplication.isPlaying)EditorSceneManager.MarkSceneDirty(beam.scene);
            Debug.Log("CRANE_CLEARANCE_FIXED: boom parked beside the rocket assembly area.");
        };
    }

    [InitializeOnLoadMethod]
    private static void RegisterViewRequest()
    {
        EditorApplication.update -= OpenRequestedView;
        EditorApplication.update += OpenRequestedView;
    }

    private static void OpenRequestedView()
    {
        const string requestPath = "Library/OpenAssemblyView.request";
        if (!File.Exists(requestPath) || !File.Exists(ScenePath) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= OpenRequestedView;
        // Preserve any unsaved work as scene copies before changing the visible view.
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isDirty) continue;
            Directory.CreateDirectory("Assets/Scenes/LocalBackups");
            var backup = "Assets/Scenes/LocalBackups/BeforeAssembly_" +
                         System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + i + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true))
            {
                Debug.LogError("Could not preserve the current scene; assembly view was not opened.");
                return;
            }
            Debug.Log("Preserved unsaved scene in " + backup);
        }
        var startPlaying=File.ReadAllText(requestPath).Trim()=="play";
        EditorSceneManager.OpenScene(ScenePath);
        var camera = GameObject.Find("Assembly Camera").GetComponent<Camera>();
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(GameObject.Find("Rocket Assembly Root").transform.position,
                camera.transform.rotation, 62f * PlanetBody.WorldUnitsPerMeter);
        Selection.activeGameObject = GameObject.Find("Rocket Assembly Root");
        File.Delete(requestPath);
        Debug.Log("ASSEMBLY_VIEW_OPENED");
        if(startPlaying)EditorApplication.delayCall+=() =>
        {
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.isPlaying=true;
        };
    }

    [MenuItem("Strauss Space/Open Rocket Assembly")]
    public static void Open()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play Mode before opening the assembly scene.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        var camera = GameObject.Find("Assembly Camera").GetComponent<Camera>();
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(GameObject.Find("Rocket Assembly Root").transform.position,
                camera.transform.rotation, 62f * PlanetBody.WorldUnitsPerMeter);
        Selection.activeGameObject = GameObject.Find("Rocket Assembly Root");
    }

    // Invoked in an isolated project to generate a serialized, ready-to-open scene.
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Materials/Assembly");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.67f, .77f, .88f);
        RenderSettings.ambientEquatorColor = new Color(.53f, .58f, .61f);
        RenderSettings.ambientGroundColor = new Color(.35f, .34f, .31f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.67f, .79f, .86f);
        RenderSettings.fogStartDistance = 140f;
        RenderSettings.fogEndDistance = 500f;
        var concrete = Material("Concrete", new Color(.48f, .51f, .51f));
        var apron = Material("Apron", new Color(.25f, .29f, .31f));
        var ground = Material("Ground", new Color(.43f, .46f, .35f));
        var yellow = Material("SafetyYellow", new Color(1f, .64f, .06f));
        var paint = Material("Markings", new Color(.84f, .87f, .82f));
        var steel = Material("GantrySteel", new Color(.19f, .24f, .27f));
        var roots = new GameObject("Kenya Assembly Site").transform;
        Block("Ground", new Vector3(0,-1,0), new Vector3(1500,1,1500), ground, roots);
        Block("Work Apron", new Vector3(0,-.35f,0), new Vector3(110,.3f,100), apron, roots);
        Block("Assembly Platform", new Vector3(0,0,0), new Vector3(32,.4f,32), concrete, roots);
        Block("Assembly Mount", new Vector3(0,.35f,0), new Vector3(5,.3f,5), steel, roots);
        var assembly = new GameObject("Rocket Assembly Root");
        assembly.transform.position = new Vector3(0,.5f,0);

        // Flat painted strips sit on the platform, not in space around the planet.
        for (var n = -15; n <= 15; n += 5)
        {
            Block("Grid", new Vector3(n,.205f,0), new Vector3(.035f,.005f,30), paint, roots, false);
            Block("Grid", new Vector3(0,.205f,n), new Vector3(30,.005f,.035f), paint, roots, false);
        }
        foreach (var sign in new[] {-1,1})
        {
            Block("Safety Edge", new Vector3(sign*15.5f,.21f,0), new Vector3(.18f,.01f,31), yellow, roots, false);
            Block("Safety Edge", new Vector3(0,.21f,sign*15.5f), new Vector3(31,.01f,.18f), yellow, roots, false);
            for (var n = -12; n <= 12; n += 3)
                Block("Approach Marking", new Vector3(sign*23f,-.19f,n), new Vector3(.18f,.01f,1.6f), paint, roots, false);
        }
        foreach (var x in new[] {-20f,-16f})
        foreach (var z in new[] {-3f,3f})
        {
            Block("Tower Foot", new Vector3(x,0,z), new Vector3(2,.6f,2), concrete, roots);
            Block("Gantry Column", new Vector3(x,14,z), new Vector3(.45f,28,.45f), steel, roots);
        }
        for (var y = 5; y <= 25; y += 5)
        {
            Block("Service Landing", new Vector3(-18,y,0), new Vector3(4.6f,.22f,6.6f), steel, roots);
            Block("Landing Rail", new Vector3(-15.7f,y+1,0), new Vector3(.1f,.1f,6.6f), yellow, roots);
            Block("Landing Rail", new Vector3(-20.3f,y+1,0), new Vector3(.1f,.1f,6.6f), yellow, roots);
        }
        Block("Crane Beam", new Vector3(-18,28,10), new Vector3(.65f,.65f,25), yellow, roots);
        Block("Crane Cable", new Vector3(-18,25,18), new Vector3(.045f,5.4f,.045f), steel, roots, false);
        Block("Crane Hook", new Vector3(-18,22.2f,18), new Vector3(.4f,.5f,.25f), yellow, roots, false);
        // Low workshop buildings give the ground view a readable human scale.
        Block("Workshop", new Vector3(30,3,30), new Vector3(22,6,13), concrete, roots);
        Block("Workshop Door", new Vector3(30,2.5f,23.45f), new Vector3(9,5,.08f), steel, roots, false);
        Block("Workshop Roof", new Vector3(30,6.15f,30), new Vector3(23,.3f,14), steel, roots);

        var sunObject = new GameObject("Assembly Sun");
        var sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.color = new Color(1f,.95f,.84f);
        sun.shadows = LightShadows.Soft;
        sunObject.transform.rotation = Quaternion.Euler(48,-35,0);
        var cameraObject = new GameObject("Assembly Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RenderSettings.fogColor;
        camera.fieldOfView = 52;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 1800f;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<AssemblyViewCamera>().ApplyPose();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/RocketAssembly.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("ASSEMBLY_SCENE_BUILT: " + ScenePath);
    }

    public static void ValidateAndRender()
    {
        EditorSceneManager.OpenScene(ScenePath);
        if (Object.FindFirstObjectByType<PlanetBody>() != null)
        {
            PlanetAssemblySetup.ValidateAndRender();
            return;
        }
        var camera = GameObject.Find("Assembly Camera").GetComponent<Camera>();
        if (Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length != 1)
            throw new System.Exception("Assembly view must have exactly one camera.");
        if (GameObject.Find("Planet") != null || GameObject.Find("World Camera") != null)
            throw new System.Exception("Planetary objects must not enter the assembly scene.");
        var pad = GameObject.Find("Assembly Platform").GetComponent<Collider>();
        var mount = GameObject.Find("Assembly Mount").GetComponent<Collider>();
        var root = GameObject.Find("Rocket Assembly Root").transform;
        Physics.SyncTransforms();
        if (Mathf.Abs(pad.bounds.min.y + .2f) > .001f ||
            Mathf.Abs(mount.bounds.max.y - root.position.y) > .001f)
            throw new System.Exception("Assembly support heights are inconsistent.");
        camera.GetComponent<AssemblyViewCamera>().ApplyPose();
        var texture = new RenderTexture(1440, 900, 24);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Reports");
        File.WriteAllBytes("Reports/RocketAssembly.png", image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(texture);
        Debug.Log("ASSEMBLY_VALIDATION_PASSED: single camera, grounded platform, mount height, rendered view.");
    }

    private static Material Material(string name, Color color)
    {
        var material = new Material(Shader.Find("Standard")) { name = name, color = color };
        material.SetFloat("_Glossiness", .18f);
        AssetDatabase.CreateAsset(material, "Assets/Materials/Assembly/" + name + ".mat");
        return material;
    }

    private static GameObject Block(string name, Vector3 position, Vector3 scale,
        Material material, Transform parent, bool collider = true)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }
}
