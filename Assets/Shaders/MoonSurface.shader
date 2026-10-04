Shader "Strauss Space/Moon Surface"
{
    // Airless body lit by the Sun alone (the scene's ambient light is tuned
    // for daylight on the launch pad and would flatten the phases): Lambert
    // from SolarSystem's _SolarDirection, plus faint earthshine on the night
    // side - so the terminator and phases look as they do from space.
    Properties
    {
        _MainTex ("Albedo (LRO colour map)", 2D) = "white" {}
        _SlopeTex ("Surface slopes (LOLA elevation): R east, G north", 2D) = "gray" {}
        _Relief ("Relief exaggeration", Float) = 1.6
        _Brightness ("Sunlit Brightness", Float) = 1.6
        _Earthshine ("Earthshine", Float) = 0.015
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _SlopeTex;
            float _Relief;
            float4 _MainTex_ST;
            float _Brightness, _Earthshine;
            float4 _SunFallback;      // MoonBody: direction toward the Sun

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float2 uv : TEXCOORD1; float3 localNormal : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.localNormal = v.normal;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 sun = _SunFallback.xyz;
                // Tilt the sphere's normal by the real surface slopes (LOLA
                // laser altimetry): crater walls and mountains catch or turn
                // away from the light, strongest along the terminator.
                float3 n = normalize(i.localNormal);
                float3 east = normalize(cross(n, float3(0, 1, 0)) + float3(1e-5, 0, 0));
                float3 north = cross(east, n);
                float2 slope = (tex2D(_SlopeTex, i.uv).rg * 2 - 1) * _Relief;
                float3 bumped = normalize(n - slope.x * east - slope.y * north);
                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, bumped));
                float lit = saturate(dot(worldNormal, normalize(sun)));
                float3 albedo = tex2D(_MainTex, i.uv).rgb;
                return fixed4(albedo * (lit * _Brightness + _Earthshine), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
