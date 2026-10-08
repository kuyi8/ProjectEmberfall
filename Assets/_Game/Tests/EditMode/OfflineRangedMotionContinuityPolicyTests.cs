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
    // Pure policy truth-table only: no PlayerLoop, real input, animation evaluation, freeze duration or NGO-peer acceptance.
    public sealed class OfflineRangedMotionContinuityPolicyTests
    {
        const string SetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        const string VisualPath = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
        Scene _preview;
        int _activeScene;
        bool _legacyDodge;
        PlayerAnimationSet _set, _production;
        AnimationClip _clip;
        CombatTuningAsset _tuning;
        Rig _rig;
        sealed class Rig { public PlayerCombatActor actor; public Animator animator; public ThirdPersonMotor motor; public PlayerAnimationPresenter presenter; }
        static IEnumerable<CombatState> NonRangedStates => Enum.GetValues(typeof(CombatState)).Cast<CombatState>().Where(s => s != CombatState.RangedAttack);

        [SetUp]
        public void Setup()
        {
            Assert.That(UnityEngine.Application.isPlaying, Is.False);
            _activeScene = SceneManager.GetActiveScene().handle;
            _legacyDodge = PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving;
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving = true;
            _preview = EditorSceneManager.NewPreviewScene();
            _production = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
            Assert.That(_production != null && _production.Controller != null, Is.True);
            _set = ScriptableObject.CreateInstance<PlayerAnimationSet>(); _set.hideFlags = HideFlags.HideAndDontSave;
            typeof(PlayerAnimationSet).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_set, _production.Controller);
            _clip = new AnimationClip { name = "Disposable_Continuity_056", frameRate = 120f, hideFlags = HideFlags.HideAndDontSave };
            AnimationUtility.SetEditorCurve(_clip, new EditorCurveBinding { path = "", type = typeof(Transform), propertyName = "m_LocalPosition.x" }, AnimationCurve.Constant(0f, .56f, 0f));
            Assert.That(_clip.length, Is.EqualTo(.56f).Within(.000001f));
            _set.ConfigureOfflineRangedAttack(_clip);
            _tuning = ScriptableObject.CreateInstance<CombatTuningAsset>(); _tuning.hideFlags = HideFlags.HideAndDontSave;
            _rig = CreateRig("Continuity_PurePolicy_P1");
            Assert.That(_rig.presenter.IsConfigured, Is.True);
        }

        [TearDown]
        public void Cleanup()
        {
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving = _legacyDodge;
            if (_preview.IsValid()) EditorSceneManager.ClosePreviewScene(_preview);
            if (_set != null) Object.DestroyImmediate(_set);
            if (_clip != null) Object.DestroyImmediate(_clip);
            if (_tuning != null) Object.DestroyImmediate(_tuning);
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(_activeScene));
        }

        Rig CreateRig(string name)
        {
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath); Assert.That(visual, Is.Not.Null);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(visual, _preview); root.name = name;
            var rig = new Rig { actor = root.AddComponent<PlayerCombatActor>(), animator = root.GetComponentsInChildren<Animator>(true).Single(),
                motor = root.AddComponent<ThirdPersonMotor>(), presenter = root.AddComponent<PlayerAnimationPresenter>() };
            rig.actor.Configure(null, _tuning, root.transform, root.transform, root.GetComponent<CharacterController>(), null);
            SetModel(rig.actor, new CombatStateMachine(_tuning.CreateRuntimeCopy()));
            rig.presenter.Configure(rig.animator, rig.actor, rig.motor, _set);
            return rig;
        }

        static void SetModel(PlayerCombatActor actor, CombatStateMachine model) =>
            typeof(PlayerCombatActor).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, model);
        static void SetTruthTableState(CombatStateMachine model, CombatState state) =>
            typeof(CombatStateMachine).GetProperty(nameof(CombatStateMachine.State)).GetSetMethod(true).Invoke(model, new object[] { state });
        void EnterOptInRanged()
        {
            _set.ConfigureOfflineRangedMotionContinuity(true);
            Assert.That(_rig.actor.Model.Submit(CombatCommand.RangedAttack), Is.True, "Pure domain setup, not input evidence.");
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.True, "Every negative fixture starts from a proven positive predicate.");
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.False);
        }

        [Test]
        public void DefaultOffWithAnOfflineClipKeepsLegacyRangedFreezeAndZeroEntryOffset()
        {
            Assert.That(_rig.actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
            Assert.That(_set.KeepOfflineRangedAnimationMoving, Is.False);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, _rig.actor, _rig.animator), Is.True);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 1f), Is.Zero);
        }

        [Test]
        public void MissingOfflineClipCannotEnableContinuityEvenWithItsFlagSet()
        {
            EnterOptInRanged(); _set.ConfigureOfflineRangedAttack(null);
            Assert.That(_set.KeepOfflineRangedAnimationMoving, Is.False);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            _set.ConfigureOfflineRangedAttack(_clip);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.True);
        }

        [Test]
        public void ExactActorAndAnimatorReferencesAreRequiredWithoutCrossInstanceSuppression()
        {
            EnterOptInRanged(); Rig second = CreateRig("Continuity_PurePolicy_P2");
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(second.actor, _rig.animator), Is.False);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, second.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, second.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(second.actor, second.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, second.actor, second.animator), Is.True);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(null, _rig.animator), Is.False);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, null), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(null, _rig.animator), Is.True, "Network/no-offline-actor caller keeps the legacy path; this is not NGO execution evidence.");
        }

        [Test]
        public void RequestTimeReadsDoNotAdvanceDomainMutateTheSetOrCreateAFreezeCoordinator()
        {
            EnterOptInRanged(); var model = _rig.actor.Model;
            string before = EditorJsonUtility.ToJson(_set);
            var snapshot = new[] { model.StateElapsed, model.Health.Current, model.Stamina.Current, model.Posture.Current, model.RangedCooldownRemaining };
            int sequence = model.AttackSequence, release = model.RangedReleaseSequence;
            Assert.That(_rig.animator.GetComponent<AnimatorSpeedCoordinator>(), Is.Null);
            for (int i = 0; i < 10; i++)
            {
                Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.False);
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, _rig.actor, _rig.animator), Is.False);
            }
            Assert.That(model.State, Is.EqualTo(CombatState.RangedAttack));
            Assert.That(new[] { model.StateElapsed, model.Health.Current, model.Stamina.Current, model.Posture.Current, model.RangedCooldownRemaining }, Is.EqualTo(snapshot));
            Assert.That(model.AttackSequence, Is.EqualTo(sequence)); Assert.That(model.RangedReleaseSequence, Is.EqualTo(release));
            Assert.That(EditorJsonUtility.ToJson(_set), Is.EqualTo(before));
            Assert.That(_rig.animator.GetComponent<AnimatorSpeedCoordinator>(), Is.Null);
        }

        [TestCaseSource(nameof(NonRangedStates))]
        public void EveryNonRangedStateRetainsItsExactLegacyOwnerAndDefenseRules(CombatState state)
        {
            EnterOptInRanged();
            // Setter is deliberately restricted to this disposable policy truth-table, NOT a legal domain-transition or actual-input claim.
            SetTruthTableState(_rig.actor.Model, state);
            bool expected = state != CombatState.Dodge;
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(state), Is.EqualTo(expected));
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.EqualTo(expected));
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, _rig.actor, _rig.animator), Is.EqualTo(expected));
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge, _rig.actor, _rig.animator), Is.False);
        }

        [TestCase("presenter-component")]
        [TestCase("actor-component")]
        [TestCase("animator-component")]
        [TestCase("actor-hierarchy")]
        [TestCase("animator-hierarchy")]
        public void DisabledInstancesCannotLeaveAStaleContinuityWindow(string target)
        {
            EnterOptInRanged();
            if (target == "presenter-component") _rig.presenter.enabled = false;
            else if (target == "actor-component") _rig.actor.enabled = false;
            else if (target == "animator-component") _rig.animator.enabled = false;
            else if (target == "actor-hierarchy") _rig.actor.gameObject.SetActive(false);
            else _rig.animator.gameObject.SetActive(false);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            _rig.actor.gameObject.SetActive(true); _rig.animator.gameObject.SetActive(true);
            _rig.presenter.enabled = true; _rig.actor.enabled = true; _rig.animator.enabled = true;
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.True);
        }

        [Test]
        public void ReconfigurationImmediatelyRejectsOldBindingsAndIncompleteConfiguration()
        {
            EnterOptInRanged(); Rig second = CreateRig("Continuity_Reconfigured_P2");
            _rig.presenter.Configure(second.animator, _rig.actor, _rig.motor, _set);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, second.animator), Is.True);
            _rig.presenter.Configure(_rig.animator, second.actor, _rig.motor, _set);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            _rig.presenter.Configure(_rig.animator, _rig.actor, null, _set);
            Assert.That(_rig.presenter.IsConfigured, Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            _rig.presenter.Configure(_rig.animator, _rig.actor, _rig.motor, null);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            _rig.presenter.Configure(_rig.animator, _rig.actor, _rig.motor, _set);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.True);
        }

        [Test]
        public void MissingDomainModelAndNullAdapterBindingsKeepLegacyRequests()
        {
            EnterOptInRanged(); SetModel(_rig.actor, null);
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(null, null), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, null, _rig.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge, null, _rig.animator), Is.False);
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void DomainEntryClockAndMotionContinuityFlagsRemainIndependent(bool entryClock, bool continuity)
        {
            _set.ConfigureOfflineRangedEntryTime(entryClock); _set.ConfigureOfflineRangedMotionContinuity(continuity);
            Assert.That(_rig.actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 1f), Is.EqualTo(entryClock ? .22f : 0f));
            Assert.That(_set.KeepOfflineRangedAnimationMoving, Is.EqualTo(continuity));
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.EqualTo(continuity));
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.EqualTo(!continuity));
            Assert.That(_set.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(_clip));
        }

        [TestCase(true)] [TestCase(false)]
        public void LegacyDodgeABAndNullNetworkCallerDoNotImplicitlyEnableRangedContinuity(bool keepDodgeMoving)
        {
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving = keepDodgeMoving;
            foreach (CombatState state in Enum.GetValues(typeof(CombatState)))
            {
                SetTruthTableState(_rig.actor.Model, state);
                bool expected = !keepDodgeMoving || state != CombatState.Dodge;
                Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.EqualTo(expected), state.ToString());
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, _rig.actor, _rig.animator), Is.EqualTo(expected), state.ToString());
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge, _rig.actor, _rig.animator), Is.EqualTo(!keepDodgeMoving), state.ToString());
            }
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(null, _rig.animator), Is.True);
            Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard, null, _rig.animator), Is.True);
            SetTruthTableState(_rig.actor.Model, CombatState.RangedAttack); _set.ConfigureOfflineRangedMotionContinuity(true);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.False, "Dodge A/B does not override the separate explicit F opt-in.");
        }

        [TestCase(false)] [TestCase(true)]
        public void RealPureDomainDamageExitsRangedAndNeverGainsInvulnerability(bool lethal)
        {
            EnterOptInRanged(); float health = _rig.actor.Model.Health.Current;
            DamageResult result = _rig.actor.Model.ReceiveDamage(new DamageRequest(123, 7, lethal ? health + 1f : 1f, 0f, AttackTag.Light), 0f);
            Assert.That(result.Accepted, Is.True); Assert.That(result.Invulnerable, Is.False);
            Assert.That(result.AppliedDamage, Is.EqualTo(lethal ? health : 1f));
            Assert.That(_rig.actor.Model.State, Is.EqualTo(lethal ? CombatState.Dead : CombatState.HitReact));
            Assert.That(_rig.presenter.KeepsOfflineRangedAnimationMoving(_rig.actor, _rig.animator), Is.False);
            Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(_rig.actor, _rig.animator), Is.True);
        }

        [Test]
        public void ExistingFreezeGradeDurationsRetainTheirExactNumericContract()
        {
            var expected = new Dictionary<HitFeedbackGrade, float> { { HitFeedbackGrade.None, 0f }, { HitFeedbackGrade.PerfectDefense, .045f },
                { HitFeedbackGrade.Light, .05f }, { HitFeedbackGrade.Sweep, .08f }, { HitFeedbackGrade.Heavy, .10f }, { HitFeedbackGrade.GuardBreak, .16f }, { HitFeedbackGrade.Execution, .22f } };
            foreach (HitFeedbackGrade grade in Enum.GetValues(typeof(HitFeedbackGrade))) Assert.That(HitFeedbackRules.Duration(grade), Is.EqualTo(expected[grade]), grade.ToString());
        }

        [Test]
        public void ProductionSetsRemainDefaultOffAndDisposableEnablingDoesNotWriteBack()
        {
            var paths = AssetDatabase.FindAssets("t:PlayerAnimationSet", new[] { "Assets/_Game/Settings" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
            Assert.That(paths, Does.Contain(SetPath));
            foreach (string path in paths)
            {
                var source = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(path); string before = EditorJsonUtility.ToJson(source);
                var disk = new[] { path, path + ".meta" }.ToDictionary(p => p, Hash);
                Assert.That(source.KeepOfflineRangedAnimationMoving, Is.False, path);
                var clone = Object.Instantiate(source);
                try
                {
                    clone.hideFlags = HideFlags.HideAndDontSave; clone.ConfigureOfflineRangedAttack(_clip);
                    Assert.That(clone.KeepOfflineRangedAnimationMoving, Is.False, "Missing source clip cannot hide an enabled author flag: " + path);
                    clone.ConfigureOfflineRangedEntryTime(true);
                    Assert.That(clone.KeepOfflineRangedAnimationMoving, Is.False, path);
                    clone.ConfigureOfflineRangedMotionContinuity(true); Assert.That(clone.KeepOfflineRangedAnimationMoving, Is.True);
                    Assert.That(clone.Controller, Is.SameAs(source.Controller));
                    Assert.That(EditorUtility.IsPersistent(clone), Is.False);
                }
                finally { Object.DestroyImmediate(clone); }
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before), path);
                foreach (var file in disk) Assert.That(Hash(file.Key), Is.EqualTo(file.Value), file.Key);
            }
        }
        static string Hash(string path)
        {
            using (var file = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
