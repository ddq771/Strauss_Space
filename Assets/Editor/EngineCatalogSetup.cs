using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EngineCatalogSetup
{
    [MenuItem("Strauss Space/Rebuild Component Catalog")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Prefabs/Engines");
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Materials/Engines");
        AssetDatabase.Refresh();
        var entries=new List<EngineCatalog.Entry>();
        foreach(var key in new[]{"F1","RD180","RS25","Merlin1D","Raptor2"})
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/"+key+"/"+key+"_Engine.fbx");
            if(model==null)throw new Exception("Missing engine "+key);
            var root=new GameObject(key);
            var instance=UnityEngine.Object.Instantiate(model,root.transform);
            Transform mount=null; var exhaust=Vector3.zero; var count=0;
            foreach(var t in instance.GetComponentsInChildren<Transform>())
            {
                if(t.name=="MountPoint")mount=t;
                if(t.name.StartsWith("ExhaustPoint")){exhaust+=t.position;count++;}
            }
            if(mount==null || count==0)throw new Exception("Missing anchors "+key);
            var delta=exhaust/count-mount.position;
            instance.transform.rotation=Quaternion.FromToRotation(delta,Vector3.down)*instance.transform.rotation;
            instance.transform.position-=mount.position;
            var renderers=instance.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;
            foreach(var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                var materials=r.sharedMaterials;
                for(var i=0;i<materials.Length;i++)
                {
                    var source=materials[i];
                    var path="Assets/Materials/Engines/"+key+"_"+source.name+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(material==null){material=new Material(Shader.Find("Strauss Space/Engine Surface"));AssetDatabase.CreateAsset(material,path);}
                    material.color=source.color;material.SetFloat("_Metallic",.7f);material.SetFloat("_Glossiness",.4f);
                    EditorUtility.SetDirty(material);materials[i]=material;
                }
                r.sharedMaterials=materials;
            }
            if(bounds.size.y<.5f || bounds.size.y>20 || bounds.min.y>=0)throw new Exception("Unexpected engine scale "+key+": "+bounds);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Engines/"+key+".prefab");
            var title=key=="F1"?"F-1":key=="RD180"?"RD-180":key=="RS25"?"RS-25":key=="Merlin1D"?"Merlin 1D":"Raptor 2";
            entries.Add(new EngineCatalog.Entry{id=key,title=title,prefab=prefab,height=-bounds.min.y,diameter=Mathf.Max(bounds.size.x,bounds.size.z)});
            Debug.Log("ENGINE_CATALOG "+key+" height="+(-bounds.min.y)+" diameter="+Mathf.Max(bounds.size.x,bounds.size.z));
            UnityEngine.Object.DestroyImmediate(root);
        }
        var catalog=AssetDatabase.LoadAssetAtPath<EngineCatalog>("Assets/Resources/EngineCatalog.asset");
        if(catalog==null){catalog=ScriptableObject.CreateInstance<EngineCatalog>();AssetDatabase.CreateAsset(catalog,"Assets/Resources/EngineCatalog.asset");}
        catalog.engines=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        var rocket=UnityEngine.Object.FindFirstObjectByType<Rocket>();
        if(rocket==null)throw new Exception("Rocket not found");
        if(rocket.GetComponent<RocketAssemblyController>()==null)rocket.gameObject.AddComponent<RocketAssemblyController>();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("COMPONENT_CATALOG_BUILD_PASSED");
    }
}
