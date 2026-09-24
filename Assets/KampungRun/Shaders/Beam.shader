// Mission beacon: a translucent column with scrolling pen-stroke bands, fading upward.
Shader "KampungRun/Beam"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.85, 0.3, 0.6)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float h = i.positionOS.y * 0.5 + 0.5;         // 0 bottom .. 1 top (Unity cylinder is y -1..1)
                float bands = step(0.5, frac(h * 14.0 - _Time.y * 1.5));
                float a = _Color.a * (1.0 - h) * (0.55 + 0.45 * bands);
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
