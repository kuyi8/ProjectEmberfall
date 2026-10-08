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
using Emberfall.Gameplay.Movement;
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
        enum GuardHitCase { TenSecondHold, PerfectGuardCounter, RepeatedHit }
        // Predeclared local-transform arithmetic limits for a non-loop terminal pose.
        // These are not floor/contact, world-foot sliding or feel allowances.
        const float GuardTerminalPoseArithmeticTolerance = .000001f;

        [Serializable] sealed class GuardHitDamageFact
        {
            public int frame, sequence; public double time;
            public string beforeState, afterState, presentedBefore, presentedAfter;
            public float beforeElapsed, afterElapsed, beforeNormalized, afterNormalized, healthBefore, healthAfter;
            public float appliedDamage, appliedPosture, counterWindow;
            public bool clipPhaseMeasured, accepted, defended, perfectGuard, killed;
        }

        [Serializable] sealed class GuardHitFrame
        {
            public int frame; public double time;
            public string domain, presented, current, next;
            public float elapsed, currentNormalized, nextNormalized, delta, health, stamina, posture, counterWindow;
            public bool guardHeld, lightHeld, counter, transition;
        }

        [Serializable] sealed class GuardHitReport
        {
            public string status = "recording", candidate, test, module;
            public string scope = "Isolated original Knight/capsule/Actor, nonpersistent SO/AOC exact-key overrides, actual InputSystem and real PlayerLoop. Controlled Actor.ReceiveDamage is not natural AI/geometry contact, directional HitReact, OS input, audio, NET, Grip, production replacement or final feel acceptance. All original signed floors/anchors/source protections and .4/.1 hips remain.";
            public string holdChoice = "Native Blocking is nonloop at original base1; terminal static hold is explicitly an isolated candidate, not breathing or final presentation.";
            public string counterScope = "Real Q then controlled defendable/front-arc Actor.ReceiveDamage inside the original perfectguard window, then real Light input. Native Blocking lasts longer than the perfectguard window, so this tests its EARLY Guard pose to counter, NOT terminal-clamped hold to counter; domain time/window are not changed.";
            public string hitScope = "Native Hit_A is a nondirectional torso baseline. Two real Actor.ReceiveDamage calls reset domain StateElapsed; the original Presenter does not reenter an unchanged HitReact state. This fact is not visual acceptance of the second response. No F elapsed/normalized phase invariant is applied across the reset.";
            public string imageScope = "Few actual EOF synchronous GameView captures, with real sampling gaps retained. Images require human orientation/readability review; no <33ms full-density, contact or formal-TPS claim.";
            public bool executionCompleted, accepted, visualAccepted, sourceBefore, memoryBefore, sourceAfter, memoryAfter;
            public bool domainReturned, fullIdle, counterObserved, guardHeldTenSeconds, secondHitReset, secondClipContinued;
            public int files, memory, guardAttemptsBefore, guardAttemptsAfter, parriesBefore, parriesAfter, attackSequenceBefore, attackSequenceAfter;
            public float declaredHoldSeconds = 10f, heldElapsed, clipLength, speed, stableIdleSeconds, maximumSampleGap, maximumHipDrift, finalHipDrift;
            public float declaredTerminalPoseArithmeticTolerance = GuardTerminalPoseArithmeticTolerance;
            public float bodyMin, capeMin, gearMin, clampedLocalPositionDifference, clampedQuaternionComponentDifference;
            public float repeatedHitBeforePhase, repeatedHitAfterPhase, repeatedHitNextEofPhase, repeatedHitNextEofElapsed;
            public float domainHealthBefore, domainHealthAfter, firstDamage, secondDamage;
            public GuardHitDamageFact[] damage; public GuardHitFrame[] frames; public string[] images;
        }

        [UnityTest] public IEnumerator ActualQ_BlockingStaticHoldTenSecondsReturnsToIdle()
            => GuardHitBaseline(GuardHitCase.TenSecondHold);

        [UnityTest] public IEnumerator ActualQ_PerfectGuardThenActualLightCounterRecordsBlend()
            => GuardHitBaseline(GuardHitCase.PerfectGuardCounter);

        [UnityTest] public IEnumerator ActualDamage_HitATwoHitsRecordsDomainResetWithoutPresenterRestart()
            => GuardHitBaseline(GuardHitCase.RepeatedHit);

        IEnumerator GuardHitBaseline(GuardHitCase kind)
        {
            var record = new GuardHitReport { test = kind.ToString(), module = GetType().Assembly.ManifestModule.ModuleVersionId.ToString() };
            var facts = new List<GuardHitDamageFact>(); var frames = new List<GuardHitFrame>();
            string recordPath = _output + "/guard-hit-review.json";
            Assert.That(File.Exists(recordPath), Is.False, "Never overwrite a prior native run.");
            AnimationClip clip = null, originalKey = null; InputAction guardAction = null;
            var presenter = _actor.GetComponent<PlayerAnimationPresenter>();
            Transform[] heldTransforms = null; Vector3[] heldPositions = null; Quaternion[] heldRotations = null;
            bool released = false, entered = false, second = false, observedAfterSecond = false, counterQueued = false;
            double stableStarted = double.NaN; Vector2 baseline = default; Vector3 root = default;
            try
            {
                clip = PrepareGuardHitCandidate(kind, out originalKey); record.candidate = Native + "::" + clip.name;
                record.clipLength = clip.length;
                _trace.ability = kind == GuardHitCase.RepeatedHit ? "Damage/HitReact" : "Q/Guard";
                _trace.scope = record.scope; _trace.abilityScope = kind == GuardHitCase.RepeatedHit ? record.hitScope : record.counterScope;
                _trace.candidate = record.candidate; _trace.candidateLength = clip.length; _trace.fullAbilityAccepted = false;
                GuardHitSafety(kind, clip, originalKey); record.sourceBefore = record.memoryBefore = true;
                var actions = (InputActionAsset)typeof(Emberfall.Gameplay.Input.PlayerInputReader).GetField("_runtimeActions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_reader);
                guardAction = actions.FindAction("Player/Guard", true); guardAction.started += OnInputPerformed; guardAction.canceled += OnInputPerformed;
                // Take the slow synchronous baseline image BEFORE the final pre-input settle.
                // No image/readback or whole-source audit is placed immediately before Q/damage.
                yield return null; yield return new WaitForEndOfFrame(); Observe(0f); GuardHitCapture("01-baseline-idle.png");
                _trace.settled = true; yield return new WaitForSeconds(.5f);
                Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); Assert.That(_animator.IsInTransition(0), Is.False);
                Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
                baseline = PlanarHips(); root = _actor.transform.position;
                _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation;
                _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
                record.domainHealthBefore = _actor.Model.Health.Current;
                record.guardAttemptsBefore = _actor.Model.GuardAttemptCount; record.parriesBefore = _actor.Model.PerfectGuardCount;
                record.attackSequenceBefore = _actor.Model.AttackSequence;
                yield return new WaitForEndOfFrame(); Observe(0f); _trace.inputQueuedAt = Time.timeAsDouble;
                if (kind == GuardHitCase.RepeatedHit)
                {
                    facts.Add(GuardHitReceive(presenter, 1, false)); record.firstDamage = facts[0].appliedDamage;
                    Assert.That(facts[0].accepted && !facts[0].killed, Is.True);
                    Assert.That(_actor.Model.State, Is.EqualTo(CombatState.HitReact)); Assert.That(_actor.Model.StateElapsed, Is.Zero);
                }
                else
                {
                    RecordInput("queued", "Guard"); InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Q));
                }
                yield return null;
                for (int frame = 0; frame < 5000; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    float drift = Vector2.Distance(baseline, PlanarHips()); Observe(drift);
                    var row = _rows[_rows.Count - 1]; GuardHitFrame sample = GuardHitSample(presenter, row); frames.Add(sample);
                    record.maximumHipDrift = Mathf.Max(record.maximumHipDrift, drift);
                    record.maximumSampleGap = Mathf.Max(record.maximumSampleGap, row.delta);
                    if (_rows.Count > 1) record.maximumSampleGap = Mathf.Max(record.maximumSampleGap, (float)(row.time - _rows[_rows.Count - 2].time));
                    GuardHitAnchor(root); Assert.That(row.time, Is.GreaterThanOrEqualTo(_rows[0].time));
                    CombatState state = _actor.Model.State;
                    if (kind == GuardHitCase.TenSecondHold && !released)
                    {
                        Assert.That(state, Is.EqualTo(CombatState.Guard), "Actual held Q must not time out into another domain state.");
                        entered = true; Assert.That(_reader.GuardHeld, Is.True); Assert.That(_actor.Model.StateDuration, Is.Zero);
                        Assert.That(row.speed, Is.EqualTo(1f)); record.speed = row.speed;
                        if (!row.transition && row.currentState == "Guard" && row.currentNormalized >= 1.05f)
                        {
                            if (heldTransforms == null)
                            {
                                heldTransforms = _animator.GetComponentsInChildren<Transform>(true);
                                heldPositions = heldTransforms.Select(t => t.localPosition).ToArray(); heldRotations = heldTransforms.Select(t => t.localRotation).ToArray();
                            }
                            for (int i = 0; i < heldTransforms.Length; i++)
                            {
                                record.clampedLocalPositionDifference = Mathf.Max(record.clampedLocalPositionDifference, Vector3.Distance(heldTransforms[i].localPosition, heldPositions[i]));
                                Quaternion q = heldTransforms[i].localRotation, h = heldRotations[i];
                                record.clampedQuaternionComponentDifference = Mathf.Max(record.clampedQuaternionComponentDifference,
                                    Mathf.Max(Mathf.Max(Mathf.Abs(q.x - h.x), Mathf.Abs(q.y - h.y)), Mathf.Max(Mathf.Abs(q.z - h.z), Mathf.Abs(q.w - h.w))));
                            }
                        }
                        if (row.elapsed >= record.declaredHoldSeconds)
                        {
                            record.heldElapsed = row.elapsed; record.guardHeldTenSeconds = true;
                            GuardHitCapture("02-ten-second-static-hold.png"); RecordInput("queued-release", "Guard");
                            InputSystem.QueueStateEvent(_keyboard, new KeyboardState()); released = true;
                        }
                    }
                    else if (kind == GuardHitCase.PerfectGuardCounter)
                    {
                        if (!counterQueued && state == CombatState.Guard)
                        {
                            entered = true; Assert.That(_actor.Model.IsPerfectGuardWindow, Is.True, "No late synthetic parry or manual elapsed reset.");
                            facts.Add(GuardHitReceive(presenter, 1, true));
                            Assert.That(facts[0].perfectGuard && facts[0].defended && !facts[0].killed, Is.True);
                            Assert.That(facts[0].appliedDamage, Is.Zero); Assert.That(facts[0].healthAfter, Is.EqualTo(record.domainHealthBefore));
                            Assert.That(_actor.Model.CanUseGuardCounter, Is.True);
                            RecordInput("queued", "LightAttack"); InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 1 });
                            RecordInput("queued-release", "Guard"); InputSystem.QueueStateEvent(_keyboard, new KeyboardState()); counterQueued = released = true;
                        }
                        else if (counterQueued && state == CombatState.LightAttack1)
                        {
                            Assert.That(_actor.Model.CurrentAttackIsGuardCounter, Is.True);
                            Assert.That(_actor.Model.GuardCounterWindowRemaining, Is.Zero);
                            Assert.That(_actor.Model.CurrentAttackDamage, Is.EqualTo(_expectedTuning.GetLightDamage(0)));
                            if (!record.counterObserved) { record.counterObserved = true; InputSystem.QueueStateEvent(_mouse, new MouseState()); }
                            if (!_images.Contains("02-real-counter.png") && row.elapsed >= .10f) GuardHitCapture("02-real-counter.png");
                        }
                    }
                    else if (kind == GuardHitCase.RepeatedHit && state == CombatState.HitReact)
                    {
                        entered = true; Assert.That(_actor.Model.StateDuration, Is.EqualTo(_expectedTuning.HitReactDuration));
                        Assert.That(row.speed, Is.EqualTo(clip.length / _expectedTuning.HitReactDuration).Within(.000001f)); record.speed = row.speed;
                        if (!second && row.elapsed >= .18f)
                        {
                            Assert.That(row.currentState == "HitReact" && !row.transition, Is.True, "Second real hit must land on an actually settled HitReact baseline.");
                            facts.Add(GuardHitReceive(presenter, 2, false)); record.secondDamage = facts[1].appliedDamage;
                            Assert.That(facts[1].accepted && !facts[1].killed, Is.True);
                            Assert.That(facts[1].beforeState, Is.EqualTo(CombatState.HitReact.ToString()));
                            Assert.That(facts[1].afterElapsed, Is.Zero); Assert.That(facts[1].presentedBefore, Is.EqualTo(CombatState.HitReact.ToString()));
                            Assert.That(facts[1].presentedAfter, Is.EqualTo(facts[1].presentedBefore));
                            record.repeatedHitBeforePhase = facts[1].beforeNormalized; record.repeatedHitAfterPhase = facts[1].afterNormalized;
                            Assert.That(record.repeatedHitAfterPhase, Is.EqualTo(record.repeatedHitBeforePhase)); record.secondHitReset = true; second = true;
                        }
                        else if (second && !observedAfterSecond)
                        {
                            Assert.That(row.currentState == "HitReact" && !row.transition, Is.True, "Original same-state Presenter must not create a hidden restart/blend.");
                            record.repeatedHitNextEofPhase = row.currentNormalized; record.repeatedHitNextEofElapsed = row.elapsed;
                            Assert.That(row.currentNormalized, Is.GreaterThan(record.repeatedHitBeforePhase));
                            Assert.That(row.elapsed, Is.EqualTo(row.delta).Within(.00001f), "A real next domain tick follows the second damage reset.");
                            record.secondClipContinued = true; observedAfterSecond = true; GuardHitCapture("02-hit-after-second-damage.png");
                        }
                    }
                    bool canFinish = kind == GuardHitCase.TenSecondHold ? released : kind == GuardHitCase.PerfectGuardCounter ? record.counterObserved : observedAfterSecond;
                    if (entered && canFinish && state == CombatState.Locomotion && !record.domainReturned)
                    {
                        record.domainReturned = _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = row.time;
                    }
                    if (record.domainReturned && double.IsNaN(stableStarted) && row.stableLocomotion)
                    {
                        stableStarted = row.time; _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = row.time;
                    }
                    if (!double.IsNaN(stableStarted))
                    {
                        Assert.That(row.stableLocomotion, Is.True, "Release/recovery must remain stable Idle; no Guard/Hit/counter replay or backlog.");
                        record.stableIdleSeconds = _trace.stableIdleSeconds = (float)(row.time - stableStarted);
                        if (record.stableIdleSeconds >= .15f)
                        {
                            record.fullIdle = _trace.stableIdleCompleted = true; _trace.stableIdleEndedAt = row.time;
                            record.finalHipDrift = _trace.final = drift; GuardHitCapture("03-complete-idle-tail.png"); break;
                        }
                    }
                    yield return null;
                }
                record.executionCompleted = true;
                Assert.That(entered && record.domainReturned && record.fullIdle, Is.True, "Deadline includes actual full domain return, original fade and .15s stable Idle.");
                Assert.That(_seen.Contains(clip), Is.True); Assert.That(_seenStates.Contains(kind == GuardHitCase.RepeatedHit ? "HitReact" : "Guard"), Is.True);
                if (kind == GuardHitCase.TenSecondHold)
                {
                    Assert.That(record.guardHeldTenSeconds && heldTransforms != null, Is.True); Assert.That(_actor.Model.GuardAttemptCount, Is.EqualTo(record.guardAttemptsBefore + 1));
                    Assert.That(record.clampedLocalPositionDifference, Is.LessThanOrEqualTo(GuardTerminalPoseArithmeticTolerance), "Terminal local pose must not accumulate drift or replay over the measured long hold.");
                    Assert.That(record.clampedQuaternionComponentDifference, Is.LessThanOrEqualTo(GuardTerminalPoseArithmeticTolerance), "Terminal local rotation components must remain static, not merely record an unchecked difference.");
                    Assert.That(_actor.Model.PerfectGuardCount, Is.EqualTo(record.parriesBefore)); Assert.That(_actor.Model.Health.Current, Is.EqualTo(record.domainHealthBefore));
                }
                else if (kind == GuardHitCase.PerfectGuardCounter)
                {
                    Assert.That(record.counterObserved, Is.True); Assert.That(_actor.Model.PerfectGuardCount, Is.EqualTo(record.parriesBefore + 1));
                    Assert.That(_actor.Model.AttackSequence, Is.EqualTo(record.attackSequenceBefore + 1)); Assert.That(_seenStates.Contains("LightAttack1Recovery"), Is.True);
                }
                else
                {
                    Assert.That(second && record.secondHitReset && record.secondClipContinued, Is.True); Assert.That(facts.Count, Is.EqualTo(2));
                    Assert.That(_actor.Model.Health.Current, Is.EqualTo(record.domainHealthBefore - record.firstDamage - record.secondDamage));
                }
                record.bodyMin = _trace.minimumBody = _rows.Min(r => r.bodyMin); record.capeMin = _trace.minimumCape = _rows.Min(r => r.capeMin); record.gearMin = _trace.minimumGear = _rows.Min(r => r.gearMin);
                _trace.peak = record.maximumHipDrift; _trace.floorGateEvaluated = true;
                _trace.bodyFloorGatePassed = record.bodyMin >= 0f; _trace.capeFloorGatePassed = record.capeMin >= 0f; _trace.gearFloorGatePassed = record.gearMin >= 0f;
                Assert.That(record.maximumHipDrift, Is.LessThan(.4f)); Assert.That(record.finalHipDrift, Is.LessThan(.1f));
                Assert.That(record.bodyMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(record.capeMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(record.gearMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(_images.Count, Is.EqualTo(3)); GuardHitSafety(kind, clip, originalKey); record.accepted = true; _completed = true;
            }
            finally
            {
                if (guardAction != null) { guardAction.started -= OnInputPerformed; guardAction.canceled -= OnInputPerformed; }
                record.sourceAfter = _disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                record.memoryAfter = _memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value);
                record.files = _disk.Count; record.memory = _memory.Count; record.frames = frames.ToArray(); record.damage = facts.ToArray(); record.images = _images.ToArray();
                if (_actor != null && _actor.Model != null)
                {
                    record.domainHealthAfter = _actor.Model.Health.Current; record.guardAttemptsAfter = _actor.Model.GuardAttemptCount;
                    record.parriesAfter = _actor.Model.PerfectGuardCount; record.attackSequenceAfter = _actor.Model.AttackSequence;
                }
                if (_rows.Count > 0) { record.bodyMin = _rows.Min(r => r.bodyMin); record.capeMin = _rows.Min(r => r.capeMin); record.gearMin = _rows.Min(r => r.gearMin); }
                record.accepted &= record.sourceAfter && record.memoryAfter; record.status = record.accepted ? "completed-contract-only-visual-pending" : "incomplete-or-failed";
                if (!record.accepted) _completed = false; File.WriteAllText(recordPath, JsonUtility.ToJson(record, true));
            }
        }

        AnimationClip PrepareGuardHitCandidate(GuardHitCase kind, out AnimationClip originalKey)
        {
            CombatState state = kind == GuardHitCase.RepeatedHit ? CombatState.HitReact : CombatState.Guard;
            string nativeName = kind == GuardHitCase.RepeatedHit ? "Hit_A" : "Blocking", field = kind == GuardHitCase.RepeatedHit ? "_hitReact" : "_guard";
            var clip = AssetDatabase.LoadAllAssetsAtPath(Native).OfType<AnimationClip>().Single(c => c.name == nativeName);
            Assert.That(clip.isLooping, Is.False); originalKey = _candidateSet.GetClip(state); Assert.That(originalKey, Is.Not.Null);
            AnimationClip key = originalKey;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(overrides);
            int index = overrides.FindIndex(p => p.Key == key); Assert.That(index, Is.GreaterThanOrEqualTo(0)); Assert.That(overrides.Count(p => p.Key == key), Is.EqualTo(1));
            overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(key, clip); _candidateController.ApplyOverrides(overrides);
            var so = new SerializedObject(_candidateSet); so.FindProperty(field).objectReferenceValue = clip; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_candidateSet.GetClip(state), Is.SameAs(clip)); Assert.That(EditorUtility.IsPersistent(_candidateSet) || EditorUtility.IsPersistent(_candidateController), Is.False);
            _stateGuard.Clear(); _stateGuard.UnionWith(kind == GuardHitCase.PerfectGuardCounter ? new[] { "Locomotion", "Guard", "LightAttack1", "LightAttack1Recovery" } : new[] { "Locomotion", state.ToString() });
            _allowed.Clear(); _allowed.UnionWith(_locomotion); _allowed.Add(clip);
            if (kind == GuardHitCase.PerfectGuardCounter) { _allowed.Add(_candidateSet.GetClip(CombatState.LightAttack1)); _allowed.Add(_candidateSet.GetRecoveryClip(CombatState.LightAttack1)); }
            var camera = Own("Review_Knight_GuardHit_Camera").AddComponent<Camera>(); camera.depth = 20; camera.fieldOfView = 32;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .20f);
            camera.transform.position = _actor.transform.position + new Vector3(3.2f, 1.5f, 5f); camera.transform.LookAt(_actor.transform.position + Vector3.up * -.05f);
            var light = Own("Review_Knight_GuardHit_KeyLight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
            // No RenderSettings, existing camera/listener, production or source material mutation.
            foreach (string path in new[] {
                "Assets/_Game/Tests/PlayMode/KayKitKnightGuardHitCandidateTests.cs", "Assets/_Game/Tests/PlayMode/KayKitKnightInputCandidateTests.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerCombatActor.cs", "Assets/_Game/Scripts/Gameplay/Combat/Domain/CombatStateMachine.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs", "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationSet.cs",
                "Assets/_Game/Scripts/Gameplay/Input/PlayerInputReader.cs", "Assets/_Game/Scripts/Gameplay/Movement/ThirdPersonMotor.cs" })
            {
                Assert.That(File.Exists(path), Is.True, path);
                foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
            }
            return clip;
        }

        void GuardHitSafety(GuardHitCase kind, AnimationClip clip, AnimationClip originalKey)
        {
            Assert.That(_scene.IsValid() && _scene.isLoaded && _actor.gameObject.scene == _scene, Is.True);
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.EquivalentTo(new[] { _actor }));
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            foreach (var flow in Object.FindObjectsOfType<M2RouteFlowController>()) Assert.That(flow.SavePath, Does.Contain("IsolatedSaves"));
            Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(_candidateController)); Assert.That(AssetDatabase.GetAssetPath(_animator.avatar), Is.EqualTo(Native));
            Assert.That(_animator.avatar.isHuman && _animator.avatar.isValid, Is.True);
            Assert.That(_candidateSet.GetClip(kind == GuardHitCase.RepeatedHit ? CombatState.HitReact : CombatState.Guard), Is.SameAs(clip));
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(overrides);
            Assert.That(overrides.Single(p => p.Key == originalKey).Value, Is.SameAs(clip));
            Assert.That(_disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True, "Stop source-byte failure without rollback.");
            Assert.That(_memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value), Is.True, "Stop source-memory failure without exceptions.");
        }

        void GuardHitAnchor(Vector3 root)
        {
            Assert.That(Vector2.Distance(new Vector2(root.x, root.z), new Vector2(_actor.transform.position.x, _actor.transform.position.z)), Is.LessThan(.01f));
            Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
            Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
        }

        GuardHitFrame GuardHitSample(PlayerAnimationPresenter presenter, Row row) => new GuardHitFrame
        {
            frame = row.frame, time = row.time, domain = row.domain, elapsed = row.elapsed, current = row.currentState, next = row.nextState,
            currentNormalized = row.currentNormalized, nextNormalized = row.nextNormalized, delta = row.delta, health = row.health,
            presented = presenter.PresentedState.ToString(), guardHeld = _reader.GuardHeld, lightHeld = _reader.CaptureSnapshot().LightAttack,
            stamina = _actor.Model.Stamina.Current, posture = _actor.Model.Posture.Current, counterWindow = _actor.Model.GuardCounterWindowRemaining,
            counter = _actor.Model.CurrentAttackIsGuardCounter, transition = row.transition
        };

        GuardHitDamageFact GuardHitReceive(PlayerAnimationPresenter presenter, int sequence, bool defendable)
        {
            bool phase = _animator.GetCurrentAnimatorStateInfo(0).IsName("HitReact");
            var fact = new GuardHitDamageFact { frame = Time.frameCount, time = Time.timeAsDouble, sequence = sequence,
                beforeState = _actor.Model.State.ToString(), beforeElapsed = _actor.Model.StateElapsed, healthBefore = _actor.Model.Health.Current,
                presentedBefore = presenter.PresentedState.ToString(), clipPhaseMeasured = phase, beforeNormalized = phase ? _animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f };
            DamageResult result = _actor.ReceiveDamage(new DamageRequest(991001, sequence, 12f, defendable ? 8f : 0f, AttackTag.Light, defendable, true));
            fact.afterState = _actor.Model.State.ToString(); fact.afterElapsed = _actor.Model.StateElapsed; fact.healthAfter = _actor.Model.Health.Current;
            fact.presentedAfter = presenter.PresentedState.ToString(); fact.afterNormalized = phase ? _animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
            fact.accepted = result.Accepted; fact.defended = result.Defended; fact.perfectGuard = result.PerfectGuard; fact.killed = result.Killed;
            fact.appliedDamage = result.AppliedDamage; fact.appliedPosture = result.PostureDamageApplied; fact.counterWindow = _actor.Model.GuardCounterWindowRemaining;
            return fact;
        }

        void GuardHitCapture(string file)
        {
            Assert.That(_capturedImages.ContainsKey(file) || File.Exists(_output + "/" + file), Is.False);
            var texture = ScreenCapture.CaptureScreenshotAsTexture(); Assert.That(texture, Is.Not.Null);
            _ownedAssets.Add(texture); _capturedImages.Add(file, texture); _images.Add(file); _rows[_rows.Count - 1].image = file;
        }
    }
}
#endif
