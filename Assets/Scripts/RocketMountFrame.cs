using UnityEngine;

public sealed class RocketMountFrame : MonoBehaviour
{
    public const float PreviewAngleLimit=10f;
    public bool CanGimbal { get; private set; }
    public Transform[] Mounts { get; private set; }
    public void Configure(bool gimballed,Transform[] mounts)
    { CanGimbal=gimballed; Mounts=mounts; }
    public void SetAngle(int index,Vector2 degrees)
    {
        if(index<0 || index>=Mounts.Length)return;
        degrees=Vector2.ClampMagnitude(degrees,PreviewAngleLimit);
        Mounts[index].localRotation=CanGimbal ? Quaternion.Euler(degrees.x,0,degrees.y) : Quaternion.identity;
    }
}
