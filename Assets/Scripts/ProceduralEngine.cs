using UnityEngine;

/// <summary>
/// Procedurally-built engine visual for engines with no imported model (see
/// EngineCatalogSetup, which builds every catalog entry either from an FBX
/// under Assets/Models or, for engines like this one, from here instead).
/// A simplified single combustion-chamber-and-bell shape stands in for the
/// real engine's chamber count - the same level of abstraction the rest of
/// the catalog already uses (e.g. RD-180's two real chambers are one prefab
/// with EngineParameters.nozzleCount=2 driving the physics, not two meshes).
/// Origin is the mount attachment point; geometry hangs down to -Y, matching
/// the imported engine prefabs' convention (EngineCatalogSetup orients each
/// FBX so MountPoint sits at the origin and ExhaustPoint faces -Y).
/// Self-configuring the same way Rocket/Atmosphere build their own meshes.
/// </summary>
public sealed class ProceduralEngine : MonoBehaviour
{
    [SerializeField] private float chamberDiameter = 1.1f;
    [SerializeField] private float chamberHeight = 0.9f;
    [SerializeField] private float nozzleExitDiameter = 1.5f;
    [SerializeField] private float nozzleHeight = 1.96f;
    [SerializeField] private Color color = new(0.32f, 0.34f, 0.36f);

    [Range(8, 64)]
    [SerializeField] private int segments = 24;

    private Material material;
    private float builtChamberDiameter = -1f, builtChamberHeight = -1f, builtNozzleExitDiameter = -1f, builtNozzleHeight = -1f;
    private int builtSegments = -1;

    private void Reset() => BuildVisual();
    private void Awake() => BuildVisual();

    private void OnValidate()
    {
#if UNITY_EDITOR
        // Same fix as Rocket.BuildVisual/Atmosphere.BuildShell: AddComponent
        // cannot run inside OnValidate's callstack.
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += DelayedBuild;
            return;
        }
#endif
        BuildVisual();
    }

#if UNITY_EDITOR
    private void DelayedBuild()
    {
        if (this == null) return;
        BuildVisual();
    }
#endif

    private void BuildVisual()
    {
        chamberDiameter = Mathf.Max(0.05f, chamberDiameter);
        chamberHeight = Mathf.Max(0.05f, chamberHeight);
        nozzleExitDiameter = Mathf.Max(chamberDiameter, nozzleExitDiameter);
        nozzleHeight = Mathf.Max(0.05f, nozzleHeight);
        segments = Mathf.Clamp(segments, 8, 64);

        if (builtChamberDiameter == chamberDiameter && builtChamberHeight == chamberHeight &&
            builtNozzleExitDiameter == nozzleExitDiameter && builtNozzleHeight == nozzleHeight &&
            builtSegments == segments && material != null)
        {
            return;
        }

        material ??= Rocket.CreateUnlitStandardMaterial(color, 0.35f);
        material.color = color;

        // Chamber: a short, near-cylindrical barrel just below the mount.
        BuildPart("Chamber", Vector3.zero,
            Rocket.BuildFrustum(chamberDiameter * 0.5f, chamberDiameter * 0.45f, chamberHeight, segments));
        // Nozzle: the flared bell below it, widening to the exit diameter.
        BuildPart("Nozzle", Vector3.down * chamberHeight,
            Rocket.BuildFrustum(chamberDiameter * 0.45f, nozzleExitDiameter * 0.5f, nozzleHeight, segments));

        builtChamberDiameter = chamberDiameter;
        builtChamberHeight = chamberHeight;
        builtNozzleExitDiameter = nozzleExitDiameter;
        builtNozzleHeight = nozzleHeight;
        builtSegments = segments;
    }

    private void BuildPart(string name, Vector3 localPosition, Mesh mesh)
    {
        var child = transform.Find(name);
        GameObject childObject;
        if (child == null)
        {
            childObject = new GameObject(name);
            childObject.transform.SetParent(transform, worldPositionStays: false);
            childObject.AddComponent<MeshFilter>();
            childObject.AddComponent<MeshRenderer>();
        }
        else
        {
            childObject = child.gameObject;
        }

        childObject.transform.localPosition = localPosition;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        childObject.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
