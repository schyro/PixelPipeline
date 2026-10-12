using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PixelPipeline.Runtime
{
    public enum PixelWeatherState
    {
        Sunny,
        Cloudy,
        Rain
    }

    [Serializable]
    public sealed class PixelEnvironmentSettings
    {
        [Header("Scene")]
        [Tooltip("World height of the ground the scene stands on. Shared by the god rays, the wind streaks, and the rain, which all centre on the point where the camera looks at this height.")]
        [SerializeField] private float groundHeight;

        [SerializeField] private PixelWindSettings wind = new();
        [SerializeField] private PixelDayNightSettings dayNight = new();
        [SerializeField] private PixelWeatherSettings weather = new();
        [SerializeField] private PixelCloudSettings clouds = new();
        [SerializeField] private PixelGodRaySettings godRays = new();
        [SerializeField] private PixelWindStreakSettings windStreaks = new();
        [SerializeField, FormerlySerializedAs("rainImpacts")] private PixelRainSettings rain = new();

        public float GroundHeight => groundHeight;
        public PixelWindSettings Wind => wind ??= new PixelWindSettings();
        public PixelDayNightSettings DayNight => dayNight ??= new PixelDayNightSettings();
        public PixelWeatherSettings Weather => weather ??= new PixelWeatherSettings();
        public PixelCloudSettings Clouds => clouds ??= new PixelCloudSettings();
        public PixelGodRaySettings GodRays => godRays ??= new PixelGodRaySettings();
        public PixelWindStreakSettings WindStreaks => windStreaks ??= new PixelWindStreakSettings();
        public PixelRainSettings Rain => rain ??= new PixelRainSettings();

        internal void Validate()
        {
            Wind.Validate();
            DayNight.Validate();
            Weather.Validate();
            Clouds.Validate();
            GodRays.Validate(groundHeight);
            WindStreaks.Validate();
            Rain.Validate();
        }
    }

    [Serializable]
    public sealed class PixelDayNightSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool advanceInPlayMode = true;
        [SerializeField, Range(0f, 24f)] private float startTimeHours = 10f;
        [SerializeField, Min(1f)] private float dayDurationInSeconds = 180f;
        [SerializeField, Range(0f, 24f)] private float sunriseTime = 6f;
        [SerializeField, Range(0f, 24f)] private float sunsetTime = 18f;
        [SerializeField] private float orbitAxisY = -32f;
        [SerializeField, Min(0f)] private float maxSunIntensity = 1.25f;
        [SerializeField, Min(0f)] private float maxMoonIntensity = 0.18f;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.72f;

        [Header("Light Colours")]
        [SerializeField, ColorUsage(false, true)] private Color sunHorizonColor = new(1f, 0.48f, 0.25f);
        [SerializeField, ColorUsage(false, true)] private Color sunDayColor = new(1f, 0.91f, 0.76f);
        [SerializeField, ColorUsage(false, true)] private Color moonHorizonColor = new(0.45f, 0.55f, 0.8f);
        [SerializeField, ColorUsage(false, true)] private Color moonZenithColor = new(0.68f, 0.76f, 1f);

        [Header("Ambient Colours")]
        [SerializeField] private Color daySkyColor = new(0.58f, 0.68f, 0.74f);
        [SerializeField] private Color dayEquatorColor = new(0.38f, 0.42f, 0.38f);
        [SerializeField] private Color dayGroundColor = new(0.20f, 0.22f, 0.18f);
        [SerializeField] private Color nightSkyColor = new(0.055f, 0.075f, 0.15f);
        [SerializeField] private Color nightEquatorColor = new(0.07f, 0.085f, 0.13f);
        [SerializeField] private Color nightGroundColor = new(0.025f, 0.03f, 0.055f);
        [SerializeField] private Color dayCameraColor = new(0.53f, 0.68f, 0.75f);
        [SerializeField] private Color nightCameraColor = new(0.025f, 0.04f, 0.095f);

        public bool Enabled => enabled;
        public bool AdvanceInPlayMode => advanceInPlayMode;
        public float StartTimeHours => startTimeHours;
        public float DayDurationInSeconds => dayDurationInSeconds;
        public float SunriseTime => sunriseTime;
        public float SunsetTime => sunsetTime;
        public float OrbitAxisY => orbitAxisY;
        public float MaxSunIntensity => maxSunIntensity;
        public float MaxMoonIntensity => maxMoonIntensity;
        public float ShadowStrength => shadowStrength;
        public Color SunHorizonColor => sunHorizonColor;
        public Color SunDayColor => sunDayColor;
        public Color MoonHorizonColor => moonHorizonColor;
        public Color MoonZenithColor => moonZenithColor;
        public Color DaySkyColor => daySkyColor;
        public Color DayEquatorColor => dayEquatorColor;
        public Color DayGroundColor => dayGroundColor;
        public Color NightSkyColor => nightSkyColor;
        public Color NightEquatorColor => nightEquatorColor;
        public Color NightGroundColor => nightGroundColor;
        public Color DayCameraColor => dayCameraColor;
        public Color NightCameraColor => nightCameraColor;

        /// <summary>
        /// Degrees the sun has travelled along its orbit: 0 at sunrise, 180 at sunset, and on to 360
        /// through the night. The sun's height above the horizon is the sine of this angle.
        /// </summary>
        internal float GetSunOrbitAngle(float hour)
        {
            bool isDay = hour >= sunriseTime && hour < sunsetTime;
            if (isDay)
            {
                float dayDuration = Mathf.Max(0.1f, sunsetTime - sunriseTime);
                return Mathf.Lerp(0f, 180f, (hour - sunriseTime) / dayDuration);
            }

            float nightDuration = Mathf.Max(0.1f, 24f - sunsetTime + sunriseTime);
            float elapsed = hour >= sunsetTime
                ? hour - sunsetTime
                : 24f - sunsetTime + hour;
            return Mathf.Lerp(180f, 360f, elapsed / nightDuration);
        }

        internal void Validate()
        {
            startTimeHours = Mathf.Repeat(startTimeHours, 24f);
            dayDurationInSeconds = Mathf.Max(1f, dayDurationInSeconds);
            sunriseTime = Mathf.Clamp(sunriseTime, 0f, 23.9f);
            sunsetTime = Mathf.Clamp(sunsetTime, sunriseTime + 0.1f, 24f);
            maxSunIntensity = Mathf.Max(0f, maxSunIntensity);
            maxMoonIntensity = Mathf.Max(0f, maxMoonIntensity);
        }
    }

    [Serializable]
    public sealed class PixelWeatherSettings
    {
        [SerializeField] private PixelWeatherState initialWeather = PixelWeatherState.Sunny;
        [SerializeField, Range(0f, 1f)] private float sunnyCloudCoverage = 0.15f;
        [SerializeField, Range(0f, 1f)] private float cloudyCloudCoverage = 0.58f;
        [SerializeField, Range(0f, 1f)] private float rainCloudCoverage = 0.902f;
        [SerializeField, Min(0.1f)] private float cloudBuildSeconds = 18f;
        [SerializeField, Min(0.1f)] private float cloudDisperseSeconds = 24f;
        [SerializeField, Range(0f, 1f)] private float rainStartCloudCoverage = 0.72f;
        [SerializeField, Min(0.1f)] private float rainFadeInSeconds = 5f;
        [SerializeField, Min(0.1f)] private float rainFadeOutSeconds = 4f;

        [Header("Lighting Multipliers")]
        [SerializeField, Range(0f, 1f)] private float sunnySunIntensity = 1f;
        [SerializeField, Range(0f, 1f)] private float cloudySunIntensity = 0.72f;
        [SerializeField, Range(0f, 1f)] private float rainSunIntensity = 0.42f;
        [SerializeField, Range(0f, 1f)] private float sunnyAmbientIntensity = 1f;
        [SerializeField, Range(0f, 1f)] private float cloudyAmbientIntensity = 0.88f;
        [SerializeField, Range(0f, 1f)] private float rainAmbientIntensity = 0.68f;
        [SerializeField, Range(0f, 2f)] private float sunnyGodRayIntensity = 0.15f;
        [SerializeField, Range(0f, 2f)] private float cloudyGodRayIntensity = 1f;
        [SerializeField, Range(0f, 2f)] private float rainGodRayIntensity = 0.25f;

        public PixelWeatherState InitialWeather => initialWeather;
        public float SunnyCloudCoverage => sunnyCloudCoverage;
        public float CloudyCloudCoverage => cloudyCloudCoverage;
        public float RainCloudCoverage => rainCloudCoverage;
        public float CloudBuildSeconds => cloudBuildSeconds;
        public float CloudDisperseSeconds => cloudDisperseSeconds;
        public float RainStartCloudCoverage => rainStartCloudCoverage;
        public float RainFadeInSeconds => rainFadeInSeconds;
        public float RainFadeOutSeconds => rainFadeOutSeconds;

        public float GetCoverage(PixelWeatherState state)
        {
            return state switch
            {
                PixelWeatherState.Cloudy => cloudyCloudCoverage,
                PixelWeatherState.Rain => rainCloudCoverage,
                _ => sunnyCloudCoverage
            };
        }

        internal PixelWeatherLighting EvaluateLighting(float coverage, float rainAmount)
        {
            float cloudRange = Mathf.Max(0.0001f, cloudyCloudCoverage - sunnyCloudCoverage);
            float rainyRange = Mathf.Max(0.0001f, rainCloudCoverage - cloudyCloudCoverage);
            float cloudBlend = Mathf.Clamp01((coverage - sunnyCloudCoverage) / cloudRange);
            float stormBlend = Mathf.Max(
                Mathf.Clamp01((coverage - cloudyCloudCoverage) / rainyRange),
                Mathf.Clamp01(rainAmount));

            float sun = Mathf.Lerp(sunnySunIntensity, cloudySunIntensity, cloudBlend);
            float ambient = Mathf.Lerp(sunnyAmbientIntensity, cloudyAmbientIntensity, cloudBlend);
            float godRays = Mathf.Lerp(sunnyGodRayIntensity, cloudyGodRayIntensity, cloudBlend);

            return new PixelWeatherLighting(
                Mathf.Lerp(sun, rainSunIntensity, stormBlend),
                Mathf.Lerp(ambient, rainAmbientIntensity, stormBlend),
                Mathf.Lerp(godRays, rainGodRayIntensity, stormBlend));
        }

        internal void Validate()
        {
            sunnyCloudCoverage = Mathf.Clamp01(sunnyCloudCoverage);
            cloudyCloudCoverage = Mathf.Clamp(cloudyCloudCoverage, sunnyCloudCoverage, 1f);
            rainCloudCoverage = Mathf.Clamp(rainCloudCoverage, cloudyCloudCoverage, 1f);
            cloudBuildSeconds = Mathf.Max(0.1f, cloudBuildSeconds);
            cloudDisperseSeconds = Mathf.Max(0.1f, cloudDisperseSeconds);
            rainStartCloudCoverage = Mathf.Clamp(rainStartCloudCoverage, cloudyCloudCoverage, rainCloudCoverage);
            rainFadeInSeconds = Mathf.Max(0.1f, rainFadeInSeconds);
            rainFadeOutSeconds = Mathf.Max(0.1f, rainFadeOutSeconds);
        }
    }

    [Serializable]
    public sealed class PixelCloudSettings
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private Texture2D noise;
        [SerializeField, Min(0.001f)] private float scale = 45f;
        [SerializeField] private float worldHeight = 20f;
        [SerializeField, Min(0f)] private float speed = 0.01f;
        [SerializeField, Min(0.01f)] private float contrast = 4f;
        [SerializeField, Range(-1f, 1f)] private float threshold = 0.485f;
        [SerializeField, Range(0f, 1f)] private float coverageInfluence = 0.479f;
        [SerializeField] private Vector3 projectionDirection = new(0.2f, -1f, 0.2f);
        [SerializeField, Range(0f, 1f)] private float shadowMinimum;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.773f;
        [SerializeField, Range(0f, 45f)] private float divergenceAngle = 10f;
        [SerializeField, Range(1f, 5f)] private float power = 3.5f;
        [Tooltip("Flattens the cloud shadow into this many brightness bands for the pixel look. Zero keeps the smooth gradient.")]
        [SerializeField, Range(0, 8)] private int shadowSteps = 4;

        public bool Enabled => enabled;
        public Texture2D Noise => noise;
        public float Scale => scale;
        public float WorldHeight => worldHeight;
        public float Speed => speed;
        public float Contrast => contrast;
        public float Threshold => threshold;
        public float CoverageInfluence => coverageInfluence;
        public Vector3 ProjectionDirection => projectionDirection;
        public float ShadowMinimum => shadowMinimum;
        public float ShadowStrength => shadowStrength;
        public float DivergenceAngle => divergenceAngle;
        public float Power => power;
        public int ShadowSteps => shadowSteps;

        internal void Validate()
        {
            scale = Mathf.Max(0.001f, scale);
            contrast = Mathf.Max(0.01f, contrast);
            speed = Mathf.Max(0f, speed);
            shadowSteps = Mathf.Clamp(shadowSteps, 0, 8);
        }
    }

    [Serializable]
    public sealed class PixelGodRaySettings
    {
        [Tooltip("Light shafts where the sun falls through gaps in the clouds. Needs cloud shadows, since the gaps are theirs.")]
        [SerializeField] private bool enabled = true;
        [Tooltip("Shaft colour. It is multiplied by the sun's own colour, so shafts warm up towards sunset.")]
        [SerializeField, ColorUsage(false, true)] private Color color = new(1f, 0.9f, 0.72f);
        [Tooltip("Overall brightness, before the weather's God Ray multiplier.")]
        [SerializeField, Range(0f, 2f)] private float intensity = 0.8f;

        [Header("Gaps")]
        [Tooltip("How wide the patch of sky is that a gap is judged against, in metres. A point is a gap when it is clear but this area around it is clouded, so open sky and solid cloud both give no shafts.")]
        [SerializeField, Min(0.5f)] private float gapSize = 7f;
        [Tooltip("How much clearer than its surroundings a point has to be to cast a shaft. Raise it to keep only the clearest holes.")]
        [SerializeField, Range(0f, 1f)] private float gapThreshold = 0.12f;
        [Tooltip("Range above the threshold over which a shaft fades in.")]
        [SerializeField, Range(0.01f, 1f)] private float gapSoftness = 0.25f;

        [Header("Volume")]
        [Tooltip("World height where the shafts begin. They end at the shared Ground Height.")]
        [SerializeField] private float topHeight = 14f;
        [Tooltip("Fraction of the shaft height over which the shafts fade out towards the top.")]
        [SerializeField, Range(0f, 1f)] private float topFade = 0.6f;
        [Tooltip("Height in metres over which the shafts fade in above Ground Height. On the ground the sunlit patch shows where a shaft lands; where there is no ground under a shaft, as beside a floating island, this keeps it from ending in mid-air at full strength.")]
        [SerializeField, Min(0f)] private float groundFade = 2f;
        [Tooltip("Height in metres over which the haze thins by about two thirds. Low values keep the shafts bright near the ground and faint above.")]
        [SerializeField, Min(0.1f)] private float hazeFalloff = 12f;
        [Tooltip("How quickly light builds up along a view ray through a shaft.")]
        [SerializeField, Min(0f)] private float density = 0.3f;
        [Tooltip("Radius around the point the camera looks at, in metres, beyond which the shafts fade out.")]
        [SerializeField, Min(1f)] private float areaRadius = 14f;
        [Tooltip("Lets scene geometry block the shafts, using the main light shadow map.")]
        [SerializeField] private bool occlusion = true;

        [Header("Pixel Look")]
        [Tooltip("Samples along each view ray. More steps give smoother shafts and cost more.")]
        [SerializeField, Range(4, 48)] private int steps = 20;
        [Tooltip("Number of flat brightness tones. Zero keeps a smooth ramp.")]
        [SerializeField, Range(0, 8)] private int bands = 3;
        [Tooltip("Ordered dither between the tones and between march steps, on the internal pixel grid.")]
        [SerializeField, Range(0f, 1f)] private float dither = 0.35f;

        [Header("Sun")]
        [Tooltip("Sun height (the up component of its direction) below which there are no shafts.")]
        [SerializeField, Range(0.01f, 0.9f)] private float minimumSunHeight = 0.2f;
        [Tooltip("Sun height range above the minimum over which the shafts fade in.")]
        [SerializeField, Range(0.01f, 0.5f)] private float sunFadeRange = 0.15f;

        public bool Enabled => enabled;
        public Color Color => color;
        public float Intensity => intensity;
        public float GapSize => gapSize;
        public float GapThreshold => gapThreshold;
        public float GapSoftness => gapSoftness;
        public float TopHeight => topHeight;
        public float TopFade => topFade;
        public float GroundFade => groundFade;
        public float HazeFalloff => hazeFalloff;
        public float Density => density;
        public float AreaRadius => areaRadius;
        public bool Occlusion => occlusion;
        public int Steps => steps;
        public int Bands => bands;
        public float Dither => dither;
        public float MinimumSunHeight => minimumSunHeight;
        public float SunFadeRange => sunFadeRange;

        // Finest cloud detail a shaft keeps, in metres. Finer detail changes from one march step to
        // the next and breaks a shaft into feathered slices.
        private const float DetailMetres = 1.5f;

        /// <summary>
        /// Mip levels of the cloud noise that average it over one gap (the clouds around a point)
        /// and over the finest detail a shaft keeps (the point itself).
        /// </summary>
        public void GetCloudLods(PixelCloudSettings clouds, out float gapLod, out float detailLod)
        {
            int width = clouds.Noise != null ? Mathf.Max(1, clouds.Noise.width) : 1;
            float texelMetres = Mathf.Max(0.0001f, clouds.Scale / width);
            gapLod = Mathf.Log(Mathf.Max(texelMetres, gapSize) / texelMetres, 2f);
            detailLod = Mathf.Log(Mathf.Max(texelMetres, DetailMetres) / texelMetres, 2f);
        }

        /// <summary>0 below the minimum sun height, rising to 1 over the fade range.</summary>
        public float EvaluateSunFade(float sunHeight)
        {
            return PixelMath.SmoothRange(minimumSunHeight, minimumSunHeight + sunFadeRange, sunHeight);
        }

        internal void Validate(float groundHeight)
        {
            gapSize = Mathf.Max(0.5f, gapSize);
            topHeight = Mathf.Max(groundHeight + 0.5f, topHeight);
            groundFade = Mathf.Max(0f, groundFade);
            hazeFalloff = Mathf.Max(0.1f, hazeFalloff);
            density = Mathf.Max(0f, density);
            areaRadius = Mathf.Max(1f, areaRadius);
            steps = Mathf.Clamp(steps, 4, 48);
            bands = Mathf.Clamp(bands, 0, 8);
        }
    }

    [Serializable]
    public sealed class PixelWindSettings
    {
        [Tooltip("World-space X/Z direction the wind travels toward; shared by clouds, grass, rain, and water.")]
        [SerializeField] private Vector2 direction = new(1f, 0.35f);
        [Tooltip("Master multiplier for every wind-driven module. Zero means calm weather.")]
        [SerializeField, Range(0f, 2f)] private float strength = 1.5f;
        [Tooltip("Horizontal rain velocity in metres per second at Strength 1.")]
        [SerializeField, Min(0f)] private float rainDriftSpeed = 3.5f;

        public Vector2 Direction => direction;
        public Vector2 NormalizedDirection => direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;
        public float Strength => strength;
        public float RainDriftSpeed => rainDriftSpeed;

        internal void Validate()
        {
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector2.right;

            strength = Mathf.Max(0f, strength);
            rainDriftSpeed = Mathf.Max(0f, rainDriftSpeed);
        }
    }

    [Serializable]
    public sealed class PixelWindStreakSettings
    {
        [Header("Rendering")]
        [Tooltip("Draws the stylised wind streaks. Turn this off to remove the instanced draw entirely.")]
        [SerializeField] private bool enabled = true;
        [Tooltip("Material using PixelPipeline/WindStreak. Its blend mode is the only value not driven from this profile.")]
        [SerializeField] private Material material;
        [Tooltip("Size of the streak pool. One instanced draw covers at most 1023 streaks.")]
        [SerializeField, Range(0, 1023)] private int maximumStreaks = 12;
        [Tooltip("How strongly the live streak count follows Wind Strength. Zero always draws the full pool.")]
        [SerializeField, Range(0f, 1f)] private float countWindResponse = 0.75f;

        [Header("Volume")]
        [Tooltip("Keeps the spawn volume centred on what the camera looks at, so streaks never run out ahead of the view.")]
        [SerializeField] private bool followCamera = true;
        [Tooltip("World-space X/Z footprint the streaks spawn inside.")]
        [SerializeField] private Vector2 areaSize = new(26f, 26f);
        [Tooltip("Lowest and highest spawn height above Ground Height.")]
        [SerializeField] private Vector2 heightRange = new(0.35f, 4.5f);

        [Header("Motion")]
        [Tooltip("Travel speed in metres per second at Wind Strength 1.")]
        [SerializeField, Min(0f)] private float speed = 5f;
        [Tooltip("Random per-streak speed spread, as a fraction of Speed.")]
        [SerializeField, Range(0f, 1f)] private float speedVariance = 0.35f;
        [SerializeField, Min(0.05f)] private float lifetimeMinimum = 2.2f;
        [SerializeField, Min(0.05f)] private float lifetimeMaximum = 3.2f;
        [Tooltip("Fraction of the lifetime spent fading in at birth and out at death.")]
        [SerializeField, Range(0.01f, 0.5f)] private float lifeFade = 0.5f;
        [Tooltip("Wind Strength below which no streak is drawn.")]
        [SerializeField, Range(0f, 2f)] private float minimumWindStrength;
        [Tooltip("Wind Strength range above the minimum over which the streaks fade in.")]
        [SerializeField, Range(0.01f, 2f)] private float windFadeRange = 0.35f;

        [Header("Shape")]
        [SerializeField, Min(0.05f)] private float lengthMinimum = 1.3f;
        [SerializeField, Min(0.05f)] private float lengthMaximum = 8f;
        [SerializeField, Min(0.001f)] private float widthMinimum = 0.035f;
        [SerializeField, Min(0.001f)] private float widthMaximum = 0.08f;
        [Tooltip("Width at the tail relative to the head, which is what makes a streak read as a trail.")]
        [SerializeField, Range(0f, 1f)] private float tailWidthScale = 0.15f;
        [Tooltip("Ribbon subdivisions along the streak. More segments means a smoother curve.")]
        [SerializeField, Range(2, 32)] private int segments = 10;

        [Header("Curve")]
        [Tooltip("Sideways offset in metres the trail bends away from the straight wind line.")]
        [SerializeField, Min(0f)] private float curveAmplitude = 0.22f;
        [Tooltip("Waves per streak length. Below 1 the streak is a single gentle arc.")]
        [SerializeField, Range(0.05f, 3f)] private float curveFrequency = 0.38f;
        [Tooltip("How fast the curve shape travels along the streak over time.")]
        [SerializeField] private float curveScrollSpeed = 0.35f;
        [Tooltip("How much of the curve is applied vertically instead of horizontally.")]
        [SerializeField, Range(0f, 1f)] private float curveVertical = 0.44f;

        [Header("Appearance")]
        [SerializeField, ColorUsage(true, true)] private Color color = new(0.56079084f, 0.6201687f, 0.6597539f, 0.55f);
        [SerializeField, Range(0f, 4f)] private float intensity = 1f;
        [Tooltip("Fraction of the streak spent sharpening into the leading tip.")]
        [SerializeField, Range(0f, 0.5f)] private float headFade = 0.06f;
        [Tooltip("Fraction of the streak spent fading out towards the tail.")]
        [SerializeField, Range(0f, 1f)] private float tailFade = 0.6f;
        [Tooltip("Softness across the ribbon width. Zero keeps a hard pixel edge.")]
        [SerializeField, Range(0f, 1f)] private float edgeSoftness = 1f;
        [Tooltip("Quantises alpha into this many steps for a banded pixel look. Zero keeps a smooth ramp.")]
        [SerializeField, Range(0, 8)] private int alphaSteps;
        [Tooltip("How far the streaks follow the light around them. At 1 the colour is scaled by the sun or moon and the sky ambient, capped at full daylight, so the streaks dim at night and in storms. At 0 they keep Color as set.")]
        [SerializeField, Range(0f, 1f)] private float lightResponse = 1f;

        public bool Enabled => enabled;
        public Material Material => material;
        public int MaximumStreaks => maximumStreaks;
        public float CountWindResponse => countWindResponse;
        public bool FollowCamera => followCamera;
        public Vector2 AreaSize => areaSize;
        public Vector2 HeightRange => heightRange;
        public float Speed => speed;
        public float SpeedVariance => speedVariance;
        public float LifetimeMinimum => lifetimeMinimum;
        public float LifetimeMaximum => lifetimeMaximum;
        public float LifeFade => lifeFade;
        public float MinimumWindStrength => minimumWindStrength;
        public float WindFadeRange => windFadeRange;
        public float LengthMinimum => lengthMinimum;
        public float LengthMaximum => lengthMaximum;
        public float WidthMinimum => widthMinimum;
        public float WidthMaximum => widthMaximum;
        public float TailWidthScale => tailWidthScale;
        public int Segments => segments;
        public float CurveAmplitude => curveAmplitude;
        public float CurveFrequency => curveFrequency;
        public float CurveScrollSpeed => curveScrollSpeed;
        public float CurveVertical => curveVertical;
        public Color Color => color;
        public float Intensity => intensity;
        public float HeadFade => headFade;
        public float TailFade => tailFade;
        public float EdgeSoftness => edgeSoftness;
        public int AlphaSteps => alphaSteps;
        public float LightResponse => lightResponse;

        /// <summary>Visibility at the given wind strength; zero once the weather is too calm.</summary>
        public float EvaluateWindResponse(float windStrength)
        {
            float t = Mathf.Clamp01((windStrength - minimumWindStrength) / Mathf.Max(0.01f, windFadeRange));
            return t * t * (3f - 2f * t);
        }

        internal void Validate()
        {
            maximumStreaks = Mathf.Clamp(maximumStreaks, 0, 1023);
            areaSize.x = Mathf.Max(0.25f, areaSize.x);
            areaSize.y = Mathf.Max(0.25f, areaSize.y);
            heightRange.y = Mathf.Max(heightRange.x, heightRange.y);
            speed = Mathf.Max(0f, speed);
            lifetimeMinimum = Mathf.Max(0.05f, lifetimeMinimum);
            lifetimeMaximum = Mathf.Max(lifetimeMinimum, lifetimeMaximum);
            lengthMinimum = Mathf.Max(0.05f, lengthMinimum);
            lengthMaximum = Mathf.Max(lengthMinimum, lengthMaximum);
            widthMinimum = Mathf.Max(0.001f, widthMinimum);
            widthMaximum = Mathf.Max(widthMinimum, widthMaximum);
            curveAmplitude = Mathf.Max(0f, curveAmplitude);
            segments = Mathf.Clamp(segments, 2, 32);
            alphaSteps = Mathf.Clamp(alphaSteps, 0, 8);
        }
    }

    [Serializable]
    public sealed class PixelRainSettings
    {
        [Header("Drops")]
        [Tooltip("Material using PixelPipeline/PixelRain.")]
        [SerializeField] private Material dropMaterial;
        [Tooltip("Drops landing per second across the rain area at full rain. Every drop that lands on a surface splashes there.")]
        [SerializeField, Min(0f)] private float dropsPerSecond = 360f;
        [Tooltip("Height in metres above its landing point at which a drop appears.")]
        [SerializeField, Min(1f)] private float fallHeight = 12f;
        [Tooltip("Falling speed in metres per second. Wind > Rain Drift Speed adds the sideways part.")]
        [SerializeField, Min(0.1f)] private float speedMinimum = 10f;
        [SerializeField, Min(0.1f)] private float speedMaximum = 13f;
        [Tooltip("Width of a drop in metres.")]
        [SerializeField, Min(0.001f)] private float widthMinimum = 0.035f;
        [SerializeField, Min(0.001f)] private float widthMaximum = 0.055f;
        [Tooltip("Length of the streak in metres at the average speed; faster drops streak further.")]
        [SerializeField, Min(0.01f)] private float length = 0.95f;
        [Tooltip("How much drops and splashes follow the light in the scene. At 1 they darken with the night the way lit surfaces do; at 0 they keep their colour.")]
        [SerializeField, Range(0f, 1f)] private float lightResponse = 1f;

        [Header("Area")]
        [Tooltip("Width in metres of the square around the point the camera looks at that rain falls in.")]
        [SerializeField, Min(1f), FormerlySerializedAs("occlusionAreaSize")] private float areaSize = 24f;

        [Header("Impacts")]
        [Tooltip("Stops drops on the first surface they meet - roofs, tree tops, water - and splashes them there. Off lets them fall to the shared Ground Height and makes no splashes.")]
        [SerializeField, FormerlySerializedAs("enabled")] private bool impacts = true;
        [Tooltip("Resolution of the depth map rendered along the rain direction. One texel covers Area Size / Resolution metres.")]
        [SerializeField, Range(128, 2048)] private int occlusionResolution = 512;
        [Tooltip("Depth range in metres along the rain direction. Everything the rain can hit has to fit inside it.")]
        [SerializeField, Min(1f)] private float occlusionDepthRange = 40f;
        [Tooltip("Layers that stop rain. Objects also need a DepthOnly pass, which every URP Lit, grass, foliage, and water material has.")]
        [SerializeField] private LayerMask occlusionLayers = ~0;

        [Header("Splashes")]
        [Tooltip("Material using PixelPipeline/RainSplash.")]
        [SerializeField] private Material splashMaterial;
        [Tooltip("Seconds one splash animation lasts.")]
        [SerializeField, Min(0.05f)] private float splashLifetime = 0.3f;
        [SerializeField, Min(0.01f)] private float splashSizeMinimum = 0.16f;
        [SerializeField, Min(0.01f)] private float splashSizeMaximum = 0.26f;
        [Tooltip("Width of the expanding ring, as a fraction of the splash size.")]
        [SerializeField, Range(0.02f, 0.4f)] private float ringWidth = 0.16f;
        [Tooltip("Surfaces facing up less than this (0 = wall, 1 = flat floor) get no splashes.")]
        [SerializeField, Range(0f, 1f)] private float minimumUpFacing = 0.55f;
        [SerializeField, ColorUsage(true, true)] private Color splashColor = new(0.86f, 0.94f, 1f, 0.9f);

        public Material DropMaterial => dropMaterial;
        public float DropsPerSecond => dropsPerSecond;
        public float FallHeight => fallHeight;
        public float SpeedMinimum => speedMinimum;
        public float SpeedMaximum => speedMaximum;
        public float AverageSpeed => (speedMinimum + speedMaximum) * 0.5f;
        public float WidthMinimum => widthMinimum;
        public float WidthMaximum => widthMaximum;
        public float Length => length;
        public float LightResponse => lightResponse;
        public float AreaSize => areaSize;
        public bool Impacts => impacts;
        public int OcclusionResolution => occlusionResolution;
        public float OcclusionDepthRange => occlusionDepthRange;
        public LayerMask OcclusionLayers => occlusionLayers;
        public Material SplashMaterial => splashMaterial;
        public float SplashLifetime => splashLifetime;
        public float SplashSizeMinimum => splashSizeMinimum;
        public float SplashSizeMaximum => splashSizeMaximum;
        public float RingWidth => ringWidth;
        public float MinimumUpFacing => minimumUpFacing;
        public Color SplashColor => splashColor;

        internal void Validate()
        {
            dropsPerSecond = Mathf.Max(0f, dropsPerSecond);
            fallHeight = Mathf.Max(1f, fallHeight);
            speedMinimum = Mathf.Max(0.1f, speedMinimum);
            speedMaximum = Mathf.Max(speedMinimum, speedMaximum);
            widthMinimum = Mathf.Max(0.001f, widthMinimum);
            widthMaximum = Mathf.Max(widthMinimum, widthMaximum);
            length = Mathf.Max(0.01f, length);
            areaSize = Mathf.Max(1f, areaSize);
            occlusionResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(occlusionResolution), 128, 2048);
            occlusionDepthRange = Mathf.Max(1f, occlusionDepthRange);
            splashLifetime = Mathf.Max(0.05f, splashLifetime);
            splashSizeMinimum = Mathf.Max(0.01f, splashSizeMinimum);
            splashSizeMaximum = Mathf.Max(splashSizeMinimum, splashSizeMaximum);
        }
    }

    internal readonly struct PixelWeatherLighting
    {
        public PixelWeatherLighting(float sun, float ambient, float godRays)
        {
            Sun = sun;
            Ambient = ambient;
            GodRays = godRays;
        }

        public float Sun { get; }
        public float Ambient { get; }
        public float GodRays { get; }
    }

    /// <summary>What the render side needs from the scene's environment controller.</summary>
    internal readonly struct PixelEnvironmentRuntimeState
    {
        public PixelEnvironmentRuntimeState(float cloudCoverage, float godRayMultiplier, float daylight)
        {
            CloudCoverage = cloudCoverage;
            GodRayStrength = godRayMultiplier * daylight;
        }

        public float CloudCoverage { get; }
        public float GodRayStrength { get; }
    }

    internal static class PixelMath
    {
        private static readonly Vector3[] UpDirection = { Vector3.up };
        private static readonly Color[] SkyAmbient = new Color[1];

        /// <summary>Smoothstep of <paramref name="value"/> between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public static float SmoothRange(float from, float to, float value)
        {
            float t = Mathf.InverseLerp(from, to, value);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Where the middle of the camera's view meets the horizontal plane at
        /// <paramref name="groundHeight"/>, so camera-following effects stay around what is on screen.
        /// False without a camera, or when it looks along or away from that plane.
        /// </summary>
        public static bool TryGetGroundFocus(Camera camera, float groundHeight, out Vector3 focus)
        {
            focus = default;
            if (camera == null)
                return false;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Mathf.Abs(ray.direction.y) <= 0.001f)
                return false;

            float distance = (groundHeight - ray.origin.y) / ray.direction.y;
            if (distance <= 0f)
                return false;

            focus = ray.GetPoint(distance);
            return true;
        }

        /// <summary>
        /// Tint for unlit effects so they follow the scene light instead of glowing as brightly at
        /// midnight as at noon: the light reaching a point from above, the sun or moon plus the sky
        /// ambient, capped at full daylight so daytime colours stay as set. Linear space.
        /// </summary>
        /// <param name="response">0 keeps white, 1 applies the full light.</param>
        public static Color SkyLightTint(float response)
        {
            RenderSettings.ambientProbe.Evaluate(UpDirection, SkyAmbient);
            Color light = SkyAmbient[0];
            Light sun = RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled)
                light += sun.color.linear * (sun.intensity * Mathf.Max(0f, -sun.transform.forward.y));

            return Color.Lerp(
                Color.white,
                new Color(Mathf.Min(1f, light.r), Mathf.Min(1f, light.g), Mathf.Min(1f, light.b)),
                response);
        }
    }
}
