using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Diagnostics;
using Emberfall.Gameplay.Movement;
using Emberfall.Infrastructure.Saves;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class EmberValleySceneTests
    {
        [UnityTest]
        public IEnumerator EmberValley_ScorchedEliteLoadsWithAuthoredPresentationAndQuota()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            ShieldEnemyActor elite = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(item => item.name == "Enemy_RuinGuard_Courtyard");
            Assert.That(elite, Is.Not.Null);
            Assert.That(elite.enabled, Is.True);
            Assert.That(elite.Brain, Is.Not.Null);
            Assert.That(elite.Definition.Id.Value, Is.EqualTo("enemy:ruin-guard-scorched"));
            Assert.That(elite.Definition.ScorchedBurstEnabled, Is.True);
            Assert.That(elite.ScorchedPresentationConfigured, Is.True);
            Assert.That(elite.GetComponentsInChildren<Animator>(true).Single().isHuman, Is.True);
            Assert.That(elite.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "ScorchedCore"), Is.True);

            CombatEncounterCoordinator courtyard = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(group => group.name.Contains("Courtyard"));
            Assert.That(courtyard.MaximumConcurrentMeleeAttackers, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator EmberValley_LoadsCompleteGrayboxRoute()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow, Is.Not.Null);
            Assert.That(flow.IsInitialized, Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.MeetScout));
            Assert.That(flow.PacingRunId, Is.Not.Empty,
                "The offline route must create a read-only pacing run for real-play logs.");
            Assert.That(
                Object.FindObjectsOfType<CombatEncounterCoordinator>()
                    .Select(coordinator => coordinator.TelemetrySegment),
                Is.EquivalentTo(new[]
                {
                    "forest-encounter", "bridge-encounter", "courtyard-encounter", "pre-sanctum-encounter"
                }));
            NetworkEmberValleyModeAdapter networkMode =
                Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            Assert.That(networkMode, Is.Not.Null, "The formal scene must carry the M5 network-mode adapter.");
            Assert.That(networkMode.IsConfigured, Is.True);
            Assert.That(networkMode.ContainsOfflineBehaviour<BridgeMechanismInteractable>(), Is.True);
            Assert.That(networkMode.ContainsOfflineBehaviour<RouteEnrichmentInteractable>(), Is.True);
            Assert.That(networkMode.ContainsOfflineBehaviour<BridgeMechanismGuidancePresenter>(), Is.True);
            Assert.That(networkMode.NetworkModeActive, Is.False,
                "Ordinary Ember Valley loading must preserve the complete offline route.");
            NetworkGymSceneController networkController =
                Object.FindObjectOfType<NetworkGymSceneController>(true);
            Assert.That(networkController, Is.Not.Null);
            Assert.That(networkController.IsSharedMainWorld, Is.True);
            Assert.That(networkController.IsSharedWorldConfigured, Is.True);
            Assert.That(networkController.IsEnemyRosterConfigured, Is.True,
                "The formal network slice must configure Fogwalker, Rune Priest, and Ruin Guard prefabs.");
            Assert.That(networkController.UsesServerTriggeredEncounters, Is.True);
            Assert.That(networkController.AuthoredEncounterCount, Is.EqualTo(4));
            Assert.That(networkController.AuthoredEnemySpawnCount, Is.EqualTo(10));
            Assert.That(
                networkController.AuthoredEncounters.Select(encounter => encounter.StableId),
                Is.EquivalentTo(new[]
                {
                    "encounter:fog-forest",
                    "encounter:broken-bridge",
                    "encounter:scorched-courtyard",
                    "encounter:pre-sanctum"
                }));
            Assert.That(
                networkController.AuthoredEncounters
                    .Select(encounter => string.Join(",", encounter.Spawns
                        .GroupBy(spawn => spawn.Archetype)
                        .OrderBy(group => group.Key)
                        .Select(group => $"{group.Key}:{group.Count()}")))
                    .Distinct()
                    .Count(),
                Is.EqualTo(4),
                "Each authored encounter must pose a distinct composition problem.");
            GameObject sharedReward = Object.FindObjectsOfType<NetworkRewardPresentation>(true)
                .SingleOrDefault()?.gameObject;
            Assert.That(sharedReward, Is.Not.Null);
            Assert.That(sharedReward.GetComponent<Collider>(), Is.Null,
                "The shared reward presentation must never participate in gameplay collision.");
            NetworkRewardPresentation rewardPresentation =
                sharedReward.GetComponent<NetworkRewardPresentation>();
            Assert.That(rewardPresentation, Is.Not.Null);
            Assert.That(rewardPresentation.IsProjectPresentation, Is.True);
            Assert.That(sharedReward.GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("M_SharedEmberShard"));
            Assert.That(sharedReward.transform.localScale.y, Is.GreaterThan(sharedReward.transform.localScale.x));
            M2RouteHud hud = Object.FindObjectOfType<M2RouteHud>();
            Assert.That(hud, Is.Not.Null);
            ThirdPersonCameraRig cameraRig = Object.FindObjectOfType<ThirdPersonCameraRig>();
            Assert.That(cameraRig, Is.Not.Null);
            hud.SetPaused(true);
            Assert.That(hud.IsPaused, Is.True);
            Assert.That(cameraRig.IsLookInputBlocked, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            hud.SetPaused(false);
            Assert.That(hud.IsPaused, Is.False);
            Assert.That(cameraRig.IsLookInputBlocked, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            InputTelemetryOverlay inputOverlay = Object.FindObjectOfType<InputTelemetryOverlay>();
            Assert.That(inputOverlay, Is.Not.Null);
            Assert.That(inputOverlay.IsVisible, Is.True);
            Assert.That(inputOverlay.EncounterTelemetryText, Does.Contain("ATTACK"));
            Assert.That(inputOverlay.TacticalTelemetryText, Does.Contain("中立符文"));
            if (ContentPackageRuntime.IsInitialized)
            {
                Assert.That(inputOverlay.ContentTelemetryText, Does.Contain("schema 1"));
                Assert.That(inputOverlay.ContentTelemetryText,
                    Does.Contain(ContentPackageRuntime.Current.Snapshot.Manifest.ContentVersion.ToString()));
            }
            else
            {
                Assert.That(inputOverlay.ContentTelemetryText, Does.Contain("未初始化"),
                    "Direct scene loading is valid without the application bootstrap; telemetry must report that state explicitly.");
            }
            Assert.That(Object.FindObjectOfType<PlayerInputReader>().HasInputOverlayAction, Is.True);
            Assert.That(Object.FindObjectOfType<PlayerInputReader>().HasHealAction, Is.True);
            Assert.That(Object.FindObjectOfType<PlayerCombatActor>(), Is.Not.Null);
            AssertAttachmentScaleAndWeaponBounds();
            Assert.That(
                Object.FindObjectOfType<PlayerCombatActor>()
                    .GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Any(material => material != null && material.name.Contains("M_Ranger_")),
                Is.True,
                "Player is not using the selected standard-proportion Ranger visual.");
            Assert.That(Object.FindObjectOfType<PlayerCombatActor>()
                .GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "Sword_Practice"), Is.False,
                "The selected Ranger must use the canonical project-owned sword, not the legacy practice sword.");
            Assert.That(Object.FindObjectOfType<PlayerInteractor>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<VoidExecutionVolume>(), Is.Not.Null);
            ForestSealTemplateCoordinator forestTemplate = Object.FindObjectOfType<ForestSealTemplateCoordinator>();
            Assert.That(forestTemplate, Is.Not.Null);
            Assert.That(forestTemplate.IsInitialized, Is.True);
            Assert.That(forestTemplate.Phase, Is.EqualTo(ForestSealPhase.Encounter));
            Assert.That(forestTemplate.SupplyCache.gameObject.activeSelf, Is.True);
            Assert.That(forestTemplate.SigilPickup.gameObject.activeSelf, Is.False);
            Assert.That(forestTemplate.EmberRune.gameObject.activeSelf, Is.False);
            GameObject artRoot = GameObject.Find("[Art] Ember Valley Baseline");
            Assert.That(artRoot, Is.Not.Null);
            Assert.That(artRoot.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThanOrEqualTo(60));
            GameObject importedArtRoot = GameObject.Find("[Art] M6 Imported Upgrade");
            Assert.That(importedArtRoot, Is.Not.Null);
            Assert.That(importedArtRoot.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThanOrEqualTo(10));
            Assert.That(Object.FindObjectsOfType<CameraOccluder>(), Has.Length.GreaterThanOrEqualTo(10));
            Assert.That(Object.FindObjectsOfType<M2RouteInteractable>(), Has.Length.EqualTo(6));
            Assert.That(Object.FindObjectsOfType<RouteEnrichmentInteractable>(), Has.Length.EqualTo(3));
            Assert.That(Object.FindObjectsOfType<BridgeMechanismInteractable>(), Has.Length.EqualTo(2));
            Assert.That(Object.FindObjectsOfType<MeleeEnemyActor>(), Has.Length.EqualTo(4));
            Assert.That(Object.FindObjectsOfType<RangedEnemyActor>(), Has.Length.EqualTo(4));
            Assert.That(Object.FindObjectsOfType<ShieldEnemyActor>(), Has.Length.EqualTo(2));
            WardenActor warden = Object.FindObjectOfType<WardenActor>();
            Assert.That(warden, Is.Not.Null);
            Assert.That(warden.name, Is.EqualTo("Enemy_EmberWarden"));
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "Shield_Warden_Equipped"), Is.True);
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "WardenAttackTelegraph"), Is.True);
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "WardenRuneCleaveTelegraph"), Is.True);
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "WardenPhaseTransitionRing"), Is.True);
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "Helmet_Closed"), Is.True);
            Assert.That(warden.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "ShoulderPads"), Is.True);
            Assert.That(warden.GetComponent<WardenAudioPresenter>().IsConfigured, Is.True);
            WardenChargeTrailPresenter chargeTrail = warden.GetComponent<WardenChargeTrailPresenter>();
            Assert.That(chargeTrail, Is.Not.Null);
            Assert.That(chargeTrail.IsConfigured, Is.True);
            Assert.That(chargeTrail.TrailCount, Is.EqualTo(2));
            WardenCombatVfxPresenter bossVfx = warden.GetComponent<WardenCombatVfxPresenter>();
            Assert.That(bossVfx, Is.Not.Null);
            Assert.That(bossVfx.IsConfigured, Is.True);
            Assert.That(bossVfx.PhaseParticleCount, Is.GreaterThan(0));
            Assert.That(bossVfx.RuneParticleCount, Is.GreaterThan(0));
            Assert.That(warden.DelayedBlastVfxConfigured, Is.True);
            CombatEncounterCoordinator[] combatGroups = Object.FindObjectsOfType<CombatEncounterCoordinator>();
            Assert.That(combatGroups, Has.Length.EqualTo(4));
            Assert.That(combatGroups.Single(group => group.name.Contains("Forest")).MeleeMemberCount, Is.EqualTo(1));
            CombatEncounterCoordinator courtyardGroup = combatGroups.Single(group => group.name.Contains("Courtyard"));
            Assert.That(courtyardGroup.MeleeMemberCount, Is.EqualTo(2));
            Assert.That(courtyardGroup.RangedMemberCount, Is.EqualTo(1));
            Assert.That(courtyardGroup.MaximumConcurrentMeleeAttackers, Is.EqualTo(2));
            Assert.That(courtyardGroup.SupportRadius, Is.GreaterThanOrEqualTo(4f));
            Assert.That(courtyardGroup.SupportArrivalDistance, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(
                courtyardGroup.TryCaptureEncounterTelemetry(out EncounterTelemetrySnapshot encounterTelemetry),
                Is.True);
            Assert.That(encounterTelemetry.EncounterLabel, Is.EqualTo("庭院"));
            TacticalPostureShrine postureShrine = Object.FindObjectOfType<TacticalPostureShrine>();
            Assert.That(postureShrine, Is.Not.Null);
            Assert.That(postureShrine.IsSpent, Is.False);
            Assert.That(postureShrine.WarningSegmentCount, Is.EqualTo(14));
            Assert.That(
                postureShrine.TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot tacticalTelemetry),
                Is.True);
            Assert.That(tacticalTelemetry.State, Is.EqualTo(TacticalTelemetryState.Ready));
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>())
            {
                Assert.That(
                    enemy.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials)
                        .Any(material => material != null && material.name.Contains("M_M6_FogwalkerBone")),
                    Is.True,
                    $"{enemy.name} is not using the selected M6 skeleton visual.");
                Assert.That(enemy.GetComponent<NavMeshAgent>().stoppingDistance,
                    Is.GreaterThanOrEqualTo(enemy.Definition.AttackRange * 0.9f));
            }
            ShieldEnemyActor shieldEnemy = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(item => item.name == "Enemy_RuinGuard_Courtyard");
            Assert.That(shieldEnemy.name, Is.EqualTo("Enemy_RuinGuard_Courtyard"));
            Assert.That(shieldEnemy.Definition.Id.Value, Is.EqualTo("enemy:ruin-guard-scorched"));
            Assert.That(shieldEnemy.Definition.ScorchedBurstEnabled, Is.True);
            Assert.That(shieldEnemy.HasSecondaryResource, Is.True);
            Assert.That(shieldEnemy.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "Shield_Wooden_Equipped"), Is.True);
            Assert.That(shieldEnemy.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "ScorchedCore"), Is.True);
            Assert.That(
                Object.FindObjectsOfType<RangedEnemyActor>().Single(enemy => enemy.name == "Enemy_RunePriest_Forest")
                    .GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Any(material => material != null && material.name.Contains("M_M6_RunePriest")),
                Is.True,
                "Rune priest is not using the selected M6 Wizard visual.");
            Assert.That(Object.FindObjectsOfType<NavMeshAgent>().All(agent => agent.isOnNavMesh), Is.True);
            Assert.That(
                Object.FindObjectsOfType<CameraOccluder>().Any(occluder => occluder.name.Contains("Wall")),
                Is.True,
                "Large art walls are not registered with the camera occlusion system.");
            Assert.That(GameObject.Find("GateBlocker_Sanctum"), Is.Not.Null);
            Assert.That(GameObject.Find("GateBlocker_ReturnShortcut"), Is.Not.Null);
            Assert.That(GameObject.Find("GateBlocker_ForestShortcut"), Is.Not.Null);
            Assert.That(Object.FindObjectsOfType<Transform>(true)
                .Any(item => item.name == "GateBlocker_WardenEncounter"), Is.True);
            Assert.That(GameObject.Find("CoverProxy_Forest_West").GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(GameObject.Find("CoverProxy_Forest_East").GetComponent<NavMeshObstacle>(), Is.Not.Null);
            Assert.That(GameObject.Find("ForestCoverRock_West"), Is.Not.Null);
            Assert.That(GameObject.Find("ValleyFloor_VisualOnly"), Is.Not.Null);
            Assert.That(GameObject.Find("ForestShoulder_West"), Is.Not.Null);
            Assert.That(GameObject.Find("CourtyardCliffShell"), Is.Not.Null);
            GameObject supportRoot = GameObject.Find("[Gameplay] Terrain Support Proxies");
            Assert.That(supportRoot, Is.Not.Null);
            Assert.That(supportRoot.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(8));
            Assert.That(supportRoot.GetComponentsInChildren<Renderer>(true), Is.Empty,
                "Gameplay support proxies must remain separate from presentation meshes.");
            Assert.That(GameObject.Find("Scout_QuestMarker"), Is.Not.Null);
            Assert.That(GameObject.Find("Seal_Forest_RuneCore"), Is.Not.Null);
            M2QuestHighlightPresenter[] routeHighlights =
                Object.FindObjectsOfType<M2QuestHighlightPresenter>();
            Assert.That(routeHighlights, Has.Length.EqualTo(11));
            Assert.That(routeHighlights.All(item => item.UsesPropertyBlocks), Is.True);
            BridgeMechanismGuidancePresenter guidance =
                Object.FindObjectOfType<BridgeMechanismGuidancePresenter>();
            Assert.That(guidance, Is.Not.Null);
            Assert.That(guidance.IsGuidanceVisible, Is.False);
            Assert.That(guidance.VisualRoot.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(3));
            Assert.That(guidance.VisualRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(GameObject.Find("Seal_Forest_HighlightRing"), Is.Not.Null);
            GameObject networkHighlightRing = GameObject.Find("Seal_Forest_HighlightRing");
            Assert.That(networkHighlightRing.GetComponent<MeshFilter>().sharedMesh.name,
                Is.EqualTo("M_NetworkObjectiveRing"));
            Assert.That(networkHighlightRing.GetComponent<Collider>(), Is.Null);
            Assert.That(networkHighlightRing.transform.Find("Seal_Forest_HighlightIcon"), Is.Not.Null);
            Assert.That(networkHighlightRing.transform.Find("Seal_Forest_HighlightIcon")
                .GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("M_NetworkObjectiveDiamond"));
            GameObject networkRouteHighlight = Object.FindObjectsOfType<Transform>(true)
                .Single(item => item.name == "NetworkSeal_Forest_HighlightRing")
                .gameObject;
            Assert.That(networkRouteHighlight, Is.Not.Null);
            Assert.That(networkRouteHighlight.GetComponent<Collider>(), Is.Null);
            Assert.That(networkRouteHighlight.GetComponent<MeshFilter>().sharedMesh.name,
                Is.EqualTo("M_NetworkObjectiveRing"));
            Assert.That(networkRouteHighlight.transform.Find("Seal_Forest_HighlightIcon"), Is.Not.Null);
            Assert.That(RenderSettings.ambientSkyColor.grayscale, Is.GreaterThanOrEqualTo(0.38f));
            Assert.That(RenderSettings.ambientGroundColor.grayscale, Is.GreaterThanOrEqualTo(0.11f));
            Assert.That(RenderSettings.fogStartDistance, Is.GreaterThanOrEqualTo(24f));
            Assert.That(RenderSettings.fogEndDistance, Is.GreaterThanOrEqualTo(70f));
            Assert.That(File.Exists(flow.SavePath), Is.True);

            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        private static void AssertAttachmentScaleAndWeaponBounds()
        {
            Transform[] sockets = Object.FindObjectsOfType<Transform>(true)
                .Where(item => item.name == "WeaponSocket_RightHand" ||
                               item.name == "ShieldSocket_LeftHand" ||
                               item.name == "StaffSocket_LeftHand")
                .ToArray();
            Assert.That(sockets, Is.Not.Empty);
            foreach (Transform attachmentSocket in sockets)
            {
                Vector3 scale = attachmentSocket.lossyScale;
                Assert.That(Mathf.Abs(scale.x), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
                Assert.That(Mathf.Abs(scale.y), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
                Assert.That(Mathf.Abs(scale.z), Is.EqualTo(1f).Within(0.04f), attachmentSocket.name);
            }

            Transform[] allTransforms = Object.FindObjectsOfType<Transform>(true);
            Assert.That(allTransforms.Any(item => item.name == "Sword_Practice"), Is.False,
                "M6 scenes must not retain the legacy practice-sword presentation.");

            Transform[] swords = allTransforms
                .Where(item => item.name.StartsWith("Sword_M6_"))
                .ToArray();
            Assert.That(swords, Is.Not.Empty, "No canonical M6 enemy sword was found.");
            Assert.That(swords.Any(item => item.name == "Sword_M6_Warden_Equipped"), Is.True,
                "The Warden must use the selected canonical rune sword.");
            foreach (Transform sword in swords)
            {
                Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                Assert.That(longest, Is.InRange(0.75f, 1.45f), sword.name);
            }

            Renderer[] shieldRenderers = allTransforms
                .Where(item => item.name == "Shield_Wooden_Equipped" ||
                               item.name == "Shield_Warden_Equipped")
                .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                .ToArray();
            Assert.That(shieldRenderers, Is.Not.Empty, "No canonical M6 shield was found.");
            Assert.That(shieldRenderers.SelectMany(renderer => renderer.sharedMaterials)
                .Any(material => material != null && material.name.StartsWith("M_M6_")), Is.True);
        }

        [UnityTest]
        public IEnumerator CourtyardPostureShrine_TelegraphsAndHitsBothSidesWithoutHealthDamage()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            TacticalPostureShrine shrine = Object.FindObjectOfType<TacticalPostureShrine>();
            MeleeEnemyActor melee = Object.FindObjectsOfType<MeleeEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_Fogwalker_Courtyard_Support");
            ShieldEnemyActor shield = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_RuinGuard_Courtyard");
            melee.enabled = false;
            shield.enabled = false;

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = shrine.transform.position + Vector3.forward * 1.4f;
            controller.enabled = true;
            melee.GetComponent<NavMeshAgent>().Warp(shrine.transform.position + Vector3.left * 2f);
            shield.GetComponent<NavMeshAgent>().Warp(shrine.transform.position + Vector3.right * 2f);
            Physics.SyncTransforms();

            float playerHealth = player.Model.Health.Current;
            float meleeHealth = melee.Brain.Health.Current;
            Assert.That(shrine.TryInteract(new InteractionContext(player)), Is.True);
            Assert.That(shrine.IsTelegraphing, Is.True);
            Assert.That(shrine.TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot armed), Is.True);
            Assert.That(armed.State, Is.EqualTo(TacticalTelemetryState.Telegraph));
            yield return new WaitForSeconds(1.2f);

            Assert.That(shrine.IsSpent, Is.True);
            Assert.That(shrine.TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot spent), Is.True);
            Assert.That(spent.State, Is.EqualTo(TacticalTelemetryState.Spent));
            Assert.That(shrine.LastAffectedCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(player.Model.Posture.Current, Is.LessThan(player.Model.Posture.Maximum));
            Assert.That(melee.Brain.Posture.Current, Is.LessThan(melee.Brain.Posture.Maximum));
            Assert.That(player.Model.Health.Current, Is.EqualTo(playerHealth));
            Assert.That(melee.Brain.Health.Current, Is.EqualTo(meleeHealth));
            Assert.That(shrine.TryInteract(new InteractionContext(player)), Is.False);

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_QuestInteractionsReachResultsAndPersist()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            PlayerInteractor playerInteractor = Object.FindObjectOfType<PlayerInteractor>();
            M2RouteInteractable[] interactions = Object.FindObjectsOfType<M2RouteInteractable>();
            var context = new InteractionContext(player);

            M2RouteInteractable scout = interactions.Single(item => item.Role == M2RouteRole.Scout);
            Assert.That(scout.TryInteract(context), Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.ActivateSeals));

            ForestSealTemplateCoordinator forest = flow.ForestTemplate;
            M2RouteInteractable forestSeal = interactions.Single(
                item => item.Role == M2RouteRole.Seal && item.StableId.Value == "seal:forest");
            Assert.That(forestSeal.TryInteract(context), Is.False,
                "The forest seal must reject interaction before the sigil encounter is solved.");

            MeleeEnemyActor bearer = Object.FindObjectsOfType<MeleeEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_Fogwalker_Forest");
            RangedEnemyActor priest = Object.FindObjectsOfType<RangedEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_RunePriest_Forest");
            Assert.That(bearer.ReceiveDamage(
                new DamageRequest(player.CombatantId, 701, 999f, 100f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.Encounter));
            Assert.That(priest.ReceiveDamage(
                new DamageRequest(player.CombatantId, 702, 999f, 100f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.SigilAvailable));
            Assert.That(forest.SigilPickup.gameObject.activeSelf, Is.True);
            Assert.That(forest.SigilPickup.TryInteract(context), Is.True);
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.SigilClaimed));
            Assert.That(forestSeal.TryInteract(context), Is.True);
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.RuneChoice));
            Assert.That(forest.EmberRune.TryInteract(context), Is.True);
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.Completed));
            Assert.That(player.ActiveRuneBlessing, Is.EqualTo(RuneBlessing.Ember));
            Assert.That(forest.GuardRune.gameObject.activeSelf, Is.False);
            Assert.That(forest.ShortcutBlocker.activeSelf, Is.False);
            Assert.That(forest.RestoredWorldRoot.activeSelf, Is.True);

            BridgeMechanismInteractable[] mechanisms =
                Object.FindObjectsOfType<BridgeMechanismInteractable>();
            BridgeMechanismInteractable mechanismA = mechanisms.Single(
                item => item.StableId == "bridge-mechanism:A");
            BridgeMechanismInteractable mechanismB = mechanisms.Single(
                item => item.StableId == "bridge-mechanism:B");
            BridgeMechanismGuidancePresenter guidance =
                Object.FindObjectOfType<BridgeMechanismGuidancePresenter>();
            Assert.That(mechanismA.HighlightState, Is.EqualTo(RouteHighlightState.Ready));
            Assert.That(mechanismB.HighlightState, Is.EqualTo(RouteHighlightState.Locked));
            Assert.That(mechanismB.PromptTextId.Value,
                Is.EqualTo("text:interaction.bridge-mechanism-b-locked"));
            Assert.That(mechanismA.UsesPropertyBlock, Is.True);
            Assert.That(mechanismB.UsesPropertyBlock, Is.True);
            Assert.That(flow.SealConditionChecklist, Does.Contain("前置机关 ✗"));
            Assert.That(mechanismA.TryInteract(context), Is.True);
            yield return null;
            Assert.That(mechanismA.HighlightState, Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(guidance.IsGuidanceVisible, Is.True);
            Assert.That(flow.SealConditionChecklist, Does.Contain("前置机关 ✓"));
            Assert.That(mechanismB.TryInteract(context), Is.False,
                "Bridge mechanism B must remain blocked until the bridge encounter is cleared.");

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = new Vector3(12f, 0.3f, 44f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            foreach (RangedEnemyActor bridgeEnemy in Object.FindObjectsOfType<RangedEnemyActor>()
                         .Where(enemy => enemy.name.Contains("Bridge")))
            {
                Assert.That(bridgeEnemy.ReceiveDamage(
                    new DamageRequest(player.CombatantId, 720 + bridgeEnemy.CombatantId, 999f, 100f, AttackTag.Heavy)).Killed,
                    Is.True);
            }
            yield return null;
            Assert.That(flow.BridgeEncounterCleared, Is.True);
            Assert.That(mechanismB.HighlightState, Is.EqualTo(RouteHighlightState.Ready));
            Assert.That(mechanismB.PromptTextId.Value,
                Is.EqualTo("text:interaction.bridge-mechanism-b"));
            Assert.That(mechanismB.TryInteract(context), Is.True);
            yield return null;
            Assert.That(mechanismB.HighlightState, Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(guidance.IsGuidanceVisible, Is.False);

            ShieldEnemyActor courtyardElite = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_RuinGuard_Courtyard");
            Assert.That(courtyardElite.ApplyNeutralPostureDamage(999f), Is.GreaterThan(0f));
            Assert.That(flow.CourtyardGuardBroken, Is.True);

            foreach (M2RouteInteractable seal in interactions.Where(
                         item => item.Role == M2RouteRole.Seal && item.StableId.Value != "seal:forest"))
            {
                Assert.That(seal.TryInteract(context), Is.True, seal.name);
            }
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.EnterSanctum));

            M2RouteInteractable checkpoint = interactions.Single(item => item.Role == M2RouteRole.Checkpoint);
            Assert.That(checkpoint.TryInteract(context), Is.True);
            Assert.That(player.ActiveCheckpointId.Value, Is.EqualTo("checkpoint:courtyard"));

            M2RouteInteractable gate = interactions.Single(item => item.Role == M2RouteRole.SanctumGate);
            player.transform.position = gate.InteractionTransform.position + new Vector3(0f, 0.3f, 1.25f);
            Physics.SyncTransforms();
            Assert.That(playerInteractor.CurrentCandidate, Is.SameAs(gate),
                "The sanctum gate control is not reachable through the real player interaction query.");
            Assert.That(playerInteractor.TryInteractNearest(), Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.DefeatWarden));
            Assert.That(GameObject.Find("GateBlocker_Sanctum"), Is.Null);
            Transform sanctumArch = Object.FindObjectsOfType<Transform>(true)
                .Single(item => item.name == "SanctumDungeonArch");
            Assert.That(sanctumArch.gameObject.activeInHierarchy, Is.False,
                "The M6 sanctum wall presentation must follow the authoritative gate blocker.");

            WardenActor warden = Object.FindObjectOfType<WardenActor>();
            yield return null;
            var routePath = new NavMeshPath();
            Assert.That(
                NavMesh.CalculatePath(player.transform.position, warden.transform.position, NavMesh.AllAreas, routePath),
                Is.True);
            Assert.That(routePath.status, Is.EqualTo(NavMeshPathStatus.PathComplete),
                "The authored camp-to-warden route is not fully connected after opening the sanctum gate.");

            controller.enabled = false;
            player.transform.position = warden.transform.position + Vector3.back * 5.5f;
            controller.enabled = true;
            Physics.SyncTransforms();
            float firstEncounterDeadline = Time.realtimeSinceStartup + 1f;
            while (!flow.IsWardenEncounterActive && Time.realtimeSinceStartup < firstEncounterDeadline)
                yield return null;
            M2RouteHud bossHud = Object.FindObjectOfType<M2RouteHud>();
            Assert.That(flow.IsWardenEncounterActive, Is.True);
            Assert.That(bossHud.ShouldShowBossHud, Is.True);
            Assert.That(GameObject.Find("GateBlocker_WardenEncounter"), Is.Not.Null);

            DamageResult playerKilled = player.ReceiveDamage(
                new DamageRequest(warden.CombatantId, 807, 999f, 100f, AttackTag.Heavy));
            Assert.That(playerKilled.Killed, Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.DefeatWarden));
            Assert.That(flow.IsWardenEncounterActive, Is.False);
            Assert.That(GameObject.Find("GateBlocker_WardenEncounter"), Is.Null);
            Assert.That(warden.Brain.Health.Current, Is.EqualTo(warden.Brain.Health.Maximum));
            yield return new WaitForSeconds(2.2f);

            controller.enabled = false;
            player.transform.position = warden.transform.position + Vector3.back * 5.5f;
            controller.enabled = true;
            Physics.SyncTransforms();
            float retryEncounterDeadline = Time.realtimeSinceStartup + 1f;
            while (!flow.IsWardenEncounterActive && Time.realtimeSinceStartup < retryEncounterDeadline)
                yield return null;
            Assert.That(flow.IsWardenEncounterActive, Is.True);

            DamageResult guardBreak = warden.ReceiveDamage(
                new DamageRequest(player.CombatantId, 808, 10f, 999f, AttackTag.Heavy));
            Assert.That(guardBreak.GuardBroken, Is.True);
            DamageResult phaseGate = warden.ReceiveDamage(
                new DamageRequest(player.CombatantId, 809, 999f, 100f, AttackTag.Heavy));
            Assert.That(phaseGate.Killed, Is.False);
            Assert.That(warden.Phase, Is.EqualTo(WardenPhase.Transition));
            Assert.That(warden.State, Is.EqualTo(WardenState.PhaseTransition));
            Assert.That(warden.Brain.Health.Normalized, Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(warden.ReceiveDamage(
                new DamageRequest(player.CombatantId, 810, 999f, 100f, AttackTag.Heavy)).Accepted, Is.False);
            yield return null;
            Assert.That(warden.GetComponent<WardenCombatVfxPresenter>().IsPhaseEffectActive, Is.True);
            controller.enabled = false;
            player.transform.position = warden.transform.position + (warden.transform.forward * 6f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(4.3f);
            Assert.That(warden.Phase, Is.EqualTo(WardenPhase.PhaseTwo));
            Assert.That(warden.GetComponent<WardenCombatVfxPresenter>().IsPhaseEffectActive, Is.False);
            Transform shield = warden.GetComponentsInChildren<Transform>(true)
                .Single(child => child.name == "Shield_Warden_Equipped");
            Assert.That(shield.gameObject.activeSelf, Is.False);

            RangedGroundRune bossBlast = null;
            float blastDeadline = Time.time + 2.5f;
            for (int frame = 0; frame < 10000 && bossBlast == null && Time.time < blastDeadline; frame++)
            {
                yield return null;
                bossBlast = Object.FindObjectsOfType<RangedGroundRune>()
                    .FirstOrDefault(rune => rune.name.StartsWith("WardenDelayedBlast_"));
            }
            Assert.That(bossBlast, Is.Not.Null,
                $"Phase two did not release its delayed-blast authority adapter. " +
                $"State={warden.State}, Attack={warden.Brain.CurrentAttack}, Event={warden.LastBossEvent}");
            Assert.That(bossBlast.WarningSegmentCount, Is.EqualTo(10));
            Assert.That(bossBlast.ImpactVfxConfigured, Is.True);

            DamageResult killed = warden.ReceiveDamage(
                new DamageRequest(player.CombatantId, 811, 999f, 100f, AttackTag.Heavy));
            Assert.That(killed.Killed, Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.ReturnToScout));
            yield return null;
            Assert.That(GameObject.Find("GateBlocker_ReturnShortcut"), Is.Null);

            Assert.That(scout.TryInteract(context), Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.Complete));
            Assert.That(flow.IsComplete, Is.True);
            Assert.That(File.Exists(flow.SavePath), Is.True);
            yield return null;
            M2RouteHud hud = Object.FindObjectOfType<M2RouteHud>();
            ThirdPersonCameraRig cameraRig = Object.FindObjectOfType<ThirdPersonCameraRig>();
            Assert.That(hud.IsCompletionPresented, Is.True);
            Assert.That(cameraRig.IsLookInputBlocked, Is.True);

            SaveLoadResult reloaded = new JsonSaveGameStore(flow.SavePath).LoadOrCreate(() => null);
            Assert.That(reloaded.Status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(reloaded.Save.mainQuest.ToSnapshot().Stage, Is.EqualTo(MainQuestStage.Complete));
            Assert.That(reloaded.Save.forestSeal.ToSnapshot().RuneChoice, Is.EqualTo(ForestRuneChoice.Ember));
            Assert.That(reloaded.Save.hasSealConditionProgress, Is.True);
            Assert.That(reloaded.Save.sealConditions.ToSnapshot().BridgeMechanismAActivated, Is.True);
            Assert.That(reloaded.Save.sealConditions.ToSnapshot().BridgeMechanismBActivated, Is.True);
            Assert.That(reloaded.Save.sealConditions.ToSnapshot().CourtyardGuardBroken, Is.True);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_ForestChoicePersistsAcrossContinueWithoutDuplicateRewards()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            var context = new InteractionContext(player);
            Object.FindObjectsOfType<M2RouteInteractable>()
                .Single(item => item.Role == M2RouteRole.Scout)
                .TryInteract(context);

            ForestSealTemplateCoordinator forest = flow.ForestTemplate;
            MeleeEnemyActor bearer = Object.FindObjectsOfType<MeleeEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_Fogwalker_Forest");
            RangedEnemyActor priest = Object.FindObjectsOfType<RangedEnemyActor>()
                .Single(enemy => enemy.name == "Enemy_RunePriest_Forest");
            bearer.ReceiveDamage(new DamageRequest(player.CombatantId, 711, 999f, 100f, AttackTag.Heavy));
            priest.ReceiveDamage(new DamageRequest(player.CombatantId, 712, 999f, 100f, AttackTag.Heavy));
            Assert.That(forest.SigilPickup.TryInteract(context), Is.True);
            M2RouteInteractable forestSeal = Object.FindObjectsOfType<M2RouteInteractable>()
                .Single(item => item.Role == M2RouteRole.Seal && item.StableId.Value == "seal:forest");
            Assert.That(forestSeal.TryInteract(context), Is.True);
            Assert.That(forest.GuardRune.TryInteract(context), Is.True);
            string savePath = flow.SavePath;

            M2LaunchIntent.RequestContinue();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            flow = Object.FindObjectOfType<M2RouteFlowController>();
            forest = flow.ForestTemplate;
            player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(flow.LoadStatus, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(forest.Phase, Is.EqualTo(ForestSealPhase.Completed));
            Assert.That(forest.RuneChoice, Is.EqualTo(ForestRuneChoice.Guard));
            Assert.That(player.ActiveRuneBlessing, Is.EqualTo(RuneBlessing.Guard));
            Assert.That(forest.SigilPickup.gameObject.activeSelf, Is.False);
            Assert.That(forest.EmberRune.gameObject.activeSelf, Is.False);
            Assert.That(forest.GuardRune.gameObject.activeSelf, Is.False);
            Assert.That(forest.RestoredWorldRoot.activeSelf, Is.True);
            Assert.That(forest.ShortcutBlocker.activeSelf, Is.False);

            new JsonSaveGameStore(savePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_WatchtowerAndRouteChoicePersistWithoutDuplicateCapacity()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            var context = new InteractionContext(player);
            Object.FindObjectsOfType<M2RouteInteractable>()
                .Single(item => item.Role == M2RouteRole.Scout)
                .TryInteract(context);

            RouteEnrichmentInteractable[] routeInteractions =
                Object.FindObjectsOfType<RouteEnrichmentInteractable>();
            RouteEnrichmentInteractable watchtower = routeInteractions.Single(
                item => item.Kind == RouteEnrichmentInteractionKind.Watchtower);
            RouteEnrichmentInteractable supply = routeInteractions.Single(
                item => item.Kind == RouteEnrichmentInteractionKind.SupplyRoute);
            RouteEnrichmentInteractable risk = routeInteractions.Single(
                item => item.Kind == RouteEnrichmentInteractionKind.RiskRoute);
            int baseCapacity = player.Model.HealingFlasks.MaximumCharges;

            Assert.That(watchtower.TryInteract(context), Is.True);
            yield return null;
            Assert.That(watchtower.HighlightState, Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(watchtower.UsesPropertyBlock, Is.True);
            Assert.That(player.Model.HealingFlasks.MaximumCharges, Is.EqualTo(baseCapacity + 1));
            Assert.That(watchtower.TryInteract(context), Is.False);
            Assert.That(supply.TryInteract(context), Is.True);
            yield return null;
            Assert.That(supply.HighlightState, Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(risk.HighlightState, Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(supply.UsesPropertyBlock, Is.True);
            Assert.That(risk.UsesPropertyBlock, Is.True);
            Assert.That(risk.IsAvailable, Is.False);
            yield return null;

            CombatEncounterCoordinator preSanctum = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "pre-sanctum-encounter");
            Assert.That(preSanctum.MaximumConcurrentMeleeAttackers, Is.EqualTo(1));
            string savePath = flow.SavePath;

            M2LaunchIntent.RequestContinue();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            flow = Object.FindObjectOfType<M2RouteFlowController>();
            player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(flow.WatchtowerDiscovered, Is.True);
            Assert.That(flow.RouteChoice, Is.EqualTo(EmberValleyRouteChoice.Supply));
            Assert.That(player.Model.HealingFlasks.MaximumCharges, Is.EqualTo(baseCapacity + 1));
            new JsonSaveGameStore(savePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_EncounterRoute_HasNoConcurrentNormalEncounterIntervalsAndAComesFirst()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            var context = new InteractionContext(player);
            Object.FindObjectsOfType<M2RouteInteractable>()
                .Single(item => item.Role == M2RouteRole.Scout)
                .TryInteract(context);
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()) enemy.enabled = false;
            foreach (RangedEnemyActor enemy in Object.FindObjectsOfType<RangedEnemyActor>()) enemy.enabled = false;
            foreach (ShieldEnemyActor enemy in Object.FindObjectsOfType<ShieldEnemyActor>()) enemy.enabled = false;

            CombatEncounterCoordinator[] coordinators = Object.FindObjectsOfType<CombatEncounterCoordinator>();
            Assert.That(coordinators.Select(item => item.TelemetrySegment), Is.EquivalentTo(new[]
            {
                "forest-encounter", "bridge-encounter", "courtyard-encounter", "pre-sanctum-encounter"
            }));
            var active = new HashSet<string>();
            var overlapErrors = new List<string>();
            foreach (CombatEncounterCoordinator coordinator in coordinators)
            {
                coordinator.EncounterStarted += started =>
                {
                    if (active.Count > 0)
                        overlapErrors.Add($"{started.TelemetrySegment} overlapped {string.Join(",", active)}");
                    active.Add(started.TelemetrySegment);
                };
                coordinator.EncounterCleared += cleared => active.Remove(cleared.TelemetrySegment);
                coordinator.EncounterReset += reset => active.Remove(reset.TelemetrySegment);
            }

            CombatEncounterCoordinator forest = coordinators.Single(item => item.TelemetrySegment == "forest-encounter");
            yield return WalkPlayerAlongNavMesh(player, forest.ArenaCenter);
            Assert.That(forest.IsTelemetryActive, Is.True);
            KillMelee(player, "Enemy_Fogwalker_Forest", 1200);
            KillRanged(player, "Enemy_RunePriest_Forest", 1201);
            yield return null;
            Assert.That(forest.IsTelemetryActive, Is.False);

            BridgeMechanismInteractable mechanismA = Object.FindObjectsOfType<BridgeMechanismInteractable>()
                .Single(item => item.StableId == "bridge-mechanism:A");
            Assert.That(Vector3.Distance(mechanismA.transform.position, new Vector3(8f, 0.4f, 43.4f)),
                Is.LessThan(0.001f));
            CombatEncounterCoordinator bridge = coordinators.Single(item => item.TelemetrySegment == "bridge-encounter");
            float bridgeEntryX = bridge.ArenaCenter.x - bridge.ArenaHalfExtents.x - bridge.TelemetryActivationMargin;
            Assert.That(mechanismA.transform.position.x, Is.LessThan(bridgeEntryX));
            yield return WalkPlayerAlongNavMesh(player, mechanismA.transform.position);
            Assert.That(bridge.IsTelemetryActive, Is.False,
                "Mechanism A must be reachable before the bridge encounter activates.");
            Assert.That(mechanismA.TryInteract(context), Is.True);
            Assert.That(flow.BridgeMechanismAActivated, Is.True);
            Assert.That(flow.BridgeEncounterCleared, Is.False);

            yield return WalkPlayerAlongNavMesh(player, bridge.ArenaCenter);
            Assert.That(bridge.IsTelemetryActive, Is.True);
            KillRanged(player, "Enemy_RunePriest_Bridge_Left", 1210);
            KillRanged(player, "Enemy_RunePriest_Bridge_Right", 1211);
            yield return null;
            Assert.That(bridge.IsTelemetryActive, Is.False);

            CombatEncounterCoordinator courtyard = coordinators.Single(item => item.TelemetrySegment == "courtyard-encounter");
            yield return WalkPlayerAlongNavMesh(player, courtyard.ArenaCenter);
            Assert.That(courtyard.IsTelemetryActive, Is.True);
            KillShield(player, "Enemy_RuinGuard_Courtyard", 1220);
            KillMelee(player, "Enemy_Fogwalker_Courtyard_Support", 1221);
            KillRanged(player, "Enemy_RunePriest_Courtyard", 1222);
            yield return null;
            Assert.That(courtyard.IsTelemetryActive, Is.False);

            CombatEncounterCoordinator preSanctum = coordinators.Single(item => item.TelemetrySegment == "pre-sanctum-encounter");
            yield return WalkPlayerAlongNavMesh(player, preSanctum.ArenaCenter);
            Assert.That(preSanctum.IsTelemetryActive, Is.True);
            KillMelee(player, "Enemy_Fogwalker_PreSanctum_Left", 1230);
            KillMelee(player, "Enemy_Fogwalker_PreSanctum_Right", 1231);
            KillShield(player, "Enemy_RuinGuard_PreSanctum", 1232);
            yield return null;
            Assert.That(preSanctum.IsTelemetryActive, Is.False);

            Assert.That(overlapErrors, Is.Empty);
            Assert.That(active, Is.Empty);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_ArenasAreExpandedSeparatedAndHaveVisibleFaces()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var baselineAreas = new Dictionary<string, float>
            {
                { "forest-encounter", 12.6f * 12f },
                { "bridge-encounter", 6.4f * 8.4f },
                { "courtyard-encounter", 15f * 12.4f },
                { "pre-sanctum-encounter", 8f * 6f }
            };
            CombatEncounterCoordinator[] arenas = Object.FindObjectsOfType<CombatEncounterCoordinator>();
            foreach (CombatEncounterCoordinator arena in arenas)
            {
                float authoredArea = arena.ArenaHalfExtents.x * 2f * arena.ArenaHalfExtents.y * 2f;
                Assert.That(authoredArea / baselineAreas[arena.TelemetrySegment],
                    Is.InRange(1.49f, 1.51f), arena.TelemetrySegment);
            }

            for (int left = 0; left < arenas.Length; left++)
            for (int right = left + 1; right < arenas.Length; right++)
            {
                Assert.That(ArenasOverlap(arenas[left], arenas[right]), Is.False,
                    $"{arenas[left].TelemetrySegment} overlaps {arenas[right].TelemetrySegment}");
            }

            EncounterBoundaryVisualMarker[] markers = Object.FindObjectsOfType<EncounterBoundaryVisualMarker>();
            Assert.That(markers.Length, Is.EqualTo(16));
            foreach (CombatEncounterCoordinator arena in arenas)
            {
                EncounterBoundaryVisualMarker[] faces = markers
                    .Where(marker => marker.Segment == arena.TelemetrySegment).ToArray();
                Assert.That(faces.Select(marker => marker.Face).Distinct().Count(), Is.EqualTo(4));
                foreach (EncounterBoundaryVisualMarker marker in faces)
                {
                    Assert.That(marker.VisibleRenderer, Is.Not.Null);
                    Assert.That(marker.VisibleRenderer.enabled, Is.True);
                    Bounds solid = marker.GetComponent<BoxCollider>().bounds;
                    Bounds visible = marker.VisibleRenderer.bounds;
                    Assert.That(Vector3.Distance(solid.center, visible.center), Is.LessThan(0.02f), marker.name);
                    Assert.That(Vector3.Distance(solid.size, visible.size), Is.LessThan(0.02f),
                        "A named renderer alone cannot prove that a wall is visible at collider scale: " + marker.name);
                    float faceDistance = marker.Face == EncounterBoundaryFace.North || marker.Face == EncounterBoundaryFace.South
                        ? Mathf.Abs(Mathf.Abs(marker.transform.position.z - arena.ArenaCenter.z) - arena.ArenaHalfExtents.y)
                        : Mathf.Abs(Mathf.Abs(marker.transform.position.x - arena.ArenaCenter.x) - arena.ArenaHalfExtents.x);
                    Assert.That(faceDistance, Is.LessThan(0.12f), $"{arena.TelemetrySegment}/{marker.Face}");
                }
            }
        }

        [UnityTest]
        public IEnumerator RangedEncounters_DoNotAcquireAcrossZones_AndRetreatStaysInside()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            player.GetComponent<ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>())
            {
                enemy.enabled = false;
                enemy.GetComponent<Collider>().enabled = false;
            }
            foreach (ShieldEnemyActor enemy in Object.FindObjectsOfType<ShieldEnemyActor>())
            {
                enemy.enabled = false;
                enemy.GetComponent<Collider>().enabled = false;
            }
            RangedEnemyActor[] priests = Object.FindObjectsOfType<RangedEnemyActor>()
                .Where(e => e.name.Contains("Forest") || e.name.Contains("Bridge")).ToArray();
            Assert.That(priests.Length, Is.EqualTo(3));
            RangedEnemyActor forest = priests.Single(e => e.name.Contains("Forest"));
            RangedEnemyActor[] bridge = priests.Where(e => e.name.Contains("Bridge")).ToArray();
            Vector3[] bridgeSpawns = bridge.Select(e => e.transform.position).ToArray();
            // Arrange an unobstructed close-range encounter, not a target hidden behind the east cover.
            forest.GetComponent<NavMeshAgent>().Warp(new Vector3(2f, 0f, 35f));
            player.transform.position = new Vector3(0f, 0.1f, 35f);
            forest.transform.rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(player.transform.position - forest.transform.position, Vector3.up));
            Vector3 forestStart = forest.transform.position;
            Physics.SyncTransforms();
            bool sawRetreat = false;
            for (float elapsed = 0f; elapsed < 3f; elapsed += Time.deltaTime)
            {
                yield return null;
                sawRetreat |= forest.State == RangedEnemyState.Retreat;
                foreach (RangedEnemyActor priest in priests)
                    Assert.That(priest.GetComponent<EncounterLeash>().Contains(priest.transform.position), Is.True, priest.name);
                for (int i = 0; i < bridge.Length; i++)
                {
                    Assert.That(bridge[i].Brain.AttackSequence, Is.Zero, "Bridge must not cast into the forest.");
                    Assert.That(Vector3.Distance(bridgeSpawns[i], bridge[i].transform.position), Is.LessThan(0.2f));
                }
            }
            Vector3 sight = player.AimPoint.position - forest.AimPoint.position;
            bool blocked = Physics.Raycast(forest.AimPoint.position, sight.normalized, out RaycastHit sightHit,
                sight.magnitude, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(sawRetreat, Is.True,
                $"Exercise real close-range retreat. State={forest.State} authority={forest.HasSimulationAuthority} " +
                $"enemy={forest.transform.position} player={player.transform.position} available={player.IsAvailable} " +
                $"scale={Time.timeScale} facing={forest.transform.forward} blocked={(blocked ? sightHit.collider.name : "none")}");
            Assert.That(Vector3.Distance(forestStart, forest.transform.position), Is.GreaterThan(0.2f),
                $"The leash must permit real retreat: state={forest.State}, destination={forest.GetComponent<NavMeshAgent>().destination}, " +
                $"path={forest.GetComponent<NavMeshAgent>().pathStatus}, velocity={forest.GetComponent<NavMeshAgent>().velocity}");
            forest.ReceiveDamage(new DamageRequest(player.CombatantId, 9411, 10f, 0f, AttackTag.Light));
            float health = forest.Brain.Health.Current;
            player.transform.position = new Vector3(13f, 0.1f, 44.8f);
            Physics.SyncTransforms();
            int sequence = forest.Brain.AttackSequence;
            for (float elapsed = 0f; elapsed < 3f; elapsed += Time.deltaTime)
            {
                yield return null;
                Assert.That(forest.GetComponent<EncounterLeash>().Contains(forest.transform.position), Is.True);
                Assert.That(forest.Brain.AttackSequence, Is.EqualTo(sequence), "Forest must disengage across zones.");
                Assert.That(forest.Brain.Health.Current, Is.EqualTo(health), "Returning must not heal or respawn.");
            }
            Assert.That(forest.State == RangedEnemyState.Return || forest.State == RangedEnemyState.Idle, Is.True);
            EncounterLeash leash = forest.GetComponent<EncounterLeash>();
            forest.ApplySweepImpulse(forest.transform.position + Vector3.left, 100f);
            Assert.That(leash.Contains(forest.transform.position), Is.True, "Sweep must not push an enemy out of its encounter.");
            new JsonSaveGameStore(Object.FindObjectOfType<M2RouteFlowController>().SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator DeathInActiveCourtyard_DoesNotRespawnClearedBridgeEnemies()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()) enemy.enabled = false;
            foreach (RangedEnemyActor enemy in Object.FindObjectsOfType<RangedEnemyActor>()) enemy.enabled = false;
            foreach (ShieldEnemyActor enemy in Object.FindObjectsOfType<ShieldEnemyActor>()) enemy.enabled = false;

            RangedEnemyActor bridgeLeft = GameObject.Find("Enemy_RunePriest_Bridge_Left")
                .GetComponent<RangedEnemyActor>();
            RangedEnemyActor bridgeRight = GameObject.Find("Enemy_RunePriest_Bridge_Right")
                .GetComponent<RangedEnemyActor>();
            CombatEncounterCoordinator bridge = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "bridge-encounter");
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = bridge.ArenaCenter + Vector3.up * 0.3f;
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(bridge.IsTelemetryActive, Is.True);

            Assert.That(bridgeLeft.ReceiveDamage(
                new DamageRequest(player.CombatantId, 8901, 9999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(bridgeRight.ReceiveDamage(
                new DamageRequest(player.CombatantId, 8902, 9999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            yield return null;
            Assert.That(bridgeLeft.IsAvailable, Is.False);
            Assert.That(bridgeRight.IsAvailable, Is.False);
            Assert.That(bridge.IsTelemetryActive, Is.False);

            CombatEncounterCoordinator courtyard = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "courtyard-encounter");
            controller.enabled = false;
            player.transform.position = courtyard.ArenaCenter + Vector3.up * 0.3f;
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(courtyard.IsTelemetryActive, Is.True);

            DamageResult death = player.ReceiveDamage(
                new DamageRequest(8900, 1, 9999f, 0f, AttackTag.Hazard));
            Assert.That(death.Killed, Is.True);
            yield return null;

            Assert.That(bridgeLeft.IsAvailable, Is.False,
                "A death in the courtyard must not reset an earlier cleared bridge encounter.");
            Assert.That(bridgeRight.IsAvailable, Is.False,
                "A death in the courtyard must not reset an earlier cleared bridge encounter.");
        }

        [UnityTest]
        public IEnumerator DeathAfterLeavingUnclearedBridge_DoesNotRespawnItsDefeatedMember()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()) enemy.enabled = false;
            foreach (RangedEnemyActor enemy in Object.FindObjectsOfType<RangedEnemyActor>()) enemy.enabled = false;
            foreach (ShieldEnemyActor enemy in Object.FindObjectsOfType<ShieldEnemyActor>()) enemy.enabled = false;

            RangedEnemyActor bridgeLeft = GameObject.Find("Enemy_RunePriest_Bridge_Left")
                .GetComponent<RangedEnemyActor>();
            RangedEnemyActor bridgeRight = GameObject.Find("Enemy_RunePriest_Bridge_Right")
                .GetComponent<RangedEnemyActor>();
            CombatEncounterCoordinator bridge = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "bridge-encounter");
            CombatEncounterCoordinator courtyard = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "courtyard-encounter");
            CharacterController controller = player.GetComponent<CharacterController>();

            controller.enabled = false;
            player.transform.position = bridge.ArenaCenter + Vector3.up * 0.3f;
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(bridge.IsTelemetryActive, Is.True);

            Assert.That(bridgeLeft.ReceiveDamage(
                new DamageRequest(player.CombatantId, 8911, 9999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(bridgeRight.IsAvailable, Is.True,
                "The bridge encounter must remain unfinished for this regression case.");

            controller.enabled = false;
            player.transform.position = courtyard.ArenaCenter + Vector3.up * 0.3f;
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(courtyard.IsTelemetryActive, Is.True);

            DamageResult death = player.ReceiveDamage(
                new DamageRequest(8910, 1, 9999f, 0f, AttackTag.Hazard));
            Assert.That(death.Killed, Is.True);
            yield return null;

            Assert.That(bridge.IsTelemetryActive, Is.False,
                "Leaving an unfinished arena must abandon its active telemetry interval on a later death.");
            Assert.That(bridgeLeft.IsAvailable, Is.False,
                "A defeated member of an abandoned earlier encounter must not respawn after a remote death.");
            Assert.That(bridgeRight.IsAvailable, Is.True,
                "An undefeated member must keep its existing state rather than receive a remote reset.");
        }

        [UnityTest]
        public IEnumerator EmberValley_NewOfflineEncounters_EnterAndClearAlongNavMeshRoute()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()) enemy.enabled = false;
            foreach (RangedEnemyActor enemy in Object.FindObjectsOfType<RangedEnemyActor>()) enemy.enabled = false;
            foreach (ShieldEnemyActor enemy in Object.FindObjectsOfType<ShieldEnemyActor>()) enemy.enabled = false;

            CombatEncounterCoordinator bridge = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "bridge-encounter");
            CombatEncounterCoordinator preSanctum = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "pre-sanctum-encounter");
            bool bridgeEntered = false;
            bool bridgeCleared = false;
            bool preSanctumEntered = false;
            bool preSanctumCleared = false;
            bridge.EncounterStarted += _ => bridgeEntered = true;
            bridge.EncounterCleared += _ => bridgeCleared = true;
            preSanctum.EncounterStarted += _ => preSanctumEntered = true;
            preSanctum.EncounterCleared += _ => preSanctumCleared = true;

            yield return WalkPlayerAlongNavMesh(player, new Vector3(12.4f, 0f, 44.8f));
            Assert.That(bridgeEntered, Is.True,
                "Walking the formal offline route did not enter bridge-encounter.");
            foreach (RangedEnemyActor enemy in Object.FindObjectsOfType<RangedEnemyActor>()
                         .Where(item => item.name.Contains("Bridge")))
            {
                enemy.ReceiveDamage(new DamageRequest(
                    player.CombatantId, 900 + enemy.CombatantId, 999f, 100f, AttackTag.Heavy));
            }
            yield return null;
            Assert.That(bridgeCleared, Is.True,
                "Clearing the authored bridge roster did not emit bridge-encounter cleared.");
            Assert.That(flow.BridgeEncounterCleared, Is.True);

            yield return WalkPlayerAlongNavMesh(player, new Vector3(39f, 0f, 31f));
            Assert.That(preSanctumEntered, Is.True,
                "Walking the formal offline route did not enter pre-sanctum-encounter.");
            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()
                         .Where(item => item.name.Contains("PreSanctum")))
            {
                enemy.ReceiveDamage(new DamageRequest(
                    player.CombatantId, 940 + enemy.CombatantId, 999f, 100f, AttackTag.Heavy));
            }
            ShieldEnemyActor guard = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(item => item.name == "Enemy_RuinGuard_PreSanctum");
            guard.ApplyNeutralPostureDamage(999f);
            guard.ReceiveDamage(new DamageRequest(player.CombatantId, 999, 999f, 100f, AttackTag.Heavy));
            yield return null;
            Assert.That(preSanctumCleared, Is.True,
                "Clearing the authored pre-sanctum roster did not emit pre-sanctum-encounter cleared.");

            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_RiskRouteReward_IsGrantedOnceAndPersists()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            var context = new InteractionContext(player);
            Object.FindObjectsOfType<M2RouteInteractable>()
                .Single(item => item.Role == M2RouteRole.Scout)
                .TryInteract(context);
            RouteEnrichmentInteractable risk = Object.FindObjectsOfType<RouteEnrichmentInteractable>()
                .Single(item => item.Kind == RouteEnrichmentInteractionKind.RiskRoute);
            int baseCapacity = player.Model.HealingFlasks.MaximumCharges;
            Assert.That(risk.TryInteract(context), Is.True);
            yield return null;

            CombatEncounterCoordinator preSanctum = Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .Single(item => item.TelemetrySegment == "pre-sanctum-encounter");
            Assert.That(preSanctum.MaximumConcurrentMeleeAttackers, Is.EqualTo(2));
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = new Vector3(39f, 0.3f, 30f);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;

            foreach (MeleeEnemyActor enemy in Object.FindObjectsOfType<MeleeEnemyActor>()
                         .Where(item => item.name.Contains("PreSanctum")))
            {
                enemy.ReceiveDamage(new DamageRequest(
                    player.CombatantId, 1010 + enemy.CombatantId, 999f, 100f, AttackTag.Heavy));
            }
            ShieldEnemyActor guard = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(item => item.name == "Enemy_RuinGuard_PreSanctum");
            guard.ApplyNeutralPostureDamage(999f);
            guard.ReceiveDamage(new DamageRequest(player.CombatantId, 1099, 999f, 100f, AttackTag.Heavy));
            yield return null;

            Assert.That(flow.RiskRouteRewardClaimed, Is.True);
            Assert.That(player.Model.HealingFlasks.MaximumCharges, Is.EqualTo(baseCapacity + 1));
            string savePath = flow.SavePath;

            M2LaunchIntent.RequestContinue();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return null;

            flow = Object.FindObjectOfType<M2RouteFlowController>();
            player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(flow.RiskRouteRewardClaimed, Is.True);
            Assert.That(player.Model.HealingFlasks.MaximumCharges, Is.EqualTo(baseCapacity + 1));
            new JsonSaveGameStore(savePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator EmberValley_SessionElapsedTimePersistsAcrossContinue()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSecondsRealtime(0.15f);

            M2RouteFlowController firstSession = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(firstSession.IsInitialized, Is.True);
            firstSession.SaveSessionProgress();
            float savedElapsed = firstSession.SessionElapsedSeconds;
            string savePath = firstSession.SavePath;
            Assert.That(savedElapsed, Is.GreaterThan(0.05f));

            M2LaunchIntent.RequestContinue();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;

            M2RouteFlowController continuedSession = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(continuedSession.LoadStatus, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(continuedSession.SessionElapsedSeconds, Is.GreaterThanOrEqualTo(savedElapsed));
            new JsonSaveGameStore(savePath).DeleteAllRevisions();
        }

        private static IEnumerator WalkPlayerAlongNavMesh(PlayerCombatActor player, Vector3 destination)
        {
            Assert.That(
                NavMesh.SamplePosition(destination, out NavMeshHit sampledDestination, 3.5f, NavMesh.AllAreas),
                Is.True,
                $"Could not sample an authored route point near {destination}.");
            var path = new NavMeshPath();
            Assert.That(
                NavMesh.CalculatePath(player.transform.position, sampledDestination.position, NavMesh.AllAreas, path),
                Is.True,
                $"Could not calculate authored route to {destination} (sampled {sampledDestination.position}).");
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));

            ThirdPersonMotor motor = player.GetComponent<ThirdPersonMotor>();
            CharacterController controller = player.GetComponent<CharacterController>();
            bool motorWasEnabled = motor != null && motor.enabled;
            if (motor != null) motor.enabled = false;
            controller.enabled = false;
            for (int cornerIndex = 1; cornerIndex < path.corners.Length; cornerIndex++)
            {
                Vector3 corner = path.corners[cornerIndex];
                int frameBudget = 200;
                while (Vector3.Distance(player.transform.position, corner) > 0.2f && frameBudget-- > 0)
                {
                    player.transform.position = Vector3.MoveTowards(
                        player.transform.position,
                        corner,
                        0.65f);
                    Physics.SyncTransforms();
                    yield return null;
                }

                Assert.That(frameBudget, Is.GreaterThan(0), $"Route traversal stalled before corner {cornerIndex}.");
            }

            controller.enabled = true;
            if (motor != null) motor.enabled = motorWasEnabled;
            Physics.SyncTransforms();
            yield return null;
        }

        private static bool ArenasOverlap(CombatEncounterCoordinator left, CombatEncounterCoordinator right)
        {
            float leftX = left.ArenaHalfExtents.x + left.TelemetryActivationMargin;
            float leftZ = left.ArenaHalfExtents.y + left.TelemetryActivationMargin;
            float rightX = right.ArenaHalfExtents.x + right.TelemetryActivationMargin;
            float rightZ = right.ArenaHalfExtents.y + right.TelemetryActivationMargin;
            return Mathf.Abs(left.ArenaCenter.x - right.ArenaCenter.x) < leftX + rightX &&
                   Mathf.Abs(left.ArenaCenter.z - right.ArenaCenter.z) < leftZ + rightZ;
        }

        private static void KillMelee(PlayerCombatActor player, string name, int sequence)
        {
            MeleeEnemyActor enemy = Object.FindObjectsOfType<MeleeEnemyActor>()
                .Single(item => item.name == name);
            Assert.That(enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, sequence, 999f, 100f, AttackTag.Heavy)).Killed, Is.True, name);
        }

        private static void KillRanged(PlayerCombatActor player, string name, int sequence)
        {
            RangedEnemyActor enemy = Object.FindObjectsOfType<RangedEnemyActor>()
                .Single(item => item.name == name);
            Assert.That(enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, sequence, 999f, 100f, AttackTag.Heavy)).Killed, Is.True, name);
        }

        private static void KillShield(PlayerCombatActor player, string name, int sequence)
        {
            ShieldEnemyActor enemy = Object.FindObjectsOfType<ShieldEnemyActor>()
                .Single(item => item.name == name);
            enemy.ApplyNeutralPostureDamage(999f);
            Assert.That(enemy.ReceiveDamage(
                new DamageRequest(player.CombatantId, sequence, 999f, 100f, AttackTag.Heavy)).Killed, Is.True, name);
        }
    }
}
