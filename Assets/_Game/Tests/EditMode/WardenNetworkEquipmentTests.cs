using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenNetworkEquipmentTests
    {
        readonly string[] protectedPaths = {
            WardenNetworkEquipmentSetup.NetworkPrefabPath, WardenNetworkEquipmentSetup.SwordPrefabPath,
            WardenNetworkEquipmentSetup.ShieldPrefabPath,
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab",
            "Assets/_Game/Scenes/10_EmberValley.unity", "Assets/_Game/Scenes/20_Sanctum.unity"
        };

        [TestCase(1f)] [TestCase(1.12f)]
        public void TemporaryPrefab_EquipmentKeepsWorldMetresAndAllOldComponentFields(float visualScale)
        {
            var disk = FreezeDisk();
            WithCopy(root =>
            {
                Transform visual = root.transform.Find("Warden_Visual");
                visual.localScale = Vector3.one * visualScale;
                root.transform.SetPositionAndRotation(new Vector3(11f, 1f, -6f), Quaternion.Euler(0f, 37f, 0f));
                Animator animator = root.GetComponentInChildren<Animator>(true);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var components = root.GetComponentsInChildren<Component>(true).ToDictionary(c => c, Contract);
                var oldChildren = new[] { right, left }.ToDictionary(t => t, t => Children(t));
                var localScales = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localScale);
                var parents = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.parent);
                int colliders = root.GetComponentsInChildren<Collider>(true).Length;
                int behaviours = root.GetComponentsInChildren<MonoBehaviour>(true).Length;
                var result = WardenNetworkEquipmentSetup.Apply(root);
                Assert.That(result.created, Is.True);
                Assert.That(result.rightHandPath, Is.Not.Empty); Assert.That(result.leftHandPath, Is.Not.Empty);
                Assert.That(result.sword.transform.parent.parent, Is.SameAs(right));
                Assert.That(result.shield.transform.parent.parent, Is.SameAs(left));
                Assert.That(Vector3.Distance(Abs(result.rightSocketWorldScale), Vector3.one), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(Abs(result.leftSocketWorldScale), Vector3.one), Is.LessThan(.002f));
                Assert.That(root.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(colliders));
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Has.Length.EqualTo(behaviours));
                Assert.That(root.GetComponentsInChildren<Animator>(true), Has.Length.EqualTo(1));
                foreach (var row in components)
                    Assert.That(Contract(row.Key), Is.EqualTo(row.Value), row.Key.GetType().FullName + "/" + row.Key.name);
                foreach (var row in oldChildren)
                    Assert.That(Children(row.Key).Where(t => t.name != WardenNetworkEquipmentSetup.SwordSocketName &&
                        t.name != WardenNetworkEquipmentSetup.ShieldSocketName), Is.EqualTo(row.Value), "Original hand children retain order.");
                foreach (var row in localScales) Assert.That(row.Key.localScale, Is.EqualTo(row.Value), row.Key.name);
                foreach (var row in parents) Assert.That(row.Key.parent, Is.SameAs(row.Value), row.Key.name + " original parent");
                AssertGearOnly(result.sword); AssertGearOnly(result.shield);
                GameObject swordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenNetworkEquipmentSetup.SwordPrefabPath);
                Assert.That(WorldSpan(result.sword, result.sword.transform.parent.up),
                    Is.EqualTo(WorldSpan(swordPrefab, Vector3.up)).Within(.0001f),
                    "Compare metre-based blade span, not Actor-to-tip distance or differently rotated AABBs.");
                var serialized = new SerializedObject(Warden(root));
                Assert.That(serialized.FindProperty("_weaponRenderer").objectReferenceValue, Is.SameAs(result.weaponRenderer));
                Assert.That(serialized.FindProperty("_shieldRenderer").objectReferenceValue, Is.SameAs(result.shieldRenderer));
                Assert.That(serialized.FindProperty("_shieldVisualRoot").objectReferenceValue, Is.SameAs(result.shield));
            });
            AssertDisk(disk);
        }

        [Test]
        public void ApplyTwice_ReusesExactlyTheSameNodesAndSerializedFields()
        {
            WithCopy(root =>
            {
                var first = WardenNetworkEquipmentSetup.Apply(root);
                string[] before = Snapshot(root);
                var second = WardenNetworkEquipmentSetup.Apply(root);
                Assert.That(second.created, Is.False);
                Assert.That(second.sword, Is.SameAs(first.sword)); Assert.That(second.shield, Is.SameAs(first.shield));
                Assert.That(second.weaponRenderer, Is.SameAs(first.weaponRenderer));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [TestCase("WeaponSocket_RightHand")] [TestCase("ShieldSocket_LeftHand")]
        [TestCase("Sword_M6_Warden_Equipped")] [TestCase("Shield_Warden_Equipped")]
        public void PartialOrUnknownEquipment_ThrowsWithoutReplacingAnyExistingObject(string name)
        {
            WithCopy(root =>
            {
                new GameObject(name).transform.SetParent(root.transform, false);
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [Test]
        public void DuplicateEquipment_ThrowsWithoutSilentlyCleaningIt()
        {
            WithCopy(root =>
            {
                WardenNetworkEquipmentSetup.Apply(root);
                new GameObject(WardenNetworkEquipmentSetup.SwordName).transform.SetParent(root.transform, false);
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [Test]
        public void UnexpectedEquipmentCollider_ThrowsAndKeepsItForDiagnosis()
        {
            WithCopy(root =>
            {
                var result = WardenNetworkEquipmentSetup.Apply(root);
                Collider collider = result.sword.AddComponent<BoxCollider>();
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(collider, Is.Not.Null);
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [Test]
        public void UnexpectedSocketScale_ThrowsWithoutChangingIt()
        {
            WithCopy(root =>
            {
                var result = WardenNetworkEquipmentSetup.Apply(root);
                result.sword.transform.parent.localScale *= 1.12f;
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [Test]
        public void PartialSerializedReferences_ThrowWithoutRebinding()
        {
            WithCopy(root =>
            {
                var result = WardenNetworkEquipmentSetup.Apply(root);
                var serialized = new SerializedObject(Warden(root));
                serialized.FindProperty("_shieldRenderer").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        [Test]
        public void PersistentPrefab_ThrowsAndLeavesAllDiskInputsUnchanged()
        {
            var disk = FreezeDisk();
            Assert.Throws<InvalidOperationException>(() => WardenNetworkEquipmentSetup.Apply(
                AssetDatabase.LoadAssetAtPath<GameObject>(WardenNetworkEquipmentSetup.NetworkPrefabPath)));
            AssertDisk(disk);
        }

        [Test]
        public void InvalidAvatar_ThrowsBeforeCreatingAnyEquipment()
        {
            WithCopy(root =>
            {
                root.GetComponentInChildren<Animator>(true).avatar = null;
                string[] before = Snapshot(root);
                Assert.Throws<InvalidDataException>(() => WardenNetworkEquipmentSetup.Apply(root));
                Assert.That(Snapshot(root), Is.EqualTo(before));
            });
        }

        static void WithCopy(Action<GameObject> action)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenNetworkEquipmentSetup.NetworkPrefabPath);
                Assert.That(prefab, Is.Not.Null);
                GameObject root = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
                Assert.That(root, Is.Not.Null);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Explicitly remove only this helper's equipment on the controlled copy,
                // so add-path tests remain meaningful after production migration too.
                var sockets = root.GetComponentsInChildren<Transform>(true).Where(t =>
                    t.name == WardenNetworkEquipmentSetup.SwordSocketName || t.name == WardenNetworkEquipmentSetup.ShieldSocketName).ToArray();
                foreach (Transform socket in sockets) Object.DestroyImmediate(socket.gameObject);
                var bindings = new SerializedObject(Warden(root));
                foreach (string field in new[] { "_weaponRenderer", "_shieldRenderer", "_shieldVisualRoot" })
                    bindings.FindProperty(field).objectReferenceValue = null;
                bindings.ApplyModifiedPropertiesWithoutUndo();
                action(root);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        Dictionary<string, byte[]> FreezeDisk() => protectedPaths.SelectMany(p => new[] { p, p + ".meta" })
            .ToDictionary(p => p, File.ReadAllBytes);
        static void AssertDisk(Dictionary<string, byte[]> bytes)
        {
            foreach (var row in bytes) Assert.That(File.ReadAllBytes(row.Key), Is.EqualTo(row.Value), row.Key);
        }
        static Component Warden(GameObject root) => root.GetComponents<Component>().Single(c =>
            c != null && c.GetType().FullName == "Emberfall.Networking.NetworkWarden");
        static string Contract(Component component)
        {
            string json = EditorJsonUtility.ToJson(component);
            if (component.GetType().FullName == "Emberfall.Networking.NetworkWarden")
            {
                foreach (string field in new[] { "_weaponRenderer", "_shieldRenderer", "_shieldVisualRoot" })
                {
                    Assert.That(json, Does.Contain("\"" + field + "\""), "The exact three-field whitelist must exist.");
                    json = Regex.Replace(json, "\"" + field + "\"\\s*:\\s*\\{[^}]*\\}", "\"" + field + "\":{}");
                }
            }
            return json;
        }
        static Transform[] Children(Transform parent) => Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToArray();
        static string[] Snapshot(GameObject root) => root.GetComponentsInChildren<Component>(true)
            .Select(c => c.GetInstanceID() + ":" + EditorJsonUtility.ToJson(c)).ToArray();
        static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        static void AssertGearOnly(GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                Assert.That(component is Transform || component is MeshFilter || component is MeshRenderer, Is.True);
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetStaticEditorFlags(item.gameObject), Is.EqualTo((StaticEditorFlags)0));
        }
        static float WorldSpan(GameObject root, Vector3 axis)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float coordinate = Vector3.Dot(filter.transform.TransformPoint(vertex) - root.transform.position, axis.normalized);
                min = Mathf.Min(min, coordinate); max = Mathf.Max(max, coordinate);
            }
            Assert.That(float.IsInfinity(min), Is.False);
            return max - min;
        }
    }
}
