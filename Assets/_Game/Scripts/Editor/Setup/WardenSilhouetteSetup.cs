using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Isolated, same-Avatar mesh candidates. No prefab/scene save, import,
    /// materials, transforms, components, enabled flags or gameplay changes.</summary>
    public static class WardenSilhouetteSetup
    {
        public const string AssetRoot = "Assets/_Game/Art/WardenSilhouetteCandidate";
        public const string CharacterPack = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/addons/kaykit_character_pack_adventures";
        public const string HelmetSourcePath = CharacterPack + "/Characters/fbx/Knight.fbx";
        public const string SwordSourcePath = CharacterPack + "/Assets/fbx/sword_1handed.fbx";
        public const string LicensePath = CharacterPack + "/LICENSE.txt";
        public const string ReferencePack = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/01_Warden_Boss/Knight Character by @Quaternius/FBX";
        public const string ReferenceHelmetPath = ReferencePack + "/Helmet2.fbx";
        public const string ReferenceSwordPath = ReferencePack + "/Katana.fbx";
        public const string CanonicalWeaponPath = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RuneSword.prefab";
        public const string HelmetMeshPath = AssetRoot + "/Meshes/M_Warden_KayHelmet.asset";
        public const string SwordMeshPath = AssetRoot + "/Meshes/M_Warden_KaySword.asset";
        public const string HelmetNode = "Helmet_Closed";
        public const string SwordNode = "Sword_M6_Warden_Equipped";
        // Front-view review: the native blade face was edge-on at the unchanged
        // Warden socket. Rotate geometry only; never rotate the socket/Model.
        public const float SwordSourceYaw = 90f;
        // Centroid regions in the source sword's canonical Y interval; material
        // ordering is the existing Katana's blade(0), grip(1), guard(2).
        public const float GripFraction = .24f;
        public const float GuardFraction = .32f;

        [Serializable] public sealed class Diagnostics
        {
            public string scope = "Editor-only static mesh candidate; original Avatar, pose, slots, transforms, enabled flags and colliders retained. Not gameplay/contact/network/performance acceptance.";
            public string licensePath = LicensePath;
            public string[] sourcePaths, sourceSha256, sourceGuids;
            public int helmetSourceVertices, swordSourceVertices;
            public int[] swordTrianglesPerSlot;
            public Bounds originalHelmetLocal, candidateHelmetLocal, originalSwordCanonical, candidateSwordCanonical;
            public float helmetYaw, swordSourceYaw, originalYMin, originalYMax, candidateYMin, candidateYMax;
            public Matrix4x4 referenceFilterToWeaponRoot;
            public bool sourcesUnchanged;
        }
        public sealed class PreparedMeshes { public Mesh helmet, sword; public Diagnostics diagnostics; }
        public sealed class ApplyResult
        {
            public int changedMeshFilters;
            public bool helmetFound, swordFound;
            public PreparedMeshes prepared;
            public string[] matchedFilters = new string[0];
        }

        /// <summary>Prepares ONLY project-owned mesh assets. Reference meshes are
        /// the old local meshes, and the matrix maps old sword mesh to weapon root.</summary>
        public static PreparedMeshes Prepare(Mesh referenceHelmet, Mesh referenceSword,
            Matrix4x4 referenceFilterToWeaponRoot, float helmetYaw = 0f)
        {
            RequireIdle();
            RequireMesh(referenceHelmet); RequireMesh(referenceSword);
            if (new[] { referenceHelmet, referenceSword }.Any(m => AssetDatabase.GetAssetPath(m).StartsWith(AssetRoot + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Never fit against a previous candidate; use original reference meshes.");
            var frozen = CaptureSourceFingerprints(referenceHelmet, referenceSword);
            Mesh helmet = null, sword = null;
            var diagnostics = new Diagnostics {
                sourcePaths = frozen.Keys.ToArray(), sourceSha256 = frozen.Values.ToArray(),
                sourceGuids = frozen.Keys.Select(p => AssetDatabase.AssetPathToGUID(p.EndsWith(".meta", StringComparison.Ordinal) ? p.Substring(0, p.Length - 5) : p)).ToArray(),
                originalHelmetLocal = referenceHelmet.bounds, helmetYaw = NormalizeYaw(helmetYaw),
                swordSourceYaw = SwordSourceYaw,
                referenceFilterToWeaponRoot = referenceFilterToWeaponRoot
            };
            try
            {
                string license = File.ReadAllText(LicensePath);
                if (!license.Contains("CC0") || !license.Contains("KayKit")) throw new InvalidDataException("Missing actual KayKit CC0 license.");
                Mesh helmetSource = LoadHelmetSource();
                Mesh swordSource = LoadSingleSourceMesh(SwordSourcePath);
                if (helmetSource.vertexCount != 610 || swordSource.vertexCount != 417)
                    throw new InvalidDataException("The reviewed helmet/sword source geometry changed; review the new originals.");
                diagnostics.helmetSourceVertices = helmetSource.vertexCount; diagnostics.swordSourceVertices = swordSource.vertexCount;
                helmet = BuildHelmet(helmetSource, referenceHelmet.bounds, diagnostics.helmetYaw);
                sword = BuildSword(swordSource, referenceSword, referenceFilterToWeaponRoot);
                diagnostics.candidateHelmetLocal = helmet.bounds;
                diagnostics.originalSwordCanonical = VertexBounds(referenceSword, referenceFilterToWeaponRoot);
                diagnostics.candidateSwordCanonical = VertexBounds(sword, referenceFilterToWeaponRoot);
                diagnostics.originalYMin = diagnostics.originalSwordCanonical.min.y; diagnostics.originalYMax = diagnostics.originalSwordCanonical.max.y;
                diagnostics.candidateYMin = diagnostics.candidateSwordCanonical.min.y; diagnostics.candidateYMax = diagnostics.candidateSwordCanonical.max.y;
                diagnostics.swordTrianglesPerSlot = Enumerable.Range(0, 3).Select(s => sword.GetIndices(s).Length / 3).ToArray();
                string helmetPath = diagnostics.helmetYaw == 0f ? HelmetMeshPath : HelmetMeshPath.Replace(".asset", "_yaw" + BitConverter.ToInt32(BitConverter.GetBytes(diagnostics.helmetYaw), 0).ToString("X8") + ".asset");
                Mesh savedHelmet = StoreOwnedMesh(helmet, helmetPath); helmet = null;
                Mesh savedSword = StoreOwnedMesh(sword, SwordMeshPath); sword = null;
                return new PreparedMeshes { helmet = savedHelmet, sword = savedSword, diagnostics = diagnostics };
            }
            finally
            {
                if (helmet != null) Object.DestroyImmediate(helmet);
                if (sword != null) Object.DestroyImmediate(sword);
                diagnostics.sourcesUnchanged = frozen.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                if (!diagnostics.sourcesUnchanged) throw new InvalidOperationException("A source/model/meta/license changed during candidate preparation.");
            }
        }

        /// <summary>Only exact approved existing mesh filters are remapped. The
        /// caller owns preview/capture and any separately approved production save.</summary>
        public static ApplyResult ApplyToTemporary(GameObject root, float helmetYaw = 0f)
        {
            ValidateTemporary(root);
            Targets targets = FindTargets(root);
            if (targets.helmet == null && targets.sword == null) return new ApplyResult();
            Mesh referenceHelmet = LoadSingleSourceMesh(ReferenceHelmetPath);
            Mesh referenceSword = LoadSingleSourceMesh(ReferenceSwordPath);
            Matrix4x4 matrix;
            if (targets.sword != null) matrix = FilterToRoot(targets.sword, targets.weaponRoot);
            else
            {
                GameObject canonical = AssetDatabase.LoadAssetAtPath<GameObject>(CanonicalWeaponPath);
                if (canonical == null) throw new InvalidDataException("Missing original canonical Warden weapon.");
                Targets weapon = FindTargets(canonical);
                if (weapon.sword == null) throw new InvalidDataException("Missing canonical Warden Model mesh.");
                matrix = FilterToRoot(weapon.sword, weapon.weaponRoot);
            }
            return ApplyPrepared(root, Prepare(referenceHelmet, referenceSword, matrix, helmetYaw));
        }

        public static ApplyResult ApplyPrepared(GameObject root, PreparedMeshes prepared)
        {
            ValidateTemporary(root);
            if (prepared == null || prepared.helmet == null || prepared.sword == null || prepared.helmet.subMeshCount != 1 || prepared.sword.subMeshCount != 3)
                throw new ArgumentException("Prepare the complete one-slot helmet and three-slot sword first.", nameof(prepared));
            Targets targets = FindTargets(root);
            var result = new ApplyResult { prepared = prepared, helmetFound = targets.helmet != null, swordFound = targets.sword != null };
            var paths = new List<string>();
            // Validate ALL target slots before the first reference mutation.
            foreach (MeshFilter filter in new[] { targets.helmet, targets.sword }.Where(f => f != null))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                int count = filter == targets.helmet ? 1 : 3;
                if (renderer == null || renderer.sharedMaterials.Length != count)
                    throw new InvalidDataException("Unexpected original material-slot structure: " + HierarchyPath(filter.transform));
                paths.Add(HierarchyPath(filter.transform));
            }
            foreach (MeshFilter filter in new[] { targets.helmet, targets.sword }.Where(f => f != null))
            {
                Mesh candidate = filter == targets.helmet ? prepared.helmet : prepared.sword;
                if (filter.sharedMesh == candidate) continue;
                filter.sharedMesh = candidate;
                if (PrefabUtility.IsPartOfPrefabInstance(filter)) PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                result.changedMeshFilters++;
            }
            result.matchedFilters = paths.ToArray();
            return result;
        }

        /// <summary>Pure geometry: isotropic yawed fit INSIDE old mesh-local bounds.</summary>
        public static Mesh BuildHelmet(Mesh source, Bounds referenceBounds, float yaw = 0f)
        {
            RequireMesh(source); RequireBounds(referenceBounds);
            if (source.subMeshCount != 1) throw new InvalidDataException("Reviewed helmet must have one material slot.");
            Quaternion rotation = Quaternion.Euler(0f, NormalizeYaw(yaw), 0f);
            Matrix4x4 rotate = Matrix4x4.Rotate(rotation);
            Bounds rotated = VertexBounds(source, rotate);
            float scale = Mathf.Min(referenceBounds.size.x / rotated.size.x, referenceBounds.size.y / rotated.size.y, referenceBounds.size.z / rotated.size.z);
            // Two ppm containment margin absorbs float rounding, not an art resize.
            scale *= .999998f;
            Matrix4x4 transform = Matrix4x4.TRS(referenceBounds.center - rotated.center * scale, Quaternion.identity, Vector3.one * scale) * rotate;
            Mesh mesh = TransformCopy(source, transform, "M_Warden_KayHelmet");
            Bounds actual = VertexBounds(mesh, Matrix4x4.identity);
            mesh.bounds = new Bounds(referenceBounds.center, actual.size);
            return mesh;
        }

        /// <summary>Pure geometry: rotate the source blade face around canonical Y,
        /// fit old Y endpoints/centre X/Z, then map back through the original filter.</summary>
        public static Mesh BuildSword(Mesh source, Mesh reference, Matrix4x4 referenceFilterToWeaponRoot)
        {
            RequireMesh(source); RequireMesh(reference); RequireMatrix(referenceFilterToWeaponRoot);
            if (source.subMeshCount != 1 || source.GetTopology(0) != MeshTopology.Triangles)
                throw new InvalidDataException("Reviewed sword must have one triangular source submesh.");
            Matrix4x4 sourceRotation = Matrix4x4.Rotate(Quaternion.Euler(0f, SwordSourceYaw, 0f));
            Bounds sourceBounds = VertexBounds(source, Matrix4x4.identity);
            Bounds from = VertexBounds(source, sourceRotation), to = VertexBounds(reference, referenceFilterToWeaponRoot);
            float scale = to.size.y / from.size.y;
            Matrix4x4 fit = Matrix4x4.TRS(new Vector3(to.center.x - from.center.x * scale, to.min.y - from.min.y * scale, to.center.z - from.center.z * scale), Quaternion.identity, Vector3.one * scale);
            Mesh mesh = TransformCopy(source, referenceFilterToWeaponRoot.inverse * fit * sourceRotation, "M_Warden_KaySword");
            var slots = new[] { new List<int>(), new List<int>(), new List<int>() };
            Vector3[] vertices = source.vertices; int[] triangles = source.GetTriangles(0);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float y = ((vertices[triangles[i]].y + vertices[triangles[i + 1]].y + vertices[triangles[i + 2]].y) / 3f - sourceBounds.min.y) / sourceBounds.size.y;
                int slot = y <= GripFraction ? 1 : y <= GuardFraction ? 2 : 0;
                slots[slot].Add(triangles[i]); slots[slot].Add(triangles[i + 1]); slots[slot].Add(triangles[i + 2]);
            }
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(slots[s].ToArray(), s, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Bounds VertexBounds(Mesh mesh, Matrix4x4 matrix)
        {
            RequireMesh(mesh); RequireMatrix(matrix);
            Vector3[] vertices = mesh.vertices;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 point in vertices)
            {
                Vector3 transformed = matrix.MultiplyPoint3x4(point);
                if (!Finite(transformed.x) || !Finite(transformed.y) || !Finite(transformed.z)) throw new InvalidDataException("Non-finite mesh position.");
                bounds.Encapsulate(transformed);
            }
            RequireBounds(bounds); return bounds;
        }

        /// <summary>Local TRS accumulation avoids world-position cancellation and
        /// excludes Actor/socket scale, so offline and network use the same space.</summary>
        public static Matrix4x4 FilterToRoot(MeshFilter filter, Transform weaponRoot)
        {
            if (filter == null || weaponRoot == null) throw new ArgumentNullException("Mesh filter/root required.");
            Matrix4x4 matrix = Matrix4x4.identity;
            Transform cursor = filter.transform;
            while (cursor != weaponRoot)
            {
                if (cursor == null) throw new ArgumentException("Filter is not a descendant of weapon root.");
                matrix = Matrix4x4.TRS(cursor.localPosition, cursor.localRotation, cursor.localScale) * matrix;
                cursor = cursor.parent;
            }
            RequireMatrix(matrix); return matrix;
        }

        public static Dictionary<string, string> CaptureSourceFingerprints(Mesh referenceHelmet = null, Mesh referenceSword = null)
        {
            string[] required = { HelmetSourcePath, SwordSourcePath, LicensePath, ReferenceHelmetPath, ReferenceSwordPath };
            foreach (string path in required) if (!File.Exists(path) || !File.Exists(path + ".meta")) throw new FileNotFoundException("Missing source/license/meta: " + path);
            var roots = required.Concat(new[] { referenceHelmet, referenceSword }.Where(m => m != null).Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p))).Distinct();
            var paths = roots.Concat(roots.SelectMany(p => AssetDatabase.GetDependencies(p, true))).Where(File.Exists);
            return paths.SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p }).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash);
        }

        static Mesh TransformCopy(Mesh source, Matrix4x4 matrix, string name)
        {
            RequireMatrix(matrix);
            if (source.blendShapeCount != 0) throw new InvalidDataException("This static accessory candidate cannot adapt blend shapes.");
            Mesh mesh = Object.Instantiate(source); mesh.name = name; mesh.hideFlags = HideFlags.None;
            mesh.vertices = source.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            Vector3[] normals = source.normals;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            if (normals.Length == source.vertexCount) mesh.normals = normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            else if (normals.Length != 0) throw new InvalidDataException("Invalid source normal count.");
            Vector4[] tangents = source.tangents;
            if (tangents.Length == source.vertexCount)
            {
                Vector3[] outputNormals = mesh.normals;
                mesh.tangents = tangents.Select((t, i) => {
                    Vector3 v = matrix.MultiplyVector(new Vector3(t.x, t.y, t.z));
                    if (outputNormals.Length == source.vertexCount) v -= outputNormals[i] * Vector3.Dot(outputNormals[i], v);
                    v.Normalize(); return new Vector4(v.x, v.y, v.z, t.w * (matrix.determinant < 0f ? -1f : 1f));
                }).ToArray();
            }
            else if (tangents.Length != 0) throw new InvalidDataException("Invalid source tangent count.");
            if (normals.Length == 0) mesh.RecalculateNormals();
            mesh.RecalculateBounds(); return mesh;
        }

        static Mesh LoadHelmetSource()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(HelmetSourcePath);
            if (source == null) throw new InvalidDataException("Missing licensed Knight model.");
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name == "Knight_Helmet" && f.sharedMesh != null).ToArray();
            if (filters.Length != 1) throw new InvalidDataException("Expected exactly one static Knight_Helmet mesh.");
            return filters[0].sharedMesh;
        }
        static Mesh LoadSingleSourceMesh(string path)
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            if (meshes.Length != 1) throw new InvalidDataException("Expected one source mesh: " + path);
            return meshes[0];
        }
        sealed class Targets { public MeshFilter helmet, sword; public Transform weaponRoot; }
        static Targets FindTargets(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var result = new Targets();
            Transform[] helmets = all.Where(t => t.name == HelmetNode).ToArray();
            if (helmets.Length > 1) throw new InvalidDataException("Ambiguous exact Warden helmet target.");
            if (helmets.Length == 1) result.helmet = SingleFilter(helmets[0]);
            Transform[] weapons = all.Where(t => t.name == SwordNode).ToArray();
            if (weapons.Length > 1) throw new InvalidDataException("Ambiguous exact Warden equipped weapon target.");
            if (weapons.Length == 1) result.weaponRoot = weapons[0];
            else
            {
                Object original = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (root.name == "P_M6_Warden_RuneSword" || (original != null && AssetDatabase.GetAssetPath(original) == CanonicalWeaponPath)) result.weaponRoot = root.transform;
            }
            if (result.weaponRoot != null)
            {
                Transform model = result.weaponRoot.Find("Model");
                if (model == null) throw new InvalidDataException("Missing exact Warden weapon Model child.");
                result.sword = SingleFilter(model);
            }
            return result;
        }
        static MeshFilter SingleFilter(Transform root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length != 1) throw new InvalidDataException("Expected exactly one existing mesh filter under " + HierarchyPath(root));
            return filters[0];
        }
        static Mesh StoreOwnedMesh(Mesh candidate, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                if (File.Exists(path)) throw new InvalidDataException("Candidate target is not a Mesh: " + path);
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(candidate, path); return candidate;
            }
            try
            {
                if (!EqualGeometry(existing, candidate))
                {
                    EditorUtility.CopySerialized(candidate, existing);
                    EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
                }
                return existing;
            }
            finally { Object.DestroyImmediate(candidate); }
        }
        public static bool EqualGeometry(Mesh a, Mesh b)
        {
            if (a == null || b == null || a.name != b.name || a.indexFormat != b.indexFormat || !a.bounds.Equals(b.bounds) || a.subMeshCount != b.subMeshCount ||
                !a.GetVertexAttributes().SequenceEqual(b.GetVertexAttributes()) || !a.vertices.SequenceEqual(b.vertices) || !a.normals.SequenceEqual(b.normals) ||
                !a.tangents.SequenceEqual(b.tangents) || !a.colors.SequenceEqual(b.colors) || a.blendShapeCount != b.blendShapeCount ||
                !a.bindposes.SequenceEqual(b.bindposes) || !a.boneWeights.SequenceEqual(b.boneWeights)) return false;
            // These are reviewed static accessories, never blend-shape/skin adapters.
            if (a.blendShapeCount != 0) return false;
            for (int channel = 0; channel < 8; channel++)
            {
                var left = new List<Vector4>(); var right = new List<Vector4>(); a.GetUVs(channel, left); b.GetUVs(channel, right);
                if (!left.SequenceEqual(right)) return false;
            }
            for (int s = 0; s < a.subMeshCount; s++) if (a.GetTopology(s) != b.GetTopology(s) || !a.GetIndices(s).SequenceEqual(b.GetIndices(s))) return false;
            return true;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidDataException("Invalid owned candidate directory.");
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create candidate folder.");
        }
        static string HierarchyPath(Transform transform) => transform.parent == null ? transform.name : HierarchyPath(transform.parent) + "/" + transform.name;
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static float NormalizeYaw(float yaw) { if (!Finite(yaw)) throw new ArgumentOutOfRangeException(nameof(yaw)); return Mathf.Repeat(yaw, 360f); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void RequireMesh(Mesh mesh) { if (mesh == null || mesh.vertexCount == 0) throw new ArgumentException("Non-empty mesh required."); }
        static void RequireBounds(Bounds bounds)
        {
            if (!Finite(bounds.center.x) || !Finite(bounds.center.y) || !Finite(bounds.center.z) || !Finite(bounds.size.x) || !Finite(bounds.size.y) || !Finite(bounds.size.z) ||
                bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f) throw new InvalidDataException("Finite non-zero 3D bounds required.");
        }
        static void RequireMatrix(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) throw new ArgumentException("Finite affine transform required.");
            if (Mathf.Abs(matrix.m30) > .000001f || Mathf.Abs(matrix.m31) > .000001f || Mathf.Abs(matrix.m32) > .000001f || matrix.m33 != 1f || !Finite(matrix.determinant) || Mathf.Abs(matrix.determinant) < 1e-12f)
                throw new ArgumentException("Invertible affine transform required.");
        }
        static void ValidateTemporary(GameObject root)
        {
            RequireIdle();
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (EditorUtility.IsPersistent(root)) throw new InvalidOperationException("Instantiate or load temporary prefab contents; never modify a source asset.");
        }
        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Edit mode required for a static candidate.");
        }
    }
}
