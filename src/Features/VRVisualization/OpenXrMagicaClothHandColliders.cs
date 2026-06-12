#if OPENXR_BUILD
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrMagicaClothHandColliders
    {
        private const float RefreshIntervalSeconds = 2.0f;
        private static readonly Vector3 DisabledColliderPosition = new(0f, -10000f, 0f);

        private readonly List<System.Collections.IList> _patchedColliderLists = [];
        private readonly HashSet<int> _patchedListIds = [];
        private readonly List<RuntimeRegistration> _runtimeRegistrations = [];
        private readonly HashSet<string> _runtimeRegistrationIds = [];

        private Type _magicaClothType;
        private Type _magicaSphereColliderType;
        private Type _colliderComponentType;
        private Type _magicaManagerType;

        private PropertyInfo _serializeDataProperty;
        private PropertyInfo _processProperty;
        private PropertyInfo _teamIdProperty;
        private PropertyInfo _magicaManagerColliderProperty;
        private FieldInfo _serializeDataField;
        private FieldInfo _colliderCollisionConstraintField;
        private FieldInfo _colliderListField;
        private FieldInfo _colliderCenterField;
        private MethodInfo _sphereSetSizeMethod;
        private MethodInfo _colliderManagerAddColliderMethod;
        private MethodInfo _colliderManagerRemoveColliderMethod;
        private MethodInfo _colliderManagerUpdateParametersMethod;
        private bool _bindingsResolved;

        private GameObject _leftColliderObject;
        private GameObject _rightColliderObject;
        private Component _leftCollider;
        private Component _rightCollider;
        private float _nextRefreshTime;
        private string _lastSceneName = string.Empty;
        private bool _wasEnabled;
        private int _lastPatchedMagicaClothCount = -1;
        private float _lastAppliedRadius = -1f;

        public void Update(
            bool hasLeftHandPose,
            Vector3 leftHandWorldPos,
            Quaternion leftHandWorldRot,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos,
            Quaternion rightHandWorldRot)
        {
            if (!(ConfigManager.OpenXR_EnableMagicaClothHandColliders?.Value ?? false))
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

            float radius = Mathf.Clamp(ConfigManager.OpenXR_MagicaClothHandColliderRadius?.Value ?? 0.06f, 0.005f, 0.30f);
            if (!Mathf.Approximately(radius, _lastAppliedRadius))
            {
                _lastAppliedRadius = radius;
                ApplyColliderShape(_leftCollider, radius);
                ApplyColliderShape(_rightCollider, radius);
            }

            UpdateHandCollider(_leftColliderObject, hasLeftHandPose, leftHandWorldPos, leftHandWorldRot);
            UpdateHandCollider(_rightColliderObject, hasRightHandPose, rightHandWorldPos, rightHandWorldRot);

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
            _lastPatchedMagicaClothCount = -1;
            _lastAppliedRadius = -1f;
        }

        private bool EnsureBindings()
        {
            if (_bindingsResolved)
            {
                return _magicaClothType != null
                    && _magicaSphereColliderType != null
                    && _colliderComponentType != null
                    && _colliderListField != null;
            }

            _bindingsResolved = true;
            _magicaClothType = ResolveTypeAnyAssembly("MagicaCloth2.MagicaCloth");
            _magicaSphereColliderType = ResolveTypeAnyAssembly("MagicaCloth2.MagicaSphereCollider");
            _colliderComponentType = ResolveTypeAnyAssembly("MagicaCloth2.ColliderComponent");
            _magicaManagerType = ResolveTypeAnyAssembly("MagicaCloth2.MagicaManager");

            if (_magicaClothType == null || _magicaSphereColliderType == null || _colliderComponentType == null)
            {
                VRModCore.LogWarning("[Physics][OpenXR] MagicaCloth2 types not found; MagicaCloth hand collider proxies disabled.");
                return false;
            }

            _serializeDataProperty = _magicaClothType.GetProperty("SerializeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _processProperty = _magicaClothType.GetProperty("Process", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _serializeDataField = _magicaClothType.GetField("serializeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            Type serializeDataType = ResolveTypeAnyAssembly("MagicaCloth2.ClothSerializeData");
            _colliderCollisionConstraintField = serializeDataType?.GetField("colliderCollisionConstraint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            Type colliderCollisionSerializeDataType = ResolveTypeAnyAssembly("MagicaCloth2.ColliderCollisionConstraint+SerializeData");
            _colliderListField = colliderCollisionSerializeDataType?.GetField("colliderList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _colliderCenterField = _colliderComponentType.GetField("center", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _sphereSetSizeMethod = _magicaSphereColliderType.GetMethod("SetSize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(float)], null);

            Type clothProcessType = ResolveTypeAnyAssembly("MagicaCloth2.ClothProcess");
            _teamIdProperty = clothProcessType?.GetProperty("TeamId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _magicaManagerColliderProperty = _magicaManagerType?.GetProperty("Collider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Type colliderManagerType = ResolveTypeAnyAssembly("MagicaCloth2.ColliderManager");
            if (colliderManagerType != null && clothProcessType != null)
            {
                _colliderManagerAddColliderMethod = colliderManagerType.GetMethod(
                    "AddCollider",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [clothProcessType, _colliderComponentType],
                    null);
                _colliderManagerRemoveColliderMethod = colliderManagerType.GetMethod(
                    "RemoveCollider",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [_colliderComponentType, typeof(int)],
                    null);
                _colliderManagerUpdateParametersMethod = colliderManagerType.GetMethod(
                    "UpdateParameters",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [_colliderComponentType, typeof(int)],
                    null);
            }

            if (_colliderListField == null)
            {
                VRModCore.LogWarning("[Physics][OpenXR] MagicaCloth colliderList field not found; MagicaCloth hand collider proxies disabled.");
                return false;
            }

            if (_colliderManagerAddColliderMethod == null)
            {
                VRModCore.LogWarning("[Physics][OpenXR] MagicaCloth ColliderManager.AddCollider not found; falling back to serialized colliderList patch only.");
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
            colliderObject = new GameObject($"UnityVRMod_OpenXR_MagicaClothHandCollider_{handName}");
            UnityEngine.Object.DontDestroyOnLoad(colliderObject);
            colliderObject.hideFlags = HideFlags.HideAndDontSave;
            colliderObject.transform.position = DisabledColliderPosition;
            return colliderObject.AddComponent(_magicaSphereColliderType);
        }

        private void ApplyColliderShape(Component collider, float radius)
        {
            if (collider == null)
            {
                return;
            }

            TrySetField(_colliderCenterField, collider, Vector3.zero);
            TryInvoke(_sphereSetSizeMethod, collider, radius);

            object manager = GetColliderManager();
            if (manager == null || _colliderManagerUpdateParametersMethod == null)
            {
                return;
            }

            for (int i = 0; i < _runtimeRegistrations.Count; i++)
            {
                RuntimeRegistration registration = _runtimeRegistrations[i];
                if (registration.Collider == collider)
                {
                    TryInvoke(_colliderManagerUpdateParametersMethod, manager, collider, registration.TeamId);
                }
            }
        }

        private static void UpdateHandCollider(GameObject colliderObject, bool hasPose, Vector3 worldPos, Quaternion worldRot)
        {
            if (colliderObject == null)
            {
                return;
            }

            colliderObject.transform.position = hasPose ? worldPos : DisabledColliderPosition;
            colliderObject.transform.rotation = hasPose ? worldRot : Quaternion.identity;
        }

        private void RefreshTargets()
        {
            if (_leftCollider == null || _rightCollider == null)
            {
                return;
            }

            RemovePatchedColliders();

            int patchedMagicaCloths = 0;
            UnityEngine.Object[] magicaCloths = UnityEngine.Object.FindObjectsOfType(_magicaClothType);
            if (magicaCloths == null)
            {
                return;
            }

            for (int i = 0; i < magicaCloths.Length; i++)
            {
                Component magicaCloth = magicaCloths[i] as Component;
                if (magicaCloth == null)
                {
                    continue;
                }

                if (TryPatchMagicaCloth(magicaCloth))
                {
                    patchedMagicaCloths++;
                }
            }

            if (patchedMagicaCloths != _lastPatchedMagicaClothCount)
            {
                _lastPatchedMagicaClothCount = patchedMagicaCloths;
                VRModCore.Log($"[Physics][OpenXR] MagicaCloth hand colliders patched into {patchedMagicaCloths} MagicaCloth components.");
            }
        }

        private bool TryPatchMagicaCloth(Component magicaCloth)
        {
            bool patchedSerializedList = TryPatchSerializedColliderList(magicaCloth);
            bool patchedRuntime = TryRegisterRuntimeCollider(magicaCloth, _leftCollider)
                | TryRegisterRuntimeCollider(magicaCloth, _rightCollider);

            return patchedSerializedList || patchedRuntime;
        }

        private bool TryPatchSerializedColliderList(Component magicaCloth)
        {
            object serializeData = GetSerializeData(magicaCloth);
            object colliderCollisionConstraint = TryGetField(_colliderCollisionConstraintField, serializeData);
            if (colliderCollisionConstraint == null)
            {
                return false;
            }

            System.Collections.IList colliders = GetOrCreateColliderList(colliderCollisionConstraint);
            if (colliders == null)
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

        private bool TryRegisterRuntimeCollider(Component magicaCloth, Component collider)
        {
            if (magicaCloth == null || collider == null || _colliderManagerAddColliderMethod == null)
            {
                return false;
            }

            object process = GetPropertyValue(_processProperty, magicaCloth);
            object manager = GetColliderManager();
            if (process == null || manager == null)
            {
                return false;
            }

            int teamId = GetTeamId(process);
            if (teamId < 0)
            {
                return false;
            }

            int clothId = magicaCloth.GetInstanceID();
            int colliderId = collider.GetInstanceID();
            string registrationId = $"{clothId}:{colliderId}:{teamId}";
            if (!_runtimeRegistrationIds.Add(registrationId))
            {
                return true;
            }

            if (!TryInvoke(_colliderManagerAddColliderMethod, manager, process, collider))
            {
                _runtimeRegistrationIds.Remove(registrationId);
                return false;
            }

            _runtimeRegistrations.Add(new RuntimeRegistration(registrationId, teamId, collider));
            return true;
        }

        private object GetSerializeData(Component magicaCloth)
        {
            object serializeData = GetPropertyValue(_serializeDataProperty, magicaCloth);
            return serializeData ?? TryGetField(_serializeDataField, magicaCloth);
        }

        private System.Collections.IList GetOrCreateColliderList(object colliderCollisionConstraint)
        {
            object value = TryGetField(_colliderListField, colliderCollisionConstraint);
            if (value is System.Collections.IList existing)
            {
                return existing;
            }

            try
            {
                Type listType = typeof(List<>).MakeGenericType(_colliderComponentType);
                object created = Activator.CreateInstance(listType);
                _colliderListField.SetValue(colliderCollisionConstraint, created);
                return created as System.Collections.IList;
            }
            catch
            {
                return null;
            }
        }

        private object GetColliderManager()
        {
            return GetPropertyValue(_magicaManagerColliderProperty, null);
        }

        private int GetTeamId(object process)
        {
            object value = GetPropertyValue(_teamIdProperty, process);
            return value is int teamId ? teamId : -1;
        }

        private void RemovePatchedColliders()
        {
            for (int i = _runtimeRegistrations.Count - 1; i >= 0; i--)
            {
                RuntimeRegistration registration = _runtimeRegistrations[i];
                object manager = GetColliderManager();
                if (manager != null && _colliderManagerRemoveColliderMethod != null)
                {
                    TryInvoke(_colliderManagerRemoveColliderMethod, manager, registration.Collider, registration.TeamId);
                }
            }

            _runtimeRegistrations.Clear();
            _runtimeRegistrationIds.Clear();

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

        private static object GetPropertyValue(PropertyInfo property, object target)
        {
            if (property == null)
            {
                return null;
            }

            try
            {
                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static object TryGetField(FieldInfo field, object target)
        {
            if (field == null || target == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(target);
            }
            catch
            {
                return null;
            }
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

        private static bool TryInvoke(MethodInfo method, object target, params object[] args)
        {
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct RuntimeRegistration
        {
            public RuntimeRegistration(string id, int teamId, Component collider)
            {
                Id = id;
                TeamId = teamId;
                Collider = collider;
            }

            public string Id { get; }
            public int TeamId { get; }
            public Component Collider { get; }
        }
    }
}
#endif
