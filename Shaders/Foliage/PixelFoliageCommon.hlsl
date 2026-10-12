#ifndef PIXEL_PIPELINE_FOLIAGE_COMMON_INCLUDED
#define PIXEL_PIPELINE_FOLIAGE_COMMON_INCLUDED

// Shared by every pass of PixelPipeline/Foliage, so the SRP Batcher sees one UnityPerMaterial layout
// and the shadow, depth, and colour passes all expand the leaf cards identically.
//
// Mesh layout, as written by PixelFoliageCanopy:
//   POSITION   card centre (object space; all four corners share it)
//   NORMAL     lighting normal, rounded over the whole canopy rather than per card
//   TEXCOORD0  sprite UV
//   TEXCOORD1  xy: corner offset in metres, z: per-card random 0-1, w: wind flexibility 0-1
//   COLOR      r: canopy occlusion, 0 deep inside to 1 on the outer shell

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_LeafTexture);
SAMPLER(sampler_LeafTexture);
TEXTURE2D(_Noise2);
SAMPLER(sampler_Noise2);
TEXTURE2D(_Noise3);
SAMPLER(sampler_Noise3);
TEXTURE2D(_WindNoise);
SAMPLER(sampler_WindNoise);

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half4 _Color2;
    half4 _Color3;
    float _Noise2Scale;
    float _Noise2Threshold;
    float _Noise3Scale;
    float _Noise3Threshold;
    half _Cutoff;
    half _TextureDetail;
    half _ColorVariation;
    half _InnerDarkness;
    half _Cuts;
    half _Wrap;
    half _Steepness;
    half _ShadowSampleOffset;
    float _CardSizeScale;
    float _CardRotationJitter;
    float _WindNoiseScale;
    float _WindNoiseSpeed;
    float _WindSway;
    float _FlutterStrength;
    float _FlutterSpeed;
    half _OutlineExclude;
CBUFFER_END

float4 _PixelWindDirection;
float _PixelWindStrength;

struct FoliageAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float4 cardData : TEXCOORD1;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FoliageVertex
{
    float3 positionWS;
    float3 centreWS;
    float3 normalWS;
    float2 uv;
    float seed;
    half occlusion;
};

float2 PixelFoliageWindDirection()
{
    float2 direction = _PixelWindDirection.xz;
    float directionLength = length(direction);
    return directionLength > 0.0001 ? direction / directionLength : float2(1.0, 0.0);
}

// View-facing card expansion. In the shadow caster the "view" is the light, so every card faces the
// light there, which gives the canopy a solid shadow instead of a scatter of edge-on slivers.
FoliageVertex PixelFoliageExpand(FoliageAttributes input)
{
    FoliageVertex output;

    float3 centreWS = TransformObjectToWorld(input.positionOS.xyz);
    float objectScale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float seed = input.cardData.z;
    float flexibility = input.cardData.w;

    // Large, slow sway from the shared wind noise, so neighbouring trees lean together, plus a
    // small per-card flutter so the canopy never moves as one rigid block.
    float2 windDirection = PixelFoliageWindDirection();
    float2 windUv = centreWS.xz * _WindNoiseScale - windDirection * (_Time.y * _WindNoiseSpeed * _PixelWindStrength);
    float windNoise = SAMPLE_TEXTURE2D_LOD(_WindNoise, sampler_WindNoise, windUv, 0).r;
    float sway = (windNoise * 1.6 - 0.3) * _WindSway * _PixelWindStrength * flexibility;
    float flutterPhase = _Time.y * _FlutterSpeed * (0.75 + seed * 0.5) + seed * TWO_PI;
    float flutter = sin(flutterPhase) * _FlutterStrength * _PixelWindStrength * flexibility;

    centreWS += float3(windDirection.x, 0.0, windDirection.y) * sway;
    centreWS.y += flutter * 0.5;

    float angle = (seed - 0.5) * radians(_CardRotationJitter) + flutter * 2.0;
    float sine = sin(angle);
    float cosine = cos(angle);
    float2 corner = input.cardData.xy * (_CardSizeScale * objectScale);
    corner = float2(corner.x * cosine - corner.y * sine, corner.x * sine + corner.y * cosine);

    float3 cameraRight = UNITY_MATRIX_V[0].xyz;
    float3 cameraUp = UNITY_MATRIX_V[1].xyz;

    output.centreWS = centreWS;
    output.positionWS = centreWS + cameraRight * corner.x + cameraUp * corner.y;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = input.uv;
    output.seed = seed;
    output.occlusion = input.color.r;
    return output;
}

half PixelFoliageAlpha(float2 uv)
{
    return SAMPLE_TEXTURE2D(_LeafTexture, sampler_LeafTexture, uv).a;
}

#endif
