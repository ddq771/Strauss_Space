using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off diagnostic, not a regression check: measures which way the rocket
// actually rotates in response to a gimbal command, to determine whether
// keyboard pitch/yaw ("reversed" per the bug report) match what a player
// would expect from pressing W/Up (nose should swing toward local +Z) and
// D/Right (nose should swing toward local +X).
[InitializeOnLoad]
public static class GimbalDirectionCheck
{
    static double deadline; static int stage; static float next; static float holdUntil;
    static GimbalDirectionCheck()
    {
        if (SessionState.GetBool("GimbalDirectionCheck", false))
        { deadline = EditorApplication.timeSinceStartup + 60; EditorApplication.update += Tick; }
    }

    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("GimbalDirectionCheck", true);
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
            var body = rocket.GetComponent<Rigidbody>();

            if (Time.fixedTime < next) return;

            if (stage == 0)
            {
                a.InstallEngine(0, "F1");
                a.SetFrame(true, true); // frame installed, gimballed
                rocket.StartEngine(1f);
                Debug.Log("PRE_LAUNCH_UP=" + rocket.transform.up + " FORWARD=" + rocket.transform.forward +
                    " RIGHT=" + rocket.transform.right);
                next = Time.fixedTime + 0.3f; stage = 1; holdUntil = Time.fixedTime + 0.3f + 1.5f; return;
            }
            if (stage == 1)
            {
                // UpdateFlightGimbals() runs every frame while launched and
                // continuously re-derives its target angle from raw keyboard
                // state (zero, since nothing is pressed here), decaying any
                // one-shot SetGimbal call back toward zero within a fraction
                // of a second. A held key keeps re-asserting a nonzero
                // target every frame, so emulate that by re-issuing the
                // command every tick instead of once.
                a.SetGimbal(0, new Vector2(5f, 0f));
                if (Time.fixedTime > holdUntil)
                {
                    var localAngularVel = rocket.transform.InverseTransformDirection(body.angularVelocity);
                    Debug.Log("AFTER_HELD_PITCH_INPUT local angularVelocity=" + localAngularVel +
                        " (world=" + body.angularVelocity + ")" +
                        " THRUST_DIR=" + a.GetThrustDirection(0) + " COM_WORLD=" + body.worldCenterOfMass +
                        " SOCKET_POS=" + a.GetSocketPosition(0));
                    a.SetGimbal(0, Vector2.zero);
                    body.angularVelocity = Vector3.zero;
                    next = Time.fixedTime + 0.3f; holdUntil = Time.fixedTime + 0.3f + 1.5f; stage = 2;
                }
                return;
            }
            if (stage == 2)
            {
                a.SetGimbal(0, new Vector2(0f, 5f));
                if (Time.fixedTime > holdUntil)
                {
                    var localAngularVel = rocket.transform.InverseTransformDirection(body.angularVelocity);
                    Debug.Log("AFTER_HELD_YAW_INPUT local angularVelocity=" + localAngularVel +
                        " (world=" + body.angularVelocity + ")" +
                        " THRUST_DIR=" + a.GetThrustDirection(0));
                    a.SetGimbal(0, Vector2.zero);
                    body.angularVelocity = Vector3.zero;
                    next = Time.fixedTime + 0.3f; holdUntil = Time.fixedTime + 0.3f + 1.5f; stage = 3;
                }
                return;
            }
            if (stage == 3)
            {
                // Stands in for what UpdateFlightGimbals now sends to the
                // mount when W is held (flightGimbal=+5, negated to -5) -
                // confirms the fixed pipeline swings the nose toward +X,
                // matching the key pressed, not away from it.
                a.SetGimbal(0, new Vector2(-5f, 0f));
                if (Time.fixedTime > holdUntil)
                {
                    var localAngularVel = rocket.transform.InverseTransformDirection(body.angularVelocity);
                    Debug.Log("AFTER_FIXED_PITCH_COMMAND local angularVelocity=" + localAngularVel +
                        " (world=" + body.angularVelocity + ")");
                    Debug.Log("GIMBAL_DIRECTION_CHECK_DONE");
                    SessionState.SetBool("GimbalDirectionCheck", false);
                    EditorApplication.Exit(0);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("GIMBAL_DIRECTION_CHECK_FAILED: " + e);
            SessionState.SetBool("GimbalDirectionCheck", false);
            EditorApplication.Exit(1);
        }
    }
}
