using System;
using System.Collections.Generic;
using UnityEngine;
using RayTracing.Data;
public class BVHBuilder
{ 
    private List<BVHNode> _flatNodes = new();  // 发送给gpu的数组
    private List<GPUTriangle> _orderedTriangles = new();

    public (BVHNode[] nodes, GPUTriangle[] triangles) Build(List<GPUTriangle> tris)
    {
        _flatNodes.Clear();
        _orderedTriangles.Clear();
        _orderedTriangles.AddRange(tris);

        if (tris.Count > 0)
            RecursiveBuild(0, _orderedTriangles.Count);

        return (_flatNodes.ToArray(), _orderedTriangles.ToArray());
    }

    // start offset in triangle array of this node ,
    // count: the number of triangles included in this node
    private int RecursiveBuild(int start, int count)
    {
        Bounds bound = CalculateBounds(start, count);
        int nodeIdx = _flatNodes.Count;
        _flatNodes.Add(default);

        if (count <= 100) // leaf node 
        {
            _flatNodes[nodeIdx] = new BVHNode
            {
                aabbMin_leftChildOrOffset = new Vector4(bound.min.x, bound.min.y, bound.min.z, -1 * (start + 1)),
                aabbMax_rightChildOrCount = new Vector4(bound.max.x, bound.max.y, bound.max.z, count)
            };
            return nodeIdx;
        }

        int splitIdx = FindBestSplit(start, count, bound);

        int left = RecursiveBuild(start, splitIdx - start);
        int right = RecursiveBuild(splitIdx, start + count - splitIdx);

        _flatNodes[nodeIdx] = new BVHNode
        {
            aabbMin_leftChildOrOffset = new Vector4(bound.min.x, bound.min.y, bound.min.z, left),
            aabbMax_rightChildOrCount = new Vector4(bound.max.x, bound.max.y, bound.max.z, right)
        };
        return nodeIdx;
    }
    // sah algorithm
    private int FindBestSplit(int start, int count, Bounds bounds)
    {
        int bestAxis = 0;
        float bestPos = 0;
        float minCost = float.MaxValue;

        // 简单的 SAH 实现：在长轴上采样多个候选点
        Vector3 size = bounds.size;
        for (int axis = 0; axis < 3; axis++)
        {
            float startPos = bounds.min[axis];
            float step = size[axis] / 8.0f; // 采样 8 个点

            for (int i = 1; i < 8; i++)
            {
                float candidatePos = startPos + step * i;
                float cost = EvaluateSAH(start, count, axis, candidatePos, bounds);
                if (cost < minCost)
                {
                    minCost = cost;      //跟新当前最小cost
                    bestAxis = axis;      // 跟新当前cost最小的轴，之后分区就按照这个轴
                    bestPos = candidatePos; // 记录使得cost最小的采样点位置
                }
            }
        }

        // 按照cost最小轴上的cost最小采样点，执行实际划分
        int mid = Partition(start, count, bestAxis, bestPos);

        // 逻辑兜底：如果划分失败（所有三角形都在一边），强行中点划分
        if (mid == start || mid == start + count) mid = start + count / 2;

        return mid;
    }

    // 代价估算
    private float EvaluateSAH(int start, int count, int axis, float pos, Bounds parentBounds)
    {
        Bounds leftB = new Bounds(), rightB = new Bounds();
        int leftCount = 0, rightCount = 0;
        bool lFirst = true, rFirst = true;

        for (int i = start; i < start + count; i++)
        {
            Vector3 center = GetCentroid(_orderedTriangles[i]);
            if (center[axis] < pos)
            {
                if (lFirst) { leftB.SetMinMax(center, center); lFirst = false; }
                leftB.Encapsulate(center);
                leftCount++;
            }
            else
            {
                if (rFirst) { rightB.SetMinMax(center, center); rFirst = false; }
                rightB.Encapsulate(center);
                rightCount++;
            }
        }

        float areaP = SurfaceArea(parentBounds.size);
        float areaL = SurfaceArea(leftB.size);
        float areaR = SurfaceArea(rightB.size);

        return (areaL * leftCount + areaR * rightCount) / areaP;
    }

    
    private int Partition(int start, int count, int axis, float pos)
    {
        //再当前节点所包含的三角形内
        int i = start;
        int j = start + count - 1;
        while (i <= j)
        {
            //如果三角形中心点坐标本身就在轴位置左侧，不动它
            if (GetCentroid(_orderedTriangles[i])[axis] < pos) i++;
            else // 若是在右侧，调换过来
            {
                var temp = _orderedTriangles[i];
                _orderedTriangles[i] = _orderedTriangles[j];
                _orderedTriangles[j] = temp;
                j--;
            }
        }
        //最终效果，orderedTriangles序号从左到右，位置也是从左到右，左右分界线（mid）在i
        return i;
    }

    private Vector3 GetCentroid(GPUTriangle tri) => (Vector3)(tri.A + tri.B + tri.C) / 3.0f;
    private float SurfaceArea(Vector3 s) => 2 * (s.x * s.y + s.y * s.z + s.z * s.x);

    // 计算该节点的包围盒
    private Bounds CalculateBounds(int start, int count)
    {
        Bounds b = new Bounds();
        bool first = true;
        for (int i = start; i < start + count; i++)
        {
            var t = _orderedTriangles[i];
            if (first) { b.SetMinMax(t.A, t.A); first = false; }
            b.Encapsulate(t.A); b.Encapsulate(t.B); b.Encapsulate(t.C);
        }
        return b;
    }
}