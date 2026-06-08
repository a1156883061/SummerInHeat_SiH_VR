using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityVRMod.Features.VRVisualization.OpenVR;
using System.Runtime.InteropServices;

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrCameraSetup_CoreOpenVR : IVrCameraSetup
    {
        private bool _isVrInitialized = false;
        private GameObject _vrRig = null;
        private float _currentAppliedRigScale = 1.0f;

        private Camera _leftVrCamera = null;
        private GameObject _leftVrCameraGO = null;
        private Camera _rightVrCamera = null;
        private GameObject _rightVrCameraGO = null;
        private float _lastCalculatedVerticalOffset;

        private GameObject _currentlyTrackedOriginalCameraGO = null;

        private CVRSystem _hmd = null;
        private CVRCompositor _compositor = null;
        private readonly TrackedDevicePose_t[] _trackedPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private uint _hmdRenderWidth = 0;
        private uint _hmdRenderHeight = 0;
        private RenderTexture _leftEyeTexture = null;
        private RenderTexture _rightEyeTexture = null;
        private ETextureType _vrTextureType = ETextureType.DirectX;
        private static CommandBuffer _flushCommandBuffer;
        private readonly GameCameraRigFollowState _gameCameraRigFollow = new();
        private ControllerLogState _leftControllerLogState;
        private ControllerLogState _rightControllerLogState;
        private readonly OpenVrRigLocomotion _locomotion = new();
        private readonly OpenVrUiInteractor _uiInteractor = new();
        private readonly OpenVrUiProjectionPlane _uiProjectionPlane = new();
        private readonly Dictionary<Camera, List<Component>> _syncedPostFxComponents = new();
        private readonly Dictionary<Camera, bool> _suppressedSceneCameras = new();
        private readonly List<OverlayCameraBinding> _overlayCameraBindings = new();
        private float _nextOverlayCameraRefreshTime;
        private float _nextSceneCameraSuppressRefreshTime;
        private Type _nguiUiCameraType;
        private bool _nguiUiCameraTypeResolved;
        private bool _beautifyAssignFailureLogged;
        private GameObject _leftControllerPoseMarkerGO;
        private GameObject _rightControllerPoseMarkerGO;
        private float _nextControllerPoseLogTime;

        private const float TriggerAnalogLogDelta = 0.25f;
        private const float Primary2DAxisLogDelta = 0.10f;
        private const float ControllerPoseLogIntervalSeconds = 1.0f;
        private const float OverlayCameraRefreshIntervalSeconds = 1.0f;
        private const float SceneCameraSuppressRefreshIntervalSeconds = 1.0f;

        private struct ControllerLogState
        {
            public bool HasSample;
            public bool IsConnected;
            public bool TriggerPressed;
            public bool GripPressed;
            public bool APressed;
            public bool MenuPressed;
            public bool JoystickClickPressed;
            public bool TouchpadPressed;
            public bool TouchpadTouched;
            public float TriggerValue;
            public float Primary2DX;
            public float Primary2DY;
        }

        private sealed class OverlayCameraBinding
        {
            public Camera Source;
            public Camera LeftEye;
            public Camera RightEye;
        }

        public bool IsVrAvailable => _isVrInitialized && _hmd != null && _compositor != null;

        public bool InitializeVr(string applicationKey)
        {
            VRModCore.LogRuntimeDebug($"InitializeVr START. AppKey: {(string.IsNullOrEmpty(applicationKey) ? "N/A" : applicationKey)}");
            if (_isVrInitialized)
            {
                VRModCore.LogWarning("InitializeVr called but already initialized.");
                return true;
            }

            EVRInitError initError = EVRInitError.None;
            try
            {
                _hmd = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Scene, applicationKey);
                if (initError != EVRInitError.None)
                {
                    VRModCore.LogError($"OpenVR.Init FAILED. Error: {initError} ({OpenVR.GetStringForHmdError(initError)})");
                    TeardownVrInternal(); return false;
                }
                VRModCore.LogRuntimeDebug("OpenVR.Init SUCCESS. CVRSystem instance obtained.");

                _compositor = OpenVR.Compositor;
                if (_compositor == null)
                {
                    VRModCore.LogError("OpenVR.Compositor is null. Cannot proceed.");
                    TeardownVrInternal(); return false;
                }
                VRModCore.LogRuntimeDebug("CVRCompositor instance obtained.");

                if (!_hmd.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd))
                {
                    VRModCore.LogWarning("HMD is not connected.");
                    TeardownVrInternal(); return false;
                }

                string driverName = GetStringProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_TrackingSystemName_String);
                string modelNumber = GetStringProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_ModelNumber_String);
                VRModCore.Log($"OpenVR: HMD Initialized: Driver='{driverName}', Model='{modelNumber}'");

                _hmd.GetRecommendedRenderTargetSize(ref _hmdRenderWidth, ref _hmdRenderHeight);
                VRModCore.LogRuntimeDebug($"Initial Recommended RT Size: {_hmdRenderWidth}x{_hmdRenderHeight}");
                if (_hmdRenderWidth == 0 || _hmdRenderHeight == 0)
                {
                    VRModCore.LogWarning("RT Size zero. Using fallback 1024x1024.");
                    _hmdRenderWidth = 1024; _hmdRenderHeight = 1024;
                }

                var graphicsDeviceType = SystemInfo.graphicsDeviceType;
                if (graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 || graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
                    _vrTextureType = ETextureType.DirectX;
                else if (graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore)
                    _vrTextureType = ETextureType.OpenGL;
                else
                {
                    VRModCore.LogError($"Unsupported Graphics Device Type for OpenVR: {graphicsDeviceType}. Failing initialization.");
                    TeardownVrInternal(); return false;
                }

                if (!SetupRenderTargets())
                {
                    VRModCore.LogError("SetupRenderTargets FAILED. Cannot proceed.");
                    TeardownVrInternal(); return false;
                }

                _compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
                _isVrInitialized = true;
                VRModCore.Log($"InitializeVr SUCCESS. RT Size: {_leftEyeTexture.width}x{_leftEyeTexture.height}, TextureType: {_vrTextureType}");

                _flushCommandBuffer ??= new CommandBuffer { name = "VRModFlush_OpenVR" };
            }
            catch (Exception e)
            {
                VRModCore.LogError("InitializeVr EXCEPTION:", e);
                TeardownVrInternal(); return false;
            }
            return _isVrInitialized;
        }

        private string GetStringProperty(uint deviceIndex, ETrackedDeviceProperty prop)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            uint capacity = _hmd.GetStringTrackedDeviceProperty(deviceIndex, prop, null, 0, ref error);
            if (capacity == 0) return "";
            var buffer = new System.Text.StringBuilder((int)capacity);
            _hmd.GetStringTrackedDeviceProperty(deviceIndex, prop, buffer, capacity, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success) ? buffer.ToString() : error.ToString();
        }

        public void SetupCameraRig(Camera mainCamera)
        {
            if (mainCamera == null)
            {
                VRModCore.LogError("SetupCameraRig FAILED: mainCamera is null.");
                return;
            }

            mainCamera.fieldOfView = 60f;
            VRModCore.LogRuntimeDebug($"SetupCameraRig for camera '{mainCamera.name}'.");

            if (_vrRig != null) TeardownCameraRig();
            if (!IsVrAvailable) { VRModCore.LogError("SetupCameraRig FAILED: VR system not available/initialized."); return; }

            _vrRig = new GameObject("UnityVRMod_VRRig_OpenVR");
            _currentlyTrackedOriginalCameraGO = mainCamera.gameObject;

            Vector3 targetPosition = mainCamera.transform.position;
            Quaternion targetRotation = Quaternion.Euler(0, mainCamera.transform.eulerAngles.y, 0);

            var poseOverrides = PoseParser.Parse(ConfigManager.ScenePoseOverrides.Value);
            string currentSceneName = mainCamera.gameObject.scene.name;

            if (poseOverrides.TryGetValue(currentSceneName, out PoseOverride poseOverride))
            {
                VRModCore.Log($"Applying pose override for scene '{currentSceneName}'");
                var p = poseOverride.Position;
                var r = poseOverride.Rotation;
                var originalPos = mainCamera.transform.position;
                var originalRot = mainCamera.transform.eulerAngles;

                Vector3 finalPos = new(float.IsNaN(p.x) ? originalPos.x : p.x, float.IsNaN(p.y) ? originalPos.y : p.y, float.IsNaN(p.z) ? originalPos.z : p.z);
                Vector3 finalRot = new(float.IsNaN(r.x) ? originalRot.x : r.x, float.IsNaN(r.y) ? originalRot.y : r.y, float.IsNaN(r.z) ? originalRot.z : r.z);
                targetPosition = finalPos;
                targetRotation = Quaternion.Euler(finalRot);
            }
            _vrRig.transform.SetPositionAndRotation(targetPosition, targetRotation);
            _gameCameraRigFollow.Reset(mainCamera);

            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, ConfigManager.VrWorldScale.Value);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            _leftVrCameraGO = new GameObject("OpenVR_VRCamera_Left");
            _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_leftVrCameraGO.GetComponent<AudioListener>() == null) _leftVrCameraGO.AddComponent<AudioListener>();

            _rightVrCameraGO = new GameObject("OpenVR_VRCamera_Right");
            _rightVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _rightVrCamera = _rightVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
            SyncMinimalPostProcessing(mainCamera);
            _nextOverlayCameraRefreshTime = 0f;
            _nextSceneCameraSuppressRefreshTime = 0f;
            DestroyOverlayCameras();
            VRModCore.Log("[PostFX] Overlay cameras disabled. Using VR eye cameras with HDR effect copy only (ScreenOverlay/Beautify).");
            RefreshSceneCameraSuppressionIfNeeded(mainCamera, force: true);

            _lastCalculatedVerticalOffset = 0f;
            UpdateVerticalOffset();
            SetupControllerPoseTestMarkers();
            _nextControllerPoseLogTime = 0f;
            _uiProjectionPlane.Initialize(_vrRig, mainCamera);
            _uiInteractor.Initialize(mainCamera);
            VRModCore.Log("[UI] OpenVR UI interactor enabled (Right Trigger=UI click).");
            VRModCore.Log("[Locomotion] Enabled (Right hand only: TouchpadClick=Teleport, Right stick X=SnapTurn).");

            VRModCore.Log("OpenVR: VR Camera Rig setup complete.");
        }

        private void ConfigureVrCamera(Camera vrCam, Camera mainCamRef, string eyeName)
        {
            vrCam.stereoTargetEye = StereoTargetEyeMask.None;
            vrCam.enabled = false;

            vrCam.clearFlags = mainCamRef.clearFlags;
            vrCam.backgroundColor = mainCamRef.backgroundColor;
            int resolvedMask = mainCamRef.cullingMask;
            if (resolvedMask == 0)
            {
                // Synthetic/UI fallback cameras may carry an empty mask; avoid rendering a black frame.
                resolvedMask = ~0;
            }

            if (_vrRig != null)
            {
                resolvedMask |= 1 << _vrRig.layer;
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                // Keep original game UI hidden in VR eye cameras; UI is shown via projection panel.
                resolvedMask &= ~(1 << uiLayer);
            }

            vrCam.cullingMask = resolvedMask;
            vrCam.renderingPath = mainCamRef.renderingPath;
            vrCam.allowHDR = mainCamRef.allowHDR;
            vrCam.allowMSAA = mainCamRef.allowMSAA;

            float nearClipPlaneUserValue = ConfigManager.VrCameraNearClipPlane.Value;
            vrCam.nearClipPlane = Mathf.Max(0.001f, nearClipPlaneUserValue * _currentAppliedRigScale);
            vrCam.farClipPlane = mainCamRef.farClipPlane * _currentAppliedRigScale;

            VRModCore.LogRuntimeDebug($"  {eyeName} Cam Clips Updated: Near={vrCam.nearClipPlane:F4}, Far={vrCam.farClipPlane:F2}");
        }

        public void UpdatePoses()
        {
            if (!IsVrAvailable || _vrRig == null) return;

            try
            {
                int delay = ConfigManager.OpenVR_WaitGetPosesDelayMs.Value;
                if (delay > 0) System.Threading.Thread.Sleep(delay);

                var compositorError = _compositor.WaitGetPoses(_trackedPoses, _gamePoses);
                if (compositorError != EVRCompositorError.None)
                {
                    VRModCore.LogError($"WaitGetPoses FAILED. Error: {compositorError}");
                    return;
                }

                if (!_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
                {
                    VRModCore.LogWarning("UpdatePoses: HMD pose NOT valid. Rendering from origin to re-establish session.");
                }

                UpdateControllerInputLogs();
                Camera currentMainCamera = _currentlyTrackedOriginalCameraGO != null
                    ? _currentlyTrackedOriginalCameraGO.GetComponent<Camera>()
                    : null;
                ApplyGameCameraRigFollow(currentMainCamera);
                RefreshSceneCameraSuppressionIfNeeded(currentMainCamera, force: false);
                _uiProjectionPlane.Update(currentMainCamera, _hmd, _trackedPoses);
                _uiInteractor.Update(_hmd, _vrRig, _trackedPoses);
                _locomotion.Update(_hmd, _vrRig, _trackedPoses);
                UpdateControllerPoseTest();

                RenderEye(EVREye.Eye_Left);
                RenderEye(EVREye.Eye_Right);
            }
            catch (Exception e)
            {
                VRModCore.LogError("UpdatePoses EXCEPTION:", e);
            }
        }

        private void ApplyGameCameraRigFollow(Camera currentMainCamera)
        {
            if (!ConfigManager.VrRigFollowsGameCamera.Value)
            {
                _gameCameraRigFollow.Clear();
                return;
            }

            _gameCameraRigFollow.ApplyDelta(currentMainCamera, _vrRig);
        }

        private void UpdateControllerInputLogs()
        {
            if (_hmd == null) return;

            UpdateControllerLogForHand(ETrackedControllerRole.LeftHand, "Left", ref _leftControllerLogState);
            UpdateControllerLogForHand(ETrackedControllerRole.RightHand, "Right", ref _rightControllerLogState);
        }

        private void UpdateControllerLogForHand(ETrackedControllerRole role, string handName, ref ControllerLogState previousState)
        {
            uint deviceIndex = _hmd.GetTrackedDeviceIndexForControllerRole(role);
            bool isConnected = deviceIndex != OpenVR.k_unTrackedDeviceIndexInvalid && _hmd.IsTrackedDeviceConnected(deviceIndex);

            if (!isConnected)
            {
                if (!previousState.HasSample || previousState.IsConnected)
                {
                    VRModCore.LogRuntimeDebug($"[Input][{handName}] Controller not connected.");
                }
                previousState = new ControllerLogState { HasSample = true, IsConnected = false };
                return;
            }

            VRControllerState_t state = new();
            bool readSuccess = _hmd.GetControllerState(deviceIndex, ref state, (uint)Marshal.SizeOf(typeof(VRControllerState_t)));
            if (!readSuccess)
            {
                if (!previousState.HasSample || previousState.IsConnected)
                {
                    VRModCore.LogWarning($"[Input][{handName}] GetControllerState failed for device index {deviceIndex}.");
                }
                previousState = new ControllerLogState { HasSample = true, IsConnected = false };
                return;
            }

            bool triggerPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_SteamVR_Trigger);
            bool gripPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_Grip);
            bool aPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_A);
            bool menuPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_ApplicationMenu);
            bool joystickClickPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_IndexController_JoyStick);
            bool touchpadPressed = IsButtonPressed(state.ulButtonPressed, EVRButtonId.k_EButton_SteamVR_Touchpad);
            bool touchpadTouched = IsButtonPressed(state.ulButtonTouched, EVRButtonId.k_EButton_SteamVR_Touchpad);
            float triggerValue = state.rAxis1.x;
            float primary2DX = state.rAxis0.x;
            float primary2DY = state.rAxis0.y;

            if (!previousState.HasSample || !previousState.IsConnected)
            {
                VRModCore.Log($"[Input][{handName}] Controller connected (device {deviceIndex}).");
            }

            if (!previousState.HasSample || triggerPressed != previousState.TriggerPressed)
            {
                VRModCore.Log($"[Input][{handName}] Trigger {(triggerPressed ? "Pressed" : "Released")} (value={triggerValue:F2})");
            }
            if (!previousState.HasSample || gripPressed != previousState.GripPressed)
            {
                VRModCore.Log($"[Input][{handName}] Grip {(gripPressed ? "Pressed" : "Released")}");
            }
            if (!previousState.HasSample || aPressed != previousState.APressed)
            {
                VRModCore.Log($"[Input][{handName}] A {(aPressed ? "Pressed" : "Released")}");
            }
            if (!previousState.HasSample || menuPressed != previousState.MenuPressed)
            {
                VRModCore.Log($"[Input][{handName}] Menu {(menuPressed ? "Pressed" : "Released")}");
            }
            if (!previousState.HasSample || joystickClickPressed != previousState.JoystickClickPressed)
            {
                VRModCore.Log($"[Input][{handName}] JoystickClick {(joystickClickPressed ? "Pressed" : "Released")}");
            }
            if (!previousState.HasSample || touchpadPressed != previousState.TouchpadPressed)
            {
                VRModCore.Log($"[Input][{handName}] TouchpadClick {(touchpadPressed ? "Pressed" : "Released")}");
            }
            if (!previousState.HasSample || touchpadTouched != previousState.TouchpadTouched)
            {
                VRModCore.Log($"[Input][{handName}] TouchpadTouch {(touchpadTouched ? "Touched" : "Untouched")}");
            }
            if (!previousState.HasSample || Math.Abs(triggerValue - previousState.TriggerValue) >= TriggerAnalogLogDelta)
            {
                VRModCore.LogRuntimeDebug($"[Input][{handName}] Trigger analog {triggerValue:F2}");
            }
            if (!previousState.HasSample ||
                Math.Abs(primary2DX - previousState.Primary2DX) >= Primary2DAxisLogDelta ||
                Math.Abs(primary2DY - previousState.Primary2DY) >= Primary2DAxisLogDelta)
            {
                VRModCore.Log($"[Input][{handName}] Primary2D ({primary2DX:F2}, {primary2DY:F2})");
            }

            previousState = new ControllerLogState
            {
                HasSample = true,
                IsConnected = true,
                TriggerPressed = triggerPressed,
                GripPressed = gripPressed,
                APressed = aPressed,
                MenuPressed = menuPressed,
                JoystickClickPressed = joystickClickPressed,
                TouchpadPressed = touchpadPressed,
                TouchpadTouched = touchpadTouched,
                TriggerValue = triggerValue,
                Primary2DX = primary2DX,
                Primary2DY = primary2DY
            };
        }

        private static bool IsButtonPressed(ulong buttonMask, EVRButtonId buttonId)
        {
            int buttonBit = (int)buttonId;
            if (buttonBit < 0 || buttonBit > 63) return false;
            ulong bit = 1UL << buttonBit;
            return (buttonMask & bit) != 0;
        }

        private void SetupControllerPoseTestMarkers()
        {
            _leftControllerPoseMarkerGO = CreateControllerPoseMarker("OpenVR_ControllerPose_Left", new Color(1f, 0.2f, 0.2f, 0.95f));
            _rightControllerPoseMarkerGO = CreateControllerPoseMarker("OpenVR_ControllerPose_Right", new Color(0.2f, 0.4f, 1f, 0.95f));
        }

        private GameObject CreateControllerPoseMarker(string markerName, Color color)
        {
            if (_vrRig == null) return null;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            marker.name = markerName;
            marker.transform.SetParent(_vrRig.transform, false);
            marker.transform.localScale = new Vector3(0.04f, 0.06f, 0.04f);

            var collider = marker.GetComponent("Collider");
            if (collider != null) UnityEngine.Object.Destroy(collider);

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.material.color = color;
            }

            marker.SetActive(false);
            return marker;
        }

        private void UpdateControllerPoseTest()
        {
            if (_hmd == null) return;

            bool leftValid = TryUpdateControllerPoseMarker(ETrackedControllerRole.LeftHand, _leftControllerPoseMarkerGO, out Vector3 leftLocalPos);
            bool rightValid = TryUpdateControllerPoseMarker(ETrackedControllerRole.RightHand, _rightControllerPoseMarkerGO, out Vector3 rightLocalPos);

            if (Time.time < _nextControllerPoseLogTime) return;
            _nextControllerPoseLogTime = Time.time + ControllerPoseLogIntervalSeconds;

            if (!_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid) return;

            Vector3 hmdLocalPos = GetPositionFromHmdMatrix(_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);

            if (leftValid)
            {
                float dist = Vector3.Distance(hmdLocalPos, leftLocalPos);
                VRModCore.Log($"[PoseTest][Left] LocalPos={leftLocalPos:F3} DistToHmd={dist:F3}m");
            }

            if (rightValid)
            {
                float dist = Vector3.Distance(hmdLocalPos, rightLocalPos);
                VRModCore.Log($"[PoseTest][Right] LocalPos={rightLocalPos:F3} DistToHmd={dist:F3}m");
            }
        }

        private bool TryUpdateControllerPoseMarker(ETrackedControllerRole role, GameObject marker, out Vector3 localPos)
        {
            localPos = Vector3.zero;
            if (_hmd == null) return false;

            uint deviceIndex = _hmd.GetTrackedDeviceIndexForControllerRole(role);
            bool isConnected = deviceIndex != OpenVR.k_unTrackedDeviceIndexInvalid && _hmd.IsTrackedDeviceConnected(deviceIndex);
            if (!isConnected)
            {
                if (marker != null) marker.SetActive(false);
                return false;
            }

            if (deviceIndex >= _trackedPoses.Length || !_trackedPoses[deviceIndex].bPoseIsValid)
            {
                if (marker != null) marker.SetActive(false);
                return false;
            }

            HmdMatrix34_t pose = _trackedPoses[deviceIndex].mDeviceToAbsoluteTracking;
            localPos = GetPositionFromHmdMatrix(pose);
            Quaternion localRot = GetRotationFromHmdMatrix(pose);

            if (marker != null)
            {
                if (!marker.activeSelf) marker.SetActive(true);
#if MONO
                marker.transform.localPosition = localPos;
                marker.transform.localRotation = localRot;
#elif CPP
                marker.transform.SetLocalPositionAndRotation(localPos, localRot);
#endif
            }

            return true;
        }

        private void RenderEye(EVREye eye)
        {
            Camera vrCam = (eye == EVREye.Eye_Left) ? _leftVrCamera : _rightVrCamera;
            RenderTexture targetTexture = (eye == EVREye.Eye_Left) ? _leftEyeTexture : _rightEyeTexture;
            if (vrCam == null || targetTexture == null) return;

            if (_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
            {
                HmdMatrix34_t hmdPose = _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking;
                HmdMatrix34_t eyeToHeadMatrix = _hmd.GetEyeToHeadTransform(eye);

                Vector3 headPos = GetPositionFromHmdMatrix(hmdPose);
                Quaternion headRot = GetRotationFromHmdMatrix(hmdPose);
                Vector3 eyeOffset = GetPositionFromHmdMatrix(eyeToHeadMatrix);

#if MONO
                vrCam.transform.localPosition = headPos + (headRot * eyeOffset);
                vrCam.transform.localRotation = headRot;
#elif CPP
                vrCam.transform.SetLocalPositionAndRotation(headPos + (headRot * eyeOffset), headRot);
#endif
            }
            else
            {
                VRModCore.LogWarning("Rendering eye with last known valid pose because current pose is invalid.");
            }

            float l = 0, r = 0, t = 0, b = 0;
            _hmd.GetProjectionRaw(eye, ref l, ref r, ref t, ref b);

            vrCam.projectionMatrix = CreateUnityProjectionMatrixFromOpenVRFrustum(
                l * vrCam.nearClipPlane, r * vrCam.nearClipPlane,
                t * vrCam.nearClipPlane, b * vrCam.nearClipPlane,
                vrCam.nearClipPlane, vrCam.farClipPlane);

            vrCam.targetTexture = targetTexture;

            bool originalInvertCulling = GL.invertCulling;
            RebindCameraInstanceMembers(vrCam, vrCam);
            GL.invertCulling = true;
            vrCam.Render();
            GL.invertCulling = originalInvertCulling;

            vrCam.targetTexture = null;

            Graphics.ExecuteCommandBuffer(_flushCommandBuffer);

            Texture_t eyeTexture = new() { handle = targetTexture.GetNativeTexturePtr(), eType = _vrTextureType, eColorSpace = EColorSpace.Auto };
            VRTextureBounds_t textureBounds = new() { uMin = 0f, vMin = 0f, uMax = 1f, vMax = 1f };
            var submitError = _compositor.Submit(eye, ref eyeTexture, ref textureBounds, EVRSubmitFlags.Submit_Default);

            if (submitError != EVRCompositorError.None)
            {
                VRModCore.LogError($"Submit FAILED for eye {eye}. Error: {submitError}");
            }
        }

        private static Matrix4x4 CreateUnityProjectionMatrixFromOpenVRFrustum(float left, float right, float top, float bottom, float nearClip, float farClip)
        {
            Matrix4x4 m = new();
            m[0, 0] = (2.0f * nearClip) / (right - left);
            m[0, 2] = (right + left) / (right - left);
            m[1, 1] = (2.0f * nearClip) / (top - bottom);
            m[1, 2] = (top + bottom) / (top - bottom);
            m[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
            m[2, 3] = -(2.0f * farClip * nearClip) / (farClip - nearClip);
            m[3, 2] = -1.0f;
            return m;
        }

        private static Vector3 GetPositionFromHmdMatrix(HmdMatrix34_t matrix)
        {
            return new Vector3(matrix.m3, matrix.m7, -matrix.m11);
        }

        private static Quaternion GetRotationFromHmdMatrix(HmdMatrix34_t matrix)
        {
            Quaternion q = new()
            {
                w = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m0 + matrix.m5 + matrix.m10)) / 2,
                x = Mathf.Sqrt(Mathf.Max(0, 1 + matrix.m0 - matrix.m5 - matrix.m10)) / 2,
                y = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m0 + matrix.m5 - matrix.m10)) / 2,
                z = Mathf.Sqrt(Mathf.Max(0, 1 - matrix.m0 - matrix.m5 + matrix.m10)) / 2
            };
            q.x *= Mathf.Sign(q.x * (matrix.m9 - matrix.m6));
            q.y *= Mathf.Sign(q.y * (matrix.m2 - matrix.m8));
            q.z *= Mathf.Sign(q.z * (matrix.m4 - matrix.m1));
            return new Quaternion(-q.x, -q.y, q.z, q.w);
        }

        public void TeardownCameraRig()
        {
            VRModCore.LogRuntimeDebug("Tearing down VR camera rig.");
            RestoreSuppressedSceneCameras();
            DestroyOverlayCameras();
            ClearAllSyncedPostFxComponents();
            _uiProjectionPlane.Teardown();
            _uiInteractor.Teardown();
            ReleaseRenderTargets();
            if (_vrRig != null) UnityEngine.Object.Destroy(_vrRig);
            _vrRig = null;
            _leftVrCamera = null; _leftVrCameraGO = null;
            _rightVrCamera = null; _rightVrCameraGO = null;
            _leftControllerPoseMarkerGO = null;
            _rightControllerPoseMarkerGO = null;
            _nextOverlayCameraRefreshTime = 0f;
            _nextSceneCameraSuppressRefreshTime = 0f;
            _currentlyTrackedOriginalCameraGO = null;
            _gameCameraRigFollow.Clear();
        }

        private void TeardownVrInternal()
        {
            VRModCore.LogRuntimeDebug("Tearing down OpenVR system.");
            TeardownCameraRig();
            if (_hmd != null) OpenVR.Shutdown();
            _hmd = null;
            _compositor = null;
            _isVrInitialized = false;
        }

        public void TeardownVr() { TeardownVrInternal(); }

        private void ReleaseRenderTargets()
        {
            if (_leftEyeTexture != null) { _leftEyeTexture.Release(); UnityEngine.Object.Destroy(_leftEyeTexture); _leftEyeTexture = null; }
            if (_rightEyeTexture != null) { _rightEyeTexture.Release(); UnityEngine.Object.Destroy(_rightEyeTexture); _rightEyeTexture = null; }
        }

        private bool SetupRenderTargets()
        {
            uint width = _hmdRenderWidth;
            uint height = _hmdRenderHeight;
            int maxDim = ConfigManager.OpenVR_MaxRenderTargetDimension.Value;
            if (maxDim > 0)
            {
                width = Math.Min(width, (uint)maxDim);
                height = Math.Min(height, (uint)maxDim);
            }
            ReleaseRenderTargets();
            _leftEyeTexture = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32);
            _rightEyeTexture = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32);
            return _leftEyeTexture.Create() && _rightEyeTexture.Create();
        }

        public VrCameraRig GetVrCameraGameObjects() => new() { LeftEye = _leftVrCameraGO, RightEye = _rightVrCameraGO };

        public void SetCameraNearClip(float newNearClipBaseValue)
        {
            if (_vrRig == null) return;
            Camera mainCamera = null;
            if (_currentlyTrackedOriginalCameraGO != null)
                mainCamera = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();
            if (_leftVrCamera != null && mainCamera != null) ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_rightVrCamera != null && mainCamera != null) ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
            SyncMinimalPostProcessing(mainCamera);
        }

        private void UpdateVerticalOffset()
        {
            if (_vrRig == null) return;
            float newTotalOffset = ConfigManager.VrUserEyeHeightOffset.Value * _currentAppliedRigScale;
            float delta = newTotalOffset - _lastCalculatedVerticalOffset;
            _vrRig.transform.position += new Vector3(0, delta, 0);
            _lastCalculatedVerticalOffset = newTotalOffset;
        }

        public void SetWorldScale(float newWorldScale, Camera mainCameraRef)
        {
            if (_vrRig == null) return;
            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, newWorldScale);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            Camera mainCam = mainCameraRef;
            if (mainCam == null && _currentlyTrackedOriginalCameraGO != null)
                mainCam = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCam, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCam, "Right");
            SyncMinimalPostProcessing(mainCam);

            UpdateVerticalOffset();
        }

        public void SetUserEyeHeightOffset(float newOffset)
        {
            UpdateVerticalOffset();
        }

        private void RefreshSceneCameraSuppressionIfNeeded(Camera mainCamera, bool force)
        {
            if (!ConfigManager.OpenVR_DisableOriginalSceneCameras.Value)
            {
                if (_suppressedSceneCameras.Count > 0)
                {
                    RestoreSuppressedSceneCameras();
                }
                return;
            }

            if (!force && Time.time < _nextSceneCameraSuppressRefreshTime) return;
            _nextSceneCameraSuppressRefreshTime = Time.time + SceneCameraSuppressRefreshIntervalSeconds;

            if (mainCamera == null) return;
            int suppressedNow = 0;
            foreach (Camera camera in Camera.allCameras)
            {
                if (!ShouldSuppressSceneCamera(camera, mainCamera)) continue;
                if (!_suppressedSceneCameras.ContainsKey(camera))
                {
                    _suppressedSceneCameras[camera] = camera.enabled;
                }

                if (camera.enabled)
                {
                    camera.enabled = false;
                    suppressedNow++;
                }
            }

            if (suppressedNow > 0)
            {
                VRModCore.LogRuntimeDebug($"[Perf] Disabled {suppressedNow} original scene camera(s) while VR is active.");
            }
        }

        private bool ShouldSuppressSceneCamera(Camera camera, Camera mainCamera)
        {
            if (camera == null || mainCamera == null) return false;
            if (camera == mainCamera) return false;
            if (camera == _leftVrCamera || camera == _rightVrCamera) return false;
            if (!camera.gameObject.activeInHierarchy) return false;
            if (camera.gameObject.scene != mainCamera.gameObject.scene) return false;
            if (camera.name.StartsWith("OpenVR_", StringComparison.OrdinalIgnoreCase) ||
                camera.name.StartsWith("UnityVRMod_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Type nguiType = ResolveNguiUiCameraType();
            if (nguiType != null && camera.GetComponent(nguiType) != null)
            {
                // Keep NGUI camera alive for UI update/capture consistency.
                return false;
            }

            return true;
        }

        private void RestoreSuppressedSceneCameras()
        {
            if (_suppressedSceneCameras.Count == 0) return;

            int restored = 0;
            foreach (KeyValuePair<Camera, bool> kvp in _suppressedSceneCameras)
            {
                Camera camera = kvp.Key;
                if (camera == null) continue;
                camera.enabled = kvp.Value;
                restored++;
            }

            _suppressedSceneCameras.Clear();
            VRModCore.LogRuntimeDebug($"[Perf] Restored {restored} original scene camera(s).");
        }

        private void RefreshOverlayCamerasIfNeeded(Camera mainCamera)
        {
            if (Time.time < _nextOverlayCameraRefreshTime) return;
            _nextOverlayCameraRefreshTime = Time.time + OverlayCameraRefreshIntervalSeconds;

            if (mainCamera == null)
            {
                DestroyOverlayCameras();
                return;
            }

            List<Camera> sourceCameras = FindOverlaySourceCameras(mainCamera);
            if (HasSameOverlaySources(sourceCameras))
            {
                UpdateOverlayCameraConfigurations();
                return;
            }

            RebuildOverlayCameras(mainCamera, sourceCameras);
        }

        private void RebuildOverlayCameras(Camera mainCamera)
        {
            List<Camera> sourceCameras = FindOverlaySourceCameras(mainCamera);
            RebuildOverlayCameras(mainCamera, sourceCameras);
        }

        private void RebuildOverlayCameras(Camera mainCamera, List<Camera> sourceCameras)
        {
            DestroyOverlayCameras();
            if (mainCamera == null || sourceCameras == null || sourceCameras.Count == 0) return;

            for (int i = 0; i < sourceCameras.Count; i++)
            {
                Camera source = sourceCameras[i];
                if (source == null) continue;

                int sourceId = source.GetInstanceID();

                GameObject leftOverlayGo = new($"OpenVR_Overlay_{i}_Left_{sourceId}");
                leftOverlayGo.transform.SetParent(_vrRig.transform, false);
                Camera leftOverlay = leftOverlayGo.AddComponent<Camera>();

                GameObject rightOverlayGo = new($"OpenVR_Overlay_{i}_Right_{sourceId}");
                rightOverlayGo.transform.SetParent(_vrRig.transform, false);
                Camera rightOverlay = rightOverlayGo.AddComponent<Camera>();

                ConfigureOverlayCameraClone(leftOverlay, source);
                ConfigureOverlayCameraClone(rightOverlay, source);
                SyncMinimalPostProcessingForEye(source, leftOverlay, _leftVrCamera);
                SyncMinimalPostProcessingForEye(source, rightOverlay, _rightVrCamera);

                _overlayCameraBindings.Add(new OverlayCameraBinding
                {
                    Source = source,
                    LeftEye = leftOverlay,
                    RightEye = rightOverlay
                });
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("[PostFX] Overlay camera sources: ");
            for (int i = 0; i < _overlayCameraBindings.Count; i++)
            {
                Camera source = _overlayCameraBindings[i].Source;
                if (source == null) continue;
                if (i > 0) sb.Append(", ");
                sb.Append(source.name);
                sb.Append("(depth=");
                sb.Append(source.depth.ToString("F1"));
                sb.Append(", clear=");
                sb.Append(source.clearFlags);
                sb.Append(", left=");
                sb.Append(_overlayCameraBindings[i].LeftEye != null ? _overlayCameraBindings[i].LeftEye.name : "null");
                sb.Append(", right=");
                sb.Append(_overlayCameraBindings[i].RightEye != null ? _overlayCameraBindings[i].RightEye.name : "null");
                sb.Append(')');
            }
            VRModCore.Log(sb.ToString());
        }

        private List<Camera> FindOverlaySourceCameras(Camera mainCamera)
        {
            List<Camera> result = new();
            if (mainCamera == null) return result;

            int uiLayer = LayerMask.NameToLayer("UI");
            Type nguiType = ResolveNguiUiCameraType();
            var scanLog = new System.Text.StringBuilder();
            scanLog.Append("[PostFX][OverlayScan] main=");
            scanLog.Append(mainCamera.name);
            scanLog.Append(" scene=");
            scanLog.Append(mainCamera.gameObject.scene.name);
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null)
                {
                    scanLog.Append(" | <null>:skip(null)");
                    continue;
                }

                string decisionReason;
                bool include = true;

                if (cam == mainCamera)
                {
                    include = false;
                    decisionReason = "main-camera";
                }
                else if (!cam.enabled)
                {
                    include = false;
                    decisionReason = "disabled";
                }
                else if (!cam.gameObject.activeInHierarchy)
                {
                    include = false;
                    decisionReason = "inactive";
                }
                else if (cam == _leftVrCamera || cam == _rightVrCamera)
                {
                    include = false;
                    decisionReason = "vr-eye-camera";
                }
                else if (cam.targetTexture != null)
                {
                    include = false;
                    decisionReason = $"targetTexture={cam.targetTexture.name}";
                }
                else if (cam.gameObject.scene != mainCamera.gameObject.scene)
                {
                    include = false;
                    decisionReason = $"different-scene={cam.gameObject.scene.name}";
                }
                else if (cam.name.StartsWith("OpenVR_", StringComparison.OrdinalIgnoreCase) || cam.name.StartsWith("UnityVRMod_", StringComparison.OrdinalIgnoreCase))
                {
                    include = false;
                    decisionReason = "mod-generated-name";
                }
                else if (nguiType != null && cam.GetComponent(nguiType) != null)
                {
                    include = false;
                    decisionReason = "ngui-uicamera";
                }
                else
                {
                    if (uiLayer >= 0)
                    {
                        int uiMask = 1 << uiLayer;
                        // cullingMask == 0 is often used by post-process/composite cameras.
                        // Do not treat it as UI-only; allow later HDR/OnRenderImage checks to decide.
                        bool isUiOnlyNonZeroMask = cam.cullingMask != 0 && (cam.cullingMask & ~uiMask) == 0;
                        if (isUiOnlyNonZeroMask)
                        {
                            include = false;
                            decisionReason = "ui-only-culling-mask(non-zero)";
                        }
                        else
                        {
                            decisionReason = string.Empty;
                        }
                    }
                    else
                    {
                        decisionReason = string.Empty;
                    }
                }

                bool hasOnRenderImageFx = false;
                bool isHdrNamed = false;
                if (include)
                {
                    hasOnRenderImageFx = CameraHasOnRenderImageBehaviour(cam);
                    isHdrNamed = cam.name.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hasOnRenderImageFx && !isHdrNamed)
                    {
                        include = false;
                        decisionReason = "no-onrenderimage-and-name-not-hdr";
                    }
                }

                scanLog.Append(" | ");
                scanLog.Append(cam.name);
                scanLog.Append(":");
                scanLog.Append(include ? "include" : "skip");
                scanLog.Append('(');
                scanLog.Append(include ? $"hdrName={isHdrNamed},onRenderImage={hasOnRenderImageFx}" : decisionReason);
                scanLog.Append(")");
                scanLog.Append(" depth=");
                scanLog.Append(cam.depth.ToString("F1"));
                scanLog.Append(" clear=");
                scanLog.Append(cam.clearFlags);
                scanLog.Append(" mask=");
                scanLog.Append(cam.cullingMask);
                scanLog.Append(" rt=");
                scanLog.Append(cam.targetTexture != null ? cam.targetTexture.name : "null");

                if (include)
                {
                    result.Add(cam);
                }
            }

            VRModCore.LogRuntimeDebug(scanLog.ToString());

            result.Sort(static (a, b) =>
            {
                int byDepth = a.depth.CompareTo(b.depth);
                if (byDepth != 0) return byDepth;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });
            return result;
        }

        private bool HasSameOverlaySources(List<Camera> sourceCameras)
        {
            if (sourceCameras == null) return _overlayCameraBindings.Count == 0;
            if (sourceCameras.Count != _overlayCameraBindings.Count) return false;

            for (int i = 0; i < sourceCameras.Count; i++)
            {
                if (_overlayCameraBindings[i].Source != sourceCameras[i]) return false;
            }

            return true;
        }

        private void UpdateOverlayCameraConfigurations()
        {
            for (int i = 0; i < _overlayCameraBindings.Count; i++)
            {
                OverlayCameraBinding binding = _overlayCameraBindings[i];
                Camera source = binding?.Source;
                if (source == null) continue;

                if (binding.LeftEye != null)
                {
                    ConfigureOverlayCameraClone(binding.LeftEye, source);
                }

                if (binding.RightEye != null)
                {
                    ConfigureOverlayCameraClone(binding.RightEye, source);
                }
            }
        }

        private void ConfigureOverlayCameraClone(Camera overlayCamera, Camera sourceCamera)
        {
            if (overlayCamera == null || sourceCamera == null) return;

            overlayCamera.stereoTargetEye = StereoTargetEyeMask.None;
            overlayCamera.enabled = false;

            overlayCamera.clearFlags = sourceCamera.clearFlags;
            overlayCamera.backgroundColor = sourceCamera.backgroundColor;
            overlayCamera.cullingMask = sourceCamera.cullingMask;
            overlayCamera.renderingPath = sourceCamera.renderingPath;
            overlayCamera.allowHDR = sourceCamera.allowHDR;
            overlayCamera.allowMSAA = sourceCamera.allowMSAA;
            overlayCamera.depthTextureMode = sourceCamera.depthTextureMode;
            overlayCamera.depth = sourceCamera.depth;

            float near = Mathf.Max(0.001f, sourceCamera.nearClipPlane * _currentAppliedRigScale);
            float far = Mathf.Max(near + 0.01f, sourceCamera.farClipPlane * _currentAppliedRigScale);
            overlayCamera.nearClipPlane = near;
            overlayCamera.farClipPlane = far;
        }

        private void RenderOverlayCamerasForEye(EVREye eye, RenderTexture targetTexture, Vector3 worldPos, Quaternion worldRot, Matrix4x4 projection)
        {
            if (_overlayCameraBindings.Count == 0 || targetTexture == null) return;

            for (int i = 0; i < _overlayCameraBindings.Count; i++)
            {
                OverlayCameraBinding binding = _overlayCameraBindings[i];
                Camera source = binding?.Source;
                Camera overlay = eye == EVREye.Eye_Left ? binding?.LeftEye : binding?.RightEye;
                if (source == null || overlay == null) continue;
                if (!source.enabled || !source.gameObject.activeInHierarchy) continue;

#if MONO
                overlay.transform.position = worldPos;
                overlay.transform.rotation = worldRot;
#elif CPP
                overlay.transform.SetPositionAndRotation(worldPos, worldRot);
#endif

                ConfigureOverlayCameraClone(overlay, source);
                overlay.projectionMatrix = projection;
                overlay.targetTexture = targetTexture;

                Camera eyeVrCamera = eye == EVREye.Eye_Left ? _leftVrCamera : _rightVrCamera;
                RebindCameraInstanceMembers(overlay, eyeVrCamera);

                bool originalInvertCulling = GL.invertCulling;
                GL.invertCulling = true;
                overlay.Render();
                GL.invertCulling = originalInvertCulling;

                overlay.targetTexture = null;
            }
        }

        private void DestroyOverlayCameras()
        {
            for (int i = 0; i < _overlayCameraBindings.Count; i++)
            {
                OverlayCameraBinding binding = _overlayCameraBindings[i];
                if (binding == null) continue;

                if (binding.LeftEye != null)
                {
                    ClearSyncedPostFxForCamera(binding.LeftEye);
                    UnityEngine.Object.Destroy(binding.LeftEye.gameObject);
                }

                if (binding.RightEye != null)
                {
                    ClearSyncedPostFxForCamera(binding.RightEye);
                    UnityEngine.Object.Destroy(binding.RightEye.gameObject);
                }
            }

            _overlayCameraBindings.Clear();
        }

        private Type ResolveNguiUiCameraType()
        {
            if (_nguiUiCameraTypeResolved) return _nguiUiCameraType;
            _nguiUiCameraTypeResolved = true;
            _nguiUiCameraType = ResolveTypeAnyAssembly("UICamera");
            return _nguiUiCameraType;
        }

        private static bool CameraHasOnRenderImageBehaviour(Camera camera)
        {
            if (camera == null) return false;
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            if (behaviours == null || behaviours.Length == 0) return false;

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                if (HasOnRenderImageCallback(behaviour.GetType())) return true;
            }
            return false;
        }

        private static Camera FindHdrEffectSourceCamera(Camera mainCamera)
        {
            if (mainCamera == null) return null;

            Camera best = null;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null) continue;
                if (camera == mainCamera) continue;
                if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
                if (camera.targetTexture != null) continue;
                if (camera.gameObject.scene != mainCamera.gameObject.scene) continue;
                if (camera.name.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            return best;
        }

        private static bool IsTargetHdrEffectType(Type sourceType)
        {
            if (sourceType == null) return false;

            string fullName = sourceType.FullName ?? string.Empty;
            if (fullName.IndexOf("UnityStandardAssets.ImageEffects.ScreenOverlay", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (fullName.IndexOf("BeautifyEffect.Beautify", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            string typeName = sourceType.Name ?? string.Empty;
            if (string.Equals(typeName, "ScreenOverlay", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(typeName, "Beautify", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private void SyncMinimalPostProcessing(Camera sourceCamera)
        {
            if (sourceCamera == null) return;

            if (ConfigManager.OpenVR_DisablePostFxSync?.Value == true)
            {
                VRModCore.Log("[PostFX][OpenVR] Post-processing sync disabled by config. Skipping.");
                return;
            }

            Camera hdrSourceCamera = FindHdrEffectSourceCamera(sourceCamera);
            Camera effectSource = hdrSourceCamera != null ? hdrSourceCamera : sourceCamera;
            int leftCount = SyncMinimalPostProcessingForEye(effectSource, _leftVrCamera, _leftVrCamera);
            int rightCount = SyncMinimalPostProcessingForEye(effectSource, _rightVrCamera, _rightVrCamera);

            VRModCore.LogRuntimeDebug(
                $"[PostFX] Synced HDR effects (ScreenOverlay/Beautify) from '{effectSource.name}': Left={leftCount}, Right={rightCount}, DepthMode={effectSource.depthTextureMode}.");
        }

        private int SyncMinimalPostProcessingForEye(Camera sourceCamera, Camera targetCamera, Camera instanceCamera)
        {
            if (sourceCamera == null || targetCamera == null) return 0;

            ClearSyncedPostFxForCamera(targetCamera);
            targetCamera.depthTextureMode = sourceCamera.depthTextureMode;

            MonoBehaviour[] sourceBehaviours = sourceCamera.GetComponents<MonoBehaviour>();
            if (sourceBehaviours == null || sourceBehaviours.Length == 0)
            {
                return 0;
            }

            List<Component> createdComponents = new();
            int copiedCount = 0;
            int remappedReferenceCount = 0;
            int remappedInstanceMemberCount = 0;
            Camera referenceCamera = instanceCamera != null ? instanceCamera : targetCamera;
            foreach (MonoBehaviour sourceBehaviour in sourceBehaviours)
            {
                if (sourceBehaviour == null) continue;

                Type sourceType = sourceBehaviour.GetType();
                if (!IsTargetHdrEffectType(sourceType)) continue;

                try
                {
                    Component created = targetCamera.gameObject.AddComponent(sourceType);
                    if (created == null) continue;

                    CopyComponentFields(sourceBehaviour, created);
                    remappedReferenceCount += RemapCopiedComponentReferences(created, sourceCamera, referenceCamera);
                    if (TrySetInstanceCameraMember(created, referenceCamera))
                    {
                        remappedInstanceMemberCount++;
                    }

                    if (created is Behaviour createdBehaviour && sourceBehaviour is Behaviour sourceAsBehaviour)
                    {
                        createdBehaviour.enabled = sourceAsBehaviour.enabled;
                    }

                    createdComponents.Add(created);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    VRModCore.LogRuntimeDebug($"[PostFX] Failed to clone '{sourceType.FullName}' on '{targetCamera.name}': {ex.Message}");
                }
            }

            if (createdComponents.Count > 0)
            {
                _syncedPostFxComponents[targetCamera] = createdComponents;
            }

            if (copiedCount > 0 && (remappedReferenceCount > 0 || remappedInstanceMemberCount > 0))
            {
                VRModCore.LogRuntimeDebug(
                    $"[PostFX] Remapped refs on '{targetCamera.name}' (source='{sourceCamera.name}', instanceCam='{(referenceCamera != null ? referenceCamera.name : "null")}'): refs={remappedReferenceCount}, instanceMembers={remappedInstanceMemberCount}.");
            }

            return copiedCount;
        }

        private void ClearSyncedPostFxForCamera(Camera targetCamera)
        {
            if (targetCamera == null) return;
            if (!_syncedPostFxComponents.TryGetValue(targetCamera, out List<Component> components)) return;

            foreach (Component component in components)
            {
                if (component != null) UnityEngine.Object.Destroy(component);
            }

            _syncedPostFxComponents.Remove(targetCamera);
        }

        private void ClearAllSyncedPostFxComponents()
        {
            foreach (var kvp in _syncedPostFxComponents)
            {
                List<Component> components = kvp.Value;
                if (components == null) continue;

                foreach (Component component in components)
                {
                    if (component != null) UnityEngine.Object.Destroy(component);
                }
            }

            _syncedPostFxComponents.Clear();
        }

        private static bool HasOnRenderImageCallback(Type behaviourType)
        {
            if (behaviourType == null || !typeof(MonoBehaviour).IsAssignableFrom(behaviourType)) return false;

            MethodInfo onRenderImage = behaviourType.GetMethod(
                "OnRenderImage",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [ typeof(RenderTexture), typeof(RenderTexture) ],
                null);

            return onRenderImage != null;
        }

        private static void CopyComponentFields(Component source, Component destination)
        {
            if (source == null || destination == null) return;

            Type sourceType = source.GetType();
            if (sourceType != destination.GetType()) return;

            for (Type t = sourceType; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component); t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.IsLiteral || field.IsInitOnly) continue;
                    try
                    {
                        object value = field.GetValue(source);
                        field.SetValue(destination, value);
                    }
                    catch
                    {
                        // Best effort clone; some fields may be non-settable at runtime.
                    }
                }
            }
        }

        private static int RemapCopiedComponentReferences(Component destination, Camera sourceCamera, Camera replacementCamera)
        {
            if (destination == null || sourceCamera == null || replacementCamera == null) return 0;

            int changedCount = 0;
            Type destinationType = destination.GetType();
            for (Type t = destinationType; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component); t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.IsLiteral || field.IsInitOnly) continue;

                    try
                    {
                        object currentValue = field.GetValue(destination);
                        object remappedValue = RemapReferenceValue(currentValue, sourceCamera, replacementCamera, field.FieldType);
                        if (ReferenceEquals(currentValue, remappedValue)) continue;
                        field.SetValue(destination, remappedValue);
                        changedCount++;
                    }
                    catch
                    {
                        // Best effort remap.
                    }
                }
            }

            return changedCount;
        }

        private static object RemapReferenceValue(object value, Camera sourceCamera, Camera replacementCamera, Type targetFieldType)
        {
            if (value == null || sourceCamera == null || replacementCamera == null) return value;

            if (value is Camera cameraValue && cameraValue == sourceCamera)
            {
                if (targetFieldType == null || targetFieldType.IsAssignableFrom(typeof(Camera)))
                {
                    return replacementCamera;
                }
                return value;
            }

            if (value is Transform transformValue && transformValue == sourceCamera.transform)
            {
                if (targetFieldType == null || targetFieldType.IsAssignableFrom(typeof(Transform)))
                {
                    return replacementCamera.transform;
                }
                return value;
            }

            if (value is GameObject gameObjectValue && gameObjectValue == sourceCamera.gameObject)
            {
                if (targetFieldType == null || targetFieldType.IsAssignableFrom(typeof(GameObject)))
                {
                    return replacementCamera.gameObject;
                }
                return value;
            }

            if (value is Component componentValue && componentValue.gameObject == sourceCamera.gameObject)
            {
                if (targetFieldType != null)
                {
                    if (targetFieldType.IsAssignableFrom(typeof(Camera)))
                    {
                        return replacementCamera;
                    }

                    if (targetFieldType.IsAssignableFrom(typeof(Transform)))
                    {
                        return replacementCamera.transform;
                    }
                }

                Component replacementComponent = replacementCamera.GetComponent(componentValue.GetType());
                if (replacementComponent != null)
                {
                    if (targetFieldType == null || targetFieldType.IsAssignableFrom(replacementComponent.GetType()))
                    {
                        return replacementComponent;
                    }
                }
            }

            return value;
        }

        private static int RebindCameraInstanceMembers(Camera hostCamera, Camera replacementCamera)
        {
            if (hostCamera == null || replacementCamera == null) return 0;

            int changedCount = 0;
            Component[] components = hostCamera.GetComponents<Component>();
            if (components == null || components.Length == 0) return 0;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                if (TrySetInstanceCameraMember(component, replacementCamera))
                {
                    changedCount++;
                }
            }

            return changedCount;
        }

        private static bool TrySetInstanceCameraMember(Component destination, Camera replacementCamera)
        {
            if (destination == null || replacementCamera == null) return false;

            bool changed = false;
            Type destinationType = destination.GetType();
            for (Type t = destinationType; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral) continue;
                    if (!string.Equals(field.Name, "instance", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        object remapped = BuildInstanceMemberValue(field.FieldType, replacementCamera);
                        if (remapped == null) continue;
                        field.SetValue(destination, remapped);
                        changed = true;
                    }
                    catch
                    {
                    }
                }

                PropertyInfo[] properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in properties)
                {
                    if (!property.CanWrite) continue;
                    if (!string.Equals(property.Name, "instance", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        object remapped = BuildInstanceMemberValue(property.PropertyType, replacementCamera);
                        if (remapped == null) continue;
                        property.SetValue(destination, remapped, null);
                        changed = true;
                    }
                    catch
                    {
                    }
                }
            }

            return changed;
        }

        private static object BuildInstanceMemberValue(Type memberType, Camera replacementCamera)
        {
            if (memberType == null || replacementCamera == null) return null;

            if (memberType.IsAssignableFrom(typeof(Camera)))
            {
                return replacementCamera;
            }

            if (memberType.IsAssignableFrom(typeof(Transform)))
            {
                return replacementCamera.transform;
            }

            if (memberType.IsAssignableFrom(typeof(GameObject)))
            {
                return replacementCamera.gameObject;
            }

            if (typeof(Component).IsAssignableFrom(memberType))
            {
                return replacementCamera.GetComponent(memberType);
            }

            return null;
        }

        private void TrySetBeautifyInstanceForEye(EVREye eye, Camera primaryCamera, Camera secondaryCamera)
        {
            Component beautifyComponent = FindBeautifyComponent(primaryCamera) ?? FindBeautifyComponent(secondaryCamera);
            if (beautifyComponent == null) return;

            bool assigned = TryAssignStaticInstance(beautifyComponent);
            if (!assigned && !_beautifyAssignFailureLogged)
            {
                _beautifyAssignFailureLogged = true;
                VRModCore.LogRuntimeDebug(
                    $"[PostFX][Beautify] Failed to assign static instance from '{beautifyComponent.GetType().FullName}' on '{beautifyComponent.gameObject.name}'.");
            }
        }

        private static Component FindBeautifyComponent(Camera camera)
        {
            if (camera == null) return null;

            Component[] components = camera.GetComponents<Component>();
            if (components == null || components.Length == 0) return null;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                Type type = component.GetType();
                string fullName = type.FullName ?? string.Empty;
                string typeName = type.Name ?? string.Empty;
                if (fullName.IndexOf("BeautifyEffect.Beautify", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(typeName, "Beautify", StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }

            return null;
        }

        private static bool TryAssignStaticInstance(Component component)
        {
            if (component == null) return false;

            Type componentType = component.GetType();
            bool changed = false;

            for (Type t = componentType; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (!string.Equals(field.Name, "instance", StringComparison.OrdinalIgnoreCase)) continue;
                    if (field.IsInitOnly || field.IsLiteral) continue;
                    if (!field.FieldType.IsAssignableFrom(componentType)) continue;

                    try
                    {
                        field.SetValue(null, component);
                        changed = true;
                    }
                    catch
                    {
                    }
                }

                PropertyInfo[] properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in properties)
                {
                    if (!string.Equals(property.Name, "instance", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!property.CanWrite) continue;
                    if (!property.PropertyType.IsAssignableFrom(componentType)) continue;

                    try
                    {
                        property.SetValue(null, component, null);
                        changed = true;
                    }
                    catch
                    {
                    }
                }
            }

            return changed;
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
    }
}
