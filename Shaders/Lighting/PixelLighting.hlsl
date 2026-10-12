#ifndef PIXEL_PIPELINE_LIGHTING_INCLUDED
#define PIXEL_PIPELINE_LIGHTING_INCLUDED

// Direct light snapped to a few flat bands. A smooth N·L ramp across a curved surface comes out of
// the point downsample as a noisy gradient; banded, it turns from lit to unlit in clean steps whose
// edges are one pixel wide, like the shadow edges from PixelShadows.hlsl.

float _PixelLightBands;
float _PixelLightBandBias;

half PixelPipelineBandLight(half amount)
{
    amount = saturate(amount);
    // Zero bands means the profile switched banding off; keep the smooth ramp.
    if (_PixelLightBands < 0.5)
        return amount;

    half bands = round(_PixelLightBands);
    return saturate(floor(amount * bands + 0.5h + _PixelLightBandBias) / bands);
}

// How much of an additional light reaches a point, as a fraction of the light's own reach: one at
// the light, falling to zero at its Range, times the spot cone. One everywhere for a directional
// light. URP's own attenuation is inverse-square, far above one beside the light and a sliver of it
// across most of the range, so banding it leaves a small blown-out disc whatever the light is set
// to. Banded on this instead, Range is where the outer ring sits and Intensity is how bright the
// pool is, each on its own.
//
// visibleLightIndex counts the visible additional lights, not the lights of one object.
// Requires Lighting.hlsl to be included first.
half PixelPipelineLightFalloff(int visibleLightIndex, float3 positionWS)
{
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 lightPositionWS = _AdditionalLightsBuffer[visibleLightIndex].position;
        half4 attenuation = _AdditionalLightsBuffer[visibleLightIndex].attenuation;
        half4 spotDirection = _AdditionalLightsBuffer[visibleLightIndex].spotDirection;
    #else
        float4 lightPositionWS = _AdditionalLightsPosition[visibleLightIndex];
        half4 attenuation = _AdditionalLightsAttenuation[visibleLightIndex];
        half4 spotDirection = _AdditionalLightsSpotDir[visibleLightIndex];
    #endif

    // A directional light stores its direction here with a zero w, and a zero range term below.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));

    // attenuation.x is one over the squared range.
    half reach = saturate(1.0h - half(distanceSqr * attenuation.x));
    return reach * reach * AngleAttenuation(spotDirection.xyz, lightDirection, attenuation.zw);
}

// Ambient light from the probes, which fades from ground colour to sky colour across a curved
// surface. With banding on, the normal is snapped to facing the ground, the horizon, or the sky, so
// the fade becomes three flat zones. Floors, walls, and ceilings keep exactly their own ambient, and
// the few zones do not multiply with the direct bands into a stack of rings.
// Requires Lighting.hlsl (for SampleSH) to be included first.
half3 PixelPipelineAmbient(half3 normalWS)
{
    if (_PixelLightBands < 0.5)
        return SampleSH(normalWS);

    half height = round(normalWS.y);
    half2 horizontal = height == 0.0h ? normalWS.xz / max(length(normalWS.xz), 1e-4h) : half2(0.0h, 0.0h);
    return SampleSH(half3(horizontal.x, height, horizontal.y));
}

#endif
