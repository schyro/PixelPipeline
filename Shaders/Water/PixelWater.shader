// Stylised water for the pixel pipeline. Almost every knob lives on PixelPipelineSettings, under
// the Water header, so the whole scene stays in sync; only the per-surface multipliers below are
// material properties.
//
// Everything the water adds on top of the refracted scene - body colour, surface pattern, shore
// foam, sparkles - is lit by the main light and ambient like any other surface, so it follows the
// day/night cycle and the weather. Every pattern is anchored in world space, so orbiting the camera
// never moves it.
Shader "PixelPipeline/Water"
{
    Properties
    {
        _SurfaceTint ("Surface Tint", Color) = (1, 1, 1, 1)
        _DepthScale ("Depth Scale", Range(0.05, 8)) = 1
        _ReflectionScale ("Reflection Scale", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            // Before the other transparents, and after the opaque colour copy that refraction reads.
            "Queue" = "Transparent-100"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Water/PixelWaterCommon.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"

            struct WaterAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WaterVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            WaterVaryings WaterVertex(WaterAttributes input)
            {
                WaterVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                float waveHeight;
                float3 waveNormal;
                PixelWaterSampleWaves(positionInputs.positionWS.xz, waveHeight, waveNormal);

                float3 positionWS = positionInputs.positionWS + float3(0.0, waveHeight, 0.0);
                output.positionWS = positionWS;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 WaterFragment(WaterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionHCS);
                // Screen derivatives are only valid outside per-pixel branches, so everything that
                // needs them is evaluated up front.
                float worldPerPixel = PixelWaterWorldPerPixel(input.positionWS);

                float waveHeight;
                float3 normalWS;
                PixelWaterSampleWaves(input.positionWS.xz, waveHeight, normalWS);
                float patternMask = PixelWaterSurfacePattern(input.positionWS, waveHeight);

                float surfaceEyeDepth = -TransformWorldToView(input.positionWS).z;
                float sceneEyeDepth;
                float3 scenePositionWS;
                float sceneIsBackground;
                PixelWaterSampleScene(screenUv, sceneEyeDepth, scenePositionWS, sceneIsBackground);

                // Two depth measures: straight down for the colour ramp, and along the view ray so a
                // vertical bank still reports a thin sliver of water and therefore grows foam.
                float farDepth = _PixelWaterDepthDistance * 8.0;
                float verticalDepth = sceneIsBackground > 0.5
                    ? farDepth
                    : max(0.0, input.positionWS.y - scenePositionWS.y);
                float viewDepth = sceneIsBackground > 0.5
                    ? farDepth
                    : max(0.0, sceneEyeDepth - surfaceEyeDepth);
                float shoreDistance = min(verticalDepth, viewDepth);

                float2 refractedUv = screenUv;
                if (_PixelWaterRefraction > 0.0001)
                {
                    float fade = saturate(shoreDistance / _PixelWaterRefractionFade);
                    float2 offset = PixelWaterSnapOffset(normalWS.xz * _PixelWaterRefraction * fade);
                    float2 candidateUv = saturate(screenUv + offset);

                    float candidateEyeDepth;
                    float3 candidatePositionWS;
                    float candidateIsBackground;
                    PixelWaterSampleScene(
                        candidateUv,
                        candidateEyeDepth,
                        candidatePositionWS,
                        candidateIsBackground);

                    // Reject anything the offset dragged in from in front of the surface, which is
                    // what makes objects bleed over the waterline in naive refraction.
                    if (candidateIsBackground > 0.5 || candidateEyeDepth >= surfaceEyeDepth)
                        refractedUv = candidateUv;
                }

                // The refracted scene is already lit; only what the water adds needs lighting.
                half3 sceneColor = SampleSceneColor(refractedUv);

                // Flat, stylised lighting: the surface is treated as level for the diffuse term, so
                // the colour bands stay clean and only the light level changes over the day.
                Light mainLight = GetMainLight();
                half shadow = lerp(1.0h, PixelPipelineMainLightShadow(input.positionWS), _PixelWaterShadowStrength);
                half3 directLight = mainLight.color * (saturate(mainLight.direction.y) * shadow);
                half3 lighting = directLight + SampleSH(half3(0.0h, 1.0h, 0.0h));

                float depthRamp = PixelWaterQuantize(
                    verticalDepth / max(0.001, _PixelWaterDepthDistance * _DepthScale),
                    _PixelWaterColorSteps);
                half3 bodyColor = lerp(
                    _PixelWaterShallowColor.rgb,
                    _PixelWaterDeepColor.rgb,
                    depthRamp) * _SurfaceTint.rgb;
                float opacity = saturate(
                    lerp(_PixelWaterShallowOpacity, _PixelWaterDeepOpacity, depthRamp));

                half3 color = lerp(sceneColor, bodyColor * lighting, opacity);

                if (_PixelWaterReflectionOn > 0.5
                    && _PixelWaterReflectionStrength * _ReflectionScale > 0.0001)
                {
                    float2 reflectionUv = saturate(
                        screenUv + PixelWaterSnapOffset(normalWS.xz * _PixelWaterReflectionDistortion));
                    half3 reflectionColor = SAMPLE_TEXTURE2D(
                        _PixelWaterReflection,
                        sampler_PixelWaterReflection,
                        reflectionUv).rgb;

                    float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    float fresnel = pow(
                        saturate(1.0 - saturate(dot(normalWS, viewDirectionWS))),
                        _PixelWaterReflectionFresnel);
                    float reflectionAmount = saturate(
                        _PixelWaterReflectionStrength
                        * _ReflectionScale
                        * lerp(fresnel, 1.0, _PixelWaterReflectionFlatness));
                    color = lerp(color, reflectionColor, reflectionAmount);
                }

                color = lerp(
                    color,
                    _PixelWaterPatternColor.rgb * lighting,
                    patternMask * _PixelWaterPatternColor.a);

                float shoreMask = PixelWaterShoreFoam(
                    input.positionWS,
                    PixelWaterShoreDistance(input.positionWS, shoreDistance));
                color = lerp(
                    color,
                    _PixelWaterShoreColor.rgb * lighting,
                    shoreMask * _PixelWaterShoreColor.a);

                // Sparkles reflect the light itself, so they take its colour and vanish in shadow; the
                // moon gives faint, cool ones at night.
                float glint = PixelWaterGlints(
                    input.positionWS,
                    normalWS,
                    GetWorldSpaceNormalizeViewDir(input.positionWS),
                    mainLight.direction,
                    worldPerPixel);
                color += glint * _PixelWaterGlintStrength * mainLight.color * shadow * (1.0 - shoreMask);

                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // Not used by the camera, whose depth prepass only draws opaque queues. The rain occlusion map
        // draws every queue, and this is what lets drops end on the water surface instead of on the
        // pond bed below it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterDepthVertex
            #pragma fragment WaterDepthFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.schyro.pixel-pipeline/Shaders/Water/PixelWaterCommon.hlsl"

            struct WaterDepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct WaterDepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            WaterDepthVaryings WaterDepthVertex(WaterDepthAttributes input)
            {
                WaterDepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float waveHeight;
                float3 waveNormal;
                PixelWaterSampleWaves(positionWS.xz, waveHeight, waveNormal);
                output.positionHCS = TransformWorldToHClip(positionWS + float3(0.0, waveHeight, 0.0));
                return output;
            }

            half WaterDepthFragment(WaterDepthVaryings input) : SV_Target
            {
                return input.positionHCS.z;
            }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
