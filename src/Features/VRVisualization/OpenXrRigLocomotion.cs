using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrRigLocomotion
    {
        private const float TeleportMaxDistanceMeters = 12f;
        private const float TeleportColliderSurfaceMinUpDot = 0.35f;
        private const float TeleportLineWidthMeters = 0.010f;
        private const float TeleportHitRingYOffsetMeters = 0.05f;
        private const float TeleportHitRingRadiusMeters = 0.085f;
        private const float TeleportHitRingWidthMeters = 0.0075f;
        private const int TeleportHitRingSegments = 40;
        private const int TeleportArcSegments = 28;
        private const float TeleportArcTimeStepSeconds = 0.05f;
        private const float TeleportArcInitialSpeedMetersPerSecond = 7.8f;
        private const float TeleportTopModelTargetLongestAxisMeters = 0.14f;
        private const float TeleportTopModelScaleMultiplier = 1.0f;
        private const string TeleportTopModelFileName = "headset.obj";
        private const string TeleportTopModelRelativeFolder = "Model";
        private const float TeleportTopSphereDiameterMeters = 0.14f;
        private const float TeleportConeHeightMeters = 0.12f;
        private const float TeleportConeRadiusMeters = 0.06f;
        private const int TeleportConeSegments = 20;
        private const float GroundPlaneRefreshIntervalSeconds = 1.5f;
        private const string GroundLayerName = "BG";
        private const float DefaultSnapTurnDegrees = 30f;
        private const float DefaultSmoothTurnDegreesPerSecond = 45f;
        private const float SnapTurnThreshold = 0.70f;
        private const float SnapTurnReleaseThreshold = 0.28f;
        private const float SnapTurnCooldownSeconds = 0.20f;
        private const float SmoothTurnDeadzone = 0.18f;
        private const float SmoothMoveSpeedMetersPerSecond = 1.5f;
        private const float DefaultGripDragSensitivity = 0.45f;
        private const float GripDragDeadzoneMeters = 0.002f;
        private const float GripDragMaxStepMeters = 0.20f;
        private const float GripReleaseGraceSeconds = 0.08f;
        private const float GripDeltaSmoothing = 0.35f;
        private static readonly bool GripDragLockY = false;
        private static readonly bool GripDragInvertDirection = true;
        private static readonly Vector3 TeleportArcGravity = new(0f, -9.81f, 0f);

        private static readonly string[] GroundNamesExact =
        [
            "floor",
            "ground",
            "tatami"
        ];

        private static readonly Color TeleportValidColor = new(1f, 1f, 1f, 0.92f);
        private static readonly Color TeleportInvalidColor = new(1f, 1f, 1f, 0.55f);
        private static readonly Color TeleportHitRingColor = new(0.63f, 0.84f, 1.00f, 0.95f);
        private static MethodInfo PhysicsRaycastMethod;
        private static MethodInfo PhysicsRaycastAllMethod;
        private static Type PhysicsRaycastHitType;
        private static Type PhysicsQueryTriggerInteractionType;
        private static int PhysicsRaycastParameterCount;
        private static int PhysicsRaycastAllParameterCount;
        private static bool PhysicsRaycastResolved;
        private static readonly Dictionary<Type, MethodInfo> ColliderRaycastMethodCache = [];
        private static readonly Dictionary<Type, Type> ColliderRaycastHitTypeCache = [];
        private static readonly Dictionary<Type, RaycastHitAccessor> RaycastHitAccessorCache = [];

        private bool _lastConfirmPressed;
        private GameObject _teleportMarkerRoot;
        private GameObject _teleportMarkerLine;
        private LineRenderer _teleportArcLineRenderer;
        private GameObject _teleportMarkerTopModel;
        private LineRenderer _teleportHitRingRenderer;
        private Material _teleportLineMaterial;
        private Material _teleportTopModelMaterial;
        private static Mesh _teleportTopModelMesh;
        private static bool _teleportTopModelMeshLoadAttempted;
        private bool _hasGroundPlaneY;
        private float _groundPlaneY;
        private string _groundObjectName = string.Empty;
        private bool _hasGroundProbeReferenceWorldPos;
        private Vector3 _groundProbeReferenceWorldPos;
        private float _nextGroundProbeTime;
        private bool _hasLoggedMissingGround;
        private bool _hasFixedTeleportPlaneY;
        private float _fixedTeleportPlaneY;
        private bool _snapTurnLatched;
        private float _nextSnapTurnTime;
        private bool _isGripDragging;
        private Vector3 _lastGripControllerTrackingLocalPos;
        private Vector3 _smoothedGripDelta;
        private float _lastGripPressedTime;
        private string _lastTeleportColliderSourceName = string.Empty;
        private string _teleportColliderCacheSceneName = string.Empty;
        private readonly List<TeleportColliderCandidate> _teleportColliderCandidates = [];
        private bool _teleportColliderCacheReady;
        private readonly Vector3[] _teleportArcPoints = new Vector3[TeleportArcSegments + 1];
        private int _teleportArcPointCount;
        private TeleportTargetSource _lastTeleportTargetSource = TeleportTargetSource.Unknown;
        private int _teleportEvalSegmentCount;
        private int _teleportEvalSegmentsWithAnyHit;
        private int _teleportEvalHitsTotal;
        private int _teleportEvalRejectedByNormal;
        private int _teleportEvalRejectedByDistance;
        private string _teleportEvalLastRejectedName = string.Empty;
        private float _teleportEvalLastRejectedDotUp;
        private int _teleportEvalAllLayerSegmentsWithAnyHit;
        private int _teleportEvalAllLayerHitsTotal;
        private string _teleportEvalAllLayerFirstHitName = string.Empty;
        private int _teleportEvalAllLayerFirstHitLayer = -1;
        private string _lastFallbackRejectSummary = string.Empty;
        private string _lastColliderInventorySceneName = string.Empty;

        private enum TeleportTargetSource
        {
            Unknown = 0,
            SceneHit = 1,
            FallbackPlane = 2
        }

        private sealed class RaycastHitAccessor
        {
            public PropertyInfo DistanceProperty;
            public PropertyInfo PointProperty;
            public PropertyInfo NormalProperty;
            public PropertyInfo ColliderProperty;
        }

        private sealed class TeleportColliderCandidate
        {
            public object Collider;
            public string Name;
            public string HierarchyPath;
        }

        public void Update(
            GameObject vrRig,
            float rightStickX,
            bool isSmoothTurnHeld,
            bool isGripHeld,
            bool hasGripLocalPose,
            Vector3 rightGripTrackingLocalPos,
            bool isAiming,
            bool confirmPressed,
            bool hasPointerPose,
            Vector3 pointerOriginWorld,
            Vector3 pointerDirectionWorld,
            Vector3 cameraForwardWorld,
            bool hasHmdWorldPose,
            Vector3 hmdWorldPos,
            float leftStickX,
            float leftStickY)
        {
            if (vrRig == null)
            {
                SetTeleportMarkerVisible(false);
                _lastConfirmPressed = false;
                return;
            }

            if (!EnsurePhysicsRaycastBinding())
            {
                string activeSceneName = SceneManager.GetActiveScene().name;
                if (!_teleportColliderCacheReady || !string.Equals(_teleportColliderCacheSceneName, activeSceneName, StringComparison.Ordinal))
                {
                    RefreshTeleportColliderCache(vrRig);
                }
            }

            LogTeleportColliderInventoryIfNeeded();

            if (!isAiming)
            {
                UpdateTurn(vrRig, rightStickX, isSmoothTurnHeld, hasHmdWorldPose, hmdWorldPos);
                UpdateSmoothMove(vrRig, leftStickX, leftStickY, cameraForwardWorld);
            }
            UpdateGripDragTranslate(vrRig, isGripHeld, hasGripLocalPose, rightGripTrackingLocalPos);

            if (isGripHeld)
            {
                SetTeleportMarkerVisible(false);
                _lastConfirmPressed = confirmPressed;
                return;
            }

            if (!isAiming)
            {
                SetTeleportMarkerVisible(false);
                _lastConfirmPressed = confirmPressed;
                return;
            }

            if (!hasPointerPose)
            {
                SetTeleportMarkerVisible(false);
                _lastConfirmPressed = confirmPressed;
                return;
            }

            bool hasTeleportTarget = TryGetTeleportTarget(vrRig, pointerOriginWorld, pointerDirectionWorld, out Vector3 teleportTargetGround);
            UpdateTeleportMarker(vrRig, hasTeleportTarget, teleportTargetGround);

            if (confirmPressed && !_lastConfirmPressed && hasTeleportTarget)
            {
                PerformTeleport(vrRig, teleportTargetGround, hasHmdWorldPose, hmdWorldPos);
            }

            _lastConfirmPressed = confirmPressed;
        }

        public void Teardown()
        {
            if (_teleportMarkerRoot != null)
            {
                UnityEngine.Object.Destroy(_teleportMarkerRoot);
                _teleportMarkerRoot = null;
            }

            _teleportMarkerLine = null;
            _teleportArcLineRenderer = null;
            _teleportMarkerTopModel = null;
            _teleportHitRingRenderer = null;
            if (_teleportLineMaterial != null)
            {
                UnityEngine.Object.Destroy(_teleportLineMaterial);
                _teleportLineMaterial = null;
            }
            if (_teleportTopModelMaterial != null)
            {
                UnityEngine.Object.Destroy(_teleportTopModelMaterial);
                _teleportTopModelMaterial = null;
            }
            _lastConfirmPressed = false;
            _hasGroundPlaneY = false;
            _groundPlaneY = 0f;
            _groundObjectName = string.Empty;
            _hasGroundProbeReferenceWorldPos = false;
            _groundProbeReferenceWorldPos = Vector3.zero;
            _nextGroundProbeTime = 0f;
            _hasLoggedMissingGround = false;
            _hasFixedTeleportPlaneY = false;
            _fixedTeleportPlaneY = 0f;
            _snapTurnLatched = false;
            _nextSnapTurnTime = 0f;
            _isGripDragging = false;
            _lastGripControllerTrackingLocalPos = default;
            _smoothedGripDelta = Vector3.zero;
            _lastGripPressedTime = 0f;
            _lastTeleportColliderSourceName = string.Empty;
            _teleportColliderCacheSceneName = string.Empty;
            _teleportColliderCandidates.Clear();
            _teleportColliderCacheReady = false;
            _teleportArcPointCount = 0;
            _lastTeleportTargetSource = TeleportTargetSource.Unknown;
            _teleportEvalSegmentCount = 0;
            _teleportEvalSegmentsWithAnyHit = 0;
            _teleportEvalHitsTotal = 0;
            _teleportEvalRejectedByNormal = 0;
            _teleportEvalRejectedByDistance = 0;
            _teleportEvalLastRejectedName = string.Empty;
            _teleportEvalLastRejectedDotUp = 0f;
            _teleportEvalAllLayerSegmentsWithAnyHit = 0;
            _teleportEvalAllLayerHitsTotal = 0;
            _teleportEvalAllLayerFirstHitName = string.Empty;
            _teleportEvalAllLayerFirstHitLayer = -1;
            _lastFallbackRejectSummary = string.Empty;
            _lastColliderInventorySceneName = string.Empty;
        }

        public void SetFixedTeleportPlaneY(float y)
        {
            _hasFixedTeleportPlaneY = true;
            _fixedTeleportPlaneY = y;
            VRModCore.Log($"[Locomotion][OpenXR] Teleport fallback default Y set to initial Y={_fixedTeleportPlaneY:F2}");
        }

        public void SetGroundProbeReferenceWorldPosition(Vector3 worldPos)
        {
            _hasGroundProbeReferenceWorldPos = true;
            _groundProbeReferenceWorldPos = worldPos;
            _nextGroundProbeTime = 0f;
            VRModCore.Log($"[Locomotion][OpenXR] Ground probe reference set to ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2})");
        }

        public void RefreshTeleportColliderCache(GameObject vrRig)
        {
            _teleportColliderCandidates.Clear();
            _teleportColliderCacheReady = false;
            _lastTeleportColliderSourceName = string.Empty;
            _teleportColliderCacheSceneName = SceneManager.GetActiveScene().name ?? string.Empty;

            if (EnsurePhysicsRaycastBinding())
            {
                VRModCore.Log($"[Locomotion][OpenXR] Teleport uses Physics.Raycast in scene '{_teleportColliderCacheSceneName}' (cache scan skipped).");
                return;
            }

            if (vrRig == null)
            {
                VRModCore.LogWarning("[Locomotion][OpenXR] Teleport collider cache skipped: vrRig is null.");
                return;
            }

            GameObject[] objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            HashSet<int> seenColliderIds = [];

            foreach (GameObject go in objects)
            {
                if (go == null || !go.activeInHierarchy) continue;
                if (go.transform.IsChildOf(vrRig.transform)) continue;

                object collider = go.GetComponent("Collider");
                if (collider == null) continue;

                if (!(collider is UnityEngine.Object unityColliderObj) || unityColliderObj == null) continue;
                int colliderId = unityColliderObj.GetInstanceID();
                if (!seenColliderIds.Add(colliderId)) continue;

                _teleportColliderCandidates.Add(new TeleportColliderCandidate
                {
                    Collider = collider,
                    Name = go.name ?? string.Empty,
                    HierarchyPath = BuildHierarchyPath(go.transform)
                });

                // Collider exists: prefer collider raycast path for this object.
                continue;
            }

            _teleportColliderCandidates.Sort(static (a, b) => string.Compare(a.HierarchyPath, b.HierarchyPath, StringComparison.Ordinal));
            _teleportColliderCacheReady = true;

            VRModCore.Log($"[Locomotion][OpenXR] Teleport collider cache built for scene '{_teleportColliderCacheSceneName}': colliders={_teleportColliderCandidates.Count} (All active colliders except rig).");
            if (_teleportColliderCandidates.Count == 0)
            {
                VRModCore.LogWarning("[Locomotion][OpenXR] Teleport candidate cache is empty; teleport may fallback to fixed plane.");
                return;
            }

            for (int i = 0; i < _teleportColliderCandidates.Count; i++)
            {
                TeleportColliderCandidate candidate = _teleportColliderCandidates[i];
                VRModCore.Log($"[Locomotion][OpenXR] Teleport collider[{i}] name='{candidate.Name}' path='{candidate.HierarchyPath}'");
            }
        }

        private void UpdateTurn(GameObject vrRig, float rightStickX, bool isSmoothTurnHeld, bool hasHmdWorldPose, Vector3 hmdWorldPos)
        {
            float absX = Mathf.Abs(rightStickX);
            if (absX <= SnapTurnReleaseThreshold)
            {
                _snapTurnLatched = false;
            }

            if (!isSmoothTurnHeld && absX >= SnapTurnThreshold && !_snapTurnLatched && Time.time >= _nextSnapTurnTime)
            {
                float turnAngle = rightStickX > 0f ? GetSnapTurnDegrees() : -GetSnapTurnDegrees();
                RotateRig(vrRig, hasHmdWorldPose ? hmdWorldPos : vrRig.transform.position, turnAngle);
                _snapTurnLatched = true;
                _nextSnapTurnTime = Time.time + SnapTurnCooldownSeconds;
                VRModCore.Log($"[Locomotion][OpenXR] SnapTurn {turnAngle:F0} deg");
                return;
            }

            if (isSmoothTurnHeld && absX >= SmoothTurnDeadzone)
            {
                float smoothTurnDelta = Mathf.Sign(rightStickX) * GetSmoothTurnDegreesPerSecond() * Time.deltaTime;
                if (Mathf.Abs(smoothTurnDelta) > 0.001f)
                {
                    RotateRig(vrRig, hasHmdWorldPose ? hmdWorldPos : vrRig.transform.position, smoothTurnDelta);
                }
            }
        }

        private void UpdateSmoothMove(GameObject vrRig, float moveX, float moveY, Vector3 cameraForward)
        {
            const float deadzone = 0.15f;
            float magnitude = Mathf.Sqrt(moveX * moveX + moveY * moveY);
            if (magnitude < deadzone) return;

            float speed = magnitude * GetSmoothMoveSpeed() * Time.deltaTime;
            Vector3 flatForward = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;
            Vector3 move = (flatForward * moveY + flatRight * moveX).normalized * speed;
            vrRig.transform.position += move;
        }

        private void UpdateGripDragTranslate(GameObject vrRig, bool gripHeld, bool hasGripLocalPose, Vector3 currentGripTrackingLocalPos)
        {
            if (gripHeld)
            {
                _lastGripPressedTime = Time.time;
            }

            bool treatAsHeld = gripHeld || (_isGripDragging && (Time.time - _lastGripPressedTime) <= GripReleaseGraceSeconds);
            if (!treatAsHeld)
            {
                _isGripDragging = false;
                _smoothedGripDelta = Vector3.zero;
                return;
            }

            if (!hasGripLocalPose)
            {
                _isGripDragging = false;
                _smoothedGripDelta = Vector3.zero;
                return;
            }

            if (!_isGripDragging)
            {
                _isGripDragging = true;
                _lastGripControllerTrackingLocalPos = currentGripTrackingLocalPos;
                _smoothedGripDelta = Vector3.zero;
                return;
            }

            Vector3 rawControllerDeltaLocal = currentGripTrackingLocalPos - _lastGripControllerTrackingLocalPos;
            _lastGripControllerTrackingLocalPos = currentGripTrackingLocalPos;

            if (GripDragLockY)
            {
                rawControllerDeltaLocal.y = 0f;
            }

            if (GripDragInvertDirection)
            {
                rawControllerDeltaLocal = -rawControllerDeltaLocal;
            }

            float rawDeltaMagnitude = rawControllerDeltaLocal.magnitude;
            if (rawDeltaMagnitude < GripDragDeadzoneMeters)
            {
                return;
            }

            if (rawDeltaMagnitude > GripDragMaxStepMeters)
            {
                rawControllerDeltaLocal = rawControllerDeltaLocal / rawDeltaMagnitude * GripDragMaxStepMeters;
            }

            Vector3 smoothedControllerDeltaLocal = Vector3.Lerp(_smoothedGripDelta, rawControllerDeltaLocal, GripDeltaSmoothing);
            _smoothedGripDelta = smoothedControllerDeltaLocal;

            float sensitivity = GetGripDragSensitivity();
            if (sensitivity <= 0f)
            {
                return;
            }

            Vector3 controllerDeltaWorld = vrRig.transform.TransformVector(smoothedControllerDeltaLocal);
            vrRig.transform.position += controllerDeltaWorld * sensitivity;
        }

        private static void RotateRig(GameObject vrRig, Vector3 pivotWorld, float turnAngle)
        {
            vrRig.transform.RotateAround(pivotWorld, Vector3.up, turnAngle);
        }

        private static float GetSnapTurnDegrees()
        {
#if OPENXR_BUILD
            if (ConfigManager.OpenXR_SnapTurnDegrees != null)
            {
                return Mathf.Clamp(ConfigManager.OpenXR_SnapTurnDegrees.Value, 1f, 180f);
            }
#endif
            return DefaultSnapTurnDegrees;
        }

        private static float GetSmoothMoveSpeed()
        {
#if OPENXR_BUILD
            if (ConfigManager.OpenXR_SmoothMoveSpeed != null)
            {
                return Mathf.Clamp(ConfigManager.OpenXR_SmoothMoveSpeed.Value, 0.1f, 20f);
            }
#endif
            return SmoothMoveSpeedMetersPerSecond;
        }

        private static float GetSmoothTurnDegreesPerSecond()
        {
#if OPENXR_BUILD
            if (ConfigManager.OpenXR_SmoothTurnDegreesPerSecond != null)
            {
                return Mathf.Clamp(ConfigManager.OpenXR_SmoothTurnDegreesPerSecond.Value, 0f, 360f);
            }
#endif
            return DefaultSmoothTurnDegreesPerSecond;
        }

        private static float GetGripDragSensitivity()
        {
#if OPENXR_BUILD
            if (ConfigManager.OpenXR_GripDragSensitivity != null)
            {
                return Mathf.Clamp(ConfigManager.OpenXR_GripDragSensitivity.Value, 0.00f, 3.00f);
            }
#endif
            return DefaultGripDragSensitivity;
        }

        private bool TryGetTeleportTarget(GameObject vrRig, Vector3 originWorld, Vector3 directionWorld, out Vector3 targetWorld)
        {
            targetWorld = default;
            _teleportArcPointCount = 0;
            _lastTeleportTargetSource = TeleportTargetSource.Unknown;
            _teleportEvalSegmentCount = 0;
            _teleportEvalSegmentsWithAnyHit = 0;
            _teleportEvalHitsTotal = 0;
            _teleportEvalRejectedByNormal = 0;
            _teleportEvalRejectedByDistance = 0;
            _teleportEvalLastRejectedName = string.Empty;
            _teleportEvalLastRejectedDotUp = 0f;
            _teleportEvalAllLayerSegmentsWithAnyHit = 0;
            _teleportEvalAllLayerHitsTotal = 0;
            _teleportEvalAllLayerFirstHitName = string.Empty;
            _teleportEvalAllLayerFirstHitLayer = -1;
            _lastFallbackRejectSummary = string.Empty;

            Vector3 direction = directionWorld.normalized;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            _teleportArcPoints[0] = originWorld;
            _teleportArcPointCount = 1;

            bool hasGroundPlane = TryGetGroundPlaneY(vrRig, out float groundPlaneY);
            Vector3 launchVelocity = direction * TeleportArcInitialSpeedMetersPerSecond;
            Vector3 previousPoint = originWorld;
            float maxDistanceSqr = TeleportMaxDistanceMeters * TeleportMaxDistanceMeters;
            bool hasFallbackCandidate = false;
            Vector3 fallbackCandidatePoint = default;
            int fallbackCandidateIndex = -1;

            for (int i = 1; i <= TeleportArcSegments; i++)
            {
                float time = i * TeleportArcTimeStepSeconds;
                Vector3 nextPoint = originWorld
                    + (launchVelocity * time)
                    + (TeleportArcGravity * (0.5f * time * time));

                Vector3 fromOrigin = nextPoint - originWorld;
                if (fromOrigin.sqrMagnitude > maxDistanceSqr && fromOrigin.sqrMagnitude > 0.000001f)
                {
                    nextPoint = originWorld + (fromOrigin.normalized * TeleportMaxDistanceMeters);
                }

                if (_teleportArcPointCount < _teleportArcPoints.Length)
                {
                    _teleportArcPoints[_teleportArcPointCount] = nextPoint;
                    _teleportArcPointCount++;
                }

                Vector3 segment = nextPoint - previousPoint;
                float segmentLength = segment.magnitude;
                if (segmentLength > 0.0001f)
                {
                    _teleportEvalSegmentCount++;
                    if (TryGetTeleportTargetFromSceneCollider(vrRig, previousPoint, segment / segmentLength, segmentLength, out Vector3 sceneHit))
                    {
                        targetWorld = sceneHit;
                        _teleportArcPoints[_teleportArcPointCount - 1] = sceneHit;
                        _lastTeleportTargetSource = TeleportTargetSource.SceneHit;
                        _lastFallbackRejectSummary = string.Empty;
                        return true;
                    }

                    if (!hasFallbackCandidate && hasGroundPlane && TryGetSegmentGroundPlaneHit(previousPoint, nextPoint, groundPlaneY, out Vector3 planeHit))
                    {
                        hasFallbackCandidate = true;
                        fallbackCandidatePoint = planeHit;
                        fallbackCandidateIndex = _teleportArcPointCount - 1;
                    }
                }

                previousPoint = nextPoint;
                if ((nextPoint - originWorld).sqrMagnitude >= maxDistanceSqr)
                {
                    break;
                }
            }

            if (hasFallbackCandidate)
            {
                targetWorld = fallbackCandidatePoint;
                if (fallbackCandidateIndex >= 0 && fallbackCandidateIndex < _teleportArcPointCount)
                {
                    _teleportArcPoints[fallbackCandidateIndex] = fallbackCandidatePoint;
                }
                _lastTeleportTargetSource = TeleportTargetSource.FallbackPlane;
                _lastFallbackRejectSummary = BuildFallbackRejectSummary();
                return true;
            }

            _lastFallbackRejectSummary = BuildFallbackRejectSummary();
            return false;
        }

        private bool TryGetTeleportTargetFromSceneCollider(GameObject vrRig, Vector3 originWorld, Vector3 directionWorld, float maxDistance, out Vector3 targetWorld)
        {
            targetWorld = default;
            if (TryGetTeleportTargetFromPhysicsRaycast(originWorld, directionWorld, maxDistance, out Vector3 physicsPoint, out string physicsSourceName, out bool usedRaycastAll))
            {
                targetWorld = physicsPoint;
                if (!string.Equals(_lastTeleportColliderSourceName, physicsSourceName, StringComparison.Ordinal))
                {
                    _lastTeleportColliderSourceName = physicsSourceName;
                    string mode = usedRaycastAll ? "PhysicsRaycastAll" : "PhysicsRaycast";
                    VRModCore.Log($"[Locomotion][OpenXR] Teleport source='{physicsSourceName}' mode={mode} hitY={physicsPoint.y:F2}");
                }
                return true;
            }

            if (!_teleportColliderCacheReady) return false;

            if (TryGetTeleportTargetFromCachedColliders(originWorld, directionWorld, maxDistance, out Vector3 colliderPoint, out string colliderName))
            {
                targetWorld = colliderPoint;
                if (!string.Equals(_lastTeleportColliderSourceName, colliderName, StringComparison.Ordinal))
                {
                    _lastTeleportColliderSourceName = colliderName;
                    VRModCore.Log($"[Locomotion][OpenXR] Teleport source='{colliderName}' mode=ColliderAll hitY={colliderPoint.y:F2}");
                }
                return true;
            }

            return false;
        }

        private bool TryGetTeleportTargetFromCachedColliders(Vector3 originWorld, Vector3 directionWorld, float maxDistance, out Vector3 targetWorld, out string sourceName)
        {
            targetWorld = default;
            sourceName = string.Empty;
            if (_teleportColliderCandidates.Count == 0) return false;

            bool hasCandidate = false;
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = default;
            string bestName = string.Empty;

            for (int i = 0; i < _teleportColliderCandidates.Count; i++)
            {
                TeleportColliderCandidate candidate = _teleportColliderCandidates[i];
                object collider = candidate.Collider;
                if (!(collider is UnityEngine.Object unityColliderObj) || unityColliderObj == null) continue;

                if (!TryRaycastCollider(collider, originWorld, directionWorld, maxDistance, out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal))
                {
                    continue;
                }

                if (Vector3.Dot(hitNormal, Vector3.up) < TeleportColliderSurfaceMinUpDot)
                {
                    continue;
                }

                if (!hasCandidate || hitDistance < bestDistance)
                {
                    hasCandidate = true;
                    bestDistance = hitDistance;
                    bestPoint = hitPoint;
                    bestName = candidate.Name;
                }
            }

            if (!hasCandidate) return false;
            targetWorld = bestPoint;
            sourceName = bestName;
            return true;
        }

        private bool TryGetTeleportTargetFromPhysicsRaycast(Vector3 originWorld, Vector3 directionWorld, float maxDistance, out Vector3 targetWorld, out string sourceName, out bool usedRaycastAll)
        {
            targetWorld = default;
            sourceName = string.Empty;
            usedRaycastAll = false;
            if (!EnsurePhysicsRaycastBinding()) return false;

            int layerMask = GetTeleportPhysicsLayerMask();
            Ray ray = new Ray(originWorld, directionWorld);
            if (PhysicsRaycastAllMethod != null)
            {
                object[] allArgs = BuildPhysicsRaycastAllArgs(ray, maxDistance, layerMask);
                bool hasMaskedSegmentHit = false;
                if (allArgs != null)
                {
                    object hitsObj = PhysicsRaycastAllMethod.Invoke(null, allArgs);
                    if (hitsObj is Array hits && hits.Length > 0)
                    {
                        hasMaskedSegmentHit = true;
                        _teleportEvalSegmentsWithAnyHit++;
                        _teleportEvalHitsTotal += hits.Length;
                        bool hasCandidate = false;
                        float bestDistance = float.MaxValue;
                        Vector3 bestPoint = default;
                        string bestSource = string.Empty;

                        foreach (object hit in hits)
                        {
                            if (hit == null) continue;
                            if (!TryReadRaycastHit(hit, out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal, out object colliderObj))
                            {
                                continue;
                            }

                            if (hitDistance <= 0f || hitDistance > maxDistance)
                            {
                                _teleportEvalRejectedByDistance++;
                                continue;
                            }

                            float dotUp = Vector3.Dot(hitNormal, Vector3.up);
                            if (dotUp < TeleportColliderSurfaceMinUpDot)
                            {
                                _teleportEvalRejectedByNormal++;
                                _teleportEvalLastRejectedName = ResolveColliderName(colliderObj);
                                _teleportEvalLastRejectedDotUp = dotUp;
                                continue;
                            }

                            if (!hasCandidate || hitDistance < bestDistance)
                            {
                                hasCandidate = true;
                                bestDistance = hitDistance;
                                bestPoint = hitPoint;
                                bestSource = ResolveColliderName(colliderObj);
                            }
                        }

                        if (hasCandidate)
                        {
                            targetWorld = bestPoint;
                            sourceName = bestSource;
                            usedRaycastAll = true;
                            return true;
                        }
                    }
                }

                if (!hasMaskedSegmentHit)
                {
                    ProbeAllLayersForDiagnostics(ray, maxDistance, layerMask);
                }
            }

            if (PhysicsRaycastMethod == null || PhysicsRaycastHitType == null) return false;

            object boxedHit = Activator.CreateInstance(PhysicsRaycastHitType);
            object[] args = BuildPhysicsRaycastArgs(ray, boxedHit, maxDistance, layerMask);
            if (args == null) return false;

            object result = PhysicsRaycastMethod.Invoke(null, args);
            if (!(result is bool didHit) || !didHit) return false;
            _teleportEvalSegmentsWithAnyHit++;
            _teleportEvalHitsTotal++;

            object hitStruct = args[1];
            if (hitStruct == null) return false;
            if (!TryReadRaycastHit(hitStruct, out float fallbackDistance, out Vector3 fallbackPoint, out Vector3 fallbackNormal, out object fallbackColliderObj))
            {
                return false;
            }

            if (fallbackDistance <= 0f || fallbackDistance > maxDistance)
            {
                _teleportEvalRejectedByDistance++;
                return false;
            }

            float fallbackDotUp = Vector3.Dot(fallbackNormal, Vector3.up);
            if (fallbackDotUp < TeleportColliderSurfaceMinUpDot)
            {
                _teleportEvalRejectedByNormal++;
                _teleportEvalLastRejectedName = ResolveColliderName(fallbackColliderObj);
                _teleportEvalLastRejectedDotUp = fallbackDotUp;
                return false;
            }

            targetWorld = fallbackPoint;
            sourceName = ResolveColliderName(fallbackColliderObj);
            return true;
        }

        private static bool EnsurePhysicsRaycastBinding()
        {
            if (PhysicsRaycastResolved) return true;

            Type physicsType = ResolveTypeAnyAssembly("UnityEngine.Physics");
            if (physicsType == null)
            {
                return false;
            }

            Type raycastHitType = ResolveTypeAnyAssembly("UnityEngine.RaycastHit");
            if (raycastHitType == null)
            {
                return false;
            }

            Type queryTriggerInteractionType = ResolveTypeAnyAssembly("UnityEngine.QueryTriggerInteraction");
            Type raycastHitByRef = raycastHitType.MakeByRefType();

            MethodInfo raycastMethod = null;
            if (queryTriggerInteractionType != null)
            {
                raycastMethod = physicsType.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), raycastHitByRef, typeof(float), typeof(int), queryTriggerInteractionType ], null);
            }

            raycastMethod ??= physicsType.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), raycastHitByRef, typeof(float), typeof(int) ], null);
            raycastMethod ??= physicsType.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), raycastHitByRef, typeof(float) ], null);
            raycastMethod ??= physicsType.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), raycastHitByRef ], null);

            MethodInfo raycastAllMethod = null;
            if (queryTriggerInteractionType != null)
            {
                raycastAllMethod = physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float), typeof(int), queryTriggerInteractionType ], null);
            }
            raycastAllMethod ??= physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float), typeof(int) ], null);
            raycastAllMethod ??= physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray), typeof(float) ], null);
            raycastAllMethod ??= physicsType.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, [ typeof(Ray) ], null);

            if (raycastMethod == null && raycastAllMethod == null)
            {
                return false;
            }

            PhysicsRaycastMethod = raycastMethod;
            PhysicsRaycastAllMethod = raycastAllMethod;
            PhysicsRaycastHitType = raycastHitType;
            PhysicsQueryTriggerInteractionType = queryTriggerInteractionType;
            PhysicsRaycastParameterCount = raycastMethod != null ? raycastMethod.GetParameters().Length : 0;
            PhysicsRaycastAllParameterCount = raycastAllMethod != null ? raycastAllMethod.GetParameters().Length : 0;
            PhysicsRaycastResolved = true;
            return true;
        }

        private static object[] BuildPhysicsRaycastArgs(Ray ray, object boxedHit, float maxDistance, int layerMask)
        {
            return PhysicsRaycastParameterCount switch
            {
                2 => [ ray, boxedHit ],
                3 => [ ray, boxedHit, maxDistance ],
                4 => [ ray, boxedHit, maxDistance, layerMask ],
                5 when PhysicsQueryTriggerInteractionType != null => [ ray, boxedHit, maxDistance, layerMask, Enum.ToObject(PhysicsQueryTriggerInteractionType, 0) ],
                _ => null
            };
        }

        private static object[] BuildPhysicsRaycastAllArgs(Ray ray, float maxDistance, int layerMask)
        {
            return PhysicsRaycastAllParameterCount switch
            {
                1 => [ ray ],
                2 => [ ray, maxDistance ],
                3 => [ ray, maxDistance, layerMask ],
                4 when PhysicsQueryTriggerInteractionType != null => [ ray, maxDistance, layerMask, Enum.ToObject(PhysicsQueryTriggerInteractionType, 0) ],
                _ => null
            };
        }

        private static int GetTeleportPhysicsLayerMask()
        {
            int mask = 0;
            int bgLayer = LayerMask.NameToLayer(GroundLayerName);
            if (bgLayer >= 0)
            {
                mask |= 1 << bgLayer;
            }

            int bg2Layer = LayerMask.NameToLayer("BG2");
            if (bg2Layer >= 0)
            {
                mask |= 1 << bg2Layer;
            }

            return mask != 0 ? mask : ~0;
        }

        private void LogTeleportColliderInventoryIfNeeded()
        {
            string sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            if (string.Equals(_lastColliderInventorySceneName, sceneName, StringComparison.Ordinal))
            {
                return;
            }

            _lastColliderInventorySceneName = sceneName;
            int bgLayer = LayerMask.NameToLayer(GroundLayerName);
            int bg2Layer = LayerMask.NameToLayer("BG2");
            int count = 0;

            foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go == null || !go.activeInHierarchy) continue;
                int layer = go.layer;
                if (layer != bgLayer && layer != bg2Layer) continue;

                Component collider = go.GetComponent("Collider");
                if (collider == null) continue;

                bool colliderEnabled = true;
                bool isTrigger = false;
                PropertyInfo enabledProperty = collider.GetType().GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
                if (enabledProperty != null && enabledProperty.PropertyType == typeof(bool))
                {
                    object rawEnabled = enabledProperty.GetValue(collider, null);
                    if (rawEnabled is bool enabledValue)
                    {
                        colliderEnabled = enabledValue;
                    }
                }

                PropertyInfo triggerProperty = collider.GetType().GetProperty("isTrigger", BindingFlags.Public | BindingFlags.Instance);
                if (triggerProperty != null && triggerProperty.PropertyType == typeof(bool))
                {
                    object rawTrigger = triggerProperty.GetValue(collider, null);
                    if (rawTrigger is bool triggerValue)
                    {
                        isTrigger = triggerValue;
                    }
                }

                count++;
                string path = BuildHierarchyPath(go.transform);
                VRModCore.Log($"[Locomotion][OpenXR] Teleport collider inventory scene='{sceneName}' item={count} name='{go.name}' path='{path}' type='{collider.GetType().Name}' layer={layer} enabled={colliderEnabled} trigger={isTrigger}");
            }

            VRModCore.Log($"[Locomotion][OpenXR] Teleport collider inventory scene='{sceneName}' total={count} (layers: BG={bgLayer}, BG2={bg2Layer})");
        }

        private void ProbeAllLayersForDiagnostics(Ray ray, float maxDistance, int constrainedLayerMask)
        {
            if (PhysicsRaycastAllMethod == null) return;
            if (constrainedLayerMask == ~0) return;

            object[] args = BuildPhysicsRaycastAllArgs(ray, maxDistance, ~0);
            if (args == null) return;

            object hitsObj = PhysicsRaycastAllMethod.Invoke(null, args);
            if (hitsObj is not Array hits || hits.Length == 0) return;

            _teleportEvalAllLayerSegmentsWithAnyHit++;
            _teleportEvalAllLayerHitsTotal += hits.Length;

            if (!string.IsNullOrWhiteSpace(_teleportEvalAllLayerFirstHitName)) return;

            float bestDistance = float.MaxValue;
            string bestName = string.Empty;
            int bestLayer = -1;
            foreach (object hit in hits)
            {
                if (hit == null) continue;
                if (!TryReadRaycastHit(hit, out float distance, out _, out _, out object colliderObj))
                {
                    continue;
                }

                if (distance <= 0f || distance > maxDistance) continue;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestName = ResolveColliderName(colliderObj);
                bestLayer = ResolveColliderLayer(colliderObj);
            }

            if (bestDistance < float.MaxValue)
            {
                _teleportEvalAllLayerFirstHitName = bestName;
                _teleportEvalAllLayerFirstHitLayer = bestLayer;
            }
        }

        private string BuildFallbackRejectSummary()
        {
            string lastRejected = string.IsNullOrWhiteSpace(_teleportEvalLastRejectedName)
                ? "none"
                : $"{_teleportEvalLastRejectedName}(dotUp={_teleportEvalLastRejectedDotUp:F2})";
            string firstAllLayerHit = string.IsNullOrWhiteSpace(_teleportEvalAllLayerFirstHitName)
                ? "none"
                : $"{_teleportEvalAllLayerFirstHitName}(layer={_teleportEvalAllLayerFirstHitLayer})";
            return $"seg={_teleportEvalSegmentCount}, segWithHit={_teleportEvalSegmentsWithAnyHit}, hits={_teleportEvalHitsTotal}, rejectNormal={_teleportEvalRejectedByNormal}, rejectDistance={_teleportEvalRejectedByDistance}, allLayerSegWithHit={_teleportEvalAllLayerSegmentsWithAnyHit}, allLayerHits={_teleportEvalAllLayerHitsTotal}, allLayerFirstHit={firstAllLayerHit}, lastReject={lastRejected}";
        }

        private static bool TryGetSegmentGroundPlaneHit(Vector3 start, Vector3 end, float groundY, out Vector3 hitPoint)
        {
            hitPoint = default;
            float startDelta = start.y - groundY;
            float endDelta = end.y - groundY;
            if (startDelta * endDelta > 0f) return false;

            Vector3 segment = end - start;
            if (Mathf.Abs(segment.y) <= 0.00001f) return false;

            float t = (groundY - start.y) / segment.y;
            if (t < 0f || t > 1f) return false;

            hitPoint = start + (segment * t);
            hitPoint.y = groundY;
            return true;
        }

        private static string ResolveColliderName(object colliderObj)
        {
            if (colliderObj is Component colliderComponent && colliderComponent != null && colliderComponent.gameObject != null)
            {
                return colliderComponent.gameObject.name ?? string.Empty;
            }

            return "unknown";
        }

        private static int ResolveColliderLayer(object colliderObj)
        {
            if (colliderObj is Component colliderComponent && colliderComponent != null && colliderComponent.gameObject != null)
            {
                return colliderComponent.gameObject.layer;
            }

            return -1;
        }

        private static Type ResolveTypeAnyAssembly(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            Type type = Type.GetType(fullName, false);
            if (type != null) return type;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                type = asm.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            List<string> parts = [];
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name ?? string.Empty);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool TryRaycastCollider(
            object collider,
            Vector3 originWorld,
            Vector3 directionWorld,
            float maxDistance,
            out float hitDistance,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            hitDistance = 0f;
            hitPoint = default;
            hitNormal = Vector3.up;
            if (collider == null) return false;

            Type colliderType = collider.GetType();
            if (!TryGetColliderRaycastMethod(colliderType, out MethodInfo raycastMethod, out Type raycastHitType))
            {
                return false;
            }

            object boxedHit = Activator.CreateInstance(raycastHitType);
            object[] args = [new Ray(originWorld, directionWorld), boxedHit, maxDistance];
            object result = raycastMethod.Invoke(collider, args);
            if (!(result is bool didHit) || !didHit)
            {
                return false;
            }

            object hitStruct = args[1];
            if (hitStruct == null) return false;
            return TryReadRaycastHit(hitStruct, out hitDistance, out hitPoint, out hitNormal);
        }

        private static bool TryGetColliderRaycastMethod(Type colliderType, out MethodInfo method, out Type raycastHitType)
        {
            method = null;
            raycastHitType = null;
            if (colliderType == null) return false;

            if (ColliderRaycastMethodCache.TryGetValue(colliderType, out MethodInfo cachedMethod) &&
                ColliderRaycastHitTypeCache.TryGetValue(colliderType, out Type cachedHitType))
            {
                method = cachedMethod;
                raycastHitType = cachedHitType;
                return method != null && raycastHitType != null;
            }

            foreach (MethodInfo candidate in colliderType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(candidate.Name, "Raycast", StringComparison.Ordinal)) continue;
                if (candidate.ReturnType != typeof(bool)) continue;

                ParameterInfo[] ps = candidate.GetParameters();
                if (ps.Length != 3) continue;
                if (ps[0].ParameterType != typeof(Ray)) continue;
                if (!ps[1].ParameterType.IsByRef) continue;
                if (ps[2].ParameterType != typeof(float)) continue;

                Type hitType = ps[1].ParameterType.GetElementType();
                if (hitType == null) continue;

                method = candidate;
                raycastHitType = hitType;
                break;
            }

            ColliderRaycastMethodCache[colliderType] = method;
            ColliderRaycastHitTypeCache[colliderType] = raycastHitType;
            return method != null && raycastHitType != null;
        }

        private static bool TryReadRaycastHit(object hitStruct, out float distance, out Vector3 point, out Vector3 normal)
        {
            return TryReadRaycastHit(hitStruct, out distance, out point, out normal, out _);
        }

        private static bool TryReadRaycastHit(object hitStruct, out float distance, out Vector3 point, out Vector3 normal, out object colliderObj)
        {
            distance = 0f;
            point = default;
            normal = Vector3.up;
            colliderObj = null;
            if (hitStruct == null) return false;

            Type hitType = hitStruct.GetType();
            if (!RaycastHitAccessorCache.TryGetValue(hitType, out RaycastHitAccessor accessor))
            {
                accessor = new RaycastHitAccessor
                {
                    DistanceProperty = hitType.GetProperty("distance", BindingFlags.Instance | BindingFlags.Public),
                    PointProperty = hitType.GetProperty("point", BindingFlags.Instance | BindingFlags.Public),
                    NormalProperty = hitType.GetProperty("normal", BindingFlags.Instance | BindingFlags.Public),
                    ColliderProperty = hitType.GetProperty("collider", BindingFlags.Instance | BindingFlags.Public)
                };
                RaycastHitAccessorCache[hitType] = accessor;
            }

            if (accessor.DistanceProperty == null || accessor.PointProperty == null || accessor.NormalProperty == null)
            {
                return false;
            }

            object distanceObj = accessor.DistanceProperty.GetValue(hitStruct, null);
            object pointObj = accessor.PointProperty.GetValue(hitStruct, null);
            object normalObj = accessor.NormalProperty.GetValue(hitStruct, null);
            colliderObj = accessor.ColliderProperty?.GetValue(hitStruct, null);
            if (!(distanceObj is float distanceValue) || !(pointObj is Vector3 pointValue) || !(normalObj is Vector3 normalValue))
            {
                return false;
            }

            distance = distanceValue;
            point = pointValue;
            normal = normalValue;
            return true;
        }

        private bool TryGetGroundPlaneY(GameObject vrRig, out float groundPlaneY)
        {
            groundPlaneY = 0f;
            Vector3 referenceWorld = _hasGroundProbeReferenceWorldPos
                ? _groundProbeReferenceWorldPos
                : (vrRig != null ? vrRig.transform.position : Vector3.zero);

            if (Time.time >= _nextGroundProbeTime)
            {
                _nextGroundProbeTime = Time.time + GroundPlaneRefreshIntervalSeconds;

                if (TryFindGroundPlaneY(referenceWorld, out float foundY, out string foundName))
                {
                    bool changed = !_hasGroundPlaneY ||
                                   Mathf.Abs(foundY - _groundPlaneY) > 0.01f ||
                                   !string.Equals(foundName, _groundObjectName, StringComparison.Ordinal);

                    _groundPlaneY = foundY;
                    _groundObjectName = foundName;
                    _hasGroundPlaneY = true;
                    _hasLoggedMissingGround = false;

                    if (changed)
                    {
                        VRModCore.Log($"[Locomotion][OpenXR] Ground plane source='{_groundObjectName}' y={_groundPlaneY:F2}");
                    }
                }
                else if (!_hasGroundPlaneY && !_hasLoggedMissingGround)
                {
                    VRModCore.LogWarning("[Locomotion][OpenXR] No ground object found (Layer=BG/BG2, name exact match: floor/ground/tatami); using fallback default Y when needed.");
                    _hasLoggedMissingGround = true;
                }
            }

            if (_hasGroundPlaneY)
            {
                groundPlaneY = _groundPlaneY;
                return true;
            }

            if (_hasFixedTeleportPlaneY)
            {
                groundPlaneY = _fixedTeleportPlaneY;
                return true;
            }

            return false;
        }

        private static bool TryFindGroundPlaneY(Vector3 referenceWorld, out float groundY, out string groundName)
        {
            groundY = 0f;
            groundName = string.Empty;

            GameObject[] objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            int bgLayer = LayerMask.NameToLayer(GroundLayerName);
            int bg2Layer = LayerMask.NameToLayer("BG2");
            if (bgLayer < 0 && bg2Layer < 0) return false;

            bool hasCandidate = false;
            float bestHorizontalDistance = float.MaxValue;
            float bestVerticalDistance = float.MaxValue;
            float bestY = 0f;
            string bestName = string.Empty;

            foreach (GameObject go in objects)
            {
                if (go == null || !go.activeInHierarchy) continue;
                int layer = go.layer;
                if (layer != bgLayer && layer != bg2Layer) continue;
                if (!IsGroundNameCandidate(go.name)) continue;

                float candidateY;
                Vector3 candidateCenter = go.transform.position;
                if (TryGetColliderBounds(go, out Bounds bounds))
                {
                    candidateY = bounds.max.y;
                    candidateCenter = bounds.center;
                }
                else
                {
                    candidateY = go.transform.position.y;
                }

                Vector2 candidateCenterXZ = new(candidateCenter.x, candidateCenter.z);
                Vector2 referenceXZ = new(referenceWorld.x, referenceWorld.z);
                float horizontalDistance = Vector2.Distance(candidateCenterXZ, referenceXZ);
                float verticalDistance = Mathf.Abs(candidateY - referenceWorld.y);
                bool isBetter =
                    !hasCandidate ||
                    horizontalDistance < bestHorizontalDistance - 0.01f ||
                    (Mathf.Abs(horizontalDistance - bestHorizontalDistance) <= 0.01f && verticalDistance < bestVerticalDistance);
                if (isBetter)
                {
                    bestHorizontalDistance = horizontalDistance;
                    bestVerticalDistance = verticalDistance;
                    bestY = candidateY;
                    bestName = go.name;
                    hasCandidate = true;
                }
            }

            if (!hasCandidate) return false;

            groundY = bestY;
            groundName = bestName;
            return true;
        }

        private static bool IsGroundNameCandidate(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return false;

            string trimmedName = objectName.Trim();
            foreach (string candidate in GroundNamesExact)
            {
                if (string.Equals(trimmedName, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetColliderBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;

            object collider = go.GetComponent("Collider");
            if (collider == null) return false;

            PropertyInfo boundsProp = collider.GetType().GetProperty("bounds", BindingFlags.Public | BindingFlags.Instance);
            if (boundsProp == null) return false;

            object boundsObj = boundsProp.GetValue(collider);
            if (boundsObj is not Bounds typedBounds) return false;
            bounds = typedBounds;
            return true;
        }

        private void PerformTeleport(GameObject vrRig, Vector3 targetGroundWorld, bool hasHmdWorldPose, Vector3 hmdWorldPos)
        {
            string sourceLabel = _lastTeleportTargetSource.ToString();
            string rejectSummary = string.IsNullOrWhiteSpace(_lastFallbackRejectSummary) ? string.Empty : $" RejectSummary=[{_lastFallbackRejectSummary}]";
            if (hasHmdWorldPose)
            {
                Vector3 hmdOffsetFromRig = hmdWorldPos - vrRig.transform.position;
                Vector3 desiredHmdWorld = new(targetGroundWorld.x, hmdWorldPos.y, targetGroundWorld.z);
                vrRig.transform.position = desiredHmdWorld - hmdOffsetFromRig;
                string maybeReject = _lastTeleportTargetSource == TeleportTargetSource.FallbackPlane ? rejectSummary : string.Empty;
                VRModCore.Log($"[Locomotion][OpenXR] Teleport -> Ground({targetGroundWorld.x:F2}, {targetGroundWorld.y:F2}, {targetGroundWorld.z:F2}) KeepEyeY={desiredHmdWorld.y:F2} Source={sourceLabel}{maybeReject}");
                return;
            }

            Vector3 fallbackRigPos = vrRig.transform.position;
            vrRig.transform.position = new Vector3(targetGroundWorld.x, fallbackRigPos.y, targetGroundWorld.z);
            string maybeFallbackReject = _lastTeleportTargetSource == TeleportTargetSource.FallbackPlane ? rejectSummary : string.Empty;
            VRModCore.LogWarning($"[Locomotion][OpenXR] Teleport fallback: HMD pose unavailable, keeping rig Y. Source={sourceLabel}{maybeFallbackReject}");
        }

        private void UpdateTeleportMarker(GameObject vrRig, bool hasTeleportTarget, Vector3 targetWorld)
        {
            EnsureTeleportMarker(vrRig);
            if (_teleportMarkerRoot == null || _teleportMarkerLine == null || _teleportMarkerTopModel == null || _teleportArcLineRenderer == null || _teleportHitRingRenderer == null)
            {
                return;
            }

            _teleportMarkerRoot.SetActive(true);

            if (_teleportLineMaterial != null)
            {
                _teleportLineMaterial.color = hasTeleportTarget ? TeleportValidColor : TeleportInvalidColor;
            }

            if (_teleportArcPointCount <= 0)
            {
                _teleportArcPoints[0] = vrRig.transform.position + Vector3.up * 0.02f;
                _teleportArcPointCount = 1;
            }

            if (!hasTeleportTarget)
            {
                targetWorld = _teleportArcPoints[_teleportArcPointCount - 1];
            }

            _teleportArcLineRenderer.positionCount = _teleportArcPointCount;
            for (int i = 0; i < _teleportArcPointCount; i++)
            {
                _teleportArcLineRenderer.SetPosition(i, _teleportArcPoints[i]);
            }

            _teleportMarkerRoot.transform.position = targetWorld;
            _teleportMarkerRoot.transform.rotation = Quaternion.identity;

            _teleportMarkerTopModel.transform.position = targetWorld + (Vector3.up * TeleportHitRingYOffsetMeters);
            _teleportMarkerTopModel.transform.rotation = Quaternion.identity;
        }

        private void EnsureTeleportMarker(GameObject vrRig)
        {
            if (_teleportMarkerRoot != null) return;

            _teleportMarkerRoot = new GameObject("OpenXR_TeleportTargetMarker");
            _teleportMarkerRoot.transform.SetParent(vrRig.transform, true);
            _teleportMarkerRoot.layer = vrRig.layer;

            _teleportMarkerLine = new GameObject("ArcLine");
            _teleportMarkerLine.transform.SetParent(_teleportMarkerRoot.transform, false);
            _teleportMarkerLine.layer = vrRig.layer;
            _teleportArcLineRenderer = _teleportMarkerLine.AddComponent<LineRenderer>();

            _teleportMarkerTopModel = new GameObject("HitRing");
            _teleportMarkerTopModel.transform.SetParent(_teleportMarkerRoot.transform, true);
            _teleportMarkerTopModel.layer = vrRig.layer;
            _teleportHitRingRenderer = _teleportMarkerTopModel.AddComponent<LineRenderer>();

            Shader markerShader = Shader.Find("Sprites/Default");
            if (markerShader == null) markerShader = Shader.Find("Unlit/Color");
            if (markerShader == null) markerShader = Shader.Find("Legacy Shaders/Diffuse");
            _teleportLineMaterial = new Material(markerShader);
            _teleportLineMaterial.color = TeleportInvalidColor;
            _teleportTopModelMaterial = new Material(markerShader);
            _teleportTopModelMaterial.color = TeleportHitRingColor;

            if (_teleportArcLineRenderer != null)
            {
                _teleportArcLineRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _teleportArcLineRenderer.receiveShadows = false;
                _teleportArcLineRenderer.sharedMaterial = _teleportLineMaterial;
                _teleportArcLineRenderer.useWorldSpace = true;
                _teleportArcLineRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, TeleportLineWidthMeters);
                _teleportArcLineRenderer.numCornerVertices = 2;
                _teleportArcLineRenderer.numCapVertices = 2;
                _teleportArcLineRenderer.positionCount = 0;
            }

            if (_teleportHitRingRenderer != null)
            {
                _teleportHitRingRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _teleportHitRingRenderer.receiveShadows = false;
                _teleportHitRingRenderer.sharedMaterial = _teleportTopModelMaterial;
                _teleportHitRingRenderer.useWorldSpace = false;
                _teleportHitRingRenderer.loop = true;
                _teleportHitRingRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, TeleportHitRingWidthMeters);
                _teleportHitRingRenderer.numCornerVertices = 6;
                _teleportHitRingRenderer.numCapVertices = 6;
                _teleportHitRingRenderer.positionCount = TeleportHitRingSegments;
                for (int i = 0; i < TeleportHitRingSegments; i++)
                {
                    float t = (float)i / TeleportHitRingSegments;
                    float angle = t * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * TeleportHitRingRadiusMeters;
                    float z = Mathf.Sin(angle) * TeleportHitRingRadiusMeters;
                    _teleportHitRingRenderer.SetPosition(i, new Vector3(x, 0f, z));
                }
            }
        }

        private void SetTeleportMarkerVisible(bool visible)
        {
            if (_teleportMarkerRoot != null && _teleportMarkerRoot.activeSelf != visible)
            {
                _teleportMarkerRoot.SetActive(visible);
            }
        }

        private static Mesh CreateConeMesh(float radius, float height, int segments)
        {
            segments = Mathf.Max(3, segments);
            radius = Mathf.Max(0.001f, radius);
            height = Mathf.Max(0.001f, height);

            var vertices = new List<Vector3>(segments + 2);
            var triangles = new List<int>(segments * 6);
            var uvs = new List<Vector2>(segments + 2);

            Vector3 tip = new(0f, 0f, height);
            Vector3 baseCenter = Vector3.zero;
            vertices.Add(tip);
            uvs.Add(new Vector2(0.5f, 1f));

            vertices.Add(baseCenter);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments;
                float angle = t * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, y, 0f));
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (y / (radius * 2f)) + 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int current = 2 + i;
                int next = 2 + ((i + 1) % segments);

                triangles.Add(0);
                triangles.Add(current);
                triangles.Add(next);
            }

            for (int i = 0; i < segments; i++)
            {
                int current = 2 + i;
                int next = 2 + ((i + 1) % segments);

                triangles.Add(1);
                triangles.Add(next);
                triangles.Add(current);
            }

            Mesh coneMesh = new Mesh
            {
                name = "OpenXR_TeleportCone"
            };
            coneMesh.SetVertices(vertices);
            coneMesh.SetTriangles(triangles, 0, true);
            coneMesh.SetUVs(0, uvs);
            coneMesh.RecalculateNormals();
            coneMesh.RecalculateBounds();
            return coneMesh;
        }

        private static Mesh GetOrLoadTeleportTopModelMesh()
        {
            if (_teleportTopModelMesh != null)
            {
                return _teleportTopModelMesh;
            }

            if (_teleportTopModelMeshLoadAttempted)
            {
                return null;
            }

            _teleportTopModelMeshLoadAttempted = true;
            string[] candidatePaths = BuildTeleportTopModelCandidatePaths();
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string path = candidatePaths[i];
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                _teleportTopModelMesh = TryLoadObjMesh(path, "OpenXR_TeleportTopModel");
                if (_teleportTopModelMesh != null)
                {
                    VRModCore.Log($"[Locomotion][OpenXR] Teleport top model loaded: {path}");
                    return _teleportTopModelMesh;
                }
            }

            VRModCore.LogWarning("[Locomotion][OpenXR] Failed to load teleport top model (headset.obj). Falling back to cone.");
            return null;
        }

        private static string[] BuildTeleportTopModelCandidatePaths()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(OpenXrRigLocomotion).Assembly.Location) ?? string.Empty;
            string modelInAssemblyDir = Path.Combine(assemblyDir, TeleportTopModelFileName);
            string modelInAssemblyModelDir = Path.Combine(assemblyDir, TeleportTopModelRelativeFolder, TeleportTopModelFileName);

            return
            [
                modelInAssemblyDir,
                modelInAssemblyModelDir
            ];
        }

        private static Mesh TryLoadObjMesh(string filePath, string meshName)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines == null || lines.Length == 0) return null;

                var positions = new List<Vector3>(4096);
                var texCoords = new List<Vector2>(4096);
                var normals = new List<Vector3>(4096);
                var outVertices = new List<Vector3>(8192);
                var outTexCoords = new List<Vector2>(8192);
                var outNormals = new List<Vector3>(8192);
                var outTriangles = new List<int>(16384);
                var vertexCache = new Dictionary<string, int>(16384, StringComparer.Ordinal);

                bool sawNormals = false;
                NumberFormatInfo numberFormat = CultureInfo.InvariantCulture.NumberFormat;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("v ", StringComparison.Ordinal))
                    {
                        if (TryParseVector3(line, numberFormat, out Vector3 v)) positions.Add(v);
                        continue;
                    }

                    if (line.StartsWith("vt ", StringComparison.Ordinal))
                    {
                        if (TryParseVector2(line, numberFormat, out Vector2 vt)) texCoords.Add(vt);
                        continue;
                    }

                    if (line.StartsWith("vn ", StringComparison.Ordinal))
                    {
                        if (TryParseVector3(line, numberFormat, out Vector3 vn))
                        {
                            normals.Add(vn);
                            sawNormals = true;
                        }
                        continue;
                    }

                    if (!line.StartsWith("f ", StringComparison.Ordinal)) continue;

                    string[] faceElements = line.Substring(2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (faceElements.Length < 3) continue;

                    int a = ResolveFaceVertex(faceElements[0], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);
                    for (int f = 1; f < faceElements.Length - 1; f++)
                    {
                        int b = ResolveFaceVertex(faceElements[f], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);
                        int c = ResolveFaceVertex(faceElements[f + 1], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);
                        if (a < 0 || b < 0 || c < 0) continue;
                        outTriangles.Add(a);
                        outTriangles.Add(b);
                        outTriangles.Add(c);
                    }
                }

                if (outVertices.Count == 0 || outTriangles.Count == 0) return null;

                var mesh = new Mesh
                {
                    name = meshName
                };
                if (outVertices.Count > 65535)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.SetVertices(outVertices);
                mesh.SetTriangles(outTriangles, 0, true);
                if (outTexCoords.Count == outVertices.Count)
                {
                    mesh.SetUVs(0, outTexCoords);
                }

                if (sawNormals && outNormals.Count == outVertices.Count)
                {
                    mesh.SetNormals(outNormals);
                }
                else
                {
                    mesh.RecalculateNormals();
                }

                mesh.RecalculateBounds();
                ApplyAutoScale(mesh, TeleportTopModelTargetLongestAxisMeters);
                return mesh;
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning($"[Locomotion][OpenXR] Failed loading OBJ mesh '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static bool TryParseVector3(string line, NumberFormatInfo numberFormat, out Vector3 result)
        {
            result = Vector3.zero;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, numberFormat, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, numberFormat, out float y)) return false;
            if (!float.TryParse(parts[3], NumberStyles.Float, numberFormat, out float z)) return false;
            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseVector2(string line, NumberFormatInfo numberFormat, out Vector2 result)
        {
            result = Vector2.zero;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, numberFormat, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, numberFormat, out float y)) return false;
            result = new Vector2(x, y);
            return true;
        }

        private static int ResolveFaceVertex(
            string token,
            List<Vector3> positions,
            List<Vector2> texCoords,
            List<Vector3> normals,
            List<Vector3> outVertices,
            List<Vector2> outTexCoords,
            List<Vector3> outNormals,
            Dictionary<string, int> cache)
        {
            if (string.IsNullOrWhiteSpace(token)) return -1;
            if (cache.TryGetValue(token, out int cached)) return cached;

            string[] indices = token.Split('/');
            int pIndex = ParseObjIndex(indices, 0, positions.Count);
            if (pIndex < 0 || pIndex >= positions.Count) return -1;
            int tIndex = ParseObjIndex(indices, 1, texCoords.Count);
            int nIndex = ParseObjIndex(indices, 2, normals.Count);

            int outIndex = outVertices.Count;
            outVertices.Add(positions[pIndex]);
            outTexCoords.Add((tIndex >= 0 && tIndex < texCoords.Count) ? texCoords[tIndex] : Vector2.zero);
            outNormals.Add((nIndex >= 0 && nIndex < normals.Count) ? normals[nIndex] : Vector3.zero);
            cache[token] = outIndex;
            return outIndex;
        }

        private static int ParseObjIndex(string[] elements, int elementIndex, int sourceCount)
        {
            if (elements == null || elementIndex >= elements.Length) return -1;
            string raw = elements[elementIndex];
            if (string.IsNullOrEmpty(raw)) return -1;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return -1;
            if (parsed > 0) return parsed - 1;
            if (parsed < 0) return sourceCount + parsed;
            return -1;
        }

        private static void ApplyAutoScale(Mesh mesh, float targetLongestAxisMeters)
        {
            if (mesh == null || targetLongestAxisMeters <= 0f) return;
            Bounds bounds = mesh.bounds;
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest <= 0.0001f) return;

            float scale = targetLongestAxisMeters / longest;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] *= scale;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
    }
}
