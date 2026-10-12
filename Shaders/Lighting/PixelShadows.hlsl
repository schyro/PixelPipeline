#ifndef PIXEL_PIPELINE_SHADOWS_INCLUDED
#define PIXEL_PIPELINE_SHADOWS_INCLUDED

// Main-light shadow sampling for the pixel look. URP's hard shadows are still one bilinear PCF tap,
// and one shadow-map texel covers several internal pixels at typical zoom levels, so every shadow
// edge used to fade across a few pixels. Here the filtered value is snapped to a small number of
// bands, which keeps the edge exactly one pixel wide after the point downsample.
//
// Snapping a single tap still leaves the edge on the shadow-map texel grid: a staircase of texel-
// sized teeth, turned against the screen by the light direction. Snapping a wide tent-filtered
// value instead puts the edge where the true outline crosses the middle of the filter, so it runs
// as a straight line, which the point downsample then draws as a clean pixel line.
//
// Requires Shadows.hlsl (Lighting.hlsl or Core.hlsl + Shadows.hlsl) to be included first.

float _PixelShadowSteps;
float _PixelShadowEdgeBias;
float _PixelShadowCascadeCount;
// 0: one tap, 1: 3x3 tent, 2: 5x5 tent, 3: 7x7 tent.
float _PixelShadowFilter;
// Shadow atlas texel size (xy) and resolution (zw); URP only uploads its own copy with soft shadows.
float4 _PixelShadowmapSize;

half PixelPipelineQuantizeShadow(half attenuation)
{
    // Zero steps means the profile switched the effect off; keep URP's filtered value.
    if (_PixelShadowSteps < 0.5)
        return attenuation;

    half steps = max(1.0h, round(_PixelShadowSteps));
    return saturate(floor(attenuation * steps + 0.5h + _PixelShadowEdgeBias) / steps);
}

half PixelPipelineShadowCascadeIndex(float3 positionWS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
        return ComputeCascadeIndex(positionWS);
    #else
        // The screen-space variant cannot tell from its keywords whether cascades are in use, and
        // URP only uploads the split spheres when they are, so the count comes from the pixel pass.
        return _PixelShadowCascadeCount > 1.5 ? ComputeCascadeIndex(positionWS) : half(0.0);
    #endif
}

#define PIXEL_SHADOW_TAP(uv) SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3(uv, shadowCoord.z))

half PixelPipelineFilterMainLightShadow(float4 shadowCoord)
{
    // Filtering needs the atlas size, which the pixel shadow pass provides; until it has run, and
    // whenever pixel shadows are off, this is URP's own single tap.
    if (_PixelShadowFilter < 0.5 || _PixelShadowmapSize.z < 1.0)
        return PIXEL_SHADOW_TAP(shadowCoord.xy);

    half attenuation = 0.0h;
    if (_PixelShadowFilter < 1.5)
    {
        real weights[4];
        real2 uvs[4];
        SampleShadow_ComputeSamples_Tent_3x3(_PixelShadowmapSize, shadowCoord.xy, weights, uvs);
        [unroll] for (int i = 0; i < 4; i++)
            attenuation += weights[i] * PIXEL_SHADOW_TAP(uvs[i]);
    }
    else if (_PixelShadowFilter < 2.5)
    {
        real weights[9];
        real2 uvs[9];
        SampleShadow_ComputeSamples_Tent_5x5(_PixelShadowmapSize, shadowCoord.xy, weights, uvs);
        [unroll] for (int i = 0; i < 9; i++)
            attenuation += weights[i] * PIXEL_SHADOW_TAP(uvs[i]);
    }
    else
    {
        real weights[16];
        real2 uvs[16];
        SampleShadow_ComputeSamples_Tent_7x7(_PixelShadowmapSize, shadowCoord.xy, weights, uvs);
        [unroll] for (int i = 0; i < 16; i++)
            attenuation += weights[i] * PIXEL_SHADOW_TAP(uvs[i]);
    }

    return attenuation;
}

#undef PIXEL_SHADOW_TAP

// Realtime shadow with the light's shadow strength applied, but without the distance fade. This is
// what URP stores in the screen-space shadow texture; Lit applies the fade itself afterwards.
half PixelPipelineSampleMainLightShadow(float3 positionWS)
{
    half cascadeIndex = PixelPipelineShadowCascadeIndex(positionWS);
    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));
    if (BEYOND_SHADOW_FAR(shadowCoord))
        return 1.0h;

    half attenuation = PixelPipelineFilterMainLightShadow(shadowCoord);
    attenuation = PixelPipelineQuantizeShadow(attenuation);
    return LerpWhiteTo(attenuation, GetMainLightShadowParams().x);
}

// Complete main-light shadow for custom shaders, including the shadow-distance fade. Compiles to a
// constant when the shader variant has no main-light shadows at all.
half PixelPipelineMainLightShadow(float3 positionWS)
{
    #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        half attenuation = PixelPipelineSampleMainLightShadow(positionWS);
        half fade = GetMainLightShadowFade(positionWS);
        return lerp(attenuation, 1.0h, fade);
    #else
        return 1.0h;
    #endif
}

#endif
