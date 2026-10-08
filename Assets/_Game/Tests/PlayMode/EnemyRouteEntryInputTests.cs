#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    // Limited entry/reaction assertions, not a combat robot or a whole-route/human verdict.
    public sealed class EnemyRouteEntryInputTests : IPrebuildSetup, IPostBuildCleanup
    {
        readonly List<string> samples = new List<string>();
        Keyboard keyboard;
        bool sawForwardInput;

        public void Setup() => new EnemyParticipationInputTests().Setup();
        public void Cleanup() => new EnemyParticipationInputTests().Cleanup();

        [UnityTest]
        public IEnumerator BridgeRight_ForwardKeyboardEntry_WithoutFirstHit_ReleasesAttack() =>
            VerifyEntry("BridgeEntry", new[] { "Enemy_RunePriest_Bridge_Right" }, Vector3.forward);

        [UnityTest]
        public IEnumerator PreSanctum_WestKeyboardEntry_WithoutFirstHit_AllMembersAttack() =>
            VerifyEntry("PreSanctumEntry", new[] { "Enemy_Fogwalker_PreSanctum_Left",
                "Enemy_Fogwalker_PreSanctum_Right", "Enemy_RuinGuard_PreSanctum" }, Vector3.right);

        [UnityTest]
        public IEnumerator EncounterAlert_DoesNotBypassPhysicalOcclusion()
        {
            yield return LoadBoundaryFixture();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var guard = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            var encounter = guard.GetComponent<EncounterLeash>().Encounter;
            yield return new WaitForSeconds(.3f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True, "Actual visible member must establish the alert.");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.name = "TEST_ONLY_AlertSightBlocker";
                wall.transform.position = (guard.AimPoint.position + player.AimPoint.position) * .5f;
                wall.transform.localScale = Vector3.one * .45f;
                var ally = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Enemy_Fogwalker_PreSanctum_Left");
                wall.transform.SetParent(ally.transform, true);
                Physics.SyncTransforms();
                Assert.That((bool)typeof(ShieldEnemyActor).GetMethod("CanSeeTarget", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(guard, null), Is.True, "An allied body must not act as an opaque wall.");
                wall.transform.SetParent(null, true);
                Physics.SyncTransforms();
                Assert.That((bool)typeof(ShieldEnemyActor).GetMethod("CanSeeTarget", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(guard, null), Is.False, "An alert is not an unobstructed sight ray.");
            }
            finally { Object.Destroy(wall); }
        }

        [UnityTest]
        public IEnumerator EncounterAlert_ClearsOnExitResetAndDisable_AndDoesNotReachAnotherArena()
        {
            yield return LoadBoundaryFixture();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var guard = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            var encounter = guard.GetComponent<EncounterLeash>().Encounter;
            yield return new WaitForSeconds(.3f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True);
            foreach (var other in Object.FindObjectsOfType<CombatEncounterCoordinator>().Where(c => c != encounter))
                Assert.That(other.HasCombatAlertFor(player), Is.False, other.name + " received a different arena's alert.");
            // Synthetic lifecycle counterexample ONLY. This is not ordinary route evidence.
            var capsule = player.GetComponent<CharacterController>();
            capsule.enabled = false;
            Vector3 saved = player.transform.position;
            player.transform.position = encounter.ArenaCenter + Vector3.right * (encounter.ArenaHalfExtents.x + 20f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.False, "Exit rejection must not wait for next Update.");
            yield return null;
            Assert.That((bool)typeof(CombatEncounterCoordinator).GetField("_combatAlerted", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(encounter), Is.False);
            player.transform.position = saved;
            capsule.enabled = true;
            yield return new WaitForSeconds(.3f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True);
            encounter.enabled = false;
            Assert.That(encounter.HasCombatAlertFor(player), Is.False);
            encounter.enabled = true;
            Assert.That(encounter.HasCombatAlertFor(player), Is.False, "Re-enable must not resurrect the old latch.");
            yield return new WaitForSeconds(.3f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True);
            encounter.ResetEncounter();
            Assert.That(encounter.HasCombatAlertFor(player), Is.False);
        }

        [UnityTest]
        public IEnumerator EncounterAlert_ClearsWhenPlayerDies()
        {
            yield return LoadBoundaryFixture();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var guard = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            var encounter = guard.GetComponent<EncounterLeash>().Encounter;
            yield return new WaitForSeconds(.3f);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True);
            player.ReceiveDamage(new Emberfall.Gameplay.Combat.Domain.DamageRequest(
                guard.CombatantId, 999, 10000f, 0f, Emberfall.Gameplay.Combat.Domain.AttackTag.Heavy));
            Assert.That(player.IsAvailable, Is.False);
            Assert.That(encounter.HasCombatAlertFor(player), Is.False);
            Assert.That((bool)typeof(CombatEncounterCoordinator).GetField("_combatAlerted", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(encounter), Is.False);
        }

        [UnityTest]
        public IEnumerator CrowdedSight_FiveProductionBodiesRemainTransparent_ButWallBlocks()
        {
            yield return LoadBoundaryFixture();
            yield return new WaitForSeconds(.3f);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var observer = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            var encounter = observer.GetComponent<EncounterLeash>().Encounter;
            Assert.That(observer.HasSimulationAuthority && observer.isActiveAndEnabled && observer.IsAvailable, Is.True);
            Assert.That(player.IsAvailable && InsideArena(encounter), Is.True);
            Assert.That(encounter.HasCombatAlertFor(player), Is.True);
            Assert.That(Sight(observer), Is.True, "The controlled crowd needs an initially unobstructed production ray.");
            // Synthetic arrangement of REAL bodies, not a natural route or owner-spawn verdict.
            // Five is the largest current designated+owner-two-summon group. Two ordinary
            // capsules stand in for the identically shaped runtime minion bodies; no fake cast.
            string[] names = { "Enemy_Fogwalker_PreSanctum_Left", "Enemy_Fogwalker_PreSanctum_Right",
                "Enemy_AshCaller_Approach", "Enemy_Fogwalker_Forest", "Enemy_Fogwalker_Courtyard_Support" };
            var crowd = Object.FindObjectsOfType<CombatTarget>().Where(a => names.Contains(a.name)).ToArray();
            Assert.That(crowd.Length, Is.EqualTo(5));
            Vector3[] positions = crowd.Select(a => a.transform.position).ToArray();
            var agents = crowd.Select(a => a.GetComponent<NavMeshAgent>()).ToArray();
            bool[] enabled = agents.Select(a => a.enabled).ToArray();
            var wall = new GameObject("TEST_ONLY_CrowdWall");
            try
            {
                Vector3 from = observer.AimPoint.position, to = player.AimPoint.position;
                Assert.That(Vector3.Distance(from, to), Is.GreaterThan(2f));
                for (int i = 0; i < crowd.Length; i++)
                {
                    var body = crowd[i].GetComponent<CapsuleCollider>();
                    Assert.That(crowd[i].GetComponentsInChildren<Collider>(true).Count(c => c.enabled && !c.isTrigger), Is.EqualTo(1));
                    Assert.That(body.radius, Is.EqualTo(.42f).Within(.001f));
                    Assert.That(body.height, Is.EqualTo(2f).Within(.001f));
                    agents[i].enabled = false;
                    Vector3 point = Vector3.Lerp(from, to, .16f + i * .17f);
                    crowd[i].transform.position += point - body.bounds.center;
                }
                Physics.SyncTransforms();
                int count = RecordCrowd(observer, player, encounter, "five-real-body-shapes");
                Assert.That(count, Is.GreaterThanOrEqualTo(5).And.LessThan(Buffer(observer).Length));
                Assert.That(Sight(observer), Is.True, "Non-player bodies, including a different encounter, must not become walls.");
                wall.transform.position = Vector3.Lerp(from, to, .9f);
                var box = wall.AddComponent<BoxCollider>(); box.size = Vector3.one * .2f;
                Physics.SyncTransforms();
                RecordCrowd(observer, player, encounter, "five-body-shapes-plus-wall");
                Assert.That(Sight(observer), Is.False, "Transparency must not discard the actual wall behind a crowd.");
            }
            finally
            {
                Object.Destroy(wall);
                for (int i = 0; i < crowd.Length; i++)
                { crowd[i].transform.position = positions[i]; agents[i].enabled = enabled[i]; }
                Physics.SyncTransforms();
            }
        }

        [UnityTest]
        public IEnumerator CrowdedSight_SyntheticBufferSaturationRefusesUnorderedResults()
        {
            yield return LoadBoundaryFixture();
            yield return new WaitForSeconds(.3f);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var observer = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            var encounter = observer.GetComponent<EncounterLeash>().Encounter;
            Assert.That(observer.HasSimulationAuthority && observer.isActiveAndEnabled && player.IsAvailable, Is.True);
            Assert.That(InsideArena(encounter), Is.True);
            Assert.That(encounter.HasCombatAlertFor(player) && Sight(observer), Is.True);
            var ally = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Enemy_Fogwalker_PreSanctum_Left");
            var overflow = new GameObject("TEST_ONLY_SyntheticOverCapacity");
            overflow.transform.SetParent(ally.transform, false);
            try
            {
                Vector3 from = observer.AimPoint.position, to = player.AimPoint.position;
                for (int i = 0; i < Buffer(observer).Length + 2; i++)
                {
                    var child = new GameObject("TransparentSyntheticHit_" + i);
                    child.transform.SetParent(overflow.transform, false);
                    child.transform.position = Vector3.Lerp(from, to, .15f + i * .035f);
                    child.AddComponent<BoxCollider>().size = Vector3.one * .025f;
                }
                Physics.SyncTransforms();
                Assert.That(RecordCrowd(observer, player, encounter, "synthetic-over-capacity"), Is.EqualTo(Buffer(observer).Length));
                Assert.That(Sight(observer), Is.False, "A saturated unordered ray might omit a wall; never infer clear sight.");
            }
            finally { Object.Destroy(overflow); }
        }

        static RaycastHit[] Buffer(ShieldEnemyActor actor) => (RaycastHit[])typeof(ShieldEnemyActor)
            .GetField("_sightHits", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actor);
        static bool InsideArena(CombatEncounterCoordinator encounter) => (bool)typeof(CombatEncounterCoordinator)
            .GetMethod("IsPlayerPositionInsideTelemetryArena", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(encounter, null);
        static bool Sight(ShieldEnemyActor actor) => (bool)typeof(ShieldEnemyActor)
            .GetMethod("CanSeeTarget", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(actor, null);
        int RecordCrowd(ShieldEnemyActor observer, PlayerCombatActor player, CombatEncounterCoordinator encounter, string scenario)
        {
            Vector3 direction = player.AimPoint.position - observer.AimPoint.position;
            var buffer = Buffer(observer);
            int count = Physics.RaycastNonAlloc(observer.AimPoint.position, direction.normalized, buffer,
                direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
            samples.Add(JsonUtility.ToJson(new CrowdRow { scenario = scenario, capacity = buffer.Length, count = count,
                authority = observer.HasSimulationAuthority, available = observer.IsAvailable && player.IsAvailable,
                inside = InsideArena(encounter), alert = encounter.HasCombatAlertFor(player),
                canSee = Sight(observer), distance = direction.magnitude,
                angle = Vector3.Angle(observer.transform.forward, Vector3.ProjectOnPlane(direction, Vector3.up)),
                hits = buffer.Take(count).Select(h => h.collider.name).ToArray() }));
            return count;
        }
        [Serializable] sealed class CrowdRow
        {
            public string scenario; public string[] hits; public int capacity, count;
            public bool authority, available, inside, alert, canSee; public float distance, angle;
        }

        static IEnumerator LoadBoundaryFixture()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            Assert.That(M2RouteFlowController.EditorTestSavePath.Contains("IsolatedSaves"), Is.True);
            M2LaunchIntent.RequestNewGame();
            string root = SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "");
            Assert.That(root.StartsWith("Assets/_Game/Scenes/Review/__P0Sensing_", StringComparison.Ordinal), Is.True);
            EditorSceneManager.LoadSceneInPlayMode(root + "/PreSanctumEntry.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            Assert.That(Path.GetFullPath(Object.FindObjectOfType<M2RouteFlowController>().SavePath),
                Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
        }

        [UnityTearDown]
        public IEnumerator RetainEvidence()
        {
            if (keyboard != null && keyboard.added)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.RemoveDevice(keyboard);
            }
            keyboard = null;
            string folder = "Builds/TestResults/EnemyRouteEntry/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(folder + "/" + TestContext.CurrentContext.Test.Name + ".jsonl", samples);
            File.WriteAllText(folder + "/outcome.json", JsonUtility.ToJson(new Outcome {
                name = TestContext.CurrentContext.Test.FullName,
                state = TestContext.CurrentContext.Result.Outcome.Status.ToString(),
                message = TestContext.CurrentContext.Result.Message }, true));
            samples.Clear();
            yield return null;
        }

        IEnumerator VerifyEntry(string fixture, string[] members, Vector3 forward)
        {
            sawForwardInput = false;
            string root = SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "");
            Assert.That(root.StartsWith("Assets/_Game/Scenes/Review/__P0Sensing_", StringComparison.Ordinal), Is.True);
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty,
                "Save isolation must be established BEFORE loading/NewGame.");
            Assert.That(M2RouteFlowController.EditorTestSavePath.Contains("IsolatedSaves"), Is.True);
            M2LaunchIntent.RequestNewGame();
            EditorSceneManager.LoadSceneInPlayMode(root + "/" + fixture + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow.IsInitialized && player.IsAvailable, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
            var actors = Object.FindObjectsOfType<CombatTarget>().Where(a => members.Contains(a.name)).ToArray();
            Assert.That(actors.Length, Is.EqualTo(members.Length));
            foreach (var actor in actors)
                Assert.That(actor.isActiveAndEnabled && actor.IsAvailable, Is.True, actor.name);
            var input = player.GetComponent<PlayerInputReader>();
            keyboard = InputSystem.AddDevice<Keyboard>("RouteEntryAssertionKeyboard");
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Vector3 start = player.transform.position;
            float started = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            while (Vector3.Dot(player.transform.position - start, forward) < 1.8f && Time.time - started < 3f)
            {
                Sample(actors, player, input);
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Assert.That(Vector3.Dot(player.transform.position - start, forward), Is.GreaterThanOrEqualTo(1.8f),
                "Actual keyboard/capsule entry failed; no teleport/AI override substitutes for it.");
            Assert.That(sawForwardInput, Is.True, "Observe the actual bound W action.");
            while (Time.time - started < 10f && actors.Any(a => Sequence(a) == 0))
            {
                Sample(actors, player, input);
                Assert.That(player.IsAvailable, Is.True, "Natural reaction killed the observer; retain failure, never ease balance.");
                yield return null;
            }
            Sample(actors, player, input);
            foreach (var actor in actors)
                Assert.That(Sequence(actor), Is.GreaterThan(0), actor.name + " never attacked after keyboard entry without a first hit.");
        }

        void Sample(CombatTarget[] actors, PlayerCombatActor player, PlayerInputReader input)
        {
            bool forwardInput = input.Move.y > .9f && Mathf.Abs(input.Move.x) < .01f;
            sawForwardInput |= forwardInput;
            foreach (var actor in actors)
            {
                var agent = actor.GetComponent<NavMeshAgent>();
                var leash = actor.GetComponent<EncounterLeash>();
                Vector3 direction = player.AimPoint.position - actor.AimPoint.position;
                bool hit = Physics.Raycast(actor.AimPoint.position, direction.normalized, out var sight,
                    direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
                samples.Add(JsonUtility.ToJson(new Row {
                    name = actor.name, time = Time.time, player = player.transform.position,
                    position = actor.transform.position, health = actor.HealthNormalized,
                    forwardInput = forwardInput,
                    move = input.Move, sequence = Sequence(actor),
                    state = actor is RangedEnemyActor ranged ? ranged.State.ToString() :
                        actor is MeleeEnemyActor melee ? melee.State.ToString() : ((ShieldEnemyActor)actor).State.ToString(),
                    angle = Vector3.Angle(actor.transform.forward, Vector3.ProjectOnPlane(direction, Vector3.up)),
                    leashAllows = leash == null || leash.AllowsTarget(player.transform.position),
                    onNavMesh = agent.enabled && agent.isOnNavMesh,
                    actualCanSee = (bool)actor.GetType().GetMethod("CanSeeTarget", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(actor, null),
                    groupAlert = leash != null && leash.Encounter.HasCombatAlertFor(player),
                    attackAllowed = actor is MeleeEnemyActor m ? m.IsGroupAttackAllowed :
                        actor is ShieldEnemyActor s ? s.IsGroupAttackAllowed : true,
                    support = actor is MeleeEnemyActor sm ? sm.SupportDestination :
                        actor is ShieldEnemyActor ss ? ss.SupportDestination : Vector3.zero,
                    hasPath = agent.enabled && agent.isOnNavMesh && agent.hasPath,
                    velocity = agent.enabled && agent.isOnNavMesh ? agent.velocity : Vector3.zero,
                    path = agent.enabled && agent.isOnNavMesh ? agent.pathStatus.ToString() : "OffNavMesh",
                    sightHit = hit ? sight.collider.name : "None",
                    rootForward = player.transform.forward
                }));
            }
        }

        static int Sequence(CombatTarget actor) => actor is RangedEnemyActor ranged ? ranged.ReleasedAttackSequence :
            actor is MeleeEnemyActor melee ? melee.Brain.AttackSequence : ((ShieldEnemyActor)actor).Brain.AttackSequence;

        [Serializable] sealed class Row
        {
            public string name, state, path, sightHit;
            public float time, health, angle;
            public bool forwardInput, leashAllows, onNavMesh, hasPath, actualCanSee, groupAlert, attackAllowed;
            public int sequence;
            public Vector2 move;
            public Vector3 player, position, rootForward, velocity, support;
        }
        [Serializable] sealed class Outcome { public string name, state, message; }
    }
}
#endif
