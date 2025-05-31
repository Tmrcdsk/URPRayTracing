Shader "Tutorial/Diffuse"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float3 normal : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = _Color * half4(dot(i.normal, float3(0.0f, 1.0f, 0.0f)).xxx, 1.0f);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    SubShader
    {
        Pass
        {
            Name "RayTracing"
            Tags { "LightMode" = "RayTracing" }

            HLSLPROGRAM
            
            #define CBUFFER_START(name) cbuffer name {
            #define CBUFFER_END };
            
            #pragma raytracing test

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct RayIntersection
            {
                float4 color;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END

            [shader("closesthit")]
            void ClosestHitShader(inout RayIntersection rayIntersection : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
            {
                rayIntersection.color = _Color;
            }

            ENDHLSL
        }
    }
}
