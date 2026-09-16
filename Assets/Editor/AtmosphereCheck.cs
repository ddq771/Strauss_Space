using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; never runs automatically in the user's editor.
// Verifies the Atmosphere shell is wired up correctly and renders the actual
// gameplay camera (AssemblyViewCamera, on "Assembly Camera") in both its
// orbital-planet and ground-assembly poses to PNGs under Reports/, so the
// real look can be inspected without opening the interactive editor.
public static class AtmosphereCheck
{
    public static void Begin()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");

            var planet = GameObject.Find("Planet");
            if (planet == null)
            {
                throw new Exception("Planet not found in PlanetScene");
            }

            var atmosphere = planet.GetComponent<Atmosphere>();
            if (atmosphere == null)
            {
                // AddComponent triggers Atmosphere.Reset() synchronously in
                // the editor, which builds the shell immediately - same
                // proven pattern as PlanetBody/Rocket self-configuring.
                atmosphere = planet.AddComponent<Atmosphere>();
            }
            else
            {
                // A component saved by an older script version only carries
                // over serialized fields that still exist - it does not
                // re-run Awake/OnValidate just because the scene was opened.
                // Force a rebuild so a pre-existing single-shell save (or any
                // stale layer set) is brought up to date with the current
                // script before verifying it.
                var buildShell = typeof(Atmosphere).GetMethod("BuildShell",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                buildShell.Invoke(atmosphere, null);
            }

            // The shell is rendered as several nested layers (see
            // Atmosphere.Layer), not one child - verify each one, and that
            // their thicknesses are strictly increasing (innermost first),
            // since that ordering is what makes them read as nested shells
            // instead of overlapping/fighting at the same radius.
            var layersField = typeof(Atmosphere).GetField("layers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var layers = (Atmosphere.Layer[])layersField.GetValue(atmosphere);
            if (layers == null || layers.Length < 2)
            {
                throw new Exception("Expected multiple atmosphere layers, found " + (layers?.Length ?? 0));
            }

            var previousThickness = 0f;
            for (var i = 0; i < layers.Length; i++)
            {
                var layerName = string.IsNullOrEmpty(layers[i].name) ? $"Atmosphere Layer {i}" : layers[i].name;
                var shell = planet.transform.Find(layerName);
                if (shell == null)
                {
                    throw new Exception("Atmosphere layer child was not created: " + layerName);
                }

                var shellRenderer = shell.GetComponent<MeshRenderer>();
                if (shellRenderer == null || shellRenderer.sharedMaterial == null)
                {
                    throw new Exception("Atmosphere layer has no material: " + layerName);
                }

                var shaderName = shellRenderer.sharedMaterial.shader.name;
                if (shaderName != "Strauss Space/Atmosphere")
                {
                    throw new Exception("Atmosphere layer '" + layerName + "' uses the wrong shader: " + shaderName);
                }

                var shellMeshFilter = shell.GetComponent<MeshFilter>();
                if (shellMeshFilter == null || shellMeshFilter.sharedMesh == null || shellMeshFilter.sharedMesh.vertexCount == 0)
                {
                    throw new Exception("Atmosphere layer has no generated mesh: " + layerName);
                }

                if (shell.localScale.x <= 1f)
                {
                    throw new Exception("Atmosphere layer '" + layerName + "' is not larger than the planet (localScale.x = " + shell.localScale.x + ")");
                }

                if (layers[i].thicknessFraction <= previousThickness)
                {
                    throw new Exception("Atmosphere layers are not nested innermost-first at '" + layerName + "'");
                }
                previousThickness = layers[i].thicknessFraction;
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory("Reports");

            var assemblyView = UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
            if (assemblyView == null)
            {
                throw new Exception("AssemblyViewCamera not found - is it still on 'Assembly Camera'?");
            }

            var camera = assemblyView.GetComponent<Camera>();

            // ShowPlanet/ShowAssembly are the same calls the game's own UI
            // buttons use (AssemblyViewCamera.OnGUI), so this exercises the
            // real fog/clip-plane/position logic exactly as gameplay does,
            // not an ad-hoc approximation of it.
            assemblyView.ShowPlanet();
            Render(camera, "Reports/AtmosphereSpaceView.png");

            assemblyView.ShowAssembly();
            Render(camera, "Reports/AtmosphereGroundView.png");

            Debug.Log("ATMOSPHERE_CHECK_PASSED: shell built, shader wired, renders written to Reports/");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ATMOSPHERE_CHECK_FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(Camera camera, string path)
    {
        var rt = new RenderTexture(1600, 1000, 24);
        camera.targetTexture = rt;
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = old;
        UnityEngine.Object.Destroy(image);
        rt.Release();
        UnityEngine.Object.Destroy(rt);
    }
}
