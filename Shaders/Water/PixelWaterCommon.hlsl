#ifndef PIXEL_PIPELINE_WATER_COMMON_INCLUDED
#define PIXEL_PIPELINE_WATER_COMMON_INCLUDED

// The per-surface multipliers, shared by every pass so the SRP Batcher sees one layout.
CBUFFER_START(UnityPerMaterial)
    half4 _SurfaceTint;
    float _DepthScale;
    float _ReflectionScale;
CBUFFER_END

// Every uniform below is uploaded by PixelPipeline.Runtime.PixelWaterSettings.ApplyGlobals(), so one
// PixelPipelineSettings asset drives every water surface. Keep them outside UnityPerMaterial.

float4 _PixelWaterShallowColor;
float4 _PixelWaterDeepColor;
float _PixelWaterDepthDistance;
float _PixelWaterColorSteps;
float _PixelWaterShallowOpacity;
float _PixelWaterDeepOpacity;
float _PixelWaterShadowStrength;

float _PixelWaterRefraction;
float _PixelWaterRefractionFade;

// rgb: colour, a: opacity; zero opacity switches the pattern off.
float4 _PixelWaterPatternColor;
float _PixelWaterPatternScale;
float _PixelWaterPatternWidth;
float _PixelWaterPatternFlowSpeed;
float _PixelWaterPatternMorphSpeed;
float _PixelWaterPatternWarp;
float _PixelWaterPatternCoverage;
float _PixelWaterPatternBreakupScale;

float4 _PixelWaterShoreColor;
float _PixelWaterShoreWidth;
float _PixelWaterShoreNoiseScale;
float _PixelWaterShoreNoiseAmount;
float _PixelWaterShoreLapSpeed;
float _PixelWaterShoreLapAmount;
float _PixelWaterShoreRippleSpacing;
float _PixelWaterShoreRippleSpeed;
float _PixelWaterShoreRippleRange;
float _PixelWaterShoreRippleWidth;

// Per surface, set by PixelWaterShoreline through a property block: horizontal distance to the
// nearest shore in metres. Zero _PixelWaterShoreMapOn falls back to the depth-based estimate.
TEXTURE2D(_PixelWaterShoreMap);
SAMPLER(sampler_PixelWaterShoreMap);
float _PixelWaterShoreMapOn;
float4x4 _PixelWaterWorldToShoreMap;

float _PixelWaterGlintStrength;
float _PixelWaterGlintThreshold;
float _PixelWaterGlintCellSize;
float _PixelWaterGlintDensity;
float _PixelWaterGlintSpeed;

float _PixelWaterWaveHeight;
float _PixelWaterWaveLength;
float _PixelWaterWaveSpeed;
float _PixelWaterWaveChoppiness;
float _PixelWaterWaveNormalStrength;
float4 _PixelWaterWaveDirection;

TEXTURE2D(_PixelWaterReflection);
SAMPLER(sampler_PixelWaterReflection);
float _PixelWaterReflectionOn;
float _PixelWaterReflectionStrength;
float _PixelWaterReflectionFlatness;
float _PixelWaterReflectionFresnel;
float _PixelWaterReflectionDistortion;

// xy = internal pixel resolution, zw = its reciprocal.
float4 _PixelWaterPixelGrid;
float _PixelWaterTime;

float PixelWaterHash(float2 position)
{
    return frac(sin(dot(position, float2(12.9898, 78.233))) * 43758.5453);
}

float2 PixelWaterHash2(float2 position)
{
    float2 projected = float2(
        dot(position, float2(127.1, 311.7)),
        dot(position, float2(269.5, 183.3)));
    return frac(sin(projected) * 43758.5453);
}

float PixelWaterNoise(float2 position)
{
    float2 cell = floor(position);
    float2 offset = frac(position);
    offset = offset * offset * (3.0 - 2.0 * offset);

    float lowerLeft = PixelWaterHash(cell);
    float lowerRight = PixelWaterHash(cell + float2(1.0, 0.0));
    float upperLeft = PixelWaterHash(cell + float2(0.0, 1.0));
    float upperRight = PixelWaterHash(cell + float2(1.0, 1.0));

    return lerp(
        lerp(lowerLeft, lowerRight, offset.x),
        lerp(upperLeft, upperRight, offset.x),
        offset.y);
}

float PixelWaterFbm(float2 position)
{
    return PixelWaterNoise(position) * 0.6
        + PixelWaterNoise(position * 2.13 + 17.3) * 0.3
        + PixelWaterNoise(position * 4.31 + 41.7) * 0.1;
}

// Flattens a 0..1 ramp into a fixed number of bands, the same trick the grass shader uses to keep
// its toon lighting on a palette.
float PixelWaterQuantize(float value, float steps)
{
    float count = max(1.0, steps);
    return saturate(ceil(saturate(value) * count) / count);
}

// Screen space offsets are rounded onto the internal pixel grid, otherwise refraction and the
// reflection wobble at a finer scale than the pixels the player actually sees.
float2 PixelWaterSnapOffset(float2 offset)
{
    float2 resolution = max(1.0, _PixelWaterPixelGrid.xy);
    return floor(offset * resolution + 0.5) / resolution;
}

// Render target pixels per internal pixel. Where the scene is drawn at screen resolution and point
// sampled down afterwards, a line has to be this many pixels wide to come out as one internal
// pixel; drawn at the internal resolution itself, this is one.
float PixelWaterPixelRatio()
{
    return max(1.0, _ScaledScreenParams.y * _PixelWaterPixelGrid.w);
}

// World size of one internal pixel on the camera plane.
float PixelWaterWorldPerPixel(float3 positionWS)
{
    if (unity_OrthoParams.w > 0.5)
        return 2.0 * unity_OrthoParams.y * _PixelWaterPixelGrid.w;

    return length(ddx(positionWS)) * PixelWaterPixelRatio();
}

// A band of the given width in internal pixels around the zero crossing of a distance-like value.
// Measuring the width through the screen derivative keeps it constant whatever the view angle.
float PixelWaterPixelLine(float distanceValue, float widthPixels)
{
    float halfWidth = fwidth(distanceValue) * PixelWaterPixelRatio() * widthPixels * 0.5;
    return step(distanceValue, halfWidth);
}

float2 PixelWaterPrimaryWaveDirection()
{
    float length2 = dot(_PixelWaterWaveDirection.xy, _PixelWaterWaveDirection.xy);
    return length2 > 0.0001 ? _PixelWaterWaveDirection.xy * rsqrt(length2) : float2(1.0, 0.0);
}

// Two crossing directional waves. Vertical displacement only, so world XZ stays stable and every
// pattern below can keep using it as its coordinate frame.
void PixelWaterSampleWaves(float2 positionXZ, out float height, out float3 normalWS)
{
    float2 primaryDirection = PixelWaterPrimaryWaveDirection();
    float2 secondaryDirection = normalize(
        primaryDirection + float2(-primaryDirection.y, primaryDirection.x) * 1.6);

    float primaryFrequency = TWO_PI / max(0.01, _PixelWaterWaveLength);
    float secondaryFrequency = primaryFrequency * 1.73;
    float time = _PixelWaterTime * _PixelWaterWaveSpeed;

    float primaryPhase = (dot(positionXZ, primaryDirection) - time) * primaryFrequency;
    float secondaryPhase = (dot(positionXZ, secondaryDirection) - time * 0.8) * secondaryFrequency;

    float primaryAmplitude = _PixelWaterWaveHeight;
    float secondaryAmplitude = _PixelWaterWaveHeight * _PixelWaterWaveChoppiness;

    height = sin(primaryPhase) * primaryAmplitude + sin(secondaryPhase) * secondaryAmplitude;

    float2 slope =
        primaryDirection * (cos(primaryPhase) * primaryAmplitude * primaryFrequency) +
        secondaryDirection * (cos(secondaryPhase) * secondaryAmplitude * secondaryFrequency);
    slope *= _PixelWaterWaveNormalStrength;

    normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
}

void PixelWaterSampleScene(float2 uv, out float eyeDepth, out float3 positionWS, out float isBackground)
{
    float rawDepth = SampleSceneDepth(uv);

    #if UNITY_REVERSED_Z
        isBackground = rawDepth <= 1e-6 ? 1.0 : 0.0;
        float deviceDepth = rawDepth;
    #else
        isBackground = rawDepth >= 1.0 - 1e-6 ? 1.0 : 0.0;
        float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
    #endif

    eyeDepth = unity_OrthoParams.w > 0.5
        ? LinearDepthToEyeDepth(rawDepth)
        : LinearEyeDepth(rawDepth, _ZBufferParams);
    positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
}

// Distance to the nearest cell border of a Voronoi pattern, approximated by F2 - F1. The feature
// points circle inside their cells, so the network slowly reshapes as well as drifting.
float PixelWaterVoronoiEdge(float2 position, float morphPhase)
{
    float2 cell = floor(position);
    float2 local = frac(position);
    float nearest = 8.0;
    float secondNearest = 8.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbour = float2(x, y);
            float2 random = PixelWaterHash2(cell + neighbour);
            float2 feature = neighbour + 0.5 + 0.35 * sin(morphPhase + TWO_PI * random) - local;
            float distanceSquared = dot(feature, feature);
            if (distanceSquared < nearest)
            {
                secondNearest = nearest;
                nearest = distanceSquared;
            }
            else if (distanceSquared < secondNearest)
            {
                secondNearest = distanceSquared;
            }
        }
    }

    return sqrt(secondNearest) - sqrt(nearest);
}

// World-anchored network of thin lines that drifts downwind, broken into patches by a slower noise.
// Nothing in it depends on the camera, so orbiting the view never moves the pattern.
float PixelWaterSurfacePattern(float3 positionWS, float waveHeight)
{
    if (_PixelWaterPatternColor.a <= 0.0001)
        return 0.0;

    float2 flow = PixelWaterPrimaryWaveDirection() * (_PixelWaterTime * _PixelWaterPatternFlowSpeed);
    float2 patternPosition = (positionWS.xz - flow) / _PixelWaterPatternScale;

    // Warping the domain bends the straight cell borders into wavy, organic lines, and the swell
    // nudges them so they bob with the waves instead of sliding rigidly.
    float warpTime = _PixelWaterTime * 0.15;
    float2 warp = float2(
        PixelWaterNoise(patternPosition * 0.8 + warpTime),
        PixelWaterNoise(patternPosition * 0.8 + float2(31.7, 11.3) - warpTime)) - 0.5;
    patternPosition += warp * _PixelWaterPatternWarp + waveHeight * 4.0;
    float edge = PixelWaterVoronoiEdge(patternPosition, _PixelWaterTime * _PixelWaterPatternMorphSpeed);
    // Evaluated before any per-pixel branch, since it needs screen derivatives.
    float lineMask = PixelWaterPixelLine(edge, _PixelWaterPatternWidth);

    float breakup = PixelWaterNoise(
        (positionWS.xz - flow * 0.5) * _PixelWaterPatternBreakupScale + _PixelWaterTime * 0.03);
    return lineMask * step(1.0 - _PixelWaterPatternCoverage, breakup);
}

// Horizontal distance to the shore in metres, from the surface's baked map when it has one. The
// depth-based fallback is only an estimate and depends on the view, so banks facing away from the
// camera get little foam without a PixelWaterShoreline on the surface.
float PixelWaterShoreDistance(float3 positionWS, float depthEstimate)
{
    if (_PixelWaterShoreMapOn < 0.5)
        return depthEstimate;

    float2 uv = mul(_PixelWaterWorldToShoreMap, float4(positionWS, 1.0)).xz;
    return SAMPLE_TEXTURE2D_LOD(_PixelWaterShoreMap, sampler_PixelWaterShoreMap, uv, 0).r;
}

// A solid band hugging the shore that laps in and out, and thin ripple lines that leave it and
// travel outwards, breaking up as they go. All distances are horizontal, so the foam is the same
// width on every bank and around every rock.
float PixelWaterShoreFoam(float3 positionWS, float shoreDistance)
{
    if (_PixelWaterShoreWidth <= 0.0001)
        return 0.0;

    // Ripple lines need screen derivatives, which are undefined inside the per-pixel branch below.
    float spacing = max(0.01, _PixelWaterShoreRippleSpacing);
    float rippleHalfWidth = fwidth(shoreDistance) * PixelWaterPixelRatio() * _PixelWaterShoreRippleWidth * 0.5;

    // The furthest the foam can reach, with the noise at its maximum and the lap fully out. Most of
    // a water surface lies beyond it, and there the foam is empty without evaluating the noise.
    float maximumEdge = _PixelWaterShoreWidth * (1.0 + abs(_PixelWaterShoreNoiseAmount))
        + abs(_PixelWaterShoreLapAmount);
    if (shoreDistance > maximumEdge + max(0.0, _PixelWaterShoreRippleRange) + 0.001)
        return 0.0;

    float noise = PixelWaterFbm(
        positionWS.xz * _PixelWaterShoreNoiseScale + _PixelWaterTime * 0.05);
    float lapPhase = _PixelWaterTime * _PixelWaterShoreLapSpeed
        + PixelWaterFbm(positionWS.xz * (_PixelWaterShoreNoiseScale * 0.35)) * TWO_PI;

    float edge = _PixelWaterShoreWidth * (1.0 + (noise - 0.5) * 2.0 * _PixelWaterShoreNoiseAmount)
        + sin(lapPhase) * _PixelWaterShoreLapAmount;
    edge = max(0.0, edge);

    float solid = step(shoreDistance, edge);
    if (_PixelWaterShoreRippleRange <= 0.0001)
        return solid;

    // Distance past the solid band, measured in ripples that are born at the band and move out.
    float outside = shoreDistance - edge;
    float ripplePhase = outside / spacing - _PixelWaterTime * _PixelWaterShoreRippleSpeed / spacing;
    float toLine = min(frac(ripplePhase), 1.0 - frac(ripplePhase)) * spacing;
    float ripple = step(toLine, rippleHalfWidth) * step(0.0, outside);

    // Further out, less of each ripple survives, so the lines dissolve instead of ending on a hard edge.
    float travelled = saturate(outside / _PixelWaterShoreRippleRange);
    float breakup = PixelWaterNoise(positionWS.xz * (_PixelWaterShoreNoiseScale * 1.5) + _PixelWaterTime * 0.1);
    ripple *= step(travelled, breakup * 0.85);

    return saturate(solid + ripple);
}

// Pixel sparkles where the waves mirror the main light towards the camera. Sparkle points are
// scattered over world cells and each one twinkles on its own clock; the shape is measured in
// internal pixels, so a sparkle is always a crisp dot or a small plus, never a smeared highlight.
float PixelWaterGlints(
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    float3 lightDirectionWS,
    float worldPerPixel)
{
    if (_PixelWaterGlintStrength <= 0.0001)
        return 0.0;

    float specular = dot(reflect(-lightDirectionWS, normalWS), viewDirectionWS);
    if (specular < _PixelWaterGlintThreshold)
        return 0.0;

    float cellSize = max(0.05, _PixelWaterGlintCellSize);
    float2 cell = floor(positionWS.xz / cellSize);
    float2 random = PixelWaterHash2(cell + 17.0);
    float phase = _PixelWaterTime * _PixelWaterGlintSpeed + random.x * 13.0;
    if (PixelWaterHash(cell + floor(phase) * 7.31) > _PixelWaterGlintDensity)
        return 0.0;

    // Kept away from the cell border so a sparkle never needs a neighbouring cell to finish it.
    float2 sparkleXZ = (cell + 0.25 + 0.5 * random) * cellSize;
    float3 offset = positionWS - float3(sparkleXZ.x, positionWS.y, sparkleXZ.y);
    float2 pixels = abs(float2(
        dot(offset, UNITY_MATRIX_V[0].xyz),
        dot(offset, UNITY_MATRIX_V[1].xyz))) / worldPerPixel;

    // A single pixel that stretches into a short horizontal dash around the middle of its twinkle,
    // the way a glint on rippling water reads in pixel art.
    float flare = step(abs(frac(phase) - 0.5), 0.2);
    float halfLength = lerp(0.5, 1.5, flare);
    return step(pixels.x, halfLength) * step(pixels.y, 0.5);
}

#endif
