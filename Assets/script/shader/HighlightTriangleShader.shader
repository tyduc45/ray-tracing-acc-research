Shader "Debug/TriangleHitHighlight"
{
    Properties
    {
        _MainColor ("Base Color", Color) = (1,1,1,1)
        _HitColor ("Hit Color", Color) = (1,0,0,1)
        _ObjectTriOffset ("Manager中的偏移量", Int) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            StructuredBuffer<int> _HitResultBuffer;
            int _ObjectTriOffset;
            float4 _MainColor;
            float4 _HitColor;

            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i, uint primID : SV_PrimitiveID) : SV_Target
            {
                // primID 是当前模型内部的三角形序号
                //加上在 Manager 全局 Buffer 里的偏移，得到全局唯一索引
                int globalIdx = _ObjectTriOffset + (int)primID;

                if (globalIdx == _HitResultBuffer[0])
                {
                    return _HitColor;
                }
                return _MainColor;
            }
            ENDCG
        }
    }
}