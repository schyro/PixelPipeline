#ifndef PIXEL_PIPELINE_RAIN_DROPS_INCLUDED
#define PIXEL_PIPELINE_RAIN_DROPS_INCLUDED

// Every rain drop is worked out from its instance id and the time alone: where it lands, when, and
// how fast it falls. The drop shader and the splash shader call the same function for the same
// instance, so each splash plays exactly where and when its drop ends, with no buffer shared
// between them and no CPU simulation.
//
// Drops are dealt out on a grid of square tiles laid across the plane the rain falls through. A
// tile is seeded by its place in the world rather than by its place in the draw, so while the
// camera pans and the grid slides along, a drop already falling keeps falling where it was.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.schyro.pixel-pipeline/Shaders/Environment/RainOcclusion.hlsl"

// Uploaded by PixelRainRenderer.
// x: tiles per side, y: tile size in metres, z: drops per tile, w: chance a drop falls in a cycle.
float4 _PixelRainGrid;
// xy: plane coordinates of the first tile, in tiles. zw: plane coordinates of the rain area's
// corner, in metres.
float4 _PixelRainGridOrigin;
// x: rain area width in metres, y: ground height used without the occlusion map, zw: unused.
float4 _PixelRainArea;
// xyz: unit direction the drops travel.
float4 _PixelRainFall;
// xyz: the plane axes the grid runs along, both at right angles to the fall.
float4 _PixelRainPlaneX;
float4 _PixelRainPlaneY;
// x: time in seconds, y: cycle length in seconds, z: seconds into a cycle at which a drop lands,
// w: path length in metres from where a drop appears to where it lands.
float4 _PixelRainTiming;
// x: minimum speed, y: maximum speed in metres per second.
float4 _PixelRainSpeed;
// Scene light the unlit drops and splashes are tinted by, so they darken with the night.
half4 _PixelRainLight;

struct PixelRainDrop
{
    // Where the drop ends: the first surface under it, or the far end of the occlusion map.
    float3 landingWS;
    // Occlusion map coordinates of the landing point.
    float2 landingUV;
    // True when it lands on something in the occlusion map, and so splashes.
    bool onSurface;
    float speed;
    // Seconds since the drop landed; negative while it falls.
    float sinceLanding;
    // Per drop and per cycle, for anything else that varies between drops.
    uint seed;
};

bool PixelRainGetDrop(uint instanceID, out PixelRainDrop drop)
{
    drop = (PixelRainDrop)0;

    uint tilesPerSide = max(1u, (uint)_PixelRainGrid.x);
    uint dropsPerTile = max(1u, (uint)_PixelRainGrid.z);
    uint tileIndex = instanceID / dropsPerTile;
    uint slot = instanceID - tileIndex * dropsPerTile;
    int2 tile = int2(_PixelRainGridOrigin.xy) + int2(tileIndex % tilesPerSide, tileIndex / tilesPerSide);

    // Each slot runs on its own phase so the drops never land in unison, and takes a new place in
    // its tile every cycle.
    uint slotSeed = JenkinsHash(uint3(asuint(tile.x), asuint(tile.y), slot));
    float cycleSeconds = _PixelRainTiming.y;
    float phase = _PixelRainTiming.x / cycleSeconds + GenerateHashedRandomFloat(slotSeed);
    float cycle = floor(phase);
    uint seed = JenkinsHash(uint2(slotSeed, (uint)cycle));
    if (GenerateHashedRandomFloat(seed) >= _PixelRainGrid.w)
        return false;

    float2 plane = (float2(tile) + float2(GenerateHashedRandomFloat(seed + 1u), GenerateHashedRandomFloat(seed + 2u)))
                 * _PixelRainGrid.y;
    float2 inArea = plane - _PixelRainGridOrigin.zw;
    if (any(inArea < 0.0) || any(inArea > _PixelRainArea.x))
        return false;

    float3 fall = _PixelRainFall.xyz;
    float3 onPlane = _PixelRainPlaneX.xyz * plane.x + _PixelRainPlaneY.xyz * plane.y;
    // Without a surface under it, a drop is timed against the ground height of the rain area and
    // falls on through it.
    drop.landingWS = onPlane + fall * ((_PixelRainArea.y - onPlane.y) / fall.y);
    if (PixelRainOcclusionAvailable())
    {
        float2 uv = PixelRainOcclusionMapPosition(onPlane).xy;
        if (!PixelRainOcclusionInsideMap(uv))
            return false;

        // The map is rendered along the fall, so the texel under a drop's path holds the first
        // surface on it, and nothing stands between the drop and where it lands.
        float depth = PixelRainOcclusionSampleDepth(uv);
        drop.landingUV = uv;
        drop.onSurface = !PixelRainOcclusionIsEmpty(depth);
        if (drop.onSurface)
            drop.landingWS = PixelRainOcclusionSurfacePosition(uv, depth);
    }

    drop.speed = lerp(_PixelRainSpeed.x, _PixelRainSpeed.y, GenerateHashedRandomFloat(seed + 3u));
    drop.sinceLanding = (phase - cycle) * cycleSeconds - _PixelRainTiming.z;
    drop.seed = seed;
    return true;
}

#endif
