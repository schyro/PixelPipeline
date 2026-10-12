#ifndef PIXEL_PIPELINE_GRASS_DEPTH_PASS_INCLUDED
#define PIXEL_PIPELINE_GRASS_DEPTH_PASS_INCLUDED

#include "Packages/com.schyro.pixel-pipeline/Shaders/Grass/GrassBladeCommon.hlsl"

struct GrassDepthVaryings
{
    float4 positionHCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 seeds : TEXCOORD1;
    float windNoiseSample : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

GrassDepthVaryings GrassDepthVertex(GrassBladeAttributes input)
{
    GrassDepthVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    GrassBlade blade = GrassBladeExpand(input);
    output.positionHCS = TransformWorldToHClip(blade.positionWS);
    output.uv = input.uv;
    output.seeds = blade.seeds;
    output.windNoiseSample = blade.windNoiseSample;
    return output;
}

void GrassDepthClip(GrassDepthVaryings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    float2 uv = GrassBladeSpriteUV(input.uv, input.windNoiseSample);
    clip(GrassBladeSampleSprite(input.seeds, uv).a - _Cutoff);
}

half4 GrassDepthOnlyFragment(GrassDepthVaryings input) : SV_Target
{
    GrassDepthClip(input);
    return 0;
}

half4 GrassDepthNormalsFragment(GrassDepthVaryings input) : SV_Target
{
    GrassDepthClip(input);
    // An impossible unit-normal value used by the outline pass as an opt-out marker.
    // Grass still writes depth, so it occludes outlines belonging to geometry behind it.
    return half4(1.0, 0.0, 1.0, 1.0);
}

#endif
