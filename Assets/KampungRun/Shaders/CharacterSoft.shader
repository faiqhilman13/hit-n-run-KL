// Smooth, matte character lighting. The palette remains swappable at runtime;
// the existing LatInk shader continues to render buildings, roads and vehicles.
Shader "KampungRun/CharacterSoft"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _BaseMap ("Palette / Base Map", 2D) = "white" {}
        _Gloss ("Palette Highlight Strength", Range(0, 1)) = 1
        // Keep the shared pass material layout compatible with LatInk.
        [HideInInspector] _ShadowTint ("Shadow Tint", Color) = (0.78, 0.72, 0.80, 1)
        [HideInInspector] _PaperColor ("Paper Colour", Color) = (0.98, 0.95, 0.87, 1)
        [HideInInspector] _InkColor ("Ink Colour", Color) = (0.09, 0.07, 0.06, 1)
        [HideInInspector] _Wash ("Wash", Float) = 0
        [HideInInspector] _LightThreshold ("Threshold", Float) = 0.42
        [HideInInspector] _HatchDensity ("Hatch Density", Float) = 9
        [HideInInspector] _HatchStrength ("Hatch", Float) = 0
        [HideInInspector] _OutlineWidth ("Outline", Float) = 0
        [HideInInspector] _OutlineWobble ("Wobble", Float) = 0
        [HideInInspector] _Emit ("Emission", Float) = 0
        [HideInInspector] _Surface ("Surface", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "CharacterForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowTint;
                half4 _PaperColor;
                half4 _InkColor;
                half _Wash;
                half _LightThreshold;
                float _HatchDensity;
                half _HatchStrength;
                float _OutlineWidth;
                half _OutlineWobble;
                half _Gloss;
                half _Emit;
                half _Surface;
            CBUFFER_END
            half4 _ShadeSky;
            half4 _ShadeGround;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
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
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light key = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half4 palette = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 baseCol = palette.rgb * _BaseColor.rgb;
                half gloss = palette.a > 0.5 ? saturate((palette.a * 255.0 - 128.0) / 127.0) * _Gloss : 0;

                // The game's time-of-day ambient is shared with the city. The fallback
                // gives the model a useful appearance in the standalone asset preview.
                half3 sky = _ShadeSky.a > 0 ? _ShadeSky.rgb : half3(0.30, 0.34, 0.42);
                half3 ground = _ShadeGround.a > 0 ? _ShadeGround.rgb : half3(0.20, 0.17, 0.15);
                half3 ambient = lerp(ground, sky, n.y * 0.5 + 0.5);
                half wrapped = saturate((dot(n, key.direction) + 0.28) / 1.28);
                wrapped = smoothstep(0.0, 1.0, wrapped);
                half shadow = lerp(0.38, 1.0, key.shadowAttenuation);
                half3 colour = baseCol * (ambient + key.color * wrapped * shadow * 0.80);

                // Broad highlights reveal curved cheeks and fabric without a plastic shine.
                half3 halfDir = SafeNormalize(key.direction + view);
                half highlight = pow(saturate(dot(n, halfDir)), lerp(30.0, 72.0, gloss));
                colour += key.color * highlight * shadow * lerp(0.028, 0.16, gloss);
                half rim = pow(1.0 - saturate(dot(n, view)), 4.0);
                colour += baseCol * sky * rim * saturate(n.y + 0.45) * 0.22;
                return half4(MixFog(colour, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "KampungRun/LatInk/ShadowCaster"
        UsePass "KampungRun/LatInk/DepthOnly"
        UsePass "KampungRun/LatInk/DepthNormals"
    }
    FallBack Off
}
