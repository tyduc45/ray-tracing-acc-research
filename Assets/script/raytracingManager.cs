using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RayTracing.Data;

[RequireComponent(typeof(Camera))]
public class RayTracingManager : MonoBehaviour
{
    public static RayTracingManager Instance { get; private set; }
    private ComputeBuffer hitBuffer;
    private static readonly List<RayTracedMesh> s_meshes = new();

    public ComputeShader rayTraceCS;
    private Camera cam;
    private int kernel;
    private uint tgx, tgy, tgz;

    private ComputeBuffer bvhNodeBuffer;
    private ComputeBuffer triangleBuffer;
    private BVHBuilder _builder = new();


    public static void Register(RayTracedMesh mesh) { if (!s_meshes.Contains(mesh)) s_meshes.Add(mesh); }
    public static void UnRegister(RayTracedMesh mesh) { s_meshes.Remove(mesh); }

    public void RegisterHitBuffer(ComputeBuffer buffer) => hitBuffer = buffer;

    private void Awake()
    {
        if (Instance != null) { DestroyImmediate(this); return; }
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
    }

    void ReleaseAll()
    {
        bvhNodeBuffer?.Release();
        triangleBuffer?.Release();
    }

    void BuildAndUpload()
    {
        s_meshes.RemoveAll(m => m == null);
        if (s_meshes.Count == 0) return;

        List<GPUTriangle> allTris = new List<GPUTriangle>();
        foreach (var rtm in s_meshes)
        {
            var mf = rtm.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            AppendWorldTriangles(mf.sharedMesh, rtm.transform, allTris);
        }

        var (nodes, tris) = _builder.Build(allTris);

        if (nodes.Length > 0)
        {
            EnsureBuffers(nodes.Length, tris.Length);
            bvhNodeBuffer.SetData(nodes);
            triangleBuffer.SetData(tris);

            rayTraceCS.SetBuffer(kernel, "_BVHNodes", bvhNodeBuffer);
            rayTraceCS.SetBuffer(kernel, "_Triangles", triangleBuffer);
            rayTraceCS.SetBuffer(kernel, "_HitResultBuffer", hitBuffer);
            rayTraceCS.SetInt("_NodeCount", nodes.Length);
        }
    }

    void EnsureBuffers(int nodeCount, int triCount)
    {
        // BVHNode: vec4 * 2 = 32 bytes
        if (bvhNodeBuffer == null || bvhNodeBuffer.count != nodeCount)
        {
            bvhNodeBuffer?.Release();
            bvhNodeBuffer = new ComputeBuffer(nodeCount, 32);
        }
        if (triangleBuffer == null || triangleBuffer.count != triCount)
        {
            triangleBuffer?.Release();
            triangleBuffer = new ComputeBuffer(triCount, 48);
        }
    }

    static void AppendWorldTriangles(Mesh mesh, Transform tr, List<GPUTriangle> outTris)
    {
        var verts = mesh.vertices;
        var indices = mesh.triangles;
        Matrix4x4 localToWorld = tr.localToWorldMatrix;

        for (int i = 0; i < indices.Length; i += 3)
        {
            outTris.Add(new GPUTriangle
            {
                A = localToWorld.MultiplyPoint3x4(verts[indices[i]]),
                B = localToWorld.MultiplyPoint3x4(verts[indices[i + 1]]),
                C = localToWorld.MultiplyPoint3x4(verts[indices[i + 2]])
            });
        }
    }

    void MyRenderer(ScriptableRenderContext context, Camera cam)
    {
        if (cam != this.cam) return;
        BuildAndUpload();

        int w = cam.pixelWidth, h = cam.pixelHeight;
        rayTraceCS.SetMatrix("_CameraToWorld", cam.cameraToWorldMatrix);
        rayTraceCS.SetMatrix("_CameraInverseProjection", cam.projectionMatrix.inverse);
        rayTraceCS.Dispatch(kernel, Mathf.CeilToInt(w / (float)tgx), Mathf.CeilToInt(h / (float)tgy), 1);
    }
}