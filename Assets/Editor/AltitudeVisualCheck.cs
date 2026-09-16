using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; never runs automatically in the user's editor.
// Renders the tracking camera's view of the rocket at a sweep of altitudes
// to Reports/, to diagnose exactly where/how the visuals break as altitude
// increases (reported: breaks after ~100,000 m, Earth disappears higher up).
[InitializeOnLoad]
public static class AltitudeVisualCheck
{
    static double deadline;
    static AltitudeVisualCheck()
    {
        if (SessionState.GetBool("AltitudeVisualCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("AltitudeVisualCheck", true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out");
            if (!EditorApplication.isPlaying) return;
            var a = UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();
            if (a == null || a.FuelTank == null) return;

            var rocket = a.GetComponent<Rocket>();
            var planet = UnityEngine.Object.FindFirstObjectByType<PlanetBody>();
            var view = UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
            var camera = view.GetComponent<Camera>();
            var body = rocket.GetComponent<Rigidbody>();

            a.InstallEngine(0, "F1");
            rocket.StartEngine(1f);
            view.ShowAssembly();

            // ApplyPose() alone does not reproduce real gameplay: the
            // camera's target only re-centres on the rocket inside
            // LateUpdate()'s tracking branch, which normally runs once per
            // real engine frame. This whole sweep runs inside a single
            // editor-update tick with no frame boundary between teleports,
            // so LateUpdate never re-fires on its own - invoke it directly
            // via reflection after each teleport, exactly as the engine
            // would between frames.
            var lateUpdate = typeof(AssemblyViewCamera).GetMethod("LateUpdate",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Directory.CreateDirectory("Reports");
            var outward = (rocket.transform.position - planet.transform.position).normalized;
            float[] altitudes = { 20000f, 50000f, 90000f, 100000f, 110000f, 130000f, 159000f, 160000f, 170000f, 200000f, 500000f, 1000000f };
            foreach (var altitude in altitudes)
            {
                body.position = planet.transform.position +
                    outward * ((planet.Radius + altitude) * PlanetBody.WorldUnitsPerMeter);
                rocket.transform.position = body.position;
                lateUpdate.Invoke(view, null);
                Render(camera, $"Reports/Altitude_{altitude:F0}m.png");

                var camToPlanetCenter = (planet.transform.position - camera.transform.position).magnitude;
                var camToSurface = camToPlanetCenter - planet.Radius * PlanetBody.WorldUnitsPerMeter;
                var dirToPlanet = (planet.transform.position - camera.transform.position).normalized;
                var angleToPlanetCenter = Vector3.Angle(camera.transform.forward, dirToPlanet);
                Debug.Log($"ALT={altitude:F0} near={camera.nearClipPlane:F4} far={camera.farClipPlane:F1} " +
                    $"camPos={camera.transform.position} camToPlanetCenter={camToPlanetCenter:F2} " +
                    $"camToSurface={camToSurface:F2} fog={RenderSettings.fog} clearFlags={camera.clearFlags} " +
                    $"fov={camera.fieldOfView:F1} angleToPlanetCenter={angleToPlanetCenter:F1} " +
                    $"camForward={camera.transform.forward} camEuler={camera.transform.eulerAngles}");
            }

            Debug.Log("ALTITUDE_VISUAL_CHECK_DONE: renders written to Reports/");
            SessionState.SetBool("AltitudeVisualCheck", false);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ALTITUDE_VISUAL_CHECK_FAILED: " + e);
            SessionState.SetBool("AltitudeVisualCheck", false);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(Camera camera, string path)
    {
        var rt = new RenderTexture(1280, 800, 24);
        camera.targetTexture = rt;
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = old;
        UnityEngine.Object.Destroy(image);
        rt.Release();
        UnityEngine.Object.Destroy(rt);
    }
}
