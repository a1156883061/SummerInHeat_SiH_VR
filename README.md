# Summer in Heat VR Mod

> [!IMPORTANT]
> This is a specialized version of the **UnityVRMod** framework, custom-tuned specifically for **Summer in Heat** (夏のサカり) by Miconisomi.

---

## ✨ Features

*   **Native 6DOF VR Experience**: Full 6-Degrees-of-Freedom tracking powered by **OpenXR**.
*   **Immersive Interactions**: 3D UI interaction system tailored for VR controllers.
*   **Advanced Locomotion**: Includes VR Teleportation and comfort-focused view controls (Snap/Smooth turning).
*   **Dynamic UI Management**: Reposition and scale in-game UI panels in VR.
*   **Seamless Perspective**: Optimized first-person camera bindings and fixed scene transitions.
*   **Physics Interaction Support**: Interact with physics-enabled parts such as hair, breasts, and skirts.

---

## 🛠️ Prerequisites & Hardware

*   **Game**: Summer in Heat (夏のサカり)
*   **Core Framework**: [BepInEx 5 x64](https://github.com/BepInEx/BepInEx/releases)
    *   BepInEx 6 is also supported if you build it yourself.
*   **Tested Hardware**: Meta Quest 3 (via Meta Quest Link / Air Link / Virtual Desktop), Pico neo3, Pico 4.
    *   *Note: Other headsets may work via OpenXR but have not been officially tested.*
    *   *Pico 3/4 testing was reported by other users.*

---

## 🚀 Installation

1.  **Install BepInEx 5**: Ensure you have the x64 version of BepInEx 5 installed in your game directory.
2.  **Deploy Mod Files**: Extract the contents of this release into the game root folder, the same folder that contains `『夏のサカり』起動ランチャー.exe`.
3.  **Configure**:
    *   Launch the game once to generate initial configuration files.
4.  **Launch VR using either method**:
    *   Start your VR runtime (e.g., Meta Quest Link or Virtual Desktop).
    *   Launch the game and press **F11** to toggle VR mode (the toggle key can be customized in the configuration file).
    *   Or start **start_vr.cmd** directly.

---

## 🎮 Controls (Oculus/Meta Touch)

> [!TIP]
> `OpenXR Control Hand` switches locomotion and interaction hand. `OpenXR UI Panel Follow Hand` switches which hand the UI panel follows. Double-click **X(Left)/A(Right)** to switch panel follow hand at runtime.

| Action | VR Controller Input |
| :--- | :--- |
| **Teleport Aim** | **Right Stick ↑** (always right hand) |
| **Confirm Teleport** | **Right Trigger** (always right hand) |
| **Snap Turning** | **Right Stick ←→** (always right hand) |
| **Smooth Turning** | **Right Stick Click (Hold)** + Move ←→ |
| **Smooth Move / Walk** | **Left Stick** (camera-relative, configurable speed) |
| **Toggle Follow / Free Mode** | **Left Stick Click** |
| **Move Viewport** | **Hold Grip (either hand)** & Drag |
| **Toggle UI Anchor/Follow** | **Click Y(Left) / B(Right)** |
| **Switch Panel Follow Hand** | **Double-click X(Left) / A(Right)** |
| **Toggle UI Panel Visible** | **Click X(Left) / A(Right)** |
| **Click/Select UI** | **Either Trigger** (auto-switches on first press) |
| **Resize & Move UI Panel** | **Trigger (on boundary) & Drag** |
| **Petting Interact** | **Trigger (either hand)** + hand near target |
| **Toggle SubCam Move Mode** | **Hold A (Right)** |
| **Move SubCam** | **Right Stick** (while SubCam Move Mode ON) |
| **Rotate SubCam** | **Hold Right Stick Click** + **Right Stick** |
| **SubCam Height Step** | **Tap A (Up) / Tap B (Down)** (SubCam Move Mode ON) |

### UI Interaction

- **Either trigger clicks UI**: Press trigger on left or right hand — the clicking hand is locked on first press and stays until the other hand clicks.
- **Panel follow hand**: Double-click X(Left) or A(Right) to toggle which hand the UI panel follows. Panel position is independent of the control hand used for locomotion.
- **Panel visibility**: Click X(Left) or A(Right) to show/hide the UI panel. This is disabled in Hybrid2D scenes.

### Move Viewport

1. Press and hold **Grip** on either controller.
2. Move your hand to drag the world.
3. Release to stop.

### Follow / Free Mode

- **Follow mode**: VR follows the original game camera position and horizontal yaw changes.
- **Free mode**: VR keeps the current viewport independent from original game camera movement; use **Left Stick**, **Grip drag**, teleport, and turning to adjust view manually.
- Press **Left Stick Click** to switch modes at runtime.

### Petting Interaction

1. Move either controller close to the target body point.
2. When a valid target is detected, the icon turns **blue**.
3. Press **Trigger** to interact — the icon turns **orange**.
4. Only works in the game's free mode.

### SubCamera Control

1. **Hold A** to toggle SubCam move mode ON/OFF.
2. When move mode is ON, move the **Right Stick** to move subcamera.
3. Hold **Right Stick Click**, then move stick to rotate.
4. In move mode, **tap A** to raise height and **tap B** to lower height.
5. Red SubCam outline is shown while move mode is ON.

---

## ⚠️ Current Status & Roadmap (February 2026)

> [!NOTE]
> This mod is under active development. Below are the current known limitations and planned updates:

*   **ADV Scene**: Currently displayed as a 2D projection. Visual effects in VR may differ from the PC version and will be addressed in future updates.
*   **UI Integration**: Certain dialog icons currently require interaction via the main UI panel rather than direct clicking.
*   **Performance Note**: High graphics settings and effects significantly impact VR frame rates; adjust carefully for stability.

---

## 📖 About the Framework

This mod is built upon the [UnityVRMod](https://github.com/newunitymodder/UnityVRMod) framework, which provides core 6DOF injection capabilities for Unity games.

---

## 📜 Credits

*   **Original Framework**: [newunitymodder](https://github.com/newunitymodder/UnityVRMod)
