#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Real E/movement plus controlled lifecycle hits; never natural difficulty/duration acceptance.</summary>
    public sealed class AshReinforcementChoiceTests
    {
        M2RouteFlowController flow;
        PlayerCombatActor player;
        CombatEncounterCoordinator group;
        RouteChoiceEncounterModifier modifier;
        ShieldEnemyActor guard;
        SummonerEnemyActor caller;
        GameObject root, walls;
        Keyboard keyboard; Gamepad pad;
        bool host; int choiceBeats;
        UnityEngine.Application.LogCallback observer;

        [UnitySetUp] public IEnumerator Begin()
        {
            // NUnit reuses this fixture instance; counters and removed device references must
            // not leak between separate new-game tests.
            keyboard = null; pad = null; host = false; choiceBeats = 0; observer = null;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            Bind(); Assert.That(flow.SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
            observer = (message, stack, type) => { if (message.StartsWith(PacingTelemetryRecorder.LogPrefix) && message.Contains("segment=reinforcement-choice_")) choiceBeats++; };
            UnityEngine.Application.logMessageReceived += observer;
        }
        void Bind()
        {
            flow = Object.FindObjectOfType<M2RouteFlowController>(); player = Object.FindObjectOfType<PlayerCombatActor>();
            root = GameObject.Find("Content_AshGuardPass_v1"); group = root.GetComponent<CombatEncounterCoordinator>();
            modifier = root.GetComponent<RouteChoiceEncounterModifier>(); guard = root.GetComponentInChildren<ShieldEnemyActor>(true);
            caller = root.GetComponentInChildren<SummonerEnemyActor>(true); walls = root.transform.Find("AshReinforcement_NarrowWalls").gameObject;
            Assert.That(modifier, Is.Not.Null);
        }
        [UnityTearDown] public IEnumerator End()
        {
            if (observer != null) UnityEngine.Application.logMessageReceived -= observer;
            if (keyboard != null) { InputSystem.ResetDevice(keyboard); InputSystem.RemoveDevice(keyboard); }
            if (pad != null) { InputSystem.ResetDevice(pad); InputSystem.RemoveDevice(pad); }
            if (host) { SessionRuntime.Current.Shutdown(); yield return null; }
        }

        [UnityTest] public IEnumerator Unselected_PreservesVerifiedBAndDoesNotForgeAChoiceBeat()
        {
            Assert.That(flow.ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.None));
            string message = flow.LastMessage;
            flow.NotifyReinforcementArrival(false); flow.NotifyReinforcementArrival(true);
            Assert.That(flow.LastMessage, Is.EqualTo(message), "Unselected route must not invent a choice receipt.");
            Assert.That(group.MeleeMemberCount, Is.EqualTo(1)); Assert.That(group.SummonerMemberCount, Is.EqualTo(1));
            Assert.That(group.RangedMemberCount, Is.Zero); Assert.That(group.ArenaHalfExtents, Is.EqualTo(new Vector2(4.2f, 3.35f)));
            Assert.That(group.ArenaCenter, Is.EqualTo(new Vector3(0, 0, 21.75f))); Assert.That(group.TelemetryActivationMargin, Is.EqualTo(.15f));
            Assert.That(Vector3.ProjectOnPlane(guard.transform.position - new Vector3(-1.6f, 0, 21.2f), Vector3.up).magnitude, Is.LessThan(.005f));
            Assert.That(Vector3.ProjectOnPlane(caller.transform.position - new Vector3(1.8f, 0, 24.2f), Vector3.up).magnitude, Is.LessThan(.005f));
            Assert.That(walls.activeSelf, Is.False); Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False);
            Assert.That(modifier.Reinforcement.Brain, Is.Null); Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(2));
            Place(new Vector3(0, 0, 21)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.True); Assert.That(choiceBeats, Is.Zero);
            Kill(caller, 1); Kill(guard, 2); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.True, "Unselected still requires only the original owner and guard.");
            Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False); Assert.That(choiceBeats, Is.Zero);
        }

        [UnityTest] public IEnumerator RealE_StagedChoiceHasTwoThenOneAndNoSameFrameFalseClear()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged);
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(2)); Assert.That(group.MeleeMemberCount, Is.EqualTo(2));
            Assert.That(walls.activeSelf, Is.True); Assert.That(group.ArenaHalfExtents.x, Is.EqualTo(2.6f));
            Assert.That(SummonerEnemyDefinition.MaximumLivingSummons, Is.EqualTo(2)); Assert.That(group.MaximumConcurrentMeleeAttackers, Is.EqualTo(1));
            AssertNarrowGeometry(); Place(new Vector3(0, 0, 21)); yield return null;
            Kill(caller, 3); Kill(guard, 4);
            Assert.That(flow.AshGuardPassCleared, Is.False); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.False, "Required pending member prevents a false clear even when both opening members died together.");
            yield return WaitForWave(); Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(1));
            Assert.That(flow.LastMessage, Does.Contain("窄口分批").And.Contain("已到场"));
            Assert.That(modifier.Reinforcement.Definition.Id.Value, Is.EqualTo("enemy:fogwalker"));
            // A health-dead actor can still be in the first frames of its real death clip.
            // Freeze ONLY the living wave for this controlled art observation, not traversal.
            modifier.Reinforcement.SetSimulationAuthority(false);
            yield return WaitForSettledDeaths();
            yield return Capture("staged-arrived");
            modifier.Reinforcement.SetSimulationAuthority(true);
            Kill(modifier.Reinforcement, 5); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.True); Assert.That(choiceBeats, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator RealE_TogetherChoiceHasThreeAtOnceAndRequiresAllThreeDeaths()
        {
            yield return ChooseWithE(AshReinforcementChoice.Together, true);
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(3)); Assert.That(walls.activeSelf, Is.False);
            string message = flow.LastMessage;
            flow.NotifyReinforcementArrival(false); flow.NotifyReinforcementArrival(true);
            Assert.That(flow.LastMessage, Is.EqualTo(message), "Together choice must not claim a later staged arrival.");
            Assert.That(group.ArenaHalfExtents.x, Is.EqualTo(4.2f)); Assert.That(group.MaximumConcurrentMeleeAttackers, Is.EqualTo(1));
            Assert.That(SummonerEnemyDefinition.MaximumLivingSummons, Is.EqualTo(2));
            Place(new Vector3(0, 0, 21)); yield return null;
            caller.SetSimulationAuthority(false); guard.SetSimulationAuthority(false); modifier.Reinforcement.SetSimulationAuthority(false);
            yield return Capture("together-three");
            caller.SetSimulationAuthority(true); Kill(caller, 6); guard.SetSimulationAuthority(true); Kill(guard, 7); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.False); Assert.That(modifier.Reinforcement.IsAvailable, Is.True, "Ordinary reinforcement is NOT an owner-bound spirit.");
            modifier.Reinforcement.SetSimulationAuthority(true); Kill(modifier.Reinforcement, 8); yield return null;
            Assert.That(flow.AshGuardPassCleared, Is.True); Assert.That(choiceBeats, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator Staged_ProximityDefersAndReentryNeitherLosesNorRepeatsWave()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged);
            caller.SetSimulationAuthority(false); Place(modifier.Reinforcement.transform.position); yield return null; Kill(guard, 9);
            yield return new WaitForSeconds(.9f);
            Assert.That(modifier.ReinforcementPending, Is.True); Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False);
            Assert.That(root.transform.Find("AshReinforcement_ArrivalCue").gameObject.activeSelf, Is.True);
            Place(new Vector3(0, 0, 26.15f)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.False); Assert.That(modifier.ReinforcementPending, Is.True);
            Place(new Vector3(0, 0, 20.5f)); yield return WaitForWave(); int id = modifier.Reinforcement.GetInstanceID();
            Kill(modifier.Reinforcement, 10); yield return null;
            Place(new Vector3(0, 0, 26.15f)); yield return null;
            Place(new Vector3(0, 0, 20.5f)); yield return new WaitForSeconds(1f);
            Assert.That(modifier.Reinforcement.GetInstanceID(), Is.EqualTo(id)); Assert.That(modifier.Reinforcement.Brain.Health.IsDead, Is.True);
            Assert.That(modifier.ReinforcementPending, Is.False); Assert.That(flow.AshGuardPassCleared, Is.False);
            Assert.That(flow.TryChooseReinforcement(AshReinforcementChoice.Together), Is.False);
        }

        [UnityTest] public IEnumerator Staged_InArenaDeathResetsWaveButPreservesChoiceAndWalls()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged); caller.SetSimulationAuthority(false);
            Place(new Vector3(0, 0, 21)); yield return null; Kill(guard, 11); yield return WaitForWave();
            int id = modifier.Reinforcement.GetInstanceID(); Kill(player, 12);
            Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False, "Reset latch and activation agree in the actual death callback frame.");
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(2)); yield return null;
            Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False); Assert.That(guard.Brain.Health.IsDead, Is.False);
            Assert.That(modifier.Reinforcement.Brain.Health.IsDead, Is.False); Assert.That(walls.activeSelf, Is.True);
            Assert.That(flow.ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.Staged));
            yield return new WaitForSeconds(2.3f); Assert.That(player.IsAvailable, Is.True);
            Place(new Vector3(0, 0, 21)); yield return null; Kill(guard, 13); yield return WaitForWave();
            Assert.That(modifier.Reinforcement.GetInstanceID(), Is.EqualTo(id)); Assert.That(choiceBeats, Is.EqualTo(1));
            Assert.That(flow.AshGuardPassCleared, Is.False);
        }

        [UnityTest] public IEnumerator Staged_LoadAppliesGeometryBeforeFirstUpdateAndClearRemainsIndependent()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged);
            for (int reload = 0; reload < 2; reload++)
            {
                M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; Bind();
                Assert.That(flow.ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.Staged)); Assert.That(walls.activeSelf, Is.True);
                Assert.That(modifier.AppliedBeforeEncounterFirstUpdate, Is.True); Assert.That(group.FirstUpdateArenaHalfExtents, Is.EqualTo(new Vector2(2.6f, 3.35f)));
                Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False);
            }
            Place(new Vector3(0, 0, 21)); yield return null; Kill(caller, 14); Kill(guard, 15); yield return WaitForWave();
            Kill(modifier.Reinforcement, 16); yield return null;
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; Bind();
            Assert.That(flow.AshGuardPassCleared, Is.True); Assert.That(flow.AshApproachCleared, Is.False); Assert.That(flow.AshReturnCleared, Is.False);
            Assert.That(root.GetComponentsInChildren<CombatTarget>(true).All(t => !t.gameObject.activeInHierarchy), Is.True);
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.Zero); Assert.That(walls.activeSelf, Is.True);
        }

        [UnityTest] public IEnumerator Together_ContinueRestoresThreeAndDoesNotRefireChoiceBeat()
        {
            yield return ChooseWithE(AshReinforcementChoice.Together);
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; Bind();
            Assert.That(flow.ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.Together));
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(3)); Assert.That(modifier.ReinforcementPending, Is.False);
            Assert.That(modifier.AppliedBeforeEncounterFirstUpdate, Is.True); Assert.That(walls.activeSelf, Is.False);
            Assert.That(flow.TryChooseReinforcement(AshReinforcementChoice.Staged), Is.False); Assert.That(choiceBeats, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator Staged_NavigationActivationFaultRetriesWithoutDroppingRequiredMember()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged); Place(new Vector3(0, 0, 21)); yield return null;
            var agent = modifier.Reinforcement.GetComponent<NavMeshAgent>(); agent.enabled = false;
            LogAssert.Expect(LogType.Warning, "EMBERFALL_REINFORCEMENT_WAIT reason=navigation-unavailable failures=1; required member retained, bounded retries active");
            Kill(caller, 17); Kill(guard, 18); yield return new WaitForSeconds(1f);
            Assert.That(modifier.ReinforcementActivationFailures, Is.GreaterThan(0)); Assert.That(modifier.ReinforcementPending, Is.True);
            Assert.That(flow.AshGuardPassCleared, Is.False); Assert.That(group.MeleeMemberCount, Is.EqualTo(2));
            Assert.That(flow.LastMessage, Does.Contain("等待导航恢复"));
            Assert.That(modifier.Reinforcement.gameObject.activeSelf, Is.False);
            agent.enabled = true; yield return WaitForWave(); Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator NormalInput_NarrowPassageKeepsRetreatAndCarvingDoesNotLeakOnReload()
        {
            yield return ChooseWithE(AshReinforcementChoice.Staged); pad = InputSystem.AddDevice<Gamepad>();
            yield return Walk(new Vector3(0, 0, 21.3f)); Assert.That(group.IsTelemetryActive, Is.True);
            yield return Walk(new Vector3(0, 0, 17.7f)); Assert.That(group.IsTelemetryActive, Is.False);
            Assert.That(flow.DeathCount, Is.Zero); Assert.That(flow.AshGuardPassCleared, Is.False);
            Assert.That(NavMesh.Raycast(new Vector3(0, 0, 22), new Vector3(3.5f, 0, 22), out NavMeshHit hit, NavMesh.AllAreas), Is.True);
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null; yield return null; Bind();
            Assert.That(walls.activeSelf, Is.False); Assert.That(group.ArenaHalfExtents.x, Is.EqualTo(4.2f));
            Assert.That(NavMesh.Raycast(new Vector3(0, 0, 22), new Vector3(3.5f, 0, 22), out hit, NavMesh.AllAreas), Is.False, "Owned carving must not leave holes after new-game reload.");
        }

        [UnityTest] public IEnumerator ListeningHost_DisablesChoiceAndFutureMemberBeforeOfflineAwake()
        {
            SessionRuntime.Current.Shutdown(); yield return null;
            Assert.That(SessionRuntime.Current.StartHost(47794), Is.True); host = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var choices = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshReinforcementChoice_v1");
            Assert.That(choices.activeSelf, Is.False); Assert.That(choices.GetComponentsInChildren<Behaviour>(true).All(b => !b.enabled), Is.True);
            var variant = Object.FindObjectsOfType<RouteChoiceEncounterModifier>(true).Single(m => m.IsReinforcementVariant);
            Assert.That(variant.enabled, Is.False); Assert.That(variant.Reinforcement.Brain, Is.Null);
            Assert.That(variant.Reinforcement.GetComponentsInChildren<Behaviour>(true).All(b => !b.enabled), Is.True);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
        }

        IEnumerator ChooseWithE(AshReinforcementChoice choice, bool capturePrompt = false)
        {
            Assert.That(flow.TryTalkToScout(), Is.True); keyboard = InputSystem.AddDevice<Keyboard>();
            var kind = choice == AshReinforcementChoice.Staged ? RouteEnrichmentInteractionKind.StagedReinforcement : RouteEnrichmentInteractionKind.TogetherReinforcement;
            var interactable = Object.FindObjectsOfType<RouteEnrichmentInteractable>().Single(i => i.Kind == kind);
            Place(interactable.transform.position); yield return null;
            Assert.That(Object.FindObjectOfType<PlayerInteractor>().CurrentCandidate, Is.SameAs(interactable));
            if (capturePrompt) yield return Capture("choice-input-prompt");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(flow.ReinforcementChoice, Is.EqualTo(choice)); Assert.That(flow.CanChooseReinforcement(), Is.False);
            Assert.That(choiceBeats, Is.EqualTo(1)); Assert.That(group.IsTelemetryActive, Is.False, "Choose in the real neutral strip before combat.");
        }
        void Place(Vector3 feet)
        {
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            var body = player.GetComponent<CharacterController>(); body.enabled = false;
            player.transform.position = feet + offset + Vector3.up * .04f; body.enabled = true; Physics.SyncTransforms();
        }
        static void Kill(CombatTarget target, int sequence)
        {
            if (target is ShieldEnemyActor shield) shield.ApplyNeutralPostureDamage(999f);
            Assert.That(target.ReceiveDamage(new DamageRequest(998, sequence, 9999f, 100f, AttackTag.Heavy)).Killed, Is.True, target.name);
        }
        IEnumerator WaitForWave()
        {
            float deadline = Time.time + 3f;
            while (!modifier.Reinforcement.gameObject.activeSelf && Time.time < deadline) yield return null;
            Assert.That(modifier.Reinforcement.isActiveAndEnabled && modifier.Reinforcement.IsAvailable, Is.True,
                $"pending={modifier.ReinforcementPending} failures={modifier.ReinforcementActivationFailures} player={player.NavigationFootPosition}");
            Assert.That(modifier.Reinforcement.GetComponent<NavMeshAgent>().isOnNavMesh, Is.True); Assert.That(modifier.ReinforcementPending, Is.False);
        }
        void AssertNarrowGeometry()
        {
            var masonry = walls.GetComponentsInChildren<Transform>().Where(t => t.name == "Masonry").ToArray(); Assert.That(masonry.Length, Is.EqualTo(2));
            foreach (var wall in masonry)
            {
                var collider = wall.GetComponentsInChildren<Collider>().Single(c => !c.isTrigger); var renderer = wall.GetComponentsInChildren<Renderer>().Single();
                Assert.That(Vector3.Distance(collider.bounds.center, renderer.bounds.center), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(collider.bounds.size, renderer.bounds.size), Is.LessThan(.001f));
                Assert.That(collider.bounds.min.z, Is.EqualTo(18.75f).Within(.001f)); Assert.That(collider.bounds.max.z, Is.EqualTo(25.1f).Within(.001f));
            }
        }
        IEnumerator Walk(Vector3 destination)
        {
            float deadline = Time.time + 8f;
            while (Vector3.ProjectOnPlane(player.transform.position - destination, Vector3.up).magnitude > .3f && Time.time < deadline)
            {
                Vector3 direction = Vector3.ProjectOnPlane(destination - player.transform.position, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) });
                Assert.That(player.IsAvailable, Is.True); Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Count(c => c.IsTelemetryActive), Is.LessThanOrEqualTo(1));
                yield return null;
            }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(Vector3.ProjectOnPlane(player.transform.position - destination, Vector3.up).magnitude, Is.LessThan(.4f), "Normal motor/collision, no traversal warps or invulnerability.");
        }
        IEnumerator Capture(string name)
        {
            string folder = Path.GetFullPath("Builds/ArtReview/AshReinforcement/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(folder);
            // Advance actual LateUpdate/GPU frames after the controlled setup placement.
            yield return new WaitForSeconds(.6f); yield return null;
            string path = folder + "/" + name + ".png"; ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 4f; while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }
        IEnumerator WaitForSettledDeaths()
        {
            var ownerAnimator = caller.GetComponentInChildren<Animator>();
            var guardAnimator = guard.GetComponentInChildren<Animator>();
            float deadline = Time.time + 5f;
            while ((!Settled(ownerAnimator) || !Settled(guardAnimator)) && Time.time < deadline) yield return null;
            Assert.That(caller.Brain.Health.IsDead && guard.Brain.Health.IsDead, Is.True);
            Assert.That(Settled(ownerAnimator), Is.True, "Owner death must progress in real PlayerLoop frames.");
            Assert.That(Settled(guardAnimator), Is.True, "Guard death must progress in real PlayerLoop frames.");
            Assert.That(modifier.ActualAvailableAuthoredCount, Is.EqualTo(1));
        }
        static bool Settled(Animator animator)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);
            return state.IsName("Dead") && state.normalizedTime >= .98f && !animator.IsInTransition(0);
        }
    }
}
#endif
