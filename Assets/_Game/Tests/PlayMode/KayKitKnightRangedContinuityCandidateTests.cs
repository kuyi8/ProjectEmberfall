#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed partial class KayKitKnightInputCandidateTests
    {
        [Serializable] sealed class ContinuityFrame
        {
            public int frame, attackSequence, injectedAttackSequence, presentedCount, lastFeedbackFrame, lastAudioFrame;
            public double time, realtime;
            public string beforeDomain, afterDomain, beforeOwnerGrade, afterOwnerGrade, beforeTargetGrade, afterTargetGrade, injected;
            public float elapsed, delta, beforeOwnerSpeed, afterOwnerSpeed, ownerBase, beforeTargetSpeed, afterTargetSpeed, targetBase;
            public float currentNormalized, nextNormalized, targetNormalized;
            public bool beforeCaptured, afterCaptured, eofCaptured, predicate, pending, cameraChanged, hitAudioPlaying, defenseAudioPlaying, transition;
            public ulong injectedId, ownerSequence, targetSequence, lastConfirmation;
            public string beforePresented, afterPresented, eofDomain, eofOwnerGrade, eofTargetGrade;
            public float afterElapsed, eofElapsed, eofOwnerSpeed, eofTargetSpeed;
            public bool beforePredicate, beforeAllowOwnerFreeze, eofPredicate, eofAllowOwnerFreeze;
        }
        [Serializable] sealed class ContinuityEvent
        {
            public string kind, domain, grade; public int frame, attackSequence, suppliedAttackSequence; public ulong sequence;
            public double realtime, milliseconds; public bool requestAcceptanceMeasured, requestAccepted;
        }
        [Serializable] sealed class ContinuityRelease
        {
            public int frame, attackSequence; public float elapsed, damage, posture, speed, range; public bool matchesOriginalTuning;
        }
        [Serializable] sealed class ContinuityReview
        {
            public string scope = "One genuine synthetic-device F through the original Reader/Actor/Controller/Presenter/Launcher, staged owned .56s clip at1x and original .22 release, on the isolated .75 Knight. Controlled direct presentation requests, confirmed impact injection and Guard event-delegate invocation are NOT natural damage/contact/perfect defense, OS-input, formal TPS, SecondPeer, production Profile or complete-character acceptance. No manual domain Tick/Submit, Animator.Update/Play, Coordinator.Cancel, root lift or authority writes.";
            public string ordering = "Actor -200, before observer50, Presenter100, HitFeedback150, Coordinator10000, after observer10001, then real EOF. Only exact attacker owner requests are suppressed. Distinct target is independently requested; no attack-sequence filtering. Post-tail restoration is stable Locomotion, NOT the exact F-exit-frame boundary.";
            public string injectionScope = "Impact events carry controlled old/current AttackSequence but fresh monotonically increasing confirmation IDs. These are presentation-adapter inputs, not proof of an earlier naturally launched knife/contact. requestAcceptanceMeasured applies only to direct Coordinator.Request returns; dispatch itself is measured by frame/count/audio/target.";
            public string pendingScope = "Actual private HitFeedbackBatch._pending boolean is observed; false is not an invented deferred-request count. No deferred freeze queue is created or cleared by this fixture.";
            public string status = "started", caseName, output, candidate, candidateGuid, module, controller, guardInjection;
            public int actorId, animatorId, targetAnimatorId, setId, controllerId, entryFrame, fileCount, memoryCount, guardFrame;
            public bool sourcesBefore, sourcesAfter, memoryAfter, originalFgatesPassed, feedbackGatesPassed, guardAudioObserved, guardDomainUnchanged, accepted;
            public bool clockPassed, phasePassed, lifetimePassed, fullIdlePassed, floorsMeasured;
            public float maximumClockError, bodyMin, capeMin, gearMin, firstFElapsed, maximumFDelta, stableIdleSeconds;
            public ContinuityFrame[] frames; public ContinuityEvent[] events; public ContinuityRelease[] releases; public string[] images;
            public bool exitCase, exitBoundaryObserved, exitBoundaryGatesPassed, quietNoReplayPassed;
            public int exitFrame, priorFFrame; public float priorFElapsed;
        }
        sealed class ContinuityContext
        {
            public bool entryCase, exitCase, exitSent, armed, entered, directSent; public int impactStage, queued, oldAttackSequence;
            public ContinuityReview report; public ContinuityFrame current;
            public readonly List<ContinuityFrame> frames = new List<ContinuityFrame>();
            public readonly List<ContinuityEvent> events = new List<ContinuityEvent>();
            public readonly List<ContinuityRelease> releases = new List<ContinuityRelease>();
            public Animator target; public AnimatorSpeedCoordinator ownerSpeed, targetSpeed;
            public CombatHitFeedbackPresenter feedback; public PerfectDefenseFeedbackPresenter defense;
            public AudioSource hitAudio, defenseAudio; public Camera camera; public CombatCameraImpulse impulse;
            public Matrix4x4 projection; public PlayerAnimationPresenter presenter; public HitFeedbackBatch batch;
            public FieldInfo pending, ownerSequence, lastConfirmation; public Action<PerfectDefenseKind> guard;
            public Action<HitFeedbackGrade, double> ownerEnded, targetEnded; public Action<RangedAttackRelease> released;
            public KnightHitStopBeforePresenterObserver before; public KnightHitStopAfterCoordinatorObserver after;
        }

        [UnityTest]
        public IEnumerator ActualF_MotionContinuityCancelsExistingOwnerFreezeAndKeepsDistinctTarget()
            => RangedContinuityCase(true);

        [UnityTest]
        public IEnumerator ActualF_MotionContinuityDispatchesFeedbackAndRestoresAfterFullTail()
            => RangedContinuityCase(false);

        [UnityTest]
        public IEnumerator ActualF_MotionContinuityRestoresFeedbackOnExactDomainExitFrame()
            => RangedContinuityCase(false, true);

        IEnumerator RangedContinuityCase(bool entryCase, bool exitCase = false)
        {
            var c = new ContinuityContext { entryCase = entryCase, exitCase = exitCase, report = new ContinuityReview { output = _output, exitCase = exitCase, caseName = exitCase ? "exact-first-domain-exit-feedback" : entryCase ? "existing-owner-plus-direct-request" : "confirmed-feedback-and-Guard" } };
            if (exitCase)
            {
                c.report.ordering = "Actor -200 completes real F; observer50 detects consecutive F-to-Locomotion while Presenter still presents F and injects one fresh controlled impact; Presenter100 changes state/fade, HitFeedback150 dispatches, Coordinator10000 updates, observer10001 and EOF record facts. This is the first domain-exit frame, NOT an already-settled Idle frame.";
                c.report.injectionScope = "One controlled fresh confirmation1003 at the first consecutive observed F-to-Locomotion frame; no F-time impact/Guard injection. Actual Enqueue/150 dispatch and distinct target are measured, NOT natural contact, all producer execution orders, or NET. The original return fade may legitimately pause for the normal .10s owner freeze.";
            }
            string path = _output + "/ranged-continuity-review.json";
            Assert.That(File.Exists(path), Is.False);
            var stack = new Stack<IEnumerator>();
            try
            {
                Assert.That(_withKnife && _knife != null && _knife.IsConfigured && !HasKnifeTail(), Is.True);
                PrepareUpperBodyCandidate(StagedUpperThrow);
                _candidateSet.ConfigureOfflineRangedEntryTime(true);
                _candidateSet.ConfigureOfflineRangedMotionContinuity(true);
                _domainEntrySync = true; _trace.domainEntrySyncEnabled = true;
                PrepareContinuity(c);
                VerifyEntrySafety(); c.report.sourcesBefore = true;
                // All asset/dependency/JSON audits precede the original Ability .5s settling interval.
                // Recursively run the original strict clock/phase/floor/lease/full-Idle routine without swallowing assertions.
                stack.Push(VisibleKnifeAbility());
                while (stack.Count != 0)
                {
                    IEnumerator active = stack.Peek();
                    if (!active.MoveNext()) { stack.Pop(); (active as IDisposable)?.Dispose(); continue; }
                    object value = active.Current;
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)) { stack.Push(nested); continue; }
                    if (entryCase && !c.armed && _trace.inputQueuedAt > 0d && _actor.Model.State == CombatState.Locomotion)
                    {
                        c.armed = true;
                        ContinuityRequest(c, "existing-owner-before-F", c.ownerSpeed, 3001);
                        ContinuityRequest(c, "distinct-target-before-F", c.targetSpeed, 3002);
                    }
                    yield return value;
                    if (value is WaitForEndOfFrame) { CaptureContinuityEof(c); CheckFreezeSafety(_rows.Count == 0 ? _actor.transform.position : _rows[0].root); }
                }
                _completed = false; c.report.originalFgatesPassed = true;
                AssertContinuityF(c);
                if (exitCase)
                {
                    Assert.That(c.exitSent && c.report.exitBoundaryObserved, Is.True, "Do not substitute a later stable Locomotion confirmation for the exact exit boundary.");
                    var exit = c.frames.Single(r => r.injected == "exact-domain-exit-confirmation");
                    var prior = c.frames.Single(r => r.frame == c.report.priorFFrame);
                    Assert.That(exit.frame, Is.EqualTo(prior.frame + 1));
                    Assert.That(prior.beforeCaptured && prior.afterCaptured && prior.eofCaptured, Is.True);
                    Assert.That(prior.beforeDomain, Is.EqualTo(CombatState.RangedAttack.ToString()));
                    Assert.That(prior.afterDomain, Is.EqualTo(CombatState.RangedAttack.ToString()));
                    Assert.That(exit.frame, Is.EqualTo(_rows.First(r => r.frame > c.report.entryFrame && r.domain == CombatState.Locomotion.ToString()).frame));
                    Assert.That(exit.beforeCaptured && exit.afterCaptured && exit.eofCaptured, Is.True);
                    Assert.That(exit.beforeDomain, Is.EqualTo(CombatState.Locomotion.ToString()));
                    Assert.That(exit.afterDomain, Is.EqualTo(CombatState.Locomotion.ToString()));
                    Assert.That(exit.eofDomain, Is.EqualTo(CombatState.Locomotion.ToString()));
                    Assert.That(exit.beforePresented, Is.EqualTo(CombatState.RangedAttack.ToString()));
                    Assert.That(exit.afterPresented, Is.EqualTo(CombatState.Locomotion.ToString()));
                    Assert.That(exit.elapsed, Is.Zero); Assert.That(exit.afterElapsed, Is.Zero); Assert.That(exit.eofElapsed, Is.Zero);
                    Assert.That(exit.beforePredicate || exit.predicate || exit.eofPredicate, Is.False);
                    Assert.That(exit.beforeAllowOwnerFreeze && exit.eofAllowOwnerFreeze, Is.True);
                    Assert.That(exit.beforeOwnerGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(exit.beforeTargetGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(exit.afterOwnerGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(exit.afterTargetGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(exit.eofOwnerGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(exit.eofTargetGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(exit.afterOwnerSpeed, Is.Zero); Assert.That(exit.afterTargetSpeed, Is.Zero);
                    Assert.That(exit.eofOwnerSpeed, Is.Zero); Assert.That(exit.eofTargetSpeed, Is.Zero);
                    Assert.That(exit.injectedId, Is.EqualTo(1003UL));
                    Assert.That(exit.ownerSequence, Is.EqualTo(exit.injectedId)); Assert.That(exit.targetSequence, Is.EqualTo(exit.injectedId));
                    Assert.That(exit.lastConfirmation, Is.EqualTo(exit.injectedId));
                    Assert.That(exit.presentedCount, Is.EqualTo(1)); Assert.That(exit.pending, Is.False);
                    Assert.That(exit.lastFeedbackFrame, Is.EqualTo(exit.frame)); Assert.That(exit.lastAudioFrame, Is.EqualTo(exit.frame));
                    Assert.That(exit.hitAudioPlaying && exit.cameraChanged, Is.True);
                    Assert.That(c.events.Count(e => e.kind == "exact-domain-exit-confirmation"), Is.EqualTo(1));
                    AssertNaturalContinuityEnd(c, "owner-ended", 1003); AssertNaturalContinuityEnd(c, "target-ended", 1003);
                    Assert.That(c.ownerSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None)); Assert.That(c.targetSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                    Assert.That(_animator.speed, Is.EqualTo(c.ownerSpeed.BaseSpeed)); Assert.That(c.target.speed, Is.EqualTo(c.targetSpeed.BaseSpeed));
                    c.report.exitBoundaryGatesPassed = true;
                    double quietUntil = Time.realtimeSinceStartupAsDouble + .25d;
                    while (Time.realtimeSinceStartupAsDouble < quietUntil)
                    {
                        yield return null; yield return new WaitForEndOfFrame(); CaptureContinuityEof(c); CheckFreezeSafety(_rows[0].root);
                        Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion));
                        Assert.That(c.ownerSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None)); Assert.That(c.targetSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                        Assert.That(c.feedback.PresentedCount, Is.EqualTo(1)); Assert.That(ReadContinuityPending(c), Is.False);
                        Assert.That(c.presenter.KeepsOfflineRangedAnimationMoving(_actor, _animator), Is.False);
                    }
                    c.report.quietNoReplayPassed = true;
                }
                else if (entryCase)
                {
                    var entry = c.frames.Single(r => r.frame == c.report.entryFrame);
                    Assert.That(c.armed && c.directSent, Is.True);
                    Assert.That(entry.beforeOwnerGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(entry.beforeOwnerSpeed, Is.Zero);
                    Assert.That(entry.afterOwnerGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(entry.afterOwnerSpeed, Is.EqualTo(entry.ownerBase));
                    Assert.That(entry.beforeTargetGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(entry.afterTargetGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(entry.afterTargetSpeed, Is.Zero);
                    var direct = c.frames.Single(r => r.injected == "direct-owner-at50");
                    Assert.That(c.events.Single(e => e.kind == "direct-owner-at50").requestAccepted, Is.True);
                    Assert.That(direct.beforeOwnerGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(direct.afterOwnerGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(direct.afterOwnerSpeed, Is.EqualTo(direct.ownerBase));
                    Assert.That(c.events.Count(e => e.kind == "owner-ended" && e.grade == HitFeedbackGrade.Heavy.ToString()), Is.EqualTo(2));
                    AssertNaturalContinuityEnd(c, "target-ended", 3002);
                }
                else
                {
                    Assert.That(c.impactStage, Is.EqualTo(2));
                    foreach (var kind in new[] { "old-attack-new-confirmation", "current-attack-new-confirmation" })
                    {
                        var frame = c.frames.Single(r => r.injected == kind);
                        Assert.That(frame.afterCaptured && frame.eofCaptured, Is.True);
                        Assert.That(frame.lastFeedbackFrame, Is.EqualTo(frame.frame));
                        Assert.That(frame.lastAudioFrame, Is.EqualTo(frame.frame));
                        Assert.That(frame.cameraChanged, Is.True, "Actual LateUpdate lens impulse, not only a Request call.");
                        Assert.That(frame.afterTargetGrade, Is.EqualTo(kind.StartsWith("old-", StringComparison.Ordinal) ? HitFeedbackGrade.Light.ToString() : HitFeedbackGrade.Heavy.ToString()));
                        Assert.That(frame.afterTargetSpeed, Is.Zero);
                        Assert.That(frame.targetSequence, Is.EqualTo(frame.injectedId));
                    }
                    Assert.That(c.feedback.PresentedCount, Is.EqualTo(2));
                    Assert.That((ulong)c.lastConfirmation.GetValue(c.batch), Is.EqualTo(1002UL));
                    Assert.That(c.report.guardAudioObserved && c.report.guardDomainUnchanged, Is.True);
                    Assert.That(c.events.Where(e => e.kind == "owner-ended"), Is.Empty, "Neither F impact nor Guard confirmation may request an owner freeze.");
                    AssertNaturalContinuityEnd(c, "target-ended", 1002);
                    // Quiet complete-tail observation must not replay suppressed owner requests.
                    double quietUntil = Time.realtimeSinceStartupAsDouble + .25d;
                    while (Time.realtimeSinceStartupAsDouble < quietUntil)
                    {
                        yield return null; yield return new WaitForEndOfFrame(); CaptureContinuityEof(c); CheckFreezeSafety(_rows[0].root);
                        Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion));
                        Assert.That(c.ownerSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                        Assert.That(c.targetSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                        Assert.That(c.feedback.PresentedCount, Is.EqualTo(2)); Assert.That(ReadContinuityPending(c), Is.False);
                    }
                    c.queued = 1; yield return null; yield return new WaitForEndOfFrame(); CaptureContinuityEof(c);
                    var duplicate = c.frames.Single(r => r.injected == "duplicate-old-confirmation");
                    Assert.That(duplicate.presentedCount, Is.EqualTo(2));
                    Assert.That(duplicate.afterOwnerGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(duplicate.afterTargetGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(duplicate.pending, Is.False);
                    c.queued = 2; yield return null; yield return new WaitForEndOfFrame(); CaptureContinuityEof(c);
                    var future = c.frames.Single(r => r.injected == "future-Locomotion-confirmation");
                    Assert.That(future.afterDomain, Is.EqualTo(CombatState.Locomotion.ToString()));
                    Assert.That(future.presentedCount, Is.EqualTo(3));
                    Assert.That(future.lastFeedbackFrame, Is.EqualTo(future.frame)); Assert.That(future.lastAudioFrame, Is.EqualTo(future.frame));
                    Assert.That(future.afterOwnerGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(future.afterTargetGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
                    Assert.That(future.afterOwnerSpeed, Is.Zero); Assert.That(future.afterTargetSpeed, Is.Zero); Assert.That(future.cameraChanged, Is.True);
                    double timeout = Time.realtimeSinceStartupAsDouble + 2d;
                    while ((c.ownerSpeed.ActiveGrade != HitFeedbackGrade.None || c.targetSpeed.ActiveGrade != HitFeedbackGrade.None) && Time.realtimeSinceStartupAsDouble < timeout)
                    { yield return null; yield return new WaitForEndOfFrame(); CaptureContinuityEof(c); CheckFreezeSafety(_rows[0].root); }
                    Assert.That(c.ownerSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                    Assert.That(c.targetSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                    Assert.That(_animator.speed, Is.EqualTo(c.ownerSpeed.BaseSpeed));
                    Assert.That(c.target.speed, Is.EqualTo(c.targetSpeed.BaseSpeed));
                    AssertNaturalContinuityEnd(c, "owner-ended", 1003); AssertNaturalContinuityEnd(c, "target-ended", 1003);
                    Assert.That(c.feedback.PresentedCount, Is.EqualTo(3)); Assert.That(ReadContinuityPending(c), Is.False);
                }
                c.report.feedbackGatesPassed = true; VerifyEntrySafety(); c.report.accepted = true; _completed = true;
            }
            finally
            {
                // Ordinary exceptions and native assertion failures are never caught/continued.
                while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
                if (c.before != null) c.before.Sample = null; if (c.after != null) c.after.Sample = null;
                if (c.ownerSpeed != null && c.ownerEnded != null) c.ownerSpeed.FreezeEnded -= c.ownerEnded;
                if (c.targetSpeed != null && c.targetEnded != null) c.targetSpeed.FreezeEnded -= c.targetEnded;
                if (_actor != null && c.released != null) _actor.RangedAttackReleased -= c.released;
                c.report.sourcesAfter = _disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                c.report.memoryAfter = _memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value);
                c.report.frames = c.frames.ToArray(); c.report.events = c.events.ToArray(); c.report.releases = c.releases.ToArray(); c.report.images = _images.ToArray();
                c.report.fileCount = _disk.Count; c.report.memoryCount = _memory.Count;
                c.report.clockPassed = _trace.entryClockGateEvaluated && _trace.entryClockGatePassed; c.report.maximumClockError = _trace.maximumEntryClockError;
                c.report.phasePassed = _trace.knifeHandoffPassed; c.report.lifetimePassed = _trace.knifeLifetimeCompleted;
                c.report.fullIdlePassed = _trace.stableIdleCompleted; c.report.stableIdleSeconds = _trace.stableIdleSeconds;
                c.report.floorsMeasured = _trace.floorGateEvaluated; c.report.bodyMin = _trace.minimumBody; c.report.capeMin = _trace.minimumCape; c.report.gearMin = _trace.minimumGear;
                c.report.accepted &= c.report.sourcesAfter && c.report.memoryAfter; c.report.status = c.report.accepted ? "completed-contract-only" : "incomplete-or-failed";
                if (!c.report.accepted) _completed = false;
                File.WriteAllText(path, JsonUtility.ToJson(c.report, true));
            }
        }

        void PrepareContinuity(ContinuityContext c)
        {
            string[] scripts = {
                "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationSet.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/AnimatorSpeedCoordinator.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerFreezePresentationPolicy.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/CombatHitFeedbackPresenter.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/PerfectDefenseFeedbackPresenter.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/CombatImpactPresentation.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/HitFeedbackRules.cs",
                "Assets/_Game/Scripts/Gameplay/Movement/CombatCameraImpulse.cs",
                "Assets/_Game/Tests/PlayMode/KayKitKnightRangedContinuityCandidateTests.cs",
                "Assets/_Game/Settings/CombatImpactAudio_M6.asset" };
            foreach (string source in scripts)
            {
                Assert.That(File.Exists(source), Is.True, source);
                foreach (string dependency in AssetDatabase.GetDependencies(source, true).Concat(new[] { source }).Distinct())
                {
                    foreach (string file in new[] { dependency, dependency + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(dependency).Where(o => o != null && EditorUtility.IsPersistent(o)))
                        if (!_memory.ContainsKey(asset)) _memory.Add(asset, EditorJsonUtility.ToJson(asset));
                }
            }
            c.report.actorId = _actor.GetInstanceID(); c.report.animatorId = _animator.GetInstanceID(); c.report.setId = _candidateSet.GetInstanceID();
            c.report.controllerId = _candidateController.GetInstanceID(); c.report.controller = _trace.sourceController;
            c.report.candidate = AssetDatabase.GetAssetPath(_throwClip); c.report.candidateGuid = AssetDatabase.AssetPathToGUID(c.report.candidate);
            c.report.module = GetType().Assembly.ManifestModule.ModuleVersionId.ToString();
            c.presenter = _actor.GetComponent<PlayerAnimationPresenter>(); c.ownerSpeed = AnimatorSpeedCoordinator.For(_animator);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(Prefab), _scene);
            visual.name = "Review_Distinct_ContinuityTarget"; visual.transform.position = _actor.transform.position + Vector3.right * 15f;
            Assert.That(visual.GetComponentsInChildren<PlayerCombatActor>(true), Is.Empty); Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => { var copy = new Material(m); _ownedAssets.Add(copy); return copy; }).ToArray();
            c.target = visual.GetComponentInChildren<Animator>(true); Assert.That(c.target != null && c.target != _animator, Is.True);
            c.target.applyRootMotion = false; c.target.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            c.target.runtimeAnimatorController = _candidateController; c.targetSpeed = AnimatorSpeedCoordinator.For(c.target);
            c.report.targetAnimatorId = c.target.GetInstanceID();
            c.camera = _scene.GetRootGameObjects().Single(g => g.name == "Review_Knight_Camera").GetComponent<Camera>();
            c.projection = c.camera.projectionMatrix; c.impulse = c.camera.gameObject.AddComponent<CombatCameraImpulse>();
            var audio = Object.Instantiate(Required<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset")); _ownedAssets.Add(audio);
            c.feedback = _actor.gameObject.AddComponent<CombatHitFeedbackPresenter>(); c.feedback.Configure(_actor, _animator, audio, c.impulse);
            c.hitAudio = c.feedback.GetComponentsInChildren<AudioSource>(true).Single();
            c.batch = (HitFeedbackBatch)typeof(CombatHitFeedbackPresenter).GetField("_batch", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c.feedback);
            c.pending = typeof(HitFeedbackBatch).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic);
            c.ownerSequence = typeof(AnimatorSpeedCoordinator).GetField("_sequence", BindingFlags.Instance | BindingFlags.NonPublic);
            c.lastConfirmation = typeof(HitFeedbackBatch).GetField("_lastSequence", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(c.batch != null && c.pending != null && c.ownerSequence != null && c.lastConfirmation != null, Is.True);
            var defenseRoot = Own("Review_Controlled_GuardConfirmation"); defenseRoot.SetActive(false);
            c.defense = defenseRoot.AddComponent<PerfectDefenseFeedbackPresenter>(); c.defense.Configure(_actor, _animator); defenseRoot.SetActive(true);
            c.defenseAudio = defenseRoot.GetComponent<AudioSource>();
            var field = typeof(PlayerCombatActor).GetField("PerfectDefensePresented", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null); c.guard = field.GetValue(_actor) as Action<PerfectDefenseKind>;
            Assert.That(c.guard != null && c.guard.GetInvocationList().Any(d => ReferenceEquals(d.Target, c.defense)), Is.True);
            c.ownerEnded = (grade, ms) => ContinuityEventRecord(c, "owner-ended", grade, ReadContinuitySequence(c, c.ownerSpeed), false, ms);
            c.targetEnded = (grade, ms) => ContinuityEventRecord(c, "target-ended", grade, ReadContinuitySequence(c, c.targetSpeed), false, ms);
            c.ownerSpeed.FreezeEnded += c.ownerEnded; c.targetSpeed.FreezeEnded += c.targetEnded;
            c.released = r => c.releases.Add(new ContinuityRelease { frame = Time.frameCount, attackSequence = r.AttackSequence, elapsed = _actor.Model.StateElapsed, damage = r.Damage, posture = r.PostureDamage, speed = r.ProjectileSpeed, range = r.MaximumDistance,
                matchesOriginalTuning = r.Damage == _expectedTuning.RangedDamage && r.PostureDamage == _expectedTuning.RangedPostureDamage && r.ProjectileSpeed == _expectedTuning.RangedProjectileSpeed && r.MaximumDistance == _expectedTuning.RangedMaximumDistance });
            _actor.RangedAttackReleased += c.released;
            c.before = _actor.gameObject.AddComponent<KnightHitStopBeforePresenterObserver>(); c.after = _actor.gameObject.AddComponent<KnightHitStopAfterCoordinatorObserver>();
            c.before.Sample = () => CaptureContinuityBefore(c); c.after.Sample = () => CaptureContinuityAfter(c);
        }
        void CaptureContinuityBefore(ContinuityContext c)
        {
            var prior = c.current;
            var row = new ContinuityFrame { frame = Time.frameCount, time = Time.timeAsDouble, realtime = Time.realtimeSinceStartupAsDouble,
                beforeDomain = _actor.Model.State.ToString(), elapsed = _actor.Model.StateElapsed, delta = Time.deltaTime, attackSequence = _actor.Model.AttackSequence, beforeCaptured = true };
            if (c.exitCase)
            {
                row.beforePresented = c.presenter.PresentedState.ToString();
                row.beforePredicate = c.presenter.KeepsOfflineRangedAnimationMoving(_actor, _animator);
                row.beforeAllowOwnerFreeze = PlayerFreezePresentationPolicy.AllowOwnerFreeze(_actor, _animator);
            }
            c.current = row; c.frames.Add(row);
            if (_actor.Model.State == CombatState.RangedAttack)
            {
                if (!c.entered)
                {
                    c.entered = true; c.report.entryFrame = row.frame; c.report.firstFElapsed = row.elapsed;
                    if (!c.entryCase && !c.exitCase)
                    {
                        c.oldAttackSequence = row.attackSequence - 1;
                        InjectContinuity(c, row, "old-attack-new-confirmation", 1001, c.oldAttackSequence, HitFeedbackGrade.Light);
                        float health = _actor.Model.Health.Current, stamina = _actor.Model.Stamina.Current, posture = _actor.Model.Posture.Current;
                        c.guard(PerfectDefenseKind.Guard);
                        c.report.guardFrame = row.frame; c.report.guardInjection = "Controlled invocation of the actual Actor.PerfectDefensePresented multicast delegate (Guard); not ReceiveDamage/perfect-guard domain acceptance.";
                        c.report.guardDomainUnchanged = _actor.Model.State == CombatState.RangedAttack && _actor.Model.AttackSequence == row.attackSequence &&
                            _actor.Model.Health.Current == health && _actor.Model.Stamina.Current == stamina && _actor.Model.Posture.Current == posture;
                        c.report.guardAudioObserved = c.defenseAudio.isPlaying && c.defenseAudio.pitch == 1.15f;
                        c.impactStage = 1;
                    }
                }
                else if (c.entryCase && !c.directSent)
                {
                    c.directSent = true; row.injected = "direct-owner-at50";
                    ContinuityRequest(c, row.injected, c.ownerSpeed, 3003);
                }
                else if (!c.entryCase && !c.exitCase && c.impactStage == 1 && row.elapsed >= .12f)
                { InjectContinuity(c, row, "current-attack-new-confirmation", 1002, row.attackSequence, HitFeedbackGrade.Heavy); c.impactStage = 2; }
            }
            if (c.exitCase && !c.exitSent && c.entered && prior != null && prior.frame == row.frame - 1 &&
                prior.afterCaptured && prior.beforeDomain == CombatState.RangedAttack.ToString() &&
                prior.afterDomain == CombatState.RangedAttack.ToString() && row.beforeDomain == CombatState.Locomotion.ToString() &&
                row.beforePresented == CombatState.RangedAttack.ToString())
            {
                c.exitSent = true; c.report.exitBoundaryObserved = true; c.report.exitFrame = row.frame;
                c.report.priorFFrame = prior.frame; c.report.priorFElapsed = prior.elapsed;
                InjectContinuity(c, row, "exact-domain-exit-confirmation", 1003, row.attackSequence, HitFeedbackGrade.Heavy);
            }
            if (c.queued != 0)
            {
                bool fresh = c.queued == 2; c.queued = 0;
                InjectContinuity(c, row, fresh ? "future-Locomotion-confirmation" : "duplicate-old-confirmation", fresh ? 1003UL : 1001UL,
                    fresh ? row.attackSequence : c.oldAttackSequence, fresh ? HitFeedbackGrade.Heavy : HitFeedbackGrade.Light);
            }
            row.beforeOwnerGrade = c.ownerSpeed.ActiveGrade.ToString(); row.beforeOwnerSpeed = _animator.speed;
            row.beforeTargetGrade = c.targetSpeed.ActiveGrade.ToString(); row.beforeTargetSpeed = c.target.speed;
        }
        void CaptureContinuityAfter(ContinuityContext c)
        {
            var row = c.current; if (row == null || row.frame != Time.frameCount) return;
            row.afterCaptured = true; row.afterDomain = _actor.Model.State.ToString();
            if (c.exitCase) { row.afterPresented = c.presenter.PresentedState.ToString(); row.afterElapsed = _actor.Model.StateElapsed; }
            row.afterOwnerGrade = c.ownerSpeed.ActiveGrade.ToString(); row.afterOwnerSpeed = _animator.speed; row.ownerBase = c.ownerSpeed.BaseSpeed;
            row.afterTargetGrade = c.targetSpeed.ActiveGrade.ToString(); row.afterTargetSpeed = c.target.speed; row.targetBase = c.targetSpeed.BaseSpeed;
            row.ownerSequence = ReadContinuitySequence(c, c.ownerSpeed); row.targetSequence = ReadContinuitySequence(c, c.targetSpeed);
            row.lastConfirmation = (ulong)c.lastConfirmation.GetValue(c.batch);
            row.predicate = c.presenter.KeepsOfflineRangedAnimationMoving(_actor, _animator); row.pending = ReadContinuityPending(c);
            row.presentedCount = c.feedback.PresentedCount; row.lastFeedbackFrame = c.feedback.LastFeedbackFrame; row.lastAudioFrame = c.feedback.LastAudioFrame;
        }
        void CaptureContinuityEof(ContinuityContext c)
        {
            var row = c.current; if (row == null || row.frame != Time.frameCount || row.eofCaptured) return;
            row.eofCaptured = true; row.cameraChanged = c.camera.projectionMatrix != c.projection;
            if (c.exitCase)
            {
                row.eofDomain = _actor.Model.State.ToString(); row.eofElapsed = _actor.Model.StateElapsed;
                row.eofOwnerGrade = c.ownerSpeed.ActiveGrade.ToString(); row.eofTargetGrade = c.targetSpeed.ActiveGrade.ToString();
                row.eofOwnerSpeed = _animator.speed; row.eofTargetSpeed = c.target.speed;
                row.eofPredicate = c.presenter.KeepsOfflineRangedAnimationMoving(_actor, _animator);
                row.eofAllowOwnerFreeze = PlayerFreezePresentationPolicy.AllowOwnerFreeze(_actor, _animator);
            }
            row.hitAudioPlaying = c.hitAudio.isPlaying; row.defenseAudioPlaying = c.defenseAudio.isPlaying;
            row.currentNormalized = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime; row.nextNormalized = _animator.GetNextAnimatorStateInfo(0).normalizedTime;
            row.targetNormalized = c.target.GetCurrentAnimatorStateInfo(0).normalizedTime; row.transition = _animator.IsInTransition(0);
        }
        void InjectContinuity(ContinuityContext c, ContinuityFrame row, string kind, ulong id, int attack, HitFeedbackGrade grade)
        {
            row.injected = kind; row.injectedId = id; row.injectedAttackSequence = attack;
            c.feedback.Enqueue(new CombatImpactPresentationEvent(_actor.transform.position, CombatImpactStyle.Steel, id, attack, c.target.GetInstanceID(), grade, ImpactSurface.Metal, c.target, AttackTag.Projectile));
            ContinuityEventRecord(c, kind, grade, id, false, 0d);
            c.events[c.events.Count - 1].suppliedAttackSequence = attack;
        }
        void ContinuityRequest(ContinuityContext c, string kind, AnimatorSpeedCoordinator target, ulong id)
        {
            bool accepted = target.Request(HitFeedbackGrade.Heavy, id);
            ContinuityEventRecord(c, kind, HitFeedbackGrade.Heavy, id, accepted, 0d);
        }
        void ContinuityEventRecord(ContinuityContext c, string kind, HitFeedbackGrade grade, ulong id, bool accepted, double ms)
            => c.events.Add(new ContinuityEvent { kind = kind, grade = grade.ToString(), frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble, domain = _actor.Model.State.ToString(), attackSequence = _actor.Model.AttackSequence, sequence = id, requestAcceptanceMeasured = kind == "existing-owner-before-F" || kind == "distinct-target-before-F" || kind == "direct-owner-at50", requestAccepted = accepted, milliseconds = ms });
        static bool ReadContinuityPending(ContinuityContext c) => (bool)c.pending.GetValue(c.batch);
        static ulong ReadContinuitySequence(ContinuityContext c, AnimatorSpeedCoordinator speed) => (ulong)c.ownerSequence.GetValue(speed);
        static void AssertNaturalContinuityEnd(ContinuityContext c, string kind, ulong id)
        {
            var ended = c.events.Single(e => e.kind == kind && e.sequence == id);
            Assert.That(ended.grade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString()));
            Assert.That(ended.milliseconds, Is.GreaterThanOrEqualTo(.10f * 1000d - .01d));
        }
        void AssertContinuityF(ContinuityContext c)
        {
            Assert.That(c.entered, Is.True); Assert.That(c.report.originalFgatesPassed, Is.True);
            var ranged = c.frames.Where(r => r.afterDomain == CombatState.RangedAttack.ToString()).ToArray();
            Assert.That(ranged, Is.Not.Empty);
            Assert.That(ranged.All(r => r.beforeCaptured && r.afterCaptured && r.predicate && !r.pending), Is.True);
            Assert.That(ranged.All(r => r.afterOwnerGrade == HitFeedbackGrade.None.ToString() && r.afterOwnerSpeed == r.ownerBase), Is.True);
            c.report.maximumFDelta = ranged.Max(r => r.delta);
            Assert.That(_trace.entryClockGatePassed && _trace.knifeHandoffPassed && _trace.knifeLifetimeCompleted && _trace.stableIdleCompleted, Is.True);
            Assert.That(_trace.stableIdleSeconds, Is.GreaterThanOrEqualTo(.15f));
            Assert.That(c.releases.Count, Is.EqualTo(1)); Assert.That(c.releases[0].matchesOriginalTuning, Is.True);
            Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); Assert.That(HasKnifeTail(), Is.False);
            Assert.That(_candidateSet.KeepOfflineRangedAnimationMoving, Is.True);
            Assert.That(c.presenter.KeepsOfflineRangedAnimationMoving(_actor, _animator), Is.False, "Immediate predicate expires with real F, not a cached window.");
        }
    }
}
#endif
