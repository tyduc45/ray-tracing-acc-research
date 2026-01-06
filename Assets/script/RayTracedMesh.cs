using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class RayTracedMesh : MonoBehaviour
{
    // 用于在全局 Buffer 中定位
    [HideInInspector] public int triOffset;
    [HideInInspector] public int triCount;
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
    private void Update()
    {
        if (transform.hasChanged)
        {
            // 提示：你可以在管理器里加个简单的 Dirty 标记
            // RayTracingManager.Instance.MarkObjectsDirty();
            transform.hasChanged = false;
        }
    }
}