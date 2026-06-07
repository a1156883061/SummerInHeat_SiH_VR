#if OPENXR_BUILD
using System.Reflection;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrUiProjectionPlane
    {
        private const string ProjectionPlaneName = "UnityVRMod_UIProjectionPlane";
        private const string CaptureObjectName = "OpenXR_UIProjectionCapture";
        private const float PlaneDistanceMeters = 1.25f;
        private const float PlaneVerticalOffsetMeters = -0.08f;
        private const float PlaneWidthMeters = 1.6f;
        private const float RightHandPanelForwardOffsetMeters = 0.01f;
        private const float RightHandPanelUpOffsetMeters = 0.10f;
        private const float RightHandPanelRightOffsetMeters = -0.06f;
        private const float NguiProbeIntervalSeconds = 0.75f;
        private const int MaxProjectionTextureDimension = 1920;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const string ViewportFrameObjectName = "OpenXR_UIViewportFrame";
        private const float ViewportFrameLineWidth = 0.003f;
        private const float ViewportFrameExpandNormalized = 0.08f;
        private const string ResizeHandleBaseName = "OpenXR_UIResizeHandle_";
        private const float ResizeHandleRadiusMeters = 0.018f;
        private const float ResizeHandleDepthScale = 0.22f;
        private const float PixelRectLogIntervalSeconds = 1.0f;

        private static readonly Color ViewportFrameColor = new(1.0f, 0.82f, 0.20f, 0.95f);
        private static readonly Color ResizeHandleColor = new(1.0f, 0.95f, 0.55f, 0.95f);

        private Camera _mainCamera;
        private GameObject _vrRig;
        private GameObject _plane;
        private Material _planeMaterial;
        private RenderTexture _projectionTexture;
        private int _projectionWidth;
        private int _projectionHeight;
        private GameObject _captureObject;
        private NguiCaptureBehaviour _captureBehaviour;
        private bool _isInitialized;
        private float _nextNguiProbeTime;
        private bool _loggedMissingNguiCamera;
        private bool _isVisibilityManuallyToggled;
        private bool _isPanelAnchoredToRig;
        private Vector3 _anchoredLocalPos;
        private Quaternion _anchoredLocalRot;
        private float _lastAppliedPanelScale;
        private bool _hasManualPose;
        private Vector3 _manualWorldPos;
        private Quaternion _manualWorldRot;
        private GameObject _viewportFrameObject;
        private LineRenderer _viewportFrameLine;
        private Material _viewportFrameMaterial;
        private readonly GameObject[] _resizeHandles = new GameObject[4];
        private Material _resizeHandleMaterial;
        private float _nextPixelRectLogTime;
        private Rect _lastLoggedPixelRect;
        private bool _hasLoggedPixelRect;
        private float _runtimePanelScaleMultiplier;

        public void Initialize(GameObject vrRig, Camera mainCamera)
        {
            Teardown();

            _vrRig = vrRig;
            _mainCamera = mainCamera;
            _projectionWidth = 0;
            _projectionHeight = 0;
            _nextNguiProbeTime = 0f;
            _loggedMissingNguiCamera = false;
            _isPanelAnchoredToRig = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _hasManualPose = false;
            _manualWorldPos = Vector3.zero;
            _manualWorldRot = Quaternion.identity;
            _lastAppliedPanelScale = -1f;
            _nextPixelRectLogTime = 0f;
            _lastLoggedPixelRect = default;
            _hasLoggedPixelRect = false;
            _runtimePanelScaleMultiplier = 1f;

            CreateProjectionTextureIfNeeded(force: true);
            CreateProjectionPlane();
            CreateCaptureBehaviour();

            _isInitialized = _plane != null && _projectionTexture != null && _captureBehaviour != null;
            if (_isInitialized)
            {
                VRModCore.Log("[UI][OpenXR] NGUI projection plane enabled.");
            }
            else
            {
                VRModCore.LogWarning("[UI][OpenXR] Failed to initialize NGUI projection plane.");
            }
        }

        public void Update(Camera mainCamera, bool togglePressed, bool hasRightHandPose, Vector3 rightHandWorldPos, Quaternion rightHandWorldRot)
        {
            if (!_isInitialized) return;
            if (mainCamera != null) _mainCamera = mainCamera;

            CreateProjectionTextureIfNeeded(force: false);
            UpdatePanelScaleIfNeeded();
            HandleAnchorToggle(togglePressed);
            UpdatePlanePose(hasRightHandPose, rightHandWorldPos, rightHandWorldRot);
            UpdatePlaneScale();
            LogPixelRectIfNeeded();
            UpdatePlaneVisibility();
        }

        public void Teardown()
        {
            _isInitialized = false;

            if (_captureObject != null) UnityEngine.Object.Destroy(_captureObject);
            _captureObject = null;
            _captureBehaviour = null;

            if (_plane != null) UnityEngine.Object.Destroy(_plane);
            _plane = null;

            if (_viewportFrameObject != null) UnityEngine.Object.Destroy(_viewportFrameObject);
            _viewportFrameObject = null;

            if (_planeMaterial != null) UnityEngine.Object.Destroy(_planeMaterial);
            _planeMaterial = null;
            if (_viewportFrameMaterial != null) UnityEngine.Object.Destroy(_viewportFrameMaterial);
            _viewportFrameMaterial = null;
            _viewportFrameLine = null;
            if (_resizeHandleMaterial != null) UnityEngine.Object.Destroy(_resizeHandleMaterial);
            _resizeHandleMaterial = null;
            for (int i = 0; i < _resizeHandles.Length; i++)
            {
                _resizeHandles[i] = null;
            }

            if (_projectionTexture != null)
            {
                _projectionTexture.Release();
                UnityEngine.Object.Destroy(_projectionTexture);
                _projectionTexture = null;
            }

            _vrRig = null;
            _mainCamera = null;
            _projectionWidth = 0;
            _projectionHeight = 0;
            _nextNguiProbeTime = 0f;
            _loggedMissingNguiCamera = false;
            _isPanelAnchoredToRig = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _hasManualPose = false;
            _manualWorldPos = Vector3.zero;
            _manualWorldRot = Quaternion.identity;
            _lastAppliedPanelScale = -1f;
            _nextPixelRectLogTime = 0f;
            _lastLoggedPixelRect = default;
            _hasLoggedPixelRect = false;
            _runtimePanelScaleMultiplier = 1f;
        }

        internal bool TryGetPlaneTransform(out Transform planeTransform)
        {
            planeTransform = _plane != null ? _plane.transform : null;
            return planeTransform != null;
        }

        internal void SetManualPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            _hasManualPose = true;
            _manualWorldPos = worldPosition;
            _manualWorldRot = worldRotation;
        }

        internal void ClearManualPose()
        {
            _hasManualPose = false;
        }

        internal void SetEdgeHighlight(bool visible)
        {
            if (_viewportFrameObject == null) return;
            bool shouldShow = visible && _plane != null && _plane.activeInHierarchy;
            if (_viewportFrameObject.activeSelf != shouldShow)
            {
                _viewportFrameObject.SetActive(shouldShow);
            }
            SetResizeHandlesVisible(shouldShow);
        }

        internal bool TryRaycastResizeHandle(Ray ray, out int handleIndex, out float hitDistance)
        {
            handleIndex = -1;
            hitDistance = 0f;
            if (_plane == null || !_plane.activeInHierarchy) return false;

            float bestDistance = float.MaxValue;
            for (int i = 0; i < _resizeHandles.Length; i++)
            {
                GameObject handle = _resizeHandles[i];
                if (handle == null || !handle.activeInHierarchy) continue;
                if (!TryRaySphere(ray, handle.transform.position, ResizeHandleRadiusMeters, out float distance)) continue;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                handleIndex = i;
            }

            if (handleIndex < 0) return false;
            hitDistance = bestDistance;
            return true;
        }

        internal float GetPanelScale()
        {
            return GetPanelScaleMultiplier();
        }

        internal void SetPanelScale(float panelScale)
        {
            float configuredScale = Mathf.Max(0.01f, ConfigManager.OpenXR_UiPanelScale?.Value ?? 1.0f);
            float clampedFinalScale = Mathf.Clamp(panelScale, MinPanelScale, MaxPanelScale);
            _runtimePanelScaleMultiplier = clampedFinalScale / configuredScale;
            _lastAppliedPanelScale = clampedFinalScale;
            UpdatePlaneScale();
        }

        private void CreateProjectionPlane()
        {
            if (_vrRig == null || _projectionTexture == null) return;

            _plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _plane.name = ProjectionPlaneName;
            _plane.transform.SetParent(_vrRig.transform, false);
            _plane.layer = _vrRig.layer;

            object collider = _plane.GetComponent("Collider");
            if (collider != null) UnityEngine.Object.Destroy(collider as UnityEngine.Object);

            var renderer = _plane.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _planeMaterial = new Material(shader);
            _planeMaterial.mainTexture = _projectionTexture;
            renderer.sharedMaterial = _planeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            CreateViewportFrameIfNeeded();
            CreateResizeHandlesIfNeeded();

            UpdatePlanePose(false, default, Quaternion.identity);
            UpdatePlaneScale();
        }

        private void CreateCaptureBehaviour()
        {
            if (_projectionTexture == null) return;

            _captureObject = new GameObject(CaptureObjectName);
            if (_vrRig != null) _captureObject.transform.SetParent(_vrRig.transform, false);

            _captureBehaviour = _captureObject.AddComponent<NguiCaptureBehaviour>();
            _captureBehaviour.SetTarget(_projectionTexture);
            _captureBehaviour.enabled = true;
        }

        private void CreateProjectionTextureIfNeeded(bool force)
        {
            int targetWidth = Mathf.Clamp(Screen.width, 640, MaxProjectionTextureDimension);
            int targetHeight = Mathf.Clamp(Screen.height, 360, MaxProjectionTextureDimension);
            if (!force && targetWidth == _projectionWidth && targetHeight == _projectionHeight && _projectionTexture != null)
            {
                return;
            }

            if (_projectionTexture != null)
            {
                _projectionTexture.Release();
                UnityEngine.Object.Destroy(_projectionTexture);
                _projectionTexture = null;
            }

            _projectionTexture = new RenderTexture(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "OpenXR_UIProjectionTexture",
                useMipMap = false,
                autoGenerateMips = false
            };
            _projectionTexture.Create();

            _projectionWidth = targetWidth;
            _projectionHeight = targetHeight;

            if (_planeMaterial != null) _planeMaterial.mainTexture = _projectionTexture;
            if (_captureBehaviour != null) _captureBehaviour.SetTarget(_projectionTexture);

            UpdatePlaneScale();
        }

        private void UpdatePlanePose(bool hasRightHandPose, Vector3 rightHandWorldPos, Quaternion rightHandWorldRot)
        {
            if (_plane == null) return;

            if (_hasManualPose)
            {
#if MONO
                _plane.transform.position = _manualWorldPos;
                _plane.transform.rotation = _manualWorldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(_manualWorldPos, _manualWorldRot);
#endif
                return;
            }

            if (_isPanelAnchoredToRig && _vrRig != null)
            {
                Vector3 anchoredWorldPos;
                Quaternion anchoredWorldRot;
                if (IsWorldAnchorMode())
                {
                    // 世界固定模式：直接使用保存的世界坐标
                    anchoredWorldPos = _anchoredLocalPos;
                    anchoredWorldRot = _anchoredLocalRot;
                }
                else
                {
                    // Rig 跟随模式：将局部坐标转换回世界坐标
                    anchoredWorldPos = _vrRig.transform.TransformPoint(_anchoredLocalPos);
                    anchoredWorldRot = _vrRig.transform.rotation * _anchoredLocalRot;
                }
#if MONO
                _plane.transform.position = anchoredWorldPos;
                _plane.transform.rotation = anchoredWorldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(anchoredWorldPos, anchoredWorldRot);
#endif
                return;
            }

            if (hasRightHandPose)
            {
                Vector3 localOffset = new(RightHandPanelRightOffsetMeters, RightHandPanelUpOffsetMeters, RightHandPanelForwardOffsetMeters);
                Vector3 worldPos = rightHandWorldPos + (rightHandWorldRot * localOffset);
                Vector3 forward = rightHandWorldRot * Vector3.forward;
                Vector3 up = rightHandWorldRot * Vector3.up;
                Quaternion worldRot = Quaternion.LookRotation(forward, up);
#if MONO
                _plane.transform.position = worldPos;
                _plane.transform.rotation = worldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(worldPos, worldRot);
#endif
                return;
            }

            Transform referenceTransform = _mainCamera != null ? _mainCamera.transform : _vrRig?.transform;
            if (referenceTransform == null) return;

            Vector3 fallbackForward = referenceTransform.forward;
            Vector3 fallbackUp = referenceTransform.up;
            if (fallbackForward.sqrMagnitude < 0.001f) fallbackForward = Vector3.forward;
            if (fallbackUp.sqrMagnitude < 0.001f) fallbackUp = Vector3.up;

            Vector3 fallbackWorldPos = referenceTransform.position + fallbackForward.normalized * PlaneDistanceMeters + fallbackUp.normalized * PlaneVerticalOffsetMeters;
            Quaternion fallbackWorldRot = Quaternion.LookRotation(fallbackForward.normalized, fallbackUp.normalized);
#if MONO
            _plane.transform.position = fallbackWorldPos;
            _plane.transform.rotation = fallbackWorldRot;
#elif CPP
            _plane.transform.SetPositionAndRotation(fallbackWorldPos, fallbackWorldRot);
#endif
        }

        private void UpdatePlaneScale()
        {
            if (_plane == null) return;

            Rect viewport = GetPrimaryNguiViewportNormalized();
            float baseWidth = PlaneWidthMeters * GetPanelScaleMultiplier();
            float baseHeight = baseWidth / Mathf.Max(0.1f, _projectionHeight > 0 ? (float)_projectionWidth / _projectionHeight : (16f / 9f));
            float width = Mathf.Max(0.02f, baseWidth * Mathf.Max(0.01f, viewport.width));
            float height = Mathf.Max(0.02f, baseHeight * Mathf.Max(0.01f, viewport.height));
            _plane.transform.localScale = new Vector3(width, height, 1f);

            if (_viewportFrameLine != null)
            {
                float border = 0.5f + ViewportFrameExpandNormalized;
                _viewportFrameLine.SetPosition(0, new Vector3(-border, -border, 0.002f));
                _viewportFrameLine.SetPosition(1, new Vector3(-border, border, 0.002f));
                _viewportFrameLine.SetPosition(2, new Vector3(border, border, 0.002f));
                _viewportFrameLine.SetPosition(3, new Vector3(border, -border, 0.002f));
                _viewportFrameLine.SetPosition(4, new Vector3(-border, -border, 0.002f));
            }

            float corner = 0.5f + ViewportFrameExpandNormalized;
            float sx = Mathf.Max(0.0001f, _plane.transform.localScale.x);
            float sy = Mathf.Max(0.0001f, _plane.transform.localScale.y);
            float localDiameterX = (ResizeHandleRadiusMeters * 2f) / sx;
            float localDiameterY = (ResizeHandleRadiusMeters * 2f) / sy;
            float localDiameter = Mathf.Max(0.01f, Mathf.Min(localDiameterX, localDiameterY));
            UpdateResizeHandle(0, new Vector3(-corner, -corner, 0.003f), localDiameter);
            UpdateResizeHandle(1, new Vector3(-corner, corner, 0.003f), localDiameter);
            UpdateResizeHandle(2, new Vector3(corner, corner, 0.003f), localDiameter);
            UpdateResizeHandle(3, new Vector3(corner, -corner, 0.003f), localDiameter);
        }

        private void CreateViewportFrameIfNeeded()
        {
            if (_plane == null || _viewportFrameObject != null) return;

            _viewportFrameObject = new GameObject(ViewportFrameObjectName);
            _viewportFrameObject.transform.SetParent(_plane.transform, false);
            _viewportFrameLine = _viewportFrameObject.AddComponent<LineRenderer>();
            _viewportFrameLine.useWorldSpace = false;
            _viewportFrameLine.loop = false;
            _viewportFrameLine.positionCount = 5;
            _viewportFrameLine.startWidth = ViewportFrameLineWidth;
            _viewportFrameLine.endWidth = ViewportFrameLineWidth;
            _viewportFrameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _viewportFrameLine.receiveShadows = false;
            _viewportFrameLine.numCapVertices = 4;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _viewportFrameMaterial = new Material(shader);
            _viewportFrameMaterial.color = ViewportFrameColor;
            _viewportFrameLine.material = _viewportFrameMaterial;
            _viewportFrameObject.SetActive(false);
        }

        private void CreateResizeHandlesIfNeeded()
        {
            if (_plane == null) return;
            if (_resizeHandleMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _resizeHandleMaterial = new Material(shader);
                _resizeHandleMaterial.color = ResizeHandleColor;
            }

            for (int i = 0; i < _resizeHandles.Length; i++)
            {
                if (_resizeHandles[i] != null) continue;

                GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handle.name = ResizeHandleBaseName + i;
                handle.transform.SetParent(_plane.transform, false);
                handle.layer = _plane.layer;
                object collider = handle.GetComponent("Collider");
                if (collider != null) UnityEngine.Object.Destroy(collider as UnityEngine.Object);
                var renderer = handle.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = _resizeHandleMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                _resizeHandles[i] = handle;
            }

            SetResizeHandlesVisible(false);
        }

        private void UpdateResizeHandle(int index, Vector3 localPosition, float localDiameter)
        {
            if (index < 0 || index >= _resizeHandles.Length) return;
            GameObject handle = _resizeHandles[index];
            if (handle == null) return;

#if MONO
            handle.transform.localPosition = localPosition;
            handle.transform.localScale = new Vector3(localDiameter, localDiameter, localDiameter * ResizeHandleDepthScale);
#elif CPP
            handle.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
            handle.transform.localScale = new Vector3(localDiameter, localDiameter, localDiameter * ResizeHandleDepthScale);
#endif
        }

        private void SetResizeHandlesVisible(bool visible)
        {
            for (int i = 0; i < _resizeHandles.Length; i++)
            {
                GameObject handle = _resizeHandles[i];
                if (handle == null) continue;
                if (handle.activeSelf != visible)
                {
                    handle.SetActive(visible);
                }
            }
        }

        private static bool TryRaySphere(Ray ray, Vector3 center, float radius, out float distance)
        {
            distance = 0f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - (radius * radius);
            float h = (b * b) - c;
            if (h < 0f) return false;
            float sqrtH = Mathf.Sqrt(h);
            float t = -b - sqrtH;
            if (t <= 0f)
            {
                t = -b + sqrtH;
                if (t <= 0f) return false;
            }

            distance = t;
            return true;
        }

        private void LogPixelRectIfNeeded()
        {
            if (Time.time < _nextPixelRectLogTime) return;
            _nextPixelRectLogTime = Time.time + PixelRectLogIntervalSeconds;
            if (CameraJudge.IsHybrid2DSceneActive()) return;
            if (!TryGetPrimaryNguiPixelRect(out Rect pixelRect)) return;

            if (_hasLoggedPixelRect &&
                Mathf.Abs(pixelRect.x - _lastLoggedPixelRect.x) < 0.5f &&
                Mathf.Abs(pixelRect.y - _lastLoggedPixelRect.y) < 0.5f &&
                Mathf.Abs(pixelRect.width - _lastLoggedPixelRect.width) < 0.5f &&
                Mathf.Abs(pixelRect.height - _lastLoggedPixelRect.height) < 0.5f)
            {
                return;
            }

            _lastLoggedPixelRect = pixelRect;
            _hasLoggedPixelRect = true;

            float normW = Screen.width > 0 ? pixelRect.width / Screen.width : 1f;
            float normH = Screen.height > 0 ? pixelRect.height / Screen.height : 1f;
            VRModCore.Log($"[UI][OpenXR] UICamera.pixelRect px=({pixelRect.x:F0},{pixelRect.y:F0},{pixelRect.width:F0},{pixelRect.height:F0}) norm=({normW:F3},{normH:F3})");
        }

        private void UpdatePanelScaleIfNeeded()
        {
            float targetScale = GetPanelScaleMultiplier();
            if (Mathf.Abs(targetScale - _lastAppliedPanelScale) <= 0.0001f) return;

            _lastAppliedPanelScale = targetScale;
            UpdatePlaneScale();
            VRModCore.Log($"[UI][OpenXR] Panel scale set to {targetScale:F2}x");
        }

        private float GetPanelScaleMultiplier()
        {
            float configuredScale = ConfigManager.OpenXR_UiPanelScale?.Value ?? 1.0f;
            float runtimeScale = Mathf.Max(0.01f, _runtimePanelScaleMultiplier);
            return Mathf.Clamp(configuredScale * runtimeScale, MinPanelScale, MaxPanelScale);
        }

        private Rect GetPrimaryNguiViewportNormalized()
        {
            if (CameraJudge.IsHybrid2DSceneActive())
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            if (Screen.width <= 0 || Screen.height <= 0) return new Rect(0f, 0f, 1f, 1f);
            if (!TryGetPrimaryNguiPixelRect(out Rect pixelRect)) return new Rect(0f, 0f, 1f, 1f);

            float xMin = Mathf.Clamp(pixelRect.xMin / Screen.width, 0f, 1f);
            float yMin = Mathf.Clamp(pixelRect.yMin / Screen.height, 0f, 1f);
            float xMax = Mathf.Clamp(pixelRect.xMax / Screen.width, 0f, 1f);
            float yMax = Mathf.Clamp(pixelRect.yMax / Screen.height, 0f, 1f);
            float w = Mathf.Max(0f, xMax - xMin);
            float h = Mathf.Max(0f, yMax - yMin);
            if (w < 0.01f || h < 0.01f) return new Rect(0f, 0f, 1f, 1f);

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void HandleAnchorToggle(bool togglePressed)
        {
            if (!togglePressed) return;

            // B short-press should control follow mode; clear drag-fixed override first.
            _hasManualPose = false;
            _isPanelAnchoredToRig = !_isPanelAnchoredToRig;
            if (_isPanelAnchoredToRig)
            {
                CaptureAnchoredPoseFromCurrent();
                string anchorDesc = IsWorldAnchorMode() ? "fixed in world" : "follows rig movement";
                VRModCore.Log($"[UI][OpenXR] Panel mode: Anchored ({anchorDesc}).");
            }
            else
            {
                VRModCore.Log("[UI][OpenXR] Panel mode: Right-hand follow.");
            }
        }

        private static bool IsWorldAnchorMode()
        {
            return (ConfigManager.OpenXR_UiPanelAnchorMode?.Value ?? UiPanelAnchorMode.World) == UiPanelAnchorMode.World;
        }

        private void CaptureAnchoredPoseFromCurrent()
        {
            if (_vrRig == null || _plane == null) return;

            if (IsWorldAnchorMode())
            {
                // 世界固定模式：保存世界坐标，面板不跟随 VR Rig 移动
                _anchoredLocalPos = _plane.transform.position;
                _anchoredLocalRot = _plane.transform.rotation;
            }
            else
            {
                // Rig 跟随模式：保存相对于 VR Rig 的局部坐标（旧行为）
                _anchoredLocalPos = _vrRig.transform.InverseTransformPoint(_plane.transform.position);
                _anchoredLocalRot = Quaternion.Inverse(_vrRig.transform.rotation) * _plane.transform.rotation;
            }
        }

        private void UpdatePlaneVisibility()
        {
            if (_plane == null) return;
            if (Time.time < _nextNguiProbeTime) return;

            _nextNguiProbeTime = Time.time + NguiProbeIntervalSeconds;

            bool isHybrid2dScene = CameraJudge.IsHybrid2DSceneActive();
            bool hasSourceCamera = isHybrid2dScene ? HasActiveHybridSourceCamera() : HasActiveNguiCamera();
            if (!hasSourceCamera)
            {
                if (!_loggedMissingNguiCamera)
                {
                    _loggedMissingNguiCamera = true;
                    VRModCore.LogWarning(
                        isHybrid2dScene
                            ? "[UI][OpenXR][Hybrid2D] No active source camera found. Projection plane hidden."
                            : "[UI][OpenXR][NGUI] No active UICamera found. Projection plane hidden.");
                }
            }
            else
            {
                _loggedMissingNguiCamera = false;
            }

            if (_isVisibilityManuallyToggled) return;

            if (_plane.activeSelf != hasSourceCamera)
            {
                _plane.SetActive(hasSourceCamera);
            }
        }

        internal bool ToggleVisibility()
        {
            if (_plane == null) return false;
            if (CameraJudge.IsHybrid2DSceneActive()) return false;
            _isVisibilityManuallyToggled = !_isVisibilityManuallyToggled;
            bool nowVisible = !_plane.activeSelf;
            _plane.SetActive(nowVisible);
            VRModCore.Log($"[UI][OpenXR] Panel manually toggled: {(nowVisible ? "Visible" : "Hidden")}");
            return nowVisible;
        }

        private static bool HasActiveHybridSourceCamera()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.targetTexture != null) continue;
                if (ShouldIgnoreHybridSourceCamera(cam)) continue;
                return true;
            }

            return false;
        }

        private static bool HasActiveNguiCamera()
        {
            Type nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (nguiUiCameraType == null) return false;

            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(nguiUiCameraType) != null) return true;
            }

            return false;
        }

        private static bool ShouldIgnoreHybridSourceCamera(Camera cam)
        {
            if (cam == null) return true;
            if ((cam.hideFlags & HideFlags.HideAndDontSave) != 0) return true;

            string name = cam.name;
            return name.IndexOf("UnityVRMod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OpenXR_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OpenVR_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("XrVrCamera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetPrimaryNguiPixelRect(out Rect pixelRect)
        {
            pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Type nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (nguiUiCameraType == null) return false;

            Camera bestCamera = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(nguiUiCameraType) == null) continue;

                if (bestCamera == null || cam.depth > bestDepth)
                {
                    bestCamera = cam;
                    bestDepth = cam.depth;
                }
            }

            if (bestCamera == null) return false;
            pixelRect = bestCamera.pixelRect;
            return true;
        }

        private static Type ResolveTypeAnyAssembly(string fullTypeName)
        {
            Type type = Type.GetType(fullTypeName, false);
            if (type != null) return type;

            type = Type.GetType($"{fullTypeName}, Assembly-CSharp", false);
            if (type != null) return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullTypeName, false);
                if (type != null) return type;
            }

            return null;
        }

        private sealed class NguiCaptureBehaviour : MonoBehaviour
        {
            private const float SourceRefreshIntervalSeconds = 0.75f;

            private RenderTexture _target;
            private Camera _captureCamera;
            private Camera _sourceCamera;
            private readonly List<Camera> _hybridSourceCameras = [];
            private Type _nguiUiCameraType;
            private float _nextSourceRefreshTime;
            private bool _bindingsResolved;
            private bool _loggedMissingSource;
            private bool _lastFrameHybridMode;

            internal void SetTarget(RenderTexture target)
            {
                _target = target;
                if (_captureCamera != null) _captureCamera.targetTexture = _target;
            }

            private void LateUpdate()
            {
                if (_target == null || !_target.IsCreated()) return;
                if (!EnsureBindings()) return;

                EnsureCaptureCamera();
                RefreshSourceCameraIfNeeded();

                if (_captureCamera == null)
                {
                    ClearTarget();
                    return;
                }

                if (_lastFrameHybridMode)
                {
                    if (_hybridSourceCameras.Count == 0)
                    {
                        ClearTarget();
                        return;
                    }

                    RenderFromHybridSourceCameras();
                    return;
                }

                if (_sourceCamera == null)
                {
                    ClearTarget();
                    return;
                }

                RenderFromSourceCamera();
            }

            private void OnDestroy()
            {
                if (_captureCamera != null) UnityEngine.Object.Destroy(_captureCamera.gameObject);
                _captureCamera = null;
                _sourceCamera = null;
                _hybridSourceCameras.Clear();
            }

            private bool EnsureBindings()
            {
                if (_bindingsResolved) return true;

                _nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
                _bindingsResolved = true;
                return true;
            }

            private void EnsureCaptureCamera()
            {
                if (_captureCamera != null) return;

                GameObject cameraObject = new("OpenXR_UICaptureCamera");
                cameraObject.transform.SetParent(transform, false);
                _captureCamera = cameraObject.AddComponent<Camera>();
                _captureCamera.enabled = false;
                _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _captureCamera.targetTexture = _target;
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = Color.clear;
                _captureCamera.allowHDR = false;
                _captureCamera.allowMSAA = false;
            }

            private void RefreshSourceCameraIfNeeded()
            {
                bool isHybridCapture = CameraJudge.IsHybrid2DSceneActive();
                _lastFrameHybridMode = isHybridCapture;

                if (Time.time < _nextSourceRefreshTime &&
                    ((isHybridCapture && _hybridSourceCameras.Count > 0) ||
                     (!isHybridCapture && _sourceCamera != null && _sourceCamera.enabled && _sourceCamera.gameObject.activeInHierarchy)))
                {
                    return;
                }

                _nextSourceRefreshTime = Time.time + SourceRefreshIntervalSeconds;
                _hybridSourceCameras.Clear();

                if (isHybridCapture)
                {
                    foreach (Camera cam in Camera.allCameras)
                    {
                        if (ShouldIncludeHybridSourceCamera(cam))
                        {
                            _hybridSourceCameras.Add(cam);
                        }
                    }

                    _hybridSourceCameras.Sort(static (left, right) =>
                    {
                        int depthCompare = left.depth.CompareTo(right.depth);
                        if (depthCompare != 0) return depthCompare;
                        return string.Compare(left.name, right.name, StringComparison.Ordinal);
                    });

                    if (_hybridSourceCameras.Count == 0)
                    {
                        if (!_loggedMissingSource)
                        {
                            _loggedMissingSource = true;
                            VRModCore.LogWarning("[UI][OpenXR][Hybrid2D] No active source camera available for projection capture.");
                        }
                    }
                    else
                    {
                        _loggedMissingSource = false;
                    }

                    _sourceCamera = null;
                    return;
                }

                if (_nguiUiCameraType == null)
                {
                    if (!_loggedMissingSource)
                    {
                        _loggedMissingSource = true;
                        VRModCore.LogWarning("[UI][OpenXR][NGUI] UICamera type not found. NGUI projection capture disabled.");
                    }
                    _sourceCamera = null;
                    return;
                }

                Camera best = null;
                float bestDepth = float.MinValue;
                foreach (Camera cam in Camera.allCameras)
                {
                    if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                    if (cam.GetComponent(_nguiUiCameraType) == null) continue;
                    if (cam == _captureCamera) continue;

                    if (best == null || cam.depth >= bestDepth)
                    {
                        best = cam;
                        bestDepth = cam.depth;
                    }
                }

                _sourceCamera = best;
                if (_sourceCamera == null)
                {
                    if (!_loggedMissingSource)
                    {
                        _loggedMissingSource = true;
                        VRModCore.LogWarning("[UI][OpenXR][NGUI] No active UICamera available for projection capture.");
                    }
                }
                else
                {
                    _loggedMissingSource = false;
                }
            }

            private bool ShouldIncludeHybridSourceCamera(Camera cam)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) return false;
                if (cam.targetTexture != null) return false;
                if (cam == _captureCamera) return false;
                return !ShouldIgnoreHybridSourceCamera(cam);
            }

            private void RenderFromSourceCamera()
            {
                if (_captureCamera == null || _sourceCamera == null || _target == null) return;

                _captureCamera.CopyFrom(_sourceCamera);
                _captureCamera.enabled = false;
                _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                _captureCamera.targetTexture = _target;
                _captureCamera.clearFlags = CameraClearFlags.SolidColor;
                _captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _captureCamera.allowHDR = false;
                _captureCamera.allowMSAA = false;

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0)
                {
                    int uiMask = 1 << uiLayer;
                    if ((_sourceCamera.cullingMask & uiMask) != 0)
                    {
                        _captureCamera.cullingMask = uiMask;
                    }
                }

                _captureCamera.Render();
            }

            private void RenderFromHybridSourceCameras()
            {
                if (_captureCamera == null || _target == null || _hybridSourceCameras.Count == 0) return;

                ClearTarget();
                for (int i = 0; i < _hybridSourceCameras.Count; i++)
                {
                    Camera source = _hybridSourceCameras[i];
                    if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;

                    _captureCamera.CopyFrom(source);
                    _captureCamera.enabled = false;
                    _captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                    _captureCamera.targetTexture = _target;
                    _captureCamera.Render();
                }
            }

            private void ClearTarget()
            {
                if (_target == null) return;

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previous;
            }
        }
    }
}
#endif
