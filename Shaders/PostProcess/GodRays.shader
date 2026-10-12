// Light shafts where sunlight falls through gaps in the cloud layer, raymarched at the internal
// resolution and added over the pixelated scene.
//
// A point in the air is lit when the ground point it looks down on along the sun lies in a gap: in
// the clear, but with cloud around it. Open sky has no cloud around and solid cloud lets nothing
// through, so shafts only appear where the cloud shadows have holes, and land exactly in the sunlit
// patches those holes leave on the ground. The march is quantised into a few dithered bands.
Shader "Hidden/PixelPipeline/GodRays"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "GodRays"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"
            #include "CloudCommon.hlsl"

            #define GOD_RAY_MAX_STEPS 48

            // Linear light colour, already scaled by intensity, weather, and sun height.
            float4 _GodRayColor;
            // Unit vector from the scene towards the sun.
            float4 _GodRaySunDirection;
            // x: ground height, y: shaft top, z: height over which the haze thins by e, w: top fade length.
            float4 _GodRayVolume;
            // Height above the ground over which shafts fade in. Where there is no ground under a
            // shaft, as beside a floating island, it would otherwise stop in mid-air at full strength.
            float _GodRayGroundFade;
            // x: steps, y: density, z: bands (0 = smooth), w: dither between bands.
            float4 _GodRayMarch;
            // x: gap threshold, y: gap softness, z: mip level of the cloud around, w: mip level of the gap.
            float4 _GodRayMask;
            // xy: area centre on X/Z, z: area radius, w: 1 when buildings block the shafts.
            float4 _GodRayArea;

            // 4x4 ordered dither, anchored to the internal pixel grid like hand-placed dither.
            float GodRayBayer(float2 pixel)
            {
                uint2 p = uint2(pixel) & 3u;
                const float bayer[16] =
                {
                     0.0,  8.0,  2.0, 10.0,
                    12.0,  4.0, 14.0,  6.0,
                     3.0, 11.0,  1.0,  9.0,
                    15.0,  7.0, 13.0,  5.0
                };
                return (bayer[p.y * 4u + p.x] + 0.5) / 16.0;
            }

            // Clear here but clouded around: how much of a gap in the clouds this ground point sits in.
            float GodRayGap(float3 groundWS)
            {
                float here = PixelPipelineCloudShapeLod(groundWS, _GodRayMask.w);
                float around = PixelPipelineCloudShapeLod(groundWS, _GodRayMask.z);
                return smoothstep(_GodRayMask.x, _GodRayMask.x + _GodRayMask.y, here - around);
            }

            // One unfiltered shadow-map tap: whether the sun reaches this point past the scene geometry.
            half GodRaySunlit(float3 positionWS)
            {
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    half cascadeIndex = PixelPipelineShadowCascadeIndex(positionWS);
                    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));
                    if (BEYOND_SHADOW_FAR(shadowCoord) || any(shadowCoord.xy < 0.0) || any(shadowCoord.xy > 1.0))
                        return 1.0h;
                    return SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, shadowCoord.xyz);
                #else
                    return 1.0h;
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);

                // The view ray from the near plane to the first surface (or the far plane).
                float deviceDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);
                #endif
                float3 startWS = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 endWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 ray = endWS - startWS;

                // Only the slab of air between the ground and the top of the shafts is marched.
                float groundY = _GodRayVolume.x;
                float topY = _GodRayVolume.y;
                float enter = 0.0;
                float exit = 1.0;
                if (abs(ray.y) > 1e-5)
                {
                    float toGround = (groundY - startWS.y) / ray.y;
                    float toTop = (topY - startWS.y) / ray.y;
                    enter = max(enter, min(toGround, toTop));
                    exit = min(exit, max(toGround, toTop));
                }
                else if (startWS.y < groundY || startWS.y > topY)
                {
                    return color;
                }

                if (exit <= enter)
                    return color;

                int steps = clamp((int)_GodRayMarch.x, 1, GOD_RAY_MAX_STEPS);
                float3 stepWS = ray * ((exit - enter) / steps);
                float stepLength = length(stepWS);
                // Neighbouring pixels start their march at staggered offsets, which turns the slicing
                // of a short march into a fine ordered pattern instead of visible stripes.
                float dither = GodRayBayer(input.positionCS.xy);
                float3 positionWS = startWS + ray * enter + stepWS * dither;
                float3 sun = _GodRaySunDirection.xyz;

                float light = 0.0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float height = positionWS.y - groundY;
                    float2 fromCentre = positionWS.xz - _GodRayArea.xy;
                    float areaFade = 1.0 - smoothstep(_GodRayArea.z * 0.6, _GodRayArea.z, length(fromCentre));
                    float haze = exp(-height / _GodRayVolume.z)
                               * smoothstep(0.0, _GodRayGroundFade, height)
                               * (1.0 - smoothstep(topY - groundY - _GodRayVolume.w, topY - groundY, height));

                    float weight = areaFade * haze;
                    if (weight > 0.001)
                    {
                        // Down along the sun to the ground this point looks onto.
                        float3 groundWS = positionWS - sun * (height / sun.y);
                        float gap = GodRayGap(groundWS);
                        if (gap > 0.001 && _GodRayArea.w > 0.5)
                            gap *= GodRaySunlit(positionWS);
                        light += gap * weight;
                    }

                    positionWS += stepWS;
                }

                float shaft = 1.0 - exp(-light * stepLength * _GodRayMarch.y);

                // A few flat tones. Dither only mixes neighbouring tones close to where they meet,
                // so each band keeps a solid middle.
                float bands = _GodRayMarch.z;
                if (bands > 0.5)
                    shaft = floor(shaft * bands + 0.5 + (frac(dither + 0.5) - 0.5) * _GodRayMarch.w) / bands;

                color.rgb += _GodRayColor.rgb * saturate(shaft);
                return color;
            }
            ENDHLSL
        }
    }
}
