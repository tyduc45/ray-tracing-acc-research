using UnityEngine;

public class RayHitHighlighter : MonoBehaviour
{
    [Header("Settings")]
    public Material highlightMaterial; // 拖入使用了特定Shader的材质

    private ComputeBuffer _hitBuffer;

    private void OnEnable()
    {
        // 1. 初始化存储击中结果的 Buffer (1个int大小)
        // 使用 Structured 类型以便在 Shader 中读取
        _hitBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Structured);

        // 初始值设为 -1，表示没有击中任何三角形
        _hitBuffer.SetData(new int[] { -1 });

        // 2. 将此 Buffer 绑定到需要变红的材质上
        if (highlightMaterial != null)
        {
            highlightMaterial.SetBuffer("_HitResultBuffer", _hitBuffer);
        }

        // 3. 向单例 Manager 注册这个 Buffer
        // 这样 Manager 在每帧 Dispatch 时，就会把结果写入这里
        if (RayTracingManager.Instance != null)
        {
            RayTracingManager.Instance.RegisterHitBuffer(_hitBuffer);
        }
    }

    private void Update()
    {
        Debug.Log("run");
        RayTracingManager.Instance.RegisterHitBuffer(_hitBuffer);
    }

    private void OnDisable()
    {
        // 释放资源，防止内存泄漏
        if (_hitBuffer != null)
        {
            _hitBuffer.Release();
            _hitBuffer = null;
        }

        // 告诉 Manager 不再需要写入这个 Buffer
        if (RayTracingManager.Instance != null)
        {
            RayTracingManager.Instance.RegisterHitBuffer(null);
        }
    }
}