using System.Collections;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Interaction;
using Emberfall.Infrastructure.Saves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class ExecutionBarrierSceneTests
    {
        [UnityTest]
        public IEnumerator GraphicsEvidence_CapturesExecutionStatesAndBarrier()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Graphics evidence requires a real rendering device.");
#if UNITY_EDITOR
            UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
#endif
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var enemy = Object.FindObjectsOfType<MeleeEnemyActor>().First(x => x.name.Contains("Forest"));
            foreach (var actor in Object.FindObjectsOfType<MeleeEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<RangedEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<ShieldEnemyActor>()) actor.enabled = false;
            player.GetComponent<ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = enemy.transform.position + new Vector3(0f, 1f, -1.6f);
            foreach (var rig in Object.FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
            Camera camera = Camera.main;
            camera.transform.position = enemy.transform.position + new Vector3(3f, 3.5f, -6f);
            camera.transform.LookAt(enemy.AimPoint.position);
            var targeting = player.GetComponent<Emberfall.Gameplay.Targeting.LockOnTargeting>();
            typeof(Emberfall.Gameplay.Targeting.LockOnTargeting).GetProperty("CurrentTarget").SetValue(targeting, null);
            Physics.SyncTransforms();
            enemy.ApplyNeutralPostureDamage(enemy.Brain.Posture.Maximum * 0.85f);
            yield return Capture("execution-warning");
            enemy.ApplyNeutralPostureDamage(999f);
            yield return Capture("execution-posture-ready");
            enemy.Brain.Reset();
            enemy.ReceiveDamage(new DamageRequest(player.CombatantId, 7121,
                enemy.Brain.Health.Maximum * 0.92f + enemy.Definition.Armor, 0f, AttackTag.Light));
            yield return Capture("execution-health-ready");
            var elite = Object.FindObjectsOfType<ShieldEnemyActor>().First();
            player.transform.position = elite.transform.position + Vector3.back * 1.5f;
            camera.transform.position = elite.transform.position + new Vector3(3f, 3.5f, -6f);
            camera.transform.LookAt(elite.AimPoint.position);
            elite.ApplyNeutralPostureDamage(999f);
            Assert.That(elite.TryClaimExecution(), Is.True);
            elite.ReceiveDamage(new DamageRequest(player.CombatantId, 7122, elite.ExecutionDamage,
                0f, AttackTag.Heavy, false, true));
            Assert.That(elite.IsAvailable, Is.True);
            yield return Capture("execution-elite-spent-unlocked");
            var gate = GameObject.Find("GateBlocker_Sanctum");
            camera.transform.position = gate.transform.position + new Vector3(-4f, 2f, 6f);
            camera.transform.LookAt(gate.transform.position);
            var owner = new GameObject("EvidenceBarrierOwner");
            var barrier = M2StageBarrier.CreateManual(owner, gate);
            barrier.SetOpen(true);
            yield return Capture("barrier-dissolving", 0.18f);
            Object.Destroy(owner);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        private static IEnumerator Capture(string name, float delay = 0.2f)
        {
            string output = System.IO.Path.GetFullPath("Builds/ArtReview/0.8.10c");
            System.IO.Directory.CreateDirectory(output);
            yield return new WaitForSeconds(delay);
            string path = System.IO.Path.Combine(output, name + ".png");
            System.DateTime requestedAt = System.DateTime.UtcNow;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((!System.IO.File.Exists(path) || System.IO.File.GetLastWriteTimeUtc(path) < requestedAt) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(System.IO.File.Exists(path), Is.True, "No rendered evidence was produced: " + path);
            Assert.That(System.IO.File.GetLastWriteTimeUtc(path), Is.GreaterThanOrEqualTo(requestedAt));
        }

        [UnityTest]
        public IEnumerator VoidFall_ReturnsToActivatedCheckpointWithoutChangingDeathCountOrEnemyHealth()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var enemy = Object.FindObjectOfType<MeleeEnemyActor>();
            int deaths = flow.DeathCount;
            player.ActivateCheckpoint(new ContentId("checkpoint:test"), new Vector3(0f, 1.1f, 5f), Quaternion.identity);
            enemy.ReceiveDamage(new DamageRequest(player.CombatantId, 191, 10f, 0f, AttackTag.Light));
            float enemyHealth = enemy.Brain.Health.Current;
            player.Model.HealingFlasks.TryConsume();
            Assert.That(player.Model.Submit(CombatCommand.Dodge), Is.True);
            MoveIntoVoidVolume(player);
            for (int i = 0; i < 120 && player.LastCombatEvent != "Void recovery"; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(player.LastCombatEvent, Is.EqualTo("Void recovery"));
            Assert.That(flow.DeathCount, Is.EqualTo(deaths));
            Assert.That(Vector3.Distance(player.transform.position, player.RespawnPosition), Is.LessThan(0.15f));
            Assert.That(player.Model.Health.Normalized, Is.EqualTo(0.88f).Within(0.0001f));
            Assert.That(player.Model.HealingFlasks.CurrentCharges, Is.EqualTo(1));
            Assert.That(enemy.Brain.Health.Current, Is.EqualTo(enemyHealth));
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        private static void MoveIntoVoidVolume(PlayerCombatActor player)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = Object.FindObjectOfType<VoidExecutionVolume>().WorldBounds.center;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator AlreadyDeadThenFalls_UsesNormalDeathRespawnNotNonlethalRecovery()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            player.ActivateCheckpoint(new ContentId("checkpoint:test"), new Vector3(0f, 1.1f, 5f), Quaternion.identity);
            player.Model.HealingFlasks.TryConsume();
            Assert.That(player.ReceiveDamage(new DamageRequest(-1, 990, 9999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(flow.DeathCount, Is.EqualTo(1));
            MoveIntoVoidVolume(player);
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            Assert.That(player.Model.IsDead, Is.True);
            Assert.That(player.LastCombatEvent, Is.Not.EqualTo("Void recovery"));
            float deadline = Time.realtimeSinceStartup + 4f;
            while (player.Model.IsDead && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(player.Model.IsDead, Is.False);
            Assert.That(flow.DeathCount, Is.EqualTo(1));
            Assert.That(player.Model.Health.Normalized, Is.EqualTo(1f));
            Assert.That(player.Model.HealingFlasks.CurrentCharges, Is.EqualTo(2));
            Assert.That(Vector3.Distance(player.transform.position, player.RespawnPosition), Is.LessThan(0.15f));
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator FullHealthEnemy_DoesNotSwallowInteraction_ButReadyExecutionHasPriority()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var enemy = Object.FindObjectsOfType<MeleeEnemyActor>().First();
            foreach (var actor in Object.FindObjectsOfType<MeleeEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<RangedEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<ShieldEnemyActor>()) actor.enabled = false;
            player.GetComponent<ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = enemy.transform.position + Vector3.back * 1.5f + Vector3.up;
            var prop = new GameObject("InteractionPriorityProbe");
            prop.transform.position = player.transform.position;
            prop.AddComponent<SphereCollider>().isTrigger = true;
            var interaction = prop.AddComponent<ExecutionInteractionProbe>();
            Physics.SyncTransforms();
            var interactor = player.GetComponent<PlayerInteractor>();
            Assert.That(interactor.CurrentCandidate, Is.EqualTo(interaction));
            Assert.That(player.TryHandleExecutionInput(), Is.False);
            Assert.That(interactor.TryInteractNearest(), Is.True);
            Assert.That(interaction.Count, Is.EqualTo(1));
            enemy.ApplyNeutralPostureDamage(enemy.Brain.Posture.Maximum * 0.85f);
            Assert.That(player.TryHandleExecutionInput(), Is.True);
            Assert.That(player.FeedbackTextId, Is.EqualTo("text:execution.need-posture"));
            enemy.ApplyNeutralPostureDamage(999f);
            Assert.That(player.TryHandleExecutionInput(), Is.True);
            Assert.That(player.Model.State, Is.EqualTo(CombatState.Execution));
            Assert.That(interaction.Count, Is.EqualTo(1));
            Object.Destroy(prop);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator WardenBarrier_UsesRealFlowForCloseAndFadeOpen()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var barrier = flow.GetComponent<M2StageBarrier>();
            Assert.That(barrier, Is.Not.Null);
            Assert.That(barrier.SoundSequence, Is.Zero, "Initialization must be silent.");
            var setActive = typeof(M2RouteFlowController).GetMethod("SetWardenEncounterActive",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            setActive.Invoke(flow, new object[] { true });
            var gate = GameObject.Find("GateBlocker_WardenEncounter");
            Assert.That(gate.GetComponent<Collider>().enabled, Is.True);
            Assert.That(barrier.LastFeedbackTextId, Is.EqualTo("text:barrier.closed"));
            yield return new WaitForSeconds(0.6f);
            Assert.That(barrier.VisualOpacity, Is.EqualTo(1f).Within(.000001f)); // Float lerp can yield 0.99999994.
            setActive.Invoke(flow, new object[] { false });
            Assert.That(gate.GetComponent<Collider>().enabled, Is.False);
            Assert.That(barrier.IsTransitioning, Is.True);
            Assert.That(barrier.LastFeedbackTextId, Is.EqualTo("text:barrier.open"));
            Assert.That(barrier.SoundSequence, Is.EqualTo(2));
            Assert.That(flow.GetComponent<AudioSource>().isPlaying, Is.True);
            yield return new WaitForSeconds(0.25f);
            Assert.That(barrier.VisualOpacity, Is.InRange(0.05f, 0.95f));
            yield return new WaitForSeconds(0.35f);
            Assert.That(gate.activeSelf, Is.False);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator ActualExecutionAdapter_ShowsFailuresThenHoldsTargetAndResolvesOnce()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var enemy = Object.FindObjectsOfType<MeleeEnemyActor>().First();
            foreach (var actor in Object.FindObjectsOfType<MeleeEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<RangedEnemyActor>()) actor.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<ShieldEnemyActor>()) actor.enabled = false;
            player.GetComponent<ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = enemy.transform.position + Vector3.back * 1.5f + Vector3.up;
            // Isolate the no-interactable rejection path; coexistence is tested separately above.
            foreach (var interaction in Object.FindObjectsOfType<InteractableBehaviour>())
                foreach (var collider in interaction.GetComponentsInChildren<Collider>()) collider.enabled = false;
            Physics.SyncTransforms();
            Assert.That(player.GetComponent<PlayerInteractor>().CurrentCandidate, Is.Null);
            Assert.That(player.TryHandleExecutionInput(), Is.True);
            Assert.That(player.FeedbackTextId, Is.EqualTo("text:execution.need-posture"));
            enemy.ApplyNeutralPostureDamage(999f);
            player.Model.Stamina.TrySpend(player.Model.Stamina.Current - 21f);
            Assert.That(player.TryHandleExecutionInput(), Is.True);
            Assert.That(player.FeedbackTextId, Is.EqualTo("text:execution.need-stamina"));
            player.Model.Stamina.RestoreFull();
            float before = player.Model.Stamina.Current;
            Assert.That(player.TryHandleExecutionInput(), Is.True);
            Assert.That(player.Model.State, Is.EqualTo(CombatState.Execution));
            Assert.That(player.Model.Stamina.Current, Is.EqualTo(before - 22f));
            Assert.That(player.Model.IsInvulnerable, Is.True);
            Assert.That(enemy.IsExecutionClaimed, Is.True);
            Assert.That(enemy.IsExecutionEligible, Is.False);
            player.Model.Tick(0.33f);
            yield return null;
            Assert.That(enemy.IsAvailable, Is.False);
            Assert.That(player.Model.ExecutionResolveSequence, Is.EqualTo(1));
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }

        [UnityTest]
        public IEnumerator TaskBarrier_OpensWithFadeSoundAndMessage_AndClosesAgain()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            yield return null;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var gate = GameObject.Find("GateBlocker_Sanctum");
            var owner = new GameObject("BarrierTestOwner");
            var barrier = M2StageBarrier.CreateManual(owner, gate);
            barrier.SetOpen(true);
            Assert.That(gate.GetComponent<Collider>().enabled, Is.False);
            Assert.That(barrier.IsTransitioning, Is.True);
            Assert.That(barrier.LastFeedbackTextId, Is.EqualTo("text:barrier.open"));
            Assert.That(barrier.SoundSequence, Is.EqualTo(1));
            Assert.That(owner.GetComponent<AudioSource>().isPlaying, Is.True);
            yield return new WaitForSeconds(0.25f);
            Assert.That(barrier.VisualOpacity, Is.InRange(0.1f, 0.9f));
            var properties = new MaterialPropertyBlock();
            gate.GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor").a, Is.InRange(0.01f, 0.6f));
            yield return new WaitForSeconds(0.3f);
            Assert.That(gate.activeSelf, Is.False);
            barrier.SetOpen(false);
            Assert.That(gate.GetComponent<Collider>().enabled, Is.True);
            Assert.That(barrier.LastFeedbackTextId, Is.EqualTo("text:barrier.closed"));
            yield return new WaitForSeconds(0.6f);
            Assert.That(gate.activeSelf, Is.True);
            Assert.That(barrier.VisualOpacity, Is.EqualTo(1f));
            Assert.That(barrier.SoundSequence, Is.EqualTo(2));
            Object.Destroy(owner);
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }
    }

    public sealed class ExecutionInteractionProbe : InteractableBehaviour
    {
        public int Count { get; private set; }
        public override ContentId PromptTextId => new ContentId("text:test.interaction");
        public override bool IsAvailable => true;
        public override bool TryInteract(InteractionContext context) { Count++; return true; }
    }
}
