Shader "Strauss Space/Atmosphere"
{
    // Fresnel/rim-light atmospheric glow for a thin shell wrapped around a
    // planet. This is a rim-light approximation of real atmospheric
    // scattering - brightest at grazing view angles (the limb, matching real
    // photos of Earth from orbit), tinted toward the sun-facing side, and
    // nearly transparent when looking straight down through it so the
    // ground underneath stays clear. It is tuned to look right rather than
    // to integrate an actual Rayleigh/Mie single-scattering model.
    Properties
    {
        _RimColor ("Limb Color", Color) = (0.55, 0.75, 1.0, 1)
        _DayColor ("Day-side Color", Color) = (0.35, 0.55, 1.0, 1)
        _NightColor ("Night-side Color", Color) = (0.05, 0.05, 0.12, 1)
        _RimPower ("Rim Falloff", Range(0.5, 8)) = 3.0
        _Intensity ("Intensity", Range(0, 4)) = 1.6
        _SunDirection ("Sun Direction (world, toward the sun)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Back
        ZWrite Off
        // Additive: the shell only ever adds light, so a slightly-off alpha
        // or normal never produces a dark or inside-out-looking cutout - the
        // worst case is too much or too little glow, never a broken look.
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            fixed4 _RimColor;
            fixed4 _DayColor;
            fixed4 _NightColor;
            float _RimPower;
            float _Intensity;
            float4 _SunDirection;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                // 1 looking straight down through the shell (thin path,
                // nearly transparent), 0 at the grazing-angle limb (long
                // path through the shell, where the glow is brightest).
                float ndotv = saturate(dot(normal, viewDir));
                float rim = pow(1.0 - ndotv, _RimPower);

                float sunFacing = saturate(dot(normal, normalize(_SunDirection.xyz)) * 0.5 + 0.5);
                fixed3 tint = lerp(_NightColor.rgb, _DayColor.rgb, sunFacing);
                fixed3 color = lerp(tint, _RimColor.rgb, rim) * _Intensity;

                float alpha = rim * saturate(sunFacing * 0.8 + 0.2);
                return fixed4(color * alpha, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
