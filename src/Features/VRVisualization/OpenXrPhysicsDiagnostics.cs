#if OPENXR_BUILD && PHYSICS_LOG
using System.Text;
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrPhysicsDiagnostics
    {
        private const int MaxAncestorDepth = 8;

        private static readonly string[] CandidateTypeTokens =
        [
            "DynamicBone",
            "DynamicBoneCollider",
            "MagicaCloth",
            "Magica",
            "Obi",
            "SpringBone",
            "SpringSphereCollider",
            "SpringCapsuleCollider",
            "SpringPanelCollider",
            "SPCRJointDynamics",
            "RASCALSkinnedMeshCollider",
            "Cloth",
            "Rigidbody",
            "Collider"
        ];

        private float _nextLogTime;
        private bool _wasEnabled;
        private string _lastSceneName = string.Empty;
        private MethodInfo _physicsOverlapSphereMethod;
        private bool _physicsBindingResolved;
        private readonly HashSet<int> _loggedRootPhysicsInventories = [];

        public void Update(
            bool hasLeftHandPose,
            Vector3 leftHandWorldPos,
            bool hasRightHandPose,
            Vector3 rightHandWorldPos)
        {
            if (!(ConfigManager.OpenXR_EnablePhysicsDiagnostics?.Value ?? false))
            {
                _wasEnabled = false;
                return;
            }

            if (!_wasEnabled)
            {
                _wasEnabled = true;
                _nextLogTime = 0f;
                VRModCore.LogWarning("[PhysicsDiag][OpenXR] Enabled. Move a controller near the target body area to log colliders and candidate physics components.");
            }

            float interval = Mathf.Max(0.2f, ConfigManager.OpenXR_PhysicsDiagnosticsIntervalSeconds?.Value ?? 1.0f);
            if (Time.unscaledTime < _nextLogTime)
            {
                return;
            }

            _nextLogTime = Time.unscaledTime + interval;
            MaybeLogSceneChange();

            bool loggedAny = false;
            if (hasLeftHandPose)
            {
                loggedAny |= ProbeHand("Left", leftHandWorldPos);
            }

            if (hasRightHandPose)
            {
                loggedAny |= ProbeHand("Right", rightHandWorldPos);
            }

            if (!loggedAny)
            {
                VRModCore.LogRuntimeDebug("[PhysicsDiag][OpenXR] No nearby colliders for current controller poses.");
            }
        }

        public void Reset()
        {
            _nextLogTime = 0f;
            _wasEnabled = false;
            _lastSceneName = string.Empty;
            _loggedRootPhysicsInventories.Clear();
        }

        private bool ProbeHand(string handName, Vector3 handWorldPos)
        {
            float radius = Mathf.Clamp(ConfigManager.OpenXR_PhysicsDiagnosticsRadius?.Value ?? 0.08f, 0.005f, 1.0f);
            if (!TryOverlapSphere(handWorldPos, radius, out Array overlaps) || overlaps == null || overlaps.Length == 0)
            {
                return false;
            }

            var hits = new List<ColliderHit>(overlaps.Length);
            for (int i = 0; i < overlaps.Length; i++)
            {
                object colliderObj = overlaps.GetValue(i);
                if (colliderObj is not Component collider || collider == null || collider.gameObject == null)
                {
                    continue;
                }

                Vector3 closestPoint = GetClosestPoint(collider, handWorldPos);
                hits.Add(new ColliderHit
                {
                    Collider = collider,
                    ClosestPoint = closestPoint,
                    Distance = Vector3.Distance(handWorldPos, closestPoint)
                });
            }

            if (hits.Count == 0)
            {
                return false;
            }

            hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

            var sb = new StringBuilder(4096);
            sb.Append("[PhysicsDiag][OpenXR] ");
            sb.Append(handName);
            sb.Append(" hand probe radius=");
            sb.Append(radius.ToString("F3"));
            sb.Append(" hitCount=");
            sb.Append(hits.Count);
            sb.Append(" pos=");
            sb.Append(FormatVector(handWorldPos));

            for (int i = 0; i < hits.Count; i++)
            {
                AppendColliderReport(sb, i + 1, hits[i], handWorldPos);
                AppendRootPhysicsInventoryIfRelevant(sb, hits[i].Collider.gameObject);
            }

            VRModCore.LogWarning(sb.ToString());
            return true;
        }

        private void AppendRootPhysicsInventoryIfRelevant(StringBuilder sb, GameObject hitObject)
        {
            if (hitObject == null || !IsRelevantPhysicsHit(hitObject))
            {
                return;
            }

            GameObject root = GetSceneRoot(hitObject);
            if (root == null)
            {
                return;
            }

            int rootId = root.GetInstanceID();
            if (!_loggedRootPhysicsInventories.Add(rootId))
            {
                return;
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            var candidates = new List<Component>();
            int totalCandidateCount = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                string typeName = type.FullName ?? type.Name;
                if (!IsRootInventoryCandidate(typeName))
                {
                    continue;
                }

                totalCandidateCount++;
                candidates.Add(component);
            }

            sb.AppendLine();
            sb.Append("    root-physics-inventory root=");
            sb.Append(GetGameObjectPath(root));
            sb.Append(" candidates=");
            sb.Append(totalCandidateCount);

            for (int i = 0; i < candidates.Count; i++)
            {
                Component component = candidates[i];
                Type type = component.GetType();
                sb.AppendLine();
                sb.Append("      ");
                sb.Append(GetGameObjectPath(component.gameObject));
                sb.Append(" :: ");
                sb.Append(type.FullName ?? type.Name);
                sb.Append(DescribeKnownFields(component));
                AppendKnownReferenceLists(sb, component, type);
            }

            AppendFocusedPhysicsFieldDump(sb, components);
            AppendAllComponentsDump(sb, components);
        }

        private static void AppendAllComponentsDump(StringBuilder sb, Component[] components)
        {
            if (components == null || components.Length == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.Append("    all-components-dump count=");
            sb.Append(components.Length);

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                sb.AppendLine();
                sb.Append("      #");
                sb.Append(i + 1);
                sb.Append(" ");

                if (component == null)
                {
                    sb.Append("<missing component>");
                    continue;
                }

                Type type = component.GetType();
                sb.Append(GetGameObjectPath(component.gameObject));
                sb.Append(" :: ");
                sb.Append(type.FullName ?? type.Name);
                sb.Append(DescribeKnownFields(component));
                AppendKnownReferenceLists(sb, component, type);
            }
        }

        private static void AppendColliderReport(StringBuilder sb, int index, ColliderHit hit, Vector3 handWorldPos)
        {
            Component collider = hit.Collider;
            GameObject go = collider.gameObject;
            Type colliderType = collider.GetType();
            sb.AppendLine();
            sb.Append("  #");
            sb.Append(index);
            sb.Append(" collider=");
            sb.Append(colliderType.FullName ?? colliderType.Name);
            sb.Append(" name='");
            sb.Append(go.name);
            sb.Append("' layer=");
            sb.Append(LayerMask.LayerToName(go.layer));
            sb.Append("(");
            sb.Append(go.layer);
            sb.Append(") dist=");
            sb.Append(hit.Distance.ToString("F4"));
            sb.Append(" closest=");
            sb.Append(FormatVector(hit.ClosestPoint));
            sb.Append(" path=");
            sb.Append(GetGameObjectPath(go));

            AppendHierarchyComponents(sb, go);
            AppendCandidateSystems(sb, go);
        }

        private static void AppendHierarchyComponents(StringBuilder sb, GameObject go)
        {
            sb.AppendLine();
            sb.Append("    hierarchy-components:");

            Transform current = go.transform;
            int depth = 0;
            while (current != null && depth < MaxAncestorDepth)
            {
                sb.AppendLine();
                sb.Append("      ");
                sb.Append(depth == 0 ? "self" : $"parent{depth}");
                sb.Append(" ");
                sb.Append(current.name);
                sb.Append(": ");
                AppendComponentTypeList(sb, current.gameObject.GetComponents<Component>());

                current = current.parent;
                depth++;
            }
        }

        private static void AppendCandidateSystems(StringBuilder sb, GameObject go)
        {
            var candidates = new List<string>();
            CollectCandidateSystems(go, candidates);

            Transform current = go.transform.parent;
            int depth = 0;
            while (current != null && depth < MaxAncestorDepth)
            {
                CollectCandidateSystems(current.gameObject, candidates);
                current = current.parent;
                depth++;
            }

            if (candidates.Count == 0)
            {
                sb.AppendLine();
                sb.Append("    candidate-systems: <none on self/parents>");
                return;
            }

            sb.AppendLine();
            sb.Append("    candidate-systems:");
            for (int i = 0; i < candidates.Count; i++)
            {
                sb.AppendLine();
                sb.Append("      ");
                sb.Append(candidates[i]);
            }
        }

        private static void CollectCandidateSystems(GameObject go, List<string> output)
        {
            if (go == null)
            {
                return;
            }

            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                Type type = component.GetType();
                string typeName = type.FullName ?? type.Name;
                if (!IsCandidateType(typeName))
                {
                    continue;
                }

                output.Add($"{GetGameObjectPath(go)} :: {typeName}{DescribeKnownFields(component)}");
            }
        }

        private static bool IsCandidateType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            for (int i = 0; i < CandidateTypeTokens.Length; i++)
            {
                if (typeName.IndexOf(CandidateTypeTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRootInventoryCandidate(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            return typeName.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Magica", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Obi", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Spring", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("SPCRJointDynamics", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("RASCALSkinnedMeshCollider", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRelevantPhysicsHit(GameObject hitObject)
        {
            string path = GetGameObjectPath(hitObject);
            if (path.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("PanelCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            Transform current = hitObject.transform;
            int depth = 0;
            while (current != null && depth < MaxAncestorDepth)
            {
                Component[] components = current.gameObject.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null) continue;

                    Type type = component.GetType();
                    string typeName = type.FullName ?? type.Name;
                    if (typeName.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Magica", StringComparison.OrdinalIgnoreCase) >= 0
                        || typeName.IndexOf("Obi", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static void AppendFocusedPhysicsFieldDump(StringBuilder sb, Component[] components)
        {
            if (components == null || components.Length == 0)
            {
                return;
            }

            bool wroteHeader = false;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !IsFocusedPhysicsComponent(component))
                {
                    continue;
                }

                if (!wroteHeader)
                {
                    wroteHeader = true;
                    sb.AppendLine();
                    sb.Append("    focused-physics-field-dump:");
                }

                AppendComponentFieldDump(sb, component);
            }
        }

        private static bool IsFocusedPhysicsComponent(Component component)
        {
            Type type = component.GetType();
            string typeName = type.FullName ?? type.Name;
            string path = GetGameObjectPath(component.gameObject);

            if (typeName.IndexOf("DynamicBoneController_Breast", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (typeName.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string nameValue = GetFieldValueAsString(component, type, "Name");
                string rootValue = GetFieldValueAsString(component, type, "m_Root");
                return path.IndexOf("Joint-Breast", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Joint-Hair", StringComparison.OrdinalIgnoreCase) >= 0
                    || nameValue.IndexOf("胸", StringComparison.OrdinalIgnoreCase) >= 0
                    || nameValue.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                    || rootValue.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (typeName.IndexOf("DynamicBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return path.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("BP_Breast", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("BP_Spine2", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Spine2xx", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Pelvisxx", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("HS_Head", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (typeName.IndexOf("Magica", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return path.EndsWith("CH_Prefub_A", StringComparison.Ordinal)
                    || path.IndexOf("PanelCollider", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static string GetFieldValueAsString(Component component, Type type, string fieldName)
        {
            try
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field == null ? string.Empty : FormatSimpleValue(field.GetValue(component));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendComponentFieldDump(StringBuilder sb, Component component)
        {
            Type type = component.GetType();
            sb.AppendLine();
            sb.Append("      ");
            sb.Append(GetGameObjectPath(component.gameObject));
            sb.Append(" :: ");
            sb.Append(type.FullName ?? type.Name);

            int appended = 0;
            for (Type current = type; current != null && current != typeof(Component); current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field == null || field.IsStatic || !IsUsefulFocusedField(field))
                    {
                        continue;
                    }

                    appended++;
                    object value = null;
                    bool hasValue = false;
                    try
                    {
                        value = field.GetValue(component);
                        hasValue = true;
                    }
                    catch
                    {
                    }

                    sb.AppendLine();
                    sb.Append("        ");
                    sb.Append(field.FieldType.FullName ?? field.FieldType.Name);
                    sb.Append(" ");
                    sb.Append(field.Name);
                    sb.Append("=");
                    sb.Append(hasValue ? FormatDiagnosticValue(value) : "<read-failed>");
                }
            }

            if (appended == 0)
            {
                sb.AppendLine();
                sb.Append("        <no focused fields>");
            }
        }

        private static bool IsUsefulFocusedField(FieldInfo field)
        {
            string fieldName = field.Name ?? string.Empty;
            string fieldType = field.FieldType.FullName ?? field.FieldType.Name;
            return fieldName.IndexOf("Breast", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Collision", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Bound", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Radius", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Center", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Direction", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Root", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Stiffness", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Elastic", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Damping", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Force", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldName.IndexOf("Gravity", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldType.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0
                || fieldType.IndexOf("Transform", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatDiagnosticValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is string text)
            {
                return text;
            }

            if (value is System.Collections.IEnumerable enumerable && value is not Transform && value is not GameObject && value is not Component)
            {
                int total = 0;
                var entries = new List<string>();
                foreach (object item in enumerable)
                {
                    total++;
                    entries.Add(FormatReferenceListItem(item));
                }

                if (total == 0)
                {
                    return "[]";
                }

                return $"Count={total} [{string.Join("; ", entries)}]";
            }

            return FormatSimpleValue(value);
        }

        private static string DescribeKnownFields(Component component)
        {
            try
            {
                Type type = component.GetType();
                var parts = new List<string>(6);
                AppendFieldValue(parts, component, type, "Name");
                AppendFieldValue(parts, component, type, "RootName");
                AppendFieldValue(parts, component, type, "JointBreast");
                AppendFieldValue(parts, component, type, "BreastSize");
                AppendFieldValue(parts, component, type, "Breast_Physics_Status_L");
                AppendFieldValue(parts, component, type, "Breast_Physics_Status_R");
                AppendFieldValue(parts, component, type, "m_Root");
                AppendFieldValue(parts, component, type, "Root");
                AppendFieldCount(parts, component, type, "m_Colliders");
                AppendFieldCount(parts, component, type, "dynamicBones");

                return parts.Count == 0 ? string.Empty : " [" + string.Join(", ", parts) + "]";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendFieldValue(List<string> parts, Component component, Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            object value = field.GetValue(component);
            parts.Add($"{fieldName}={FormatSimpleValue(value)}");
        }

        private static void AppendFieldCount(List<string> parts, Component component, Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            object value = field.GetValue(component);
            if (value == null)
            {
                parts.Add($"{fieldName}=null");
                return;
            }

            PropertyInfo countProperty = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            object count = countProperty?.GetValue(value, null);
            parts.Add(count != null ? $"{fieldName}.Count={count}" : $"{fieldName}=<set>");
        }

        private static string FormatSimpleValue(object value)
        {
            if (value == null) return "null";
            if (value is float f) return f.ToString("F3");
            if (value is double d) return d.ToString("F3");
            if (value is GameObject go) return GetGameObjectPath(go);
            if (value is Transform transform) return GetGameObjectPath(transform.gameObject);
            if (value is Component component) return $"{GetGameObjectPath(component.gameObject)} :: {component.GetType().FullName ?? component.GetType().Name}";
            return value.ToString();
        }

        private static void AppendKnownReferenceLists(StringBuilder sb, Component component, Type type)
        {
            AppendReferenceListField(sb, component, type, "m_Colliders");
            AppendReferenceListField(sb, component, type, "dynamicBones");
        }

        private static void AppendReferenceListField(StringBuilder sb, Component component, Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                return;
            }

            object value = field.GetValue(component);
            if (value is not System.Collections.IEnumerable enumerable || value is string)
            {
                return;
            }

            int total = 0;
            var entries = new List<string>();
            foreach (object item in enumerable)
            {
                total++;
                entries.Add(FormatReferenceListItem(item));
            }

            if (total == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.Append("        ");
            sb.Append(fieldName);
            sb.Append(": ");
            sb.Append(string.Join("; ", entries));
        }

        private static string FormatReferenceListItem(object item)
        {
            if (item == null)
            {
                return "null";
            }

            if (item is GameObject go)
            {
                return GetGameObjectPath(go);
            }

            if (item is Component component)
            {
                return $"{GetGameObjectPath(component.gameObject)} :: {component.GetType().FullName ?? component.GetType().Name}";
            }

            return item.ToString();
        }

        private static void AppendComponentTypeList(StringBuilder sb, Component[] components)
        {
            if (components == null || components.Length == 0)
            {
                sb.Append("<none>");
                return;
            }

            int appended = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                if (appended > 0)
                {
                    sb.Append(", ");
                }

                Type type = component.GetType();
                sb.Append(type.FullName ?? type.Name);
                appended++;
            }
        }

        private void MaybeLogSceneChange()
        {
            string sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            if (string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
            {
                return;
            }

            _lastSceneName = sceneName;
            VRModCore.LogWarning($"[PhysicsDiag][OpenXR] Active scene='{sceneName}'.");
        }

        private bool TryOverlapSphere(Vector3 center, float radius, out Array overlaps)
        {
            overlaps = null;
            if (!EnsurePhysicsBinding())
            {
                return false;
            }

            try
            {
                ParameterInfo[] parameters = _physicsOverlapSphereMethod.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = center;
                args[1] = radius;
                for (int i = 2; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (i == 2 && parameterType == typeof(int))
                    {
                        args[i] = ~0;
                    }
                    else if (parameterType.IsEnum)
                    {
                        args[i] = TryParseEnum(parameterType, "Collide") ?? Enum.ToObject(parameterType, 0);
                    }
                    else if (parameterType.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(parameterType);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                overlaps = _physicsOverlapSphereMethod.Invoke(null, args) as Array;
                return overlaps != null;
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning($"[PhysicsDiag][OpenXR] Physics.OverlapSphere failed: {ex.Message}");
                return false;
            }
        }

        private bool EnsurePhysicsBinding()
        {
            if (_physicsBindingResolved)
            {
                return _physicsOverlapSphereMethod != null;
            }

            _physicsBindingResolved = true;
            Type physicsType = ResolveTypeAnyAssembly("UnityEngine.Physics");
            if (physicsType == null)
            {
                VRModCore.LogWarning("[PhysicsDiag][OpenXR] UnityEngine.Physics type not found.");
                return false;
            }

            MethodInfo best = null;
            MethodInfo[] methods = physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "OverlapSphere", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2)
                {
                    continue;
                }

                if (parameters[0].ParameterType != typeof(Vector3) || parameters[1].ParameterType != typeof(float))
                {
                    continue;
                }

                if (!method.ReturnType.IsArray)
                {
                    continue;
                }

                best = method;
                if (parameters.Length >= 4)
                {
                    break;
                }
            }

            _physicsOverlapSphereMethod = best;
            if (_physicsOverlapSphereMethod == null)
            {
                VRModCore.LogWarning("[PhysicsDiag][OpenXR] Physics.OverlapSphere overload not found.");
            }

            return _physicsOverlapSphereMethod != null;
        }

        private static Type ResolveTypeAnyAssembly(string fullTypeName)
        {
            Type type = Type.GetType(fullTypeName, false);
            if (type != null) return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null) continue;

                type = assembly.GetType(fullTypeName, false);
                if (type != null) return type;
            }

            return Type.GetType($"{fullTypeName}, UnityEngine.PhysicsModule", false);
        }

        private static object TryParseEnum(Type enumType, string value)
        {
            try
            {
                return Enum.Parse(enumType, value);
            }
            catch
            {
                return null;
            }
        }

        private static Vector3 GetClosestPoint(Component collider, Vector3 point)
        {
            try
            {
                MethodInfo method = collider.GetType().GetMethod("ClosestPoint", BindingFlags.Public | BindingFlags.Instance, null, [ typeof(Vector3) ], null);
                if (method != null && method.ReturnType == typeof(Vector3))
                {
                    object result = method.Invoke(collider, [ point ]);
                    if (result is Vector3 closestPoint)
                    {
                        return closestPoint;
                    }
                }
            }
            catch
            {
            }

            return collider.transform.position;
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null) return "<null>";

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

        private static GameObject GetSceneRoot(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            Transform current = gameObject.transform;
            while (current.parent != null)
            {
                current = current.parent;
            }

            return current.gameObject;
        }

        private static string FormatVector(Vector3 vector)
        {
            return $"({vector.x:F3},{vector.y:F3},{vector.z:F3})";
        }

        private struct ColliderHit
        {
            public Component Collider;
            public Vector3 ClosestPoint;
            public float Distance;
        }
    }
}
#endif
