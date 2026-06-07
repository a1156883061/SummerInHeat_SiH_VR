namespace UnityVRMod.Features.VrVisualization
{
    public enum UiPanelAnchorMode
    {
        /// <summary>面板固定在世界坐标中，不跟随 VR Rig 移动。</summary>
        World = 0,

        /// <summary>面板跟随 VR Rig 移动（旧行为）。</summary>
        Rig = 1
    }
}
