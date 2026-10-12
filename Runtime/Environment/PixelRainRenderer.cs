using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Draws the rain: one procedural draw for the drops and one for their splashes. Both shaders
    /// work out every drop from its instance id and the time, the same way, so a splash plays exactly
    /// where and when its drop ends. The renderer feature's depth map along the rain direction gives
    /// the surface each drop lands on, so drops stop on the ground, roofs, tree tops, and water alike,
    /// with no colliders, no particle system, and no CPU simulation.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PixelRainRenderer : MonoBehaviour
    {
        [SerializeField] private PixelEnvironmentController environmentController;

        // Drops are dealt out on tiles of this size across the plane the rain falls through.
        private const float TileSize = 4f;
        private const int MaximumDrops = 65536;

        private MaterialPropertyBlock _propertyBlock;

        private static readonly int GridId = Shader.PropertyToID("_PixelRainGrid");
        private static readonly int GridOriginId = Shader.PropertyToID("_PixelRainGridOrigin");
        private static readonly int AreaId = Shader.PropertyToID("_PixelRainArea");
        private static readonly int FallId = Shader.PropertyToID("_PixelRainFall");
        private static readonly int PlaneXId = Shader.PropertyToID("_PixelRainPlaneX");
        private static readonly int PlaneYId = Shader.PropertyToID("_PixelRainPlaneY");
        private static readonly int TimingId = Shader.PropertyToID("_PixelRainTiming");
        private static readonly int SpeedId = Shader.PropertyToID("_PixelRainSpeed");
        private static readonly int LightId = Shader.PropertyToID("_PixelRainLight");
        private static readonly int DropShapeId = Shader.PropertyToID("_PixelRainDropShape");
        private static readonly int SplashColorId = Shader.PropertyToID("_RainSplashColor");
        private static readonly int SplashShapeId = Shader.PropertyToID("_RainSplashShape");
        private static readonly int SplashSurfaceId = Shader.PropertyToID("_RainSplashSurface");

        /// <summary>
        /// Axes of the plane at right angles to the fall. The occlusion map is laid out along the
        /// same axes, so a drop's place on this plane is its place in the map.
        /// </summary>
        internal static void GetPlaneAxes(Vector3 fallDirection, out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            forward = fallDirection.sqrMagnitude > 0.0001f ? fallDirection.normalized : Vector3.down;
            Vector3 upHint = Mathf.Abs(forward.z) > 0.99f ? Vector3.right : Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(forward, upHint);
            right = rotation * Vector3.right;
            up = rotation * Vector3.up;
        }

        private void OnEnable()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (environmentController == null)
                return;

            PixelPipelineSettings profile = environmentController.Profile;
            Camera targetCamera = environmentController.TargetCamera;
            if (profile == null || targetCamera == null)
                return;

            PixelRainSettings rain = profile.Environment.Rain;
            Material dropMaterial = rain.DropMaterial;
            Material splashMaterial = rain.Impacts ? rain.SplashMaterial : null;
            if ((dropMaterial == null && splashMaterial == null) || rain.DropsPerSecond <= 0f)
                return;

            if (!PixelEnvironmentController.TryResolveRain(profile, out Vector3 centre, out Vector3 fallDirection, out float rainAmount))
                return;

            GetPlaneAxes(fallDirection, out Vector3 fall, out Vector3 planeX, out Vector3 planeY);

            // The grid covers the rain area, snapped to whole tiles in the world.
            float area = rain.AreaSize;
            Vector2 areaCorner = new Vector2(Vector3.Dot(centre, planeX), Vector3.Dot(centre, planeY)) - Vector2.one * (area * 0.5f);
            Vector2Int gridOrigin = new(Mathf.FloorToInt(areaCorner.x / TileSize), Mathf.FloorToInt(areaCorner.y / TileSize));
            int tilesPerSide = Mathf.CeilToInt(area / TileSize) + 1;

            // A cycle holds the longest fall and everything after the landing: the splash, or the
            // tail of the slowest, longest streak running in.
            float pathLength = rain.FallHeight / Mathf.Max(0.05f, -fall.y);
            float landing = pathLength / rain.SpeedMinimum;
            float longestStreak = rain.Length * rain.SpeedMaximum / rain.AverageSpeed;
            float cycle = landing + Mathf.Max(rain.SplashLifetime, longestStreak / rain.SpeedMinimum);

            // Each slot of a tile drops at most once per cycle; the chance tops up the rate exactly.
            float dropsPerTileCycle = rain.DropsPerSecond * cycle * (TileSize * TileSize) / (area * area);
            int tiles = tilesPerSide * tilesPerSide;
            int dropsPerTile = Mathf.Clamp(Mathf.CeilToInt(dropsPerTileCycle), 1, Mathf.Max(1, MaximumDrops / tiles));
            float chance = Mathf.Min(1f, dropsPerTileCycle / dropsPerTile) * rainAmount;

            Color tint = PixelMath.SkyLightTint(rain.LightResponse);

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetVector(GridId, new Vector4(tilesPerSide, TileSize, dropsPerTile, chance));
            _propertyBlock.SetVector(GridOriginId, new Vector4(gridOrigin.x, gridOrigin.y, areaCorner.x, areaCorner.y));
            _propertyBlock.SetVector(AreaId, new Vector4(area, centre.y, 0f, 0f));
            _propertyBlock.SetVector(FallId, fall);
            _propertyBlock.SetVector(PlaneXId, planeX);
            _propertyBlock.SetVector(PlaneYId, planeY);
            _propertyBlock.SetVector(TimingId, new Vector4(
                Application.isPlaying ? Time.time : Time.realtimeSinceStartup,
                cycle,
                landing,
                pathLength));
            _propertyBlock.SetVector(SpeedId, new Vector4(rain.SpeedMinimum, rain.SpeedMaximum, 0f, 0f));
            _propertyBlock.SetVector(LightId, new Vector4(tint.r, tint.g, tint.b, 1f));
            _propertyBlock.SetVector(DropShapeId, new Vector4(rain.WidthMinimum, rain.WidthMaximum, rain.Length, rain.AverageSpeed));
            _propertyBlock.SetColor(SplashColorId, rain.SplashColor);
            _propertyBlock.SetVector(SplashShapeId, new Vector4(
                rain.SplashSizeMinimum,
                rain.SplashSizeMaximum,
                rain.SplashLifetime,
                rain.RingWidth));
            _propertyBlock.SetVector(SplashSurfaceId, new Vector4(rain.MinimumUpFacing, 0f, 0f, 0f));

            // Positions come from the shader, so the bounds only have to hold the area and the
            // height the drops fall from.
            Bounds worldBounds = new(
                centre,
                new Vector3(area + pathLength * 2f, rain.OcclusionDepthRange + pathLength * 2f, area + pathLength * 2f));

            int instanceCount = tiles * dropsPerTile;
            if (dropMaterial != null)
                Draw(dropMaterial, targetCamera, worldBounds, instanceCount);
            if (splashMaterial != null)
                Draw(splashMaterial, targetCamera, worldBounds, instanceCount);
        }

        private void Draw(Material material, Camera targetCamera, Bounds worldBounds, int instanceCount)
        {
            RenderParams renderParams = new(material)
            {
                // Only this camera renders the occlusion map the drops land on.
                camera = targetCamera,
                layer = gameObject.layer,
                matProps = _propertyBlock,
                worldBounds = worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            Graphics.RenderPrimitives(in renderParams, MeshTopology.Triangles, 6, instanceCount);
        }
    }
}
