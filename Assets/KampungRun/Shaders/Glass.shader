// Kampung Run: KL - car glass for the Hit & Run look: a see-through blue-grey tint that gets
// more reflective at grazing angles (sky colour), with a crisp sun glint. Transparent, no depth
// write, drawn after the opaque car body so you can see the driver and the seats inside.
Shader "KampungRun/Glass"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.30, 0.44, 0.58, 0.32)
        _Edge ("Edge Opacity", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GlassForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Edge;
            CBUFFER_END
            half4 _ShadeSky;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light l = GetMainLight();
                half fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                half spec = pow(saturate(dot(n, normalize(l.direction + v))), 120.0);
                half3 sky = _ShadeSky.a > 0 ? _ShadeSky.rgb * 1.6 : half3(0.7, 0.8, 0.9);
                half3 col = lerp(_Tint.rgb, sky, fres * 0.8) + l.color * spec * 1.5;
                half a = saturate(_Tint.a + fres * _Edge + spec);
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
