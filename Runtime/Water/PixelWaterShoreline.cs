using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Bakes the horizontal distance from every point of a water surface to the nearest shore, and
    /// hands it to <c>PixelPipeline/Water</c> for the shore foam. The shore is wherever a visible mesh
    /// crosses the water plane, so the result depends on the scene alone: foam has the same width on
    /// every bank and around every rock, whatever the camera angle.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PixelWaterShoreline : MonoBehaviour
    {
        [Tooltip("Texels per metre of the distance map. About one per internal pixel is plenty.")]
        [SerializeField, Range(8, 128)] private int texelsPerMetre = 40;
        [Tooltip("Meshes on these layers count as shore where they cross the water plane. The surface's own layer is always ignored.")]
        [SerializeField] private LayerMask shoreLayers = ~0;
        [Tooltip("Treat the edge of the water mesh as shore too, for surfaces that end without geometry around them.")]
        [SerializeField] private bool meshEdgeIsShore = true;
        [Tooltip("How far below the water plane geometry is considered, in metres.")]
        [SerializeField, Min(0.1f)] private float probeDepth = 50f;
        // Serialised so a player build keeps the shader; nothing else references it.
        [SerializeField, HideInInspector] private Shader maskShader;

        private const int MaximumResolution = 1024;
        private const float MaximumDistance = 1000f;
        private const string MaskShaderName = "Hidden/PixelPipeline/WaterShoreMask";

        private static readonly int ShoreMapId = Shader.PropertyToID("_PixelWaterShoreMap");
        private static readonly int ShoreMapOnId = Shader.PropertyToID("_PixelWaterShoreMapOn");
        private static readonly int WorldToShoreMapId = Shader.PropertyToID("_PixelWaterWorldToShoreMap");
        private static readonly int ShoreMaskUpId = Shader.PropertyToID("_ShoreMaskUp");

        private Texture2D _distanceMap;
        private MaterialPropertyBlock _propertyBlock;
        private Matrix4x4 _bakedLocalToWorld;
        private bool _rebakeRequested;

        public Texture2D DistanceMap => _distanceMap;

        private void OnEnable()
        {
            Bake();
        }

        private void OnDisable()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.SetPropertyBlock(null);

            CoreUtils.Destroy(_distanceMap);
            _distanceMap = null;
        }

        private void Reset()
        {
            maskShader = Shader.Find(MaskShaderName);
        }

        private void OnValidate()
        {
            if (maskShader == null)
                maskShader = Shader.Find(MaskShaderName);

            // Rendering is not allowed from OnValidate, so the bake waits for the next Update.
            _rebakeRequested = true;
        }

        private void Update()
        {
            // Moving or scaling the surface in the editor moves its shoreline with it.
            if (_rebakeRequested || _distanceMap == null || transform.localToWorldMatrix != _bakedLocalToWorld)
                Bake();
        }

        /// <summary>
        /// Rebuilds the distance map. Call it after moving the geometry around the water at runtime;
        /// moving the water surface itself rebakes on its own.
        /// </summary>
        [ContextMenu("Rebake Shoreline")]
        public void Bake()
        {
            _rebakeRequested = false;
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            Shader shader = maskShader != null ? maskShader : Shader.Find(MaskShaderName);
            if (mesh == null || shader == null)
                return;

            Bounds localBounds = mesh.bounds;
            Vector3 scale = transform.lossyScale;
            float worldWidth = Mathf.Abs(localBounds.size.x * scale.x);
            float worldDepth = Mathf.Abs(localBounds.size.z * scale.z);
            int width = Mathf.Clamp(Mathf.CeilToInt(worldWidth * texelsPerMetre), 2, MaximumResolution);
            int height = Mathf.Clamp(Mathf.CeilToInt(worldDepth * texelsPerMetre), 2, MaximumResolution);

            bool[] shore = RenderShoreMask(shader, localBounds, width, height, worldWidth, worldDepth);
            if (meshEdgeIsShore)
                MarkBorder(shore, width, height);
            double[] distances = DistanceTransform(shore, width, height);

            // Texels are square in world space to within the rounding above, so one size converts both
            // axes. Clamped so a surface without any shore stays inside the half-float range.
            float texelSize = 0.5f * (worldWidth / width + worldDepth / height);
            var pixels = new Color[distances.Length];
            for (int index = 0; index < distances.Length; index++)
            {
                float distance = Mathf.Min((float)System.Math.Sqrt(distances[index]) * texelSize, MaximumDistance);
                pixels[index] = new Color(distance, 0f, 0f, 0f);
            }

            if (_distanceMap == null || _distanceMap.width != width || _distanceMap.height != height)
            {
                CoreUtils.Destroy(_distanceMap);
                _distanceMap = new Texture2D(width, height, TextureFormat.RHalf, false, true)
                {
                    name = "Pixel Water Shore Distance",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            _distanceMap.SetPixels(pixels);
            _distanceMap.Apply(false);
            _bakedLocalToWorld = transform.localToWorldMatrix;

            _propertyBlock ??= new MaterialPropertyBlock();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(ShoreMapId, _distanceMap);
            _propertyBlock.SetFloat(ShoreMapOnId, 1f);
            _propertyBlock.SetMatrix(WorldToShoreMapId, GetLocalToMap(localBounds) * transform.worldToLocalMatrix);
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        // Local XZ inside the mesh bounds to [0, 1] map coordinates, in the xz of the result.
        private static Matrix4x4 GetLocalToMap(Bounds localBounds)
        {
            return Matrix4x4.Scale(new Vector3(1f / localBounds.size.x, 1f, 1f / localBounds.size.z))
                   * Matrix4x4.Translate(-localBounds.min);
        }

        /// <summary>
        /// Draws every nearby mesh from the water plane straight down, with everything above the plane
        /// cut off by the near plane. A texel is inside a solid when the first surface below it faces
        /// down, or when more surfaces below it face down than up; see WaterShoreMask.shader.
        /// </summary>
        private bool[] RenderShoreMask(Shader shader, Bounds localBounds, int width, int height, float worldWidth, float worldDepth)
        {
            Vector3 up = transform.up;
            Vector3 centre = transform.TransformPoint(localBounds.center);
            // Map u runs along local X and v along local Z, the same frame GetLocalToMap uses.
            Quaternion lookDown = Quaternion.LookRotation(-up, transform.forward);
            if (Vector3.Dot(lookDown * Vector3.right, transform.right) < 0f)
                lookDown = Quaternion.LookRotation(-up, -transform.forward);

            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(centre, lookDown, Vector3.one).inverse;
            Matrix4x4 projection = Matrix4x4.Ortho(-worldWidth * 0.5f, worldWidth * 0.5f, -worldDepth * 0.5f, worldDepth * 0.5f, 0f, probeDepth);

            Material material = CoreUtils.CreateEngineMaterial(shader);
            material.SetVector(ShoreMaskUpId, up);
            RenderTexture firstSurface = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RenderTexture surfaceCount = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var commands = new CommandBuffer { name = "PixelPipeline.WaterShoreMask" };

            try
            {
                Bounds worldBounds = GetComponent<MeshRenderer>().bounds;
                worldBounds.Expand(new Vector3(0f, probeDepth * 2f, 0f));
                var candidates = new List<MeshRenderer>(FindCandidates(worldBounds));

                for (int pass = 0; pass < 2; pass++)
                {
                    commands.SetRenderTarget(pass == 0 ? firstSurface : surfaceCount);
                    commands.ClearRenderTarget(true, true, Color.clear);
                    commands.SetViewProjectionMatrices(view, projection);
                    foreach (MeshRenderer candidate in candidates)
                    {
                        Mesh candidateMesh = candidate.GetComponent<MeshFilter>().sharedMesh;
                        for (int submesh = 0; submesh < candidateMesh.subMeshCount; submesh++)
                            commands.DrawRenderer(candidate, material, submesh, pass);
                    }
                }

                Graphics.ExecuteCommandBuffer(commands);

                Color[] first = ReadBack(firstSurface);
                Color[] counts = ReadBack(surfaceCount);
                var shore = new bool[width * height];
                for (int index = 0; index < shore.Length; index++)
                    shore[index] = first[index].r > 0.5f || counts[index].r > counts[index].g + 0.5f;
                return shore;
            }
            finally
            {
                commands.Release();
                RenderTexture.ReleaseTemporary(firstSurface);
                RenderTexture.ReleaseTemporary(surfaceCount);
                CoreUtils.Destroy(material);
            }
        }

        private static Color[] ReadBack(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            var readback = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true);
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            RenderTexture.active = previous;

            Color[] pixels = readback.GetPixels();
            CoreUtils.Destroy(readback);
            return pixels;
        }

        private IEnumerable<MeshRenderer> FindCandidates(Bounds worldBounds)
        {
            MeshRenderer own = GetComponent<MeshRenderer>();
            int layerMask = shoreLayers.value & ~(1 << gameObject.layer);
            foreach (MeshRenderer candidate in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (candidate == own || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                    continue;
                if ((layerMask & (1 << candidate.gameObject.layer)) == 0)
                    continue;
                if (!candidate.bounds.Intersects(worldBounds))
                    continue;

                MeshFilter filter = candidate.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                yield return candidate;
            }
        }

        private static void MarkBorder(bool[] shore, int width, int height)
        {
            for (int column = 0; column < width; column++)
            {
                shore[column] = true;
                shore[(height - 1) * width + column] = true;
            }

            for (int row = 0; row < height; row++)
            {
                shore[row * width] = true;
                shore[row * width + width - 1] = true;
            }
        }

        /// <summary>
        /// Exact squared Euclidean distance in texels to the nearest shore texel, by two passes of the
        /// one-dimensional lower-envelope transform (Felzenszwalb and Huttenlocher).
        /// </summary>
        private static double[] DistanceTransform(bool[] shore, int width, int height)
        {
            // Larger than any squared distance on the grid, yet small enough that adding a squared
            // coordinate to it stays exact.
            const double Infinity = 1e12;
            var grid = new double[width * height];
            for (int index = 0; index < grid.Length; index++)
                grid[index] = shore[index] ? 0.0 : Infinity;

            int longest = Mathf.Max(width, height);
            var line = new double[longest];
            var result = new double[longest];
            var vertices = new int[longest];
            var boundaries = new double[longest + 1];

            for (int column = 0; column < width; column++)
            {
                for (int row = 0; row < height; row++)
                    line[row] = grid[row * width + column];
                Transform1D(line, height, result, vertices, boundaries);
                for (int row = 0; row < height; row++)
                    grid[row * width + column] = result[row];
            }

            for (int row = 0; row < height; row++)
            {
                System.Array.Copy(grid, row * width, line, 0, width);
                Transform1D(line, width, result, vertices, boundaries);
                System.Array.Copy(result, 0, grid, row * width, width);
            }

            return grid;
        }

        private static void Transform1D(double[] input, int count, double[] output, int[] vertices, double[] boundaries)
        {
            int envelope = 0;
            vertices[0] = 0;
            boundaries[0] = double.NegativeInfinity;
            boundaries[1] = double.PositiveInfinity;

            for (int position = 1; position < count; position++)
            {
                double intersection = Intersection(input, position, vertices[envelope]);
                // The first boundary is minus infinity, so this always stops at the first parabola.
                while (intersection <= boundaries[envelope])
                {
                    envelope--;
                    intersection = Intersection(input, position, vertices[envelope]);
                }

                envelope++;
                vertices[envelope] = position;
                boundaries[envelope] = intersection;
                boundaries[envelope + 1] = double.PositiveInfinity;
            }

            int current = 0;
            for (int position = 0; position < count; position++)
            {
                while (boundaries[current + 1] < position)
                    current++;
                int vertex = vertices[current];
                output[position] = (position - vertex) * (position - vertex) + input[vertex];
            }
        }

        // Where the parabolas rooted at two grid positions cross.
        private static double Intersection(double[] input, int position, int vertex)
        {
            return (input[position] + (double)position * position - (input[vertex] + (double)vertex * vertex))
                   / (2.0 * (position - vertex));
        }
    }
}
