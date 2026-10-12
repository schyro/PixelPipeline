// Rain impacts. Every splash is procedural and belongs to one drop: RainDrops.hlsl gives the same
// instance the same landing point and time it gives the drop shader, and a short crown/ring
// animation is drawn there with hard edges from the moment the drop lands. No mesh, no
// per-instance buffer and no CPU simulation.
Shader "PixelPipeline/RainSplash"
{
    Properties
    {
        [HDR] _SplashColor ("Splash Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RainSplash"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Environment/RainDrops.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SplashColor;
            CBUFFER_END

            // Uploaded by PixelRainRenderer.
            half4 _RainSplashColor;
            // x: minimum size, y: maximum size, z: lifetime in seconds, w: ring width.
            float4 _RainSplashShape;
            // x: minimum surface up-facing.
            float4 _RainSplashSurface;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // xy: splash-local position in units of the splash size, z: age 0-1, w: flattening.
                float4 local : TEXCOORD0;
            };

            static const float2 QuadCorners[6] =
            {
                float2(-1.0, 0.0), float2(-1.0, 1.0), float2(1.0, 0.0),
                float2(1.0, 0.0), float2(-1.0, 1.0), float2(1.0, 1.0)
            };

            Varyings Collapsed()
            {
                Varyings output = (Varyings)0;
                // Outside the clip volume on every platform, so the triangle is discarded.
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                return output;
            }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                PixelRainDrop drop;
                if (!PixelRainGetDrop(instanceID, drop) || !drop.onSurface)
                    return Collapsed();

                float age = drop.sinceLanding / max(0.05, _RainSplashShape.z);
                if (age < 0.0 || age >= 1.0)
                    return Collapsed();

                float2 uv = drop.landingUV;
                float3 surfaceWS = drop.landingWS;

                // Surfaces the drops only graze - walls, the sides of a slab - do not splash, and a
                // texel on a depth edge would reconstruct a point floating in the air.
                float texel = _PixelRainOcclusionParams.z;
                float depthRight = PixelRainOcclusionSampleDepth(uv + float2(texel, 0.0));
                float depthUp = PixelRainOcclusionSampleDepth(uv + float2(0.0, texel));
                if (PixelRainOcclusionIsEmpty(depthRight) || PixelRainOcclusionIsEmpty(depthUp))
                    return Collapsed();

                float3 toRight = PixelRainOcclusionSurfacePosition(uv + float2(texel, 0.0), depthRight) - surfaceWS;
                float3 toUp = PixelRainOcclusionSurfacePosition(uv + float2(0.0, texel), depthUp) - surfaceWS;
                float3 normalWS = normalize(cross(toUp, toRight));
                if (abs(normalWS.y) < _RainSplashSurface.x)
                    return Collapsed();

                float size = lerp(_RainSplashShape.x, _RainSplashShape.y, GenerateHashedRandomFloat(drop.seed + 5u));

                float3 cameraRight = UNITY_MATRIX_V[0].xyz;
                float3 cameraUp = UNITY_MATRIX_V[1].xyz;
                float3 towardCamera = UNITY_MATRIX_V[2].xyz;
                // How squashed a horizontal circle looks from this camera.
                float flattening = clamp(abs(towardCamera.y), 0.2, 1.0);

                float2 corner = QuadCorners[vertexID % 6];
                // The quad reaches down far enough to hold the flattened ring and up for the crown.
                float2 local = float2(corner.x, lerp(-flattening, 1.0, corner.y));

                // Pulled towards the camera so the part of the ring "below" the surface point is not
                // swallowed by the ground it lies on. The view is orthographic, so this does not move
                // the splash on screen.
                float3 positionWS = surfaceWS
                                  + cameraRight * (local.x * size)
                                  + cameraUp * (local.y * size)
                                  + towardCamera * (size * 1.5);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.local = float4(local, age, flattening);
                return output;
            }

            half Disc(float2 position, float2 centre, float radius)
            {
                return step(length(position - centre), radius);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.local.xy;
                float age = input.local.z;
                float flattening = input.local.w;

                // Impact column: a short vertical spike during the first frames.
                half column = step(abs(p.x), 0.07) * step(-0.05, p.y)
                            * step(p.y, 0.7 * saturate(1.0 - age / 0.3));

                // Crown: two droplets thrown sideways on a parabola, and a smaller one straight up.
                float arc = 4.0 * age * (1.0 - age);
                float spread = lerp(0.2, 0.8, age);
                half crown = Disc(p, float2(-spread, arc * 0.65), 0.11)
                           + Disc(p, float2(spread, arc * 0.65), 0.11)
                           + Disc(p, float2(0.0, arc * 0.95), 0.08) * step(age, 0.7);
                crown *= step(0.08, age) * step(age, 0.92);

                // Ring: a flat, expanding circle on the surface, squashed by the view angle.
                float ringRadius = lerp(0.25, 0.95, age);
                float ellipse = length(float2(p.x, p.y / flattening));
                half ring = step(abs(ellipse - ringRadius), _RainSplashShape.w) * step(0.2, age);

                half coverage = saturate(column + crown + ring);
                clip(coverage - 0.5);

                // Two alpha bands instead of a fade keep the edge a hard pixel edge.
                half alpha = _SplashColor.a * _RainSplashColor.a * (age > 0.7 ? 0.55h : 1.0h);
                return half4(_SplashColor.rgb * _RainSplashColor.rgb * _PixelRainLight.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
