#ifndef PIXEL_PIPELINE_GRASS_BLADE_COMMON_INCLUDED
#define PIXEL_PIPELINE_GRASS_BLADE_COMMON_INCLUDED

// Shared by every pass of PixelPipeline/GrassBlade, so the SRP Batcher sees one UnityPerMaterial
// layout and the colour, depth, and shadow passes all bend and billboard a blade identically.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_GrassTexture);
SAMPLER(sampler_GrassTexture);
TEXTURE2D(_Noise2);
SAMPLER(sampler_Noise2);
TEXTURE2D(_Noise3);
SAMPLER(sampler_Noise3);
TEXTURE2D(_AccentTexture1);
SAMPLER(sampler_AccentTexture1);
TEXTURE2D(_AccentTexture2);
SAMPLER(sampler_AccentTexture2);
TEXTURE2D(_WindNoise);
SAMPLER(sampler_WindNoise);

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    float _Cutoff;

    half4 _Color2;
    float _Noise2Scale;
    float _Noise2Threshold;

    half4 _Color3;
    float _Noise3Scale;
    float _Noise3Threshold;

    half4 _AccentColor1;
    float _AccentFrequency1;
    float _AccentHeight1;
    float _AccentScale1;

    half4 _AccentColor2;
    float _AccentFrequency2;
    float _AccentHeight2;
    float _AccentScale2;

    int _Cuts;
    float _Steepness;
    float _Wrap;
    float _ThresholdGradientSize;

    float _WindNoiseScale;
    float _WindNoiseSpeed;
    float _WindNoiseThreshold;
    float _WindSwayAngle;
    float _NoiseDivergeAngle;

    float _FakePerspectiveScale;

    float _UseDither;
    float _DitherStrength;
CBUFFER_END

float4 _PixelWindDirection;
float _PixelWindStrength;

UNITY_INSTANCING_BUFFER_START(InstancedProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainNormal)
UNITY_INSTANCING_BUFFER_END(InstancedProps)

struct GrassBladeAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GrassBlade
{
    float3 instancePositionWS;
    float3 positionWS;
    // x picks accent 1, y accent 2; both are per blade.
    float2 seeds;
    float windNoiseSample;
};

float GrassBladeHash(float2 position)
{
    return frac(sin(dot(position, float2(12.9898, 78.233))) * 43758.5453);
}

float2 GrassBladeRotate(float2 value, float angleDegrees)
{
    float angle = radians(angleDegrees);
    float cosine = cos(angle);
    float sine = sin(angle);
    return float2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
}

// Camera-facing blade, rotated about its root by the shared wind noise and leaning downwind.
// Call after UNITY_SETUP_INSTANCE_ID.
GrassBlade GrassBladeExpand(GrassBladeAttributes input)
{
    GrassBlade blade;

    float3 instancePositionWS = UNITY_MATRIX_M._m03_m13_m23;
    float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    float seed1 = GrassBladeHash(instancePositionWS.xz);
    float seed2 = GrassBladeHash(instancePositionWS.xz + float2(3.14159, 3.14159));

    float3 positionOS = input.positionOS.xyz;
    if (seed1 < _AccentFrequency1)
    {
        positionOS.y += _AccentHeight1;
        positionOS *= _AccentScale1;
    }
    else if (seed2 < _AccentFrequency2)
    {
        positionOS.y += _AccentHeight2;
        positionOS *= _AccentScale2;
    }

    float2 windDirection1 = GrassBladeRotate(_PixelWindDirection.xz, _NoiseDivergeAngle);
    float2 windDirection2 = GrassBladeRotate(_PixelWindDirection.xz, -_NoiseDivergeAngle);

    float time = _Time.y;
    float2 windTimeDirection1 = -time * _WindNoiseSpeed * _PixelWindStrength * normalize(windDirection1);
    float2 windTimeDirection2 = -time * _WindNoiseSpeed * _PixelWindStrength * normalize(windDirection2);

    float2 windUv1 = instancePositionWS.xz * _WindNoiseScale + windTimeDirection1;
    float2 windUv2 = instancePositionWS.xz * (_WindNoiseScale * 0.8) + (windTimeDirection2 * 0.89 + float2(1.047, 1.047));
    float windSample1 = SAMPLE_TEXTURE2D_LOD(_WindNoise, sampler_WindNoise, windUv1, 0).r;
    float windSample2 = SAMPLE_TEXTURE2D_LOD(_WindNoise, sampler_WindNoise, windUv2, 0).r;

    float windSample = windSample1 * windSample2;
    windSample = saturate(windSample + _WindNoiseThreshold);
    windSample = (windSample - 0.5) * 2.0;

    float windAngle = windSample * radians(_WindSwayAngle) * _PixelWindStrength * 0.5;
    float windSine = sin(windAngle);
    float windCosine = cos(windAngle);

    float rotatedX = positionOS.x * windCosine - positionOS.y * windSine;
    float rotatedY = positionOS.x * windSine + positionOS.y * windCosine;
    positionOS.x = rotatedX;
    positionOS.y = rotatedY;

    float3 cameraUp = UNITY_MATRIX_V[1].xyz;
    float3 cameraRight = UNITY_MATRIX_V[0].xyz;

    float3 positionWS = instancePositionWS
                      + cameraRight * positionOS.x * scaleX
                      + cameraUp * positionOS.y * scaleY;
    float3 windDirectionWS = normalize(float3(_PixelWindDirection.x, 0.0, _PixelWindDirection.z));
    positionWS += windDirectionWS * (windSine * scaleY * saturate(input.uv.y) * 0.35);

    blade.instancePositionWS = instancePositionWS;
    blade.positionWS = positionWS;
    blade.seeds = float2(seed1, seed2);
    blade.windNoiseSample = windSample;
    return blade;
}

// Squeezes the sprite towards its tip along the wind, which reads as the blade bending towards or
// away from the camera.
float2 GrassBladeSpriteUV(float2 uv, float windNoiseSample)
{
    uv.x -= 0.5;
    float perspectiveAmount = windNoiseSample * _FakePerspectiveScale;
    float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
    perspectiveAmount *= _PixelWindStrength * dot(cameraForward.xz, normalize(_PixelWindDirection.xz));
    uv.x *= uv.y * perspectiveAmount + 1.0;
    uv.x += 0.5;
    uv.x = clamp(uv.x, 0.0, 1.0);
    return uv;
}

half4 GrassBladeSampleSprite(float2 seeds, float2 uv)
{
    half4 sprite;
    if (seeds.x < _AccentFrequency1)
        sprite = SAMPLE_TEXTURE2D(_AccentTexture1, sampler_AccentTexture1, uv);
    else if (seeds.y < _AccentFrequency2)
        sprite = SAMPLE_TEXTURE2D(_AccentTexture2, sampler_AccentTexture2, uv);
    else
        sprite = SAMPLE_TEXTURE2D(_GrassTexture, sampler_GrassTexture, uv);
    return sprite;
}

#endif
