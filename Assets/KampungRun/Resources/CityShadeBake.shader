// Kampung Run: KL - bakes the city's painted shade (CityShade.cs) from a top-down height map.
// Pass 0: how much sky a spot at street level sees past the roofs, awnings, decks and crowns around it.
// Pass 1: a 3x3 tent blur that smooths pass 0's dithered directions into an even wash.
// Pass 2: draws the height map (world height per pixel, seen from straight above).
Shader "Hidden/KampungRun/CityShadeBake"
{
    Properties { _MainTex ("", 2D) = "white" {} }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _Bake;      // x metres per texel, y eye height (street level + 0.3 m)
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            float4 frag(v2f_img i) : SV_Target
            {
                float eye = _Bake.y;
                float own = tex2Dlod(_MainTex, float4(i.uv, 0, 0)).r;
                // under cover (an awning, a deck, a crown, or inside a building) the sky straight up is gone, and
                // the cover itself doesn't block the view out sideways: only things taller than it count
                bool covered = own > eye + 0.4;
                float ignore = covered ? own + 0.5 : -1e6;
                const int DIRS = 16;
                const int STEPS = 10;
                // a different start angle per texel breaks the 16 spokes up; pass 1 smooths the grain away
                float spin = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715)))) * (6.2831853 / DIRS);
                float2 perMetre = _MainTex_TexelSize.xy / _Bake.x;
                float sky = 0;
                [loop] for (int d = 0; d < DIRS; d++)
                {
                    float a = spin + d * (6.2831853 / DIRS);
                    float2 dir = float2(cos(a), sin(a)) * perMetre;
                    float steep = 0;
                    float dist = 0.7;
                    [unroll] for (int s = 0; s < STEPS; s++)
                    {
                        float hh = tex2Dlod(_MainTex, float4(i.uv + dir * dist, 0, 0)).r;
                        if (hh > ignore) steep = max(steep, (hh - eye) / dist);
                        dist *= 1.55;                 // 0.7 m .. 36 m
                    }
                    // a diffuse floor under a horizon this steep sees cos^2 of its sky slice
                    sky += 1.0 / (1.0 + steep * steep);
                }
                sky /= DIRS;
                return (covered ? sky * 0.55 : sky).xxxx;
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                float s = tex2D(_MainTex, i.uv).r * 4.0;
                s += (tex2D(_MainTex, i.uv + float2(t.x, 0)).r + tex2D(_MainTex, i.uv - float2(t.x, 0)).r
                    + tex2D(_MainTex, i.uv + float2(0, t.y)).r + tex2D(_MainTex, i.uv - float2(0, t.y)).r) * 2.0;
                s += tex2D(_MainTex, i.uv + t).r + tex2D(_MainTex, i.uv - t).r
                   + tex2D(_MainTex, i.uv + float2(t.x, -t.y)).r + tex2D(_MainTex, i.uv + float2(-t.x, t.y)).r;
                return (s / 16.0).xxxx;
            }
            ENDCG
        }

        // 2: the height map itself: city renderers drawn from straight above, each pixel keeping its world height
        Pass
        {
            ZTest LEqual ZWrite On Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct v2f { float4 pos : SV_POSITION; float y : TEXCOORD0; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float4 w = mul(unity_ObjectToWorld, float4(vertex.xyz, 1.0));
                o.pos = mul(UNITY_MATRIX_VP, w);
                o.y = w.y;
                return o;
            }

            float4 frag(v2f i) : SV_Target { return i.y.xxxx; }
            ENDCG
        }
    }
    FallBack Off
}
