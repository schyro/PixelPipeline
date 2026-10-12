// Banded light for the deferred paths, computed once per screen pixel from the G-buffer. Every
// material that writes a G-buffer (URP Lit, Simple Lit, Shader Graph Lit) is lit this way with no
// change to the material: Lambert diffuse snapped to flat bands, pixel shadows, and the ambient
// snapped to three zones. Point and spot lights are banded over their own reach, so their pools
// end in hard rings at the light's Range and are as bright as its Intensity.
//
// Runs between the G-buffer pass and URP's deferred lighting. The camera colour already holds each
// pixel's emission, reflections, and smooth ambient at that point, so this adds the banded lights
// and the difference between banded and smooth ambient on top. It then clears the pixel's material
// type in the stencil buffer, which makes URP's own smooth lighting skip it.
//
// Diffuse only, like the look it is for: no specular highlights. Baked lightmaps are not accounted
// for in the ambient difference.
Shader "Hidden/PixelPipeline/DeferredLighting"
{
    Properties
    {
        // URP's StencilUsage: 32 marks a Lit pixel, 64 a Simple Lit one.
        _PixelStencilRef ("Stencil Ref", Int) = 32
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #pragma target 3.5
        #pragma vertex Vert
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
        #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
        #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
        #pragma multi_compile_local_fragment _ _PIXEL_SIMPLE_LIT

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferInput.hlsl"
        #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl"
        #include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelLighting.hlsl"

        // Lights other than the main one that reach this camera. Deferred has no list of them per
        // pixel, so without the Deferred+ clusters every one of them is tried.
        float _PixelAdditionalLightCount;

        // lightIndex counts the visible additional lights, not the lights of one object.
        half3 PixelDeferredAdditionalLight(int lightIndex, float3 positionWS, half3 normalWS)
        {
            Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
            #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                half4 occlusionProbeChannels = _AdditionalLightsBuffer[lightIndex].occlusionProbeChannels;
            #else
                half4 occlusionProbeChannels = _AdditionalLightsOcclusionProbes[lightIndex];
            #endif
            half shadow = AdditionalLightShadow(lightIndex, positionWS, light.direction, half4(1, 1, 1, 1), occlusionProbeChannels);

            half amount = saturate(dot(normalWS, light.direction)) * PixelPipelineLightFalloff(lightIndex, positionWS);
            return light.color * (PixelPipelineBandLight(amount) * PixelPipelineQuantizeShadow(shadow));
        }

        // What has to be added to the camera colour; negative where the banded ambient is darker
        // than the smooth ambient already there and no direct light makes up for it.
        half3 PixelDeferredLightDelta(Varyings input)
        {
            GBufferData gBuffer = UnpackGBuffers(uint2(input.positionCS.xy));

            float deviceDepth = gBuffer.depth;
            #if !UNITY_REVERSED_Z
                deviceDepth = deviceDepth * 2.0 - 1.0;
            #endif
            float3 positionWS = ComputeWorldSpacePosition(input.texcoord.xy, deviceDepth, unity_MatrixInvVP);

            #if defined(_PIXEL_SIMPLE_LIT)
                half3 diffuse = gBuffer.baseColor;
            #else
                half3 diffuse = GBufferDataToBRDFData(gBuffer).diffuse;
            #endif

            half3 normalWS = gBuffer.normalWS;
            Light mainLight = GetMainLight();
            half shadow = (gBuffer.materialFlags & kMaterialFlagReceiveShadowsOff) != 0
                ? 1.0h
                : PixelPipelineMainLightShadow(positionWS);

            half3 direct = mainLight.color * (PixelPipelineBandLight(dot(normalWS, mainLight.direction)) * shadow);

            #if USE_CLUSTER_LIGHT_LOOP
                // The cluster light loop reads these two fields by name.
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = input.texcoord.xy;

                // Directional lights besides the main one (the moon by day, the sun by night) sit
                // ahead of the clustered lights.
                [loop] for (uint directionalIndex = 0; directionalIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); directionalIndex++)
                    direct += PixelDeferredAdditionalLight(directionalIndex, positionWS, normalWS);

                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    direct += PixelDeferredAdditionalLight(lightIndex, positionWS, normalWS);
                LIGHT_LOOP_END
            #else
                uint lightCount = min((uint)_PixelAdditionalLightCount, MAX_VISIBLE_LIGHTS);
                [loop] for (uint lightIndex = 0; lightIndex < lightCount; lightIndex++)
                    direct += PixelDeferredAdditionalLight(lightIndex, positionWS, normalWS);
            #endif
            half3 ambient = (PixelPipelineAmbient(normalWS) - SampleSH(normalWS)) * gBuffer.occlusion;
            return diffuse * (direct + ambient);
        }
        ENDHLSL

        // The camera colour cannot hold a negative value on the way, so the positive part is added
        // first and the negative part subtracted after it.
        Pass
        {
            Name "PixelDeferredAdd"

            Blend One One, Zero One
            Stencil
            {
                Ref [_PixelStencilRef]
                ReadMask 96
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(max(PixelDeferredLightDelta(input), 0.0h), 0.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PixelDeferredSubtract"

            BlendOp RevSub, Add
            Blend One One, Zero One
            // Back to the Unlit material type, which URP's deferred lights do not touch.
            Stencil
            {
                Ref [_PixelStencilRef]
                ReadMask 96
                WriteMask 96
                Comp Equal
                Pass Zero
            }

            HLSLPROGRAM
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(max(-PixelDeferredLightDelta(input), 0.0h), 0.0h);
            }
            ENDHLSL
        }
    }
}
