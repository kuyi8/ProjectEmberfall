using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenSilhouetteTests
    {
        readonly List<Object> owned = new List<Object>();
        [TearDown] public void Cleanup()
        {
            foreach (Object value in owned.AsEnumerable().Reverse()) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [TestCase(0f)] [TestCase(45f)] [TestCase(180f)] [TestCase(270f)]
        public void Helmet_IsotropicFitInsideOldBounds_CentresExactlyAndKeepsUvs(float yaw)
        {
            Mesh source = Box(new Vector3(0f, .57f, -.08f), new Vector3(1.14f, 1.36f, 1.26f));
            string before = EditorJsonUtility.ToJson(source);
            var target = new Bounds(new Vector3(.03f, .2f, -.04f), new Vector3(.42f, .6f, .35f));
            Mesh result = Keep(WardenSilhouetteSetup.BuildHelmet(source, target, yaw));
            Assert.That(result.bounds.center, Is.EqualTo(target.center));
            Assert.That(result.subMeshCount, Is.EqualTo(1));
            foreach (Vector3 vertex in result.vertices)
            {
                Assert.That(vertex.x, Is.InRange(target.min.x, target.max.x));
                Assert.That(vertex.y, Is.InRange(target.min.y, target.max.y));
                Assert.That(vertex.z, Is.InRange(target.min.z, target.max.z));
            }
            Vector3[] input = source.vertices, output = result.vertices;
            float ratio = Vector3.Distance(output[0], output[1]) / Vector3.Distance(input[0], input[1]);
            for (int i = 1; i < input.Length; i++)
                Assert.That(Vector3.Distance(output[0], output[i]) / Vector3.Distance(input[0], input[i]), Is.EqualTo(ratio).Within(1e-6f));
            AssertUvs(source, result);
            Assert.That(result.GetTriangles(0), Is.EqualTo(source.GetTriangles(0)));
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
        }

        [TestCase(90f, 32.349483f)] [TestCase(35f, 1.75f)]
        public void Sword_FitsOriginalCanonicalEndpointsAndCentre_WithThreeCentroidSlots(float rotation, float scale)
        {
            Mesh source = Sword();
            Mesh reference = Box(new Vector3(.002f, .001f, .006f), new Vector3(.008f, .012f, .04f));
            string sourceBefore = EditorJsonUtility.ToJson(source), referenceBefore = EditorJsonUtility.ToJson(reference);
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(.003f, -.14f, .006f), Quaternion.Euler(rotation, 0f, 0f), Vector3.one * scale);
            Mesh result = Keep(WardenSilhouetteSetup.BuildSword(source, reference, matrix));
            Bounds oldBounds = WardenSilhouetteSetup.VertexBounds(reference, matrix), newBounds = WardenSilhouetteSetup.VertexBounds(result, matrix);
            Assert.That(newBounds.min.y, Is.EqualTo(oldBounds.min.y).Within(1e-6f));
            Assert.That(newBounds.max.y, Is.EqualTo(oldBounds.max.y).Within(1e-6f));
            Assert.That(newBounds.center.x, Is.EqualTo(oldBounds.center.x).Within(1e-6f));
            Assert.That(newBounds.center.z, Is.EqualTo(oldBounds.center.z).Within(1e-6f));
            Matrix4x4 sourceRotation = Matrix4x4.Rotate(Quaternion.Euler(0f, WardenSilhouetteSetup.SwordSourceYaw, 0f));
            Bounds rotatedSourceBounds = WardenSilhouetteSetup.VertexBounds(source, sourceRotation);
            float expectedScale = oldBounds.size.y / rotatedSourceBounds.size.y;
            Assert.That(WardenSilhouetteSetup.SwordSourceYaw, Is.EqualTo(90f));
            Assert.That(newBounds.size.x, Is.EqualTo(rotatedSourceBounds.size.x * expectedScale).Within(1e-6f));
            Assert.That(newBounds.size.z, Is.EqualTo(rotatedSourceBounds.size.z * expectedScale).Within(1e-6f));
            Assert.That(result.vertexCount, Is.EqualTo(source.vertexCount));
            Assert.That(result.subMeshCount, Is.EqualTo(3));
            Assert.That(result.GetTriangles(0), Is.EqualTo(new[] { 6, 7, 8 }), "blade");
            Assert.That(result.GetTriangles(1), Is.EqualTo(new[] { 0, 1, 2 }), "grip");
            Assert.That(result.GetTriangles(2), Is.EqualTo(new[] { 3, 4, 5 }), "guard");
            Assert.That(Enumerable.Range(0, 3).Sum(s => result.GetIndices(s).Length), Is.EqualTo(source.GetIndices(0).Length));
            AssertUvs(source, result);
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(sourceBefore));
            Assert.That(EditorJsonUtility.ToJson(reference), Is.EqualTo(referenceBefore));
        }

        [Test]
        public void Sword_NormalsAndTangentHandednessTransformCorrectly_WithoutUvRepacking()
        {
            Mesh source = Sword(), reference = Box(Vector3.zero, new Vector3(.05f, .04f, .1f));
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(.05f, -.2f, .1f), Quaternion.Euler(90f, 12f, 4f), new Vector3(.7f, 1.3f, 2f));
            Mesh result = Keep(WardenSilhouetteSetup.BuildSword(source, reference, matrix));
            // Isotropic fitting does not affect normal direction. Source yaw acts
            // first; mapping into the old filter uses inverse-transpose of its inverse.
            Matrix4x4 sourceRotation = Matrix4x4.Rotate(Quaternion.Euler(0f, WardenSilhouetteSetup.SwordSourceYaw, 0f));
            Vector3 expected = matrix.transpose.MultiplyVector(sourceRotation.MultiplyVector(source.normals[0])).normalized;
            foreach (Vector3 normal in result.normals)
            {
                Assert.That(Vector3.Distance(normal, expected), Is.LessThan(1e-6f));
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(1e-6f));
            }
            for (int i = 0; i < result.tangents.Length; i++)
            {
                Vector4 tangent = result.tangents[i]; Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z);
                Assert.That(Mathf.Abs(Vector3.Dot(direction, result.normals[i])), Is.LessThan(1e-6f));
                Assert.That(direction.magnitude, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(tangent.w, Is.EqualTo(source.tangents[i].w));
            }
            AssertUvs(source, result);
        }

        [Test]
        public void FilterToRoot_IgnoresActorPositionScaleAndYaw_AndRejectsOtherHierarchies()
        {
            GameObject actor = Keep(new GameObject("Actor"));
            Transform weapon = Child(actor.transform, WardenSilhouetteSetup.SwordNode);
            Transform model = Child(weapon, "Model"); model.localPosition = new Vector3(.002f, -.005f, .014f);
            model.localRotation = Quaternion.Euler(90f, 0f, 0f); model.localScale = Vector3.one * 32.349483f;
            MeshFilter filter = model.gameObject.AddComponent<MeshFilter>();
            Matrix4x4 first = WardenSilhouetteSetup.FilterToRoot(filter, weapon);
            actor.transform.position = new Vector3(40000f, 12f, -90000f); actor.transform.localScale = Vector3.one * 1.12f;
            actor.transform.rotation = Quaternion.Euler(4f, 128f, 0f);
            Assert.That(WardenSilhouetteSetup.FilterToRoot(filter, weapon), Is.EqualTo(first));
            Assert.Throws<ArgumentException>(() => WardenSilhouetteSetup.FilterToRoot(filter, Keep(new GameObject("Unrelated")).transform));
        }

        [Test]
        public void ApplyPrepared_OnlyChangesExactMeshes_PreservesAllOtherFieldsAndIsIdempotent()
        {
            GameObject root = Fixture(out MeshFilter helmet, out MeshFilter sword, out Animator animator);
            Mesh sourceHelmet = Box(Vector3.zero, Vector3.one), sourceSword = Sword();
            var meshes = new WardenSilhouetteSetup.PreparedMeshes {
                helmet = Keep(WardenSilhouetteSetup.BuildHelmet(sourceHelmet, helmet.sharedMesh.bounds)),
                sword = Keep(WardenSilhouetteSetup.BuildSword(sourceSword, sword.sharedMesh, WardenSilhouetteSetup.FilterToRoot(sword, sword.transform.parent)))
            };
            Avatar avatar = Keep(AvatarBuilder.BuildGenericAvatar(animator.gameObject, "")); animator.avatar = avatar;
            Material[] materials = sword.GetComponent<MeshRenderer>().sharedMaterials;
            MeshFilter unrelated = Child(root.transform, "Helmet_Closed_Extra").gameObject.AddComponent<MeshFilter>(); unrelated.sharedMesh = helmet.sharedMesh;
            Mesh unrelatedMesh = unrelated.sharedMesh;
            string frozen = Snapshot(root);
            var first = WardenSilhouetteSetup.ApplyPrepared(root, meshes);
            Assert.That(first.changedMeshFilters, Is.EqualTo(2));
            Assert.That(first.helmetFound && first.swordFound, Is.True);
            Assert.That(first.matchedFilters, Has.Length.EqualTo(2));
            Assert.That(helmet.sharedMesh, Is.SameAs(meshes.helmet)); Assert.That(sword.sharedMesh, Is.SameAs(meshes.sword));
            Assert.That(unrelated.sharedMesh, Is.SameAs(unrelatedMesh));
            Assert.That(animator.avatar, Is.SameAs(avatar));
            Assert.That(sword.GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(materials));
            Assert.That(Snapshot(root), Is.EqualTo(frozen));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            Assert.That(WardenSilhouetteSetup.ApplyPrepared(root, meshes).changedMeshFilters, Is.EqualTo(0));
            Assert.That(Snapshot(root), Is.EqualTo(frozen));
        }

        [Test]
        public void ApplyPrepared_ValidatesBothMaterialSlotContractsBeforeAnyChange()
        {
            GameObject root = Fixture(out MeshFilter helmet, out MeshFilter sword, out Animator unused);
            Mesh oldHelmet = helmet.sharedMesh;
            sword.GetComponent<MeshRenderer>().sharedMaterials = new[] { Material() };
            var meshes = new WardenSilhouetteSetup.PreparedMeshes {
                helmet = Keep(WardenSilhouetteSetup.BuildHelmet(Box(Vector3.zero, Vector3.one), oldHelmet.bounds)),
                sword = Keep(WardenSilhouetteSetup.BuildSword(Sword(), sword.sharedMesh, Matrix4x4.identity))
            };
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSetup.ApplyPrepared(root, meshes));
            Assert.That(helmet.sharedMesh, Is.SameAs(oldHelmet));
        }

        [Test]
        public void ApplyPrepared_CanonicalWeaponOnly_DoesNotAddHelmetOrAnyComponents()
        {
            GameObject root = Keep(new GameObject("P_M6_Warden_RuneSword"));
            MeshFilter filter = Filter(Child(root.transform, "Model"), Box(Vector3.zero, new Vector3(.1f, 2f, .1f)), 3);
            var meshes = new WardenSilhouetteSetup.PreparedMeshes {
                helmet = Box(Vector3.zero, Vector3.one), sword = Keep(WardenSilhouetteSetup.BuildSword(Sword(), filter.sharedMesh, Matrix4x4.identity))
            };
            string before = Snapshot(root);
            var result = WardenSilhouetteSetup.ApplyPrepared(root, meshes);
            Assert.That(result.changedMeshFilters, Is.EqualTo(1));
            Assert.That(result.helmetFound, Is.False); Assert.That(result.swordFound, Is.True);
            Assert.That(Snapshot(root), Is.EqualTo(before));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test]
        public void Prepare_RealSourcesHaveReviewedVertexCounts_ExactGuidAndBytesRemainIdentical()
        {
            Mesh helmet = AssetDatabase.LoadAllAssetsAtPath(WardenSilhouetteSetup.ReferenceHelmetPath).OfType<Mesh>().Single();
            Mesh sword = AssetDatabase.LoadAllAssetsAtPath(WardenSilhouetteSetup.ReferenceSwordPath).OfType<Mesh>().Single();
            GameObject canonical = AssetDatabase.LoadAssetAtPath<GameObject>(WardenSilhouetteSetup.CanonicalWeaponPath);
            Assert.That(canonical, Is.Not.Null);
            MeshFilter filter = canonical.GetComponentsInChildren<MeshFilter>(true).Single();
            var originals = WardenSilhouetteSetup.CaptureSourceFingerprints(helmet, sword);
            var production = new[] { WardenSilhouetteSetup.CanonicalWeaponPath, "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab", "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab", "Assets/_Game/Scenes/10_EmberValley.unity" }
                .SelectMany(p => new[] { p, p + ".meta" }).ToDictionary(p => p, Hash);
            Matrix4x4 matrix = WardenSilhouetteSetup.FilterToRoot(filter, canonical.transform);
            var first = WardenSilhouetteSetup.Prepare(helmet, sword, matrix);
            Assert.That(first.diagnostics.helmetSourceVertices, Is.EqualTo(610));
            Assert.That(first.diagnostics.swordSourceVertices, Is.EqualTo(417));
            Assert.That(first.diagnostics.swordSourceYaw, Is.EqualTo(WardenSilhouetteSetup.SwordSourceYaw));
            Assert.That(first.diagnostics.sourcesUnchanged, Is.True);
            Assert.That(first.diagnostics.candidateYMin, Is.EqualTo(first.diagnostics.originalYMin).Within(1e-5f));
            Assert.That(first.diagnostics.candidateYMax, Is.EqualTo(first.diagnostics.originalYMax).Within(1e-5f));
            Assert.That(first.helmet.bounds.center, Is.EqualTo(helmet.bounds.center));
            string[] paths = { AssetDatabase.GetAssetPath(first.helmet), AssetDatabase.GetAssetPath(first.sword) };
            var guids = paths.ToDictionary(p => p, AssetDatabase.AssetPathToGUID);
            var bytes = paths.SelectMany(p => new[] { p, p + ".meta" }).ToDictionary(p => p, File.ReadAllBytes);
            for (int pass = 0; pass < 2; pass++)
            {
                var again = WardenSilhouetteSetup.Prepare(helmet, sword, matrix);
                Assert.That(again.helmet, Is.SameAs(first.helmet)); Assert.That(again.sword, Is.SameAs(first.sword));
                foreach (var row in guids) Assert.That(AssetDatabase.AssetPathToGUID(row.Key), Is.EqualTo(row.Value));
                foreach (var row in bytes) Assert.That(File.ReadAllBytes(row.Key), Is.EqualTo(row.Value), row.Key);
            }
            foreach (var row in originals.Concat(production)) Assert.That(Hash(row.Key), Is.EqualTo(row.Value), row.Key);
            Assert.Throws<InvalidOperationException>(() => WardenSilhouetteSetup.ApplyToTemporary(canonical));
        }

        [Test]
        public void ExactGeometryComparer_DetectsUvAndIndexChanges_NotOnlyBounds()
        {
            Mesh mesh = Sword(), copy = Keep(Object.Instantiate(mesh));
            copy.name = mesh.name;
            Assert.That(WardenSilhouetteSetup.EqualGeometry(mesh, copy), Is.True);
            Vector2[] uv = copy.uv; uv[0].x += .25f; copy.uv = uv;
            Assert.That(WardenSilhouetteSetup.EqualGeometry(mesh, copy), Is.False);
            Mesh other = Keep(Object.Instantiate(mesh)); other.name = mesh.name; int[] triangles = other.triangles;
            int swap = triangles[0]; triangles[0] = triangles[1]; triangles[1] = swap; other.triangles = triangles;
            Assert.That(WardenSilhouetteSetup.EqualGeometry(mesh, other), Is.False);
        }

        [Test]
        public void InvalidGeometryAndSingularTransforms_AreRejectedBeforeCandidateCreation()
        {
            Mesh source = Sword(), reference = Box(Vector3.zero, Vector3.one);
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSetup.BuildHelmet(reference, new Bounds(Vector3.zero, new Vector3(0f, 1f, 1f))));
            Assert.Throws<ArgumentException>(() => WardenSilhouetteSetup.BuildSword(source, reference, Matrix4x4.Scale(new Vector3(1f, 0f, 1f))));
            Assert.Throws<ArgumentOutOfRangeException>(() => WardenSilhouetteSetup.BuildHelmet(reference, reference.bounds, float.NaN));
        }

        GameObject Fixture(out MeshFilter helmet, out MeshFilter sword, out Animator animator)
        {
            GameObject root = Keep(new GameObject("Enemy_EmberWarden_Test"));
            var collider = root.AddComponent<CapsuleCollider>(); collider.radius = .52f; collider.height = 2.25f;
            Transform model = Child(root.transform, "Model"); animator = model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            helmet = Filter(Child(model, WardenSilhouetteSetup.HelmetNode), Box(new Vector3(0f, .01f, .02f), new Vector3(.04f, .06f, .05f)), 1);
            helmet.GetComponent<MeshRenderer>().enabled = false;
            Transform weapon = Child(model, WardenSilhouetteSetup.SwordNode), blade = Child(weapon, "Model");
            blade.localPosition = new Vector3(.001f, -.005f, .014f); blade.localRotation = Quaternion.Euler(90f, 0f, 0f); blade.localScale = Vector3.one * 32.349483f;
            sword = Filter(blade, Box(Vector3.zero, new Vector3(.004f, .01f, .04f)), 3);
            return root;
        }
        MeshFilter Filter(Transform transform, Mesh mesh, int slots)
        {
            var filter = transform.gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = transform.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = Enumerable.Range(0, slots).Select(i => Material()).ToArray();
            return filter;
        }
        Material Material() => Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        static Transform Child(Transform parent, string name) { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
        Mesh Box(Vector3 centre, Vector3 size)
        {
            Vector3[] vertices = Enumerable.Range(0, 8).Select(i => centre + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))).ToArray();
            return Mesh(vertices, new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 });
        }
        Mesh Sword() => Mesh(new[] {
            new Vector3(-.1f, 0f, -.05f), new Vector3(.1f, .3f, .05f), new Vector3(0f, .45f, -.05f),
            new Vector3(-.4f, .75f, -.06f), new Vector3(.4f, .75f, .06f), new Vector3(0f, .9f, -.06f),
            new Vector3(-.15f, 1.1f, -.06f), new Vector3(.15f, 1.1f, .06f), new Vector3(0f, 3f, -.06f)
        }, Enumerable.Range(0, 9).ToArray());
        Mesh Mesh(Vector3[] vertices, int[] triangles)
        {
            var mesh = Keep(new Mesh { name = "GeometryFixture", vertices = vertices, triangles = triangles });
            mesh.normals = vertices.Select(v => Vector3.forward).ToArray(); mesh.tangents = vertices.Select(v => new Vector4(1f, 0f, 0f, 1f)).ToArray();
            mesh.colors32 = vertices.Select((v, i) => new Color32((byte)i, 100, 150, 255)).ToArray();
            for (int channel = 0; channel < 8; channel++) mesh.SetUVs(channel, vertices.Select((v, i) => new Vector4(i * .07f, channel * .1f, i * .02f, .3f)).ToList());
            mesh.RecalculateBounds(); return mesh;
        }
        T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
        static void AssertUvs(Mesh source, Mesh result)
        {
            for (int channel = 0; channel < 8; channel++)
            {
                var before = new List<Vector4>(); var after = new List<Vector4>(); source.GetUVs(channel, before); result.GetUVs(channel, after);
                Assert.That(after, Is.EqualTo(before), "UV" + channel);
            }
        }
        static string Snapshot(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Transform>(true).SelectMany(t => t.GetComponents<Component>()).Select(c => {
            string json = EditorJsonUtility.ToJson(c);
            return c.GetInstanceID() + ":" + (c is MeshFilter ? Regex.Replace(json, "\"m_Mesh\":\\s*\\{[^}]*\\}", "\"m_Mesh\":{}") : json);
        }));
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
