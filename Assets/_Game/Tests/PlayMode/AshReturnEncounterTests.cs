#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Return combination integration. Controlled story setup is not a natural Boss playthrough.</summary>
    public sealed class AshReturnEncounterTests
    {
        GameObject root;
        CombatEncounterCoordinator group;
        SummonerEnemyActor caller;
        RangedEnemyActor priest;
        PlayerCombatActor player;
        M2RouteFlowController flow;
        Gamepad pad;
        bool ownHost;

        [UnitySetUp] public IEnumerator Begin()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null; yield return null;
            flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
            player = Object.FindObjectOfType<PlayerCombatActor>();
            root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshReturn_v1");
            group = root.GetComponent<CombatEncounterCoordinator>();
            caller = root.GetComponentInChildren<SummonerEnemyActor>(true);
            priest = root.GetComponentInChildren<RangedEnemyActor>(true);
            Assert.That(root.activeSelf, Is.False);
        }
        [UnityTearDown] public IEnumerator End()
        {
            if (pad != null && pad.added) { InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.RemoveDevice(pad); }
            pad = null; yield return null;
            if (ownHost) { SessionRuntime.Current.Shutdown(); ownHost = false; yield return null; }
        }

        [UnityTest] public IEnumerator BeforeBoss_InactiveRootHasNoAiThreatOrSummonBeat()
        {
            int beats = 0;
            UnityEngine.Application.LogCallback observer = (message, stack, type) =>
            { if (message.StartsWith(PacingTelemetryRecorder.LogPrefix) && message.Contains("segment=encounter_ash-return event=first-summon")) beats++; };
            UnityEngine.Application.logMessageReceived += observer;
            try
            {
                Place(new Vector3(23, 0, 10.2f));
                yield return new WaitForSeconds(2f);
                Assert.That(caller.Brain, Is.Null); Assert.That(priest.Brain, Is.Null);
                Assert.That(group.IsTelemetryActive, Is.False); Assert.That(root.activeSelf, Is.False);
                Assert.That(caller.LivingEntityCount, Is.Zero); Assert.That(beats, Is.Zero);
                var hud = Object.FindObjectOfType<Emberfall.UI.M2RouteHud>();
                Assert.That(hud.ObservedLivingSummonCount, Is.Zero); Assert.That(hud.OffscreenThreatCount, Is.Zero);
                Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>(true).Length, Is.EqualTo(7));
                Assert.That(group.SummonerMemberCount, Is.EqualTo(1)); Assert.That(group.RangedMemberCount, Is.EqualTo(1));
                var roots = new SerializedObject(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true)).FindProperty("_offlineActorRoots");
                Assert.That(Enumerable.Range(0, roots.arraySize).Select(i => roots.GetArrayElementAtIndex(i).objectReferenceValue), Does.Contain(root));
            }
            finally { UnityEngine.Application.logMessageReceived -= observer; }
        }

        [UnityTest] public IEnumerator BossDeath_AlreadyInsideIsDetectedAndDepartureDoesNotRecreateMembers()
        {
            Place(new Vector3(27.5f, 0, 10.2f));
            yield return DefeatBossForIntegration(); yield return null;
            Assert.That(root.activeSelf, Is.True); Assert.That(group.IsTelemetryActive, Is.True);
            Assert.That(flow.AshReturnActivationPending, Is.False);
            Assert.That(caller.EncounterId, Is.EqualTo("encounter:ash-return"));
            Assert.That(priest.Definition.Id.Value, Is.EqualTo("enemy:rune-priest"));
            foreach (var agent in root.GetComponentsInChildren<NavMeshAgent>()) Assert.That(agent.isOnNavMesh, Is.True, agent.name);
            Assert.That(group.EndsAttemptOnExit, Is.True);
            int ownerId = caller.GetInstanceID(), priestId = priest.GetInstanceID();
            caller.SetSimulationAuthority(false); priest.SetSimulationAuthority(false);
            Place(new Vector3(30.3f, 0, 10.2f)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.False); Assert.That(root.activeSelf, Is.True);
            Place(new Vector3(27.5f, 0, 10.2f)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.True);
            Assert.That(caller.GetInstanceID(), Is.EqualTo(ownerId)); Assert.That(priest.GetInstanceID(), Is.EqualTo(priestId));
            Assert.That(root.GetComponentsInChildren<SummonerEnemyActor>().Length, Is.EqualTo(1));
            Assert.That(flow.AshReturnCleared, Is.False);
        }

        [UnityTest] public IEnumerator SpawnProximity_DefersOnlyActivationUntilThePlayerLeavesFixedPoints()
        {
            Vector3 callerSpawn = caller.transform.position, priestSpawn = priest.transform.position;
            Place(callerSpawn + Vector3.right * .5f);
            yield return DefeatBossForIntegration(); yield return null;
            Assert.That(flow.AshReturnActivationPending, Is.True); Assert.That(root.activeSelf, Is.False);
            Assert.That(caller.Brain, Is.Null); Assert.That(priest.Brain, Is.Null);
            Assert.That(caller.transform.position, Is.EqualTo(callerSpawn)); Assert.That(priest.transform.position, Is.EqualTo(priestSpawn));
            Assert.That(group.IsTelemetryActive, Is.False); Assert.That(flow.AshReturnCleared, Is.False);
            Place(new Vector3(27, 0, 10.2f)); yield return null; yield return null;
            Assert.That(root.activeSelf, Is.True); Assert.That(flow.AshReturnActivationPending, Is.False);
            Assert.That(group.IsTelemetryActive, Is.True, "Coordinator polls current inclusion, no enter-trigger/reentry required.");
        }

        [UnityTest] public IEnumerator ActualSummonAndCleanup_RequirePriestDeathAndPersistOnlyReturnClear()
        {
            Place(new Vector3(28, 0, 10.2f));
            pad = InputSystem.AddDevice<Gamepad>();
            float aimUntil = Time.time + 2f;
            while (Vector3.Dot(Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized, Vector3.left) < .93f && Time.time < aimUntil)
            { InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(-.5f, 0) }); yield return null; }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized, Vector3.left), Is.GreaterThan(.9f));
            int beats = 0;
            UnityEngine.Application.LogCallback observer = (message, stack, type) =>
            { if (message.StartsWith(PacingTelemetryRecorder.LogPrefix) && message.Contains("segment=encounter_ash-return event=first-summon")) beats++; };
            UnityEngine.Application.logMessageReceived += observer;
            try
            {
                yield return DefeatBossForIntegration();
                // Isolate owner lifecycle/readability, preserving the actual visible blue priest.
                priest.SetSimulationAuthority(false);
                yield return WaitForActualSummon(); Assert.That(beats, Is.EqualTo(1));
                var minion = caller.GetLivingEntity(0); Assert.That(minion, Is.Not.Null);
                minion.ApplyNeutralPostureDamage(999f); yield return null;
                var hud = Object.FindObjectOfType<Emberfall.UI.M2RouteHud>();
                Assert.That(hud.ObservedLivingSummonCount, Is.EqualTo(1)); Assert.That(hud.SummonExecutionReadyCount, Is.EqualTo(1));
                string folder = Path.GetFullPath("Builds/ArtReview/AshReturn/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(folder); yield return null; yield return null;
                ScreenCapture.CaptureScreenshot(folder + "/formal-return-combination-hud.png");
                float captureUntil = Time.realtimeSinceStartup + 4f;
                while (!File.Exists(folder + "/formal-return-combination-hud.png") && Time.realtimeSinceStartup < captureUntil) yield return null;
                Assert.That(File.Exists(folder + "/formal-return-combination-hud.png"), Is.True);
                caller.ResetToSpawn(); yield return WaitForActualSummon(); Assert.That(beats, Is.EqualTo(1), "Retry casts never multiply content beats.");
                var retryMinion = caller.GetLivingEntity(0); Kill(caller, 30);
                Assert.That(retryMinion.IsAvailable, Is.False); Assert.That(caller.GetLivingEntity(0), Is.Null);
                yield return null; Assert.That(hud.ObservedLivingSummonCount, Is.Zero);
                Assert.That(flow.AshReturnCleared, Is.False); Assert.That(priest.IsAvailable, Is.True);
                priest.SetSimulationAuthority(true); Kill(priest, 31); yield return null;
                Assert.That(flow.AshReturnCleared, Is.True); Assert.That(root.activeSelf, Is.False);
                int clearFrame = Time.frameCount;
                yield return new WaitForEndOfFrame();
                Assert.That(Time.frameCount, Is.EqualTo(clearFrame), "Return clear banner must draw in the same published-clear frame.");
                var banner = Object.FindObjectsOfType<Emberfall.UI.M2EncounterBannerPresenter>(true)
                    .Single(p => p.Encounter == Emberfall.UI.AshEncounterBanner.Return);
                Assert.That(banner.gameObject.activeInHierarchy, Is.True, "Saved return clear decor is not owned by the deactivated actor root.");
                Assert.That(banner.IsBannerVisible, Is.True);
                Assert.That(banner.GetComponentsInChildren<Renderer>().All(r => r.enabled), Is.True);
                Assert.That(flow.AshApproachCleared, Is.False); Assert.That(flow.AshGuardPassCleared, Is.False);
                M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
                var restored = Object.FindObjectOfType<M2RouteFlowController>();
                Assert.That(restored.AshReturnCleared, Is.True); Assert.That(restored.AshApproachCleared, Is.False); Assert.That(restored.AshGuardPassCleared, Is.False);
                Assert.That(Object.FindObjectsOfType<Emberfall.UI.M2EncounterBannerPresenter>(true)
                    .Single(p => p.Encounter == Emberfall.UI.AshEncounterBanner.Return).IsBannerVisible, Is.True);
                Assert.That(SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshReturn_v1").activeSelf, Is.False);
                Assert.That(caller == null, Is.True, "Old scene reference must be destroyed, not cached across a reload.");
                Assert.That(Object.FindObjectOfType<Emberfall.UI.M2RouteHud>().ObservedLivingSummonCount, Is.Zero);
            }
            finally { UnityEngine.Application.logMessageReceived -= observer; }
        }

        [UnityTest] public IEnumerator NormalReturnMovement_CanCrossAndLeaveWithoutOverlappingAnEncounter()
        {
            Place(new Vector3(31.5f, 0, 10.2f)); yield return DefeatBossForIntegration();
            pad = InputSystem.AddDevice<Gamepad>();
            yield return Walk(new Vector3(27.5f, 0, 10.2f)); Assert.That(group.IsTelemetryActive, Is.True);
            yield return Walk(new Vector3(15.3f, 0, 10.2f)); Assert.That(group.IsTelemetryActive, Is.False);
            Assert.That(flow.DeathCount, Is.Zero); Assert.That(flow.AshReturnCleared, Is.False);
            Assert.That(caller.IsAvailable, Is.True); Assert.That(priest.IsAvailable, Is.True);
            Assert.That(caller.GetComponent<EncounterLeash>().Contains(caller.transform.position), Is.True);
            Assert.That(priest.GetComponent<EncounterLeash>().Contains(priest.transform.position), Is.True);
        }

        [UnityTest] public IEnumerator ReloadWhileSummonsExist_ClearsOldSceneAndStartsAnInactiveUnclearedReturn()
        {
            Place(new Vector3(28, 0, 10.2f)); yield return DefeatBossForIntegration();
            priest.SetSimulationAuthority(false); yield return WaitForActualSummon();
            var old = caller;
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            Assert.That(old == null, Is.True);
            Assert.That(Object.FindObjectsOfType<MeleeEnemyActor>().Any(m => m.name == "Summoned_Fogwalker"), Is.False);
            var newRoot = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshReturn_v1");
            Assert.That(newRoot.activeSelf, Is.False); Assert.That(newRoot.GetComponentInChildren<SummonerEnemyActor>(true).Brain, Is.Null);
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().AshReturnCleared, Is.False);
            Assert.That(Object.FindObjectOfType<Emberfall.UI.M2RouteHud>().ObservedLivingSummonCount, Is.Zero);
        }

        [UnityTest] public IEnumerator ListeningHost_DisablesReturnControllerAndBothActorsBeforeOfflineAwake()
        {
            var service = SessionRuntime.Current; service.Shutdown(); yield return null;
            Assert.That(service.StartHost(47792), Is.True, service.Snapshot.Message); ownHost = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var network = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true); Assert.That(network.NetworkModeActive, Is.True);
            var netRoot = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshReturn_v1");
            Assert.That(netRoot.activeSelf, Is.False);
            Assert.That(netRoot.GetComponentInChildren<SummonerEnemyActor>(true).Brain, Is.Null);
            Assert.That(netRoot.GetComponentInChildren<RangedEnemyActor>(true).Brain, Is.Null);
            Assert.That(netRoot.GetComponentsInChildren<Behaviour>(true).All(b => !b.enabled), Is.True, "Initially inactive is not sufficient proof of NET isolation.");
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>(true).enabled, Is.False);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
        }

        IEnumerator DefeatBossForIntegration()
        {
            // Controlled public story/actor setup, not natural route/Boss combat. Preserve valid
            // forest/bridge/courtyard save facts instead of changing only the main quest domain.
            Vector3 returnFeet = player.NavigationFootPosition;
            var context = new InteractionContext(player);
            Assert.That(flow.TryTalkToScout(), Is.True);
            Kill(Object.FindObjectsOfType<MeleeEnemyActor>().Single(e => e.name == "Enemy_Fogwalker_Forest"), 101);
            Kill(Object.FindObjectsOfType<RangedEnemyActor>().Single(e => e.name == "Enemy_RunePriest_Forest"), 102);
            Assert.That(flow.ForestTemplate.SigilPickup.TryInteract(context), Is.True);
            Assert.That(flow.TryActivateSeal(new ContentId("seal:forest")), Is.True);
            Assert.That(flow.ForestTemplate.EmberRune.TryInteract(context), Is.True);
            Assert.That(flow.TryActivateBridgeMechanism("bridge-mechanism:A"), Is.True);
            Place(new Vector3(12f, 0, 44f)); yield return null;
            foreach (var member in Object.FindObjectsOfType<RangedEnemyActor>().Where(e => e.name.Contains("Bridge"))) Kill(member, 103 + member.CombatantId);
            yield return null;
            Assert.That(flow.BridgeEncounterCleared, Is.True);
            Assert.That(flow.TryActivateBridgeMechanism("bridge-mechanism:B"), Is.True);
            Assert.That(flow.TryActivateSeal(new ContentId("seal:bridge")), Is.True);
            Assert.That(Object.FindObjectsOfType<ShieldEnemyActor>().Single(e => e.name == "Enemy_RuinGuard_Courtyard").ApplyNeutralPostureDamage(999f), Is.GreaterThan(0));
            Assert.That(flow.TryActivateSeal(new ContentId("seal:courtyard")), Is.True);
            Assert.That(flow.TryEnterSanctum(), Is.True);
            Place(flow.Warden.transform.position + Vector3.back * 5.5f);
            float engageUntil = Time.time + 1f;
            while (!flow.IsWardenEncounterActive && Time.time < engageUntil) yield return null;
            Assert.That(flow.IsWardenEncounterActive, Is.True, "Dormant Boss rejects damage; use real perception before setup hits.");
            Assert.That(flow.Warden.ApplyNeutralPostureDamage(999f), Is.GreaterThan(0));
            Assert.That(flow.Warden.ReceiveDamage(new DamageRequest(player.CombatantId, 10, 9999f, 100f, AttackTag.Heavy)).Killed, Is.False);
            Assert.That(flow.Warden.Phase, Is.EqualTo(WardenPhase.Transition));
            float deadline = Time.time + 6f;
            while (flow.Warden.Phase == WardenPhase.Transition && Time.time < deadline) yield return null;
            Assert.That(flow.Warden.Phase, Is.EqualTo(WardenPhase.PhaseTwo));
            // Be at the requested return point at the actual death callback, including inside/
            // spawn-proximity cases; these placements are setup, never traversal evidence.
            Place(returnFeet);
            Assert.That(flow.Warden.ReceiveDamage(new DamageRequest(player.CombatantId, 11, 9999f, 100f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.ReturnToScout)); yield return null;
        }
        IEnumerator WaitForActualSummon()
        {
            float deadline = Time.time + 12f;
            while (caller.LivingEntityCount == 0 && Time.time < deadline) { Assert.That(player.IsAvailable, Is.True); yield return null; }
            Assert.That(caller.LivingEntityCount, Is.EqualTo(1), $"state={caller.Brain.State} active={group.IsTelemetryActive} owner={caller.transform.position} player={player.NavigationFootPosition} reachable={caller.RetreatBestReachableDistance}");
        }
        void Place(Vector3 feet)
        {
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            var body = player.GetComponent<CharacterController>(); body.enabled = false;
            player.transform.position = feet + offset + Vector3.up * .04f; body.enabled = true;
            Physics.SyncTransforms(); Assert.That(Mathf.Abs(player.NavigationFootPosition.y - feet.y), Is.LessThan(.05f));
        }
        static void Kill(CombatTarget target, int sequence) => Assert.That(target.ReceiveDamage(new DamageRequest(989, sequence, 9999f, 100f, AttackTag.Heavy)).Killed, Is.True);
        IEnumerator Walk(Vector3 feet)
        {
            float deadline = Time.time + 12f;
            while (Vector3.ProjectOnPlane(player.transform.position - feet, Vector3.up).magnitude > .30f && Time.time < deadline)
            {
                Vector3 direction = Vector3.ProjectOnPlane(feet - player.transform.position, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) });
                Assert.That(player.IsAvailable, Is.True);
                Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Count(g => g.IsTelemetryActive), Is.LessThanOrEqualTo(1));
                yield return null;
            }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(Vector3.ProjectOnPlane(player.transform.position - feet, Vector3.up).magnitude, Is.LessThan(.40f), "Normal motor and real collision; no path warps, invulnerability or widened route.");
        }
    }
}
#endif
