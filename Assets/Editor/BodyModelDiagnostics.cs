using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// One-shot diagnostic: runs after a script reload when Temp/run_body_diag
// exists, and logs what each RocketBodies prefab actually contains.
[InitializeOnLoad]
public static class BodyModelDiagnostics
{
    private const string Flag = "Temp/run_body_diag";

    static BodyModelDiagnostics()
    {
        if (!File.Exists(Flag)) return;
        File.Delete(Flag);
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Strauss Space/Diagnose Rocket Body Prefabs")]
    public static void Run()
    {
        foreach (var key in new[] { "Falcon9", "Starship", "Vostok", "SaturnV", "SpaceShuttle" })
        {
            var prefab = Resources.Load<GameObject>("RocketBodies/" + key);
            if (prefab == null) { Debug.Log("BODY_DIAG " + key + " PREFAB_MISSING"); continue; }
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            var missing = filters.Count(f => f.sharedMesh == null);
            var verts = filters.Where(f => f.sharedMesh != null).Sum(f => f.sharedMesh.vertexCount);
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds();
            var first = true;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var wb = new Bounds(r.transform.TransformPoint(mb.center), Vector3.Scale(mb.size, r.transform.lossyScale));
                if (first) { b = wb; first = false; } else b.Encapsulate(wb);
            }
            var mats = renderers.SelectMany(r => r.sharedMaterials).ToArray();
            var nullMats = mats.Count(m => m == null);
            var shaders = string.Join(",", mats.Where(m => m != null).Select(m => m.shader.name).Distinct());
            var inactive = prefab.GetComponentsInChildren<Transform>(true).Count(t => !t.gameObject.activeSelf);
            Debug.Log("BODY_DIAG " + key + " filters=" + filters.Length + " missingMeshes=" + missing +
                " verts=" + verts + " renderers=" + renderers.Length + " disabledRenderers=" + renderers.Count(r => !r.enabled) +
                " inactiveObjs=" + inactive + " nullMats=" + nullMats + " shaders=" + shaders + " texturedMats=" + mats.Count(m => m != null && m.mainTexture != null) + "/" + mats.Length + " colors=" + string.Join(",", mats.Where(m => m != null).Select(m => ColorUtility.ToHtmlStringRGB(m.color)).Distinct().Take(6)) +
                " rootScale=" + prefab.transform.localScale + " size=" + b.size + " center=" + b.center);
        }
        Debug.Log("BODY_DIAG_DONE");
    }
}
