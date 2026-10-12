using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PixelPipeline.Runtime
{
    /// <summary>
    /// Renders the scene from a camera mirrored through the water plane into a render texture at the
    /// internal pixel resolution, and publishes it as a global for <c>PixelPipeline/Water</c>.
    /// Because the mirrored camera keeps the projection of the source camera, points on the plane
    /// land on the same screen position in both, so the shader samples it with plain screen UVs.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class PixelWaterPlanarReflection : MonoBehaviour
    {
        [SerializeField] private PixelPipelineSettings settings;
        [Tooltip("Camera the reflection is built for. Falls back to Camera.main.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Transform whose up axis and position define the water plane. Defaults to this transform.")]
        [SerializeField] private Transform surfacePlane;

        private static readonly int ReflectionTextureId = Shader.PropertyToID("_PixelWaterReflection");
        private static readonly int ReflectionEnabledId = Shader.PropertyToID("_PixelWaterReflectionOn");
        private static readonly HashSet<Camera> ReflectionCameras = new();

        private Camera _reflectionCamera;
        private UniversalAdditionalCameraData _reflectionCameraData;
        private RenderTexture _reflectionTexture;

        /// <summary>Lets the pixel post-processing stack skip the mirrored camera.</summary>
        internal static bool IsReflectionCamera(Camera camera)
        {
            return camera != null && ReflectionCameras.Contains(camera);
        }

        private void OnDisable()
        {
            Disable();
        }

        private void LateUpdate()
        {
            if (settings == null || !settings.Water.EnablePlanarReflection)
            {
                Disable();
                return;
            }

            Camera source = ResolveSourceCamera();
            Transform plane = surfacePlane != null ? surfacePlane : transform;
            if (source == null || !source.isActiveAndEnabled)
            {
                Disable();
                return;
            }

            PixelWaterSettings water = settings.Water;
            Vector2Int internalResolution = settings.GetInternalResolution(source.aspect);
            int width = Mathf.Max(16, Mathf.RoundToInt(internalResolution.x * water.ReflectionResolutionScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(internalResolution.y * water.ReflectionResolutionScale));
            EnsureTexture(width, height, source.allowHDR);
            EnsureCamera();

            Vector3 planeNormal = plane.up;
            Vector3 planePosition = plane.position;
            Matrix4x4 reflectionMatrix = CalculateReflectionMatrix(
                planeNormal,
                planePosition + planeNormal * water.ReflectionPlaneOffset);

            _reflectionCamera.CopyFrom(source);
            _reflectionCamera.enabled = false;
            _reflectionCamera.targetTexture = _reflectionTexture;
            _reflectionCamera.cullingMask = water.ReflectionLayers.value & ~(1 << gameObject.layer);
            _reflectionCamera.useOcclusionCulling = false;
            _reflectionCamera.allowMSAA = false;
            _reflectionCamera.allowDynamicResolution = false;
            _reflectionCamera.clearFlags = source.clearFlags == CameraClearFlags.Nothing
                ? CameraClearFlags.SolidColor
                : source.clearFlags;

            _reflectionCameraData.renderType = CameraRenderType.Base;
            _reflectionCameraData.renderPostProcessing = false;
            _reflectionCameraData.antialiasing = AntialiasingMode.None;
            _reflectionCameraData.renderShadows = water.ReflectionCastsShadows;

            // Mirror the transform as well; culling and shadow selection still read it.
            Vector3 mirroredForward = reflectionMatrix.MultiplyVector(source.transform.forward);
            Vector3 mirroredUp = reflectionMatrix.MultiplyVector(source.transform.up);
            _reflectionCamera.transform.SetPositionAndRotation(
                reflectionMatrix.MultiplyPoint(source.transform.position),
                Quaternion.LookRotation(mirroredForward, mirroredUp));

            Matrix4x4 worldToCamera = source.worldToCameraMatrix * reflectionMatrix;
            _reflectionCamera.worldToCameraMatrix = worldToCamera;

            Matrix4x4 projection = source.projectionMatrix;
            if (water.ReflectionClipsBelowSurface)
            {
                Vector4 clipPlane = CameraSpacePlane(
                    worldToCamera,
                    planePosition,
                    planeNormal,
                    water.ReflectionPlaneOffset);

                // Looking along the surface rather than down at it, the oblique matrix degenerates:
                // depth stops depending on view depth entirely and most of the frustum ends up
                // outside it, which Unity reports every frame while it sets the render up. The water
                // is an edge-on sliver at that angle, so keep the plain mirrored projection.
                if (Mathf.Abs(clipPlane.z) > MinimumClipPlaneSlope)
                    projection = CalculateObliqueMatrix(projection, clipPlane);
            }

            _reflectionCamera.projectionMatrix = projection;

            // Oblique clipping turns the frustum into a slab parallel to the water plane, which is
            // not what culling and shadow selection want to reason about. Hand them the plain
            // mirrored frustum; the near plane still discards whatever sits below the surface.
            _reflectionCamera.cullingMatrix = source.projectionMatrix * worldToCamera;

            bool previousInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            try
            {
                UniversalRenderPipeline.SingleCameraRequest request = new() { destination = _reflectionTexture };
                if (RenderPipeline.SupportsRenderRequest(_reflectionCamera, request))
                    RenderPipeline.SubmitRenderRequest(_reflectionCamera, request);
                else
                    _reflectionCamera.Render();
            }
            finally
            {
                GL.invertCulling = previousInvertCulling;
            }

            Shader.SetGlobalTexture(ReflectionTextureId, _reflectionTexture);
            Shader.SetGlobalFloat(ReflectionEnabledId, 1f);
        }

        private void Disable()
        {
            Shader.SetGlobalFloat(ReflectionEnabledId, 0f);
            ReleaseResources();
        }

        private Camera ResolveSourceCamera()
        {
            if (targetCamera != null)
                return targetCamera;

            return Camera.main;
        }

        private void EnsureCamera()
        {
            if (_reflectionCamera != null)
                return;

            GameObject holder = new("PixelWater Reflection Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            _reflectionCamera = holder.AddComponent<Camera>();
            _reflectionCamera.enabled = false;
            _reflectionCameraData = holder.AddComponent<UniversalAdditionalCameraData>();
            ReflectionCameras.Add(_reflectionCamera);
        }

        private void EnsureTexture(int width, int height, bool allowHdr)
        {
            if (_reflectionTexture != null &&
                _reflectionTexture.width == width &&
                _reflectionTexture.height == height)
                return;

            ReleaseTexture();
            _reflectionTexture = new RenderTexture(
                width,
                height,
                24,
                allowHdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
            {
                name = "PixelWaterReflection",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };

            _reflectionTexture.Create();
        }

        private void ReleaseResources()
        {
            if (_reflectionCamera != null)
            {
                ReflectionCameras.Remove(_reflectionCamera);
                _reflectionCamera.targetTexture = null;
                CoreUtils.Destroy(_reflectionCamera.gameObject);
                _reflectionCamera = null;
                _reflectionCameraData = null;
            }

            ReleaseTexture();
        }

        private void ReleaseTexture()
        {
            if (_reflectionTexture == null)
                return;

            _reflectionTexture.Release();
            CoreUtils.Destroy(_reflectionTexture);
            _reflectionTexture = null;
        }

        private static Matrix4x4 CalculateReflectionMatrix(Vector3 planeNormal, Vector3 planePosition)
        {
            Vector3 normal = planeNormal.sqrMagnitude > 0.0001f ? planeNormal.normalized : Vector3.up;
            float distance = -Vector3.Dot(normal, planePosition);

            Matrix4x4 reflection = Matrix4x4.identity;
            reflection.m00 = 1f - 2f * normal.x * normal.x;
            reflection.m01 = -2f * normal.x * normal.y;
            reflection.m02 = -2f * normal.x * normal.z;
            reflection.m03 = -2f * distance * normal.x;

            reflection.m10 = -2f * normal.y * normal.x;
            reflection.m11 = 1f - 2f * normal.y * normal.y;
            reflection.m12 = -2f * normal.y * normal.z;
            reflection.m13 = -2f * distance * normal.y;

            reflection.m20 = -2f * normal.z * normal.x;
            reflection.m21 = -2f * normal.z * normal.y;
            reflection.m22 = 1f - 2f * normal.z * normal.z;
            reflection.m23 = -2f * distance * normal.z;

            return reflection;
        }

        private static Vector4 CameraSpacePlane(
            Matrix4x4 worldToCamera,
            Vector3 planePosition,
            Vector3 planeNormal,
            float offset)
        {
            Vector3 normal = planeNormal.sqrMagnitude > 0.0001f ? planeNormal.normalized : Vector3.up;
            Vector3 offsetPosition = planePosition + normal * offset;
            Vector3 cameraPosition = worldToCamera.MultiplyPoint(offsetPosition);
            Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(
                cameraNormal.x,
                cameraNormal.y,
                cameraNormal.z,
                -Vector3.Dot(cameraPosition, cameraNormal));
        }

        /// <summary>
        /// How far from edge on the surface has to be before the oblique matrix is worth building,
        /// measured as the view space slope of the clip plane.
        /// </summary>
        private const float MinimumClipPlaneSlope = 0.08f;

        // Without this the replacement far plane passes exactly through the frustum corner it is
        // fitted to. Nudging it out keeps that corner strictly inside, for the cost of a sliver of
        // depth range; the near plane stays on the clip plane whatever the scale.
        private const float ObliqueFarPlaneMargin = 1.01f;

        // Oblique near plane clipping. The generic form also covers the orthographic case, where the
        // fourth projection row is (0, 0, 0, 1). Derived from Eric Lengyel, public domain.
        private static Matrix4x4 CalculateObliqueMatrix(Matrix4x4 projection, Vector4 clipPlane)
        {
            Vector4 corner = projection.inverse * new Vector4(
                Sign(clipPlane.x),
                Sign(clipPlane.y),
                1f,
                1f);

            float denominator = Vector4.Dot(clipPlane, corner) * ObliqueFarPlaneMargin;
            if (Mathf.Abs(denominator) < 1e-6f)
                return projection;

            Vector4 scaled = clipPlane * (2f / denominator);
            projection.m20 = scaled.x - projection.m30;
            projection.m21 = scaled.y - projection.m31;
            projection.m22 = scaled.z - projection.m32;
            projection.m23 = scaled.w - projection.m33;
            return projection;
        }

        private static float Sign(float value)
        {
            if (value > 0f)
                return 1f;

            return value < 0f ? -1f : 0f;
        }
    }
}
