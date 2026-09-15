using System;
using UnityEngine;

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
