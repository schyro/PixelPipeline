using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Owns per-scene environment state. The shared ScriptableObject remains a pure profile while
    /// clock, cloud coverage and rain transitions live here and never leak between scenes.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PixelEnvironmentController : MonoBehaviour
    {
        [Header("Profile and Scene References")]
        [SerializeField] private PixelPipelineSettings settings;
        [SerializeField] private Light sunLight;
        [SerializeField] private Light moonLight;
        [SerializeField] private Camera targetCamera;

        [Header("Scene State")]
        [SerializeField] private bool timePassing = true;
        [Tooltip("The hour this scene starts at, and its clock while it runs. Taken from the profile's Start Time Hours when a profile is assigned; after that the scene keeps its own value.")]
        [SerializeField, Range(0f, 24f)] private float currentTimeHours = 10f;
        [Tooltip("The weather this scene starts in. Taken from the profile's Initial Weather when a profile is assigned; after that the scene keeps its own value.")]
        [SerializeField] private PixelWeatherState targetWeather = PixelWeatherState.Sunny;
        // The profile the two values above were last taken from, so they are only replaced when a
        // different profile is assigned and never on an ordinary reload.
        [SerializeField, HideInInspector] private PixelPipelineSettings stateProfile;

        private static PixelEnvironmentController _active;
        private float _cloudCoverage;
        private float _rainAmount;
        private float _daylight = 1f;
        private bool _runtimeInitialised;
        private PixelEnvironmentRuntimeState _state;
        private Vector3 _rainCentre;

        public float CurrentTimeHours => currentTimeHours;
        public PixelWeatherState TargetWeather => targetWeather;
        public float CloudCoverage => _cloudCoverage;
        public float RainAmount => _rainAmount;
        public PixelPipelineSettings Profile => settings;
        public Light SunLight => sunLight;
        public Camera TargetCamera => targetCamera;
        public bool TimePassing
        {
            get => timePassing;
            set => timePassing = value;
        }

        private void OnEnable()
        {
            if (_active != null && _active != this)
                Debug.LogWarning("[PixelPipeline] Multiple environment controllers are active. The latest one will drive rendering.", this);

            _active = this;
            InitialiseRuntimeState();
            Tick(0f, false);
        }

        private void OnDisable()
        {
            if (_active == this)
                _active = null;
        }

        private void OnValidate()
        {
            if (settings == stateProfile)
                return;

            TakeStateFromProfile();
            _runtimeInitialised = false;
        }

        private void Update()
        {
            if (settings == null)
                return;

            if (!_runtimeInitialised)
                InitialiseRuntimeState();

            float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;
            Tick(deltaTime, Application.isPlaying);
        }

        public void SetWeather(PixelWeatherState weather, bool immediate = false)
        {
            targetWeather = weather;
            if (settings == null)
                return;

            if (immediate)
            {
                _cloudCoverage = settings.Environment.Weather.GetCoverage(weather);
                _rainAmount = weather == PixelWeatherState.Rain ? 1f : 0f;
                Tick(0f, false);
            }
        }

        public void SetTime(float hour)
        {
            currentTimeHours = Mathf.Repeat(hour, 24f);
            Tick(0f, false);
        }

        [ContextMenu("Weather/Sunny")]
        private void SetSunnyFromMenu() => SetWeather(PixelWeatherState.Sunny);

        [ContextMenu("Weather/Cloudy")]
        private void SetCloudyFromMenu() => SetWeather(PixelWeatherState.Cloudy);

        [ContextMenu("Weather/Rain")]
        private void SetRainFromMenu() => SetWeather(PixelWeatherState.Rain);

        [ContextMenu("Reset To Profile")]
        private void ResetToProfile()
        {
            TakeStateFromProfile();
            InitialiseRuntimeState();
            Tick(0f, false);
        }

        private void TakeStateFromProfile()
        {
            stateProfile = settings;
            if (settings == null)
                return;

            currentTimeHours = settings.Environment.DayNight.StartTimeHours;
            targetWeather = settings.Environment.Weather.InitialWeather;
        }

        internal static PixelEnvironmentRuntimeState ResolveState(PixelPipelineSettings profile)
        {
            if (_active != null && _active.isActiveAndEnabled && _active.settings == profile)
                return _active._state;

            PixelWeatherSettings weather = profile.Environment.Weather;
            float coverage = weather.GetCoverage(weather.InitialWeather);
            float rain = weather.InitialWeather == PixelWeatherState.Rain ? 1f : 0f;
            PixelWeatherLighting lighting = weather.EvaluateLighting(coverage, rain);
            return new PixelEnvironmentRuntimeState(coverage, lighting.GodRays, 1f);
        }

        /// <summary>Rain is only drawn for the active controller's target camera.</summary>
        internal static bool IsRainCamera(Camera camera)
        {
            return _active != null && camera != null && _active.targetCamera == camera;
        }

        /// <summary>
        /// Where rain is falling for the active controller: the point on the ground the rain area is
        /// centred on, and the direction the drops travel. False when it is not raining.
        /// </summary>
        internal static bool TryResolveRain(
            PixelPipelineSettings profile,
            out Vector3 centre,
            out Vector3 fallDirection,
            out float rainAmount)
        {
            centre = Vector3.zero;
            fallDirection = Vector3.down;
            rainAmount = 0f;
            if (_active == null || !_active.isActiveAndEnabled || _active.settings != profile)
                return false;

            rainAmount = _active._rainAmount;
            if (rainAmount <= 0.001f)
                return false;

            PixelWindSettings wind = profile.Environment.Wind;
            Vector2 direction = wind.NormalizedDirection;
            float horizontalSpeed = wind.RainDriftSpeed * wind.Strength;
            float fallSpeed = profile.Environment.Rain.AverageSpeed;

            centre = _active._rainCentre;
            fallDirection = new Vector3(
                direction.x * horizontalSpeed,
                -Mathf.Max(0.01f, fallSpeed),
                direction.y * horizontalSpeed).normalized;
            return true;
        }

        private void InitialiseRuntimeState()
        {
            _runtimeInitialised = true;
            if (settings == null)
                return;

            // The clock and the weather are the scene's own serialised values; only what follows
            // from them is rebuilt here.
            _cloudCoverage = settings.Environment.Weather.GetCoverage(targetWeather);
            _rainAmount = targetWeather == PixelWeatherState.Rain ? 1f : 0f;
        }

        private void Tick(float deltaTime, bool allowTransitions)
        {
            if (settings == null)
                return;

            settings.ApplyWindGlobals();
            PixelEnvironmentSettings environment = settings.Environment;
            PixelDayNightSettings dayNight = environment.DayNight;

            if (allowTransitions && dayNight.Enabled && dayNight.AdvanceInPlayMode && timePassing)
            {
                currentTimeHours = Mathf.Repeat(
                    currentTimeHours + 24f * deltaTime / dayNight.DayDurationInSeconds,
                    24f);
            }

            UpdateWeather(environment.Weather, deltaTime, allowTransitions);
            ApplyLighting(dayNight, environment.Weather);
            _rainCentre = ResolveRainCentre();
        }

        private void UpdateWeather(PixelWeatherSettings weather, float deltaTime, bool allowTransitions)
        {
            float targetCoverage = weather.GetCoverage(targetWeather);
            if (!allowTransitions)
            {
                _cloudCoverage = targetCoverage;
                _rainAmount = targetWeather == PixelWeatherState.Rain ? 1f : 0f;
                return;
            }

            if (targetWeather == PixelWeatherState.Rain)
            {
                _cloudCoverage = MoveOverDuration(
                    _cloudCoverage,
                    targetCoverage,
                    weather.CloudBuildSeconds,
                    deltaTime);

                float desiredRain = Mathf.InverseLerp(
                    weather.RainStartCloudCoverage,
                    weather.RainCloudCoverage,
                    _cloudCoverage);
                _rainAmount = MoveOverDuration(_rainAmount, desiredRain, weather.RainFadeInSeconds, deltaTime);
                return;
            }

            // Rain stops first. Only then do the clouds start to disperse, avoiding an abrupt clear
            // sky while drops are still falling.
            _rainAmount = MoveOverDuration(_rainAmount, 0f, weather.RainFadeOutSeconds, deltaTime);
            if (_rainAmount > 0.001f)
                return;

            float duration = targetCoverage < _cloudCoverage
                ? weather.CloudDisperseSeconds
                : weather.CloudBuildSeconds;
            _cloudCoverage = MoveOverDuration(_cloudCoverage, targetCoverage, duration, deltaTime);
        }

        private void ApplyLighting(PixelDayNightSettings dayNight, PixelWeatherSettings weather)
        {
            if (!dayNight.Enabled)
            {
                PixelWeatherLighting disabledLighting = weather.EvaluateLighting(_cloudCoverage, _rainAmount);
                _daylight = 1f;
                _state = CreateState(disabledLighting);
                return;
            }

            float sunAngle = dayNight.GetSunOrbitAngle(currentTimeHours);
            Quaternion orbitAxis = Quaternion.Euler(0f, dayNight.OrbitAxisY, 0f);

            if (sunLight != null)
                sunLight.transform.rotation = orbitAxis * Quaternion.AngleAxis(sunAngle, Vector3.right);
            if (moonLight != null)
                moonLight.transform.rotation = orbitAxis * Quaternion.AngleAxis(sunAngle + 180f, Vector3.right);

            float signedSunHeight = sunLight != null
                ? Vector3.Dot(sunLight.transform.forward, Vector3.down)
                : Mathf.Sin((currentTimeHours - 6f) / 24f * Mathf.PI * 2f);
            float sunHeight = Mathf.Clamp01(signedSunHeight);
            float moonHeight = moonLight != null
                ? Mathf.Clamp01(Vector3.Dot(moonLight.transform.forward, Vector3.down))
                : Mathf.Clamp01(-signedSunHeight);

            _daylight = PixelMath.SmoothRange(-0.08f, 0.22f, signedSunHeight);
            PixelWeatherLighting lighting = weather.EvaluateLighting(_cloudCoverage, _rainAmount);

            if (sunLight != null)
            {
                sunLight.intensity = dayNight.MaxSunIntensity * sunHeight * lighting.Sun;
                sunLight.color = Color.Lerp(dayNight.SunHorizonColor, dayNight.SunDayColor, sunHeight);
                sunLight.shadowStrength = dayNight.ShadowStrength;
                sunLight.shadows = sunLight.intensity > 0.005f ? LightShadows.Hard : LightShadows.None;
            }

            if (moonLight != null)
            {
                moonLight.intensity = dayNight.MaxMoonIntensity * moonHeight * Mathf.Lerp(1f, 0.7f, 1f - lighting.Ambient);
                moonLight.color = Color.Lerp(dayNight.MoonHorizonColor, dayNight.MoonZenithColor, moonHeight);
                moonLight.shadowStrength = dayNight.ShadowStrength * 0.55f;
                moonLight.shadows = moonLight.intensity > 0.005f ? LightShadows.Hard : LightShadows.None;
            }

            RenderSettings.sun = sunLight != null && sunLight.intensity >= (moonLight != null ? moonLight.intensity : 0f)
                ? sunLight
                : moonLight;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(dayNight.NightSkyColor, dayNight.DaySkyColor, _daylight)
                                             * lighting.Ambient;
            RenderSettings.ambientEquatorColor = Color.Lerp(
                                                    dayNight.NightEquatorColor,
                                                    dayNight.DayEquatorColor,
                                                    _daylight)
                                                * lighting.Ambient;
            RenderSettings.ambientGroundColor = Color.Lerp(
                                                   dayNight.NightGroundColor,
                                                   dayNight.DayGroundColor,
                                                   _daylight)
                                               * lighting.Ambient;

            if (targetCamera != null)
            {
                targetCamera.backgroundColor = Color.Lerp(
                    dayNight.NightCameraColor,
                    dayNight.DayCameraColor,
                    _daylight * lighting.Ambient);
            }

            _state = CreateState(lighting);
        }

        private PixelEnvironmentRuntimeState CreateState(PixelWeatherLighting lighting)
        {
            return new PixelEnvironmentRuntimeState(_cloudCoverage, lighting.GodRays, _daylight);
        }

        private Vector3 ResolveRainCentre()
        {
            float groundHeight = settings.Environment.GroundHeight;
            if (PixelMath.TryGetGroundFocus(targetCamera, groundHeight, out Vector3 centre))
                return centre;

            return new Vector3(transform.position.x, groundHeight, transform.position.z);
        }

        private static float MoveOverDuration(float current, float target, float duration, float deltaTime)
        {
            return Mathf.MoveTowards(current, target, deltaTime / Mathf.Max(0.1f, duration));
        }
    }
}
