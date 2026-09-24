using System.Collections;
using System.Collections.Generic;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class HitFeedbackPresentationTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (GameObject go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null;
        }

        private Animator CreateAnimator()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab");
            var go = Object.Instantiate(prefab); _objects.Add(go);
            var animator = go.GetComponentInChildren<Animator>();
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
            animator.runtimeAnimatorController = set.Controller;
#else
            var go = new GameObject("FeedbackTestActor"); _objects.Add(go);
            var animator = go.AddComponent<Animator>();
#endif
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Play("Locomotion");
            AnimatorSpeedCoordinator.SetBase(animator, 1.375f);
            return animator;
        }

        [UnityTest]
        public IEnumerator FiveGrades_ReallyStopAnimator_AndRestoreExactBaseSpeed()
        {
            Animator animator = CreateAnimator();
            yield return null;
            var speed = AnimatorSpeedCoordinator.For(animator);
            float baseline = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            yield return null; yield return null;
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(baseline), "Live controller must advance before freezing.");
            foreach (HitFeedbackGrade grade in new[] { HitFeedbackGrade.Light, HitFeedbackGrade.Sweep,
                         HitFeedbackGrade.Heavy, HitFeedbackGrade.GuardBreak, HitFeedbackGrade.Execution })
            {
                Assert.That(speed.Request(grade, (ulong)grade), Is.True);
                float normalized = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                int frozenFrames = 0;
                double timeout = Time.realtimeSinceStartupAsDouble + 2d;
                while (speed.ActiveGrade != HitFeedbackGrade.None && Time.realtimeSinceStartupAsDouble < timeout)
                {
                    Assert.That(animator.speed, Is.Zero);
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(normalized).Within(0.00001f));
                    frozenFrames++;
                    yield return null;
                }
                Assert.That(speed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                Assert.That(frozenFrames, Is.GreaterThan(0));
                Assert.That(animator.speed, Is.EqualTo(1.375f));
                Assert.That(speed.LastEffectiveMilliseconds, Is.GreaterThanOrEqualTo(HitFeedbackRules.Duration(grade) * 1000d - .01d));
                Debug.Log($"[M5C_FEEL] event=five-grade-dynamic grade={grade} frozenFrames={frozenFrames} effectiveMs={speed.LastEffectiveMilliseconds:F3} restored={animator.speed}");
                float resumed = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                yield return null; yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(resumed));
            }
        }

        [UnityTest]
        public IEnumerator Priority_DoesNotRenew_AndPerfectDefenseCannotOverwriteHitFreeze()
        {
            Animator animator = CreateAnimator();
            var speed = AnimatorSpeedCoordinator.For(animator);
            speed.Request(HitFeedbackGrade.PerfectDefense);
            Assert.That(speed.Request(HitFeedbackGrade.Heavy), Is.True);
            double start = Time.realtimeSinceStartupAsDouble;
            while (speed.ActiveGrade != HitFeedbackGrade.None && Time.realtimeSinceStartupAsDouble - start < 2d)
            {
                Assert.That(speed.Request(HitFeedbackGrade.Heavy), Is.False);
                Assert.That(speed.Request(HitFeedbackGrade.PerfectDefense), Is.False);
                AnimatorSpeedCoordinator.SetBase(animator, 2.125f);
                Assert.That(animator.speed, Is.Zero);
                yield return null;
            }
            Assert.That(speed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
            Assert.That(animator.speed, Is.EqualTo(2.125f));
        }

        [UnityTest]
        public IEnumerator DeathDisableAndSceneUnload_RestoreWithoutStaleSpeed()
        {
            Animator animator = CreateAnimator();
            var speed = AnimatorSpeedCoordinator.For(animator);
            speed.Request(HitFeedbackGrade.Execution);
            AnimatorSpeedCoordinator.SetBase(animator, 1.375f, true);
            Assert.That(animator.speed, Is.EqualTo(1.375f));
            Assert.That(speed.Request(HitFeedbackGrade.Heavy), Is.False);
            AnimatorSpeedCoordinator.SetBase(animator, 1.375f);
            speed.Request(HitFeedbackGrade.Heavy);
            speed.enabled = false;
            Assert.That(animator.speed, Is.EqualTo(1.375f));
            speed.enabled = true;
            var scene = SceneManager.CreateScene("FeedbackUnloadTest");
            SceneManager.MoveGameObjectToScene(animator.transform.root.gameObject, scene);
            bool restored = false;
            speed.FreezeEnded += (_, __) => restored = animator != null && animator.speed == 1.375f;
            speed.Request(HitFeedbackGrade.Execution);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(restored, Is.True);
        }

        [UnityTest]
        public IEnumerator CameraProjection_DoesNotChangeMovementAimOrOcclusionTransform_AndHonorsModal()
        {
            var go = new GameObject("FeedbackCamera"); _objects.Add(go);
            var camera = go.AddComponent<Camera>();
            var rig = go.AddComponent<ThirdPersonCameraRig>();
            var impulse = go.AddComponent<CombatCameraImpulse>();
            go.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(18, 52, 0));
            Vector3 position = go.transform.position, forward = go.transform.forward, right = go.transform.right;
            Matrix4x4 projection = camera.projectionMatrix;
            impulse.Request(HitFeedbackGrade.Execution);
            yield return null; yield return null;
            Assert.That(camera.projectionMatrix, Is.Not.EqualTo(projection));
            Assert.That(go.transform.position, Is.EqualTo(position));
            Assert.That(go.transform.forward, Is.EqualTo(forward));
            Assert.That(go.transform.right, Is.EqualTo(right));
            rig.SetLookInputBlocked(true);
            yield return null; yield return null;
            Assert.That(camera.projectionMatrix, Is.EqualTo(projection));
        }

        [UnityTest]
        public IEnumerator ConfirmedBatch_PlaysAudioAndFreezeSameFrame_AndNeverReplaysHostReceipt()
        {
            Animator animator = CreateAnimator();
            var presenter = animator.gameObject.AddComponent<CombatHitFeedbackPresenter>();
            var cameraObject = new GameObject("OwnerIsolationCamera"); _objects.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            var impulse = cameraObject.AddComponent<CombatCameraImpulse>();
            Matrix4x4 stableProjection = camera.projectionMatrix;
#if UNITY_EDITOR
            var audio = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            presenter.Configure(null, animator, audio, impulse);
#endif
            presenter.SetOwner(false);
            var impact = new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                1, 1, 10, HitFeedbackGrade.Light, ImpactSurface.Metal);
            presenter.Enqueue(impact); presenter.Enqueue(impact);
            presenter.Enqueue(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                2, 1, 11, HitFeedbackGrade.GuardBreak, ImpactSurface.Metal));
            yield return null; yield return null;
            Assert.That(presenter.PresentedCount, Is.EqualTo(1));
            Assert.That(presenter.LastAudioFrame, Is.EqualTo(presenter.LastFeedbackFrame));
            Assert.That(presenter.LastAudioFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(camera.projectionMatrix, Is.EqualTo(stableProjection), "Remote attack must not shake this camera.");
            presenter.Enqueue(impact);
            yield return null; yield return null;
            Assert.That(presenter.PresentedCount, Is.EqualTo(1));
            presenter.SetOwner(true);
            presenter.Enqueue(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                3, 2, 11, HitFeedbackGrade.Heavy, ImpactSurface.Metal));
            yield return null; yield return null;
            Assert.That(camera.projectionMatrix, Is.Not.EqualTo(stableProjection), "Owner attack should shake only its render projection.");
        }

        [UnityTest]
        public IEnumerator IdenticalInput_WithFeedbackOnOrOff_HasIdenticalDamageTicksAndJudgmentFrames()
        {
            Animator animator = CreateAnimator();
            var presenter = animator.gameObject.AddComponent<CombatHitFeedbackPresenter>();
            presenter.Configure(null, animator, null, null);
            var tuning = ScriptableObject.CreateInstance<CombatTuningAsset>();
            var withFeedback = new CombatStateMachine(tuning.CreateRuntimeCopy());
            var withoutFeedback = new CombatStateMachine(tuning.CreateRuntimeCopy());
            var targetOn = new CombatStateMachine(tuning.CreateRuntimeCopy());
            var targetOff = new CombatStateMachine(tuning.CreateRuntimeCopy());
            var framesOn = new List<int>(); var framesOff = new List<int>();
            int sequenceOn = -1, sequenceOff = -1, ticksOn = 0, ticksOff = 0;
            float damageOn = 0, damageOff = 0;
            ulong eventId = 0;
            // Same real domain input tape; only one side dispatches through the live presentation adapter.
            for (int frame = 0; frame < 240; frame++)
            {
                if (frame == 0 || frame == 30 || frame == 60)
                { withFeedback.Submit(CombatCommand.LightAttack); withoutFeedback.Submit(CombatCommand.LightAttack); }
                if (frame == 150) { Assert.That(withFeedback.Submit(CombatCommand.HeavyPressed), Is.True); Assert.That(withoutFeedback.Submit(CombatCommand.HeavyPressed), Is.True); }
                if (frame == 186) { Assert.That(withFeedback.Submit(CombatCommand.HeavyReleased), Is.True); Assert.That(withoutFeedback.Submit(CombatCommand.HeavyReleased), Is.True); }
                withFeedback.Tick(1f / 60f); ticksOn++;
                withoutFeedback.Tick(1f / 60f); ticksOff++;
                targetOn.Tick(1f / 60f); targetOff.Tick(1f / 60f);
                Assert.That(withFeedback.State, Is.EqualTo(withoutFeedback.State), "frame=" + frame);
                if (withFeedback.IsDamageWindowOpen && sequenceOn != withFeedback.AttackSequence)
                {
                    sequenceOn = withFeedback.AttackSequence; framesOn.Add(frame);
                    var result = targetOn.ReceiveDamage(new DamageRequest(7, sequenceOn,
                        withFeedback.CurrentAttackDamage, 0, withFeedback.CurrentAttackTag));
                    damageOn += result.AppliedDamage;
                    presenter.Enqueue(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                        ++eventId, sequenceOn, 8, HitFeedbackRules.Classify(result, withFeedback.CurrentAttackTag), ImpactSurface.Flesh));
                }
                if (withoutFeedback.IsDamageWindowOpen && sequenceOff != withoutFeedback.AttackSequence)
                {
                    sequenceOff = withoutFeedback.AttackSequence; framesOff.Add(frame);
                    var result = targetOff.ReceiveDamage(new DamageRequest(7, sequenceOff,
                        withoutFeedback.CurrentAttackDamage, 0, withoutFeedback.CurrentAttackTag));
                    damageOff += result.AppliedDamage;
                }
                yield return null;
            }
            Assert.That(framesOn.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(presenter.PresentedCount, Is.GreaterThan(0));
            Assert.That(damageOn, Is.EqualTo(damageOff));
            Assert.That(ticksOn, Is.EqualTo(ticksOff));
            CollectionAssert.AreEqual(framesOff, framesOn);
            Debug.Log($"[M5C_FEEL] event=domain-invariant damage={damageOn} ticks={ticksOn} hitFrames={string.Join(",", framesOn)} feedbackCount={presenter.PresentedCount}");
            Object.Destroy(tuning);
        }
    }
}
