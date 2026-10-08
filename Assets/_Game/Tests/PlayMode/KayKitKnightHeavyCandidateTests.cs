#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed partial class KayKitKnightInputCandidateTests
    {
        const string HeavyMotionFolder = "Assets/_Game/Art/Review/KayKitHeroMotion/knight-heavy-charge-release-author-20261003-a1/";
        const string HeavyChargePath = HeavyMotionFolder + "A_Review_Knight_HeavyCharge_RiseHold.anim";
        const string HeavyReleasePath = HeavyMotionFolder + "A_Review_Knight_HeavyRelease_Staged.anim";
        const string HeavyFixturePath = "Assets/_Game/Tests/PlayMode/KayKitKnightHeavyCandidateTests.cs";
        const float HeavyArithmeticTolerance = .00001f;
        const float HeavyHeldTransformTolerance = .000001f;
        const float HeavyMaximumSampleSeconds = 1f / 30f;
        const double HeavyPreInputSteadySeconds = .15, HeavyPreInputDeadlineSeconds = 8;
        const double HeavyRunDeadlineSeconds = 35, HeavyLongHoldSeconds = 10, HeavyMinimumTerminalHoldSeconds = 9;
        enum HeavyInputCase { SameDynamicBatch, OneEofHold, TenSecondHold }

        [Serializable] sealed class HeavyInputFact
        {
            public string phase, control, domain;
            public int frame; public double time, inputEventTime;
            public bool held, pendingPressed, pendingReleased;
            public float domainElapsed, health, stamina;
        }
        [Serializable] sealed class HeavyLoopFact
        {
            public int frame; public string domain, presentedBefore, presentedAfter, beforeGrade, afterGrade;
            public float elapsed, delta, duration, damage, stamina, health, beforeSpeed, afterSpeed, afterBase;
            public bool fullyCharged, damageWindow, beforeCaptured, afterCaptured;
        }
        [Serializable] sealed class HeavyJointSample
        {
            public string name, path;
            public Vector3 actorPositionMetres, velocityMetresPerSecond;
            public Quaternion actorRotation;
            public float angularSpeedDegreesPerSecond;
            public bool velocityMeasured;
        }
        [Serializable] sealed class HeavyPoseSnapshot
        {
            public string path; public Vector3 localPosition, localScale; public Quaternion localRotation;
        }
        [Serializable] sealed class HeavyFrame
        {
            public Row pose; public HeavyLoopFact loop; public HeavyJointSample[] joints;
            public bool clipTimeMeasured, heavyHeld, terminalPoseMeasured;
            public float actualClipSeconds, expectedDomainPhaseSeconds, signedDomainPhaseDifference;
            public float heldLocalPositionDifference, heldQuaternionComponentDifference, heldLocalScaleDifference;
        }
        [Serializable] sealed class HeavyJointSeam
        {
            public string name, path; public float positionDifferenceMetres, rotationDifferenceDegrees;
            public Vector3 approachingVelocityMetresPerSecond, firstReleaseVelocityMetresPerSecond;
            public float approachingAngularSpeedDegreesPerSecond, firstReleaseAngularSpeedDegreesPerSecond;
            public bool approachingVelocityMeasured, firstReleaseVelocityMeasured;
        }
        [Serializable] sealed class HeavySteadyFact
        {
            public int frame, frameGap; public double time, sampleSeconds, continuousSeconds;
            public float delta; public bool shortFrame, stableIdle;
        }
        [Serializable] sealed class HeavyInputReport
        {
            public string test, status = "recording", abortReason, module, output, coreTrace;
            public string scope = "Three independent cold actual Mouse/InputSystem/PlayerLoop Heavy leaves, isolated original Actor/capsule, original .75 Knight Human/full rig, owned a1 clips and nonpersistent exact-key SO/AOC. No manual Submit/Tick/Animator.Update/warp, F flags, domain/authority/runtime/profile/source/build/r4 changes. NOT OS input, natural contact, NET or production replacement.";
            public string phaseScope = "Heavy damage window .235..475 and duration .82 start at RELEASE-state entry, not Charge. Existing Presenter entry offset remains0/base1/.08 fixed fade. Domain expected phase and actual current/next clip phase are separately recorded; their first-delta error is a diagnostic, NOT an absolute domain synchronization acceptance gate. Subsequent cold clip increments use this run's actual delta.";
            public string seamScope = "Author a1 full130 curve endpoints match at native source .4; this is NOT bone-pose/C1/contact acceptance. Actual pre-release/first-release joint differences and individual m/s/deg/s are separately measured. Same-input-batch and one-EOF short releases are separate worst-seam cases; no invented minimum hold or delayed release.";
            public string holdScope = "Explicit candidate static nonloop hold: actual Charge is held for>=10s; every sampled terminal rig/equipment local TRS remains within predeclared1e-6 arithmetic limits for>=9s. Normalized time may grow beyond1. This is not breathing/feel/foot-contact acceptance.";
            public string coverageScope = "Full SHA/JSON audit before settling; >=.15s uninterrupted real finite adjacent EOF intervals<=1/30 before queueing input, bounded8s. First input frame and all motion/hold/fade/full Idle+.15 samples must stay within1/30. Missed samples remain failures, never synthetic catch-up.";
            public string imageScope = "No in-motion synchronous screenshots or PNG encoding; this leaf records actual EOF geometry/joints but rendered images/visual judgement remain pending. No old or upside-down image acceptance transferred.";
            public float declaredClockArithmeticTolerance = HeavyArithmeticTolerance, declaredHeldTransformTolerance = HeavyHeldTransformTolerance;
            public float declaredMaximumSampleSeconds = HeavyMaximumSampleSeconds, declaredPeakHips = .4f, declaredFinalHips = .1f, declaredRootDistance = .01f;
            public double declaredHoldSeconds = HeavyLongHoldSeconds, declaredTerminalHoldSeconds = HeavyMinimumTerminalHoldSeconds;
            public double preInputSteadySeconds, maximumSampleSeconds, inputQueuedAt, firstChargeAt, firstAttackAt, returnedAt, settledAt, stableIdleSeconds, terminalHoldSeconds;
            public float firstChargeElapsed, firstAttackElapsed, firstChargeActualClipSeconds, firstAttackActualClipSeconds;
            public float maximumDomainPhaseDifference, maximumClipIncrementError, releaseBeforeChargeElapsed, expectedDamage, actualDamage;
            public float healthBefore, staminaBefore, initialStaminaAfterSpend, finalHealth, finalStamina;
            public float bodyMin, capeMin, gearMin, peakHips, finalHips, rootDistance;
            public float maximumHeldLocalPositionDifference, maximumHeldQuaternionComponentDifference, maximumHeldLocalScaleDifference;
            public int sequenceBefore, sequenceAfter, firstChargeFrame, firstAttackFrame, returnedFrame, chargeEofFrames, terminalFrames, firstWindowFrame, lastWindowFrame;
            public string chargeGuid, releaseGuid, nativeAnimatorPath; public long chargeLocalId, releaseLocalId;
            public Vector3 outerLocalPosition, outerLocalScale, innerLocalScale; public Quaternion outerLocalRotation;
            public bool executionCompleted, accepted, sourceBefore, sourceAfter, memoryAfter, preInputSteadyCompleted;
            public bool enteredAttack, presentedCharge, domainReturned, fullIdle, coveragePassed, resourcePassed, damageWindowPassed, incrementPassed, holdGateApplicable, holdPassed, floorPassed, hipsPassed, lifecyclePassed;
            public bool entrySynchronizationAccepted = false, boneSeamAccepted = false, visualAccepted = false, contactAccepted = false, fullAbilityAccepted = false;
            public HeavyFrame[] frames; public HeavyInputFact[] inputs; public HeavySteadyFact[] steady;
            public HeavyJointSeam[] actualReleaseSeam; public HeavyPoseSnapshot[] terminalPose, preReleasePose, firstReleasePose;
            public GateFailure[] failures; public string[] seenStates;
        }

        HeavyInputReport _heavyRecord;
        readonly List<HeavyFrame> _heavyFrames = new List<HeavyFrame>();
        readonly List<HeavyInputFact> _heavyInputs = new List<HeavyInputFact>();
        readonly List<HeavySteadyFact> _heavySteady = new List<HeavySteadyFact>();
        readonly List<GateFailure> _heavyFailures = new List<GateFailure>();
        readonly Dictionary<int, HeavyLoopFact> _heavyLoops = new Dictionary<int, HeavyLoopFact>();
        AnimationClip _heavyChargeClip, _heavyReleaseClip, _heavyChargeKey, _heavyReleaseKey;
        Transform _heavyOuter; Transform[] _heavyTransforms, _heavyJointTransforms;
        Vector3 _heavyOuterPosition, _heavyOuterScale, _heavyInnerScale; Quaternion _heavyOuterRotation;
        HeavyPoseSnapshot[] _heavyTerminalPose;
        KeyValuePair<AnimationClip, AnimationClip>[] _heavyOverrides; string _heavySetJson;
        FieldInfo _heavyPendingPressed, _heavyPendingReleased;
        InputAction _heavyAction; Action<InputAction.CallbackContext> _heavyInputCallback;
        KnightHitStopBeforePresenterObserver _heavyBefore; KnightHitStopAfterCoordinatorObserver _heavyAfter;
        PlayerAnimationPresenter _heavyPresenter; AnimatorSpeedCoordinator _heavySpeed;
        Exception _heavyObserverError; bool _heavyRecording;

        [UnityTest] public IEnumerator ActualRmb_HeavySameDynamicBatchRecordsShortestRelease()
            => HeavyInputBaseline(HeavyInputCase.SameDynamicBatch);
        [UnityTest] public IEnumerator ActualRmb_HeavyOneEofHoldRecordsPartialRiseSeam()
            => HeavyInputBaseline(HeavyInputCase.OneEofHold);
        [UnityTest] public IEnumerator ActualRmb_HeavyTenSecondHoldClampsPoseThenReturnsToIdle()
            => HeavyInputBaseline(HeavyInputCase.TenSecondHold);

        IEnumerator HeavyInputBaseline(HeavyInputCase kind)
        {
            _heavyRecord = new HeavyInputReport { test = kind.ToString(), output = _output,
                coreTrace = _output + "/actual-input.json", module = GetType().Assembly.ManifestModule.ModuleVersionId.ToString() };
            _heavyFrames.Clear(); _heavyInputs.Clear(); _heavySteady.Clear(); _heavyFailures.Clear(); _heavyLoops.Clear();
            _heavyObserverError = null; _heavyRecording = false; _heavyTerminalPose = null;
            string recordPath = _output + "/heavy-input-review.json";
            Assert.That(File.Exists(recordPath), Is.False);
            IEnumerator run = RunHeavyInput(kind);
            try
            {
                while (true)
                {
                    bool moved;
                    try { moved = run.MoveNext(); }
                    catch (Exception error) { _heavyRecord.abortReason = error.ToString(); throw; }
                    if (!moved) break;
                    yield return run.Current;
                }
            }
            finally
            {
                _heavyRecording = false;
                if (_heavyBefore != null) _heavyBefore.Sample = null;
                if (_heavyAfter != null) _heavyAfter.Sample = null;
                if (_heavyAction != null && _heavyInputCallback != null)
                { _heavyAction.started -= _heavyInputCallback; _heavyAction.canceled -= _heavyInputCallback; }
                (run as IDisposable)?.Dispose();
                _heavyRecord.sourceAfter = _disk.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value);
                _heavyRecord.memoryAfter = _memory.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value);
                _heavyRecord.frames = _heavyFrames.ToArray(); _heavyRecord.inputs = _heavyInputs.ToArray(); _heavyRecord.steady = _heavySteady.ToArray();
                _heavyRecord.failures = _heavyFailures.ToArray(); _heavyRecord.seenStates = _seenStates.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (_actor != null && _actor.Model != null)
                { _heavyRecord.sequenceAfter = _actor.Model.AttackSequence; _heavyRecord.finalHealth = _actor.Model.Health.Current; _heavyRecord.finalStamina = _actor.Model.Stamina.Current; }
                if (_heavyFrames.Count > 0)
                {
                    _heavyRecord.bodyMin = _heavyFrames.Min(frame => frame.pose.bodyMin);
                    _heavyRecord.capeMin = _heavyFrames.Min(frame => frame.pose.capeMin);
                    _heavyRecord.gearMin = _heavyFrames.Min(frame => frame.pose.gearMin);
                }
                _heavyRecord.accepted &= _heavyRecord.sourceAfter && _heavyRecord.memoryAfter;
                _heavyRecord.status = _heavyRecord.accepted ? "completed-contract-only-phase-and-visual-pending" : "incomplete-or-failed";
                if (!_heavyRecord.accepted) _completed = false;
                File.WriteAllText(recordPath, JsonUtility.ToJson(_heavyRecord, true));
                _heavyInputCallback = null; _heavyAction = null; _heavyBefore = null; _heavyAfter = null;
                _heavyPresenter = null; _heavySpeed = null; _heavyOuter = null; _heavyTransforms = _heavyJointTransforms = null;
                _heavyChargeClip = _heavyReleaseClip = _heavyChargeKey = _heavyReleaseKey = null;
                _heavyOverrides = null; _heavySetJson = null; _heavyPendingPressed = _heavyPendingReleased = null;
            }
        }

        IEnumerator RunHeavyInput(HeavyInputCase kind)
        {
            PrepareOwnedHeavyCandidate();
            HeavySafety(true); _heavyRecord.sourceBefore = true;
            IEnumerator steady = WaitForHeavySteadySampling();
            try
            {
                while (steady.MoveNext()) yield return steady.Current;
            }
            finally { (steady as IDisposable)?.Dispose(); }
            Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(_animator.IsInTransition(0), Is.False);
            Vector2 baseline = PlanarHips(); Vector3 root = _actor.transform.position;
            _trace.settled = true; _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation;
            _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
            _heavyRecord.healthBefore = _actor.Model.Health.Current; _heavyRecord.staminaBefore = _actor.Model.Stamina.Current;
            _heavyRecord.sequenceBefore = _actor.Model.AttackSequence;
            Observe(0f); _heavyFrames.Add(CaptureHeavyFrame(_rows[_rows.Count - 1], null));
            _heavyRecording = true; _heavyRecord.inputQueuedAt = _trace.inputQueuedAt = Time.timeAsDouble;
            RecordInput("queued", "HeavyAttack"); InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 2 });
            bool releaseQueued = kind == HeavyInputCase.SameDynamicBatch;
            if (releaseQueued) { RecordInput("queued-release-same-batch", "HeavyAttack"); InputSystem.QueueStateEvent(_mouse, new MouseState()); }
            double deadline = Time.realtimeSinceStartupAsDouble + HeavyRunDeadlineSeconds, stableStarted = double.NaN, terminalStarted = double.NaN;
            bool released = false;
            yield return null;
            for (int count = 0; count < 5000 && Time.realtimeSinceStartupAsDouble <= deadline; count++)
            {
                yield return new WaitForEndOfFrame();
                if (_heavyObserverError != null) throw new InvalidOperationException("Actual Heavy observation failed without driving the PlayerLoop.", _heavyObserverError);
                HeavySafety(false);
                Assert.That(_heavyLoops.TryGetValue(Time.frameCount, out HeavyLoopFact loop) && loop.beforeCaptured && loop.afterCaptured, Is.True,
                    "Both real order50 and order10001 observations must exist for this exact EOF.");
                float drift = Vector2.Distance(baseline, PlanarHips()); Observe(drift);
                HeavyFrame frame = CaptureHeavyFrame(_rows[_rows.Count - 1], loop); _heavyFrames.Add(frame);
                _heavyRecord.peakHips = Mathf.Max(_heavyRecord.peakHips, drift);
                _heavyRecord.rootDistance = Mathf.Max(_heavyRecord.rootDistance, Vector2.Distance(new Vector2(root.x, root.z), new Vector2(frame.pose.root.x, frame.pose.root.z)));
                double sampleSeconds = frame.pose.time - _heavyFrames[_heavyFrames.Count - 2].pose.time;
                _heavyRecord.maximumSampleSeconds = Math.Max(_heavyRecord.maximumSampleSeconds, Math.Max(sampleSeconds, frame.pose.delta));
                if (loop.domain == CombatState.HeavyCharge.ToString())
                {
                    _heavyRecord.chargeEofFrames++;
                    if (_heavyRecord.firstChargeFrame == 0)
                    {
                        _heavyRecord.firstChargeFrame = frame.pose.frame; _heavyRecord.firstChargeAt = frame.pose.time;
                        _heavyRecord.firstChargeElapsed = loop.elapsed; _heavyRecord.firstChargeActualClipSeconds = frame.actualClipSeconds;
                    }
                    _heavyRecord.presentedCharge |= loop.presentedAfter == CombatState.HeavyCharge.ToString();
                    if (!frame.pose.transition && frame.pose.currentState == "HeavyCharge" && frame.pose.currentNormalized >= 1.05f)
                    {
                        if (_heavyTerminalPose == null)
                        { _heavyTerminalPose = CaptureHeavyLocalPose(); _heavyRecord.terminalPose = _heavyTerminalPose; terminalStarted = frame.pose.time; }
                        frame.terminalPoseMeasured = true; MeasureHeavyTerminalPose(frame);
                        _heavyRecord.terminalFrames++; _heavyRecord.terminalHoldSeconds = frame.pose.time - terminalStarted;
                    }
                    if (!releaseQueued && (kind == HeavyInputCase.OneEofHold || loop.elapsed >= HeavyLongHoldSeconds))
                    {
                        _heavyRecord.preReleasePose = CaptureHeavyLocalPose();
                        RecordInput("queued-release", "HeavyAttack"); InputSystem.QueueStateEvent(_mouse, new MouseState()); releaseQueued = true;
                    }
                }
                else if (loop.domain == CombatState.HeavyAttack.ToString())
                {
                    if (!_heavyRecord.enteredAttack)
                    {
                        _heavyRecord.enteredAttack = true; _heavyRecord.firstAttackFrame = frame.pose.frame; _heavyRecord.firstAttackAt = frame.pose.time;
                        _heavyRecord.firstAttackElapsed = loop.elapsed; _heavyRecord.firstAttackActualClipSeconds = frame.actualClipSeconds;
                        _heavyRecord.actualDamage = loop.damage; _heavyRecord.initialStaminaAfterSpend = loop.stamina;
                        _heavyRecord.firstReleasePose = CaptureHeavyLocalPose();
                        _heavyRecord.actualReleaseSeam = HeavyActualSeam(_heavyFrames[_heavyFrames.Count - 2], frame);
                    }
                    if (loop.damageWindow)
                    { if (_heavyRecord.firstWindowFrame == 0) _heavyRecord.firstWindowFrame = frame.pose.frame; _heavyRecord.lastWindowFrame = frame.pose.frame; }
                    released = true;
                }
                else if (_heavyRecord.enteredAttack && loop.domain == CombatState.Locomotion.ToString())
                {
                    if (!_heavyRecord.domainReturned)
                    { _heavyRecord.domainReturned = true; _heavyRecord.returnedFrame = frame.pose.frame; _heavyRecord.returnedAt = frame.pose.time; }
                    if (double.IsNaN(stableStarted) && frame.pose.stableLocomotion)
                    { stableStarted = frame.pose.time; _heavyRecord.settledAt = frame.pose.time; }
                    if (!double.IsNaN(stableStarted))
                    {
                        Assert.That(frame.pose.stableLocomotion, Is.True, "Full idle tail must stay in nontransitional native Locomotion; no replay/back-jump.");
                        _heavyRecord.stableIdleSeconds = frame.pose.time - stableStarted;
                        if (_heavyRecord.stableIdleSeconds >= .15)
                        { _heavyRecord.fullIdle = true; _heavyRecord.finalHips = drift; break; }
                    }
                }
                yield return null;
            }
            _heavyRecording = false; _heavyRecord.executionCompleted = released && _heavyRecord.fullIdle;
            EvaluateHeavyInputGates(kind);
            HeavySafety(true);
            _trace.peak = _heavyRecord.peakHips; _trace.final = _heavyRecord.finalHips;
            _trace.domainReturnedToLocomotion = _heavyRecord.domainReturned; _trace.animatorSettledToLocomotion = _heavyRecord.fullIdle;
            _trace.stableIdleCompleted = _heavyRecord.fullIdle; _trace.stableIdleSeconds = (float)_heavyRecord.stableIdleSeconds;
            _trace.domainReturnedAt = _heavyRecord.returnedAt; _trace.animatorSettledAt = _heavyRecord.settledAt;
            _trace.minimumBody = _heavyRecord.bodyMin; _trace.minimumCape = _heavyRecord.capeMin; _trace.minimumGear = _heavyRecord.gearMin;
            _trace.floorGateEvaluated = true; _trace.bodyFloorGatePassed = _heavyRecord.bodyMin >= 0;
            _trace.capeFloorGatePassed = _heavyRecord.capeMin >= 0; _trace.gearFloorGatePassed = _heavyRecord.gearMin >= 0;
            _heavyRecord.accepted = _heavyFailures.Count == 0; _completed = _heavyRecord.accepted;
            Assert.That(_heavyFailures, Is.Empty, "Independent Heavy diagnostics retained full measured tail; no phase/contact/visual acceptance inferred.");
        }

        void PrepareOwnedHeavyCandidate()
        {
            _heavyChargeClip = Required<AnimationClip>(HeavyChargePath); _heavyReleaseClip = Required<AnimationClip>(HeavyReleasePath);
            var clips = new[] { _heavyChargeClip, _heavyReleaseClip }; var durations = new[] { .55f, .82f };
            var guids = new[] { "a90efaf08e083a34a8c4815b1129da3f", "c0fb090c6cc7b3241bb7d7b04a96e3c8" };
            for (int index = 0; index < 2; index++)
            {
                var clip = clips[index]; Assert.That(clip.humanMotion && !clip.isLooping, Is.True);
                Assert.That(clip.length, Is.EqualTo(durations[index]).Within(.000001f));
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId), Is.True);
                Assert.That(guid, Is.EqualTo(guids[index])); Assert.That(localId, Is.EqualTo(7400000L));
                if (index == 0) { _heavyRecord.chargeGuid = guid; _heavyRecord.chargeLocalId = localId; }
                else { _heavyRecord.releaseGuid = guid; _heavyRecord.releaseLocalId = localId; }
                // Materialize owned clip metadata/curves before adding its immutable full JSON watch.
                var settings = AnimationUtility.GetAnimationClipSettings(clip); Assert.That(settings.loopTime || settings.loopBlend, Is.False);
                var bindings = AnimationUtility.GetCurveBindings(clip); Assert.That(bindings.Length, Is.EqualTo(130));
                Assert.That(AnimationUtility.GetAnimationEvents(clip), Is.Empty); Assert.That(AnimationUtility.GetObjectReferenceCurveBindings(clip), Is.Empty);
                foreach (var binding in bindings) { var curve = AnimationUtility.GetEditorCurve(clip, binding); Assert.That(curve, Is.Not.Null); Assert.That(curve.keys.Length, Is.GreaterThan(1)); }
                foreach (string path in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(clip), true).Concat(new[] { AssetDatabase.GetAssetPath(clip) }).Distinct())
                {
                    foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).Where(asset => asset != null && EditorUtility.IsPersistent(asset)))
                        if (!_memory.ContainsKey(asset)) _memory.Add(asset, EditorJsonUtility.ToJson(asset));
                }
            }
            _heavyChargeKey = _candidateSet.GetClip(CombatState.HeavyCharge);
            _heavyReleaseKey = _candidateSet.GetOfflineClip(CombatState.HeavyAttack);
            Assert.That(_heavyChargeKey != null && _heavyReleaseKey != null && _heavyChargeKey != _heavyReleaseKey, Is.True);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(overrides);
            foreach (var replacement in new[] { new KeyValuePair<AnimationClip, AnimationClip>(_heavyChargeKey, _heavyChargeClip), new KeyValuePair<AnimationClip, AnimationClip>(_heavyReleaseKey, _heavyReleaseClip) })
            {
                Assert.That(overrides.Count(pair => pair.Key == replacement.Key), Is.EqualTo(1));
                int index = overrides.FindIndex(pair => pair.Key == replacement.Key);
                overrides[index] = replacement;
            }
            _candidateController.ApplyOverrides(overrides);
            var set = new SerializedObject(_candidateSet); set.FindProperty("_heavyCharge").objectReferenceValue = _heavyChargeClip;
            set.FindProperty("_offlineHeavyAttack").objectReferenceValue = _heavyReleaseClip; set.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(set.FindProperty("_offlineRangedDomainEntryTime").boolValue || set.FindProperty("_offlineRangedMotionContinuity").boolValue, Is.False);
            Assert.That(EditorUtility.IsPersistent(_candidateSet) || EditorUtility.IsPersistent(_candidateController), Is.False);
            Assert.That(_candidateSet.GetOfflineStateName(CombatState.HeavyCharge), Is.EqualTo("HeavyCharge"));
            Assert.That(_candidateSet.GetOfflineStateName(CombatState.HeavyAttack), Is.EqualTo("PlayerHeavyAttack"));
            _stateGuard.Clear(); _stateGuard.UnionWith(new[] { "Locomotion", "HeavyCharge", "PlayerHeavyAttack" });
            _allowed.Clear(); _allowed.UnionWith(_locomotion); _allowed.UnionWith(clips);
            _heavyOverrides = overrides.ToArray(); _heavySetJson = EditorJsonUtility.ToJson(_candidateSet);
            _heavyPresenter = _actor.GetComponent<PlayerAnimationPresenter>(); _heavySpeed = AnimatorSpeedCoordinator.For(_animator);
            Assert.That(_heavyPresenter, Is.Not.Null); Assert.That(_heavySpeed, Is.Not.Null);
            _heavyOuter = _animator.transform;
            while (_heavyOuter.parent != null && _heavyOuter.parent != _actor.transform) _heavyOuter = _heavyOuter.parent;
            Assert.That(_heavyOuter.parent, Is.SameAs(_actor.transform)); Assert.That(_heavyOuter.localScale, Is.EqualTo(Vector3.one * .75f));
            var prefab = Required<GameObject>(Prefab); var sourceAnimator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(HeavyTransformPath(_heavyOuter, _animator.transform), Is.EqualTo(HeavyTransformPath(prefab.transform, sourceAnimator.transform)));
            _heavyRecord.nativeAnimatorPath = HeavyTransformPath(_heavyOuter, _animator.transform);
            _heavyOuterPosition = _heavyRecord.outerLocalPosition = _heavyOuter.localPosition;
            _heavyOuterScale = _heavyRecord.outerLocalScale = _heavyOuter.localScale;
            _heavyOuterRotation = _heavyRecord.outerLocalRotation = _heavyOuter.localRotation;
            _heavyInnerScale = _heavyRecord.innerLocalScale = _animator.transform.localScale;
            _heavyTransforms = _animator.GetComponentsInChildren<Transform>(true);
            _heavyJointTransforms = new[] { _hips, _chest, _rightHand, _leftHand, _animator.GetBoneTransform(HumanBodyBones.LeftFoot), _animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            Assert.That(_heavyJointTransforms.All(transform => transform != null), Is.True);
            _heavyPendingPressed = typeof(PlayerInputReader).GetField("_heavyPressed", BindingFlags.Instance | BindingFlags.NonPublic);
            _heavyPendingReleased = typeof(PlayerInputReader).GetField("_heavyReleased", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_heavyPendingPressed != null && _heavyPendingReleased != null, Is.True);
            var actions = (InputActionAsset)typeof(PlayerInputReader).GetField("_runtimeActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_reader);
            _heavyAction = actions.FindAction("Player/HeavyAttack", true);
            _heavyInputCallback = context =>
            {
                if (!_heavyRecording) return;
                try
                {
                    _heavyInputs.Add(new HeavyInputFact { phase = context.phase.ToString(), control = context.control?.path,
                        frame = Time.frameCount, time = Time.timeAsDouble, inputEventTime = context.time,
                        domain = _actor.Model.State.ToString(), domainElapsed = _actor.Model.StateElapsed,
                        held = _reader.HeavyHeld, pendingPressed = (bool)_heavyPendingPressed.GetValue(_reader), pendingReleased = (bool)_heavyPendingReleased.GetValue(_reader),
                        health = _actor.Model.Health.Current, stamina = _actor.Model.Stamina.Current });
                    RecordInput(context.phase.ToString(), "HeavyAttack");
                }
                catch (Exception error) { if (_heavyObserverError == null) _heavyObserverError = error; }
            };
            _heavyAction.started += _heavyInputCallback; _heavyAction.canceled += _heavyInputCallback;
            _heavyBefore = _actor.gameObject.AddComponent<KnightHitStopBeforePresenterObserver>();
            _heavyBefore.Sample = () =>
            {
                if (!_heavyRecording) return;
                try
                {
                    _heavyLoops[Time.frameCount] = new HeavyLoopFact { frame = Time.frameCount, domain = _actor.Model.State.ToString(),
                        elapsed = _actor.Model.StateElapsed, delta = Time.deltaTime, duration = _actor.Model.StateDuration,
                        damage = _actor.Model.CurrentAttackDamage, damageWindow = _actor.Model.IsDamageWindowOpen, fullyCharged = _actor.Model.IsHeavyFullyCharged,
                        health = _actor.Model.Health.Current, stamina = _actor.Model.Stamina.Current,
                        presentedBefore = _heavyPresenter.PresentedState.ToString(), beforeSpeed = _animator.speed,
                        beforeGrade = _heavySpeed.ActiveGrade.ToString(), beforeCaptured = true };
                }
                catch (Exception error) { if (_heavyObserverError == null) _heavyObserverError = error; }
            };
            _heavyAfter = _actor.gameObject.AddComponent<KnightHitStopAfterCoordinatorObserver>();
            _heavyAfter.Sample = () =>
            {
                if (!_heavyRecording) return;
                try
                {
                    if (!_heavyLoops.TryGetValue(Time.frameCount, out HeavyLoopFact loop)) throw new InvalidOperationException("Missing real order50 Heavy sample.");
                    loop.presentedAfter = _heavyPresenter.PresentedState.ToString(); loop.afterSpeed = _animator.speed;
                    loop.afterBase = _heavySpeed.BaseSpeed; loop.afterGrade = _heavySpeed.ActiveGrade.ToString(); loop.afterCaptured = true;
                }
                catch (Exception error) { if (_heavyObserverError == null) _heavyObserverError = error; }
            };
            foreach (string path in new[] { HeavyFixturePath, "Assets/_Game/Tests/PlayMode/KayKitKnightInputCandidateTests.cs",
                "Assets/_Game/Tests/PlayMode/KnightHitStopBeforePresenterObserver.cs", "Assets/_Game/Tests/PlayMode/KnightHitStopAfterCoordinatorObserver.cs",
                "Assets/_Game/Scripts/Editor/Review/KayKitKnightHeavyMotionDerivation.cs", "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerCombatActor.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Domain/CombatStateMachine.cs", "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationSet.cs", "Assets/_Game/Scripts/Gameplay/Animation/AnimatorSpeedCoordinator.cs",
                "Assets/_Game/Scripts/Gameplay/Input/PlayerInputReader.cs", HeavyMotionFolder.TrimEnd('/') + ".meta" })
                foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
            _trace.ability = "RMB/Heavy/" + _heavyRecord.test; _trace.candidate = HeavyChargePath + " + " + HeavyReleasePath;
            _trace.scope = _heavyRecord.scope; _trace.abilityScope = _heavyRecord.phaseScope + " " + _heavyRecord.seamScope;
            _trace.fullAbilityAccepted = false; _trace.candidateLength = _heavyReleaseClip.length;
            Assert.That(_expectedTuning.HeavyFullChargeSeconds, Is.EqualTo(.55f)); Assert.That(_expectedTuning.HeavyDuration, Is.EqualTo(.82f));
            Assert.That(_expectedTuning.HeavyDamageOpen, Is.EqualTo(.235f)); Assert.That(_expectedTuning.HeavyDamageClose, Is.EqualTo(.475f));
            Assert.That(_expectedTuning.HeavyStaminaCost, Is.EqualTo(28f));
        }

        IEnumerator WaitForHeavySteadySampling()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + HeavyPreInputDeadlineSeconds, started = double.NaN;
            for (int count = 0; count < 5000 && Time.realtimeSinceStartupAsDouble <= deadline; count++)
            {
                yield return new WaitForEndOfFrame(); HeavySafety(false);
                HeavySteadyFact previous = _heavySteady.LastOrDefault();
                double gap = previous == null ? 0 : Time.timeAsDouble - previous.time;
                int frameGap = previous == null ? 0 : Time.frameCount - previous.frame;
                bool shortFrame = HeavyFinite(Time.deltaTime) && Time.deltaTime > 0 && Time.deltaTime <= HeavyMaximumSampleSeconds &&
                    (previous == null || (frameGap == 1 && gap > 0 && gap <= (double)HeavyMaximumSampleSeconds));
                bool idle = _actor.Model.State == CombatState.Locomotion && !_animator.IsInTransition(0) && _reader.Move == Vector2.zero && !_reader.HeavyHeld &&
                    !(bool)_heavyPendingPressed.GetValue(_reader) && !(bool)_heavyPendingReleased.GetValue(_reader) && Mathf.Abs(_animator.speed - 1) <= HeavyArithmeticTolerance;
                if (!shortFrame || !idle) started = double.NaN;
                else if (double.IsNaN(started)) started = Time.timeAsDouble;
                double continuous = double.IsNaN(started) ? 0 : Time.timeAsDouble - started;
                _heavySteady.Add(new HeavySteadyFact { frame = Time.frameCount, frameGap = frameGap, time = Time.timeAsDouble,
                    delta = Time.deltaTime, sampleSeconds = gap, continuousSeconds = continuous, shortFrame = shortFrame, stableIdle = idle });
                _heavyRecord.preInputSteadySeconds = continuous;
                if (shortFrame && idle && continuous >= HeavyPreInputSteadySeconds) { _heavyRecord.preInputSteadyCompleted = true; yield break; }
                yield return null;
            }
            Assert.Fail("No actual finite adjacent <=1/30 Idle sampling for >=.15s within the declared8s window; no input or clock reset.");
        }

        HeavyFrame CaptureHeavyFrame(Row row, HeavyLoopFact loop)
        {
            var frame = new HeavyFrame { pose = row, loop = loop, heavyHeld = _reader.HeavyHeld };
            string state = row.domain == CombatState.HeavyCharge.ToString() ? "HeavyCharge" : row.domain == CombatState.HeavyAttack.ToString() ? "PlayerHeavyAttack" : null;
            float length = state == "HeavyCharge" ? .55f : .82f;
            if (state != null && (row.currentState == state || row.nextState == state))
            {
                frame.clipTimeMeasured = true; frame.actualClipSeconds = (row.currentState == state ? row.currentNormalized : row.nextNormalized) * length;
                frame.expectedDomainPhaseSeconds = row.elapsed; frame.signedDomainPhaseDifference = row.elapsed - frame.actualClipSeconds;
                _heavyRecord.maximumDomainPhaseDifference = Mathf.Max(_heavyRecord.maximumDomainPhaseDifference, Mathf.Abs(frame.signedDomainPhaseDifference));
            }
            HeavyFrame previous = _heavyFrames.LastOrDefault(); double delta = previous == null ? 0 : row.time - previous.pose.time;
            frame.joints = _heavyJointTransforms.Select((transform, index) =>
            {
                var joint = new HeavyJointSample { name = transform.name, path = HeavyTransformPath(_animator.transform, transform),
                    actorPositionMetres = _actor.transform.InverseTransformPoint(transform.position), actorRotation = Quaternion.Inverse(_actor.transform.rotation) * transform.rotation };
                if (previous != null && delta > 0)
                { joint.velocityMeasured = true; joint.velocityMetresPerSecond = (joint.actorPositionMetres - previous.joints[index].actorPositionMetres) / (float)delta;
                    joint.angularSpeedDegreesPerSecond = Quaternion.Angle(previous.joints[index].actorRotation, joint.actorRotation) / (float)delta; }
                return joint;
            }).ToArray();
            return frame;
        }

        void MeasureHeavyTerminalPose(HeavyFrame frame)
        {
            for (int index = 0; index < _heavyTransforms.Length; index++)
            {
                var transform = _heavyTransforms[index]; var held = _heavyTerminalPose[index];
                frame.heldLocalPositionDifference = Mathf.Max(frame.heldLocalPositionDifference, Vector3.Distance(transform.localPosition, held.localPosition));
                frame.heldLocalScaleDifference = Mathf.Max(frame.heldLocalScaleDifference, Vector3.Distance(transform.localScale, held.localScale));
                Quaternion q = transform.localRotation, reference = held.localRotation;
                float same = Mathf.Max(Mathf.Abs(q.x - reference.x), Mathf.Abs(q.y - reference.y), Mathf.Abs(q.z - reference.z), Mathf.Abs(q.w - reference.w));
                float opposite = Mathf.Max(Mathf.Abs(q.x + reference.x), Mathf.Abs(q.y + reference.y), Mathf.Abs(q.z + reference.z), Mathf.Abs(q.w + reference.w));
                frame.heldQuaternionComponentDifference = Mathf.Max(frame.heldQuaternionComponentDifference, Mathf.Min(same, opposite));
            }
            _heavyRecord.maximumHeldLocalPositionDifference = Mathf.Max(_heavyRecord.maximumHeldLocalPositionDifference, frame.heldLocalPositionDifference);
            _heavyRecord.maximumHeldQuaternionComponentDifference = Mathf.Max(_heavyRecord.maximumHeldQuaternionComponentDifference, frame.heldQuaternionComponentDifference);
            _heavyRecord.maximumHeldLocalScaleDifference = Mathf.Max(_heavyRecord.maximumHeldLocalScaleDifference, frame.heldLocalScaleDifference);
        }

        HeavyPoseSnapshot[] CaptureHeavyLocalPose() => _heavyTransforms.Select(transform => new HeavyPoseSnapshot
        { path = HeavyTransformPath(_animator.transform, transform), localPosition = transform.localPosition, localRotation = transform.localRotation, localScale = transform.localScale }).ToArray();

        HeavyJointSeam[] HeavyActualSeam(HeavyFrame before, HeavyFrame released) => released.joints.Select((joint, index) => new HeavyJointSeam
        {
            name = joint.name, path = joint.path, positionDifferenceMetres = Vector3.Distance(before.joints[index].actorPositionMetres, joint.actorPositionMetres),
            rotationDifferenceDegrees = Quaternion.Angle(before.joints[index].actorRotation, joint.actorRotation),
            approachingVelocityMetresPerSecond = before.joints[index].velocityMetresPerSecond, firstReleaseVelocityMetresPerSecond = joint.velocityMetresPerSecond,
            approachingAngularSpeedDegreesPerSecond = before.joints[index].angularSpeedDegreesPerSecond, firstReleaseAngularSpeedDegreesPerSecond = joint.angularSpeedDegreesPerSecond,
            approachingVelocityMeasured = before.joints[index].velocityMeasured, firstReleaseVelocityMeasured = joint.velocityMeasured
        }).ToArray();

        void EvaluateHeavyInputGates(HeavyInputCase kind)
        {
            var active = _heavyFrames.Where(frame => frame.loop != null).ToArray();
            var charge = active.Where(frame => frame.loop.domain == CombatState.HeavyCharge.ToString()).ToArray();
            var attack = active.Where(frame => frame.loop.domain == CombatState.HeavyAttack.ToString()).ToArray();
            _heavyRecord.lifecyclePassed = HeavyGate("real-input-and-full-tail", () =>
            {
                var pressed = _heavyInputs.Where(input => input.phase == InputActionPhase.Started.ToString()).ToArray();
                var canceled = _heavyInputs.Where(input => input.phase == InputActionPhase.Canceled.ToString()).ToArray();
                Assert.That(pressed.Length, Is.EqualTo(1)); Assert.That(canceled.Length, Is.EqualTo(1));
                Assert.That(pressed[0].pendingPressed && pressed[0].held, Is.True);
                Assert.That(canceled[0].pendingReleased && !canceled[0].held, Is.True);
                Assert.That(_heavyRecord.enteredAttack && _heavyRecord.domainReturned && _heavyRecord.fullIdle && _heavyRecord.executionCompleted, Is.True);
                Assert.That(_heavyRecord.stableIdleSeconds, Is.GreaterThanOrEqualTo(.15)); Assert.That(_reader.HeavyHeld, Is.False);
                Assert.That(_actor.Model.AttackSequence, Is.EqualTo(_heavyRecord.sequenceBefore + 1));
                Assert.That(_candidateSet.GetOfflineEntryTimeOffset(CombatState.HeavyCharge, .2f, 1f), Is.Zero);
                Assert.That(_candidateSet.GetOfflineEntryTimeOffset(CombatState.HeavyAttack, .2f, 1f), Is.Zero);
                Assert.That(attack, Is.Not.Empty);
                Assert.That(attack[0].loop.elapsed, Is.EqualTo(attack[0].loop.delta).Within(HeavyArithmeticTolerance));
                var returned = active.Single(frame => frame.pose.frame == _heavyRecord.returnedFrame);
                Assert.That(returned.loop.elapsed, Is.Zero);
                Assert.That(attack.Last().loop.elapsed, Is.LessThan(_expectedTuning.HeavyDuration));
                Assert.That(attack.Last().loop.elapsed + returned.loop.delta, Is.GreaterThanOrEqualTo(_expectedTuning.HeavyDuration));
                if (kind == HeavyInputCase.SameDynamicBatch)
                {
                    Assert.That(pressed[0].frame, Is.EqualTo(canceled[0].frame)); Assert.That(canceled[0].pendingPressed, Is.True);
                    Assert.That(attack[0].pose.frame, Is.EqualTo(canceled[0].frame)); Assert.That(charge, Is.Empty);
                    Assert.That(_heavyRecord.presentedCharge, Is.False); Assert.That(canceled[0].domain, Is.EqualTo(CombatState.Locomotion.ToString()));
                }
                else
                {
                    Assert.That(charge, Is.Not.Empty); Assert.That(_heavyRecord.presentedCharge, Is.True);
                    Assert.That(charge[0].loop.elapsed, Is.EqualTo(charge[0].loop.delta).Within(HeavyArithmeticTolerance));
                    Assert.That(canceled[0].domain, Is.EqualTo(CombatState.HeavyCharge.ToString()));
                    if (kind == HeavyInputCase.OneEofHold)
                    { Assert.That(charge.Length, Is.EqualTo(1)); Assert.That(canceled[0].frame, Is.EqualTo(pressed[0].frame + 1)); }
                }
            });
            _heavyRecord.coveragePassed = HeavyGate("entry-to-full-tail-sampling", () =>
            {
                Assert.That(_heavyRecord.preInputSteadyCompleted, Is.True); Assert.That(_heavyRecord.preInputSteadySeconds, Is.GreaterThanOrEqualTo(HeavyPreInputSteadySeconds));
                Assert.That(active, Is.Not.Empty);
                for (int index = 1; index < _heavyFrames.Count; index++)
                {
                    var current = _heavyFrames[index]; var previous = _heavyFrames[index - 1]; double delta = current.pose.time - previous.pose.time;
                    Assert.That(current.pose.frame - previous.pose.frame, Is.EqualTo(1)); Assert.That(HeavyFinite(current.pose.delta), Is.True);
                    Assert.That(current.pose.delta, Is.GreaterThan(0).And.LessThanOrEqualTo(HeavyMaximumSampleSeconds));
                    Assert.That(delta, Is.GreaterThan(0).And.LessThanOrEqualTo((double)HeavyMaximumSampleSeconds));
                }
            });
            _heavyRecord.resourcePassed = HeavyGate("release-relative-damage-and-single-cost", () =>
            {
                var cancellations = _heavyInputs.Where(input => input.phase == InputActionPhase.Canceled.ToString()).ToArray();
                Assert.That(cancellations.Length, Is.EqualTo(1)); var canceled = cancellations[0];
                _heavyRecord.releaseBeforeChargeElapsed = canceled.domain == CombatState.HeavyCharge.ToString() ? canceled.domainElapsed : 0;
                float fraction = Math.Min(1f, _heavyRecord.releaseBeforeChargeElapsed / _expectedTuning.HeavyFullChargeSeconds);
                _heavyRecord.expectedDamage = _expectedTuning.HeavyDamage * (.65f + .35f * fraction);
                Assert.That(attack, Is.Not.Empty);
                foreach (var frame in charge) Assert.That(frame.loop.stamina, Is.EqualTo(_heavyRecord.staminaBefore).Within(HeavyArithmeticTolerance));
                foreach (var frame in attack)
                {
                    Assert.That(frame.loop.duration, Is.EqualTo(.82f)); Assert.That(frame.loop.damage, Is.EqualTo(_heavyRecord.expectedDamage).Within(HeavyArithmeticTolerance));
                    Assert.That(frame.loop.stamina, Is.EqualTo(canceled.stamina - _expectedTuning.HeavyStaminaCost).Within(HeavyArithmeticTolerance));
                    Assert.That(frame.loop.fullyCharged, Is.EqualTo(fraction >= .999f));
                }
                Assert.That(active.All(frame => Mathf.Abs(frame.loop.health - _heavyRecord.healthBefore) <= HeavyArithmeticTolerance), Is.True);
                if (kind == HeavyInputCase.SameDynamicBatch) Assert.That(_heavyRecord.expectedDamage, Is.EqualTo(35.75f));
                if (kind == HeavyInputCase.TenSecondHold) Assert.That(_heavyRecord.expectedDamage, Is.EqualTo(55f));
            });
            _heavyRecord.damageWindowPassed = HeavyGate("original-release-state-damage-window", () =>
            {
                Assert.That(attack.Any(frame => frame.loop.damageWindow), Is.True);
                foreach (var frame in attack) Assert.That(frame.loop.damageWindow,
                    Is.EqualTo(frame.loop.elapsed >= _expectedTuning.HeavyDamageOpen && frame.loop.elapsed <= _expectedTuning.HeavyDamageClose));
                Assert.That(charge.All(frame => !frame.loop.damageWindow && frame.loop.duration == 0), Is.True);
                Assert.That(active.Where(frame => frame.loop.domain == CombatState.Locomotion.ToString()).All(frame => !frame.loop.damageWindow), Is.True);
            });
            _heavyRecord.incrementPassed = HeavyGate("cold-base1-animation-increments-not-domain-sync", () =>
            {
                foreach (var frame in active)
                {
                    Assert.That(frame.loop.afterGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(frame.loop.afterSpeed, Is.EqualTo(1f).Within(HeavyArithmeticTolerance)); Assert.That(frame.loop.afterBase, Is.EqualTo(1f).Within(HeavyArithmeticTolerance));
                    Assert.That(frame.loop.presentedAfter, Is.EqualTo(frame.loop.domain));
                }
                for (int index = 1; index < _heavyFrames.Count; index++)
                {
                    var frame = _heavyFrames[index]; var previous = _heavyFrames[index - 1];
                    if (!frame.clipTimeMeasured || !previous.clipTimeMeasured || frame.pose.domain != previous.pose.domain) continue;
                    float error = Mathf.Abs((frame.actualClipSeconds - previous.actualClipSeconds) - frame.loop.delta);
                    _heavyRecord.maximumClipIncrementError = Mathf.Max(_heavyRecord.maximumClipIncrementError, error);
                    Assert.That(error, Is.LessThanOrEqualTo(HeavyArithmeticTolerance));
                }
            });
            _heavyRecord.holdGateApplicable = kind == HeavyInputCase.TenSecondHold;
            _heavyRecord.holdPassed = _heavyRecord.holdGateApplicable && HeavyGate("long-nonloop-static-hold", () =>
            {
                Assert.That(charge.Last().loop.elapsed, Is.GreaterThanOrEqualTo(HeavyLongHoldSeconds));
                Assert.That(_heavyRecord.terminalFrames, Is.GreaterThan(1)); Assert.That(_heavyRecord.terminalHoldSeconds, Is.GreaterThanOrEqualTo(HeavyMinimumTerminalHoldSeconds));
                Assert.That(_heavyRecord.maximumHeldLocalPositionDifference, Is.LessThanOrEqualTo(HeavyHeldTransformTolerance));
                Assert.That(_heavyRecord.maximumHeldQuaternionComponentDifference, Is.LessThanOrEqualTo(HeavyHeldTransformTolerance));
                Assert.That(_heavyRecord.maximumHeldLocalScaleDifference, Is.LessThanOrEqualTo(HeavyHeldTransformTolerance));
                Assert.That(charge.All(frame => frame.heavyHeld && frame.loop.duration == 0), Is.True);
            });
            _heavyRecord.bodyMin = _heavyFrames.Min(frame => frame.pose.bodyMin); _heavyRecord.capeMin = _heavyFrames.Min(frame => frame.pose.capeMin); _heavyRecord.gearMin = _heavyFrames.Min(frame => frame.pose.gearMin);
            _heavyRecord.floorPassed = HeavyGate("unchanged-signed-body-cape-gear-floor", () =>
            {
                Assert.That(_heavyRecord.bodyMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_heavyRecord.capeMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_heavyRecord.gearMin, Is.GreaterThanOrEqualTo(0f));
            });
            _heavyRecord.hipsPassed = HeavyGate("unchanged-hips-and-Actor-anchor", () =>
            {
                Assert.That(_heavyRecord.peakHips, Is.LessThan(.4f)); Assert.That(_heavyRecord.finalHips, Is.LessThan(.1f));
                Assert.That(_heavyRecord.rootDistance, Is.LessThan(.01f));
            });
        }

        void HeavySafety(bool verifySources)
        {
            Assert.That(_scene.IsValid() && _scene.isLoaded && _actor != null && _actor.gameObject.scene == _scene, Is.True);
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.EquivalentTo(new[] { _actor }));
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            foreach (var flow in Object.FindObjectsOfType<M2RouteFlowController>()) Assert.That(flow.SavePath, Does.Contain("IsolatedSaves"));
            Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(_candidateController)); Assert.That(AssetDatabase.GetAssetPath(_animator.avatar), Is.EqualTo(Native));
            Assert.That(_animator.avatar.isHuman && _animator.avatar.isValid, Is.True);
            Assert.That(_candidateSet.GetClip(CombatState.HeavyCharge), Is.SameAs(_heavyChargeClip)); Assert.That(_candidateSet.GetOfflineClip(CombatState.HeavyAttack), Is.SameAs(_heavyReleaseClip));
            Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
            Assert.That(_animator.transform.localScale, Is.EqualTo(_heavyInnerScale)); Assert.That(_heavyOuter.localPosition, Is.EqualTo(_heavyOuterPosition));
            Assert.That(_heavyOuter.localRotation, Is.EqualTo(_heavyOuterRotation)); Assert.That(_heavyOuter.localScale, Is.EqualTo(_heavyOuterScale));
            Assert.That(_heavyOuterScale, Is.EqualTo(Vector3.one * .75f)); Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f)); Assert.That(HeavyFinite(_actor.Model.StateElapsed) && HeavyFinite(Time.deltaTime), Is.True);
            if (!verifySources) return;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(overrides);
            Assert.That(overrides, Is.EqualTo(_heavyOverrides)); Assert.That(EditorJsonUtility.ToJson(_candidateSet), Is.EqualTo(_heavySetJson));
            Assert.That(_disk.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value), Is.True, "Source bytes changed: immediate stop, no restoration or exceptions.");
            Assert.That(_memory.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value), Is.True, "Source memory changed: immediate stop, no cache exemption.");
        }

        bool HeavyGate(string gate, Action check)
        {
            try { check(); return true; }
            catch (AssertionException error) { _heavyFailures.Add(new GateFailure { gate = gate, message = error.Message, stackTrace = error.StackTrace }); return false; }
        }
        static bool HeavyFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string HeavyTransformPath(Transform root, Transform target)
        {
            var names = new List<string>();
            while (target != root && target != null) { names.Add(target.name); target = target.parent; }
            Assert.That(target, Is.SameAs(root)); names.Reverse(); return string.Join("/", names);
        }
    }
}
#endif
