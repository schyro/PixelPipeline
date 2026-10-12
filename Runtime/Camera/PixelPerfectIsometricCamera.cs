using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PixelPipeline.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PixelPerfectIsometricCamera : MonoBehaviour
    {
        [SerializeField] private PixelPipelineSettings settings;
        [SerializeField] private Camera controlledCamera;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Vector3 fixedRotation = new(20f, 45f, 0f);
        [SerializeField, Min(0f)] private float followSharpness = 12f;

        [Header("Optional Input")]
        [SerializeField] private bool enableOrbit = true;
        [SerializeField, Min(0f)] private float orbitSensitivity = 3f;
        [SerializeField, Range(1f, 89f)] private float minimumPitch = 10f;
        [SerializeField, Range(1f, 89f)] private float maximumPitch = 60f;
        [SerializeField] private bool enableZoom = true;
        [SerializeField, Min(0f)] private float zoomSpeed = 2f;
        [SerializeField, Min(0.01f)] private float minimumZoom = 3f;
        [SerializeField, Min(0.01f)] private float maximumZoom = 15f;

        private static readonly Dictionary<Camera, Vector2> PanOffsets = new();

        private Vector3 _unsnappedPosition;
        private float _pitch;
        private float _yaw;
        private InputAction _orbitButtonAction;
        private InputAction _lookDeltaAction;
        private InputAction _zoomAction;

        private void OnEnable()
        {
            _orbitButtonAction ??= new InputAction(
                "Pixel Camera Orbit",
                InputActionType.Button,
                "<Mouse>/rightButton");
            _lookDeltaAction ??= new InputAction(
                "Pixel Camera Look",
                InputActionType.Value,
                "<Pointer>/delta");
            _zoomAction ??= new InputAction(
                "Pixel Camera Zoom",
                InputActionType.Value,
                "<Mouse>/scroll/y");

            _orbitButtonAction.Enable();
            _lookDeltaAction.Enable();
            _zoomAction.Enable();
        }

        private void Awake()
        {
            ResolveCamera();
            _pitch = fixedRotation.x;
            _yaw = fixedRotation.y;
            _unsnappedPosition = followTarget != null ? followTarget.position : transform.position;

            if (controlledCamera != null && !controlledCamera.orthographic)
            {
                Debug.LogWarning("[PixelPipeline] Pixel-perfect camera requires Orthographic projection; enabling it now.", this);
                controlledCamera.orthographic = true;
            }
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (controlledCamera == null || settings == null)
                return;

            HandleInput();
            transform.rotation = Quaternion.Euler(_pitch, _yaw, fixedRotation.z);

            Vector3 targetPosition = followTarget != null ? followTarget.position : _unsnappedPosition;
            float blend = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
            _unsnappedPosition = Vector3.Lerp(_unsnappedPosition, targetPosition, blend);

            Vector2Int resolution = settings.GetInternalResolution(controlledCamera.aspect);
            float worldPerPixelX = controlledCamera.orthographicSize * 2f * controlledCamera.aspect / resolution.x;
            float worldPerPixelY = controlledCamera.orthographicSize * 2f / resolution.y;

            Vector3 cameraSpace = controlledCamera.transform.InverseTransformPoint(_unsnappedPosition);
            float snappedX = Mathf.Round(cameraSpace.x / worldPerPixelX) * worldPerPixelX;
            float snappedY = Mathf.Round(cameraSpace.y / worldPerPixelY) * worldPerPixelY;
            Vector2 error = new(cameraSpace.x - snappedX, cameraSpace.y - snappedY);

            transform.position = controlledCamera.transform.TransformPoint(new Vector3(snappedX, snappedY, cameraSpace.z));
            PanOffsets[controlledCamera] = new Vector2(
                error.x / (controlledCamera.orthographicSize * 2f * controlledCamera.aspect),
                error.y / (controlledCamera.orthographicSize * 2f));
        }

        private void OnDisable()
        {
            _orbitButtonAction?.Disable();
            _lookDeltaAction?.Disable();
            _zoomAction?.Disable();

            if (controlledCamera != null)
                PanOffsets.Remove(controlledCamera);
        }

        private void OnDestroy()
        {
            _orbitButtonAction?.Dispose();
            _lookDeltaAction?.Dispose();
            _zoomAction?.Dispose();
        }

        private void HandleInput()
        {
            if (enableOrbit && _orbitButtonAction.IsPressed())
            {
                Vector2 delta = _lookDeltaAction.ReadValue<Vector2>();
                _yaw += delta.x * orbitSensitivity * 0.02f;
                _pitch = Mathf.Clamp(
                    _pitch - delta.y * orbitSensitivity * 0.02f,
                    minimumPitch,
                    maximumPitch);
            }

            if (!enableZoom)
                return;

            float scroll = Mathf.Clamp(_zoomAction.ReadValue<float>() / 120f, -1f, 1f);
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                float min = Mathf.Min(minimumZoom, maximumZoom);
                float max = Mathf.Max(minimumZoom, maximumZoom);
                controlledCamera.orthographicSize = Mathf.Clamp(
                    controlledCamera.orthographicSize - scroll * zoomSpeed,
                    min,
                    max);
            }
        }

        private void ResolveCamera()
        {
            if (controlledCamera == null)
                controlledCamera = GetComponentInChildren<Camera>();
        }

        internal static Vector2 GetPanOffset(Camera camera)
        {
            return camera != null && PanOffsets.TryGetValue(camera, out Vector2 offset)
                ? offset
                : Vector2.zero;
        }
    }
}
