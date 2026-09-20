using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports the procedurally-built rocket body FBX models (see
/// ArtSource/*/build_*.py - generated with Blender, not downloaded/licensed
/// assets) and saves each as a prefab under Assets/Resources/RocketBodies,
/// so RocketPresets can load them at runtime via Resources.Load the same
/// way EngineCatalog is loaded. Mirrors EngineCatalogSetup's FBX-to-prefab
/// pattern, minus the engine-specific mount/exhaust/material handling that
/// doesn't apply to a body mesh.
/// </summary>
public static class RocketBodySetup
{
    private static readonly string[] Keys = { "Falcon9", "Starship", "Vostok", "SaturnV", "SpaceShuttle" };

    [MenuItem("Strauss Space/Rebuild Rocket Body Prefabs")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Resources/RocketBodies");
        AssetDatabase.Refresh();
        foreach (var key in Keys)
        {
            var fbxPath = "Assets/Models/" + key + "/" + key + "_Body.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new Exception("Missing rocket body model: " + fbxPath);

            // Sanity check the same convention the Blender build script
            // promises: a single "OriginPoint" child marking the vertical
            // centre, so callers can parent this directly with no offset.
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
        AssetDatabase.SaveAssets();
        Debug.Log("ROCKET_BODY_SETUP_PASSED");
    }
}
