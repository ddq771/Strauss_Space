Shader "Strauss Space/Atmosphere"
{
    // Single-scattering atmosphere (Nishita-style Rayleigh + Mie + ozone),
    // drawn on one shell around the planet. Each pixel marches the camera
    // ray through a continuously exponential atmosphere - no discrete
    // layers, so the thinning from dense lower air to space is a smooth
    // gradient - and accumulates sunlight scattered toward the camera:
    //
    //  * Rayleigh (air molecules, scale height 8 km): scatters blue most,
    //    which gives the blue day sky and blue limb, and leaves reddened
    //    light at sunrise/sunset where the sun's path through air is long.
    //  * Ozone (absorbs, mostly orange/red): deepens the zenith blue and
    //    the twilight sky.
    //  * Mie (haze/aerosols, scale height 1.2 km): forward-scattered white
    //    glow around the sun and low-altitude haze.
    //
    // The ray stops at the ground (scene depth, or the planet sphere), so
    // distant terrain picks up blue haze and dims ("aerial perspective").
    //
    // Compositing happens in linear light against a grab of what's already
    // drawn: background * transmittance + scattered light. The project
    // renders in Gamma colour space, where simply adding the haze on top of
    // gamma-encoded colours washes the ground out several times too much.
    //
    // Brightness is physical relative to the scene's Sun light: a sunlit
    // white surface and the sky come out in their real-world ratio.
    // Lengths are world units (km in this project); Atmosphere.cs feeds the
    // planet centre/radii and coefficients in those units.
    Properties
    {
        _PlanetCenter ("Planet Centre (world)", Vector) = (0, 0, 0, 0)
        _PlanetRadius ("Planet Radius (world)", Float) = 6378.1
        _AtmosphereRadius ("Atmosphere Top Radius (world)", Float) = 6478.1
        _RayleighScatter ("Rayleigh Scattering (per world unit)", Vector) = (0.0058, 0.0135, 0.0331, 0)
        _OzoneAbsorb ("Ozone Absorption (per world unit)", Vector) = (0.00065, 0.001881, 0.000085, 0)
        _RayleighHeight ("Rayleigh Scale Height", Float) = 8
        _MieScatter ("Mie Scattering (per world unit)", Float) = 0.004
        _MieHeight ("Mie Scale Height", Float) = 1.2
        _MieG ("Mie Anisotropy", Range(0, 0.99)) = 0.8
        _SunIntensity ("Sun Irradiance", Float) = 12
        _Saturation ("Sky Saturation", Float) = 1.3
        _SunDirection ("Sun Direction (world, toward the sun)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        GrabPass { "_AtmosphereBackground" }

        Pass
        {
            // Back faces + depth pushed to the far plane: the shell's far
            // side always covers every pixel the atmosphere could affect,
            // whether the camera is on the ground, inside the air, or out in
            // orbit, and the far clip plane can never cut the dome away. The
            // ray itself is computed analytically from the camera.
            Cull Front
            ZWrite Off
            ZTest Always
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define VIEW_SAMPLES 32

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float4 grabPos : TEXCOORD2;
            };

            float4 _PlanetCenter;
            float _PlanetRadius, _AtmosphereRadius;
            float4 _RayleighScatter, _OzoneAbsorb;
            float _RayleighHeight, _MieScatter, _MieHeight, _MieG;
            float _SunIntensity, _Saturation;
            float4 _SunDirection;
            sampler2D _AtmosphereBackground;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                #if UNITY_REVERSED_Z
                o.pos.z = o.pos.w * 1e-6;
                #else
                o.pos.z = o.pos.w * (1 - 1e-6);
                #endif
                return o;
            }

            // Ray/sphere: returns (near, far) distances, far < near if missed.
            float2 Sphere(float3 origin, float3 dir, float radius)
            {
                float b = dot(origin, dir);
                float c = dot(origin, origin) - radius * radius;
                float d = b * b - c;
                if (d < 0) return float2(1, -1);
                d = sqrt(d);
                return float2(-b - d, -b + d);
            }

            // Chapman function (Schüler's approximation): air mass along a
            // straight ray to space from radius r, relative to straight up,
            // for an exponential atmosphere - replaces a nested march toward
            // the sun. x = r / scale height, mu = cos(zenith angle).
            float Chapman(float x, float mu)
            {
                float c = sqrt(UNITY_PI * x * 0.5);
                if (mu >= 0) return c / ((c - 1) * mu + 1);
                float s = sqrt(saturate(1 - mu * mu));
                return c / ((c - 1) * mu - 1) + 2 * c * exp(min(x - x * s, 80)) * sqrt(s);
            }

            // Optical depth (Rayleigh, Mie) from a point toward the sun;
            // returns -1 if the planet blocks the sun (in shadow).
            float2 SunOpticalDepth(float3 p, float3 sunDir)
            {
                if (Sphere(p, sunDir, _PlanetRadius).x > 0) return float2(-1, -1);
                float r = length(p);
                float h = r - _PlanetRadius;
                float mu = dot(p, sunDir) / r;
                return float2(
                    _RayleighHeight * exp(-h / _RayleighHeight) * Chapman(r / _RayleighHeight, mu),
                    _MieHeight * exp(-h / _MieHeight) * Chapman(r / _MieHeight, mu));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 background = GammaToLinearSpace(tex2Dproj(_AtmosphereBackground, i.grabPos).rgb);
                float3 camera = _WorldSpaceCameraPos - _PlanetCenter.xyz;
                float3 dir = normalize(i.worldPos - _WorldSpaceCameraPos);

                float2 atmo = Sphere(camera, dir, _AtmosphereRadius);
                if (atmo.y <= 0 || atmo.x > atmo.y) return fixed4(LinearToGammaSpace(background), 1);
                float start = max(atmo.x, 0);
                float end = atmo.y;

                // Stop at the ground: the planet sphere, or whatever opaque
                // geometry (terrain patch, rocket, pad) the depth buffer saw.
                float2 ground = Sphere(camera, dir, _PlanetRadius);
                bool hitsGround = ground.x > 0;
                if (hitsGround) end = min(end, ground.x);
                float raw = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos));
                #if UNITY_REVERSED_Z
                bool hasGeometry = raw > 1e-7;
                #else
                bool hasGeometry = raw < 1 - 1e-7;
                #endif
                if (hasGeometry)
                {
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    end = min(end, LinearEyeDepth(raw) / max(dot(dir, forward), 1e-4));
                }
                if (end <= start) return fixed4(LinearToGammaSpace(background), 1);

                float3 sunDir = normalize(_SunDirection.xyz);
                float3 betaR = _RayleighScatter.xyz;
                float3 extinctionR = betaR + _OzoneAbsorb.xyz;
                float betaM = _MieScatter;
                float span = end - start;
                // From inside the air, density is highest near the camera:
                // bunch samples there (t²). From space the dense air is at the
                // far end, so sample evenly. Checked against a 3000-sample
                // reference: within ~1-3% for sky and ground views.
                bool inside = atmo.x <= 0;
                float2 viewDepth = 0;
                float3 sumR = 0, sumM = 0;
                for (int s = 0; s < VIEW_SAMPLES; s++)
                {
                    float t0 = (float)s / VIEW_SAMPLES, t1 = (float)(s + 1) / VIEW_SAMPLES;
                    if (inside) { t0 *= t0; t1 *= t1; }
                    float3 p = camera + dir * (start + 0.5 * (t0 + t1) * span);
                    float h = length(p) - _PlanetRadius;
                    float2 density = exp(-h / float2(_RayleighHeight, _MieHeight)) * (t1 - t0) * span;
                    viewDepth += density;
                    float2 sunDepth = SunOpticalDepth(p, sunDir);
                    if (sunDepth.x < 0) continue;
                    float3 tau = extinctionR * (viewDepth.x + sunDepth.x) + betaM * 1.1 * (viewDepth.y + sunDepth.y);
                    float3 attenuation = exp(-tau);
                    sumR += density.x * attenuation;
                    sumM += density.y * attenuation;
                }

                float mu = dot(dir, sunDir);
                float phaseR = 3.0 / (16.0 * UNITY_PI) * (1 + mu * mu);
                float g = _MieG;
                float phaseM = 3.0 / (8.0 * UNITY_PI) * ((1 - g * g) * (1 + mu * mu)) /
                    ((2 + g * g) * pow(max(1 + g * g - 2 * g * mu, 1e-4), 1.5));
                float3 light = _SunIntensity * (sumR * betaR * phaseR + sumM * betaM * phaseM);
                // Single scattering on a gamma display reads greyer than the
                // eye sees a real sky; a mild saturation lift compensates.
                float grey = dot(light, float3(0.2126, 0.7152, 0.0722));
                light = max(0, lerp(grey.xxx, light, _Saturation));

                float3 transmittance = exp(-(extinctionR * viewDepth.x + betaM * 1.1 * viewDepth.y));
                // Open sky: a bright daytime sky drowns out the stars behind
                // it, the way the eye's adaptation does.
                // (Not the Sun's own disc and glow, which outshines the sky.)
                bool towardSun = dot(dir, sunDir) > cos(0.035);
                if (!hitsGround && !hasGeometry && !towardSun) transmittance *= saturate(1 - 6 * grey);
                float3 color = background * transmittance + light;
                return fixed4(LinearToGammaSpace(saturate(color)), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
