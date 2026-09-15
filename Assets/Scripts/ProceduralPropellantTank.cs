using System;
using UnityEngine;

// Dimensions are metres under the assembly's metre-to-world transform.
public sealed class ProceduralPropellantTank : MonoBehaviour
{
    public enum Contents { Fuel, Oxidizer }
    public Contents Kind { get; private set; }
    public float Capacity { get; private set; }
    public float Diameter { get; private set; }
    public float Height => Capacity / (Mathf.PI * Diameter * Diameter * .25f);
    public Transform TopMount { get; private set; }
    public Transform BottomMount { get; private set; }

    public void Configure(Contents kind, float capacity, float diameter, Material material)
    {
        if (!ValidDimensions(capacity, diameter)) throw new ArgumentOutOfRangeException(nameof(capacity));
        Kind=kind; Capacity=capacity; Diameter=diameter;
        var hull=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hull.name=kind==Contents.Fuel ? "Fuel vessel" : "Oxidizer vessel";
        hull.transform.SetParent(transform,false);
        hull.transform.localPosition=Vector3.up*Height*.5f;
        hull.transform.localScale=new Vector3(diameter,Height*.5f,diameter);
        hull.GetComponent<Renderer>().sharedMaterial=material;
        var collider=hull.GetComponent<Collider>(); collider.enabled=false; Destroy(collider);
        BottomMount=new GameObject("Bottom attachment").transform;
        BottomMount.SetParent(transform,false);
        TopMount=new GameObject("Top attachment").transform;
        TopMount.SetParent(transform,false); TopMount.localPosition=Vector3.up*Height;
    }

    public static bool ValidDimensions(float volume,float diameter)
    {
        // Finite practical bounds keep mesh, camera and collider precision usable.
        if(float.IsNaN(volume)||float.IsInfinity(volume)||float.IsNaN(diameter)||float.IsInfinity(diameter))return false;
        var height=volume/(Mathf.PI*diameter*diameter*.25f);
        return volume>=.01f && volume<=100000f && diameter>=.2f && diameter<=30f && height>=.05f && height<=500f;
    }
}
