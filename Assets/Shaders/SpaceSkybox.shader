Shader "Skybox/ProceduralStarfield"
{
    Properties
    {
        _StarDensity ("Star Density", Range(50, 400)) = 220
        _StarBrightness ("Star Brightness", Range(0, 5)) = 2.2
        _SkyColorTop ("Sky Color Top", Color) = (0.01, 0.01, 0.045, 1)
        _SkyColorBottom ("Sky Color Bottom", Color) = (0, 0, 0.015, 1)
        _NebulaColorA ("Nebula Color A", Color) = (0.3, 0.06, 0.42, 1)
        _NebulaColorB ("Nebula Color B", Color) = (0.04, 0.18, 0.4, 1)
        _NebulaStrength ("Nebula Strength", Range(0, 3)) = 1.1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float _StarDensity;
            float _StarBrightness;
            fixed4 _SkyColorTop;
            fixed4 _SkyColorBottom;
            fixed4 _NebulaColorA;
            fixed4 _NebulaColorB;
            float _NebulaStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0, 0, 0));
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                float t = saturate(dir.y * 0.5 + 0.5);
                fixed3 col = lerp(_SkyColorBottom.rgb, _SkyColorTop.rgb, t);

                // Layered nebula clouds from low-frequency value noise.
                float nebulaA = valueNoise(dir * 2.2 + float3(3.7, 1.1, 5.4));
                float nebulaB = valueNoise(dir * 4.5 + float3(9.2, 4.6, 1.8));
                float nebulaMask = saturate(nebulaA * 0.65 + nebulaB * 0.35 - 0.35);
                fixed3 nebulaColor = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, nebulaB);
                col += nebulaColor * nebulaMask * _NebulaStrength;

                // Dense procedural starfield, three interleaved layers for depth.
                for (int layer = 0; layer < 3; layer++)
                {
                    float scale = _StarDensity * (1.0 + layer * 0.6);
                    float3 cell = floor(dir * scale + layer * 17.3);
                    float starSeed = hash13(cell + layer * 3.7);
                    float starMask = step(0.9975, starSeed);
                    float twinkle = hash13(cell + 91.7 + layer);
                    float brightness = _StarBrightness * (0.5 + 0.5 * twinkle) / (1.0 + layer * 0.5);
                    col += starMask * brightness;
                }

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
