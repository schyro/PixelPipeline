#ifndef PIXEL_PIPELINE_RAIN_OCCLUSION_INCLUDED
#define PIXEL_PIPELINE_RAIN_OCCLUSION_INCLUDED

// A depth map rendered along the falling direction of the rain, exactly like a shadow map for a
// light that shines the way the drops travel. Anything a drop would hit - ground, roofs, trees,
// water - is in it, without needing a collider, and each drop lands on the surface it stores.

TEXTURE2D(_PixelRainOcclusionTexture);
float4x4 _PixelRainOcclusionWorldToMap;
float4x4 _PixelRainOcclusionMapToWorld;
// x: 1 when the map is valid for the camera being rendered, y: unused, z: texel size, w: unused.
float4 _PixelRainOcclusionParams;

bool PixelRainOcclusionAvailable()
{
    return _PixelRainOcclusionParams.x > 0.5;
}

float3 PixelRainOcclusionMapPosition(float3 positionWS)
{
    return mul(_PixelRainOcclusionWorldToMap, float4(positionWS, 1.0)).xyz;
}

bool PixelRainOcclusionInsideMap(float2 uv)
{
    return all(uv > 0.0) && all(uv < 1.0);
}

float PixelRainOcclusionSampleDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_PixelRainOcclusionTexture, sampler_PointClamp, uv, 0).r;
}

// True when nothing was rendered into this texel, so rain passes straight through it.
bool PixelRainOcclusionIsEmpty(float storedDepth)
{
    #if UNITY_REVERSED_Z
        return storedDepth <= 0.00001;
    #else
        return storedDepth >= 0.99999;
    #endif
}

float3 PixelRainOcclusionSurfacePosition(float2 uv, float storedDepth)
{
    float4 positionWS = mul(_PixelRainOcclusionMapToWorld, float4(uv, storedDepth, 1.0));
    return positionWS.xyz / positionWS.w;
}

#endif
