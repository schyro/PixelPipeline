using System.Collections.Generic;
using PixelPipeline.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PixelPipeline.Editor
{
    /// <summary>
    /// A card at the top of a settings page that visualises what the values on it do. Refresh runs
    /// whenever the settings asset changes; animated previews also tick on their own while visible.
    /// The window calls <see cref="Release"/> when the page goes away; a preview outlives being
    /// detached, since docking the window moves it to another panel.
    /// </summary>
    internal abstract class SettingsPreview : VisualElement
    {
        private readonly List<Object> _ownedObjects = new();
        private IVisualElementScheduledItem _ticker;

        protected SettingsPreview(PixelPipelineSettings settings, SerializedObject serializedSettings, string title)
        {
            Settings = settings;
            SerializedSettings = serializedSettings;
            AddToClassList("pp-card");
            Add(CreateLabel(title, "pp-card__title"));
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
        }

        protected PixelPipelineSettings Settings { get; }
        protected SerializedObject SerializedSettings { get; }

        public abstract void Refresh();

        protected void Animate(long intervalMilliseconds)
        {
            _ticker = schedule.Execute(Refresh).Every(intervalMilliseconds);
        }

        protected T Own<T>(T asset) where T : Object
        {
            asset.hideFlags = HideFlags.HideAndDontSave;
            _ownedObjects.Add(asset);
            return asset;
        }

        protected SerializedProperty Property(string path) => SerializedSettings.FindProperty(path);

        protected Color ColorValue(string path) => Property(path).colorValue;

        protected float FloatValue(string path) => Property(path).floatValue;

        protected static Label CreateLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        protected Label AddCaption(string text)
        {
            Label caption = CreateLabel(text, "pp-card__caption");
            Add(caption);
            return caption;
        }

        /// <summary>A labelled one-texel-high image, stretched to the card width.</summary>
        protected Texture2D AddStrip(VisualElement parent, string label, int resolution, FilterMode filterMode)
        {
            var texture = Own(new Texture2D(resolution, 1, TextureFormat.RGBA32, false)
            {
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp
            });

            var row = new VisualElement();
            row.AddToClassList("pp-row");
            row.Add(CreateLabel(label, "pp-strip-label"));
            var image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("pp-strip");
            image.AddToClassList("pp-grow");
            row.Add(image);
            parent.Add(row);
            return texture;
        }

        protected static void AddAxis(VisualElement parent, params string[] labels)
        {
            var axis = new VisualElement();
            axis.AddToClassList("pp-axis");
            foreach (string text in labels)
                axis.Add(new Label(text));
            parent.Add(axis);
        }

        public void Release()
        {
            _ticker?.Pause();
            foreach (Object owned in _ownedObjects)
            {
                if (owned is RenderTexture renderTexture)
                    renderTexture.Release();
                Object.DestroyImmediate(owned);
            }

            _ownedObjects.Clear();
        }
    }

    /// <summary>Internal resolution for the current Game view, and how evenly it scales up.</summary>
    internal sealed class ResolutionPreview : SettingsPreview
    {
        private readonly Label _summary;
        private readonly Label _scale;

        public ResolutionPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "Game view")
        {
            _summary = new Label();
            _scale = CreateLabel(string.Empty, "pp-card__caption");
            Add(_summary);
            Add(_scale);
            // The Game view can be resized at any time; there is no event for it.
            Animate(500);
        }

        public override void Refresh()
        {
            Vector2 gameView = Handles.GetMainGameViewSize();
            Vector2Int resolution = Settings.GetInternalResolution(gameView.x / Mathf.Max(1f, gameView.y));
            float scale = gameView.y / resolution.y;

            _summary.text = $"{gameView.x:0} × {gameView.y:0} screen  →  {resolution.x} × {resolution.y} internal pixels";
            bool whole = Mathf.Abs(scale - Mathf.Round(scale)) < 0.01f;
            _scale.text = whole
                ? $"Each pixel covers exactly {Mathf.Round(scale):0} × {Mathf.Round(scale):0} screen pixels."
                : $"Each pixel covers {scale:0.00} screen pixels. The sharp upscale softens the seams of a " +
                  "non-integer scale by one screen pixel; a height that divides the screen evenly keeps them hard.";
        }
    }

    /// <summary>
    /// A sphere lit smoothly next to the same sphere with banded light, then URP's filtered shadow ramp
    /// next to the banded one the pixel shadows produce.
    /// </summary>
    internal sealed class LightingPreview : SettingsPreview
    {
        private const int SphereResolution = 48;
        private const int Resolution = 256;
        private static readonly Vector3 LightDirection = new Vector3(-0.55f, 0.6f, 0.58f).normalized;
        private static readonly Color Albedo = new(0.88f, 0.66f, 0.2f);

        private readonly Texture2D _smoothSphere;
        private readonly Texture2D _bandedSphere;
        private readonly Texture2D _filtered;
        private readonly Texture2D _banded;
        private readonly Color[] _spherePixels = new Color[SphereResolution * SphereResolution];
        private readonly Color[] _pixels = new Color[Resolution];

        public LightingPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "Light on a sphere, and a shadow edge from shadowed to lit")
        {
            var spheres = new VisualElement();
            spheres.AddToClassList("pp-row");
            spheres.style.marginBottom = 8;
            _smoothSphere = AddSphere(spheres, "Smooth");
            _bandedSphere = AddSphere(spheres, "Banded");
            Add(spheres);

            _filtered = AddStrip(this, "URP filtered", Resolution, FilterMode.Point);
            _banded = AddStrip(this, "Pixel shadows", Resolution, FilterMode.Point);
            AddCaption("Bands apply to every lit opaque material on a Deferred+ or Deferred renderer; pixel shadows apply on every path. " +
                       "The filtered ramp spans one shadow-map texel, which covers several pixels at typical zoom levels.");
        }

        public override void Refresh()
        {
            int bands = Settings.EnableLightBands ? Settings.LightBands : 0;
            float bandBias = Settings.LightBandBias;
            FillSphere(_smoothSphere, 0, 0f);
            FillSphere(_bandedSphere, bands, bandBias);

            Fill(_filtered, attenuation => attenuation);
            int steps = Settings.EnablePixelShadows ? Settings.ShadowSteps : 0;
            float bias = Settings.ShadowEdgeBias;
            Fill(_banded, attenuation => Band(attenuation, steps, bias));
        }

        // PixelPipelineQuantizeShadow in PixelShadows.hlsl and PixelPipelineBandLight in PixelLighting.hlsl.
        private static float Band(float amount, int bands, float bias)
        {
            amount = Mathf.Clamp01(amount);
            return bands < 1 ? amount : Mathf.Clamp01(Mathf.Floor(amount * bands + 0.5f + bias) / bands);
        }

        private Texture2D AddSphere(VisualElement parent, string label)
        {
            var texture = Own(new Texture2D(SphereResolution, SphereResolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            });

            var column = new VisualElement { style = { alignItems = Align.Center, marginRight = 16 } };
            column.Add(new Image { image = texture, style = { width = 120, height = 120 } });
            column.Add(CreateLabel(label, "pp-bar-label"));
            parent.Add(column);
            return texture;
        }

        /// <summary>The banded lighting, with the daytime sun and sky of the profile.</summary>
        private void FillSphere(Texture2D texture, int bands, float bias)
        {
            PixelDayNightSettings dayNight = Settings.Environment.DayNight;
            // Lit in linear space, as the shader is, then shown in gamma space.
            Color sun = dayNight.SunDayColor.linear * dayNight.MaxSunIntensity;
            Color albedo = Albedo.linear;

            for (int y = 0; y < SphereResolution; y++)
            for (int x = 0; x < SphereResolution; x++)
            {
                float u = (x + 0.5f) / SphereResolution * 2f - 1f;
                float v = (y + 0.5f) / SphereResolution * 2f - 1f;
                float radial = u * u + v * v;
                if (radial > 1f)
                {
                    _spherePixels[y * SphereResolution + x] = Color.clear;
                    continue;
                }

                var normal = new Vector3(u, v, Mathf.Sqrt(1f - radial));
                // PixelPipelineAmbient: ground, horizon, or sky once banded.
                float height = bands < 1 ? normal.y : Mathf.Round(normal.y);
                float direct = Band(Vector3.Dot(normal, LightDirection), bands, bias);
                Color light = Ambient(dayNight, height).linear + sun * direct;
                Color color = (albedo * light).gamma;
                color.a = 1f;
                _spherePixels[y * SphereResolution + x] = color;
            }

            texture.SetPixels(_spherePixels);
            texture.Apply(false);
        }

        // Trilight ambient: ground colour below, equator at the horizon, sky above.
        private static Color Ambient(PixelDayNightSettings dayNight, float height)
        {
            return height >= 0f
                ? Color.Lerp(dayNight.DayEquatorColor, dayNight.DaySkyColor, height)
                : Color.Lerp(dayNight.DayEquatorColor, dayNight.DayGroundColor, -height);
        }

        private void Fill(Texture2D texture, System.Func<float, float> evaluate)
        {
            Color shadowed = new(0.16f, 0.2f, 0.26f);
            Color lit = new(0.93f, 0.86f, 0.7f);
            for (int index = 0; index < Resolution; index++)
                _pixels[index] = Color.Lerp(shadowed, lit, evaluate(index / (Resolution - 1f)));
            texture.SetPixels(_pixels);
            texture.Apply(false);
        }
    }

    /// <summary>Wind direction on a compass seen from above, with the resulting rain slant.</summary>
    internal sealed class WindCompassPreview : SettingsPreview
    {
        private readonly VisualElement _compass;
        private readonly Label _details;

        public WindCompassPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "Wind, seen from above")
        {
            var row = new VisualElement();
            row.AddToClassList("pp-row");
            _compass = new VisualElement { style = { width = 110, height = 110, marginRight = 12 } };
            _compass.generateVisualContent += DrawCompass;
            _details = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            _details.AddToClassList("pp-grow");
            row.Add(_compass);
            row.Add(_details);
            Add(row);
        }

        public override void Refresh()
        {
            PixelWindSettings wind = Settings.Environment.Wind;
            Vector2 direction = wind.NormalizedDirection;
            float heading = Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);
            float drift = wind.RainDriftSpeed * wind.Strength;
            float fall = Settings.Environment.Rain.AverageSpeed;
            float slant = Mathf.Atan2(drift, Mathf.Max(0.01f, fall)) * Mathf.Rad2Deg;

            _details.text =
                $"Blowing towards {heading:0}° (0° = +Z, 90° = +X)\n" +
                $"Strength {wind.Strength:0.00}{(wind.Strength <= 0.001f ? "  (calm)" : string.Empty)}\n" +
                $"Rain falls {slant:0}° off vertical\n" +
                "Drives clouds, grass, foliage, rain, water, and the wind streaks.";
            _compass.MarkDirtyRepaint();
        }

        private void DrawCompass(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            Rect rect = _compass.contentRect;
            Vector2 centre = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 4f;

            painter.lineWidth = 1.5f;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.25f);
            painter.BeginPath();
            painter.Arc(centre, radius, 0f, 360f);
            painter.Stroke();

            // +Z points up the preview, +X to the right, as in a top-down Scene view.
            for (int tick = 0; tick < 4; tick++)
            {
                float angle = tick * Mathf.PI * 0.5f;
                Vector2 axis = new(Mathf.Sin(angle), -Mathf.Cos(angle));
                painter.BeginPath();
                painter.MoveTo(centre + axis * (radius - 6f));
                painter.LineTo(centre + axis * radius);
                painter.Stroke();
            }

            PixelWindSettings wind = Settings.Environment.Wind;
            Vector2 direction = wind.NormalizedDirection;
            Vector2 screenDirection = new(direction.x, -direction.y);
            float length = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(wind.Strength / 2f)) * (radius - 8f);
            Vector2 tip = centre + screenDirection * length;
            Vector2 side = new(-screenDirection.y, screenDirection.x);

            painter.strokeColor = new Color(0.45f, 0.75f, 1f);
            painter.lineWidth = 3f;
            painter.BeginPath();
            painter.MoveTo(centre - screenDirection * length * 0.35f);
            painter.LineTo(tip);
            painter.Stroke();

            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            painter.MoveTo(tip + screenDirection * 6f);
            painter.LineTo(tip - screenDirection * 4f + side * 6f);
            painter.LineTo(tip - screenDirection * 4f - side * 6f);
            painter.ClosePath();
            painter.Fill();
        }
    }

    /// <summary>
    /// The whole day at a glance: background, ambient sky and direct light colour for every hour in
    /// clear weather, the sun's height, and where the scene's clock currently stands.
    /// </summary>
    internal sealed class DayCyclePreview : SettingsPreview
    {
        private const int Resolution = 192;
        private readonly Texture2D _background;
        private readonly Texture2D _ambient;
        private readonly Texture2D _light;
        private readonly VisualElement _curve;
        private readonly Label _caption;
        private readonly Color[] _pixels = new Color[Resolution];

        public DayCyclePreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "24 hours in clear weather")
        {
            _background = AddStrip(this, "Background", Resolution, FilterMode.Bilinear);
            _ambient = AddStrip(this, "Ambient sky", Resolution, FilterMode.Bilinear);
            _light = AddStrip(this, "Sun / moon", Resolution, FilterMode.Bilinear);

            var curveRow = new VisualElement();
            curveRow.AddToClassList("pp-row");
            curveRow.Add(CreateLabel("Sun height", "pp-strip-label"));
            _curve = new VisualElement { style = { height = 44 } };
            _curve.AddToClassList("pp-grow");
            _curve.generateVisualContent += DrawCurve;
            curveRow.Add(_curve);
            Add(curveRow);

            AddAxis(this, "0:00", "6:00", "12:00", "18:00", "24:00");
            _caption = AddCaption(string.Empty);
            // Follows the scene clock while time passes.
            Animate(250);
        }

        public override void Refresh()
        {
            PixelDayNightSettings dayNight = Settings.Environment.DayNight;
            PixelWeatherSettings weather = Settings.Environment.Weather;
            float ambientScale = weather.EvaluateLighting(weather.SunnyCloudCoverage, 0f).Ambient;

            for (int index = 0; index < Resolution; index++)
            {
                float daylight = PixelMath.SmoothRange(-0.08f, 0.22f, SunHeight(dayNight, HourAt(index)));
                _pixels[index] = Color.Lerp(dayNight.NightCameraColor, dayNight.DayCameraColor, daylight * ambientScale);
            }
            Apply(_background);

            for (int index = 0; index < Resolution; index++)
            {
                float daylight = PixelMath.SmoothRange(-0.08f, 0.22f, SunHeight(dayNight, HourAt(index)));
                _pixels[index] = Color.Lerp(dayNight.NightSkyColor, dayNight.DaySkyColor, daylight) * ambientScale;
            }
            Apply(_ambient);

            for (int index = 0; index < Resolution; index++)
            {
                float sunHeight = SunHeight(dayNight, HourAt(index));
                float sun = Mathf.Clamp01(sunHeight);
                float moon = Mathf.Clamp01(-sunHeight);
                Color sunLight = Color.Lerp(dayNight.SunHorizonColor, dayNight.SunDayColor, sun) * (dayNight.MaxSunIntensity * sun);
                Color moonLight = Color.Lerp(dayNight.MoonHorizonColor, dayNight.MoonZenithColor, moon) * (dayNight.MaxMoonIntensity * moon);
                Color light = sun > 0f ? sunLight : moonLight;
                light.a = 1f;
                _pixels[index] = light;
            }
            Apply(_light);

            PixelEnvironmentController controller = FindController();
            _caption.text = !dayNight.Enabled
                ? "The day/night cycle is off; the lighting stays at its daytime values."
                : $"Sunrise {FormatHour(dayNight.SunriseTime)}, sunset {FormatHour(dayNight.SunsetTime)}. " +
                  $"A full day lasts {dayNight.DayDurationInSeconds:0} s in play mode." +
                  (controller != null ? $" Scene clock: {FormatHour(controller.CurrentTimeHours)}." : string.Empty);
            _curve.MarkDirtyRepaint();
        }

        private PixelEnvironmentController FindController()
        {
            PixelEnvironmentController controller = Object.FindFirstObjectByType<PixelEnvironmentController>();
            return controller != null && controller.Profile == Settings ? controller : null;
        }

        private static float HourAt(int index) => index / (Resolution - 1f) * 24f;

        // The light's orbit tilts about X, so its height above the horizon is the sine of the orbit angle.
        private static float SunHeight(PixelDayNightSettings dayNight, float hour)
        {
            return Mathf.Sin(dayNight.GetSunOrbitAngle(hour) * Mathf.Deg2Rad);
        }

        private static string FormatHour(float hour)
        {
            int minutes = Mathf.RoundToInt(Mathf.Repeat(hour, 24f) * 60f);
            return $"{minutes / 60 % 24}:{minutes % 60:00}";
        }

        private void Apply(Texture2D texture)
        {
            texture.SetPixels(_pixels);
            texture.Apply(false);
        }

        private void DrawCurve(MeshGenerationContext context)
        {
            Painter2D painter = context.painter2D;
            Rect rect = _curve.contentRect;
            PixelDayNightSettings dayNight = Settings.Environment.DayNight;
            float horizon = rect.yMin + rect.height * 0.5f;

            painter.lineWidth = 1f;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.2f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, horizon));
            painter.LineTo(new Vector2(rect.xMax, horizon));
            painter.Stroke();

            painter.lineWidth = 2f;
            painter.strokeColor = new Color(1f, 0.8f, 0.35f);
            painter.BeginPath();
            for (int step = 0; step <= 96; step++)
            {
                float hour = step / 96f * 24f;
                Vector2 point = new(
                    Mathf.Lerp(rect.xMin, rect.xMax, step / 96f),
                    horizon - SunHeight(dayNight, hour) * (rect.height * 0.5f - 2f));
                if (step == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.Stroke();

            PixelEnvironmentController controller = FindController();
            if (controller == null)
                return;

            float x = Mathf.Lerp(rect.xMin, rect.xMax, controller.CurrentTimeHours / 24f);
            painter.lineWidth = 2f;
            painter.strokeColor = new Color(0.45f, 0.75f, 1f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, rect.yMin));
            painter.LineTo(new Vector2(x, rect.yMax));
            painter.Stroke();
        }
    }

    /// <summary>Cloud coverage and the lighting multipliers each weather state settles on.</summary>
    internal sealed class WeatherPreview : SettingsPreview
    {
        private static readonly (PixelWeatherState State, string Name)[] States =
        {
            (PixelWeatherState.Sunny, "Sunny"),
            (PixelWeatherState.Cloudy, "Cloudy"),
            (PixelWeatherState.Rain, "Rain")
        };

        private readonly List<(VisualElement Coverage, VisualElement Sun, VisualElement Ambient, VisualElement GodRays)> _bars = new();

        public WeatherPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "What each weather state settles on")
        {
            var row = new VisualElement();
            row.AddToClassList("pp-row");
            foreach ((PixelWeatherState _, string name) in States)
            {
                var column = new VisualElement();
                column.AddToClassList("pp-weather-column");
                column.Add(CreateLabel(name, "pp-card__title"));
                _bars.Add((
                    AddBar(column, "Cloud coverage", new Color(0.7f, 0.75f, 0.82f)),
                    AddBar(column, "Sun", new Color(1f, 0.82f, 0.4f)),
                    AddBar(column, "Ambient", new Color(0.55f, 0.72f, 0.9f)),
                    AddBar(column, "God rays (of 2)", new Color(1f, 0.9f, 0.6f))));
                row.Add(column);
            }

            Add(row);
            AddCaption("Rain only starts once coverage passes Rain Start Cloud Coverage, and stops before the clouds clear.");
        }

        public override void Refresh()
        {
            PixelWeatherSettings weather = Settings.Environment.Weather;
            for (int index = 0; index < States.Length; index++)
            {
                PixelWeatherState state = States[index].State;
                float coverage = weather.GetCoverage(state);
                PixelWeatherLighting lighting = weather.EvaluateLighting(coverage, state == PixelWeatherState.Rain ? 1f : 0f);
                SetBar(_bars[index].Coverage, coverage);
                SetBar(_bars[index].Sun, lighting.Sun);
                SetBar(_bars[index].Ambient, lighting.Ambient);
                SetBar(_bars[index].GodRays, lighting.GodRays * 0.5f);
            }
        }

        private static VisualElement AddBar(VisualElement column, string label, Color color)
        {
            column.Add(CreateLabel(label, "pp-bar-label"));
            var track = new VisualElement();
            track.AddToClassList("pp-bar-track");
            var fill = new VisualElement { style = { backgroundColor = color } };
            fill.AddToClassList("pp-bar-fill");
            track.Add(fill);
            column.Add(track);
            return fill;
        }

        private static void SetBar(VisualElement fill, float value)
        {
            fill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }
    }

    /// <summary>
    /// The live cloud shadow over the ground around the scene camera, drawn by the in-game cloud
    /// function with the in-game globals, so it matches the Game view exactly.
    /// </summary>
    internal sealed class CloudShadowPreview : SettingsPreview
    {
        private const float AreaSize = 48f;
        private static readonly int PreviewAreaId = Shader.PropertyToID("_PreviewArea");
        private static readonly int PreviewLitColorId = Shader.PropertyToID("_PreviewLitColor");
        private static readonly int PreviewShadowColorId = Shader.PropertyToID("_PreviewShadowColor");
        private static readonly int PreviewGapColorId = Shader.PropertyToID("_PreviewGapColor");
        private static readonly int PreviewGapId = Shader.PropertyToID("_PreviewGap");
        private static readonly int PreviewGapLodsId = Shader.PropertyToID("_PreviewGapLods");

        private enum PreviewWeather
        {
            Scene,
            Sunny,
            Cloudy,
            Rain
        }

        private readonly Image _image;
        private readonly Label _caption;
        private readonly EnumField _weather;
        private readonly Material _material;
        private readonly RenderTexture _target;
        private readonly bool _showGaps;

        /// <param name="showGaps">Also mark the gaps god rays fall through, for the God Rays page.</param>
        public CloudShadowPreview(PixelPipelineSettings settings, SerializedObject serializedSettings, bool showGaps = false)
            : base(settings, serializedSettings, showGaps ? "Where shafts form, live" : "Cloud shadow on the ground, live")
        {
            _showGaps = showGaps;
            Shader shader = Shader.Find("Hidden/PixelPipeline/Editor/CloudShadowPreview");
            if (shader != null)
            {
                _material = Own(new Material(shader));
                // Linear, because the Image shows the texture's values as they are.
                _target = Own(new RenderTexture(192, 192, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    filterMode = FilterMode.Point
                });
            }

            _weather = new EnumField("Coverage of", showGaps ? PreviewWeather.Cloudy : PreviewWeather.Scene)
            {
                tooltip = "Scene follows the environment controller's current weather; the others preview a state's cloud coverage."
            };
            _weather.RegisterValueChangedCallback(_ => Refresh());
            Add(_weather);

            _image = new Image { image = _target, scaleMode = ScaleMode.ScaleToFit, style = { height = 192, marginTop = 4 } };
            Add(_image);
            _caption = AddCaption(string.Empty);
            Animate(66);
        }

        public override void Refresh()
        {
            if (_material == null)
            {
                _caption.text = "The preview shader was not found.";
                return;
            }

            if (!Settings.EnableCloudShadows)
            {
                _caption.text = Settings.Environment.Clouds.Noise == null
                    ? "Assign a noise texture to enable cloud shadows."
                    : "Cloud shadows are off.";
            }
            else if (_showGaps)
            {
                _caption.text = $"Yellow marks the gaps: clear ground with cloud around it, where shafts fall. " +
                                $"{AreaSize:0} × {AreaSize:0} m around the point the scene camera looks at, +Z up. " +
                                "Open sky has no gaps, so a sunny day shows few or none.";
            }
            else
            {
                _caption.text = $"{AreaSize:0} × {AreaSize:0} m around the point the scene camera looks at, +Z up.";
            }

            // The same globals the renderer feature uploads each frame. A previewed weather state only
            // borrows them for this draw, then the scene's own state goes back.
            PixelEnvironmentRuntimeState sceneState = PixelEnvironmentController.ResolveState(Settings);
            PixelEnvironmentRuntimeState previewState = (PreviewWeather)_weather.value switch
            {
                PreviewWeather.Sunny => new PixelEnvironmentRuntimeState(Settings.Environment.Weather.GetCoverage(PixelWeatherState.Sunny), 0f, 1f),
                PreviewWeather.Cloudy => new PixelEnvironmentRuntimeState(Settings.Environment.Weather.GetCoverage(PixelWeatherState.Cloudy), 0f, 1f),
                PreviewWeather.Rain => new PixelEnvironmentRuntimeState(Settings.Environment.Weather.GetCoverage(PixelWeatherState.Rain), 0f, 1f),
                _ => sceneState
            };
            Settings.ApplyCloudGlobals(previewState);

            Vector3 focus = FindFocus();
            _material.SetVector(PreviewAreaId, new Vector4(focus.x, focus.z, AreaSize, focus.y));
            _material.SetColor(PreviewLitColorId, new Color(0.62f, 0.74f, 0.45f));
            _material.SetColor(PreviewShadowColorId, new Color(0.16f, 0.22f, 0.2f));
            PixelGodRaySettings godRays = Settings.Environment.GodRays;
            godRays.GetCloudLods(Settings.Environment.Clouds, out float gapLod, out float detailLod);
            _material.SetColor(PreviewGapColorId, new Color(1f, 0.86f, 0.45f));
            _material.SetVector(PreviewGapId, new Vector4(
                _showGaps && godRays.Enabled ? 1f : 0f, godRays.GapThreshold, godRays.GapSoftness, 0f));
            _material.SetVector(PreviewGapLodsId, new Vector4(gapLod, detailLod, 0f, 0f));
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(null, _target, _material);
            RenderTexture.active = previous;

            if ((PreviewWeather)_weather.value != PreviewWeather.Scene)
                Settings.ApplyCloudGlobals(sceneState);
            _image.MarkDirtyRepaint();
        }

        private static Vector3 FindFocus()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return Vector3.zero;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.001f)
                return new Vector3(camera.transform.position.x, 0f, camera.transform.position.z);

            float distance = -ray.origin.y / ray.direction.y;
            return distance > 0f ? ray.GetPoint(distance) : new Vector3(ray.origin.x, 0f, ray.origin.z);
        }
    }

    /// <summary>One streak at its longest, with the shader's spine, taper, fades and banding.</summary>
    internal sealed class WindStreakPreview : SettingsPreview
    {
        private const int Segments = 48;
        private readonly VisualElement _canvas;
        private readonly Label _caption;

        public WindStreakPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "The longest streak, head on the right")
        {
            _canvas = new VisualElement { style = { height = 70, backgroundColor = new Color(0.12f, 0.16f, 0.2f) } };
            _canvas.generateVisualContent += DrawStreak;
            Add(_canvas);
            _caption = AddCaption(string.Empty);
            Animate(50);
        }

        public override void Refresh()
        {
            PixelWindStreakSettings streaks = Settings.Environment.WindStreaks;
            float visibility = streaks.EvaluateWindResponse(Settings.Environment.Wind.Strength);
            _caption.text = (streaks.Enabled ? string.Empty : "Wind streaks are off. ") +
                            $"Length {streaks.LengthMaximum:0.0} m; width and bend drawn exaggerated so they stay visible. " +
                            $"Visibility at the current wind strength: {visibility * 100f:0}%.";
            _canvas.MarkDirtyRepaint();
        }

        // StreakSpine in WindStreak.shader, reduced to the lateral bend seen from above.
        private static float Bend(PixelWindStreakSettings streaks, float u, float time, float seed)
        {
            const float TwoPi = Mathf.PI * 2f;
            float headAngle = seed * TwoPi - streaks.CurveScrollSpeed * time * TwoPi;
            float angle = headAngle + u * streaks.CurveFrequency * TwoPi;
            return (Mathf.Sin(angle) - Mathf.Sin(headAngle)) * streaks.CurveAmplitude;
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private void DrawStreak(MeshGenerationContext context)
        {
            PixelWindStreakSettings streaks = Settings.Environment.WindStreaks;
            Rect rect = _canvas.contentRect;
            if (rect.width < 10f)
                return;

            float time = (float)EditorApplication.timeSinceStartup;
            float length = streaks.LengthMaximum;
            float pixelsPerMetre = (rect.width - 20f) / Mathf.Max(0.05f, length);
            // Exaggerate across the streak so a few centimetres of width stay readable next to metres of length.
            float across = Mathf.Max(streaks.CurveAmplitude * 2f + streaks.WidthMaximum, 0.01f);
            float crossScale = Mathf.Max(pixelsPerMetre, (rect.height * 0.8f) / across);

            Painter2D painter = context.painter2D;
            Color color = streaks.Color;
            float headX = rect.xMax - 10f;
            float centreY = rect.center.y;

            for (int segment = 0; segment < Segments; segment++)
            {
                float u0 = segment / (float)Segments;
                float u1 = (segment + 1) / (float)Segments;
                float uMid = (u0 + u1) * 0.5f;

                float head = streaks.HeadFade > 1e-4f ? SmoothStep(0f, streaks.HeadFade, uMid) : 1f;
                float tail = streaks.TailFade > 1e-4f ? SmoothStep(0f, streaks.TailFade, 1f - uMid) : 1f;
                float alpha = Mathf.Clamp01(color.a * streaks.Intensity * head * tail);
                if (streaks.AlphaSteps >= 1)
                    alpha = Mathf.Ceil(alpha * streaks.AlphaSteps) / streaks.AlphaSteps;
                if (alpha <= 0.003f)
                    continue;

                Vector2 p0 = new(headX - u0 * length * pixelsPerMetre, centreY - Bend(streaks, u0, time, 0.3f) * crossScale);
                Vector2 p1 = new(headX - u1 * length * pixelsPerMetre, centreY - Bend(streaks, u1, time, 0.3f) * crossScale);
                float w0 = streaks.WidthMaximum * Mathf.Lerp(1f, streaks.TailWidthScale, u0) * crossScale * 0.5f;
                float w1 = streaks.WidthMaximum * Mathf.Lerp(1f, streaks.TailWidthScale, u1) * crossScale * 0.5f;

                painter.fillColor = new Color(color.r, color.g, color.b, alpha);
                painter.BeginPath();
                painter.MoveTo(p0 + Vector2.up * w0);
                painter.LineTo(p1 + Vector2.up * w1);
                painter.LineTo(p1 - Vector2.up * w1);
                painter.LineTo(p0 - Vector2.up * w0);
                painter.ClosePath();
                painter.Fill();
            }
        }
    }

    /// <summary>
    /// The water body over depth, as the shader bands it on top of a sandy bed, and a cross-section of
    /// the shore foam: the solid band, and the ripples that travel out from it.
    /// </summary>
    internal sealed class WaterPreview : SettingsPreview
    {
        private const int Resolution = 256;
        private readonly Texture2D _depthRamp;
        private readonly Texture2D _shoreProfile;
        private readonly Label _depthAxisEnd;
        private readonly Label _shoreAxisEnd;
        private readonly Color[] _pixels = new Color[Resolution];

        public WaterPreview(PixelPipelineSettings settings, SerializedObject serializedSettings)
            : base(settings, serializedSettings, "Water, surface colours before lighting")
        {
            _depthRamp = AddStrip(this, "Depth", Resolution, FilterMode.Point);
            _depthAxisEnd = AddAxisWithEnd(this);
            _shoreProfile = AddStrip(this, "From the shore", Resolution, FilterMode.Point);
            _shoreAxisEnd = AddAxisWithEnd(this);
            AddCaption("In the scene every colour here is lit by the sun or moon and the ambient light, " +
                       "so the water follows the day/night cycle. Ripples move outwards over time.");
            Animate(80);
        }

        public override void Refresh()
        {
            SerializedSettings.Update();
            Color shallow = ColorValue("water.shallowColor");
            Color deep = ColorValue("water.deepColor");
            float depthDistance = Mathf.Max(0.01f, FloatValue("water.depthDistance"));
            int steps = Mathf.Max(1, Property("water.colorSteps").intValue);
            float shallowOpacity = FloatValue("water.shallowOpacity");
            float deepOpacity = FloatValue("water.deepOpacity");
            Color bed = new(0.62f, 0.55f, 0.4f);

            float maximumDepth = depthDistance * 1.5f;
            for (int index = 0; index < Resolution; index++)
            {
                float depth = index / (Resolution - 1f) * maximumDepth;
                // PixelWaterQuantize in PixelWaterCommon.hlsl.
                float ramp = Mathf.Clamp01(Mathf.Ceil(Mathf.Clamp01(depth / depthDistance) * steps) / steps);
                Color body = Color.Lerp(shallow, deep, ramp);
                _pixels[index] = Color.Lerp(bed, body, Mathf.Lerp(shallowOpacity, deepOpacity, ramp));
            }
            Apply(_depthRamp);
            _depthAxisEnd.text = $"{maximumDepth:0.##} m";

            bool shoreOn = Property("water.enableShoreFoam").boolValue;
            Color foam = ColorValue("water.shoreFoamColor");
            float width = FloatValue("water.shoreFoamWidth");
            float spacing = Mathf.Max(0.01f, FloatValue("water.shoreRippleSpacing"));
            float speed = FloatValue("water.shoreRippleSpeed");
            float range = FloatValue("water.shoreRippleRange");
            float span = Mathf.Max(0.1f, (width + range) * 1.25f);
            float time = (float)EditorApplication.timeSinceStartup;
            float texel = span / Resolution;

            for (int index = 0; index < Resolution; index++)
            {
                float distance = (index + 0.5f) * texel;
                Color water = Color.Lerp(shallow, bed, 1f - shallowOpacity);
                bool isFoam = false;
                if (shoreOn && width > 0.0001f)
                {
                    float outside = distance - width;
                    if (outside <= 0f)
                    {
                        isFoam = true;
                    }
                    else if (outside < range)
                    {
                        // Ripples are one texel of this strip wide, like one pixel in the scene.
                        float phase = (outside - time * speed) / spacing;
                        float toLine = Mathf.Min(Mathf.Repeat(phase, 1f), 1f - Mathf.Repeat(phase, 1f)) * spacing;
                        // The in-game breakup is noise; here the survivors simply thin out with distance.
                        isFoam = toLine < texel * 0.5f && outside / range < 0.85f;
                    }
                }

                _pixels[index] = isFoam ? foam : water;
            }
            Apply(_shoreProfile);
            _shoreAxisEnd.text = $"{span:0.##} m";
        }

        private static Label AddAxisWithEnd(VisualElement parent)
        {
            var axis = new VisualElement();
            axis.AddToClassList("pp-axis");
            axis.Add(new Label("0 m"));
            var end = new Label();
            axis.Add(end);
            parent.Add(axis);
            return end;
        }

        private void Apply(Texture2D texture)
        {
            texture.SetPixels(_pixels);
            texture.Apply(false);
        }
    }
}
