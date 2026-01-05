using UnityEngine;
using System.Runtime.InteropServices;
using UnityEditor.Rendering.LookDev;

namespace RayTracing.Data
{
    // 使用 Sequential 布局确保与 GPU 显存布局严格对齐
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTriangle
    {
        public Vector4 A;
        public Vector4 B;
        public Vector4 C;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BVHNode
    {
        // xyz: 包围盒坐标 ，w：左右子节点编号，若为叶子节点，右节点退化为节点内三角形个数，左节点退化为三角形起始索引
        public Vector4 aabbMin_leftChildOrOffset;  // 非叶子节点：leftchildIndex ， 叶子节点则-offsete
        public Vector4 aabbMax_rightChildOrCount; // 非叶子节点：rightIndex ，叶子节点则显示节点内三角形个数
    }

    // 如果未来需要物体属性（材质ID、颜色等），也可以定义在这里
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUObjectData
    {
        public Matrix4x4 localToWorld;
       
        public int materialID;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUInstanceData
    {
        public Matrix4x4 localToWorld;
        public Matrix4x4 worldToLocal;
    }
}

