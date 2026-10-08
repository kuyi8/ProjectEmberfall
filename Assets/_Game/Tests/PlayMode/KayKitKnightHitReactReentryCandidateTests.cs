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
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    // Isolated actual PlayerLoop reentry review; original default-off baseline remains unchanged.
    public sealed partial class KayKitKnightInputCandidateTests
    {
        enum HitReentryCase { SecondHit, SameFrameBurstAndNeutral, ExternalFreeze }

        [Serializable] sealed class HitReentryDamage
        {
            public int frame, attackerSequence; public double realtime;
            public ulong beforeOrdinal, afterOrdinal;
            public string kind, beforeState, afterState;
            public float beforeElapsed, afterElapsed, beforeHealth, afterHealth, appliedDamage, postureApplied;
            public bool accepted, defended, killed;
        }

        [Serializable] sealed class HitReentryPublication
        {
            public string injection, progressKind, state; public int frame; public ulong ordinal;
            public float elapsed, health;
        }

        [Serializable] sealed class HitReentryFrame
        {
            public int frame; public double beforeRealtime, afterRealtime, eofRealtime;
            public bool beforeCaptured, afterCaptured, eofCaptured, beforeTransition, afterTransition, eofTransition;
            public string injected, beforeState, afterState, eofState, beforePresented, afterPresented, beforeGrade, afterGrade;
            public ulong beforeOrdinal, injectedOrdinal, afterOrdinal, consumedOrdinal;
            public float beforeElapsed, injectedElapsed, afterElapsed, eofElapsed, delta, beforeSpeed, afterSpeed, baseSpeed;
            public bool beforeCurrentHit, beforeNextHit, afterCurrentHit, afterNextHit, eofCurrentHit, eofNextHit;
            public float beforeCurrentNormalized, beforeNextNormalized, afterCurrentNormalized, afterNextNormalized, eofCurrentNormalized, eofNextNormalized;
            public bool destinationMeasured, selfTransition, transitionMeasured;
            public float destinationSeconds, expectedSeconds, clockError, transitionDuration, transitionNormalized;
            public string transitionUnit;
        }

        [Serializable] sealed class HitReentryReport
        {
            public string status = "recording", mode, candidate, module;
            public string scope = "Controlled real Actor.ReceiveDamage at observer50 after Actor -200, original Presenter100, Coordinator10000, after observer10001 and actual EOF. Disposable original Knight/capsule and exact native Hit_A SO/AOC mapping. No manual Presenter/Animator evaluation, Domain Submit/Tick, source mutation, production replacement, OS input, directional/natural contact, audio, NET, Grip or final visual acceptance.";
            public string clockScope = "A fresh same-state restart has authored offset0. In a self transition, destination is NEXT HitReact, never the same-named CURRENT source. Once first actual EOF destination0 is observed, integrate each subsequent actual post-Coordinator delta*speed while domain remains HitReact. Domain elapsed is reset by damage and is never used as an absolute frozen clip clock. Original .08 fade metadata are retained observations, not automatically a fade-duration acceptance.";
            public string freezeScope = "One controlled external Heavy .10s lease at observer50 before the second accepted hit. This does not claim ordinary victim damage currently emits HitStop. No Cancel/upgrade/extension or paused domain clock; coordinator keeps current base, then naturally ends. No replay or extra restart after unfreeze.";
            public string visualScope = "No synchronous screenshots inside measured motion. Real bone/skin floor and actual state phases are recorded; rendered reentry/pop/feel remains pending. Native Death and terminal actual PlayerLoop remain unmeasured here; separate Edit terminal contracts do not transfer.";
            public bool accepted, executionCompleted, sourceBefore, memoryBefore, sourceAfter, memoryAfter, fullIdle, restartMeasured, neutralUnchanged, naturalFreezeEnded;
            public bool visualAccepted, terminalActualPlayCovered;
            public int files, memory, acceptedHits, frozenFrames, frozenPoseComparisons, freezeEndedCount;
            public ulong finalOrdinal;
            public float actualDamage, healthBefore, healthAfter, maximumHipDrift, finalHipDrift, bodyMin, capeMin, gearMin;
            public float declaredClockTolerance = FreezeClockTolerance, maximumClockError, maximumSampleGap, stableIdleSeconds, firstRestartPhaseSeconds;
            public double freezeRequestedAt, freezeEndedAt, freezeEffectiveMilliseconds;
            public bool freezeRequestAccepted;
            public float[] warmupDeltas;
            public HitReentryDamage[] damage; public HitReentryPublication[] publications; public HitReentryFrame[] frames;
        }

        sealed class HitReentryContext
        {
            public HitReentryCase mode; public HitReentryReport report;
            public AnimationClip clip, originalKey;
            public PlayerAnimationPresenter presenter; public AnimatorSpeedCoordinator speed;
            public KnightHitStopBeforePresenterObserver before; public KnightHitStopAfterCoordinatorObserver after;
            public FieldInfo observedOrdinal;
            public readonly List<HitReentryFrame> frames = new List<HitReentryFrame>();
            public readonly List<HitReentryDamage> damage = new List<HitReentryDamage>();
            public readonly List<HitReentryPublication> publications = new List<HitReentryPublication>();
            public readonly List<float> warmup = new List<float>();
            public Action<HitFeedbackGrade, double> ended; public Action<PlayerCombatActor, CombatProgressKind> progressed;
            public bool armed, secondInjected, neutralInjected, firstRestartEof; public int stage;
            public string injection; public double expectedSeconds;
            public Row previousFrozenPose;
        }

        [UnityTest] public IEnumerator ActualDamage_HitReactOptInRestartsSecondAcceptedHit()
            => HitReactReentry(HitReentryCase.SecondHit);

        [UnityTest] public IEnumerator ActualDamage_HitReactSameFrameBurstCoalescesAndNeutralPostureDoesNotRestart()
            => HitReactReentry(HitReentryCase.SameFrameBurstAndNeutral);

        [UnityTest] public IEnumerator ActualDamage_HitReactReentryPreservesExternalFreezeAndNaturallyResumes()
            => HitReactReentry(HitReentryCase.ExternalFreeze);

        IEnumerator HitReactReentry(HitReentryCase mode)
        {
            var c = new HitReentryContext { mode = mode, report = new HitReentryReport { mode = mode.ToString(), module = GetType().Assembly.ManifestModule.ModuleVersionId.ToString() } };
            string path = _output + "/hit-react-reentry-review.json";
            Assert.That(File.Exists(path), Is.False, "Never overwrite a native attempt.");
            Vector2 baseline = default; Vector3 root = default; bool returned = false; double idleStarted = double.NaN;
            try
            {
                c.clip = PrepareGuardHitCandidate(GuardHitCase.RepeatedHit, out c.originalKey);
                _candidateSet.ConfigureOfflineHitReactReentry(true);
                Assert.That(_candidateSet.RestartOfflineHitReactOnAcceptedDamage, Is.True);
                c.presenter = _actor.GetComponent<PlayerAnimationPresenter>(); c.speed = AnimatorSpeedCoordinator.For(_animator);
                c.observedOrdinal = typeof(PlayerAnimationPresenter).GetField("_observedHitReactSequence", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(c.observedOrdinal, Is.Not.Null, "Read-only loaded Presenter consumption identity must exist.");
                c.before = _actor.gameObject.AddComponent<KnightHitStopBeforePresenterObserver>();
                c.after = _actor.gameObject.AddComponent<KnightHitStopAfterCoordinatorObserver>();
                c.progressed = (actor, kind) => c.publications.Add(new HitReentryPublication { injection = c.injection,
                    progressKind = kind.ToString(), frame = Time.frameCount, ordinal = actor.HitReactPresentationSequence,
                    state = actor.Model.State.ToString(), elapsed = actor.Model.StateElapsed, health = actor.Model.Health.Current });
                c.ended = (grade, milliseconds) =>
                {
                    c.report.freezeEndedCount++; c.report.freezeEndedAt = Time.realtimeSinceStartupAsDouble;
                    c.report.freezeEffectiveMilliseconds = milliseconds;
                    Assert.That(grade, Is.EqualTo(HitFeedbackGrade.Heavy));
                };
                _actor.CombatProgressed += c.progressed; c.speed.FreezeEnded += c.ended;
                c.report.candidate = Native + "::" + c.clip.name;
                _trace.ability = "Damage/HitReactReentry/" + mode; _trace.scope = c.report.scope; _trace.abilityScope = c.report.clockScope;
                _trace.candidate = c.report.candidate; _trace.candidateLength = c.clip.length; _trace.expectedDuration = _expectedTuning.HitReactDuration;
                _trace.fullAbilityAccepted = false; _trace.settled = true;
                foreach (string script in new[] {
                    "Assets/_Game/Tests/PlayMode/KayKitKnightHitReactReentryCandidateTests.cs",
                    "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerCombatActor.cs",
                    "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationSet.cs",
                    "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs",
                    "Assets/_Game/Scripts/Gameplay/Animation/AnimatorSpeedCoordinator.cs",
                    "Assets/_Game/Tests/PlayMode/KnightHitStopBeforePresenterObserver.cs",
                    "Assets/_Game/Tests/PlayMode/KnightHitStopAfterCoordinatorObserver.cs" })
                {
                    Assert.That(File.Exists(script), Is.True, script);
                    foreach (string file in new[] { script, script + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
                }
                GuardHitSafety(GuardHitCase.RepeatedHit, c.clip, c.originalKey); c.report.sourceBefore = c.report.memoryBefore = true;
                yield return new WaitForSeconds(.5f);
                double timeout = Time.realtimeSinceStartupAsDouble + 8d, steadyStarted = double.NaN;
                do
                {
                    yield return null; yield return new WaitForEndOfFrame(); Observe(0f);
                    Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); Assert.That(_animator.IsInTransition(0), Is.False);
                    Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
                    c.warmup.Add(Time.deltaTime);
                    if (Time.deltaTime > 0f && Time.deltaTime <= 1f / 30f)
                    {
                        if (double.IsNaN(steadyStarted)) steadyStarted = Time.timeAsDouble;
                    }
                    else steadyStarted = double.NaN;
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(timeout), "Genuine quiescent pre-hit warmup deadline; no simulated frames.");
                } while (double.IsNaN(steadyStarted) || Time.timeAsDouble - steadyStarted < .15d);
                baseline = PlanarHips(); root = _actor.transform.position;
                _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation;
                _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
                _rows.Clear(); _seen.Clear(); _seenStates.Clear();
                Observe(0f); c.report.healthBefore = _actor.Model.Health.Current;
                Assert.That(_actor.HitReactPresentationSequence, Is.Zero); Assert.That(c.speed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                c.before.Sample = () => HitReentryBefore(c); c.after.Sample = () => HitReentryAfter(c); c.armed = true;
                yield return null;
                for (int frame = 0; frame < 5000; frame++)
                {
                    yield return new WaitForEndOfFrame(); Observe(Vector2.Distance(baseline, PlanarHips()));
                    Row pose = _rows[_rows.Count - 1]; HitReentryEof(c, pose); GuardHitAnchor(root);
                    c.report.maximumHipDrift = Mathf.Max(c.report.maximumHipDrift, pose.hipDrift);
                    if (_rows.Count > 1) c.report.maximumSampleGap = Mathf.Max(c.report.maximumSampleGap, (float)(pose.time - _rows[_rows.Count - 2].time));
                    c.report.maximumSampleGap = Mathf.Max(c.report.maximumSampleGap, pose.delta);
                    if (c.secondInjected && _actor.Model.State == CombatState.Locomotion && !returned)
                    {
                        returned = _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = pose.time;
                    }
                    if (returned && double.IsNaN(idleStarted) && pose.stableLocomotion)
                    {
                        idleStarted = pose.time; _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = pose.time;
                    }
                    if (!double.IsNaN(idleStarted))
                    {
                        Assert.That(pose.stableLocomotion, Is.True, "No hit replay after genuine complete Idle fade.");
                        c.report.stableIdleSeconds = _trace.stableIdleSeconds = (float)(pose.time - idleStarted);
                        if (c.report.stableIdleSeconds >= .15f)
                        {
                            c.report.fullIdle = _trace.stableIdleCompleted = true; _trace.stableIdleEndedAt = pose.time;
                            c.report.finalHipDrift = _trace.final = pose.hipDrift; break;
                        }
                    }
                    yield return null;
                }
                c.report.executionCompleted = true;
                Assert.That(c.secondInjected && c.report.restartMeasured && returned && c.report.fullIdle, Is.True);
                Assert.That(c.frames.All(f => f.beforeCaptured && f.afterCaptured && f.eofCaptured), Is.True, "Actual 50/10001/EOF ordering must all be covered.");
                Assert.That(c.frames.Count(f => f.injected == "second" || f.injected == "burst"), Is.EqualTo(1));
                Assert.That(c.report.maximumSampleGap, Is.LessThanOrEqualTo(1f / 30f), "Actual measured phase density is not inferred from nominal frame rate.");
                Assert.That(c.report.maximumClockError, Is.LessThanOrEqualTo(FreezeClockTolerance));
                Assert.That(_seenStates.SetEquals(new[] { "Locomotion", "HitReact" }), Is.True); Assert.That(_seen.Contains(c.clip), Is.True);
                int expectedHits = mode == HitReentryCase.SameFrameBurstAndNeutral ? 3 : 2;
                Assert.That(c.damage.Count, Is.EqualTo(expectedHits)); Assert.That(c.damage.All(d => d.accepted && !d.defended && !d.killed), Is.True);
                Assert.That(_actor.HitReactPresentationSequence, Is.EqualTo((ulong)expectedHits));
                Assert.That(_actor.Model.Health.Current, Is.EqualTo(c.report.healthBefore - c.damage.Sum(d => d.appliedDamage)));
                if (mode == HitReentryCase.SameFrameBurstAndNeutral)
                {
                    Assert.That(c.neutralInjected && c.report.neutralUnchanged, Is.True);
                    Assert.That(c.damage[1].frame, Is.EqualTo(c.damage[2].frame));
                    Assert.That(c.publications.Where(p => p.injection == "burst").Select(p => p.ordinal), Is.EqualTo(new ulong[] { 2, 3 }));
                    Assert.That(c.publications.Single(p => p.injection == "neutral").ordinal, Is.EqualTo(3UL));
                }
                if (mode == HitReentryCase.ExternalFreeze)
                {
                    Assert.That(c.report.freezeRequestAccepted, Is.True); Assert.That(c.report.frozenFrames, Is.GreaterThanOrEqualTo(2));
                    Assert.That(c.report.frozenPoseComparisons, Is.GreaterThanOrEqualTo(1));
                    Assert.That(c.report.freezeEndedCount, Is.EqualTo(1)); Assert.That(c.report.freezeEffectiveMilliseconds, Is.GreaterThanOrEqualTo(100d));
                    Assert.That(c.speed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None)); c.report.naturalFreezeEnded = true;
                }
                c.report.bodyMin = _trace.minimumBody = _rows.Min(r => r.bodyMin); c.report.capeMin = _trace.minimumCape = _rows.Min(r => r.capeMin); c.report.gearMin = _trace.minimumGear = _rows.Min(r => r.gearMin);
                _trace.peak = c.report.maximumHipDrift; _trace.floorGateEvaluated = true;
                _trace.bodyFloorGatePassed = c.report.bodyMin >= 0f; _trace.capeFloorGatePassed = c.report.capeMin >= 0f; _trace.gearFloorGatePassed = c.report.gearMin >= 0f;
                Assert.That(c.report.maximumHipDrift, Is.LessThan(.4f)); Assert.That(c.report.finalHipDrift, Is.LessThan(.1f));
                Assert.That(c.report.bodyMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(c.report.capeMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(c.report.gearMin, Is.GreaterThanOrEqualTo(0f));
                GuardHitSafety(GuardHitCase.RepeatedHit, c.clip, c.originalKey); c.report.accepted = true; _completed = true;
            }
            finally
            {
                c.armed = false; if (c.before != null) c.before.Sample = null; if (c.after != null) c.after.Sample = null;
                if (_actor != null && c.progressed != null) _actor.CombatProgressed -= c.progressed;
                if (c.speed != null && c.ended != null) c.speed.FreezeEnded -= c.ended;
                c.report.sourceAfter = _disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                c.report.memoryAfter = _memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value);
                c.report.files = _disk.Count; c.report.memory = _memory.Count; c.report.frames = c.frames.ToArray(); c.report.damage = c.damage.ToArray();
                c.report.publications = c.publications.ToArray(); c.report.warmupDeltas = c.warmup.ToArray();
                c.report.acceptedHits = c.damage.Count(d => d.accepted); c.report.actualDamage = c.damage.Sum(d => d.appliedDamage);
                if (_actor != null) { c.report.finalOrdinal = _actor.HitReactPresentationSequence; c.report.healthAfter = _actor.Model.Health.Current; }
                if (_rows.Count != 0) { c.report.bodyMin = _rows.Min(r => r.bodyMin); c.report.capeMin = _rows.Min(r => r.capeMin); c.report.gearMin = _rows.Min(r => r.gearMin); }
                c.report.accepted &= c.report.sourceAfter && c.report.memoryAfter; if (!c.report.accepted) _completed = false;
                c.report.status = c.report.accepted ? "completed-contract-only-visual-pending" : "incomplete-or-failed";
                File.WriteAllText(path, JsonUtility.ToJson(c.report, true));
            }
        }

        void HitReentryBefore(HitReentryContext c)
        {
            if (!c.armed) return;
            var current = _animator.GetCurrentAnimatorStateInfo(0); var next = _animator.GetNextAnimatorStateInfo(0);
            var row = new HitReentryFrame { frame = Time.frameCount, beforeCaptured = true, beforeRealtime = Time.realtimeSinceStartupAsDouble,
                beforeState = _actor.Model.State.ToString(), beforePresented = c.presenter.PresentedState.ToString(), beforeElapsed = _actor.Model.StateElapsed,
                beforeOrdinal = _actor.HitReactPresentationSequence, delta = Time.deltaTime, beforeSpeed = _animator.speed, beforeGrade = c.speed.ActiveGrade.ToString(),
                beforeTransition = _animator.IsInTransition(0), beforeCurrentHit = current.IsName("HitReact"), beforeNextHit = next.IsName("HitReact"),
                beforeCurrentNormalized = current.normalizedTime, beforeNextNormalized = next.normalizedTime };
            c.frames.Add(row);
            if (c.stage == 0)
            {
                Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); row.injected = "first"; c.injection = "first";
                c.damage.Add(HitReentryReceive()); c.stage = 1; c.injection = null;
            }
            else if (c.stage == 1 && _actor.Model.State == CombatState.HitReact && _actor.Model.StateElapsed >= .18f && current.IsName("HitReact") && !row.beforeTransition)
            {
                if (c.mode == HitReentryCase.ExternalFreeze)
                {
                    c.report.freezeRequestedAt = Time.realtimeSinceStartupAsDouble;
                    c.report.freezeRequestAccepted = c.speed.Request(HitFeedbackGrade.Heavy, 991003);
                    Assert.That(c.report.freezeRequestAccepted, Is.True); Assert.That(_animator.speed, Is.Zero);
                }
                row.injected = c.mode == HitReentryCase.SameFrameBurstAndNeutral ? "burst" : "second"; c.injection = row.injected;
                c.damage.Add(HitReentryReceive()); if (c.mode == HitReentryCase.SameFrameBurstAndNeutral) c.damage.Add(HitReentryReceive());
                c.secondInjected = true; c.stage = 2; c.injection = null;
                Assert.That(_actor.Model.State, Is.EqualTo(CombatState.HitReact)); Assert.That(_actor.Model.StateElapsed, Is.Zero);
            }
            else if (c.mode == HitReentryCase.SameFrameBurstAndNeutral && c.stage == 2 && _actor.Model.State == CombatState.HitReact && _actor.Model.StateElapsed >= .12f && current.IsName("HitReact") && !row.beforeTransition)
            {
                ulong ordinal = _actor.HitReactPresentationSequence; float elapsed = _actor.Model.StateElapsed, health = _actor.Model.Health.Current;
                row.injected = "neutral"; c.injection = "neutral";
                float applied = _actor.ApplyNeutralPostureDamage(1f); c.injection = null;
                Assert.That(applied, Is.EqualTo(1f)); Assert.That(_actor.HitReactPresentationSequence, Is.EqualTo(ordinal));
                Assert.That(_actor.Model.StateElapsed, Is.EqualTo(elapsed)); Assert.That(_actor.Model.Health.Current, Is.EqualTo(health));
                Assert.That(_actor.Model.State, Is.EqualTo(CombatState.HitReact)); c.neutralInjected = c.report.neutralUnchanged = true; c.stage = 3;
            }
            row.injectedOrdinal = _actor.HitReactPresentationSequence; row.injectedElapsed = _actor.Model.StateElapsed;
        }

        HitReentryDamage HitReentryReceive()
        {
            var fact = new HitReentryDamage { frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble, kind = "ordinary-accepted",
                attackerSequence = 7, beforeOrdinal = _actor.HitReactPresentationSequence, beforeState = _actor.Model.State.ToString(),
                beforeElapsed = _actor.Model.StateElapsed, beforeHealth = _actor.Model.Health.Current };
            var result = _actor.ReceiveDamage(new DamageRequest(991001, 7, 12f, 0f, AttackTag.Light, false, true));
            fact.afterOrdinal = _actor.HitReactPresentationSequence; fact.afterState = _actor.Model.State.ToString(); fact.afterElapsed = _actor.Model.StateElapsed;
            fact.afterHealth = _actor.Model.Health.Current; fact.accepted = result.Accepted; fact.defended = result.Defended; fact.killed = result.Killed;
            fact.appliedDamage = result.AppliedDamage; fact.postureApplied = result.PostureDamageApplied;
            Assert.That(fact.accepted && !fact.defended && !fact.killed, Is.True); Assert.That(fact.afterOrdinal, Is.EqualTo(fact.beforeOrdinal + 1UL));
            Assert.That(fact.afterElapsed, Is.Zero); return fact;
        }

        void HitReentryAfter(HitReentryContext c)
        {
            if (!c.armed) return;
            var row = c.frames.Single(r => r.frame == Time.frameCount); var current = _animator.GetCurrentAnimatorStateInfo(0); var next = _animator.GetNextAnimatorStateInfo(0);
            row.afterCaptured = true; row.afterRealtime = Time.realtimeSinceStartupAsDouble; row.afterState = _actor.Model.State.ToString(); row.afterElapsed = _actor.Model.StateElapsed;
            row.afterPresented = c.presenter.PresentedState.ToString(); row.afterOrdinal = _actor.HitReactPresentationSequence;
            row.consumedOrdinal = (ulong)c.observedOrdinal.GetValue(c.presenter); row.afterSpeed = _animator.speed; row.baseSpeed = c.speed.BaseSpeed;
            row.afterGrade = c.speed.ActiveGrade.ToString(); row.afterTransition = _animator.IsInTransition(0);
            row.afterCurrentHit = current.IsName("HitReact"); row.afterNextHit = next.IsName("HitReact");
            row.afterCurrentNormalized = current.normalizedTime; row.afterNextNormalized = next.normalizedTime;
            Assert.That(row.consumedOrdinal, Is.EqualTo(row.afterOrdinal), "Real Presenter must consume the actual latest victim ordinal without another manual observation.");
            if (row.injected == "second" || row.injected == "burst")
            {
                Assert.That(row.afterPresented, Is.EqualTo(CombatState.HitReact.ToString())); Assert.That(row.afterElapsed, Is.Zero);
                if (c.mode == HitReentryCase.ExternalFreeze)
                {
                    Assert.That(c.speed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.Heavy)); Assert.That(row.afterSpeed, Is.Zero);
                    Assert.That(row.baseSpeed, Is.EqualTo(Mathf.Clamp(c.clip.length / _expectedTuning.HitReactDuration, .35f, 3f)));
                }
            }
        }

        void HitReentryEof(HitReentryContext c, Row pose)
        {
            var row = c.frames.Single(r => r.frame == Time.frameCount); row.eofCaptured = true; row.eofRealtime = Time.realtimeSinceStartupAsDouble;
            row.eofState = pose.domain; row.eofElapsed = pose.elapsed; row.eofTransition = pose.transition;
            row.eofCurrentHit = pose.currentState == "HitReact"; row.eofNextHit = pose.nextState == "HitReact";
            row.eofCurrentNormalized = pose.currentNormalized; row.eofNextNormalized = pose.nextNormalized;
            row.selfTransition = row.eofTransition && row.eofCurrentHit && row.eofNextHit;
            if (row.eofTransition)
            {
                var transition = _animator.GetAnimatorTransitionInfo(0); row.transitionMeasured = true;
                row.transitionDuration = transition.duration; row.transitionUnit = transition.durationUnit.ToString(); row.transitionNormalized = transition.normalizedTime;
            }
            row.destinationMeasured = row.eofNextHit || row.eofCurrentHit;
            if (row.destinationMeasured) row.destinationSeconds = (row.eofNextHit ? pose.nextNormalized : pose.currentNormalized) * c.clip.length;
            if (row.injected == "second" || row.injected == "burst")
            {
                Assert.That(row.destinationMeasured, Is.True); Assert.That(row.destinationSeconds, Is.EqualTo(0f).Within(FreezeClockTolerance), "An unchanged same-hash source phase is not a fresh requested offset0 destination.");
                Assert.That(row.beforeCurrentNormalized * c.clip.length, Is.GreaterThan(row.destinationSeconds));
                c.firstRestartEof = c.report.restartMeasured = true; c.report.firstRestartPhaseSeconds = row.destinationSeconds; c.expectedSeconds = 0d;
            }
            else if (c.firstRestartEof && row.eofState == CombatState.HitReact.ToString()) c.expectedSeconds += (double)row.delta * row.afterSpeed;
            if (c.firstRestartEof && row.eofState == CombatState.HitReact.ToString())
            {
                Assert.That(row.destinationMeasured, Is.True); row.expectedSeconds = (float)c.expectedSeconds;
                row.clockError = Mathf.Abs(row.destinationSeconds - row.expectedSeconds); c.report.maximumClockError = Mathf.Max(c.report.maximumClockError, row.clockError);
            }
            if (c.firstRestartEof && row.afterGrade == HitFeedbackGrade.Heavy.ToString() && row.afterSpeed == 0f)
            {
                c.report.frozenFrames++;
                if (c.previousFrozenPose != null)
                {
                    Assert.That(pose.hipsRelative, Is.EqualTo(c.previousFrozenPose.hipsRelative)); Assert.That(pose.chestRelative, Is.EqualTo(c.previousFrozenPose.chestRelative));
                    Assert.That(pose.rightHandRelative, Is.EqualTo(c.previousFrozenPose.rightHandRelative)); Assert.That(pose.leftHandRelative, Is.EqualTo(c.previousFrozenPose.leftHandRelative));
                    c.report.frozenPoseComparisons++;
                }
                c.previousFrozenPose = pose;
            }
            else c.previousFrozenPose = null;
        }
    }
}
#endif
