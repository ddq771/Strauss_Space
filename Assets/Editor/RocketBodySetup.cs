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
    private static readonly string[] Keys = { "Falcon9", "Starship", "Vostok", "SaturnV", "SpaceShuttle", "SLS", "Ariane5", "AtlasV" };

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
        GroupStages(key, instance.transform);
        var prefabPath = "Assets/Resources/RocketBodies/" + key + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
        Debug.Log("ROCKET_BODY_PREFAB " + key + " -> " + prefabPath);
    }

    [Serializable] private class StageFile { public float height_m; public StageEntry[] stages; }
    [Serializable] private class StageEntry { public string name; public float bottom_m, top_m; public string note; }

    /// <summary>
    /// Groups each stage's meshes (exported as "&lt;Stage&gt;__&lt;material&gt;" by
    /// ArtSource/rocket_asset.py) under a "Stage_&lt;Stage&gt;" object whose pivot
    /// sits at the stage's base, in the separation order listed in
    /// &lt;Key&gt;_stages.json - so staging can later detach a stage by its
    /// transform. Bodies without a stage file are left flat.
    /// </summary>
    private static void GroupStages(string key, Transform root)
    {
        var file = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/RocketBodies/" + key + "_stages.json");
        if (file == null) return;
        var data = JsonUtility.FromJson<StageFile>(file.text);
        var meshes = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in root) meshes.Add(child);
        foreach (var entry in data.stages)
        {
            var group = new GameObject("Stage_" + entry.name).transform;
            group.SetParent(root, false);
            // Model units are metres with the origin at the stack's centre.
            group.localPosition = new Vector3(0f, entry.bottom_m - data.height_m / 2f, 0f);
            foreach (var mesh in meshes)
                if (mesh.name.StartsWith(entry.name + "__", StringComparison.Ordinal))
                    mesh.SetParent(group, true);
        }
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
