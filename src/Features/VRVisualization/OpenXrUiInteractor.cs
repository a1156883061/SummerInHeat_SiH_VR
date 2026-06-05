#if OPENXR_BUILD
using System.Reflection;
using System.Runtime.InteropServices;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrUiInteractor
    {
        private const float PointerFallbackDistanceMeters = 4.0f;
        private const float UiSurfaceMaxRayDistanceMeters = 8.0f;
        private const float UiSurfaceBoundsEpsilon = 0.001f;
        private const float UiSurfaceVisualEdgeExpandNormalized = 0.08f;
        private const float UiSurfaceLookupIntervalSeconds = 2.0f;
        private const float RayWidthMeters = 0.0018f;
        private const float CursorScaleMeters = 0.015f;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const string UiSurfaceName = "UnityVRMod_UIProjectionPlane";
        private const string SubCameraSurfaceName = "UnityVRMod_SubCameraProjectionPlane";
        private const string SubCameraUiLayerName = "UI_SubCamera";
        private const string SubCameraControlCameraName = "Camera_MainSub";
        private const float SubCameraWidgetLookupIntervalSeconds = 1.0f;
        private const float SubCameraDebugSourceLookupIntervalSeconds = 0.75f;
        private const float SubCameraControlCameraLookupIntervalSeconds = 0.75f;
        private const float SubCameraRectLogMinIntervalSeconds = 0.5f;
        private const float SubCameraRectLogDeltaPixels = 4.0f;
        private const float SubCameraProjectionRejectZ = -0.001f;
        private static readonly string[] SubCameraSourceNameTokens = { "SubCamera", "SubCam" };
        private static readonly string[] SubCameraNameTokens = { "UI_SubCamera", "SubCamera", "SubCam" };
        private static readonly string[] SubCameraPreferredWidgetNames = { "SabCameraWindow", "SubCameraWindow" };
        private const string UiRayLayerName = "UIRay";
        private const string RayObjectName = "OpenXR_UIRay";
        private const string CursorObjectName = "OpenXR_UICursor";
        private const string ControllerIconObjectName = "OpenXR_UITouchIcon";
        private const string SubCameraRectDebugPlaneObjectName = "UnityVRMod_SubCameraProjectionPlane";
        private const string SubCameraRectDebugContentObjectName = "UnityVRMod_SubCameraProjectionContent";
        private const string SubCameraRectDebugEdgeFrameObjectName = "UnityVRMod_SubCameraProjectionEdgeFrame";
        private const string SubCameraRectDebugOutlineObjectName = "UnityVRMod_SubCameraProjectionOutline";
        private const string SubCameraRectDebugResizeHandleBaseName = "OpenXR_SubCameraResizeHandle_";
        private const string SubCameraRectDebugDragHintObjectName = "OpenXR_SubCameraDragHint";
        private const int UiRayLayerFallbackMask = 1 << 19;
        private const float UiRaycastDistanceMeters = 200.0f;
        private const float UiRayHitNormalOffsetMeters = 0.003f;
        private const float UiRayTouchRadiusMeters = 0.032f;
        private const float ControllerIconScaleMeters = 0.032f;
        private const float ControllerIconRightOffsetMeters = 0.055f;
        private const float ControllerIconUpOffsetMeters = 0.005f;
        private const float SubCameraRectDebugPlaneWidthMeters = 0.90f;
        private const float SubCameraRectDebugPlaneRightHandForwardOffsetMeters = 0.16f;
        private const float SubCameraRectDebugPlaneRightHandRightOffsetMeters = 0.06f;
        private const float SubCameraRectDebugPlaneRightHandUpOffsetMeters = 0.02f;
        private const float SubCameraRectDebugEdgeFrameExpandNormalized = 0.03f;
        private const float SubCameraRectDebugResizeHandleRadiusMeters = 0.018f;
        private const float SubCameraRectDebugResizeHandleDepthScale = 0.22f;
        private const float SubCameraRectDebugDragHintSizeNormalized = 0.032f;
        private const float SubCameraRectDebugLineWidthMeters = 0.0018f;
        private const float SubCameraRectDebugEdgeFrameLineWidthMeters = 0.0032f;
        private const uint InputTypeMouse = 0;
        private const uint MouseEventFlagLeftDown = 0x0002;
        private const uint MouseEventFlagLeftUp = 0x0004;
        private static readonly bool IsWindowsPlatform = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static readonly Color RayVisibleColor = new(1.00f, 1.00f, 1.00f, 0.95f);
        private static readonly Color SubCameraRectDebugPlaneColor = new(0.4623f, 0.4623f, 0.4623f, 1.0f);
        private static readonly Color SubCameraRectDebugOutlineColor = new(1.0f, 0.20f, 0.20f, 1.0f);
        private static readonly Color SubCameraRectDebugEdgeFrameColor = new(1.0f, 0.88f, 0.30f, 0.95f);
        private static readonly Color SubCameraRectDebugResizeHandleColor = new(1.0f, 0.95f, 0.55f, 0.95f);
        private static readonly Color SubCameraRectDebugDragHintColor = new(1.0f, 0.80f, 0.20f, 0.95f);

        private Camera _mainCamera;
        private GameObject _uiSurface;
        private GameObject _subCameraSurface;
        private float _nextUiSurfaceLookupTime;
        private bool _loggedUiSurfaceFallback;
        private bool _loggedSubCameraRectFallback;
        private bool _wasTriggerPressed;
        private bool _pointerIsDown;
        private PointerSurfaceKind _activePointerSurface;
        private bool _resolvedUiRayLayerMask;
        private bool _loggedUiRayLayerFallback;
        private bool _physicsBindingsResolved;
        private bool _physicsBindingsFailed;
        private bool _loggedMouseInjectionUnsupportedPlatform;
        private bool _loggedMouseInjectionWindowMissing;
        private int _uiRayLayerMask;
        private MethodInfo _physicsRaycastAllMethod;
        private MethodInfo _physicsOverlapSphereMethod;
        private FieldInfo _raycastHitDistanceField;
        private PropertyInfo _raycastHitDistanceProperty;
        private FieldInfo _raycastHitColliderField;
        private PropertyInfo _raycastHitColliderProperty;
        private FieldInfo _raycastHitPointField;
        private PropertyInfo _raycastHitPointProperty;
        private FieldInfo _raycastHitNormalField;
        private PropertyInfo _raycastHitNormalProperty;
        private GameObject _rayObject;
        private LineRenderer _rayLine;
        private Material _rayMaterial;
        private GameObject _cursorObject;
        private Material _cursorMaterial;
        private float _lastAppliedCursorScale;
        private GameObject _controllerIconObject;
        private Material _controllerIconMaterial;
        private Texture2D _controllerIconTexture;
        private Texture2D _fixedTouchIconTexture;
        private GameObject _subCameraRectDebugPlaneObject;
        private Material _subCameraRectDebugPlaneMaterial;
        private GameObject _subCameraRectDebugContentObject;
        private Material _subCameraRectDebugContentMaterial;
        private GameObject _subCameraRectDebugEdgeFrameObject;
        private LineRenderer _subCameraRectDebugEdgeFrameLine;
        private Material _subCameraRectDebugEdgeFrameMaterial;
        private readonly GameObject[] _subCameraRectDebugResizeHandles = new GameObject[4];
        private Material _subCameraRectDebugResizeHandleMaterial;
        private GameObject _subCameraRectDebugDragHintObject;
        private Material _subCameraRectDebugDragHintMaterial;
        private float _subCameraRectDebugPanelScale = 1f;
        private bool _subCameraRectDebugEdgeHighlightRequested;
        private Texture _subCameraRectDebugSourceTexture;
        private Camera _subCameraRectDebugSourceCamera;
        private float _nextSubCameraRectDebugSourceLookupTime;
        private bool _loggedSubCameraRectDebugMissingSource;
        private Camera _subCameraControlCamera;
        private float _nextSubCameraControlCameraLookupTime;
        private bool _hasSubCameraControlCameraStateSample;
        private bool _lastSubCameraControlCameraEnabled;
        private LineRenderer _subCameraRectDebugOutline;
        private Material _subCameraRectDebugOutlineMaterial;
        private bool _subCameraRectDebugOutlineVisibleRequested;
        private bool _isSubCameraRectDebugPlaneAnchoredToRig;
        private Vector3 _subCameraRectDebugPlaneAnchoredLocalPos;
        private Quaternion _subCameraRectDebugPlaneAnchoredLocalRot;
        private bool _hasSubCameraRectDebugManualPose;
        private Vector3 _subCameraRectDebugManualWorldPos;
        private Quaternion _subCameraRectDebugManualWorldRot;
        private Type _zngControllerType;
        private MethodInfo _zngButtonDownMethod;
        private float _nextZngControllerLookupTime;
        private UnityEngine.Object _zngControllerInstance;
        private Type _uiWidgetType;
        private Type _uiDragResizeType;
        private Component _subCameraMappingWidget;
        private float _nextSubCameraWidgetLookupTime;
        private int _lastLoggedSubCameraWidgetInstanceId;
        private int _lastLoggedSubCameraRectWidgetInstanceId;
        private Rect _lastLoggedSubCameraRect;
        private bool _hasLoggedSubCameraRect;
        private float _nextSubCameraRectLogTime;
        private int _lastLoggedSubCameraProjectionCameraInstanceId;
        private int _lastLoggedSubCameraProjectionLayer;
        private int _lastLoggedSubCameraRectFailureHash;
        private float _nextSubCameraRectFailureLogTime;
        private bool _hasLastResolvedSubCameraRect;
        private Rect _lastResolvedSubCameraRect;
        private bool _hasFrozenSubCameraRectForDrag;
        private Rect _frozenSubCameraRectForDrag;
        private bool _subCameraPlaneDragActive;
        private Vector2 _subCameraPlaneDragLastLocal;
        private Vector2 _subCameraPlaneDragScreenPos;
        private float _subCameraPlaneDragPixelsPerLocalX;
        private float _subCameraPlaneDragPixelsPerLocalY;
        private bool _debugUiSurfaceTriggerBypassActive;
        private bool _hasSubCameraRectDebugUvRect;
        private Rect _subCameraRectDebugUvRect;

        private enum PointerSurfaceKind
        {
            None,
            UiProjection,
            SubCameraProjection
        }

        private readonly struct SurfaceRaycastResult(
            PointerSurfaceKind surfaceKind,
            bool hasScreenHit,
            bool constrainToSurface,
            bool hasVisualSurfaceHit,
            Vector2 screenPos,
            Vector3 hitWorldPoint,
            float hitDistance)
        {
            public PointerSurfaceKind SurfaceKind { get; } = surfaceKind;
            public bool HasScreenHit { get; } = hasScreenHit;
            public bool ConstrainToSurface { get; } = constrainToSurface;
            public bool HasVisualSurfaceHit { get; } = hasVisualSurfaceHit;
            public Vector2 ScreenPos { get; } = screenPos;
            public Vector3 HitWorldPoint { get; } = hitWorldPoint;
            public float HitDistance { get; } = hitDistance;
        }

        public void Initialize(Camera mainCamera)
        {
            DestroyRayVisuals();

            _mainCamera = mainCamera;
            _uiSurface = null;
            _subCameraSurface = null;
            _nextUiSurfaceLookupTime = 0f;
            _loggedUiSurfaceFallback = false;
            _loggedSubCameraRectFallback = false;
            _wasTriggerPressed = false;
            _pointerIsDown = false;
            _activePointerSurface = PointerSurfaceKind.None;
            _resolvedUiRayLayerMask = false;
            _loggedUiRayLayerFallback = false;
            _physicsBindingsResolved = false;
            _physicsBindingsFailed = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _uiRayLayerMask = 0;
            _physicsRaycastAllMethod = null;
            _physicsOverlapSphereMethod = null;
            _raycastHitDistanceField = null;
            _raycastHitDistanceProperty = null;
            _raycastHitColliderField = null;
            _raycastHitColliderProperty = null;
            _raycastHitPointField = null;
            _raycastHitPointProperty = null;
            _raycastHitNormalField = null;
            _raycastHitNormalProperty = null;
            _lastAppliedCursorScale = -1f;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = CreateFixedTouchIconTexture();
            _zngControllerType = null;
            _zngButtonDownMethod = null;
            _zngControllerInstance = null;
            _nextZngControllerLookupTime = 0f;
            _uiWidgetType = null;
            _uiDragResizeType = null;
            _subCameraMappingWidget = null;
            _nextSubCameraWidgetLookupTime = 0f;
            _lastLoggedSubCameraWidgetInstanceId = 0;
            _lastLoggedSubCameraRectWidgetInstanceId = 0;
            _lastLoggedSubCameraRect = default;
            _hasLoggedSubCameraRect = false;
            _nextSubCameraRectLogTime = 0f;
            _lastLoggedSubCameraProjectionCameraInstanceId = 0;
            _lastLoggedSubCameraProjectionLayer = -1;
            _lastLoggedSubCameraRectFailureHash = 0;
            _nextSubCameraRectFailureLogTime = 0f;
            _hasLastResolvedSubCameraRect = false;
            _lastResolvedSubCameraRect = default;
            _hasFrozenSubCameraRectForDrag = false;
            _frozenSubCameraRectForDrag = default;
            _subCameraPlaneDragActive = false;
            _subCameraPlaneDragLastLocal = Vector2.zero;
            _subCameraPlaneDragScreenPos = Vector2.zero;
            _subCameraPlaneDragPixelsPerLocalX = 0f;
            _subCameraPlaneDragPixelsPerLocalY = 0f;
            _debugUiSurfaceTriggerBypassActive = false;
            _hasSubCameraRectDebugUvRect = false;
            _subCameraRectDebugUvRect = default;
            _isSubCameraRectDebugPlaneAnchoredToRig = false;
            _subCameraRectDebugPlaneAnchoredLocalPos = Vector3.zero;
            _subCameraRectDebugPlaneAnchoredLocalRot = Quaternion.identity;
            _hasSubCameraRectDebugManualPose = false;
            _subCameraRectDebugManualWorldPos = Vector3.zero;
            _subCameraRectDebugManualWorldRot = Quaternion.identity;
            _subCameraRectDebugPanelScale = 1f;
            _subCameraRectDebugOutlineVisibleRequested = false;
            _subCameraRectDebugEdgeHighlightRequested = false;
            _subCameraRectDebugSourceTexture = null;
            _subCameraRectDebugSourceCamera = null;
            _nextSubCameraRectDebugSourceLookupTime = 0f;
            _loggedSubCameraRectDebugMissingSource = false;
            _subCameraControlCamera = null;
            _nextSubCameraControlCameraLookupTime = 0f;
            _hasSubCameraControlCameraStateSample = false;
            _lastSubCameraControlCameraEnabled = false;
        }

        public void Teardown()
        {
            ReleasePointerIfNeeded();
            DestroyRayVisuals();
            _mainCamera = null;
            _uiSurface = null;
            _subCameraSurface = null;
            _loggedUiSurfaceFallback = false;
            _loggedSubCameraRectFallback = false;
            _wasTriggerPressed = false;
            _activePointerSurface = PointerSurfaceKind.None;
            _resolvedUiRayLayerMask = false;
            _loggedUiRayLayerFallback = false;
            _physicsBindingsResolved = false;
            _physicsBindingsFailed = false;
            _loggedMouseInjectionUnsupportedPlatform = false;
            _loggedMouseInjectionWindowMissing = false;
            _uiRayLayerMask = 0;
            _physicsRaycastAllMethod = null;
            _physicsOverlapSphereMethod = null;
            _raycastHitDistanceField = null;
            _raycastHitDistanceProperty = null;
            _raycastHitColliderField = null;
            _raycastHitColliderProperty = null;
            _raycastHitPointField = null;
            _raycastHitPointProperty = null;
            _raycastHitNormalField = null;
            _raycastHitNormalProperty = null;
            _lastAppliedCursorScale = -1f;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = null;
            _zngControllerType = null;
            _zngButtonDownMethod = null;
            _zngControllerInstance = null;
            _nextZngControllerLookupTime = 0f;
            _uiWidgetType = null;
            _uiDragResizeType = null;
            _subCameraMappingWidget = null;
            _nextSubCameraWidgetLookupTime = 0f;
            _lastLoggedSubCameraWidgetInstanceId = 0;
            _lastLoggedSubCameraRectWidgetInstanceId = 0;
            _lastLoggedSubCameraRect = default;
            _hasLoggedSubCameraRect = false;
            _nextSubCameraRectLogTime = 0f;
            _lastLoggedSubCameraProjectionCameraInstanceId = 0;
            _lastLoggedSubCameraProjectionLayer = -1;
            _lastLoggedSubCameraRectFailureHash = 0;
            _nextSubCameraRectFailureLogTime = 0f;
            _hasLastResolvedSubCameraRect = false;
            _lastResolvedSubCameraRect = default;
            _hasFrozenSubCameraRectForDrag = false;
            _frozenSubCameraRectForDrag = default;
            _subCameraPlaneDragActive = false;
            _subCameraPlaneDragLastLocal = Vector2.zero;
            _subCameraPlaneDragScreenPos = Vector2.zero;
            _subCameraPlaneDragPixelsPerLocalX = 0f;
            _subCameraPlaneDragPixelsPerLocalY = 0f;
            _debugUiSurfaceTriggerBypassActive = false;
            _hasSubCameraRectDebugUvRect = false;
            _subCameraRectDebugUvRect = default;
            _isSubCameraRectDebugPlaneAnchoredToRig = false;
            _subCameraRectDebugPlaneAnchoredLocalPos = Vector3.zero;
            _subCameraRectDebugPlaneAnchoredLocalRot = Quaternion.identity;
            _hasSubCameraRectDebugManualPose = false;
            _subCameraRectDebugManualWorldPos = Vector3.zero;
            _subCameraRectDebugManualWorldRot = Quaternion.identity;
            _subCameraRectDebugPanelScale = 1f;
            _subCameraRectDebugOutlineVisibleRequested = false;
            _subCameraRectDebugEdgeHighlightRequested = false;
            _subCameraRectDebugSourceTexture = null;
            _subCameraRectDebugSourceCamera = null;
            _nextSubCameraRectDebugSourceLookupTime = 0f;
            _loggedSubCameraRectDebugMissingSource = false;
            _subCameraControlCamera = null;
            _nextSubCameraControlCameraLookupTime = 0f;
            _hasSubCameraControlCameraStateSample = false;
            _lastSubCameraControlCameraEnabled = false;
        }

        internal bool TryGetSubCameraProjectionPlaneTransform(out Transform planeTransform)
        {
            planeTransform = _subCameraRectDebugPlaneObject != null ? _subCameraRectDebugPlaneObject.transform : null;
            return planeTransform != null && _subCameraRectDebugPlaneObject.activeInHierarchy;
        }

        internal void SetSubCameraProjectionManualPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            _hasSubCameraRectDebugManualPose = true;
            _subCameraRectDebugManualWorldPos = worldPosition;
            _subCameraRectDebugManualWorldRot = worldRotation;
        }

        internal void ClearSubCameraProjectionManualPose()
        {
            _hasSubCameraRectDebugManualPose = false;
        }

        internal bool IsSubCameraProjectionMoveRingHit(Vector3 worldHitPoint)
        {
            if (_subCameraRectDebugPlaneObject == null || !_subCameraRectDebugPlaneObject.activeInHierarchy) return false;
            if (!TryGetProjectionPlaneUv(_subCameraRectDebugPlaneObject, worldHitPoint, out Vector2 uv)) return false;
            if (!_hasSubCameraRectDebugUvRect) return true;

            const float epsilon = 0.0001f;
            float outerMinX = _subCameraRectDebugUvRect.xMin - SubCameraRectDebugEdgeFrameExpandNormalized - epsilon;
            float outerMaxX = _subCameraRectDebugUvRect.xMax + SubCameraRectDebugEdgeFrameExpandNormalized + epsilon;
            float outerMinY = _subCameraRectDebugUvRect.yMin - SubCameraRectDebugEdgeFrameExpandNormalized - epsilon;
            float outerMaxY = _subCameraRectDebugUvRect.yMax + SubCameraRectDebugEdgeFrameExpandNormalized + epsilon;

            bool insideOuter = uv.x >= outerMinX && uv.x <= outerMaxX &&
                               uv.y >= outerMinY && uv.y <= outerMaxY;
            if (!insideOuter) return false;

            bool insideInner = uv.x >= (_subCameraRectDebugUvRect.xMin - epsilon) &&
                               uv.x <= (_subCameraRectDebugUvRect.xMax + epsilon) &&
                               uv.y >= (_subCameraRectDebugUvRect.yMin - epsilon) &&
                               uv.y <= (_subCameraRectDebugUvRect.yMax + epsilon);
            return !insideInner;
        }

        internal void SetSubCameraProjectionEdgeHighlight(bool visible)
        {
            _subCameraRectDebugEdgeHighlightRequested = visible;
            UpdateSubCameraRectDebugEdgeFrameVisibility();
        }

        internal bool TryRaycastSubCameraResizeHandle(Ray ray, out int handleIndex, out float hitDistance)
        {
            handleIndex = -1;
            hitDistance = float.MaxValue;
            if (_subCameraRectDebugPlaneObject == null || !_subCameraRectDebugPlaneObject.activeInHierarchy) return false;

            for (int i = 0; i < _subCameraRectDebugResizeHandles.Length; i++)
            {
                GameObject handle = _subCameraRectDebugResizeHandles[i];
                if (handle == null || !handle.activeInHierarchy) continue;
                if (!TryRaySphere(ray, handle.transform.position, SubCameraRectDebugResizeHandleRadiusMeters, out float distance)) continue;
                if (distance >= hitDistance) continue;

                hitDistance = distance;
                handleIndex = i;
            }

            return handleIndex >= 0;
        }

        internal float GetSubCameraProjectionPanelScale()
        {
            return Mathf.Clamp(_subCameraRectDebugPanelScale, MinPanelScale, MaxPanelScale);
        }

        internal void SetSubCameraProjectionPanelScale(float panelScale)
        {
            _subCameraRectDebugPanelScale = Mathf.Clamp(panelScale, MinPanelScale, MaxPanelScale);
        }

        internal void SetSubCameraProjectionOutlineVisible(bool visible)
        {
            _subCameraRectDebugOutlineVisibleRequested = visible;
            if (!visible && _subCameraRectDebugOutline != null)
            {
                _subCameraRectDebugOutline.enabled = false;
            }
        }

        public void Update(
            GameObject vrRig,
            bool hasPointerPose,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            bool triggerPressed,
            bool togglePressed,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            _debugUiSurfaceTriggerBypassActive = triggerPressed;

            if (_mainCamera == null && Camera.main != null)
            {
                _mainCamera = Camera.main;
            }

            if (vrRig == null)
            {
                HandleNoPointerFrame(triggerPressed);
                SetRayVisible(false);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            EnsureRayVisuals(vrRig);
            EnsureSubCameraRectDebugVisuals(vrRig);
            HandleSubCameraRectDebugPlaneAnchorToggle(togglePressed, vrRig, hasRightHandPose, rightHandWorldPos, rightHandWorldRot);
            UpdateSubCameraRectDebugVisuals(vrRig, hasRightHandPose, rightHandWorldPos, rightHandWorldRot);

            if (!hasPointerPose || rayDirection.sqrMagnitude <= 0.0001f)
            {
                HandleNoPointerFrame(triggerPressed);
                SetRayVisible(false);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            Vector3 normalizedRayDirection = rayDirection.normalized;
            bool justPressed = triggerPressed && !_wasTriggerPressed;
            bool justReleased = !triggerPressed && _wasTriggerPressed;
            PointerSurfaceKind preferredSurface = _pointerIsDown ? _activePointerSurface : PointerSurfaceKind.None;

            if (!TryGetPointerScreenPosition(rayOrigin, normalizedRayDirection, preferredSurface, out Vector2 pointerScreenPos, out Vector3 rayEndPoint, out bool hasVisualTarget, out PointerSurfaceKind pointerSurface))
            {
                HandleNoPointerFrame(triggerPressed);
                // Keep ray visible on the frame/edge ring, but do not inject UI input.
                UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: hasVisualTarget);
                _wasTriggerPressed = triggerPressed;
                return;
            }

            _ = TryInjectMousePointerPosition(pointerScreenPos);

            if (justPressed)
            {
                if (TrySendMouseLeftEvent(true, out int downError))
                {
                    _pointerIsDown = true;
                    _activePointerSurface = pointerSurface;
                    CaptureSubCameraRectFreezeOnPointerDown(pointerSurface);
                    if (pointerSurface == PointerSurfaceKind.SubCameraProjection)
                    {
                        BeginSubCameraPlaneDragTracking(rayOrigin, normalizedRayDirection, pointerScreenPos);
                    }
                    else
                    {
                        _subCameraPlaneDragActive = false;
                    }
                }
                else
                {
                    VRModCore.LogWarning($"[UI][OpenXR][Mouse] Failed to inject LeftDown (Win32Error={downError}).");
                }
            }

            if (_pointerIsDown && justReleased)
            {
                if (!TrySendMouseLeftEvent(false, out int upError))
                {
                    VRModCore.LogWarning($"[UI][OpenXR][Mouse] Failed to inject LeftUp (Win32Error={upError}).");
                }

                _pointerIsDown = false;
                _activePointerSurface = PointerSurfaceKind.None;
                _hasFrozenSubCameraRectForDrag = false;
                _subCameraPlaneDragActive = false;
            }

            // Ray/cursor visibility should depend on ray hit mapping, not OS mouse injection success.
            UpdateRayVisual(rayOrigin, rayEndPoint, hasTarget: true);
            _wasTriggerPressed = triggerPressed;
        }

        internal bool IsDraggingSubCameraProjection()
        {
            return _pointerIsDown && _activePointerSurface == PointerSurfaceKind.SubCameraProjection;
        }

        public bool UpdateUiRayTouch(GameObject vrRig, bool hasHandPose, Vector3 handWorldPos, Quaternion handWorldRot, bool triggerPressed)
        {
            if (vrRig == null)
            {
                SetControllerIconVisible(false);
                return false;
            }

            EnsureControllerIconVisual(vrRig);

            if (!hasHandPose)
            {
                SetControllerIconVisible(false);
                return false;
            }

            bool mappedIconHit = TryGetUiRayTouchInteraction(handWorldPos, out _, out string icon, out bool hasUiRayTouchHit);
            if (!hasUiRayTouchHit)
            {
                SetControllerIconVisible(false);
                return false;
            }

            // 图标颜色：无映射=白，有映射未触发=浅绿，触发=亮绿
            if (_controllerIconMaterial != null)
            {
                _controllerIconMaterial.color = mappedIconHit
                    ? (triggerPressed ? new Color(1f, 0.7f, 0.1f, 1f) : new Color(0.2f, 0.5f, 1f, 1f))
                    : Color.white;
            }

            UpdateControllerIconVisual(_fixedTouchIconTexture, handWorldPos, handWorldRot);

            // 直接检查扳机 + 图标匹配，不用边沿检测
            bool triggered = false;
            if (mappedIconHit && triggerPressed)
            {
                triggered = TryInvokeZngButtonDown(icon);
            }

            return triggered;
        }

        private void HandleNoPointerFrame(bool triggerPressedNow)
        {
            if (_pointerIsDown && !triggerPressedNow)
            {
                ReleasePointerIfNeeded();
            }
        }

        private bool TryGetPointerScreenPosition(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            PointerSurfaceKind preferredSurface,
            out Vector2 pointerScreenPos,
            out Vector3 rayEndPoint,
            out bool hasVisualTarget,
            out PointerSurfaceKind hitSurface)
        {
            pointerScreenPos = default;
            rayEndPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            hasVisualTarget = false;
            hitSurface = PointerSurfaceKind.None;

            if (preferredSurface == PointerSurfaceKind.SubCameraProjection &&
                _subCameraPlaneDragActive &&
                TryGetSubCameraPlaneDragScreenPosition(rayOrigin, rayDirection, out pointerScreenPos, out rayEndPoint, out hasVisualTarget))
            {
                hitSurface = PointerSurfaceKind.SubCameraProjection;
                return true;
            }

            bool allowUiOutOfBoundsMapping = preferredSurface == PointerSurfaceKind.UiProjection;
            bool allowSubCameraOutOfBoundsMapping = preferredSurface == PointerSurfaceKind.SubCameraProjection;
            SurfaceRaycastResult uiResult = TryGetUiProjectionSurfaceScreenPosition(rayOrigin, rayDirection, allowUiOutOfBoundsMapping);
            SurfaceRaycastResult subCameraResult = TryGetSubCameraSurfaceScreenPosition(rayOrigin, rayDirection, allowSubCameraOutOfBoundsMapping);
            if (TrySelectSurfaceResult(uiResult, subCameraResult, preferredSurface, out SurfaceRaycastResult selected))
            {
                pointerScreenPos = selected.ScreenPos;
                rayEndPoint = selected.HitWorldPoint;
                hasVisualTarget = true;
                hitSurface = selected.SurfaceKind;
                return true;
            }

            if (preferredSurface != PointerSurfaceKind.None)
            {
                SurfaceRaycastResult preferredResult = preferredSurface == PointerSurfaceKind.SubCameraProjection ? subCameraResult : uiResult;
                if (preferredResult.ConstrainToSurface)
                {
                    rayEndPoint = preferredResult.HitWorldPoint;
                    hasVisualTarget = preferredResult.HasVisualSurfaceHit;
                    hitSurface = preferredResult.SurfaceKind;
                    return false;
                }
            }

            if (TryGetConstrainedSurfaceResult(uiResult, subCameraResult, out SurfaceRaycastResult constrained))
            {
                rayEndPoint = constrained.HitWorldPoint;
                hasVisualTarget = constrained.HasVisualSurfaceHit;
                hitSurface = constrained.SurfaceKind;
                return false;
            }

            if (_mainCamera == null)
            {
                return false;
            }

            Vector3 fallbackPointWorld = rayOrigin + rayDirection * PointerFallbackDistanceMeters;
            rayEndPoint = fallbackPointWorld;
            Vector3 screenPoint = _mainCamera.WorldToScreenPoint(fallbackPointWorld);
            if (screenPoint.z <= 0f) return false;

            pointerScreenPos = new Vector2(
                Mathf.Clamp(screenPoint.x, 0f, Screen.width),
                Mathf.Clamp(screenPoint.y, 0f, Screen.height));
            hasVisualTarget = true;
            hitSurface = PointerSurfaceKind.None;

            if (!_loggedUiSurfaceFallback)
            {
                _loggedUiSurfaceFallback = true;
                VRModCore.LogWarning($"[UI][OpenXR] '{UiSurfaceName}'/'{SubCameraSurfaceName}' not found. Using main-camera screen fallback for UI ray mapping.");
            }

            return true;
        }

        private SurfaceRaycastResult TryGetUiProjectionSurfaceScreenPosition(Vector3 rayOrigin, Vector3 rayDirection, bool allowOutOfBoundsUvMapping)
        {
            Vector3 fallbackHitPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            RefreshUiSurfaceReferenceIfNeeded();
            GameObject debugSurface = (_subCameraRectDebugPlaneObject != null && _subCameraRectDebugPlaneObject.activeInHierarchy)
                ? _subCameraRectDebugPlaneObject
                : null;

            SurfaceRaycastResult baseUiResult = TryGetUiProjectionSurfaceScreenPositionOnSurface(
                _uiSurface,
                rayOrigin,
                rayDirection,
                allowOutOfBoundsUvMapping,
                applyDebugRectFilter: false);
            SurfaceRaycastResult debugUiResult = TryGetUiProjectionSurfaceScreenPositionOnSurface(
                debugSurface,
                rayOrigin,
                rayDirection,
                allowOutOfBoundsUvMapping,
                applyDebugRectFilter: true);

            if (baseUiResult.HasScreenHit && debugUiResult.HasScreenHit)
            {
                return baseUiResult.HitDistance <= debugUiResult.HitDistance ? baseUiResult : debugUiResult;
            }

            if (baseUiResult.HasScreenHit) return baseUiResult;
            if (debugUiResult.HasScreenHit) return debugUiResult;

            bool baseConstrained = baseUiResult.ConstrainToSurface;
            bool debugConstrained = debugUiResult.ConstrainToSurface;
            if (baseConstrained && debugConstrained)
            {
                if (baseUiResult.HasVisualSurfaceHit != debugUiResult.HasVisualSurfaceHit)
                {
                    return baseUiResult.HasVisualSurfaceHit ? baseUiResult : debugUiResult;
                }

                return baseUiResult.HitDistance <= debugUiResult.HitDistance ? baseUiResult : debugUiResult;
            }

            if (baseConstrained) return baseUiResult;
            if (debugConstrained) return debugUiResult;

            return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, false, false, default, fallbackHitPoint, PointerFallbackDistanceMeters);
        }

        private SurfaceRaycastResult TryGetUiProjectionSurfaceScreenPositionOnSurface(
            GameObject surface,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            bool allowOutOfBoundsUvMapping,
            bool applyDebugRectFilter)
        {
            Vector3 fallbackHitPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            if (surface == null || !surface.activeInHierarchy)
            {
                return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, false, false, default, fallbackHitPoint, PointerFallbackDistanceMeters);
            }

            Plane surfacePlane = new(surface.transform.forward, surface.transform.position);
            if (!surfacePlane.Raycast(new Ray(rayOrigin, rayDirection), out float enterDistance) || enterDistance <= 0f)
            {
                return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, true, false, default, fallbackHitPoint, PointerFallbackDistanceMeters);
            }

            if (enterDistance > UiSurfaceMaxRayDistanceMeters)
            {
                Vector3 farHit = rayOrigin + (rayDirection * enterDistance);
                return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, true, false, default, farHit, enterDistance);
            }

            Vector3 hitWorldPoint = rayOrigin + (rayDirection * enterDistance);
            // Keep the cursor slightly in front of the hit plane to avoid depth overlap/flicker.
            Vector3 visualHitPoint = hitWorldPoint - (rayDirection * UiRayHitNormalOffsetMeters);
            if (!TryGetProjectionPlaneUv(surface, hitWorldPoint, out Vector2 uv))
            {
                if (allowOutOfBoundsUvMapping && TryGetProjectionPlaneUvUnclamped(surface, hitWorldPoint, out uv))
                {
                    if (applyDebugRectFilter && ShouldRejectDebugSurfaceHitByRect(surface, uv))
                    {
                        return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, true, false, default, visualHitPoint, enterDistance);
                    }

                    Rect clampedMappingRect = GetPrimaryNguiScreenRect();
                    Vector2 clampedMappedScreenPos = new(
                        clampedMappingRect.xMin + (uv.x * clampedMappingRect.width),
                        clampedMappingRect.yMin + (uv.y * clampedMappingRect.height));
                    bool clampedHasVisual = IsWithinProjectionPlaneVisualBounds(surface, hitWorldPoint);
                    return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, true, true, clampedHasVisual, clampedMappedScreenPos, visualHitPoint, enterDistance);
                }

                bool hasVisual = IsWithinProjectionPlaneVisualBounds(surface, hitWorldPoint);
                return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, true, hasVisual, default, visualHitPoint, enterDistance);
            }

            if (applyDebugRectFilter && ShouldRejectDebugSurfaceHitByRect(surface, uv))
            {
                return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, false, true, false, default, visualHitPoint, enterDistance);
            }

            Rect mappingRect = GetPrimaryNguiScreenRect();
            Vector2 mappedScreenPos = new(
                mappingRect.xMin + (uv.x * mappingRect.width),
                mappingRect.yMin + (uv.y * mappingRect.height));
            return new SurfaceRaycastResult(PointerSurfaceKind.UiProjection, true, true, true, mappedScreenPos, visualHitPoint, enterDistance);
        }

        private bool ShouldRejectDebugSurfaceHitByRect(GameObject surface, Vector2 uv)
        {
            if (!ReferenceEquals(surface, _subCameraRectDebugPlaneObject)) return false;
            if (_debugUiSurfaceTriggerBypassActive) return false;
            if (!_hasSubCameraRectDebugUvRect) return true;

            const float epsilon = 0.0001f;
            return uv.x < (_subCameraRectDebugUvRect.xMin - epsilon) ||
                   uv.x > (_subCameraRectDebugUvRect.xMax + epsilon) ||
                   uv.y < (_subCameraRectDebugUvRect.yMin - epsilon) ||
                   uv.y > (_subCameraRectDebugUvRect.yMax + epsilon);
        }

        private SurfaceRaycastResult TryGetSubCameraSurfaceScreenPosition(Vector3 rayOrigin, Vector3 rayDirection, bool allowOutOfBoundsUvMapping)
        {
            Vector3 fallbackHitPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            RefreshUiSurfaceReferenceIfNeeded();
            if (_subCameraSurface == null || !_subCameraSurface.activeInHierarchy)
            {
                return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, false, false, false, default, fallbackHitPoint, PointerFallbackDistanceMeters);
            }

            Plane surfacePlane = new(_subCameraSurface.transform.forward, _subCameraSurface.transform.position);
            if (!surfacePlane.Raycast(new Ray(rayOrigin, rayDirection), out float enterDistance) || enterDistance <= 0f)
            {
                return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, false, true, false, default, fallbackHitPoint, PointerFallbackDistanceMeters);
            }

            if (enterDistance > UiSurfaceMaxRayDistanceMeters)
            {
                Vector3 farHit = rayOrigin + (rayDirection * enterDistance);
                return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, false, true, false, default, farHit, enterDistance);
            }

            Vector3 hitWorldPoint = rayOrigin + (rayDirection * enterDistance);
            if (!TryGetProjectionPlaneUv(_subCameraSurface, hitWorldPoint, out Vector2 uv))
            {
                if (allowOutOfBoundsUvMapping && TryGetProjectionPlaneUvUnclamped(_subCameraSurface, hitWorldPoint, out uv))
                {
                    Rect clampedMappingRect;
                    if (!TryGetActiveSubCameraMappingRect(out clampedMappingRect))
                    {
                        clampedMappingRect = GetPrimaryNguiScreenRect();
                    }

                    Vector2 clampedMappedScreenPos = new(
                        clampedMappingRect.xMin + (uv.x * clampedMappingRect.width),
                        clampedMappingRect.yMin + (uv.y * clampedMappingRect.height));
                    bool clampedHasVisual = IsWithinProjectionPlaneVisualBounds(_subCameraSurface, hitWorldPoint);
                    return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, true, true, clampedHasVisual, clampedMappedScreenPos, hitWorldPoint, enterDistance);
                }

                bool hasVisual = IsWithinProjectionPlaneVisualBounds(_subCameraSurface, hitWorldPoint);
                return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, false, true, hasVisual, default, hitWorldPoint, enterDistance);
            }

            Rect mappingRect;
            if (!TryGetActiveSubCameraMappingRect(out mappingRect))
            {
                mappingRect = GetPrimaryNguiScreenRect();
                if (!_loggedSubCameraRectFallback)
                {
                    _loggedSubCameraRectFallback = true;
                    VRModCore.LogWarning("[UI][OpenXR] Failed to resolve UI_SubCamera widget rect. Falling back to primary NGUI screen rect.");
                }
            }
            else if (_loggedSubCameraRectFallback)
            {
                _loggedSubCameraRectFallback = false;
            }

            Vector2 mappedScreenPos = new(
                mappingRect.xMin + (uv.x * mappingRect.width),
                mappingRect.yMin + (uv.y * mappingRect.height));
            return new SurfaceRaycastResult(PointerSurfaceKind.SubCameraProjection, true, true, true, mappedScreenPos, hitWorldPoint, enterDistance);
        }

        private static bool TrySelectSurfaceResult(
            SurfaceRaycastResult uiResult,
            SurfaceRaycastResult subCameraResult,
            PointerSurfaceKind preferredSurface,
            out SurfaceRaycastResult selectedResult)
        {
            selectedResult = default;

            if (preferredSurface == PointerSurfaceKind.UiProjection)
            {
                if (!uiResult.HasScreenHit) return false;
                selectedResult = uiResult;
                return true;
            }

            if (preferredSurface == PointerSurfaceKind.SubCameraProjection)
            {
                if (!subCameraResult.HasScreenHit) return false;
                selectedResult = subCameraResult;
                return true;
            }

            if (uiResult.HasScreenHit && subCameraResult.HasScreenHit)
            {
                selectedResult = uiResult.HitDistance <= subCameraResult.HitDistance ? uiResult : subCameraResult;
                return true;
            }

            if (uiResult.HasScreenHit)
            {
                selectedResult = uiResult;
                return true;
            }

            if (subCameraResult.HasScreenHit)
            {
                selectedResult = subCameraResult;
                return true;
            }

            return false;
        }

        private static bool TryGetConstrainedSurfaceResult(
            SurfaceRaycastResult uiResult,
            SurfaceRaycastResult subCameraResult,
            out SurfaceRaycastResult constrainedResult)
        {
            constrainedResult = default;
            bool uiConstrained = uiResult.ConstrainToSurface;
            bool subConstrained = subCameraResult.ConstrainToSurface;
            if (!uiConstrained && !subConstrained) return false;

            if (uiConstrained && subConstrained)
            {
                if (uiResult.HasVisualSurfaceHit != subCameraResult.HasVisualSurfaceHit)
                {
                    constrainedResult = uiResult.HasVisualSurfaceHit ? uiResult : subCameraResult;
                    return true;
                }

                constrainedResult = uiResult.HitDistance <= subCameraResult.HitDistance ? uiResult : subCameraResult;
                return true;
            }

            constrainedResult = uiConstrained ? uiResult : subCameraResult;
            return true;
        }

        private void RefreshUiSurfaceReferenceIfNeeded()
        {
            if (Time.time < _nextUiSurfaceLookupTime) return;

            _nextUiSurfaceLookupTime = Time.time + UiSurfaceLookupIntervalSeconds;
            _uiSurface = GameObject.Find(UiSurfaceName);
            _subCameraSurface = GameObject.Find(SubCameraSurfaceName);
            if (_uiSurface != null)
            {
                _loggedUiSurfaceFallback = false;
            }
            if (_subCameraSurface != null)
            {
                _loggedSubCameraRectFallback = false;
            }
        }

        private bool TryGetSubCameraScreenRect(out Rect rect)
        {
            rect = default;
            if (!TryResolveSubCameraMappingWidget(out Component widget)) return false;
            if (!TryGetWidgetScreenRect(widget, out rect)) return false;

            _hasLastResolvedSubCameraRect = true;
            _lastResolvedSubCameraRect = rect;

            MaybeLogSubCameraRect(widget, rect);
            return true;
        }

        private bool TryGetActiveSubCameraMappingRect(out Rect rect)
        {
            rect = default;
            bool useFrozenRect =
                _pointerIsDown &&
                _activePointerSurface == PointerSurfaceKind.SubCameraProjection &&
                _hasFrozenSubCameraRectForDrag;
            if (useFrozenRect)
            {
                rect = _frozenSubCameraRectForDrag;
                return true;
            }

            return TryGetSubCameraScreenRect(out rect);
        }

        private void CaptureSubCameraRectFreezeOnPointerDown(PointerSurfaceKind pointerSurface)
        {
            _hasFrozenSubCameraRectForDrag = false;
            if (pointerSurface != PointerSurfaceKind.SubCameraProjection) return;

            if (_hasLastResolvedSubCameraRect)
            {
                _frozenSubCameraRectForDrag = _lastResolvedSubCameraRect;
                _hasFrozenSubCameraRectForDrag = true;
                return;
            }

            if (TryGetSubCameraScreenRect(out Rect resolvedRect))
            {
                _frozenSubCameraRectForDrag = resolvedRect;
                _hasFrozenSubCameraRectForDrag = true;
            }
        }

        private void BeginSubCameraPlaneDragTracking(Vector3 rayOrigin, Vector3 rayDirection, Vector2 startScreenPos)
        {
            _subCameraPlaneDragActive = false;

            if (!TryGetSubCameraPlaneLocalPoint(rayOrigin, rayDirection, out Vector2 localPoint, out _))
            {
                return;
            }

            Rect mappingRect;
            if (!TryGetActiveSubCameraMappingRect(out mappingRect))
            {
                mappingRect = GetPrimaryNguiScreenRect();
            }

            _subCameraPlaneDragLastLocal = localPoint;
            _subCameraPlaneDragScreenPos = startScreenPos;
            _subCameraPlaneDragPixelsPerLocalX = Mathf.Max(1f, mappingRect.width);
            _subCameraPlaneDragPixelsPerLocalY = Mathf.Max(1f, mappingRect.height);
            _subCameraPlaneDragActive = true;
        }

        private bool TryGetSubCameraPlaneDragScreenPosition(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out Vector2 pointerScreenPos,
            out Vector3 rayEndPoint,
            out bool hasVisualTarget)
        {
            pointerScreenPos = _subCameraPlaneDragScreenPos;
            rayEndPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            hasVisualTarget = false;
            if (!_subCameraPlaneDragActive) return false;

            if (!TryGetSubCameraPlaneLocalPoint(rayOrigin, rayDirection, out Vector2 localPoint, out Vector3 hitWorldPoint))
            {
                return true;
            }

            Vector2 localDelta = localPoint - _subCameraPlaneDragLastLocal;
            _subCameraPlaneDragLastLocal = localPoint;
            _subCameraPlaneDragScreenPos += new Vector2(
                localDelta.x * _subCameraPlaneDragPixelsPerLocalX,
                localDelta.y * _subCameraPlaneDragPixelsPerLocalY);

            pointerScreenPos = _subCameraPlaneDragScreenPos;
            rayEndPoint = hitWorldPoint;
            hasVisualTarget = IsWithinProjectionPlaneVisualBounds(_subCameraSurface, hitWorldPoint);
            return true;
        }

        private bool TryGetSubCameraPlaneLocalPoint(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out Vector2 localPoint,
            out Vector3 hitWorldPoint)
        {
            localPoint = Vector2.zero;
            hitWorldPoint = rayOrigin + (rayDirection * PointerFallbackDistanceMeters);
            RefreshUiSurfaceReferenceIfNeeded();
            if (_subCameraSurface == null || !_subCameraSurface.activeInHierarchy) return false;

            Plane surfacePlane = new(_subCameraSurface.transform.forward, _subCameraSurface.transform.position);
            if (!surfacePlane.Raycast(new Ray(rayOrigin, rayDirection), out float enterDistance) || enterDistance <= 0f)
            {
                return false;
            }

            hitWorldPoint = rayOrigin + (rayDirection * enterDistance);
            Vector3 local = _subCameraSurface.transform.InverseTransformPoint(hitWorldPoint);
            localPoint = new Vector2(local.x, local.y);
            return true;
        }

        private bool TryResolveSubCameraMappingWidget(out Component widget)
        {
            widget = null;
            if (_subCameraMappingWidget != null && _subCameraMappingWidget.gameObject != null && _subCameraMappingWidget.gameObject.activeInHierarchy)
            {
                widget = _subCameraMappingWidget;
                return true;
            }

            if (Time.time < _nextSubCameraWidgetLookupTime) return false;
            _nextSubCameraWidgetLookupTime = Time.time + SubCameraWidgetLookupIntervalSeconds;

            _uiWidgetType ??= ResolveTypeAnyAssembly("UIWidget");
            _uiDragResizeType ??= ResolveTypeAnyAssembly("UIDragResize");
            if (_uiWidgetType == null) return false;

            if (TryFindSubCameraWidgetByPreferredName(out Component preferredWidget, out string preferredSource))
            {
                _subCameraMappingWidget = preferredWidget;
                LogSubCameraWidgetResolved(_subCameraMappingWidget, preferredSource);
                widget = _subCameraMappingWidget;
                return true;
            }

            if (TryFindSubCameraWidgetFromDragResize(out Component fromDragResize, out string dragResizeSource))
            {
                _subCameraMappingWidget = fromDragResize;
                LogSubCameraWidgetResolved(_subCameraMappingWidget, dragResizeSource);
                widget = _subCameraMappingWidget;
                return true;
            }

            if (TryFindSubCameraWidgetDirect(out Component directWidget, out string directSource))
            {
                _subCameraMappingWidget = directWidget;
                LogSubCameraWidgetResolved(_subCameraMappingWidget, directSource);
                widget = _subCameraMappingWidget;
                return true;
            }

            _subCameraMappingWidget = null;
            return false;
        }

        private bool TryFindSubCameraWidgetByPreferredName(out Component widget, out string source)
        {
            widget = null;
            source = string.Empty;

            UnityEngine.Object[] widgets = UnityEngine.Object.FindObjectsOfType(_uiWidgetType);
            if (widgets == null || widgets.Length == 0) return false;

            Component bestWidget = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < widgets.Length; i++)
            {
                if (widgets[i] is not Component candidate || !candidate.gameObject.activeInHierarchy) continue;
                if (!TryGetWidgetWorldCorners(candidate, out _)) continue;

                int nameScore = GetSubCameraPreferredNameScore(candidate.gameObject.name);
                if (nameScore <= 0) continue;

                int score = (nameScore * 100) + GetSubCameraLayerScore(candidate.gameObject) + GetSubCameraNameScore(candidate.gameObject.name);
                if (score <= bestScore) continue;
                bestScore = score;
                bestWidget = candidate;
            }

            if (bestWidget == null) return false;
            widget = bestWidget;
            source = "Preferred UISprite name";
            return true;
        }

        private bool TryFindSubCameraWidgetFromDragResize(out Component widget, out string source)
        {
            widget = null;
            source = string.Empty;
            if (_uiDragResizeType == null) return false;

            UnityEngine.Object[] dragResizes = UnityEngine.Object.FindObjectsOfType(_uiDragResizeType);
            if (dragResizes == null || dragResizes.Length == 0) return false;

            Component bestWidget = null;
            Component bestDragResize = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < dragResizes.Length; i++)
            {
                if (dragResizes[i] is not Component dragResizeComponent || !dragResizeComponent.gameObject.activeInHierarchy) continue;
                Component targetWidget = GetMemberValue(dragResizeComponent, "target") as Component;
                if (targetWidget == null || !targetWidget.gameObject.activeInHierarchy) continue;
                if (!TryGetWidgetWorldCorners(targetWidget, out _)) continue;

                int score =
                    (GetSubCameraNameScore(targetWidget.gameObject.name) * 100) +
                    (GetSubCameraNameScore(dragResizeComponent.gameObject.name) * 10) +
                    GetSubCameraLayerScore(targetWidget.gameObject) +
                    GetSubCameraLayerScore(dragResizeComponent.gameObject);
                if (score <= bestScore) continue;
                bestScore = score;
                bestWidget = targetWidget;
                bestDragResize = dragResizeComponent;
            }

            if (bestWidget == null || bestScore <= 0) return false;
            widget = bestWidget;
            source = bestDragResize != null
                ? $"UIDragResize.target ({GetGameObjectPath(bestDragResize.gameObject)})"
                : "UIDragResize.target";
            return true;
        }

        private bool TryFindSubCameraWidgetDirect(out Component widget, out string source)
        {
            widget = null;
            source = string.Empty;
            UnityEngine.Object[] widgets = UnityEngine.Object.FindObjectsOfType(_uiWidgetType);
            if (widgets == null || widgets.Length == 0) return false;

            Component bestWidget = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < widgets.Length; i++)
            {
                if (widgets[i] is not Component candidate || !candidate.gameObject.activeInHierarchy) continue;
                if (!TryGetWidgetWorldCorners(candidate, out _)) continue;

                int score =
                    (GetSubCameraNameScore(candidate.gameObject.name) * 100) +
                    GetSubCameraLayerScore(candidate.gameObject);
                if (score <= bestScore) continue;
                bestScore = score;
                bestWidget = candidate;
            }

            if (bestWidget == null || bestScore <= 0) return false;
            widget = bestWidget;
            source = "UIWidget name scan";
            return true;
        }

        private void LogSubCameraWidgetResolved(Component widget, string source)
        {
            if (widget == null || widget.gameObject == null) return;

            int instanceId = widget.gameObject.GetInstanceID();
            if (_lastLoggedSubCameraWidgetInstanceId == instanceId) return;

            _lastLoggedSubCameraWidgetInstanceId = instanceId;
            _hasLoggedSubCameraRect = false;
            _lastLoggedSubCameraRectWidgetInstanceId = 0;
            _nextSubCameraRectLogTime = 0f;
            VRModCore.Log(
                $"[UI][OpenXR] SubCamera mapping widget: source={source}, target={GetGameObjectPath(widget.gameObject)}, layer={LayerMask.LayerToName(widget.gameObject.layer)}({widget.gameObject.layer}), id={instanceId}");
        }

        private void MaybeLogSubCameraRect(Component widget, Rect rect)
        {
            if (widget == null || widget.gameObject == null) return;

            int widgetId = widget.gameObject.GetInstanceID();
            bool widgetChanged = widgetId != _lastLoggedSubCameraRectWidgetInstanceId;
            bool rectChanged = !_hasLoggedSubCameraRect
                || Mathf.Abs(rect.xMin - _lastLoggedSubCameraRect.xMin) >= SubCameraRectLogDeltaPixels
                || Mathf.Abs(rect.yMin - _lastLoggedSubCameraRect.yMin) >= SubCameraRectLogDeltaPixels
                || Mathf.Abs(rect.width - _lastLoggedSubCameraRect.width) >= SubCameraRectLogDeltaPixels
                || Mathf.Abs(rect.height - _lastLoggedSubCameraRect.height) >= SubCameraRectLogDeltaPixels;

            if (!widgetChanged && !rectChanged) return;
            if (Time.time < _nextSubCameraRectLogTime) return;

            _lastLoggedSubCameraRectWidgetInstanceId = widgetId;
            _lastLoggedSubCameraRect = rect;
            _hasLoggedSubCameraRect = true;
            _nextSubCameraRectLogTime = Time.time + SubCameraRectLogMinIntervalSeconds;

            VRModCore.Log(
                $"[UI][OpenXR] SubCamera widget rect: target={GetGameObjectPath(widget.gameObject)}, x={rect.xMin:F1}, y={rect.yMin:F1}, w={rect.width:F1}, h={rect.height:F1}");
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null) return "<null>";

            Transform current = gameObject.transform;
            string path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = $"{current.name}/{path}";
            }

            return path;
        }

        private static int GetSubCameraNameScore(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;

            int score = 0;
            for (int i = 0; i < SubCameraNameTokens.Length; i++)
            {
                string token = SubCameraNameTokens[i];
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                score += 10 - i;
            }

            return score;
        }

        private static int GetSubCameraPreferredNameScore(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;

            for (int i = 0; i < SubCameraPreferredWidgetNames.Length; i++)
            {
                if (string.Equals(name, SubCameraPreferredWidgetNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return 100 - i;
                }
            }

            return 0;
        }

        private static int GetSubCameraLayerScore(GameObject gameObject)
        {
            if (gameObject == null) return 0;

            string layerName = LayerMask.LayerToName(gameObject.layer);
            return string.Equals(layerName, SubCameraUiLayerName, StringComparison.OrdinalIgnoreCase) ? 2000 : 0;
        }

        private bool TryGetWidgetScreenRect(Component widget, out Rect rect)
        {
            rect = default;
            if (widget == null || widget.gameObject == null) return false;
            if (!TryGetWidgetWorldCorners(widget, out Vector3[] corners))
            {
                MaybeLogSubCameraRectProjectionFailure(widget, null, "UIWidget.worldCorners unavailable.");
                return false;
            }
            if (!TryGetPrimaryNguiCamera(widget.gameObject.layer, out Camera nguiCamera) || nguiCamera == null)
            {
                MaybeLogSubCameraRectProjectionFailure(widget, null, "No active UICamera found for preferred layer.");
                return false;
            }
            MaybeLogSubCameraProjectionCamera(widget, nguiCamera, widget.gameObject.layer);

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 screenPoint = nguiCamera.WorldToScreenPoint(corners[i]);
                if (screenPoint.z < SubCameraProjectionRejectZ)
                {
                    MaybeLogSubCameraRectProjectionFailure(
                        widget,
                        nguiCamera,
                        $"Corner[{i}] behind camera (z={screenPoint.z:F4}). world={FormatVector3(corners[i])} screen={FormatVector3(screenPoint)}");
                    return false;
                }
                minX = Mathf.Min(minX, screenPoint.x);
                minY = Mathf.Min(minY, screenPoint.y);
                maxX = Mathf.Max(maxX, screenPoint.x);
                maxY = Mathf.Max(maxY, screenPoint.y);
            }

            float clampedMinX = Mathf.Clamp(minX, 0f, Screen.width);
            float clampedMinY = Mathf.Clamp(minY, 0f, Screen.height);
            float clampedMaxX = Mathf.Clamp(maxX, 0f, Screen.width);
            float clampedMaxY = Mathf.Clamp(maxY, 0f, Screen.height);
            if (clampedMaxX - clampedMinX < 1f || clampedMaxY - clampedMinY < 1f)
            {
                MaybeLogSubCameraRectProjectionFailure(
                    widget,
                    nguiCamera,
                    $"Projected rect too small after clamp. rawMin=({minX:F1},{minY:F1}) rawMax=({maxX:F1},{maxY:F1}) clampedMin=({clampedMinX:F1},{clampedMinY:F1}) clampedMax=({clampedMaxX:F1},{clampedMaxY:F1})");
                return false;
            }

            rect = Rect.MinMaxRect(clampedMinX, clampedMinY, clampedMaxX, clampedMaxY);
            return true;
        }

        private void MaybeLogSubCameraRectProjectionFailure(Component widget, Camera projectionCamera, string reason)
        {
            string widgetPath = widget != null && widget.gameObject != null ? GetGameObjectPath(widget.gameObject) : "<null>";
            string cameraPath = projectionCamera != null ? GetGameObjectPath(projectionCamera.gameObject) : "<none>";
            string failureKey = $"{widgetPath}|{cameraPath}|{reason}";
            int hash = failureKey.GetHashCode();
            if (hash == _lastLoggedSubCameraRectFailureHash && Time.time < _nextSubCameraRectFailureLogTime)
            {
                return;
            }

            _lastLoggedSubCameraRectFailureHash = hash;
            _nextSubCameraRectFailureLogTime = Time.time + 0.75f;
            VRModCore.LogWarning(
                $"[UI][OpenXR] SubCamera rect projection failed: reason={reason}, widget={widgetPath}, camera={cameraPath}");
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3},{value.y:F3},{value.z:F3})";
        }

        private void MaybeLogSubCameraProjectionCamera(Component widget, Camera nguiCamera, int preferredLayer)
        {
            if (nguiCamera == null) return;

            int camId = nguiCamera.GetInstanceID();
            if (camId == _lastLoggedSubCameraProjectionCameraInstanceId && preferredLayer == _lastLoggedSubCameraProjectionLayer)
            {
                return;
            }

            _lastLoggedSubCameraProjectionCameraInstanceId = camId;
            _lastLoggedSubCameraProjectionLayer = preferredLayer;

            Rect pixelRect = nguiCamera.pixelRect;
            string preferredLayerName = preferredLayer >= 0 ? LayerMask.LayerToName(preferredLayer) : "Any";
            string widgetPath = widget != null && widget.gameObject != null ? GetGameObjectPath(widget.gameObject) : "<null>";
            VRModCore.Log(
                $"[UI][OpenXR] SubCamera projection camera selected: camera={GetGameObjectPath(nguiCamera.gameObject)}, depth={nguiCamera.depth:F2}, cullingMask=0x{nguiCamera.cullingMask:X8}, pixelRect=({pixelRect.x:F0},{pixelRect.y:F0},{pixelRect.width:F0},{pixelRect.height:F0}), preferredLayer={preferredLayerName}({preferredLayer}), widget={widgetPath}");
        }

        private static bool TryGetWidgetWorldCorners(Component widget, out Vector3[] corners)
        {
            corners = null;
            if (widget == null) return false;

            object value = GetMemberValue(widget, "worldCorners");
            if (value is Vector3[] array && array.Length >= 4)
            {
                corners = array;
                return true;
            }

            return false;
        }

        private static bool TryGetPrimaryNguiCamera(out Camera bestCamera)
        {
            return TryGetPrimaryNguiCamera(-1, out bestCamera);
        }

        private static bool TryGetPrimaryNguiCamera(int preferredLayer, out Camera bestCamera)
        {
            bestCamera = null;
            Type nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (nguiUiCameraType == null) return false;

            float bestScore = float.MinValue;
            int preferredLayerMask = preferredLayer >= 0 && preferredLayer < 32 ? (1 << preferredLayer) : 0;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(nguiUiCameraType) == null) continue;

                bool matchesPreferredLayer = preferredLayerMask != 0 && (cam.cullingMask & preferredLayerMask) != 0;
                float score = cam.depth + (matchesPreferredLayer ? 10000f : 0f);
                if (bestCamera == null || score > bestScore)
                {
                    bestCamera = cam;
                    bestScore = score;
                }
            }

            return bestCamera != null;
        }

        private static Rect GetPrimaryNguiScreenRect()
        {
            if (TryGetPrimaryNguiPixelRect(out Rect pixelRect))
            {
                return pixelRect;
            }

            return new Rect(0f, 0f, Screen.width, Screen.height);
        }

        private static bool TryGetPrimaryNguiPixelRect(out Rect pixelRect)
        {
            pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
            if (!TryGetPrimaryNguiCamera(out Camera bestCamera)) return false;
            pixelRect = bestCamera.pixelRect;
            return true;
        }

        private bool TryGetUiRayTouchInteraction(Vector3 handWorldPos, out Vector3 hitPointWorld, out string icon, out bool hasUiRayTouchHit)
        {
            hitPointWorld = handWorldPos;
            icon = string.Empty;
            hasUiRayTouchHit = false;

            if (!EnsurePhysicsBindings()) return false;

            int layerMask = GetUiRayLayerMask();
            if (layerMask == 0) return false;

            int paramCount = _physicsOverlapSphereMethod.GetParameters().Length;
            object[] args;
            if (paramCount == 3)
            {
                args = [ handWorldPos, UiRayTouchRadiusMeters, layerMask ];
            }
            else if (paramCount == 2)
            {
                args = [ handWorldPos, UiRayTouchRadiusMeters ];
            }
            else
            {
                ParameterInfo[] parameters = _physicsOverlapSphereMethod.GetParameters();
                args = new object[paramCount];
                args[0] = handWorldPos;
                args[1] = UiRayTouchRadiusMeters;
                for (int i = 2; i < paramCount; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (i == 2 && parameterType == typeof(int))
                    {
                        args[i] = layerMask;
                    }
                    else if (parameterType.IsEnum)
                    {
                        args[i] = Enum.ToObject(parameterType, 0);
                    }
                    else if (parameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(parameterType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }
            }

            object overlapsObj = _physicsOverlapSphereMethod.Invoke(null, args);
            if (overlapsObj is not Array overlaps || overlaps.Length == 0) return false;

            float bestDistance = float.MaxValue;
            Vector3 bestPoint = default;
            string bestIcon = string.Empty;
            bool foundTouch = false;

            foreach (object colliderObj in overlaps)
            {
                if (colliderObj == null) continue;

                GameObject hitGo = ExtractGameObjectFromCollider(colliderObj);
                if (hitGo == null) continue;

                foundTouch = true;
                Vector3 point = GetColliderClosestPoint(colliderObj, handWorldPos);
                float hitDistance = Vector3.Distance(handWorldPos, point);
                if (hitDistance >= bestDistance) continue;

                bestDistance = hitDistance;
                bestPoint = point;
                if (TryMapUiRayColliderToIcon(hitGo.name, out string mappedIcon))
                {
                    bestIcon = mappedIcon;
                }
            }

            hasUiRayTouchHit = foundTouch;
            if (string.IsNullOrEmpty(bestIcon))
                return false;

            icon = bestIcon;
            hitPointWorld = bestPoint;
            return true;
        }

        private int GetUiRayLayerMask()
        {
            if (_resolvedUiRayLayerMask) return _uiRayLayerMask;

            _resolvedUiRayLayerMask = true;
            int uiRayLayer = LayerMask.NameToLayer(UiRayLayerName);
            if (uiRayLayer >= 0 && uiRayLayer <= 31)
            {
                _uiRayLayerMask = 1 << uiRayLayer;
                return _uiRayLayerMask;
            }

            _uiRayLayerMask = UiRayLayerFallbackMask;
            if (!_loggedUiRayLayerFallback)
            {
                _loggedUiRayLayerFallback = true;
                VRModCore.LogWarning($"[UI][OpenXR] Layer '{UiRayLayerName}' not found. Falling back to mask {UiRayLayerFallbackMask}.");
            }

            return _uiRayLayerMask;
        }

        private bool EnsurePhysicsBindings()
        {
            if (_physicsBindingsResolved) return true;
            if (_physicsBindingsFailed) return false;

            Type physicsType = ResolveTypeAnyAssembly("UnityEngine.Physics");
            if (physicsType == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] UnityEngine.Physics type not found. UIRay interaction disabled.");
                return false;
            }

            _physicsRaycastAllMethod = physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float), typeof(int) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float) ], null)
                ?? physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray) ], null);

            if (_physicsRaycastAllMethod == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] Physics.RaycastAll overload not found. UIRay interaction disabled.");
                return false;
            }

            _physicsOverlapSphereMethod = physicsType.GetMethod("OverlapSphere", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Vector3), typeof(float), typeof(int) ], null)
                ?? physicsType.GetMethod("OverlapSphere", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Vector3), typeof(float) ], null);

            if (_physicsOverlapSphereMethod == null)
            {
                foreach (MethodInfo candidate in physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!string.Equals(candidate.Name, "OverlapSphere", StringComparison.Ordinal)) continue;
                    ParameterInfo[] ps = candidate.GetParameters();
                    if (ps.Length < 2) continue;
                    if (ps[0].ParameterType != typeof(Vector3) || ps[1].ParameterType != typeof(float)) continue;
                    _physicsOverlapSphereMethod = candidate;
                    break;
                }
            }

            if (_physicsOverlapSphereMethod == null)
            {
                _physicsBindingsFailed = true;
                VRModCore.LogWarning("[UI][OpenXR] Physics.OverlapSphere overload not found. UIRay touch interaction disabled.");
                return false;
            }

            Type raycastHitType = ResolveTypeAnyAssembly("UnityEngine.RaycastHit");
            if (raycastHitType != null)
            {
                _raycastHitDistanceField = raycastHitType.GetField("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitDistanceProperty = raycastHitType.GetProperty("distance", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderField = raycastHitType.GetField("collider", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitColliderProperty = raycastHitType.GetProperty("collider", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitPointField = raycastHitType.GetField("point", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitPointProperty = raycastHitType.GetProperty("point", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitNormalField = raycastHitType.GetField("normal", BindingFlags.Public | BindingFlags.Instance);
                _raycastHitNormalProperty = raycastHitType.GetProperty("normal", BindingFlags.Public | BindingFlags.Instance);
            }

            _physicsBindingsResolved = true;
            return true;
        }

        private float GetRaycastHitDistance(object hit)
        {
            if (hit == null) return float.MaxValue;

            if (_raycastHitDistanceField != null)
            {
                object value = _raycastHitDistanceField.GetValue(hit);
                if (value is float f) return f;
            }

            if (_raycastHitDistanceProperty != null)
            {
                object value = _raycastHitDistanceProperty.GetValue(hit, null);
                if (value is float f) return f;
            }

            object byName = GetMemberValue(hit, "distance");
            if (byName is float byNameFloat) return byNameFloat;
            return float.MaxValue;
        }

        private object GetRaycastHitCollider(object hit)
        {
            if (hit == null) return null;
            if (_raycastHitColliderField != null) return _raycastHitColliderField.GetValue(hit);
            if (_raycastHitColliderProperty != null) return _raycastHitColliderProperty.GetValue(hit, null);
            return GetMemberValue(hit, "collider");
        }

        private Vector3 GetRaycastHitPoint(object hit)
        {
            if (hit == null) return default;

            if (_raycastHitPointField != null)
            {
                object value = _raycastHitPointField.GetValue(hit);
                if (value is Vector3 point) return point;
            }

            if (_raycastHitPointProperty != null)
            {
                object value = _raycastHitPointProperty.GetValue(hit, null);
                if (value is Vector3 point) return point;
            }

            object byName = GetMemberValue(hit, "point");
            return byName is Vector3 byNamePoint ? byNamePoint : default;
        }

        private Vector3 GetRaycastHitNormal(object hit)
        {
            if (hit == null) return Vector3.forward;

            if (_raycastHitNormalField != null)
            {
                object value = _raycastHitNormalField.GetValue(hit);
                if (value is Vector3 normal) return normal;
            }

            if (_raycastHitNormalProperty != null)
            {
                object value = _raycastHitNormalProperty.GetValue(hit, null);
                if (value is Vector3 normal) return normal;
            }

            object byName = GetMemberValue(hit, "normal");
            return byName is Vector3 byNameNormal ? byNameNormal : Vector3.forward;
        }

        private static Vector3 GetColliderClosestPoint(object colliderObj, Vector3 worldPoint)
        {
            if (colliderObj is Component colliderComponent)
            {
                MethodInfo closestPointMethod = colliderComponent.GetType().GetMethod("ClosestPoint", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Vector3) }, null);
                if (closestPointMethod != null)
                {
                    object value = closestPointMethod.Invoke(colliderComponent, new object[] { worldPoint });
                    if (value is Vector3 closestPoint)
                    {
                        return closestPoint;
                    }
                }

                return colliderComponent.transform.position;
            }

            return worldPoint;
        }

        private void EnsureControllerIconVisual(GameObject vrRig)
        {
            if (_controllerIconObject != null) return;

            _controllerIconObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _controllerIconObject.name = ControllerIconObjectName;
            _controllerIconObject.transform.SetParent(vrRig.transform, true);
            SetLayerRecursively(_controllerIconObject, vrRig.layer);
            _controllerIconObject.transform.localScale = Vector3.one * ControllerIconScaleMeters;

            object collider = _controllerIconObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            Renderer iconRenderer = _controllerIconObject.GetComponent<Renderer>();
            if (iconRenderer != null)
            {
                iconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                iconRenderer.receiveShadows = false;
                Shader iconShader = Shader.Find("Sprites/Default");
                if (iconShader == null) iconShader = Shader.Find("Unlit/Transparent");
                if (iconShader == null) iconShader = Shader.Find("Unlit/Texture");
                _controllerIconMaterial = new Material(iconShader);
                iconRenderer.sharedMaterial = _controllerIconMaterial;
            }

            if (_fixedTouchIconTexture == null)
            {
                _fixedTouchIconTexture = CreateFixedTouchIconTexture();
            }

            SetControllerIconVisible(false);
        }

        private void UpdateControllerIconVisual(Texture2D texture, Vector3 handWorldPos, Quaternion handWorldRot)
        {
            if (_controllerIconObject == null)
            {
                return;
            }

            if (texture == null || _controllerIconMaterial == null)
            {
                SetControllerIconVisible(false);
                return;
            }

            if (!ReferenceEquals(_controllerIconTexture, texture))
            {
                _controllerIconTexture = texture;
                _controllerIconMaterial.mainTexture = texture;
            }

            Vector3 iconWorldPos = handWorldPos + handWorldRot * new Vector3(ControllerIconRightOffsetMeters, ControllerIconUpOffsetMeters, 0f);
            _controllerIconObject.transform.position = iconWorldPos;
            _controllerIconObject.transform.localScale = Vector3.one * ControllerIconScaleMeters;

            if (_mainCamera != null)
            {
                Vector3 forward = _mainCamera.transform.position - iconWorldPos;
                if (forward.sqrMagnitude > 0.000001f)
                {
                    _controllerIconObject.transform.rotation = Quaternion.LookRotation(forward.normalized, _mainCamera.transform.up);
                }
            }
            else
            {
                _controllerIconObject.transform.rotation = handWorldRot;
            }

            SetControllerIconVisible(true);
        }

        internal void SetUIRayVisible(bool visible)
        {
            if (_rayObject != null && _rayObject.activeSelf != visible)
                _rayObject.SetActive(visible);
        }

        private void SetControllerIconVisible(bool visible)
        {
            if (_controllerIconObject != null && _controllerIconObject.activeSelf != visible)
            {
                _controllerIconObject.SetActive(visible);
            }
        }

        private static Texture2D CreateFixedTouchIconTexture()
        {
            const int size = 64;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "OpenXR_UITouchFixedIcon"
            };

            Color32 clear = new(0, 0, 0, 0);
            Color32 fill = new(255, 245, 170, 235);
            Color32 edge = new(255, 255, 255, 255);
            int center = size / 2;
            float outer = size * 0.45f;
            float inner = size * 0.34f;

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 color = clear;
                    if (d <= outer)
                    {
                        color = d >= inner ? edge : fill;
                    }

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static GameObject ExtractGameObjectFromCollider(object colliderObj)
        {
            if (colliderObj is Component colliderComponent)
            {
                return colliderComponent.gameObject;
            }

            if (colliderObj == null) return null;
            PropertyInfo gameObjectProperty = colliderObj.GetType().GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
            return gameObjectProperty?.GetValue(colliderObj, null) as GameObject;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName)) return null;

            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(instance);

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return property?.GetValue(instance, null);
        }

        private static bool TryMapUiRayColliderToIcon(string colliderName, out string icon)
        {
            switch (colliderName)
            {
                case "HS_anaruA00_Coll":
                    icon = "\u30A2\u30CA\u30EB";
                    return true;
                case "HS_Breast_R00_Coll":
                case "BP_Breast_R01_Z_Coll":
                case "milkR_(Z)_Point":
                    icon = "\u80F8RZ";
                    return true;
                case "HS_Breast_L00_Coll":
                case "BP_Breast_L01_Z_Coll":
                case "milkL_(Z)_Point":
                    icon = "\u80F8LZ";
                    return true;
                case "HS_pussy00_Coll":
                    icon = "\u30DE\u30F3\u30B3";
                    return true;
                case "HS_siriL00_Coll":
                    icon = "\u5C3BL";
                    return true;
                case "HS_siriR00_Coll":
                    icon = "\u5C3BR";
                    return true;
                default:
                    icon = string.Empty;
                    return false;
            }
        }

        private bool TryInvokeZngButtonDown(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return false;
            if (!TryResolveZngButtonDown(out object zngController, out MethodInfo buttonDownMethod)) return false;

            try
            {
                buttonDownMethod.Invoke(zngController, new object[] { icon });
                return true;
            }
            catch (Exception ex)
            {
                _zngControllerInstance = null;
                VRModCore.LogWarning($"[UI][OpenXR] Failed invoking Zng_Controller.ButtonDown0('{icon}'): {ex.Message}");
                return false;
            }
        }

        private bool TryResolveZngButtonDown(out object zngController, out MethodInfo buttonDownMethod)
        {
            zngController = null;
            buttonDownMethod = null;

            if (_zngControllerType == null)
            {
                _zngControllerType = ResolveTypeAnyAssembly("Zng_Controller");
                if (_zngControllerType == null) return false;
            }

            if (_zngButtonDownMethod == null)
            {
                _zngButtonDownMethod = _zngControllerType.GetMethod("ButtonDown0", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                if (_zngButtonDownMethod == null) return false;
            }

            if (_zngControllerInstance == null)
            {
                if (Time.time < _nextZngControllerLookupTime) return false;

                _nextZngControllerLookupTime = Time.time + 0.5f;
                _zngControllerInstance = UnityEngine.Object.FindObjectOfType(_zngControllerType);
                if (_zngControllerInstance == null) return false;
            }

            zngController = _zngControllerInstance;
            buttonDownMethod = _zngButtonDownMethod;
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

        private void EnsureSubCameraRectDebugVisuals(GameObject vrRig)
        {
            if (_subCameraRectDebugPlaneObject != null && _subCameraRectDebugOutline != null) return;

            _subCameraRectDebugPlaneObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _subCameraRectDebugPlaneObject.name = SubCameraRectDebugPlaneObjectName;
            _subCameraRectDebugPlaneObject.transform.SetParent(vrRig.transform, false);
            SetLayerRecursively(_subCameraRectDebugPlaneObject, vrRig.layer);

            object planeCollider = _subCameraRectDebugPlaneObject.GetComponent("Collider");
            if (planeCollider is UnityEngine.Object planeColliderObj) UnityEngine.Object.Destroy(planeColliderObj);

            Renderer planeRenderer = _subCameraRectDebugPlaneObject.GetComponent<Renderer>();
            if (planeRenderer != null)
            {
                planeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                planeRenderer.receiveShadows = false;
                Shader planeShader = Shader.Find("Unlit/Color");
                if (planeShader == null) planeShader = Shader.Find("Sprites/Default");
                _subCameraRectDebugPlaneMaterial = new Material(planeShader);
                _subCameraRectDebugPlaneMaterial.color = SubCameraRectDebugPlaneColor;
                _subCameraRectDebugPlaneMaterial.renderQueue = 3000;
                planeRenderer.sharedMaterial = _subCameraRectDebugPlaneMaterial;
                planeRenderer.enabled = false;
            }

            _subCameraRectDebugContentObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _subCameraRectDebugContentObject.name = SubCameraRectDebugContentObjectName;
            _subCameraRectDebugContentObject.transform.SetParent(_subCameraRectDebugPlaneObject.transform, false);
            SetLayerRecursively(_subCameraRectDebugContentObject, vrRig.layer);
            object contentCollider = _subCameraRectDebugContentObject.GetComponent("Collider");
            if (contentCollider is UnityEngine.Object contentColliderObj) UnityEngine.Object.Destroy(contentColliderObj);

            Renderer contentRenderer = _subCameraRectDebugContentObject.GetComponent<Renderer>();
            if (contentRenderer != null)
            {
                contentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                contentRenderer.receiveShadows = false;
                Shader contentShader = Shader.Find("Unlit/Texture");
                if (contentShader == null) contentShader = Shader.Find("Sprites/Default");
                _subCameraRectDebugContentMaterial = new Material(contentShader);
                _subCameraRectDebugContentMaterial.renderQueue = 3500;
                contentRenderer.sharedMaterial = _subCameraRectDebugContentMaterial;
            }
            _subCameraRectDebugContentObject.SetActive(false);

            _subCameraRectDebugEdgeFrameObject = new GameObject(SubCameraRectDebugEdgeFrameObjectName);
            _subCameraRectDebugEdgeFrameObject.transform.SetParent(_subCameraRectDebugPlaneObject.transform, false);
            SetLayerRecursively(_subCameraRectDebugEdgeFrameObject, vrRig.layer);
            _subCameraRectDebugEdgeFrameLine = _subCameraRectDebugEdgeFrameObject.AddComponent<LineRenderer>();
            _subCameraRectDebugEdgeFrameLine.useWorldSpace = false;
            _subCameraRectDebugEdgeFrameLine.loop = true;
            _subCameraRectDebugEdgeFrameLine.positionCount = 4;
            _subCameraRectDebugEdgeFrameLine.startWidth = SubCameraRectDebugEdgeFrameLineWidthMeters;
            _subCameraRectDebugEdgeFrameLine.endWidth = SubCameraRectDebugEdgeFrameLineWidthMeters;
            _subCameraRectDebugEdgeFrameLine.numCapVertices = 2;
            _subCameraRectDebugEdgeFrameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _subCameraRectDebugEdgeFrameLine.receiveShadows = false;
            Shader edgeFrameShader = Shader.Find("Sprites/Default");
            if (edgeFrameShader == null) edgeFrameShader = Shader.Find("Unlit/Color");
            _subCameraRectDebugEdgeFrameMaterial = new Material(edgeFrameShader);
            _subCameraRectDebugEdgeFrameMaterial.color = SubCameraRectDebugEdgeFrameColor;
            _subCameraRectDebugEdgeFrameMaterial.renderQueue = 4500;
            _subCameraRectDebugEdgeFrameLine.material = _subCameraRectDebugEdgeFrameMaterial;
            float edgeHalf = 0.5f + SubCameraRectDebugEdgeFrameExpandNormalized;
            _subCameraRectDebugEdgeFrameLine.SetPosition(0, new Vector3(-edgeHalf, -edgeHalf, -0.008f));
            _subCameraRectDebugEdgeFrameLine.SetPosition(1, new Vector3(-edgeHalf, edgeHalf, -0.008f));
            _subCameraRectDebugEdgeFrameLine.SetPosition(2, new Vector3(edgeHalf, edgeHalf, -0.008f));
            _subCameraRectDebugEdgeFrameLine.SetPosition(3, new Vector3(edgeHalf, -edgeHalf, -0.008f));
            _subCameraRectDebugEdgeFrameObject.SetActive(false);
            CreateSubCameraRectDebugResizeHandlesIfNeeded(vrRig);
            CreateSubCameraRectDebugDragHintIfNeeded(vrRig);

            GameObject outlineObj = new(SubCameraRectDebugOutlineObjectName);
            outlineObj.transform.SetParent(_subCameraRectDebugPlaneObject.transform, false);
            SetLayerRecursively(outlineObj, vrRig.layer);
            _subCameraRectDebugOutline = outlineObj.AddComponent<LineRenderer>();
            _subCameraRectDebugOutline.useWorldSpace = false;
            _subCameraRectDebugOutline.positionCount = 5;
            _subCameraRectDebugOutline.loop = false;
            _subCameraRectDebugOutline.startWidth = SubCameraRectDebugLineWidthMeters;
            _subCameraRectDebugOutline.endWidth = SubCameraRectDebugLineWidthMeters;
            _subCameraRectDebugOutline.numCapVertices = 2;
            _subCameraRectDebugOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _subCameraRectDebugOutline.receiveShadows = false;

            Shader outlineShader = Shader.Find("Sprites/Default");
            if (outlineShader == null) outlineShader = Shader.Find("Unlit/Color");
            _subCameraRectDebugOutlineMaterial = new Material(outlineShader);
            _subCameraRectDebugOutlineMaterial.color = SubCameraRectDebugOutlineColor;
            _subCameraRectDebugOutlineMaterial.renderQueue = 5000;
            _subCameraRectDebugOutline.material = _subCameraRectDebugOutlineMaterial;
            _subCameraRectDebugOutline.sortingOrder = 1000;
            _subCameraRectDebugOutline.enabled = false;
            _subCameraRectDebugPlaneObject.SetActive(false);
            UpdateSubCameraRectDebugEdgeFrameVisibility();
        }

        private void CreateSubCameraRectDebugResizeHandlesIfNeeded(GameObject vrRig)
        {
            if (_subCameraRectDebugPlaneObject == null) return;

            if (_subCameraRectDebugResizeHandleMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _subCameraRectDebugResizeHandleMaterial = new Material(shader);
                _subCameraRectDebugResizeHandleMaterial.color = SubCameraRectDebugResizeHandleColor;
                _subCameraRectDebugResizeHandleMaterial.renderQueue = 4600;
            }

            for (int i = 0; i < _subCameraRectDebugResizeHandles.Length; i++)
            {
                if (_subCameraRectDebugResizeHandles[i] != null) continue;

                GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handle.name = SubCameraRectDebugResizeHandleBaseName + i;
                handle.transform.SetParent(_subCameraRectDebugPlaneObject.transform, false);
                SetLayerRecursively(handle, vrRig.layer);
                object collider = handle.GetComponent("Collider");
                if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);
                Renderer renderer = handle.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = _subCameraRectDebugResizeHandleMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                _subCameraRectDebugResizeHandles[i] = handle;
            }

            SetSubCameraRectDebugResizeHandlesVisible(false);
        }

        private void CreateSubCameraRectDebugDragHintIfNeeded(GameObject vrRig)
        {
            if (_subCameraRectDebugPlaneObject == null || _subCameraRectDebugDragHintObject != null) return;

            _subCameraRectDebugDragHintObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _subCameraRectDebugDragHintObject.name = SubCameraRectDebugDragHintObjectName;
            _subCameraRectDebugDragHintObject.transform.SetParent(_subCameraRectDebugPlaneObject.transform, false);
            SetLayerRecursively(_subCameraRectDebugDragHintObject, vrRig.layer);
            object collider = _subCameraRectDebugDragHintObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            Renderer renderer = _subCameraRectDebugDragHintObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _subCameraRectDebugDragHintMaterial = new Material(shader);
                _subCameraRectDebugDragHintMaterial.color = SubCameraRectDebugDragHintColor;
                _subCameraRectDebugDragHintMaterial.renderQueue = 5050;
                renderer.sharedMaterial = _subCameraRectDebugDragHintMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            _subCameraRectDebugDragHintObject.SetActive(false);
        }

        private void HandleSubCameraRectDebugPlaneAnchorToggle(
            bool togglePressed,
            GameObject vrRig,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            if (!togglePressed) return;

            // Match Danmen behavior: toggle cancels manual pose so panel can return to follow mode.
            _hasSubCameraRectDebugManualPose = false;
            _isSubCameraRectDebugPlaneAnchoredToRig = !_isSubCameraRectDebugPlaneAnchoredToRig;
            if (_isSubCameraRectDebugPlaneAnchoredToRig)
            {
                if (TryGetSubCameraRectDebugPlaneFollowPose(hasRightHandPose, rightHandWorldPos, rightHandWorldRot, out Vector3 followPos, out Quaternion followRot))
                {
                    _subCameraRectDebugPlaneObject.transform.SetPositionAndRotation(followPos, followRot);
                }

                CaptureSubCameraRectDebugPlaneAnchoredPose(vrRig);
                VRModCore.Log("[SubCamera][OpenXR] Panel mode: Anchored (follows rig movement, not controller).");
            }
            else
            {
                VRModCore.Log("[SubCamera][OpenXR] Panel mode: Right-hand follow.");
            }
        }

        private void CaptureSubCameraRectDebugPlaneAnchoredPose(GameObject vrRig)
        {
            if (vrRig == null || _subCameraRectDebugPlaneObject == null) return;

            _subCameraRectDebugPlaneAnchoredLocalPos = vrRig.transform.InverseTransformPoint(_subCameraRectDebugPlaneObject.transform.position);
            _subCameraRectDebugPlaneAnchoredLocalRot = Quaternion.Inverse(vrRig.transform.rotation) * _subCameraRectDebugPlaneObject.transform.rotation;
        }

        private void UpdateSubCameraRectDebugVisuals(
            GameObject vrRig,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            if (vrRig == null || _subCameraRectDebugPlaneObject == null || _subCameraRectDebugOutline == null) return;

            RefreshSubCameraRectDebugSourceIfNeeded(force: false);
            bool hasControlCameraEnabled = IsSubCameraControlCameraEnabled();
            bool hasTexture = _subCameraRectDebugSourceTexture != null && hasControlCameraEnabled;

            bool hasPose;
            Vector3 planeWorldPos;
            Quaternion planeWorldRot;
            if (_hasSubCameraRectDebugManualPose)
            {
                hasPose = true;
                planeWorldPos = _subCameraRectDebugManualWorldPos;
                planeWorldRot = _subCameraRectDebugManualWorldRot;
            }
            else if (_isSubCameraRectDebugPlaneAnchoredToRig)
            {
                hasPose = true;
                planeWorldPos = vrRig.transform.TransformPoint(_subCameraRectDebugPlaneAnchoredLocalPos);
                planeWorldRot = vrRig.transform.rotation * _subCameraRectDebugPlaneAnchoredLocalRot;
            }
            else
            {
                hasPose = TryGetSubCameraRectDebugPlaneFollowPose(hasRightHandPose, rightHandWorldPos, rightHandWorldRot, out planeWorldPos, out planeWorldRot);
            }

            if (!hasPose || !hasTexture)
            {
                _hasSubCameraRectDebugUvRect = false;
                _subCameraRectDebugUvRect = default;
                _subCameraRectDebugOutline.enabled = false;
                if (_subCameraRectDebugContentObject != null && _subCameraRectDebugContentObject.activeSelf)
                {
                    _subCameraRectDebugContentObject.SetActive(false);
                }
                if (_subCameraRectDebugPlaneObject.activeSelf)
                {
                    _subCameraRectDebugPlaneObject.SetActive(false);
                }
                UpdateSubCameraRectDebugEdgeFrameVisibility();
                SetSubCameraRectDebugResizeHandlesVisible(false);
                SetSubCameraRectDebugDragHintVisible(false);
                return;
            }

            if (!_subCameraRectDebugPlaneObject.activeSelf)
            {
                _subCameraRectDebugPlaneObject.SetActive(true);
            }

            _subCameraRectDebugPlaneObject.transform.SetPositionAndRotation(planeWorldPos, planeWorldRot);

            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float aspect = screenWidth / screenHeight;
            float planeWidth = SubCameraRectDebugPlaneWidthMeters * GetSubCameraProjectionPanelScale();
            float planeHeight = planeWidth / Mathf.Max(0.1f, aspect);
            _subCameraRectDebugPlaneObject.transform.localScale = new Vector3(planeWidth, planeHeight, 1f);

            if (!TryGetSubCameraScreenRect(out Rect rect))
            {
                _hasSubCameraRectDebugUvRect = false;
                _subCameraRectDebugUvRect = default;
                _subCameraRectDebugOutline.enabled = false;
                if (_subCameraRectDebugContentObject != null && _subCameraRectDebugContentObject.activeSelf)
                {
                    _subCameraRectDebugContentObject.SetActive(false);
                }
                SetSubCameraRectDebugResizeHandlesVisible(false);
                SetSubCameraRectDebugDragHintVisible(false);
                return;
            }

            float minXNorm = Mathf.Clamp01(rect.xMin / screenWidth);
            float minYNorm = Mathf.Clamp01(rect.yMin / screenHeight);
            float maxXNorm = Mathf.Clamp01(rect.xMax / screenWidth);
            float maxYNorm = Mathf.Clamp01(rect.yMax / screenHeight);
            _hasSubCameraRectDebugUvRect = true;
            _subCameraRectDebugUvRect = Rect.MinMaxRect(minXNorm, minYNorm, maxXNorm, maxYNorm);

            float minX = minXNorm - 0.5f;
            float minY = minYNorm - 0.5f;
            float maxX = maxXNorm - 0.5f;
            float maxY = maxYNorm - 0.5f;
            const float zOffset = -0.01f;
            const float contentZOffset = -0.006f;

            _subCameraRectDebugOutline.SetPosition(0, new Vector3(minX, minY, zOffset));
            _subCameraRectDebugOutline.SetPosition(1, new Vector3(minX, maxY, zOffset));
            _subCameraRectDebugOutline.SetPosition(2, new Vector3(maxX, maxY, zOffset));
            _subCameraRectDebugOutline.SetPosition(3, new Vector3(maxX, minY, zOffset));
            _subCameraRectDebugOutline.SetPosition(4, new Vector3(minX, minY, zOffset));
            _subCameraRectDebugOutline.enabled = _subCameraRectDebugOutlineVisibleRequested;

            UpdateSubCameraRectDebugEdgeFrameVisual(minX, minY, maxX, maxY);
            UpdateSubCameraRectDebugResizeHandlesVisual(minX, minY, maxX, maxY);
            UpdateSubCameraRectDebugDragHintVisual(minX, minY, maxX, maxY);
            UpdateSubCameraRectDebugContentVisual(minX, minY, maxX, maxY, contentZOffset);
            UpdateSubCameraRectDebugEdgeFrameVisibility();
        }

        private void UpdateSubCameraRectDebugEdgeFrameVisual(float minX, float minY, float maxX, float maxY)
        {
            if (_subCameraRectDebugEdgeFrameLine == null) return;

            float edgeMinX = minX - SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMaxX = maxX + SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMinY = minY - SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMaxY = maxY + SubCameraRectDebugEdgeFrameExpandNormalized;
            const float zOffset = -0.008f;

            _subCameraRectDebugEdgeFrameLine.SetPosition(0, new Vector3(edgeMinX, edgeMinY, zOffset));
            _subCameraRectDebugEdgeFrameLine.SetPosition(1, new Vector3(edgeMinX, edgeMaxY, zOffset));
            _subCameraRectDebugEdgeFrameLine.SetPosition(2, new Vector3(edgeMaxX, edgeMaxY, zOffset));
            _subCameraRectDebugEdgeFrameLine.SetPosition(3, new Vector3(edgeMaxX, edgeMinY, zOffset));
        }

        private void UpdateSubCameraRectDebugResizeHandlesVisual(float minX, float minY, float maxX, float maxY)
        {
            float edgeMinX = minX - SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMaxX = maxX + SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMinY = minY - SubCameraRectDebugEdgeFrameExpandNormalized;
            float edgeMaxY = maxY + SubCameraRectDebugEdgeFrameExpandNormalized;

            float sx = Mathf.Max(0.0001f, _subCameraRectDebugPlaneObject != null ? _subCameraRectDebugPlaneObject.transform.localScale.x : 1f);
            float sy = Mathf.Max(0.0001f, _subCameraRectDebugPlaneObject != null ? _subCameraRectDebugPlaneObject.transform.localScale.y : 1f);
            float localDiameterX = (SubCameraRectDebugResizeHandleRadiusMeters * 2f) / sx;
            float localDiameterY = (SubCameraRectDebugResizeHandleRadiusMeters * 2f) / sy;
            float localDiameter = Mathf.Max(0.01f, Mathf.Min(localDiameterX, localDiameterY));

            UpdateSubCameraRectDebugResizeHandle(0, new Vector3(edgeMinX, edgeMinY, -0.007f), localDiameter);
            UpdateSubCameraRectDebugResizeHandle(1, new Vector3(edgeMinX, edgeMaxY, -0.007f), localDiameter);
            UpdateSubCameraRectDebugResizeHandle(2, new Vector3(edgeMaxX, edgeMaxY, -0.007f), localDiameter);
            UpdateSubCameraRectDebugResizeHandle(3, new Vector3(edgeMaxX, edgeMinY, -0.007f), localDiameter);
        }

        private void UpdateSubCameraRectDebugResizeHandle(int index, Vector3 localPosition, float localDiameter)
        {
            if (index < 0 || index >= _subCameraRectDebugResizeHandles.Length) return;
            GameObject handle = _subCameraRectDebugResizeHandles[index];
            if (handle == null) return;

#if MONO
            handle.transform.localPosition = localPosition;
            handle.transform.localScale = new Vector3(localDiameter, localDiameter, localDiameter * SubCameraRectDebugResizeHandleDepthScale);
#elif CPP
            handle.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
            handle.transform.localScale = new Vector3(localDiameter, localDiameter, localDiameter * SubCameraRectDebugResizeHandleDepthScale);
#endif
        }

        private void SetSubCameraRectDebugResizeHandlesVisible(bool visible)
        {
            for (int i = 0; i < _subCameraRectDebugResizeHandles.Length; i++)
            {
                GameObject handle = _subCameraRectDebugResizeHandles[i];
                if (handle == null) continue;
                if (handle.activeSelf != visible)
                {
                    handle.SetActive(visible);
                }
            }
        }

        private void UpdateSubCameraRectDebugDragHintVisual(float minX, float minY, float maxX, float maxY)
        {
            if (_subCameraRectDebugDragHintObject == null) return;
            float size = SubCameraRectDebugDragHintSizeNormalized;
            float half = size * 0.5f;
            // Keep the drag hint inside the red content frame (bottom-right), not on the outer edge frame.
            float inset = Mathf.Max(0.006f, size * 0.35f);
            float hintX = maxX - half - inset;
            float hintY = minY + half + inset;

#if MONO
            _subCameraRectDebugDragHintObject.transform.localPosition = new Vector3(hintX, hintY, -0.0065f);
            _subCameraRectDebugDragHintObject.transform.localRotation = Quaternion.identity;
            _subCameraRectDebugDragHintObject.transform.localScale = new Vector3(size, size, 1f);
#elif CPP
            _subCameraRectDebugDragHintObject.transform.SetLocalPositionAndRotation(
                new Vector3(hintX, hintY, -0.0065f),
                Quaternion.identity);
            _subCameraRectDebugDragHintObject.transform.localScale = new Vector3(size, size, 1f);
#endif
        }

        private void SetSubCameraRectDebugDragHintVisible(bool visible)
        {
            if (_subCameraRectDebugDragHintObject == null) return;
            if (_subCameraRectDebugDragHintObject.activeSelf != visible)
            {
                _subCameraRectDebugDragHintObject.SetActive(visible);
            }
        }

        private void UpdateSubCameraRectDebugEdgeFrameVisibility()
        {
            if (_subCameraRectDebugEdgeFrameObject == null) return;
            bool shouldShow = _subCameraRectDebugEdgeHighlightRequested &&
                              _subCameraRectDebugPlaneObject != null &&
                              _subCameraRectDebugPlaneObject.activeInHierarchy;
            if (_subCameraRectDebugEdgeFrameObject.activeSelf != shouldShow)
            {
                _subCameraRectDebugEdgeFrameObject.SetActive(shouldShow);
            }

            SetSubCameraRectDebugResizeHandlesVisible(shouldShow);
            SetSubCameraRectDebugDragHintVisible(
                _subCameraRectDebugPlaneObject != null &&
                _subCameraRectDebugPlaneObject.activeInHierarchy &&
                _hasSubCameraRectDebugUvRect);
        }

        private void UpdateSubCameraRectDebugContentVisual(float minX, float minY, float maxX, float maxY, float zOffset)
        {
            if (_subCameraRectDebugContentObject == null || _subCameraRectDebugContentMaterial == null) return;

            if (_subCameraRectDebugSourceTexture == null)
            {
                if (_subCameraRectDebugContentObject.activeSelf)
                {
                    _subCameraRectDebugContentObject.SetActive(false);
                }
                return;
            }

            if (!ReferenceEquals(_subCameraRectDebugContentMaterial.mainTexture, _subCameraRectDebugSourceTexture))
            {
                _subCameraRectDebugContentMaterial.mainTexture = _subCameraRectDebugSourceTexture;
            }

            float width = Mathf.Max(0.001f, maxX - minX);
            float height = Mathf.Max(0.001f, maxY - minY);
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            _subCameraRectDebugContentObject.transform.localPosition = new Vector3(centerX, centerY, zOffset);
            _subCameraRectDebugContentObject.transform.localRotation = Quaternion.identity;
            _subCameraRectDebugContentObject.transform.localScale = new Vector3(width, height, 1f);
            if (!_subCameraRectDebugContentObject.activeSelf)
            {
                _subCameraRectDebugContentObject.SetActive(true);
            }
        }

        private void RefreshSubCameraRectDebugSourceIfNeeded(bool force)
        {
            if (!force && Time.time < _nextSubCameraRectDebugSourceLookupTime && _subCameraRectDebugSourceTexture != null)
            {
                return;
            }

            _nextSubCameraRectDebugSourceLookupTime = Time.time + SubCameraDebugSourceLookupIntervalSeconds;

            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (!IsValidSubCameraDebugSourceCamera(cam)) continue;
                if (best == null || cam.depth >= bestDepth)
                {
                    best = cam;
                    bestDepth = cam.depth;
                }
            }

            _subCameraRectDebugSourceCamera = best;
            _subCameraRectDebugSourceTexture = _subCameraRectDebugSourceCamera != null ? _subCameraRectDebugSourceCamera.targetTexture : null;

            if (_subCameraRectDebugSourceTexture == null)
            {
                if (!_loggedSubCameraRectDebugMissingSource)
                {
                    _loggedSubCameraRectDebugMissingSource = true;
                    VRModCore.LogWarning("[SubCamera][OpenXR] No active SubCamera RenderTexture source found for projection panel.");
                }
            }
            else
            {
                _loggedSubCameraRectDebugMissingSource = false;
            }
        }

        private bool IsSubCameraControlCameraEnabled()
        {
            Camera controlCamera = ResolveSubCameraControlCamera();
            bool enabled = controlCamera != null && controlCamera.enabled && controlCamera.gameObject.activeInHierarchy;

            if (!_hasSubCameraControlCameraStateSample || enabled != _lastSubCameraControlCameraEnabled)
            {
                _hasSubCameraControlCameraStateSample = true;
                _lastSubCameraControlCameraEnabled = enabled;
                if (enabled)
                {
                    VRModCore.Log("[SubCamera][OpenXR] Camera_MainSub enabled. Projection panel visible.");
                }
                else
                {
                    VRModCore.LogWarning("[SubCamera][OpenXR] Camera_MainSub disabled or missing. Projection panel hidden.");
                }
            }

            return enabled;
        }

        private Camera ResolveSubCameraControlCamera()
        {
            if (_subCameraControlCamera != null)
            {
                return _subCameraControlCamera;
            }

            if (Time.time < _nextSubCameraControlCameraLookupTime)
            {
                return null;
            }

            _nextSubCameraControlCameraLookupTime = Time.time + SubCameraControlCameraLookupIntervalSeconds;

            Camera found = null;
            foreach (Camera cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (cam == null) continue;
                if (string.Equals(cam.name, SubCameraControlCameraName, StringComparison.OrdinalIgnoreCase))
                {
                    found = cam;
                    break;
                }
            }

            _subCameraControlCamera = found;
            return _subCameraControlCamera;
        }

        private static bool IsValidSubCameraDebugSourceCamera(Camera cam)
        {
            if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) return false;
            if (cam.targetTexture == null) return false;
            if ((cam.hideFlags & HideFlags.HideAndDontSave) != 0) return false;

            string cameraName = cam.name ?? string.Empty;
            string textureName = cam.targetTexture != null ? (cam.targetTexture.name ?? string.Empty) : string.Empty;
            for (int i = 0; i < SubCameraSourceNameTokens.Length; i++)
            {
                string token = SubCameraSourceNameTokens[i];
                if (cameraName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    textureName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSubCameraRectDebugPlaneFollowPose(
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot,
            out Vector3 planeWorldPos,
            out Quaternion planeWorldRot)
        {
            planeWorldPos = default;
            planeWorldRot = Quaternion.identity;
            if (!hasRightHandPose)
            {
                return false;
            }

            Vector3 handOffset = new(
                SubCameraRectDebugPlaneRightHandRightOffsetMeters,
                SubCameraRectDebugPlaneRightHandUpOffsetMeters,
                SubCameraRectDebugPlaneRightHandForwardOffsetMeters);
            planeWorldPos = rightHandWorldPos + (rightHandWorldRot * handOffset);
            planeWorldRot = Quaternion.LookRotation(
                rightHandWorldRot * Vector3.forward,
                rightHandWorldRot * Vector3.up);
            return true;
        }

        private void EnsureRayVisuals(GameObject vrRig)
        {
            if (_rayLine != null && _cursorObject != null) return;

            _rayObject = new GameObject(RayObjectName);
            _rayObject.transform.SetParent(vrRig.transform, false);
            SetLayerRecursively(_rayObject, vrRig.layer);
            _rayLine = _rayObject.AddComponent<LineRenderer>();
            _rayLine.useWorldSpace = true;
            _rayLine.positionCount = 2;
            _rayLine.startWidth = RayWidthMeters;
            _rayLine.endWidth = RayWidthMeters;
            _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rayLine.receiveShadows = false;
            _rayLine.numCapVertices = 4;

            Shader rayShader = Shader.Find("Sprites/Default");
            if (rayShader == null) rayShader = Shader.Find("Unlit/Color");
            _rayMaterial = new Material(rayShader);
            _rayMaterial.color = RayVisibleColor;
            _rayLine.material = _rayMaterial;

            _cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _cursorObject.name = CursorObjectName;
            _cursorObject.transform.SetParent(vrRig.transform, true);
            SetLayerRecursively(_cursorObject, vrRig.layer);
            _cursorObject.transform.localScale = Vector3.one * (CursorScaleMeters * GetUiPanelScaleMultiplier());

            object collider = _cursorObject.GetComponent("Collider");
            if (collider is UnityEngine.Object colliderObj) UnityEngine.Object.Destroy(colliderObj);

            Renderer cursorRenderer = _cursorObject.GetComponent<Renderer>();
            if (cursorRenderer != null)
            {
                cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cursorRenderer.receiveShadows = false;
                Shader cursorShader = Shader.Find("Sprites/Default");
                if (cursorShader == null) cursorShader = Shader.Find("Unlit/Color");
                _cursorMaterial = new Material(cursorShader);
                _cursorMaterial.color = RayVisibleColor;
                cursorRenderer.sharedMaterial = _cursorMaterial;
            }

            SetRayVisible(false);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;

            root.layer = layer;
            Transform tr = root.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                SetLayerRecursively(tr.GetChild(i).gameObject, layer);
            }
        }

        private void SetRayVisible(bool visible)
        {
            if (_rayLine != null) _rayLine.enabled = visible;
            if (_cursorObject != null && _cursorObject.activeSelf != visible) _cursorObject.SetActive(visible);
        }

        private void UpdateRayVisual(Vector3 origin, Vector3 endPoint, bool hasTarget)
        {
            if (_rayLine == null || _cursorObject == null) return;
            UpdateCursorScaleIfNeeded();

            SetRayVisible(hasTarget);
            if (!hasTarget) return;

            _rayLine.SetPosition(0, origin);
            _rayLine.SetPosition(1, endPoint);

            if (_rayMaterial != null) _rayMaterial.color = RayVisibleColor;
            if (_cursorMaterial != null) _cursorMaterial.color = RayVisibleColor;
            _cursorObject.transform.position = endPoint;
        }

        private void UpdateCursorScaleIfNeeded()
        {
            if (_cursorObject == null) return;

            float scaleMultiplier = GetUiPanelScaleMultiplier();
            if (Mathf.Abs(scaleMultiplier - _lastAppliedCursorScale) <= 0.0001f) return;

            _lastAppliedCursorScale = scaleMultiplier;
            _cursorObject.transform.localScale = Vector3.one * (CursorScaleMeters * scaleMultiplier);
        }

        private static float GetUiPanelScaleMultiplier()
        {
            float configuredScale = ConfigManager.OpenXR_UiPanelScale?.Value ?? 1.0f;
            return Mathf.Clamp(configuredScale, MinPanelScale, MaxPanelScale);
        }

        private void DestroyRayVisuals()
        {
            if (_rayObject != null) UnityEngine.Object.Destroy(_rayObject);
            if (_cursorObject != null) UnityEngine.Object.Destroy(_cursorObject);
            if (_rayMaterial != null) UnityEngine.Object.Destroy(_rayMaterial);
            if (_cursorMaterial != null) UnityEngine.Object.Destroy(_cursorMaterial);
            if (_controllerIconObject != null) UnityEngine.Object.Destroy(_controllerIconObject);
            if (_controllerIconMaterial != null) UnityEngine.Object.Destroy(_controllerIconMaterial);
            if (_fixedTouchIconTexture != null) UnityEngine.Object.Destroy(_fixedTouchIconTexture);
            if (_subCameraRectDebugPlaneObject != null) UnityEngine.Object.Destroy(_subCameraRectDebugPlaneObject);
            if (_subCameraRectDebugPlaneMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugPlaneMaterial);
            if (_subCameraRectDebugContentMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugContentMaterial);
            if (_subCameraRectDebugEdgeFrameMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugEdgeFrameMaterial);
            if (_subCameraRectDebugResizeHandleMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugResizeHandleMaterial);
            if (_subCameraRectDebugDragHintMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugDragHintMaterial);
            if (_subCameraRectDebugOutlineMaterial != null) UnityEngine.Object.Destroy(_subCameraRectDebugOutlineMaterial);

            _rayObject = null;
            _rayLine = null;
            _rayMaterial = null;
            _cursorObject = null;
            _cursorMaterial = null;
            _controllerIconObject = null;
            _controllerIconMaterial = null;
            _controllerIconTexture = null;
            _fixedTouchIconTexture = null;
            _subCameraRectDebugPlaneObject = null;
            _subCameraRectDebugPlaneMaterial = null;
            _subCameraRectDebugContentObject = null;
            _subCameraRectDebugContentMaterial = null;
            _subCameraRectDebugEdgeFrameObject = null;
            _subCameraRectDebugEdgeFrameLine = null;
            _subCameraRectDebugEdgeFrameMaterial = null;
            for (int i = 0; i < _subCameraRectDebugResizeHandles.Length; i++)
            {
                _subCameraRectDebugResizeHandles[i] = null;
            }
            _subCameraRectDebugResizeHandleMaterial = null;
            _subCameraRectDebugDragHintObject = null;
            _subCameraRectDebugDragHintMaterial = null;
            _subCameraRectDebugPanelScale = 1f;
            _subCameraRectDebugEdgeHighlightRequested = false;
            _subCameraRectDebugSourceTexture = null;
            _subCameraRectDebugSourceCamera = null;
            _nextSubCameraRectDebugSourceLookupTime = 0f;
            _loggedSubCameraRectDebugMissingSource = false;
            _subCameraControlCamera = null;
            _nextSubCameraControlCameraLookupTime = 0f;
            _hasSubCameraControlCameraStateSample = false;
            _lastSubCameraControlCameraEnabled = false;
            _subCameraRectDebugOutline = null;
            _subCameraRectDebugOutlineMaterial = null;
        }

        private static bool TryGetProjectionPlaneUv(GameObject surface, Vector3 hitWorldPoint, out Vector2 uv)
        {
            uv = default;
            if (surface == null) return false;

            Vector3 local = surface.transform.InverseTransformPoint(hitWorldPoint);
            if (local.x < -0.5f - UiSurfaceBoundsEpsilon || local.x > 0.5f + UiSurfaceBoundsEpsilon ||
                local.y < -0.5f - UiSurfaceBoundsEpsilon || local.y > 0.5f + UiSurfaceBoundsEpsilon)
            {
                return false;
            }

            uv.x = Mathf.Clamp01(local.x + 0.5f);
            uv.y = Mathf.Clamp01(local.y + 0.5f);
            return true;
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

        private static bool TryGetProjectionPlaneUvUnclamped(GameObject surface, Vector3 hitWorldPoint, out Vector2 uv)
        {
            uv = default;
            if (surface == null) return false;

            Vector3 local = surface.transform.InverseTransformPoint(hitWorldPoint);
            // Deliberately not clamped: dragging can continue naturally after the ray exits the panel.
            uv.x = local.x + 0.5f;
            uv.y = local.y + 0.5f;
            return true;
        }

        private static bool IsWithinProjectionPlaneVisualBounds(GameObject surface, Vector3 hitWorldPoint)
        {
            if (surface == null) return false;

            Vector3 local = surface.transform.InverseTransformPoint(hitWorldPoint);
            float visualHalf = 0.5f + UiSurfaceVisualEdgeExpandNormalized + UiSurfaceBoundsEpsilon;
            return local.x >= -visualHalf && local.x <= visualHalf &&
                   local.y >= -visualHalf && local.y <= visualHalf;
        }

        private void ReleasePointerIfNeeded()
        {
            if (!_pointerIsDown) return;

            if (IsWindowsPlatform)
            {
                _ = TrySendMouseLeftEvent(false, out _);
            }

            _pointerIsDown = false;
            _activePointerSurface = PointerSurfaceKind.None;
            _hasFrozenSubCameraRectForDrag = false;
            _subCameraPlaneDragActive = false;
        }

        private bool TryInjectMousePointerPosition(Vector2 pointerScreenPos)
        {
            if (!IsWindowsPlatform)
            {
                if (!_loggedMouseInjectionUnsupportedPlatform)
                {
                    _loggedMouseInjectionUnsupportedPlatform = true;
                    VRModCore.LogWarning("[UI][OpenXR][Mouse] Mouse pointer injection is only supported on Windows.");
                }

                return false;
            }

            if (!TryGetMouseInjectionWindowHandle(out IntPtr windowHandle))
            {
                if (!_loggedMouseInjectionWindowMissing)
                {
                    _loggedMouseInjectionWindowMissing = true;
                    VRModCore.LogWarning("[UI][OpenXR][Mouse] Failed to resolve game window handle for pointer injection.");
                }

                return false;
            }

            _loggedMouseInjectionWindowMissing = false;

            if (!GetClientRect(windowHandle, out RECT clientRect))
            {
                return false;
            }

            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;
            if (clientWidth <= 0 || clientHeight <= 0) return false;

            float screenWidth = Mathf.Max(1f, Screen.width - 1f);
            float screenHeight = Mathf.Max(1f, Screen.height - 1f);
            float clampedX = Mathf.Clamp(pointerScreenPos.x, 0f, screenWidth);
            float clampedY = Mathf.Clamp(pointerScreenPos.y, 0f, screenHeight);

            int clientX = Mathf.Clamp(Mathf.RoundToInt((clampedX / screenWidth) * (clientWidth - 1)), 0, clientWidth - 1);
            int clientY = Mathf.Clamp(Mathf.RoundToInt(((screenHeight - clampedY) / screenHeight) * (clientHeight - 1)), 0, clientHeight - 1);

            POINT clientPoint = new() { X = clientX, Y = clientY };
            if (!ClientToScreen(windowHandle, ref clientPoint))
            {
                return false;
            }

            return SetCursorPos(clientPoint.X, clientPoint.Y);
        }

        private static bool TrySendMouseLeftEvent(bool buttonDown, out int lastError)
        {
            INPUT[] input =
            [
                new INPUT
                {
                    type = InputTypeMouse,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = buttonDown ? MouseEventFlagLeftDown : MouseEventFlagLeftUp,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            ];

            uint sent = SendInput(1u, input, Marshal.SizeOf(typeof(INPUT)));
            if (sent == 1u)
            {
                lastError = 0;
                return true;
            }

            lastError = Marshal.GetLastWin32Error();
            return false;
        }

        private static bool TryGetMouseInjectionWindowHandle(out IntPtr windowHandle)
        {
            windowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (windowHandle != IntPtr.Zero) return true;

            windowHandle = GetForegroundWindow();
            return windowHandle != IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
#endif
