using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; not run automatically. Actually launches a
// preset (not just checking static TWR) and steps physics forward to see
// if it really leaves the ground - reproduces "things don't fly" concretely.
[InitializeOnLoad]
public static class PresetFlightCheck
{
    static double deadline; static int stage; static float next; static Vector3 startPos; static int presetIndex;
    static PresetFlightCheck()
    {
        if (SessionState.GetBool("PresetFlightCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("PresetFlightCheck", true);
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
            var flight = a.GetComponent<RocketFlightModel>();
            var body = rocket.GetComponent<Rigidbody>();

            if (Time.fixedTime < next) return;

            if (stage == 0)
            {
                if (presetIndex >= RocketPresets.All.Length)
                {
                    Debug.Log("PRESET_FLIGHT_CHECK_PASSED");
                    SessionState.SetBool("PresetFlightCheck", false);
                    EditorApplication.Exit(0);
                    return;
                }
                a.LoadPreset(presetIndex);
                startPos = rocket.transform.position;
                Debug.Log("PRE_LAUNCH " + RocketPresets.All[presetIndex].name + " pos=" + startPos +
                    " isKinematic=" + body.isKinematic + " useGravity=" + body.useGravity + " mass=" + body.mass);
                next = Time.fixedTime + 0.1f; stage = 1; return;
            }
            if (stage == 1)
            {
                rocket.StartEngine(1f);
                Debug.Log("AFTER_START_ENGINE Launched=" + rocket.Launched + " EngineEnabled=" + rocket.EngineEnabled +
                    " Status=" + flight.Status + " Thrust=" + flight.Thrust + " TWR=" + flight.TWR +
                    " isKinematic=" + body.isKinematic);
                next = Time.fixedTime + 2f; stage = 2; return;
            }
            if (stage == 2)
            {
                var moved = Vector3.Distance(rocket.transform.position, startPos) / PlanetBody.WorldUnitsPerMeter;
                var name = RocketPresets.All[presetIndex].name;
                Debug.Log("AFTER_2S " + name + " pos=" + rocket.transform.position + " movedMeters=" + moved +
                    " velocity=" + body.linearVelocity + " angularVelocity=" + body.angularVelocity +
                    " Launched=" + rocket.Launched + " EngineEnabled=" + rocket.EngineEnabled + " Status=" + flight.Status +
                    " Thrust=" + flight.Thrust + " TWR=" + flight.TWR + " Altitude=" + flight.Altitude);
                if (flight.TWR >= 1.0)
                    Require(moved > 1, name + ": did not move after 2s despite TWR=" + flight.TWR + ", status=" + flight.Status);
                else
                    Debug.Log(name + ": TWR<1, expected not to lift off");
                rocket.ReturnToAssembly();
                presetIndex++;
                stage = 0;
                next = Time.fixedTime + 0.2f;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("PRESET_FLIGHT_CHECK_FAILED: " + e);
            SessionState.SetBool("PresetFlightCheck", false);
            EditorApplication.Exit(1);
        }
    }
}
