using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlanetAssemblySetup
{
    public static void Build()
    {
        var planetScene = EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        var planet = Object.FindFirstObjectByType<PlanetBody>();
        var surface = planet.GetSurfacePosition(-3.2f, 40.1f);
        var normal = planet.GetSurfaceNormal(-3.2f, 40.1f);
        // A translated origin improves precision at the worksite while preserving
        // the full-size planet and all relative positions in the planetary scene.
        foreach (var obj in planetScene.GetRootGameObjects()) obj.transform.position -= surface;
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            camera.enabled = false;
            camera.tag = "Untagged";
            var listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            var controller = camera.GetComponent<RocketCameraController>();
            if (controller != null) controller.enabled = false;
        }
        var oldPlatform = GameObject.Find("Kenya Launch Platform");
        if (oldPlatform != null) Object.DestroyImmediate(oldPlatform);
        var template = EditorSceneManager.OpenScene("Assets/Scenes/RocketAssembly.unity", OpenSceneMode.Additive);
        var frame = new GameObject("Kenya Surface Frame");
        SceneManager.MoveGameObjectToScene(frame, template);
        foreach (var obj in template.GetRootGameObjects())
        {
            if (obj == frame) continue;
            if (obj.name == "Assembly Sun") { Object.DestroyImmediate(obj); continue; }
            obj.transform.SetParent(frame.transform, false);
        }
        var ground = GameObject.Find("Ground");
        Object.DestroyImmediate(ground.GetComponent<BoxCollider>());
        var terrain = CreateGround(planet.Radius);
        AssetDatabase.CreateAsset(terrain, "Assets/Materials/Assembly/KenyaTerrain.asset");
        ground.GetComponent<MeshFilter>().sharedMesh = terrain;
        ground.transform.localPosition = Vector3.zero;
        ground.transform.localRotation = Quaternion.identity;
        ground.transform.localScale = Vector3.one;
        frame.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        frame.transform.localScale = Vector3.one * PlanetBody.WorldUnitsPerMeter;
        EditorSceneManager.MergeScenes(template, planetScene);
        SceneManager.SetActiveScene(planetScene);
        var controllerView = GameObject.Find("Assembly Camera").GetComponent<AssemblyViewCamera>();
        controllerView.SetPlanet(planet);
        var settings = new SerializedObject(controllerView);
        settings.FindProperty("target").vector3Value = new Vector3(0,18,0);
        settings.FindProperty("distance").floatValue = 90;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var cameraView = controllerView.GetComponent<Camera>();
        cameraView.enabled = true;
        cameraView.tag = "MainCamera";
        cameraView.GetComponent<AudioListener>().enabled = true;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
        var sun = GameObject.Find("Sun").GetComponent<Light>();
        sun.enabled = true;
        sun.transform.rotation = frame.transform.rotation * Quaternion.Euler(48,-35,0);
        sun.intensity = 1.1f;
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SpaceSkybox.mat");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.67f,.77f,.88f);
        RenderSettings.ambientEquatorColor = new Color(.53f,.58f,.61f);
        RenderSettings.ambientGroundColor = new Color(.35f,.34f,.31f);
        var rocket = Object.FindFirstObjectByType<Rocket>();
        if (rocket != null)
        {
            rocket.SendMessage("Awake");
            rocket.PlaceAtLatitudeLongitude(planet,-3.2f,40.1f,25f);
            rocket.PlaceOnAssemblyMount();
        }
        controllerView.ApplyPose();
        EditorSceneManager.SaveScene(planetScene, "Assets/Scenes/PlanetScene.unity");
        AssetDatabase.SaveAssets();
        ValidateAndRender();
    }

    private static Mesh CreateGround(float radius)
    {
        var vertices = new List<Vector3> { new Vector3(0,-.5f,0) };
        var triangles = new List<int>();
        // A curved surface patch is enough for the high precision site view.
        // Do not add a lowered duplicate rim: at intermediate zoom it reads as
        // a gigantic circular washer around the planet.
        var radii = new[] { 100f, 500f, 2000f, 10000f, 30000f };
        const int sides = 128;
        for (var ring=0; ring<radii.Length; ring++)
        {
            double r = radii[ring];
            var y = (float)(System.Math.Sqrt((double)radius*radius-r*r)-radius)-.5f;
            for (var j=0; j<sides; j++)
            {
                var a=j*Mathf.PI*2/sides;
                vertices.Add(new Vector3((float)r*Mathf.Cos(a),y,(float)r*Mathf.Sin(a)));
            }
        }
        for (var j=0; j<sides; j++)
        {
            var next=(j+1)%sides;
            triangles.AddRange(new[] {0,1+next,1+j});
            for (var ring=0; ring<radii.Length-1; ring++)
            {
                var a=1+ring*sides+j; var b=1+ring*sides+next;
                var c=b+sides; var d=a+sides;
                triangles.AddRange(new[] {a,b,c,a,c,d});
            }
        }
        var mesh = new Mesh { name="Kenya curved ground" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static void ValidateAndRender()
    {
        var planet = Object.FindFirstObjectByType<PlanetBody>();
        var frame = GameObject.Find("Kenya Surface Frame").transform;
        var controller = GameObject.Find("Assembly Camera").GetComponent<AssemblyViewCamera>();
        if (planet == null || Mathf.Abs(planet.Radius-6378100f)>1)
            throw new System.Exception("Full-size Earth must remain in the scene.");
        var altitude = ((double)Vector3.Distance(frame.position,planet.transform.position) /
                        PlanetBody.WorldUnitsPerMeter) - planet.Radius;
        if (System.Math.Abs(altitude)>1.5) throw new System.Exception("Site is not at Earth's surface: "+altitude);
        if (Vector3.Dot(frame.up,planet.GetSurfaceNormal(-3.2f,40.1f))<.9999f)
            throw new System.Exception("Site orientation is not the Kenya surface normal.");
        var sphere = planet.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sphere.vertices;
        var indices = sphere.triangles;
        for (var i=0; i<indices.Length; i+=3)
        {
            var a=vertices[indices[i]]; var b=vertices[indices[i+1]]; var c=vertices[indices[i+2]];
            if (Vector3.Dot(Vector3.Cross(b-a,c-a),a+b+c)<-.0000001f)
                throw new System.Exception("Earth contains inward-facing triangles.");
        }
        Render(controller.GetComponent<Camera>(),"Reports/KenyaOnEarth.png");
        controller.ShowPlanet();
        if (RenderSettings.fog) throw new System.Exception("Orbital view must not use local fog.");
        var camera=controller.GetComponent<Camera>();
        var viewport=camera.WorldToViewportPoint(planet.transform.position);
        if (viewport.z<0 || Mathf.Abs(viewport.x-.5f)>.01f || Mathf.Abs(viewport.y-.5f)>.01f)
            throw new System.Exception("Orbital camera must frame Earth's center.");
        Render(camera,"Reports/KenyaEarthOverview.png");
        var serialized = new SerializedObject(controller);
        foreach (var distance in new[] {90f,500f,2000f,10000f,100000f,2000000f,5000000f,10000000f,16000000f,20000000f})
        {
            serialized.FindProperty("distance").floatValue=distance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            controller.ApplyPose();
            if (Vector3.Distance(camera.transform.position,planet.transform.position)<planet.Radius*PlanetBody.WorldUnitsPerMeter)
                throw new System.Exception("Zoom moves the camera inside Earth at distance "+distance);
        }
        controller.ShowAssembly();
        Debug.Log("PLANET_ASSEMBLY_VALIDATED: full Earth; Kenya at surface; local and orbital cameras; altitude metres="+altitude);
    }

    private static void Render(Camera camera,string path)
    {
        var target=new RenderTexture(1440,900,24);
        camera.targetTexture=target;
        camera.Render();
        RenderTexture.active=target;
        var image=new Texture2D(1440,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1440,900),0,0);
        image.Apply();
        Directory.CreateDirectory("Reports");
        File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=null;
        RenderTexture.active=null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
    }
}
