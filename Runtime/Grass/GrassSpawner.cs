using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    [DisallowMultipleComponent]
    public sealed class GrassSpawner : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private Mesh grassMesh;
        [SerializeField] private Material grassMaterial;
        [SerializeField, Min(0)] private int grassCount = 10000;
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows;

        [Header("Placement")]
        [SerializeField] private Bounds spawnArea = new(Vector3.zero, new Vector3(50f, 20f, 50f));
        [SerializeField] private LayerMask terrainLayer = ~0;
        [SerializeField, Min(1)] private int randomSeed = 12345;
        [SerializeField, Range(1, 20)] private int attemptsPerBlade = 4;
        [SerializeField, Min(1f)] private float spatialTileSize = 12f;

        [Header("Scale")]
        [SerializeField, Min(0.01f)] private float bladeWidth = 0.35f;
        [SerializeField, Min(0.01f)] private float minimumHeight = 0.8f;
        [SerializeField, Min(0.01f)] private float maximumHeight = 1.2f;

        private const int MaximumInstancesPerBatch = 1023;
        private static readonly int TerrainNormalId = Shader.PropertyToID("_TerrainNormal");

        private readonly List<GrassBatch> _batches = new();
        private readonly struct GrassInstance
        {
            public readonly Matrix4x4 Matrix;
            public readonly Vector4 Normal;
            public readonly Vector3 Position;

            public GrassInstance(Matrix4x4 matrix, Vector4 normal, Vector3 position)
            {
                Matrix = matrix;
                Normal = normal;
                Position = position;
            }
        }

        private sealed class GrassBatch
        {
            public Matrix4x4[] Matrices;
            public Bounds Bounds;
            public MaterialPropertyBlock PropertyBlock;
        }

        public int SpawnedCount { get; private set; }

        private void Start()
        {
            Regenerate();
        }

        private void Update()
        {
            if (grassMesh == null || grassMaterial == null || _batches.Count == 0)
                return;

            foreach (GrassBatch batch in _batches)
            {
                RenderParams renderParams = new(grassMaterial)
                {
                    layer = gameObject.layer,
                    matProps = batch.PropertyBlock,
                    worldBounds = batch.Bounds,
                    shadowCastingMode = shadowCastingMode,
                    receiveShadows = receiveShadows,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off
                };

                Graphics.RenderMeshInstanced(
                    in renderParams,
                    grassMesh,
                    0,
                    batch.Matrices,
                    batch.Matrices.Length);
            }
        }

        [ContextMenu("Regenerate Grass")]
        public void Regenerate()
        {
            _batches.Clear();
            SpawnedCount = 0;

            if (grassMesh == null || grassMaterial == null || grassCount <= 0)
            {
                if (Application.isPlaying)
                    Debug.LogWarning("[PixelPipeline] Grass mesh/material is missing or grass count is zero.", this);
                return;
            }

            if (!grassMaterial.enableInstancing)
            {
                Debug.LogWarning("[PixelPipeline] Enabling GPU instancing on the grass material.", grassMaterial);
                grassMaterial.enableInstancing = true;
            }

            Dictionary<Vector2Int, List<GrassInstance>> tiles = new();
            System.Random random = new(randomSeed);
            int maximumAttempts = Mathf.Max(grassCount, 1) * attemptsPerBlade;
            float minHeight = Mathf.Min(minimumHeight, maximumHeight);
            float maxHeight = Mathf.Max(minimumHeight, maximumHeight);

            for (int attempt = 0; attempt < maximumAttempts && SpawnedCount < grassCount; attempt++)
            {
                float localX = NextRange(random, spawnArea.min.x, spawnArea.max.x);
                float localZ = NextRange(random, spawnArea.min.z, spawnArea.max.z);
                Vector3 rayOrigin = transform.TransformPoint(new Vector3(localX, spawnArea.max.y, localZ));
                float rayDistance = Mathf.Max(0.01f, spawnArea.size.y);

                if (!Physics.Raycast(
                        rayOrigin,
                        Vector3.down,
                        out RaycastHit hit,
                        rayDistance,
                        terrainLayer,
                        QueryTriggerInteraction.Ignore))
                    continue;

                float height = NextRange(random, minHeight, maxHeight);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    hit.point,
                    Quaternion.identity,
                    new Vector3(bladeWidth, height, 1f));
                Vector4 normal = new(hit.normal.x, hit.normal.y, hit.normal.z, 0f);
                Vector2Int tile = new(
                    Mathf.FloorToInt(localX / spatialTileSize),
                    Mathf.FloorToInt(localZ / spatialTileSize));

                if (!tiles.TryGetValue(tile, out List<GrassInstance> instances))
                {
                    instances = new List<GrassInstance>();
                    tiles.Add(tile, instances);
                }

                instances.Add(new GrassInstance(matrix, normal, hit.point));
                SpawnedCount++;
            }

            foreach (List<GrassInstance> tileInstances in tiles.Values)
            {
                for (int start = 0; start < tileInstances.Count; start += MaximumInstancesPerBatch)
                {
                    int count = Mathf.Min(MaximumInstancesPerBatch, tileInstances.Count - start);
                    Matrix4x4[] matrices = new Matrix4x4[count];
                    Vector4[] normals = new Vector4[count];
                    Bounds bounds = new(tileInstances[start].Position, Vector3.zero);

                    for (int index = 0; index < count; index++)
                    {
                        GrassInstance instance = tileInstances[start + index];
                        matrices[index] = instance.Matrix;
                        normals[index] = instance.Normal;
                        bounds.Encapsulate(instance.Position);
                    }

                    bounds.Expand(new Vector3(bladeWidth * 2f, maxHeight * 2f, bladeWidth * 2f));
                    MaterialPropertyBlock propertyBlock = new();
                    propertyBlock.SetVectorArray(TerrainNormalId, normals);
                    _batches.Add(new GrassBatch
                    {
                        Matrices = matrices,
                        Bounds = bounds,
                        PropertyBlock = propertyBlock
                    });
                }
            }

            if (SpawnedCount < grassCount)
            {
                Debug.LogWarning(
                    $"[PixelPipeline] Placed {SpawnedCount}/{grassCount} grass blades. " +
                    "Increase Attempts Per Blade or check the terrain layer and spawn bounds.",
                    this);
            }
        }

        private static float NextRange(System.Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private void OnValidate()
        {
            grassCount = Mathf.Max(0, grassCount);
            attemptsPerBlade = Mathf.Max(1, attemptsPerBlade);
            spatialTileSize = Mathf.Max(1f, spatialTileSize);
            bladeWidth = Mathf.Max(0.01f, bladeWidth);
            minimumHeight = Mathf.Max(0.01f, minimumHeight);
            maximumHeight = Mathf.Max(0.01f, maximumHeight);
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.25f, 0.25f);
            Gizmos.DrawCube(spawnArea.center, spawnArea.size);
            Gizmos.color = new Color(0.2f, 1f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(spawnArea.center, spawnArea.size);
            Gizmos.matrix = previousMatrix;
        }
    }
}
