using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PixelPipeline.Runtime
{
    /// <summary>How widely the shadow map is filtered before the pixel shadow snaps it to bands.</summary>
    public enum PixelShadowFilter
    {
        [InspectorName("Off (texel stairs)")] SingleTap = 0,
        [InspectorName("Tent 3x3")] Tent3x3 = 1,
        [InspectorName("Tent 5x5")] Tent5x5 = 2,
        [InspectorName("Tent 7x7")] Tent7x7 = 3
    }

    [CreateAssetMenu(fileName = "PixelPipelineSettings", menuName = "Rendering/Pixel Pipeline Settings")]
    public sealed class PixelPipelineSettings : ScriptableObject
    {
        [Header("Internal Resolution")]
        [SerializeField, Min(64)] private int internalWidth = 960;
        [SerializeField, Min(64)] private int internalHeight = 540;
        [SerializeField] private bool matchCameraAspect = true;

        [Header("Camera Filtering")]
        [SerializeField] private bool renderSceneView;
        [SerializeField] private bool baseCameraOnly = true;

        [Header("Outline")]
        [SerializeField] private bool enableOutline = true;
        [SerializeField, Range(0f, 1f)] private float lineDarken = 0.45f;
        [SerializeField, Range(0f, 1f)] private float lineAlpha = 1f;
        [SerializeField, Range(0f, 1f)] private float creaseBrighten = 0.4f;
        [SerializeField, Range(0f, 1f)] private float creaseAlpha = 0.8f;
        [SerializeField, Range(0.5f, 4f)] private float kernelRadius = 0.54f;
        [SerializeField, Min(0.0001f)] private float depthThreshold = 0.15f;
        [SerializeField, Range(0f, 0.99f)] private float grazingAngleStart = 0.3f;
        [SerializeField, Range(0f, 4f)] private float grazingDepthScale = 3f;
        [SerializeField, Min(0f)] private float creaseDepthLow = 0.01f;
        [SerializeField, Min(0.0001f)] private float creaseDepthHigh = 0.08f;
        [SerializeField, Min(0f)] private float creaseNormalLow = 0.04f;
        [SerializeField, Min(0.0001f)] private float creaseNormalHigh = 0.25f;

        [Header("Lighting")]
        [Tooltip("Snaps direct light and the ambient gradient on lit opaque surfaces to flat bands, so curved surfaces shade in clean pixel steps instead of a smooth ramp. Needs a Deferred+ or Deferred renderer; forward renderers keep URP's smooth lighting.")]
        [SerializeField] private bool enableLightBands = true;
        [Tooltip("Number of brightness steps between unlit and fully lit.")]
        [SerializeField, Range(1, 6)] private int lightBands = 3;
        [Tooltip("Moves the band thresholds. Positive values grow the brighter bands, negative values the darker ones.")]
        [SerializeField, Range(-0.45f, 0.45f)] private float lightBandBias;

        [Header("Shadows")]
        [Tooltip("Resolves main-light shadows in screen space and snaps their edges to hard bands, so a shadow edge is one pixel wide instead of a blurred ramp. Applies to URP Lit and Shader Graph materials as well as grass, foliage, and water.")]
        [SerializeField] private bool enablePixelShadows = true;
        [Tooltip("Number of brightness bands between lit and shadowed. 1 is a single hard edge; 2-4 keep a stepped penumbra.")]
        [SerializeField, Range(1, 4)] private int shadowSteps = 1;
        [Tooltip("Moves the band thresholds. Positive values grow the lit side, negative values grow the shadow.")]
        [SerializeField, Range(-0.45f, 0.45f)] private float shadowEdgeBias;
        [Tooltip("Filters the shadow map before the edge is snapped. Without it the snapped edge follows the shadow-map texels, a staircase of teeth several pixels wide at an angle to the screen; a wider tent puts the edge on the true outline, so it becomes a straight pixel line. Wider filters round sharp shadow corners a little and cost more texture fetches (4, 9, or 16).")]
        [SerializeField] private PixelShadowFilter shadowEdgeFilter = PixelShadowFilter.Tent5x5;

        [Header("Environment Systems")]
        [SerializeField] private PixelEnvironmentSettings environment = new();

        [Header("Water")]
        [SerializeField] private PixelWaterSettings water = new();

        public PixelEnvironmentSettings Environment => environment ??= new PixelEnvironmentSettings();
        public PixelWaterSettings Water => water ??= new PixelWaterSettings();
        public bool RenderSceneView => renderSceneView;
        public bool BaseCameraOnly => baseCameraOnly;
        public bool EnableOutline => enableOutline;
        public bool EnablePixelShadows => enablePixelShadows;
        public int ShadowSteps => shadowSteps;
        public float ShadowEdgeBias => shadowEdgeBias;
        public PixelShadowFilter ShadowEdgeFilter => shadowEdgeFilter;
        public bool EnableLightBands => enableLightBands;
        public int LightBands => lightBands;
        public float LightBandBias => lightBandBias;
        public bool EnableRainImpacts => Environment.Rain.Impacts;
        public bool EnableCloudShadows => Environment.Clouds.Enabled && Environment.Clouds.Noise != null;
        public bool EnableGodRays => EnableCloudShadows && Environment.GodRays.Enabled;
        public bool EnableWindStreaks => Environment.WindStreaks.Enabled
                                         && Environment.WindStreaks.Material != null
                                         && Environment.WindStreaks.MaximumStreaks > 0;
        public float LineDarken => lineDarken;
        public float LineAlpha => lineAlpha;
        public float CreaseBrighten => creaseBrighten;
        public float CreaseAlpha => creaseAlpha;
        public float KernelRadius => kernelRadius;
        public float DepthThreshold => depthThreshold;
        public float GrazingAngleStart => grazingAngleStart;
        public float GrazingDepthScale => grazingDepthScale;
        public float CreaseDepthLow => creaseDepthLow;
        public float CreaseDepthHigh => creaseDepthHigh;
        public float CreaseNormalLow => creaseNormalLow;
        public float CreaseNormalHigh => creaseNormalHigh;

        private static readonly int CloudNoiseId = Shader.PropertyToID("_CloudNoise");
        private static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
        private static readonly int CloudWorldYId = Shader.PropertyToID("_CloudWorldY");
        private static readonly int CloudSpeedId = Shader.PropertyToID("_CloudSpeed");
        private static readonly int CloudTimeId = Shader.PropertyToID("_CloudTime");
        private static readonly int CloudContrastId = Shader.PropertyToID("_CloudContrast");
        private static readonly int CloudThresholdId = Shader.PropertyToID("_CloudThreshold");
        private static readonly int CloudCoverageId = Shader.PropertyToID("_CloudCoverage");
        private static readonly int CloudCoverageInfluenceId = Shader.PropertyToID("_CloudCoverageInfluence");
        private static readonly int CloudDirectionId = Shader.PropertyToID("_CloudDirection");
        private static readonly int CloudProjectionDirectionId = Shader.PropertyToID("_CloudLightDirection");
        private static readonly int CloudShadowMinimumId = Shader.PropertyToID("_CloudShadowMin");
        private static readonly int CloudShadowStrengthId = Shader.PropertyToID("_CloudShadowStrength");
        private static readonly int CloudDivergenceAngleId = Shader.PropertyToID("_CloudDivergeAngle");
        private static readonly int CloudPowerId = Shader.PropertyToID("_CloudPower");
        private static readonly int CloudShadowStepsId = Shader.PropertyToID("_CloudShadowSteps");
        private static readonly int PixelShadowStepsId = Shader.PropertyToID("_PixelShadowSteps");
        private static readonly int PixelShadowEdgeBiasId = Shader.PropertyToID("_PixelShadowEdgeBias");
        private static readonly int PixelShadowFilterId = Shader.PropertyToID("_PixelShadowFilter");
        private static readonly int PixelLightBandsId = Shader.PropertyToID("_PixelLightBands");
        private static readonly int PixelLightBandBiasId = Shader.PropertyToID("_PixelLightBandBias");
        private static readonly int WindDirectionId = Shader.PropertyToID("_PixelWindDirection");
        private static readonly int WindStrengthId = Shader.PropertyToID("_PixelWindStrength");

        public Vector2Int GetInternalResolution(float cameraAspect)
        {
            int height = Mathf.Max(64, internalHeight);
            int width = matchCameraAspect
                ? Mathf.Max(64, Mathf.RoundToInt(height * Mathf.Max(0.1f, cameraAspect)))
                : Mathf.Max(64, internalWidth);

            return new Vector2Int(width, height);
        }

        /// <summary>
        /// The URP render scale at which a camera's target comes out at exactly
        /// <see cref="GetInternalResolution"/>. False when the scene cannot be drawn at the internal
        /// resolution: the width is fixed, or URP would not accept the scale.
        /// </summary>
        internal bool TryGetRenderScale(Camera camera, out float scale)
        {
            scale = 1f;
            int pixelWidth = camera.pixelWidth;
            int pixelHeight = camera.pixelHeight;
            if (!matchCameraAspect || pixelWidth <= 0 || pixelHeight <= 0)
                return false;

            // URP truncates pixel size times scale on each axis, so every scale in this range gives
            // the wanted size on both; the middle of it is safe from rounding.
            Vector2Int target = GetInternalResolution(camera.aspect);
            double low = System.Math.Max(target.x / (double)pixelWidth, target.y / (double)pixelHeight);
            double high = System.Math.Min((target.x + 1) / (double)pixelWidth, (target.y + 1) / (double)pixelHeight);
            if (high <= low)
                return false;

            scale = (float)((low + high) * 0.5);
            return scale >= UniversalRenderPipeline.minRenderScale && scale <= UniversalRenderPipeline.maxRenderScale;
        }

        internal void ApplyCloudGlobals(PixelEnvironmentRuntimeState runtimeState)
        {
            PixelCloudSettings clouds = Environment.Clouds;
            PixelWindSettings wind = Environment.Wind;
            Shader.SetGlobalTexture(CloudNoiseId, clouds.Noise);
            Shader.SetGlobalFloat(CloudScaleId, Mathf.Max(0.001f, clouds.Scale));
            Shader.SetGlobalFloat(CloudWorldYId, clouds.WorldHeight);
            Shader.SetGlobalFloat(CloudSpeedId, clouds.Speed * wind.Strength);
            Shader.SetGlobalFloat(CloudTimeId, Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
            Shader.SetGlobalFloat(CloudContrastId, Mathf.Max(0.01f, clouds.Contrast));
            Shader.SetGlobalFloat(CloudThresholdId, clouds.Threshold);
            Shader.SetGlobalFloat(CloudCoverageId, runtimeState.CloudCoverage);
            Shader.SetGlobalFloat(CloudCoverageInfluenceId, clouds.CoverageInfluence);
            Shader.SetGlobalFloat(CloudShadowMinimumId, clouds.ShadowMinimum);
            Shader.SetGlobalFloat(CloudShadowStrengthId, EnableCloudShadows ? clouds.ShadowStrength : 0f);
            Shader.SetGlobalFloat(CloudDivergenceAngleId, clouds.DivergenceAngle);
            Shader.SetGlobalFloat(CloudPowerId, clouds.Power);
            Shader.SetGlobalFloat(CloudShadowStepsId, clouds.ShadowSteps);

            Vector2 direction = wind.NormalizedDirection;
            Vector3 projection = clouds.ProjectionDirection.sqrMagnitude > 0.0001f
                ? clouds.ProjectionDirection.normalized
                : new Vector3(0.2f, -1f, 0.2f).normalized;

            Shader.SetGlobalVector(CloudDirectionId, new Vector4(direction.x, direction.y, 0f, 0f));
            Shader.SetGlobalVector(CloudProjectionDirectionId, projection);
        }

        /// <summary>
        /// Shaders that sample the shadow map themselves (grass, foliage, water, deferred lighting) read the
        /// shadow values, so they match the screen-space result in every camera, including the scene
        /// view. The light bands are read by the deferred lighting pass.
        /// </summary>
        internal void ApplyLightingGlobals()
        {
            Shader.SetGlobalFloat(PixelShadowStepsId, enablePixelShadows ? shadowSteps : 0f);
            Shader.SetGlobalFloat(PixelShadowEdgeBiasId, shadowEdgeBias);
            Shader.SetGlobalFloat(PixelShadowFilterId, enablePixelShadows ? (float)shadowEdgeFilter : 0f);
            Shader.SetGlobalFloat(PixelLightBandsId, enableLightBands ? lightBands : 0f);
            Shader.SetGlobalFloat(PixelLightBandBiasId, lightBandBias);
        }

        internal void ApplyWindGlobals()
        {
            PixelWindSettings wind = Environment.Wind;
            Vector2 direction = wind.NormalizedDirection;
            Shader.SetGlobalVector(
                WindDirectionId,
                new Vector4(direction.x, 0f, direction.y, 0f));
            Shader.SetGlobalFloat(WindStrengthId, wind.Strength);
        }

        internal void ApplyWaterGlobals(Vector2Int internalResolution)
        {
            if (!Water.EnableWater)
                return;

            Water.ApplyGlobals(
                internalResolution,
                Environment.Wind.NormalizedDirection,
                Environment.Wind.Strength);
        }

        private void OnValidate()
        {
            Environment.Validate();
            Water.Validate();
            internalWidth = Mathf.Max(64, internalWidth);
            internalHeight = Mathf.Max(64, internalHeight);
            shadowSteps = Mathf.Clamp(shadowSteps, 1, 4);
            lightBands = Mathf.Clamp(lightBands, 1, 6);
            creaseDepthHigh = Mathf.Max(creaseDepthLow + 0.0001f, creaseDepthHigh);
            creaseNormalHigh = Mathf.Max(creaseNormalLow + 0.0001f, creaseNormalHigh);
        }
    }
}
