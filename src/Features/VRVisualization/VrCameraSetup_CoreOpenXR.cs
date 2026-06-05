using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityVRMod.Features.VRVisualization.OpenXR;
using System.Text;

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrCameraSetup_CoreOpenXR : IVrCameraSetup
    {
        private ulong _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _xrSystemId = OpenXRConstants.XR_NULL_SYSTEM_ID;
        private ulong _xrSession = OpenXRConstants.XR_NULL_HANDLE;

        public bool IsVrAvailable { get; private set; } = false;

        private GameObject _vrRig = null;
        private float _currentAppliedRigScale = 1.0f;

        private XrFrameState _xrFrameState;
        private XrSessionState _currentSessionState = XrSessionState.XR_SESSION_STATE_UNKNOWN;
        private readonly XrViewConfigurationType _viewConfigType = XrViewConfigurationType.XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
        private readonly List<XrViewConfigurationView> _viewConfigViews = [];

        private readonly List<long> _supportedSwapchainFormats = [];
        private long _selectedSwapchainFormat = 0;
        private readonly List<ulong> _eyeSwapchains = [];
        private readonly List<List<IntPtr>> _eyeSwapchainImages = [];
        private ulong _appSpace = OpenXRConstants.XR_NULL_HANDLE;
        private bool _isSessionRunning = false;
        private XrReferenceSpaceType _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
        private XrView[] _locatedViews;
        private XrViewState _locatedViewState;

        private Camera _leftVrCamera = null;
        private GameObject _leftVrCameraGO = null;
        private Camera _rightVrCamera = null;
        private GameObject _rightVrCameraGO = null;
        private Camera _leftVrHdrEffectCamera = null;
        private Camera _rightVrHdrEffectCamera = null;
        private readonly Dictionary<Camera, List<Component>> _syncedPostFxComponents = [];
        private readonly HashSet<int> _renderOverrideEyeRetoggledCameraIds = [];

        private RenderTexture _leftEyeIntermediateRT = null;
        private RenderTexture _rightEyeIntermediateRT = null;

        private IntPtr _d3d11Device = IntPtr.Zero;

        private XrCompositionLayerProjectionView[] _projectionLayerViews;
        private IntPtr _pProjectionLayerViews = IntPtr.Zero;
        private XrCompositionLayerProjection _projectionLayer;
        private IntPtr _pProjectionLayer = IntPtr.Zero;
        private IntPtr _pLayersForSubmit = IntPtr.Zero;

        private CameraClearFlags _mainCameraClearFlags;
        private Color _mainCameraBackgroundColor;
        private int _mainCameraCullingMask;
        private bool _isUsing2dSyntheticFallbackCamera;
        private bool _isUsingForcedDefaultRenderState;
        private bool _isUsingForcedSolidClearState;
        private readonly HashSet<string> _forceDefaultRenderSceneNames = new(StringComparer.OrdinalIgnoreCase);
        private string _cachedForceDefaultRenderScenesRaw = string.Empty;
        private readonly HashSet<string> _forceSolidClearSceneNames = new(StringComparer.OrdinalIgnoreCase);
        private string _cachedForceSolidClearScenesRaw = string.Empty;
        private const int VrRigRenderLayer = 31;
        private static readonly Color VrRigClearColor = new(0.4623f, 0.4623f, 0.4623f, 0f);
        private static bool UseSingleSharedVrEyeCamera = true;
        private static bool EnableOpenXrPostFxSync = true;
        private const float OpenXrPerfLogIntervalSeconds = 1.0f;

        private GameObject _currentlyTrackedOriginalCameraGO = null;
        private float _lastCalculatedVerticalOffset;
        private static CommandBuffer _flushCommandBuffer;
        private float _lastUpdatePosesCpuMs;
        private float _lastLeftEyeRenderCpuMs;
        private float _lastRightEyeRenderCpuMs;
        private float _lastXrWaitFrameCpuMs;
        private float _lastXrLocateViewsCpuMs;
        private float _lastLocomotionCpuMs;
        private float _lastSwapchainWaitCpuMs;
        private float _lastXrEndFrameCpuMs;
        private int _lastPoseValidFlag;
        private float _nextOpenXrPerfLogTime;
        private bool _hasLoggedXrEndFrameFailure;

        private readonly List<List<IntPtr>> _eyeSwapchainSRVs = [];
        private readonly OpenXrRigLocomotion _locomotion = new();
        private readonly OpenXrControllerVisualizer _controllerVisualizer = new();
        private readonly OpenXrUiInteractor _uiInteractor = new();
        private readonly OpenXrUiProjectionPlane _uiProjectionPlane = new();
        private readonly OpenXrDanmenProjectionPlane _danmenProjectionPlane = new();
        private const float PlaneEditEdgeRingInnerOffsetNormalized = 0.02f;
        private const float PlaneEditEdgeRingOuterOffsetNormalized = 0.08f;
        private bool _wasPlaneEditTogglePressed;
        private bool _wasPlaneEditTriggerPressed;
        private bool _wasLeftTriggerPressed;
        private bool _wasRightTriggerPressed;
        private OpenXrControlHand? _uiClickHand;
        private OpenXrControlHand? _activeGripHand;
        private OpenXrControlHand? _lastActiveGripHand;
        private bool _wasXOrAPressed;
        private float _lastXaPressTime = -1f;
        private float _xaClickPendingTime = -1f;
        private bool _wasUiTogglePressed;
        private const float UiToggleDoubleClickWindow = 0.3f;
        private PlaneEditSelection _activePlaneEditSelection = PlaneEditSelection.None;
        private Vector3 _activePlaneEditHandLocalPos = Vector3.zero;
        private Quaternion _activePlaneEditHandLocalRot = Quaternion.identity;
        private PlaneEditMode _activePlaneEditMode = PlaneEditMode.None;
        private int _activePlaneEditResizeHandleIndex = -1;
        private float _activePlaneEditResizeStartHandDistance;
        private float _activePlaneEditResizeStartScale = 1f;
        private ulong _inputActionSet = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _triggerValueAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _gripValueAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _hapticAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _thumbstickAxisAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _thumbstickClickAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _aClickAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _bClickAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _xClickAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _yClickAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _leftAimPoseAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _leftAimSpace = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _rightAimPoseAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _rightAimSpace = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _leftGripPoseAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _leftGripSpace = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _rightGripPoseAction = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _rightGripSpace = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _leftHandPath = OpenXRConstants.XR_NULL_PATH;
        private ulong _rightHandPath = OpenXRConstants.XR_NULL_PATH;
        private OpenXrTriggerLogState _leftTriggerLogState;
        private OpenXrTriggerLogState _rightTriggerLogState;
        private OpenXrFloatLogState _leftGripLogState;
        private OpenXrFloatLogState _rightGripLogState;
        private OpenXrVector2LogState _leftThumbstickAxisLogState;
        private OpenXrVector2LogState _rightThumbstickAxisLogState;
        private OpenXrBooleanLogState _leftThumbstickClickLogState;
        private OpenXrBooleanLogState _rightThumbstickClickLogState;
        private OpenXrBooleanLogState _leftXLogState;
        private OpenXrBooleanLogState _leftYLogState;
        private OpenXrBooleanLogState _rightALogState;
        private OpenXrBooleanLogState _rightBLogState;
        private bool _inputSyncErrorLogged;
        private const float FloatLogDelta = 0.05f;
        private const float AxisLogDelta = 0.10f;
        private const float TriggerPressThreshold = 0.75f;
        private const float TriggerReleaseThreshold = 0.65f;
        private const float TeleportAimStickPressThreshold = 0.60f;
        private const float TeleportAimStickReleaseThreshold = 0.45f;
        private const float GripHoldThreshold = 0.65f;
        private const float UiTouchHapticAmplitude = 0.55f;
        private const long UiTouchHapticDurationNs = 45_000_000L;
        private bool _leftAimPoseErrorLogged;
        private bool _rightAimPoseErrorLogged;
        private bool _leftGripPoseErrorLogged;
        private bool _rightGripPoseErrorLogged;
        private bool _isTeleportAimingByStick;
        private Transform _mainCameraLookAtTarget;
        private float _nextLookAtTargetResolveTime;
        private const float LookAtTargetResolveIntervalSeconds = 1.0f;
        private const float SubCameraMoveModeToggleHoldSeconds = 0.45f;
        private const float SubCameraRotateModeHoldSeconds = 0.30f;
        private bool _isSubCameraMoveModeActive;
        private float _subCameraMoveAHoldSeconds;
        private bool _subCameraMoveAToggleHoldConsumed;
        private bool _wasSubCameraAButtonPressed;
        private bool _wasSubCameraBButtonPressed;
        private float _subCameraMoveStickClickHoldSeconds;

        private struct OpenXrBooleanLogState
        {
            public bool HasSample;
            public bool IsActive;
            public bool IsPressed;
        }

        private struct OpenXrFloatLogState
        {
            public bool HasSample;
            public bool IsActive;
            public float Value;
        }

        private struct OpenXrTriggerLogState
        {
            public bool HasSample;
            public bool IsActive;
            public float Value;
            public bool IsPressed;
        }

        private struct OpenXrVector2LogState
        {
            public bool HasSample;
            public bool IsActive;
            public float X;
            public float Y;
        }

        private enum PlaneEditSelection
        {
            None,
            UiProjectionPlane,
            DanmenProjectionPlane,
            SubCameraProjectionPlane
        }

        private enum PlaneEditMode
        {
            None,
            Drag,
            Resize
        }

        public bool InitializeVr(string applicationKey)
        {
            VRModCore.Log("Attempting to initialize OpenXR via P/Invoke...");

            try
            {
                if (!OpenXRNativeLoader.LoadOpenXRLibrary())
                    throw new Exception("Failed to load OpenXR native library (openxr_loader.dll).");

                if (!OpenXRAPI.InitializeCoreFunctions(OpenXRNativeLoader.xrGetInstanceProcAddr_ptr_delegate))
                    throw new Exception("Failed to initialize core OpenXR functions.");

                string[] requestedExtensions = [OpenXRConstants.XR_KHR_D3D11_ENABLE_EXTENSION_NAME];
                IntPtr pRequestedExtensions = MarshallStringUtils.MarshalStringArrayToAnsi(requestedExtensions);

                // Try API 1.1 first, fall back to 1.0 for runtimes like VDXR
                XrResult instanceResult;
                ulong[] apiVersionsToTry = [OpenXRConstants.XR_API_VERSION_1_1, OpenXRConstants.XR_API_VERSION_1_0];
                bool instanceCreated = false;
                foreach (ulong apiVer in apiVersionsToTry)
                {
                    var appInfo = new XrApplicationInfo
                    {
                        applicationName = "UnityVRMod",
                        applicationVersion = 1,
                        engineName = "Unity",
                        engineVersion = 1,
                        apiVersion = apiVer
                    };
                    var instanceCreateInfo = new XrInstanceCreateInfo
                    {
                        type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO,
                        applicationInfo = appInfo,
                        enabledExtensionCount = (uint)requestedExtensions.Length,
                        enabledExtensionNames = pRequestedExtensions
                    };
                    instanceResult = OpenXRAPI.xrCreateInstance(in instanceCreateInfo, out _xrInstance);
                    if (instanceResult >= 0 && _xrInstance != OpenXRConstants.XR_NULL_HANDLE)
                    {
                        instanceCreated = true;
                        VRModCore.Log($"OpenXR Instance created with API version 0x{apiVer:X}.");
                        break;
                    }
                    if (instanceResult != XrResult.XR_ERROR_API_VERSION_UNSUPPORTED)
                    {
                        OpenXRHelper.CheckResult(instanceResult, "xrCreateInstance");
                    }
                    VRModCore.LogWarning($"OpenXR runtime does not support API version 0x{apiVer:X}, trying next...");
                }
                MarshallStringUtils.FreeMarshalledStringArray(pRequestedExtensions, requestedExtensions.Length);
                if (!instanceCreated) throw new Exception("xrCreateInstance failed: no compatible API version found.");

                VRModCore.Log($"OpenXR Instance created. Handle: {_xrInstance}");

                if (!OpenXRAPI.InitializeInstanceFunctions(_xrInstance))
                    throw new Exception("Failed to initialize instance-specific OpenXR functions.");

                _d3d11Device = NativeBridge.GetD3D11DevicePointer(Texture2D.whiteTexture);
                if (_d3d11Device == IntPtr.Zero)
                    throw new Exception("Failed to get D3D11 device pointer.");

                NativeBridge.SetDevicePointerFromCSharp(_d3d11Device);

                var systemGetInfo = new XrSystemGetInfo { type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO, formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetSystem(_xrInstance, in systemGetInfo, out _xrSystemId), "xrGetSystem");
                if (_xrSystemId == OpenXRConstants.XR_NULL_SYSTEM_ID) throw new Exception("xrGetSystem failed for HMD.");
                VRModCore.Log($"OpenXR SystemId obtained: {_xrSystemId}");

                var d3d11GraphicsRequirements = new XrGraphicsRequirementsD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetD3D11GraphicsRequirementsKHR(_xrInstance, _xrSystemId, out d3d11GraphicsRequirements), "xrGetD3D11GraphicsRequirementsKHR");

                var graphicsBinding = new XrGraphicsBindingD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_D3D11_KHR, device = _d3d11Device };
                IntPtr pGraphicsBinding = Marshal.AllocHGlobal(Marshal.SizeOf(graphicsBinding));
                Marshal.StructureToPtr(graphicsBinding, pGraphicsBinding, false);
                var sessionCreateInfo = new XrSessionCreateInfo { type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO, next = pGraphicsBinding, systemId = _xrSystemId };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSession(_xrInstance, in sessionCreateInfo, out _xrSession), "xrCreateSession");
                Marshal.FreeHGlobal(pGraphicsBinding);
                if (_xrSession == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("xrCreateSession returned a null handle.");
                VRModCore.Log($"OpenXR Session created. Handle: {_xrSession}");

                InitializeInputActions();

                var sessionBeginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                OpenXRHelper.CheckResult(OpenXRAPI.xrBeginSession(_xrSession, in sessionBeginInfo), "xrBeginSession");
                _currentSessionState = XrSessionState.XR_SESSION_STATE_READY;
                _isSessionRunning = true;
                VRModCore.Log("OpenXR Session begun.");

                var refSpaceCreateInfo = new XrReferenceSpaceCreateInfo { type = XrStructureType.XR_TYPE_REFERENCE_SPACE_CREATE_INFO, referenceSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE, poseInReferenceSpace = new XrPosef { orientation = new XrQuaternionf { w = 1f } } };
                if (OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace) < 0)
                {
                    VRModCore.LogWarning("Failed to create STAGE space. Trying LOCAL.");
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
                    refSpaceCreateInfo.referenceSpaceType = _appSpaceType;
                    OpenXRHelper.CheckResult(OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace), "xrCreateReferenceSpace_Fallback");
                }
                else
                {
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE;
                }
                if (_appSpace == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("Failed to create any reference space.");
                VRModCore.Log($"Created {_appSpaceType} space.");

                InitializeViews();
                InitializeSwapchains();

                if (_viewConfigViews.Count > 0)
                {
                    _pProjectionLayerViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjectionView>() * _viewConfigViews.Count);
                    _projectionLayer = new XrCompositionLayerProjection { type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION };
                    _pProjectionLayer = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjection>());
                    _pLayersForSubmit = Marshal.AllocHGlobal(Marshal.SizeOf<IntPtr>());
                    Marshal.WriteIntPtr(_pLayersForSubmit, IntPtr.Zero);
                }

                _flushCommandBuffer ??= new CommandBuffer
                    {
                        name = "VRModFlush"
                    };

                VRModCore.Log("OpenXR fully initialized.");
                IsVrAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during OpenXR initialization:", ex);
                TeardownVr();
                return false;
            }
        }

        private void InitializeInputActions()
        {
            _leftHandPath = StringToPath("/user/hand/left");
            _rightHandPath = StringToPath("/user/hand/right");

            var actionSetCreateInfo = new XrActionSetCreateInfo
            {
                type = XrStructureType.XR_TYPE_ACTION_SET_CREATE_INFO,
                actionSetName = MarshallStringUtils.CreateFixedSizeUtf8NullTerminated("gameplay", OpenXRConstants.XR_MAX_ACTION_SET_NAME_SIZE),
                localizedActionSetName = MarshallStringUtils.CreateFixedSizeUtf8NullTerminated("Gameplay", OpenXRConstants.XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE),
                priority = 0
            };
            OpenXRHelper.CheckResult(OpenXRAPI.xrCreateActionSet(_xrInstance, in actionSetCreateInfo, out _inputActionSet), "xrCreateActionSet");

            IntPtr pSubactionPaths = IntPtr.Zero;
            IntPtr pLeftSubactionPath = IntPtr.Zero;
            IntPtr pRightSubactionPath = IntPtr.Zero;
            IntPtr pSuggestedBindings = IntPtr.Zero;
            IntPtr pActionSets = IntPtr.Zero;
            try
            {
                pSubactionPaths = Marshal.AllocHGlobal(sizeof(ulong) * 2);
                Marshal.WriteInt64(pSubactionPaths, 0, unchecked((long)_leftHandPath));
                Marshal.WriteInt64(pSubactionPaths, sizeof(ulong), unchecked((long)_rightHandPath));

                pLeftSubactionPath = Marshal.AllocHGlobal(sizeof(ulong));
                Marshal.WriteInt64(pLeftSubactionPath, 0, unchecked((long)_leftHandPath));

                pRightSubactionPath = Marshal.AllocHGlobal(sizeof(ulong));
                Marshal.WriteInt64(pRightSubactionPath, 0, unchecked((long)_rightHandPath));

                ulong CreateAction(string actionName, string localizedName, XrActionType actionType, IntPtr subactionPathsPtr, uint subactionCount)
                {
                    var actionCreateInfo = new XrActionCreateInfo
                    {
                        type = XrStructureType.XR_TYPE_ACTION_CREATE_INFO,
                        actionName = MarshallStringUtils.CreateFixedSizeUtf8NullTerminated(actionName, OpenXRConstants.XR_MAX_ACTION_NAME_SIZE),
                        actionType = actionType,
                        countSubactionPaths = subactionCount,
                        subactionPaths = subactionPathsPtr,
                        localizedActionName = MarshallStringUtils.CreateFixedSizeUtf8NullTerminated(localizedName, OpenXRConstants.XR_MAX_LOCALIZED_ACTION_NAME_SIZE)
                    };
                    OpenXRHelper.CheckResult(OpenXRAPI.xrCreateAction(_inputActionSet, in actionCreateInfo, out ulong actionHandle), $"xrCreateAction({actionName})");
                    return actionHandle;
                }

                _triggerValueAction = CreateAction("trigger_value", "Trigger Value", XrActionType.XR_ACTION_TYPE_FLOAT_INPUT, pSubactionPaths, 2);
                _gripValueAction = CreateAction("grip_value", "Grip Value", XrActionType.XR_ACTION_TYPE_FLOAT_INPUT, pSubactionPaths, 2);
                _hapticAction = CreateAction("haptic_output", "Haptic Output", XrActionType.XR_ACTION_TYPE_VIBRATION_OUTPUT, pSubactionPaths, 2);
                _thumbstickAxisAction = CreateAction("thumbstick_axis", "Thumbstick Axis", XrActionType.XR_ACTION_TYPE_VECTOR2F_INPUT, pSubactionPaths, 2);
                _thumbstickClickAction = CreateAction("thumbstick_click", "Thumbstick Click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT, pSubactionPaths, 2);
                _aClickAction = CreateAction("a_click", "A Click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT, pRightSubactionPath, 1);
                _bClickAction = CreateAction("b_click", "B Click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT, pRightSubactionPath, 1);
                _xClickAction = CreateAction("x_click", "X Click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT, pLeftSubactionPath, 1);
                _yClickAction = CreateAction("y_click", "Y Click", XrActionType.XR_ACTION_TYPE_BOOLEAN_INPUT, pLeftSubactionPath, 1);
                _leftAimPoseAction = CreateAction("left_aim_pose", "Left Aim Pose", XrActionType.XR_ACTION_TYPE_POSE_INPUT, pLeftSubactionPath, 1);
                _leftGripPoseAction = CreateAction("left_grip_pose", "Left Grip Pose", XrActionType.XR_ACTION_TYPE_POSE_INPUT, pLeftSubactionPath, 1);
                _rightAimPoseAction = CreateAction("right_aim_pose", "Right Aim Pose", XrActionType.XR_ACTION_TYPE_POSE_INPUT, pRightSubactionPath, 1);
                _rightGripPoseAction = CreateAction("right_grip_pose", "Right Grip Pose", XrActionType.XR_ACTION_TYPE_POSE_INPUT, pRightSubactionPath, 1);

                int bindingSize = Marshal.SizeOf<XrActionSuggestedBinding>();

                ulong touchControllerProfile = StringToPath("/interaction_profiles/oculus/touch_controller");
                ulong leftTriggerValue = StringToPath("/user/hand/left/input/trigger/value");
                ulong rightTriggerValue = StringToPath("/user/hand/right/input/trigger/value");
                ulong leftGripValue = StringToPath("/user/hand/left/input/squeeze/value");
                ulong rightGripValue = StringToPath("/user/hand/right/input/squeeze/value");
                ulong leftThumbstick = StringToPath("/user/hand/left/input/thumbstick");
                ulong rightThumbstick = StringToPath("/user/hand/right/input/thumbstick");
                ulong leftThumbstickClick = StringToPath("/user/hand/left/input/thumbstick/click");
                ulong rightThumbstickClick = StringToPath("/user/hand/right/input/thumbstick/click");
                ulong leftHaptic = StringToPath("/user/hand/left/output/haptic");
                ulong rightHaptic = StringToPath("/user/hand/right/output/haptic");
                ulong rightAClick = StringToPath("/user/hand/right/input/a/click");
                ulong rightBClick = StringToPath("/user/hand/right/input/b/click");
                ulong leftXClick = StringToPath("/user/hand/left/input/x/click");
                ulong leftYClick = StringToPath("/user/hand/left/input/y/click");
                ulong leftAimPose = StringToPath("/user/hand/left/input/aim/pose");
                ulong leftGripPose = StringToPath("/user/hand/left/input/grip/pose");
                ulong rightAimPose = StringToPath("/user/hand/right/input/aim/pose");
                ulong rightGripPose = StringToPath("/user/hand/right/input/grip/pose");

                XrActionSuggestedBinding[] touchBindings =
                [
                    new XrActionSuggestedBinding { action = _triggerValueAction, binding = leftTriggerValue },
                    new XrActionSuggestedBinding { action = _triggerValueAction, binding = rightTriggerValue },
                    new XrActionSuggestedBinding { action = _gripValueAction, binding = leftGripValue },
                    new XrActionSuggestedBinding { action = _gripValueAction, binding = rightGripValue },
                    new XrActionSuggestedBinding { action = _thumbstickAxisAction, binding = leftThumbstick },
                    new XrActionSuggestedBinding { action = _thumbstickAxisAction, binding = rightThumbstick },
                    new XrActionSuggestedBinding { action = _thumbstickClickAction, binding = leftThumbstickClick },
                    new XrActionSuggestedBinding { action = _thumbstickClickAction, binding = rightThumbstickClick },
                    new XrActionSuggestedBinding { action = _hapticAction, binding = leftHaptic },
                    new XrActionSuggestedBinding { action = _hapticAction, binding = rightHaptic },
                    new XrActionSuggestedBinding { action = _aClickAction, binding = rightAClick },
                    new XrActionSuggestedBinding { action = _bClickAction, binding = rightBClick },
                    new XrActionSuggestedBinding { action = _xClickAction, binding = leftXClick },
                    new XrActionSuggestedBinding { action = _yClickAction, binding = leftYClick },
                    new XrActionSuggestedBinding { action = _leftAimPoseAction, binding = leftAimPose },
                    new XrActionSuggestedBinding { action = _leftGripPoseAction, binding = leftGripPose },
                    new XrActionSuggestedBinding { action = _rightAimPoseAction, binding = rightAimPose },
                    new XrActionSuggestedBinding { action = _rightGripPoseAction, binding = rightGripPose }
                ];

                pSuggestedBindings = Marshal.AllocHGlobal(bindingSize * touchBindings.Length);
                for (int i = 0; i < touchBindings.Length; i++)
                {
                    Marshal.StructureToPtr(touchBindings[i], pSuggestedBindings + (i * bindingSize), false);
                }

                var touchSuggestedBindingInfo = new XrInteractionProfileSuggestedBinding
                {
                    type = XrStructureType.XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING,
                    interactionProfile = touchControllerProfile,
                    countSuggestedBindings = (uint)touchBindings.Length,
                    suggestedBindings = pSuggestedBindings
                };
                XrResult touchSuggestResult = OpenXRAPI.xrSuggestInteractionProfileBindings(_xrInstance, in touchSuggestedBindingInfo);
                if (touchSuggestResult < 0)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] Touch profile binding suggestion failed: {touchSuggestResult}");
                }
                else
                {
                    VRModCore.Log("[Input][OpenXR] Touch profile bindings suggested.");
                }

                pActionSets = Marshal.AllocHGlobal(sizeof(ulong));
                Marshal.WriteInt64(pActionSets, 0, unchecked((long)_inputActionSet));

                var attachInfo = new XrSessionActionSetsAttachInfo
                {
                    type = XrStructureType.XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO,
                    countActionSets = 1,
                    actionSets = pActionSets
                };
                OpenXRHelper.CheckResult(OpenXRAPI.xrAttachSessionActionSets(_xrSession, in attachInfo), "xrAttachSessionActionSets");

                var leftGripSpaceCreateInfo = new XrActionSpaceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_ACTION_SPACE_CREATE_INFO,
                    action = _leftGripPoseAction,
                    subactionPath = _leftHandPath,
                    poseInActionSpace = new XrPosef
                    {
                        orientation = new XrQuaternionf { w = 1f }
                    }
                };
                XrResult leftGripSpaceResult = OpenXRAPI.xrCreateActionSpace(_xrSession, in leftGripSpaceCreateInfo, out _leftGripSpace);
                if (leftGripSpaceResult < 0)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] Left grip space creation failed: {leftGripSpaceResult}");
                    _leftGripSpace = OpenXRConstants.XR_NULL_HANDLE;
                }

                var leftAimSpaceCreateInfo = new XrActionSpaceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_ACTION_SPACE_CREATE_INFO,
                    action = _leftAimPoseAction,
                    subactionPath = _leftHandPath,
                    poseInActionSpace = new XrPosef
                    {
                        orientation = new XrQuaternionf { w = 1f }
                    }
                };
                XrResult leftAimSpaceResult = OpenXRAPI.xrCreateActionSpace(_xrSession, in leftAimSpaceCreateInfo, out _leftAimSpace);
                if (leftAimSpaceResult < 0)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] Left aim space creation failed: {leftAimSpaceResult}");
                    _leftAimSpace = OpenXRConstants.XR_NULL_HANDLE;
                }

                var rightAimSpaceCreateInfo = new XrActionSpaceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_ACTION_SPACE_CREATE_INFO,
                    action = _rightAimPoseAction,
                    subactionPath = _rightHandPath,
                    poseInActionSpace = new XrPosef
                    {
                        orientation = new XrQuaternionf { w = 1f }
                    }
                };
                XrResult rightAimSpaceResult = OpenXRAPI.xrCreateActionSpace(_xrSession, in rightAimSpaceCreateInfo, out _rightAimSpace);
                if (rightAimSpaceResult < 0)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] Right aim space creation failed: {rightAimSpaceResult}");
                    _rightAimSpace = OpenXRConstants.XR_NULL_HANDLE;
                }

                var rightGripSpaceCreateInfo = new XrActionSpaceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_ACTION_SPACE_CREATE_INFO,
                    action = _rightGripPoseAction,
                    subactionPath = _rightHandPath,
                    poseInActionSpace = new XrPosef
                    {
                        orientation = new XrQuaternionf { w = 1f }
                    }
                };
                XrResult rightGripSpaceResult = OpenXRAPI.xrCreateActionSpace(_xrSession, in rightGripSpaceCreateInfo, out _rightGripSpace);
                if (rightGripSpaceResult < 0)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] Right grip space creation failed: {rightGripSpaceResult}");
                    _rightGripSpace = OpenXRConstants.XR_NULL_HANDLE;
                }
            }
            finally
            {
                if (pActionSets != IntPtr.Zero) Marshal.FreeHGlobal(pActionSets);
                if (pSuggestedBindings != IntPtr.Zero) Marshal.FreeHGlobal(pSuggestedBindings);
                if (pLeftSubactionPath != IntPtr.Zero) Marshal.FreeHGlobal(pLeftSubactionPath);
                if (pRightSubactionPath != IntPtr.Zero) Marshal.FreeHGlobal(pRightSubactionPath);
                if (pSubactionPaths != IntPtr.Zero) Marshal.FreeHGlobal(pSubactionPaths);
            }

            _leftTriggerLogState = default;
            _rightTriggerLogState = default;
            _leftGripLogState = default;
            _rightGripLogState = default;
            _leftThumbstickAxisLogState = default;
            _rightThumbstickAxisLogState = default;
            _leftThumbstickClickLogState = default;
            _rightThumbstickClickLogState = default;
            _leftXLogState = default;
            _leftYLogState = default;
            _rightALogState = default;
            _rightBLogState = default;
            _inputSyncErrorLogged = false;
            _leftAimPoseErrorLogged = false;
            _rightAimPoseErrorLogged = false;
            _leftGripPoseErrorLogged = false;
            _rightGripPoseErrorLogged = false;
            VRModCore.Log("OpenXR input action set initialized (trigger, grip, thumbstick, right A/B, left Y, left aim/grip pose, right aim/grip pose).");
        }

        private void LogEndFrameFailureOnce(XrResult endFrameResult, in XrFrameEndInfo frameEndInfo)
        {
            if (endFrameResult >= 0)
            {
                _hasLoggedXrEndFrameFailure = false;
                return;
            }

            if (_hasLoggedXrEndFrameFailure) return;
            _hasLoggedXrEndFrameFailure = true;
            VRModCore.LogWarning(
                $"[OpenXR] xrEndFrame failed: {endFrameResult} (layerCount={frameEndInfo.layerCount}, blend={frameEndInfo.environmentBlendMode})");
        }

        private ulong StringToPath(string path)
        {
            OpenXRHelper.CheckResult(OpenXRAPI.xrStringToPath(_xrInstance, path, out ulong xrPath), $"xrStringToPath({path})");
            return xrPath;
        }

        private void InitializeViews()
        {
            OpenXRAPI.xrEnumerateViewConfigurations(_xrInstance, _xrSystemId, 0, out uint viewConfigCountOutput, IntPtr.Zero);
            if (viewConfigCountOutput == 0) throw new Exception("No view configurations available.");

            OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, 0, out uint viewCountOutput, IntPtr.Zero);
            if (viewCountOutput == 0) throw new Exception("No views for primary stereo config.");

            _viewConfigViews.Clear();
            IntPtr pViewStructs = Marshal.AllocHGlobal((int)(viewCountOutput * Marshal.SizeOf<XrViewConfigurationView>()));
            try
            {
                for (int i = 0; i < viewCountOutput; ++i) Marshal.StructureToPtr(new XrViewConfigurationView { type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_VIEW }, pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>()), false);
                OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, viewCountOutput, out viewCountOutput, pViewStructs);
                for (int i = 0; i < viewCountOutput; ++i) _viewConfigViews.Add(Marshal.PtrToStructure<XrViewConfigurationView>(pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>())));
            }
            finally { Marshal.FreeHGlobal(pViewStructs); }

            if (_viewConfigViews.Count > 0)
            {
                _locatedViews = new XrView[_viewConfigViews.Count];
                for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i].type = XrStructureType.XR_TYPE_VIEW;

                _projectionLayerViews = new XrCompositionLayerProjectionView[_viewConfigViews.Count];
                for (int i = 0; i < _projectionLayerViews.Length; i++) _projectionLayerViews[i].type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW;
            }
            VRModCore.LogRuntimeDebug("View configurations enumerated.");
        }

        private void InitializeSwapchains()
        {
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, 0, out uint formatCount, null);
            if (formatCount == 0) throw new Exception("No swapchain formats.");
            long[] formatsArray = new long[formatCount];
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, formatCount, out formatCount, formatsArray);
            _supportedSwapchainFormats.Clear();
            _supportedSwapchainFormats.AddRange(formatsArray);
            long DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91;
            if (_supportedSwapchainFormats.Contains(DXGI_FORMAT_B8G8R8A8_UNORM_SRGB))
                _selectedSwapchainFormat = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
            else if (_supportedSwapchainFormats.Count > 0)
                _selectedSwapchainFormat = _supportedSwapchainFormats[0];
            else
                throw new Exception("No suitable swapchain format found.");

            VRModCore.Log($"Selected Swapchain Format (DXGI): {_selectedSwapchainFormat}");

            _eyeSwapchains.Clear();
            _eyeSwapchainImages.Clear();
            _eyeSwapchainSRVs.Clear();

            for (int i = 0; i < _viewConfigViews.Count; i++)
            {
                var view = _viewConfigViews[i];
                var swapchainCreateInfo = new XrSwapchainCreateInfo
                {
                    type = XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO,
                    usageFlags = XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
                    format = _selectedSwapchainFormat,
                    sampleCount = view.recommendedSwapchainSampleCount,
                    width = view.recommendedImageRectWidth,
                    height = view.recommendedImageRectHeight,
                    faceCount = 1,
                    arraySize = 1,
                    mipCount = 1
                };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSwapchain(_xrSession, in swapchainCreateInfo, out ulong scHandle), "xrCreateSwapchain");
                _eyeSwapchains.Add(scHandle);

                OpenXRAPI.xrEnumerateSwapchainImages(scHandle, 0, out uint imgCount, IntPtr.Zero);
                IntPtr scImagesPtr = Marshal.AllocHGlobal((int)(imgCount * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                List<IntPtr> currentEyeTexList = [];
                List<IntPtr> currentEyeSrvList = [];
                try
                {
                    for (int j = 0; j < imgCount; ++j) Marshal.StructureToPtr(new XrSwapchainImageD3D11KHR { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR }, scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()), false);
                    OpenXRAPI.xrEnumerateSwapchainImages(scHandle, imgCount, out imgCount, scImagesPtr);
                    for (int j = 0; j < imgCount; j++)
                    {
                        var swapchainImage = Marshal.PtrToStructure<XrSwapchainImageD3D11KHR>(scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                        currentEyeTexList.Add(swapchainImage.texture);
                        int hResultSrv = NativeBridge.CreateAndRegisterSRV_Internal(swapchainImage.texture, (int)_selectedSwapchainFormat, out IntPtr srvPtr);
                        if (hResultSrv == 0 && srvPtr != IntPtr.Zero)
                            currentEyeSrvList.Add(srvPtr);
                        else
                            currentEyeSrvList.Add(IntPtr.Zero);
                    }
                }
                finally { Marshal.FreeHGlobal(scImagesPtr); }

                _eyeSwapchainImages.Add(currentEyeTexList);
                _eyeSwapchainSRVs.Add(currentEyeSrvList);
                VRModCore.Log($"Swapchain for view {i} created. Images: {currentEyeTexList.Count}");
            }
        }

        public void UpdatePoses()
        {
            if (!IsVrAvailable || _xrSession == OpenXRConstants.XR_NULL_HANDLE) return;
            float updatePosesStartTime = Time.realtimeSinceStartup;
            _lastXrLocateViewsCpuMs = 0f;
            _lastLocomotionCpuMs = 0f;
            _lastSwapchainWaitCpuMs = 0f;
            _lastXrEndFrameCpuMs = 0f;
            _lastPoseValidFlag = 0;
            _lastLeftEyeRenderCpuMs = 0f;
            _lastRightEyeRenderCpuMs = 0f;

            // --- RESTORED: Full event polling and session state management ---
            IntPtr eventBufferPtr = IntPtr.Zero;
            try
            {
                int eventBufferSize = XrEventDataBuffer.GetSize();
                eventBufferPtr = Marshal.AllocHGlobal(eventBufferSize);
                while (true)
                {
                    var headerForInput = new XrEventDataBaseHeader { type = XrStructureType.XR_TYPE_EVENT_DATA_BUFFER };
                    Marshal.StructureToPtr(headerForInput, eventBufferPtr, false);
                    XrResult pollResult = OpenXRAPI.xrPollEvent(_xrInstance, eventBufferPtr);
                    if (pollResult == XrResult.XR_EVENT_UNAVAILABLE) break;

                    if (pollResult < 0) { IsVrAvailable = false; break; }

                    var actualEventType = (XrStructureType)Marshal.ReadInt32(eventBufferPtr);
                    if (actualEventType == XrStructureType.XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
                    {
                        var stateEvent = Marshal.PtrToStructure<XrEventDataSessionStateChanged>(eventBufferPtr);
                        if (stateEvent.state != _currentSessionState)
                        {
                            VRModCore.Log($"OpenXR Session State Changed: {_currentSessionState} -> {stateEvent.state}");
                            _currentSessionState = stateEvent.state;

                            switch (_currentSessionState)
                            {
                                case XrSessionState.XR_SESSION_STATE_EXITING:
                                case XrSessionState.XR_SESSION_STATE_LOSS_PENDING:
                                    _isSessionRunning = false;
                                    IsVrAvailable = false;
                                    break;
                                case XrSessionState.XR_SESSION_STATE_STOPPING:
                                    if (_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is stopping, calling xrEndSession.");
                                        OpenXRAPI.xrEndSession(_xrSession);
                                        _isSessionRunning = false;
                                    }
                                    break;
                                case XrSessionState.XR_SESSION_STATE_READY:
                                    if (!_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is ready, calling xrBeginSession to resume.");
                                        var beginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                                        if (OpenXRAPI.xrBeginSession(_xrSession, in beginInfo) == XrResult.XR_SUCCESS)
                                        {
                                            _isSessionRunning = true;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            finally { if (eventBufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(eventBufferPtr); }

            if (!_isSessionRunning) return;

            UpdateInputStates();

            var frameWaitInfo = new XrFrameWaitInfo { type = XrStructureType.XR_TYPE_FRAME_WAIT_INFO };
            _xrFrameState.type = XrStructureType.XR_TYPE_FRAME_STATE;
            float waitFrameStartTime = Time.realtimeSinceStartup;
            OpenXRAPI.xrWaitFrame(_xrSession, in frameWaitInfo, out _xrFrameState);
            _lastXrWaitFrameCpuMs = (Time.realtimeSinceStartup - waitFrameStartTime) * 1000f;

            var frameBeginInfo = new XrFrameBeginInfo { type = XrStructureType.XR_TYPE_FRAME_BEGIN_INFO };
            if (OpenXRAPI.xrBeginFrame(_xrSession, in frameBeginInfo) == XrResult.XR_FRAME_DISCARDED)
            {
                var discardedFrameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, layerCount = 0, layers = IntPtr.Zero, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE };
                float discardedEndFrameStartTime = Time.realtimeSinceStartup;
                XrResult discardedEndFrameResult = OpenXRAPI.xrEndFrame(_xrSession, in discardedFrameEndInfo);
                LogEndFrameFailureOnce(discardedEndFrameResult, in discardedFrameEndInfo);
                _lastXrEndFrameCpuMs = (Time.realtimeSinceStartup - discardedEndFrameStartTime) * 1000f;
                _lastUpdatePosesCpuMs = (Time.realtimeSinceStartup - updatePosesStartTime) * 1000f;
                LogOpenXrPerfIfNeeded();
                return;
            }

            var frameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE, layerCount = 0, layers = IntPtr.Zero };
            bool locomotionUpdated = false;
            bool submitProjectionLayer = false;

            if (_xrFrameState.shouldRender == XrBool32.XR_TRUE)
            {
                var viewLocateInfo = new XrViewLocateInfo { type = XrStructureType.XR_TYPE_VIEW_LOCATE_INFO, viewConfigurationType = _viewConfigType, displayTime = _xrFrameState.predictedDisplayTime, space = _appSpace };
                _locatedViewState.type = XrStructureType.XR_TYPE_VIEW_STATE;

                IntPtr pLocatedViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrView>() * _locatedViews.Length);
                float locateViewsStartTime = Time.realtimeSinceStartup;
                try
                {
                    for (int i = 0; i < _locatedViews.Length; i++) Marshal.StructureToPtr(_locatedViews[i], pLocatedViews + (i * Marshal.SizeOf<XrView>()), false);
                    OpenXRAPI.xrLocateViews(_xrSession, in viewLocateInfo, out _locatedViewState, (uint)_locatedViews.Length, out _, pLocatedViews);
                    for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i] = Marshal.PtrToStructure<XrView>(pLocatedViews + (i * Marshal.SizeOf<XrView>()));
                }
                finally { Marshal.FreeHGlobal(pLocatedViews); }
                _lastXrLocateViewsCpuMs = (Time.realtimeSinceStartup - locateViewsStartTime) * 1000f;

                bool poseIsValid = (_locatedViewState.viewStateFlags & XrViewStateFlags.XR_VIEW_STATE_ORIENTATION_VALID_BIT) != 0;
                _lastPoseValidFlag = poseIsValid ? 1 : 0;
                float locomotionStartTime = Time.realtimeSinceStartup;
                UpdateLocomotion(poseIsValid);
                _lastLocomotionCpuMs = (Time.realtimeSinceStartup - locomotionStartTime) * 1000f;
                locomotionUpdated = true;

                if (poseIsValid)
                {
                    float swapchainWaitStartTime = Time.realtimeSinceStartup;
                    var acquireInfo = new XrSwapchainImageAcquireInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
                    OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[0], in acquireInfo, out uint acquiredIdx0);
                    OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[1], in acquireInfo, out uint acquiredIdx1);

                    var waitInfo = new XrSwapchainImageWaitInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, timeout = OpenXRConstants.XR_INFINITE_DURATION };
                    OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[0], in waitInfo);
                    OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[1], in waitInfo);
                    _lastSwapchainWaitCpuMs = (Time.realtimeSinceStartup - swapchainWaitStartTime) * 1000f;

                    RenderEye(0, acquiredIdx0);
                    RenderEye(1, acquiredIdx1);

                    var releaseInfo = new XrSwapchainImageReleaseInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
                    OpenXRAPI.xrReleaseSwapchainImage(_eyeSwapchains[0], in releaseInfo);
                    OpenXRAPI.xrReleaseSwapchainImage(_eyeSwapchains[1], in releaseInfo);

                    PopulateProjectionLayer();
                    submitProjectionLayer = true;
                }
            }

            if (!locomotionUpdated)
            {
                float locomotionStartTime = Time.realtimeSinceStartup;
                UpdateLocomotion(false);
                _lastLocomotionCpuMs = (Time.realtimeSinceStartup - locomotionStartTime) * 1000f;
            }

            if (submitProjectionLayer && _pLayersForSubmit != IntPtr.Zero && _pProjectionLayer != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(_pLayersForSubmit, _pProjectionLayer);
                frameEndInfo.layerCount = 1;
                frameEndInfo.layers = _pLayersForSubmit;
            }

            float endFrameStartTime = Time.realtimeSinceStartup;
            XrResult endFrameResult = OpenXRAPI.xrEndFrame(_xrSession, in frameEndInfo);
            LogEndFrameFailureOnce(endFrameResult, in frameEndInfo);
            _lastXrEndFrameCpuMs = (Time.realtimeSinceStartup - endFrameStartTime) * 1000f;
            _lastUpdatePosesCpuMs = (Time.realtimeSinceStartup - updatePosesStartTime) * 1000f;
            LogOpenXrPerfIfNeeded();
        }

        private void RenderEye(int eyeIndex, uint swapchainImageIndex)
        {
            float renderEyeStartTime = Time.realtimeSinceStartup;
            Camera currentEyeCamera = (eyeIndex == 0) ? _leftVrCamera : _rightVrCamera;
            RenderTexture currentIntermediateRT = (eyeIndex == 0) ? _leftEyeIntermediateRT : _rightEyeIntermediateRT;
            IntPtr nativeTextureResourcePtr = _eyeSwapchainImages[eyeIndex][(int)swapchainImageIndex];
            XrViewConfigurationView viewConfig = _viewConfigViews[eyeIndex];

            bool rtNeedsRecreation = (currentIntermediateRT == null || !currentIntermediateRT.IsCreated() || currentIntermediateRT.width != (int)viewConfig.recommendedImageRectWidth || currentIntermediateRT.height != (int)viewConfig.recommendedImageRectHeight);
            if (rtNeedsRecreation)
            {
                if (currentIntermediateRT != null) { currentIntermediateRT.Release(); UnityEngine.Object.Destroy(currentIntermediateRT); }

                var renderTextureFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB
                    : UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_UNorm;

                VRModCore.LogSpammyDebug($"Creating intermediate RenderTexture with format: {renderTextureFormat}");
                currentIntermediateRT = new RenderTexture((int)viewConfig.recommendedImageRectWidth, (int)viewConfig.recommendedImageRectHeight, 24, renderTextureFormat);

                if (eyeIndex == 0) _leftEyeIntermediateRT = currentIntermediateRT; else _rightEyeIntermediateRT = currentIntermediateRT;
            }

            currentEyeCamera.targetTexture = currentIntermediateRT;
            ApplyVrCameraRenderState(currentEyeCamera);
            // Some legacy image-effects initialize/use camera callbacks only when the camera is enabled.
            // Keep it enabled only during manual Render() and disable immediately after to avoid auto rendering.
            currentEyeCamera.enabled = true;
            EnsureRenderOverrideEyeRetoggled(currentEyeCamera);

            XrPosef eyePose = _locatedViews[eyeIndex].pose;
            Vector3 position = ToUnityLocalPosition(eyePose);
            Quaternion rotation = ToUnityLocalRotation(eyePose);
#if MONO
            currentEyeCamera.transform.localPosition = position;
            currentEyeCamera.transform.localRotation = rotation;
#elif CPP
            currentEyeCamera.transform.SetLocalPositionAndRotation(position, rotation);
#endif

            Matrix4x4 projM = CreateProjectionMatrixFromFovUsingFrustum(_locatedViews[eyeIndex].fov, currentEyeCamera.nearClipPlane, currentEyeCamera.farClipPlane);
            projM = Matrix4x4.Scale(new Vector3(1, -1, 1)) * projM;
            currentEyeCamera.projectionMatrix = projM;

            bool originalInvertCulling = GL.invertCulling;
            GL.invertCulling = true;
            currentEyeCamera.Render();
            if (EnableOpenXrPostFxSync && !(ConfigManager.OpenXR_DisablePostFxSync?.Value == true))
            {
                RenderHdrEffectCameraForEye(eyeIndex, currentEyeCamera, currentIntermediateRT);
            }
            currentEyeCamera.enabled = false;
            GL.invertCulling = originalInvertCulling;
            Graphics.ExecuteCommandBuffer(_flushCommandBuffer);
            RenderTexture.active = null;

            if (currentIntermediateRT != null && currentIntermediateRT.IsCreated())
            {
                IntPtr sourceNativePtr = currentIntermediateRT.GetNativeTexturePtr();
                if (sourceNativePtr != IntPtr.Zero && nativeTextureResourcePtr != IntPtr.Zero)
                {
                    NativeBridge.DirectCopyResource_Internal(nativeTextureResourcePtr, sourceNativePtr);
                }
            }

            float renderEyeCpuMs = (Time.realtimeSinceStartup - renderEyeStartTime) * 1000f;
            if (eyeIndex == 0)
            {
                _lastLeftEyeRenderCpuMs = renderEyeCpuMs;
            }
            else
            {
                _lastRightEyeRenderCpuMs = renderEyeCpuMs;
            }
        }

        private void LogOpenXrPerfIfNeeded()
        {
            if (!(ConfigManager.OpenXR_EnablePerfLogging?.Value ?? false)) return;
            if (Time.unscaledTime < _nextOpenXrPerfLogTime) return;

            _nextOpenXrPerfLogTime = Time.unscaledTime + OpenXrPerfLogIntervalSeconds;
            float appFrameCpuMs = Time.unscaledDeltaTime * 1000f;
            VRModCore.Log(
                $"[Perf][OpenXR] AppFrame={appFrameCpuMs:F1}ms Update={_lastUpdatePosesCpuMs:F1}ms WaitFrame={_lastXrWaitFrameCpuMs:F1}ms LocateViews={_lastXrLocateViewsCpuMs:F1}ms Loco={_lastLocomotionCpuMs:F1}ms SwapWait={_lastSwapchainWaitCpuMs:F1}ms EyeL={_lastLeftEyeRenderCpuMs:F1}ms EyeR={_lastRightEyeRenderCpuMs:F1}ms EndFrame={_lastXrEndFrameCpuMs:F1}ms PoseValid={_lastPoseValidFlag} SessionRunning={(_isSessionRunning ? 1 : 0)} ShouldRender={(_xrFrameState.shouldRender == XrBool32.XR_TRUE ? 1 : 0)}");
        }

        private void UpdateLocomotion(bool hasValidViewPose)
        {
            if (_vrRig == null) return;

            Camera currentMainCamera = GetTrackedMainCamera();
            OpenXrControlHand activeControlHand = GetActiveControlHand();
            bool useLeftControlHand = activeControlHand == OpenXrControlHand.Left;
            bool useLeftPanelHand = (ConfigManager.OpenXR_UiPanelFollowHand?.Value ?? OpenXrControlHand.Left) == OpenXrControlHand.Left;
            // 传送和转向始终使用右手摇杆，不受控制手配置影响
            OpenXrVector2LogState turnThumbstickState = _rightThumbstickAxisLogState;
            OpenXrBooleanLogState turnThumbstickClickState = _rightThumbstickClickLogState;
            OpenXrFloatLogState activeGripState = useLeftControlHand ? _leftGripLogState : _rightGripLogState;
            bool leftTriggerPressed = IsTriggerPressed(_leftTriggerLogState);
            bool rightTriggerPressed = IsTriggerPressed(_rightTriggerLogState);
            bool leftGripPressed = GetFloatActionValue(_leftGripLogState) >= GripHoldThreshold;
            bool rightGripPressed = GetFloatActionValue(_rightGripLogState) >= GripHoldThreshold;
            float turnStickX = GetThumbstickX(turnThumbstickState);
            float turnStickY = GetThumbstickY(turnThumbstickState);
            bool isSmoothTurnHeld = IsBooleanActionPressed(turnThumbstickClickState);
            float activeGripValue = GetFloatActionValue(activeGripState);
            bool isGripHeld = activeGripValue >= GripHoldThreshold || leftGripPressed || rightGripPressed;
            // Y(左)/B(右) 单击：跟随/锚定切换
            bool yOrBPressed = IsBooleanActionPressed(_leftYLogState) || IsBooleanActionPressed(_rightBLogState);
            bool isUiTogglePressed = yOrBPressed && !_wasUiTogglePressed;
            _wasUiTogglePressed = yOrBPressed;

            bool teleportConfirmPressed = rightTriggerPressed;
            float leftStickX = GetThumbstickX(_leftThumbstickAxisLogState);
            float leftStickY = GetThumbstickY(_leftThumbstickAxisLogState);
            float rightStickX = GetThumbstickX(_rightThumbstickAxisLogState);
            float rightStickY = GetThumbstickY(_rightThumbstickAxisLogState);
            bool rightStickClickPressed = IsBooleanActionPressed(_rightThumbstickClickLogState);
            bool leftXPressed = IsBooleanActionPressed(_leftXLogState);
            bool rightAPressed = IsBooleanActionPressed(_rightALogState);
            bool rightBPressed = IsBooleanActionPressed(_rightBLogState);

            UpdateSubCameraMoveModeState(
                rightAPressed,
                rightBPressed,
                rightStickX,
                rightStickY,
                rightStickClickPressed,
                out bool subCameraMoveModeActive,
                out bool subCameraRotateModeActive,
                out float subCameraMoveAxisX,
                out float subCameraMoveAxisY,
                out bool subCameraHeightUpStep,
                out bool subCameraHeightDownStep);

#if MONO
            CameraTypesVrFollowPatch.SetSubCameraMoveInput(
                subCameraMoveModeActive,
                subCameraRotateModeActive,
                subCameraMoveAxisX,
                subCameraMoveAxisY,
                subCameraHeightUpStep,
                subCameraHeightDownStep);
#endif

            if (subCameraMoveModeActive)
            {
                turnStickX = 0f;
                turnStickY = 0f;
                isSmoothTurnHeld = false;
                isUiTogglePressed = false;
                teleportConfirmPressed = false;
            }

            // X(左)/A(右) 双击切换面板跟随手，单击切换面板显隐
            bool xOrAPressed = leftXPressed || rightAPressed;
            bool xOrAEdge = xOrAPressed && !_wasXOrAPressed;
            if (xOrAEdge)
            {
                float sinceLast = (_lastXaPressTime > 0f) ? Time.time - _lastXaPressTime : float.MaxValue;
                if (sinceLast < UiToggleDoubleClickWindow)
                {
                    // 双击：切换面板跟随手
                    var current = ConfigManager.OpenXR_UiPanelFollowHand?.Value ?? OpenXrControlHand.Left;
                    var next = current == OpenXrControlHand.Left ? OpenXrControlHand.Right : OpenXrControlHand.Left;
                    VRModCore.Log($"[UI][OpenXR] Double-click X/A: switching panel follow hand to {next}");
                    ConfigManager.OpenXR_UiPanelFollowHand!.Value = next;
                    _xaClickPendingTime = -1f;
                    _lastXaPressTime = -1f;
                }
                else
                {
                    _xaClickPendingTime = Time.time;
                    _lastXaPressTime = Time.time;
                }
            }
            // 窗口过期，确认是单击 → 切换显隐
            if (_xaClickPendingTime > 0f && Time.time - _xaClickPendingTime >= UiToggleDoubleClickWindow)
            {
                bool panelVisible = _uiProjectionPlane.ToggleVisibility();
                _uiInteractor.SetUIRayVisible(panelVisible);
                _xaClickPendingTime = -1f;
            }
            _wasXOrAPressed = xOrAPressed;

            bool isTeleportAiming = UpdateTeleportAimStateFromStick(turnStickY);

            bool hasGripLocalPose = false;
            Vector3 activeGripLocalPos = default;
            bool hasLeftHandWorldPose = false;
            Vector3 leftHandWorldPos = default;
            Quaternion leftHandWorldRot = Quaternion.identity;
            bool hasRightHandWorldPose = false;
            Vector3 rightHandWorldPos = default;
            Quaternion rightHandWorldRot = Quaternion.identity;
            if (hasValidViewPose)
            {
                bool hasLeftGripLP = TryGetLeftGripPoseLocalPosition(_xrFrameState.predictedDisplayTime, out Vector3 leftGripLP);
                bool hasRightGripLP = TryGetRightGripPoseLocalPosition(_xrFrameState.predictedDisplayTime, out Vector3 rightGripLP);

                // 锁定到当前按下的手
                if (leftGripPressed)
                    _activeGripHand = OpenXrControlHand.Left;
                else if (rightGripPressed)
                    _activeGripHand = OpenXrControlHand.Right;
                bool useLeftForGrip = _activeGripHand.HasValue
                    ? _activeGripHand.Value == OpenXrControlHand.Left
                    : useLeftControlHand;
                bool gripHandChanged = _activeGripHand != _lastActiveGripHand;
                hasGripLocalPose = gripHandChanged ? false : (useLeftForGrip ? hasLeftGripLP : hasRightGripLP);
                activeGripLocalPos = useLeftForGrip ? leftGripLP : rightGripLP;
                _lastActiveGripHand = _activeGripHand;

                hasLeftHandWorldPose = TryGetLeftGripPoseWorldTransform(_xrFrameState.predictedDisplayTime, out leftHandWorldPos, out leftHandWorldRot);
                hasRightHandWorldPose = TryGetRightGripPoseWorldTransform(_xrFrameState.predictedDisplayTime, out rightHandWorldPos, out rightHandWorldRot);
            }

            bool hasActiveHandWorldPose = useLeftControlHand ? hasLeftHandWorldPose : hasRightHandWorldPose;
            Vector3 activeHandWorldPos = useLeftControlHand ? leftHandWorldPos : rightHandWorldPos;
            Quaternion activeHandWorldRot = useLeftControlHand ? leftHandWorldRot : rightHandWorldRot;

            bool hasLeftPointerPose = false;
            Vector3 leftPointerOrigin = default;
            Vector3 leftPointerDirection = default;
            bool hasRightPointerPose = false;
            Vector3 rightPointerOrigin = default;
            Vector3 rightPointerDirection = default;
            bool hasPointerPose = false;
            Vector3 pointerOriginWorld = default;
            Vector3 pointerDirectionWorld = default;
            bool hasLeftAimWorldPose = false;
            Vector3 leftAimWorldPos = default;
            Quaternion leftAimWorldRot = Quaternion.identity;
            bool hasRightAimWorldPose = false;
            Vector3 rightAimWorldPos = default;
            Quaternion rightAimWorldRot = Quaternion.identity;
            bool useLeftForUi = useLeftControlHand;
            if (hasValidViewPose)
            {
                // 始终计算左手指针
                hasLeftAimWorldPose = TryGetLeftAimPoseWorldTransform(_xrFrameState.predictedDisplayTime, out leftAimWorldPos, out leftAimWorldRot);
                if (hasLeftAimWorldPose)
                {
                    hasLeftPointerPose = true;
                    leftPointerOrigin = leftAimWorldPos;
                    leftPointerDirection = (leftAimWorldRot * Vector3.forward).normalized;
                }
                else if (TryGetLeftAimPoseRay(_xrFrameState.predictedDisplayTime, out leftPointerOrigin, out leftPointerDirection))
                {
                    hasLeftPointerPose = true;
                }
                else if (hasLeftHandWorldPose)
                {
                    hasLeftPointerPose = true;
                    leftPointerOrigin = leftHandWorldPos;
                    leftPointerDirection = (leftHandWorldRot * Vector3.forward).normalized;
                }

                // 始终计算右手指针
                hasRightAimWorldPose = TryGetRightAimPoseWorldTransform(_xrFrameState.predictedDisplayTime, out rightAimWorldPos, out rightAimWorldRot);
                if (hasRightAimWorldPose)
                {
                    hasRightPointerPose = true;
                    rightPointerOrigin = rightAimWorldPos;
                    rightPointerDirection = (rightAimWorldRot * Vector3.forward).normalized;
                }
                else
                {
                    hasRightPointerPose = TryGetRightAimPoseRay(_xrFrameState.predictedDisplayTime, out rightPointerOrigin, out rightPointerDirection);
                }

                // 边沿触发：点击扳机键就切换到该手，保持直到另一只手点击才切换
                bool leftTriggerEdge = leftTriggerPressed && !_wasLeftTriggerPressed;
                bool rightTriggerEdge = rightTriggerPressed && !_wasRightTriggerPressed;
                if (leftTriggerEdge)
                    _uiClickHand = OpenXrControlHand.Left;
                else if (rightTriggerEdge)
                    _uiClickHand = OpenXrControlHand.Right;
                _wasLeftTriggerPressed = leftTriggerPressed;
                _wasRightTriggerPressed = rightTriggerPressed;

                useLeftForUi = _uiClickHand.HasValue
                    ? _uiClickHand.Value == OpenXrControlHand.Left
                    : useLeftControlHand;
                // 传送瞄准时强制使用右手指针，完成传送后恢复
                if (isTeleportAiming)
                    useLeftForUi = false;
                hasPointerPose = useLeftForUi ? hasLeftPointerPose : hasRightPointerPose;
                pointerOriginWorld = useLeftForUi ? leftPointerOrigin : rightPointerOrigin;
                pointerDirectionWorld = useLeftForUi ? leftPointerDirection : rightPointerDirection;
            }

            // 面板编辑也跟随切换后的手
            bool hasPlaneEditHandPose = useLeftForUi ? hasLeftHandWorldPose : hasRightHandWorldPose;
            Vector3 planeEditHandWorldPos = useLeftForUi ? leftHandWorldPos : rightHandWorldPos;
            Quaternion planeEditHandWorldRot = useLeftForUi ? leftHandWorldRot : rightHandWorldRot;
            bool planeEditTriggerPressed = useLeftForUi ? leftTriggerPressed : rightTriggerPressed;

            bool hasPanelPose = useLeftPanelHand ? (hasLeftHandWorldPose || hasLeftAimWorldPose) : (hasRightHandWorldPose || hasRightAimWorldPose);
            Vector3 panelWorldPos = useLeftPanelHand
                ? (hasLeftHandWorldPose ? leftHandWorldPos : leftAimWorldPos)
                : (hasRightHandWorldPose ? rightHandWorldPos : rightAimWorldPos);
            Quaternion panelWorldRot = useLeftPanelHand
                ? (hasLeftAimWorldPose ? leftAimWorldRot : leftHandWorldRot)
                : (hasRightAimWorldPose ? rightAimWorldRot : rightHandWorldRot);
            HandlePlaneEditInput(
                isUiTogglePressed,
                planeEditTriggerPressed,
                hasPointerPose,
                pointerOriginWorld,
                pointerDirectionWorld,
                hasPlaneEditHandPose,
                planeEditHandWorldPos,
                planeEditHandWorldRot,
                out bool uiToggleShortPress,
                out bool isPlaneEditTriggerConsumed);

            _uiProjectionPlane.Update(currentMainCamera, uiToggleShortPress, hasPanelPose, panelWorldPos, panelWorldRot);
            bool uiTriggerPressed = !isPlaneEditTriggerConsumed && (leftTriggerPressed || rightTriggerPressed) && !isTeleportAiming;
            bool hasRightDebugPose = hasRightAimWorldPose || hasRightHandWorldPose;
            Vector3 rightDebugWorldPos = hasRightAimWorldPose ? rightAimWorldPos : rightHandWorldPos;
            Quaternion rightDebugWorldRot = hasRightAimWorldPose ? rightAimWorldRot : rightHandWorldRot;
            _uiInteractor.Update(
                _vrRig,
                hasPointerPose,
                pointerOriginWorld,
                pointerDirectionWorld,
                uiTriggerPressed,
                uiToggleShortPress,
                hasRightDebugPose,
                rightDebugWorldPos,
                rightDebugWorldRot);
            // 触摸检测手跟随 UI 射线切换（点击哪只手扳机就切换到哪只手）
            bool hasTouchHandPose = useLeftForUi ? hasLeftHandWorldPose : hasRightHandWorldPose;
            Vector3 touchHandPos = useLeftForUi ? leftHandWorldPos : rightHandWorldPos;
            Quaternion touchHandRot = useLeftForUi ? leftHandWorldRot : rightHandWorldRot;
            bool touchTriggerHeld = leftTriggerPressed || rightTriggerPressed;
            bool uiTouchInteractionTriggered = _uiInteractor.UpdateUiRayTouch(_vrRig, hasTouchHandPose, touchHandPos, touchHandRot, touchTriggerHeld);
            if (uiTouchInteractionTriggered)
            {
                PlayUiTouchHaptic(activeControlHand);
            }

            Vector3 hmdWorldPos = default;
            bool hasHmdPose = hasValidViewPose && TryGetCurrentHmdWorldPoseFromViews(out hmdWorldPos);
            UpdateMainCameraLookAtTargetFollow(currentMainCamera, hasHmdPose, hmdWorldPos);
            bool hasTeleportPointer = hasPointerPose && isTeleportAiming && !isGripHeld && !isPlaneEditTriggerConsumed;
            Vector3 cameraForwardWorld = currentMainCamera != null ? currentMainCamera.transform.forward : _vrRig.transform.forward;

            _controllerVisualizer.Update(_vrRig, _vrRig.layer, hasLeftHandWorldPose, leftHandWorldPos, leftHandWorldRot, hasRightHandWorldPose, rightHandWorldPos, rightHandWorldRot, activeControlHand);
            _danmenProjectionPlane.Update(uiToggleShortPress, hasPanelPose, panelWorldPos, panelWorldRot, hasPointerPose, pointerOriginWorld, pointerDirectionWorld);
            bool locomotionTeleportConfirmPressed = teleportConfirmPressed && !isPlaneEditTriggerConsumed;
            _locomotion.Update(_vrRig, turnStickX, isSmoothTurnHeld, isGripHeld, hasGripLocalPose, activeGripLocalPos, isTeleportAiming, locomotionTeleportConfirmPressed, hasTeleportPointer, pointerOriginWorld, pointerDirectionWorld, cameraForwardWorld, hasHmdPose, hmdWorldPos, leftStickX, leftStickY);
            if (!isGripHeld)
                _activeGripHand = null;
        }

        private void HandlePlaneEditInput(
            bool uiTogglePressed,
            bool triggerPressed,
            bool hasPointerPose,
            Vector3 pointerOriginWorld,
            Vector3 pointerDirectionWorld,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot,
            out bool uiToggleShortPress,
            out bool triggerConsumedByPlaneEdit)
        {
            uiToggleShortPress = uiTogglePressed && !_wasPlaneEditTogglePressed;
            _wasPlaneEditTogglePressed = uiTogglePressed;
            triggerConsumedByPlaneEdit = false;

            bool triggerDown = triggerPressed && !_wasPlaneEditTriggerPressed;
            bool triggerUp = !triggerPressed && _wasPlaneEditTriggerPressed;

            PlaneEditSelection hoveredEdgeSelection = PlaneEditSelection.None;
            if (_activePlaneEditSelection != PlaneEditSelection.None)
            {
                hoveredEdgeSelection = _activePlaneEditSelection;
            }
            else if (hasPointerPose && pointerDirectionWorld.sqrMagnitude > 0.0001f)
            {
                Ray hoverRay = new(pointerOriginWorld, pointerDirectionWorld.normalized);
                if (TryRaycastResizeHandle(hoverRay, out PlaneEditSelection hoveredHandleSelection, out _, out _, out _))
                {
                    hoveredEdgeSelection = hoveredHandleSelection;
                }
                else if (TryRaycastEditablePlane(hoverRay, requireEdge: true, out PlaneEditSelection hoveredSelection, out _, out _))
                {
                    hoveredEdgeSelection = hoveredSelection;
                }
            }
            SetPlaneEditEdgeHighlight(hoveredEdgeSelection);

            if (triggerDown)
            {
                if (TryBeginPlaneEditResize(hasPointerPose, pointerOriginWorld, pointerDirectionWorld, hasRightHandPose, rightHandWorldPos))
                {
                    triggerConsumedByPlaneEdit = true;
                    VRModCore.Log($"[PlaneEdit][OpenXR] Resizing {_activePlaneEditSelection} (handle {_activePlaneEditResizeHandleIndex}).");
                }
                else if (TryBeginPlaneEditDrag(hasPointerPose, pointerOriginWorld, pointerDirectionWorld, hasRightHandPose, rightHandWorldPos, rightHandWorldRot))
                {
                    triggerConsumedByPlaneEdit = true;
                    VRModCore.Log($"[PlaneEdit][OpenXR] Dragging {_activePlaneEditSelection}.");
                }
            }

            if (triggerPressed && _activePlaneEditSelection != PlaneEditSelection.None)
            {
                triggerConsumedByPlaneEdit = true;
                if (_activePlaneEditMode == PlaneEditMode.Resize)
                {
                    UpdateActivePlaneEditResize(hasRightHandPose, rightHandWorldPos);
                }
                else
                {
                    UpdateActivePlaneEditDrag(hasRightHandPose, rightHandWorldPos, rightHandWorldRot);
                }
            }

            if (triggerUp)
            {
                ClearActivePlaneEditState();
            }

            _wasPlaneEditTriggerPressed = triggerPressed;
        }

        private bool TryBeginPlaneEditResize(
            bool hasPointerPose,
            Vector3 pointerOriginWorld,
            Vector3 pointerDirectionWorld,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos)
        {
            if (!hasPointerPose || pointerDirectionWorld.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            if (!hasRightHandPose)
            {
                return false;
            }

            Ray ray = new(pointerOriginWorld, pointerDirectionWorld.normalized);
            if (!TryRaycastResizeHandle(ray, out PlaneEditSelection selection, out int handleIndex, out _, out Transform planeTransform))
            {
                return false;
            }

            if (!TryGetPanelScaleForSelection(selection, out float startScale))
            {
                return false;
            }

            _activePlaneEditMode = PlaneEditMode.Resize;
            _activePlaneEditSelection = selection;
            _activePlaneEditResizeHandleIndex = handleIndex;
            _activePlaneEditResizeStartScale = startScale;
            _activePlaneEditResizeStartHandDistance = Mathf.Max(0.02f, Vector3.Distance(rightHandWorldPos, planeTransform.position));
            ApplyManualPoseToSelection(selection, planeTransform.position, planeTransform.rotation);
            return true;
        }

        private bool TryBeginPlaneEditDrag(
            bool hasPointerPose,
            Vector3 pointerOriginWorld,
            Vector3 pointerDirectionWorld,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            if (!hasPointerPose || pointerDirectionWorld.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Ray ray = new(pointerOriginWorld, pointerDirectionWorld.normalized);
            if (!TryRaycastEditablePlane(ray, requireEdge: true, out PlaneEditSelection selection, out Transform planeTransform, out _))
            {
                return false;
            }
            if (!hasRightHandPose)
            {
                return false;
            }

            _activePlaneEditMode = PlaneEditMode.Drag;
            _activePlaneEditSelection = selection;
            Quaternion inverseHandRot = Quaternion.Inverse(rightHandWorldRot);
            _activePlaneEditHandLocalPos = inverseHandRot * (planeTransform.position - rightHandWorldPos);
            _activePlaneEditHandLocalRot = inverseHandRot * planeTransform.rotation;
            ApplyManualPoseToSelection(selection, planeTransform.position, planeTransform.rotation);
            return true;
        }

        private void UpdateActivePlaneEditDrag(bool hasRightHandPose, Vector3 rightHandWorldPos, Quaternion rightHandWorldRot)
        {
            if (_activePlaneEditSelection == PlaneEditSelection.None) return;
            if (!hasRightHandPose) return;
            if (!TryGetPlaneTransformForSelection(_activePlaneEditSelection, out _)) return;

            Vector3 targetPosition = rightHandWorldPos + (rightHandWorldRot * _activePlaneEditHandLocalPos);
            Quaternion targetRotation = rightHandWorldRot * _activePlaneEditHandLocalRot;
            ApplyManualPoseToSelection(_activePlaneEditSelection, targetPosition, targetRotation);
        }

        private void UpdateActivePlaneEditResize(bool hasRightHandPose, Vector3 rightHandWorldPos)
        {
            if (_activePlaneEditSelection == PlaneEditSelection.None) return;
            if (!hasRightHandPose) return;
            if (!TryGetPlaneTransformForSelection(_activePlaneEditSelection, out Transform planeTransform)) return;
            if (_activePlaneEditResizeStartHandDistance <= 0.0001f) return;

            float currentDistance = Mathf.Max(0.01f, Vector3.Distance(rightHandWorldPos, planeTransform.position));
            float scaleRatio = currentDistance / _activePlaneEditResizeStartHandDistance;
            float resizeSensitivity = Mathf.Max(0.01f, ConfigManager.OpenXR_PanelResizeSensitivity.Value);
            float adjustedRatio = Mathf.Max(0.01f, 1f + ((scaleRatio - 1f) * resizeSensitivity));
            float targetScale = _activePlaneEditResizeStartScale * adjustedRatio;
            SetPanelScaleForSelection(_activePlaneEditSelection, targetScale);
        }

        private bool TryRaycastResizeHandle(
            Ray ray,
            out PlaneEditSelection selection,
            out int handleIndex,
            out float hitDistance,
            out Transform selectionTransform)
        {
            selection = PlaneEditSelection.None;
            handleIndex = -1;
            hitDistance = float.MaxValue;
            selectionTransform = null;

            if (_uiProjectionPlane.TryRaycastResizeHandle(ray, out int uiHandleIndex, out float uiDistance) &&
                _uiProjectionPlane.TryGetPlaneTransform(out Transform uiTransform))
            {
                selection = PlaneEditSelection.UiProjectionPlane;
                handleIndex = uiHandleIndex;
                hitDistance = uiDistance;
                selectionTransform = uiTransform;
            }

            if (_danmenProjectionPlane.TryRaycastResizeHandle(ray, out int danmenHandleIndex, out float danmenDistance) &&
                _danmenProjectionPlane.TryGetPlaneTransform(out Transform danmenTransform) &&
                danmenDistance < hitDistance)
            {
                selection = PlaneEditSelection.DanmenProjectionPlane;
                handleIndex = danmenHandleIndex;
                hitDistance = danmenDistance;
                selectionTransform = danmenTransform;
            }

            if (_uiInteractor.TryRaycastSubCameraResizeHandle(ray, out int subCameraHandleIndex, out float subCameraDistance) &&
                _uiInteractor.TryGetSubCameraProjectionPlaneTransform(out Transform subCameraTransform) &&
                subCameraDistance < hitDistance)
            {
                selection = PlaneEditSelection.SubCameraProjectionPlane;
                handleIndex = subCameraHandleIndex;
                hitDistance = subCameraDistance;
                selectionTransform = subCameraTransform;
            }

            return selection != PlaneEditSelection.None;
        }

        private bool TryRaycastEditablePlane(Ray ray, bool requireEdge, out PlaneEditSelection selection, out Transform selectionTransform, out Vector3 hitPointWorld)
        {
            selection = PlaneEditSelection.None;
            selectionTransform = null;
            hitPointWorld = default;
            float bestDistance = float.MaxValue;

            if (TryGetPlaneTransformForSelection(PlaneEditSelection.UiProjectionPlane, out Transform uiPlaneTransform) &&
                TryRaycastPlaneQuad(ray, uiPlaneTransform, out float uiDistance, out Vector3 uiHitPoint, out bool uiIsEdge) &&
                (!requireEdge || uiIsEdge))
            {
                selection = PlaneEditSelection.UiProjectionPlane;
                selectionTransform = uiPlaneTransform;
                hitPointWorld = uiHitPoint;
                bestDistance = uiDistance;
            }

            if (TryGetPlaneTransformForSelection(PlaneEditSelection.DanmenProjectionPlane, out Transform danmenPlaneTransform) &&
                TryRaycastPlaneQuad(ray, danmenPlaneTransform, out float danmenDistance, out Vector3 danmenHitPoint, out bool danmenIsEdge) &&
                (!requireEdge || danmenIsEdge) &&
                danmenDistance < bestDistance)
            {
                selection = PlaneEditSelection.DanmenProjectionPlane;
                selectionTransform = danmenPlaneTransform;
                hitPointWorld = danmenHitPoint;
                bestDistance = danmenDistance;
            }

            if (_uiInteractor.TryGetSubCameraProjectionPlaneTransform(out Transform debugPlaneTransform) &&
                TryRaycastPlaneQuad(ray, debugPlaneTransform, out float debugDistance, out Vector3 debugHitPoint, out _) &&
                (!requireEdge || _uiInteractor.IsSubCameraProjectionMoveRingHit(debugHitPoint)) &&
                debugDistance < bestDistance)
            {
                selection = PlaneEditSelection.SubCameraProjectionPlane;
                selectionTransform = debugPlaneTransform;
                hitPointWorld = debugHitPoint;
                bestDistance = debugDistance;
            }

            return selection != PlaneEditSelection.None;
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
            float absX = Mathf.Abs(localHit.x);
            float absY = Mathf.Abs(localHit.y);
            float inner = 0.5f + PlaneEditEdgeRingInnerOffsetNormalized;
            float outer = 0.5f + PlaneEditEdgeRingOuterOffsetNormalized;

            if (absX > outer || absY > outer)
            {
                return false;
            }

            bool onVerticalRing = absX >= inner && absY <= outer;
            bool onHorizontalRing = absY >= inner && absX <= outer;
            isEdge = onVerticalRing || onHorizontalRing;

            hitDistance = distance;
            hitPointWorld = worldHit;
            return true;
        }

        private bool TryGetPlaneTransformForSelection(PlaneEditSelection selection, out Transform planeTransform)
        {
            planeTransform = null;
            switch (selection)
            {
                case PlaneEditSelection.UiProjectionPlane:
                    return _uiProjectionPlane.TryGetPlaneTransform(out planeTransform);
                case PlaneEditSelection.DanmenProjectionPlane:
                    return _danmenProjectionPlane.TryGetPlaneTransform(out planeTransform);
                case PlaneEditSelection.SubCameraProjectionPlane:
                    return _uiInteractor.TryGetSubCameraProjectionPlaneTransform(out planeTransform);
                default:
                    return false;
            }
        }

        private void ApplyManualPoseToSelection(PlaneEditSelection selection, Vector3 worldPosition, Quaternion worldRotation)
        {
            switch (selection)
            {
                case PlaneEditSelection.UiProjectionPlane:
                    _uiProjectionPlane.SetManualPose(worldPosition, worldRotation);
                    break;
                case PlaneEditSelection.DanmenProjectionPlane:
                    _danmenProjectionPlane.SetManualPose(worldPosition, worldRotation);
                    break;
                case PlaneEditSelection.SubCameraProjectionPlane:
                    _uiInteractor.SetSubCameraProjectionManualPose(worldPosition, worldRotation);
                    break;
            }
        }

        private bool TryGetPanelScaleForSelection(PlaneEditSelection selection, out float panelScale)
        {
            panelScale = 1f;
            switch (selection)
            {
                case PlaneEditSelection.UiProjectionPlane:
                    panelScale = _uiProjectionPlane.GetPanelScale();
                    return true;
                case PlaneEditSelection.DanmenProjectionPlane:
                    panelScale = _danmenProjectionPlane.GetPanelScale();
                    return true;
                case PlaneEditSelection.SubCameraProjectionPlane:
                    panelScale = _uiInteractor.GetSubCameraProjectionPanelScale();
                    return true;
                default:
                    return false;
            }
        }

        private void SetPanelScaleForSelection(PlaneEditSelection selection, float panelScale)
        {
            switch (selection)
            {
                case PlaneEditSelection.UiProjectionPlane:
                    _uiProjectionPlane.SetPanelScale(panelScale);
                    break;
                case PlaneEditSelection.DanmenProjectionPlane:
                    _danmenProjectionPlane.SetPanelScale(panelScale);
                    break;
                case PlaneEditSelection.SubCameraProjectionPlane:
                    _uiInteractor.SetSubCameraProjectionPanelScale(panelScale);
                    break;
            }
        }

        private void SetPlaneEditEdgeHighlight(PlaneEditSelection highlightedSelection)
        {
            _uiProjectionPlane.SetEdgeHighlight(highlightedSelection == PlaneEditSelection.UiProjectionPlane);
            _danmenProjectionPlane.SetEdgeHighlight(highlightedSelection == PlaneEditSelection.DanmenProjectionPlane);
            _uiInteractor.SetSubCameraProjectionEdgeHighlight(highlightedSelection == PlaneEditSelection.SubCameraProjectionPlane);
            _uiInteractor.SetSubCameraProjectionOutlineVisible(_isSubCameraMoveModeActive);
        }

        private void ClearActivePlaneEditState()
        {
            _activePlaneEditSelection = PlaneEditSelection.None;
            _activePlaneEditMode = PlaneEditMode.None;
            _activePlaneEditHandLocalPos = Vector3.zero;
            _activePlaneEditHandLocalRot = Quaternion.identity;
            _activePlaneEditResizeHandleIndex = -1;
            _activePlaneEditResizeStartHandDistance = 0f;
            _activePlaneEditResizeStartScale = 1f;
        }

        private bool TryGetLeftAimPoseRay(long displayTime, out Vector3 originWorld, out Vector3 directionWorld)
        {
            originWorld = default;
            directionWorld = default;

            if (_vrRig == null ||
                _appSpace == OpenXRConstants.XR_NULL_HANDLE ||
                _leftAimSpace == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrLocateSpace == null)
            {
                return false;
            }

            var location = new XrSpaceLocation
            {
                type = XrStructureType.XR_TYPE_SPACE_LOCATION
            };

            XrResult locateResult = OpenXRAPI.xrLocateSpace(_leftAimSpace, _appSpace, displayTime, out location);
            if (locateResult < 0)
            {
                if (!_leftAimPoseErrorLogged)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] xrLocateSpace(left aim) failed: {locateResult}");
                    _leftAimPoseErrorLogged = true;
                }
                return false;
            }

            _leftAimPoseErrorLogged = false;
            bool hasPosition = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0;
            bool hasOrientation = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0;
            if (!hasPosition || !hasOrientation)
            {
                return false;
            }

            Vector3 localPos = ToUnityLocalPosition(location.pose);
            Quaternion localRot = ToUnityLocalRotation(location.pose);

            originWorld = _vrRig.transform.TransformPoint(localPos);
            directionWorld = _vrRig.transform.TransformDirection(localRot * Vector3.forward).normalized;
            return true;
        }

        private bool TryGetLeftAimPoseWorldTransform(long displayTime, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = default;
            worldRotation = Quaternion.identity;

            if (_vrRig == null ||
                _appSpace == OpenXRConstants.XR_NULL_HANDLE ||
                _leftAimSpace == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrLocateSpace == null)
            {
                return false;
            }

            var location = new XrSpaceLocation
            {
                type = XrStructureType.XR_TYPE_SPACE_LOCATION
            };

            XrResult locateResult = OpenXRAPI.xrLocateSpace(_leftAimSpace, _appSpace, displayTime, out location);
            if (locateResult < 0)
            {
                if (!_leftAimPoseErrorLogged)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] xrLocateSpace(left aim) failed: {locateResult}");
                    _leftAimPoseErrorLogged = true;
                }
                return false;
            }

            _leftAimPoseErrorLogged = false;
            bool hasPosition = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0;
            bool hasOrientation = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0;
            if (!hasPosition || !hasOrientation)
            {
                return false;
            }

            Vector3 localPos = ToUnityLocalPosition(location.pose);
            Quaternion localRot = ToUnityLocalRotation(location.pose);
            worldPosition = _vrRig.transform.TransformPoint(localPos);
            worldRotation = _vrRig.transform.rotation * localRot;
            return true;
        }

        private bool TryGetRightAimPoseRay(long displayTime, out Vector3 originWorld, out Vector3 directionWorld)
        {
            originWorld = default;
            directionWorld = default;

            if (_vrRig == null ||
                _appSpace == OpenXRConstants.XR_NULL_HANDLE ||
                _rightAimSpace == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrLocateSpace == null)
            {
                return false;
            }

            var location = new XrSpaceLocation
            {
                type = XrStructureType.XR_TYPE_SPACE_LOCATION
            };

            XrResult locateResult = OpenXRAPI.xrLocateSpace(_rightAimSpace, _appSpace, displayTime, out location);
            if (locateResult < 0)
            {
                if (!_rightAimPoseErrorLogged)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] xrLocateSpace(right aim) failed: {locateResult}");
                    _rightAimPoseErrorLogged = true;
                }
                return false;
            }

            _rightAimPoseErrorLogged = false;
            bool hasPosition = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0;
            bool hasOrientation = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0;
            if (!hasPosition || !hasOrientation)
            {
                return false;
            }

            Vector3 localPos = ToUnityLocalPosition(location.pose);
            Quaternion localRot = ToUnityLocalRotation(location.pose);

            originWorld = _vrRig.transform.TransformPoint(localPos);
            directionWorld = _vrRig.transform.TransformDirection(localRot * Vector3.forward).normalized;
            return true;
        }

        private bool TryGetRightAimPoseWorldTransform(long displayTime, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = default;
            worldRotation = Quaternion.identity;

            if (_vrRig == null ||
                _appSpace == OpenXRConstants.XR_NULL_HANDLE ||
                _rightAimSpace == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrLocateSpace == null)
            {
                return false;
            }

            var location = new XrSpaceLocation
            {
                type = XrStructureType.XR_TYPE_SPACE_LOCATION
            };

            XrResult locateResult = OpenXRAPI.xrLocateSpace(_rightAimSpace, _appSpace, displayTime, out location);
            if (locateResult < 0)
            {
                if (!_rightAimPoseErrorLogged)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] xrLocateSpace(right aim) failed: {locateResult}");
                    _rightAimPoseErrorLogged = true;
                }
                return false;
            }

            _rightAimPoseErrorLogged = false;
            bool hasPosition = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0;
            bool hasOrientation = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0;
            if (!hasPosition || !hasOrientation)
            {
                return false;
            }

            Vector3 localPos = ToUnityLocalPosition(location.pose);
            Quaternion localRot = ToUnityLocalRotation(location.pose);
            worldPosition = _vrRig.transform.TransformPoint(localPos);
            worldRotation = _vrRig.transform.rotation * localRot;
            return true;
        }

        private bool TryGetLeftGripPoseLocalPosition(long displayTime, out Vector3 localPosition)
        {
            localPosition = default;
            if (!TryLocateGripPose(_leftGripSpace, "left", displayTime, requireOrientation: false, ref _leftGripPoseErrorLogged, out Vector3 resolvedLocalPosition, out _))
            {
                return false;
            }

            localPosition = resolvedLocalPosition;
            return true;
        }

        private bool TryGetRightGripPoseLocalPosition(long displayTime, out Vector3 localPosition)
        {
            localPosition = default;
            if (!TryLocateGripPose(_rightGripSpace, "right", displayTime, requireOrientation: false, ref _rightGripPoseErrorLogged, out Vector3 resolvedLocalPosition, out _))
            {
                return false;
            }

            localPosition = resolvedLocalPosition;
            return true;
        }

        private bool TryGetLeftGripPoseWorldTransform(long displayTime, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = default;
            worldRotation = Quaternion.identity;

            if (_vrRig == null)
            {
                return false;
            }

            if (!TryLocateGripPose(_leftGripSpace, "left", displayTime, requireOrientation: true, ref _leftGripPoseErrorLogged, out Vector3 localPosition, out Quaternion localRotation))
            {
                return false;
            }

            worldPosition = _vrRig.transform.TransformPoint(localPosition);
            worldRotation = _vrRig.transform.rotation * localRotation;
            return true;
        }

        private bool TryGetRightGripPoseWorldTransform(long displayTime, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = default;
            worldRotation = Quaternion.identity;

            if (_vrRig == null)
            {
                return false;
            }

            if (!TryLocateGripPose(_rightGripSpace, "right", displayTime, requireOrientation: true, ref _rightGripPoseErrorLogged, out Vector3 localPosition, out Quaternion localRotation))
            {
                return false;
            }

            worldPosition = _vrRig.transform.TransformPoint(localPosition);
            worldRotation = _vrRig.transform.rotation * localRotation;
            return true;
        }

        private bool TryLocateGripPose(ulong gripSpaceHandle, string handName, long displayTime, bool requireOrientation, ref bool errorLogged, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = default;
            localRotation = Quaternion.identity;
            if (_appSpace == OpenXRConstants.XR_NULL_HANDLE ||
                gripSpaceHandle == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrLocateSpace == null)
            {
                return false;
            }

            var location = new XrSpaceLocation
            {
                type = XrStructureType.XR_TYPE_SPACE_LOCATION
            };

            XrResult locateResult = OpenXRAPI.xrLocateSpace(gripSpaceHandle, _appSpace, displayTime, out location);
            if (locateResult < 0)
            {
                if (!errorLogged)
                {
                    VRModCore.LogWarning($"[Input][OpenXR] xrLocateSpace({handName} grip) failed: {locateResult}");
                    errorLogged = true;
                }
                return false;
            }

            errorLogged = false;
            bool hasPosition = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0;
            if (!hasPosition)
            {
                return false;
            }

            bool hasOrientation = (location.locationFlags & XrSpaceLocationFlags.XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0;
            if (requireOrientation && !hasOrientation)
            {
                return false;
            }

            localPosition = ToUnityLocalPosition(location.pose);
            if (hasOrientation)
            {
                localRotation = ToUnityLocalRotation(location.pose);
            }
            return true;
        }

        private bool TryGetCurrentHmdWorldPoseFromViews(out Vector3 hmdWorldPos)
        {
            hmdWorldPos = default;

            if (_vrRig == null || _locatedViews == null || _locatedViews.Length == 0)
            {
                return false;
            }

            Vector3 hmdLocalPos = ToUnityLocalPosition(_locatedViews[0].pose);
            if (_locatedViews.Length > 1)
            {
                hmdLocalPos = (hmdLocalPos + ToUnityLocalPosition(_locatedViews[1].pose)) * 0.5f;
            }

            hmdWorldPos = _vrRig.transform.TransformPoint(hmdLocalPos);
            return true;
        }

        private Camera GetTrackedMainCamera()
        {
            if (_currentlyTrackedOriginalCameraGO != null &&
                _currentlyTrackedOriginalCameraGO.TryGetComponent<Camera>(out Camera trackedCamera))
            {
                return trackedCamera;
            }

            return Camera.main;
        }

        private static OpenXrControlHand GetActiveControlHand()
        {
            return ConfigManager.OpenXR_ControlHand?.Value ?? OpenXrControlHand.Right;
        }

        private void PlayUiTouchHaptic(OpenXrControlHand controlHand)
        {
            if (_xrSession == OpenXRConstants.XR_NULL_HANDLE ||
                _hapticAction == OpenXRConstants.XR_NULL_HANDLE ||
                OpenXRAPI.xrApplyHapticFeedback == null)
            {
                return;
            }

            ulong subactionPath = controlHand == OpenXrControlHand.Left ? _leftHandPath : _rightHandPath;
            if (subactionPath == OpenXRConstants.XR_NULL_PATH)
            {
                return;
            }

            var hapticInfo = new XrHapticActionInfo
            {
                type = XrStructureType.XR_TYPE_HAPTIC_ACTION_INFO,
                action = _hapticAction,
                subactionPath = subactionPath
            };

            var hapticVibration = new XrHapticVibration
            {
                type = XrStructureType.XR_TYPE_HAPTIC_VIBRATION,
                duration = UiTouchHapticDurationNs,
                frequency = 0f,
                amplitude = UiTouchHapticAmplitude
            };

            XrResult hapticResult = OpenXRAPI.xrApplyHapticFeedback(_xrSession, in hapticInfo, in hapticVibration);
            if (hapticResult < 0)
            {
                VRModCore.LogRuntimeDebug($"[Input][OpenXR] UI touch haptic failed: {hapticResult}");
            }
        }

        private void UpdateSubCameraMoveModeState(
            bool rightAPressed,
            bool rightBPressed,
            float rightStickX,
            float rightStickY,
            bool rightStickClickPressed,
            out bool moveModeActive,
            out bool rotateModeActive,
            out float moveAxisX,
            out float moveAxisY,
            out bool heightUpStep,
            out bool heightDownStep)
        {
            heightUpStep = false;
            heightDownStep = false;

            if (rightAPressed)
            {
                _subCameraMoveAHoldSeconds += Time.unscaledDeltaTime;
                if (!_subCameraMoveAToggleHoldConsumed && _subCameraMoveAHoldSeconds >= SubCameraMoveModeToggleHoldSeconds)
                {
                    _isSubCameraMoveModeActive = !_isSubCameraMoveModeActive;
                    _subCameraMoveAToggleHoldConsumed = true;
                    VRModCore.Log($"[SubCamera][OpenXR] Move mode {(_isSubCameraMoveModeActive ? "ON" : "OFF")}.");
                }
            }
            else
            {
                if (_wasSubCameraAButtonPressed &&
                    _isSubCameraMoveModeActive &&
                    !_subCameraMoveAToggleHoldConsumed)
                {
                    heightUpStep = true;
                }

                _subCameraMoveAHoldSeconds = 0f;
                _subCameraMoveAToggleHoldConsumed = false;
            }

            if (_wasSubCameraBButtonPressed && !rightBPressed && _isSubCameraMoveModeActive)
            {
                heightDownStep = true;
            }

            if (rightStickClickPressed)
            {
                _subCameraMoveStickClickHoldSeconds += Time.unscaledDeltaTime;
            }
            else
            {
                _subCameraMoveStickClickHoldSeconds = 0f;
            }

            moveModeActive = _isSubCameraMoveModeActive;
            rotateModeActive = moveModeActive &&
                               rightStickClickPressed &&
                               _subCameraMoveStickClickHoldSeconds >= SubCameraRotateModeHoldSeconds;
            moveAxisX = moveModeActive ? rightStickX : 0f;
            moveAxisY = moveModeActive ? rightStickY : 0f;

            _wasSubCameraAButtonPressed = rightAPressed;
            _wasSubCameraBButtonPressed = rightBPressed;
        }

        private static bool IsBooleanActionPressed(OpenXrBooleanLogState state)
        {
            return state.HasSample && state.IsActive && state.IsPressed;
        }

        private static bool IsTriggerPressed(OpenXrTriggerLogState state)
        {
            return state.HasSample && state.IsActive && state.IsPressed;
        }

        private static float GetFloatActionValue(OpenXrFloatLogState state)
        {
            if (!state.HasSample || !state.IsActive)
            {
                return 0f;
            }

            return Mathf.Clamp01(state.Value);
        }

        private static float GetThumbstickX(OpenXrVector2LogState state)
        {
            if (!state.HasSample || !state.IsActive)
            {
                return 0f;
            }

            return Mathf.Clamp(state.X, -1f, 1f);
        }

        private static float GetThumbstickY(OpenXrVector2LogState state)
        {
            if (!state.HasSample || !state.IsActive)
            {
                return 0f;
            }

            return Mathf.Clamp(state.Y, -1f, 1f);
        }

        private bool UpdateTeleportAimStateFromStick(float rightStickY)
        {
            if (_isTeleportAimingByStick)
            {
                if (rightStickY <= TeleportAimStickReleaseThreshold)
                {
                    _isTeleportAimingByStick = false;
                }
            }
            else if (rightStickY >= TeleportAimStickPressThreshold)
            {
                _isTeleportAimingByStick = true;
            }

            return _isTeleportAimingByStick;
        }

        private static Vector3 ToUnityLocalPosition(in XrPosef pose)
        {
            return new Vector3(pose.position.x, pose.position.y, -pose.position.z);
        }

        private static Quaternion ToUnityLocalRotation(in XrPosef pose)
        {
            return new Quaternion(pose.orientation.x, pose.orientation.y, -pose.orientation.z, -pose.orientation.w);
        }

        private void PopulateProjectionLayer()
        {
            for (int i = 0; i < 2; i++)
            {
                _projectionLayerViews[i].pose = _locatedViews[i].pose;
                _projectionLayerViews[i].fov = _locatedViews[i].fov;
                _projectionLayerViews[i].subImage.swapchain = _eyeSwapchains[i];
                _projectionLayerViews[i].subImage.imageRect.offset = new XrOffset2Di { x = 0, y = 0 };
                _projectionLayerViews[i].subImage.imageRect.extent.width = (int)_viewConfigViews[i].recommendedImageRectWidth;
                _projectionLayerViews[i].subImage.imageRect.extent.height = (int)_viewConfigViews[i].recommendedImageRectHeight;
                _projectionLayerViews[i].subImage.imageArrayIndex = 0;
                Marshal.StructureToPtr(_projectionLayerViews[i], _pProjectionLayerViews + (i * Marshal.SizeOf<XrCompositionLayerProjectionView>()), false);
            }
            _projectionLayer.layerFlags = XrCompositionLayerFlags.None;
            _projectionLayer.space = _appSpace;
            _projectionLayer.viewCount = (uint)_viewConfigViews.Count;
            _projectionLayer.views = _pProjectionLayerViews;
            Marshal.StructureToPtr(_projectionLayer, _pProjectionLayer, false);
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

            _vrRig = new GameObject("UnityVRMod_XrRig");
            _vrRig.layer = VrRigRenderLayer;
            _currentlyTrackedOriginalCameraGO = mainCamera.gameObject;
            _isUsing2dSyntheticFallbackCamera = CameraFinder.IsSynthetic2dFallbackCamera(mainCamera);
            _mainCameraLookAtTarget = null;
            _nextLookAtTargetResolveTime = 0f;
            ResolveMainCameraLookAtTarget(mainCamera, force: true);

            Vector3 targetPosition = mainCamera.transform.position;
            float teleportPlaneY = targetPosition.y;
            Quaternion targetRotation = Quaternion.Euler(0, mainCamera.transform.eulerAngles.y, 0);
            if (TryGetCameraTypesInitialPosition(mainCamera, out Vector3 cameraTypesInitialPosition))
            {
                targetPosition = cameraTypesInitialPosition;
                teleportPlaneY = cameraTypesInitialPosition.y;
                VRModCore.LogRuntimeDebug($"Using CameraTypes.initialPosition for rig init: ({targetPosition.x:F3}, {targetPosition.y:F3}, {targetPosition.z:F3})");
            }

            var poseOverrides = PoseParser.Parse(ConfigManager.ScenePoseOverrides.Value);
            string currentSceneName = mainCamera.gameObject.scene.name;
            _isUsingForcedDefaultRenderState = ShouldForceDefaultRenderStateForScene(currentSceneName);
            _isUsingForcedSolidClearState = !_isUsingForcedDefaultRenderState && ShouldForceSolidClearStateForScene(currentSceneName);

            if (poseOverrides.TryGetValue(currentSceneName, out PoseOverride poseOverride))
            {
                VRModCore.Log($"Applying pose override for scene '{currentSceneName}'");
                var p = poseOverride.Position;
                var r = poseOverride.Rotation;
                var originalPos = targetPosition;
                var originalRot = mainCamera.transform.eulerAngles;

                Vector3 finalPos = new(float.IsNaN(p.x) ? originalPos.x : p.x, float.IsNaN(p.y) ? originalPos.y : p.y, float.IsNaN(p.z) ? originalPos.z : p.z);
                Vector3 finalRot = new(float.IsNaN(r.x) ? originalRot.x : r.x, float.IsNaN(r.y) ? originalRot.y : r.y, float.IsNaN(r.z) ? originalRot.z : r.z);
                targetPosition = finalPos;
                targetRotation = Quaternion.Euler(finalRot);
            }
            _vrRig.transform.SetPositionAndRotation(targetPosition, targetRotation);

            _mainCameraClearFlags = mainCamera.clearFlags;
            _mainCameraBackgroundColor = mainCamera.backgroundColor;
            _mainCameraCullingMask = mainCamera.cullingMask;

            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, ConfigManager.VrWorldScale.Value);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            if (UseSingleSharedVrEyeCamera)
            {
                _leftVrCameraGO = new GameObject("XrVrCamera_Shared");
                _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
                _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
                _rightVrCameraGO = _leftVrCameraGO;
                _rightVrCamera = _leftVrCamera;
                ConfigureVrCamera(_leftVrCamera, mainCamera, "Shared");
            }
            else
            {
                _leftVrCameraGO = new GameObject("XrVrCamera_Left");
                _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
                _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
                ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");

                _rightVrCameraGO = new GameObject("XrVrCamera_Right");
                _rightVrCameraGO.transform.SetParent(_vrRig.transform, false);
                _rightVrCamera = _rightVrCameraGO.AddComponent<Camera>();
                ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
            }

            bool useFallbackRenderState = ShouldUseFallbackRenderState();
            LogReferenceCameraDiagnostics(mainCamera, currentSceneName, useFallbackRenderState);
            if (!useFallbackRenderState && EnableOpenXrPostFxSync)
            {
                SyncOpenXrPostProcessing(mainCamera);
            }
            else if (!useFallbackRenderState && !EnableOpenXrPostFxSync)
            {
                VRModCore.LogRuntimeDebug("[PostFX][OpenXR] PostFX sync disabled for performance test.");
            }

            _lastCalculatedVerticalOffset = 0f;
            UpdateVerticalOffset();
            _locomotion.SetFixedTeleportPlaneY(teleportPlaneY);
            _locomotion.SetGroundProbeReferenceWorldPosition(targetPosition);
            _locomotion.RefreshTeleportColliderCache(_vrRig);
            _uiProjectionPlane.Initialize(_vrRig, mainCamera);
            _uiInteractor.Initialize(mainCamera);
            _danmenProjectionPlane.Initialize(_vrRig);
            VRModCore.Log($"[UI][OpenXR] UI projection + ray interactor enabled (PrimaryHand={GetActiveControlHand()}, Trigger=UI click, Toggle={(GetActiveControlHand() == OpenXrControlHand.Left ? "Y(Left)" : "B(Right)")}).");
            if (_isUsing2dSyntheticFallbackCamera)
            {
                VRModCore.Log("[Camera][OpenXR] 2D synthetic fallback mode active: eye cameras render VR rig layer only (projection-plane-first).");
            }
            else if (_isUsingForcedDefaultRenderState)
            {
                VRModCore.Log($"[Camera][OpenXR] Forced default render state active for scene '{currentSceneName}' (main-camera PostFX sync skipped).");
            }
            else if (_isUsingForcedSolidClearState)
            {
                VRModCore.Log($"[Camera][OpenXR] Forced solid clear active for scene '{currentSceneName}' (main-camera clearFlags ignored).");
            }

            VRModCore.Log("OpenXR: VR Camera Rig setup complete.");
        }

        private void ConfigureVrCamera(Camera vrCam, Camera mainCamRef, string eyeName)
        {
            VRModCore.LogRuntimeDebug($"Configuring VR Camera properties for: {eyeName}");
            vrCam.stereoTargetEye = StereoTargetEyeMask.None;
            vrCam.enabled = false;

            ApplyVrCameraRenderState(vrCam);

            float nearClipPlaneUserValue = ConfigManager.VrCameraNearClipPlane.Value;
            vrCam.nearClipPlane = Mathf.Max(0.001f, nearClipPlaneUserValue * _currentAppliedRigScale);

            float referenceFarClip = 1000f;
            if (mainCamRef != null)
            {
                referenceFarClip = mainCamRef.farClipPlane;
            }
            else if (_currentlyTrackedOriginalCameraGO != null)
            {
                if (_currentlyTrackedOriginalCameraGO.TryGetComponent<Camera>(out var trackedCam)) referenceFarClip = trackedCam.farClipPlane;
            }
            vrCam.farClipPlane = referenceFarClip * _currentAppliedRigScale;

            if (vrCam == _leftVrCamera)
            {
                SyncEffectCameraClipPlanes(_leftVrHdrEffectCamera, vrCam);
            }
            else if (vrCam == _rightVrCamera)
            {
                SyncEffectCameraClipPlanes(_rightVrHdrEffectCamera, vrCam);
            }
        }

        private void ApplyVrCameraRenderState(Camera vrCam)
        {
            if (vrCam == null) return;

            if (_isUsing2dSyntheticFallbackCamera)
            {
                vrCam.clearFlags = CameraClearFlags.SolidColor;
                vrCam.backgroundColor = VrRigClearColor;
                vrCam.cullingMask = GetVrRigLayerMask();
                return;
            }

            if (_isUsingForcedDefaultRenderState)
            {
                vrCam.clearFlags = CameraClearFlags.SolidColor;
                vrCam.backgroundColor = VrRigClearColor;
                vrCam.cullingMask = _mainCameraCullingMask | GetVrRigLayerMask();
                return;
            }

            if (_isUsingForcedSolidClearState)
            {
                vrCam.clearFlags = CameraClearFlags.SolidColor;
                vrCam.backgroundColor = VrRigClearColor;
                vrCam.cullingMask = _mainCameraCullingMask | GetVrRigLayerMask();
                return;
            }

            vrCam.clearFlags = _mainCameraClearFlags;
            vrCam.cullingMask = _mainCameraCullingMask | GetVrRigLayerMask();
            if (_mainCameraClearFlags == CameraClearFlags.SolidColor)
            {
                vrCam.backgroundColor = _mainCameraBackgroundColor;
            }
        }

        private bool ShouldUseFallbackRenderState()
        {
            return _isUsing2dSyntheticFallbackCamera || _isUsingForcedDefaultRenderState;
        }

        private void RefreshForceDefaultRenderScenesCacheIfNeeded()
        {
            string raw = ConfigManager.OpenXR_ForceDefaultRenderScenes?.Value ?? string.Empty;
            if (string.Equals(raw, _cachedForceDefaultRenderScenesRaw, StringComparison.Ordinal))
            {
                return;
            }

            _cachedForceDefaultRenderScenesRaw = raw;
            _forceDefaultRenderSceneNames.Clear();

            string[] tokens = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string sceneName = token.Trim();
                if (sceneName.Length > 0)
                {
                    _forceDefaultRenderSceneNames.Add(sceneName);
                }
            }
        }

        private bool ShouldForceDefaultRenderStateForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            RefreshForceDefaultRenderScenesCacheIfNeeded();
            return _forceDefaultRenderSceneNames.Contains(sceneName);
        }

        private void RefreshForceSolidClearScenesCacheIfNeeded()
        {
            string raw = ConfigManager.OpenXR_ForceSolidClearScenes?.Value ?? string.Empty;
            if (string.Equals(raw, _cachedForceSolidClearScenesRaw, StringComparison.Ordinal))
            {
                return;
            }

            _cachedForceSolidClearScenesRaw = raw;
            _forceSolidClearSceneNames.Clear();

            string[] tokens = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string sceneName = token.Trim();
                if (sceneName.Length > 0)
                {
                    _forceSolidClearSceneNames.Add(sceneName);
                }
            }
        }

        private bool ShouldForceSolidClearStateForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            RefreshForceSolidClearScenesCacheIfNeeded();
            return _forceSolidClearSceneNames.Contains(sceneName);
        }

        private int GetVrRigLayerMask()
        {
            if (_vrRig == null) return 0;
            return 1 << _vrRig.layer;
        }

        private void UpdateInputStates()
        {
            if (_inputActionSet == OpenXRConstants.XR_NULL_HANDLE)
            {
                return;
            }

            IntPtr pActiveActionSet = IntPtr.Zero;
            try
            {
                pActiveActionSet = Marshal.AllocHGlobal(Marshal.SizeOf<XrActiveActionSet>());
                Marshal.StructureToPtr(new XrActiveActionSet { actionSet = _inputActionSet, subactionPath = OpenXRConstants.XR_NULL_PATH }, pActiveActionSet, false);

                var syncInfo = new XrActionsSyncInfo
                {
                    type = XrStructureType.XR_TYPE_ACTIONS_SYNC_INFO,
                    countActiveActionSets = 1,
                    activeActionSets = pActiveActionSet
                };

                XrResult syncResult = OpenXRAPI.xrSyncActions(_xrSession, in syncInfo);
                if (syncResult < 0)
                {
                    if (!_inputSyncErrorLogged)
                    {
                        VRModCore.LogWarning($"[Input][OpenXR] xrSyncActions failed: {syncResult}");
                        _inputSyncErrorLogged = true;
                    }
                    return;
                }

                _inputSyncErrorLogged = false;
                PollTriggerValueForHand("Left", _leftHandPath, ref _leftTriggerLogState);
                PollTriggerValueForHand("Right", _rightHandPath, ref _rightTriggerLogState);

                PollFloatActionForHand("Left", "Grip", _gripValueAction, _leftHandPath, ref _leftGripLogState);
                PollFloatActionForHand("Right", "Grip", _gripValueAction, _rightHandPath, ref _rightGripLogState);

                PollVector2ActionForHand("Left", "Thumbstick", _thumbstickAxisAction, _leftHandPath, ref _leftThumbstickAxisLogState);
                PollVector2ActionForHand("Right", "Thumbstick", _thumbstickAxisAction, _rightHandPath, ref _rightThumbstickAxisLogState);

                PollBooleanActionForHand("Left", "Thumbstick Click", _thumbstickClickAction, _leftHandPath, ref _leftThumbstickClickLogState);
                PollBooleanActionForHand("Right", "Thumbstick Click", _thumbstickClickAction, _rightHandPath, ref _rightThumbstickClickLogState);
                PollBooleanActionForHand("Left", "X", _xClickAction, _leftHandPath, ref _leftXLogState);
                PollBooleanActionForHand("Left", "Y", _yClickAction, _leftHandPath, ref _leftYLogState);
                PollBooleanActionForHand("Right", "A", _aClickAction, _rightHandPath, ref _rightALogState);
                PollBooleanActionForHand("Right", "B", _bClickAction, _rightHandPath, ref _rightBLogState);
            }
            finally
            {
                if (pActiveActionSet != IntPtr.Zero) Marshal.FreeHGlobal(pActiveActionSet);
            }
        }

        private void PollTriggerValueForHand(string handName, ulong subactionPath, ref OpenXrTriggerLogState previousState)
        {
            if (_triggerValueAction == OpenXRConstants.XR_NULL_HANDLE || subactionPath == OpenXRConstants.XR_NULL_PATH) return;

            var getInfo = new XrActionStateGetInfo
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_GET_INFO,
                action = _triggerValueAction,
                subactionPath = subactionPath
            };

            var actionState = new XrActionStateFloat
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_FLOAT
            };

            XrResult getStateResult = OpenXRAPI.xrGetActionStateFloat(_xrSession, in getInfo, ref actionState);
            if (getStateResult < 0)
            {
                if (!previousState.HasSample)
                {
                    VRModCore.LogWarning($"[Input][OpenXR][{handName}] Trigger xrGetActionStateFloat failed: {getStateResult}");
                }
                return;
            }

            bool isActive = actionState.isActive == XrBool32.XR_TRUE;
            float currentValue = isActive ? actionState.currentState : 0f;
            bool wasPressed = previousState.HasSample && previousState.IsPressed;
            bool isPressed = isActive &&
                             (wasPressed
                                 ? currentValue >= TriggerReleaseThreshold
                                 : currentValue >= TriggerPressThreshold);

            if (!previousState.HasSample || isActive != previousState.IsActive)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] Trigger action {(isActive ? "Active" : "Inactive")}");
            }

            bool triggerEdge = (previousState.HasSample && isPressed != previousState.IsPressed) ||
                               (!previousState.HasSample && isPressed);
            if (triggerEdge)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] Trigger {(isPressed ? "Pressed" : "Released")}");
            }

            if (isActive && (!previousState.HasSample || !previousState.IsActive || Mathf.Abs(currentValue - previousState.Value) >= FloatLogDelta))
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] Trigger value {currentValue:F2}");
            }

            previousState = new OpenXrTriggerLogState
            {
                HasSample = true,
                IsActive = isActive,
                Value = currentValue,
                IsPressed = isPressed
            };
        }

        private void PollBooleanActionForHand(string handName, string actionLabel, ulong actionHandle, ulong subactionPath, ref OpenXrBooleanLogState previousState)
        {
            if (actionHandle == OpenXRConstants.XR_NULL_HANDLE || subactionPath == OpenXRConstants.XR_NULL_PATH) return;

            var getInfo = new XrActionStateGetInfo
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_GET_INFO,
                action = actionHandle,
                subactionPath = subactionPath
            };

            var actionState = new XrActionStateBoolean
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_BOOLEAN
            };

            XrResult getStateResult = OpenXRAPI.xrGetActionStateBoolean(_xrSession, in getInfo, ref actionState);
            if (getStateResult < 0)
            {
                if (!previousState.HasSample)
                {
                    VRModCore.LogWarning($"[Input][OpenXR][{handName}] {actionLabel} xrGetActionStateBoolean failed: {getStateResult}");
                }
                return;
            }

            bool isActive = actionState.isActive == XrBool32.XR_TRUE;
            bool isPressed = isActive && actionState.currentState == XrBool32.XR_TRUE;

            if (!previousState.HasSample || isActive != previousState.IsActive)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} action {(isActive ? "Active" : "Inactive")}");
            }

            if (isActive && (!previousState.HasSample || isPressed != previousState.IsPressed))
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} {(isPressed ? "Pressed" : "Released")}");
            }

            previousState = new OpenXrBooleanLogState
            {
                HasSample = true,
                IsActive = isActive,
                IsPressed = isPressed
            };
        }

        private void PollFloatActionForHand(string handName, string actionLabel, ulong actionHandle, ulong subactionPath, ref OpenXrFloatLogState previousState)
        {
            if (actionHandle == OpenXRConstants.XR_NULL_HANDLE || subactionPath == OpenXRConstants.XR_NULL_PATH) return;

            var getInfo = new XrActionStateGetInfo
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_GET_INFO,
                action = actionHandle,
                subactionPath = subactionPath
            };

            var actionState = new XrActionStateFloat
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_FLOAT
            };

            XrResult getStateResult = OpenXRAPI.xrGetActionStateFloat(_xrSession, in getInfo, ref actionState);
            if (getStateResult < 0)
            {
                if (!previousState.HasSample)
                {
                    VRModCore.LogWarning($"[Input][OpenXR][{handName}] {actionLabel} xrGetActionStateFloat failed: {getStateResult}");
                }
                return;
            }

            bool isActive = actionState.isActive == XrBool32.XR_TRUE;
            float currentValue = isActive ? actionState.currentState : 0f;

            if (!previousState.HasSample || isActive != previousState.IsActive)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} action {(isActive ? "Active" : "Inactive")}");
            }

            if (isActive && (!previousState.HasSample || !previousState.IsActive || Mathf.Abs(currentValue - previousState.Value) >= FloatLogDelta))
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} value {currentValue:F2}");
            }

            previousState = new OpenXrFloatLogState
            {
                HasSample = true,
                IsActive = isActive,
                Value = currentValue
            };
        }

        private void PollVector2ActionForHand(string handName, string actionLabel, ulong actionHandle, ulong subactionPath, ref OpenXrVector2LogState previousState)
        {
            if (actionHandle == OpenXRConstants.XR_NULL_HANDLE || subactionPath == OpenXRConstants.XR_NULL_PATH) return;

            var getInfo = new XrActionStateGetInfo
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_GET_INFO,
                action = actionHandle,
                subactionPath = subactionPath
            };

            var actionState = new XrActionStateVector2f
            {
                type = XrStructureType.XR_TYPE_ACTION_STATE_VECTOR2F
            };

            XrResult getStateResult = OpenXRAPI.xrGetActionStateVector2f(_xrSession, in getInfo, ref actionState);
            if (getStateResult < 0)
            {
                if (!previousState.HasSample)
                {
                    VRModCore.LogWarning($"[Input][OpenXR][{handName}] {actionLabel} xrGetActionStateVector2f failed: {getStateResult}");
                }
                return;
            }

            bool isActive = actionState.isActive == XrBool32.XR_TRUE;
            float currentX = isActive ? actionState.currentState.x : 0f;
            float currentY = isActive ? actionState.currentState.y : 0f;

            if (!previousState.HasSample || isActive != previousState.IsActive)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} action {(isActive ? "Active" : "Inactive")}");
            }

            bool axisChanged = !previousState.HasSample ||
                               !previousState.IsActive ||
                               Mathf.Abs(currentX - previousState.X) >= AxisLogDelta ||
                               Mathf.Abs(currentY - previousState.Y) >= AxisLogDelta;
            if (isActive && axisChanged)
            {
                VRModCore.Log($"[Input][OpenXR][{handName}] {actionLabel} axis ({currentX:F2}, {currentY:F2})");
            }

            previousState = new OpenXrVector2LogState
            {
                HasSample = true,
                IsActive = isActive,
                X = currentX,
                Y = currentY
            };
        }

        private void TeardownInputActions()
        {
            if (_leftAimSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
            {
                OpenXRAPI.xrDestroySpace(_leftAimSpace);
            }

            if (_leftGripSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
            {
                OpenXRAPI.xrDestroySpace(_leftGripSpace);
            }

            if (_rightGripSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
            {
                OpenXRAPI.xrDestroySpace(_rightGripSpace);
            }

            if (_rightAimSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
            {
                OpenXRAPI.xrDestroySpace(_rightAimSpace);
            }

            if (_bClickAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_bClickAction);
            }

            if (_xClickAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_xClickAction);
            }
            if (_yClickAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_yClickAction);
            }

            if (_leftAimPoseAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_leftAimPoseAction);
            }

            if (_aClickAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_aClickAction);
            }

            if (_rightAimPoseAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_rightAimPoseAction);
            }

            if (_rightGripPoseAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_rightGripPoseAction);
            }

            if (_leftGripPoseAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_leftGripPoseAction);
            }

            if (_thumbstickClickAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_thumbstickClickAction);
            }

            if (_thumbstickAxisAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_thumbstickAxisAction);
            }

            if (_gripValueAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_gripValueAction);
            }

            if (_hapticAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_hapticAction);
            }

            if (_triggerValueAction != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyAction != null)
            {
                OpenXRAPI.xrDestroyAction(_triggerValueAction);
            }

            if (_inputActionSet != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyActionSet != null)
            {
                OpenXRAPI.xrDestroyActionSet(_inputActionSet);
            }

            _bClickAction = OpenXRConstants.XR_NULL_HANDLE;
            _yClickAction = OpenXRConstants.XR_NULL_HANDLE;
            _leftAimPoseAction = OpenXRConstants.XR_NULL_HANDLE;
            _aClickAction = OpenXRConstants.XR_NULL_HANDLE;
            _leftAimSpace = OpenXRConstants.XR_NULL_HANDLE;
            _rightAimPoseAction = OpenXRConstants.XR_NULL_HANDLE;
            _rightAimSpace = OpenXRConstants.XR_NULL_HANDLE;
            _leftGripPoseAction = OpenXRConstants.XR_NULL_HANDLE;
            _leftGripSpace = OpenXRConstants.XR_NULL_HANDLE;
            _rightGripPoseAction = OpenXRConstants.XR_NULL_HANDLE;
            _rightGripSpace = OpenXRConstants.XR_NULL_HANDLE;
            _thumbstickClickAction = OpenXRConstants.XR_NULL_HANDLE;
            _thumbstickAxisAction = OpenXRConstants.XR_NULL_HANDLE;
            _gripValueAction = OpenXRConstants.XR_NULL_HANDLE;
            _hapticAction = OpenXRConstants.XR_NULL_HANDLE;
            _triggerValueAction = OpenXRConstants.XR_NULL_HANDLE;
            _inputActionSet = OpenXRConstants.XR_NULL_HANDLE;
            _leftHandPath = OpenXRConstants.XR_NULL_PATH;
            _rightHandPath = OpenXRConstants.XR_NULL_PATH;
            _leftTriggerLogState = default;
            _rightTriggerLogState = default;
            _leftGripLogState = default;
            _rightGripLogState = default;
            _leftThumbstickAxisLogState = default;
            _rightThumbstickAxisLogState = default;
            _leftThumbstickClickLogState = default;
            _rightThumbstickClickLogState = default;
            _leftXLogState = default;
            _leftYLogState = default;
            _rightALogState = default;
            _rightBLogState = default;
            _inputSyncErrorLogged = false;
            _leftAimPoseErrorLogged = false;
            _rightAimPoseErrorLogged = false;
            _leftGripPoseErrorLogged = false;
            _rightGripPoseErrorLogged = false;
        }

        private void SyncOpenXrPostProcessing(Camera mainCamera)
        {
            if (mainCamera == null || _leftVrCamera == null || _rightVrCamera == null)
            {
                return;
            }

            if (ConfigManager.OpenXR_DisablePostFxSync?.Value == true)
            {
                VRModCore.Log("[PostFX][OpenXR] Post-processing sync disabled by config. Skipping.");
                return;
            }

            bool sharedEyeCamera = ReferenceEquals(_leftVrCamera, _rightVrCamera);

            int leftMainCount = SyncSelectedPostProcessingForEye(mainCamera, _leftVrCamera, IsTargetMainEffectType, _leftVrCamera);
            int rightMainCount = sharedEyeCamera
                ? leftMainCount
                : SyncSelectedPostProcessingForEye(mainCamera, _rightVrCamera, IsTargetMainEffectType, _rightVrCamera);

            Camera hdrSource = FindHdrEffectSourceCamera(mainCamera);
            int leftHdrCount = 0;
            int rightHdrCount = 0;
            if (hdrSource != null)
            {
                _leftVrHdrEffectCamera = CreateHdrEffectCameraForEye("XrVrCamera_Left_HdrEffects", _leftVrCameraGO?.transform, hdrSource, _leftVrCamera);
                leftHdrCount = SyncSelectedPostProcessingForEye(hdrSource, _leftVrHdrEffectCamera, IsTargetHdrEffectType, _leftVrHdrEffectCamera);
                if (sharedEyeCamera)
                {
                    _rightVrHdrEffectCamera = _leftVrHdrEffectCamera;
                    rightHdrCount = leftHdrCount;
                }
                else
                {
                    _rightVrHdrEffectCamera = CreateHdrEffectCameraForEye("XrVrCamera_Right_HdrEffects", _rightVrCameraGO?.transform, hdrSource, _rightVrCamera);
                    rightHdrCount = SyncSelectedPostProcessingForEye(hdrSource, _rightVrHdrEffectCamera, IsTargetHdrEffectType, _rightVrHdrEffectCamera);
                }
            }
            else
            {
                _leftVrHdrEffectCamera = null;
                _rightVrHdrEffectCamera = null;
            }

            string hdrSourceName = hdrSource != null ? hdrSource.name : "None";
            VRModCore.Log(
                $"[PostFX][OpenXR] Synced main effects from '{mainCamera.name}' (L={leftMainCount}, R={rightMainCount}); HDR source='{hdrSourceName}' (L={leftHdrCount}, R={rightHdrCount}).");
        }

        private static Camera CreateHdrEffectCameraForEye(string name, Transform eyeParent, Camera sourceCamera, Camera eyeCamera)
        {
            if (eyeParent == null || sourceCamera == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(eyeParent, false);
            var camera = go.AddComponent<Camera>();

            camera.CopyFrom(sourceCamera);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.enabled = false;
            camera.targetTexture = null;

            SyncEffectCameraClipPlanes(camera, eyeCamera);
            return camera;
        }

        private static void SyncEffectCameraClipPlanes(Camera effectCamera, Camera eyeCamera)
        {
            if (effectCamera == null || eyeCamera == null) return;
            effectCamera.nearClipPlane = eyeCamera.nearClipPlane;
            effectCamera.farClipPlane = eyeCamera.farClipPlane;
        }

        private void RenderHdrEffectCameraForEye(int eyeIndex, Camera referenceEyeCamera, RenderTexture targetTexture)
        {
            if (referenceEyeCamera == null || targetTexture == null) return;

            Camera effectCamera = eyeIndex == 0 ? _leftVrHdrEffectCamera : _rightVrHdrEffectCamera;
            if (effectCamera == null) return;

            SyncEffectCameraClipPlanes(effectCamera, referenceEyeCamera);
            effectCamera.transform.SetPositionAndRotation(referenceEyeCamera.transform.position, referenceEyeCamera.transform.rotation);
            effectCamera.worldToCameraMatrix = referenceEyeCamera.worldToCameraMatrix;
            effectCamera.projectionMatrix = referenceEyeCamera.projectionMatrix;
            effectCamera.targetTexture = targetTexture;
            effectCamera.Render();
            effectCamera.targetTexture = null;
        }

        private int SyncSelectedPostProcessingForEye(Camera sourceCamera, Camera targetCamera, Func<Type, bool> filter, Camera replacementCamera)
        {
            if (sourceCamera == null || targetCamera == null || filter == null) return 0;

            ClearSyncedPostFxForCamera(targetCamera);
            _renderOverrideEyeRetoggledCameraIds.Remove(targetCamera.GetInstanceID());
            targetCamera.depthTextureMode = sourceCamera.depthTextureMode;

            MonoBehaviour[] sourceBehaviours = sourceCamera.GetComponents<MonoBehaviour>();
            if (sourceBehaviours == null || sourceBehaviours.Length == 0)
            {
                return 0;
            }

            List<Component> createdComponents = new();
            int copiedCount = 0;
            foreach (MonoBehaviour sourceBehaviour in sourceBehaviours)
            {
                if (sourceBehaviour == null) continue;

                Type sourceType = sourceBehaviour.GetType();
                if (!filter(sourceType)) continue;

                try
                {
                    Component created = targetCamera.gameObject.AddComponent(sourceType);
                    if (created == null) continue;

                    CopyComponentFields(sourceBehaviour, created);
                    RemapCopiedComponentReferences(created, sourceCamera, replacementCamera);
                    TrySetInstanceCameraMember(created, replacementCamera);

                    if (created is Behaviour createdBehaviour && sourceBehaviour is Behaviour sourceAsBehaviour)
                    {
                        createdBehaviour.enabled = sourceAsBehaviour.enabled;
                    }

                    createdComponents.Add(created);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    VRModCore.LogRuntimeDebug($"[PostFX][OpenXR] Failed to clone '{sourceType.FullName}' on '{targetCamera.name}': {ex.Message}");
                }
            }

            if (createdComponents.Count > 0)
            {
                _syncedPostFxComponents[targetCamera] = createdComponents;
            }

            return copiedCount;
        }

        private void EnsureRenderOverrideEyeRetoggled(Camera targetCamera)
        {
            if (targetCamera == null) return;

            int cameraId = targetCamera.GetInstanceID();
            if (_renderOverrideEyeRetoggledCameraIds.Contains(cameraId))
            {
                return;
            }

            MonoBehaviour[] behaviours = targetCamera.GetComponents<MonoBehaviour>();
            if (behaviours != null)
            {
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null) continue;
                    if (!string.Equals(behaviour.GetType().Name, "RenderOverrideEye", StringComparison.OrdinalIgnoreCase)) continue;

                    if (behaviour.enabled)
                    {
                        behaviour.enabled = false;
                        behaviour.enabled = true;
                    }
                }
            }

            _renderOverrideEyeRetoggledCameraIds.Add(cameraId);
        }

        private void ClearSyncedPostFxForCamera(Camera targetCamera)
        {
            if (targetCamera == null) return;
            if (!_syncedPostFxComponents.TryGetValue(targetCamera, out List<Component> components)) return;

            for (int i = 0; i < components.Count; i++)
            {
                Component component = components[i];
                if (component != null)
                {
                    UnityEngine.Object.Destroy(component);
                }
            }

            _syncedPostFxComponents.Remove(targetCamera);
        }

        private void ClearAllSyncedPostFxComponents()
        {
            foreach (var kvp in _syncedPostFxComponents)
            {
                List<Component> components = kvp.Value;
                if (components == null) continue;

                for (int i = 0; i < components.Count; i++)
                {
                    Component component = components[i];
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                    }
                }
            }

            _syncedPostFxComponents.Clear();
            _renderOverrideEyeRetoggledCameraIds.Clear();
        }

        private static Camera FindHdrEffectSourceCamera(Camera mainCamera)
        {
            if (mainCamera == null) return null;

            Camera best = null;
            Camera[] childCameras = mainCamera.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < childCameras.Length; i++)
            {
                Camera camera = childCameras[i];
                if (camera == null || camera == mainCamera) continue;
                if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
                if (camera.targetTexture != null) continue;
                if (!CameraHasTargetPostFx(camera, IsTargetHdrEffectType)) continue;

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            if (best != null) return best;

            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null) continue;
                if (camera == mainCamera) continue;
                if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
                if (camera.targetTexture != null) continue;
                if (camera.gameObject.scene != mainCamera.gameObject.scene) continue;
                if (camera.name.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!CameraHasTargetPostFx(camera, IsTargetHdrEffectType)) continue;

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            return best;
        }

        private static bool CameraHasTargetPostFx(Camera camera, Func<Type, bool> filter)
        {
            if (camera == null || filter == null) return false;

            MonoBehaviour[] sourceBehaviours = camera.GetComponents<MonoBehaviour>();
            if (sourceBehaviours == null || sourceBehaviours.Length == 0) return false;

            for (int i = 0; i < sourceBehaviours.Length; i++)
            {
                MonoBehaviour sourceBehaviour = sourceBehaviours[i];
                if (sourceBehaviour == null) continue;
                if (filter(sourceBehaviour.GetType())) return true;
            }

            return false;
        }

        private static bool IsTargetMainEffectType(Type sourceType)
        {
            if (sourceType == null) return false;

            string typeName = sourceType.Name ?? string.Empty;
            if (string.Equals(typeName, "RenderOverrideEye", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(typeName, "Beautify", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(typeName, "BeautifyDistance", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(typeName, "NoiseAndScratches", StringComparison.OrdinalIgnoreCase)) return true;
            if (typeName.StartsWith("CameraFilterPack_", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
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

        private void LogReferenceCameraDiagnostics(Camera mainCamera, string sceneName, bool useFallbackRenderState)
        {
            if (mainCamera == null) return;

            string mainCameraPath = GetTransformPath(mainCamera.transform);
            string mainCameraSummary = DescribeCamera(mainCamera);
            VRModCore.Log($"[CameraDiag][OpenXR] Scene='{sceneName}', Main='{mainCamera.name}', Path='{mainCameraPath}', UseFallbackRenderState={useFallbackRenderState}, IsSynthetic2D={_isUsing2dSyntheticFallbackCamera}, ForcedDefault={_isUsingForcedDefaultRenderState}, ForcedSolidClear={_isUsingForcedSolidClearState}");
            VRModCore.Log($"[CameraDiag][OpenXR] Main settings: {mainCameraSummary}");

            List<string> mainFx = CollectTargetEffects(mainCamera, IsTargetMainEffectType);
            if (mainFx.Count > 0)
            {
                VRModCore.Log($"[CameraDiag][OpenXR] Main target FX ({mainFx.Count}): {string.Join(", ", mainFx)}");
            }
            else
            {
                VRModCore.Log("[CameraDiag][OpenXR] Main target FX: none");
            }

            Camera hdrSource = FindHdrEffectSourceCamera(mainCamera);
            if (hdrSource != null)
            {
                string hdrPath = GetTransformPath(hdrSource.transform);
                string hdrSummary = DescribeCamera(hdrSource);
                VRModCore.Log($"[CameraDiag][OpenXR] HDR source='{hdrSource.name}', Path='{hdrPath}', Settings={hdrSummary}");

                List<string> hdrFx = CollectTargetEffects(hdrSource, IsTargetHdrEffectType);
                if (hdrFx.Count > 0)
                {
                    VRModCore.Log($"[CameraDiag][OpenXR] HDR target FX ({hdrFx.Count}): {string.Join(", ", hdrFx)}");
                }
                else
                {
                    VRModCore.Log("[CameraDiag][OpenXR] HDR target FX: none");
                }
            }
            else
            {
                VRModCore.Log("[CameraDiag][OpenXR] HDR source: none");
            }
        }

        private static List<string> CollectTargetEffects(Camera camera, Func<Type, bool> filter)
        {
            List<string> result = [];
            if (camera == null || filter == null) return result;

            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            if (behaviours == null || behaviours.Length == 0) return result;

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                Type type = behaviour.GetType();
                if (!filter(type)) continue;

                bool enabled = behaviour is Behaviour b && b.enabled;
                string typeName = type.FullName ?? type.Name ?? "UnknownType";
                result.Add($"{typeName}[{(enabled ? "On" : "Off")}]");
            }

            return result;
        }

        private static string DescribeCamera(Camera camera)
        {
            if (camera == null) return "null";

            string targetTextureName = camera.targetTexture != null ? camera.targetTexture.name : "null";
            return
                $"enabled={camera.enabled}, clearFlags={camera.clearFlags}, bg=({camera.backgroundColor.r:F2},{camera.backgroundColor.g:F2},{camera.backgroundColor.b:F2},{camera.backgroundColor.a:F2}), " +
                $"cullingMask=0x{camera.cullingMask:X8}, depth={camera.depth:F2}, depthTextureMode={camera.depthTextureMode}, renderingPath={camera.renderingPath}, allowHDR={camera.allowHDR}, allowMSAA={camera.allowMSAA}, " +
                $"orthographic={camera.orthographic}, near={camera.nearClipPlane:F3}, far={camera.farClipPlane:F3}, targetTexture={targetTextureName}";
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            StringBuilder sb = new();
            Transform current = transform;
            while (current != null)
            {
                if (sb.Length == 0)
                {
                    sb.Insert(0, current.name);
                }
                else
                {
                    sb.Insert(0, '/');
                    sb.Insert(0, current.name);
                }
                current = current.parent;
            }
            return sb.ToString();
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

        private static bool TryGetCameraTypesInitialPosition(Camera mainCamera, out Vector3 initialPosition)
        {
            initialPosition = default;
            if (mainCamera == null) return false;

            Component cameraTypesComponent = mainCamera.GetComponent("CameraTypes");
            if (cameraTypesComponent == null) return false;

            try
            {
                Type componentType = cameraTypesComponent.GetType();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                PropertyInfo initialPositionProperty = componentType.GetProperty("initialPosition", flags);
                if (initialPositionProperty != null && initialPositionProperty.CanRead)
                {
                    object value = initialPositionProperty.GetValue(cameraTypesComponent, null);
                    if (value is Vector3 propertyPos)
                    {
                        initialPosition = propertyPos;
                        return true;
                    }
                }

                FieldInfo initialPositionField = componentType.GetField("initialPosition", flags);
                if (initialPositionField != null)
                {
                    object value = initialPositionField.GetValue(cameraTypesComponent);
                    if (value is Vector3 fieldPos)
                    {
                        initialPosition = fieldPos;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                VRModCore.LogRuntimeDebug($"Failed to read CameraTypes.initialPosition: {ex.Message}");
            }

            return false;
        }

        private void UpdateMainCameraLookAtTargetFollow(Camera mainCamera, bool hasHmdPose, Vector3 hmdWorldPos)
        {
            if (_isUsing2dSyntheticFallbackCamera) return;
            if (mainCamera == null) return;

            Vector3 followWorldPos = hmdWorldPos;
            bool hasFollowWorldPos = hasHmdPose;
            if (TryGetVrEyeMidpointWorld(out Vector3 vrEyeMidpoint))
            {
                followWorldPos = vrEyeMidpoint;
                hasFollowWorldPos = true;
            }

            if (_mainCameraLookAtTarget == null && Time.time >= _nextLookAtTargetResolveTime)
            {
                _nextLookAtTargetResolveTime = Time.time + LookAtTargetResolveIntervalSeconds;
                ResolveMainCameraLookAtTarget(mainCamera, force: true);
            }

            if (_mainCameraLookAtTarget != null && hasFollowWorldPos)
            {
                _mainCameraLookAtTarget.position = followWorldPos;
            }

        }

        private bool TryGetVrEyeMidpointWorld(out Vector3 midpointWorld)
        {
            midpointWorld = default;

            if (_leftVrCamera != null && _rightVrCamera != null)
            {
                midpointWorld = (_leftVrCamera.transform.position + _rightVrCamera.transform.position) * 0.5f;
                return true;
            }

            if (_leftVrCamera != null)
            {
                midpointWorld = _leftVrCamera.transform.position;
                return true;
            }

            if (_rightVrCamera != null)
            {
                midpointWorld = _rightVrCamera.transform.position;
                return true;
            }

            return false;
        }

        private void ResolveMainCameraLookAtTarget(Camera mainCamera, bool force)
        {
            if (mainCamera == null) return;
            if (!force && _mainCameraLookAtTarget != null) return;

            Transform root = mainCamera.transform;
            Transform direct = root.Find("LookAtTarget");
            if (direct != null)
            {
                _mainCameraLookAtTarget = direct;
                return;
            }

            _mainCameraLookAtTarget = FindChildByName(root, "LookAtTarget");
        }

        private static Transform FindChildByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName)) return null;

            int childCount = root.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                Transform nested = FindChildByName(child, targetName);
                if (nested != null) return nested;
            }

            return null;
        }

        public void TeardownCameraRig()
        {
            VRModCore.LogRuntimeDebug("Tearing down VR camera rig.");
            _locomotion.Teardown();
            _controllerVisualizer.Teardown();
            _uiProjectionPlane.Teardown();
            _uiInteractor.Teardown();
            _danmenProjectionPlane.Teardown();
            ClearAllSyncedPostFxComponents();
            _isUsing2dSyntheticFallbackCamera = false;
            _isUsingForcedDefaultRenderState = false;
            _isUsingForcedSolidClearState = false;
            if (_leftEyeIntermediateRT != null) { _leftEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_leftEyeIntermediateRT); _leftEyeIntermediateRT = null; }
            if (_rightEyeIntermediateRT != null) { _rightEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_rightEyeIntermediateRT); _rightEyeIntermediateRT = null; }
            if (_vrRig != null) { UnityEngine.Object.Destroy(_vrRig); _vrRig = null; }
            _leftVrCameraGO = null; _leftVrCamera = null; _rightVrCameraGO = null; _rightVrCamera = null;
            _leftVrHdrEffectCamera = null; _rightVrHdrEffectCamera = null;
            _currentlyTrackedOriginalCameraGO = null;
            _mainCameraLookAtTarget = null;
            _nextLookAtTargetResolveTime = 0f;
            _wasPlaneEditTogglePressed = false;
            _wasPlaneEditTriggerPressed = false;
            _wasUiTogglePressed = false;
            _wasXOrAPressed = false;
            _lastXaPressTime = -1f;
            _xaClickPendingTime = -1f;
            _wasLeftTriggerPressed = false;
            _wasRightTriggerPressed = false;
            _uiClickHand = null;
            _activeGripHand = null;
            _lastActiveGripHand = null;
            _activePlaneEditSelection = PlaneEditSelection.None;
            _activePlaneEditHandLocalPos = Vector3.zero;
            _activePlaneEditHandLocalRot = Quaternion.identity;
            _activePlaneEditMode = PlaneEditMode.None;
            _activePlaneEditResizeHandleIndex = -1;
            _activePlaneEditResizeStartHandDistance = 0f;
            _activePlaneEditResizeStartScale = 1f;
            _isTeleportAimingByStick = false;
            _isSubCameraMoveModeActive = false;
            _subCameraMoveAHoldSeconds = 0f;
            _subCameraMoveAToggleHoldConsumed = false;
            _wasSubCameraAButtonPressed = false;
            _wasSubCameraBButtonPressed = false;
            _subCameraMoveStickClickHoldSeconds = 0f;
#if MONO
            CameraTypesVrFollowPatch.ResetSubCameraMoveInput();
#endif
            SetPlaneEditEdgeHighlight(PlaneEditSelection.None);
        }

        public void TeardownVr()
        {
            VRModCore.LogRuntimeDebug("Tearing down OpenXR system.");
            TeardownCameraRig();
            TeardownInputActions();

            if (_pProjectionLayerViews != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayerViews); _pProjectionLayerViews = IntPtr.Zero; }
            if (_pProjectionLayer != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayer); _pProjectionLayer = IntPtr.Zero; }
            if (_pLayersForSubmit != IntPtr.Zero) { Marshal.FreeHGlobal(_pLayersForSubmit); _pLayersForSubmit = IntPtr.Zero; }

            if (_appSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
                OpenXRAPI.xrDestroySpace(_appSpace);

            if (_eyeSwapchainSRVs != null)
            {
                foreach (var srvList in _eyeSwapchainSRVs)
                    foreach (var srv in srvList)
                        if (srv != IntPtr.Zero) NativeBridge.ReleaseNativeObject_Internal(srv);
            }

            if (_eyeSwapchains.Count > 0)
            {
                foreach (ulong sc in _eyeSwapchains)
                    if (sc != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySwapchain != null)
                        OpenXRAPI.xrDestroySwapchain(sc);
            }

            if (_xrSession != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySession != null)
                OpenXRAPI.xrDestroySession(_xrSession);

            if (_xrInstance != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyInstance != null)
                OpenXRAPI.xrDestroyInstance(_xrInstance);

            _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
            _xrSession = OpenXRConstants.XR_NULL_HANDLE;
            _appSpace = OpenXRConstants.XR_NULL_HANDLE;

            OpenXRNativeLoader.FreeOpenXRLibrary();
            IsVrAvailable = false;
        }

        private static Matrix4x4 CreateProjectionMatrixFromFovUsingFrustum(XrFovf fov, float nearClip, float farClip)
        {
            float nPR = Mathf.Tan(fov.angleRight) * nearClip;
            float nPL = Mathf.Tan(fov.angleLeft) * nearClip;
            float nPT = Mathf.Tan(fov.angleUp) * nearClip;
            float nPB = Mathf.Tan(fov.angleDown) * nearClip;

            Matrix4x4 m = new();
            m[0, 0] = (2.0f * nearClip) / (nPR - nPL);
            m[0, 2] = (nPR + nPL) / (nPR - nPL);
            m[1, 1] = (2.0f * nearClip) / (nPT - nPB);
            m[1, 2] = (nPT + nPB) / (nPT - nPB);
            m[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
            m[2, 3] = -(2.0f * farClip * nearClip) / (farClip - nearClip);
            m[3, 2] = -1.0f;
            return m;
        }

        public void SetCameraNearClip(float newNearClipBaseValue)
        {
            if (_vrRig == null) return;
            Camera mainCamera = null;
            if (_currentlyTrackedOriginalCameraGO != null)
                mainCamera = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
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

            UpdateVerticalOffset();
        }

        public void SetUserEyeHeightOffset(float newOffset)
        {
            UpdateVerticalOffset();
        }

        public VrCameraRig GetVrCameraGameObjects()
        {
            return new VrCameraRig { LeftEye = _leftVrCameraGO, RightEye = _rightVrCameraGO };
        }
    }
}
