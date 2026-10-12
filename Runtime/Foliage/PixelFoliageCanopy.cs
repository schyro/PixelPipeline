using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Generates the leaf-card mesh that PixelPipeline/Foliage renders. Cards are scattered over the
    /// surface of a canopy shape - a sphere, a blob, any closed mesh - and pushed a random distance
    /// inwards, so the crown reads as a volume of leaves. Every card stores its centre, a lighting
    /// normal rounded over the whole shape, and how deep inside the crown it sits; the shader turns
    /// each card towards the camera.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PixelFoliageCanopy : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Closed mesh describing the crown volume, in this object's local space. The built-in sphere is used when empty.")]
        [SerializeField] private Mesh canopyShape;
        [Tooltip("Number of leaf cards. Each is one quad.")]
        [SerializeField, Range(1, 8000)] private int cardCount = 450;
        [Tooltip("Random card width in metres, before the object scale.")]
        [SerializeField] private Vector2 cardSize = new(0.35f, 0.55f);
        [Tooltip("How far into the crown cards may sink, as a fraction of its radius. Deeper cards fill gaps and are shaded darker.")]
        [SerializeField, Range(0f, 1f)] private float depthSpread = 0.35f;
        [Tooltip("0 uses the shape's own surface normal, 1 a perfectly rounded normal from the crown centre. Rounder normals give broad, clean light bands.")]
        [SerializeField, Range(0f, 1f)] private float normalRoundness = 0.8f;
        [Tooltip("Cards facing further down than this are dropped, which flattens the underside of the crown.")]
        [SerializeField, Range(-1f, 1f)] private float minimumNormalY = -0.75f;
        [SerializeField] private int randomSeed = 1337;

        [Header("Wind")]
        [Tooltip("Wind flexibility of the lowest cards. The top of the crown always moves fully.")]
        [SerializeField, Range(0f, 1f)] private float baseFlexibility = 0.35f;

        private Mesh _mesh;
        private bool _rebuildRequested;

        public Mesh GeneratedMesh => _mesh;

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh == _mesh)
                meshFilter.sharedMesh = null;

            DestroyMesh();
        }

        private void OnValidate()
        {
            cardCount = Mathf.Clamp(cardCount, 1, 8000);
            cardSize.x = Mathf.Max(0.01f, cardSize.x);
            cardSize.y = Mathf.Max(cardSize.x, cardSize.y);

            // Assigning a mesh from OnValidate is not allowed, so the rebuild waits for the next Update.
            _rebuildRequested = true;
        }

        private void Update()
        {
            if (_rebuildRequested || _mesh == null)
                Rebuild();
        }

        [ContextMenu("Rebuild Foliage")]
        public void Rebuild()
        {
            _rebuildRequested = false;
            Mesh shape = canopyShape != null ? canopyShape : GetBuiltinSphere();
            if (shape == null || !shape.isReadable)
            {
                Debug.LogWarning("[PixelPipeline] Foliage canopy shape is missing or not readable (enable Read/Write on its import settings).", this);
                return;
            }

            if (_mesh == null)
            {
                // Regenerated on load, so it is never written into the scene.
                _mesh = new Mesh { name = "Pixel Foliage Cards", hideFlags = HideFlags.DontSave };
            }

            BuildCards(shape, _mesh);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void BuildCards(Mesh shape, Mesh target)
        {
            Vector3[] shapeVertices = shape.vertices;
            Vector3[] shapeNormals = shape.normals;
            int[] shapeTriangles = shape.triangles;
            bool hasNormals = shapeNormals != null && shapeNormals.Length == shapeVertices.Length;

            Bounds shapeBounds = shape.bounds;
            Vector3 centre = shapeBounds.center;
            Vector3 extents = Vector3.Max(shapeBounds.extents, Vector3.one * 0.001f);

            // Area-weighted triangle picking keeps the density even on unevenly tessellated shapes.
            int triangleCount = shapeTriangles.Length / 3;
            float[] cumulativeArea = new float[triangleCount];
            float totalArea = 0f;
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                Vector3 a = shapeVertices[shapeTriangles[triangle * 3]];
                Vector3 b = shapeVertices[shapeTriangles[triangle * 3 + 1]];
                Vector3 c = shapeVertices[shapeTriangles[triangle * 3 + 2]];
                totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulativeArea[triangle] = totalArea;
            }

            var vertices = new List<Vector3>(cardCount * 4);
            var normals = new List<Vector3>(cardCount * 4);
            var uvs = new List<Vector2>(cardCount * 4);
            var cardData = new List<Vector4>(cardCount * 4);
            var colors = new List<Color>(cardCount * 4);
            var indices = new List<int>(cardCount * 6);

            System.Random random = new(randomSeed);
            float largestCard = cardSize.y;
            int attempts = cardCount * 4;

            for (int attempt = 0; attempt < attempts && vertices.Count < cardCount * 4; attempt++)
            {
                int triangle = FindTriangle(cumulativeArea, (float)random.NextDouble() * totalArea);
                int i0 = shapeTriangles[triangle * 3];
                int i1 = shapeTriangles[triangle * 3 + 1];
                int i2 = shapeTriangles[triangle * 3 + 2];

                float u = (float)random.NextDouble();
                float v = (float)random.NextDouble();
                if (u + v > 1f)
                {
                    u = 1f - u;
                    v = 1f - v;
                }

                float w = 1f - u - v;
                Vector3 surface = shapeVertices[i0] * w + shapeVertices[i1] * u + shapeVertices[i2] * v;
                Vector3 surfaceNormal = hasNormals
                    ? (shapeNormals[i0] * w + shapeNormals[i1] * u + shapeNormals[i2] * v).normalized
                    : Vector3.Cross(shapeVertices[i1] - shapeVertices[i0], shapeVertices[i2] - shapeVertices[i0]).normalized;

                // The gradient of the bounding ellipsoid, which is what "rounded" means for a stretched crown.
                Vector3 offset = surface - centre;
                Vector3 roundNormal = new Vector3(
                    offset.x / (extents.x * extents.x),
                    offset.y / (extents.y * extents.y),
                    offset.z / (extents.z * extents.z)).normalized;
                Vector3 normal = Vector3.Slerp(surfaceNormal, roundNormal, normalRoundness).normalized;
                if (normal.y < minimumNormalY)
                    continue;

                float depth = (float)random.NextDouble() * depthSpread;
                Vector3 position = Vector3.Lerp(surface, centre, depth);
                float occlusion = depthSpread > 0.0001f ? 1f - depth / depthSpread : 1f;
                // The underside of the crown gets less sky, too.
                occlusion *= Mathf.Lerp(0.55f, 1f, normal.y * 0.5f + 0.5f);

                float height01 = Mathf.InverseLerp(shapeBounds.min.y, shapeBounds.max.y, position.y);
                float flexibility = Mathf.Lerp(baseFlexibility, 1f, height01);
                float seed = (float)random.NextDouble();
                float halfSize = Mathf.Lerp(cardSize.x, cardSize.y, (float)random.NextDouble()) * 0.5f;

                int start = vertices.Count;
                AddCorner(vertices, normals, uvs, cardData, colors, position, normal, new Vector2(0f, 0f), -halfSize, -halfSize, seed, flexibility, occlusion);
                AddCorner(vertices, normals, uvs, cardData, colors, position, normal, new Vector2(1f, 0f), halfSize, -halfSize, seed, flexibility, occlusion);
                AddCorner(vertices, normals, uvs, cardData, colors, position, normal, new Vector2(0f, 1f), -halfSize, halfSize, seed, flexibility, occlusion);
                AddCorner(vertices, normals, uvs, cardData, colors, position, normal, new Vector2(1f, 1f), halfSize, halfSize, seed, flexibility, occlusion);
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start + 3);
                indices.Add(start + 1);
            }

            target.Clear();
            target.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            target.SetVertices(vertices);
            target.SetNormals(normals);
            target.SetUVs(0, uvs);
            target.SetUVs(1, cardData);
            target.SetColors(colors);
            target.SetTriangles(indices, 0, false);

            // Vertices only hold card centres; the bounds must also contain the expanded quads and the
            // wind sway, or the crown pops out of view at the screen edges.
            Bounds bounds = shapeBounds;
            bounds.Expand(largestCard * 2f + 0.5f);
            target.bounds = bounds;
        }

        private static void AddCorner(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Vector4> cardData,
            List<Color> colors,
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            float cornerX,
            float cornerY,
            float seed,
            float flexibility,
            float occlusion)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            cardData.Add(new Vector4(cornerX, cornerY, seed, flexibility));
            colors.Add(new Color(occlusion, 0f, 0f, 1f));
        }

        private static int FindTriangle(float[] cumulativeArea, float value)
        {
            int low = 0;
            int high = cumulativeArea.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (cumulativeArea[middle] < value)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private static Mesh GetBuiltinSphere()
        {
            return Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        }

        private void DestroyMesh()
        {
            CoreUtils.Destroy(_mesh);
            _mesh = null;
        }
    }
}
