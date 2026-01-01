using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class RayTracingManager : MonoBehaviour
{
    public static RayTracingManager Instance { get; private set; }
    private ComputeBuffer hitBuffer; // 外部传入的结果引用
    private static readonly List<RayTracedMesh> s_meshes = new();

    [Header("Compute")]
    public ComputeShader rayTraceCS;

    private Camera cam;
    private int kernel;
    private uint tgx, tgy, tgz;

    private ComputeBuffer objectBuffer;
    private ComputeBuffer triangleBuffer;
    

    struct GPUObject
    {
        public Vector4 aabbMin;
        public Vector4 aabbMax;
        public int triOffset;
        public int triCount;
        public int pad0;
        public int pad1;
    }

    struct GPUTriangle
    {
        public Vector4 A;
        public Vector4 B;
        public Vector4 C;
    }

    public static void Register(RayTracedMesh mesh)
    {
        if (mesh == null) return;
        if (!s_meshes.Contains(mesh)) s_meshes.Add(mesh);
    }

    public static void UnRegister(RayTracedMesh mesh)
    {
        if (mesh == null) return;
        if (s_meshes.Contains(mesh)) s_meshes.Remove(mesh);
    }

    /// <summary>
    /// 供外部脚本（如Highlighter）注册输出Buffer
    /// </summary>
    public void RegisterHitBuffer(ComputeBuffer buffer)
    {
        
        hitBuffer = buffer;
        Debug.Log($"function called and buffer : {buffer != null},{buffer}");
    }

    private void Awake()
    {
        // 初始化单例
        if (Instance != null && Instance != this)
        {
            DestroyImmediate(this);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        cam = GetComponent<Camera>();

        RenderPipelineManager.beginCameraRendering += MyRenderer;

        if (rayTraceCS != null)
        {
            kernel = rayTraceCS.FindKernel("CSMain");
            rayTraceCS.GetKernelThreadGroupSizes(kernel, out tgx, out tgy, out tgz);
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= MyRenderer;
        ReleaseAll();
        if (Instance == this) Instance = null;
    }

    void ReleaseAll()
    {
        if (objectBuffer != null) { objectBuffer.Release(); objectBuffer = null; }
        if (triangleBuffer != null) { triangleBuffer.Release(); triangleBuffer = null; }
    }

    static void AppendWorldTriangles(Mesh mesh, Transform tr, List<GPUTriangle> outTris, ref Vector3 aabbMin, ref Vector3 aabbMax, ref bool first)
    {
        var verts = mesh.vertices;
        var indices = mesh.triangles;

        var pos = tr.position;
        var rot = tr.rotation;
        var scale = tr.lossyScale;

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 la = verts[indices[i + 0]];
            Vector3 lb = verts[indices[i + 1]];
            Vector3 lc = verts[indices[i + 2]];

            Vector3 wa = rot * Vector3.Scale(la, scale) + pos;
            Vector3 wb = rot * Vector3.Scale(lb, scale) + pos;
            Vector3 wc = rot * Vector3.Scale(lc, scale) + pos;

            outTris.Add(new GPUTriangle
            {
                A = new Vector4(wa.x, wa.y, wa.z, 0),
                B = new Vector4(wb.x, wb.y, wb.z, 0),
                C = new Vector4(wc.x, wc.y, wc.z, 0),
            });

            if (first)
            {
                aabbMin = wa;
                aabbMax = wa;
                first = false;
            }

            aabbMin = Vector3.Min(aabbMin, Vector3.Min(wa, Vector3.Min(wb, wc)));
            aabbMax = Vector3.Max(aabbMax, Vector3.Max(wa, Vector3.Max(wb, wc)));
        }
    }

    void BuildAndUpload()
    {
        // 1. 清理引用
        for (int i = s_meshes.Count - 1; i >= 0; i--)
            if (s_meshes[i] == null) s_meshes.RemoveAt(i);

        if (s_meshes.Count == 0) return;

        var objects = new List<GPUObject>();
        var triangles = new List<GPUTriangle>();

        // 2. 收集所有物体数据
        for (int mi = 0; mi < s_meshes.Count; mi++)
        {
            var rtm = s_meshes[mi];
            var mf = rtm.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            int triOffset = triangles.Count;
            Vector3 aabbMin = Vector3.zero, aabbMax = Vector3.zero;
            bool first = true;

            AppendWorldTriangles(mf.sharedMesh, rtm.transform, triangles, ref aabbMin, ref aabbMax, ref first);

            objects.Add(new GPUObject
            {
                aabbMin = aabbMin,
                aabbMax = aabbMax,
                triOffset = triOffset,
                triCount = triangles.Count - triOffset,
                pad0 = 0,
                pad1 = 0
            });
        }
       
        // 3. 统一上传 (修正：移出循环)
        if (objects.Count > 0 && triangles.Count > 0)
        {
            EnsureBuffers(objects.Count, triangles.Count);
            objectBuffer.SetData(objects);
            triangleBuffer.SetData(triangles);

            rayTraceCS.SetInt("_ObjectCount", objects.Count);
            rayTraceCS.SetBuffer(kernel, "_Objects", objectBuffer);
            rayTraceCS.SetBuffer(kernel, "_Triangles", triangleBuffer);
            rayTraceCS.SetBuffer(kernel, "_HitResultBuffer", hitBuffer);
        }
    }

    void EnsureBuffers(int objCount, int triCount)
    {
        int objStride = 48; // Vector4*2 + int*4
        int triStride = 48; // Vector4*3

        if (objectBuffer == null || objectBuffer.count != objCount)
        {
            if (objectBuffer != null) objectBuffer.Release();
            objectBuffer = new ComputeBuffer(objCount, objStride);
        }

        if (triangleBuffer == null || triangleBuffer.count != triCount)
        {
            if (triangleBuffer != null) triangleBuffer.Release();
            triangleBuffer = new ComputeBuffer(triCount, triStride);
        }
    }

    void Dispatch()
    {
        if (rayTraceCS == null || cam == null) return;

        int w = Mathf.Max(1, cam.pixelWidth);
        int h = Mathf.Max(1, cam.pixelHeight);

        rayTraceCS.SetMatrix("_CameraToWorld", cam.cameraToWorldMatrix);
        rayTraceCS.SetMatrix("_CameraInverseProjection", cam.projectionMatrix.inverse);
        rayTraceCS.SetInt("_Width", w);
        rayTraceCS.SetInt("_Height", h);

        int groupsX = Mathf.CeilToInt(w / (float)tgx);
        int groupsY = Mathf.CeilToInt(h / (float)tgy);
        rayTraceCS.Dispatch(kernel, groupsX, groupsY, 1);
    }

    void MyRenderer(ScriptableRenderContext contex, Camera cam)
    {
        BuildAndUpload();
        Dispatch();
    }
}