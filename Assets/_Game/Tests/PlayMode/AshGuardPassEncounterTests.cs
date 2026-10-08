#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
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
    /// <summary>Formal shield combination. Controlled integration is not natural combat/difficulty acceptance.</summary>
    public sealed class AshGuardPassEncounterTests
    {
        CombatEncounterCoordinator group;
        SummonerEnemyActor caller;
        ShieldEnemyActor guard;
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
            group = Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(g => g.TelemetrySegment == "ash-guard-pass-encounter");
            caller = group.GetComponentInChildren<SummonerEnemyActor>();
            guard = group.GetComponentInChildren<ShieldEnemyActor>();
            Assert.That(caller.Brain, Is.Not.Null); Assert.That(guard.Brain, Is.Not.Null);
        }

        [UnityTearDown] public IEnumerator End()
        {
            if (pad != null && pad.added) { InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.RemoveDevice(pad); }
            pad = null; yield return null;
            if (ownHost) { SessionRuntime.Current.Shutdown(); ownHost = false; yield return null; }
        }

        [UnityTest] public IEnumerator RosterAndGeometry_HaveRealClearanceAndWholeNetworkExclusion()
        {
            Assert.That(group.SummonerMemberCount, Is.EqualTo(1));
            Assert.That(group.MeleeMemberCount, Is.EqualTo(1));
            Assert.That(group.RangedMemberCount, Is.Zero);
            Assert.That(caller.EncounterId, Is.EqualTo("encounter:ash-guard-pass"));
            Assert.That(guard.Definition.Id.Value, Is.EqualTo("enemy:ruin-guard"));
            Assert.That(guard.Definition.ScorchedBurstEnabled, Is.False);
            Assert.That(group.EndsAttemptOnExit, Is.True);
            foreach (var agent in group.GetComponentsInChildren<NavMeshAgent>()) Assert.That(agent.isOnNavMesh, Is.True, agent.name);
            yield return new WaitForSeconds(.7f);
            Physics.SyncTransforms();
            var walls = group.GetComponentsInChildren<Transform>().Where(t => t.name == "AshGuardPass_VisibleEndWall").ToArray();
            Assert.That(walls.Length, Is.EqualTo(2), "Reuse the old north wall rather than stack a second wall on it.");
            foreach (var wall in walls)
            {
                var collider = wall.GetComponentsInChildren<Collider>().Single();
                var renderer = wall.GetComponentsInChildren<Renderer>().Single();
                Assert.That(Vector3.Distance(collider.bounds.size, new Vector3(2.6f, .8f, .5f)), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(collider.bounds.center, renderer.bounds.center), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(collider.bounds.size, renderer.bounds.size), Is.LessThan(.001f));
                Assert.That(collider.bounds.center.z, Is.EqualTo(18.4f).Within(.001f));
            }
            var south = walls.Select(w => w.GetComponentInChildren<Collider>()).OrderBy(c => c.bounds.center.x).ToArray();
            Assert.That(south[1].bounds.min.x - south[0].bounds.max.x, Is.EqualTo(3.2f).Within(.001f));
            var a = Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(g => g.TelemetrySegment == "ash-approach-encounter");
            var aNorth = a.GetComponentsInChildren<Transform>().Where(t => t.name == "AshApproach_VisibleEndWall")
                .Select(w => w.GetComponentInChildren<Collider>()).Where(c => c.bounds.center.z > 16).ToArray();
            Assert.That(south.Min(c => c.bounds.min.z) - aNorth.Max(c => c.bounds.max.z), Is.EqualTo(.90f).Within(.001f), "Longitudinal wall separation is not the door width.");
            Assert.That(group.ArenaCenter.z + group.ArenaHalfExtents.y + group.TelemetryActivationMargin, Is.LessThan(25.5f));
            var leash = guard.GetComponent<EncounterLeash>();
            Assert.That(leash.AllowsTarget(new Vector3(0, 0, 25.5f)), Is.False);
            Assert.That(leash.Contains(leash.ClampDestination(new Vector3(0, 0, 28))), Is.True);
            var path = new NavMeshPath();
            Assert.That(NavMesh.CalculatePath(new Vector3(0, 0, 17.7f), new Vector3(0, 0, 28.5f), NavMesh.AllAreas, path), Is.True);
            Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            var network = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            var roots = new SerializedObject(network).FindProperty("_offlineActorRoots");
            Assert.That(Enumerable.Range(0, roots.arraySize).Select(i => roots.GetArrayElementAtIndex(i).objectReferenceValue), Does.Contain(group.gameObject));
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
        }

        [UnityTest] public IEnumerator ClearRequiresBothMembers_ContinueRestoresBWithoutClearingA()
        {
            Place(new Vector3(.8f, 0, 20)); yield return null;
            Kill(caller, 10); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.False);
            Assert.That(Object.FindObjectsOfType<Emberfall.UI.M2EncounterBannerPresenter>(true)
                .Single(p => p.Encounter == Emberfall.UI.AshEncounterBanner.GuardPass).IsBannerVisible, Is.False);
            guard.ApplyNeutralPostureDamage(999f); Kill(guard, 11); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.True);
            int clearFrame = Time.frameCount;
            yield return new WaitForEndOfFrame();
            Assert.That(Time.frameCount, Is.EqualTo(clearFrame), "Read the rendered banner after LateUpdate, without an extra-frame grace period.");
            Assert.That(Object.FindObjectsOfType<Emberfall.UI.M2EncounterBannerPresenter>(true)
                .Single(p => p.Encounter == Emberfall.UI.AshEncounterBanner.GuardPass).IsBannerVisible, Is.True);
            Assert.That(flow.AshApproachCleared, Is.False);
            Assert.That(File.Exists(flow.SavePath), Is.True);
            M2LaunchIntent.RequestContinue();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var continued = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(continued.AshGuardPassCleared, Is.True); Assert.That(continued.AshApproachCleared, Is.False);
            Assert.That(Object.FindObjectsOfType<Emberfall.UI.M2EncounterBannerPresenter>(true)
                .Single(p => p.Encounter == Emberfall.UI.AshEncounterBanner.GuardPass).IsBannerVisible, Is.True);
            var b = Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(g => g.TelemetrySegment == "ash-guard-pass-encounter");
            Assert.That(b.GetComponentsInChildren<SummonerEnemyActor>(), Is.Empty);
            Assert.That(b.GetComponentsInChildren<ShieldEnemyActor>(), Is.Empty); Assert.That(b.IsTelemetryActive, Is.False);
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(g => g.TelemetrySegment == "ash-approach-encounter")
                .GetComponentInChildren<SummonerEnemyActor>().IsAvailable, Is.True);
        }

        [UnityTest] public IEnumerator NormalMovement_CrossesTwoEncountersAndNeutralStripsWithoutOverlap()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            yield return Walk(new Vector3(.8f, 0, 12)); AssertActive("ash-approach-encounter");
            yield return Walk(new Vector3(.8f, 0, 17.7f)); AssertActive(null);
            yield return Walk(new Vector3(.8f, 0, 21)); AssertActive("ash-guard-pass-encounter");
            yield return Walk(new Vector3(.8f, 0, 26.15f)); AssertActive(null);
            yield return Walk(new Vector3(0, 0, 28.5f)); AssertActive("forest-encounter");
            Assert.That(flow.AshGuardPassCleared, Is.False, "Leaving is not winning or resetting the survivors.");
            Assert.That(caller.IsAvailable, Is.True); Assert.That(guard.IsAvailable, Is.True);
            Assert.That(guard.GetComponent<EncounterLeash>().Contains(guard.transform.position), Is.True);
        }

        [UnityTest] public IEnumerator RealSummon_HasIndependentBeatAndImmediateOwnerCleanupWithShieldStillRequired()
        {
            Place(new Vector3(.8f, 0, 19.7f));
            // Lifecycle/readability isolation only. Do not use this as natural shield combat acceptance.
            guard.SetSimulationAuthority(false);
            int bBeats = 0, aBeats = 0;
            UnityEngine.Application.LogCallback observer = (message, stack, type) =>
            {
                if (!message.StartsWith(PacingTelemetryRecorder.LogPrefix) || !message.Contains("event=first-summon")) return;
                if (message.Contains("segment=encounter_ash-guard-pass ")) bBeats++;
                if (message.Contains("segment=encounter_ash-approach ")) aBeats++;
            };
            UnityEngine.Application.logMessageReceived += observer;
            try
            {
                float deadline = Time.time + 12f;
                while (caller.LivingEntityCount == 0 && Time.time < deadline) { Assert.That(player.IsAvailable, Is.True); yield return null; }
                Assert.That(caller.LivingEntityCount, Is.EqualTo(1), $"state={caller.Brain.State} owner={caller.transform.position} player={player.NavigationFootPosition} reachable={caller.RetreatBestReachableDistance}");
                Assert.That(bBeats, Is.EqualTo(1)); Assert.That(aBeats, Is.Zero);
                var minion = caller.GetLivingEntity(0); Assert.That(minion, Is.Not.Null);
                var hud = Object.FindObjectOfType<Emberfall.UI.M2RouteHud>();
                yield return null; Assert.That(hud.ObservedLivingSummonCount, Is.EqualTo(1));
                string folder = Path.GetFullPath("Builds/ArtReview/AshGuardPass/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(folder);
                yield return null; yield return null;
                ScreenCapture.CaptureScreenshot(folder + "/formal-shield-combination-hud.png");
                deadline = Time.realtimeSinceStartup + 4f;
                while (!File.Exists(folder + "/formal-shield-combination-hud.png") && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(folder + "/formal-shield-combination-hud.png"), Is.True);
                Kill(caller, 50);
                Assert.That(caller.GetLivingEntity(0), Is.Null); Assert.That(minion.IsAvailable, Is.False);
                yield return null; Assert.That(hud.ObservedLivingSummonCount, Is.Zero);
                Assert.That(flow.AshGuardPassCleared, Is.False, "Owner death never counts as shield defeat.");
                guard.SetSimulationAuthority(true); guard.ApplyNeutralPostureDamage(999f); Kill(guard, 51); yield return null;
                Assert.That(flow.AshGuardPassCleared, Is.True);
            }
            finally { UnityEngine.Application.logMessageReceived -= observer; }
        }

        [UnityTest] public IEnumerator OrdinaryShield_FrontalBlockAndRearDamageRemainDifferent()
        {
            // Deterministic adapter/geometry test, not a simulated natural fight.
            guard.enabled = false;
            Vector3 feet = guard.transform.position;
            guard.transform.rotation = Quaternion.Euler(0, 180, 0);
            Place(feet + Vector3.back * 1.5f);
            float health = guard.HealthNormalized;
            DamageResult front = guard.ReceiveDamage(new DamageRequest(player.CombatantId, 80, 30f, 10f, AttackTag.Light));
            Assert.That(front.Blocked, Is.True); Assert.That(front.Accepted, Is.False);
            Assert.That(guard.HealthNormalized, Is.EqualTo(health));
            Place(feet + Vector3.forward * 1.5f);
            DamageResult rear = guard.ReceiveDamage(new DamageRequest(player.CombatantId, 81, 30f, 10f, AttackTag.Light));
            Assert.That(rear.Blocked, Is.False); Assert.That(rear.Accepted, Is.True);
            Assert.That(guard.HealthNormalized, Is.LessThan(health));
            Assert.That(flow.AshGuardPassCleared, Is.False);
            yield return null;
        }

        [UnityTest] public IEnumerator ListeningHost_DisablesWholeBRootBeforeOfflineAiAwake()
        {
            var service = SessionRuntime.Current; service.Shutdown(); yield return null;
            Assert.That(service.StartHost(47791), Is.True, service.Snapshot.Message); ownHost = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            Assert.That(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true).NetworkModeActive, Is.True);
            var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshGuardPass_v1");
            Assert.That(root.activeInHierarchy, Is.False);
            Assert.That(root.GetComponentInChildren<SummonerEnemyActor>(true).Brain, Is.Null);
            Assert.That(root.GetComponentInChildren<ShieldEnemyActor>(true).Brain, Is.Null);
            Assert.That(Object.FindObjectsOfType<SummonerEnemyActor>(), Is.Empty);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
        }

        void Place(Vector3 feet)
        {
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            var body = player.GetComponent<CharacterController>(); body.enabled = false;
            player.transform.position = feet + offset + Vector3.up * .04f; body.enabled = true;
            Physics.SyncTransforms(); Assert.That(Mathf.Abs(player.NavigationFootPosition.y - feet.y), Is.LessThan(.05f));
        }
        static void Kill(CombatTarget target, int sequence) => Assert.That(target.ReceiveDamage(new DamageRequest(988, sequence, 9999f, 100f, AttackTag.Heavy)).Killed, Is.True);
        void AssertActive(string segment)
        {
            var active = Object.FindObjectsOfType<CombatEncounterCoordinator>().Where(g => g.IsTelemetryActive).ToArray();
            Assert.That(active.Length, Is.EqualTo(segment == null ? 0 : 1));
            if (segment != null) Assert.That(active[0].TelemetrySegment, Is.EqualTo(segment));
        }
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
            Assert.That(Vector3.ProjectOnPlane(player.transform.position - feet, Vector3.up).magnitude, Is.LessThan(.40f), "Actual motor/collision traversal, no warps or invulnerability.");
        }
    }
}
#endif
