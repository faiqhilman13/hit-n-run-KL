// A wheel turning faster than the eye (and the frame rate) can follow: a translucent disc over the rim that
// fades in with speed (_Blur), the spokes smeared into a soft ring, with two cartoon speed arcs going round
// (_Spin, in turns). Drawn on a quad whose uv spans the disc.
Shader "KampungRun/WheelBlur"
{
    Properties
    {
        _Color ("Rim", Color) = (0.78, 0.80, 0.84, 1)
        _Dark ("Well", Color) = (0.07, 0.07, 0.08, 1)
        _Blur ("Blur", Range(0, 1)) = 0
        _Spin ("Spin (turns)", Float) = 0
        _Hub ("Hub radius (0..1)", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "WheelBlur"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Dark;
                half _Blur, _Spin, _Hub;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                clip(1.0 - r);
                float turn = atan2(p.y, p.x) * 0.15915494 + 0.5;     // 0..1 round the disc
                // the smeared spokes: rim-coloured, a touch darker through the middle of their length, a bright hub
                // (faint concentric bands: the spokes' and the lip's edges smeared round)
                float bands = 0.5 + 0.5 * cos(r * 34.0);
                float3 col = lerp(_Dark.rgb, _Color.rgb, 0.3 + 0.2 * bands + 0.3 * smoothstep(0.82, 0.97, r));
                col = lerp(col, _Color.rgb * 1.1, 1.0 - smoothstep(_Hub * 0.85, _Hub, r));
                // two speed arcs a quarter of the way round, on opposite sides, at different radii
                float a1 = (1.0 - smoothstep(0.025, 0.05, abs(r - 0.62))) * step(frac(turn + _Spin), 0.24);
                float a2 = (1.0 - smoothstep(0.025, 0.05, abs(r - 0.84))) * step(frac(turn + _Spin + 0.5), 0.2);
                col = lerp(col, float3(1.0, 1.0, 0.97), saturate(a1 + a2) * 0.85);
                float a = _Blur * (0.62 + 0.3 * saturate(a1 + a2)) * (1.0 - smoothstep(0.93, 1.0, r));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
