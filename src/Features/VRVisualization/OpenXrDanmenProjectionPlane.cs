#if OPENXR_BUILD
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrDanmenProjectionPlane
    {
        private const string PlaneName = "UnityVRMod_DanmenProjectionPlane";
        private const float SourceRefreshIntervalSeconds = 0.75f;
        private const float PlaneWidthMeters = 0.55f;
        private const float MinPanelScale = 0.35f;
        private const float MaxPanelScale = 3.0f;
        private const float RightHandLeftOffsetMeters = 0.14f;
        private const float RightHandUpOffsetMeters = 0.02f;
        private const float RightHandForwardOffsetMeters = 0.06f;
        private const float EdgeFrameExpandNormalized = 0.08f;
        private const string ResizeHandleBaseName = "OpenXR_DanmenResizeHandle_";
        private const float ResizeHandleRadiusMeters = 0.018f;
        private const float ResizeHandleDepthScale = 0.22f;
        private const float RayMaxDistanceMeters = 8.0f;
        private const float RayWidthMeters = 0.0018f;
        private const float CursorScaleMeters = 0.012f;
        private const string RayObjectName = "OpenXR_DanmenRay";
        private const string CursorObjectName = "OpenXR_DanmenCursor";
        private const string EdgeFrameObjectName = "OpenXR_DanmenEdgeFrame";
        private const float EdgeFrameLineWidth = 0.0032f;
        private static readonly string[] SourceCameraNameTokens = { "Dammen", "Danmen" };
        private static readonly Color EdgeFrameColor = new(1.0f, 0.88f, 0.30f, 0.95f);
        private static readonly Color ResizeHandleColor = new(1.0f, 0.95f, 0.55f, 0.95f);
        private static readonly Color RayColor = new(1.0f, 1.0f, 1.0f, 0.95f);
        private const float EdgeHitOuterNormalized = 0.5f + EdgeFrameExpandNormalized;

        private GameObject _vrRig;
        private GameObject _plane;
        private Material _planeMaterial;
        private Texture _sourceTexture;
        private Camera _sourceCamera;
        private float _nextSourceRefreshTime;
        private bool _loggedMissingSource;
        private bool _hasManualPose;
        private Vector3 _manualWorldPos;
        private Quaternion _manualWorldRot;
        private bool _isPanelAnchoredToRig;
        private Vector3 _anchoredLocalPos;
        private Quaternion _anchoredLocalRot;
        private GameObject _edgeFrameObject;
        private LineRenderer _edgeFrameLine;
        private Material _edgeFrameMaterial;
        private readonly GameObject[] _resizeHandles = new GameObject[4];
        private Material _resizeHandleMaterial;
        private GameObject _rayObject;
        private LineRenderer _rayLine;
        private Material _rayMaterial;
        private GameObject _cursorObject;
        private Material _cursorMaterial;
        private float _panelScaleMultiplier;

        public void Initialize(GameObject vrRig)
        {
            Teardown();

            _vrRig = vrRig;
            _nextSourceRefreshTime = 0f;
            _loggedMissingSource = false;
            _hasManualPose = false;
            _manualWorldPos = Vector3.zero;
            _manualWorldRot = Quaternion.identity;
            _isPanelAnchoredToRig = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _panelScaleMultiplier = 1f;
            CreatePlane();
            RefreshSource(force: true);
            ApplySourceTexture();
            UpdatePlaneScale();
            SetPlaneVisible(false);
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
            if (_edgeFrameObject == null) return;
            bool shouldShow = visible && _plane != null && _plane.activeInHierarchy;
            if (_edgeFrameObject.activeSelf != shouldShow)
            {
                _edgeFrameObject.SetActive(shouldShow);
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
            _panelScaleMultiplier = Mathf.Clamp(panelScale, MinPanelScale, MaxPanelScale);
            UpdatePlaneScale();
        }

        public void Update(
            bool togglePressed,
            bool hasPanelPose,
            Vector3 panelWorldPos,
            Quaternion panelWorldRot,
            bool hasPointerPose,
            Vector3 pointerOriginWorld,
            Vector3 pointerDirectionWorld)
        {
            if (_plane == null) return;

            RefreshSource(force: false);
            ApplySourceTexture();
            HandleAnchorToggle(togglePressed);

            bool hasTexture = _sourceTexture != null;
            bool shouldShow = hasTexture && (hasPanelPose || _hasManualPose || _isPanelAnchoredToRig);
            SetPlaneVisible(shouldShow);
            if (!shouldShow)
            {
                SetRayVisible(false);
                return;
            }

            if (_hasManualPose)
            {
#if MONO
                _plane.transform.position = _manualWorldPos;
                _plane.transform.rotation = _manualWorldRot;
#elif CPP
                _plane.transform.SetPositionAndRotation(_manualWorldPos, _manualWorldRot);
#endif
                UpdatePlaneScale();
                UpdateRayAndCursor(hasPointerPose, pointerOriginWorld, pointerDirectionWorld);
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
                UpdatePlaneScale();
                UpdateRayAndCursor(hasPointerPose, pointerOriginWorld, pointerDirectionWorld);
                return;
            }

            Vector3 localOffset = new(-RightHandLeftOffsetMeters, RightHandUpOffsetMeters, RightHandForwardOffsetMeters);
            Vector3 worldPos = panelWorldPos + (panelWorldRot * localOffset);
            Quaternion worldRot = Quaternion.LookRotation(panelWorldRot * Vector3.forward, panelWorldRot * Vector3.up);
#if MONO
            _plane.transform.position = worldPos;
            _plane.transform.rotation = worldRot;
#elif CPP
            _plane.transform.SetPositionAndRotation(worldPos, worldRot);
#endif
            UpdatePlaneScale();
            UpdateRayAndCursor(hasPointerPose, pointerOriginWorld, pointerDirectionWorld);
        }

        public void Teardown()
        {
            if (_plane != null) UnityEngine.Object.Destroy(_plane);
            _plane = null;

            if (_edgeFrameObject != null) UnityEngine.Object.Destroy(_edgeFrameObject);
            _edgeFrameObject = null;
            _edgeFrameLine = null;
            if (_rayObject != null) UnityEngine.Object.Destroy(_rayObject);
            _rayObject = null;
            _rayLine = null;
            if (_cursorObject != null) UnityEngine.Object.Destroy(_cursorObject);
            _cursorObject = null;

            if (_planeMaterial != null) UnityEngine.Object.Destroy(_planeMaterial);
            _planeMaterial = null;
            if (_edgeFrameMaterial != null) UnityEngine.Object.Destroy(_edgeFrameMaterial);
            _edgeFrameMaterial = null;
            if (_resizeHandleMaterial != null) UnityEngine.Object.Destroy(_resizeHandleMaterial);
            _resizeHandleMaterial = null;
            for (int i = 0; i < _resizeHandles.Length; i++)
            {
                _resizeHandles[i] = null;
            }
            if (_rayMaterial != null) UnityEngine.Object.Destroy(_rayMaterial);
            _rayMaterial = null;
            if (_cursorMaterial != null) UnityEngine.Object.Destroy(_cursorMaterial);
            _cursorMaterial = null;

            _vrRig = null;
            _sourceTexture = null;
            _sourceCamera = null;
            _nextSourceRefreshTime = 0f;
            _loggedMissingSource = false;
            _hasManualPose = false;
            _manualWorldPos = Vector3.zero;
            _manualWorldRot = Quaternion.identity;
            _isPanelAnchoredToRig = false;
            _anchoredLocalPos = Vector3.zero;
            _anchoredLocalRot = Quaternion.identity;
            _panelScaleMultiplier = 1f;
        }

        private void HandleAnchorToggle(bool togglePressed)
        {
            if (!togglePressed) return;

            // B short-press should control follow mode; clear any drag-fixed override first.
            _hasManualPose = false;
            _isPanelAnchoredToRig = !_isPanelAnchoredToRig;
            if (_isPanelAnchoredToRig)
            {
                CaptureAnchoredPoseFromCurrent();
                string anchorDesc = IsWorldAnchorMode() ? "fixed in world" : "follows rig movement";
                VRModCore.Log($"[Danmen][OpenXR] Panel mode: Anchored ({anchorDesc}).");
            }
            else
            {
                VRModCore.Log("[Danmen][OpenXR] Panel mode: Right-hand follow.");
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

        private void CreatePlane()
        {
            if (_vrRig == null) return;

            _plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _plane.name = PlaneName;
            _plane.transform.SetParent(_vrRig.transform, false);
            _plane.layer = _vrRig.layer;

            object collider = _plane.GetComponent("Collider");
            if (collider != null) UnityEngine.Object.Destroy(collider as UnityEngine.Object);

            var renderer = _plane.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");

            _planeMaterial = new Material(shader);
            _planeMaterial.color = Color.white;
            renderer.sharedMaterial = _planeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            CreateEdgeFrameIfNeeded();
            CreateResizeHandlesIfNeeded();
        }

        private void RefreshSource(bool force)
        {
            if (!force && Time.time < _nextSourceRefreshTime && _sourceTexture != null)
            {
                return;
            }

            _nextSourceRefreshTime = Time.time + SourceRefreshIntervalSeconds;

            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (!IsValidSourceCamera(cam)) continue;
                if (best == null || cam.depth >= bestDepth)
                {
                    best = cam;
                    bestDepth = cam.depth;
                }
            }

            _sourceCamera = best;
            _sourceTexture = _sourceCamera != null ? _sourceCamera.targetTexture : null;

            if (_sourceTexture == null)
            {
                if (!_loggedMissingSource)
                {
                    _loggedMissingSource = true;
                    VRModCore.LogWarning("[Danmen][OpenXR] No active Dammen/Danmen camera with RenderTexture target found.");
                }
            }
            else
            {
                if (_loggedMissingSource)
                {
                    _loggedMissingSource = false;
                }
            }
        }

        private static bool IsValidSourceCamera(Camera cam)
        {
            if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) return false;
            if (cam.targetTexture == null) return false;
            if ((cam.hideFlags & HideFlags.HideAndDontSave) != 0) return false;

            string cameraName = cam.name ?? string.Empty;
            for (int i = 0; i < SourceCameraNameTokens.Length; i++)
            {
                if (cameraName.IndexOf(SourceCameraNameTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplySourceTexture()
        {
            if (_planeMaterial == null) return;
            if (_planeMaterial.mainTexture == _sourceTexture) return;
            _planeMaterial.mainTexture = _sourceTexture;
        }

        private void UpdatePlaneScale()
        {
            if (_plane == null) return;

            float aspect = 16f / 9f;
            if (_sourceTexture != null && _sourceTexture.width > 0 && _sourceTexture.height > 0)
            {
                aspect = Mathf.Max(0.1f, (float)_sourceTexture.width / _sourceTexture.height);
            }

            float width = PlaneWidthMeters * GetPanelScaleMultiplier();
            float height = width / aspect;
            _plane.transform.localScale = new Vector3(width, height, 1f);

            if (_edgeFrameLine != null)
            {
                float border = 0.5f + EdgeFrameExpandNormalized;
                _edgeFrameLine.SetPosition(0, new Vector3(-border, -border, 0.002f));
                _edgeFrameLine.SetPosition(1, new Vector3(-border, border, 0.002f));
                _edgeFrameLine.SetPosition(2, new Vector3(border, border, 0.002f));
                _edgeFrameLine.SetPosition(3, new Vector3(border, -border, 0.002f));
                _edgeFrameLine.SetPosition(4, new Vector3(-border, -border, 0.002f));
            }

            float corner = 0.5f + EdgeFrameExpandNormalized;
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

        private void SetPlaneVisible(bool visible)
        {
            if (_plane == null) return;
            if (_plane.activeSelf == visible) return;
            _plane.SetActive(visible);
        }

        private void EnsureRayVisuals()
        {
            if (_vrRig == null) return;

            if (_rayObject == null)
            {
                _rayObject = new GameObject(RayObjectName);
                _rayObject.transform.SetParent(_vrRig.transform, false);
                _rayObject.layer = _vrRig.layer;
                _rayLine = _rayObject.AddComponent<LineRenderer>();
                _rayLine.useWorldSpace = true;
                _rayLine.loop = false;
                _rayLine.positionCount = 2;
                _rayLine.startWidth = RayWidthMeters;
                _rayLine.endWidth = RayWidthMeters;
                _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rayLine.receiveShadows = false;
                _rayLine.numCapVertices = 2;
                Shader rayShader = Shader.Find("Unlit/Color");
                if (rayShader == null) rayShader = Shader.Find("Sprites/Default");
                _rayMaterial = new Material(rayShader);
                _rayMaterial.color = RayColor;
                _rayLine.material = _rayMaterial;
                _rayObject.SetActive(false);
            }

            if (_cursorObject == null)
            {
                _cursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _cursorObject.name = CursorObjectName;
                _cursorObject.transform.SetParent(_vrRig.transform, false);
                _cursorObject.layer = _vrRig.layer;
                object collider = _cursorObject.GetComponent("Collider");
                if (collider != null) UnityEngine.Object.Destroy(collider as UnityEngine.Object);
                var renderer = _cursorObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Shader cursorShader = Shader.Find("Unlit/Color");
                    if (cursorShader == null) cursorShader = Shader.Find("Sprites/Default");
                    _cursorMaterial = new Material(cursorShader);
                    _cursorMaterial.color = RayColor;
                    renderer.sharedMaterial = _cursorMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                _cursorObject.transform.localScale = Vector3.one * CursorScaleMeters;
                _cursorObject.SetActive(false);
            }
        }

        private void UpdateRayAndCursor(bool hasPointerPose, Vector3 pointerOriginWorld, Vector3 pointerDirectionWorld)
        {
            EnsureRayVisuals();
            if (_rayLine == null || _rayObject == null || _cursorObject == null)
            {
                return;
            }

            if (!hasPointerPose || pointerDirectionWorld.sqrMagnitude <= 0.0001f || _plane == null || !_plane.activeInHierarchy)
            {
                SetRayVisible(false);
                return;
            }

            Vector3 direction = pointerDirectionWorld.normalized;
            Ray ray = new(pointerOriginWorld, direction);
            bool hasHit = TryRaycastPlaneAreaOrEdgeFrame(ray, _plane.transform, out Vector3 hitPointWorld);
            if (!hasHit)
            {
                SetRayVisible(false);
                return;
            }

            Vector3 rayEnd = hitPointWorld;

            _rayLine.SetPosition(0, pointerOriginWorld);
            _rayLine.SetPosition(1, rayEnd);
            if (!_rayObject.activeSelf) _rayObject.SetActive(true);

            if (!_cursorObject.activeSelf) _cursorObject.SetActive(true);
#if MONO
            _cursorObject.transform.position = hitPointWorld;
#elif CPP
            _cursorObject.transform.SetPositionAndRotation(hitPointWorld, Quaternion.identity);
#endif
        }

        private void SetRayVisible(bool visible)
        {
            if (_rayObject != null && _rayObject.activeSelf != visible)
            {
                _rayObject.SetActive(visible);
            }
            if (!visible && _cursorObject != null && _cursorObject.activeSelf)
            {
                _cursorObject.SetActive(false);
            }
        }

        private static bool TryRaycastPlaneQuad(Ray ray, Transform planeTransform, out float hitDistance, out Vector3 hitPointWorld, out bool isEdge)
        {
            hitDistance = 0f;
            hitPointWorld = default;
            isEdge = false;
            if (planeTransform == null || !planeTransform.gameObject.activeInHierarchy) return false;

            Plane plane = new(planeTransform.forward, planeTransform.position);
            if (!plane.Raycast(ray, out float distance) || distance <= 0f) return false;

            Vector3 worldHit = ray.GetPoint(distance);
            Vector3 localHit = planeTransform.InverseTransformPoint(worldHit);
            if (Mathf.Abs(localHit.x) > 0.5f || Mathf.Abs(localHit.y) > 0.5f)
            {
                return false;
            }

            float absX = Mathf.Abs(localHit.x);
            float absY = Mathf.Abs(localHit.y);
            isEdge = absX >= 0.4f || absY >= 0.4f;

            hitDistance = distance;
            hitPointWorld = worldHit;
            return true;
        }

        private static bool TryRaycastPlaneAreaOrEdgeFrame(Ray ray, Transform planeTransform, out Vector3 hitPointWorld)
        {
            hitPointWorld = default;
            if (planeTransform == null || !planeTransform.gameObject.activeInHierarchy) return false;

            Plane plane = new(planeTransform.forward, planeTransform.position);
            if (!plane.Raycast(ray, out float distance) || distance <= 0f) return false;

            Vector3 worldHit = ray.GetPoint(distance);
            Vector3 localHit = planeTransform.InverseTransformPoint(worldHit);
            if (Mathf.Abs(localHit.x) > EdgeHitOuterNormalized || Mathf.Abs(localHit.y) > EdgeHitOuterNormalized)
            {
                return false;
            }

            hitPointWorld = worldHit;
            return true;
        }

        private void CreateEdgeFrameIfNeeded()
        {
            if (_plane == null || _edgeFrameObject != null) return;

            _edgeFrameObject = new GameObject(EdgeFrameObjectName);
            _edgeFrameObject.transform.SetParent(_plane.transform, false);
            _edgeFrameLine = _edgeFrameObject.AddComponent<LineRenderer>();
            _edgeFrameLine.useWorldSpace = false;
            _edgeFrameLine.loop = false;
            _edgeFrameLine.positionCount = 5;
            _edgeFrameLine.startWidth = EdgeFrameLineWidth;
            _edgeFrameLine.endWidth = EdgeFrameLineWidth;
            _edgeFrameLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _edgeFrameLine.receiveShadows = false;
            _edgeFrameLine.numCapVertices = 4;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _edgeFrameMaterial = new Material(shader);
            _edgeFrameMaterial.color = EdgeFrameColor;
            _edgeFrameLine.material = _edgeFrameMaterial;
            _edgeFrameObject.SetActive(false);
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

        private float GetPanelScaleMultiplier()
        {
            return Mathf.Clamp(_panelScaleMultiplier, MinPanelScale, MaxPanelScale);
        }
    }
}
#endif
