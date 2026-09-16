using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; never runs automatically in the user's editor.
// Verifies that ground-level fog/sky no longer persist once a tracked rocket
// climbs to a real space altitude while the camera stays zoomed in close to
// it (the "planet is all blue at 1,000,000 m" bug: distance alone, the
// camera's own zoom/orbit distance, used to gate fog/clear-flags without
// regard for the rocket's actual altitude).
[InitializeOnLoad]
public static class AltitudeFogCheck
{
    static double deadline;
    static AltitudeFogCheck()
    {
        if (SessionState.GetBool("AltitudeFogCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("AltitudeFogCheck", true);
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

            var rocket = a.GetComponent<Rocket>();
            var planet = UnityEngine.Object.FindFirstObjectByType<PlanetBody>();
            var view = UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
            var camera = view.GetComponent<Camera>();

            a.InstallEngine(0, "F1");
            rocket.StartEngine(1f);
            Require(rocket.Launched, "Rocket did not ignite");

            // Keep the camera zoomed in tight, as if a player never scrolled
            // out while watching the ascent - this is the exact scenario
            // that exposed the bug.
            view.ShowAssembly();
            view.ApplyPose();
            Require(RenderSettings.fog, "Sanity check failed: fog should start ON at ground level");

            // Teleport straight to a real space altitude instead of actually
            // flying there - this test is about the camera/fog response to
            // altitude, not about ascent physics (already covered by
            // RocketFlightCheck).
            var outward = (rocket.transform.position - planet.transform.position).normalized;
            var body = rocket.GetComponent<Rigidbody>();
            body.position = planet.transform.position +
                outward * ((planet.Radius + 1_000_000f) * PlanetBody.WorldUnitsPerMeter);
            rocket.transform.position = body.position;

            view.ApplyPose();

            Require(!RenderSettings.fog,
                "Fog is still on at 1,000,000 m altitude - ground fog is leaking into the space view");
            Require(camera.clearFlags == CameraClearFlags.Skybox,
                "Camera is still using a solid ground-fog color at 1,000,000 m altitude instead of the starfield skybox");

            Debug.Log("ALTITUDE_FOG_CHECK_PASSED: fog and sky correctly clear at space altitude despite a tight camera zoom");
            SessionState.SetBool("AltitudeFogCheck", false);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ALTITUDE_FOG_CHECK_FAILED: " + e);
            SessionState.SetBool("AltitudeFogCheck", false);
            EditorApplication.Exit(1);
        }
    }
}
