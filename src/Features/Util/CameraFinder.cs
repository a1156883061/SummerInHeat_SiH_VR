using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.Util
{
    /// <summary>
    /// A utility class responsible for finding the primary game camera that the VR rig should follow.
    /// </summary>
    public static class CameraFinder
    {
        private static Camera _cachedCamera = null;
        private static Camera _synthetic3dFallbackCamera = null;
        private static Camera _synthetic2dFallbackCamera = null;

        /// <summary>
        /// Invalidates the cached camera, forcing a new search on the next call to FindGameCamera.
        /// </summary>
        public static void InvalidateCache()
        {
            VRModCore.LogRuntimeDebug("CameraFinder cache invalidated.");
            _cachedCamera = null;
        }

        /// <summary>
        /// Finds the most appropriate game camera for VR injection.
        /// It prioritizes user-defined overrides and falls back to Camera.main.
        /// It uses a cache to avoid expensive lookups every frame.
        /// </summary>
        /// <returns>The found Camera component, or null if no suitable camera is found.</returns>
        public static Camera FindGameCamera()
        {
            // ADV contains real 3D characters even though its UI is 2D. Prefer
            // the game's perspective ADV camera whenever it is active.
            if (CameraJudge.IsHybrid2DSceneActive())
            {
                GameObject advCameraObject = GameObject.Find("Camera/Camera_ADV");
                Camera advCamera = advCameraObject != null ? advCameraObject.GetComponent<Camera>() : null;
                if (advCamera != null && advCamera.enabled && advCamera.gameObject.activeInHierarchy)
                {
                    _cachedCamera = advCamera;
                    return advCamera;
                }
            }

            // Return the cached camera if it's still valid (not destroyed and is enabled).
            if (_cachedCamera != null && _cachedCamera.enabled)
            {
                return _cachedCamera;
            }

            // If cache is invalid, find a new camera.
            _cachedCamera = FindGameCameraInternal();
            return _cachedCamera;
        }

        public static bool IsSynthetic2dFallbackCamera(Camera camera)
        {
            return camera != null &&
                   _synthetic2dFallbackCamera != null &&
                   ReferenceEquals(camera, _synthetic2dFallbackCamera);
        }

        private static Camera FindGameCameraInternal()
        {
            SceneCameraDimension sceneDimension = CameraJudge.JudgeSceneDimensionByMainCameraTag(
                out int mainTagCameraCount,
                out int mainTagEnabledCameraCount);
            VRModCore.LogRuntimeDebug(
                $"CameraFinder scene dimension={sceneDimension}, MainTag(total={mainTagCameraCount}, enabled={mainTagEnabledCameraCount}).");

            // Priority 1: Check for user-defined camera overrides.
            var identifiers = CameraIdentifierHelper.Parse(ConfigManager.AssertedCameraOverrides.Value);
            if (identifiers.Count > 0)
            {
                string currentSceneName = SceneManager.GetActiveScene().name;
                foreach (var id in identifiers)
                {
                    if (string.IsNullOrEmpty(id.Scene) || id.Scene.Equals(currentSceneName, StringComparison.OrdinalIgnoreCase))
                    {
                        GameObject targetGO = GameObject.Find(id.Path);
                        if (targetGO != null)
                        {
                            var cam = targetGO.GetComponent<Camera>();
                            if (cam != null && cam.enabled)
                            {
                                VRModCore.Log($"Found game camera via AssertedCameraOverrides: '{id.Path}' in scene '{currentSceneName}'");
                                return cam;
                            }
                        }
                    }
                }
            }

            if (sceneDimension == SceneCameraDimension.Scene3D)
            {
                // Priority 2 (3D): Fallback to the default Camera.main.
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    VRModCore.Log("Found game camera via fallback to Camera.main.");
                    return mainCam;
                }
            }
            else
            {
                // Priority 2 (2D): Use dedicated 2D synthetic fallback camera.
                Camera referenceCamera = FindBest2dReferenceCamera();
                Camera synthetic2d = GetOrCreateSynthetic2dFallbackCamera(referenceCamera);
                if (synthetic2d != null)
                {
                    string sourceName = referenceCamera != null ? referenceCamera.name : "None";
                    VRModCore.Log($"Found game camera via 2D synthetic fallback: source='{sourceName}', synthetic='{synthetic2d.name}'.");
                    return synthetic2d;
                }
            }

            // Priority 3 (3D): Fallback to a synthetic camera that follows active NGUI UICamera pose.
            // This avoids directly inheriting UI camera clear/depth settings that can cause artifacts.
            Camera nguiCam = FindBestNguiUiCamera();
            if (nguiCam != null)
            {
                Camera synthetic = GetOrCreateSynthetic3dFallbackCamera();
                if (synthetic != null)
                {
                    synthetic.transform.SetPositionAndRotation(nguiCam.transform.position, nguiCam.transform.rotation);
                    VRModCore.Log($"Found game camera via NGUI UICamera fallback (synthetic): source='{nguiCam.name}', synthetic='{synthetic.name}'.");
                    return synthetic;
                }
            }

            return null;
        }

        private static Camera FindBestNguiUiCamera()
        {
            Type uiCameraType = ResolveTypeAnyAssembly("UICamera");
            if (uiCameraType == null) return null;

            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.GetComponent(uiCameraType) == null) continue;

                if (best == null || cam.depth > bestDepth)
                {
                    best = cam;
                    bestDepth = cam.depth;
                }
            }

            return best;
        }

        private static Camera FindBest2dReferenceCamera()
        {
            Camera nguiCamera = FindBestNguiUiCamera();
            if (nguiCamera != null)
            {
                return nguiCamera;
            }

            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy) continue;
                if (cam.targetTexture != null) continue;
                if (ShouldIgnoreCameraAsReference(cam)) continue;

                if (best == null || cam.depth > bestDepth)
                {
                    best = cam;
                    bestDepth = cam.depth;
                }
            }

            return best;
        }

        private static bool ShouldIgnoreCameraAsReference(Camera cam)
        {
            if (cam == null) return true;
            if ((cam.hideFlags & HideFlags.HideAndDontSave) != 0) return true;

            string name = cam.name;
            return name.IndexOf("UnityVRMod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OpenXR_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OpenVR_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("XrVrCamera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Camera GetOrCreateSynthetic3dFallbackCamera()
        {
            if (_synthetic3dFallbackCamera != null) return _synthetic3dFallbackCamera;

            var go = new GameObject("UnityVRMod_Synthetic3DFallbackCamera");
            go.tag = "Untagged";
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);

            var cam = go.AddComponent<Camera>();
            cam.enabled = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            cam.depth = float.MinValue;

            _synthetic3dFallbackCamera = cam;
            return _synthetic3dFallbackCamera;
        }

        private static Camera GetOrCreateSynthetic2dFallbackCamera(Camera referenceCamera)
        {
            Camera synthetic = _synthetic2dFallbackCamera;
            if (synthetic == null)
            {
                var go = new GameObject("UnityVRMod_Synthetic2DFallbackCamera");
                go.tag = "Untagged";
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);

                synthetic = go.AddComponent<Camera>();
                synthetic.enabled = true;
                synthetic.clearFlags = CameraClearFlags.SolidColor;
                synthetic.backgroundColor = Color.black;
                synthetic.cullingMask = 0;
                synthetic.renderingPath = RenderingPath.Forward;
                synthetic.allowHDR = false;
                synthetic.allowMSAA = false;
                synthetic.nearClipPlane = 0.01f;
                synthetic.farClipPlane = 1000f;
                synthetic.depth = float.MinValue;
                synthetic.orthographic = true;
                synthetic.orthographicSize = 1f;

                _synthetic2dFallbackCamera = synthetic;
            }

            if (referenceCamera != null)
            {
                synthetic.transform.SetPositionAndRotation(referenceCamera.transform.position, referenceCamera.transform.rotation);
                synthetic.nearClipPlane = Mathf.Max(0.001f, referenceCamera.nearClipPlane);
                synthetic.farClipPlane = Mathf.Max(synthetic.nearClipPlane + 0.01f, referenceCamera.farClipPlane);
            }

            return synthetic;
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
