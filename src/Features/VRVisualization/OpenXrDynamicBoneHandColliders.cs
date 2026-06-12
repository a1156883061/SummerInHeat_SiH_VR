#if OPENXR_BUILD
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrDynamicBoneHandColliders
    {
        private const float RefreshIntervalSeconds = 2.0f;

        private readonly List<System.Collections.IList> _patchedColliderLists = [];
        private readonly HashSet<int> _patchedListIds = [];

        private Type _dynamicBoneType;
        private Type _dynamicBoneColliderType;
        private FieldInfo _dynamicBoneCollidersField;
        private FieldInfo _dynamicBoneRootField;
        private FieldInfo _dynamicBoneNameField;
        private FieldInfo _dynamicBoneColliderRadiusField;
        private FieldInfo _dynamicBoneColliderCenterField;
        private FieldInfo _dynamicBoneColliderHeightField;
        private FieldInfo _dynamicBoneColliderDirectionField;
        private FieldInfo _dynamicBoneColliderBoundField;
        private bool _bindingsResolved;

        private GameObject _leftColliderObject;
        private GameObject _rightColliderObject;
        private Component _leftCollider;
        private Component _rightCollider;
        private float _nextRefreshTime;
        private string _lastSceneName = string.Empty;
        private bool _wasEnabled;
        private int _lastPatchedDynamicBoneCount = -1;
        private float _lastAppliedRadius = -1f;

        public void Update(
            bool hasLeftHandPose,
            Vector3 leftHandWorldPos,
            Quaternion leftHandWorldRot,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            if (!(ConfigManager.OpenXR_EnableDynamicBoneHandColliders?.Value ?? false))
            {
                if (_wasEnabled)
                {
                    Reset();
                }

                return;
            }

            _wasEnabled = true;
            if (!EnsureBindings() || !EnsureHandColliders())
            {
                return;
            }

            float radius = Mathf.Clamp(ConfigManager.OpenXR_DynamicBoneHandColliderRadius?.Value ?? 0.06f, 0.005f, 0.30f);
            if (!Mathf.Approximately(radius, _lastAppliedRadius))
            {
                _lastAppliedRadius = radius;
                ApplyColliderShape(_leftCollider, radius);
                ApplyColliderShape(_rightCollider, radius);
            }

            UpdateHandCollider(_leftColliderObject, _leftCollider, hasLeftHandPose, leftHandWorldPos, leftHandWorldRot);
            UpdateHandCollider(_rightColliderObject, _rightCollider, hasRightHandPose, rightHandWorldPos, rightHandWorldRot);

            string sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            if (!string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
            {
                _lastSceneName = sceneName;
                _nextRefreshTime = 0f;
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
                RefreshTargets();
            }
        }

        public void Reset()
        {
            RemovePatchedColliders();
            DestroyHandCollider(ref _leftColliderObject, ref _leftCollider);
            DestroyHandCollider(ref _rightColliderObject, ref _rightCollider);
            _nextRefreshTime = 0f;
            _lastSceneName = string.Empty;
            _wasEnabled = false;
            _lastPatchedDynamicBoneCount = -1;
            _lastAppliedRadius = -1f;
        }

        private bool EnsureBindings()
        {
            if (_bindingsResolved)
            {
                return _dynamicBoneType != null && _dynamicBoneColliderType != null && _dynamicBoneCollidersField != null;
            }

            _bindingsResolved = true;
            _dynamicBoneType = ResolveTypeAnyAssembly("DynamicBone");
            _dynamicBoneColliderType = ResolveTypeAnyAssembly("DynamicBoneCollider");
            if (_dynamicBoneType == null || _dynamicBoneColliderType == null)
            {
                VRModCore.LogWarning("[Physics][OpenXR] DynamicBone or DynamicBoneCollider type not found; hand collider proxies disabled.");
                return false;
            }

            _dynamicBoneCollidersField = _dynamicBoneType.GetField("m_Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneRootField = _dynamicBoneType.GetField("m_Root", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneNameField = _dynamicBoneType.GetField("Name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _dynamicBoneColliderRadiusField = _dynamicBoneColliderType.GetField("m_Radius", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneColliderCenterField = _dynamicBoneColliderType.GetField("m_Center", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneColliderHeightField = _dynamicBoneColliderType.GetField("m_Height", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneColliderDirectionField = _dynamicBoneColliderType.GetField("m_Direction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dynamicBoneColliderBoundField = _dynamicBoneColliderType.GetField("m_Bound", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (_dynamicBoneCollidersField == null)
            {
                VRModCore.LogWarning("[Physics][OpenXR] DynamicBone.m_Colliders field not found; hand collider proxies disabled.");
                return false;
            }

            return true;
        }

        private bool EnsureHandColliders()
        {
            if (_leftCollider != null && _rightCollider != null)
            {
                return true;
            }

            _leftCollider = CreateHandCollider("Left", out _leftColliderObject);
            _rightCollider = CreateHandCollider("Right", out _rightColliderObject);
            return _leftCollider != null && _rightCollider != null;
        }

        private Component CreateHandCollider(string handName, out GameObject colliderObject)
        {
            colliderObject = new GameObject($"UnityVRMod_OpenXR_DynamicBoneHandCollider_{handName}");
            UnityEngine.Object.DontDestroyOnLoad(colliderObject);
            colliderObject.hideFlags = HideFlags.HideAndDontSave;
            Component collider = colliderObject.AddComponent(_dynamicBoneColliderType);
            if (collider is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }

            return collider;
        }

        private void ApplyColliderShape(Component collider, float radius)
        {
            if (collider == null)
            {
                return;
            }

            TrySetField(_dynamicBoneColliderRadiusField, collider, radius);
            TrySetField(_dynamicBoneColliderCenterField, collider, Vector3.zero);
            TrySetField(_dynamicBoneColliderHeightField, collider, 0f);
            TrySetEnumField(_dynamicBoneColliderDirectionField, collider, "Y");
            TrySetEnumField(_dynamicBoneColliderBoundField, collider, "Outside");
        }

        private static void UpdateHandCollider(GameObject colliderObject, Component collider, bool hasPose, Vector3 worldPos, Quaternion worldRot)
        {
            if (colliderObject == null || collider == null)
            {
                return;
            }

            colliderObject.transform.position = worldPos;
            colliderObject.transform.rotation = worldRot;
            if (collider is Behaviour behaviour)
            {
                behaviour.enabled = hasPose;
            }
        }

        private void RefreshTargets()
        {
            if (_leftCollider == null || _rightCollider == null)
            {
                return;
            }

            RemovePatchedColliders();

            int patchedDynamicBones = 0;
            UnityEngine.Object[] dynamicBones = UnityEngine.Object.FindObjectsOfType(_dynamicBoneType);
            if (dynamicBones == null)
            {
                return;
            }

            for (int i = 0; i < dynamicBones.Length; i++)
            {
                Component dynamicBone = dynamicBones[i] as Component;
                if (dynamicBone == null || !ShouldPatchDynamicBone(dynamicBone))
                {
                    continue;
                }

                if (TryPatchDynamicBone(dynamicBone))
                {
                    patchedDynamicBones++;
                }
            }

            if (patchedDynamicBones != _lastPatchedDynamicBoneCount)
            {
                _lastPatchedDynamicBoneCount = patchedDynamicBones;
                VRModCore.Log($"[Physics][OpenXR] DynamicBone hand colliders patched into {patchedDynamicBones} DynamicBone components.");
            }
        }

        private bool ShouldPatchDynamicBone(Component dynamicBone)
        {
            // Test mode: patch every DynamicBone so we can verify which game body/clothing
            // systems actually respond to the controller DynamicBoneCollider proxies.
            return true;

            /*
            string path = GetGameObjectPath(dynamicBone.gameObject);
            string name = GetFieldString(_dynamicBoneNameField, dynamicBone);
            string rootPath = GetTransformFieldPath(_dynamicBoneRootField, dynamicBone);

            bool isBreast = path.IndexOf("Joint-Breast", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("胸", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                || rootPath.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isHair = path.IndexOf("Joint-Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || rootPath.IndexOf("/HS_hair/", StringComparison.OrdinalIgnoreCase) >= 0
                || rootPath.IndexOf("BP_hair", StringComparison.OrdinalIgnoreCase) >= 0;

            return isBreast || isHair;
            */
        }

        private bool TryPatchDynamicBone(Component dynamicBone)
        {
            object value = _dynamicBoneCollidersField.GetValue(dynamicBone);
            if (value is not System.Collections.IList colliders)
            {
                return false;
            }

            AddColliderIfMissing(colliders, _leftCollider);
            AddColliderIfMissing(colliders, _rightCollider);

            int listId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(colliders);
            if (_patchedListIds.Add(listId))
            {
                _patchedColliderLists.Add(colliders);
            }

            return true;
        }

        private void RemovePatchedColliders()
        {
            for (int i = 0; i < _patchedColliderLists.Count; i++)
            {
                System.Collections.IList colliders = _patchedColliderLists[i];
                if (colliders == null)
                {
                    continue;
                }

                RemoveColliderIfPresent(colliders, _leftCollider);
                RemoveColliderIfPresent(colliders, _rightCollider);
            }

            _patchedColliderLists.Clear();
            _patchedListIds.Clear();
        }

        private static void AddColliderIfMissing(System.Collections.IList colliders, Component collider)
        {
            if (colliders == null || collider == null || colliders.Contains(collider))
            {
                return;
            }

            colliders.Add(collider);
        }

        private static void RemoveColliderIfPresent(System.Collections.IList colliders, Component collider)
        {
            if (colliders == null || collider == null)
            {
                return;
            }

            while (colliders.Contains(collider))
            {
                colliders.Remove(collider);
            }
        }

        private static void DestroyHandCollider(ref GameObject colliderObject, ref Component collider)
        {
            collider = null;
            if (colliderObject != null)
            {
                UnityEngine.Object.Destroy(colliderObject);
                colliderObject = null;
            }
        }

        private static Type ResolveTypeAnyAssembly(string fullTypeName)
        {
            Type type = Type.GetType(fullTypeName, false);
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                type = assembly.GetType(fullTypeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void TrySetField(FieldInfo field, object target, object value)
        {
            if (field == null || target == null)
            {
                return;
            }

            try
            {
                field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static void TrySetEnumField(FieldInfo field, object target, string valueName)
        {
            if (field == null || target == null || !field.FieldType.IsEnum)
            {
                return;
            }

            try
            {
                object value = Enum.Parse(field.FieldType, valueName);
                field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static string GetFieldString(FieldInfo field, object target)
        {
            try
            {
                return field?.GetValue(target)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetTransformFieldPath(FieldInfo field, object target)
        {
            try
            {
                return field?.GetValue(target) is Transform transform ? GetGameObjectPath(transform.gameObject) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            var parts = new List<string>(16);
            Transform current = gameObject.transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
