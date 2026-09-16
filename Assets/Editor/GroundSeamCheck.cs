using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off render, not a regression check: captures the ground-level assembly
// view and a pulled-back view so the Kenya ground patch's seam against the
// real planet sphere can be inspected visually.
[InitializeOnLoad]
public static class GroundSeamCheck
{
    static double deadline;
    static GroundSeamCheck()
    {
        if (SessionState.GetBool("GroundSeamCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("GroundSeamCheck", true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out");
            if (!EditorApplication.isPlaying) return;
            var view = UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
            if (view == null) return;
            var camera = view.GetComponent<Camera>();

            Directory.CreateDirectory("Reports");
            view.ShowAssembly();
            Render(camera, "Reports/GroundSeam_Assembly.png");

            var serialized = new SerializedObject(view);
            serialized.FindProperty("distance").floatValue = 20000f;
            serialized.FindProperty("pitch").floatValue = 10f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.ApplyPose();
            Render(camera, "Reports/GroundSeam_PullBack.png");

            Debug.Log("GROUND_SEAM_CHECK_DONE");
            SessionState.SetBool("GroundSeamCheck", false);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("GROUND_SEAM_CHECK_FAILED: " + e);
            SessionState.SetBool("GroundSeamCheck", false);
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
