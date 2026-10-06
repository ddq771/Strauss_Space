Shader "Strauss Space/Heat Zones"
{
    // The heat-shield map (H in flight), drawn over a copy of each hull
    // mesh. Every surface point takes the zone its normal falls in (the
    // first whose cone holds it, as ReentryHeating assigns patches): each
    // zone tinted its own colour with a bright line along its edge, bare
    // structure striped. On top, the nearest heating patch's temperature
    // as a fraction of its rating: yellow from ~60%, red near the limit,
    // flashing over it.
    Properties
    {
        _ZoneCount ("Zones", Float) = 0
        _Opacity ("Opacity", Range(0, 1)) = .6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _PatchDir[96];     // world direction, w = temperature / rating
            float4 _ZoneAxis[8];      // world direction, w = cos(half-angle)
            float4 _ZoneColor[9];     // per zone; [_ZoneCount] = bare structure
            float _ZoneCount, _Opacity;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                int count = (int)_ZoneCount;
                int zone = count;
                float edge = 0;
                for (int z = 0; z < 8; z++)
                {
                    if (z >= count) break;
                    float d = dot(n, _ZoneAxis[z].xyz) - _ZoneAxis[z].w;
                    if (d >= 0) { zone = z; edge = 1 - saturate(d / .03); break; }
                }
                float3 color = _ZoneColor[zone].rgb;
                float alpha = _Opacity;
                if (zone == count)
                {
                    // Bare structure: diagonal stripes in screen-independent world space.
                    float stripe = frac(dot(i.world, float3(.7, .7, 0)) * 4);
                    alpha *= stripe < .5 ? 1 : .35;
                }
                // Nearest heating patch: its share of the rating.
                float best = -2, heat = 0;
                for (int p = 0; p < 96; p++)
                {
                    float d = dot(n, _PatchDir[p].xyz);
                    if (d > best) { best = d; heat = _PatchDir[p].w; }
                }
                float3 hot = heat < .85 ? lerp(float3(1, .95, .2), float3(1, .45, .1), saturate((heat - .6) / .25))
                                        : lerp(float3(1, .45, .1), float3(1, .1, .08), saturate((heat - .85) / .15));
                float heating = saturate((heat - .35) / .25);
                color = lerp(color, hot, heating);
                alpha = lerp(alpha, .85, heating);
                if (heat > 1) color *= .6 + .4 * sin(_Time.y * 20);
                // Bright line along a zone's edge.
                color = lerp(color, float3(1, 1, 1), edge * .85);
                alpha = max(alpha, edge * .95);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
