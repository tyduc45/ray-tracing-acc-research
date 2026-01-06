using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using RayTracing.Data;

public class BVHBuilderTests:MonoBehaviour
{
    private BVHBuilder builder;

    [SetUp]
    public void Setup() => builder = new BVHBuilder();

    [Test]
    public void Test_BVH_Build_Consistency()
    {
        // 1. 构造测试数据：两个相距较远的立方体
        List<GPUTriangle> tris = CreateCube(new Vector3(0, 0, 0));
        tris.AddRange(CreateCube(new Vector3(10, 10, 10)));

        // 2. 构建 BVH
        var (nodes, triangles) = builder.Build(tris);

        // 3. 验证基础正确性
        Assert.AreEqual(tris.Count, triangles.Length, "三角形总数不一致");
        Assert.Greater(nodes.Length, 0, "未生成任何节点");

        // 4. 验证根节点包围盒是否包含所有顶点
        BVHNode root = nodes[0];
        Bounds rootBounds = GetBoundsFromNode(root);
        foreach (var tri in triangles)
        {
            Assert.IsTrue(rootBounds.Contains(tri.A), "根节点未包含顶点A");
            Assert.IsTrue(rootBounds.Contains(tri.B), "根节点未包含顶点B");
            Assert.IsTrue(rootBounds.Contains(tri.C), "根节点未包含顶点C");
        }

        Debug.Log($"BVH构建成功，节点数: {nodes.Length}");
    }

    private List<GPUTriangle> CreateCube(Vector3 offset)
    {
        List<GPUTriangle> list = new List<GPUTriangle>();

        // 定义立方体的8个顶点 (0-1 范围，加上 offset)
        Vector3 v000 = offset + new Vector3(0, 0, 0);
        Vector3 v100 = offset + new Vector3(1, 0, 0);
        Vector3 v110 = offset + new Vector3(1, 1, 0);
        Vector3 v010 = offset + new Vector3(0, 1, 0);
        Vector3 v001 = offset + new Vector3(0, 0, 1);
        Vector3 v101 = offset + new Vector3(1, 0, 1);
        Vector3 v111 = offset + new Vector3(1, 1, 1);
        Vector3 v011 = offset + new Vector3(0, 1, 1);

        // 辅助函数：添加一个四边形（两个三角形）
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            list.Add(new GPUTriangle { A = a, B = b, C = c });
            list.Add(new GPUTriangle { A = a, B = c, C = d });
        }

        // 6个面
        AddQuad(v000, v010, v110, v100); // Back
        AddQuad(v101, v111, v011, v001); // Front
        AddQuad(v000, v001, v011, v010); // Left
        AddQuad(v100, v110, v111, v101); // Right
        AddQuad(v010, v011, v111, v110); // Top
        AddQuad(v000, v100, v101, v001); // Bottom

        return list;
    }

    private Bounds GetBoundsFromNode(BVHNode node)
    {
        Bounds b = new Bounds();
        b.SetMinMax((Vector3)node.aabbMin_leftChildOrOffset, (Vector3)node.aabbMax_rightChildOrCount);
        return b;
    }

    private void OnEnable()
    {
        Setup();
    }

    private void Update()
    {
        Test_BVH_Build_Consistency();
    }
}