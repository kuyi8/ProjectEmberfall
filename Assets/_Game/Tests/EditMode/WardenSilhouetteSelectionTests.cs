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
    public sealed class WardenSilhouetteSelectionTests
    {
        const string Leather = "Assets/_Game/Art/Materials/M6Art/M_M6_WeaponLeather.mat";
        const string Brass = "Assets/_Game/Art/Materials/M6Art/M_M6_WeaponBrass.mat";
        const string Character = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab";
        readonly List<Object> owned = new List<Object>();
        [TearDown] public void Cleanup()
        {
            foreach (Object value in owned.AsEnumerable().Reverse()) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void Character_ChangesOnlyExactMeshesAndBlade_PreservesAllOtherFieldsAndRepeatIsZero()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            Animator animator = root.GetComponent<Animator>(); Avatar avatar = animator.avatar;
            Renderer blade = sword.GetComponent<MeshRenderer>(); Material[] slots = blade.sharedMaterials;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.6f, .1f, .2f));
            block.SetColor("_EmissionColor", new Color(.25f, .02f, .01f)); blade.SetPropertyBlock(block, 0);
            string frozen = Snapshot(root, new[] { helmet, sword }, blade);
            var materials = root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
            int[] components = ComponentIds(root); var files = FreezeInputs();
            var first = WardenSilhouetteSelection.Apply(root);
            Assert.That(first.meshChanges, Is.EqualTo(2)); Assert.That(first.materialChanges, Is.EqualTo(1));
            Assert.That(first.matchedPaths, Has.Length.EqualTo(2));
            Assert.That(first.source.selectedBlade, Is.EqualTo(WardenSilhouetteSelection.SelectedBladePath));
            Assert.That(first.source.swordSourceYaw, Is.EqualTo(90f));
            Assert.That(AssetDatabase.GetAssetPath(helmet.sharedMesh), Is.EqualTo(WardenSilhouetteSetup.HelmetMeshPath));
            Assert.That(AssetDatabase.GetAssetPath(sword.sharedMesh), Is.EqualTo(WardenSilhouetteSetup.SwordMeshPath));
            Assert.That(AssetDatabase.GetAssetPath(blade.sharedMaterials[0]), Is.EqualTo(WardenSilhouetteSelection.SelectedBladePath));
            Assert.That(blade.sharedMaterials[1], Is.SameAs(slots[1])); Assert.That(blade.sharedMaterials[2], Is.SameAs(slots[2]));
            foreach (var row in materials)
                for (int i = row.Key == blade ? 1 : 0; i < row.Value.Length; i++) Assert.That(row.Key.sharedMaterials[i], Is.SameAs(row.Value[i]));
            Assert.That(animator.avatar, Is.SameAs(avatar)); Assert.That(avatar, Is.Not.Null);
            Assert.That(ComponentIds(root), Is.EqualTo(components)); Assert.That(Snapshot(root, new[] { helmet, sword }, blade), Is.EqualTo(frozen));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1));
            var read = new MaterialPropertyBlock(); blade.GetPropertyBlock(read, 0);
            Assert.That(read.GetColor("_BaseColor"), Is.EqualTo(block.GetColor("_BaseColor")));
            Assert.That(read.GetColor("_EmissionColor"), Is.EqualTo(block.GetColor("_EmissionColor")));
            string selected = Snapshot(root, new MeshFilter[0], null);
            var second = WardenSilhouetteSelection.Apply(root);
            Assert.That(second.meshChanges, Is.EqualTo(0)); Assert.That(second.materialChanges, Is.EqualTo(0));
            Assert.That(second.matchedPaths, Is.EqualTo(first.matchedPaths)); Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(selected));
            AssertUnchanged(files);
        }

        [Test]
        public void CanonicalWeaponOnly_ChangesOneMeshAndFirstSlot_WithoutAddingAnyComponents()
        {
            GameObject root = Fixture(true, out MeshFilter helmet, out MeshFilter sword);
            var renderer = sword.GetComponent<MeshRenderer>(); Material[] slots = renderer.sharedMaterials;
            int[] components = ComponentIds(root); string before = Snapshot(root, new[] { sword }, renderer);
            var result = WardenSilhouetteSelection.Apply(root);
            Assert.That(result.meshChanges, Is.EqualTo(1)); Assert.That(result.materialChanges, Is.EqualTo(1));
            Assert.That(result.matchedPaths, Has.Length.EqualTo(1));
            Assert.That(renderer.sharedMaterials[1], Is.SameAs(slots[1])); Assert.That(renderer.sharedMaterials[2], Is.SameAs(slots[2]));
            Assert.That(ComponentIds(root), Is.EqualTo(components)); Assert.That(Snapshot(root, new[] { sword }, renderer), Is.EqualTo(before));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(WardenSilhouetteSelection.Apply(root).meshChanges, Is.EqualTo(0));
            Assert.That(WardenSilhouetteSelection.Apply(root).materialChanges, Is.EqualTo(0));
        }

        [Test]
        public void UnknownBlade_IsRejectedBeforeEitherMeshReferenceChanges()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            var renderer = sword.GetComponent<MeshRenderer>(); Material[] slots = renderer.sharedMaterials;
            slots[0] = Keep(new Material(slots[0])); renderer.sharedMaterials = slots;
            string before = Snapshot(root, new MeshFilter[0], null);
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSelection.Apply(root));
            Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(before));
        }

        [TestCase(2)] [TestCase(4)]
        public void WrongSwordSlotCount_IsRejectedBeforeEitherMeshChanges(int count)
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            sword.GetComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(Material(WardenSilhouetteSelection.OriginalBladePath), count).ToArray();
            string before = Snapshot(root, new MeshFilter[0], null);
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSelection.Apply(root));
            Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(before));
        }

        [Test]
        public void UnknownExactHelmetMesh_IsRejectedBeforeSwordOrMaterialChanges()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            helmet.sharedMesh = Keep(Object.Instantiate(helmet.sharedMesh));
            string before = Snapshot(root, new MeshFilter[0], null);
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSelection.Apply(root));
            Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(before));
        }

        [Test]
        public void SimilarNamesAndUnrelatedProps_AreNotMapped()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            helmet.transform.parent.name += "_Extra"; sword.transform.parent.name += "_Extra";
            string before = Snapshot(root, new MeshFilter[0], null);
            var result = WardenSilhouetteSelection.Apply(root);
            Assert.That(result.meshChanges, Is.EqualTo(0)); Assert.That(result.materialChanges, Is.EqualTo(0));
            Assert.That(result.matchedPaths, Is.Empty); Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(before));
        }

        [Test]
        public void DuplicateExactHelmetNode_IsRejectedBeforeAnyReferenceChanges()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            Child(root.transform, WardenSilhouetteSetup.HelmetNode);
            string before = Snapshot(root, new MeshFilter[0], null);
            Assert.Throws<InvalidDataException>(() => WardenSilhouetteSelection.Apply(root));
            Assert.That(Snapshot(root, new MeshFilter[0], null), Is.EqualTo(before));
        }

        [Test]
        public void HelmetOnly_DoesNotAddWeaponOrChangeAnyMaterialSlot()
        {
            GameObject root = Fixture(false, out MeshFilter helmet, out MeshFilter sword);
            Object.DestroyImmediate(sword.transform.parent.gameObject);
            int[] components = ComponentIds(root); string before = Snapshot(root, new[] { helmet }, null);
            var result = WardenSilhouetteSelection.Apply(root);
            Assert.That(result.meshChanges, Is.EqualTo(1)); Assert.That(result.materialChanges, Is.EqualTo(0));
            Assert.That(result.matchedPaths, Has.Length.EqualTo(1));
            Assert.That(ComponentIds(root), Is.EqualTo(components)); Assert.That(Snapshot(root, new[] { helmet }, null), Is.EqualTo(before));
        }

        [Test]
        public void PersistentCanonicalSource_CannotBeMutated()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(WardenSilhouetteSetup.CanonicalWeaponPath);
            string before = Hash(WardenSilhouetteSetup.CanonicalWeaponPath);
            Assert.Throws<InvalidOperationException>(() => WardenSilhouetteSelection.Apply(source));
            Assert.That(Hash(WardenSilhouetteSetup.CanonicalWeaponPath), Is.EqualTo(before));
        }

        [Test]
        public void RealCharacterPrefabInstance_RetainsAvatarHierarchyAndOriginalAssetBytes()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Character);
            GameObject root = Keep((GameObject)PrefabUtility.InstantiatePrefab(source));
            Animator animator = root.GetComponentInChildren<Animator>(true); Avatar avatar = animator.avatar;
            MeshFilter helmet = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == WardenSilhouetteSetup.HelmetNode).GetComponentsInChildren<MeshFilter>(true).Single();
            string before = Snapshot(root, new[] { helmet }, null); int[] components = ComponentIds(root);
            var files = new[] { Character, Character + ".meta" }.ToDictionary(p => p, Hash);
            var result = WardenSilhouetteSelection.Apply(root);
            Assert.That(result.meshChanges, Is.InRange(0, 1)); Assert.That(result.materialChanges, Is.EqualTo(0));
            Assert.That(result.matchedPaths, Has.Length.EqualTo(1));
            Assert.That(animator.avatar, Is.SameAs(avatar)); Assert.That(animator.isHuman, Is.True);
            Assert.That(ComponentIds(root), Is.EqualTo(components)); Assert.That(Snapshot(root, new[] { helmet }, null), Is.EqualTo(before));
            Assert.That(WardenSilhouetteSelection.Apply(root).meshChanges, Is.EqualTo(0)); AssertUnchanged(files);
        }

        GameObject Fixture(bool canonicalOnly, out MeshFilter helmet, out MeshFilter sword)
        {
            GameObject root = Keep(new GameObject(canonicalOnly ? WardenSilhouetteSelection.CanonicalSwordName : WardenSilhouetteSelection.WardenCharacterName));
            root.transform.position = new Vector3(12f, .1f, -4f); root.transform.rotation = Quaternion.Euler(0f, 45f, 0f); root.transform.localScale = Vector3.one * 1.12f;
            helmet = null;
            if (!canonicalOnly)
            {
                var collider = root.AddComponent<CapsuleCollider>(); collider.radius = .5f; collider.height = 2.25f;
                var animator = root.AddComponent<Animator>();
                Animator original = AssetDatabase.LoadAssetAtPath<GameObject>(Character).GetComponentInChildren<Animator>(true);
                animator.avatar = original.avatar; animator.runtimeAnimatorController = original.runtimeAnimatorController;
                animator.applyRootMotion = false;
                helmet = Filter(Child(Child(root.transform, WardenSilhouetteSetup.HelmetNode), "Helmet2"), Mesh(WardenSilhouetteSetup.ReferenceHelmetPath), new[] { Material(WardenSilhouetteSelection.SelectedBladePath) });
                helmet.GetComponent<MeshRenderer>().enabled = false;
            }
            Transform weapon = canonicalOnly ? root.transform : Child(root.transform, WardenSilhouetteSetup.SwordNode);
            Transform model = Child(weapon, "Model"); model.localPosition = new Vector3(.001f, -.005f, .014f); model.localRotation = Quaternion.Euler(90f, 0f, 0f); model.localScale = Vector3.one * 32.349483f;
            sword = Filter(model, Mesh(WardenSilhouetteSetup.ReferenceSwordPath), new[] { Material(WardenSilhouetteSelection.OriginalBladePath), Material(Leather), Material(Brass) });
            return root;
        }
        static MeshFilter Filter(Transform transform, Mesh mesh, Material[] materials)
        {
            var filter = transform.gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            transform.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials; return filter;
        }
        static Transform Child(Transform parent, string name) { var value = new GameObject(name).transform; value.SetParent(parent, false); return value; }
        static Mesh Mesh(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single();
        static Material Material(string path) { var value = AssetDatabase.LoadAssetAtPath<Material>(path); Assert.That(value, Is.Not.Null, path); return value; }
        T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
        static int[] ComponentIds(GameObject root) => root.GetComponentsInChildren<Transform>(true).SelectMany(t => t.GetComponents<Component>()).Select(c => c.GetInstanceID()).ToArray();
        static string Snapshot(GameObject root, MeshFilter[] allowedMeshes, Renderer allowedBlade) => HierarchySnapshot(root) + "\n" + string.Join("\n", root.GetComponentsInChildren<Transform>(true).SelectMany(t => t.GetComponents<Component>()).Select(c => {
            string json = EditorJsonUtility.ToJson(c);
            if (c is MeshFilter filter && allowedMeshes.Contains(filter)) json = Regex.Replace(json, "\"m_Mesh\":\\s*\\{[^}]*\\}", "\"m_Mesh\":{}");
            if (c == allowedBlade) json = Regex.Replace(json, "\"m_Materials\":\\s*\\[[^\\]]*\\]", "\"m_Materials\":[]");
            return c.GetInstanceID() + ":" + c.hideFlags + ":" + json;
        }));
        static string HierarchySnapshot(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Transform>(true).Select(t => {
            GameObject go = t.gameObject;
            string children = string.Join(",", Enumerable.Range(0, t.childCount).Select(i => t.GetChild(i).GetInstanceID()));
            return t.GetInstanceID() + ":parent=" + (t.parent == null ? 0 : t.parent.GetInstanceID()) + ":children=" + children +
                ":go=" + go.GetInstanceID() + ":name=" + go.name + ":active=" + go.activeSelf + "/" + go.activeInHierarchy +
                ":layer=" + go.layer + ":tag=" + go.tag + ":hide=" + go.hideFlags + ":static=" + GameObjectUtility.GetStaticEditorFlags(go) +
                ":serialized=" + EditorJsonUtility.ToJson(go);
        }));
        static Dictionary<string, string> FreezeInputs()
        {
            string[] roots = { WardenSilhouetteSetup.HelmetSourcePath, WardenSilhouetteSetup.SwordSourcePath, WardenSilhouetteSetup.LicensePath,
                WardenSilhouetteSetup.ReferenceHelmetPath, WardenSilhouetteSetup.ReferenceSwordPath, WardenSilhouetteSetup.HelmetMeshPath, WardenSilhouetteSetup.SwordMeshPath,
                WardenSilhouetteSelection.OriginalBladePath, WardenSilhouetteSelection.SelectedBladePath, Leather, Brass };
            return roots.SelectMany(p => new[] { p, p + ".meta" }).ToDictionary(p => p, Hash);
        }
        static void AssertUnchanged(Dictionary<string, string> files) { foreach (var row in files) Assert.That(Hash(row.Key), Is.EqualTo(row.Value), row.Key); }
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
