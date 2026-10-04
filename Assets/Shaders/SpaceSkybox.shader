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
            // Set by SolarSystem: xyz = direction to the Sun, w = its angular
            // radius (radians) - 0.27° from Earth's distance, varying a few
            // percent between perihelion and aphelion.
            float4 _SolarDirection;
            // Earth's rotation angle (SolarSystem): the stars are fixed in
            // inertial space, so they turn across the Earth-fixed sky.
            float _SkyAngle;
            // Set by MoonBody: world position + radius (world units), its
            // orientation (for the texture), its map, and whether the sky
            // should draw it (not once the camera can see the real sphere).
            float4 _MoonPosition;
            float4x4 _MoonWorldToLocal;
            sampler2D _MoonTex;
            float _MoonSkyDisc;

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
                float3 viewDir = normalize(i.dir);
                // Earth-fixed view direction -> inertial (add the rotation to
                // its longitude) for the stars and nebulae.
                float ca = cos(_SkyAngle), sa = sin(_SkyAngle);
                float3 dir = float3(viewDir.x * ca - viewDir.z * sa, viewDir.y, viewDir.x * sa + viewDir.z * ca);

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

                // The Sun's disc at its true angular size, limb-darkened, with a
                // faint glow around it. (The atmosphere shader reddens and dims
                // it through the air like any other background.)
                float sunRadius = max(_SolarDirection.w, 1e-4);
                if (dot(_SolarDirection.xyz, _SolarDirection.xyz) > 0.5)
                {
                    float angle = acos(saturate(dot(viewDir, normalize(_SolarDirection.xyz))));
                    float r = angle / sunRadius;
                    if (r < 1)
                    {
                        float mu = sqrt(1 - r * r);
                        col = fixed3(1, .97, .9) * (0.4 + 0.6 * mu);
                    }
                    else
                    {
                        col += fixed3(1, .9, .7) * 0.35 * exp(-(r - 1) * 1.6);
                    }
                }

                // The Moon's disc, from where this camera actually is (so no
                // parallax error at the surface): the LRO map on a sphere,
                // lit by the Sun - real phases - plus a little earthshine on
                // the dark side.
                if (_MoonSkyDisc > 0.5 && _MoonPosition.w > 0)
                {
                    float3 toMoon = _MoonPosition.xyz - _WorldSpaceCameraPos;
                    float moonDist = length(toMoon);
                    float3 m = toMoon / moonDist;
                    float moonRadius = asin(saturate(_MoonPosition.w / moonDist));
                    float rr = acos(saturate(dot(viewDir, m))) / moonRadius;
                    if (rr < 1)
                    {
                        float3 t = viewDir - m * dot(viewDir, m);
                        float tl = length(t);
                        t = tl > 1e-7 ? t / tl : float3(0, 0, 0);
                        float3 n = normalize(t * rr - m * sqrt(1 - rr * rr));
                        float3 local = mul((float3x3)_MoonWorldToLocal, n);
                        float2 uv = float2(atan2(local.z, local.x) / (2 * UNITY_PI) + 0.5, 0.5 + asin(clamp(local.y, -1, 1)) / UNITY_PI);
                        float3 albedo = tex2Dlod(_MoonTex, float4(uv, 0, 0)).rgb;
                        float3 sunDir = _SolarDirection.xyz;
                        float lit = dot(sunDir, sunDir) > 0.5 ? saturate(dot(n, normalize(sunDir))) : 1;
                        col = albedo * (lit * 1.6 + 0.015);
                    }
                }

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
