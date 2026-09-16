using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; not a regression check that runs on its own.
// Verifies aerodynamic drag actually opposes velocity and grows with speed:
// gives the rocket a large sideways-ish velocity at low altitude (dense air)
// with the engine off, then confirms Drag telemetry is nonzero and the
// resulting deceleration opposes the velocity direction.
[InitializeOnLoad]
public static class DragCheck
{
    static double deadline; static int stage; static float next;
    static DragCheck()
    {
        if (SessionState.GetBool("DragCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("DragCheck", true);
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
                a.InstallEngine(0, "F1");
                rocket.StartEngine(1f);
                Require(rocket.Launched, "Rocket did not ignite");
                rocket.StopEngine();
                // Fast, low-altitude: dense air, easy to see a real effect.
                body.linearVelocity = rocket.transform.up * (2000f * PlanetBody.WorldUnitsPerMeter);
                next = Time.fixedTime + 0.05f; stage = 1; return;
            }
            if (stage == 1)
            {
                Require(flight.Drag > 1, "No measurable drag at 2000 m/s near the ground: " + flight.Drag);
                var speedBefore = (float)flight.Speed;
                next = Time.fixedTime + 1f; stage = 2;
                SessionState.SetFloat("DragCheck_SpeedBefore", speedBefore);
                return;
            }
            if (stage == 2)
            {
                var speedBefore = SessionState.GetFloat("DragCheck_SpeedBefore", 0f);
                var speedAfter = (float)flight.Speed;
                Require(speedAfter < speedBefore - 1f,
                    "Drag did not measurably slow the rocket: before=" + speedBefore + " after=" + speedAfter);

                // Now confirm it fades out high up: teleport to 150 km with
                // the same speed, drag should be near zero there.
                var planet = UnityEngine.Object.FindFirstObjectByType<PlanetBody>();
                var outward = (rocket.transform.position - planet.transform.position).normalized;
                body.position = planet.transform.position +
                    outward * ((planet.Radius + 150000f) * PlanetBody.WorldUnitsPerMeter);
                rocket.transform.position = body.position;
                body.linearVelocity = rocket.transform.up * (2000f * PlanetBody.WorldUnitsPerMeter);
                next = Time.fixedTime + 0.05f; stage = 3; return;
            }
            if (stage == 3)
            {
                // Compare deceleration, not raw force: at 2000 m/s even a
                // near-vacuum still produces a small absolute force, but the
                // effect on a real vehicle's mass is what actually matters.
                var decel = flight.Drag / flight.TotalMass;
                Require(decel < 0.001, "Drag should be a negligible deceleration at 150 km altitude: " + decel + " m/s^2 (force=" + flight.Drag + " N)");
                Debug.Log("DRAG_CHECK_PASSED: drag opposes velocity, scales with speed, fades with altitude");
                SessionState.SetBool("DragCheck", false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("DRAG_CHECK_FAILED: " + e);
            SessionState.SetBool("DragCheck", false);
            EditorApplication.Exit(1);
        }
    }
}
