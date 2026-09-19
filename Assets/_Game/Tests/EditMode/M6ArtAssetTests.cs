using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class M6ArtAssetTests
    {
        private const string PrefabRoot = "Assets/_Game/Prefabs/Characters/M6Art";

        [TestCase("P_M6_Enemy_FogwalkerSkeleton.prefab", "M_M6_FogwalkerBone")]
        [TestCase("P_M6_Enemy_RunePriest.prefab", "M_M6_RunePriest")]
        [TestCase("P_M6_Boss_EmberWarden.prefab", "M_M6_Warden")]
        public void SelectedCharacterPrefab_HasValidHumanoidAndProjectOwnedMaterials(
            string fileName,
            string expectedMaterialPrefix)
        {
            string path = $"{PrefabRoot}/{fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, path);
            Assert.That(animator.isHuman, Is.True, path);
            Assert.That(animator.avatar, Is.Not.Null, path);
            Assert.That(animator.avatar.isValid, Is.True, path);
            Assert.That(animator.applyRootMotion, Is.False, path);

            Material[] materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .ToArray();
            Assert.That(materials, Is.Not.Empty, path);
            Assert.That(materials.Any(material => material.name.StartsWith(expectedMaterialPrefix)), Is.True, path);
            Assert.That(materials.All(material =>
                AssetDatabase.GetAssetPath(material).StartsWith("Assets/_Game/Art/Materials/M6Art/")), Is.True, path);
            Assert.That(materials.All(material =>
                material.shader.name.StartsWith("Universal Render Pipeline/")), Is.True, path);
        }

        [Test]
        public void SelectedPlayer_UsesStandardProportionRangerWithValidHumanoid()
        {
            const string path = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman, Is.True);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.GetBoneTransform(HumanBodyBones.RightHand), Is.Not.Null);
            Assert.That(animator.GetBoneTransform(HumanBodyBones.LeftFoot), Is.Not.Null);
            Assert.That(animator.GetBoneTransform(HumanBodyBones.RightFoot), Is.Not.Null);

            Material[] materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .ToArray();
            Assert.That(materials, Is.Not.Empty);
            Assert.That(materials.All(material => AssetDatabase.GetAssetPath(material)
                .StartsWith("Assets/_Game/Art/Materials/Character/M3Art/")), Is.True);
        }

        [Test]
        public void WardenPrefab_HasDistinctImportedArmorAttachments()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{PrefabRoot}/P_M6_Boss_EmberWarden.prefab");
            Transform[] hierarchy = prefab.GetComponentsInChildren<Transform>(true);
            Assert.That(hierarchy.Any(item => item.name == "Helmet_Closed"), Is.True);
            Assert.That(hierarchy.Any(item => item.name == "ShoulderPads"), Is.True);
        }

        [Test]
        public void EmberValley_ContainsPresentationOnlyImportedDungeonUpgrade()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
            GameObject root = GameObject.Find("[Art] M6 Imported Upgrade");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThanOrEqualTo(10));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty,
                "Imported M6 presentation geometry must not replace gameplay collision authority.");
            Assert.That(GameObject.Find("WardenGateArch"), Is.Not.Null);
            Assert.That(GameObject.Find("WardenHorseStatue_West"), Is.Not.Null);
            Assert.That(GameObject.Find("CourtyardBrazier"), Is.Not.Null);
        }

        [TestCase("P_M6_Impact_Steel.prefab")]
        [TestCase("P_M6_Impact_Guard.prefab")]
        [TestCase("P_M6_Impact_Ember.prefab")]
        public void CombatImpactVariant_IsProjectOwnedAndParticleOnly(string fileName)
        {
            string path = $"Assets/_Game/Prefabs/VFX/M6Art/{fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true), Is.Not.Empty, path);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty,
                "Imported demo scripts must not become runtime dependencies of the project VFX variant.");
        }

        [TestCase("P_M6_Warden_PhaseTransition.prefab")]
        [TestCase("P_M6_Warden_RuneCleave.prefab")]
        [TestCase("P_M6_Warden_DelayedBlast.prefab")]
        public void WardenVfxVariant_IsProjectOwnedAndParticleOnly(string fileName)
        {
            string path = $"Assets/_Game/Prefabs/VFX/M6Art/{fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true), Is.Not.Empty, path);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty,
                "Boss VFX variants must not carry imported demo behaviours.");
        }

        [Test]
        public void EmberValley_BindsProjectOwnedWardenCombatVfx()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
            WardenCombatVfxPresenter presenter = Object.FindObjectOfType<WardenCombatVfxPresenter>(true);
            WardenActor warden = Object.FindObjectOfType<WardenActor>(true);
            Assert.That(presenter, Is.Not.Null);
            Assert.That(warden, Is.Not.Null);
            var serialized = new SerializedObject(presenter);
            foreach (string field in new[] { "_phaseTransitionPrefab", "_runeCleavePrefab" })
            {
                Object value = serialized.FindProperty(field).objectReferenceValue;
                Assert.That(value, Is.Not.Null, field);
                StringAssert.StartsWith("Assets/_Game/Prefabs/VFX/M6Art/", AssetDatabase.GetAssetPath(value));
            }
            var actorSerialized = new SerializedObject(warden);
            Object blast = actorSerialized.FindProperty("_delayedBlastVfxPrefab").objectReferenceValue;
            Assert.That(blast, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(blast),
                Is.EqualTo("Assets/_Game/Prefabs/VFX/M6Art/P_M6_Warden_DelayedBlast.prefab"));
        }

        [Test]
        public void CombatScenes_BindProjectOwnedImpactPresentation()
        {
            foreach (string scenePath in new[]
                     {
                         "Assets/_Game/Scenes/10_EmberValley.unity",
                         "Assets/_Game/Scenes/90_CombatGym.unity"
                     })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                CombatImpactVfxPresenter presenter = Object.FindObjectOfType<CombatImpactVfxPresenter>(true);
                Assert.That(presenter, Is.Not.Null, scenePath);
                var serialized = new SerializedObject(presenter);
                foreach (string field in new[]
                         {
                             "_steelImpactPrefab",
                             "_guardImpactPrefab",
                             "_emberImpactPrefab"
                         })
                {
                    Object value = serialized.FindProperty(field).objectReferenceValue;
                    Assert.That(value, Is.Not.Null, $"{scenePath}: {field}");
                    StringAssert.StartsWith("Assets/_Game/Prefabs/VFX/M6Art/", AssetDatabase.GetAssetPath(value));
                }
            }
        }

        [Test]
        public void CombatScenes_BindSwordTrailToAuthoredWeaponAnchors()
        {
            foreach (string scenePath in new[]
                     {
                         "Assets/_Game/Scenes/10_EmberValley.unity",
                         "Assets/_Game/Scenes/90_CombatGym.unity"
                     })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                SwordTrailPresenter presenter = Object.FindObjectOfType<SwordTrailPresenter>(true);
                Assert.That(presenter, Is.Not.Null, scenePath);
                var serialized = new SerializedObject(presenter);
                Transform bladeRoot = serialized.FindProperty("_bladeRoot").objectReferenceValue as Transform;
                Transform bladeTip = serialized.FindProperty("_bladeTip").objectReferenceValue as Transform;
                Material material = serialized.FindProperty("_trailMaterial").objectReferenceValue as Material;
                Assert.That(bladeRoot, Is.Not.Null, $"{scenePath}: blade root");
                Assert.That(bladeTip, Is.Not.Null, $"{scenePath}: blade tip");
                Assert.That(Vector3.Distance(bladeRoot.position, bladeTip.position), Is.GreaterThan(0.1f));
                Assert.That(material, Is.Not.Null, $"{scenePath}: trail material");
                Assert.That(AssetDatabase.GetAssetPath(material),
                    Is.EqualTo("Assets/_Game/Art/Materials/M6Art/M_M6_SwordTrail.mat"));
                Assert.That(material.shader.name, Is.EqualTo("Emberfall/VFX/SwordTrail"));
            }
        }

        [Test]
        public void ThrowingKnife_UsesAVisualScaleMatchedBoxSweepProfile()
        {
            const string path = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.GetComponent<PlayerThrowingKnifeProjectile>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<TrailRenderer>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
            BoxCollider hitbox = prefab.GetComponent<BoxCollider>();
            Assert.That(hitbox, Is.Not.Null);
            Assert.That(hitbox.isTrigger, Is.True);
            Assert.That(hitbox.enabled, Is.False,
                "The box defines the continuous sweep profile and must not become a second physics authority.");
            Assert.That(hitbox.size.z, Is.GreaterThan(hitbox.size.x));
            Assert.That(hitbox.size.z, Is.GreaterThan(hitbox.size.y));
            Assert.That(hitbox.size.z, Is.InRange(0.34f, 0.44f));
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => !(renderer is TrailRenderer))
                .ToArray();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.InRange(0.35f, 0.5f));
        }

        [Test]
        public void CombatScenes_BindThrowingKnifeLauncherToProjectOwnedPrefab()
        {
            foreach (string scenePath in new[]
                     {
                         "Assets/_Game/Scenes/10_EmberValley.unity",
                         "Assets/_Game/Scenes/90_CombatGym.unity"
                     })
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                PlayerThrowingKnifeLauncher launcher = Object.FindObjectOfType<PlayerThrowingKnifeLauncher>(true);
                Assert.That(launcher, Is.Not.Null, scenePath);
                var serialized = new SerializedObject(launcher);
                GameObject prefab = serialized.FindProperty("_projectilePrefab").objectReferenceValue as GameObject;
                Assert.That(prefab, Is.Not.Null, scenePath);
                Assert.That(AssetDatabase.GetAssetPath(prefab),
                    Is.EqualTo("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab"));
                Assert.That(serialized.FindProperty("_launchOrigin").objectReferenceValue, Is.Not.Null);
            }
        }
    }
}
