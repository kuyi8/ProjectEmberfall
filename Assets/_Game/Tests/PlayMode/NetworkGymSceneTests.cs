using System.Collections;
using System.Linq;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using Emberfall.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class NetworkGymSceneTests
    {
        [Test]
        public void NetworkPlayerMeleeSweep_CountsEveryServerSideTargetInSharedSector()
        {
            Vector3 source = new Vector3(10f, 0f, 10f);
            Vector3 forward = Vector3.forward;
            Vector3[] serverTargetPositions =
            {
                source + new Vector3(-1.5f, 0f, 1.5f),
                source + new Vector3(0f, 0f, 2.4f),
                source + new Vector3(1.5f, 0f, 1.5f),
                source + new Vector3(0f, 0f, -1f)
            };

            int attemptedTargets = 0;
            int hitCount = NetworkGymSceneController.ResolveAllMeleeTargets(
                serverTargetPositions,
                target =>
                {
                    attemptedTargets++;
                    return NetworkCombatSpatialValidator.IsValidPlayerMeleeHit(
                        source.x, source.z, forward.x, forward.z, target.x, target.z);
                });

            Assert.That(hitCount, Is.EqualTo(3),
                "The server-authored sweep must keep every in-sector target, not stop after the first hit.");
            Assert.That(attemptedTargets, Is.EqualTo(4),
                "The production sweep iterator must evaluate all Server targets even after a successful hit.");
        }

        [Test]
        public void ForestSealInteractionBuildsVisibleInRangePrompt()
        {
            string label = NetworkLocalizedText.Resolve(
                NetworkGymWorldObjective.ResolveInteractionTextId(
                    MainQuestStage.ActivateSeals,
                    bossRoute: true,
                    rewardClaimed: false));

            Assert.That(label, Is.EqualTo("使用余烬符印激活雾林封印"));
            Assert.That(
                NetworkRouteHud.BuildInteractionPrompt(label, inRange: true, distance: 1.4f),
                Is.EqualTo("E　使用余烬符印激活雾林封印"));
        }

        [TestCase(1920f)]
        [TestCase(1728f)]
        [TestCase(1280f)]
        [TestCase(1024f)]
        [TestCase(960f)]
        public void NetworkHudTopPanelsDoNotOverlapAtSupportedWidths(float width)
        {
            NetworkHudLayout layout = NetworkHudLayout.Resolve(width);

            Assert.That(layout.Player.Overlaps(layout.Boss), Is.False);
            Assert.That(layout.Player.Overlaps(layout.Quest), Is.False);
            Assert.That(layout.Boss.Overlaps(layout.Quest), Is.False);
            Assert.That(layout.Player.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(layout.Quest.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(layout.Quest.width, Is.GreaterThanOrEqualTo(320f));
            Assert.That(layout.Quest.height, Is.GreaterThanOrEqualTo(150f));
        }

        [UnityTest]
        public IEnumerator SceneAndRegisteredPlayerPrefabContainRequiredNetworkAdapters()
        {
            SessionRuntime.Current.Shutdown();
            yield return SceneManager.LoadSceneAsync("91_NetworkGym", LoadSceneMode.Single);
            yield return null;

            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>().UsesServerTriggeredEncounters, Is.False,
                "The isolated Network Gym must retain its immediate three-enemy diagnostic roster.");
            Assert.That(GameObject.Find("Arena_Floor"), Is.Not.Null);
            Assert.That(GameObject.Find("Wall_North"), Is.Not.Null);

            GameObject prefab = Resources.Load<GameObject>("Networking/P_M5_NetworkGymPlayer");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            NetworkGymPlayer networkPlayer = prefab.GetComponent<NetworkGymPlayer>();
            Assert.That(networkPlayer, Is.Not.Null);
            Assert.That(networkPlayer.IsAttackPresentationConfigured, Is.True);
            Assert.That(networkPlayer.OwnerCameraUsesGameplayRig, Is.True);
            Assert.That(networkPlayer.OwnerHasNetworkTargeting, Is.True);
            LockOnTargeting targeting = prefab.GetComponent<LockOnTargeting>();
            Assert.That(targeting, Is.Not.Null);
            NetworkRouteHud routeHud = prefab.GetComponent<NetworkRouteHud>();
            Assert.That(routeHud, Is.Not.Null);
            Assert.That(routeHud.IsConfigured, Is.True);
            Assert.That(routeHud.IsLockOnConfigured, Is.True);
            Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);
            Transform playerVisual = prefab.transform.Find("Ranger_Visual");
            Assert.That(playerVisual, Is.Not.Null);
            Assert.That(playerVisual.localPosition.y, Is.EqualTo(1.05f).Within(0.001f));
            ThirdPersonCameraRig ownerCamera = prefab.GetComponentInChildren<ThirdPersonCameraRig>(true);
            Assert.That(ownerCamera, Is.Not.Null);
            Assert.That(ownerCamera.gameObject.activeSelf, Is.False,
                "Only the locally owned network player may activate its gameplay camera.");
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(prefab.transform.Find("Player_DownedRescueBeacon"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Player_DefenseWindow"), Is.Not.Null);
            Transform attackSector = prefab.transform.Find("Player_AttackWindow");
            Assert.That(attackSector, Is.Not.Null);
            Assert.That(attackSector.localPosition.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(attackSector.GetComponent<Collider>(), Is.Null);
            Mesh attackMesh = attackSector.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(attackMesh, Is.Not.Null);
            Assert.That(attackMesh.name, Is.EqualTo("M_PlayerAttackSector"));
            Assert.That(attackMesh.vertices.Max(vertex => vertex.z), Is.GreaterThan(0.2f));
            Assert.That(attackMesh.vertices.Min(vertex => vertex.x), Is.LessThan(-2.3f));
            Assert.That(attackMesh.vertices.Max(vertex => vertex.x), Is.GreaterThan(2.3f));

            GameObject enemyPrefab = Resources.Load<GameObject>("Networking/P_M5_NetworkGymEnemy");
            Assert.That(enemyPrefab, Is.Not.Null);
            Assert.That(enemyPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(enemyPrefab.GetComponent<NetworkGymEnemy>(), Is.Not.Null);
            Assert.That(enemyPrefab.GetComponent<NetworkCombatTargetProxy>(), Is.Not.Null);
            Assert.That(enemyPrefab.transform.Find("NetworkTarget_AimPoint"), Is.Not.Null);
            Assert.That(enemyPrefab.GetComponent<CapsuleCollider>(), Is.Not.Null);
            Assert.That(enemyPrefab.transform.Find("Fogwalker_Visual").localPosition.y,
                Is.EqualTo(1.05f).Within(0.001f));

            GameObject rangedEnemyPrefab = Resources.Load<GameObject>("Networking/P_M5_NetworkRunePriest");
            Assert.That(rangedEnemyPrefab, Is.Not.Null);
            Assert.That(rangedEnemyPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(rangedEnemyPrefab.GetComponent<NetworkGymEnemy>().Archetype,
                Is.EqualTo(NetworkEnemyArchetype.RunePriest));
            Assert.That(rangedEnemyPrefab.GetComponentInChildren<Animator>(true), Is.Not.Null);
            Assert.That(rangedEnemyPrefab.transform.Find("RunePriest_Visual").localPosition.y,
                Is.EqualTo(1.05f).Within(0.001f));

            GameObject shieldEnemyPrefab = Resources.Load<GameObject>("Networking/P_M5_NetworkRuinGuard");
            Assert.That(shieldEnemyPrefab, Is.Not.Null);
            Assert.That(shieldEnemyPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(shieldEnemyPrefab.GetComponent<NetworkGymEnemy>().Archetype,
                Is.EqualTo(NetworkEnemyArchetype.RuinGuard));
            Assert.That(shieldEnemyPrefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
            Assert.That(shieldEnemyPrefab.transform.Find("RuinGuard_Visual").localPosition.y,
                Is.EqualTo(1.05f).Within(0.001f));

            GameObject wardenPrefab = Resources.Load<GameObject>("Networking/P_M5_NetworkWarden");
            Assert.That(wardenPrefab, Is.Not.Null);
            Assert.That(wardenPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(wardenPrefab.GetComponent<NetworkWarden>(), Is.Not.Null);
            Assert.That(wardenPrefab.GetComponent<NetworkCombatTargetProxy>(), Is.Not.Null);
            Assert.That(wardenPrefab.GetComponent<NetworkCombatTargetProxy>().IsBoss, Is.True);
            Assert.That(wardenPrefab.GetComponent<CapsuleCollider>(), Is.Not.Null);
            Assert.That(wardenPrefab.transform.Find("Warden_Visual").localPosition.y,
                Is.EqualTo(1.08f).Within(0.001f));

            GameObject worldPrefab = Resources.Load<GameObject>("Networking/P_M5_NetworkGymWorldObjective");
            Assert.That(worldPrefab, Is.Not.Null);
            Assert.That(worldPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(worldPrefab.GetComponent<NetworkGymWorldObjective>(), Is.Not.Null);
            Transform gate = worldPrefab.transform.Find("SharedGate_Blocker");
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate.GetComponent<BoxCollider>(), Is.Not.Null);
            Transform highlight = worldPrefab.transform.Find("SharedSeal_Highlight");
            Assert.That(highlight, Is.Not.Null);
            Assert.That(highlight.GetComponent<Collider>(), Is.Null);
            Assert.That(highlight.GetComponent<MeshFilter>().sharedMesh.name,
                Is.EqualTo("M_NetworkObjectiveRing"));
            Assert.That(highlight.Find("SharedSeal_HighlightIcon"), Is.Not.Null);

            yield return SceneManager.LoadSceneAsync("20_Sanctum", LoadSceneMode.Additive);
            Assert.That(GameObject.Find("Sanctum_ArenaFloor"), Is.Not.Null);
            Assert.That(GameObject.Find("NetworkSanctum_PlayerSpawn_A"), Is.Not.Null);
            Assert.That(GameObject.Find("NetworkSanctum_PlayerSpawn_B"), Is.Not.Null);
            Assert.That(GameObject.Find("NetworkSanctum_WardenSpawn"), Is.Not.Null);
            yield return SceneManager.UnloadSceneAsync("20_Sanctum");
        }
    }
}
