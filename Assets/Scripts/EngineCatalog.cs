using System;
using UnityEngine;

// STUDY: ==================== STUDY GUIDE: EngineCatalog ====================
// STUDY: WHAT: data asset - the list of engines the sandbox can place, each Entry an
// STUDY: id, title, prefab (the 3D model) and its height/diameter. The engine's
// STUDY: performance numbers come from EnginePerformance.Reference(id), not here.
// STUDY: Built by the Editor tool EngineCatalogSetup; read by
// STUDY: RocketAssemblyController.FindEngine.

[CreateAssetMenu(menuName = "Strauss Space/Engine Catalog")]
public sealed class EngineCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string title;
        public GameObject prefab;
        public float height;
        public float diameter;
    }
    public Entry[] engines;
}
