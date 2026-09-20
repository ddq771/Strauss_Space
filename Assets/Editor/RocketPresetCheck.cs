using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; not run automatically. Verifies every
// RocketPreset loads to the right engine count/type, locks the assembly UI,
// reports a sane TWR, and that "Custom build" cleanly unlocks again.
[InitializeOnLoad]
public static class RocketPresetCheck
{
    static double deadline; static int presetIndex = -1; static float next;
    static RocketPresetCheck()
    {
        if (SessionState.GetBool("RocketPresetCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 90; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("RocketPresetCheck", true);
        EditorApplication.isPlaying = true;
    }

    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out");
            if (!EditorApplication.isPlaying) return;
            var a = UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();
            if (a == null || a.FuelTank == null) return;
            if (Time.unscaledTime < next) return;

            if (presetIndex == -1)
            {
                presetIndex = 0;
                next = Time.unscaledTime + 0.2f;
                return;
            }
            if (presetIndex < RocketPresets.All.Length)
            {
                var preset = RocketPresets.All[presetIndex];
                a.LoadPreset(presetIndex);
                var flight = a.GetComponent<RocketFlightModel>();
                var rocket = a.GetComponent<Rocket>();

                Require(a.IsLocked, preset.name + ": controller did not lock");
                Require(a.ActivePresetName == preset.name, preset.name + ": active preset name mismatch");
                Require(a.InstalledCount == preset.engineCount,
                    preset.name + ": expected " + preset.engineCount + " engines, got " + a.InstalledCount);
                for (var i = 0; i < preset.engineCount; i++)
                    Require(a.GetEngineId(i) == preset.engineId,
                        preset.name + ": socket " + i + " has " + a.GetEngineId(i) + ", expected " + preset.engineId);
                // TWR/Thrust are only populated once Prepare()/Step() has
                // run (normally when the engine fires) - call Prepare
                // directly to compute them without actually igniting.
                var ready=flight.Prepare(1.0);
                Require(flight.TWR > 0.5 && flight.TWR < 50,
                    preset.name + ": implausible TWR " + flight.TWR + " (ready=" + ready + " status=" + flight.Status +
                    " thrust=" + flight.Thrust + " mass=" + flight.TotalMass + " gravity=" + flight.LocalGravity +
                    " pressure=" + flight.Pressure + ")");
                Require(Mathf.Abs(flight.FuelType == preset.fuelType ? 0 : 1) < 0.5f,
                    preset.name + ": fuel type is " + flight.FuelType + ", expected " + preset.fuelType);

                Directory.CreateDirectory("Reports");
                var view = UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
                view.ShowAssembly();
                var camera = view.GetComponent<Camera>();
                Render(camera, "Reports/Preset_" + preset.name.Replace(" ", "") + ".png");

                Debug.Log("PRESET_OK " + preset.name + " engines=" + a.InstalledCount +
                    " TWR=" + flight.TWR.ToString("F2") + " mass=" + flight.TotalMass.ToString("F0") + "kg" +
                    " fuel=" + flight.FuelType + " drag@0m/s=" + flight.Drag);

                presetIndex++;
                next = Time.unscaledTime + 0.2f;
                return;
            }

            // Final check: leaving a preset unlocks and clears it.
            var last = UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();
            last.ExitPreset();
            Require(!last.IsLocked, "ExitPreset did not unlock");
            Require(last.InstalledCount == 0, "ExitPreset left engines installed");

            Debug.Log("ROCKET_PRESET_CHECK_PASSED: all presets load, lock, report plausible TWR, and unlock cleanly");
            SessionState.SetBool("RocketPresetCheck", false);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ROCKET_PRESET_CHECK_FAILED: " + e);
            SessionState.SetBool("RocketPresetCheck", false);
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
