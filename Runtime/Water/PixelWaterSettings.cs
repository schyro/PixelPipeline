using UnityEngine;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Stylised water tuning. Every value is pushed to the shader as a global uniform, so one
    /// settings asset drives every water surface in the scene, exactly like the cloud shadows.
    /// Colours are surface colours: the shader lights them with the main light and ambient, so they
    /// follow the day/night cycle on their own.
    /// </summary>
    [System.Serializable]
    public sealed class PixelWaterSettings
    {
        [Tooltip("Turn off when the scene has no water; skips the global uploads and the reflection camera.")]
        [SerializeField] private bool enableWater = true;

        [Header("Depth Colour")]
        [SerializeField] private Color shallowColor = new(0.2f, 0.5f, 0.52f);
        [SerializeField] private Color deepColor = new(0.05f, 0.15f, 0.3f);
        [Tooltip("Water depth in metres at which Deep Colour is reached.")]
        [SerializeField, Min(0.01f)] private float depthDistance = 0.9f;
        [Tooltip("Number of flat colour bands between shallow and deep.")]
        [SerializeField, Range(1, 8)] private int colorSteps = 4;
        [SerializeField, Range(0f, 1f)] private float shallowOpacity = 0.45f;
        [SerializeField, Range(0f, 1f)] private float deepOpacity = 0.95f;
        [Tooltip("How much main light shadows remove the direct light on the water. Ambient light always remains.")]
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.85f;

        [Header("Refraction")]
        [SerializeField] private bool enableRefraction = true;
        [Tooltip("Screen space offset in normalised units, snapped to the internal pixel grid.")]
        [SerializeField, Range(0f, 0.08f)] private float refractionStrength = 0.014f;
        [Tooltip("Refraction fades in over this depth so the shoreline never smears.")]
        [SerializeField, Min(0.01f)] private float refractionDepthFade = 0.35f;

        [Header("Surface Pattern")]
        [Tooltip("A world-anchored network of thin lines drifting downwind across the surface.")]
        [SerializeField] private bool enablePattern = true;
        [Tooltip("Line colour; alpha is its opacity.")]
        [SerializeField] private Color patternColor = new(0.55f, 0.78f, 0.8f, 0.65f);
        [Tooltip("Size of one pattern cell in metres.")]
        [SerializeField, Min(0.05f)] private float patternScale = 0.55f;
        [Tooltip("Line width in internal pixels, whatever the zoom or view angle.")]
        [SerializeField, Range(0.5f, 3f)] private float patternLineWidth = 1f;
        [Tooltip("Drift speed along the wind in metres per second at Wind Strength 1.")]
        [SerializeField, Min(0f)] private float patternFlowSpeed = 0.08f;
        [Tooltip("How fast the cells reshape.")]
        [SerializeField, Min(0f)] private float patternMorphSpeed = 0.6f;
        [Tooltip("Bends the straight cell borders into wavy lines. Zero keeps a crisp cell network.")]
        [SerializeField, Range(0f, 2f)] private float patternWarp = 0.9f;
        [Tooltip("Fraction of the surface the lines are allowed to cover; the rest is broken into gaps.")]
        [SerializeField, Range(0f, 1f)] private float patternCoverage = 0.45f;
        [Tooltip("Scale of the noise that breaks the lines into patches.")]
        [SerializeField, Min(0.001f)] private float patternBreakupScale = 1.3f;

        [Header("Shore Foam")]
        [SerializeField] private bool enableShoreFoam = true;
        [SerializeField] private Color shoreFoamColor = new(0.72f, 0.8f, 0.8f);
        [Tooltip("Width of the solid foam band in metres.")]
        [SerializeField, Min(0f)] private float shoreFoamWidth = 0.06f;
        [SerializeField, Min(0.001f)] private float shoreFoamNoiseScale = 2.4f;
        [SerializeField, Range(0f, 1f)] private float shoreFoamNoiseAmount = 0.5f;
        [Tooltip("How fast the foam laps in and out of the shore.")]
        [SerializeField, Min(0f)] private float shoreFoamLapSpeed = 1.1f;
        [SerializeField, Min(0f)] private float shoreFoamLapAmount = 0.018f;
        [Tooltip("Distance in metres between two ripple lines travelling out from the foam band.")]
        [SerializeField, Min(0.01f)] private float shoreRippleSpacing = 0.09f;
        [Tooltip("Speed in metres per second at which the ripples move away from the shore.")]
        [SerializeField, Min(0f)] private float shoreRippleSpeed = 0.05f;
        [Tooltip("How far past the foam band the ripples travel before they have dissolved. Zero turns them off.")]
        [SerializeField, Min(0f)] private float shoreRippleRange = 0.3f;
        [Tooltip("Ripple line width in internal pixels.")]
        [SerializeField, Range(0.5f, 3f)] private float shoreRippleWidth = 1f;

        [Header("Glints")]
        [Tooltip("Pixel sparkles where the waves mirror the main light. They take the light's colour, so the moon gives faint ones.")]
        [SerializeField] private bool enableGlints = true;
        [SerializeField, Range(0f, 2f)] private float glintStrength = 0.9f;
        [Tooltip("How closely a wave has to mirror the light towards the camera before it sparkles.")]
        [SerializeField, Range(-1f, 1f)] private float glintThreshold = 0.2f;
        [Tooltip("Spacing of the sparkle points in metres.")]
        [SerializeField, Min(0.05f)] private float glintCellSize = 0.3f;
        [Tooltip("Fraction of sparkle points lit at any moment.")]
        [SerializeField, Range(0f, 1f)] private float glintDensity = 0.3f;
        [Tooltip("Twinkles per second of one sparkle point.")]
        [SerializeField, Min(0f)] private float glintSpeed = 1.5f;

        [Header("Waves")]
        [SerializeField, Min(0f)] private float waveHeight = 0.025f;
        [SerializeField, Min(0.01f)] private float waveLength = 2.2f;
        [SerializeField] private float waveSpeed = 0.5f;
        [Tooltip("Amplitude of the cross wave relative to the main one.")]
        [SerializeField, Range(0f, 1f)] private float waveChoppiness = 0.45f;
        [Tooltip("Scales the wave normal that drives refraction, reflection, and the glints.")]
        [SerializeField, Range(0f, 8f)] private float waveNormalStrength = 2.5f;

        [Header("Planar Reflection")]
        [SerializeField] private bool enablePlanarReflection;
        [Tooltip("Reflection render target size relative to the internal pixel resolution.")]
        [SerializeField, Range(0.25f, 1f)] private float reflectionResolutionScale = 1f;
        [Tooltip("Layers the mirrored camera renders. The water surface layer is always excluded.")]
        [SerializeField] private LayerMask reflectionLayers = ~0;
        [SerializeField] private bool reflectionCastsShadows = true;
        [Tooltip("Oblique near plane clipping, so geometry below the surface is not mirrored back up.")]
        [SerializeField] private bool reflectionClipsBelowSurface = true;
        [SerializeField] private float reflectionPlaneOffset = 0.01f;
        [SerializeField, Range(0f, 1f)] private float reflectionStrength = 0.45f;
        [Tooltip("0 keeps the Fresnel falloff, 1 mirrors evenly across the whole surface.")]
        [SerializeField, Range(0f, 1f)] private float reflectionFlatness = 0.5f;
        [SerializeField, Range(0.5f, 8f)] private float reflectionFresnelPower = 4f;
        [SerializeField, Range(0f, 0.08f)] private float reflectionDistortion = 0.006f;

        public bool EnableWater => enableWater;
        public bool EnablePlanarReflection => enableWater && enablePlanarReflection;
        public float ReflectionResolutionScale => reflectionResolutionScale;
        public LayerMask ReflectionLayers => reflectionLayers;
        public bool ReflectionCastsShadows => reflectionCastsShadows;
        public bool ReflectionClipsBelowSurface => reflectionClipsBelowSurface;
        public float ReflectionPlaneOffset => reflectionPlaneOffset;

        private static readonly int ShallowColorId = Shader.PropertyToID("_PixelWaterShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_PixelWaterDeepColor");
        private static readonly int DepthDistanceId = Shader.PropertyToID("_PixelWaterDepthDistance");
        private static readonly int ColorStepsId = Shader.PropertyToID("_PixelWaterColorSteps");
        private static readonly int ShallowOpacityId = Shader.PropertyToID("_PixelWaterShallowOpacity");
        private static readonly int DeepOpacityId = Shader.PropertyToID("_PixelWaterDeepOpacity");
        private static readonly int ShadowStrengthId = Shader.PropertyToID("_PixelWaterShadowStrength");

        private static readonly int RefractionStrengthId = Shader.PropertyToID("_PixelWaterRefraction");
        private static readonly int RefractionDepthFadeId = Shader.PropertyToID("_PixelWaterRefractionFade");

        private static readonly int PatternColorId = Shader.PropertyToID("_PixelWaterPatternColor");
        private static readonly int PatternScaleId = Shader.PropertyToID("_PixelWaterPatternScale");
        private static readonly int PatternWidthId = Shader.PropertyToID("_PixelWaterPatternWidth");
        private static readonly int PatternFlowSpeedId = Shader.PropertyToID("_PixelWaterPatternFlowSpeed");
        private static readonly int PatternMorphSpeedId = Shader.PropertyToID("_PixelWaterPatternMorphSpeed");
        private static readonly int PatternWarpId = Shader.PropertyToID("_PixelWaterPatternWarp");
        private static readonly int PatternCoverageId = Shader.PropertyToID("_PixelWaterPatternCoverage");
        private static readonly int PatternBreakupScaleId = Shader.PropertyToID("_PixelWaterPatternBreakupScale");

        private static readonly int ShoreFoamColorId = Shader.PropertyToID("_PixelWaterShoreColor");
        private static readonly int ShoreFoamWidthId = Shader.PropertyToID("_PixelWaterShoreWidth");
        private static readonly int ShoreFoamNoiseScaleId = Shader.PropertyToID("_PixelWaterShoreNoiseScale");
        private static readonly int ShoreFoamNoiseAmountId = Shader.PropertyToID("_PixelWaterShoreNoiseAmount");
        private static readonly int ShoreFoamLapSpeedId = Shader.PropertyToID("_PixelWaterShoreLapSpeed");
        private static readonly int ShoreFoamLapAmountId = Shader.PropertyToID("_PixelWaterShoreLapAmount");
        private static readonly int ShoreRippleSpacingId = Shader.PropertyToID("_PixelWaterShoreRippleSpacing");
        private static readonly int ShoreRippleSpeedId = Shader.PropertyToID("_PixelWaterShoreRippleSpeed");
        private static readonly int ShoreRippleRangeId = Shader.PropertyToID("_PixelWaterShoreRippleRange");
        private static readonly int ShoreRippleWidthId = Shader.PropertyToID("_PixelWaterShoreRippleWidth");

        private static readonly int GlintStrengthId = Shader.PropertyToID("_PixelWaterGlintStrength");
        private static readonly int GlintThresholdId = Shader.PropertyToID("_PixelWaterGlintThreshold");
        private static readonly int GlintCellSizeId = Shader.PropertyToID("_PixelWaterGlintCellSize");
        private static readonly int GlintDensityId = Shader.PropertyToID("_PixelWaterGlintDensity");
        private static readonly int GlintSpeedId = Shader.PropertyToID("_PixelWaterGlintSpeed");

        private static readonly int WaveHeightId = Shader.PropertyToID("_PixelWaterWaveHeight");
        private static readonly int WaveLengthId = Shader.PropertyToID("_PixelWaterWaveLength");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_PixelWaterWaveSpeed");
        private static readonly int WaveChoppinessId = Shader.PropertyToID("_PixelWaterWaveChoppiness");
        private static readonly int WaveNormalStrengthId = Shader.PropertyToID("_PixelWaterWaveNormalStrength");
        private static readonly int WaveDirectionId = Shader.PropertyToID("_PixelWaterWaveDirection");

        private static readonly int ReflectionStrengthId = Shader.PropertyToID("_PixelWaterReflectionStrength");
        private static readonly int ReflectionFlatnessId = Shader.PropertyToID("_PixelWaterReflectionFlatness");
        private static readonly int ReflectionFresnelPowerId = Shader.PropertyToID("_PixelWaterReflectionFresnel");
        private static readonly int ReflectionDistortionId = Shader.PropertyToID("_PixelWaterReflectionDistortion");

        private static readonly int PixelGridId = Shader.PropertyToID("_PixelWaterPixelGrid");
        private static readonly int TimeId = Shader.PropertyToID("_PixelWaterTime");

        internal void ApplyGlobals(
            Vector2Int internalResolution,
            Vector2 windDirection,
            float windStrength)
        {
            float strength = Mathf.Max(0f, windStrength);

            Shader.SetGlobalColor(ShallowColorId, shallowColor);
            Shader.SetGlobalColor(DeepColorId, deepColor);
            Shader.SetGlobalFloat(DepthDistanceId, Mathf.Max(0.01f, depthDistance));
            Shader.SetGlobalFloat(ColorStepsId, Mathf.Max(1, colorSteps));
            Shader.SetGlobalFloat(ShallowOpacityId, shallowOpacity);
            Shader.SetGlobalFloat(DeepOpacityId, deepOpacity);
            Shader.SetGlobalFloat(ShadowStrengthId, shadowStrength);

            Shader.SetGlobalFloat(RefractionStrengthId, enableRefraction ? refractionStrength : 0f);
            Shader.SetGlobalFloat(RefractionDepthFadeId, Mathf.Max(0.01f, refractionDepthFade));

            Color pattern = patternColor;
            if (!enablePattern)
                pattern.a = 0f;
            Shader.SetGlobalColor(PatternColorId, pattern);
            Shader.SetGlobalFloat(PatternScaleId, Mathf.Max(0.05f, patternScale));
            Shader.SetGlobalFloat(PatternWidthId, patternLineWidth);
            Shader.SetGlobalFloat(PatternFlowSpeedId, patternFlowSpeed * strength);
            Shader.SetGlobalFloat(PatternMorphSpeedId, patternMorphSpeed);
            Shader.SetGlobalFloat(PatternWarpId, patternWarp);
            Shader.SetGlobalFloat(PatternCoverageId, patternCoverage);
            Shader.SetGlobalFloat(PatternBreakupScaleId, Mathf.Max(0.001f, patternBreakupScale));

            Shader.SetGlobalColor(ShoreFoamColorId, shoreFoamColor);
            Shader.SetGlobalFloat(ShoreFoamWidthId, enableShoreFoam ? shoreFoamWidth : 0f);
            Shader.SetGlobalFloat(ShoreFoamNoiseScaleId, Mathf.Max(0.001f, shoreFoamNoiseScale));
            Shader.SetGlobalFloat(ShoreFoamNoiseAmountId, shoreFoamNoiseAmount);
            Shader.SetGlobalFloat(ShoreFoamLapSpeedId, shoreFoamLapSpeed);
            Shader.SetGlobalFloat(ShoreFoamLapAmountId, shoreFoamLapAmount);
            Shader.SetGlobalFloat(ShoreRippleSpacingId, Mathf.Max(0.01f, shoreRippleSpacing));
            Shader.SetGlobalFloat(ShoreRippleSpeedId, shoreRippleSpeed);
            Shader.SetGlobalFloat(ShoreRippleRangeId, shoreRippleRange);
            Shader.SetGlobalFloat(ShoreRippleWidthId, shoreRippleWidth);

            Shader.SetGlobalFloat(GlintStrengthId, enableGlints ? glintStrength : 0f);
            Shader.SetGlobalFloat(GlintThresholdId, glintThreshold);
            Shader.SetGlobalFloat(GlintCellSizeId, Mathf.Max(0.05f, glintCellSize));
            Shader.SetGlobalFloat(GlintDensityId, glintDensity);
            Shader.SetGlobalFloat(GlintSpeedId, glintSpeed);

            Shader.SetGlobalFloat(WaveHeightId, waveHeight * strength);
            Shader.SetGlobalFloat(WaveLengthId, Mathf.Max(0.01f, waveLength));
            Shader.SetGlobalFloat(WaveSpeedId, waveSpeed * strength);
            Shader.SetGlobalFloat(WaveChoppinessId, waveChoppiness);
            // Wave height already carries Strength, so scaling the normal multiplier as well would
            // square the response and make medium wind unnaturally flat.
            Shader.SetGlobalFloat(WaveNormalStrengthId, waveNormalStrength);

            Vector2 direction = windDirection.sqrMagnitude > 0.0001f
                ? windDirection.normalized
                : Vector2.right;
            Shader.SetGlobalVector(WaveDirectionId, new Vector4(direction.x, direction.y, 0f, 0f));

            Shader.SetGlobalFloat(ReflectionStrengthId, reflectionStrength);
            Shader.SetGlobalFloat(ReflectionFlatnessId, reflectionFlatness);
            Shader.SetGlobalFloat(ReflectionFresnelPowerId, reflectionFresnelPower);
            Shader.SetGlobalFloat(ReflectionDistortionId, reflectionDistortion);

            Vector2 resolution = new(Mathf.Max(1, internalResolution.x), Mathf.Max(1, internalResolution.y));
            Shader.SetGlobalVector(
                PixelGridId,
                new Vector4(resolution.x, resolution.y, 1f / resolution.x, 1f / resolution.y));
            Shader.SetGlobalFloat(TimeId, Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
        }

        internal void Validate()
        {
            depthDistance = Mathf.Max(0.01f, depthDistance);
            refractionDepthFade = Mathf.Max(0.01f, refractionDepthFade);
            patternScale = Mathf.Max(0.05f, patternScale);
            patternBreakupScale = Mathf.Max(0.001f, patternBreakupScale);
            shoreFoamNoiseScale = Mathf.Max(0.001f, shoreFoamNoiseScale);
            shoreRippleSpacing = Mathf.Max(0.01f, shoreRippleSpacing);
            glintCellSize = Mathf.Max(0.05f, glintCellSize);
            waveLength = Mathf.Max(0.01f, waveLength);
        }
    }
}
