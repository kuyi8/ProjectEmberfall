using System.Linq;
using Emberfall.AI.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class M5cScorchedEliteAssetTests
    {
        private const string VisualPath =
            "Assets/_Game/Prefabs/Characters/M5c/P_M5c_Enemy_RuinGuardScorched_Knight.prefab";

        [Test]
        public void ScorchedKnightPrefab_UsesSelectedHumanoidAndPresentationOnlyGear()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
            Assert.That(prefab, Is.Not.Null);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.isHuman, Is.True);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "Sword_M5c_Scorched_Equipped"), Is.True);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "Shield_Wooden_Equipped"), Is.True);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "ScorchedCore"), Is.True);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                "The selected art prefab must not become gameplay collision authority.");
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .All(material => AssetDatabase.GetAssetPath(material).StartsWith("Assets/_Game/")), Is.True);
        }

        [Test]
        public void EmberValleyAndNetworkPrefab_BindScorchedStableIdAndAuthorityAdapters()
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
            ShieldEnemyActor actor = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ShieldEnemyActor>(true))
                .Single(item => item.name == "Enemy_RuinGuard_Courtyard");
            var actorData = new SerializedObject(actor);
            Assert.That(actorData.FindProperty("_enemyId").stringValue,
                Is.EqualTo("enemy:ruin-guard-scorched"));
            Assert.That(actorData.FindProperty("_scorchedWarningMaterial").objectReferenceValue, Is.Not.Null);
            Assert.That(actorData.FindProperty("_scorchedImpactVfxPrefab").objectReferenceValue, Is.Not.Null);

            GameObject networkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Resources/Networking/P_M5_NetworkRuinGuard.prefab");
            Assert.That(networkPrefab, Is.Not.Null);
            MonoBehaviour networkEnemy = networkPrefab.GetComponents<MonoBehaviour>()
                .Single(component => component.GetType().Name == "NetworkGymEnemy");
            Assert.That(networkEnemy, Is.Not.Null);
            var networkData = new SerializedObject(networkEnemy);
            Assert.That(networkData.FindProperty("_enemyId").stringValue,
                Is.EqualTo("enemy:ruin-guard-scorched"));
            Assert.That(networkData.FindProperty("_archetype").enumValueIndex,
                Is.EqualTo(2));
            Assert.That(networkPrefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "ScorchedCore"), Is.True);
        }
    }
}
