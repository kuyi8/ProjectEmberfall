using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    // Disposable observation/adapter contracts only, not PlayerLoop, animation evaluation, contact, HitStop or NET evidence.
    public sealed class OfflineHitReactReentryPolicyTests
    {
        const string SetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene _preview;
        int _activeScene;
        PlayerAnimationSet _set, _production;
        AnimationClip _clip;
        CombatTuningAsset _tuning;
        Rig _rig;
        readonly List<Object> _ownedAssets = new List<Object>();

        sealed class Rig
        {
            public PlayerCombatActor actor;
            public Animator animator;
            public ThirdPersonMotor motor;
            public PlayerAnimationPresenter presenter;
        }

        static IEnumerable<CombatState> NonHitReactStates =>
            Enum.GetValues(typeof(CombatState)).Cast<CombatState>().Where(state => state != CombatState.HitReact);

        [SetUp]
        public void Setup()
        {
            Assert.That(UnityEngine.Application.isPlaying, Is.False);
            _activeScene = SceneManager.GetActiveScene().handle;
            _preview = EditorSceneManager.NewPreviewScene();
            _production = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
            Assert.That(_production != null && _production.Controller != null, Is.True);
            _set = Own(ScriptableObject.CreateInstance<PlayerAnimationSet>());
            _clip = Own(new AnimationClip { name = "Disposable_HitReact_Observation" });
            SetField(_set, "_controller", _production.Controller);
            SetField(_set, "_hitReact", _clip);
            _tuning = Own(ScriptableObject.CreateInstance<CombatTuningAsset>());
            _rig = CreateRig("HitReact_Observation_P1");
            Assert.That(_rig.presenter.IsConfigured, Is.True);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_preview.IsValid()) EditorSceneManager.ClosePreviewScene(_preview);
            foreach (Object asset in _ownedAssets) if (asset != null) Object.DestroyImmediate(asset);
            _ownedAssets.Clear();
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(_activeScene));
        }

        T Own<T>(T asset) where T : Object
        {
            asset.hideFlags = HideFlags.HideAndDontSave;
            _ownedAssets.Add(asset);
            return asset;
        }

        Rig CreateRig(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, _preview);
            var visual = new GameObject("Disposable_Animator");
            visual.transform.SetParent(root.transform, false);
            var rig = new Rig { actor = root.AddComponent<PlayerCombatActor>(), animator = visual.AddComponent<Animator>(),
                motor = root.AddComponent<ThirdPersonMotor>(), presenter = root.AddComponent<PlayerAnimationPresenter>() };
            rig.actor.Configure(null, _tuning, root.transform, root.transform, null, null);
            SetModel(rig.actor, new CombatStateMachine(_tuning.CreateRuntimeCopy()));
            rig.presenter.Configure(rig.animator, rig.actor, rig.motor, _set);
            return rig;
        }

        static void SetField(Object target, string field, object value) =>
            target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
        static void SetModel(PlayerCombatActor actor, CombatStateMachine model) => SetField(actor, "_model", model);
        static void SetPresented(PlayerAnimationPresenter presenter, CombatState state) => SetField(presenter, "_presentedState", state);
        static void SetTruthTableState(CombatStateMachine model, CombatState state) =>
            typeof(CombatStateMachine).GetProperty(nameof(CombatStateMachine.State)).GetSetMethod(true).Invoke(model, new object[] { state });
        static bool Observe(PlayerAnimationPresenter presenter) =>
            (bool)typeof(PlayerAnimationPresenter).GetMethod("ObserveHitReactReentry", PrivateInstance).Invoke(presenter, null);
        static void Lifecycle(PlayerAnimationPresenter presenter, string method) =>
            typeof(PlayerAnimationPresenter).GetMethod(method, PrivateInstance).Invoke(presenter, null);
        static DamageResult Hit(PlayerCombatActor actor, int attackSequence = 7, bool lethal = false) =>
            actor.ReceiveDamage(new DamageRequest(830001, attackSequence, lethal ? 1000f : 12f, 0f, AttackTag.Light, false));

        void PrimeSameStateObservation()
        {
            _set.ConfigureOfflineHitReactReentry(true);
            Assert.That(Observe(_rig.presenter), Is.False, "Enabling establishes a baseline, never a historical replay.");
            Assert.That(Hit(_rig.actor).Accepted, Is.True);
            Assert.That(Observe(_rig.presenter), Is.False, "First state entry still belongs to the original Presenter path.");
            SetPresented(_rig.presenter, CombatState.HitReact); // Observation truth-table only; not animation evaluation.
            _rig.actor.Model.Tick(.18f);
        }

        [Test]
        public void DefaultOffAndMissingClipRemainIndependentFromRangedOptions()
        {
            Assert.That(_set.RestartOfflineHitReactOnAcceptedDamage, Is.False);
            _set.ConfigureOfflineRangedEntryTime(true);
            _set.ConfigureOfflineRangedMotionContinuity(true);
            Assert.That(_set.RestartOfflineHitReactOnAcceptedDamage, Is.False);
            _set.ConfigureOfflineHitReactReentry(true);
            Assert.That(_set.RestartOfflineHitReactOnAcceptedDamage, Is.True);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.HitReact, .18f, 1.5f), Is.Zero);
            SetField(_set, "_hitReact", null);
            Assert.That(_set.RestartOfflineHitReactOnAcceptedDamage, Is.False);
        }

        [Test]
        public void EachOrdinaryAcceptedResetPublishesBeforeProgressCallbacksWithoutAttackerDedup()
        {
            var observed = new List<ulong>();
            _rig.actor.CombatProgressed += (actor, kind) =>
            {
                Assert.That(actor, Is.SameAs(_rig.actor));
                Assert.That(kind, Is.EqualTo(CombatProgressKind.DamageReceived));
                Assert.That(actor.Model.State, Is.EqualTo(CombatState.HitReact));
                Assert.That(actor.Model.StateElapsed, Is.Zero);
                observed.Add(actor.HitReactPresentationSequence);
            };
            float health = _rig.actor.Model.Health.Current;
            DamageResult first = Hit(_rig.actor, 7);
            _rig.actor.Model.Tick(.18f);
            DamageResult second = Hit(_rig.actor, 7); // Existing domain accepts both; presentation must not redefine a hit.
            Assert.That(first.Accepted && second.Accepted && !first.Killed && !second.Killed, Is.True);
            Assert.That(observed, Is.EqualTo(new ulong[] { 1, 2 }));
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(2UL));
            Assert.That(_rig.actor.Model.Health.Current, Is.EqualTo(health - first.AppliedDamage - second.AppliedDamage));
            Assert.That(_rig.actor.Model.AttackSequence, Is.Zero, "Victim presentation ordinal is not its attacker sequence.");
        }

        [Test]
        public void ReentrantAcceptedDamagePublishesItsOwnOrdinalBeforeItsCallbacks()
        {
            var observed = new List<ulong>();
            bool nested = false;
            _rig.actor.CombatProgressed += (actor, _) =>
            {
                observed.Add(actor.HitReactPresentationSequence);
                if (nested) return;
                nested = true;
                Assert.That(Hit(actor).Accepted, Is.True);
            };
            Hit(_rig.actor);
            Assert.That(observed, Is.EqualTo(new ulong[] { 1, 2 }));
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(2UL));
        }

        [TestCase("null-model")]
        [TestCase("dead-ignored")]
        [TestCase("ordinary-guard")]
        [TestCase("guard-break")]
        [TestCase("guard-killed")]
        [TestCase("ordinary-dodge")]
        [TestCase("execution-invulnerable")]
        public void NonHitReactOutcomesDoNotPublishVictimReentryFacts(string kind)
        {
            var model = _rig.actor.Model;
            DamageRequest request = new DamageRequest(830001, 7, 12f, 0f, AttackTag.Light, true, true);
            if (kind == "null-model") SetModel(_rig.actor, null);
            else if (kind == "dead-ignored") model.ForceDeath();
            else if (kind == "ordinary-dodge") { Assert.That(model.Submit(CombatCommand.Dodge), Is.True); model.Tick(.19f); }
            else if (kind == "execution-invulnerable") Assert.That(model.Submit(CombatCommand.Execution), Is.True);
            else
            {
                Assert.That(model.Submit(CombatCommand.GuardPressed), Is.True);
                model.Tick(.21f); // Non-perfect: avoid creating VFX in the user's active scene from an Edit fixture.
                if (kind == "guard-break") request = new DamageRequest(830001, 7, 12f, 1000f, AttackTag.Light, true, true);
                if (kind == "guard-killed") request = new DamageRequest(830001, 7, 10000f, 0f, AttackTag.Light, true, true);
            }
            DamageResult result = _rig.actor.ReceiveDamage(request);
            if (kind == "ordinary-guard") Assert.That(result.Defended && result.Accepted && !result.GuardBroken && !result.Killed, Is.True);
            else if (kind == "guard-break") Assert.That(result.Defended && result.GuardBroken && !result.Killed, Is.True);
            else if (kind == "guard-killed") Assert.That(result.Defended && result.Killed, Is.True);
            else if (kind == "ordinary-dodge" || kind == "execution-invulnerable") Assert.That(result.Invulnerable && !result.Accepted, Is.True);
            else Assert.That(result.Accepted, Is.False);
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.Zero);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void BehindOrUndefendableGuardHitsStillPublishTheirActualHitReactReset(bool defendable, bool front)
        {
            Assert.That(_rig.actor.Model.Submit(CombatCommand.GuardPressed), Is.True);
            DamageResult result = _rig.actor.ReceiveDamage(new DamageRequest(830001, 7, 12f, 0f, AttackTag.Light, defendable, front));
            Assert.That(result.Accepted && !result.Defended && !result.Killed, Is.True);
            Assert.That(_rig.actor.Model.State, Is.EqualTo(CombatState.HitReact));
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(1UL));
        }

        [Test]
        public void SameStateResetIsConsumedOnceAndSeveralUnobservedHitsCoalesce()
        {
            PrimeSameStateObservation();
            Hit(_rig.actor); Hit(_rig.actor);
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(3UL));
            Assert.That(Observe(_rig.presenter), Is.True);
            Assert.That(Observe(_rig.presenter), Is.False);
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
        }

        [Test]
        public void DefaultOffObservationsAndFlagEnableDoNotReplayPreviousResets()
        {
            SetPresented(_rig.presenter, CombatState.HitReact);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.False);
            _set.ConfigureOfflineHitReactReentry(true);
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
            _set.ConfigureOfflineHitReactReentry(false);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.False);
            _set.ConfigureOfflineHitReactReentry(true);
            Assert.That(Observe(_rig.presenter), Is.False);
        }

        [TestCaseSource(nameof(NonHitReactStates))]
        public void EveryOtherCurrentDomainStateConsumesWithoutADeferredHitReplay(CombatState state)
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            SetTruthTableState(_rig.actor.Model, state); // Pure observation truth-table, not legal-transition/input evidence.
            Assert.That(Observe(_rig.presenter), Is.False);
            SetTruthTableState(_rig.actor.Model, CombatState.HitReact);
            Assert.That(Observe(_rig.presenter), Is.False);
        }

        [Test]
        public void ASubsequentLethalResultWinsWithoutPublishingOrDeferringAnotherReaction()
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            ulong beforeDeath = _rig.actor.HitReactPresentationSequence;
            Assert.That(Hit(_rig.actor, lethal: true).Killed, Is.True);
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(beforeDeath));
            Assert.That(Observe(_rig.presenter), Is.False);
            _rig.actor.Model.Reset(); SetPresented(_rig.presenter, CombatState.Locomotion);
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(beforeDeath), "Reset never rewinds an adapter ordinal.");
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.False, "Fresh post-reset first state entry is still the original entry path.");
        }

        [Test]
        public void AnotherActorsAcceptedDamageCannotRestartThisPresenter()
        {
            PrimeSameStateObservation(); Rig second = CreateRig("HitReact_Observation_P2");
            Hit(second.actor); Hit(second.actor);
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
        }

        [TestCase("actor")]
        [TestCase("model")]
        [TestCase("animator")]
        [TestCase("set")]
        public void BindingIdentityChangesEstablishANewBaselineWithoutHistory(string binding)
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            Rig second = CreateRig("HitReact_Observation_Binding_P2");
            if (binding == "actor")
            {
                Hit(second.actor);
                SetField(_rig.presenter, "_combat", second.actor);
            }
            else if (binding == "model")
            {
                SetModel(_rig.actor, new CombatStateMachine(_tuning.CreateRuntimeCopy()));
                Hit(_rig.actor);
            }
            else if (binding == "animator") SetField(_rig.presenter, "_animator", second.animator);
            else
            {
                var secondSet = Own(Object.Instantiate(_set));
                SetField(_rig.presenter, "_animationSet", secondSet);
            }
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(binding == "actor" ? second.actor : _rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
        }

        [TestCase("motor")]
        [TestCase("controller")]
        [TestCase("actor-null")]
        [TestCase("model-null")]
        [TestCase("animator-null")]
        [TestCase("set-null")]
        [TestCase("hit-clip-null")]
        [TestCase("actor-disabled")]
        [TestCase("animator-disabled")]
        [TestCase("presenter-disabled")]
        public void InvalidObservationsDrainAndRestoringReadinessDoesNotReplay(string kind)
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            CombatStateMachine model = _rig.actor.Model;
            if (kind == "motor") SetField(_rig.presenter, "_motor", null);
            else if (kind == "controller") SetField(_set, "_controller", null);
            else if (kind == "actor-null") SetField(_rig.presenter, "_combat", null);
            else if (kind == "model-null") SetModel(_rig.actor, null);
            else if (kind == "animator-null") SetField(_rig.presenter, "_animator", null);
            else if (kind == "set-null") SetField(_rig.presenter, "_animationSet", null);
            else if (kind == "hit-clip-null") SetField(_set, "_hitReact", null);
            else if (kind == "actor-disabled") _rig.actor.enabled = false;
            else if (kind == "animator-disabled") _rig.animator.enabled = false;
            else _rig.presenter.enabled = false;
            Assert.That(Observe(_rig.presenter), Is.False);
            SetField(_rig.presenter, "_motor", _rig.motor);
            SetField(_rig.presenter, "_combat", _rig.actor);
            SetField(_rig.presenter, "_animator", _rig.animator);
            SetField(_rig.presenter, "_animationSet", _set);
            SetModel(_rig.actor, model);
            SetField(_set, "_controller", _production.Controller);
            SetField(_set, "_hitReact", _clip);
            _rig.actor.enabled = _rig.animator.enabled = _rig.presenter.enabled = true;
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
        }

        [TestCase("Configure")]
        [TestCase("OnEnable")]
        [TestCase("OnDisable")]
        public void ExplicitLifecycleBaselinesDiscardUnobservedHistory(string method)
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            if (method == "Configure") _rig.presenter.Configure(_rig.animator, _rig.actor, _rig.motor, _set);
            else
            {
                // Explicit callback invocation tests its observation contract, not Unity lifecycle scheduling.
                _rig.presenter.enabled = method == "OnEnable";
                Lifecycle(_rig.presenter, method);
                if (method == "OnDisable") { Hit(_rig.actor); _rig.presenter.enabled = true; Lifecycle(_rig.presenter, "OnEnable"); }
            }
            Assert.That(Observe(_rig.presenter), Is.False);
            Hit(_rig.actor);
            Assert.That(Observe(_rig.presenter), Is.True);
        }

        [Test]
        public void ObservationDoesNotWriteAnimatorBaseDomainOrAuthoredSet()
        {
            PrimeSameStateObservation(); Hit(_rig.actor);
            var model = _rig.actor.Model;
            float[] values = { model.StateElapsed, model.Health.Current, model.Stamina.Current, model.Posture.Current };
            int attack = model.AttackSequence;
            ulong ordinal = _rig.actor.HitReactPresentationSequence;
            string authored = EditorJsonUtility.ToJson(_set);
            _rig.animator.speed = .73f;
            Assert.That(_rig.animator.GetComponent<AnimatorSpeedCoordinator>(), Is.Null);
            Assert.That(Observe(_rig.presenter), Is.True);
            for (int i = 0; i < 10; i++) Assert.That(Observe(_rig.presenter), Is.False);
            Assert.That(_rig.animator.speed, Is.EqualTo(.73f));
            Assert.That(_rig.animator.GetComponent<AnimatorSpeedCoordinator>(), Is.Null);
            Assert.That(new[] { model.StateElapsed, model.Health.Current, model.Stamina.Current, model.Posture.Current }, Is.EqualTo(values));
            Assert.That(model.AttackSequence, Is.EqualTo(attack));
            Assert.That(_rig.actor.HitReactPresentationSequence, Is.EqualTo(ordinal));
            Assert.That(EditorJsonUtility.ToJson(_set), Is.EqualTo(authored));
        }

        [Test]
        public void ProductionSetsRemainDefaultOffAndDisposableEnablingNeverWritesBack()
        {
            string[] paths = AssetDatabase.FindAssets("t:PlayerAnimationSet", new[] { "Assets/_Game/Settings" })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
            Assert.That(paths, Does.Contain(SetPath));
            foreach (string path in paths)
            {
                var source = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(path);
                string before = EditorJsonUtility.ToJson(source);
                var disk = new[] { path, path + ".meta" }.ToDictionary(file => file, Hash);
                var clone = Object.Instantiate(source);
                try
                {
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    Assert.That(source.RestartOfflineHitReactOnAcceptedDamage, Is.False, path);
                    SetField(clone, "_hitReact", _clip);
                    Assert.That(clone.RestartOfflineHitReactOnAcceptedDamage, Is.False, "A missing source clip must not conceal an enabled flag: " + path);
                    clone.ConfigureOfflineHitReactReentry(true);
                    Assert.That(clone.RestartOfflineHitReactOnAcceptedDamage, Is.True);
                    Assert.That(EditorUtility.IsPersistent(clone), Is.False);
                }
                finally { Object.DestroyImmediate(clone); }
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before), path);
                foreach (var file in disk) Assert.That(Hash(file.Key), Is.EqualTo(file.Value), file.Key);
            }
        }

        static string Hash(string path)
        {
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
