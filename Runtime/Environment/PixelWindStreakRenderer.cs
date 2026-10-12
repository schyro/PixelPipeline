using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Draws stylised wind streaks: GPU-instanced ribbons that travel along the profile wind
    /// direction and trail a gentle curve behind their leading point. A fixed pool is recycled in
    /// place, so the draw cost never depends on how long the scene has been running.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PixelWindStreakRenderer : MonoBehaviour
    {
        [SerializeField] private PixelEnvironmentController environmentController;

        private const int MaximumInstances = 1023;
        private const float MaximumDeltaTime = 0.1f;

        private struct Streak
        {
            public Vector3 SpawnOffset;
            public float Age;
            public float Lifetime;
            public float Speed;
            public float Length;
            public float Width;
            public float Seed;
        }

        private Streak[] _streaks = System.Array.Empty<Streak>();
        private Matrix4x4[] _matrices = System.Array.Empty<Matrix4x4>();
        private Vector4[] _streakData = System.Array.Empty<Vector4>();
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _ribbonMesh;
        private int _ribbonSegments;
        private System.Random _random;
        private float _lastTime;
        private bool _hasLastTime;

        private static readonly int StreakDataId = Shader.PropertyToID("_StreakData");
        private static readonly int StreakColorId = Shader.PropertyToID("_StreakColor");
        private static readonly int StreakIntensityId = Shader.PropertyToID("_StreakIntensity");
        private static readonly int StreakTailWidthId = Shader.PropertyToID("_StreakTailWidth");
        private static readonly int StreakHeadFadeId = Shader.PropertyToID("_StreakHeadFade");
        private static readonly int StreakTailFadeId = Shader.PropertyToID("_StreakTailFade");
        private static readonly int StreakEdgeSoftnessId = Shader.PropertyToID("_StreakEdgeSoftness");
        private static readonly int StreakLifeFadeId = Shader.PropertyToID("_StreakLifeFade");
        private static readonly int StreakAlphaStepsId = Shader.PropertyToID("_StreakAlphaSteps");
        private static readonly int StreakCurveAmplitudeId = Shader.PropertyToID("_StreakCurveAmplitude");
        private static readonly int StreakCurveFrequencyId = Shader.PropertyToID("_StreakCurveFrequency");
        private static readonly int StreakCurveScrollId = Shader.PropertyToID("_StreakCurveScroll");
        private static readonly int StreakCurveVerticalId = Shader.PropertyToID("_StreakCurveVertical");
        private static readonly int StreakTimeId = Shader.PropertyToID("_StreakTime");


        private void OnEnable()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            _random ??= new System.Random(9173);
            _hasLastTime = false;
        }

        private void OnDisable()
        {
            ReleaseMesh();
        }

        private void Update()
        {
            if (environmentController == null)
                return;

            PixelPipelineSettings profile = environmentController.Profile;
            if (profile == null || !profile.EnableWindStreaks)
                return;

            PixelWindStreakSettings settings = profile.Environment.WindStreaks;
            PixelWindSettings wind = profile.Environment.Wind;
            float windResponse = settings.EvaluateWindResponse(wind.Strength);
            if (windResponse <= 0.001f)
                return;

            int streakCount = Mathf.Min(
                Mathf.RoundToInt(settings.MaximumStreaks * Mathf.Lerp(1f, windResponse, settings.CountWindResponse)),
                MaximumInstances);
            if (streakCount <= 0)
                return;

            Camera targetCamera = environmentController.TargetCamera;
            Vector3 volumeCentre = GetVolumeCentre(settings, profile.Environment.GroundHeight, targetCamera);
            Vector2 windDirection = wind.NormalizedDirection;
            Vector3 windDirectionWS = new(windDirection.x, 0f, windDirection.y);

            EnsurePool(settings, streakCount);
            EnsureMesh(settings.Segments);

            float deltaTime = AdvanceClock(out float time);
            float speedScale = Mathf.Max(0f, wind.Strength);
            float longestTravel = 0f;

            for (int index = 0; index < streakCount; index++)
            {
                Streak streak = _streaks[index];
                streak.Age += deltaTime;
                if (streak.Lifetime <= 0f || streak.Age >= streak.Lifetime)
                    streak = Respawn(settings, streak.Lifetime > 0f ? streak.Age - streak.Lifetime : 0f);

                _streaks[index] = streak;

                float travelled = streak.Speed * speedScale * streak.Age;
                // Centring the path on the volume keeps the visible band around the camera instead
                // of letting every streak drift off downwind before it dies.
                float pathOffset = streak.Speed * speedScale * streak.Lifetime * 0.5f;
                Vector3 position = volumeCentre
                                   + streak.SpawnOffset
                                   + windDirectionWS * (travelled - pathOffset);

                _matrices[index] = Matrix4x4.Translate(position);
                _streakData[index] = new Vector4(
                    streak.Seed,
                    Mathf.Clamp01(streak.Age / Mathf.Max(0.0001f, streak.Lifetime)),
                    streak.Length,
                    streak.Width);

                longestTravel = Mathf.Max(longestTravel, pathOffset + streak.Length);
            }

            _propertyBlock.Clear();
            _propertyBlock.SetVectorArray(StreakDataId, _streakData);
            _propertyBlock.SetVector(StreakColorId, GetLitColor(settings));
            _propertyBlock.SetFloat(StreakIntensityId, settings.Intensity * windResponse);
            _propertyBlock.SetFloat(StreakTailWidthId, settings.TailWidthScale);
            _propertyBlock.SetFloat(StreakHeadFadeId, settings.HeadFade);
            _propertyBlock.SetFloat(StreakTailFadeId, settings.TailFade);
            _propertyBlock.SetFloat(StreakEdgeSoftnessId, settings.EdgeSoftness);
            _propertyBlock.SetFloat(StreakLifeFadeId, settings.LifeFade);
            _propertyBlock.SetFloat(StreakAlphaStepsId, settings.AlphaSteps);
            _propertyBlock.SetFloat(StreakCurveAmplitudeId, settings.CurveAmplitude);
            _propertyBlock.SetFloat(StreakCurveFrequencyId, settings.CurveFrequency);
            _propertyBlock.SetFloat(StreakCurveScrollId, settings.CurveScrollSpeed);
            _propertyBlock.SetFloat(StreakCurveVerticalId, settings.CurveVertical);
            _propertyBlock.SetFloat(StreakTimeId, time);

            Material material = settings.Material;
            if (!material.enableInstancing)
                material.enableInstancing = true;

            float padding = longestTravel + settings.CurveAmplitude * 2f + settings.WidthMaximum;
            Bounds worldBounds = new(
                volumeCentre,
                new Vector3(
                    settings.AreaSize.x + padding * 2f,
                    Mathf.Max(0.1f, settings.HeightRange.y - settings.HeightRange.x) + padding * 2f,
                    settings.AreaSize.y + padding * 2f));

            RenderParams renderParams = new(material)
            {
                camera = targetCamera,
                layer = gameObject.layer,
                matProps = _propertyBlock,
                worldBounds = worldBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            Graphics.RenderMeshInstanced(in renderParams, _ribbonMesh, 0, _matrices, streakCount);
        }

        /// <summary>
        /// The streaks are unlit and additive, so on their own they glow as brightly at midnight as
        /// at noon; their colour follows the scene light. Linear space, as the shader reads it.
        /// </summary>
        private static Vector4 GetLitColor(PixelWindStreakSettings settings)
        {
            Color color = settings.Color.linear;
            Color tint = PixelMath.SkyLightTint(settings.LightResponse);
            return new Vector4(color.r * tint.r, color.g * tint.g, color.b * tint.b, color.a);
        }

        [ContextMenu("Reseed Streaks")]
        private void Reseed()
        {
            _random = new System.Random(9173);
            _streaks = System.Array.Empty<Streak>();
        }

        /// <summary>
        /// Editor previews do not advance <see cref="Time.deltaTime"/>, so the streaks are stepped
        /// from the same wall clock the cloud animation uses.
        /// </summary>
        private float AdvanceClock(out float time)
        {
            time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
            if (!_hasLastTime)
            {
                _hasLastTime = true;
                _lastTime = time;
                return 0f;
            }

            float deltaTime = Mathf.Clamp(time - _lastTime, 0f, MaximumDeltaTime);
            _lastTime = time;
            return deltaTime;
        }

        private void EnsurePool(PixelWindStreakSettings settings, int streakCount)
        {
            if (_streaks.Length == streakCount)
                return;

            Streak[] streaks = new Streak[streakCount];
            int carried = Mathf.Min(_streaks.Length, streakCount);
            for (int index = 0; index < carried; index++)
                streaks[index] = _streaks[index];
            for (int index = carried; index < streakCount; index++)
            {
                // A fresh streak starts at a random point in its own life, so growing the pool does
                // not line every new streak up on the same spawn edge.
                streaks[index] = Respawn(settings, 0f);
                streaks[index].Age = NextRange(0f, streaks[index].Lifetime);
            }

            _streaks = streaks;
            _matrices = new Matrix4x4[streakCount];
            _streakData = new Vector4[streakCount];
        }

        private Streak Respawn(PixelWindStreakSettings settings, float carriedAge)
        {
            float speedVariance = settings.Speed * settings.SpeedVariance;
            return new Streak
            {
                SpawnOffset = new Vector3(
                    NextRange(-settings.AreaSize.x, settings.AreaSize.x) * 0.5f,
                    NextRange(settings.HeightRange.x, settings.HeightRange.y)
                    - (settings.HeightRange.x + settings.HeightRange.y) * 0.5f,
                    NextRange(-settings.AreaSize.y, settings.AreaSize.y) * 0.5f),
                Age = Mathf.Max(0f, carriedAge),
                Lifetime = NextRange(settings.LifetimeMinimum, settings.LifetimeMaximum),
                Speed = Mathf.Max(0f, settings.Speed + NextRange(-speedVariance, speedVariance)),
                Length = NextRange(settings.LengthMinimum, settings.LengthMaximum),
                Width = NextRange(settings.WidthMinimum, settings.WidthMaximum),
                Seed = NextRange(0f, 1f)
            };
        }

        private Vector3 GetVolumeCentre(PixelWindStreakSettings settings, float groundHeight, Camera targetCamera)
        {
            Vector3 centre;
            if (!settings.FollowCamera || targetCamera == null)
                centre = transform.position;
            else if (!PixelMath.TryGetGroundFocus(targetCamera, groundHeight, out centre))
                centre = targetCamera.transform.position;

            centre.y = groundHeight + (settings.HeightRange.x + settings.HeightRange.y) * 0.5f;
            return centre;
        }

        private float NextRange(float minimum, float maximum)
        {
            _random ??= new System.Random(9173);
            return minimum + (float)_random.NextDouble() * (maximum - minimum);
        }

        private void EnsureMesh(int segments)
        {
            if (_ribbonMesh != null && _ribbonSegments == segments)
                return;

            ReleaseMesh();
            _ribbonMesh = CreateRibbon(segments);
            _ribbonSegments = segments;
        }

        private void ReleaseMesh()
        {
            CoreUtils.Destroy(_ribbonMesh);
            _ribbonMesh = null;
            _ribbonSegments = 0;
        }

        /// <summary>
        /// A flat strip along local X. The vertex shader bends it along the streak spine, so the
        /// only thing that matters here is having enough segments for the curve to read smoothly.
        /// </summary>
        private static Mesh CreateRibbon(int segments)
        {
            int steps = Mathf.Max(2, segments);
            Vector3[] vertices = new Vector3[(steps + 1) * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[steps * 6];

            for (int step = 0; step <= steps; step++)
            {
                float u = step / (float)steps;
                int vertex = step * 2;
                vertices[vertex] = new Vector3(u, -0.5f, 0f);
                vertices[vertex + 1] = new Vector3(u, 0.5f, 0f);
                uvs[vertex] = new Vector2(u, 0f);
                uvs[vertex + 1] = new Vector2(u, 1f);
            }

            int triangle = 0;
            for (int step = 0; step < steps; step++)
            {
                int origin = step * 2;
                triangles[triangle++] = origin;
                triangles[triangle++] = origin + 1;
                triangles[triangle++] = origin + 2;
                triangles[triangle++] = origin + 2;
                triangles[triangle++] = origin + 1;
                triangles[triangle++] = origin + 3;
            }

            Mesh mesh = new()
            {
                name = "Pixel Wind Streak Ribbon",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0f), new Vector3(1f, 1f, 1f));
            return mesh;
        }
    }
}
