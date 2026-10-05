Shader "Strauss Space/Engine Plume"
{
    // A rocket exhaust plume, drawn additively on a cone hanging from the
    // nozzle (mesh uv.y: 0 at the nozzle exit, 1 at the tail). Brightest in
    // the core just behind the nozzle, fading down the plume and toward the
    // edges (it's a glowing gas volume: looking through the middle sees
    // more of it), with Mach diamonds for engines that show them at low
    // altitude and turbulent flicker.
    Properties
    {
        _CoreColor ("Core colour", Color) = (1, .9, .7, 1)
        _EdgeColor ("Edge colour", Color) = (1, .45, .1, 1)
        _Intensity ("Intensity", Float) = 1
        _Diamonds ("Mach diamonds", Range(0, 1)) = 0
        _DiamondCount ("Diamond count", Float) = 6
        _Seed ("Flicker seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CoreColor, _EdgeColor;
            float _Intensity, _Diamonds, _DiamondCount, _Seed;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; float3 view : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float along = i.uv.y;
                // Through the middle of the gas, not the thin edge.
                float facing = abs(dot(normalize(i.normal), i.view));
                float body = pow(facing, 1.4);
                float core = exp(-along * 4);
                // Fade in over the first few percent so the cone's open top
                // (at the nozzle exit) doesn't show as a hard ring.
                float fade = pow(saturate(1 - along), 1.3) * smoothstep(0, .07, along);
                float diamonds = 1 + _Diamonds * 1.4 * pow(saturate(cos(along * _DiamondCount * 6.2832)), 10) * saturate(1 - along * 1.6);
                float t = _Time.y * 37 + _Seed;
                float flicker = .85 + .15 * sin(t + along * 23) * sin(t * .7 + i.uv.x * 40);
                float3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core * 1.4));
                return fixed4(color * body * fade * diamonds * flicker * _Intensity, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
