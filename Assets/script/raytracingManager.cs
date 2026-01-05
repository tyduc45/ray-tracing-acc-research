
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RayTracing.Data;
using System;


[RequireComponent(typeof(Camera))]
public class RayTracingManager : MonoBehaviour
{

    public static RayTracingManager Instance { get; private set; }

    [Header("Settings")]
    public ComputeShader rayTraceCS;

    private Camera _cam;
    private int _kernel;
    private uint _tgx, _tgy, _tgz;

    // GPU Buffers
    private ComputeBuffer _bvhNodeBuffer;
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _instanceBuffer; // 存储每帧变化的矩阵
    private ComputeBuffer _hitBuffer;

    private BVHNode[] _debugNodes; // 用于可视化调试

    private static readonly List<RayTracedMesh> s_meshes = new();
    private BVHBuilder _builder = new BVHBuilder();
    private bool _isInitialized = false;

    // 每一帧缓存的矩阵数组
    private GPUInstanceData[] _instDataCache;

    public static void Register(RayTracedMesh mesh) { if (!s_meshes.Contains(mesh)) s_meshes.Add(mesh); }
    public static void UnRegister(RayTracedMesh mesh) { s_meshes.Remove(mesh); }

    public void RegisterHitBuffer(ComputeBuffer buffer) => _hitBuffer = buffer;

    private void Awake()
    {
        if (Instance != null) { DestroyImmediate(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        _cam = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        if (rayTraceCS != null)
        {
            _kernel = rayTraceCS.FindKernel("CSMain");
            rayTraceCS.GetKernelThreadGroupSizes(_kernel, out _tgx, out _tgy, out _tgz);
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        ReleaseBuffers();
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _cam) return;

        // 1. 仅在有数据且未初始化时构建一次 BVH
        if (!_isInitialized && s_meshes.Count > 0)
        {
            BuildOnce();
            _isInitialized = true;
        }
        // 2. 每一帧都同步所有物体的 Transform
        if (_isInitialized)
        {
            UpdateInstances();
            Dispatch(camera);
        }
        printBufferData();
    }

    private void BuildOnce()
    {
        s_meshes.RemoveAll(m => m == null);
        List<GPUTriangle> allTris = new List<GPUTriangle>();

        // 【逻辑修正】：此时构建的是本地空间 BVH。
        // 注意：如果你有多个物体，目前的逻辑是将它们所有本地三角形合在一起。
        // 为了支持独立移动，你只需要为每个物体记录其在 triangleBuffer 里的 offset。
        // 这里简化处理：我们假设每个 RayTracedMesh 对应一个独立实例。
        foreach (var rtm in s_meshes)
        {
            var mf = rtm.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            AppendLocalTriangles(mf.sharedMesh, allTris);
        }

        var (nodes, tris) = _builder.Build(allTris);
        _debugNodes = nodes; // 存储副本

        if (nodes.Length > 0)
        {
            CreateBuffers(nodes.Length, tris.Length);
            _bvhNodeBuffer.SetData(nodes);
            _triangleBuffer.SetData(tris);

            rayTraceCS.SetBuffer(_kernel, "_BVHNodes", _bvhNodeBuffer);
            rayTraceCS.SetBuffer(_kernel, "_Triangles", _triangleBuffer);
            rayTraceCS.SetInt("_NodeCount", nodes.Length);
        }
    }

    private void UpdateInstances()
    {
        if (s_meshes.Count == 0) return;

        // 更新矩阵缓存
        if (_instDataCache == null || _instDataCache.Length != s_meshes.Count)
            _instDataCache = new GPUInstanceData[s_meshes.Count];

        for (int i = 0; i < s_meshes.Count; i++)
        {
            if (s_meshes[i] == null) continue;
            Transform t = s_meshes[i].transform;
            _instDataCache[i] = new GPUInstanceData
            {
                localToWorld = t.localToWorldMatrix,
                worldToLocal = t.worldToLocalMatrix
            };
        }

        // 更新 GPU Buffer
        if (_instanceBuffer == null || _instanceBuffer.count != s_meshes.Count)
        {
            _instanceBuffer?.Release();
            _instanceBuffer = new ComputeBuffer(s_meshes.Count, 128); // 2 * Matrix4x4 (64 bytes each)
        }
        _instanceBuffer.SetData(_instDataCache);
        rayTraceCS.SetBuffer(_kernel, "_Instances", _instanceBuffer);
        rayTraceCS.SetInt("_InstanceCount", s_meshes.Count);
    }

    private void CreateBuffers(int nodeCount, int triCount)
    {
        _bvhNodeBuffer?.Release();
        _triangleBuffer?.Release();
        _bvhNodeBuffer = new ComputeBuffer(nodeCount, 32);
        _triangleBuffer = new ComputeBuffer(triCount, 48);
    }

    private void ReleaseBuffers()
    {
        _bvhNodeBuffer?.Release();
        _triangleBuffer?.Release();
        _instanceBuffer?.Release();
    }

    private void AppendLocalTriangles(Mesh mesh, List<GPUTriangle> outTris)
    {
        var verts = mesh.vertices;
        var indices = mesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            outTris.Add(new GPUTriangle
            {
                A = (Vector4)verts[indices[i]],
                B = (Vector4)verts[indices[i + 1]],
                C = (Vector4)verts[indices[i + 2]]
            });
        }
    }

    private void Dispatch(Camera camera)
    {
        int w = camera.pixelWidth;
        int h = camera.pixelHeight;

        rayTraceCS.SetInt("_Width", w);
        rayTraceCS.SetInt("_Height", h);
        rayTraceCS.SetMatrix("_CameraToWorld", camera.cameraToWorldMatrix);
        rayTraceCS.SetMatrix("_CameraInverseProjection", camera.projectionMatrix.inverse);
        if (_hitBuffer != null) rayTraceCS.SetBuffer(_kernel, "_HitResultBuffer", _hitBuffer);



        int groupsX = Mathf.CeilToInt(w / (float)_tgx);
        int groupsY = Mathf.CeilToInt(h / (float)_tgy);
        rayTraceCS.Dispatch(_kernel, groupsX, groupsY, 1);
        
    }

    void printBufferData()
    {
        Array array = new int[1];
        _hitBuffer.GetData(array);
        foreach (var elem in array)
        {
            Debug.Log("manager:" + elem.ToString());
        }
    }

    internal void RegisterHitBuffer(object value)
    {
        throw new NotImplementedException();
    }

    /*private void OnDrawGizmos()
    {
        // 只有在初始化完成且有节点数据时才绘制
        if (!_isInitialized || _debugNodes == null || _debugNodes.Length == 0) return;

        // 默认以第一个物体的矩阵作为绘制坐标系
        // 如果没有物体，则使用 Identity (世界原点)
        Matrix4x4 drawMatrix = s_meshes.Count > 0 ? s_meshes[0].transform.localToWorldMatrix : Matrix4x4.identity;
        Gizmos.matrix = drawMatrix;

        // 递归绘制（或者循环遍历数组绘制）
        for (int i = 0; i < _debugNodes.Length; i++)
        {
            BVHNode node = _debugNodes[i];

            // 区分叶子节点和内部节点：叶子节点用绿色，内部节点用黄色
            bool isLeaf = node.aabbMin_leftChildOrOffset.w < 0;
            Gizmos.color = isLeaf ? Color.green : Color.yellow;

            // 计算中心点和大小
            Vector3 min = (Vector3)node.aabbMin_leftChildOrOffset;
            Vector3 max = (Vector3)node.aabbMax_rightChildOrCount;
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;

            // 绘制线框盒
            Gizmos.DrawWireCube(center, size);
        }

    }*/

}
