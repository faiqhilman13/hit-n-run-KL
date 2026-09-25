// Kampung Run: KL - "Lat" ink & wash shader for URP.
//
// Look: flat, slightly faded watercolour fills on warm paper, a wobbly hand-inked
// outline (inverted hull), and pen hatching / cross-hatching instead of smooth shading.
// Hatching is world-space triplanar so it stays glued to surfaces while driving.
Shader "KampungRun/LatInk"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _BaseMap ("Palette / Base Map", 2D) = "white" {}
        _ShadowTint ("Shadow Tint", Color) = (0.78, 0.72, 0.80, 1)
        _PaperColor ("Paper Colour", Color) = (0.98, 0.95, 0.87, 1)
        _InkColor ("Ink Colour", Color) = (0.09, 0.07, 0.06, 1)
        _Wash ("Watercolour Wash", Range(0, 1)) = 0.28
        _LightThreshold ("Light Threshold", Range(0, 1)) = 0.42
        _HatchDensity ("Hatch Lines / Metre", Float) = 9
        _HatchStrength ("Hatch Strength", Range(0, 1)) = 0.7
        _OutlineWidth ("Outline Width (px)", Float) = 2.2
        _OutlineWobble ("Outline Wobble", Range(0, 1)) = 0.45
        _Gloss ("Paint Gloss (Hit & Run mode)", Range(0, 1)) = 0
        _Emit ("Lamp Glow", Float) = 0
        _Surface ("Painted Surface (0 = none, 1..11)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

        // painted surface detail textures (Tools/gen_surfaces.py), one array slice per surface
        TEXTURE2D_ARRAY(_SurfaceArray);
        SAMPLER(sampler_SurfaceArray);
        half _SurfaceStrength;

        // Hit & Run look (set globally by the GameManager): 1 = smooth soft-lit cartoon shading,
        // glossy car paint, no hatching / paper, outlines scaled by _ShadeOutline.
        half _ShadeMode;
        half _ShadeOutline;
        half4 _ShadeSky;       // hemisphere ambient, upper
        half4 _ShadeGround;    // hemisphere ambient, lower

        // time-of-day look, set globally by the GameManager for every level
        half4 _LatPaper;
        half4 _LatShadow;
        half4 _LatStyle;    // x hatch, y wash, z grain, w outline wobble
        half4 _LatStyle2;   // x hatch shaded side (0 = cast shadows only), y saturation
        half _LatStyleSet;

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash13(i), n100 = Hash13(i + float3(1, 0, 0));
            float n010 = Hash13(i + float3(0, 1, 0)), n110 = Hash13(i + float3(1, 1, 0));
            float n001 = Hash13(i + float3(0, 0, 1)), n101 = Hash13(i + float3(1, 0, 1));
            float n011 = Hash13(i + float3(0, 1, 1)), n111 = Hash13(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }
        ENDHLSL

        // ------------------------------------------------------------------------------
        // Colour + hatching
        // ------------------------------------------------------------------------------
        Pass
        {
            Name "LatForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float2 uv : TEXCOORD4;
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

            // One family of wobbly pen lines. 'darkness' fattens the lines so tone
            // is carried by line weight, the way a pen artist would do it.
            float HatchLines(float2 uv, float2 dir, float density, float darkness, float seed)
            {
                float t = dot(uv, dir) * density;
                float along = dot(uv, float2(-dir.y, dir.x));
                t += sin(along * 2.3 + seed) * 0.12 + sin(along * 7.1 + seed * 3.0) * 0.04;
                float dist = abs(frac(t) - 0.5);                 // 0 at line centre .. 0.5 between
                float halfWidth = lerp(0.05, 0.17, saturate(darkness));
                float aa = max(fwidth(t), 1e-4);
                float line_ = 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
                // Far away the lines turn to grey soup; fade to their average coverage.
                float fade = saturate(1.6 - aa * 3.0);
                return lerp(halfWidth * 2.0, line_, fade);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);

                // Global art-style knobs (Kampung Boy x Simpsons). When the game hasn't set
                // them (_LatStyleSet = 0) the material's own values are used.
                bool styled = _LatStyleSet > 0.5;
                half hatchAmt  = styled ? _LatStyle.x : _HatchStrength;
                half washAmt   = styled ? _LatStyle.y : _Wash;
                half grainAmt  = styled ? _LatStyle.z : 1.0;
                half formHatch = styled ? _LatStyle2.x : 1.0;   // hatch the shaded side too, or only cast shadows
                half saturation = styled ? _LatStyle2.y : 1.0;

                float ndl = dot(n, light.direction);
                float castShadow = 1.0 - light.shadowAttenuation;
                float lightAmt = saturate(ndl) * light.shadowAttenuation;

                // --- flat fill (optionally faded toward paper like watercolour) --------------
                float wash = ValueNoise(i.positionWS * 0.35) * 0.6 + ValueNoise(i.positionWS * 1.7) * 0.4;
                half3 paper = _LatPaper.a > 0 ? _LatPaper.rgb : _PaperColor.rgb;
                half3 shadowTint = _LatShadow.a > 0 ? _LatShadow.rgb : _ShadowTint.rgb;
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 baseCol = _BaseColor.rgb * tex.rgb;
                // KL atlas alpha: 128..255 = gloss, 1..15 = painted surface id, 0 = plain
                half a255 = tex.a * 255.0;
                half gloss = a255 > 127.5 ? _Gloss * (a255 - 128.0) / 127.0 : 0.0;
                half surf = a255 > 127.5 ? _Surface : max(_Surface, floor(a255 + 0.5));
                half lum = dot(baseCol, half3(0.3, 0.59, 0.11));
                baseCol = saturate(lerp(lum.xxx, baseCol, saturation));

                if (_ShadeMode > 0.5)
                {
                    // --- painted textures: world-space triplanar detail, tinted by the surface colour
                    if (surf > 0.5 && _SurfaceStrength > 0.001)
                    {
                        // metres per texture tile, per surface
                        const float scales[12] = { 1, 4.0, 5.0, 2.4, 2.4, 2.6, 4.5, 4.0, 1.8, 7.0, 2.6, 2.0 };
                        int id = (int)surf;
                        if (id == 4 && n.y > 0.45) id = 10;            // brick facing the sky reads as roof tiles
                        float3 p = i.positionWS / scales[min(id, 11)];
                        if (id == 9) p.xz += _Time.y * float2(0.05, 0.02);   // water drifts
                        float3 w = pow(abs(n), 4.0);
                        w /= (w.x + w.y + w.z);
                        float slice = id - 1;
                        half dX = SAMPLE_TEXTURE2D_ARRAY(_SurfaceArray, sampler_SurfaceArray, p.zy, slice).r;
                        half dY = SAMPLE_TEXTURE2D_ARRAY(_SurfaceArray, sampler_SurfaceArray, p.xz, slice).r;
                        half dZ = SAMPLE_TEXTURE2D_ARRAY(_SurfaceArray, sampler_SurfaceArray, p.xy, slice).r;
                        half d = dX * w.x + dY * w.y + dZ * w.z;
                        float dist = distance(_WorldSpaceCameraPos, i.positionWS);
                        half k = _SurfaceStrength * saturate(1.6 - dist / 140.0);
                        baseCol *= clamp(1.0 + (d - 0.5) * 2.6 * k, 0.35, 1.7);
                        // big painted patches of lighter / darker / warmer colour across lawns, walls, roads
                        float big = ValueNoise(i.positionWS * 0.09) * 0.65 + ValueNoise(i.positionWS * 0.31) * 0.35;
                        half3 warm = baseCol * half3(1.08, 1.04, 0.86);
                        baseCol = lerp(baseCol * 0.86, warm * 1.06, big * k);
                    }
                    // --- Hit & Run: soft wrapped diffuse, hemisphere ambient, painted-texture mottling
                    float3 vdir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half wrap = saturate((ndl + 0.45) / 1.45);
                    wrap = wrap * wrap * (3.0 - 2.0 * wrap);                         // soft terminator
                    half sh = lerp(0.35, 1.0, light.shadowAttenuation);            // cast shadows stay light
                    half3 amb = lerp(_ShadeGround.rgb, _ShadeSky.rgb, n.y * 0.5 + 0.5);
                    half3 lit = amb + light.color * wrap * sh * 0.78;
                    // large, gentle colour variation so flat fills read like painted textures
                    float mott = ValueNoise(i.positionWS * 0.45) * 0.6 + ValueNoise(i.positionWS * 2.3) * 0.4;
                    half3 c = baseCol * lit * (0.94 + 0.12 * mott * (1.0 - gloss));
                    // glossy paint + chrome: tight highlight, soft sky reflection, rim
                    float3 h = normalize(light.direction + vdir);
                    half spec = pow(saturate(dot(n, h)), lerp(24.0, 90.0, gloss)) * sh;
                    half fres = pow(1.0 - saturate(dot(n, vdir)), 3.0);
                    c += light.color * spec * (0.08 + 0.9 * gloss);
                    c = lerp(c, _ShadeSky.rgb * 1.25, fres * (0.06 + 0.3 * gloss) * saturate(n.y + 0.6));
                    c *= lerp(half3(1, 1, 1), min(light.color, 1.2), 0.25);
                    c += baseCol * _Emit;                                          // lit lamps
                    c = MixFog(c, i.fogFactor);
                    return half4(c, 1);
                }

                half3 fill = lerp(baseCol, paper, washAmt * (0.55 + 0.45 * wash));
                // crisp two-tone cel shading, cartoon style
                half toneStep = smoothstep(_LightThreshold - 0.03, _LightThreshold + 0.03, lightAmt);
                half3 col = lerp(fill * shadowTint, fill, toneStep);

                // --- pen hatching (Lat): cast shadows always, shaded side if formHatch -------
                float3 an = abs(n);
                float2 uv = an.x > an.y && an.x > an.z ? i.positionWS.zy
                          : (an.y > an.z ? i.positionWS.xz : i.positionWS.xy);
                float formDark = saturate((_LightThreshold - saturate(ndl)) / max(_LightThreshold, 1e-3)) * formHatch;
                // only shadows *cast onto* sun-facing surfaces get hatched; the far side of a
                // building just takes the flat cel shadow tone
                float darkness = max(formDark, castShadow * 0.8 * smoothstep(0.05, 0.2, ndl));
                float h1 = HatchLines(uv, normalize(float2(1, 1)), _HatchDensity, darkness, 1.7);
                float h2 = HatchLines(uv, normalize(float2(1, -1)), _HatchDensity * 1.1, darkness - 0.45, 4.1);
                float hatch = h1 * step(0.02, darkness) + h2 * step(0.55, darkness) * (1.0 - h1);
                col = lerp(col, _InkColor.rgb, saturate(hatch) * hatchAmt);

                // time of day: a wash of the sun's colour (sunset orange, night blue)
                col *= lerp(half3(1, 1, 1), min(light.color, 1.2), 0.55);

                // --- paper grain -----------------------------------------------------------------
                float grain = ValueNoise(i.positionWS * 9.0);
                col *= 1.0 + (0.08 * grain - 0.06) * grainAmt;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------
        // Hand-inked outline: inverted hull, constant-ish pixel width, noisy thickness.
        // Uses smoothed normals from UV3 (written by the model importer) when present so
        // hard-edged boxes don't crack at the corners.
        // ------------------------------------------------------------------------------
        Pass
        {
            Name "LatOutline"
            // the game runs the Hit & Run look, which has no ink outlines: under its own LightMode URP never
            // draws this pass (a custom renderer feature could bring the outlines back)
            Tags { "LightMode" = "LatOutline" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 smoothNormal : TEXCOORD3;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                // smoothed normals: UV3 for rigid meshes; for skinned meshes they ride in the
                // tangent (flagged w = 2) because Unity skins tangents but not UVs
                float3 nOS = dot(v.smoothNormal.xyz, v.smoothNormal.xyz) > 0.25 ? v.smoothNormal.xyz
                           : (v.tangentOS.w > 1.5 ? v.tangentOS.xyz : v.normalOS);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float4 posCS = TransformWorldToHClip(posWS);
                float3 nWS = TransformObjectToWorldNormal(nOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                nCS = len > 1e-5 ? nCS / len : float2(0, 0);

                half wob = _LatStyleSet > 0.5 ? _LatStyle.w : _OutlineWobble;
                float wobble = 1.0 + (ValueNoise(posWS * 1.3) - 0.5) * 2.0 * wob;
                // thinner for far objects, so the skyline doesn't turn into a black blob
                float distFade = lerp(1.0, 0.35, saturate(posCS.w / 180.0));
                float px = _OutlineWidth * wobble * distFade * (_ShadeMode > 0.5 ? _ShadeOutline : 1.0);
                posCS.xy += nCS * px * 2.0 / _ScreenParams.xy * posCS.w;
                // outlines switched off: collapse the hull so it rasterises nothing
                if (_ShadeMode > 0.5 && _ShadeOutline < 0.01) posCS = float4(0, 0, 0, 1);
                o.positionCS = posCS;
                o.fogFactor = ComputeFogFactor(posCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(MixFog(_InkColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
            #if UNITY_REVERSED_Z
                posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return posCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
