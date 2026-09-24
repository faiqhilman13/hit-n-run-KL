// Cartoon sky: flat blue gradient with puffy, ink-edged clouds drifting by (the
// "Simpsons intro" sky), plus stars for the night level. Used as the skybox.
Shader "KampungRun/CartoonSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.30, 0.62, 0.95, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.88, 0.98, 1)
        _Ground ("Below Horizon", Color) = (0.62, 0.72, 0.62, 1)
        _CloudColor ("Cloud", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud Shade", Color) = (0.78, 0.84, 0.95, 1)
        _CloudInk ("Cloud Ink", Color) = (0.12, 0.14, 0.22, 1)
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.45
        _CloudSpeed ("Cloud Speed", Float) = 0.004
        _Stars ("Stars", Range(0, 1)) = 0
        _SunDir ("Sun Direction", Vector) = (0.3, 0.6, 0.4, 0)
        _SunColor ("Sun Disc", Color) = (1, 0.95, 0.6, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _Ground, _CloudColor, _CloudShade, _CloudInk, _SunColor;
                half _CloudCover, _Stars;
                float _CloudSpeed;
                float4 _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float s = 0, a = 0.55;
                for (int k = 0; k < 4; k++) { s += Noise(p) * a; p *= 2.07; a *= 0.5; }
                return s;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 col = h > 0 ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(h), 0.55)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6));

                // stars (night)
                if (_Stars > 0 && h > 0)
                {
                    float2 sp = d.xz / (d.y + 0.3) * 120;
                    float s = step(0.985, Hash(floor(sp))) * saturate(h * 3);
                    col += s * _Stars;
                }

                // sun disc with a cartoon ring
                float3 sd = normalize(_SunDir.xyz);
                float sdot = dot(d, sd);
                col = lerp(col, _SunColor.rgb, smoothstep(0.9975, 0.9985, sdot));
                col = lerp(col, _CloudInk.rgb, smoothstep(0.9968, 0.9972, sdot) * (1 - smoothstep(0.9975, 0.9978, sdot)));

                // puffy clouds on a virtual plane, ink-outlined
                if (h > 0.01)
                {
                    float2 uv = d.xz / (h + 0.18) * 1.6 + _Time.y * _CloudSpeed * float2(1, 0.3);
                    float n = Fbm(uv);
                    float lumpy = n + Noise(uv * 3.1) * 0.08;
                    float th = 1.0 - _CloudCover * 0.75;
                    float body = smoothstep(th, th + 0.015, lumpy);
                    float inner = smoothstep(th + 0.05, th + 0.065, lumpy);
                    float edge = body * (1 - smoothstep(th + 0.012, th + 0.03, lumpy));
                    half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, inner);
                    float fade = saturate(h * 5);
                    col = lerp(col, cloud, body * fade);
                    col = lerp(col, _CloudInk.rgb, edge * fade * 0.85);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
