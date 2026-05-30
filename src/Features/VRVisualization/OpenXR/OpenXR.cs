using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VRVisualization.OpenXR
{
    #region --- Enums ---

    public enum XrResult
    {
        XR_SUCCESS = 0,
        XR_TIMEOUT_EXPIRED = 1,
        XR_SESSION_LOSS_PENDING = 3,
        XR_EVENT_UNAVAILABLE = 4,
        XR_SPACE_BOUNDS_UNAVAILABLE = 7,
        XR_SESSION_NOT_FOCUSED = 8,
        XR_FRAME_DISCARDED = 9,
        XR_ERROR_VALIDATION_FAILURE = -1,
        XR_ERROR_RUNTIME_FAILURE = -2,
        XR_ERROR_OUT_OF_MEMORY = -3,
        XR_ERROR_API_VERSION_UNSUPPORTED = -4,
        XR_ERROR_INITIALIZATION_FAILED = -6,
        XR_ERROR_FUNCTION_UNSUPPORTED = -7,
        XR_ERROR_FEATURE_UNSUPPORTED = -8,
        XR_ERROR_EXTENSION_NOT_PRESENT = -9,
        XR_ERROR_LIMIT_REACHED = -10,
        XR_ERROR_SIZE_INSUFFICIENT = -11,
        XR_ERROR_HANDLE_INVALID = -12,
        XR_ERROR_INSTANCE_LOST = -13,
        XR_ERROR_SESSION_RUNNING = -14,
        XR_ERROR_SESSION_NOT_RUNNING = -16,
        XR_ERROR_SESSION_LOST = -17,
        XR_ERROR_SYSTEM_INVALID = -18,
        XR_ERROR_PATH_INVALID = -19,
        XR_ERROR_PATH_COUNT_EXCEEDED = -20,
        XR_ERROR_PATH_FORMAT_INVALID = -21,
        XR_ERROR_PATH_UNSUPPORTED = -22,
        XR_ERROR_LAYER_INVALID = -23,
        XR_ERROR_LAYER_LIMIT_EXCEEDED = -24,
        XR_ERROR_SWAPCHAIN_RECT_INVALID = -25,
        XR_ERROR_SWAPCHAIN_FORMAT_UNSUPPORTED = -26,
        XR_ERROR_ACTION_TYPE_MISMATCH = -27,
        XR_ERROR_SESSION_NOT_READY = -28,
        XR_ERROR_SESSION_NOT_STOPPING = -29,
        XR_ERROR_TIME_INVALID = -30,
        XR_ERROR_REFERENCE_SPACE_UNSUPPORTED = -31,
        XR_ERROR_FILE_ACCESS_ERROR = -32,
        XR_ERROR_FILE_CONTENTS_INVALID = -33,
        XR_ERROR_FORM_FACTOR_UNSUPPORTED = -34,
        XR_ERROR_FORM_FACTOR_UNAVAILABLE = -35,
        XR_ERROR_API_LAYER_NOT_PRESENT = -36,
        XR_ERROR_CALL_ORDER_INVALID = -37,
        XR_ERROR_GRAPHICS_DEVICE_INVALID = -38,
        XR_ERROR_POSE_INVALID = -39,
        XR_ERROR_INDEX_OUT_OF_RANGE = -40,
        XR_ERROR_VIEW_CONFIGURATION_TYPE_UNSUPPORTED = -41,
        XR_ERROR_ENVIRONMENT_BLEND_MODE_UNSUPPORTED = -42,
        XR_ERROR_NAME_DUPLICATED = -44,
        XR_ERROR_NAME_INVALID = -45,
        XR_ERROR_ACTIONSET_NOT_ATTACHED = -46,
        XR_ERROR_ACTIONSETS_ALREADY_ATTACHED = -47,
        XR_ERROR_LOCALIZED_NAME_DUPLICATED = -48,
        XR_ERROR_LOCALIZED_NAME_INVALID = -49,
        XR_ERROR_GRAPHICS_REQUIREMENTS_CALL_MISSING = -50,
        XR_ERROR_RUNTIME_UNAVAILABLE = -51,
        XR_ERROR_EXTENSION_DEPENDENCY_NOT_ENABLED = -1000710001,
        XR_ERROR_PERMISSION_INSUFFICIENT = -1000710000,
    }

    public enum XrStructureType
    {
        XR_TYPE_UNKNOWN = 0,
        XR_TYPE_API_LAYER_PROPERTIES = 1,
        XR_TYPE_EXTENSION_PROPERTIES = 2,
        XR_TYPE_INSTANCE_CREATE_INFO = 3,
        XR_TYPE_SYSTEM_GET_INFO = 4,
        XR_TYPE_SYSTEM_PROPERTIES = 5,
        XR_TYPE_VIEW_LOCATE_INFO = 6,
        XR_TYPE_VIEW = 7,
        XR_TYPE_SESSION_CREATE_INFO = 8,
        XR_TYPE_SWAPCHAIN_CREATE_INFO = 9,
        XR_TYPE_SESSION_BEGIN_INFO = 10,
        XR_TYPE_VIEW_STATE = 11,
        XR_TYPE_FRAME_END_INFO = 12,
        XR_TYPE_HAPTIC_VIBRATION = 13,
        XR_TYPE_EVENT_DATA_BUFFER = 16,
        XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING = 17,
        XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED = 18,
        XR_TYPE_ACTION_STATE_BOOLEAN = 23,
        XR_TYPE_ACTION_STATE_FLOAT = 24,
        XR_TYPE_ACTION_STATE_VECTOR2F = 25,
        XR_TYPE_ACTION_STATE_POSE = 27,
        XR_TYPE_ACTION_SET_CREATE_INFO = 28,
        XR_TYPE_ACTION_CREATE_INFO = 29,
        XR_TYPE_FRAME_WAIT_INFO = 33,
        XR_TYPE_COMPOSITION_LAYER_PROJECTION = 35,
        XR_TYPE_REFERENCE_SPACE_CREATE_INFO = 37,
        XR_TYPE_ACTION_SPACE_CREATE_INFO = 38,
        XR_TYPE_SPACE_LOCATION = 42,
        XR_TYPE_FRAME_STATE = 44,
        XR_TYPE_FRAME_BEGIN_INFO = 46,
        XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW = 48,
        XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING = 51,
        XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO = 55,
        XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO = 56,
        XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO = 57,
        XR_TYPE_ACTION_STATE_GET_INFO = 58,
        XR_TYPE_HAPTIC_ACTION_INFO = 59,
        XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO = 60,
        XR_TYPE_ACTIONS_SYNC_INFO = 61,
        XR_TYPE_VIEW_CONFIGURATION_VIEW = 41,
        XR_TYPE_GRAPHICS_BINDING_D3D11_KHR = 1000027000,
        XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR = 1000027001,
        XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR = 1000027002,
    }

    public enum XrFormFactor { XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY = 1 }
    public enum XrEnvironmentBlendMode { XR_ENVIRONMENT_BLEND_MODE_OPAQUE = 1, XR_ENVIRONMENT_BLEND_MODE_ADDITIVE = 2, XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND = 3 }
    public enum D3D_FEATURE_LEVEL { D3D_FEATURE_LEVEL_11_0 = 0xb000, D3D_FEATURE_LEVEL_11_1 = 0xb100 }
    public enum XrViewConfigurationType { XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO = 2 }
    public enum XrSessionState { XR_SESSION_STATE_UNKNOWN = 0, XR_SESSION_STATE_IDLE = 1, XR_SESSION_STATE_READY = 2, XR_SESSION_STATE_SYNCHRONIZED = 3, XR_SESSION_STATE_VISIBLE = 4, XR_SESSION_STATE_FOCUSED = 5, XR_SESSION_STATE_STOPPING = 6, XR_SESSION_STATE_LOSS_PENDING = 7, XR_SESSION_STATE_EXITING = 8 }
    public enum XrReferenceSpaceType { XR_REFERENCE_SPACE_TYPE_VIEW = 1, XR_REFERENCE_SPACE_TYPE_LOCAL = 2, XR_REFERENCE_SPACE_TYPE_STAGE = 3 }
    public enum XrActionType { XR_ACTION_TYPE_BOOLEAN_INPUT = 1, XR_ACTION_TYPE_FLOAT_INPUT = 2, XR_ACTION_TYPE_VECTOR2F_INPUT = 3, XR_ACTION_TYPE_POSE_INPUT = 4, XR_ACTION_TYPE_VIBRATION_OUTPUT = 100 }
    [Flags] public enum XrSwapchainCreateFlags : ulong { None = 0 }
    [Flags] public enum XrSwapchainUsageFlags : ulong { XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT = 1, XR_SWAPCHAIN_USAGE_SAMPLED_BIT = 32 }
    [Flags] public enum XrViewStateFlags : ulong { XR_VIEW_STATE_ORIENTATION_VALID_BIT = 1, XR_VIEW_STATE_POSITION_VALID_BIT = 2 }
    [Flags] public enum XrSpaceLocationFlags : ulong
    {
        None = 0,
        XR_SPACE_LOCATION_ORIENTATION_VALID_BIT = 1,
        XR_SPACE_LOCATION_POSITION_VALID_BIT = 2,
        XR_SPACE_LOCATION_ORIENTATION_TRACKED_BIT = 4,
        XR_SPACE_LOCATION_POSITION_TRACKED_BIT = 8
    }
    [Flags]
    public enum XrCompositionLayerFlags : ulong
    {
        None = 0,
        XR_COMPOSITION_LAYER_CORRECT_CHROMATIC_ABERRATION_BIT = 0x00000001,
        XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT = 0x00000002,
        XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT = 0x00000004
    }
    #endregion

    #region --- Constants ---
    public static class OpenXRConstants
    {
        public const ulong XR_NULL_HANDLE = 0;
        public const ulong XR_NULL_SYSTEM_ID = 0;
        public const ulong XR_NULL_PATH = 0;
        public const long XR_INFINITE_DURATION = 0x7FFFFFFFFFFFFFFF;
        public const int XR_MAX_APPLICATION_NAME_SIZE = 128;
        public const int XR_MAX_ENGINE_NAME_SIZE = 128;
        public const int XR_MAX_SYSTEM_NAME_SIZE = 256;
        public const int XR_MAX_EXTENSION_NAME_SIZE = 128;
        public const int XR_MAX_ACTION_SET_NAME_SIZE = 64;
        public const int XR_MAX_ACTION_NAME_SIZE = 64;
        public const int XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE = 128;
        public const int XR_MAX_LOCALIZED_ACTION_NAME_SIZE = 128;
        public static ulong XR_API_VERSION_1_0 = (1UL << 48);
        public static ulong XR_API_VERSION_1_1 = (1UL << 48) | (1UL << 32);
        public const string XR_KHR_D3D11_ENABLE_EXTENSION_NAME = "XR_KHR_D3D11_enable";
    }
    public static class XrBool32 { public const uint XR_FALSE = 0; public const uint XR_TRUE = 1; }
    #endregion

    #region --- PFN Delegates ---
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetInstanceProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEnumerateInstanceExtensionProperties(string layerName, uint propertyCapacityInput, out uint propertyCountOutput, IntPtr properties);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateInstance(in XrInstanceCreateInfo createInfo, out ulong instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroyInstance(ulong instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetSystem(ulong instance, in XrSystemGetInfo getInfo, out ulong systemId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetSystemProperties(ulong instance, ulong systemId, ref XrSystemProperties properties);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetD3D11GraphicsRequirementsKHR(ulong instance, ulong systemId, out XrGraphicsRequirementsD3D11KHR graphicsRequirements);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateSession(ulong instance, in XrSessionCreateInfo createInfo, out ulong session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroySession(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrWaitFrame(ulong session, in XrFrameWaitInfo frameWaitInfo, out XrFrameState frameState);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrBeginFrame(ulong session, in XrFrameBeginInfo frameBeginInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEndFrame(ulong session, in XrFrameEndInfo frameEndInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrBeginSession(ulong session, in XrSessionBeginInfo beginInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrPollEvent(ulong instance, IntPtr eventDataBuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrStringToPath(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string pathString, out ulong path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateActionSet(ulong instance, in XrActionSetCreateInfo createInfo, out ulong actionSet);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroyActionSet(ulong actionSet);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateAction(ulong actionSet, in XrActionCreateInfo createInfo, out ulong action);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroyAction(ulong action);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrSuggestInteractionProfileBindings(ulong instance, in XrInteractionProfileSuggestedBinding suggestedBindings);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrAttachSessionActionSets(ulong session, in XrSessionActionSetsAttachInfo attachInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrSyncActions(ulong session, in XrActionsSyncInfo syncInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateBoolean(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateBoolean state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateFloat(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateFloat state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateVector2f(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateVector2f state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateActionSpace(ulong session, in XrActionSpaceCreateInfo createInfo, out ulong space);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrLocateSpace(ulong space, ulong baseSpace, long time, out XrSpaceLocation location);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEnumerateViewConfigurations(ulong instance, ulong systemId, uint viewConfigurationTypeCapacityInput, out uint viewConfigurationTypeCountOutput, IntPtr viewConfigurationTypes);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEnumerateViewConfigurationViews(ulong instance, ulong systemId, XrViewConfigurationType viewConfigurationType, uint viewCapacityInput, out uint viewCountOutput, IntPtr views);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEnumerateSwapchainFormats(ulong session, uint formatCapacityInput, out uint formatCountOutput, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] long[] formats);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateSwapchain(ulong session, in XrSwapchainCreateInfo createInfo, out ulong swapchain);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroySwapchain(ulong swapchain);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEnumerateSwapchainImages(ulong swapchain, uint imageCapacityInput, out uint imageCountOutput, IntPtr imagesBuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrLocateViews(ulong session, in XrViewLocateInfo viewLocateInfo, out XrViewState viewState, uint viewCapacityInput, out uint viewCountOutput, IntPtr viewsBuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateReferenceSpace(ulong session, in XrReferenceSpaceCreateInfo createInfo, out ulong space);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrDestroySpace(ulong space);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrAcquireSwapchainImage(ulong swapchain, in XrSwapchainImageAcquireInfo acquireInfo, out uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrWaitSwapchainImage(ulong swapchain, in XrSwapchainImageWaitInfo waitInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrReleaseSwapchainImage(ulong swapchain, in XrSwapchainImageReleaseInfo releaseInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrEndSession(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrApplyHapticFeedback(ulong session, in XrHapticActionInfo hapticActionInfo, in XrHapticVibration hapticFeedback);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrStopHapticFeedback(ulong session, in XrHapticActionInfo hapticActionInfo);
    #endregion

    #region --- Native Loader ---
    public static class OpenXRNativeLoader
    {
        private static IntPtr openxrLibHandle = IntPtr.Zero;
        public static PFN_xrGetInstanceProcAddr xrGetInstanceProcAddr_ptr_delegate;

        private static class Kernel32NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibrary(string lpFileName);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);
            [DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeLibrary(IntPtr hModule);
        }

        public static bool LoadOpenXRLibrary()
        {
            if (openxrLibHandle != IntPtr.Zero) return true;

            try
            {
                string modAssemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(modAssemblyDir)) throw new Exception("Failed to get mod assembly directory.");

                string fullPathToLoader = Path.Combine(modAssemblyDir, "openxr_loader.dll");
                if (!File.Exists(fullPathToLoader)) throw new FileNotFoundException("openxr_loader.dll not found in mod directory.", fullPathToLoader);

                openxrLibHandle = Kernel32NativeMethods.LoadLibrary(fullPathToLoader);
                if (openxrLibHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load openxr_loader.dll.");

                IntPtr pfnGetInstanceProcAddrRaw = Kernel32NativeMethods.GetProcAddress(openxrLibHandle, "xrGetInstanceProcAddr");
                if (pfnGetInstanceProcAddrRaw == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get address of xrGetInstanceProcAddr.");

                xrGetInstanceProcAddr_ptr_delegate = Marshal.GetDelegateForFunctionPointer<PFN_xrGetInstanceProcAddr>(pfnGetInstanceProcAddrRaw);
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during OpenXR library loading:", ex);
                FreeOpenXRLibrary();
                return false;
            }
        }

        public static void FreeOpenXRLibrary()
        {
            if (openxrLibHandle != IntPtr.Zero)
            {
                Kernel32NativeMethods.FreeLibrary(openxrLibHandle);
                openxrLibHandle = IntPtr.Zero;
                xrGetInstanceProcAddr_ptr_delegate = null;
            }
        }
    }
    #endregion

    #region --- API Function Storage & Loading ---
    public static partial class OpenXRAPI
    {
        private static PFN_xrGetInstanceProcAddr xrGetInstanceProcAddr_func_ptr;
        public static PFN_xrCreateInstance xrCreateInstance;
        public static PFN_xrEnumerateInstanceExtensionProperties xrEnumerateInstanceExtensionProperties;
        public static PFN_xrDestroyInstance xrDestroyInstance;
        public static PFN_xrGetSystem xrGetSystem;
        public static PFN_xrGetSystemProperties xrGetSystemProperties;
        public static PFN_xrGetD3D11GraphicsRequirementsKHR xrGetD3D11GraphicsRequirementsKHR;
        public static PFN_xrCreateSession xrCreateSession;
        public static PFN_xrDestroySession xrDestroySession;
        public static PFN_xrWaitFrame xrWaitFrame;
        public static PFN_xrBeginFrame xrBeginFrame;
        public static PFN_xrEndFrame xrEndFrame;
        public static PFN_xrBeginSession xrBeginSession;
        public static PFN_xrPollEvent xrPollEvent;
        public static PFN_xrStringToPath xrStringToPath;
        public static PFN_xrCreateActionSet xrCreateActionSet;
        public static PFN_xrDestroyActionSet xrDestroyActionSet;
        public static PFN_xrCreateAction xrCreateAction;
        public static PFN_xrDestroyAction xrDestroyAction;
        public static PFN_xrSuggestInteractionProfileBindings xrSuggestInteractionProfileBindings;
        public static PFN_xrAttachSessionActionSets xrAttachSessionActionSets;
        public static PFN_xrSyncActions xrSyncActions;
        public static PFN_xrGetActionStateBoolean xrGetActionStateBoolean;
        public static PFN_xrGetActionStateFloat xrGetActionStateFloat;
        public static PFN_xrGetActionStateVector2f xrGetActionStateVector2f;
        public static PFN_xrCreateActionSpace xrCreateActionSpace;
        public static PFN_xrLocateSpace xrLocateSpace;
        public static PFN_xrEnumerateViewConfigurations xrEnumerateViewConfigurations;
        public static PFN_xrEnumerateViewConfigurationViews xrEnumerateViewConfigurationViews;
        public static PFN_xrEnumerateSwapchainFormats xrEnumerateSwapchainFormats;
        public static PFN_xrCreateSwapchain xrCreateSwapchain;
        public static PFN_xrDestroySwapchain xrDestroySwapchain;
        public static PFN_xrEnumerateSwapchainImages xrEnumerateSwapchainImages;
        public static PFN_xrLocateViews xrLocateViews;
        public static PFN_xrCreateReferenceSpace xrCreateReferenceSpace;
        public static PFN_xrDestroySpace xrDestroySpace;
        public static PFN_xrAcquireSwapchainImage xrAcquireSwapchainImage;
        public static PFN_xrWaitSwapchainImage xrWaitSwapchainImage;
        public static PFN_xrReleaseSwapchainImage xrReleaseSwapchainImage;
        public static PFN_xrEndSession xrEndSession;
        public static PFN_xrApplyHapticFeedback xrApplyHapticFeedback;
        public static PFN_xrStopHapticFeedback xrStopHapticFeedback;

        public static bool InitializeCoreFunctions(PFN_xrGetInstanceProcAddr getInstanceProcAddrEntry)
        {
            xrGetInstanceProcAddr_func_ptr = getInstanceProcAddrEntry ?? throw new ArgumentNullException(nameof(getInstanceProcAddrEntry));
            xrCreateInstance = GetXrFunction<PFN_xrCreateInstance>(OpenXRConstants.XR_NULL_HANDLE, xrGetInstanceProcAddr_func_ptr);
            xrEnumerateInstanceExtensionProperties = GetXrFunction<PFN_xrEnumerateInstanceExtensionProperties>(OpenXRConstants.XR_NULL_HANDLE, xrGetInstanceProcAddr_func_ptr);
            return true;
        }

        public static bool InitializeInstanceFunctions(ulong instanceHandle)
        {
            if (instanceHandle == OpenXRConstants.XR_NULL_HANDLE || xrGetInstanceProcAddr_func_ptr == null) return false;
            try
            {
                xrDestroyInstance = GetXrFunction<PFN_xrDestroyInstance>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetSystem = GetXrFunction<PFN_xrGetSystem>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetSystemProperties = GetXrFunction<PFN_xrGetSystemProperties>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetD3D11GraphicsRequirementsKHR = GetXrFunction<PFN_xrGetD3D11GraphicsRequirementsKHR>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateSession = GetXrFunction<PFN_xrCreateSession>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrDestroySession = GetXrFunction<PFN_xrDestroySession>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrWaitFrame = GetXrFunction<PFN_xrWaitFrame>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrBeginFrame = GetXrFunction<PFN_xrBeginFrame>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEndFrame = GetXrFunction<PFN_xrEndFrame>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrBeginSession = GetXrFunction<PFN_xrBeginSession>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrPollEvent = GetXrFunction<PFN_xrPollEvent>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrStringToPath = GetXrFunction<PFN_xrStringToPath>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateActionSet = GetXrFunction<PFN_xrCreateActionSet>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrDestroyActionSet = GetXrFunction<PFN_xrDestroyActionSet>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateAction = GetXrFunction<PFN_xrCreateAction>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrDestroyAction = GetXrFunction<PFN_xrDestroyAction>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrSuggestInteractionProfileBindings = GetXrFunction<PFN_xrSuggestInteractionProfileBindings>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrAttachSessionActionSets = GetXrFunction<PFN_xrAttachSessionActionSets>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrSyncActions = GetXrFunction<PFN_xrSyncActions>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetActionStateBoolean = GetXrFunction<PFN_xrGetActionStateBoolean>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetActionStateFloat = GetXrFunction<PFN_xrGetActionStateFloat>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrGetActionStateVector2f = GetXrFunction<PFN_xrGetActionStateVector2f>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateActionSpace = GetXrFunction<PFN_xrCreateActionSpace>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrLocateSpace = GetXrFunction<PFN_xrLocateSpace>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEnumerateViewConfigurations = GetXrFunction<PFN_xrEnumerateViewConfigurations>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEnumerateViewConfigurationViews = GetXrFunction<PFN_xrEnumerateViewConfigurationViews>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEnumerateSwapchainFormats = GetXrFunction<PFN_xrEnumerateSwapchainFormats>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateSwapchain = GetXrFunction<PFN_xrCreateSwapchain>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrDestroySwapchain = GetXrFunction<PFN_xrDestroySwapchain>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEnumerateSwapchainImages = GetXrFunction<PFN_xrEnumerateSwapchainImages>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrLocateViews = GetXrFunction<PFN_xrLocateViews>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrCreateReferenceSpace = GetXrFunction<PFN_xrCreateReferenceSpace>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrDestroySpace = GetXrFunction<PFN_xrDestroySpace>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrAcquireSwapchainImage = GetXrFunction<PFN_xrAcquireSwapchainImage>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrWaitSwapchainImage = GetXrFunction<PFN_xrWaitSwapchainImage>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrReleaseSwapchainImage = GetXrFunction<PFN_xrReleaseSwapchainImage>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrEndSession = GetXrFunction<PFN_xrEndSession>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrApplyHapticFeedback = GetXrFunction<PFN_xrApplyHapticFeedback>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                xrStopHapticFeedback = GetXrFunction<PFN_xrStopHapticFeedback>(instanceHandle, xrGetInstanceProcAddr_func_ptr);
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Error initializing instance-specific OpenXR functions:", ex);
                return false;
            }
        }

        private static TDelegate GetXrFunction<TDelegate>(ulong instanceHandle, PFN_xrGetInstanceProcAddr getInstanceProcAddrFunc) where TDelegate : Delegate
        {
            string functionName = typeof(TDelegate).Name.Substring("PFN_".Length);
            XrResult result = getInstanceProcAddrFunc(instanceHandle, functionName, out IntPtr funcPtr);
            if (result != XrResult.XR_SUCCESS || funcPtr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"Failed to load OpenXR function '{functionName}'. Result: {result}");
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(funcPtr);
        }
    }
    #endregion

    #region --- Helper Classes & Structs ---

    public static class OpenXRHelper
    {
        public static void CheckResult(XrResult result, string originatingFunction, bool throwOnError = true)
        {
            if (result >= 0) return;
            string errorMsg = $"OpenXR Error: Function '{originatingFunction}' failed with result: {result} ({(int)result})";
            VRModCore.LogError(errorMsg);
            if (throwOnError) throw new OpenXRException(result, originatingFunction);
        }

        public static bool CheckResultAndLog(XrResult result, string originatingFunction, string context, bool throwOnError)
        {
            if (result >= 0)
            {
                if (result != XrResult.XR_SUCCESS)
                {
                    VRModCore.LogRuntimeDebug($"OpenXR Info ({context}): Function '{originatingFunction}' completed with status: {result}");
                }
                return true;
            }

            string errorMsg = $"OpenXR Error ({context}): Function '{originatingFunction}' failed with result: {result}";
            VRModCore.LogError(errorMsg);
            if (throwOnError)
            {
                throw new OpenXRException(result, $"{context} - {originatingFunction}");
            }
            return false;
        }
    }

    public class OpenXRException(XrResult result, string message) : Exception($"OpenXR Error in {message}: {result} ({(int)result})")
    {
        public XrResult ResultCode { get; private set; } = result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = OpenXRConstants.XR_MAX_APPLICATION_NAME_SIZE)]
        public string applicationName;
        public uint applicationVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = OpenXRConstants.XR_MAX_ENGINE_NAME_SIZE)]
        public string engineName;
        public uint engineVersion;
        public ulong apiVersion;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct XrExtensionProperties { public XrStructureType type; public IntPtr next; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string extensionName; public uint extensionVersion; }
    [StructLayout(LayoutKind.Sequential)] public struct XrInstanceCreateInfo { public XrStructureType type; public IntPtr next; public ulong createFlags; public XrApplicationInfo applicationInfo; public uint enabledApiLayerCount; public IntPtr enabledApiLayerNames; public uint enabledExtensionCount; public IntPtr enabledExtensionNames; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSystemGetInfo { public XrStructureType type; public IntPtr next; public XrFormFactor formFactor; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSystemGraphicsProperties { public uint maxSwapchainImageHeight; public uint maxSwapchainImageWidth; public uint maxLayerCount; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSystemTrackingProperties { public uint orientationTracking; public uint positionTracking; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSystemProperties { public XrStructureType type; public IntPtr next; public ulong systemId; public uint vendorId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] systemName; public XrSystemGraphicsProperties graphicsProperties; public XrSystemTrackingProperties trackingProperties; }
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct XrGraphicsRequirementsD3D11KHR { public XrStructureType type; public IntPtr next; public LUID adapterLuid; public D3D_FEATURE_LEVEL minFeatureLevel; }
    [StructLayout(LayoutKind.Sequential)] public struct XrGraphicsBindingD3D11KHR { public XrStructureType type; public IntPtr next; public IntPtr device; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSessionCreateInfo { public XrStructureType type; public IntPtr next; public ulong createFlags; public ulong systemId; }
    [StructLayout(LayoutKind.Sequential)] public struct XrFrameWaitInfo { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrFrameBeginInfo { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSessionBeginInfo { public XrStructureType type; public IntPtr next; public XrViewConfigurationType primaryViewConfigurationType; }
    [StructLayout(LayoutKind.Sequential)] public struct XrEventDataBaseHeader { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrEventDataSessionStateChanged { public XrStructureType type; public IntPtr next; public ulong session; public XrSessionState state; public long time; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionSetCreateInfo { public XrStructureType type; public IntPtr next; [MarshalAs(UnmanagedType.ByValArray, SizeConst = OpenXRConstants.XR_MAX_ACTION_SET_NAME_SIZE)] public byte[] actionSetName; [MarshalAs(UnmanagedType.ByValArray, SizeConst = OpenXRConstants.XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE)] public byte[] localizedActionSetName; public uint priority; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionCreateInfo { public XrStructureType type; public IntPtr next; [MarshalAs(UnmanagedType.ByValArray, SizeConst = OpenXRConstants.XR_MAX_ACTION_NAME_SIZE)] public byte[] actionName; public XrActionType actionType; public uint countSubactionPaths; public IntPtr subactionPaths; [MarshalAs(UnmanagedType.ByValArray, SizeConst = OpenXRConstants.XR_MAX_LOCALIZED_ACTION_NAME_SIZE)] public byte[] localizedActionName; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionSuggestedBinding { public ulong action; public ulong binding; }
    [StructLayout(LayoutKind.Sequential)] public struct XrInteractionProfileSuggestedBinding { public XrStructureType type; public IntPtr next; public ulong interactionProfile; public uint countSuggestedBindings; public IntPtr suggestedBindings; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSessionActionSetsAttachInfo { public XrStructureType type; public IntPtr next; public uint countActionSets; public IntPtr actionSets; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActiveActionSet { public ulong actionSet; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionsSyncInfo { public XrStructureType type; public IntPtr next; public uint countActiveActionSets; public IntPtr activeActionSets; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionSpaceCreateInfo { public XrStructureType type; public IntPtr next; public ulong action; public ulong subactionPath; public XrPosef poseInActionSpace; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateGetInfo { public XrStructureType type; public IntPtr next; public ulong action; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrHapticActionInfo { public XrStructureType type; public IntPtr next; public ulong action; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrHapticBaseHeader { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrHapticVibration { public XrStructureType type; public IntPtr next; public long duration; public float frequency; public float amplitude; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateBoolean { public XrStructureType type; public IntPtr next; public uint currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateFloat { public XrStructureType type; public IntPtr next; public float currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrVector2f { public float x; public float y; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateVector2f { public XrStructureType type; public IntPtr next; public XrVector2f currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrFrameState { public XrStructureType type; public IntPtr next; public long predictedDisplayTime; public long predictedDisplayPeriod; public uint shouldRender; }
    [StructLayout(LayoutKind.Sequential)] public struct XrViewConfigurationView { public XrStructureType type; public IntPtr next; public uint recommendedImageRectWidth; public uint maxImageRectWidth; public uint recommendedImageRectHeight; public uint maxImageRectHeight; public uint recommendedSwapchainSampleCount; public uint maxSwapchainSampleCount; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainCreateInfo { public XrStructureType type; public IntPtr next; public XrSwapchainCreateFlags createFlags; public XrSwapchainUsageFlags usageFlags; public long format; public uint sampleCount; public uint width; public uint height; public uint faceCount; public uint arraySize; public uint mipCount; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainImageD3D11KHR { public XrStructureType type; public IntPtr next; public IntPtr texture; }
    [StructLayout(LayoutKind.Sequential)] public struct XrViewLocateInfo { public XrStructureType type; public IntPtr next; public XrViewConfigurationType viewConfigurationType; public long displayTime; public ulong space; }
    [StructLayout(LayoutKind.Sequential)] public struct XrViewState { public XrStructureType type; public IntPtr next; public XrViewStateFlags viewStateFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct XrFovf { public float angleLeft; public float angleRight; public float angleUp; public float angleDown; }
    [StructLayout(LayoutKind.Sequential)] public struct XrQuaternionf { public float x; public float y; public float z; public float w; }
    [StructLayout(LayoutKind.Sequential)] public struct XrVector3f { public float x; public float y; public float z; }
    [StructLayout(LayoutKind.Sequential)] public struct XrPosef { public XrQuaternionf orientation; public XrVector3f position; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSpaceLocation { public XrStructureType type; public IntPtr next; public XrSpaceLocationFlags locationFlags; public XrPosef pose; }
    [StructLayout(LayoutKind.Sequential)] public struct XrView { public XrStructureType type; public IntPtr next; public XrPosef pose; public XrFovf fov; }
    [StructLayout(LayoutKind.Sequential)] public struct XrReferenceSpaceCreateInfo { public XrStructureType type; public IntPtr next; public XrReferenceSpaceType referenceSpaceType; public XrPosef poseInReferenceSpace; }
    [StructLayout(LayoutKind.Sequential)] public struct XrOffset2Di { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] public struct XrExtent2Di { public int width; public int height; }
    [StructLayout(LayoutKind.Sequential)] public struct XrRect2Di { public XrOffset2Di offset; public XrExtent2Di extent; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainSubImage { public ulong swapchain; public XrRect2Di imageRect; public uint imageArrayIndex; }
    [StructLayout(LayoutKind.Sequential)] public struct XrCompositionLayerProjectionView { public XrStructureType type; public IntPtr next; public XrPosef pose; public XrFovf fov; public XrSwapchainSubImage subImage; }
    [StructLayout(LayoutKind.Sequential)] public struct XrCompositionLayerProjection { public XrStructureType type; public IntPtr next; public XrCompositionLayerFlags layerFlags; public ulong space; public uint viewCount; public IntPtr views; }
    [StructLayout(LayoutKind.Sequential)] public struct XrFrameEndInfo { public XrStructureType type; public IntPtr next; public long displayTime; public XrEnvironmentBlendMode environmentBlendMode; public uint layerCount; public IntPtr layers; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainImageAcquireInfo { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainImageWaitInfo { public XrStructureType type; public IntPtr next; public long timeout; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSwapchainImageReleaseInfo { public XrStructureType type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)] public struct XrEventDataBuffer
    {
        public XrStructureType type;
        public IntPtr next;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4000)]
        public byte[] varying;
        public static int GetSize() { return Marshal.SizeOf(typeof(XrEventDataBuffer)); }
    }

    public static class MarshallStringUtils
    {
        public static IntPtr MarshalStringArrayToAnsi(string[] strings)
        {
            if (strings == null || strings.Length == 0) return IntPtr.Zero;
            IntPtr[] unmanagedStringPointers = new IntPtr[strings.Length];
            IntPtr pointerArray = Marshal.AllocHGlobal(Marshal.SizeOf<IntPtr>() * strings.Length);
            try
            {
                for (int i = 0; i < strings.Length; i++)
                {
                    byte[] utf8Bytes = Encoding.UTF8.GetBytes(strings[i] + "\0");
                    unmanagedStringPointers[i] = Marshal.AllocHGlobal(utf8Bytes.Length);
                    Marshal.Copy(utf8Bytes, 0, unmanagedStringPointers[i], utf8Bytes.Length);
                }
                Marshal.Copy(unmanagedStringPointers, 0, pointerArray, strings.Length);
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during string array marshalling:", ex);
                FreeMarshalledStringArray(pointerArray, unmanagedStringPointers.Length);
                throw;
            }
            return pointerArray;
        }

        public static void FreeMarshalledStringArray(IntPtr pointerArray, int numStrings)
        {
            if (pointerArray == IntPtr.Zero) return;
            IntPtr[] unmanagedStringPointers = new IntPtr[numStrings];
            Marshal.Copy(pointerArray, unmanagedStringPointers, 0, numStrings);
            for (int i = 0; i < numStrings; i++)
            {
                if (unmanagedStringPointers[i] != IntPtr.Zero) Marshal.FreeHGlobal(unmanagedStringPointers[i]);
            }
            Marshal.FreeHGlobal(pointerArray);
        }

        public static byte[] CreateFixedSizeUtf8NullTerminated(string value, int size)
        {
            byte[] buffer = new byte[size];
            if (string.IsNullOrEmpty(value) || size <= 0) return buffer;

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(value);
            int copyCount = Math.Min(utf8Bytes.Length, size - 1);
            Buffer.BlockCopy(utf8Bytes, 0, buffer, 0, copyCount);
            buffer[copyCount] = 0;
            return buffer;
        }
    }

    #endregion
}
