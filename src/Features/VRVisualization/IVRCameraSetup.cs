namespace UnityVRMod.Features.VrVisualization
{
    public struct VrCameraRig
    {
        public GameObject LeftEye;
        public GameObject RightEye;
    }

    internal interface IVrCameraSetup
    {
        bool IsVrAvailable { get; }
        bool IsGameCameraFollowModeActive { get; }
        bool InitializeVr(string applicationKey);
        void TeardownVr();
        void SetupCameraRig(Camera mainCamera);
        void TeardownCameraRig();
        VrCameraRig GetVrCameraGameObjects();
        void UpdatePoses();

        // --- METHODS FOR LIVE RELOADING ---
        void SetWorldScale(float newWorldScale, Camera mainCamera);
        void SetCameraNearClip(float newNearClip);
        void SetUserEyeHeightOffset(float newEyeHeightOffset);
    }
}
