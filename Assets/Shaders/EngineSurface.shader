Shader "Strauss Space/Engine Surface"
{
    Properties
    {
        _Color ("Color", Color) = (0.5,0.5,0.5,1)
        _Metallic ("Metallic", Range(0,1)) = 0.7
        _Glossiness ("Smoothness", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        // The imported visual models include open, thin nozzle shells.
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        struct Input { float facing : VFACE; };
        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
            o.Normal = float3(0,0,IN.facing > 0 ? 1 : -1);
        }
        ENDCG
    }
    FallBack "Standard"
}
