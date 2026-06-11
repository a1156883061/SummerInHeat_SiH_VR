#if MONO
using HarmonyLib;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal static class CameraTypesVrFollowPatch
    {
        private const string HarmonyId = "com.newunitymodder.unityvrmod.cameratypesvrfollow";
        private const float SubCameraMoveDeadzone = 0.12f;
        private const float SubCameraMoveSpeedMetersPerSecond = 0.85f;
        private const float SubCameraRotateSpeedDegreesPerSecond = 95f;
        private const float SubCameraHeightStepMeters = 0.06f;

        private static Harmony _harmony;
        private static bool _installed;
        private static bool _installFailed;
        private static SubCameraMoveState _subCameraMoveState;
        private static bool _hasSubCameraManualPose;
        private static Vector3 _subCameraManualPosition;
        private static Quaternion _subCameraManualRotation = Quaternion.identity;

        private struct SubCameraMoveState
        {
            public bool MoveModeActive;
            public bool RotateModeActive;
            public float MoveAxisX;
            public float MoveAxisY;
            public bool HeightUpStep;
            public bool HeightDownStep;
        }

        public static void EnsureInstalled()
        {
            if (_installed || _installFailed) return;

            try
            {
                Type cameraTypesType = ResolveTypeAnyAssembly("CameraTypes");
                if (cameraTypesType == null)
                {
                    _installFailed = true;
                    VRModCore.LogWarning("[CameraTypesPatch] Type 'CameraTypes' not found; patch not installed.");
                    return;
                }

                MethodInfo updateMethod = cameraTypesType.GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (updateMethod == null)
                {
                    _installFailed = true;
                    VRModCore.LogWarning("[CameraTypesPatch] Method 'CameraTypes.Update()' not found; patch not installed.");
                    return;
                }

                MethodInfo postfixMethod = typeof(CameraTypesVrFollowPatch).GetMethod(
                    nameof(OnCameraTypesUpdatePostfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfixMethod == null)
                {
                    _installFailed = true;
                    VRModCore.LogWarning("[CameraTypesPatch] Internal postfix method not found; patch not installed.");
                    return;
                }

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(updateMethod, postfix: new HarmonyMethod(postfixMethod));
                _installed = true;
                VRModCore.Log("[CameraTypesPatch] Installed CameraTypes.Update postfix.");
            }
            catch (Exception ex)
            {
                _installFailed = true;
                VRModCore.LogWarning($"[CameraTypesPatch] Failed to install patch: {ex.Message}");
            }
        }

        private static void OnCameraTypesUpdatePostfix(object __instance)
        {
            if (__instance is not Component cameraComponent) return;
            if (string.Equals(cameraComponent.name, "Camera_Main", StringComparison.OrdinalIgnoreCase))
            {
                VrVisualizationManager manager = VRModCore.VrVisualizationFeature;
                if (manager?.IsGameCameraFollowModeActive == true)
                {
                    return;
                }

                if (manager == null) return;

                if (!manager.TryGetVrEyeCenterPoseWorld(out Vector3 vrMidpointWorld, out Quaternion vrCenterRotation)) return;
                cameraComponent.transform.position = vrMidpointWorld;
                cameraComponent.transform.rotation = vrCenterRotation;
                return;
            }

            if (string.Equals(cameraComponent.name, "Camera_MainSub", StringComparison.OrdinalIgnoreCase))
            {
                ApplySubCameraMoveMode(cameraComponent.transform);
            }
        }

        internal static void SetSubCameraMoveInput(
            bool moveModeActive,
            bool rotateModeActive,
            float moveAxisX,
            float moveAxisY,
            bool heightUpStep,
            bool heightDownStep)
        {
            _subCameraMoveState = new SubCameraMoveState
            {
                MoveModeActive = moveModeActive,
                RotateModeActive = rotateModeActive,
                MoveAxisX = Mathf.Clamp(moveAxisX, -1f, 1f),
                MoveAxisY = Mathf.Clamp(moveAxisY, -1f, 1f),
                HeightUpStep = heightUpStep,
                HeightDownStep = heightDownStep
            };
        }

        internal static void ResetSubCameraMoveInput()
        {
            _subCameraMoveState = default;
            _hasSubCameraManualPose = false;
            _subCameraManualPosition = Vector3.zero;
            _subCameraManualRotation = Quaternion.identity;
        }

        private static void ApplySubCameraMoveMode(Transform cameraTransform)
        {
            if (cameraTransform == null)
            {
                return;
            }

            if (!_subCameraMoveState.MoveModeActive)
            {
                if (_hasSubCameraManualPose)
                {
                    cameraTransform.position = _subCameraManualPosition;
                    cameraTransform.rotation = _subCameraManualRotation;
                }
                return;
            }

            if (!_hasSubCameraManualPose)
            {
                _subCameraManualPosition = cameraTransform.position;
                _subCameraManualRotation = cameraTransform.rotation;
                _hasSubCameraManualPose = true;
            }

            if (_subCameraMoveState.HeightUpStep)
            {
                _subCameraManualPosition += Vector3.up * SubCameraHeightStepMeters;
            }

            if (_subCameraMoveState.HeightDownStep)
            {
                _subCameraManualPosition -= Vector3.up * SubCameraHeightStepMeters;
            }

            float moveX = Mathf.Abs(_subCameraMoveState.MoveAxisX) >= SubCameraMoveDeadzone ? _subCameraMoveState.MoveAxisX : 0f;
            float moveY = Mathf.Abs(_subCameraMoveState.MoveAxisY) >= SubCameraMoveDeadzone ? _subCameraMoveState.MoveAxisY : 0f;
            if (Mathf.Abs(moveX) > 0.0001f || Mathf.Abs(moveY) > 0.0001f)
            {
                if (_subCameraMoveState.RotateModeActive)
                {
                    float absX = Mathf.Abs(moveX);
                    float absY = Mathf.Abs(moveY);
                    if (absX >= absY)
                    {
                        float yaw = moveX * SubCameraRotateSpeedDegreesPerSecond * Time.deltaTime;
                        Quaternion yawRot = Quaternion.AngleAxis(yaw, Vector3.up);
                        _subCameraManualRotation = yawRot * _subCameraManualRotation;
                    }
                    else
                    {
                        float pitch = -moveY * SubCameraRotateSpeedDegreesPerSecond * Time.deltaTime;
                        Vector3 rightAxis = _subCameraManualRotation * Vector3.right;
                        Quaternion pitchRot = Quaternion.AngleAxis(pitch, rightAxis);
                        _subCameraManualRotation = pitchRot * _subCameraManualRotation;
                    }
                }
                else
                {
                    Vector3 moveForward = Vector3.ProjectOnPlane(_subCameraManualRotation * Vector3.forward, Vector3.up);
                    if (moveForward.sqrMagnitude <= 0.0001f)
                    {
                        moveForward = Vector3.forward;
                    }
                    moveForward.Normalize();

                    Vector3 moveRight = Vector3.ProjectOnPlane(_subCameraManualRotation * Vector3.right, Vector3.up);
                    if (moveRight.sqrMagnitude <= 0.0001f)
                    {
                        moveRight = Vector3.right;
                    }
                    moveRight.Normalize();

                    Vector3 move = (moveRight * moveX) + (moveForward * moveY);
                    if (move.sqrMagnitude > 1f)
                    {
                        move.Normalize();
                    }

                    _subCameraManualPosition += move * (SubCameraMoveSpeedMetersPerSecond * Time.deltaTime);
                }
            }

            cameraTransform.position = _subCameraManualPosition;
            cameraTransform.rotation = _subCameraManualRotation;
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
#endif
