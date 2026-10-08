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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    /// <summary>
    /// Static authored-material and hierarchy contracts only. These tests neither open/save
    /// production scenes nor rebuild generators, and do not certify runtime Actor/MPB tint,
    /// natural combat, rendered style, network authority, or frame-time performance.
    /// </summary>
    public sealed class CharacterPresentationPaletteTests
    {
        private const string M6 = "Assets/_Game/Art/Materials/M6Art/";
        private const string FogSource = M6 + "M_M6_FogwalkerBone.mat";
        private const string UntouchedBlade = M6 + "M_M6_WardenBlade.mat";
        private static readonly Dictionary<string, string> SourceByRole = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Bone", FogSource }, { "Steel", FogSource }, { "Cloth", FogSource }, { "Eyes", FogSource },
            { "WardenMetal", M6 + "M_M6_WardenMetal.mat" },
            { "WardenCloth", M6 + "M_M6_WardenCloth.mat" },
            { "WardenRune", M6 + "M_M6_WardenRune.mat" },
            { "RangerOutfit", "Assets/_Game/Art/Materials/Character/M3Art/M_Ranger_Outfit.mat" },
            { "Priest", M6 + "M_M6_RunePriest.mat" },
            { "Scorched", M6 + "M_M5c_ScorchedKnight.mat" }
        };

        private static readonly string[] ProductionPrefabs =
        {
            "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab",
            "Assets/_Game/Prefabs/Characters/M5c/P_M5c_Enemy_RuinGuardScorched_Knight.prefab",
            "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab",
            "Assets/_Game/Resources/Networking/P_M5_NetworkGymEnemy.prefab",
            "Assets/_Game/Resources/Networking/P_M5_NetworkRunePriest.prefab",
            "Assets/_Game/Resources/Networking/P_M5_NetworkRuinGuard.prefab",
            "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab"
        };

        private static IEnumerable<TestCaseData> PrefabCases => ProductionPrefabs.Select(path =>
            new TestCaseData(path).SetName("{m}(" + Path.GetFileNameWithoutExtension(path) + ")"));

        [Test]
        public void MigrationFreeze_ExplicitlyIncludesAuthoredInputsAndAllPreparedOutputs()
        {
            CharacterPresentationPaletteSetup.EnsureMaterials();
            var frozen = Emberfall.Editor.Review.CharacterPaletteReview.ProtectedDependencies();
            var paths = SourceByRole.Values.Append(CharacterPresentationPaletteSetup.PalettePath)
                .Concat(SourceByRole.Keys.Select(CharacterPresentationPaletteSetup.TargetPath)).Distinct();
            foreach (string path in paths)
            {
                Assert.That(frozen.ContainsKey(path), Is.True, path);
                Assert.That(frozen.ContainsKey(path + ".meta"), Is.True, path + ".meta");
            }
        }

        [Test]
        public void AuthoredPalette_ContainsExactlyTenBoundedRolesAndOnlyLowIntensityEyeEmission()
        {
            var palette = CharacterPresentationPaletteSetup.ReadPalette();
            Assert.That(palette.format, Is.EqualTo(1));
            Assert.That(palette.entries.Select(entry => entry.id), Is.EquivalentTo(SourceByRole.Keys));
            Assert.That(palette.entries.Select(entry => entry.id).Distinct().Count(), Is.EqualTo(10));
            foreach (var entry in palette.entries)
            {
                AssertColorRange(entry.baseColor, 1f);
                Assert.That(entry.baseColor.a, Is.EqualTo(1f));
                Assert.That(entry.smoothness, Is.InRange(0f, 1f));
                Assert.That(entry.metallic, Is.InRange(0f, 1f));
                AssertColorRange(entry.emissionColor, .65f);
                Assert.That(entry.emissionColor.a, Is.EqualTo(1f));
                if (entry.id != "Eyes") Assert.That(Rgb(entry.emissionColor), Is.EqualTo(Vector3.zero), entry.id);
            }
        }

        [Test]
        public void EnsureMaterials_PreservesSourceTexturesUvKeywordsAndAuthoredUrpProperties()
        {
            var frozen = FreezeSourceFiles();
            var materials = CharacterPresentationPaletteSetup.EnsureMaterials();
            Assert.That(materials, Has.Length.EqualTo(10));
            foreach (var entry in CharacterPresentationPaletteSetup.ReadPalette().entries)
            {
                Material source = RequiredMaterial(SourceByRole[entry.id]);
                Material target = RequiredMaterial(CharacterPresentationPaletteSetup.TargetPath(entry.id));
                Assert.That(target, Is.Not.SameAs(source), entry.id);
                Assert.That(target.shader, Is.SameAs(source.shader));
                Assert.That(target.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                AssertStoredColor(target.GetColor("_BaseColor"), entry.baseColor);
                if (target.HasProperty("_Color")) AssertStoredColor(target.GetColor("_Color"), entry.baseColor);
                Assert.That(target.GetFloat("_Smoothness"), Is.EqualTo(entry.smoothness));
                Assert.That(target.GetFloat("_Metallic"), Is.EqualTo(entry.metallic));
                AssertStoredColor(target.GetColor("_EmissionColor"), entry.emissionColor);
                Assert.That(target.IsKeywordEnabled("_EMISSION"), Is.EqualTo(entry.id == "Eyes" && Rgb(entry.emissionColor).sqrMagnitude > 0f));
                Assert.That(target.globalIlluminationFlags, Is.EqualTo(entry.id == "Eyes" ?
                    MaterialGlobalIlluminationFlags.None : MaterialGlobalIlluminationFlags.EmissiveIsBlack));
                Assert.That(target.shaderKeywords.Where(keyword => keyword != "_EMISSION"),
                    Is.EquivalentTo(source.shaderKeywords.Where(keyword => keyword != "_EMISSION")), entry.id);
                Assert.That(target.GetTexturePropertyNames(), Is.EquivalentTo(source.GetTexturePropertyNames()), entry.id);
                foreach (string property in source.GetTexturePropertyNames())
                {
                    Assert.That(target.GetTexture(property), Is.SameAs(source.GetTexture(property)), entry.id + "/" + property);
                    Assert.That(target.GetTextureScale(property), Is.EqualTo(source.GetTextureScale(property)));
                    Assert.That(target.GetTextureOffset(property), Is.EqualTo(source.GetTextureOffset(property)));
                }
            }
            AssertFrozenFiles(frozen);
        }

        [Test]
        public void RepeatedEnsureMaterials_KeepsTargetGuidAndMaterialMetaBytesIdenticalAndSourcesUntouched()
        {
            var sourceBytes = FreezeSourceFiles();
            Material[] first = CharacterPresentationPaletteSetup.EnsureMaterials();
            string[] paths = first.Select(material => AssetDatabase.GetAssetPath(material)).ToArray();
            var guids = paths.ToDictionary(path => path, path => AssetDatabase.AssetPathToGUID(path));
            foreach (string guid in guids.Values) Assert.That(guid, Has.Length.EqualTo(32));
            var targetBytes = FreezeFiles(paths.SelectMany(path => new[] { path, path + ".meta" }));
            for (int pass = 0; pass < 2; pass++)
            {
                var repeated = CharacterPresentationPaletteSetup.EnsureMaterials();
                Assert.That(repeated, Is.EqualTo(first));
                foreach (var row in guids) Assert.That(AssetDatabase.AssetPathToGUID(row.Key), Is.EqualTo(row.Value));
                AssertFrozenFiles(targetBytes);
            }
            AssertFrozenFiles(sourceBytes);
        }

        [TestCase("Skeleton_Warrior_Helmet", "Steel")]
        [TestCase("Skeleton_Warrior_Cloak", "Cloth")]
        [TestCase("Skeleton_Warrior_Eyes", "Eyes")]
        [TestCase("Skeleton_Warrior_Body", "Bone")]
        [TestCase("Helmet", "Bone")]
        [TestCase("Skeleton_Warrior_HelmetExtra", "Bone")]
        public void FogwalkerMapping_UsesExactRendererNameNotSubstringAndIsIdempotent(string rendererName, string role)
        {
            WithFixture((container, character) =>
            {
                Renderer renderer = AddRenderer(character.transform, rendererName,
                    RequiredMaterial(FogSource), RequiredMaterial(UntouchedBlade));
                renderer.enabled = false;
                var frozen = HierarchyContract(character);
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]), Is.EqualTo(CharacterPresentationPaletteSetup.TargetPath(role)));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterials[1]), Is.EqualTo(UntouchedBlade));
                Assert.That(HierarchyContract(character), Is.EqualTo(frozen));
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.Zero);
            });
        }

        [TestCase("WardenMetal")]
        [TestCase("WardenCloth")]
        [TestCase("WardenRune")]
        [TestCase("RangerOutfit")]
        [TestCase("Priest")]
        [TestCase("Scorched")]
        public void OtherRoles_MapOnlyWhitelistedMaterialIdentityAndPreserveEveryOtherSlot(string role)
        {
            WithFixture((container, character) =>
            {
                Renderer renderer = AddRenderer(character.transform, "AnyCharacterMesh",
                    RequiredMaterial(SourceByRole[role]), RequiredMaterial(UntouchedBlade));
                var frozen = HierarchyContract(character);
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]), Is.EqualTo(CharacterPresentationPaletteSetup.TargetPath(role)));
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterials[1]), Is.EqualTo(UntouchedBlade));
                Assert.That(HierarchyContract(character), Is.EqualTo(frozen));
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.Zero);
            });
        }

        [Test]
        public void WorldBrazierWithoutAnimator_RemainsUntouchedEvenWhenSharingWardenMaterial()
        {
            WithFixture((container, character) =>
            {
                Renderer brazier = AddRenderer(container.transform, "WardenBrazier", RequiredMaterial(SourceByRole["WardenMetal"]));
                Renderer body = AddRenderer(character.transform, "CharacterBody", RequiredMaterial(SourceByRole["WardenMetal"]));
                Material before = brazier.sharedMaterial;
                var frozen = HierarchyContract(container);
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.EqualTo(1));
                Assert.That(brazier.sharedMaterial, Is.SameAs(before));
                Assert.That(AssetDatabase.GetAssetPath(body.sharedMaterial), Is.EqualTo(CharacterPresentationPaletteSetup.TargetPath("WardenMetal")));
                Assert.That(HierarchyContract(container), Is.EqualTo(frozen));
                Assert.That(CharacterPresentationPaletteSetup.Apply(container), Is.Zero);
            });
        }

        [Test]
        public void PersistentPrefabRoot_IsRejectedBeforeAnyRendererMutation()
        {
            string path = ProductionPrefabs[0];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var bytes = FreezeFiles(new[] { path, path + ".meta" });
            Assert.Throws<InvalidOperationException>(() => CharacterPresentationPaletteSetup.Apply(prefab));
            AssertFrozenFiles(bytes);
        }

        [TestCaseSource(nameof(PrefabCases))]
        public void ProductionHierarchyMapping_FromControlledLegacyCopyPreservesComponentsMeshAvatarAndTransforms(string path)
        {
            var diskBytes = FreezeFiles(new[] { path, path + ".meta" });
            WithPrefabCopy(path, root =>
            {
                RestoreLegacyMaterialsOnCopy(root);
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var expected = new Dictionary<Renderer, string[]>();
                foreach (Renderer renderer in renderers)
                {
                    expected[renderer] = renderer.sharedMaterials.Select(material =>
                    {
                        string role = ExpectedRole(renderer, material);
                        return role == null ? AssetDatabase.GetAssetPath(material) : CharacterPresentationPaletteSetup.TargetPath(role);
                    }).ToArray();
                }
                Assert.That(expected.Values.SelectMany(slots => slots).Count(IsPalettePath), Is.GreaterThan(0), path);
                var frozen = HierarchyContract(root);
                Assert.That(CharacterPresentationPaletteSetup.Apply(root), Is.GreaterThan(0), path);
                Assert.That(HierarchyContract(root), Is.EqualTo(frozen), "Only material references may differ: " + path);
                foreach (var row in expected)
                    Assert.That(row.Key.sharedMaterials.Select(material => AssetDatabase.GetAssetPath(material)), Is.EqualTo(row.Value), path + "/" + row.Key.name);
                Assert.That(CharacterPresentationPaletteSetup.Apply(root), Is.Zero, path);
                Assert.That(HierarchyContract(root), Is.EqualTo(frozen));
            });
            AssertFrozenFiles(diskBytes);
        }

        [TestCaseSource(nameof(PrefabCases))]
        public void SavedProductionPrefab_HasApprovedMappedRolesAndNeedsNoFurtherApply(string path)
        {
            // Deliberately fails before the explicit production migration. A transient
            // Apply on a preview must never be presented as saved-pipeline acceptance.
            var diskBytes = FreezeFiles(new[] { path, path + ".meta" });
            WithPrefabCopy(path, root =>
            {
                int applicableSlots = 0;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    string role = ExpectedRole(renderer, material);
                    if (role == null) continue;
                    applicableSlots++;
                    Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(CharacterPresentationPaletteSetup.TargetPath(role)),
                        path + "/" + renderer.name + ": saved production material has not been migrated.");
                }
                Assert.That(applicableSlots, Is.GreaterThan(0), path);
                Assert.That(CharacterPresentationPaletteSetup.Apply(root), Is.Zero, path);
            });
            AssertFrozenFiles(diskBytes);
        }

        private static string ExpectedRole(Renderer renderer, Material material)
        {
            if (renderer.GetComponentInParent<Animator>() == null) return null;
            string path = AssetDatabase.GetAssetPath(material);
            if (path == FogSource || new[] { "Bone", "Steel", "Cloth", "Eyes" }.Any(role => path == CharacterPresentationPaletteSetup.TargetPath(role)))
            {
                switch (renderer.name)
                {
                    case "Skeleton_Warrior_Helmet": return "Steel";
                    case "Skeleton_Warrior_Cloak": return "Cloth";
                    case "Skeleton_Warrior_Eyes": return "Eyes";
                    default: return "Bone";
                }
            }
            foreach (var row in SourceByRole.Where(row => row.Value != FogSource))
                if (path == row.Value || path == CharacterPresentationPaletteSetup.TargetPath(row.Key)) return row.Key;
            return null;
        }

        private static void RestoreLegacyMaterialsOnCopy(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string path = AssetDatabase.GetAssetPath(slots[i]);
                    foreach (var row in SourceByRole)
                        if (path == CharacterPresentationPaletteSetup.TargetPath(row.Key)) slots[i] = RequiredMaterial(row.Value);
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static string[] HierarchyContract(GameObject root)
        {
            // Full serialized component snapshots include transform, collider, Actor fields,
            // Animator Avatar/controller and MeshFilter/SkinnedMeshRenderer mesh/bone refs.
            // Only Renderer.m_Materials is removed; the slot count is explicitly retained.
            var rows = new List<string>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                rows.Add("GO:" + transform.gameObject.GetInstanceID() + ":" + EditorJsonUtility.ToJson(transform.gameObject));
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                Assert.That(component, Is.Not.Null, "Missing-script components cannot be silently excluded.");
                string json = EditorJsonUtility.ToJson(component);
                if (component is Renderer renderer)
                {
                    Assert.That(json, Does.Contain("\"m_Materials\""), "Renderer serialization must actually expose the field being excluded.");
                    json = Regex.Replace(json, "\"m_Materials\"\\s*:\\s*\\[[^\\]]*\\]", "\"m_Materials\":[]");
                    rows.Add("Slots:" + renderer.GetInstanceID() + ":" + renderer.sharedMaterials.Length + ":Enabled:" + renderer.enabled);
                }
                rows.Add(component.GetType().FullName + ":" + component.GetInstanceID() + ":" + json);
            }
            return rows.OrderBy(row => row, StringComparer.Ordinal).ToArray();
        }

        private static void WithPrefabCopy(string path, Action<GameObject> action)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            WithPreviewScene(preview =>
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
                Assert.That(instance, Is.Not.Null, path);
                try { action(instance); }
                finally { Object.DestroyImmediate(instance); }
            });
        }

        private static void WithFixture(Action<GameObject, GameObject> action)
        {
            WithPreviewScene(preview =>
            {
                var container = EditorUtility.CreateGameObjectWithHideFlags("PaletteFixture", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(container, preview);
                var character = EditorUtility.CreateGameObjectWithHideFlags("Character", HideFlags.HideAndDontSave, typeof(Animator));
                SceneManager.MoveGameObjectToScene(character, preview);
                character.transform.SetParent(container.transform, false);
                try { action(container, character); }
                finally { Object.DestroyImmediate(container); }
            });
        }

        private static void WithPreviewScene(Action<Scene> action)
        {
            string[] before = OpenSceneState();
            var preview = EditorSceneManager.NewPreviewScene();
            try { action(preview); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                Assert.That(OpenSceneState(), Is.EqualTo(before), "Tests must not dirty/switch the user's open scenes.");
            }
        }

        private static string[] OpenSceneState() => Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
        {
            Scene scene = SceneManager.GetSceneAt(index);
            return scene.handle + ":" + scene.path + ":" + scene.isDirty + ":active=" + (scene == SceneManager.GetActiveScene());
        }).ToArray();

        private static Renderer AddRenderer(Transform parent, string name, params Material[] materials)
        {
            var item = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, typeof(MeshRenderer), typeof(BoxCollider));
            SceneManager.MoveGameObjectToScene(item, parent.gameObject.scene);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = new Vector3(.3f, .5f, -.2f);
            item.transform.localRotation = Quaternion.Euler(4f, 17f, 3f);
            item.transform.localScale = new Vector3(.8f, 1.1f, .7f);
            Renderer renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        private static Material RequiredMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, path);
            return material;
        }

        private static Dictionary<string, byte[]> FreezeSourceFiles() => FreezeFiles(SourceByRole.Values.Distinct()
            .Concat(new[] { CharacterPresentationPaletteSetup.PalettePath })
            .SelectMany(path => new[] { path, path + ".meta" }));

        private static Dictionary<string, byte[]> FreezeFiles(IEnumerable<string> paths)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in paths.Distinct())
            {
                Assert.That(File.Exists(path), Is.True, path);
                result[path] = File.ReadAllBytes(path);
            }
            return result;
        }

        private static void AssertFrozenFiles(Dictionary<string, byte[]> frozen)
        {
            foreach (var row in frozen)
                Assert.That(File.ReadAllBytes(row.Key), Is.EqualTo(row.Value), "Asset bytes changed: " + row.Key);
        }

        private static bool IsPalettePath(string path) => path != null && path.StartsWith(CharacterPresentationPaletteSetup.MaterialRoot + "/", StringComparison.Ordinal);
        private static Vector3 Rgb(Color color) => new Vector3(color.r, color.g, color.b);

        private static void AssertColorRange(Color color, float maximum)
        {
            Assert.That(color.r, Is.InRange(0f, maximum));
            Assert.That(color.g, Is.InRange(0f, maximum));
            Assert.That(color.b, Is.InRange(0f, maximum));
        }

        private static void AssertStoredColor(Color actual, Color authored)
        {
            // Measured native URP storage precision only; byte/GUID idempotence tests
            // above remain exact and combat/contact tolerances are not involved.
            Assert.That(actual.r, Is.EqualTo(authored.r).Within(.000001f));
            Assert.That(actual.g, Is.EqualTo(authored.g).Within(.000001f));
            Assert.That(actual.b, Is.EqualTo(authored.b).Within(.000001f));
            Assert.That(actual.a, Is.EqualTo(authored.a).Within(.000001f));
        }
    }
}
