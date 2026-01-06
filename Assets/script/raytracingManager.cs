using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RayTracingManager : MonoBehaviour
{
    public static RayTracingManager Instance { get; private set; }
    public ComputeShader rayTraceCS;

    private ComputeBuffer _hitBuffer;
    private static readonly List<RayTracedMesh> s_meshes = new();
    private static bool s_structureDirty = true; // 结构脏：物体增删

    private Camera _cam;
    private ComputeBuffer _objectBuffer;
    private ComputeBuffer _triangleBuffer;

    // 缓存数据
    private readonly List<GPUObject> _gpuObjects = new();
    private readonly List<GPUTriangle> _gpuTriangles = new();

    struct GPUObject
    {
        public Matrix4x4 worldToLocal; // 64 bytes
        public Vector4 localAABBMin;   // 16 bytes
        public Vector4 localAABBMax;   // 16 bytes
        public int triOffset;          // 4 bytes
        public int triCount;           // 4 bytes
        public int pad0, pad1;         // 8 bytes (合计 112 bytes)
    }

    struct GPUTriangle { public Vector4 A, B, C; }

    public static void Register(RayTracedMesh m) { s_meshes.Add(m); s_structureDirty = true; }
    public static void UnRegister(RayTracedMesh m) { s_meshes.Remove(m); s_structureDirty = true; }

    private void Awake() { Instance = this; _cam = GetComponent<Camera>(); }

    void BuildStructure()
    {
        if (!s_structureDirty && _triangleBuffer != null) return;

        _gpuTriangles.Clear();
        foreach (var rtm in s_meshes)
        {
            var mesh = rtm.GetComponent<MeshFilter>().sharedMesh;
            rtm.triOffset = _gpuTriangles.Count;
            rtm.triCount = mesh.triangles.Length / 3;

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                _gpuTriangles.Add(new GPUTriangle
                {
                    A = verts[tris[i]],
                    B = verts[tris[i + 1]],
                    C = verts[tris[i + 2]]
                });
            }
        }

        _triangleBuffer?.Release();
        _triangleBuffer = new ComputeBuffer(_gpuTriangles.Count, 48);
        _triangleBuffer.SetData(_gpuTriangles);
        s_structureDirty = false;
    }

    void UpdateObjectBuffer()
    {
        _gpuObjects.Clear();
        foreach (var rtm in s_meshes)
        {
            Mesh m = rtm.GetComponent<MeshFilter>().sharedMesh;
            _gpuObjects.Add(new GPUObject
            {
                worldToLocal = rtm.transform.worldToLocalMatrix, // 每帧更新矩阵即可
                localAABBMin = m.bounds.min, // 局部 AABB 是恒定的
                localAABBMax = m.bounds.max,
                triOffset = rtm.triOffset,
                triCount = rtm.triCount
            });
        }

        if (_objectBuffer == null || _objectBuffer.count != _gpuObjects.Count)
        {
            _objectBuffer?.Release();
            _objectBuffer = new ComputeBuffer(_gpuObjects.Count, 112);
        }
        _objectBuffer.SetData(_gpuObjects);
    }

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += MyRenderer;
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= MyRenderer;
        _objectBuffer?.Release(); _triangleBuffer?.Release();
    }

    void MyRenderer(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _cam || s_meshes.Count == 0) return;

        BuildStructure();    // 只有物体增删才重建顶点
        UpdateObjectBuffer(); // 每帧只传矩阵，极快

        rayTraceCS.SetInt("_Width", camera.pixelWidth);
        rayTraceCS.SetInt("_Height", camera.pixelHeight);
        rayTraceCS.SetInt("_ObjectCount", _gpuObjects.Count);
        rayTraceCS.SetMatrix("_CameraToWorld", camera.cameraToWorldMatrix);
        rayTraceCS.SetMatrix("_CameraInverseProjection", camera.projectionMatrix.inverse);

        rayTraceCS.SetBuffer(0, "_Objects", _objectBuffer);
        rayTraceCS.SetBuffer(0, "_Triangles", _triangleBuffer);
        rayTraceCS.SetBuffer(0, "_HitResultBuffer", _hitBuffer);
        rayTraceCS.Dispatch(0, Mathf.CeilToInt(camera.pixelWidth / 8f), Mathf.CeilToInt(camera.pixelHeight / 8f), 1);
    }

    public void RegisterHitBuffer(ComputeBuffer b) => _hitBuffer = b;
}