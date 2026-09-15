using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit batch entry point; never runs automatically in the user's editor.
[InitializeOnLoad]
public static class ComponentAssemblyCheck
{
    static double deadline; static int step; static double next;
    static ComponentAssemblyCheck()
    {
        if(SessionState.GetBool("ComponentAssemblyCheck",false))
        {deadline=EditorApplication.timeSinceStartup+60;EditorApplication.update+=Tick;}
    }
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        if(!Application.isBatchMode)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if(EditorWindow.focusedWindow!=null)EditorWindow.focusedWindow.maximized=true;
        }
        SessionState.SetBool("ComponentAssemblyCheck",true);EditorApplication.isPlaying=true;
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timed out");
            if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup<next)return;
            var a=UnityEngine.Object.FindFirstObjectByType<RocketAssemblyController>();
            if(a==null || a.FuelTank==null)return;
            if(step==0)
            {
                var h=a.FuelTank.Height;var oh=a.OxidizerTank.Height;
                a.SetTank(false,true,200,3.7f);
                Require(Mathf.Abs(a.FuelTank.Height/h-2)<.001f,"Fuel volume did not rebuild height");
                Require(Mathf.Abs(a.OxidizerTank.Height-oh)<.001f,"Fuel edit altered oxidizer");
                Require(Vector3.Distance(a.FuelTank.TopMount.position,a.OxidizerTank.BottomMount.position)<.000001f,"Tank attachments separated");
                a.UndoChange();Require(Mathf.Abs(a.FuelTank.Height-h)<.001f,"Undo tank geometry");
                Require(!ProceduralPropellantTank.ValidDimensions(float.NaN,3.7f),"NaN accepted");
                Require(!ProceduralPropellantTank.ValidDimensions(100,0),"Zero diameter accepted");
                a.SetFrame(true,true);a.SetSocketCount(7);
                var keys=new[]{"F1","RD180","RS25","Merlin1D","Raptor2","Merlin1D","Raptor2"};
                for(var i=0;i<keys.Length;i++)a.InstallEngine(i,keys[i]);
                Require(a.InstalledCount==7,"Mixed cluster incomplete");
                a.SelectEngine(0);
                var selected=a.SelectedPart;Require(selected!=null,"Engine selection failed");
                var surface=selected.GetComponentInChildren<Renderer>();
                var props=new MaterialPropertyBlock();surface.GetPropertyBlock(props,0);
                Require(!props.isEmpty,"Selection highlight missing");
                a.SelectPart(null);surface.GetPropertyBlock(props,0);Require(props.isEmpty,"Selection highlight not cleared");
                var box=selected.GetComponent<BoxCollider>();var center=selected.transform.TransformPoint(box.center);
                Physics.SyncTransforms();
                var rayOrigin=selected.transform.TransformPoint(box.center+Vector3.right*(box.size.x*.5f+.01f));
                a.SelectAtRay(new Ray(rayOrigin,-selected.transform.right),1f);
                Require(a.SelectedPart==selected,"Ray click did not select the engine");
                a.SelectEngine(0);a.DeleteSelection();Require(a.GetEngineId(0)==null && a.InstalledCount==6,"Delete selected engine failed");
                a.UndoChange();Require(a.InstalledCount==7,"Undo selected deletion failed");
                var fuelPart=a.FuelTank.GetComponent<AssemblySelectable>();a.SelectPart(fuelPart);a.DeleteSelection();
                Require(a.FuelTank==null && a.OxidizerTank!=null,"Delete selected tank affected wrong component");
                a.UndoChange();Require(a.FuelTank!=null,"Undo selected tank deletion failed");
                var framePart=a.GetComponentInChildren<RocketMountFrame>().GetComponent<AssemblySelectable>();
                a.SelectPart(framePart);a.DeleteSelection();Require(!a.FrameInstalled && a.InstalledCount==0,"Delete selected frame failed");
                a.UndoChange();Require(a.FrameInstalled && a.InstalledCount==7,"Undo frame selection deletion failed");
                Debug.Log("COMPONENT_SELECTION_CHECK_PASSED: ray picking, color highlight, clearing, Delete engine/tank/frame, Undo");
                var up=a.transform.up;a.SetGimbal(0,new Vector2(8,0));
                Require(Vector3.Angle(up,a.GetThrustDirection(0))>7.9f,"Gimbal did not rotate thrust");
                Require(Vector3.Angle(up,a.GetThrustDirection(1))<.01f,"Gimbal moved another engine");
                a.SetFrame(true,false);a.SetGimbal(0,new Vector2(8,0));
                Require(Vector3.Angle(up,a.GetThrustDirection(0))<.01f,"Fixed frame rotates");
                var file=Path.Combine(Path.GetTempPath(),"strauss-component-check-"+Guid.NewGuid()+".json");
                a.SaveLayout(file);a.RemoveEngine(3);Require(a.InstalledCount==6,"Removal failed");
                a.LoadLayout(file);File.Delete(file);Require(a.InstalledCount==7,"Save/load lost engines");
                a.SetFrame(false,false);Require(a.InstalledCount==0 && a.SocketCount==0,"Frame removal failed");
                a.UndoChange();Require(a.InstalledCount==7,"Frame undo failed");
                a.SetTank(false,false,100,3.7f);Require(a.FuelTank==null,"Tank removal failed");
                a.UndoChange();Require(a.FuelTank!=null,"Tank removal undo failed");
                a.SetSocketCount(3);a.InstallEngine(0,"RS25");a.InstallEngine(1,"Merlin1D");a.InstallEngine(2,"Raptor2");
                a.SetFrame(true,true);a.SetGimbal(2,new Vector2(8,0));a.FocusRocket();
                step=1;next=EditorApplication.timeSinceStartup+3;
                Debug.Log("COMPONENT_BEHAVIOR_CHECK_PASSED: independent volumes, anchors, all five models, mixed mounts, gimbal, fixed frame, save/load, undo");
                return;
            }
            if(step==1)
            {
                Directory.CreateDirectory("Reports");
                var camera=UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>().GetComponent<Camera>();
                Render(camera,"Reports/ComponentAssembly.png");
                if(!Application.isBatchMode)ScreenCapture.CaptureScreenshot("Reports/ComponentAssemblyMenu.png");
                a.FocusCluster();step=2;next=EditorApplication.timeSinceStartup+2;return;
            }
            if(step==2)
            {
                var view=UnityEngine.Object.FindFirstObjectByType<AssemblyViewCamera>();
                Render(view.GetComponent<Camera>(),"Reports/EngineCluster.png");
                view.ShowPlanet();
                var p=view.GetComponent<Camera>().WorldToViewportPoint(UnityEngine.Object.FindFirstObjectByType<PlanetBody>().transform.position);
                Require(p.z>0 && Mathf.Abs(p.x-.5f)<.01f && Mathf.Abs(p.y-.5f)<.01f,"Earth view regressed");
                Debug.Log("COMPONENT_ASSEMBLY_CHECK_PASSED");
                SessionState.SetBool("ComponentAssemblyCheck",false);EditorApplication.Exit(0);
            }
        }
        catch(Exception e){Debug.LogError("COMPONENT_ASSEMBLY_CHECK_FAILED: "+e);SessionState.SetBool("ComponentAssemblyCheck",false);EditorApplication.Exit(1);}
    }
    static void Render(Camera camera,string path)
    {
        var rt=new RenderTexture(1600,1000,24);camera.targetTexture=rt;camera.Render();
        var old=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=old;
        UnityEngine.Object.Destroy(image);rt.Release();UnityEngine.Object.Destroy(rt);
    }
}
