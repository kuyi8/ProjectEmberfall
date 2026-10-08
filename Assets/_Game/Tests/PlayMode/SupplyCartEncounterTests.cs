#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Actual E/motor and actual attack query; controlled story setup is not human difficulty.</summary>
    public sealed class SupplyCartEncounterTests
    {
        M2RouteFlowController flow; PlayerCombatActor player; RouteEnrichmentInteractable cart;
        Keyboard keyboard; Gamepad pad; bool host; int beats;
        UnityEngine.Application.LogCallback observer;
        [UnitySetUp] public IEnumerator Begin()
        {
            keyboard = null; pad = null; host = false; beats = 0;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null; yield return null; Bind();
            Assert.That(flow.SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
            observer = (message, stack, type) => { if (message.StartsWith(PacingTelemetryRecorder.LogPrefix) && message.Contains("segment=abandoned-supply-cart event=claimed")) beats++; };
            UnityEngine.Application.logMessageReceived += observer;
        }
        void Bind()
        {
            flow = Object.FindObjectOfType<M2RouteFlowController>(); player = Object.FindObjectOfType<PlayerCombatActor>();
            cart = Object.FindObjectsOfType<RouteEnrichmentInteractable>().Single(i => i.Kind == RouteEnrichmentInteractionKind.SupplyCart);
        }
        [UnityTearDown] public IEnumerator End()
        {
            UnityEngine.Application.logMessageReceived -= observer;
            if (keyboard != null && keyboard.added) { InputSystem.ResetDevice(keyboard); InputSystem.RemoveDevice(keyboard); }
            if (pad != null && pad.added) { InputSystem.ResetDevice(pad); InputSystem.RemoveDevice(pad); }
            foreach (var probe in Object.FindObjectsOfType<SupplyCartDamageProbe>()) Object.Destroy(probe.gameObject);
            if (host) { SessionRuntime.Current.Shutdown(); yield return null; }
            Object.FindObjectOfType<M2RouteHud>()?.SetPaused(false);
            Time.timeScale = 1;
        }

        [UnityTest] public IEnumerator RealE_GrantsOneVisiblePersistentRewardAndRejectsRepeatOrInvalidRecipient()
        {
            Assert.That(flow.CanClaimSupplyCart(), Is.False); Assert.That(flow.TryClaimSupplyCart(null), Is.False);
            Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1)); Assert.That(flow.SupplyCartClaimed, Is.False);
            yield return ClaimWithE(true);
            Assert.That(flow.SupplyCartClaimed, Is.True); Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1.15f));
            Assert.That(flow.LastMessage, Does.Contain("+15%").And.Contain("生命伤害不变"));
            Assert.That(flow.SupplyCartStatus, Does.Contain("重击架势 +15%"));
            Assert.That(cart.HighlightState, Is.EqualTo(RouteHighlightState.Completed)); Assert.That(cart.IsAvailable, Is.False);
            Assert.That(flow.TryClaimSupplyCart(player), Is.False); Assert.That(cart.TryInteract(new InteractionContext(player)), Is.False);
            Assert.That(flow.TryClaimSupplyCart(null), Is.False); Assert.That(beats, Is.EqualTo(1));
            Assert.That(flow.WatchtowerDiscovered || flow.RiskRouteRewardClaimed || flow.AshApproachCleared || flow.AshGuardPassCleared, Is.False);
        }

        [UnityTest] public IEnumerator ActualAttackColliderQueryChangesOnlyHeavyPostureNotHealthDamage()
        {
            var target = new GameObject("SupplyCart_ControlledDamageProbe").AddComponent<SupplyCartDamageProbe>();
            var collider = target.gameObject.AddComponent<BoxCollider>(); collider.center = Vector3.up; collider.size = new Vector3(.6f, 1.8f, .6f);
            target.transform.position = player.transform.position + player.transform.forward * 1.1f; Physics.SyncTransforms();
            yield return Strike(target, false); DamageRequest before = target.Last;
            Assert.That(before.Tag, Is.EqualTo(AttackTag.Heavy)); Assert.That(before.PostureDamage, Is.EqualTo(60));
            yield return ClaimWithE();
            target.transform.position = player.transform.position + player.transform.forward * 1.1f; Physics.SyncTransforms();
            yield return Strike(target, false); DamageRequest after = target.Last;
            Assert.That(after.PostureDamage, Is.EqualTo(before.PostureDamage * 1.15f).Within(.0001f));
            Assert.That(after.RawDamage, Is.EqualTo(before.RawDamage));
            yield return Strike(target, true);
            Assert.That(target.Last.Tag, Is.EqualTo(AttackTag.Light)); Assert.That(target.Last.PostureDamage, Is.EqualTo(18));
            Object.Destroy(target.gameObject);
        }

        [UnityTest] public IEnumerator InArenaDeathAndContinueKeepClaimWithoutRegrantAndNewGameClearsIt()
        {
            yield return ClaimWithE(); Place(new Vector3(0, 0, 10.5f)); yield return null;
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Count(g => g.IsTelemetryActive), Is.EqualTo(1));
            Assert.That(player.ReceiveDamage(new DamageRequest(980, 1, 9999, 100, AttackTag.Hazard, false)).Killed, Is.True);
            Assert.That(flow.SupplyCartClaimed, Is.True); Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1.15f));
            yield return new WaitForSeconds(2.4f); Assert.That(player.IsAvailable, Is.True);
            Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1.15f)); flow.SaveSessionProgress();
            for (int reload = 0; reload < 2; reload++)
            {
                M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; Bind();
                Assert.That(flow.SupplyCartClaimed, Is.True); Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1.15f));
                Assert.That(flow.AshApproachCleared, Is.False); Assert.That(cart.IsAvailable, Is.False); Assert.That(beats, Is.EqualTo(1));
            }
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; Bind();
            Assert.That(flow.SupplyCartClaimed, Is.False); Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator NormalMotorCanReachCartRetreatAndEnterNextEncounterWithExactMeshCollisions()
        {
            Assert.That(flow.TryTalkToScout(), Is.True); pad = InputSystem.AddDevice<Gamepad>();
            var model = cart.transform.parent.Find("SupplyCart_VisualAndCollision");
            Assert.That(model.GetComponentInChildren<Renderer>().sharedMaterial.name, Is.EqualTo("M_SupplyCart_WornWood"));
            Assert.That(cart.transform.Find("InteractionMarker").GetComponent<Renderer>().enabled, Is.False,
                "The interaction anchor must not leave a greybox pillar next to the visible cart.");
            var meshes = model.GetComponentsInChildren<MeshFilter>(); Assert.That(meshes.Length, Is.GreaterThan(0));
            foreach (var mesh in meshes)
            {
                Assert.That(mesh.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(mesh.sharedMesh));
                Assert.That(mesh.GetComponent<MeshCollider>().isTrigger, Is.False);
            }
            Assert.That(cart.GetComponentsInChildren<Collider>().All(c => c.isTrigger), Is.True, "Interaction does not add an invisible solid wall.");
            yield return Walk(new Vector3(3.65f, 0, 4));
            Assert.That(Object.FindObjectOfType<PlayerInteractor>().CurrentCandidate, Is.SameAs(cart));
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Any(g => g.IsTelemetryActive), Is.False);
            keyboard = InputSystem.AddDevice<Keyboard>(); yield return PressE(); Assert.That(beats, Is.EqualTo(1));
            yield return Walk(new Vector3(3.65f, 0, 7)); yield return Walk(new Vector3(0, 0, 7.8f));
            yield return Walk(new Vector3(0, 0, 10.5f));
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(g => g.IsTelemetryActive).TelemetrySegment, Is.EqualTo("ash-approach-encounter"));
            yield return Walk(new Vector3(0, 0, 7.8f)); Assert.That(flow.DeathCount, Is.Zero);
        }

        [UnityTest] public IEnumerator CompletionShowsActualRewardWithoutResettingIt()
        {
            yield return ClaimWithE(); yield return CompleteStoryForControlledPresentation();
            Assert.That(flow.TryTalkToScout(), Is.True); yield return null;
            Assert.That(flow.IsComplete, Is.True); Assert.That(Object.FindObjectOfType<M2RouteHud>().IsCompletionPresented, Is.True);
            Assert.That(flow.SupplyCartStatus, Does.Contain("+15%")); Assert.That(player.HeavyPostureMultiplier, Is.EqualTo(1.15f));
            yield return Capture("cart-completion-results");
        }

        [UnityTest] public IEnumerator ListeningHostDisablesWholeCartAndOfflineGrantBeforeInitialization()
        {
            SessionRuntime.Current.Shutdown(); yield return null;
            Assert.That(SessionRuntime.Current.StartHost(47796), Is.True); host = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AbandonedSupplyCart_v1");
            Assert.That(root.activeSelf, Is.False); Assert.That(root.GetComponentsInChildren<Behaviour>(true).All(b => !b.enabled), Is.True);
            var offline = Object.FindObjectOfType<PlayerCombatActor>(true); Assert.That(offline.HeavyPostureMultiplier, Is.EqualTo(1));
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>(true).enabled, Is.False);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
        }

        IEnumerator ClaimWithE(bool capture = false)
        {
            Assert.That(flow.TryTalkToScout(), Is.True); keyboard = InputSystem.AddDevice<Keyboard>();
            Place(new Vector3(3.65f, 0, 4)); yield return null;
            Assert.That(Object.FindObjectOfType<PlayerInteractor>().CurrentCandidate, Is.SameAs(cart));
            if (capture) yield return Capture("cart-interaction-prompt");
            yield return PressE(); Assert.That(flow.SupplyCartClaimed, Is.True); Assert.That(beats, Is.EqualTo(1));
            if (capture) yield return Capture("cart-reward-receipt");
        }
        IEnumerator PressE()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        IEnumerator Strike(SupplyCartDamageProbe target, bool light)
        {
            // Public domain command drives real Update/physics; not a fabricated ReceiveDamage request.
            int count = target.Count; player.Model.Reset(); target.NewIdentity();
            if (light) player.Model.Submit(CombatCommand.LightAttack);
            else { player.Model.Submit(CombatCommand.HeavyPressed); player.Model.Submit(CombatCommand.HeavyReleased); }
            float deadline = Time.time + 2;
            while (target.Count == count && Time.time < deadline) yield return null;
            Assert.That(target.Count, Is.EqualTo(count + 1));
            while (player.Model.IsAttacking && Time.time < deadline) yield return null;
        }
        void Place(Vector3 feet)
        {
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            var body = player.GetComponent<CharacterController>(); body.enabled = false;
            player.transform.position = feet + offset + Vector3.up * .04f; body.enabled = true; Physics.SyncTransforms();
        }
        IEnumerator Walk(Vector3 destination)
        {
            float deadline = Time.time + 9;
            while (Vector3.ProjectOnPlane(player.NavigationFootPosition - destination, Vector3.up).magnitude > .3f && Time.time < deadline)
            {
                Vector3 direction = Vector3.ProjectOnPlane(destination - player.NavigationFootPosition, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward)) });
                Assert.That(player.IsAvailable, Is.True); Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Count(g => g.IsTelemetryActive), Is.LessThanOrEqualTo(1));
                yield return null;
            }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(Vector3.ProjectOnPlane(player.NavigationFootPosition - destination, Vector3.up).magnitude, Is.LessThan(.4f), "Actual collision/motor, no traversal warp.");
        }
        IEnumerator Capture(string name)
        {
            string folder = Path.GetFullPath("Builds/ArtReview/SupplyCart/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(folder);
            yield return new WaitForSecondsRealtime(.6f); yield return null;
            string path = folder + "/" + name + ".png"; ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 4;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }
        static void Kill(CombatTarget target, int sequence) => Assert.That(target.ReceiveDamage(new DamageRequest(979, sequence, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
        IEnumerator CompleteStoryForControlledPresentation()
        {
            // Valid public story/save facts, never claimed as natural combat or route completion.
            var context = new InteractionContext(player);
            Kill(Object.FindObjectsOfType<MeleeEnemyActor>().Single(e => e.name == "Enemy_Fogwalker_Forest"), 101);
            Kill(Object.FindObjectsOfType<RangedEnemyActor>().Single(e => e.name == "Enemy_RunePriest_Forest"), 102);
            Assert.That(flow.ForestTemplate.SigilPickup.TryInteract(context), Is.True);
            Assert.That(flow.TryActivateSeal(new ContentId("seal:forest")), Is.True);
            Assert.That(flow.ForestTemplate.EmberRune.TryInteract(context), Is.True);
            Assert.That(flow.TryActivateBridgeMechanism("bridge-mechanism:A"), Is.True);
            Place(new Vector3(12, 0, 44)); yield return null;
            foreach (var member in Object.FindObjectsOfType<RangedEnemyActor>().Where(e => e.name.Contains("Bridge"))) Kill(member, 103 + member.CombatantId);
            yield return null; Assert.That(flow.BridgeEncounterCleared, Is.True);
            Assert.That(flow.TryActivateBridgeMechanism("bridge-mechanism:B"), Is.True);
            Assert.That(flow.TryActivateSeal(new ContentId("seal:bridge")), Is.True);
            Assert.That(Object.FindObjectsOfType<ShieldEnemyActor>().Single(e => e.name == "Enemy_RuinGuard_Courtyard").ApplyNeutralPostureDamage(999), Is.GreaterThan(0));
            Assert.That(flow.TryActivateSeal(new ContentId("seal:courtyard")), Is.True); Assert.That(flow.TryEnterSanctum(), Is.True);
            Place(flow.Warden.transform.position + Vector3.back * 5.5f);
            float deadline = Time.time + 1;
            while (!flow.IsWardenEncounterActive && Time.time < deadline) yield return null;
            Assert.That(flow.IsWardenEncounterActive, Is.True); Assert.That(flow.Warden.ApplyNeutralPostureDamage(999), Is.GreaterThan(0));
            Assert.That(flow.Warden.ReceiveDamage(new DamageRequest(player.CombatantId, 10, 9999, 100, AttackTag.Heavy)).Killed, Is.False);
            deadline = Time.time + 6;
            while (flow.Warden.Phase == WardenPhase.Transition && Time.time < deadline) yield return null;
            Assert.That(flow.Warden.Phase, Is.EqualTo(WardenPhase.PhaseTwo));
            Place(new Vector3(0, 0, 0));
            Assert.That(flow.Warden.ReceiveDamage(new DamageRequest(player.CombatantId, 11, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
            yield return null; Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.ReturnToScout));
        }
    }

    public sealed class SupplyCartDamageProbe : CombatTarget
    {
        int identity = 200000; public int Count { get; private set; } public DamageRequest Last { get; private set; }
        public void NewIdentity() => identity++;
        public override int CombatantId => identity;
        public override Transform AimPoint => transform;
        public override bool IsAvailable => true;
        public override float HealthNormalized => 1;
        public override DamageResult ReceiveDamage(DamageRequest request)
        { Last = request; Count++; return new DamageResult(true, false, request.RawDamage, false, postureDamageApplied: request.PostureDamage); }
    }
}
#endif
