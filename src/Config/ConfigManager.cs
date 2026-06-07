using UnityVRMod.Core;
using UnityVRMod.Features.VrVisualization;

namespace UnityVRMod.Config
{
    public static class ConfigManager
    {
        internal static readonly Dictionary<string, IConfigElement> ConfigElements = [];
        internal static readonly Dictionary<string, IConfigElement> InternalConfigs = [];
        public static ConfigHandler Handler { get; private set; }
        internal static InternalConfigHandler InternalHandler { get; private set; }

        // -------- CONFIG ELEMENT DEFINITIONS --------
        // --- Internal Settings (Require Restart) ---
        public static ConfigElement<float> Startup_Delay_Time;
        public static ConfigElement<bool> Disable_EventSystem_Override;
        public static ConfigElement<string> Reflection_Signature_Blacklist;
        public static ConfigElement<bool> EnableVrInjection;
        public static ConfigElement<string> VrApplicationKey;
        public static ConfigElement<bool> SafeModeStartsActive;

        // --- VR Rendering Settings ---
        public static ConfigElement<float> VrCameraNearClipPlane;
        public static ConfigElement<float> VrWorldScale;
        public static ConfigElement<float> VrUserEyeHeightOffset;
        public static ConfigElement<string> ScenePoseOverrides;
        public static ConfigElement<string> DisableVrRigScenes;

        // --- Backend-Specific Stability Settings ---
#if OPENVR_BUILD
        public static ConfigElement<bool> OpenVR_DisablePostFxSync;
        public static ConfigElement<int> OpenVR_WaitGetPosesDelayMs;
        public static ConfigElement<int> OpenVR_MaxRenderTargetDimension;
        public static ConfigElement<float> OpenVR_InitialEyeHeightAboveGroundMeters;
        public static ConfigElement<float> OpenVR_GripDragSensitivity;
        public static ConfigElement<float> OpenVR_UiPanelScale;
        public static ConfigElement<bool> OpenVR_UseMouseInjectionForUi;
        public static ConfigElement<bool> OpenVR_DisableOriginalSceneCameras;
        public static ConfigElement<bool> OpenVR_MapRightMenuButtonToSpace;
#endif
#if OPENXR_BUILD
        public static ConfigElement<bool> OpenXR_DisablePostFxSync;
        public static ConfigElement<bool> OpenXR_ForceUiOnTop;
        public static ConfigElement<OpenXrControlHand> OpenXR_UiPanelFollowHand;
        public static ConfigElement<float> OpenXR_SmoothMoveSpeed;
        public static ConfigElement<float> OpenXR_GripDragSensitivity;
        public static ConfigElement<float> OpenXR_UiPanelScale;
        public static ConfigElement<float> OpenXR_PanelResizeSensitivity;
        public static ConfigElement<float> OpenXR_SnapTurnDegrees;
        public static ConfigElement<float> OpenXR_SmoothTurnDegreesPerSecond;
        public static ConfigElement<OpenXrControlHand> OpenXR_ControlHand;
        public static ConfigElement<bool> OpenXR_EnablePerfLogging;
        public static ConfigElement<string> OpenXR_ForceDefaultRenderScenes;
        public static ConfigElement<string> OpenXR_ForceSolidClearScenes;
#endif

        // --- General Settings ---
        public static ConfigElement<bool> Force_Unlock_Mouse;
        public static ConfigElement<KeyCode> ToggleSafeModeKey;
        public static ConfigElement<SafeModeLevel> ActiveSafeModeLevel;
        public static ConfigElement<bool> EnableAutomaticSafeMode;
        public static ConfigElement<float> AutomaticSafeModeDurationSecs;
        public static ConfigElement<bool> EnableRuntimeDebugLogging;
        public static ConfigElement<string> AssertedCameraOverrides;
        // -------- END OF CONFIG ELEMENT DEFINITIONS --------

        internal static void Init(ConfigHandler mainHandler)
        {
            Handler = mainHandler;
            Handler.Init();

            InternalHandler = new InternalConfigHandler();
            InternalHandler.Init();

            CreateConfigElements();

            VRModCore.LogRuntimeDebug("Loading main configuration...");
            Handler.LoadConfig();
            VRModCore.LogRuntimeDebug("Loading internal configuration...");
            InternalHandler.LoadConfig();
            VRModCore.Log("Configuration initialized and loaded.");
        }

        internal static void RegisterConfigElement<T>(ConfigElement<T> configElement)
        {
            if (!configElement.IsInternal)
            {
                Handler.RegisterConfigElement(configElement);
                ConfigElements.Add(configElement.Name, configElement);
            }
            else
            {
                InternalHandler.RegisterConfigElement(configElement);
                InternalConfigs.Add(configElement.Name, configElement);
            }
        }

        private static void CreateConfigElements()
        {
            VRModCore.Log("Creating configuration elements...");

            // --- Internal Settings (Require Restart) ---
            Startup_Delay_Time = new ConfigElement<float>("Startup Delay Time",
                "The delay (in seconds) before the mod fully initializes after game start.", 1.0f, isInternal: true);

            Disable_EventSystem_Override = new ConfigElement<bool>("Disable EventSystem Override",
                "If true, the mod will not override the game's EventSystem.", false, isInternal: true);

            Reflection_Signature_Blacklist = new ConfigElement<string>("Member Signature Blacklist",
                "Prevents the mod from reflecting on specific class members. Separate with ';'. Ex: 'UnityEngine.Camera.main;'", "", isInternal: true);

            EnableVrInjection = new ConfigElement<bool>("Enable VR Injection",
                "Master switch for all VR-related functionality. Change requires restart.", true, isInternal: true);

            VrApplicationKey = new ConfigElement<string>("VR Application Key",
                "The application key used when initializing OpenVR/OpenXR. Change requires restart.", "unityvrmod.default.key", isInternal: true);

            SafeModeStartsActive = new ConfigElement<bool>("Safe Mode Starts Active",
                "If true, VR rendering is disabled when the mod first loads.", true, isInternal: true);

            // --- VR Rendering Settings ---
            VrCameraNearClipPlane = new ConfigElement<float>("VR Camera Near Clip",
                "The closest distance (in meters) that the VR cameras can see. This value is scaled by World Scale.", 0.01f);
            VrCameraNearClipPlane.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateCameraNearClip(value);

            VrWorldScale = new ConfigElement<float>("VR World Scale",
                "Adjusts the perceived size of the world. >1 makes the world feel larger; <1 makes it feel smaller.", 1.0f);
            VrWorldScale.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateWorldScale(value);

            VrUserEyeHeightOffset = new ConfigElement<float>("User Eye Height Offset",
                "How much taller (+) or shorter (-) you want to feel in the virtual world (in meters). This value is scaled by World Scale.", 0.0f);
            VrUserEyeHeightOffset.OnValueChanged += value => VRModCore.VrVisualizationFeature?.LiveUpdateUserEyeHeightOffset(value);

            ScenePoseOverrides = new ConfigElement<string>("Scene-Specific Pose Overrides",
                "Defines a starting position and, optionally, rotation for the VR rig in specific scenes. Format: 'SceneName|X Y Z|Pitch Yaw Roll;'. Use '~' to keep a game's original value for any axis.", "");

            DisableVrRigScenes = new ConfigElement<string>("Disable VR Rig Scenes",
                "Skips VR rig creation in specific scenes. Format: 'SceneNameA;SceneNameB;'. Matching is done against SceneManager.GetActiveScene().name.",
                "");
            
            // --- Backend-Specific Stability Settings ---
#if OPENVR_BUILD
            OpenVR_DisablePostFxSync = new ConfigElement<bool>("OpenVR Disable PostFX Sync",
                "[OpenVR ONLY] If true, disables cloning of post-processing effects (Beautify, ScreenOverlay, etc.) from the game's main camera to VR eye cameras.", true);

            OpenVR_WaitGetPosesDelayMs = new ConfigElement<int>("OpenVR WaitGetPoses Delay (ms)",
                "[OpenVR ONLY] A small delay (in milliseconds) before retrieving headset poses. Helps prevent crashes in some games.", 2);

            OpenVR_MaxRenderTargetDimension = new ConfigElement<int>("OpenVR Max Render Target Dimension",
                "[OpenVR ONLY] The maximum width or height for the VR eye textures. 0 uses the default recommended by SteamVR.", 0);

            OpenVR_InitialEyeHeightAboveGroundMeters = new ConfigElement<float>("OpenVR Initial Eye Height Above Ground (m)",
                "[OpenVR ONLY] On first valid ground detection, align headset height to GroundY + this value (meters).", 1.65f);

            OpenVR_GripDragSensitivity = new ConfigElement<float>("OpenVR Grip Drag Sensitivity",
                "[OpenVR ONLY] Multiplier for Grip drag translation. Lower values reduce motion intensity and nausea.", 0.45f);

            OpenVR_UiPanelScale = new ConfigElement<float>("OpenVR UI Panel Scale",
                "[OpenVR ONLY] Scale multiplier for the projected NGUI panel. 1.0 = default size.", 1.0f);

            OpenVR_UseMouseInjectionForUi = new ConfigElement<bool>("OpenVR Use Mouse Injection For UI",
                "[OpenVR ONLY] If true, UI clicks use OS-level mouse cursor injection instead of NGUI OnPress/OnClick messages.", true);

            OpenVR_DisableOriginalSceneCameras = new ConfigElement<bool>("OpenVR Disable Original Scene Cameras",
                "[OpenVR ONLY] If true, disables non-UI original scene cameras while VR rig is active to reduce CPU/GPU cost. VR eye cameras and NGUI UICamera stay active.", true);

            OpenVR_MapRightMenuButtonToSpace = new ConfigElement<bool>("OpenVR Map Right Menu Button To Space",
                "[OpenVR ONLY] If true, right controller Menu/B is injected as keyboard Space down/up. Useful for hold actions implemented via Input.GetKey(KeyCode.Space).", true);
#endif
#if OPENXR_BUILD
            OpenXR_DisablePostFxSync = new ConfigElement<bool>("OpenXR Disable PostFX Sync",
                "[OpenXR ONLY] If true, disables cloning of post-processing effects (Beautify, CameraFilterPack, etc.) from the game's main camera to VR eye cameras. Disable to remove blur/DOF effects in VR.", true);

            OpenXR_ForceUiOnTop = new ConfigElement<bool>("OpenXR Force UI On Top",
                "[OpenXR ONLY] If true, uses an overlay camera to render VR UI panels (main panel, Danmen, SubCamera) on top of all scene geometry so they are never occluded by characters or objects.", true);

            OpenXR_UiPanelFollowHand = new ConfigElement<OpenXrControlHand>("OpenXR UI Panel Follow Hand",
                "[OpenXR ONLY] Which hand the UI panel follows. Double-click Y(Left)/B(Right) to switch at runtime.", OpenXrControlHand.Left);

            OpenXR_SmoothMoveSpeed = new ConfigElement<float>("OpenXR Smooth Move Speed",
                "[OpenXR ONLY] Walking speed in meters per second when using the left thumbstick.", 1.5f);

            OpenXR_GripDragSensitivity = new ConfigElement<float>("OpenXR Grip Drag Sensitivity",
                "[OpenXR ONLY] Multiplier for Grip drag translation. Lower values reduce motion intensity and nausea.", 0.45f);

            OpenXR_UiPanelScale = new ConfigElement<float>("OpenXR UI Panel Scale",
                "[OpenXR ONLY] Scale multiplier for the projected NGUI panel. 1.0 = default size.", 1.0f);

            OpenXR_PanelResizeSensitivity = new ConfigElement<float>("OpenXR Panel Resize Sensitivity",
                "[OpenXR ONLY] Multiplier for corner-handle panel resize responsiveness. 1.0 = linear; 2.0 = stronger resize.", 2.0f);

            OpenXR_SnapTurnDegrees = new ConfigElement<float>("OpenXR Snap Turn Degrees",
                "[OpenXR ONLY] Degrees rotated per snap turn when the stick is flicked left/right.", 30.0f);

            OpenXR_SmoothTurnDegreesPerSecond = new ConfigElement<float>("OpenXR Smooth Turn Speed (deg/s)",
                "[OpenXR ONLY] Degrees per second used for gentle hold smooth turning with the stick.", 45.0f);

            OpenXR_ControlHand = new ConfigElement<OpenXrControlHand>("OpenXR Control Hand",
                "[OpenXR ONLY] Select which hand drives locomotion, teleport aim/confirm, UI ray click, and panel drag/resize.", OpenXrControlHand.Right);

            OpenXR_EnablePerfLogging = new ConfigElement<bool>("OpenXR Enable Perf Logging",
                "[OpenXR ONLY] Enables periodic [Perf][OpenXR] timing logs for diagnosis.", false);

            OpenXR_ForceDefaultRenderScenes = new ConfigElement<string>("OpenXR Force Default Render Scenes",
                "[OpenXR ONLY] Forces safe default VR eye-camera render state in specific scenes to avoid artifacts from copied main-camera settings. Format: 'SceneNameA;SceneNameB;'.", "");

            OpenXR_ForceSolidClearScenes = new ConfigElement<string>("OpenXR Force Solid Clear Scenes",
                "[OpenXR ONLY] Keeps main-camera reference and PostFX sync, but forces VR eye-camera clearFlags to SolidColor in specific scenes. Useful when main camera uses ClearFlags=Nothing and causes ghosting/overexposure in VR. Format: 'SceneNameA;SceneNameB;'.", "");
#endif

            // --- General Settings ---
            Force_Unlock_Mouse = new ConfigElement<bool>("Force Unlock Mouse",
               "Forces the mouse cursor to be visible and unlocked when any mod UI is open.", true);
            Force_Unlock_Mouse.OnValueChanged += value => UniverseLib.Config.ConfigManager.Force_Unlock_Mouse = value;

            ToggleSafeModeKey = new ConfigElement<KeyCode>("Toggle Safe Mode Keybind",
                "The key used to toggle VR rendering on and off.", KeyCode.F11);

            ActiveSafeModeLevel = new ConfigElement<SafeModeLevel>("Safe Mode Level",
                "Defines the behavior of the Safe Mode toggle. Fast is quickest. RigReinit is safer but slower. FullVrReinit is the most aggressive and safest option for delicate games.", SafeModeLevel.RigReinitOnToggle);

            EnableAutomaticSafeMode = new ConfigElement<bool>("Enable Automatic Safe Mode",
                "If true, VR rendering will be temporarily disabled during scene loads or when the game's main camera changes.", true);

            AutomaticSafeModeDurationSecs = new ConfigElement<float>("Automatic Safe Mode Duration",
                "The time (in seconds) that automatic safe mode will remain active.", 1.0f);

            EnableRuntimeDebugLogging = new ConfigElement<bool>("Enable Runtime Debug Logging",
                "Enables detailed, non-spammy debug messages to be printed to the console.", false);

            AssertedCameraOverrides = new ConfigElement<string>("Asserted Camera Overrides",
                "Manual overrides for camera detection if heuristics fail. Format: 'SceneName|GameObjectPath;GameObjectPath2'. Use full hierarchy or just the name. An empty scene name applies the override to all scenes.",
                "");

            VRModCore.Log($"Finished creating {ConfigElements.Count + InternalConfigs.Count} config elements.");
        }

        public static void SaveAll()
        {
            VRModCore.LogRuntimeDebug("Saving main configuration...");
            Handler?.SaveConfig();
            VRModCore.LogRuntimeDebug("Saving internal configuration...");
            InternalHandler?.SaveConfig();
        }

        public static void LoadAll()
        {
            VRModCore.LogRuntimeDebug("Reloading main configuration...");
            Handler?.LoadConfig();
            VRModCore.LogRuntimeDebug("Reloading internal configuration...");
            InternalHandler?.LoadConfig();
        }
    }
}
