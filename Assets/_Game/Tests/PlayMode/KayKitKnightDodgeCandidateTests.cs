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
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed partial class KayKitKnightInputCandidateTests
    {
        // These are declared before a run, not inferred from measured foot sliding or facing.
        // At this fixture's original world coordinate500 a float ULP is about .0000305m.
        // .0002m covers fewer than7 coordinate ULPs for CC.Move/subtraction arithmetic.
        const float DodgeFramePositionTolerance = .0002f;
        const float DodgeRotationArithmeticTolerance = .05f;
        const float DodgeScalarTolerance = .00001f;
        const float DodgeMaximumSampleSeconds = 1f / 30f;
        const double DodgeRequiredSteadySeconds = .15, DodgeSteadyDeadlineSeconds = 8, DodgeGpuDeadlineSeconds = 8;
        const string DodgeFixturePath = "Assets/_Game/Tests/PlayMode/KayKitKnightDodgeCandidateTests.cs";
        static readonly string[] DodgeRuntimeSources =
        {
            "Assets/_Game/Scripts/Gameplay/Movement/ThirdPersonMotor.cs",
            "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerCombatActor.cs",
            "Assets/_Game/Scripts/Gameplay/Combat/Domain/CombatStateMachine.cs",
            "Assets/_Game/Scripts/Gameplay/Combat/Domain/DodgeTravelProfile.cs",
            "Assets/_Game/Scripts/Gameplay/Combat/Domain/StaminaModel.cs",
            "Assets/_Game/Scripts/Gameplay/Input/PlayerInputReader.cs",
            "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs",
            "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationSet.cs",
            "Assets/_Game/Scripts/Gameplay/Animation/AnimatorSpeedCoordinator.cs",
            "Assets/_Game/Tests/PlayMode/KayKitKnightInputCandidateTests.cs",
            DodgeFixturePath
        };

        [Serializable] sealed class DodgeInputRecord
        {
            public string kind, action, control, domain; public int frame, keyboardId, mouseId;
            public double time; public Vector2 move; public bool dodge, w, a, s, d, space;
        }
        [Serializable] sealed class DodgeLoopRecord
        {
            public int frame; public double time; public float delta, elapsed, normalized, duration;
            public string domain, presentedBeforeAnimator; public Vector2 actorDodgeInput, currentMove;
            public Vector3 root, committedDirection, horizontalVelocity; public Quaternion rotation;
            public float motorTravel, authoredRotationSpeed; public bool captured;
        }
        [Serializable] sealed class DodgeRow
        {
            public Row pose; public DodgeLoopRecord beforePresenter; public Vector3 leftFootWorld, rightFootWorld, leftFootActor, rightFootActor;
            public Vector3 actualFrameDisplacement, expectedFrameDisplacement, leftFootWorldVelocity, rightFootWorldVelocity;
            public float stamina, capsuleMin, stateNormalized, motorTravel, travelFormulaError, framePositionError, rootYaw;
            public float turnDegrees, maximumTurnDegrees, expectedRotationError, facingVsCommitted, facingVsVelocity;
            public bool grounded, invulnerable, worldFootVelocityMeasured, velocityFacingMeasured;
            public float currentStateSpeed, currentStateMultiplier, nextStateSpeed, nextStateMultiplier, transitionDuration, transitionNormalized; public string transitionDurationUnit;
            public Vector3 outerLocalPosition, outerLocalScale, animatorLocalPosition, animatorLocalScale, animatorLossyScale;
            public Quaternion outerLocalRotation, animatorLocalRotation;
            public double eofRealtime, sampleSeconds, realtimeSampleSeconds, lightSafetyMilliseconds, poseObservationMilliseconds, imageSubmissionMilliseconds;
            public int previousFrame, frameGap; public string previousDomain;
        }
        [Serializable] sealed class DodgeSteadyRow
        {
            public int frame, previousFrame, frameGap; public double time, realtime, continuousSeconds, sampleSeconds; public float delta;
            public bool finiteShortFrame, stableIdle; public string resetReason;
        }
        [Serializable] sealed class DodgeGpuCapture
        {
            public string file, output, nativeError, encodeError, scope = "Actual EOF ScreenCapture into an independent preallocated RT; callback copies native RGBA32 bytes; PNG encoding is after the measured tail (or after an aborted run).";
            public int sourceFrame, completedFrame, width, height, byteCount;
            public double sourceTime, sourceRealtime, completedRealtime, submissionMilliseconds;
            public bool captureSubmitted, requestCallBegan, requestReturned, completed, timedOut, released, encoded, completedAfterClosure;
            [NonSerialized] public RenderTexture target;
            [NonSerialized] public byte[] rgba;
        }
        sealed class DodgeGpuBatch
        {
            public readonly string output;
            public readonly List<DodgeGpuCapture> captures = new List<DodgeGpuCapture>();
            public bool closed;
            public DodgeGpuBatch(string outputPath) { output = outputPath; }
        }
        [Serializable] sealed class DodgeRecord
        {
            public string input, status = "running", abortReason, candidate, clipGuid, sourceActor, sourceController, output, coreTrace;
            public long clipLocalId;
            public string scope = "One isolated original .75 Knight/Human rig and original authored Actor/capsule; original native Dodge_Forward only, nonpersistent SO/AOC. Actual queued Keyboard W/A/S/D+Space through PlayerLoop/InputReader/Actor/Motor/Presenter, not OS input or formal TPS. No manual Tick/Animator.Update/Cancel/warp/root lift/source mutations. Four independent leaves; no production/network/contact/feel acceptance.";
            public string directionScope = "Actor captures Move when consuming the actual Space edge; Motor resolves the fixed original camera-reference frame once on the first Dodge Update and commits _dodgeDirection. Direction is never resampled by the test. Actor smoothly rotates using the original authored rotation speed; side/back native clips are not also applied.";
            public string footScope = "Mapped LeftFoot/RightFoot WORLD joint positions and finite-difference velocities are diagnostics, not plantar contact, inferred source displacement, slip thresholds or feel acceptance. Facing max/mean are independently reported per input, without averaging four leaves into acceptance.";
            public string floorScope = "All enabled actual body/cape/gear drawn vertices, BakeMesh(true), signed world Y>=0 at original capsule-foot anchor. No CPU Y0/old rig acceptance transfer. Native model-origin Y0 evidence stays separate and is NOT newly sampled in this run.";
            public string motionScope = "Frame XZ displacement equals original DodgeTravelProfile.Evaluate(actual per-state normalized time) increments times original4.5m, with predeclared world500 float arithmetic tolerance .0002m. Quaternion rotation arithmetic tolerance .05deg is not a facing-feel allowance. Complete actual Idle fade followed by >=.15s settled Idle; final tail must not slide .01m.";
            public string elapsedSource = "CombatStateMachine.EnterState resets StateElapsed=0; Tick adds actual delta before Motor order-100. Order50 observer reads that per-state value after Motor and before Presenter100, then normal EOF reads evaluated Animator pose. No global-time-derived substitute.";
            public float declaredFramePositionTolerance = DodgeFramePositionTolerance, declaredRotationTolerance = DodgeRotationArithmeticTolerance;
            public float declaredPeakHipsLimit = .4f, declaredFinalHipsLimit = .1f, declaredTailDistanceLimit = .01f, declaredStableIdleSeconds = .15f;
            public float declaredMaximumSampleSeconds = DodgeMaximumSampleSeconds;
            public double declaredPreInputSteadySeconds = DodgeRequiredSteadySeconds, declaredPreInputDeadlineSeconds = DodgeSteadyDeadlineSeconds, declaredGpuDeadlineSeconds = DodgeGpuDeadlineSeconds;
            public string coverageScope = "Source SHA/full-JSON audit precedes settling. Before actual input, >=.15s uninterrupted normal PlayerLoop finite delta<=1/30 is required. The first Motor/Animator frame and every actual action/transition/Idle-tail sample gap must also be <=1/30; a missed interval remains a coverage failure, never synthetic catch-up or motion acceptance.";
            public string gapAttributionScope = "Game-time and realtime gaps are measured independently. Same-run light safety, pose/BakeMesh observation and GPU submission wall durations identify possible preceding work, not an exclusive cause or a solver/contact diagnosis.";
            public double preInputSteadySeconds, preInputStartedRealtime, preInputEndedRealtime, maximumSampleSeconds, maximumRealtimeSampleSeconds;
            public double preInputPoseWarmupMilliseconds;
            public bool preInputSteadyCompleted, coverageGatePassed, gpuReadbackGatePassed;
            public int firstSampleFrame, firstMotorFrame, largestGapPreviousFrame, largestGapFrame, largestGapFrameCount;
            public double firstSampleSeconds; public float firstMotorDelta, firstMotorElapsed, firstMotorNormalized, firstMotorTravel, firstAnimatorCurrentNormalized, firstAnimatorNextNormalized;
            public string largestGapPreviousDomain, largestGapDomain, largestGapPreviousImage;
            public double largestGapPreviousLightSafetyMilliseconds, largestGapPreviousPoseObservationMilliseconds, largestGapPreviousImageSubmissionMilliseconds;
            public float duration, invulnerabilitySeconds, distance, staminaCost, travelEndNormalized, nativeLength, expectedPlaybackSpeed;
            public float healthBefore, staminaBefore, initialStaminaAfterSpend, peakHips, finalHips, bodyMin, capeMin, gearMin, capsuleMin;
            public float maximumFramePositionError, maximumTravelFormulaError, maximumTurnArithmeticError, finalTravelDistanceError, tailDistance, stableIdleSeconds;
            public float maximumFacingVsVelocity, meanFacingVsVelocity, timeWeightedMeanFacingVsVelocity, maximumLeftFootWorldSpeed, maximumRightFootWorldSpeed;
            public Vector2 requestedMove, capturedActorDodgeInput; public Vector3 cameraForward, cameraRight, requestedWorldDirection, committedDirection, rootBefore, rootAfter;
            public Quaternion cameraRotation, rotationBefore; public Vector3 cameraPosition;
            public int enteredFrame, returnedFrame, settledFrame, travelCompletedFrame, inputPerformedCount, dodgeAttemptBefore, dodgeAttemptAfter, dodgeFrames, velocityFacingSamples, worldFootVelocitySamples;
            public Vector3 travelCompletedRoot; public double travelCompletedAt;
            public double inputQueuedAt, enteredAt, domainReturnedAt, animatorSettledAt, stableIdleEndedAt;
            public bool entered, domainReturned, animatorSettled, stableIdleCompleted, executionCompleted, sourcesVerifiedBefore, sourcesVerifiedAfter;
            public bool movementGatePassed, directionGatePassed, rotationGatePassed, hipsGatePassed, signedFloorPassed, capsuleGatePassed, lifecycleGatePassed, resourceGatePassed;
            public bool diagnosticContractPassed, fullAbilityAccepted, footContactAccepted, facingFeelAccepted, naturalContactAccepted;
            public string freezePolicyScope = "Cold Dodge only: no actual freeze Request in this batch, so the production Dodge cancellation policy is not accepted by these four cases.";
            public string hierarchyScope = "Exact persistent owned Prefab Animator path binds the cloned outer Actor child to the native inner Animator. The original .75 belongs to the OUTER visual; inner local TRS stays the source Prefab's TRS. Lossy scale is measured per EOF, never an exact world-float equality gate.";
            public string sourcePrefabGuid, sourcePrefabName, sourceAnimatorPath, outerPathFromActor, animatorPathFromActor;
            public Vector3 originalOuterLocalPosition, originalOuterLocalScale, originalAnimatorLocalPosition, originalAnimatorLocalScale, initialAnimatorLossyScale;
            public Quaternion originalOuterLocalRotation, originalAnimatorLocalRotation;
            public string sourceFileSha; public string[] images, seenStates; public DodgeRow[] rows; public DodgeInputRecord[] inputs; public GateFailure[] failures; public DiskRow[] files; public MemoryRow[] changedMemory;
            public DodgeSteadyRow[] preInputRows; public DodgeGpuCapture[] gpuCaptures;
        }

        DodgeRecord _dodgeRecord; ThirdPersonMotor _dodgeMotor; CharacterController _dodgeCapsule;
        Transform _dodgeLeftFoot, _dodgeRightFoot, _dodgeCameraFrame, _dodgeOuterVisual;
        string _dodgeAnimatorPath;
        Vector3 _dodgeOuterPosition, _dodgeOuterScale, _dodgeInnerPosition, _dodgeInnerScale;
        Quaternion _dodgeOuterRotation, _dodgeInnerRotation;
        AnimationClip _dodgeClip, _dodgeOriginalKey; KnightHitStopBeforePresenterObserver _dodgeBefore;
        FieldInfo _dodgeDirectionField, _dodgeTravelField, _dodgeVelocityField, _dodgeCameraField, _dodgeRotationField;
        PlayerAnimationPresenter _dodgePresenter; InputAction _dodgeAction; Action<InputAction.CallbackContext> _dodgeInputCallback;
        readonly List<DodgeRow> _dodgeRows = new List<DodgeRow>();
        readonly List<DodgeInputRecord> _dodgeInputs = new List<DodgeInputRecord>();
        readonly List<GateFailure> _dodgeFailures = new List<GateFailure>();
        readonly Dictionary<int, DodgeLoopRecord> _dodgeLoops = new Dictionary<int, DodgeLoopRecord>();
        KeyValuePair<AnimationClip, AnimationClip>[] _dodgeFrozenOverrides;
        string _dodgeFrozenSet; Exception _dodgeObserverError;
        readonly List<DodgeSteadyRow> _dodgeSteadyRows = new List<DodgeSteadyRow>();
        DodgeGpuBatch _dodgeGpuBatch;
        static readonly List<DodgeGpuCapture> ClosedDodgeGpuCaptures = new List<DodgeGpuCapture>();

        [UnityTest] public IEnumerator ActualW_SpaceNativeForwardDodgeRecordsCommittedMotorAndFullIdle() => NativeForwardDodge(Key.W, new Vector2(0f, 1f));
        [UnityTest] public IEnumerator ActualA_SpaceNativeForwardDodgeRecordsCommittedMotorAndFullIdle() => NativeForwardDodge(Key.A, new Vector2(-1f, 0f));
        [UnityTest] public IEnumerator ActualS_SpaceNativeForwardDodgeRecordsCommittedMotorAndFullIdle() => NativeForwardDodge(Key.S, new Vector2(0f, -1f));
        [UnityTest] public IEnumerator ActualD_SpaceNativeForwardDodgeRecordsCommittedMotorAndFullIdle() => NativeForwardDodge(Key.D, new Vector2(1f, 0f));

        IEnumerator NativeForwardDodge(Key key, Vector2 move)
        {
            _dodgeRows.Clear(); _dodgeInputs.Clear(); _dodgeFailures.Clear(); _dodgeLoops.Clear(); _dodgeSteadyRows.Clear(); _dodgeObserverError = null; _dodgeGpuBatch = null;
            _dodgeRecord = new DodgeRecord { input = key + "+Space", requestedMove = move, output = _output, coreTrace = _output + "/actual-input.json", sourceActor = _trace.sourceActor, sourceController = _trace.sourceController };
            IEnumerator run = DriveNativeForwardDodge(key, move);
            try
            {
                while (true)
                {
                    bool next;
                    try { next = run.MoveNext(); }
                    catch (Exception error) { if (!(error is AssertionException && _dodgeFailures.Count > 0 && _dodgeRecord.status == "diagnostic-gate-failed")) { _dodgeRecord.abortReason = error.ToString(); _dodgeRecord.status = "aborted"; } throw; }
                    if (!next) break;
                    yield return run.Current;
                }
            }
            finally
            {
                (run as IDisposable)?.Dispose();
                if (_dodgeAction != null && _dodgeInputCallback != null) _dodgeAction.performed -= _dodgeInputCallback;
                if (_dodgeBefore != null) _dodgeBefore.Sample = null;
                if (_keyboard != null && _keyboard.added) InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                CloseDodgeGpuBatch(_dodgeGpuBatch);
                if (_dodgeRecord != null)
                {
                    _dodgeRecord.rows = _dodgeRows.ToArray(); _dodgeRecord.inputs = _dodgeInputs.ToArray(); _dodgeRecord.failures = _dodgeFailures.ToArray();
                    _dodgeRecord.images = _images.ToArray(); _dodgeRecord.seenStates = _seenStates.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    _dodgeRecord.files = _disk.Select(p => new DiskRow { path = p.Key, before = p.Value, after = File.Exists(p.Key) ? Hash(p.Key) : "MISSING" }).ToArray();
                    _dodgeRecord.changedMemory = _memory.Where(p => p.Key == null || EditorJsonUtility.ToJson(p.Key) != p.Value).Select(p => new MemoryRow { path = p.Key == null ? "DESTROYED" : AssetDatabase.GetAssetPath(p.Key), name = p.Key == null ? "DESTROYED" : p.Key.name, beforeJson = p.Value, afterJson = p.Key == null ? "DESTROYED" : EditorJsonUtility.ToJson(p.Key) }).ToArray();
                    _dodgeRecord.preInputRows = _dodgeSteadyRows.ToArray(); _dodgeRecord.gpuCaptures = _dodgeGpuBatch?.captures.ToArray();
                    File.WriteAllText(_output + "/actual-dodge.json", JsonUtility.ToJson(_dodgeRecord, true));
                }
                _dodgeAction = null; _dodgeInputCallback = null; _dodgeBefore = null; _dodgePresenter = null; _dodgeMotor = null; _dodgeCapsule = null;
                _dodgeLeftFoot = _dodgeRightFoot = _dodgeCameraFrame = null; _dodgeClip = _dodgeOriginalKey = null; _dodgeFrozenOverrides = null; _dodgeFrozenSet = null;
                _dodgeOuterVisual = null; _dodgeAnimatorPath = null;
                _dodgeGpuBatch = null;
            }
        }

        IEnumerator DriveNativeForwardDodge(Key key, Vector2 move)
        {
            PrepareNativeForwardDodge();
            // The first real avatar/BakeMesh observation took30.6ms in retained a3.
            // Warm only the read-only observer before auditing/settling; never omit,
            // interpolate or relax a measured action frame. Baseline is sampled anew.
            double poseWarmupStarted = Time.realtimeSinceStartupAsDouble;
            Observe(0f);
            _dodgeRecord.preInputPoseWarmupMilliseconds = (Time.realtimeSinceStartupAsDouble - poseWarmupStarted) * 1000;
            _rows.Clear(); _seen.Clear(); _seenStates.Clear();
            // The full immutable-source audit is deliberately BEFORE all settling and input.
            // Its elapsed wall time must not become the first Dodge delta.
            DodgeVerifySafety(true); _dodgeRecord.sourcesVerifiedBefore = true;
            yield return new WaitForSeconds(.5f);
            IEnumerator steady = WaitForStableDodgeSampling();
            try { while (steady.MoveNext()) yield return steady.Current; }
            finally { (steady as IDisposable)?.Dispose(); }
            double safetyStarted = Time.realtimeSinceStartupAsDouble;
            DodgeVerifySafety(false);
            double safetyMilliseconds = (Time.realtimeSinceStartupAsDouble - safetyStarted) * 1000;
            Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); Assert.That(_animator.IsInTransition(0), Is.False); Assert.That(_dodgeCapsule.isGrounded, Is.True);
            Assert.That(_reader.Move, Is.EqualTo(Vector2.zero)); Assert.That(_animator.speed, Is.EqualTo(1f).Within(DodgeScalarTolerance));
            Vector2 hipsBaseline = PlanarHips(); _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation; _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
            _dodgeRecord.rootBefore = _actor.transform.position; _dodgeRecord.rotationBefore = _actor.transform.rotation;
            _dodgeRecord.healthBefore = _actor.Model.Health.Current; _dodgeRecord.staminaBefore = _actor.Model.Stamina.Current; _dodgeRecord.dodgeAttemptBefore = _actor.Model.DodgeAttemptCount;
            _dodgeRecord.cameraPosition = _dodgeCameraFrame.position; _dodgeRecord.cameraRotation = _dodgeCameraFrame.rotation;
            _dodgeRecord.cameraForward = Vector3.ProjectOnPlane(_dodgeCameraFrame.forward, Vector3.up).normalized; _dodgeRecord.cameraRight = Vector3.Cross(Vector3.up, _dodgeRecord.cameraForward);
            _dodgeRecord.requestedWorldDirection = Vector3.ClampMagnitude(_dodgeRecord.cameraForward * move.y + _dodgeRecord.cameraRight * move.x, 1f);
            double observationStarted = Time.realtimeSinceStartupAsDouble;
            Observe(0f); DodgeRow baseline = CaptureDodgeRow(_rows[_rows.Count - 1], null);
            baseline.lightSafetyMilliseconds = safetyMilliseconds; baseline.poseObservationMilliseconds = (Time.realtimeSinceStartupAsDouble - observationStarted) * 1000;
            DodgeCaptureInput("queued", "Dodge", key + "+Space"); RecordInput("queued", "Dodge");
            _dodgeRecord.inputQueuedAt = Time.timeAsDouble; InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key, Key.Space));
            yield return null;
            bool released = false, travelCompleted = false; double stableStarted = double.NaN;
            for (int frame = 0; frame < 5000; frame++)
            {
                yield return new WaitForEndOfFrame();
                if (_dodgeObserverError != null) throw new InvalidOperationException("Order50 Dodge observation aborted.", _dodgeObserverError);
                ThrowDodgeGpuError(_dodgeGpuBatch);
                safetyStarted = Time.realtimeSinceStartupAsDouble;
                DodgeVerifySafety(false);
                safetyMilliseconds = (Time.realtimeSinceStartupAsDouble - safetyStarted) * 1000;
                observationStarted = Time.realtimeSinceStartupAsDouble;
                float drift = Vector2.Distance(hipsBaseline, PlanarHips()); Observe(drift);
                Assert.That(_dodgeLoops.TryGetValue(Time.frameCount, out DodgeLoopRecord loop), Is.True, "Every EOF must have the same real order50 observation; no inferred tick.");
                Row pose = _rows[_rows.Count - 1]; DodgeRow row = CaptureDodgeRow(pose, loop);
                row.lightSafetyMilliseconds = safetyMilliseconds; row.poseObservationMilliseconds = (Time.realtimeSinceStartupAsDouble - observationStarted) * 1000;
                if (_actor.Model.State == CombatState.Dodge && row.motorTravel == 1f && !travelCompleted)
                {
                    travelCompleted = true; _dodgeRecord.travelCompletedFrame = pose.frame; _dodgeRecord.travelCompletedAt = pose.time; _dodgeRecord.travelCompletedRoot = pose.root;
                }
                if (travelCompleted) _dodgeRecord.tailDistance = Mathf.Max(_dodgeRecord.tailDistance, DodgePlanarDistance(_dodgeRecord.travelCompletedRoot, pose.root));
                if (_actor.Model.State == CombatState.Dodge && !_dodgeRecord.entered)
                {
                    _dodgeRecord.entered = true; _dodgeRecord.enteredFrame = row.pose.frame; _dodgeRecord.enteredAt = row.pose.time;
                    _dodgeRecord.capturedActorDodgeInput = loop.actorDodgeInput; _dodgeRecord.committedDirection = loop.committedDirection; _dodgeRecord.initialStaminaAfterSpend = row.stamina;
                    _dodgeRecord.firstSampleFrame = row.pose.frame; _dodgeRecord.firstSampleSeconds = row.sampleSeconds; _dodgeRecord.firstMotorFrame = loop.frame;
                    _dodgeRecord.firstMotorDelta = loop.delta; _dodgeRecord.firstMotorElapsed = loop.elapsed; _dodgeRecord.firstMotorNormalized = loop.normalized; _dodgeRecord.firstMotorTravel = loop.motorTravel;
                    _dodgeRecord.firstAnimatorCurrentNormalized = pose.currentNormalized; _dodgeRecord.firstAnimatorNextNormalized = pose.nextNormalized;
                }
                if (_dodgeRecord.entered && !released)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState()); released = true;
                    DodgeCaptureInput("release-queued", "Dodge/Move", "empty keyboard"); RecordInput("release-queued", "Dodge");
                }
                if (_dodgeRecord.entered && _actor.Model.State == CombatState.Locomotion && !_dodgeRecord.domainReturned)
                {
                    _dodgeRecord.domainReturned = true; _dodgeRecord.returnedFrame = pose.frame; _dodgeRecord.domainReturnedAt = pose.time;
                    _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = pose.time;
                }
                if (_dodgeRecord.domainReturned && double.IsNaN(stableStarted) && pose.stableLocomotion)
                {
                    stableStarted = pose.time; _dodgeRecord.animatorSettled = true; _dodgeRecord.settledFrame = pose.frame; _dodgeRecord.animatorSettledAt = pose.time;
                    _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = pose.time;
                }
                if (!double.IsNaN(stableStarted))
                {
                    _dodgeRecord.stableIdleSeconds = (float)(pose.time - stableStarted);
                    if (pose.time - stableStarted >= .15)
                    {
                        _dodgeRecord.stableIdleCompleted = true; _dodgeRecord.stableIdleEndedAt = pose.time; _dodgeRecord.finalHips = drift;
                        _trace.stableIdleCompleted = true; _trace.stableIdleSeconds = _dodgeRecord.stableIdleSeconds; _trace.stableIdleEndedAt = pose.time; _trace.final = drift;
                        CaptureDodgeImage("02-dodge-settled-idle.png", pose); break;
                    }
                }
                if (_actor.Model.State == CombatState.Dodge && pose.elapsed >= _expectedTuning.DodgeDuration * .5f && !_images.Contains("01-dodge-mid.png"))
                    CaptureDodgeImage("01-dodge-mid.png", pose);
                yield return null;
            }
            _dodgeRecord.rootAfter = _actor.transform.position; _dodgeRecord.dodgeAttemptAfter = _actor.Model.DodgeAttemptCount;
            _dodgeRecord.executionCompleted = _dodgeRecord.stableIdleCompleted;
            // Measurements have ended; readbacks/PNG work cannot create extra pose samples.
            if (_dodgeBefore != null) _dodgeBefore.Sample = null;
            IEnumerator readbacks = FinishDodgeGpuReadbacks();
            try { while (readbacks.MoveNext()) yield return readbacks.Current; }
            finally { (readbacks as IDisposable)?.Dispose(); }
            DodgeVerifySafety(true); _dodgeRecord.sourcesVerifiedAfter = true;
            EvaluateDodgeGates();
            // GPU readback happened at the real EOF poses; expensive PNG encoding is after the full tail.
            SaveCapturedImages();
            foreach (DodgeGpuCapture capture in _dodgeGpuBatch.captures)
                capture.encoded = capture.completed && string.IsNullOrEmpty(capture.nativeError) && string.IsNullOrEmpty(_trace.imageWriteError) && File.Exists(capture.output + "/" + capture.file);
            DodgeGate("images", () =>
            {
                Assert.That(_trace.imageWriteError, Is.Null.Or.Empty);
                Assert.That(_images, Is.EquivalentTo(new[] { "01-dodge-mid.png", "02-dodge-settled-idle.png" }));
                foreach (string file in _images) Assert.That(File.Exists(_output + "/" + file) && new FileInfo(_output + "/" + file).Length > 100, Is.True);
            });
            _dodgeRecord.diagnosticContractPassed = _dodgeFailures.Count == 0;
            _dodgeRecord.status = _dodgeRecord.diagnosticContractPassed ? "diagnostic-contract-passed-not-full-ability" : "diagnostic-gate-failed";
            _completed = _dodgeRecord.diagnosticContractPassed;
            Assert.That(_dodgeFailures, Is.Empty, "Dodge diagnostics retained through the complete tail: " + string.Join("; ", _dodgeFailures.Select(f => f.gate + ": " + f.message)));
        }

        IEnumerator WaitForStableDodgeSampling()
        {
            _dodgeRecord.preInputStartedRealtime = Time.realtimeSinceStartupAsDouble;
            double steadyStarted = double.NaN;
            for (int frame = 0; frame < 5000 && Time.realtimeSinceStartupAsDouble - _dodgeRecord.preInputStartedRealtime <= DodgeSteadyDeadlineSeconds; frame++)
            {
                yield return new WaitForEndOfFrame();
                if (_dodgeObserverError != null) throw new InvalidOperationException("Pre-input order50 observation aborted.", _dodgeObserverError);
                DodgeVerifySafety(false);
                float delta = Time.deltaTime;
                DodgeSteadyRow previous = _dodgeSteadyRows.LastOrDefault();
                double sampleSeconds = previous == null ? 0 : Time.timeAsDouble - previous.time;
                int frameGap = previous == null ? 0 : Time.frameCount - previous.frame;
                bool shortFrame = DodgeFinite(delta) && delta > 0f && delta <= DodgeMaximumSampleSeconds && (previous == null || (frameGap == 1 && sampleSeconds > 0d && sampleSeconds <= (double)DodgeMaximumSampleSeconds));
                bool stable = _actor.Model.State == CombatState.Locomotion && !_animator.IsInTransition(0) && _dodgeCapsule.isGrounded && _reader.Move == Vector2.zero && Mathf.Abs(_animator.speed - 1f) <= DodgeScalarTolerance && Time.timeScale == 1f;
                if (!shortFrame || !stable) steadyStarted = double.NaN;
                else if (double.IsNaN(steadyStarted)) steadyStarted = Time.timeAsDouble;
                double continuous = double.IsNaN(steadyStarted) ? 0 : Time.timeAsDouble - steadyStarted;
                _dodgeSteadyRows.Add(new DodgeSteadyRow { frame = Time.frameCount, previousFrame = previous == null ? 0 : previous.frame, frameGap = frameGap, sampleSeconds = sampleSeconds, time = Time.timeAsDouble, realtime = Time.realtimeSinceStartupAsDouble, delta = delta, finiteShortFrame = shortFrame, stableIdle = stable, continuousSeconds = continuous, resetReason = !shortFrame ? "nonfinite/zero/over-1/30 real delta or nonadjacent EOF" : !stable ? "not grounded empty-input normal Idle" : null });
                _dodgeRecord.preInputSteadySeconds = continuous;
                if (shortFrame && stable && continuous >= DodgeRequiredSteadySeconds)
                {
                    _dodgeRecord.preInputSteadyCompleted = true; _dodgeRecord.preInputEndedRealtime = Time.realtimeSinceStartupAsDouble;
                    yield break;
                }
                yield return null;
            }
            _dodgeRecord.preInputEndedRealtime = Time.realtimeSinceStartupAsDouble;
            Assert.That(_dodgeRecord.preInputSteadyCompleted, Is.True, "No input is queued unless real normal PlayerLoop provides >=.15s uninterrupted finite delta<=1/30 within the declared8s/5000-frame deadline. Engine clocks are never reset.");
        }

        void PrepareNativeForwardDodge()
        {
            // Any earlier failed run's late pixels are encoded only here, BEFORE auditing,
            // settling or input. A late callback itself never encodes/writes during another
            // fixture's measured PlayerLoop.
            FlushCompletedClosedDodgeGpuCaptures();
            Assert.That(_withKnife, Is.False); Assert.That(_candidateSet != null && _candidateController != null, Is.True);
            Assert.That(EditorUtility.IsPersistent(_candidateSet) || EditorUtility.IsPersistent(_candidateController), Is.False);
            foreach (string path in DodgeRuntimeSources.SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists))
                if (!_disk.ContainsKey(path)) _disk.Add(path, Hash(path));
            _dodgeRecord.sourceFileSha = Hash(DodgeFixturePath);
            _dodgeClip = AssetDatabase.LoadAllAssetsAtPath(Native).OfType<AnimationClip>().Single(c => c.name == "Dodge_Forward");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_dodgeClip, out string guid, out long localId), Is.True);
            Assert.That(guid, Is.EqualTo("33196f9d403b6e44ca18f3182d28beb6")); Assert.That(localId, Is.EqualTo(-3851722262833059593L));
            _dodgeRecord.candidate = Native + "#Dodge_Forward"; _dodgeRecord.clipGuid = guid; _dodgeRecord.clipLocalId = localId;
            _dodgeOriginalKey = _candidateSet.GetClip(CombatState.Dodge); Assert.That(_dodgeOriginalKey, Is.Not.Null); Assert.That(_dodgeOriginalKey, Is.Not.SameAs(_dodgeClip));
            var before = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(before);
            Assert.That(before.Count(p => p.Key == _dodgeOriginalKey), Is.EqualTo(1), "Add exactly the original Dodge identity, not a similarly named key.");
            var after = before.Select(p => p.Key == _dodgeOriginalKey ? new KeyValuePair<AnimationClip, AnimationClip>(p.Key, _dodgeClip) : p).ToList();
            _candidateController.ApplyOverrides(after);
            var actual = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(actual);
            Assert.That(actual.Count, Is.EqualTo(before.Count)); Assert.That(actual.Count(p => p.Value != before.Single(b => b.Key == p.Key).Value), Is.EqualTo(1), "All ten previously configured keys and every other override must stay exact.");
            var so = new SerializedObject(_candidateSet); so.FindProperty("_dodge").objectReferenceValue = _dodgeClip; so.ApplyModifiedPropertiesWithoutUndo();
            _dodgeFrozenOverrides = actual.ToArray(); _dodgeFrozenSet = EditorJsonUtility.ToJson(_candidateSet);
            _allowed.Clear(); _allowed.UnionWith(_locomotion); _allowed.Add(_dodgeClip); _stateGuard.Clear(); _stateGuard.UnionWith(new[] { "Locomotion", "Dodge" });
            _dodgeMotor = _actor.GetComponent<ThirdPersonMotor>(); _dodgeCapsule = _actor.GetComponent<CharacterController>(); _dodgePresenter = _actor.GetComponent<PlayerAnimationPresenter>();
            Assert.That(_dodgeMotor != null && _dodgeCapsule != null && _dodgePresenter != null, Is.True);
            BindOriginalDodgeVisualHierarchy();
            _dodgeDirectionField = DodgeMotorField("_dodgeDirection"); _dodgeTravelField = DodgeMotorField("_previousDodgeTravel"); _dodgeVelocityField = DodgeMotorField("_horizontalVelocity");
            _dodgeCameraField = DodgeMotorField("_cameraTransform"); _dodgeRotationField = DodgeMotorField("_rotationSpeed");
            _dodgeCameraFrame = (Transform)_dodgeCameraField.GetValue(_dodgeMotor); Assert.That(_dodgeCameraFrame, Is.Not.Null);
            _dodgeLeftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot); _dodgeRightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Assert.That(_dodgeLeftFoot != null && _dodgeRightFoot != null, Is.True, "No invented zero feet when the real Avatar is unmapped.");
            _dodgeRecord.duration = _expectedTuning.DodgeDuration; _dodgeRecord.invulnerabilitySeconds = _expectedTuning.DodgeInvulnerabilitySeconds; _dodgeRecord.distance = _expectedTuning.DodgeDistance; _dodgeRecord.staminaCost = _expectedTuning.DodgeStaminaCost;
            _dodgeRecord.travelEndNormalized = DodgeTravelProfile.TravelEndNormalized; _dodgeRecord.nativeLength = _dodgeClip.length; _dodgeRecord.expectedPlaybackSpeed = Mathf.Clamp(_dodgeClip.length / _expectedTuning.DodgeDuration, .35f, 3f);
            Assert.That(_dodgeRecord.nativeLength, Is.EqualTo(.4f).Within(.000001f));
            FieldInfo originalFade = typeof(PlayerAnimationPresenter).GetField("CrossFadeSeconds", BindingFlags.Static | BindingFlags.NonPublic); Assert.That(originalFade, Is.Not.Null); Assert.That((float)originalFade.GetRawConstantValue(), Is.EqualTo(.08f));
            Assert.That(_dodgeRecord.duration, Is.EqualTo(.52f)); Assert.That(_dodgeRecord.invulnerabilitySeconds, Is.EqualTo(.43f)); Assert.That(_dodgeRecord.distance, Is.EqualTo(4.5f)); Assert.That(_dodgeRecord.staminaCost, Is.EqualTo(24f));
            var runtimeActions = (InputActionAsset)typeof(PlayerInputReader).GetField("_runtimeActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_reader);
            _dodgeAction = runtimeActions.FindAction("Player/Dodge", true); _dodgeInputCallback = context => { RecordInput("performed", context.action.name); DodgeCaptureInput("performed", context.action.name, context.control?.path); _dodgeRecord.inputPerformedCount++; };
            _dodgeAction.performed += _dodgeInputCallback;
            _dodgeBefore = _actor.gameObject.AddComponent<KnightHitStopBeforePresenterObserver>();
            _dodgeBefore.Sample = () =>
            {
                try
                {
                    var row = new DodgeLoopRecord { frame = Time.frameCount, time = Time.timeAsDouble, delta = Time.deltaTime, elapsed = _actor.Model.StateElapsed, normalized = _actor.Model.StateNormalized, duration = _actor.Model.StateDuration, domain = _actor.Model.State.ToString(), presentedBeforeAnimator = _dodgePresenter.PresentedState.ToString(), actorDodgeInput = _actor.DodgeInput, currentMove = _reader.Move, root = _actor.transform.position, rotation = _actor.transform.rotation, committedDirection = (Vector3)_dodgeDirectionField.GetValue(_dodgeMotor), motorTravel = (float)_dodgeTravelField.GetValue(_dodgeMotor), horizontalVelocity = (Vector3)_dodgeVelocityField.GetValue(_dodgeMotor), authoredRotationSpeed = (float)_dodgeRotationField.GetValue(_dodgeMotor), captured = true };
                    Assert.That(DodgeFinite(row.delta) && DodgeFinite(row.elapsed) && DodgeFinite(row.normalized) && DodgeFinite(row.motorTravel), Is.True); _dodgeLoops[row.frame] = row;
                }
                catch (Exception error) { _dodgeObserverError = error; }
            };
            var camera = Own("Review_Knight_Dodge_DiagnosticCamera").AddComponent<Camera>();
            camera.transform.position = _actor.transform.position + new Vector3(9f, 7f, -11f); camera.transform.LookAt(_actor.transform.position + Vector3.up * .8f);
            camera.depth = 10f; camera.fieldOfView = 55f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .2f);
            var light = Own("Review_Knight_Dodge_NeutralLight").AddComponent<Light>(); light.type = LightType.Directional; light.color = Color.white; light.intensity = 1.05f; light.shadows = LightShadows.None; light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            PrepareDodgeGpuCaptures();
            _trace.ability = "Dodge"; _trace.candidate = _dodgeRecord.candidate; _trace.scope = _dodgeRecord.scope; _trace.abilityScope = _dodgeRecord.motionScope; _trace.fullAbilityAccepted = false;
        }

        DodgeRow CaptureDodgeRow(Row pose, DodgeLoopRecord loop)
        {
            var row = new DodgeRow { pose = pose, beforePresenter = loop, eofRealtime = Time.realtimeSinceStartupAsDouble, leftFootWorld = _dodgeLeftFoot.position, rightFootWorld = _dodgeRightFoot.position, leftFootActor = _actor.transform.InverseTransformPoint(_dodgeLeftFoot.position), rightFootActor = _actor.transform.InverseTransformPoint(_dodgeRightFoot.position), stamina = _actor.Model.Stamina.Current, capsuleMin = _dodgeCapsule.bounds.min.y, grounded = _dodgeCapsule.isGrounded, invulnerable = _actor.Model.IsInvulnerable, stateNormalized = _actor.Model.StateNormalized, motorTravel = (float)_dodgeTravelField.GetValue(_dodgeMotor), rootYaw = _actor.transform.eulerAngles.y };
            row.outerLocalPosition = _dodgeOuterVisual.localPosition; row.outerLocalRotation = _dodgeOuterVisual.localRotation; row.outerLocalScale = _dodgeOuterVisual.localScale;
            row.animatorLocalPosition = _animator.transform.localPosition; row.animatorLocalRotation = _animator.transform.localRotation; row.animatorLocalScale = _animator.transform.localScale; row.animatorLossyScale = _animator.transform.lossyScale;
            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0), next = _animator.GetNextAnimatorStateInfo(0);
            row.currentStateSpeed = current.speed; row.currentStateMultiplier = current.speedMultiplier; row.nextStateSpeed = next.speed; row.nextStateMultiplier = next.speedMultiplier;
            if (pose.transition) { AnimatorTransitionInfo transition = _animator.GetAnimatorTransitionInfo(0); row.transitionDuration = transition.duration; row.transitionDurationUnit = transition.durationUnit.ToString(); row.transitionNormalized = transition.normalizedTime; }
            if (_dodgeRows.Count > 0 && loop != null)
            {
                DodgeRow previous = _dodgeRows[_dodgeRows.Count - 1]; row.actualFrameDisplacement = Vector3.ProjectOnPlane(pose.root - previous.pose.root, Vector3.up);
                row.previousFrame = previous.pose.frame; row.frameGap = pose.frame - previous.pose.frame; row.previousDomain = previous.pose.domain;
                row.sampleSeconds = pose.time - previous.pose.time; row.realtimeSampleSeconds = row.eofRealtime - previous.eofRealtime;
                _dodgeRecord.maximumRealtimeSampleSeconds = Math.Max(_dodgeRecord.maximumRealtimeSampleSeconds, row.realtimeSampleSeconds);
                if (row.sampleSeconds > _dodgeRecord.maximumSampleSeconds)
                {
                    _dodgeRecord.maximumSampleSeconds = row.sampleSeconds; _dodgeRecord.largestGapPreviousFrame = previous.pose.frame; _dodgeRecord.largestGapFrame = pose.frame; _dodgeRecord.largestGapFrameCount = row.frameGap;
                    _dodgeRecord.largestGapPreviousDomain = previous.pose.domain; _dodgeRecord.largestGapDomain = pose.domain; _dodgeRecord.largestGapPreviousImage = previous.pose.image;
                    _dodgeRecord.largestGapPreviousLightSafetyMilliseconds = previous.lightSafetyMilliseconds; _dodgeRecord.largestGapPreviousPoseObservationMilliseconds = previous.poseObservationMilliseconds; _dodgeRecord.largestGapPreviousImageSubmissionMilliseconds = previous.imageSubmissionMilliseconds;
                }
                bool dodging = pose.domain == CombatState.Dodge.ToString();
                float travel = dodging ? DodgeTravelProfile.Evaluate(loop.normalized) : previous.motorTravel;
                row.expectedFrameDisplacement = dodging ? loop.committedDirection * Mathf.Max(0f, travel - previous.motorTravel) * _expectedTuning.DodgeDistance : Vector3.zero;
                row.travelFormulaError = dodging ? Mathf.Abs(loop.motorTravel - travel) : 0f; row.framePositionError = (row.actualFrameDisplacement - row.expectedFrameDisplacement).magnitude;
                row.turnDegrees = Quaternion.Angle(previous.beforePresenter != null ? previous.beforePresenter.rotation : _dodgeRecord.rotationBefore, loop.rotation);
                row.maximumTurnDegrees = dodging ? loop.authoredRotationSpeed * loop.delta : 0f;
                Quaternion from = previous.beforePresenter != null ? previous.beforePresenter.rotation : _dodgeRecord.rotationBefore;
                Quaternion expectedRotation = dodging ? Quaternion.RotateTowards(from, Quaternion.LookRotation(loop.committedDirection, Vector3.up), row.maximumTurnDegrees) : from;
                row.expectedRotationError = Quaternion.Angle(expectedRotation, loop.rotation); row.facingVsCommitted = dodging ? Vector3.Angle(_actor.transform.forward, loop.committedDirection) : 0f;
                double actualSeconds = pose.time - previous.pose.time;
                if (actualSeconds > 0)
                {
                    row.worldFootVelocityMeasured = true; row.leftFootWorldVelocity = (row.leftFootWorld - previous.leftFootWorld) / (float)actualSeconds; row.rightFootWorldVelocity = (row.rightFootWorld - previous.rightFootWorld) / (float)actualSeconds;
                }
                if (dodging && row.actualFrameDisplacement.sqrMagnitude > DodgeFramePositionTolerance * DodgeFramePositionTolerance)
                { row.velocityFacingMeasured = true; row.facingVsVelocity = Vector3.Angle(_actor.transform.forward, row.actualFrameDisplacement); }
            }
            _dodgeRows.Add(row); return row;
        }

        void DodgeVerifySafety(bool protectSources)
        {
            Assert.That(_scene.IsValid() && _scene.isLoaded && _actor.gameObject.scene == _scene, Is.True);
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.EquivalentTo(new[] { _actor }));
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            foreach (var flow in Object.FindObjectsOfType<M2RouteFlowController>()) Assert.That(flow.SavePath, Does.Contain("IsolatedSaves"));
            Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
            Assert.That(_dodgeOuterVisual != null && _dodgeOuterVisual.parent == _actor.transform, Is.True);
            Assert.That(_dodgeOuterVisual.Find(_dodgeAnimatorPath), Is.SameAs(_animator.transform), "Preserve the exact owned Prefab's native Animator hierarchy path.");
            Assert.That(_dodgeOuterVisual.localPosition, Is.EqualTo(_dodgeOuterPosition)); Assert.That(_dodgeOuterVisual.localRotation, Is.EqualTo(_dodgeOuterRotation)); Assert.That(_dodgeOuterVisual.localScale, Is.EqualTo(_dodgeOuterScale));
            Assert.That(_dodgeOuterVisual.localScale, Is.EqualTo(Vector3.one * .75f), "Original .75 scale belongs to the instantiated OUTER visual, not the inner Animator.");
            Assert.That(_animator.transform.localPosition, Is.EqualTo(_dodgeInnerPosition)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_dodgeInnerRotation)); Assert.That(_animator.transform.localScale, Is.EqualTo(_dodgeInnerScale));
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(_candidateController)); Assert.That(AssetDatabase.GetAssetPath(_animator.avatar), Is.EqualTo(Native)); Assert.That(_animator.avatar.isValid && _animator.avatar.isHuman, Is.True);
            Assert.That(_candidateSet.GetClip(CombatState.Dodge), Is.SameAs(_dodgeClip)); Assert.That(_actor.Model.State == CombatState.Locomotion || _actor.Model.State == CombatState.Dodge, Is.True, "Unexpected combat state aborts this isolated entry.");
            Assert.That(DodgeFinite(_actor.transform.position.x) && DodgeFinite(_actor.transform.position.y) && DodgeFinite(_actor.transform.position.z), Is.True);
            Assert.That((Transform)_dodgeCameraField.GetValue(_dodgeMotor), Is.SameAs(_dodgeCameraFrame));
            if (_dodgeRecord.entered)
            { Assert.That(_dodgeCameraFrame.position, Is.EqualTo(_dodgeRecord.cameraPosition)); Assert.That(_dodgeCameraFrame.rotation, Is.EqualTo(_dodgeRecord.cameraRotation)); }
            if (!protectSources) return;
            Assert.That(EditorJsonUtility.ToJson(_candidateSet), Is.EqualTo(_dodgeFrozenSet), "The nonpersistent candidate SO contract is frozen.");
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(pairs);
            Assert.That(pairs.Count, Is.EqualTo(_dodgeFrozenOverrides.Length));
            foreach (var pair in _dodgeFrozenOverrides) Assert.That(pairs.Single(p => p.Key == pair.Key).Value, Is.SameAs(pair.Value));
            Assert.That(_disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True, "Source bytes changed; do not continue or restore unknown changes.");
            Assert.That(_memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value), Is.True, "Full persistent source memory changed.");
        }

        void BindOriginalDodgeVisualHierarchy()
        {
            GameObject sourcePrefab = Required<GameObject>(Prefab);
            Animator sourceAnimator = sourcePrefab.GetComponentsInChildren<Animator>(true).Single();
            Assert.That(AssetDatabase.GetAssetPath(sourceAnimator.avatar), Is.EqualTo(Native));
            _dodgeAnimatorPath = AnimationUtility.CalculateTransformPath(sourceAnimator.transform, sourcePrefab.transform);
            Assert.That(_dodgeAnimatorPath, Is.Not.Empty, "The owned visual has a distinct native inner Animator, not an Animator on the .75 outer wrapper.");
            _dodgeOuterVisual = _animator.transform;
            while (_dodgeOuterVisual.parent != _actor.transform)
            {
                Assert.That(_dodgeOuterVisual.parent, Is.Not.Null, "Animator must belong to this exact Actor's instantiated visual.");
                _dodgeOuterVisual = _dodgeOuterVisual.parent;
            }
            Assert.That(_dodgeOuterVisual, Is.Not.SameAs(_animator.transform));
            Assert.That(_dodgeOuterVisual.name, Is.EqualTo(sourcePrefab.name + "(Clone)"));
            Assert.That(_dodgeOuterVisual.GetComponentsInChildren<Animator>(true), Is.EquivalentTo(new[] { _animator }));
            Assert.That(AnimationUtility.CalculateTransformPath(_animator.transform, _dodgeOuterVisual), Is.EqualTo(_dodgeAnimatorPath));
            Assert.That(_dodgeOuterVisual.Find(_dodgeAnimatorPath), Is.SameAs(_animator.transform));
            _dodgeOuterPosition = _dodgeCapsule.center - Vector3.up * (_dodgeCapsule.height * .5f); _dodgeOuterRotation = Quaternion.identity; _dodgeOuterScale = sourcePrefab.transform.localScale;
            Assert.That(_dodgeOuterScale, Is.EqualTo(Vector3.one * .75f));
            _dodgeInnerPosition = sourceAnimator.transform.localPosition; _dodgeInnerRotation = sourceAnimator.transform.localRotation; _dodgeInnerScale = sourceAnimator.transform.localScale;
            _dodgeRecord.sourcePrefabGuid = AssetDatabase.AssetPathToGUID(Prefab); _dodgeRecord.sourcePrefabName = sourcePrefab.name; _dodgeRecord.sourceAnimatorPath = _dodgeAnimatorPath;
            _dodgeRecord.outerPathFromActor = AnimationUtility.CalculateTransformPath(_dodgeOuterVisual, _actor.transform); _dodgeRecord.animatorPathFromActor = AnimationUtility.CalculateTransformPath(_animator.transform, _actor.transform);
            _dodgeRecord.originalOuterLocalPosition = _dodgeOuterPosition; _dodgeRecord.originalOuterLocalRotation = _dodgeOuterRotation; _dodgeRecord.originalOuterLocalScale = _dodgeOuterScale;
            _dodgeRecord.originalAnimatorLocalPosition = _dodgeInnerPosition; _dodgeRecord.originalAnimatorLocalRotation = _dodgeInnerRotation; _dodgeRecord.originalAnimatorLocalScale = _dodgeInnerScale; _dodgeRecord.initialAnimatorLossyScale = _animator.transform.lossyScale;
        }

        void EvaluateDodgeGates()
        {
            DodgeRow[] measured = _dodgeRows.Where(r => r.beforePresenter != null).ToArray();
            DodgeRow[] dodging = measured.Where(r => r.pose.domain == CombatState.Dodge.ToString()).ToArray();
            _dodgeRecord.dodgeFrames = dodging.Length; _dodgeRecord.peakHips = _dodgeRows.Max(r => r.pose.hipDrift); _trace.peak = _dodgeRecord.peakHips;
            _dodgeRecord.bodyMin = _dodgeRows.Min(r => r.pose.bodyMin); _dodgeRecord.capeMin = _dodgeRows.Min(r => r.pose.capeMin); _dodgeRecord.gearMin = _dodgeRows.Min(r => r.pose.gearMin); _dodgeRecord.capsuleMin = _dodgeRows.Min(r => r.capsuleMin);
            _dodgeRecord.maximumFramePositionError = measured.Max(r => r.framePositionError); _dodgeRecord.maximumTravelFormulaError = measured.Max(r => r.travelFormulaError); _dodgeRecord.maximumTurnArithmeticError = measured.Max(r => r.expectedRotationError);
            _dodgeRecord.finalTravelDistanceError = DodgePlanarDistance(_dodgeRecord.rootBefore + _dodgeRecord.requestedWorldDirection * _expectedTuning.DodgeDistance, _dodgeRecord.rootAfter);
            DodgeRow[] velocity = dodging.Where(r => r.velocityFacingMeasured).ToArray(); _dodgeRecord.velocityFacingSamples = velocity.Length;
            if (velocity.Length > 0)
            {
                _dodgeRecord.maximumFacingVsVelocity = velocity.Max(r => r.facingVsVelocity); _dodgeRecord.meanFacingVsVelocity = velocity.Average(r => r.facingVsVelocity);
                _dodgeRecord.timeWeightedMeanFacingVsVelocity = velocity.Sum(r => r.facingVsVelocity * r.pose.delta) / velocity.Sum(r => r.pose.delta);
            }
            _dodgeRecord.worldFootVelocitySamples = measured.Count(r => r.worldFootVelocityMeasured);
            _dodgeRecord.maximumLeftFootWorldSpeed = measured.Where(r => r.worldFootVelocityMeasured).Select(r => r.leftFootWorldVelocity.magnitude).DefaultIfEmpty(0f).Max();
            _dodgeRecord.maximumRightFootWorldSpeed = measured.Where(r => r.worldFootVelocityMeasured).Select(r => r.rightFootWorldVelocity.magnitude).DefaultIfEmpty(0f).Max();
            _dodgeRecord.coverageGatePassed = DodgeGate("complete-first-frame-action-transition-tail-sample-coverage", () =>
            {
                Assert.That(_dodgeRecord.preInputSteadyCompleted, Is.True); Assert.That(_dodgeRecord.preInputSteadySeconds, Is.GreaterThanOrEqualTo(DodgeRequiredSteadySeconds));
                Assert.That(dodging, Is.Not.Empty); Assert.That(_dodgeRecord.firstMotorFrame, Is.EqualTo(_dodgeRecord.enteredFrame)); Assert.That(_dodgeRecord.firstSampleFrame, Is.EqualTo(_dodgeRecord.enteredFrame));
                Assert.That(_dodgeRecord.firstMotorDelta, Is.GreaterThan(0f).And.LessThanOrEqualTo(DodgeMaximumSampleSeconds));
                Assert.That(_dodgeRecord.firstSampleSeconds, Is.GreaterThan(0d).And.LessThanOrEqualTo((double)DodgeMaximumSampleSeconds));
                foreach (DodgeRow row in measured)
                {
                    Assert.That(row.frameGap, Is.EqualTo(1), "Each EOF from the queued-input baseline through full Idle+.15 must have the adjacent real frame, not a missing-pose interpolation.");
                    Assert.That(DodgeFinite(row.pose.delta) && row.pose.delta > 0f && row.pose.delta <= DodgeMaximumSampleSeconds, Is.True, "Real PlayerLoop delta exceeds the predeclared1/30 coverage bound at frame" + row.pose.frame);
                    Assert.That(!double.IsNaN(row.sampleSeconds) && !double.IsInfinity(row.sampleSeconds) && row.sampleSeconds > 0d && row.sampleSeconds <= (double)DodgeMaximumSampleSeconds, Is.True, "Actual adjacent pose time exceeds the predeclared1/30 coverage bound at frame" + row.pose.frame);
                }
                Assert.That(_dodgeRecord.maximumSampleSeconds, Is.LessThanOrEqualTo((double)DodgeMaximumSampleSeconds));
            });
            _dodgeRecord.lifecycleGatePassed = DodgeGate("lifecycle", () =>
            {
                Assert.That(_dodgeRecord.entered && _dodgeRecord.domainReturned && _dodgeRecord.animatorSettled && _dodgeRecord.stableIdleCompleted, Is.True, "5000-frame deadline covers entry, domain return, entire actual Idle fade and .15s settled tail.");
                Assert.That(_dodgeRecord.stableIdleSeconds, Is.GreaterThanOrEqualTo(.15f)); Assert.That(_dodgeRecord.worldFootVelocitySamples, Is.GreaterThanOrEqualTo(2)); Assert.That(_seenStates, Is.EquivalentTo(new[] { "Locomotion", "Dodge" })); Assert.That(_seen.Contains(_dodgeClip), Is.True);
                Assert.That(measured.Where(r => r.pose.frame >= _dodgeRecord.settledFrame).All(r => r.pose.stableLocomotion), Is.True);
                Assert.That(_dodgeRecord.dodgeAttemptAfter - _dodgeRecord.dodgeAttemptBefore, Is.EqualTo(1)); Assert.That(_dodgeRecord.inputPerformedCount, Is.EqualTo(1));
            });
            _dodgeRecord.directionGatePassed = DodgeGate("committed-direction", () =>
            {
                Assert.That(dodging, Is.Not.Empty); Assert.That(_dodgeRecord.capturedActorDodgeInput, Is.EqualTo(_dodgeRecord.requestedMove));
                foreach (var row in dodging) { Assert.That((row.beforePresenter.committedDirection - _dodgeRecord.committedDirection).magnitude, Is.LessThanOrEqualTo(DodgeScalarTolerance)); Assert.That((row.beforePresenter.committedDirection - _dodgeRecord.requestedWorldDirection).magnitude, Is.LessThanOrEqualTo(DodgeScalarTolerance)); Assert.That(row.beforePresenter.actorDodgeInput, Is.EqualTo(_dodgeRecord.capturedActorDodgeInput)); }
                Assert.That(_dodgeInputs.Single(r => r.kind == "performed").dodge, Is.True); Assert.That(_dodgeInputs.Single(r => r.kind == "performed").move, Is.EqualTo(_dodgeRecord.requestedMove));
                Assert.That(_reader.Move, Is.EqualTo(Vector2.zero)); Assert.That(_reader.CaptureSnapshot().Dodge, Is.False);
            });
            _dodgeRecord.movementGatePassed = DodgeGate("original-travel-and-no-idle-slide", () =>
            {
                Assert.That(_dodgeRecord.maximumTravelFormulaError, Is.LessThanOrEqualTo(DodgeScalarTolerance)); Assert.That(_dodgeRecord.maximumFramePositionError, Is.LessThanOrEqualTo(DodgeFramePositionTolerance));
                Assert.That(dodging.Any(r => r.motorTravel == 1f), Is.True, "Observe the original82%-complete travel before domain return; no fitted endpoint.");
                Assert.That(_dodgeRecord.finalTravelDistanceError, Is.LessThan(.01f)); Assert.That(_dodgeRecord.tailDistance, Is.LessThanOrEqualTo(.01f));
                foreach (var row in measured.Where(r => r.pose.frame >= _dodgeRecord.returnedFrame)) Assert.That(row.beforePresenter.horizontalVelocity.magnitude, Is.LessThanOrEqualTo(DodgeScalarTolerance));
            });
            _dodgeRecord.rotationGatePassed = DodgeGate("original-smooth-turn", () =>
            {
                Assert.That(_dodgeRecord.maximumTurnArithmeticError, Is.LessThanOrEqualTo(DodgeRotationArithmeticTolerance));
                foreach (var row in measured) Assert.That(row.turnDegrees, Is.LessThanOrEqualTo(row.maximumTurnDegrees + DodgeRotationArithmeticTolerance));
            });
            _dodgeRecord.hipsGatePassed = DodgeGate("original-hips-envelope", () => { Assert.That(_dodgeRecord.peakHips, Is.LessThan(.4f)); Assert.That(_dodgeRecord.finalHips, Is.LessThan(.1f)); });
            _dodgeRecord.signedFloorPassed = DodgeGate("independent-signed-body-cape-gear-floor", () => { Assert.That(_dodgeRecord.bodyMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_dodgeRecord.capeMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_dodgeRecord.gearMin, Is.GreaterThanOrEqualTo(0f)); });
            _dodgeRecord.capsuleGatePassed = DodgeGate("original-capsule-ground", () => { Assert.That(_dodgeRecord.capsuleMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_dodgeRows.All(r => r.grounded), Is.True); });
            _dodgeRecord.resourceGatePassed = DodgeGate("original-resources-iframe-and-presenter-speed", () =>
            {
                Assert.That(_dodgeRecord.initialStaminaAfterSpend, Is.EqualTo(_dodgeRecord.staminaBefore - _expectedTuning.DodgeStaminaCost).Within(.0001f));
                foreach (var row in _dodgeRows)
                {
                    Assert.That(row.pose.health, Is.EqualTo(_dodgeRecord.healthBefore)); Assert.That(DodgeFinite(row.stamina) && row.stamina >= 0f && row.stamina <= _expectedTuning.MaxStamina, Is.True);
                    Assert.That(row.invulnerable, Is.EqualTo(row.pose.domain == CombatState.Dodge.ToString() && row.pose.elapsed <= _expectedTuning.DodgeInvulnerabilitySeconds));
                }
                Assert.That(dodging, Is.Not.Empty);
                Assert.That(dodging[0].pose.elapsed, Is.EqualTo(dodging[0].pose.delta).Within(DodgeScalarTolerance), "Fresh Dodge elapsed is its first real Tick, not the prior Locomotion clock.");
                for (int index = 1; index < dodging.Length; index++) Assert.That(dodging[index].pose.elapsed, Is.EqualTo(dodging[index - 1].pose.elapsed + dodging[index].pose.delta).Within(DodgeScalarTolerance));
                Assert.That(dodging.Any(r => r.pose.elapsed <= .43f) && dodging.Any(r => r.pose.elapsed > .43f), Is.True, "The actual sampled domain trace must cover both sides of the original iframe boundary.");
                Assert.That(measured.Single(r => r.pose.frame == _dodgeRecord.returnedFrame).pose.elapsed, Is.EqualTo(0f), "Normal EnterState resets the return-state clock.");
                foreach (var row in measured.Where(r => r.pose.transition)) { Assert.That(row.transitionDurationUnit, Is.EqualTo("Fixed")); Assert.That(row.transitionDuration, Is.EqualTo(.08f).Within(DodgeScalarTolerance)); }
                foreach (var row in measured)
                {
                    if (row.pose.currentState == "Dodge") { Assert.That(row.currentStateSpeed, Is.EqualTo(1f).Within(DodgeScalarTolerance)); Assert.That(row.currentStateMultiplier, Is.EqualTo(1f).Within(DodgeScalarTolerance)); }
                    if (row.pose.nextState == "Dodge") { Assert.That(row.nextStateSpeed, Is.EqualTo(1f).Within(DodgeScalarTolerance)); Assert.That(row.nextStateMultiplier, Is.EqualTo(1f).Within(DodgeScalarTolerance)); }
                }
                foreach (var row in dodging)
                {
                    Assert.That(row.stamina, Is.EqualTo(_dodgeRecord.initialStaminaAfterSpend).Within(.0001f));
                    Assert.That(row.beforePresenter.duration, Is.EqualTo(_expectedTuning.DodgeDuration)); Assert.That(row.pose.speed, Is.EqualTo(_dodgeRecord.expectedPlaybackSpeed).Within(DodgeScalarTolerance));
                }
            });
        }

        bool DodgeGate(string name, Action check)
        {
            try { check(); return true; }
            catch (AssertionException error) { _dodgeFailures.Add(new GateFailure { gate = name, message = error.Message, stackTrace = error.StackTrace }); return false; }
        }
        void DodgeCaptureInput(string kind, string action, string control)
        {
            var snapshot = _reader.CaptureSnapshot();
            _dodgeInputs.Add(new DodgeInputRecord { kind = kind, action = action, control = control, frame = Time.frameCount, time = Time.timeAsDouble, keyboardId = _keyboard.deviceId, mouseId = _mouse.deviceId, domain = _actor.Model.State.ToString(), move = snapshot.Move, dodge = snapshot.Dodge, w = _keyboard.wKey.isPressed, a = _keyboard.aKey.isPressed, s = _keyboard.sKey.isPressed, d = _keyboard.dKey.isPressed, space = _keyboard.spaceKey.isPressed });
        }
        void CaptureDodgeImage(string file, Row pose)
        {
            Assert.That(_dodgeGpuBatch != null && !_dodgeGpuBatch.closed, Is.True);
            DodgeGpuBatch batch = _dodgeGpuBatch;
            DodgeGpuCapture capture = batch.captures.Single(c => c.file == file);
            Assert.That(capture.captureSubmitted || capture.requestCallBegan, Is.False, "Exactly one actual EOF request per independent preallocated RT.");
            Assert.That(capture.target != null && capture.target.IsCreated(), Is.True);
            Assert.That(Screen.width == capture.width && Screen.height == capture.height, Is.True, "A resized GameView does not silently change this capture's predeclared dimensions.");
            capture.sourceFrame = pose.frame; capture.sourceTime = pose.time; capture.sourceRealtime = Time.realtimeSinceStartupAsDouble;
            double started = Time.realtimeSinceStartupAsDouble;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(capture.target); capture.captureSubmitted = true;
            // The callback owns this capture/RT and immutable output path, NOT the fixture's
            // mutable fields. Never add this in-flight RT to the core teardown asset list.
            capture.requestCallBegan = true;
            try
            {
                AsyncGPUReadback.Request(capture.target, 0, TextureFormat.RGBA32, request => CompleteDodgeGpuReadback(batch, capture, request));
                capture.requestReturned = true;
            }
            catch (Exception error)
            {
                capture.nativeError = error.ToString();
                // A throwing native request does not prove nothing was queued. Keep its
                // lease until a callback; never destroy a possibly-in-flight resource.
                throw;
            }
            capture.submissionMilliseconds = (Time.realtimeSinceStartupAsDouble - started) * 1000;
            _dodgeRows[_dodgeRows.Count - 1].imageSubmissionMilliseconds = capture.submissionMilliseconds;
            _images.Add(file); pose.image = file;
        }

        void PrepareDodgeGpuCaptures()
        {
            Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True, "Native async GPU readback is required; no synchronous or fabricated-image fallback.");
            Assert.That(Screen.width, Is.GreaterThan(0)); Assert.That(Screen.height, Is.GreaterThan(0));
            _dodgeGpuBatch = new DodgeGpuBatch(_output);
            foreach (string file in new[] { "01-dodge-mid.png", "02-dodge-settled-idle.png" })
            {
                var capture = new DodgeGpuCapture { file = file, output = _output, width = Screen.width, height = Screen.height };
                _dodgeGpuBatch.captures.Add(capture);
                capture.target = new RenderTexture(capture.width, capture.height, 0, RenderTextureFormat.ARGB32) { name = "DodgeActualEof_" + file, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                Assert.That(capture.target.Create(), Is.True, "Independent preallocated GPU capture target creation failed.");
            }
        }

        static void CompleteDodgeGpuReadback(DodgeGpuBatch batch, DodgeGpuCapture capture, AsyncGPUReadbackRequest request)
        {
            try
            {
                if (request.hasError) capture.nativeError = (capture.nativeError ?? "") + "Native AsyncGPUReadbackRequest.hasError; the actual image is not accepted.";
                else
                {
                    capture.rgba = request.GetData<byte>().ToArray(); capture.byteCount = capture.rgba.Length;
                    if (capture.byteCount != checked(capture.width * capture.height * 4)) capture.nativeError = "Native RGBA32 readback byte count differs from the independently frozen EOF dimensions.";
                }
            }
            catch (Exception error) { capture.nativeError = (capture.nativeError ?? "") + error; }
            finally
            {
                capture.completedFrame = Time.frameCount; capture.completedRealtime = Time.realtimeSinceStartupAsDouble; capture.completed = true; capture.completedAfterClosure = batch.closed;
                try { ReleaseDodgeGpuTarget(capture); }
                catch (Exception error) { capture.nativeError = (capture.nativeError ?? "") + error; }
                // If this run already closed, CloseDodgeGpuBatch retains the capture in
                // its own queue. No PNG/IO here: a late callback may occur while a DIFFERENT
                // fixture is measuring. Pixels and source metadata remain available for
                // the next pre-settle prepare or explicit idle-only flush.
            }
        }

        IEnumerator FinishDodgeGpuReadbacks()
        {
            foreach (DodgeGpuCapture capture in _dodgeGpuBatch.captures.Where(c => c.requestCallBegan))
            {
                while (!capture.completed && Time.realtimeSinceStartupAsDouble - capture.sourceRealtime <= DodgeGpuDeadlineSeconds)
                {
                    ThrowDodgeGpuError(_dodgeGpuBatch);
                    yield return null;
                }
                if (!capture.completed || capture.completedRealtime - capture.sourceRealtime > DodgeGpuDeadlineSeconds) capture.timedOut = true;
            }
            ThrowDodgeGpuError(_dodgeGpuBatch);
            _dodgeRecord.gpuReadbackGatePassed = DodgeGate("native-actual-EOF-async-GPU-readbacks", () =>
            {
                Assert.That(_dodgeGpuBatch.captures.Count, Is.EqualTo(2));
                foreach (DodgeGpuCapture capture in _dodgeGpuBatch.captures)
                {
                    Assert.That(capture.captureSubmitted && capture.requestCallBegan && capture.requestReturned, Is.True);
                    Assert.That(capture.completed && !capture.timedOut && capture.released, Is.True, "Actual native readback must finish within the declared8s real deadline; failure keeps its live lease until callback.");
                    Assert.That(capture.completedRealtime - capture.sourceRealtime, Is.GreaterThanOrEqualTo(0d).And.LessThanOrEqualTo(DodgeGpuDeadlineSeconds));
                    Assert.That(capture.nativeError, Is.Null.Or.Empty); Assert.That(capture.rgba, Is.Not.Null); Assert.That(capture.byteCount, Is.EqualTo(checked(capture.width * capture.height * 4)));
                    Assert.That(capture.sourceFrame, Is.EqualTo(_dodgeRows.Single(r => r.pose.image == capture.file).pose.frame));
                    Assert.That(capture.sourceTime, Is.EqualTo(_dodgeRows.Single(r => r.pose.image == capture.file).pose.time));
                }
            });
            foreach (DodgeGpuCapture capture in _dodgeGpuBatch.captures.Where(c => c.completed && string.IsNullOrEmpty(c.nativeError) && c.rgba != null))
            {
                var screenshot = new Texture2D(capture.width, capture.height, TextureFormat.RGBA32, false);
                _ownedAssets.Add(screenshot); screenshot.LoadRawTextureData(capture.rgba); screenshot.Apply(false, false);
                _capturedImages.Add(capture.file, screenshot);
            }
        }

        static void ThrowDodgeGpuError(DodgeGpuBatch batch)
        {
            DodgeGpuCapture failure = batch?.captures.FirstOrDefault(c => !string.IsNullOrEmpty(c.nativeError));
            if (failure != null) throw new InvalidOperationException("Actual EOF native GPU capture aborted: " + failure.file + ": " + failure.nativeError);
        }

        static void ReleaseDodgeGpuTarget(DodgeGpuCapture capture)
        {
            if (capture.target != null)
            {
                capture.target.Release();
                if (UnityEngine.Application.isPlaying) Object.Destroy(capture.target); else Object.DestroyImmediate(capture.target);
                capture.target = null;
            }
            capture.released = true;
        }

        static void CloseDodgeGpuBatch(DodgeGpuBatch batch)
        {
            if (batch == null) return;
            batch.closed = true;
            foreach (DodgeGpuCapture capture in batch.captures)
            {
                // Submitted requests (including an ambiguous throwing native request) stay
                // alive until their own callback. No WaitForCompletion/forced destruction.
                if (!capture.requestCallBegan || capture.completed)
                {
                    try { ReleaseDodgeGpuTarget(capture); }
                    catch (Exception error) { capture.nativeError = (capture.nativeError ?? "") + error; }
                }
                if (capture.completed) EncodeClosedDodgeCapture(capture);
                else if (capture.requestCallBegan && !ClosedDodgeGpuCaptures.Contains(capture)) ClosedDodgeGpuCaptures.Add(capture);
                WriteDodgeGpuSidecar(capture);
            }
        }

        public static int FlushCompletedDodgeGpuCapturesWhenEditorIdle()
        {
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling, Is.False, "Explicit late-data encoding is idle-only; never inject it into any fixture's measured frames.");
            return FlushCompletedClosedDodgeGpuCaptures();
        }

        static int FlushCompletedClosedDodgeGpuCaptures()
        {
            int flushed = 0;
            foreach (DodgeGpuCapture capture in ClosedDodgeGpuCaptures.Where(c => c.completed).ToArray())
            {
                EncodeClosedDodgeCapture(capture); WriteDodgeGpuSidecar(capture);
                ClosedDodgeGpuCaptures.Remove(capture); flushed++;
            }
            return flushed;
        }

        static void EncodeClosedDodgeCapture(DodgeGpuCapture capture)
        {
            if (capture.encoded || !capture.completed || !string.IsNullOrEmpty(capture.nativeError) || capture.rgba == null) return;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(capture.width, capture.height, TextureFormat.RGBA32, false); texture.LoadRawTextureData(capture.rgba); texture.Apply(false, false);
                string path = capture.output + "/" + capture.file;
                if (!File.Exists(path)) File.WriteAllBytes(path, texture.EncodeToPNG());
                capture.encoded = File.Exists(path) && new FileInfo(path).Length > 100;
            }
            catch (Exception error) { capture.encodeError = error.ToString(); }
            finally { if (texture != null) { if (UnityEngine.Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture); } }
        }

        static void WriteDodgeGpuSidecar(DodgeGpuCapture capture)
        {
            try { File.WriteAllText(capture.output + (capture.completedAfterClosure ? "/gpu-late-" : "/gpu-") + capture.file + ".json", JsonUtility.ToJson(capture, true)); }
            catch (Exception error) { capture.encodeError = (capture.encodeError ?? "") + error; }
        }
        static FieldInfo DodgeMotorField(string name) { FieldInfo field = typeof(ThirdPersonMotor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); Assert.That(field, Is.Not.Null, name); return field; }
        static bool DodgeFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float DodgePlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
#endif
