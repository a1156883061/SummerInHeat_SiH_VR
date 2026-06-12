# v1.4.0 Release Notes

## English

### Camera Follow Mode

- Added a `VR Rig Follows Game Camera` config option, enabled by default.
- Added game camera follow state so the VR rig can follow the original game camera's position and yaw changes.
- Added OpenXR runtime switching between game-camera follow mode and free VR viewport mode with Left Stick Click.
- Recentered OpenXR HMD tracking when initializing or returning to follow mode, reducing offsets caused by the user's physical play-space position.
- Applied the same recentered pose to OpenXR projection layer submission, keeping the rendered camera pose and compositor pose consistent.
- Prevented the main game camera follow patch from forcing `Camera_Main` back to the VR pose while game-camera follow mode is enabled.
- Added basic OpenVR support for applying the original game camera's position and yaw deltas to the VR rig.

### Notes

- In follow mode, scripted game camera movement should now move the VR rig with the original camera.
- In free mode, the VR viewport can still be moved independently.

## 中文

### 镜头跟随模式

- 新增 `VR Rig Follows Game Camera` 配置项，默认开启。
- 新增原游戏镜头跟随状态，使 VR Rig 可以跟随原游戏镜头的位置与 yaw 变化。
- OpenXR 支持运行时使用左摇杆按下切换“跟随原游戏镜头”和“自由 VR 视角”。
- 初始化或切回跟随模式时，会重新居中 OpenXR HMD 水平追踪位置，减少由玩家现实空间位置造成的偏移。
- OpenXR 提交投影层时也使用同一套居中后的 pose，保持 Unity 渲染相机与 OpenXR compositor 的镜头一致。
- 跟随模式开启时，避免 `Camera_Main` 被旧的 VR pose 回写逻辑强制覆盖。
- OpenVR 增加基础支持，可以将原游戏镜头的位置与 yaw 增量应用到 VR Rig。

### 说明

- 跟随模式下，原游戏的脚本镜头移动会同步带动 VR Rig。
- 自由模式下，VR 视角仍可独立移动。
