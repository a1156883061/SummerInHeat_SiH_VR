using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VrVisualization
{
    internal sealed class OpenXrControllerVisualizer
    {
        private const string DefaultObjRootPath = @"E:\gal\summer\oculus-controller-art-v1.8\Meta Quest Touch Plus\models\OBJ";
        private const string LeftObjFileName = "Left.obj";
        private const string RightObjFileName = "Right.obj";
        private const float TargetControllerLongestAxisMeters = 0.16f;
        private const float ControllerSphereDiameterMeters = 0.07f;
        private static readonly Quaternion LeftModelRotationOffset = Quaternion.identity;
        private static readonly Quaternion RightModelRotationOffset = Quaternion.identity;
        private static readonly Vector3 LeftModelPositionOffset = Vector3.zero;
        private static readonly Vector3 RightModelPositionOffset = Vector3.zero;
        private static readonly Color LeftControllerColor = new(0.10f, 0.45f, 1.00f, 0.95f);
        private static readonly Color RightControllerColor = new(1.00f, 0.15f, 0.15f, 0.95f);

        private static Mesh s_leftMesh;
        private static Mesh s_rightMesh;
        private static Texture2D s_leftTexture;
        private static Texture2D s_rightTexture;
        private static bool s_meshLoadAttempted;
        private static MethodInfo s_loadImageMethod;

        private Transform _rigTransform;
        private GameObject _root;
        private GameObject _leftControllerObject;
        private GameObject _rightControllerObject;
        private Material _leftControllerMaterial;
        private Material _rightControllerMaterial;

        public void Update(
            GameObject vrRig,
            int vrRigLayer,
            bool hasLeftPose,
            Vector3 leftWorldPos,
            Quaternion leftWorldRot,
            bool hasRightPose,
            Vector3 rightWorldPos,
            Quaternion rightWorldRot,
            OpenXrControlHand activeControlHand)
        {
            if (vrRig == null)
            {
                SetVisible(false, false);
                return;
            }

            EnsureInitialized(vrRig, vrRigLayer);
            if (_root == null)
            {
                return;
            }

            bool showLeft = hasLeftPose;
            bool showRight = hasRightPose;
            UpdateControllerObject(_leftControllerObject, showLeft, leftWorldPos, leftWorldRot, LeftModelPositionOffset, LeftModelRotationOffset);
            UpdateControllerObject(_rightControllerObject, showRight, rightWorldPos, rightWorldRot, RightModelPositionOffset, RightModelRotationOffset);
        }

        public void Teardown()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            if (_leftControllerMaterial != null)
            {
                UnityEngine.Object.Destroy(_leftControllerMaterial);
                _leftControllerMaterial = null;
            }

            if (_rightControllerMaterial != null)
            {
                UnityEngine.Object.Destroy(_rightControllerMaterial);
                _rightControllerMaterial = null;
            }

            _leftControllerObject = null;
            _rightControllerObject = null;
            _rigTransform = null;
        }

        private void EnsureInitialized(GameObject vrRig, int vrRigLayer)
        {
            if (vrRig == null)
            {
                return;
            }

            if (_rigTransform != null && _rigTransform != vrRig.transform)
            {
                Teardown();
            }

            if (_root != null)
            {
                return;
            }

            _rigTransform = vrRig.transform;
            _root = new GameObject("OpenXR_ControllerVisualizer");
            _root.transform.SetParent(vrRig.transform, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;

            TryLoadMeshesIfNeeded();
            bool hasObjModel = s_leftMesh != null || s_rightMesh != null;
            _leftControllerMaterial = CreateControllerMaterial(LeftControllerColor);
            _rightControllerMaterial = CreateControllerMaterial(RightControllerColor);

            _leftControllerObject = CreateControllerObject("OpenXR_LeftControllerModel", _leftControllerMaterial, vrRigLayer, s_leftMesh);
            _rightControllerObject = CreateControllerObject("OpenXR_RightControllerModel", _rightControllerMaterial, vrRigLayer, s_rightMesh);

            VRModCore.Log(hasObjModel
                ? "[OpenXR][ControllerModel] Using OBJ controller models for left/right controllers."
                : "[OpenXR][ControllerModel] OBJ models not found, using procedural capsule fallback.");
        }

        private void UpdateControllerObject(
            GameObject targetObject,
            bool hasPose,
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 localOffset,
            Quaternion rotationOffset)
        {
            if (targetObject == null)
            {
                return;
            }

            if (!hasPose)
            {
                if (targetObject.activeSelf)
                {
                    targetObject.SetActive(false);
                }
                return;
            }

            if (!targetObject.activeSelf)
            {
                targetObject.SetActive(true);
            }

            targetObject.transform.position = worldPos + (worldRot * localOffset);
            targetObject.transform.rotation = worldRot * rotationOffset;
        }

        private GameObject CreateControllerObject(string objectName, Material material, int vrRigLayer, Mesh mesh)
        {
            GameObject go;
            if (mesh != null)
            {
                go = new GameObject(objectName);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = material;
            }
            else
            {
                go = CreateProceduralController(objectName, material);
            }
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(go, vrRigLayer);
            return go;
        }

        private static GameObject CreateProceduralController(string name, Material material)
        {
            var root = new GameObject(name);
            // 手柄主体：胶囊体
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(0.03f, 0.05f, 0.03f);
            DestroyCollider(body);
            ApplyMaterial(body, material);
            // 追踪环：圆环（用缩放压扁的环体模拟）
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            ring.transform.localRotation = Quaternion.identity;
            ring.transform.localScale = new Vector3(0.035f, 0.008f, 0.035f);
            DestroyCollider(ring);
            ApplyMaterial(ring, material);
            return root;
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent("Collider");
            if (col != null) UnityEngine.Object.Destroy(col);
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = material;
            }
        }

        private static Material CreateControllerMaterial(Color color)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

            Material material = new Material(shader)
            {
                color = color
            };

            return material;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            int childCount = root.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
            }
        }

        private static void TryLoadMeshesIfNeeded()
        {
            if (s_meshLoadAttempted)
            {
                return;
            }

            s_meshLoadAttempted = true;

            string leftObjPath = Path.Combine(DefaultObjRootPath, LeftObjFileName);
            string rightObjPath = Path.Combine(DefaultObjRootPath, RightObjFileName);

            s_leftMesh = TryLoadObjMesh(leftObjPath, "OpenXR_LeftControllerOBJ", out string leftTexturePath);
            s_rightMesh = TryLoadObjMesh(rightObjPath, "OpenXR_RightControllerOBJ", out string rightTexturePath);

            s_leftTexture = TryLoadTexture(leftTexturePath, "OpenXR_LeftControllerTexture");
            s_rightTexture = TryLoadTexture(rightTexturePath, "OpenXR_RightControllerTexture");

            if (s_leftMesh == null || s_rightMesh == null)
            {
                VRModCore.LogWarning($"[OpenXR][ControllerModel] Failed to load OBJ controller models. Left='{leftObjPath}', Right='{rightObjPath}'");
            }
            else
            {
                VRModCore.Log($"[OpenXR][ControllerModel] Model mapping Left='{Path.GetFileName(leftObjPath)}' Right='{Path.GetFileName(rightObjPath)}' SphereDiameter={ControllerSphereDiameterMeters:F2}");
            }
        }

        private static Texture2D TryLoadTexture(string texturePath, string textureName)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            try
            {
                if (!File.Exists(texturePath))
                {
                    VRModCore.LogWarning($"[OpenXR][ControllerModel] Texture file missing: {texturePath}");
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(texturePath);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = textureName
                };
                if (!TryLoadImage(texture, bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 2;
                return texture;
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning($"[OpenXR][ControllerModel] Failed loading texture '{texturePath}': {ex.Message}");
                return null;
            }
        }

        private static bool TryLoadImage(Texture2D texture, byte[] bytes)
        {
            if (texture == null || bytes == null || bytes.Length == 0) return false;

            if (s_loadImageMethod == null)
            {
                s_loadImageMethod = ResolveLoadImageMethod();
                if (s_loadImageMethod == null)
                {
                    VRModCore.LogWarning("[OpenXR][ControllerModel] ImageConversion.LoadImage API not found; texture loading disabled in this Unity runtime.");
                    return false;
                }
            }

            try
            {
                ParameterInfo[] parameters = s_loadImageMethod.GetParameters();
                object result;
                if (parameters.Length == 2)
                {
                    result = s_loadImageMethod.Invoke(null, new object[] { texture, bytes });
                }
                else if (parameters.Length == 3)
                {
                    result = s_loadImageMethod.Invoke(null, new object[] { texture, bytes, false });
                }
                else
                {
                    return false;
                }

                if (s_loadImageMethod.ReturnType == typeof(bool))
                {
                    return result is bool ok && ok;
                }

                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning($"[OpenXR][ControllerModel] ImageConversion.LoadImage invoke failed: {ex.Message}");
                return false;
            }
        }

        private static MethodInfo ResolveLoadImageMethod()
        {
            const string imageConversionTypeName = "UnityEngine.ImageConversion";
            for (int i = 0; i < AppDomain.CurrentDomain.GetAssemblies().Length; i++)
            {
                Assembly asm = AppDomain.CurrentDomain.GetAssemblies()[i];
                if (asm == null) continue;

                Type imageConversionType = asm.GetType(imageConversionTypeName, false);
                if (imageConversionType == null) continue;

                MethodInfo[] methods = imageConversionType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    if (!string.Equals(method.Name, "LoadImage", StringComparison.Ordinal)) continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length < 2 || parameters.Length > 3) continue;
                    if (parameters[0].ParameterType != typeof(Texture2D)) continue;
                    if (parameters[1].ParameterType != typeof(byte[])) continue;
                    if (parameters.Length == 3 && parameters[2].ParameterType != typeof(bool)) continue;

                    return method;
                }
            }

            return null;
        }

        private static Mesh TryLoadObjMesh(string filePath, string meshName, out string diffuseTexturePath)
        {
            diffuseTexturePath = null;

            try
            {
                if (!File.Exists(filePath))
                {
                    VRModCore.LogWarning($"[OpenXR][ControllerModel] OBJ file missing: {filePath}");
                    return null;
                }

                string[] lines = File.ReadAllLines(filePath);
                if (lines == null || lines.Length == 0)
                {
                    VRModCore.LogWarning($"[OpenXR][ControllerModel] OBJ file is empty: {filePath}");
                    return null;
                }

                diffuseTexturePath = TryResolveTexturePathFromObjAndMtl(filePath, lines);

                var positions = new List<Vector3>(4096);
                var texCoords = new List<Vector2>(4096);
                var normals = new List<Vector3>(4096);

                var outVertices = new List<Vector3>(8192);
                var outTexCoords = new List<Vector2>(8192);
                var outNormals = new List<Vector3>(8192);
                var outTriangles = new List<int>(16384);
                var vertexCache = new Dictionary<string, int>(16384, StringComparer.Ordinal);

                bool sawNormals = false;
                NumberFormatInfo numberFormat = CultureInfo.InvariantCulture.NumberFormat;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("v ", StringComparison.Ordinal))
                    {
                        if (TryParseVector3(line, numberFormat, out Vector3 v))
                        {
                            positions.Add(v);
                        }
                        continue;
                    }

                    if (line.StartsWith("vt ", StringComparison.Ordinal))
                    {
                        if (TryParseVector2(line, numberFormat, out Vector2 vt))
                        {
                            texCoords.Add(vt);
                        }
                        continue;
                    }

                    if (line.StartsWith("vn ", StringComparison.Ordinal))
                    {
                        if (TryParseVector3(line, numberFormat, out Vector3 vn))
                        {
                            normals.Add(vn);
                            sawNormals = true;
                        }
                        continue;
                    }

                    if (!line.StartsWith("f ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] faceElements = line.Substring(2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (faceElements.Length < 3) continue;

                    int a = ResolveFaceVertex(faceElements[0], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);
                    for (int f = 1; f < faceElements.Length - 1; f++)
                    {
                        int b = ResolveFaceVertex(faceElements[f], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);
                        int c = ResolveFaceVertex(faceElements[f + 1], positions, texCoords, normals, outVertices, outTexCoords, outNormals, vertexCache);

                        if (a < 0 || b < 0 || c < 0) continue;
                        outTriangles.Add(a);
                        outTriangles.Add(b);
                        outTriangles.Add(c);
                    }
                }

                if (outVertices.Count == 0 || outTriangles.Count == 0)
                {
                    VRModCore.LogWarning($"[OpenXR][ControllerModel] OBJ has no valid mesh data: {filePath}");
                    return null;
                }

                var mesh = new Mesh
                {
                    name = meshName
                };

                if (outVertices.Count > 65535)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.SetVertices(outVertices);
                mesh.SetTriangles(outTriangles, 0, true);
                if (outTexCoords.Count == outVertices.Count)
                {
                    mesh.SetUVs(0, outTexCoords);
                }

                if (sawNormals && outNormals.Count == outVertices.Count)
                {
                    mesh.SetNormals(outNormals);
                }
                else
                {
                    mesh.RecalculateNormals();
                }

                mesh.RecalculateBounds();
                ApplyAutoScale(mesh, TargetControllerLongestAxisMeters);
                return mesh;
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning($"[OpenXR][ControllerModel] OBJ load exception for '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static string TryResolveTexturePathFromObjAndMtl(string objPath, string[] objLines)
        {
            try
            {
                string objDir = Path.GetDirectoryName(objPath) ?? string.Empty;
                var candidateMtlPaths = new List<string>();

                for (int i = 0; i < objLines.Length; i++)
                {
                    string line = objLines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    line = line.Trim();
                    if (!line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase)) continue;

                    string mtllib = line.Substring(7).Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(mtllib)) continue;

                    string resolvedMtl = ResolveFilePath(mtllib, objDir, objDir);
                    if (!string.IsNullOrWhiteSpace(resolvedMtl))
                    {
                        candidateMtlPaths.Add(resolvedMtl);
                    }
                }

                string defaultMtl = Path.ChangeExtension(objPath, ".mtl");
                if (File.Exists(defaultMtl))
                {
                    candidateMtlPaths.Add(defaultMtl);
                }

                for (int i = 0; i < candidateMtlPaths.Count; i++)
                {
                    string mtlPath = candidateMtlPaths[i];
                    if (!File.Exists(mtlPath)) continue;

                    string texturePath = TryResolveTexturePathFromMtl(mtlPath, objDir);
                    if (!string.IsNullOrWhiteSpace(texturePath))
                    {
                        return texturePath;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string TryResolveTexturePathFromMtl(string mtlPath, string objDir)
        {
            string mtlDir = Path.GetDirectoryName(mtlPath) ?? objDir;
            string[] mtlLines = File.ReadAllLines(mtlPath);
            for (int i = 0; i < mtlLines.Length; i++)
            {
                string line = mtlLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                line = line.Trim();
                if (!line.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase)) continue;

                string rawTextureRef = line.Substring(7).Trim().Trim('"');
                string resolved = ResolveFilePath(rawTextureRef, mtlDir, objDir);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        private static string ResolveFilePath(string fileRef, string primaryDir, string secondaryDir)
        {
            if (string.IsNullOrWhiteSpace(fileRef))
            {
                return null;
            }

            string normalized = fileRef.Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            {
                return normalized;
            }

            string primaryPath = Path.Combine(primaryDir ?? string.Empty, normalized);
            if (File.Exists(primaryPath))
            {
                return primaryPath;
            }

            string secondaryPath = Path.Combine(secondaryDir ?? string.Empty, normalized);
            if (File.Exists(secondaryPath))
            {
                return secondaryPath;
            }

            string fileNameOnly = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(fileNameOnly))
            {
                string primaryNamePath = Path.Combine(primaryDir ?? string.Empty, fileNameOnly);
                if (File.Exists(primaryNamePath))
                {
                    return primaryNamePath;
                }

                string secondaryNamePath = Path.Combine(secondaryDir ?? string.Empty, fileNameOnly);
                if (File.Exists(secondaryNamePath))
                {
                    return secondaryNamePath;
                }
            }

            return null;
        }

        private static bool TryParseVector3(string line, NumberFormatInfo numberFormat, out Vector3 result)
        {
            result = Vector3.zero;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, numberFormat, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, numberFormat, out float y)) return false;
            if (!float.TryParse(parts[3], NumberStyles.Float, numberFormat, out float z)) return false;
            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseVector2(string line, NumberFormatInfo numberFormat, out Vector2 result)
        {
            result = Vector2.zero;
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, numberFormat, out float x)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, numberFormat, out float y)) return false;
            result = new Vector2(x, y);
            return true;
        }

        private static int ResolveFaceVertex(
            string token,
            List<Vector3> positions,
            List<Vector2> texCoords,
            List<Vector3> normals,
            List<Vector3> outVertices,
            List<Vector2> outTexCoords,
            List<Vector3> outNormals,
            Dictionary<string, int> cache)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }

            if (cache.TryGetValue(token, out int cached))
            {
                return cached;
            }

            string[] indices = token.Split('/');
            int pIndex = ParseObjIndex(indices, 0, positions.Count);
            if (pIndex < 0 || pIndex >= positions.Count)
            {
                return -1;
            }

            int tIndex = ParseObjIndex(indices, 1, texCoords.Count);
            int nIndex = ParseObjIndex(indices, 2, normals.Count);

            int outIndex = outVertices.Count;
            outVertices.Add(positions[pIndex]);
            outTexCoords.Add((tIndex >= 0 && tIndex < texCoords.Count) ? texCoords[tIndex] : Vector2.zero);
            outNormals.Add((nIndex >= 0 && nIndex < normals.Count) ? normals[nIndex] : Vector3.zero);

            cache[token] = outIndex;
            return outIndex;
        }

        private static int ParseObjIndex(string[] elements, int elementIndex, int sourceCount)
        {
            if (elements == null || elementIndex >= elements.Length) return -1;
            string raw = elements[elementIndex];
            if (string.IsNullOrEmpty(raw)) return -1;

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return -1;
            }

            if (parsed > 0)
            {
                return parsed - 1;
            }

            if (parsed < 0)
            {
                return sourceCount + parsed;
            }

            return -1;
        }

        private static void ApplyAutoScale(Mesh mesh, float targetLongestAxisMeters)
        {
            if (mesh == null) return;
            if (targetLongestAxisMeters <= 0f) return;

            Bounds bounds = mesh.bounds;
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest <= 0.0001f) return;

            float scale = targetLongestAxisMeters / longest;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] *= scale;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private void SetVisible(bool leftVisible, bool rightVisible)
        {
            if (_leftControllerObject != null && _leftControllerObject.activeSelf != leftVisible)
            {
                _leftControllerObject.SetActive(leftVisible);
            }

            if (_rightControllerObject != null && _rightControllerObject.activeSelf != rightVisible)
            {
                _rightControllerObject.SetActive(rightVisible);
            }
        }
    }
}
