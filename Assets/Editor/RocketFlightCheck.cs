using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RocketFlightCheck
{
    static double deadline;static int stage;static float next;static float speed;static Vector3 position,up;
    static RocketFlightCheck()
    {
        if(SessionState.GetBool("RocketFlightCheck",false)){deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=Tick;}
    }
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        if(!Application.isBatchMode){EditorApplication.ExecuteMenuItem("Window/General/Game");if(EditorWindow.focusedWindow!=null)EditorWindow.focusedWindow.maximized=true;}
        SessionState.SetBool("RocketFlightCheck",true);EditorApplication.isPlaying=true;
    }
    static void Require(bool test,string message){if(!test)throw new Exception(message);}
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out");
            if(!EditorApplication.isPlaying)return;
            var a=UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();if(a==null || a.FuelTank==null)return;
            var r=a.GetComponent<Rocket>();var f=a.GetComponent<RocketFlightModel>();var body=r.GetComponent<Rigidbody>();
            if(Time.fixedTime<next)return;
            if(stage==0)
            {
                foreach(var id in new[]{"F1","RD180","RS25","Merlin1D","Raptor2"})
                {
                    var p=EnginePerformance.Reference(id);var vac=EnginePerformance.Evaluate(p,1,0);var sea=EnginePerformance.Evaluate(p,1,101325);
                    Require(vac.valid && sea.valid && vac.thrust>sea.thrust,"Invalid baseline "+id);
                    Require(Math.Abs(vac.massFlow-sea.massFlow)<1e-6,"Pressure changes prescribed mass flow");
                    Require(Math.Abs(vac.fuelFlow+vac.oxidizerFlow-vac.massFlow)<1e-6,"Mixture flow does not conserve mass");
                }
                a.InstallEngine(0,"F1");a.FocusRocket();up=r.transform.up;position=r.transform.position;
                r.StartEngine();Require(r.EngineEnabled && f.TWR>1,"F-1 failed to ignite with TWR > 1");
                Require(body.mass>r.StructuralMass+f.FuelRemaining+f.OxidizerRemaining,"Engine/tank mass missing");
                next=Time.fixedTime+2;stage=1;return;
            }
            if(stage==1)
            {
                var height=Vector3.Dot(r.transform.position-position,up)/PlanetBody.WorldUnitsPerMeter;
                speed=Vector3.Dot(body.linearVelocity,up)/PlanetBody.WorldUnitsPerMeter;
                Require(height>2 && speed>1 && speed<100,"Powered ascent or unit scale invalid: height="+height+" speed="+speed);
                Require(f.FuelRemaining<100*810 && f.OxidizerRemaining<180*1141,"Propellants not consumed");
                if(!Application.isBatchMode)
                {
                    System.IO.Directory.CreateDirectory("Reports");ScreenCapture.CaptureScreenshot("Reports/RocketFlight.png");
                    next=Time.fixedTime+.4f;stage=10;return;
                }
                r.StopEngine();position=r.transform.position;next=Time.fixedTime+1;stage=2;return;
            }
            if(stage==10)
            {
                speed=Vector3.Dot(body.linearVelocity,up)/PlanetBody.WorldUnitsPerMeter;
                r.StopEngine();position=r.transform.position;next=Time.fixedTime+1;stage=2;return;
            }
            if(stage==2)
            {
                var current=Vector3.Dot(body.linearVelocity,up)/PlanetBody.WorldUnitsPerMeter;
                Require(r.Launched && !body.isKinematic,"Shutdown reclamped rocket");
                Require(speed-current>7 && speed-current<12,"Gravity coast acceleration invalid: "+(speed-current));
                Require(Vector3.Distance(r.transform.position,position)<.1f,"Shutdown teleported rocket");
                r.ReturnToAssembly();a.SetFrame(true,false);
                var p=a.GetParameters(0).Copy();p.vacuumThrust=1100000;a.SetParameters(0,p);
                position=r.transform.position;r.StartEngine();Require(r.EngineEnabled && f.TWR<1,"Underpowered test not set up");
                next=Time.fixedTime+1;stage=3;return;
            }
            if(stage==3)
            {
                var height=Vector3.Dot(r.transform.position-position,up)/PlanetBody.WorldUnitsPerMeter;
                Require(height<.5,"Insufficient thrust lifted rocket: "+height);
                r.ReturnToAssembly();a.InstallEngine(0,"F1");a.SetFrame(true,true);a.SetGimbal(0,new Vector2(5,0));
                r.StartEngine();next=Time.fixedTime+.5f;stage=4;return;
            }
            if(stage==4)
            {
                Require(body.angularVelocity.magnitude>.001f,"Gimballed thrust creates no torque");
                r.ReturnToAssembly();a.SetFrame(true,false);f.SetFill(.000001f);r.StartEngine();next=Time.fixedTime+.1f;stage=5;return;
            }
            if(stage==5)
            {
                Require(!r.EngineEnabled && f.FuelRemaining>=0 && f.OxidizerRemaining>=0,"Depletion failed");
                Require(r.Launched,"Depletion teleported/clamped rocket");
                Debug.Log("ROCKET_FLIGHT_CHECK_PASSED: five baselines, pressure response, mass accounting, powered ascent, scaled units, gravity coast, underpowered pad hold, gimbal torque, propellant depletion");
                SessionState.SetBool("RocketFlightCheck",false);EditorApplication.Exit(0);
            }
        }
        catch(Exception e){Debug.LogError("ROCKET_FLIGHT_CHECK_FAILED: "+e);SessionState.SetBool("RocketFlightCheck",false);EditorApplication.Exit(1);}
    }
}
