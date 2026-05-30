#if BIE

using BepInEx;
using BepInEx.Logging;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Loader.BIE;
using UnityVRMod.Loader;


#if CPP
using BepInEx.Unity.IL2CPP;
#endif
#if MONO && !BIE5
using BepInEx.Unity.Mono;
#endif

#pragma warning disable IDE0130
namespace UnityVRMod
#pragma warning restore IDE0130
{
    [BepInPlugin(GUID, MOD_NAME, VERSION)]
    public class VRModBepInPlugin :
#if MONO
        BaseUnityPlugin,
#else
        BasePlugin,
#endif
        IVRModLoader
    {
        public const string GUID = "com.newunitymodder.unityvrmod";
        public const string MOD_NAME = "Unity VR Mod";
        public const string VERSION = "1.0.0";

        public static VRModBepInPlugin Instance { get; private set; }

        public ManualLogSource LogSource =>
#if MONO
            Logger;
#else
            Log;
#endif
        public string InteropAssembliesPath => Path.Combine(Paths.BepInExRootPath, "interop");

        public ConfigHandler ConfigHandler => _configHandler;
        private BepInExConfigHandler _configHandler;

        public Action<object> LogMessage => LogSource.LogMessage;
        public Action<object> LogWarning => LogSource.LogWarning;
        public Action<object> LogError => LogSource.LogError;

        private void Init()
        {
            Instance = this;
            _configHandler = new BepInExConfigHandler();

            // Delegate core initialization to VRModCore
            VRModCore.Init(this);
        }

#if MONO
        internal void Awake()
#else
        public override void Load()
#endif
        {
            Init();
        }
    }
}
#endif