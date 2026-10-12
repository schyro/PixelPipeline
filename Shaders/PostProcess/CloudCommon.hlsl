#ifndef PIXEL_PIPELINE_CLOUD_COMMON_INCLUDED
#define PIXEL_PIPELINE_CLOUD_COMMON_INCLUDED

TEXTURE2D(_CloudNoise);
SAMPLER(sampler_CloudNoise);
float _CloudScale;
float _CloudWorldY;
float _CloudSpeed;
float _CloudTime;
float _CloudContrast;
float _CloudThreshold;
float _CloudCoverage;
float _CloudCoverageInfluence;
float4 _CloudDirection;
float4 _CloudLightDirection;
float _CloudShadowMin;
float _CloudShadowStrength;
float _CloudDivergeAngle;
float _CloudPower;
float _CloudShadowSteps;

float2 PixelPipelineRotate2D(float2 value, float angleDegrees)
{
    float angle = radians(angleDegrees);
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    return float2(
        value.x * cosine - value.y * sine,
        value.x * sine + value.y * cosine);
}

// Where a world position's cloud shadow is read: projected along the cloud light direction onto the
// cloud layer, then into the two noise layers, which drift apart along the wind.
bool PixelPipelineCloudUVs(float3 worldPosition, out float2 firstUV, out float2 secondUV)
{
    firstUV = 0.0;
    secondUV = 0.0;
    if (_CloudScale <= 0.0001 || abs(_CloudLightDirection.y) < 0.05)
        return false;

    float projectionDistance = (_CloudWorldY - worldPosition.y) / _CloudLightDirection.y;
    float3 projectedPosition = worldPosition + projectionDistance * _CloudLightDirection.xyz;
    float2 direction = length(_CloudDirection.xy) > 0.0001
        ? normalize(_CloudDirection.xy)
        : float2(1.0, 0.0);
    float2 firstDirection = PixelPipelineRotate2D(direction, _CloudDivergeAngle);
    float2 secondDirection = PixelPipelineRotate2D(direction, -_CloudDivergeAngle);
    // Sampling coordinates move opposite to the visible pattern. Negating the offset makes the
    // cloud shadow travel toward the direction selected in the shared Wind settings.
    float2 firstOffset = -_CloudTime * _CloudSpeed * firstDirection;
    float2 secondOffset = -_CloudTime * _CloudSpeed * secondDirection;
    float inverseScale = rcp(_CloudScale);

    firstUV = projectedPosition.xz * inverseScale + firstOffset;
    secondUV = projectedPosition.xz * (inverseScale * 0.8) + secondOffset * 0.89 * 1.047;
    return true;
}

// Light getting through the cloud layer, from 0 under solid cloud to 1 in the open, before the
// shadow strength and banding are applied.
float PixelPipelineCloudShape(float combinedNoise)
{
    // Coverage changes the reference project's threshold rather than replacing its noise shape.
    // At 0.5 coverage the original look is preserved; weather can then clear or fill the pattern.
    float effectiveThreshold = _CloudThreshold + (0.5 - saturate(_CloudCoverage)) * _CloudCoverageInfluence;
    float transmission = saturate(combinedNoise + effectiveThreshold);
    transmission = (transmission - 0.5) * _CloudContrast + 0.5;
    transmission = clamp(transmission + effectiveThreshold, _CloudShadowMin, 1.0);
    return pow(saturate(transmission), _CloudPower);
}

float PixelPipelineCloudTransmission(float3 worldPosition)
{
    float2 firstUV;
    float2 secondUV;
    if (!PixelPipelineCloudUVs(worldPosition, firstUV, secondUV))
        return 1.0;

    float firstNoise = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, firstUV).r;
    float secondNoise = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, secondUV).r;
    float transmission = PixelPipelineCloudShape(firstNoise * secondNoise);
    // Banded rather than smooth, so cloud edges read as a few flat pixel tones.
    if (_CloudShadowSteps > 0.5)
        transmission = ceil(transmission * _CloudShadowSteps - 0.001) / _CloudShadowSteps;
    return lerp(1.0, transmission, _CloudShadowStrength);
}

// The unbanded cloud shape at an explicit mip level, for loops and for a blurred view of the
// clouds around a point: a higher level averages the noise over a wider patch of sky.
float PixelPipelineCloudShapeLod(float3 worldPosition, float lod)
{
    float2 firstUV;
    float2 secondUV;
    if (!PixelPipelineCloudUVs(worldPosition, firstUV, secondUV))
        return 1.0;

    float firstNoise = SAMPLE_TEXTURE2D_LOD(_CloudNoise, sampler_CloudNoise, firstUV, lod).r;
    float secondNoise = SAMPLE_TEXTURE2D_LOD(_CloudNoise, sampler_CloudNoise, secondUV, lod).r;
    return PixelPipelineCloudShape(firstNoise * secondNoise);
}

#endif
