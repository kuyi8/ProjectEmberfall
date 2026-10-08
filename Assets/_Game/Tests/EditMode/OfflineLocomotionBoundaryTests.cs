using System;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class OfflineLocomotionBoundaryTests
    {
        const string SharedPath = "Assets/_Game/Art/Animations/Player/AC_Player_M1.controller";
        static readonly Type[] EnemyPresenters = { typeof(MeleeEnemyAnimationPresenter), typeof(ShieldEnemyAnimationPresenter),
            typeof(RangedEnemyAnimationPresenter), typeof(WardenAnimationPresenter), typeof(SummonerEnemyActor) };
        // Declare before inspecting: new pure visual assets since r7. Not all art or original terrain.
        static readonly string[] NewVisualPrefabs = {
            "Assets/_Game/Prefabs/Weapons/Player/P_Player_KayKitSword.prefab",
            ChoiceMarkerArtSetup.PrefabPath, AshEncounterBannerSetup.PrefabPath };

        [TestCase("Assets/_Game/Scenes/10_EmberValley.unity")]
        [TestCase("Assets/_Game/Scenes/90_CombatGym.unity")]
        public void ActualSceneEnemySerializedBindings_KeepSharedController(string path)
        {
            var original = SceneManager.GetActiveScene();
            var scene = OpenReadOnlyScene(path, out bool opened);
            try
            {
                int count = 0;
                foreach (var component in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)))
                    if (EnemyPresenters.Contains(component.GetType())) { CheckBinding(component, path); count++; }
                Assert.That(count, Is.GreaterThan(0), "The real scene must contribute bindings; an empty scan is not a pass.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        }

        [Test]
        public void ActualSanctum_IsEnvironmentOnly_NotAnEmptyEnemyBindingPass()
        {
            var original = SceneManager.GetActiveScene();
            var scene = OpenReadOnlyScene("Assets/_Game/Scenes/20_Sanctum.unity", out bool opened);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Count(), Is.GreaterThan(0));
                Assert.That(roots.SelectMany(g => g.GetComponentsInChildren<Animator>(true)), Is.Empty);
                Assert.That(roots.SelectMany(g => g.GetComponentsInChildren<Emberfall.Gameplay.Combat.Unity.CombatTarget>(true)), Is.Empty);
                // Offline Warden is authored in Valley; network Warden spawns from the separately tested real prefab.
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        }

        [TestCase("P_M5_NetworkGymPlayer")]
        [TestCase("P_M5_NetworkGymEnemy")]
        [TestCase("P_M5_NetworkRunePriest")]
        [TestCase("P_M5_NetworkRuinGuard")]
        [TestCase("P_M5_NetworkWarden")]
        public void ActualNetworkPrefabSerializedBindings_KeepSharedController(string name)
        {
            string path = "Assets/_Game/Resources/Networking/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            // Inspect serialized adapters without adding a NET assembly dependency to this test assembly.
            string[] types = { "Emberfall.Networking.NetworkGymPlayer", "Emberfall.Networking.NetworkGymEnemy", "Emberfall.Networking.NetworkWarden" };
            var bindings = prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c =>
                types.Contains(c.GetType().FullName)).ToArray();
            Assert.That(bindings.Length, Is.EqualTo(1), path);
            CheckBinding(bindings[0], path);
        }

        [TestCase("Assets/_Game/Scenes/10_EmberValley.unity")]
        [TestCase("Assets/_Game/Scenes/91_NetworkGym.unity")]
        public void ActualNetworkSceneSpawner_ReferencesTheVerifiedPrefabs(string path)
        {
            var original = SceneManager.GetActiveScene();
            var scene = OpenReadOnlyScene(path, out bool opened);
            try
            {
                var spawner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                    .Single(c => c.GetType().FullName == "Emberfall.Networking.NetworkGymSceneController");
                var data = new SerializedObject(spawner);
                string[] fields = { "_playerPrefab", "_enemyPrefab", "_rangedEnemyPrefab", "_shieldEnemyPrefab", "_wardenPrefab" };
                string[] names = { "P_M5_NetworkGymPlayer", "P_M5_NetworkGymEnemy", "P_M5_NetworkRunePriest",
                    "P_M5_NetworkRuinGuard", "P_M5_NetworkWarden" };
                for (int i = 0; i < fields.Length; i++)
                    Assert.That(data.FindProperty(fields[i]).objectReferenceValue,
                        // Gym deliberately spawns only Player/Fogwalker. It is not the formal five-prefab roster.
                        path.EndsWith("91_NetworkGym.unity", StringComparison.Ordinal) && i >= 2 ? Is.Null :
                        Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/Networking/" + names[i] + ".prefab")),
                        path + " / " + fields[i] + " actual inspector override");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        }

        static void CheckBinding(MonoBehaviour component, string context)
        {
            var data = new SerializedObject(component);
            var animator = data.FindProperty("_animator").objectReferenceValue as Animator;
            var set = data.FindProperty("_animationSet").objectReferenceValue as PlayerAnimationSet;
            Assert.That(animator, Is.Not.Null, context + " / " + component.name);
            Assert.That(set, Is.Not.Null, context + " / " + component.name);
            var shared = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SharedPath);
            Assert.That(set.Controller, Is.SameAs(shared), context + " / " + component.name);
            Assert.That(animator.runtimeAnimatorController, Is.SameAs(shared),
                context + " / " + component.name + " actual saved Animator, including scene/prefab overrides");
        }

        [Test]
        public void OwnedController_IsUniqueAndRetainsGuidAcrossScopedSetup()
        {
            string[] Controllers() => AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/_Game" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("OfflineLocomotion")).OrderBy(p => p).ToArray();
            var before = Controllers();
            Assert.That(before, Is.EqualTo(new[] { OfflineLocomotionSetup.ControllerPath }));
            string guid = AssetDatabase.AssetPathToGUID(OfflineLocomotionSetup.ControllerPath);
            Assert.That(guid, Is.Not.Empty);
            OfflineLocomotionSetup.Apply(); OfflineLocomotionSetup.Apply();
            Assert.That(AssetDatabase.AssetPathToGUID(OfflineLocomotionSetup.ControllerPath), Is.EqualTo(guid));
            Assert.That(Controllers(), Is.EqualTo(before), "Generated backups must not become imported controllers.");
        }

        [Test]
        public void DeclaredNewVisuals_HaveNoPhysicsOrCombatAuthority()
        {
            foreach (string path in NewVisualPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), path);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<Emberfall.Gameplay.Combat.Unity.CombatTarget>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true), Is.Empty, path);
            }
            var original = SceneManager.GetActiveScene();
            var scene = OpenReadOnlyScene("Assets/_Game/Scenes/10_EmberValley.unity", out bool opened);
            try
            {
                var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                var plaques = transforms.Where(t => t.name == ChoiceMarkerArtSetup.ChildName).ToArray();
                Assert.That(plaques.Length, Is.EqualTo(6));
                foreach (var plaque in plaques) Assert.That(plaque.GetComponentsInChildren<Collider>(true), Is.Empty, plaque.parent.name);
                var banners = scene.GetRootGameObjects().Single(g => g.name == AshEncounterBannerSetup.RootName);
                Assert.That(banners.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
        }

        static Scene OpenReadOnlyScene(string path, out bool opened)
        {
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False, "Preserve unsaved user scene before additive inspection.");
            var existing = SceneManager.GetSceneByPath(path);
            opened = !existing.IsValid() || !existing.isLoaded;
            return opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : existing;
        }
    }
}
