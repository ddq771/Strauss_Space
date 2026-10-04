Shader "Strauss Space/Earth Surface"
{
    // Earth lit by the Sun alone (SolarSystem's _SunWorldDir, no scene
    // ambient): NASA Blue Marble by day, Black Marble city lights on the night
    // side fading in across the terminator, sunglint on water (mask in the
    // day map's alpha), and a slowly drifting cloud layer that is lit by the
    // Sun and dims the city lights beneath it. The atmosphere shell adds the
    // haze and limb glow on top.
    Properties
    {
        _DayTex ("Day (RGB) + water mask (A)", 2D) = "white" {}
        _NightTex ("City lights", 2D) = "black" {}
        _CloudTex ("Clouds", 2D) = "black" {}
        _CloudOffset ("Cloud drift (U)", Float) = 0
        _Sunlight ("Sunlit brightness", Float) = 1.15
        _CityLights ("City light brightness", Float) = 1.3
        _Glint ("Ocean sunglint", Float) = .55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _DayTex, _NightTex, _CloudTex;
            float _CloudOffset, _Sunlight, _CityLights, _Glint;
            float4 _SunWorldDir;   // SolarSystem: toward the Sun

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 worldPos : TEXCOORD1; float2 uv : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 sun = normalize(_SunWorldDir.xyz + float3(0, 1e-6, 0));
                float ndl = dot(n, sun);
                float4 day = tex2D(_DayTex, i.uv);
                float water = day.a;

                // Day side: diffuse sunlight with a soft terminator.
                float light = saturate(ndl * 1.05 + .03);
                float3 color = day.rgb * light * _Sunlight;

                // Sunglint: the Sun's reflection off open water.
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 h = normalize(sun + view);
                color += pow(saturate(dot(n, h)), 140) * water * _Glint * saturate(ndl * 4) * float3(1, .95, .85);

                // Clouds, lit by the Sun.
                float cloud = tex2D(_CloudTex, i.uv + float2(_CloudOffset, 0)).r;
                color = lerp(color, cloud * saturate(ndl * 1.1 + .05) * _Sunlight * float3(1, 1, 1), cloud * .85);

                // Night side: city lights, dimmed under cloud.
                float night = smoothstep(.06, -.18, ndl);
                float3 cities = tex2D(_NightTex, i.uv).rgb;
                color += pow(cities, 1.6) * _CityLights * night * (1 - cloud * .75) * float3(1, .86, .62);

                return fixed4(color, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
