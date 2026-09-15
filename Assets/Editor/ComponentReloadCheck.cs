using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ComponentReloadCheck
{
    static double deadline;
    static ComponentReloadCheck()
    {
        if(SessionState.GetBool("ComponentReloadCheck",false))
        {deadline=EditorApplication.timeSinceStartup+60;EditorApplication.update+=Tick;}
    }
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        SessionState.SetBool("ComponentReloadCheck",true);SessionState.SetInt("ComponentReloadStage",0);
        EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Reload test timed out");
            if(!EditorApplication.isPlaying)return;
            var a=UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();
            if(a==null || a.FuelTank==null)return;
            var stage=SessionState.GetInt("ComponentReloadStage",0);
            if(stage==0)
            {
                a.InstallEngine(0,"Raptor2");a.SetTank(false,true,123,3.7f);
                SessionState.SetInt("ComponentReloadStage",1);EditorUtility.RequestScriptReload();
                EditorApplication.update-=Tick;return;
            }
            if(a.InstalledCount!=1 || a.GetEngineId(0)!="Raptor2" || Mathf.Abs(a.FuelTank.Capacity-123)>.001f)
                throw new Exception("Assembly state was lost on script reload");
            a.FocusCluster();a.SelectEngine(0);
            var engine=a.SelectedPart;var box=engine.GetComponent<BoxCollider>();
            var camera=UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>().GetComponent<Camera>();
            var point=camera.WorldToScreenPoint(engine.transform.TransformPoint(box.center));
            a.SelectPart(null);a.SelectAtRay(camera.ScreenPointToRay(point),camera.farClipPlane);
            if(a.SelectedPart!=engine)throw new Exception("Engine is not selectable through the assembly camera after reload");
            a.DeleteSelection();if(a.InstalledCount!=0)throw new Exception("Delete failed after reload");
            Debug.Log("COMPONENT_RELOAD_CHECK_PASSED: layout preserved; engine picked through camera and deleted after script reload");
            SessionState.SetBool("ComponentReloadCheck",false);EditorApplication.Exit(0);
        }
        catch(Exception e)
        {
            Debug.LogError("COMPONENT_RELOAD_CHECK_FAILED: "+e);
            SessionState.SetBool("ComponentReloadCheck",false);EditorApplication.Exit(1);
        }
    }
}
