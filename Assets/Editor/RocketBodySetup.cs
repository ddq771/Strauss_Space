using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the rocket body FBX models (procedural ones from
/// ArtSource/*/build_*.py, downloaded ones converted by
/// ArtSource/import_body_model.py - see ArtSource/CREDITS.md) and saves
/// each as a prefab under Assets/Resources/RocketBodies, so RocketPresets
/// can load them at runtime via Resources.Load the same way EngineCatalog
/// is loaded. Mirrors EngineCatalogSetup's FBX-to-prefab pattern, minus
/// the engine-specific mount/exhaust/material handling that doesn't apply
/// to a body mesh.
/// </summary>
public static class RocketBodySetup
{
    private static readonly string[] Keys = { "Falcon9", "Starship", "Vostok", "SaturnV", "SpaceShuttle" };

    [MenuItem("Strauss Space/Rebuild Rocket Body Prefabs")]
    public static void Build()
    {
        foreach (var key in Keys) BuildOne(key);
        AssetDatabase.SaveAssets();
        Debug.Log("ROCKET_BODY_SETUP_PASSED");
    }

    public static void BuildOne(string key)
    {
        Directory.CreateDirectory("Assets/Resources/RocketBodies");
        var fbxPath = "Assets/Models/" + key + "/" + key + "_Body.fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) throw new Exception("Missing rocket body model: " + fbxPath);

        // Sanity check the same convention the Blender scripts promise: a
        // single "OriginPoint" child marking the vertical centre, so
        // callers can parent this directly with no offset.
        var hasOrigin = false;
        foreach (var t in model.GetComponentsInChildren<Transform>())
            if (t.name == "OriginPoint") hasOrigin = true;
        if (!hasOrigin) throw new Exception(key + " body model has no OriginPoint marker");

        // SaveAsPrefabAsset needs a scene instance, not the FBX asset
        // reference itself - instantiate, save, then clean up the
        // temporary scene copy.
        var instance = UnityEngine.Object.Instantiate(model);
        instance.name = key;
        var prefabPath = "Assets/Resources/RocketBodies/" + key + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
        Debug.Log("ROCKET_BODY_PREFAB " + key + " -> " + prefabPath);
    }

    /// <summary>
    /// Re-exporting a body FBX renames its meshes, which leaves the old
    /// prefab pointing at meshes that no longer exist (an invisible body).
    /// Rebuild the matching prefab automatically whenever one is imported.
    /// </summary>
    private class Reimport : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                foreach (var key in Keys)
                    if (path == "Assets/Models/" + key + "/" + key + "_Body.fbx")
                        EditorApplication.delayCall += () => { BuildOne(key); AssetDatabase.SaveAssets(); };
        }
    }
}
