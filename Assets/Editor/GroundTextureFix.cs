using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off utility, not a regression check: gives the "Kenya Surface Frame"
// ground patch real UVs and the same satellite texture the planet itself
// uses, instead of a flat, unrelated placeholder color. The patch is a
// separate precision-friendly mesh (see PlanetAssemblySetup.CreateGround) so
// close-up assembly work doesn't suffer float precision loss from the
// planet's true ~6,378 km-radius vertex coordinates, but with no UVs and a
// flat olive-gray material it reads as a disconnected object dropped onto
// the real Earth rather than a patch of the same surface.
public static class GroundTextureFix
{
    public static void Apply()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlanetScene.unity");
        var planet = Object.FindFirstObjectByType<PlanetBody>();
        var frame = GameObject.Find("Kenya Surface Frame").transform;
        var ground = GameObject.Find("Ground");
        var meshFilter = ground.GetComponent<MeshFilter>();
        var mesh = meshFilter.sharedMesh;

        var vertices = mesh.vertices;
        var uvs = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            // Ground's local transform is identity relative to its parent
            // (the frame), so a mesh-local vertex maps to world space via
            // the frame's own rotation/position/scale - see
            // PlanetAssemblySetup.Build, which parents Ground directly under
            // "Kenya Surface Frame" with zeroed local position/rotation and
            // unit local scale.
            var worldPos = frame.position + frame.rotation * (vertices[i] * frame.localScale.x);
            var normal = (worldPos - planet.transform.position).normalized;
            var lat = Mathf.Asin(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
            var lon = Mathf.Atan2(normal.z, normal.x) * Mathf.Rad2Deg;
            // Matches PlanetBody.BuildUvSphere's convention exactly, so the
            // patch samples the same texture region the sphere itself shows
            // just past the patch's edge, closing the seam.
            var u = lon / 360f + 0.5f;
            var v = 0.5f + lat / 180f;
            uvs[i] = new Vector2(u, v);
        }
        mesh.uv = uvs;
        EditorUtility.SetDirty(mesh);

        var earthMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/New Material.mat");
        var groundMaterialPath = "Assets/Materials/Assembly/Ground.mat";
        var groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(groundMaterialPath);
        groundMaterial.shader = earthMaterial.shader;
        groundMaterial.SetTexture("_MainTex", earthMaterial.GetTexture("_MainTex"));
        groundMaterial.SetColor("_Color", Color.white);
        groundMaterial.SetFloat("_Glossiness", 0.18f);
        EditorUtility.SetDirty(groundMaterial);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(ground.scene);
        EditorSceneManager.SaveScene(ground.scene);

        Debug.Log("GROUND_TEXTURE_FIX_DONE");
    }
}
