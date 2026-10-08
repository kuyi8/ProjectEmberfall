#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Named, real-input reaction assertions, not route automation or Player/human acceptance.</summary>
    public sealed class EnemyParticipationInputTests : IPrebuildSetup, IPostBuildCleanup
    {
        readonly List<string> evidence = new List<string>();
        Gamepad pad;

        public void Setup()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "")))
            {
                if (!SessionState.GetBool("Emberfall.EnemySensingFixturePrepared", false))
                    throw new InvalidOperationException("Previous preparation incomplete; clean its generated fixtures before retrying.");
                return;
            }
            var original = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || original.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Sensing prebuild refuses unsaved/multiple/playing scenes.");
            string originalPath = original.path;
            try
            {
                if (originalPath != "Assets/_Game/Scenes/10_EmberValley.unity")
                    EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
                FixtureTool().GetMethod("Prepare").Invoke(null, null);
            }
            finally
            {
                if (originalPath != "Assets/_Game/Scenes/10_EmberValley.unity")
                {
                    if (string.IsNullOrEmpty(originalPath)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else EditorSceneManager.OpenScene(originalPath);
                }
            }
        }

        public void Cleanup()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "")))
                FixtureTool().GetMethod("RequestCleanup").Invoke(null, null);
        }

        static Type FixtureTool() => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Emberfall.Editor")
            .GetType("Emberfall.Editor.Review.EnemySensingFixtureSetup", true);
        [UnityTearDown] public IEnumerator PreserveEvidence()
        {
            if (pad != null && pad.added) { InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.RemoveDevice(pad); }
            pad = null;
            string folder = "Builds/TestResults/EnemyParticipation/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(folder + "/" + TestContext.CurrentContext.Test.Name + ".jsonl", evidence);
            File.WriteAllText(folder + "/outcome.json", JsonUtility.ToJson(new Outcome {
                name = TestContext.CurrentContext.Test.FullName,
                state = TestContext.CurrentContext.Result.Outcome.Status.ToString(),
                message = TestContext.CurrentContext.Result.Message }, true));
            evidence.Clear(); yield return null;
        }

        [UnityTest] public IEnumerator BridgeLeft_FrontStanding_PerceptsActsAndCasts() => Verify("Enemy_RunePriest_Bridge_Left", false);
        [UnityTest] public IEnumerator BridgeRight_FrontStanding_PerceptsActsAndCasts() => Verify("Enemy_RunePriest_Bridge_Right", false);
        [UnityTest] public IEnumerator PreLeft_FrontStanding_PerceptsActsAndAttacks() => Verify("Enemy_Fogwalker_PreSanctum_Left", false);
        [UnityTest] public IEnumerator PreRight_FrontStanding_PerceptsActsAndAttacks() => Verify("Enemy_Fogwalker_PreSanctum_Right", false);
        [UnityTest] public IEnumerator PreGuard_FrontStanding_PerceptsActsAndAttacks() => Verify("Enemy_RuinGuard_PreSanctum", false);
        [UnityTest] public IEnumerator BridgeLeft_BackInputHit_WakesAndCasts() => Verify("Enemy_RunePriest_Bridge_Left", true);
        [UnityTest] public IEnumerator BridgeRight_BackInputHit_WakesAndCasts() => Verify("Enemy_RunePriest_Bridge_Right", true);
        [UnityTest] public IEnumerator PreLeft_BackInputHit_WakesAndAttacks() => Verify("Enemy_Fogwalker_PreSanctum_Left", true);
        [UnityTest] public IEnumerator PreRight_BackInputHit_WakesAndAttacks() => Verify("Enemy_Fogwalker_PreSanctum_Right", true);
        [UnityTest] public IEnumerator PreGuard_BackInputHit_WakesAndAttacks() => Verify("Enemy_RuinGuard_PreSanctum", true);
        [UnityTest] public IEnumerator ForestPriest_FrontStanding_RetreatsAndCasts() => Verify("Enemy_RunePriest_Forest", false);
        [UnityTest] public IEnumerator ForestPriest_BackInputHit_WakesAndCasts() => Verify("Enemy_RunePriest_Forest", true);
        [UnityTest] public IEnumerator CourtyardPriest_FrontStanding_RetreatsAndCasts() => Verify("Enemy_RunePriest_Courtyard", false);
        [UnityTest] public IEnumerator CourtyardPriest_BackInputHit_WakesAndCasts() => Verify("Enemy_RunePriest_Courtyard", true);
        [UnityTest] public IEnumerator ForestPriest_SyntheticPocket_UsesBoundedNormalCast() => VerifyCorner("Enemy_RunePriest_Forest");
        [UnityTest] public IEnumerator CourtyardPriest_SyntheticPocket_UsesBoundedNormalCast() => VerifyCorner("Enemy_RunePriest_Courtyard");
        [UnityTest] public IEnumerator BridgeLeft_SyntheticPocket_UsesBoundedNormalCast() => VerifyCorner("Enemy_RunePriest_Bridge_Left");
        [UnityTest] public IEnumerator BridgeRight_SyntheticPocket_UsesBoundedNormalCast() => VerifyCorner("Enemy_RunePriest_Bridge_Right");

        IEnumerator VerifyCorner(string name)
        {
            string root = SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "");
            Assert.That(root.StartsWith("Assets/_Game/Scenes/Review/__P0Sensing_", StringComparison.Ordinal), Is.True);
            M2LaunchIntent.RequestNewGame();
            EditorSceneManager.LoadSceneInPlayMode(root + "/" + name + "_Corner.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow.IsInitialized && player.IsAvailable, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
            Assert.That(flow.SavePath.Contains("IsolatedSaves"), Is.True);
            var enemy = Object.FindObjectsOfType<RangedEnemyActor>().Single(t => t.name == name);
            var leash = enemy.GetComponent<EncounterLeash>();
            Assert.That(enemy.enabled && enemy.HasSimulationAuthority && enemy.IsAvailable, Is.True);
            Assert.That(Object.FindObjectsOfType<UnityEngine.AI.NavMeshObstacle>().Count(x => x.name.StartsWith("TEST_ONLY_RetreatPocket_")), Is.EqualTo(4));
            Vector3 initialPlayer = player.transform.position;
            float started = Time.time, next = 0, maxBlocked = 0, windupStart = -1;
            bool fallback = false;
            while (Time.time - started < 8f && enemy.ReleasedAttackSequence == 0)
            {
                maxBlocked = Mathf.Max(maxBlocked, enemy.Brain.RetreatBlockedElapsed);
                if (enemy.Brain.IsCorneredWindup)
                {
                    fallback = true; if (windupStart < 0) windupStart = Time.time;
                    Assert.That(enemy.RetreatUnavailable, Is.True);
                    Assert.That(enemy.RetreatHasPreferredDestination, Is.False, "This counterexample must actually lack a band-restoring path.");
                }
                Assert.That(leash.Contains(enemy.transform.position, -.05f) && leash.AllowsTarget(player.transform.position), Is.True);
                Assert.That(enemy.IsAvailable && player.IsAvailable, Is.True, "Keep real deaths, never bypass balance.");
                if (Time.time >= next) { next = Time.time + .1f; RecordSample(enemy, player, Time.time - started, false); }
                yield return null;
            }
            RecordSample(enemy, player, Time.time - started, false);
            Assert.That(fallback, Is.True, "Requires geometric evidence and Brain-owned cornered Windup, not normal-band casting.");
            Assert.That(maxBlocked, Is.GreaterThanOrEqualTo(player.Model.EnemyRetreatBlockedSeconds - .05f));
            Assert.That(enemy.ReleasedAttackSequence, Is.GreaterThan(0), "Actual adapter release is mandatory.");
            Assert.That(Time.time - windupStart, Is.GreaterThanOrEqualTo(enemy.Definition.WindupDuration - .05f), "Cannot skip Windup.");
            Assert.That(Vector3.Distance(enemy.transform.position, player.transform.position), Is.LessThan(enemy.Definition.PreferredMinimumRange));
            Assert.That(Vector3.Distance(initialPlayer, player.transform.position), Is.LessThan(.4f));
        }

        IEnumerator Verify(string name, bool rear)
        {
            string root = SessionState.GetString("Emberfall.EnemySensingFixtureRoot", "");
            Assert.That(root.StartsWith("Assets/_Game/Scenes/Review/__P0Sensing_", StringComparison.Ordinal), Is.True,
                "Run EnemySensingFixtureSetup.Prepare in clean Edit Mode first. Fixtures have pre-authored spawns; no runtime teleport.");
            M2LaunchIntent.RequestNewGame();
            EditorSceneManager.LoadSceneInPlayMode(root + "/" + name + (rear ? "_Back" : "_Front") + ".unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow.IsInitialized && player.IsAvailable, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
            Assert.That(flow.SavePath.Contains("IsolatedSaves"), Is.True, "Real user saves must never be used.");
            var target = Object.FindObjectsOfType<CombatTarget>().Single(t => t.name == name);
            Assert.That(((Behaviour)target).enabled && target.IsAvailable, Is.True);
            var leash = target.GetComponent<EncounterLeash>();
            Assert.That(leash.Encounter.enabled && leash.AllowsTarget(player.transform.position), Is.True);
            Assert.That((bool)target.GetType().GetProperty("HasSimulationAuthority").GetValue(target), Is.True);
            // No brain calls, destination writes, actor/coordinator disabling or health cheats.
            float initialAngle = Vector3.Angle(target.transform.forward,
                Vector3.ProjectOnPlane(player.transform.position - target.transform.position, Vector3.up));
            var definition = target.GetType().GetProperty("Definition").GetValue(target);
            float halfFov = (float)definition.GetType().GetProperty("FieldOfView").GetValue(definition) * .5f;
            Assert.That(rear ? initialAngle > halfFov : initialAngle <= halfFov, Is.True,
                "Fixture must actually cover its stated entry angle.");
            pad = InputSystem.AddDevice<Gamepad>("EnemyParticipationAssertionPad");
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            float originalHealth = target.HealthNormalized;
            float initialDistance = Vector3.Distance(target.transform.position, player.transform.position);
            float started = Time.time, next = 0;
            int initialSequence = Sequence(target);
            bool hit = !rear, action = false, stimulus = false, retreat = false, preferredDestination = false;
            bool corneredFallback = false;
            Vector3 initialPlayer = player.transform.position;
            if (rear)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(pad, new GamepadState());
            }
            while (Time.time - started < 12f && (Sequence(target) <= initialSequence ||
                (target is RangedEnemyActor casting && casting.ReleasedAttackSequence <= initialSequence)))
            {
                hit |= target.HealthNormalized < originalHealth;
                stimulus |= (float)target.GetType().GetProperty("HitAwarenessRemaining").GetValue(target) > 0;
                string state = target.GetType().GetProperty("State").GetValue(target).ToString();
                action |= state == "Chase" || state == "Approach" || state == "Retreat" || state == "Windup";
                retreat |= state == "Retreat";
                preferredDestination |= target is RangedEnemyActor planning && planning.RetreatHasPreferredDestination;
                corneredFallback |= target is RangedEnemyActor constrained && constrained.Brain.IsCorneredWindup;
                Assert.That(leash.Contains(target.transform.position, -.05f), Is.True, "Reaction must stay inside the authored encounter.");
                if (Time.time >= next)
                {
                    next = Time.time + .1f;
                    RecordSample(target, player, Time.time - started, rear);
                }
                Assert.That(player.IsAvailable, Is.True, "Standing reaction assertion died; retain evidence, no balance bypass.");
                yield return null;
            }
            RecordSample(target, player, Time.time - started, rear);
            Assert.That(hit, Is.True, "Back case must damage this member through the actual Light input.");
            if (rear) Assert.That(stimulus, Is.True, "Back hit did not record source-specific awareness.");
            Assert.That(action, Is.True, "Member did not act via its own state machine.");
            Assert.That(Sequence(target), Is.GreaterThan(initialSequence), "Member never committed an attack/release.");
            if (target is RangedEnemyActor ranged)
            {
                Assert.That(corneredFallback, Is.False, "Normal progressing retreat must not use cornered fallback.");
                Assert.That(ranged.ReleasedAttackSequence, Is.GreaterThan(initialSequence), "Intent without actual projectile/rune release is insufficient.");
                if (initialDistance < ranged.Definition.PreferredMinimumRange)
                    Assert.That(retreat && preferredDestination, Is.True, "Close fixtures must preserve normal retreat to a viable preferred position.");
                Assert.That(Vector3.Distance(target.transform.position, player.transform.position),
                    Is.InRange(ranged.Definition.PreferredMinimumRange - .05f, ranged.Definition.PreferredMaximumRange + .05f),
                    "Do not obtain a cast by lowering the preferred range.");
            }
            Assert.That(Vector3.Distance(initialPlayer, player.transform.position), Is.LessThan(.4f), "Standing test moved unexpectedly.");
        }

        void RecordSample(CombatTarget target, PlayerCombatActor player, float elapsed, bool rear)
        {
            evidence.Add(JsonUtility.ToJson(new Sample { name = target.name, time = elapsed, rear = rear,
                health = target.HealthNormalized, playerHealth = player.HealthNormalized,
                state = target.GetType().GetProperty("State").GetValue(target).ToString(),
                sequence = Sequence(target), angle = Vector3.Angle(target.transform.forward,
                    Vector3.ProjectOnPlane(player.transform.position - target.transform.position, Vector3.up)),
                awareness = (float)target.GetType().GetProperty("HitAwarenessRemaining").GetValue(target),
                position = target.transform.position, player = player.transform.position,
                preferredRetreat = target is RangedEnemyActor planner && planner.RetreatHasPreferredDestination,
                retreatUnavailable = target is RangedEnemyActor constrained && constrained.RetreatUnavailable,
                cornered = target is RangedEnemyActor fallback && fallback.Brain.IsCorneredWindup,
                blockedElapsed = target is RangedEnemyActor blocked ? blocked.Brain.RetreatBlockedElapsed : 0,
                releaseSequence = target is RangedEnemyActor caster ? caster.ReleasedAttackSequence : 0 }));
        }

        static int Sequence(CombatTarget target)
        {
            if (target is RangedEnemyActor ranged) return ranged.Brain.AttackSequence;
            if (target is MeleeEnemyActor melee) return melee.Brain.AttackSequence;
            return ((ShieldEnemyActor)target).Brain.AttackSequence;
        }
        [Serializable] sealed class Sample
        {
            public string name, state;
            public float time, health, playerHealth, angle, awareness;
            public float blockedElapsed;
            public int sequence, releaseSequence;
            public bool rear, preferredRetreat;
            public bool retreatUnavailable, cornered;
            public Vector3 position, player;
        }
        [Serializable] sealed class Outcome { public string name, state, message; }
    }
}
#endif
