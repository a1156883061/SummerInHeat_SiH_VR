using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityVRMod.Config;
using UnityVRMod.Features.Debug;
using UnityVRMod.Features.Samples;
using UnityVRMod.Loader;

#pragma warning disable IDE0130
namespace UnityVRMod.Core
#pragma warning restore IDE0130
{
    public static class VRModCore
    {
        public const string GUID = "com.newunitymodder.unityvrmod";
        public const string MOD_NAME = "Unity VR Mod";
        public const string VERSION = "1.4.0";
        public const string AUTHOR = "New Unity Modder";

        public static IVRModLoader Loader { get; private set; }
        private static bool _autoVrRequested;

        internal static Features.VrVisualization.VrVisualizationManager VrVisualizationFeature { get; private set; }

        public static void Init(IVRModLoader loader)
        {
            if (Loader != null)
            {
                loader.LogWarning($"{MOD_NAME} is already loaded!");
                return;
            }
            Loader = loader;

            // --vr 命令行参数：启动后自动进入 VR
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--vr") { _autoVrRequested = true; break; }

            Log($"{MOD_NAME} {VERSION} initializing...");

            ConfigManager.Init(Loader.ConfigHandler);

            var universeConfig = new UniverseLib.Config.UniverseLibConfig
            {
                Disable_EventSystem_Override = ConfigManager.Disable_EventSystem_Override?.Value ?? false,
                Force_Unlock_Mouse = ConfigManager.Force_Unlock_Mouse?.Value ?? true,
                Unhollowed_Modules_Folder = Loader.InteropAssembliesPath
            };
            LogRuntimeDebug($"UniverseLibConfig prepared: EventSystemOverrideDisabled={universeConfig.Disable_EventSystem_Override}, ForceUnlockMouse={universeConfig.Force_Unlock_Mouse}");

            Universe.Init(ConfigManager.Startup_Delay_Time?.Value ?? 1.0f, LateInit, UniverseLib_Log, universeConfig);

            TemporaryLiveReloadTester.Initialize();
            HelloWorldFeature.Initialize();
            VRModBehaviour.Setup();
            LogRuntimeDebug("Core VRModCore.Init phase complete.");
        }

        static void LateInit()
        {
            LogRuntimeDebug("Executing LateInit tasks...");

            if (ConfigManager.EnableVrInjection?.Value ?? false)
            {
                Log("EnableVrInjection is true, initializing VrVisualizationFeature...");
                try
                {
                    VrVisualizationFeature = new Features.VrVisualization.VrVisualizationManager();
                    VrVisualizationFeature.Initialize();
#if MONO
                    Features.VrVisualization.BeautifyRenderDiagnostics.EnsureInstalled();
                    Features.VrVisualization.CameraTypesVrFollowPatch.EnsureInstalled();
#endif
                }
                catch (Exception ex)
                {
                    LogError("Exception during VrVisualizationManager creation or initialization:", ex);
                }
            }
            else
            {
                Log("EnableVrInjection is false, skipping VR initialization.");
            }
            Log($"{MOD_NAME} {VERSION} ({Universe.Context}) fully initialized.");
        }

        internal static void Update()
        {
            try
            {
                // --vr 自动进入 VR（延迟到 VR 模块就绪后）
                if (_autoVrRequested && VrVisualizationFeature != null)
                {
                    _autoVrRequested = false;
                    VrVisualizationFeature.ToggleUserSafeMode();
                    Log("Auto-entering VR mode (--vr flag).");
                }
                VRModKeybind.Update();
                VrVisualizationFeature?.Update();
                TemporaryLiveReloadTester.Update();
                HelloWorldFeature.Update();
            }
            catch (Exception ex)
            {
                LogError("Exception during VRModCore.Update():", ex);
            }
        }

        #region LOGGING
        private const string UNIVERSELIB_IGNORE_WARNING_PHRASE = "THIS WARNING IS NOT BUG!!!! DON'T REPORT THIS!!!!!";

        // This method is specifically for UniverseLib's logger delegate.
        private static void UniverseLib_Log(string message, UnityEngine.LogType logType)
        {
            if (logType == UnityEngine.LogType.Warning && message.Contains(UNIVERSELIB_IGNORE_WARNING_PHRASE))
            {
                return;
            }
            LogImpl(message, logType, "UniverseLib", "", 0);
        }

        private static void LogImpl(object message, UnityEngine.LogType logType, string callerName, string callerFile, int callerLine)
        {
            if (Loader == null || message == null) return;
            string messageStr = message.ToString();
            string formattedMessage;

#if DEBUG
            bool isDebug = true;
#else
            bool isDebug = ConfigManager.EnableRuntimeDebugLogging?.Value ?? false;
#endif

            if (isDebug)
            {
                string prefix = string.IsNullOrEmpty(callerFile)
                    ? $"[{callerName}]"
                    : $"[{Path.GetFileNameWithoutExtension(callerFile)}.{callerName}:{callerLine}]";
                formattedMessage = $"{prefix} {messageStr}";
            }
            else
            {
                formattedMessage = $"[{Path.GetFileNameWithoutExtension(callerFile)}] {messageStr}";
            }

            switch (logType)
            {
                case UnityEngine.LogType.Log:
                case UnityEngine.LogType.Assert:
                    Loader.LogMessage(formattedMessage);
                    break;
                case UnityEngine.LogType.Warning:
                    Loader.LogWarning(formattedMessage);
                    break;
                case UnityEngine.LogType.Error:
                case UnityEngine.LogType.Exception:
                    Loader.LogError(formattedMessage);
                    break;
            }
        }

        public static void Log(object message, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            LogImpl(message, UnityEngine.LogType.Log, callerName, callerFile, callerLine);
        }

        public static void LogWarning(object message, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            LogImpl(message, UnityEngine.LogType.Warning, callerName, callerFile, callerLine);
        }

        public static void LogError(object message, object exception = null, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            string finalMessage = (exception != null) ? $"{message}\n{exception}" : message.ToString();
            LogImpl(finalMessage, UnityEngine.LogType.Error, callerName, callerFile, callerLine);
        }

        public static void LogRuntimeDebug(object message, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
#if DEBUG
            LogImpl($"[DEBUG] {message}", UnityEngine.LogType.Log, callerName, callerFile, callerLine);
#else
            if (ConfigManager.EnableRuntimeDebugLogging?.Value ?? false)
            {
                LogImpl($"[DEBUG] {message}", UnityEngine.LogType.Log, callerName, callerFile, callerLine);
            }
#endif
        }

        [Conditional("ENABLE_VDEBUG_LOGGING")]
        public static void LogSpammyDebug(object message, [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            LogImpl($"[VDEBUG] {message}", UnityEngine.LogType.Log, callerName, callerFile, callerLine);
        }
        #endregion
    }
}
