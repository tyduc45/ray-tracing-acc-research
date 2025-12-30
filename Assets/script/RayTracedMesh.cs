using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class RayTracedMesh : MonoBehaviour
{
    public MeshFilter meshFilter;

    void Reset()
    {
        meshFilter = GetComponent<MeshFilter>();
    }

    void OnEnable()
    {
        RayTracingManager.Register(this);
    }

    void OnDisable()
    {
        RayTracingManager.UnRegister(this);
    }
}