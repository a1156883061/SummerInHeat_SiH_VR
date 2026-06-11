using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

#if CPP
using Il2CppInterop.Runtime;
#endif

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrVisualizationManager
    {
        private IVrCameraSetup _cameraSetup;
        private bool _managerInitialized = false;
        private GameObject _currentlyTrackedOriginalCameraGO = null;

        private bool _isUserSafeModeActive = true;
        private float _autoSafeModeEndTime = -1f;

        private UnityAction<Scene, Scene> _sceneChangedActionDelegate;

        private bool _hasVrBeenAttemptedByUser = false;
        internal bool IsVrReady => _hasVrBeenAttemptedByUser && _cameraSetup != null && _cameraSetup.IsVrAvailable;
        internal bool IsGameCameraFollowModeActive => IsVrReady && _cameraSetup != null && _cameraSetup.IsGameCameraFollowModeActive;
        private float _nextCameraJudgeLogTime;
        private string _lastCameraJudgeSignature = string.Empty;
        private const float CameraJudgeLogIntervalSeconds = 1.0f;
        private readonly HashSet<string> _disableRigSceneNames = new(StringComparer.OrdinalIgnoreCase);
        private string _cachedDisableRigScenesRaw = string.Empty;
        private string _lastSkipRigLogScene = string.Empty;

        internal Camera VrCameraForUIParenting
        {
            get
            {
                if (!IsVrReady) return null;
                VrCameraRig rig = _cameraSetup.GetVrCameraGameObjects();
                if (rig.LeftEye != null) return rig.LeftEye.GetComponent<Camera>();
                if (rig.RightEye != null) return rig.RightEye.GetComponent<Camera>();
                return null;
            }
        }

        internal bool TryGetVrEyeMidpointWorld(out Vector3 midpointWorld)
        {
            midpointWorld = default;
            return TryGetVrEyeCenterPoseWorld(out midpointWorld, out _);
        }

        internal bool TryGetVrEyeCenterPoseWorld(out Vector3 midpointWorld, out Quaternion centerRotationWorld)
        {
            midpointWorld = default;
            centerRotationWorld = Quaternion.identity;

            if (!IsVrReady || _cameraSetup == null)
            {
                return false;
            }

            bool autoSafeModeEngaged = Time.time < _autoSafeModeEndTime;
            if (_isUserSafeModeActive || autoSafeModeEngaged)
            {
                return false;
            }

            VrCameraRig rig = _cameraSetup.GetVrCameraGameObjects();
            Transform leftEye = rig.LeftEye != null ? rig.LeftEye.transform : null;
            Transform rightEye = rig.RightEye != null ? rig.RightEye.transform : null;

            if (leftEye != null && rightEye != null)
            {
                midpointWorld = (leftEye.position + rightEye.position) * 0.5f;
                Vector3 forward = (leftEye.forward + rightEye.forward).normalized;
                Vector3 up = (leftEye.up + rightEye.up).normalized;
                if (forward.sqrMagnitude <= 0.0001f)
                {
                    forward = leftEye.forward.sqrMagnitude > 0.0001f ? leftEye.forward : rightEye.forward;
                }
                if (up.sqrMagnitude <= 0.0001f)
                {
                    up = leftEye.up.sqrMagnitude > 0.0001f ? leftEye.up : rightEye.up;
                }
                centerRotationWorld = Quaternion.LookRotation(forward, up);
                return true;
            }

            if (leftEye != null)
            {
                midpointWorld = leftEye.position;
                centerRotationWorld = leftEye.rotation;
                return true;
            }

            if (rightEye != null)
            {
                midpointWorld = rightEye.position;
                centerRotationWorld = rightEye.rotation;
                return true;
            }

            return false;
        }

        internal void Initialize()
        {
            if (_managerInitialized) return;
            _managerInitialized = true;

            if (!ConfigManager.EnableVrInjection.Value)
            {
                VRModCore.Log("VR Visualization feature is disabled by config.");
                return;
            }

            _isUserSafeModeActive = ConfigManager.SafeModeStartsActive.Value;
            VRModCore.Log($"Initial user safe mode active: {_isUserSafeModeActive}. VR init will be delayed until user first deactivates Safe Mode.");

            if (ConfigManager.EnableAutomaticSafeMode.Value)
            {
                VRModCore.LogRuntimeDebug("Automatic safe mode enabled, subscribing to scene changes.");
#if CPP
                Action<Scene, Scene> csAction = OnActiveSceneChanged;
                _sceneChangedActionDelegate = DelegateSupport.ConvertDelegate<UnityAction<Scene, Scene>>(csAction);
                if (_sceneChangedActionDelegate != null) SceneManager.activeSceneChanged += _sceneChangedActionDelegate;
                else VRModCore.LogError("(IL2CPP) Failed to convert scene change delegate.");
#else
                _sceneChangedActionDelegate = OnActiveSceneChanged;
                SceneManager.activeSceneChanged += _sceneChangedActionDelegate;
#endif
            }
            VRModCore.Log("VrVisualizationManager initialized.");
        }

        private bool EnsureAndInitializeVrSubsystem()
        {
            if (_cameraSetup != null && _cameraSetup.IsVrAvailable)
            {
                VRModCore.LogRuntimeDebug("VR subsystem already initialized and available.");
                return true;
            }

            VRModCore.LogRuntimeDebug("Attempting to initialize VR subsystem...");

            string cameraSetupTypeNameFull;
#if OPENVR_BUILD
            cameraSetupTypeNameFull = "UnityVRMod.Features.VrVisualization.VrCameraSetup_CoreOpenVR";
#elif OPENXR_BUILD
            cameraSetupTypeNameFull = "UnityVRMod.Features.VrVisualization.VrCameraSetup_CoreOpenXR";
#else
            VRModCore.LogError("Critical Error! No VR backend build symbol (OPENVR_BUILD or OPENXR_BUILD) defined!");
            return false;
#endif

            try
            {
                Type setupType = Type.GetType(cameraSetupTypeNameFull);
                if (setupType == null || !typeof(IVrCameraSetup).IsAssignableFrom(setupType))
                {
                    VRModCore.LogError($"Type '{cameraSetupTypeNameFull}' not found or invalid.");
                    return false;
                }
                _cameraSetup = Activator.CreateInstance(setupType) as IVrCameraSetup;
                if (_cameraSetup == null)
                {
                    VRModCore.LogError($"Failed to create instance of '{cameraSetupTypeNameFull}'.");
                    return false;
                }

                VRModCore.Log($"Loaded {_cameraSetup.GetType().Name}. Initializing VR subsystem...");
                if (!_cameraSetup.InitializeVr(ConfigManager.VrApplicationKey.Value))
                {
                    VRModCore.LogWarning("VR subsystem initialization failed.");
                    _cameraSetup = null;
                    return false;
                }

                VRModCore.LogRuntimeDebug("VR subsystem initialization successful.");
                _hasVrBeenAttemptedByUser = true;
                                
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during VR subsystem instantiation or initialization:", ex);
                _cameraSetup = null;
                return false;
            }
        }

        private void OnActiveSceneChanged(Scene current, Scene next)
        {
            VRModCore.LogRuntimeDebug($"Scene changed from '{current.name}' to '{next.name}'.");
            CameraFinder.InvalidateCache(); // Invalidate cache on scene change.
            _lastCameraJudgeSignature = string.Empty;
            _nextCameraJudgeLogTime = 0f;
            _lastSkipRigLogScene = string.Empty;
            if (!IsVrReady) return;

            if (ConfigManager.EnableAutomaticSafeMode.Value)
            {
                ActivateAutomaticSafeMode($"Scene changed to '{next.name}'");
            }
        }

        private void ActivateAutomaticSafeMode(string reason)
        {
            if (!IsVrReady || !ConfigManager.EnableAutomaticSafeMode.Value) return;

            float duration = ConfigManager.AutomaticSafeModeDurationSecs.Value;
            _autoSafeModeEndTime = Time.time + duration;
            VRModCore.Log($"Automatic Safe Mode ENGAGED for {duration:F1}s. Reason: {reason}.");
        }

        public void ToggleUserSafeMode()
        {
            _isUserSafeModeActive = !_isUserSafeModeActive;
            VRModCore.Log($"User Safe Mode Toggled. Now: {(_isUserSafeModeActive ? "ACTIVE (Rendering OFF)" : "INACTIVE (Rendering ON)")}");

            if (_isUserSafeModeActive)
            {
                if (_cameraSetup != null)
                {
                    var level = ConfigManager.ActiveSafeModeLevel.Value;
                    VRModCore.LogRuntimeDebug($"Entering Safe Mode with level: {level}");

                    if (level == SafeModeLevel.FullVrReinitOnToggle)
                    {
                        VRModCore.Log("Tearing down full VR subsystem for re-initialization.");
                        _cameraSetup.TeardownVr();
                        _cameraSetup = null;
                        _hasVrBeenAttemptedByUser = false;
                        CameraFinder.InvalidateCache();
                    }
                    else if (level == SafeModeLevel.RigReinitOnToggle)
                    {
                        VRModCore.Log("Tearing down VR camera rig for re-initialization.");
                        _cameraSetup.TeardownCameraRig();
                        _currentlyTrackedOriginalCameraGO = null;
                        CameraFinder.InvalidateCache();
                    }
                }
            }
            else
            {
                _autoSafeModeEndTime = -1f;
                if (!_hasVrBeenAttemptedByUser)
                {
                    VRModCore.LogRuntimeDebug("First time user is disabling Safe Mode. Attempting to initialize VR subsystem...");
                    if (!EnsureAndInitializeVrSubsystem())
                    {
                        VRModCore.LogError("Failed to initialize VR subsystem. Re-enabling Safe Mode.");
                        _isUserSafeModeActive = true;
                        _hasVrBeenAttemptedByUser = false;
                    }
                }
            }
        }

        internal void Update()
        {
            if (_cameraSetup == null && _hasVrBeenAttemptedByUser)
            {
                if (!_isUserSafeModeActive)
                {
                    VRModCore.LogRuntimeDebug("VR Subsystem is fully torn down. Re-initializing due to user exiting safe mode.");
                    if (!EnsureAndInitializeVrSubsystem())
                    {
                        VRModCore.LogError("Failed to re-initialize VR subsystem. Re-enabling Safe Mode.");
                        _isUserSafeModeActive = true;
                    }
                }
                return;
            }

            if (!_hasVrBeenAttemptedByUser || _cameraSetup == null || !_cameraSetup.IsVrAvailable) return;

            bool autoSafeModeEngaged = Time.time < _autoSafeModeEndTime;
            bool shouldRender = !_isUserSafeModeActive && !autoSafeModeEngaged;

            VrCameraRig vrCameras = _cameraSetup.GetVrCameraGameObjects();
            bool rigIsSetUp = vrCameras.LeftEye != null || vrCameras.RightEye != null;
            string activeSceneName = SceneManager.GetActiveScene().name;

            if (ShouldSkipRigForScene(activeSceneName))
            {
                if (!string.Equals(_lastSkipRigLogScene, activeSceneName, StringComparison.Ordinal))
                {
                    VRModCore.Log($"Skipping VR rig setup in scene '{activeSceneName}' (Disable VR Rig Scenes config).");
                    _lastSkipRigLogScene = activeSceneName;
                }

                if (rigIsSetUp)
                {
                    _cameraSetup.TeardownCameraRig();
                    _currentlyTrackedOriginalCameraGO = null;
                    CameraFinder.InvalidateCache();
                }

                return;
            }

            _lastSkipRigLogScene = string.Empty;
            LogCameraJudgeIfNeeded();

            Camera mainCam = CameraFinder.FindGameCamera();

            if (mainCam != null)
            {
                if (rigIsSetUp)
                {
                    if (_currentlyTrackedOriginalCameraGO != mainCam.gameObject)
                    {
                        VRModCore.Log($"Game's main camera changed to '{mainCam.name}'. Re-creating VR rig.");
                        _cameraSetup.TeardownCameraRig();
                        CameraFinder.InvalidateCache(); // Invalidate since we are about to re-setup
                        _cameraSetup.SetupCameraRig(mainCam);
                        _currentlyTrackedOriginalCameraGO = mainCam.gameObject;
                        if (ConfigManager.EnableAutomaticSafeMode.Value)
                            ActivateAutomaticSafeMode("Main camera changed");
                    }
                }
                else if (shouldRender)
                {
                    VRModCore.Log($"Found game camera '{mainCam.name}'. Setting up VR rig.");
                    _cameraSetup.SetupCameraRig(mainCam);
                    _currentlyTrackedOriginalCameraGO = mainCam.gameObject;
                    if (ConfigManager.EnableAutomaticSafeMode.Value)
                        ActivateAutomaticSafeMode("Initial rig setup");
                }
            }
            else if (rigIsSetUp)
            {
                VRModCore.LogWarning("Game's main camera has become null. Tearing down VR rig to prevent conflicts.");
                _cameraSetup.TeardownCameraRig();
                _currentlyTrackedOriginalCameraGO = null;
                CameraFinder.InvalidateCache();
            }

            if (shouldRender && rigIsSetUp)
            {
                _cameraSetup.UpdatePoses();
            }
        }

        private void RefreshDisableRigScenesCacheIfNeeded()
        {
            string raw = ConfigManager.DisableVrRigScenes?.Value ?? string.Empty;
            if (string.Equals(raw, _cachedDisableRigScenesRaw, StringComparison.Ordinal))
            {
                return;
            }

            _cachedDisableRigScenesRaw = raw;
            _disableRigSceneNames.Clear();

            string[] tokens = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string sceneName = token.Trim();
                if (sceneName.Length > 0)
                {
                    _disableRigSceneNames.Add(sceneName);
                }
            }
        }

        private bool ShouldSkipRigForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            RefreshDisableRigScenesCacheIfNeeded();
            return _disableRigSceneNames.Contains(sceneName);
        }

        internal void Shutdown()
        {
            VRModCore.LogRuntimeDebug("Shutdown called.");
            if (ConfigManager.EnableAutomaticSafeMode.Value && _sceneChangedActionDelegate != null)
            {
                SceneManager.activeSceneChanged -= _sceneChangedActionDelegate;
            }

            if (_cameraSetup != null)
            {
                VRModCore.LogRuntimeDebug($"Shutting down camera setup: {_cameraSetup.GetType().Name}.");
                _cameraSetup.TeardownVr();
            }
            _lastCameraJudgeSignature = string.Empty;
            _nextCameraJudgeLogTime = 0f;
            _lastSkipRigLogScene = string.Empty;
            _managerInitialized = false;
        }

        public void LiveUpdateWorldScale(float newScale)
        {
            if (IsVrReady)
            {
                Camera cameraComponent = _currentlyTrackedOriginalCameraGO != null
                    ? _currentlyTrackedOriginalCameraGO.GetComponent<Camera>()
                    : null;
                _cameraSetup.SetWorldScale(newScale, cameraComponent);
            }
        }

        public void LiveUpdateCameraNearClip(float newNearClip)
        {
            if (IsVrReady) _cameraSetup.SetCameraNearClip(newNearClip);
        }

        public void LiveUpdateUserEyeHeightOffset(float newOffset)
        {
            if (IsVrReady) _cameraSetup.SetUserEyeHeightOffset(newOffset);
        }

        private void LogCameraJudgeIfNeeded()
        {
            if (!(ConfigManager.EnableRuntimeDebugLogging?.Value ?? false)) return;
            if (Time.time < _nextCameraJudgeLogTime) return;
            _nextCameraJudgeLogTime = Time.time + CameraJudgeLogIntervalSeconds;

            string signature = CameraJudge.BuildStateSignature();
            if (string.Equals(signature, _lastCameraJudgeSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastCameraJudgeSignature = signature;
            VRModCore.Log(CameraJudge.BuildDebugSummary());

            Camera mainCameraForHierarchy = null;
            if (_currentlyTrackedOriginalCameraGO != null)
            {
                mainCameraForHierarchy = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();
            }
            if (mainCameraForHierarchy == null)
            {
                mainCameraForHierarchy = Camera.main;
            }
            VRModCore.Log(CameraJudge.BuildMainCameraHierarchyDebugSummary(mainCameraForHierarchy));
        }
    }
}
