// Kampung Run: KL - traffic signal lamps (TrafficSignals.cs).
//
// Every lamp in the city is in a few merged meshes, and none of them is touched from the CPU: each vertex says
// what it is (vertex colour r: 0 car red, 0.25 amber, 0.5 green, 0.75 red man, 1 green man), which way its
// junction's phase runs (g: 0 north-south, 1 east-west) and the junction's offset into the cycle (b). The game
// sets the clock and the phase lengths as globals (TrafficSignals.SignalClock), the same numbers the drivers and
// pedestrians read, so what you see is what they obey.
Shader "KampungRun/SignalLamp"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Lamp"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _SignalClock;          // seconds (Time.time)
            float4 _SignalPhase;         // green, amber, all-red, walk (seconds)

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 col : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);

                float G = _SignalPhase.x, A = _SignalPhase.y, R = _SignalPhase.z, W = _SignalPhase.w;
                float half_ = G + A + R;
                float cycle = 2.0 * half_ + W + R;
                float t = fmod(_SignalClock + v.color.b * cycle + cycle * 64.0, cycle);
                float role = v.color.r * 4.0 + 0.5;
                int kind = (int)role;                       // 0 red, 1 amber, 2 green, 3 red man, 4 green man
                float tg = t - (v.color.g > 0.5 ? half_ : 0.0); // time into this group's own green
                bool green = tg >= 0.0 && tg < G;
                bool amber = tg >= G && tg < G + A;
                float tw = t - 2.0 * half_;
                bool walk = tw >= 0.0 && tw < W;
                // the green man blinks for his last 2.5 s
                bool walkLit = walk && (tw < W - 2.5 || frac(tw * 2.0) < 0.5);
                bool on = kind == 0 ? !(green || amber) : kind == 1 ? amber : kind == 2 ? green : kind == 3 ? !walk : walkLit;

                half3 lit = kind == 0 ? half3(1.0, 0.16, 0.10) : kind == 1 ? half3(1.0, 0.66, 0.08)
                          : kind == 2 ? half3(0.15, 1.0, 0.45) : kind == 3 ? half3(1.0, 0.2, 0.12) : half3(0.3, 1.0, 0.5);
                o.col = on ? lit * 1.35 : lit * 0.13 + 0.04;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(MixFog(i.col, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
