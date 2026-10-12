#ifndef PIXEL_PIPELINE_ADDITIONAL_LIGHTS_INCLUDED
#define PIXEL_PIPELINE_ADDITIONAL_LIGHTS_INCLUDED

// Point, spot, and extra directional lights for the shaders that light themselves: grass, foliage,
// and the grass terrain. They are banded the way the deferred lighting bands them for G-buffer
// pixels, so a pool of light ends in the same hard rings on a lawn as on the stone beside it.
//
// Requires Lighting.hlsl and PixelShadows.hlsl to be included first, and these variants:
//   #pragma multi_compile _ _ADDITIONAL_LIGHTS
//   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
//   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

#include "Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelLighting.hlsl"

// wrap lets light reach around the surface, as the callers' main light does. bands is the number of
// flat brightness steps; zero keeps the smooth ramp. visibleLightIndex is the light's index among
// the visible additional lights.
half3 PixelPipelineAdditionalLight(Light light, int visibleLightIndex, float3 positionWS, half3 normalWS, half wrap, half bands)
{
    half facing = saturate((dot(normalWS, light.direction) + wrap) / (1.0h + wrap));
    half amount = facing * PixelPipelineLightFalloff(visibleLightIndex, positionWS);
    if (bands > 0.5h)
        amount = saturate(floor(amount * round(bands) + 0.5h) / round(bands));

    return light.color * (amount * PixelPipelineQuantizeShadow(light.shadowAttenuation));
}

// positionWS is the point being lit, which for a billboard is its root or centre rather than the
// fragment, so the whole card takes one light value. normalizedScreenSpaceUV is the fragment's own;
// the Forward+ and Deferred+ clusters are looked up with it.
half3 PixelPipelineAdditionalLights(float3 positionWS, float2 normalizedScreenSpaceUV, half3 normalWS, half wrap, half bands)
{
    half3 lighting = half3(0.0h, 0.0h, 0.0h);

    #if defined(_ADDITIONAL_LIGHTS)
        uint lightCount = GetAdditionalLightsCount();

        #if USE_CLUSTER_LIGHT_LOOP
            // The cluster light loop reads these two fields by name.
            InputData inputData = (InputData)0;
            inputData.positionWS = positionWS;
            inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;

            // Directional lights besides the main one (the moon by day, the sun by night) sit
            // ahead of the clustered lights. Both loops count the visible lights.
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                lighting += PixelPipelineAdditionalLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), lightIndex, positionWS, normalWS, wrap, bands);

            LIGHT_LOOP_BEGIN(lightCount)
                lighting += PixelPipelineAdditionalLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), lightIndex, positionWS, normalWS, wrap, bands);
            LIGHT_LOOP_END
        #else
            // This loop counts the lights of one object.
            LIGHT_LOOP_BEGIN(lightCount)
                lighting += PixelPipelineAdditionalLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), GetPerObjectLightIndex(lightIndex), positionWS, normalWS, wrap, bands);
            LIGHT_LOOP_END
        #endif
    #endif

    return lighting;
}

#endif
