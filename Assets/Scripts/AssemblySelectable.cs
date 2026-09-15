using UnityEngine;

public sealed class AssemblySelectable : MonoBehaviour
{
    public enum PartKind { Engine, Frame, FuelTank, OxidizerTank }
    public PartKind Kind { get; private set; }
    public int Slot { get; private set; }
    private Renderer[] surfaces;

    public void Configure(PartKind kind,int slot,Renderer[] renderers)
    {
        Kind=kind;Slot=slot;surfaces=renderers;
        var bounds=new Bounds();var first=true;
        foreach(var r in renderers)
        {
            var b=r.localBounds;
            for(var i=0;i<8;i++)
            {
                var corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var p=transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
            }
        }
        if(first)return;
        var hitbox=gameObject.AddComponent<BoxCollider>();hitbox.isTrigger=true;
        hitbox.center=bounds.center;hitbox.size=Vector3.Max(bounds.size,Vector3.one*.02f);
    }

    public bool IntersectRay(Ray worldRay,out float distance)
    {
        distance=float.PositiveInfinity;
        if(surfaces==null)return false;
        foreach(var surface in surfaces)
        {
            if(surface==null || !surface.enabled)continue;
            var localOrigin=surface.transform.InverseTransformPoint(worldRay.origin);
            var localDirection=surface.transform.InverseTransformVector(worldRay.direction);
            var ray=new Ray(localOrigin,localDirection.normalized);
            if(surface.localBounds.IntersectRay(ray,out var localDistance))
            {
                var worldPoint=surface.transform.TransformPoint(ray.GetPoint(localDistance));
                distance=Mathf.Min(distance,Vector3.Distance(worldRay.origin,worldPoint));
            }
        }
        return !float.IsInfinity(distance);
    }

    public void Highlight(bool selected)
    {
        if(surfaces==null)return;
        foreach(var r in surfaces)
        {
            if(r==null)continue;
            var materials=r.sharedMaterials;
            for(var i=0;i<materials.Length;i++)
            {
                if(!selected){r.SetPropertyBlock(null,i);continue;}
                var block=new MaterialPropertyBlock();
                var original=materials[i]!=null && materials[i].HasProperty("_Color")?materials[i].color:Color.white;
                block.SetColor("_Color",Color.Lerp(original,new Color(.2f,.8f,1f),.4f));
                r.SetPropertyBlock(block,i);
            }
        }
    }
}
