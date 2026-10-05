Shader "Strauss Space/Reentry Plasma"
{
    // The glowing shock layer of a vehicle entering the atmosphere, drawn
    // additively over a slightly inflated copy of each hull mesh. It glows
    // where the surface faces the direction of motion (_FlowDir, world
    // space) - the windward side that meets the air - brightest head-on,
    // with a rim of hot gas wrapping round the edges, flickering with
    // turbulence. The trail behind is a particle system (ReentryHeating).
    Properties
    {
        _Color ("Plasma colour", Color) = (1, .45, .15, 1)
        _Intensity ("Intensity", Float) = 0
        _FlowDir ("Direction of motion (world)", Vector) = (0, 1, 0, 0)
        _Inflate ("Shock stand-off (world units)", Float) = 0
        _Seed ("Flicker seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color, _FlowDir;
            float _Intensity, _Inflate, _Seed;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; float3 world : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                // The shock stands off the windward surface more than the lee.
                float windward = saturate(dot(n, normalize(_FlowDir.xyz)));
                world += n * _Inflate * (.35 + windward);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.normal = n;
                o.view = normalize(_WorldSpaceCameraPos - world);
                o.world = world;
                return o;
            }

            float hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 flow = normalize(_FlowDir.xyz);
                float windward = saturate(dot(n, flow));
                float rim = pow(1 - saturate(abs(dot(n, i.view))), 2);
                // Hot gas wraps a little round the shoulders, but the lee is dark.
                float shoulder = saturate(dot(n, flow) + .35);
                float t = _Time.y * 40 + _Seed;
                float flicker = .82 + .18 * sin(t + dot(i.world, float3(311, 173, 251)))
                              + .1 * (hash(floor(i.world * 3000 + t * .2)) - .5);
                float glow = (pow(windward, 1.3) * .75 + rim * shoulder * .8) * flicker * _Intensity;
                float3 color = lerp(_Color.rgb, float3(1, .9, .82), saturate(windward * windward * _Intensity * .12));
                return fixed4(color * glow, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
