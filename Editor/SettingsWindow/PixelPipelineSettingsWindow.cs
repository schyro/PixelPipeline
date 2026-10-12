using System;
using System.Collections.Generic;
using PixelPipeline.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace PixelPipeline.Editor
{
    /// <summary>
    /// Every value of a <see cref="PixelPipelineSettings"/> asset, split into pages with a preview of
    /// what the page controls. Edits go straight to the asset with full undo, exactly as in the
    /// Inspector, and repaint the Scene and Game views as they happen.
    /// </summary>
    public sealed class PixelPipelineSettingsWindow : EditorWindow
    {
        private sealed class Page
        {
            public string Group;
            public string Title;
            public string Description;
            // Paths into the asset. A path to a nested settings class lists that class's fields.
            public string[] Properties;
            public Func<PixelPipelineSettings, SerializedObject, SettingsPreview> Preview;
        }

        private static readonly Page[] Pages =
        {
            new()
            {
                Group = "Rendering",
                Title = "Resolution",
                Description = "The internal resolution the scene is pixelated to, and which cameras get the pixel passes.",
                Properties = new[] { "internalWidth", "internalHeight", "matchCameraAspect", "renderSceneView", "baseCameraOnly" },
                Preview = (settings, serialized) => new ResolutionPreview(settings, serialized)
            },
            new()
            {
                Group = "Rendering",
                Title = "Outline",
                Description = "Dark silhouette lines where depth jumps, and light crease lines where surfaces fold.",
                Properties = new[]
                {
                    "enableOutline", "lineDarken", "lineAlpha", "creaseBrighten", "creaseAlpha", "kernelRadius",
                    "depthThreshold", "grazingAngleStart", "grazingDepthScale", "creaseDepthLow", "creaseDepthHigh",
                    "creaseNormalLow", "creaseNormalHigh"
                }
            },
            new()
            {
                Group = "Rendering",
                Title = "Lighting",
                Description = "Light and shadow snapped to hard bands, so a curved surface shades in clean steps " +
                              "and a shadow edge is one pixel wide. Light bands need a Deferred+ or Deferred renderer.",
                Properties = new[]
                {
                    "enableLightBands", "lightBands", "lightBandBias", "enablePixelShadows", "shadowSteps", "shadowEdgeBias", "shadowEdgeFilter"
                },
                Preview = (settings, serialized) => new LightingPreview(settings, serialized)
            },
            new()
            {
                Group = "Environment",
                Title = "Wind",
                Description = "One direction and strength shared by clouds, grass, foliage, rain, water, and the wind streaks.",
                Properties = new[] { "environment.wind" },
                Preview = (settings, serialized) => new WindCompassPreview(settings, serialized)
            },
            new()
            {
                Group = "Environment",
                Title = "Day & Night",
                Description = "The sun and moon orbit, their colours, and the ambient light over the day.",
                Properties = new[] { "environment.dayNight" },
                Preview = (settings, serialized) => new DayCyclePreview(settings, serialized)
            },
            new()
            {
                Group = "Environment",
                Title = "Weather",
                Description = "Sunny, Cloudy, and Rain: how cloudy each gets, how quickly it changes, and how the light dims.",
                Properties = new[] { "environment.weather" },
                Preview = (settings, serialized) => new WeatherPreview(settings, serialized)
            },
            new()
            {
                Group = "Environment",
                Title = "Clouds",
                Description = "Shadows of passing clouds, projected along the light onto everything in view.",
                Properties = new[] { "environment.clouds" },
                Preview = (settings, serialized) => new CloudShadowPreview(settings, serialized)
            },
            new()
            {
                Group = "Environment",
                Title = "God Rays",
                Description = "Light shafts where the sun falls through gaps in the clouds, marched through the air at " +
                              "the internal resolution and snapped to a few tones. Open sky has no gaps, so shafts " +
                              "belong to cloudy weather; the Weather page scales them per state.",
                Properties = new[] { "environment.godRays", "environment.groundHeight" },
                Preview = (settings, serialized) => new CloudShadowPreview(settings, serialized, showGaps: true)
            },
            new()
            {
                Group = "Effects",
                Title = "Wind Streaks",
                Description = "Stylised trails that travel along the wind and bend behind their head.",
                Properties = new[] { "environment.windStreaks", "environment.groundHeight" },
                Preview = (settings, serialized) => new WindStreakPreview(settings, serialized)
            },
            new()
            {
                Group = "Effects",
                Title = "Rain",
                Description = "Drops falling along the wind onto the first surface under them, and the splash each " +
                              "one makes exactly where and when it lands. The Weather page decides when it rains.",
                Properties = new[] { "environment.rain", "environment.groundHeight" }
            },
            new()
            {
                Group = "Effects",
                Title = "Water",
                Description = "Depth colour, refraction, surface pattern, shore foam, glints, waves, and the planar reflection. " +
                              "Add Pixel Water Shoreline to a surface for foam that follows its banks exactly.",
                Properties = new[] { "water" },
                Preview = (settings, serialized) => new WaterPreview(settings, serialized)
            }
        };

        [SerializeField] private PixelPipelineSettings settings;
        [SerializeField] private int selectedPage;

        private readonly List<Button> _tabs = new();
        private ObjectField _settingsField;
        private ScrollView _content;
        private SerializedObject _serializedSettings;
        private SettingsPreview _preview;

        [MenuItem("Tools/Pixel Pipeline/Settings Window", priority = 0)]
        public static void Open()
        {
            Open(null);
        }

        public static void Open(PixelPipelineSettings target)
        {
            var window = GetWindow<PixelPipelineSettingsWindow>();
            window.titleContent = new GUIContent("Pixel Pipeline", EditorGUIUtility.IconContent("SettingsIcon").image);
            window.minSize = new Vector2(620f, 420f);
            if (target != null)
                window.SetSettings(target);
            window.Show();
        }

        private void OnDisable()
        {
            ReleasePreview();
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            StyleSheet styleSheet = FindStyleSheet();
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);
            root.AddToClassList("pp-root");

            if (settings == null)
                settings = FindDefaultSettings();

            root.Add(CreateToolbar());

            var body = new VisualElement();
            body.AddToClassList("pp-body");
            body.Add(CreateSidebar());
            _content = new ScrollView(ScrollViewMode.Vertical);
            _content.AddToClassList("pp-content");
            body.Add(_content);
            root.Add(body);

            ShowPage(selectedPage);
        }

        private VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("pp-toolbar");

            var title = new Label("Pixel Pipeline");
            title.AddToClassList("pp-toolbar__title");
            toolbar.Add(title);

            _settingsField = new ObjectField
            {
                objectType = typeof(PixelPipelineSettings),
                allowSceneObjects = false,
                value = settings,
                tooltip = "The settings asset being edited. Every renderer and scene component that uses it updates live."
            };
            _settingsField.AddToClassList("pp-toolbar__asset");
            _settingsField.RegisterValueChangedCallback(change => SetSettings(change.newValue as PixelPipelineSettings));
            toolbar.Add(_settingsField);

            toolbar.Add(new Button(() =>
            {
                if (settings == null)
                    return;
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            })
            {
                text = "Select",
                tooltip = "Select the asset in the Project window."
            });

            return toolbar;
        }

        private VisualElement CreateSidebar()
        {
            var sidebar = new ScrollView(ScrollViewMode.Vertical);
            sidebar.AddToClassList("pp-sidebar");
            _tabs.Clear();

            string group = null;
            for (int index = 0; index < Pages.Length; index++)
            {
                Page page = Pages[index];
                if (page.Group != group)
                {
                    group = page.Group;
                    var groupLabel = new Label(group.ToUpperInvariant());
                    groupLabel.AddToClassList("pp-sidebar__group");
                    sidebar.Add(groupLabel);
                }

                int pageIndex = index;
                var tab = new Button(() => ShowPage(pageIndex)) { text = page.Title };
                tab.AddToClassList("pp-tab");
                _tabs.Add(tab);
                sidebar.Add(tab);
            }

            return sidebar;
        }

        private void SetSettings(PixelPipelineSettings target)
        {
            settings = target;
            _settingsField?.SetValueWithoutNotify(target);
            if (_content != null)
                ShowPage(selectedPage);
        }

        private void ShowPage(int index)
        {
            selectedPage = Mathf.Clamp(index, 0, Pages.Length - 1);
            for (int tab = 0; tab < _tabs.Count; tab++)
                _tabs[tab].EnableInClassList("pp-tab--selected", tab == selectedPage);

            ReleasePreview();
            _content.Clear();

            if (settings == null)
            {
                var empty = new VisualElement();
                empty.AddToClassList("pp-empty");
                empty.Add(new HelpBox(
                    "No Pixel Pipeline Settings asset was found. Create one with Assets > Create > Rendering > Pixel Pipeline Settings, then pick it above.",
                    HelpBoxMessageType.Info));
                _content.Add(empty);
                return;
            }

            Page page = Pages[selectedPage];
            _serializedSettings = new SerializedObject(settings);

            var container = new VisualElement();
            container.AddToClassList("pp-page");

            var title = new Label(page.Title);
            title.AddToClassList("pp-page__title");
            container.Add(title);
            var description = new Label(page.Description);
            description.AddToClassList("pp-page__description");
            container.Add(description);

            if (page.Preview != null)
            {
                _preview = page.Preview(settings, _serializedSettings);
                container.Add(_preview);
            }

            foreach (string path in page.Properties ?? Array.Empty<string>())
                AddProperty(container, _serializedSettings.FindProperty(path));

            container.Bind(_serializedSettings);
            container.TrackSerializedObjectValue(_serializedSettings, _ => OnSettingsChanged());
            _content.Add(container);
            _content.scrollOffset = Vector2.zero;
        }

        private static void AddProperty(VisualElement container, SerializedProperty property)
        {
            if (property == null)
                return;

            // A nested settings class is listed field by field rather than as one foldout, so the page
            // itself is the grouping.
            if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren && !property.isArray)
            {
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    container.Add(new PropertyField(child.Copy()));
                    enterChildren = false;
                }

                return;
            }

            container.Add(new PropertyField(property.Copy()));
        }

        private void OnSettingsChanged()
        {
            _preview?.Refresh();
            // The renderer feature reads the asset every frame, but edit-mode views only redraw on demand.
            InternalEditorUtility.RepaintAllViews();
        }

        private void ReleasePreview()
        {
            _preview?.Release();
            _preview = null;
        }

        private static PixelPipelineSettings FindDefaultSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PixelPipelineSettings"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<PixelPipelineSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static StyleSheet FindStyleSheet()
        {
            foreach (string guid in AssetDatabase.FindAssets("PixelPipelineSettingsWindow t:StyleSheet"))
                return AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));

            return null;
        }
    }
}
