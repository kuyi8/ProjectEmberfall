#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    // Isolated real-controller input review; never modifies production assets.
    public sealed partial class KayKitKnightInputCandidateTests
    {
        const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Native = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx/Knight.fbx";
        const string Prefab = "Assets/_Game/Art/Review/KayKitHero/knight-assembly-20261003-a1/Review_Knight_Hero.prefab";
        const string Motion = "Assets/_Game/Art/Review/KayKitHeroMotion/knight-motion-facing-20261003-a2/";
        const string UpperThrow = "Assets/_Game/Art/Review/KayKitHeroMotion/knight-upperbody-throw-author-20261003-a1/A_Review_Knight_InPlace_UpperBody_Throw.anim";
        const string StagedUpperThrow = "Assets/_Game/Art/Review/KayKitHeroMotion/knight-upperbody-throw-author-20261003-a2/A_Review_Knight_InPlace_UpperBody_Throw.anim";
        readonly List<Object> _ownedAssets = new List<Object>();
        readonly List<Row> _rows = new List<Row>();
        readonly List<EventRow> _events = new List<EventRow>();
        readonly Dictionary<string, string> _disk = new Dictionary<string, string>();
        readonly Dictionary<Object, string> _memory = new Dictionary<Object, string>();
        readonly HashSet<AnimationClip> _allowed = new HashSet<AnimationClip>(), _seen = new HashSet<AnimationClip>(), _locomotion = new HashSet<AnimationClip>();
        static readonly string[] ExpectedStates = { "Locomotion", "LightAttack1", "LightAttack1Recovery", "LightAttack2", "LightAttack2Recovery", "LightAttack3" };
        static readonly string[] KnownStates = ExpectedStates.Concat(new[] { "PlayerRangedAttack", "Heal", "Dodge", "Guard", "HitReact", "HeavyCharge", "PlayerHeavyAttack" }).ToArray();
        readonly HashSet<string> _stateGuard = new HashSet<string>();
        readonly HashSet<string> _seenStates = new HashSet<string>();
        Scene _scene; InputSettings _previousSettings; Mouse _mouse; Keyboard _keyboard; PlayerCombatActor _actor; Animator _animator;
        Transform _hips, _chest, _head, _rightHand, _leftHand, _rightUpperArm; AnimationClip _throwClip, _healClip; CombatTuning _expectedTuning;
        AnimatorOverrideController _candidateController; PlayerAnimationSet _candidateSet; AnimationClip _originalRangedKey; bool _upperBodyCandidate;
        PlayerInputReader _reader; PlayerThrowingKnifeLauncher _launcher; PlayerKnifePresentation _knife; bool _withKnife;
        readonly List<InputRow> _inputs = new List<InputRow>(); readonly List<string> _images = new List<string>();
        readonly Dictionary<string, Texture2D> _capturedImages = new Dictionary<string, Texture2D>();
        readonly List<EntryRecord> _entries = new List<EntryRecord>();
        readonly List<EntryRecord> _attemptedEntries = new List<EntryRecord>();
        readonly List<GateFailure> _entryFailures = new List<GateFailure>();
        bool _collectEntryDiagnostics, _atSignedFloor;
        bool _domainEntrySync; int _entryNumber;
        const float FreezeClockTolerance = .00001f;
        readonly List<FreezeLoopRow> _freezeLoops = new List<FreezeLoopRow>();
        readonly List<FreezeEvent> _freezeEvents = new List<FreezeEvent>();
        readonly List<GateFailure> _freezeFailures = new List<GateFailure>();
        KnightHitStopBeforePresenterObserver _freezeBefore;
        KnightHitStopAfterCoordinatorObserver _freezeAfter;
        AnimatorSpeedCoordinator _freezeSpeed; PlayerAnimationPresenter _freezePresenter;
        FieldInfo _freezeSequenceField; Action<HitFeedbackGrade, double> _freezeEnded;
        HitStopRecord _hitStop; double _expectedFreezeClipTime; bool _freezeEntryObserved;
        Vector3 _anchor; Quaternion _anchorRotation; CursorLockMode _cursorLock; bool _cursorVisible;
        string _output; Trace _trace; bool _completed, _globalsCaptured;
        [Serializable] sealed class Row
        {
            public int frame, currentHash, nextHash; public string domain, current, next, currentState, nextState;
            public float elapsed, delta, currentNormalized, nextNormalized, speed, hipsYaw, chestYaw, hipsYawFromIdle, chestYawFromIdle, hipDrift, bodyMin, capeMin, gearMin;
            public Vector3 root, hips; public Quaternion hipsRelative, chestRelative;
            public Vector3 headActor, rightHandActor, leftHandActor, rightUpperArmActor; public Quaternion rightHandRelative, leftHandRelative, rightUpperArmRelative;
            public float health, rightHandForwardVelocity; public int charges, healSequence, releaseSequence, attackSequence; public bool rightHandVelocityMeasured;
            public double time; public bool transition, conservativeRigidFloor, stableLocomotion; public float[] currentWeights, nextWeights;
            public bool heldVisible, flightVisible, knifeGeometryMeasured, conservativeKnifeFloor; public int projectiles, visuals, trailPoints; public float bridgeAge, knifeMin; public Vector3 knifePosition;
            public string image;
        }
        [Serializable] sealed class InputRow { public string kind, action, domain, editorWindow, background, editorBehavior; public bool appFocused, keyboardEnabled, mouseEnabled, rangedHeld, healHeld, lightHeld; public int frame, keyboardId, mouseId; public double time; }
        [Serializable] sealed class EventRow { public string kind, domain; public int frame, sequence, attackSequence, charges; public double time; public float elapsed, delta, health; public Vector3 rightHandActor; }
        [Serializable] sealed class DiskRow { public string path, before, after; }
        [Serializable] sealed class MemoryRow { public string path, name, beforeJson, afterJson; }
        [Serializable] sealed class EntryRecord
        {
            public int index; public Row[] rows; public EventRow[] events; public InputRow[] inputs; public string[] images;
            public float bodyMin, capeMin, gearMin, stableIdleSeconds, maximumClockError, forwardSampleMarginSeconds;
            public bool handoffPassed, lifetimeCompleted, bridgeSamplingComplete; public float maximumBridgeSampleGap;
            public string status, candidate, sourceActor, sourceController, scope, abilityScope, knifeScope, abortReason; public GateFailure[] failures;
            public bool attempted, executionCompleted, accepted, abilityEvidenceComplete, clockGateEvaluated, clockGatePassed, phaseGateEvaluated, floorGateEvaluated, minimumFloorsMeasured, bodyFloorPassed, capeFloorPassed, gearFloorPassed, sourcesVerifiedBefore, sourcesVerifiedAfter;
            public int finalProjectiles, finalVisuals, finalTrailPoints, bridgeInteriorSamples; public bool finalSwordVisible; public string[] seenStates;
        }
        [Serializable] sealed class GateFailure { public string gate, message, stackTrace; }
        enum FreezeBoundary { EntryBeforeFreeze, EntryDuringFreeze, EntryAfterUnfreeze }
        [Serializable] sealed class FreezeEvent
        {
            public string kind, domain, grade, activeGrade; public ulong requestedSequence, actualSequence;
            public int frame; public double realtime, time, effectiveMilliseconds;
            public float elapsed, speed, baseSpeed; public bool requestAccepted;
        }
        [Serializable] sealed class FreezeLoopRow
        {
            public int frame; public double beforeRealtime, afterRealtime, expectedClipTime;
            public string beforeDomain, beforePresented, beforeGrade, afterDomain, afterPresented, afterGrade;
            public float beforeElapsed, beforeDelta, beforeSpeed, beforeBase, afterElapsed, afterDelta, afterSpeed, afterBase;
            public ulong beforeSequence, afterSequence; public bool entry, beforeCaptured, afterCaptured, destinationObserved;
            public float expectedEntryTime, observedClipTime, destinationStateSpeed, clockError, incrementError; public Row eof;
            public int sourceFullHash, sourceShortHash;
            public float sourceStateLength, sourceStateSpeed, sourceStateSpeedMultiplier;
            public bool sourceWasTransitioning, locomotionEntry, transitionInfoMeasured;
            public string transitionDurationUnit, fadeDirection;
            public float transitionDuration, transitionNormalized, fadeProgressError;
            public double fadeUnfrozenSeconds, fadeObservedProgressSeconds;
        }
        [Serializable] sealed class HitStopRecord
        {
            public string boundary, status, abortReason;
            public string scope = "Test-only presentation freeze on an isolated original .75 Knight/capsule, owned staged Throw, SO/AOC clone and genuine F/PlayerLoop. Exactly one original Heavy .10s Request; no manual Tick/Animator.Update/Cancel, domain pause, catch-up seek, source/production/network/contact/Grip acceptance. NOT OS-input or natural-contact evidence.";
            public string clockScope = "One entry offset plus actual post-Coordinator, pre-Animator unfrozen delta integral; first entry frame is an offset, not an extra delta. Natural unfreeze frame advances its real delta. Cold whole-state absolute clock equality is not this freeze contract.";
            public string fadeScope = "Predeclared original .08s forward and reverse fade in actual unfrozen Animator time. Normalized duration uses the observed SOURCE state length (never destination .56); source speed/multiplier and Animator base stay1. First actual transition fraction is only a bounded initial progress observation, then integrate each actual post-Coordinator delta*speed, including natural-unfreeze/completion frames. Adjacent transitional/settled EOF samples must bracket .08; no exact wall-time .08 or fitted duration.";
            public string phaseScope, stateElapsedEvidence, candidate, clipGuid; public long clipLocalId;
            public bool fullAbilityAccepted, freezeContractPassed, executionCompleted, sourcesVerifiedBefore, sourcesVerifiedAfter;
            public bool entered, domainReturned, settled, stableContinuity = true, entryOffsetMeasured, freezeAwareClockMeasured;
            public bool releasePhaseMeasured, releasePhaseActualPassed, coldPhaseApplicable, coldPhaseGatePassed;
            public bool handoffLifecyclePassed, lifetimeGatePassed, signedFloorPassed;
            public float tolerance = FreezeClockTolerance, idleElapsedBeforeInput, entryElapsed, entryDelta, expectedEntryTime, observedEntryTime;
            public float actualEntryDuration;
            public float maximumClockError, maximumIncrementError, unfreezeIncrement, expectedUnfreezeIncrement, bodyMin, capeMin, gearMin;
            public float releasePreviousHandZ, releaseBeforeHandZ, releaseHandZ, releaseHandVelocity;
            public int requestCount, frozenFrames, frozenRangedFrames, unfreezeFrames, entryFrame, releaseFrame;
            public ulong requestSequence; public FreezeLoopRow[] loops; public FreezeEvent[] freezeEvents; public GateFailure[] failures;
            public float declaredFadeSeconds = .08f;
            public bool forwardFadeMeasured, forwardFadePassed, reverseFadeMeasured, reverseFadePassed;
            public int forwardFadeEntryFrame, reverseFadeEntryFrame, forwardFadeLastTransitionFrame, reverseFadeLastTransitionFrame, forwardFadeSettledFrame, reverseFadeSettledFrame;
            public float forwardSourceLength, forwardSourceSpeed, forwardSourceSpeedMultiplier, reverseSourceLength, reverseSourceSpeed, reverseSourceSpeedMultiplier;
            public float forwardTransitionDuration, reverseTransitionDuration, forwardEffectiveFadeSeconds, reverseEffectiveFadeSeconds, forwardFadeMaximumProgressError, reverseFadeMaximumProgressError;
            public string forwardTransitionUnit, reverseTransitionUnit;
            public double forwardFadeInitialProgress, reverseFadeInitialProgress, forwardFadeBracketBefore, reverseFadeBracketBefore, forwardFadeBracketAfter, reverseFadeBracketAfter;
        }
        [Serializable] sealed class Trace
        {
            public string scope = "Disposable Actor from exact authored Valley Player values, original .75 Knight Avatar, nonpersistent SO/AOC, actual InputSystem/PlayerLoop/Presenter blend. No manual Animator.Update/Tick/Simulate, no contact/HitStop/knife/heal/production/network acceptance. Skins use BakeMesh(true); unreadable rigid parts use conservative world Renderer bounds, NOT exact contact proof. Yaws are bone-axis diagnostics, not a universal anatomical facing axis.";
            public string status = "setup", sourceActor, sourceController, output; public bool settled, sourcesUnchanged, sourceMemoryUnchanged;
            public bool domainReturnedToLocomotion, animatorSettledToLocomotion, stableIdleCompleted, globalsCaptured, inputSettingsRestored, cursorRestored;
            public double domainReturnedAt, animatorSettledAt, stableIdleEndedAt; public float stableIdleSeconds;
            public int inputSettingsBeforeId, inputSettingsAfterId; public string cursorLockBefore, cursorLockAfter; public bool cursorVisibleBefore, cursorVisibleAfter;
            public string ability, candidate, abilityScope = "Input/domain/Controller and signed body/cape/gear floor baseline only. No Launcher, KnifePresentation, bottle, IK, projectile/contact/grip acceptance. Head is NOT a mouth anchor. RightUpperArm (upperarm.r) is a proximal-joint reference, NOT a mapped RightShoulder. Hand velocity uses adjacent actual sampled times, not .22 multiplied by playback speed.";
            public string grip = "not-measured", mouth = "not-measured", visibleKnife = "not-measured", projectile = "not-measured";
            public bool rightShoulderBoneMapped, abilityEvidenceComplete, floorGateEvaluated, bodyFloorGatePassed, capeFloorGatePassed, gearFloorGatePassed, fullAbilityAccepted;
            public float expectedReleaseTime, expectedDuration, candidateLength, domainFirstObservedElapsed, healthBefore, fixtureHealthLoss, minimumBody, minimumCape, minimumGear;
            public int chargesBefore, sequenceBefore; public double inputQueuedAt, domainFirstObservedAt; public EventRow[] events;
            public float peak, final; public Quaternion idleHipsRelative, idleChestRelative; public string[] seenStates; public Row[] rows; public DiskRow[] files; public MemoryRow[] changedMemory;
            public InputRow[] inputs; public string[] images; public bool knifeHandoffPassed, knifeLifetimeCompleted; public string knifeScope = "not-measured";
            public bool bridgeSamplingComplete; public float maximumBridgeSampleGap; public int bridgeInteriorSamples;
            public bool domainEntrySyncEnabled, entryClockGateEvaluated, entryClockGatePassed; public float maximumEntryClockError, forwardSampleMarginSeconds;
            public EntryRecord[] completedEntries; public string imageWriteError;
            public EntryRecord[] attemptedEntries; public int attemptedEntryCount, executionCompletedEntryCount, acceptedEntryCount;
            public HitStopRecord hitStop;
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _output = Path.GetFullPath("Builds/ArtReview/kaykit-input/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_output);
            _trace = new Trace { output = _output };
            _withKnife = TestContext.CurrentContext.Test.MethodName.StartsWith("ActualF_StagedThrowStyledTrail", StringComparison.Ordinal) ||
                TestContext.CurrentContext.Test.MethodName == "ActualF_MotionContinuityCancelsExistingOwnerFreezeAndKeepsDistinctTarget" ||
                TestContext.CurrentContext.Test.MethodName == "ActualF_MotionContinuityDispatchesFeedbackAndRestoresAfterFullTail" ||
                TestContext.CurrentContext.Test.MethodName == "ActualF_MotionContinuityRestoresFeedbackOnExactDomainExitFrame" ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_UpperBodyThrowWithVisibleKnifeRecordsHandoff) ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_DomainAlignedUpperBodyThrowWithVisibleKnifeRecordsThreeEntries) ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_DomainAlignedStagedUpperBodyThrowRecordsThreeEntries) ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_StagedThrowEnteredBeforeHitStopKeepsFreezeContract) ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_StagedThrowEnteredDuringHitStopKeepsFreezeContract) ||
                TestContext.CurrentContext.Test.MethodName == nameof(ActualF_StagedThrowEnteredAfterNaturalUnfreezeKeepsColdPhase);
            _stateGuard.UnionWith(ExpectedStates);
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            foreach (var flow in Object.FindObjectsOfType<M2RouteFlowController>()) Assert.That(flow.SavePath, Does.Contain("IsolatedSaves"));
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.Empty, "Run in the test runner's empty scene; never disable an existing user/production Actor.");
            Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            string yaml = File.ReadAllText(ScenePath), actorYaml = Doc(yaml, 114, 1504813786), ccYaml = Doc(yaml, 143, 1504813787), motorYaml = Doc(yaml, 114, 1504813789);
            Assert.That(Doc(yaml, 1, 1504813781), Does.Contain("  m_Name: Player\n"));
            Assert.That(actorYaml, Does.Contain("guid: d759c7efa7d36ac478aac35c21be8487")); Assert.That(motorYaml, Does.Contain("guid: 56467171120e04f49805e300bf0e784d"));
            var sourceSet = Required<PlayerAnimationSet>(AssetPath(Doc(yaml, 114, 1504813788), "_animationSet"));
            var tuning = Required<CombatTuningAsset>(AssetPath(actorYaml, "_tuning")); var actions = Required<InputActionAsset>(AssetPath(Doc(yaml, 114, 1504813782), "_inputActions"));
            _expectedTuning = tuning.CreateRuntimeCopy();
            var paths = new[] { Prefab, Native, ScenePath, AssetDatabase.GetAssetPath(sourceSet), AssetDatabase.GetAssetPath(sourceSet.Controller), AssetDatabase.GetAssetPath(tuning), AssetDatabase.GetAssetPath(actions) }
                .Concat(Directory.GetFiles(Motion, "*.anim")).Concat(_withKnife ? new[] { AssetPath(Doc(yaml, 114, 1504813792), "_projectilePrefab") } : Array.Empty<string>()).SelectMany(p => AssetDatabase.GetDependencies(p, true).Concat(new[] { p })).Distinct().ToArray();
            foreach (string p in paths.SelectMany(p => new[] { p, p + ".meta" }).Distinct().Where(File.Exists)) _disk.Add(p, Hash(p));
            // Scene bytes remain frozen above; scenes are not ordinary LoadAllAssetsAtPath sources.
            foreach (Object o in paths.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !p.EndsWith(".unity", StringComparison.Ordinal)).SelectMany(AssetDatabase.LoadAllAssetsAtPath).Distinct().Where(o => o != null && EditorUtility.IsPersistent(o))) _memory.Add(o, EditorJsonUtility.ToJson(o));
            _trace.sourceActor = actorYaml + ccYaml + motorYaml + Doc(yaml, 4, 162416934) + Doc(yaml, 4, 73622325); _trace.sourceController = AssetDatabase.GetAssetPath(sourceSet.Controller);
            _previousSettings = InputSystem.settings; Assert.That(_previousSettings, Is.Not.Null); _cursorLock = Cursor.lockState; _cursorVisible = Cursor.visible; _globalsCaptured = true;
            _trace.globalsCaptured = true; _trace.inputSettingsBeforeId = _previousSettings.GetInstanceID(); _trace.cursorLockBefore = _cursorLock.ToString(); _trace.cursorVisibleBefore = _cursorVisible;
            var settings = Object.Instantiate(_previousSettings); _ownedAssets.Add(settings); settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = settings;
            _mouse = InputSystem.AddDevice<Mouse>("KnightCandidateMouse"); _keyboard = InputSystem.AddDevice<Keyboard>("KnightCandidateKeyboard"); _scene = SceneManager.CreateScene("KnightCandidate_" + Guid.NewGuid().ToString("N"));
            var root = Own("Review_Knight_InputActor"); root.SetActive(false); root.layer = (int)Number(Doc(yaml, 1, 1504813781), "m_Layer"); Vector3 sourcePosition = Vector(Doc(yaml, 4, 1504813783), "m_LocalPosition"); root.transform.position = new Vector3(500, sourcePosition.y, 500);
            var cc = root.AddComponent<CharacterController>(); cc.height = Number(ccYaml, "m_Height"); cc.radius = Number(ccYaml, "m_Radius"); cc.center = Vector(ccYaml, "m_Center"); cc.slopeLimit = Number(ccYaml, "m_SlopeLimit"); cc.stepOffset = Number(ccYaml, "m_StepOffset"); cc.skinWidth = Number(ccYaml, "m_SkinWidth"); cc.minMoveDistance = Number(ccYaml, "m_MinMoveDistance");
            var input = root.AddComponent<PlayerInputReader>(); input.Configure(actions); SetBool(input, "_lockCursorOnEnable", false);
            _reader = input;
            var visual = Object.Instantiate(Required<GameObject>(Prefab), root.transform); Assert.That(visual.transform.localScale, Is.EqualTo(Vector3.one * .75f));
            // Native feet are zero-based; anchor to the authored capsule foot, never fit/lift from floor minima.
            visual.transform.localPosition = cc.center - Vector3.up * (cc.height * .5f); visual.transform.localRotation = Quaternion.identity;
            _animator = visual.GetComponentInChildren<Animator>(true); Assert.That(AssetDatabase.GetAssetPath(_animator.avatar), Is.EqualTo(Native)); Assert.That(_animator.avatar.isHuman && _animator.avatar.isValid, Is.True);
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(m => { var copy = new Material(m); _ownedAssets.Add(copy); return copy; }).ToArray();
            _anchor = _animator.transform.localPosition; _anchorRotation = _animator.transform.localRotation; _hips = _animator.GetBoneTransform(HumanBodyBones.Hips); _chest = _animator.GetBoneTransform(HumanBodyBones.Chest); Assert.That(_hips != null && _chest != null, Is.True);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head); _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand); _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand); _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Assert.That(new[] { _head, _rightHand, _leftHand, _rightUpperArm }.All(t => t != null), Is.True); _trace.rightShoulderBoneMapped = _animator.GetBoneTransform(HumanBodyBones.RightShoulder) != null;
            var attack = OwnChild(root, "AttackOrigin", Vector(Doc(yaml, 4, 162416934), "m_LocalPosition")); var aim = OwnChild(root, "AimPoint", Vector(Doc(yaml, 4, 73622325), "m_LocalPosition"));
            _actor = root.AddComponent<PlayerCombatActor>(); _actor.Configure(input, tuning, attack, aim, cc, null); CopyFloats(_actor, actorYaml, "_attackRadius", "_attackAngle", "_armor", "_respawnDelay");
            var frame = Own("Review_InputFrame"); var targeting = root.AddComponent<LockOnTargeting>(); targeting.Configure(input, frame.transform);
            var motor = root.AddComponent<ThirdPersonMotor>(); motor.Configure(input, _actor, targeting, frame.transform, cc); CopyFloats(motor, motorYaml, "_moveSpeed", "_sprintSpeed", "_acceleration", "_deceleration", "_rotationSpeed", "_reversalRotationSpeed", "_combatRotationSpeed", "_gravity");
            if (_withKnife)
            {
                string launcherYaml = Doc(yaml, 114, 1504813792);
                _launcher = root.AddComponent<PlayerThrowingKnifeLauncher>(); _launcher.Configure(_actor, targeting, OwnChild(root, "LaunchOrigin", Vector(Doc(yaml, 4, 1835574816), "m_LocalPosition")), Required<GameObject>(AssetPath(launcherYaml, "_projectilePrefab")));
                CopyFloats(_launcher, launcherYaml, "_unlockedAimAssistRadius"); var launchSo = new SerializedObject(_launcher); launchSo.FindProperty("_prewarmCount").intValue = (int)Number(launcherYaml, "_prewarmCount"); launchSo.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(_launcher.PresentationGrip, Is.Null, "Never borrow the Ranger grip for this new rig.");
                var camera = Own("Review_Knight_Camera").AddComponent<Camera>(); camera.transform.position = root.transform.position + new Vector3(3.2f, 1.5f, 5); camera.transform.LookAt(root.transform.position + Vector3.up * -.05f); camera.depth = 10; camera.fieldOfView = 32; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .20f);
            }
            var set = Object.Instantiate(sourceSet); _ownedAssets.Add(set); var controller = new AnimatorOverrideController(sourceSet.Controller); _ownedAssets.Add(controller);
            _candidateController = controller; _candidateSet = set; _originalRangedKey = sourceSet.GetOfflineClip(CombatState.RangedAttack);
            var native = AssetDatabase.LoadAllAssetsAtPath(Native).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToDictionary(c => c.name);
            _throwClip = native["Throw"]; _healClip = native["Use_Item"];
            foreach (string name in new[] { "Idle", "Walking_A", "Running_A" }) _locomotion.Add(native[name]);
            var clips = new[] { Required<AnimationClip>(Motion + "A_Review_Knight_Light1.anim"), Required<AnimationClip>(Motion + "A_Review_Knight_Light1Recovery.anim"), Required<AnimationClip>(Motion + "A_Review_Knight_Light2.anim"), Required<AnimationClip>(Motion + "A_Review_Knight_Light2Recovery.anim"), native["1H_Melee_Attack_Slice_Horizontal"] };
            var replacement = new Dictionary<AnimationClip, AnimationClip> { [sourceSet.GetClip(CombatState.LightAttack1)] = clips[0], [sourceSet.GetRecoveryClip(CombatState.LightAttack1)] = clips[1], [sourceSet.GetClip(CombatState.LightAttack2)] = clips[2], [sourceSet.GetRecoveryClip(CombatState.LightAttack2)] = clips[3], [sourceSet.GetClip(CombatState.LightAttack3)] = clips[4] };
            replacement.Add(sourceSet.GetOfflineClip(CombatState.RangedAttack), _throwClip); replacement.Add(sourceSet.GetClip(CombatState.Heal), _healClip);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); controller.GetOverrides(pairs); var mapped = new HashSet<AnimationClip>();
            for (int i = 0; i < pairs.Count; i++) { AnimationClip key = pairs[i].Key, value; if (!replacement.TryGetValue(key, out value)) { string n = key.name == "Rig|Idle_Loop" ? "Idle" : key.name == "Rig|Walk_Loop" ? "Walking_A" : key.name == "Rig|Sprint_Loop" ? "Running_A" : null; if (n == null) continue; value = native[n]; } pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, value); mapped.Add(key); if (value != _throwClip && value != _healClip) _allowed.Add(value); }
            Assert.That(mapped.Count, Is.EqualTo(10), "The original eight keys plus exact offline ranged and Heal identities must be replaced."); foreach (var pair in replacement) Assert.That(pairs.Single(p => p.Key == pair.Key).Value, Is.SameAs(pair.Value)); controller.ApplyOverrides(pairs);
            var so = new SerializedObject(set); so.FindProperty("_controller").objectReferenceValue = controller; string[] fields = { "_lightAttack1", "_lightAttack1Recovery", "_lightAttack2", "_lightAttack2Recovery", "_lightAttack3" }; for (int i = 0; i < fields.Length; i++) so.FindProperty(fields[i]).objectReferenceValue = clips[i]; so.FindProperty("_offlineRangedAttack").objectReferenceValue = _throwClip; so.FindProperty("_heal").objectReferenceValue = _healClip; so.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<PlayerAnimationPresenter>().Configure(_animator, _actor, motor, set); AnimatorSpeedCoordinator.For(_animator);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, _scene); floor.name = "Review_VisibleGround"; floor.transform.position = new Vector3(500, -.1f, 500); floor.transform.localScale = new Vector3(30, .2f, 30);
            root.SetActive(true); _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var runtimeActions = (InputActionAsset)typeof(PlayerInputReader).GetField("_runtimeActions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input); runtimeActions.devices = new InputDevice[] { _mouse, _keyboard };
            foreach (string name in new[] { "LightAttack", "RangedAttack", "Heal" }) runtimeActions.FindAction("Player/" + name, true).performed += OnInputPerformed;
            for (int i = 0; i < 20; i++) yield return null;
            if (_withKnife)
            {
                var pool = ((Queue<PlayerThrowingKnifeProjectile>)typeof(PlayerThrowingKnifeLauncher).GetField("_available", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_launcher)).ToArray(); Assert.That(pool.Length, Is.EqualTo(4));
                var grip = _animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Review_1H_Sword"); _knife = root.AddComponent<PlayerKnifePresentation>(); _knife.Initialize(_actor, _launcher, grip, pool);
                _trace.knifeScope = "Actual original offline Launcher/Presentation, own Knight sword anchor, no borrowed finger/palm Grip. Real PlayerLoop; visible handoff/floor/lease lifecycle only, no hit target/network/contact acceptance.";
                _trace.scope = "Disposable original Actor/capsule and Knight Avatar, actual InputSystem/Actor/Motor/Presenter plus original Launcher/KnifePresentation in real PlayerLoop. Test-only cloned InputSettings ignore focus; NOT OS input/foreground/contact/HitStop/production/network acceptance.";
                _trace.abilityScope = "Original .22/.56 F/domain timing, new owned clip at 1x, complete Idle fade, visible knife same-frame lease and lifetime/floor. Sword-anchor fallback only, no borrowed Ranger Grip/finger contact; fullAbilityAccepted remains false.";
            }
            Assert.That(_actor.Model, Is.Not.Null); Assert.That(root.GetComponent<PlayerAnimationPresenter>().IsConfigured, Is.True); _trace.status = "recording";
        }

        [UnityTest] public IEnumerator ActualThreeClickCombo_UsesKnightAndExistingBlends() => Combo(false);
        [UnityTest] public IEnumerator SettledActualThreeClickCombo_UsesKnightAndExistingBlends() => Combo(true);
        [UnityTest] public IEnumerator ActualF_NativeThrowRecordsDomainReleaseAndFloor() => Ability(false);
        [UnityTest] public IEnumerator ActualR_NativeUseItemRecordsDomainResolveAndFloor() => Ability(true);
        [UnityTest] public IEnumerator ActualF_UpperBodyThrowRecordsDomainReleaseAndFloor()
        { PrepareUpperBodyCandidate(); yield return Ability(false); }
        [UnityTest] public IEnumerator ActualF_UpperBodyThrowWithVisibleKnifeRecordsHandoff()
        { PrepareUpperBodyCandidate(); yield return VisibleKnifeAbility(); }
        [UnityTest] public IEnumerator ActualF_DomainAlignedUpperBodyThrowWithVisibleKnifeRecordsThreeEntries()
        {
            PrepareUpperBodyCandidate(); _candidateSet.ConfigureOfflineRangedEntryTime(true); _domainEntrySync = true;
            _trace.domainEntrySyncEnabled = true;
            for (int entry = 1; entry <= 3; entry++)
            {
                _completed = false;
                for (int frame = 0; frame < 5000 && _actor.Model.RangedCooldownRemaining > 0f; frame++) yield return null;
                Assert.That(_actor.Model.CanUseRangedAttack, Is.True, "Repeat only after the original domain cooldown; no resets or manual Tick.");
                _entryNumber = entry; _completed = false; _rows.Clear(); _events.Clear(); _inputs.Clear(); _seen.Clear(); _seenStates.Clear(); _images.Clear(); _capturedImages.Clear();
                _trace.knifeHandoffPassed = _trace.knifeLifetimeCompleted = _trace.bridgeSamplingComplete = _trace.entryClockGateEvaluated = _trace.entryClockGatePassed = false;
                _trace.maximumEntryClockError = _trace.maximumBridgeSampleGap = _trace.forwardSampleMarginSeconds = 0f; _trace.bridgeInteriorSamples = 0;
                yield return VisibleKnifeAbility();
                _entries.Add(new EntryRecord { index = entry, rows = _rows.ToArray(), events = _events.ToArray(), inputs = _inputs.ToArray(), images = _images.ToArray(), bodyMin = _trace.minimumBody, capeMin = _trace.minimumCape, gearMin = _trace.minimumGear, stableIdleSeconds = _trace.stableIdleSeconds, maximumClockError = _trace.maximumEntryClockError, forwardSampleMarginSeconds = _trace.forwardSampleMarginSeconds, handoffPassed = _trace.knifeHandoffPassed, lifetimeCompleted = _trace.knifeLifetimeCompleted, bridgeSamplingComplete = _trace.bridgeSamplingComplete, maximumBridgeSampleGap = _trace.maximumBridgeSampleGap });
            }
            Assert.That(_entries.Count, Is.EqualTo(3)); _completed = true;
        }
        [UnityTest] public IEnumerator ActualF_DomainAlignedStagedUpperBodyThrowRecordsThreeEntries()
        {
            PrepareUpperBodyCandidate(StagedUpperThrow); _candidateSet.ConfigureOfflineRangedEntryTime(true); _domainEntrySync = true;
            _trace.domainEntrySyncEnabled = true; _collectEntryDiagnostics = true;
            for (int entry = 1; entry <= 3; entry++)
            {
                _completed = false; VerifyEntrySafety();
                for (int frame = 0; frame < 5000 && (_actor.Model.RangedCooldownRemaining > 0f || HasKnifeTail()); frame++) yield return null;
                Assert.That(_actor.Model.CanUseRangedAttack, Is.True, "Repeat only after the original domain cooldown; no resets or manual Tick.");
                Assert.That(HasKnifeTail(), Is.False, "Never start another entry while the previous real lease/trail is outstanding.");
                Assert.That(_knife.ActiveVisualCount, Is.Zero); Assert.That(_knife.SwordVisible, Is.True);
                _entryNumber = entry; _rows.Clear(); _events.Clear(); _inputs.Clear(); _seen.Clear(); _seenStates.Clear(); _images.Clear(); _entryFailures.Clear();
                Assert.That(_capturedImages, Is.Empty, "Previous entry textures must be encoded and destroyed, not silently cleared.");
                _trace.knifeHandoffPassed = _trace.knifeLifetimeCompleted = _trace.bridgeSamplingComplete = _trace.entryClockGateEvaluated = _trace.entryClockGatePassed = false;
                _trace.maximumEntryClockError = _trace.maximumBridgeSampleGap = _trace.forwardSampleMarginSeconds = 0f; _trace.bridgeInteriorSamples = 0;
                _trace.imageWriteError = null; _trace.visibleKnife = "not-measured";
                var record = new EntryRecord { index = entry, attempted = true, status = "running", candidate = StagedUpperThrow, sourceActor = _trace.sourceActor, sourceController = _trace.sourceController, scope = _trace.scope, abilityScope = _trace.abilityScope, knifeScope = _trace.knifeScope, sourcesVerifiedBefore = true };
                _attemptedEntries.Add(record);
                try
                {
                    _actor.RangedAttackReleased -= OnRangedReleased;
                    // Recursively advance the original nested coroutine. Only its explicit terminal signed-floor assertions may be retained and continued.
                    yield return CaptureDiagnostic(Ability(false), "ability-signed-floor", true); _completed = false;
                    yield return CaptureDiagnostic(DiagnosticClock(), "clock");
                    record.phaseGateEvaluated = true;
                    yield return CaptureDiagnostic(DiagnosticHandoff(), "handoff-phase");
                    for (int frame = 0; frame < 5000 && HasKnifeTail(); frame++)
                    { yield return null; yield return new WaitForEndOfFrame(); Observe(Vector2.Distance(PlanarHips(), new Vector2(_rows[0].hips.x - _rows[0].root.x, _rows[0].hips.z - _rows[0].root.z))); }
                    yield return CaptureDiagnostic(DiagnosticLifetime(), "lifetime-and-floor");
                    DiagnosticBridge(); // Raw sampling assessment only; no new acceptance assertion.
                    SaveCapturedImages(); yield return CaptureDiagnostic(DiagnosticImages(), "images");
                    VerifyEntrySafety(); record.sourcesVerifiedAfter = true; record.executionCompleted = true;
                }
                finally
                {
                    // Also retain partial evidence on an ordinary exception or safety failure; neither is swallowed or followed by another entry.
                    SaveCapturedImages(); record.rows = _rows.ToArray(); record.events = _events.ToArray(); record.inputs = _inputs.ToArray(); record.images = _images.ToArray(); record.failures = _entryFailures.ToArray(); record.seenStates = _seenStates.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    record.candidate = _throwClip != null ? AssetDatabase.GetAssetPath(_throwClip) + "::" + _throwClip.name : "DESTROYED";
                    record.minimumFloorsMeasured = _rows.Count != 0;
                    if (record.minimumFloorsMeasured) { record.bodyMin = _rows.Min(r => r.bodyMin); record.capeMin = _rows.Min(r => r.capeMin); record.gearMin = _rows.Min(r => r.gearMin); }
                    record.stableIdleSeconds = _trace.stableIdleSeconds;
                    record.maximumClockError = _trace.maximumEntryClockError; record.forwardSampleMarginSeconds = _trace.forwardSampleMarginSeconds; record.clockGateEvaluated = _trace.entryClockGateEvaluated; record.clockGatePassed = _trace.entryClockGatePassed;
                    record.abilityEvidenceComplete = _trace.abilityEvidenceComplete; record.floorGateEvaluated = _trace.floorGateEvaluated;
                    record.bodyFloorPassed = record.floorGateEvaluated && record.minimumFloorsMeasured && record.bodyMin >= 0f; record.capeFloorPassed = record.floorGateEvaluated && record.minimumFloorsMeasured && record.capeMin >= 0f; record.gearFloorPassed = record.floorGateEvaluated && record.minimumFloorsMeasured && record.gearMin >= 0f;
                    record.handoffPassed = _trace.knifeHandoffPassed; record.lifetimeCompleted = _trace.knifeLifetimeCompleted; record.bridgeSamplingComplete = _trace.bridgeSamplingComplete; record.maximumBridgeSampleGap = _trace.maximumBridgeSampleGap; record.bridgeInteriorSamples = _trace.bridgeInteriorSamples;
                    record.finalProjectiles = _launcher != null ? _launcher.ActiveProjectileCount : -1; record.finalVisuals = _knife != null ? _knife.ActiveVisualCount : -1; record.finalTrailPoints = _knife != null ? _knife.TrailPointCount : -1; record.finalSwordVisible = _knife != null && _knife.SwordVisible;
                    record.accepted = record.executionCompleted && record.failures.Length == 0; record.status = !record.executionCompleted ? "aborted-safety-or-exception" : record.accepted ? "passed" : "diagnostic-failed";
                    if (!record.executionCompleted) record.abortReason = "Uncaught safety assertion or ordinary exception; retained native NUnit result is authoritative. No subsequent entry is allowed.";
                    try { string path = _output + "/entry-" + entry + "-actual-input.json"; Assert.That(File.Exists(path), Is.False, "Never overwrite an entry record."); File.WriteAllText(path, JsonUtility.ToJson(record, true)); }
                    finally { foreach (var image in _capturedImages.Values) { _ownedAssets.Remove(image); if (image != null) Object.Destroy(image); } _capturedImages.Clear(); _atSignedFloor = false; }
                }
                if (record.accepted) _entries.Add(record);
            }
            Assert.That(_attemptedEntries.Count, Is.EqualTo(3)); _completed = _attemptedEntries.All(e => e.accepted);
            Assert.That(_completed, Is.True, string.Join("\n", _attemptedEntries.SelectMany(e => e.failures.Select(f => "Entry " + e.index + " / " + f.gate + ": " + f.message))));
        }
        [UnityTest] public IEnumerator ActualF_StagedThrowEnteredBeforeHitStopKeepsFreezeContract()
            => HitStopBoundaryCase(FreezeBoundary.EntryBeforeFreeze);
        [UnityTest] public IEnumerator ActualF_StagedThrowEnteredDuringHitStopKeepsFreezeContract()
            => HitStopBoundaryCase(FreezeBoundary.EntryDuringFreeze);
        [UnityTest] public IEnumerator ActualF_StagedThrowEnteredAfterNaturalUnfreezeKeepsColdPhase()
            => HitStopBoundaryCase(FreezeBoundary.EntryAfterUnfreeze);

        IEnumerator HitStopBoundaryCase(FreezeBoundary boundary)
        {
            PrepareUpperBodyCandidate(StagedUpperThrow); _candidateSet.ConfigureOfflineRangedEntryTime(true);
            _stateGuard.Clear(); _stateGuard.UnionWith(new[] { "Locomotion", "PlayerRangedAttack" });
            _allowed.Clear(); _allowed.UnionWith(_locomotion); _allowed.Add(_throwClip);
            _hitStop = new HitStopRecord { boundary = boundary.ToString(), status = "recording", candidate = StagedUpperThrow,
                fullAbilityAccepted = false,
                coldPhaseApplicable = boundary == FreezeBoundary.EntryAfterUnfreeze,
                phaseScope = boundary == FreezeBoundary.EntryAfterUnfreeze
                    ? "No post-entry freeze debt: original two-increment/positive-velocity cold hand phase is independently required."
                    : "Release-frame hand differences are measured, never used to award the cold phase gate or full F acceptance. Frozen-contract success does not waive later ability-phase acceptance." };
            _trace.hitStop = _hitStop; _trace.scope = _hitStop.scope; _trace.abilityScope = _hitStop.clockScope;
            _trace.ability = "F/HitStop/" + boundary; _trace.candidate = StagedUpperThrow; _trace.candidateLength = _throwClip.length;
            _trace.expectedReleaseTime = _expectedTuning.RangedReleaseTime; _trace.expectedDuration = _expectedTuning.RangedDuration;
            _trace.fullAbilityAccepted = false; _trace.domainEntrySyncEnabled = true;
            foreach (string path in new[] {
                "Assets/_Game/Scripts/Gameplay/Combat/Domain/CombatStateMachine.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/PlayerCombatActor.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs",
                "Assets/_Game/Scripts/Gameplay/Animation/AnimatorSpeedCoordinator.cs",
                "Assets/_Game/Scripts/Gameplay/Combat/Unity/HitFeedbackRules.cs" })
                foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists))
                    if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
            _hitStop.stateElapsedEvidence = "CombatStateMachine.EnterState sets StateElapsed=0; Actor consumes F then calls its sole normal Tick(Time.deltaTime). Verify first entry elapsed==that frame delta and returned Locomotion elapsed==0. Domain source SHA256="
                + Hash("Assets/_Game/Scripts/Gameplay/Combat/Domain/CombatStateMachine.cs");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_throwClip, out string guid, out long localId), Is.True);
            _hitStop.clipGuid = guid; _hitStop.clipLocalId = localId;
            _freezeSpeed = AnimatorSpeedCoordinator.For(_animator); _freezePresenter = _actor.GetComponent<PlayerAnimationPresenter>();
            _freezeSequenceField = typeof(AnimatorSpeedCoordinator).GetField("_sequence", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_freezeSequenceField, Is.Not.Null); Assert.That(Time.timeScale, Is.EqualTo(1f));
            var originalFade = typeof(PlayerAnimationPresenter).GetField("CrossFadeSeconds", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(originalFade, Is.Not.Null, "Check the actual Presenter constant before input, not a measured/fitted replacement duration.");
            Assert.That((float)originalFade.GetRawConstantValue(), Is.EqualTo(_hitStop.declaredFadeSeconds));
            _freezeEnded = (grade, milliseconds) => RecordFreezeEvent("ended", grade, _hitStop.requestSequence, true, milliseconds);
            _freezeSpeed.FreezeEnded += _freezeEnded;
            try
            {
                VerifyEntrySafety(); _hitStop.sourcesVerifiedBefore = true;
                yield return new WaitForSeconds(.5f);
                Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion));
                Assert.That(_animator.IsInTransition(0), Is.False); Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
                Assert.That(_freezeSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None));
                _freezeBefore = _actor.gameObject.AddComponent<KnightHitStopBeforePresenterObserver>(); _freezeBefore.Sample = ObserveBeforePresenter;
                _freezeAfter = _actor.gameObject.AddComponent<KnightHitStopAfterCoordinatorObserver>(); _freezeAfter.Sample = ObserveAfterCoordinator;
                _trace.sequenceBefore = _actor.Model.RangedReleaseSequence; _trace.healthBefore = _actor.Model.Health.Current;
                _trace.chargesBefore = _actor.Model.HealingFlasks.CurrentCharges;
                _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation;
                _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
                Vector2 baseline = PlanarHips(); Vector3 root = _actor.transform.position;
                yield return new WaitForEndOfFrame(); ObserveFreezeEndOfFrame(baseline);
                if (boundary != FreezeBoundary.EntryBeforeFreeze)
                {
                    RequestBoundaryFreeze(boundary);
                    if (boundary == FreezeBoundary.EntryAfterUnfreeze)
                    {
                        for (int frame = 0; frame < 5000 && _freezeSpeed.ActiveGrade != HitFeedbackGrade.None; frame++)
                        { yield return null; yield return new WaitForEndOfFrame(); CheckFreezeSafety(root); ObserveFreezeEndOfFrame(baseline); }
                        Assert.That(_freezeSpeed.ActiveGrade, Is.EqualTo(HitFeedbackGrade.None), "Natural-unfreeze prerequisite was not covered; never relabel a still-frozen F as after-unfreeze.");
                        Assert.That(_animator.speed, Is.EqualTo(_freezeSpeed.BaseSpeed).Within(FreezeClockTolerance));
                    }
                }
                _hitStop.idleElapsedBeforeInput = _actor.Model.StateElapsed; _trace.inputQueuedAt = Time.timeAsDouble;
                _actor.RangedAttackReleased -= OnRangedReleased; _actor.RangedAttackReleased += OnRangedReleased;
                RecordInput("queued", "RangedAttack"); Assert.That(_keyboard.enabled, Is.True);
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.F)); yield return null;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                double stableStarted = double.NaN;
                for (int frame = 0; frame < 5000; frame++)
                {
                    yield return new WaitForEndOfFrame(); CheckFreezeSafety(root); ObserveFreezeEndOfFrame(baseline);
                    Row row = _rows[_rows.Count - 1];
                    if (!_hitStop.entered && row.domain == CombatState.RangedAttack.ToString())
                    { _hitStop.entered = true; _trace.domainFirstObservedElapsed = row.elapsed; _trace.domainFirstObservedAt = row.time; }
                    if (boundary == FreezeBoundary.EntryBeforeFreeze && _hitStop.requestCount == 0 &&
                        row.domain == CombatState.RangedAttack.ToString() &&
                        (row.currentState == "PlayerRangedAttack" || row.nextState == "PlayerRangedAttack"))
                        RequestBoundaryFreeze(boundary); // AFTER this actual EOF pose, not before Animator evaluation.
                    if (_hitStop.entered && !_hitStop.domainReturned && row.domain == CombatState.Locomotion.ToString())
                    { _hitStop.domainReturned = true; _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = row.time; }
                    if (_hitStop.domainReturned && row.stableLocomotion)
                    {
                        if (double.IsNaN(stableStarted))
                        { stableStarted = row.time; _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = row.time; }
                        _trace.stableIdleSeconds = (float)(row.time - stableStarted);
                        if (_trace.stableIdleSeconds >= .15f)
                        { _hitStop.settled = true; _trace.stableIdleCompleted = true; _trace.stableIdleEndedAt = row.time; }
                    }
                    else if (!double.IsNaN(stableStarted))
                    { _hitStop.stableContinuity = false; stableStarted = double.NaN; _hitStop.settled = false; _trace.stableIdleCompleted = false; }
                    if (_hitStop.entered && _hitStop.domainReturned && _hitStop.settled && !HasKnifeTail() &&
                        _freezeSpeed.ActiveGrade == HitFeedbackGrade.None) break;
                    yield return null;
                }
                // Expected diagnostic failures are evaluated only after the entire real tail; safety/ordinary exceptions above stop immediately.
                _hitStop.executionCompleted = _hitStop.entered && _hitStop.domainReturned && _hitStop.settled &&
                    !HasKnifeTail() && _freezeSpeed.ActiveGrade == HitFeedbackGrade.None;
                _trace.abilityEvidenceComplete = _hitStop.entered && _hitStop.domainReturned && _hitStop.settled;
                EvaluateHitStopGates(boundary); VerifyEntrySafety(); _hitStop.sourcesVerifiedAfter = true;
                SaveCapturedImages(); yield return CaptureFreezeDiagnostic(DiagnosticImages(), "images");
                _hitStop.freezeContractPassed = _hitStop.executionCompleted && _freezeFailures.Count == 0;
                _hitStop.status = _hitStop.freezeContractPassed ? "freeze-contract-passed-not-full-F" : "diagnostic-failed";
                _completed = _hitStop.freezeContractPassed;
                Assert.That(_completed, Is.True, string.Join("\n", _freezeFailures.Select(f => f.gate + ": " + f.message)));
            }
            finally
            {
                if (_freezeBefore != null) _freezeBefore.Sample = null;
                if (_freezeAfter != null) _freezeAfter.Sample = null;
                if (_freezeSpeed != null && _freezeEnded != null) _freezeSpeed.FreezeEnded -= _freezeEnded;
                _hitStop.loops = _freezeLoops.ToArray(); _hitStop.freezeEvents = _freezeEvents.ToArray(); _hitStop.failures = _freezeFailures.ToArray();
                if (!_hitStop.executionCompleted)
                { _hitStop.status = "aborted-or-uncovered"; _hitStop.abortReason = "Raw native NUnit result is authoritative; no manual recovery, freeze extension or subsequent attempt."; }
            }
        }

        void ObserveBeforePresenter()
        {
            var sourceInfo = _animator.GetCurrentAnimatorStateInfo(0);
            var row = new FreezeLoopRow { frame = Time.frameCount, beforeCaptured = true, beforeRealtime = Time.realtimeSinceStartupAsDouble,
                beforeDomain = _actor.Model.State.ToString(), beforePresented = _freezePresenter.PresentedState.ToString(),
                beforeElapsed = _actor.Model.StateElapsed, beforeDelta = Time.deltaTime, beforeSpeed = _animator.speed,
                beforeBase = _freezeSpeed.BaseSpeed, beforeGrade = _freezeSpeed.ActiveGrade.ToString(), beforeSequence = ReadFreezeSequence(),
                sourceFullHash = sourceInfo.fullPathHash, sourceShortHash = sourceInfo.shortNameHash,
                sourceStateLength = sourceInfo.length, sourceStateSpeed = sourceInfo.speed, sourceStateSpeedMultiplier = sourceInfo.speedMultiplier,
                sourceWasTransitioning = _animator.IsInTransition(0) };
            row.entry = _actor.Model.State == CombatState.RangedAttack && _freezePresenter.PresentedState != CombatState.RangedAttack;
            row.locomotionEntry = _actor.Model.State == CombatState.Locomotion && _freezePresenter.PresentedState == CombatState.RangedAttack;
            if (row.entry && !_freezeEntryObserved)
            {
                _freezeEntryObserved = true; _hitStop.entryFrame = row.frame; _hitStop.entryElapsed = row.beforeElapsed; _hitStop.entryDelta = row.beforeDelta;
                _hitStop.actualEntryDuration = _actor.Model.StateDuration;
                float basePlayback = Mathf.Clamp(_throwClip.length / _actor.Model.StateDuration, .35f, 3f);
                row.expectedEntryTime = _candidateSet.GetOfflineEntryTimeOffset(CombatState.RangedAttack, row.beforeElapsed, basePlayback);
                _hitStop.expectedEntryTime = row.expectedEntryTime; _expectedFreezeClipTime = row.expectedEntryTime;
            }
            _freezeLoops.Add(row);
        }
        void ObserveAfterCoordinator()
        {
            var row = _freezeLoops.LastOrDefault(r => r.frame == Time.frameCount);
            if (row == null) { row = new FreezeLoopRow { frame = Time.frameCount }; _freezeLoops.Add(row); }
            row.afterCaptured = true; row.afterRealtime = Time.realtimeSinceStartupAsDouble;
            row.afterDomain = _actor.Model.State.ToString(); row.afterPresented = _freezePresenter.PresentedState.ToString();
            row.afterElapsed = _actor.Model.StateElapsed; row.afterDelta = Time.deltaTime; row.afterSpeed = _animator.speed;
            row.afterBase = _freezeSpeed.BaseSpeed; row.afterGrade = _freezeSpeed.ActiveGrade.ToString(); row.afterSequence = ReadFreezeSequence();
            if (_freezeEntryObserved && _actor.Model.State == CombatState.RangedAttack && row.frame != _hitStop.entryFrame)
                _expectedFreezeClipTime += (double)row.afterDelta * row.afterSpeed;
            row.expectedClipTime = _expectedFreezeClipTime;
        }
        void ObserveFreezeEndOfFrame(Vector2 baseline)
        {
            Observe(Vector2.Distance(baseline, PlanarHips()));
            _trace.peak = Mathf.Max(_trace.peak, _rows[_rows.Count - 1].hipDrift); _trace.final = _rows[_rows.Count - 1].hipDrift;
            var loop = _freezeLoops.LastOrDefault(r => r.frame == Time.frameCount); if (loop == null) return;
            Row row = _rows[_rows.Count - 1]; loop.eof = row;
            if (row.transition)
            {
                var transition = _animator.GetAnimatorTransitionInfo(0); loop.transitionInfoMeasured = true;
                loop.transitionDuration = transition.duration; loop.transitionDurationUnit = transition.durationUnit.ToString();
                loop.transitionNormalized = transition.normalizedTime;
            }
            bool current = row.currentState == "PlayerRangedAttack", next = row.nextState == "PlayerRangedAttack";
            loop.destinationObserved = current || next;
            if (!loop.destinationObserved) return;
            loop.observedClipTime = (current ? row.currentNormalized : row.nextNormalized) * _throwClip.length;
            loop.destinationStateSpeed = (current ? _animator.GetCurrentAnimatorStateInfo(0) : _animator.GetNextAnimatorStateInfo(0)).speed;
            if (row.domain != CombatState.RangedAttack.ToString()) return;
            loop.clockError = Mathf.Abs(loop.observedClipTime - (float)loop.expectedClipTime);
            _hitStop.freezeAwareClockMeasured = true; _hitStop.maximumClockError = Mathf.Max(_hitStop.maximumClockError, loop.clockError);
            if (loop.frame == _hitStop.entryFrame)
            { _hitStop.entryOffsetMeasured = true; _hitStop.observedEntryTime = loop.observedClipTime; }
            var before = _freezeLoops.LastOrDefault(r => r.frame < loop.frame && r.eof != null && r.destinationObserved &&
                r.eof.domain == CombatState.RangedAttack.ToString());
            if (before != null)
            {
                float expected = loop.afterDelta * loop.afterSpeed, observed = loop.observedClipTime - before.observedClipTime;
                loop.incrementError = Mathf.Abs(observed - expected); _hitStop.maximumIncrementError = Mathf.Max(_hitStop.maximumIncrementError, loop.incrementError);
                if (before.afterSpeed == 0f && loop.afterSpeed > 0f)
                { _hitStop.unfreezeFrames++; _hitStop.unfreezeIncrement = observed; _hitStop.expectedUnfreezeIncrement = expected; }
            }
        }
        ulong ReadFreezeSequence() => (ulong)_freezeSequenceField.GetValue(_freezeSpeed);
        void RequestBoundaryFreeze(FreezeBoundary boundary)
        {
            Assert.That(_hitStop.requestCount, Is.Zero, "Exactly one normal presentation request; no renewals, upgrades or fallback.");
            _hitStop.requestCount++; _hitStop.requestSequence = 70001UL + (ulong)boundary;
            RecordFreezeEvent("requested", HitFeedbackGrade.Heavy, _hitStop.requestSequence, false, 0d);
            bool applied = _freezeSpeed.Request(HitFeedbackGrade.Heavy, _hitStop.requestSequence);
            RecordFreezeEvent("applied", HitFeedbackGrade.Heavy, _hitStop.requestSequence, applied, 0d);
        }
        void RecordFreezeEvent(string kind, HitFeedbackGrade grade, ulong sequence, bool accepted, double milliseconds)
            => _freezeEvents.Add(new FreezeEvent { kind = kind, grade = grade.ToString(), requestedSequence = sequence,
                actualSequence = ReadFreezeSequence(), frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                time = Time.timeAsDouble, effectiveMilliseconds = milliseconds, domain = _actor.Model.State.ToString(),
                elapsed = _actor.Model.StateElapsed, speed = _animator.speed, baseSpeed = _freezeSpeed.BaseSpeed,
                activeGrade = _freezeSpeed.ActiveGrade.ToString(), requestAccepted = accepted });
        void CheckFreezeSafety(Vector3 origin)
        {
            Assert.That(float.IsNaN(Time.deltaTime) || float.IsInfinity(Time.deltaTime) || Time.deltaTime < 0f, Is.False);
            Assert.That(double.IsNaN(Time.timeAsDouble) || double.IsInfinity(Time.timeAsDouble), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f), "Presentation-only freeze must never alter global/domain time.");
            Assert.That(_scene.IsValid() && _scene.isLoaded && _actor != null && _actor.gameObject.scene == _scene, Is.True);
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.EquivalentTo(new[] { _actor }));
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(_candidateController));
            Assert.That(_actor.Model.State == CombatState.Locomotion || _actor.Model.State == CombatState.RangedAttack, Is.True);
            Assert.That(Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(_actor.transform.position.x, _actor.transform.position.z)), Is.LessThan(.01f));
            Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor));
            Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
        }
        IEnumerator CaptureFreezeDiagnostic(IEnumerator routine, string gate)
        {
            while (true)
            {
                bool moved = false, failed = false; object value = null;
                try { moved = routine.MoveNext(); if (moved) value = routine.Current; }
                catch (AssertionException error) { _freezeFailures.Add(new GateFailure { gate = gate, message = error.Message, stackTrace = error.StackTrace }); failed = true; }
                if (failed || !moved) { (routine as IDisposable)?.Dispose(); yield break; } yield return value;
            }
        }
        void FreezeGate(string gate, Action check)
        {
            try { check(); }
            catch (AssertionException error) { _freezeFailures.Add(new GateFailure { gate = gate, message = error.Message, stackTrace = error.StackTrace }); }
        }
        void EvaluateFreezeFade(bool forward, FreezeBoundary boundary)
        {
            string source = forward ? "Locomotion" : "PlayerRangedAttack", target = forward ? "PlayerRangedAttack" : "Locomotion";
            string presented = (forward ? CombatState.RangedAttack : CombatState.Locomotion).ToString();
            var entries = _freezeLoops.Where(r => forward ? r.entry : r.locomotionEntry).ToArray();
            Assert.That(entries.Length, Is.EqualTo(1), "Exactly one genuine Presenter entry per fade direction is required.");
            var entry = entries[0];
            var segment = _freezeLoops.Where(r => r.frame >= entry.frame)
                .TakeWhile(r => !forward || r.beforeDomain == CombatState.RangedAttack.ToString()).ToArray();
            Assert.That(segment, Is.Not.Empty); Assert.That(segment[0], Is.SameAs(entry));
            Assert.That(entry.beforeCaptured && entry.afterCaptured && entry.eof != null, Is.True);
            Assert.That(entry.sourceWasTransitioning, Is.False, "The source must already be settled; a blend interrupted before this entry is not the declared fixture.");
            Assert.That(entry.sourceShortHash, Is.EqualTo(Animator.StringToHash(source)));
            Assert.That(float.IsNaN(entry.sourceStateLength) || float.IsInfinity(entry.sourceStateLength), Is.False);
            Assert.That(entry.sourceStateLength, Is.GreaterThan(0f));
            Assert.That(entry.sourceStateSpeed, Is.EqualTo(1f).Within(.000001f));
            Assert.That(entry.sourceStateSpeedMultiplier, Is.EqualTo(1f).Within(.000001f));
            Assert.That(entry.beforeBase, Is.EqualTo(1f).Within(.000001f));
            Assert.That(entry.afterBase, Is.EqualTo(1f).Within(.000001f));
            Assert.That(entry.eof.transition && entry.transitionInfoMeasured, Is.True, "No adjacent transition samples means uncovered, not an exact .08s pass.");
            string unit = (forward && boundary == FreezeBoundary.EntryDuringFreeze ? DurationUnit.Normalized : DurationUnit.Fixed).ToString();
            float effective = entry.transitionDuration * (unit == DurationUnit.Normalized.ToString() ? entry.sourceStateLength : 1f);
            if (forward)
            {
                _hitStop.forwardFadeEntryFrame = entry.frame; _hitStop.forwardSourceLength = entry.sourceStateLength;
                _hitStop.forwardSourceSpeed = entry.sourceStateSpeed; _hitStop.forwardSourceSpeedMultiplier = entry.sourceStateSpeedMultiplier;
                _hitStop.forwardTransitionDuration = entry.transitionDuration; _hitStop.forwardTransitionUnit = entry.transitionDurationUnit;
                _hitStop.forwardEffectiveFadeSeconds = effective; _hitStop.forwardFadeMeasured = true;
            }
            else
            {
                _hitStop.reverseFadeEntryFrame = entry.frame; _hitStop.reverseSourceLength = entry.sourceStateLength;
                _hitStop.reverseSourceSpeed = entry.sourceStateSpeed; _hitStop.reverseSourceSpeedMultiplier = entry.sourceStateSpeedMultiplier;
                _hitStop.reverseTransitionDuration = entry.transitionDuration; _hitStop.reverseTransitionUnit = entry.transitionDurationUnit;
                _hitStop.reverseEffectiveFadeSeconds = effective; _hitStop.reverseFadeMeasured = true;
            }
            Assert.That(entry.transitionDurationUnit, Is.EqualTo(unit));
            Assert.That(float.IsNaN(effective) || float.IsInfinity(effective), Is.False);
            Assert.That(effective, Is.EqualTo(_hitStop.declaredFadeSeconds).Within(FreezeClockTolerance),
                "Normalize by this entry's observed SOURCE length; using the destination .56 would change the original fade.");
            double initial = (double)entry.transitionNormalized * _hitStop.declaredFadeSeconds;
            if (forward) _hitStop.forwardFadeInitialProgress = initial; else _hitStop.reverseFadeInitialProgress = initial;
            Assert.That(double.IsNaN(initial) || double.IsInfinity(initial), Is.False);
            Assert.That(initial, Is.GreaterThanOrEqualTo(-FreezeClockTolerance));
            Assert.That(initial, Is.LessThanOrEqualTo((double)entry.afterDelta * entry.afterSpeed + FreezeClockTolerance),
                "An initial progress observation may consume at most the real first frame; it cannot conceal a skipped blend.");
            double integral = initial; float maximumError = 0f; FreezeLoopRow previous = null; bool completed = false;
            for (int index = 0; index < segment.Length; index++)
            {
                var row = segment[index];
                Assert.That(row.beforeCaptured && row.afterCaptured && row.eof != null, Is.True);
                Assert.That(row.afterPresented, Is.EqualTo(presented));
                Assert.That(row.afterBase, Is.EqualTo(1f).Within(.000001f));
                Assert.That(float.IsNaN(row.afterDelta) || float.IsInfinity(row.afterDelta), Is.False);
                Assert.That(row.afterDelta, Is.GreaterThanOrEqualTo(0f));
                Assert.That(row.afterSpeed, Is.EqualTo(row.afterGrade == HitFeedbackGrade.None.ToString() ? row.afterBase : 0f).Within(FreezeClockTolerance));
                if (previous != null)
                {
                    Assert.That(row.frame, Is.EqualTo(previous.frame + 1));
                    // The coordinator's actual pre-Animator speed owns this increment.
                    // A natural-unfreeze or completion frame consumes its delta, not zero.
                    integral += (double)row.afterDelta * row.afterSpeed;
                }
                row.fadeDirection = forward ? "forward" : "reverse"; row.fadeUnfrozenSeconds = integral;
                if (row.eof.transition)
                {
                    Assert.That(row.transitionInfoMeasured, Is.True);
                    Assert.That(row.eof.currentState, Is.EqualTo(source)); Assert.That(row.eof.nextState, Is.EqualTo(target));
                    Assert.That(row.transitionDurationUnit, Is.EqualTo(unit));
                    Assert.That(row.transitionDuration, Is.EqualTo(entry.transitionDuration).Within(FreezeClockTolerance));
                    Assert.That(float.IsNaN(row.transitionNormalized) || float.IsInfinity(row.transitionNormalized), Is.False);
                    Assert.That(row.transitionNormalized, Is.GreaterThanOrEqualTo(-FreezeClockTolerance));
                    Assert.That(row.transitionNormalized, Is.LessThanOrEqualTo(1f + FreezeClockTolerance));
                    row.fadeObservedProgressSeconds = (double)row.transitionNormalized * _hitStop.declaredFadeSeconds;
                    row.fadeProgressError = (float)Math.Abs(row.fadeObservedProgressSeconds - integral);
                    maximumError = Mathf.Max(maximumError, row.fadeProgressError);
                    if (forward) _hitStop.forwardFadeMaximumProgressError = maximumError; else _hitStop.reverseFadeMaximumProgressError = maximumError;
                    Assert.That(row.fadeProgressError, Is.LessThanOrEqualTo(FreezeClockTolerance),
                        "Transition progress must match actual unfrozen delta integration, not wall time or a fitted duration.");
                    previous = row; continue;
                }
                Assert.That(previous, Is.Not.Null); Assert.That(previous.eof.transition, Is.True);
                Assert.That(row.eof.currentState, Is.EqualTo(target));
                Assert.That(row.eof.nextState, Is.Empty);
                if (!forward) Assert.That(row.eof.stableLocomotion, Is.True);
                if (forward)
                {
                    _hitStop.forwardFadeLastTransitionFrame = previous.frame; _hitStop.forwardFadeSettledFrame = row.frame;
                    _hitStop.forwardFadeBracketBefore = previous.fadeUnfrozenSeconds; _hitStop.forwardFadeBracketAfter = integral;
                }
                else
                {
                    _hitStop.reverseFadeLastTransitionFrame = previous.frame; _hitStop.reverseFadeSettledFrame = row.frame;
                    _hitStop.reverseFadeBracketBefore = previous.fadeUnfrozenSeconds; _hitStop.reverseFadeBracketAfter = integral;
                }
                Assert.That(previous.fadeUnfrozenSeconds, Is.LessThanOrEqualTo(_hitStop.declaredFadeSeconds + FreezeClockTolerance));
                Assert.That(integral, Is.GreaterThanOrEqualTo(_hitStop.declaredFadeSeconds - FreezeClockTolerance));
                Assert.That(integral - previous.fadeUnfrozenSeconds,
                    Is.EqualTo((double)row.afterDelta * row.afterSpeed).Within(FreezeClockTolerance),
                    "Only the real adjacent completion frame may quantize the .08s endpoint; never assert exact elapsed=.08.");
                completed = true; break;
            }
            Assert.That(completed, Is.True, "Both fade directions need a truly transitional frame followed immediately by a settled target frame.");
            if (forward) _hitStop.forwardFadePassed = true; else _hitStop.reverseFadePassed = true;
        }
        void EvaluateHitStopGates(FreezeBoundary boundary)
        {
            var ranged = _freezeLoops.Where(r => r.beforeDomain == CombatState.RangedAttack.ToString()).ToArray();
            var entry = _freezeLoops.FirstOrDefault(r => r.entry);
            var applied = _freezeEvents.FirstOrDefault(e => e.kind == "applied");
            var ended = _freezeEvents.FirstOrDefault(e => e.kind == "ended");
            _hitStop.frozenFrames = _freezeLoops.Count(r => r.beforeCaptured && r.afterCaptured &&
                r.beforeGrade == HitFeedbackGrade.Heavy.ToString() && r.afterGrade == HitFeedbackGrade.Heavy.ToString() &&
                r.beforeSpeed == 0f && r.afterSpeed == 0f && r.eof != null);
            _hitStop.frozenRangedFrames = ranged.Count(r => r.beforeCaptured && r.afterCaptured &&
                r.beforeGrade == HitFeedbackGrade.Heavy.ToString() && r.afterGrade == HitFeedbackGrade.Heavy.ToString() &&
                r.beforeSpeed == 0f && r.afterSpeed == 0f && r.destinationObserved && r.eof != null);
            FreezeGate("complete-domain-fade-tail", () => {
                Assert.That(_hitStop.executionCompleted, Is.True, "Deadline must cover actual domain completion, full Idle+.15 and every knife tail.");
                Assert.That(_hitStop.stableContinuity, Is.True);
                Assert.That(_trace.stableIdleSeconds, Is.GreaterThanOrEqualTo(.15f));
                Assert.That(_seenStates.SetEquals(new[] { "Locomotion", "PlayerRangedAttack" }), Is.True);
                Assert.That(_seen.Contains(_throwClip), Is.True);
                Assert.That(_inputs.Count(i => i.kind == "performed" && i.action == "RangedAttack"), Is.EqualTo(1));
            });
            FreezeGate("one-real-freeze-request-and-end", () => {
                Assert.That(_hitStop.requestCount, Is.EqualTo(1)); Assert.That(_freezeEvents.Count(e => e.kind == "requested"), Is.EqualTo(1));
                Assert.That(_freezeEvents.Count(e => e.kind == "applied"), Is.EqualTo(1)); Assert.That(_freezeEvents.Count(e => e.kind == "ended"), Is.EqualTo(1));
                Assert.That(applied, Is.Not.Null); Assert.That(ended, Is.Not.Null); Assert.That(applied.requestAccepted, Is.True);
                Assert.That(applied.grade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString())); Assert.That(applied.activeGrade, Is.EqualTo(applied.grade));
                Assert.That(applied.actualSequence, Is.EqualTo(_hitStop.requestSequence)); Assert.That(applied.speed, Is.Zero);
                Assert.That(ended.actualSequence, Is.EqualTo(_hitStop.requestSequence)); Assert.That(ended.activeGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                Assert.That(ended.speed, Is.EqualTo(ended.baseSpeed).Within(FreezeClockTolerance));
                Assert.That(HitFeedbackRules.Duration(HitFeedbackGrade.Heavy), Is.EqualTo(.10f));
                Assert.That(ended.effectiveMilliseconds, Is.GreaterThanOrEqualTo(HitFeedbackRules.Duration(HitFeedbackGrade.Heavy) * 1000d - .01d));
                Assert.That(_hitStop.frozenFrames, Is.GreaterThanOrEqualTo(2), "Request acknowledgement without two actual frozen frames is uncovered.");
            });
            FreezeGate("actual-entry-boundary-and-offset", () => {
                Assert.That(_freezeLoops.Count(r => r.entry), Is.EqualTo(1)); Assert.That(entry, Is.Not.Null);
                Assert.That(entry.beforeCaptured && entry.afterCaptured && entry.eof != null, Is.True);
                Assert.That(entry.beforePresented, Is.EqualTo(CombatState.Locomotion.ToString()));
                Assert.That(entry.afterPresented, Is.EqualTo(CombatState.RangedAttack.ToString()));
                Assert.That(entry.destinationObserved && _hitStop.entryOffsetMeasured, Is.True, "Actual destination and offset must be observed even at speed0.");
                Assert.That(entry.afterBase, Is.EqualTo(1f).Within(.000001f), "Same .56 timeline/base1, never phase fitting.");
                if (boundary == FreezeBoundary.EntryDuringFreeze)
                {
                    Assert.That(entry.beforeGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString())); Assert.That(entry.beforeSpeed, Is.Zero);
                    Assert.That(entry.afterGrade, Is.EqualTo(HitFeedbackGrade.Heavy.ToString())); Assert.That(entry.afterSpeed, Is.Zero);
                    Assert.That(_hitStop.frozenRangedFrames, Is.GreaterThanOrEqualTo(2));
                }
                else
                {
                    Assert.That(entry.beforeGrade, Is.EqualTo(HitFeedbackGrade.None.ToString()));
                    Assert.That(entry.beforeSpeed, Is.EqualTo(1f).Within(.000001f));
                    if (boundary == FreezeBoundary.EntryBeforeFreeze)
                    { Assert.That(applied, Is.Not.Null); Assert.That(applied.frame, Is.GreaterThanOrEqualTo(entry.frame)); Assert.That(_hitStop.frozenRangedFrames, Is.GreaterThanOrEqualTo(2)); }
                    else
                    {
                        Assert.That(ended, Is.Not.Null); Assert.That(ended.frame, Is.LessThan(entry.frame)); Assert.That(_hitStop.frozenRangedFrames, Is.Zero);
                        Assert.That(ranged.All(r => r.beforeGrade == HitFeedbackGrade.None.ToString() && r.afterGrade == HitFeedbackGrade.None.ToString() &&
                            Mathf.Abs(r.beforeSpeed - 1f) <= .000001f && Mathf.Abs(r.afterSpeed - 1f) <= .000001f), Is.True, "After-unfreeze F must actually have no freeze debt.");
                    }
                }
                Assert.That(_hitStop.observedEntryTime, Is.EqualTo(_hitStop.expectedEntryTime).Within(FreezeClockTolerance), "Fixed-time API may discard nonzero offset at speed0; preserve the actual failure.");
            });
            FreezeGate("per-state-reset-and-real-domain-ticks", () => {
                Assert.That(entry, Is.Not.Null); Assert.That(_hitStop.entryElapsed, Is.EqualTo(_hitStop.entryDelta).Within(FreezeClockTolerance));
                Assert.That(_hitStop.entryDelta, Is.GreaterThan(0f)); Assert.That(_hitStop.idleElapsedBeforeInput, Is.GreaterThan(_hitStop.entryElapsed));
                Assert.That(_hitStop.actualEntryDuration, Is.EqualTo(.56f).Within(.000001f));
                Assert.That(_expectedTuning.RangedDuration, Is.EqualTo(.56f).Within(.000001f));
                Assert.That(_expectedTuning.RangedReleaseTime, Is.EqualTo(.22f).Within(.000001f));
                for (int i = 1; i < ranged.Length; i++)
                { Assert.That(ranged[i].frame, Is.EqualTo(ranged[i - 1].frame + 1)); Assert.That(ranged[i].beforeElapsed, Is.EqualTo(ranged[i - 1].beforeElapsed + ranged[i].beforeDelta).Within(FreezeClockTolerance)); }
                var returned = _rows.FirstOrDefault(r => r.frame > _hitStop.entryFrame && r.domain == CombatState.Locomotion.ToString());
                Assert.That(returned, Is.Not.Null); Assert.That(returned.elapsed, Is.Zero.Within(FreezeClockTolerance));
                Assert.That(_events.Count, Is.EqualTo(1)); var release = _events[0];
                Assert.That(release.domain, Is.EqualTo(CombatState.RangedAttack.ToString()));
                Assert.That(release.sequence, Is.EqualTo(_trace.sequenceBefore + 1)); Assert.That(release.attackSequence, Is.EqualTo(_actor.Model.AttackSequence));
                Assert.That(release.elapsed, Is.GreaterThanOrEqualTo(.22f));
                Assert.That(release.elapsed, Is.LessThanOrEqualTo(.22f + release.delta + FreezeClockTolerance));
                Assert.That(_actor.Model.RangedReleaseSequence, Is.EqualTo(_trace.sequenceBefore + 1));
            });
            FreezeGate("freeze-aware-clock-and-no-extra-seek", () => {
                Assert.That(ranged, Is.Not.Empty); Assert.That(_hitStop.freezeAwareClockMeasured, Is.True);
                Assert.That(ranged.All(r => r.beforeCaptured && r.afterCaptured && r.eof != null && r.destinationObserved), Is.True);
                foreach (var row in ranged)
                {
                    Assert.That(row.destinationStateSpeed, Is.EqualTo(1f).Within(.000001f));
                    Assert.That(row.afterBase, Is.EqualTo(1f).Within(.000001f));
                    Assert.That(row.afterSpeed, Is.EqualTo(row.afterGrade == HitFeedbackGrade.None.ToString() ? row.afterBase : 0f).Within(FreezeClockTolerance));
                    Assert.That(float.IsNaN(row.observedClipTime) || float.IsInfinity(row.observedClipTime), Is.False);
                }
                Assert.That(_hitStop.maximumClockError, Is.LessThanOrEqualTo(FreezeClockTolerance));
                Assert.That(_hitStop.maximumIncrementError, Is.LessThanOrEqualTo(FreezeClockTolerance));
                if (boundary != FreezeBoundary.EntryAfterUnfreeze)
                {
                    Assert.That(_hitStop.unfreezeFrames, Is.EqualTo(1));
                    Assert.That(_hitStop.expectedUnfreezeIncrement, Is.GreaterThan(0f));
                    Assert.That(_hitStop.unfreezeIncrement, Is.EqualTo(_hitStop.expectedUnfreezeIncrement).Within(FreezeClockTolerance), "Unfreeze naturally advances this frame's delta; zero difference is NOT required.");
                }
            });
            FreezeGate("actual-frozen-state-holds-in-idle-or-F", () => {
                int compared = 0;
                for (int i = 1; i < _freezeLoops.Count; i++)
                {
                    var before = _freezeLoops[i - 1]; var row = _freezeLoops[i];
                    if (before.eof == null || row.eof == null || !before.afterCaptured || !row.afterCaptured ||
                        before.afterSpeed != 0f || row.afterSpeed != 0f || row.entry) continue;
                    Assert.That(row.frame, Is.EqualTo(before.frame + 1));
                    Assert.That(row.eof.currentHash, Is.EqualTo(before.eof.currentHash));
                    Assert.That(row.eof.currentNormalized, Is.EqualTo(before.eof.currentNormalized).Within(FreezeClockTolerance));
                    Assert.That(row.eof.nextHash, Is.EqualTo(before.eof.nextHash));
                    Assert.That(row.eof.nextNormalized, Is.EqualTo(before.eof.nextNormalized).Within(FreezeClockTolerance));
                    compared++;
                }
                Assert.That(compared, Is.GreaterThanOrEqualTo(1), "Two real frozen poses, not merely an acknowledged Request, must be compared.");
            });
            FreezeGate("release-frame-lease-not-cold-phase", () => {
                Assert.That(_events.Count, Is.EqualTo(1)); int index = _rows.FindIndex(r => r.frame == _events[0].frame);
                Assert.That(index, Is.GreaterThanOrEqualTo(2));
                Row previous = _rows[index - 2], before = _rows[index - 1], release = _rows[index];
                _hitStop.releaseFrame = release.frame; _hitStop.releasePhaseMeasured = release.rightHandVelocityMeasured;
                _hitStop.releasePreviousHandZ = previous.rightHandActor.z; _hitStop.releaseBeforeHandZ = before.rightHandActor.z;
                _hitStop.releaseHandZ = release.rightHandActor.z; _hitStop.releaseHandVelocity = release.rightHandForwardVelocity;
                _hitStop.releasePhaseActualPassed = _hitStop.releasePhaseMeasured && release.rightHandActor.z > before.rightHandActor.z &&
                    before.rightHandActor.z > previous.rightHandActor.z && release.rightHandForwardVelocity > 0f;
                Assert.That(before.heldVisible && !before.flightVisible && !release.heldVisible && release.flightVisible, Is.True);
                Assert.That(release.projectiles, Is.EqualTo(1)); Assert.That(release.visuals, Is.EqualTo(1));
                Assert.That(release.knifeGeometryMeasured, Is.True); Assert.That(release.bridgeAge, Is.EqualTo(0f).Within(.000001f));
                Assert.That(_rows.All(r => r.projectiles <= 1 && r.visuals <= 1), Is.True);
                _hitStop.handoffLifecyclePassed = true;
            });
            if (_hitStop.coldPhaseApplicable)
                FreezeGate("independent-original-cold-phase-after-unfreeze", () => {
                    Assert.That(_hitStop.releasePhaseMeasured, Is.True);
                    Assert.That(_hitStop.releaseHandZ, Is.GreaterThan(_hitStop.releaseBeforeHandZ));
                    Assert.That(_hitStop.releaseBeforeHandZ, Is.GreaterThan(_hitStop.releasePreviousHandZ));
                    Assert.That(_hitStop.releaseHandVelocity, Is.GreaterThan(0f));
                    _hitStop.coldPhaseGatePassed = true; _trace.knifeHandoffPassed = _hitStop.handoffLifecyclePassed;
                });
            FreezeGate("knife-full-return-and-trail", () => {
                Assert.That(_launcher.ActiveProjectileCount, Is.Zero); Assert.That(_knife.ActiveVisualCount, Is.Zero);
                Assert.That(_knife.TrailPointCount, Is.Zero); Assert.That(_knife.HeldVisible || _knife.FlightVisible, Is.False);
                Assert.That(_knife.SwordVisible, Is.True); _hitStop.lifetimeGatePassed = true;
            });
            FreezeGate("independent-signed-body-cape-gear-knife-floor", () => {
                Assert.That(_rows, Is.Not.Empty);
                _hitStop.bodyMin = _trace.minimumBody = _rows.Min(r => r.bodyMin);
                _hitStop.capeMin = _trace.minimumCape = _rows.Min(r => r.capeMin); _hitStop.gearMin = _trace.minimumGear = _rows.Min(r => r.gearMin);
                _trace.floorGateEvaluated = true; _trace.bodyFloorGatePassed = _hitStop.bodyMin >= 0f;
                _trace.capeFloorGatePassed = _hitStop.capeMin >= 0f; _trace.gearFloorGatePassed = _hitStop.gearMin >= 0f;
                Assert.That(_hitStop.bodyMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_hitStop.capeMin, Is.GreaterThanOrEqualTo(0f)); Assert.That(_hitStop.gearMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(_rows.Any(r => r.knifeGeometryMeasured), Is.True);
                Assert.That(_rows.Where(r => r.knifeGeometryMeasured).All(r => r.knifeMin >= 0f), Is.True);
                _hitStop.signedFloorPassed = true; _trace.knifeLifetimeCompleted = _hitStop.lifetimeGatePassed;
            });
            FreezeGate("forward-original-unfrozen-80ms-fade", () => EvaluateFreezeFade(true, boundary));
            FreezeGate("reverse-original-unfrozen-80ms-fade", () => EvaluateFreezeFade(false, boundary));
        }
        IEnumerator CaptureDiagnostic(IEnumerator root, string gate, bool signedFloorOnly = false)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(root);
            try
            {
                while (stack.Count != 0)
                {
                    IEnumerator active = stack.Peek(); bool moved = false, failed = false; object value = null;
                    try { moved = active.MoveNext(); if (moved) value = active.Current; }
                    catch (AssertionException error) when (!signedFloorOnly || _atSignedFloor)
                    { _entryFailures.Add(new GateFailure { gate = gate, message = error.Message, stackTrace = error.StackTrace }); failed = true; }
                    if (failed) yield break;
                    if (!moved) { stack.Pop(); (active as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)) stack.Push(nested); else yield return value;
                }
            }
            finally { while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); _atSignedFloor = false; }
        }
        IEnumerator DiagnosticClock()
        {
            var rangedRows = _rows.Where(r => r.domain == CombatState.RangedAttack.ToString()).ToArray(); Assert.That(rangedRows, Is.Not.Empty);
            _trace.maximumEntryClockError = rangedRows.Max(r => Mathf.Abs(r.elapsed - (r.currentState == "PlayerRangedAttack" ? r.currentNormalized : r.nextNormalized) * _throwClip.length));
            _trace.entryClockGateEvaluated = true; _trace.entryClockGatePassed = _trace.maximumEntryClockError <= .00001f;
            Assert.That(_trace.entryClockGatePassed, Is.True, "Clock alignment is independent of the hand-phase gate; retain either failure."); yield break;
        }
        IEnumerator DiagnosticHandoff()
        {
            var fact = _events.Single(); int index = _rows.FindIndex(r => r.frame == fact.frame);
            Assert.That(index, Is.GreaterThanOrEqualTo(2), "Use the real event's EndOfFrame row, not its pre-Animator callback hand.");
            var before = _rows[index - 1]; var release = _rows[index]; var previous = _rows[index - 2];
            Assert.That(before.heldVisible && !before.flightVisible && !release.heldVisible && release.flightVisible, Is.True);
            Assert.That(release.projectiles, Is.EqualTo(1)); Assert.That(release.visuals, Is.EqualTo(1)); Assert.That(release.knifeGeometryMeasured, Is.True); Assert.That(release.bridgeAge, Is.EqualTo(0f).Within(.000001f));
            Assert.That(release.rightHandActor.z, Is.GreaterThan(before.rightHandActor.z)); Assert.That(before.rightHandActor.z, Is.GreaterThan(previous.rightHandActor.z)); Assert.That(release.rightHandForwardVelocity, Is.GreaterThan(0f));
            int forwardStart = index; while (forwardStart > 0 && _rows[forwardStart].rightHandActor.z > _rows[forwardStart - 1].rightHandActor.z) forwardStart--;
            _trace.forwardSampleMarginSeconds = (float)(release.time - _rows[forwardStart].time); _trace.knifeHandoffPassed = true; yield break;
        }
        IEnumerator DiagnosticLifetime()
        {
            Assert.That(_launcher.ActiveProjectileCount, Is.Zero); Assert.That(_knife.ActiveVisualCount, Is.Zero); Assert.That(_knife.TrailPointCount, Is.Zero); Assert.That(_knife.HeldVisible || _knife.FlightVisible, Is.False); Assert.That(_knife.SwordVisible, Is.True);
            Assert.That(_rows.Where(r => r.knifeGeometryMeasured).All(r => r.knifeMin >= 0f), Is.True, "Visible knife geometry has its own signed floor gate.");
            _trace.minimumBody = _rows.Min(r => r.bodyMin); _trace.minimumCape = _rows.Min(r => r.capeMin); _trace.minimumGear = _rows.Min(r => r.gearMin);
            _trace.bodyFloorGatePassed = _trace.minimumBody >= 0f; _trace.capeFloorGatePassed = _trace.minimumCape >= 0f; _trace.gearFloorGatePassed = _trace.minimumGear >= 0f;
            Assert.That(_rows.All(r => r.bodyMin >= 0 && r.capeMin >= 0 && r.gearMin >= 0), Is.True); _trace.knifeLifetimeCompleted = true; yield break;
        }
        void DiagnosticBridge()
        {
            int index = _rows.FindIndex(r => r.frame == _events.Single().frame); if (index < 0) return; bool ended = false;
            for (int i = index + 1; i < _rows.Count; i++) { var sample = _rows[i]; _trace.maximumBridgeSampleGap = Mathf.Max(_trace.maximumBridgeSampleGap, (float)(sample.time - _rows[i - 1].time)); if (!sample.flightVisible) break; if (sample.bridgeAge >= PlayerKnifePresentation.BridgeDuration) { ended = true; break; } _trace.bridgeInteriorSamples++; }
            _trace.bridgeSamplingComplete = ended && _trace.bridgeInteriorSamples >= 2 && _trace.maximumBridgeSampleGap <= 1f / 30f;
        }
        IEnumerator DiagnosticImages()
        {
            Assert.That(_trace.imageWriteError, Is.Null.Or.Empty); Assert.That(_images.Count, Is.EqualTo(3), "All three actual held/release/flight images must have been requested.");
            double deadline = Time.realtimeSinceStartupAsDouble + 8;
            while (_images.Any(file => !File.Exists(_output + "/" + file)) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            foreach (string file in _images) Assert.That(File.Exists(_output + "/" + file) && new FileInfo(_output + "/" + file).Length > 100, Is.True, "Actual GameView PNG must exist; visual review remains separate."); yield break;
        }
        bool HasKnifeTail() => _launcher.ActiveProjectileCount != 0 || _knife.ActiveVisualCount != 0 || _knife.HeldVisible || _knife.FlightVisible || _knife.TrailPointCount != 0;
        void VerifyEntrySafety()
        {
            Assert.That(_scene.IsValid() && _scene.isLoaded && _actor != null && _actor.gameObject.scene == _scene, Is.True);
            Assert.That(Object.FindObjectsOfType<PlayerCombatActor>(true), Is.EquivalentTo(new[] { _actor }), "Never continue with a user/production Actor present.");
            foreach (var flow in Object.FindObjectsOfType<M2RouteFlowController>()) Assert.That(flow.SavePath, Does.Contain("IsolatedSaves"));
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves")); Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
            Assert.That(_candidateSet.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(_throwClip)); Assert.That(AssetDatabase.GetAssetPath(_throwClip), Is.EqualTo(StagedUpperThrow));
            Assert.That(_animator.runtimeAnimatorController, Is.SameAs(_candidateController)); Assert.That(AssetDatabase.GetAssetPath(_animator.avatar), Is.EqualTo(Native)); Assert.That(_animator.avatar.isHuman && _animator.avatar.isValid, Is.True);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(pairs); Assert.That(pairs.Single(p => p.Key == _originalRangedKey).Value, Is.SameAs(_throwClip));
            Assert.That(_disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True, "Source byte protection failure stops the sequence; no rollback or continuation.");
            Assert.That(_memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value), Is.True, "Source-memory protection failure stops the sequence.");
        }
        IEnumerator VisibleKnifeAbility()
        {
            _actor.RangedAttackReleased -= OnRangedReleased;
            yield return Ability(false); _completed = false;
            if (_domainEntrySync)
            {
                var rangedRows = _rows.Where(r => r.domain == CombatState.RangedAttack.ToString()).ToArray();
                Assert.That(rangedRows, Is.Not.Empty);
                _trace.maximumEntryClockError = rangedRows.Max(r => Mathf.Abs(r.elapsed - (r.currentState == "PlayerRangedAttack" ? r.currentNormalized : r.nextNormalized) * _throwClip.length));
                _trace.entryClockGateEvaluated = true; _trace.entryClockGatePassed = _trace.maximumEntryClockError <= .00001f;
                Assert.That(_trace.entryClockGatePassed, Is.True, "Clock alignment is independent of the hand-phase gate; retain either failure.");
            }
            var fact = _events.Single(); int index = _rows.FindIndex(r => r.frame == fact.frame);
            Assert.That(index, Is.GreaterThanOrEqualTo(2), "Use the real event's EndOfFrame row, not its pre-Animator callback hand.");
            var before = _rows[index - 1]; var release = _rows[index]; var previous = _rows[index - 2];
            Assert.That(before.heldVisible && !before.flightVisible && !release.heldVisible && release.flightVisible, Is.True);
            Assert.That(release.projectiles, Is.EqualTo(1)); Assert.That(release.visuals, Is.EqualTo(1)); Assert.That(release.knifeGeometryMeasured, Is.True); Assert.That(release.bridgeAge, Is.EqualTo(0f).Within(.000001f));
            Assert.That(release.rightHandActor.z, Is.GreaterThan(before.rightHandActor.z)); Assert.That(before.rightHandActor.z, Is.GreaterThan(previous.rightHandActor.z)); Assert.That(release.rightHandForwardVelocity, Is.GreaterThan(0f));
            int forwardStart = index;
            while (forwardStart > 0 && _rows[forwardStart].rightHandActor.z > _rows[forwardStart - 1].rightHandActor.z) forwardStart--;
            _trace.forwardSampleMarginSeconds = (float)(release.time - _rows[forwardStart].time); // Sampled contiguous-forward interval, not a continuous/analytic margin.
            _trace.knifeHandoffPassed = true;
            for (int i = 0; i < 5000 && (_launcher.ActiveProjectileCount != 0 || _knife.HeldVisible || _knife.FlightVisible || _knife.TrailPointCount != 0); i++) { yield return null; yield return new WaitForEndOfFrame(); Observe(Vector2.Distance(PlanarHips(), new Vector2(_rows[0].hips.x - _rows[0].root.x, _rows[0].hips.z - _rows[0].root.z))); }
            Assert.That(_launcher.ActiveProjectileCount, Is.Zero); Assert.That(_knife.ActiveVisualCount, Is.Zero); Assert.That(_knife.TrailPointCount, Is.Zero); Assert.That(_knife.HeldVisible || _knife.FlightVisible, Is.False); Assert.That(_knife.SwordVisible, Is.True);
            Assert.That(_rows.Where(r => r.knifeGeometryMeasured).All(r => r.knifeMin >= 0f), Is.True, "Visible knife geometry has its own signed floor gate.");
            _trace.minimumBody = _rows.Min(r => r.bodyMin); _trace.minimumCape = _rows.Min(r => r.capeMin); _trace.minimumGear = _rows.Min(r => r.gearMin);
            Assert.That(_rows.All(r => r.bodyMin >= 0 && r.capeMin >= 0 && r.gearMin >= 0), Is.True); _trace.knifeLifetimeCompleted = true;
            bool bridgeEnded = false;
            for (int i = index + 1; i < _rows.Count; i++) { var sample = _rows[i]; _trace.maximumBridgeSampleGap = Mathf.Max(_trace.maximumBridgeSampleGap, (float)(sample.time - _rows[i - 1].time)); if (!sample.flightVisible) break; if (sample.bridgeAge >= PlayerKnifePresentation.BridgeDuration) { bridgeEnded = true; break; } _trace.bridgeInteriorSamples++; }
            _trace.bridgeSamplingComplete = bridgeEnded && _trace.bridgeInteriorSamples >= 2 && _trace.maximumBridgeSampleGap <= 1f / 30f;
            // PNG compression is deliberately AFTER the measured motion/bridge/lifetime, not inside Observe.
            SaveCapturedImages(); Assert.That(_trace.imageWriteError, Is.Null.Or.Empty);
            Assert.That(_images.Count, Is.EqualTo(3), "All three actual held/release/flight images must have been requested.");
            double screenshotDeadline = Time.realtimeSinceStartupAsDouble + 8;
            while (_images.Any(file => !File.Exists(_output + "/" + file)) && Time.realtimeSinceStartupAsDouble < screenshotDeadline) yield return null;
            foreach (string file in _images) Assert.That(File.Exists(_output + "/" + file) && new FileInfo(_output + "/" + file).Length > 100, Is.True, "Actual GameView PNG must exist; visual review remains separate.");
            _trace.visibleKnife = "measured-handoff-and-lifecycle; rendered images pending human review"; _completed = true;
        }
        void PrepareUpperBodyCandidate(string candidatePath = UpperThrow)
        {
            // New acceptance case, not a replacement/Ignore for the retained native-F failure.
            var clip = Required<AnimationClip>(candidatePath); Assert.That(clip.isHumanMotion, Is.True); Assert.That(clip.length, Is.EqualTo(.56f).Within(.000001f));
            foreach (string path in AssetDatabase.GetDependencies(candidatePath, true).Concat(new[] { candidatePath }).Distinct())
            {
                foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists)) if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != null && EditorUtility.IsPersistent(o))) if (!_memory.ContainsKey(asset)) _memory.Add(asset, EditorJsonUtility.ToJson(asset));
            }
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); _candidateController.GetOverrides(pairs);
            Assert.That(pairs.Count(p => p.Key == _originalRangedKey), Is.EqualTo(1)); int index = pairs.FindIndex(p => p.Key == _originalRangedKey);
            Assert.That(pairs[index].Value, Is.SameAs(_throwClip)); pairs[index] = new KeyValuePair<AnimationClip, AnimationClip>(_originalRangedKey, clip); _candidateController.ApplyOverrides(pairs);
            var so = new SerializedObject(_candidateSet); so.FindProperty("_offlineRangedAttack").objectReferenceValue = clip; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_candidateSet.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(clip));
            _throwClip = clip; _upperBodyCandidate = true;
        }
        IEnumerator Ability(bool heal)
        {
            // Each repeated entry owns its completion/times; never borrow a previous tail on a deadline failure.
            _trace.domainReturnedToLocomotion = _trace.animatorSettledToLocomotion = _trace.stableIdleCompleted = _trace.abilityEvidenceComplete = false;
            _trace.floorGateEvaluated = _trace.bodyFloorGatePassed = _trace.capeFloorGatePassed = _trace.gearFloorGatePassed = false;
            _trace.domainReturnedAt = _trace.animatorSettledAt = _trace.stableIdleEndedAt = _trace.domainFirstObservedAt = 0;
            _trace.stableIdleSeconds = _trace.domainFirstObservedElapsed = _trace.peak = _trace.final = 0;
            AnimationClip clip = heal ? _healClip : _throwClip; CombatState domain = heal ? CombatState.Heal : CombatState.RangedAttack; string stateName = heal ? "Heal" : "PlayerRangedAttack";
            _stateGuard.Clear(); _stateGuard.UnionWith(new[] { "Locomotion", stateName }); _allowed.Clear(); _allowed.UnionWith(_locomotion); _allowed.Add(clip);
            _trace.ability = heal ? "R/Heal" : "F/RangedAttack"; _trace.candidate = AssetDatabase.GetAssetPath(clip) + "::" + clip.name; _trace.candidateLength = clip.length; _trace.fullAbilityAccepted = false;
            _trace.expectedReleaseTime = heal ? _expectedTuning.HealResolveTime : _expectedTuning.RangedReleaseTime; _trace.expectedDuration = heal ? _expectedTuning.HealDuration : _expectedTuning.RangedDuration;
            yield return new WaitForSeconds(.5f); Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion)); Assert.That(_animator.IsInTransition(0), Is.False); Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True);
            if (heal) { _trace.fixtureHealthLoss = _actor.Model.Health.ApplyDamage(_actor.Model.Health.Maximum * .5f, 0f); Assert.That(_actor.Model.State, Is.EqualTo(CombatState.Locomotion), "Fixture Health.ApplyDamage prerequisite is NOT natural damage/HitReact."); }
            _trace.healthBefore = _actor.Model.Health.Current; _trace.chargesBefore = _actor.Model.HealingFlasks.CurrentCharges; _trace.sequenceBefore = heal ? _actor.Model.HealSequence : _actor.Model.RangedReleaseSequence;
            if (!heal) _actor.RangedAttackReleased += OnRangedReleased;
            Vector2 baseline = PlanarHips(); Vector3 root = _actor.transform.position; bool entered = false, recovered = false; double stableStarted = double.NaN;
            _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation; _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
            yield return new WaitForEndOfFrame(); Observe(0f); _trace.inputQueuedAt = Time.timeAsDouble;
            RecordInput("queued", heal ? "Heal" : "RangedAttack"); Assert.That(_keyboard.enabled, Is.True);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(heal ? Key.R : Key.F)); yield return null; InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            for (int f = 0; f < 5000; f++)
            {
                yield return new WaitForEndOfFrame(); float drift = Vector2.Distance(baseline, PlanarHips()); Observe(drift); _trace.peak = Mathf.Max(_trace.peak, drift); Row row = _rows[_rows.Count - 1];
                Assert.That(double.IsNaN(row.time) || double.IsInfinity(row.time), Is.False);
                Assert.That(Vector2.Distance(new Vector2(root.x, root.z), new Vector2(_actor.transform.position.x, _actor.transform.position.z)), Is.LessThan(.01f)); Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
                if (!entered && _actor.Model.State == domain) { entered = true; _trace.domainFirstObservedAt = row.time; _trace.domainFirstObservedElapsed = row.elapsed; Assert.That(_actor.Model.StateDuration, Is.EqualTo(_trace.expectedDuration)); }
                if (_upperBodyCandidate && row.domain == CombatState.RangedAttack.ToString()) Assert.That(row.speed, Is.EqualTo(1f).Within(.000001f), "Authored .56s timeline must actually play at 1x; no phase fitting.");
                if (heal && row.healSequence != _trace.sequenceBefore && _events.Count == 0) _events.Add(EventSnapshot("HealSequence-first-EndOfFrame", row.healSequence, row.attackSequence));
                if (entered && _actor.Model.State == CombatState.Locomotion && !recovered) { recovered = true; _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = row.time; }
                if (recovered && double.IsNaN(stableStarted) && row.stableLocomotion) { stableStarted = row.time; _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = row.time; }
                if (!double.IsNaN(stableStarted))
                {
                    Assert.That(row.stableLocomotion, Is.True, "Ability recovery must stay settled, no transition, with only native locomotion clips."); Assert.That(row.time, Is.GreaterThanOrEqualTo(stableStarted)); _trace.stableIdleSeconds = (float)(row.time - stableStarted);
                    if (row.time - stableStarted >= .15) { _trace.stableIdleCompleted = true; _trace.stableIdleEndedAt = row.time; _trace.final = drift; break; }
                }
                yield return null;
            }
            _trace.abilityEvidenceComplete = entered && recovered && _trace.stableIdleCompleted; _trace.minimumBody = _rows.Min(r => r.bodyMin); _trace.minimumCape = _rows.Min(r => r.capeMin); _trace.minimumGear = _rows.Min(r => r.gearMin);
            _trace.floorGateEvaluated = true; _trace.bodyFloorGatePassed = _trace.minimumBody >= 0f; _trace.capeFloorGatePassed = _trace.minimumCape >= 0f; _trace.gearFloorGatePassed = _trace.minimumGear >= 0f;
            Assert.That(_trace.abilityEvidenceComplete, Is.True, "5000-frame deadline must include actual domain completion and full Idle fade plus .15s stable."); Assert.That(_seenStates.SetEquals(new[] { "Locomotion", stateName }), Is.True); Assert.That(_seen.Contains(clip), Is.True);
            Assert.That(_events.Count, Is.EqualTo(1)); EventRow fact = _events[0]; Assert.That(fact.sequence, Is.EqualTo(_trace.sequenceBefore + 1)); Assert.That(fact.domain, Is.EqualTo(domain.ToString()));
            Assert.That(fact.elapsed, Is.GreaterThanOrEqualTo(_trace.expectedReleaseTime)); Assert.That(fact.elapsed, Is.LessThanOrEqualTo(_trace.expectedReleaseTime + fact.delta + .00001f), "One real domain tick quantization, not an imposed animation phase.");
            if (heal) { Assert.That(_actor.Model.HealSequence, Is.EqualTo(_trace.sequenceBefore + 1)); Assert.That(fact.charges, Is.EqualTo(_trace.chargesBefore - 1)); Assert.That(fact.health, Is.EqualTo(Mathf.Min(_trace.healthBefore + _actor.Model.Health.Maximum * _expectedTuning.HealHealthFraction, _actor.Model.Health.Maximum)).Within(.00001f)); }
            else { Assert.That(_actor.Model.RangedReleaseSequence, Is.EqualTo(_trace.sequenceBefore + 1)); Assert.That(fact.attackSequence, Is.EqualTo(_actor.Model.AttackSequence)); }
            // Predeclared signed world-y floor gates: no negative tolerance/root lift. Assert only AFTER full trace/fade facts exist.
            _atSignedFloor = _collectEntryDiagnostics;
            Assert.That(_trace.minimumBody, Is.GreaterThanOrEqualTo(0f), "Native body floor failed; complete input/domain/fade evidence retained."); Assert.That(_trace.minimumCape, Is.GreaterThanOrEqualTo(0f)); Assert.That(_trace.minimumGear, Is.GreaterThanOrEqualTo(0f)); _completed = true;
            _atSignedFloor = false;
        }
        void OnRangedReleased(RangedAttackRelease release) => _events.Add(EventSnapshot("Actor.RangedAttackReleased-before-Presenter", _actor.Model.RangedReleaseSequence, release.AttackSequence));
        EventRow EventSnapshot(string kind, int sequence, int attackSequence) => new EventRow { kind = kind, domain = _actor.Model.State.ToString(), frame = Time.frameCount, time = Time.timeAsDouble, elapsed = _actor.Model.StateElapsed, delta = Time.deltaTime, sequence = sequence, attackSequence = attackSequence, health = _actor.Model.Health.Current, charges = _actor.Model.HealingFlasks.CurrentCharges, rightHandActor = _actor.transform.InverseTransformPoint(_rightHand.position) };
        IEnumerator Combo(bool settled)
        {
            _trace.settled = settled; if (settled) yield return new WaitForSeconds(.5f);
            Assert.That(_actor.GetComponent<CharacterController>().isGrounded, Is.True); Assert.That(_animator.IsInTransition(0), Is.False);
            Vector2 baseline = PlanarHips(); Vector3 root = _actor.transform.position; bool second = false, third = false, reachedThird = false, recovered = false; float peak = 0; double stableStarted = double.NaN;
            _trace.idleHipsRelative = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation; _trace.idleChestRelative = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
            RecordInput("queued", "LightAttack"); Assert.That(_mouse.enabled, Is.True);
            InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 1 }); yield return null; InputSystem.QueueStateEvent(_mouse, new MouseState()); yield return null;
            Assert.That(_actor.Model.State, Is.EqualTo(CombatState.LightAttack1));
            for (int f = 0; f < 5000; f++)
            {
                yield return new WaitForEndOfFrame(); float drift = Vector2.Distance(baseline, PlanarHips()); peak = Mathf.Max(peak, drift); Observe(drift); _trace.peak = peak;
                Assert.That(Vector2.Distance(new Vector2(root.x, root.z), new Vector2(_actor.transform.position.x, _actor.transform.position.z)), Is.LessThan(.01f));
                Assert.That(_animator.applyRootMotion, Is.False); Assert.That(_animator.transform.localPosition, Is.EqualTo(_anchor)); Assert.That(_animator.transform.localRotation, Is.EqualTo(_anchorRotation));
                CombatState state = _actor.Model.State;
                if ((state == CombatState.LightAttack1 && !second || state == CombatState.LightAttack2 && !third) && _actor.Model.StateElapsed >= _actor.Model.LightRecoveryStart + .01f)
                { if (state == CombatState.LightAttack1) second = true; else third = true; InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 1 }); yield return null; InputSystem.QueueStateEvent(_mouse, new MouseState()); }
                reachedThird |= state == CombatState.LightAttack3;
                Row row = _rows[_rows.Count - 1]; Assert.That(double.IsNaN(row.time) || double.IsInfinity(row.time), Is.False);
                if (reachedThird && state == CombatState.Locomotion && !recovered) { recovered = true; _trace.domainReturnedToLocomotion = true; _trace.domainReturnedAt = row.time; }
                if (recovered && double.IsNaN(stableStarted) && row.stableLocomotion) { stableStarted = row.time; _trace.animatorSettledToLocomotion = true; _trace.animatorSettledAt = row.time; }
                if (!double.IsNaN(stableStarted))
                {
                    Assert.That(row.stableLocomotion, Is.True, "The settled .15s observation must stay in actual Locomotion with no transition and only positive-weight native locomotion clips.");
                    Assert.That(row.time, Is.GreaterThanOrEqualTo(stableStarted)); _trace.stableIdleSeconds = (float)(row.time - stableStarted);
                    if (row.time - stableStarted >= .15) { _trace.stableIdleCompleted = true; _trace.stableIdleEndedAt = row.time; _trace.final = drift; break; }
                }
                yield return null;
            }
            Assert.That(second && third && reachedThird && recovered, Is.True); Assert.That(_seen.Any(c => c.name == "A_Review_Knight_Light1Recovery"), Is.True); Assert.That(_seen.Any(c => c.name == "A_Review_Knight_Light2Recovery"), Is.True);
            Assert.That(_trace.stableIdleCompleted, Is.True, "5000-frame deadline: actual Locomotion must finish its fade and remain settled for at least .15s before final is measured.");
            Assert.That(ExpectedStates.All(_seenStates.Contains), Is.True, "All six real reachable states must be observed, including both recovery destinations.");
            Assert.That(peak, Is.LessThan(.4f)); Assert.That(_trace.final, Is.LessThan(.1f)); _completed = true;
        }
        void Observe(float drift)
        {
            var current = _animator.GetCurrentAnimatorClipInfo(0); var next = _animator.GetNextAnimatorClipInfo(0);
            Assert.That(current.Concat(next).All(c => !float.IsNaN(c.weight) && !float.IsInfinity(c.weight)), Is.True, "Animator weights must be finite.");
            foreach (var c in current.Concat(next).Where(c => c.weight > 0)) { Assert.That(_allowed.Contains(c.clip), Is.True, "Unexpected Ranger/other fallback: " + c.clip.name); _seen.Add(c.clip); }
            var ci = _animator.GetCurrentAnimatorStateInfo(0); var ni = _animator.GetNextAnimatorStateInfo(0); Quaternion hip = Quaternion.Inverse(_actor.transform.rotation) * _hips.rotation, chest = Quaternion.Inverse(_actor.transform.rotation) * _chest.rotation;
            Floor(out float body, out float cape, out float gear, out bool conservative);
            string state = KnownState(ci.shortNameHash), nextState = _animator.IsInTransition(0) ? KnownState(ni.shortNameHash) : ""; _seenStates.Add(state); if (nextState.Length != 0) _seenStates.Add(nextState);
            var positive = current.Concat(next).Where(c => c.weight > 0).ToArray();
            bool stable = _actor.Model.State == CombatState.Locomotion && state == "Locomotion" && !_animator.IsInTransition(0) && positive.Length > 0 && positive.All(c => _locomotion.Contains(c.clip));
            _rows.Add(new Row { frame = Time.frameCount, time = Time.timeAsDouble, stableLocomotion = stable, domain = _actor.Model.State.ToString(), elapsed = _actor.Model.StateElapsed, delta = Time.deltaTime, root = _actor.transform.position, hips = _hips.position, hipsRelative = hip, chestRelative = chest, hipsYaw = hip.eulerAngles.y, chestYaw = chest.eulerAngles.y, hipsYawFromIdle = Mathf.DeltaAngle(_trace.idleHipsRelative.eulerAngles.y, hip.eulerAngles.y), chestYawFromIdle = Mathf.DeltaAngle(_trace.idleChestRelative.eulerAngles.y, chest.eulerAngles.y), hipDrift = drift, currentHash = ci.fullPathHash, nextHash = ni.fullPathHash, currentState = state, nextState = nextState, current = string.Join("|", current.Select(c => c.clip.name)), next = string.Join("|", next.Select(c => c.clip.name)), currentWeights = current.Select(c => c.weight).ToArray(), nextWeights = next.Select(c => c.weight).ToArray(), currentNormalized = ci.normalizedTime, nextNormalized = ni.normalizedTime, transition = _animator.IsInTransition(0), speed = _animator.speed, bodyMin = body, capeMin = cape, gearMin = gear, conservativeRigidFloor = conservative });
            Row row = _rows[_rows.Count - 1]; Quaternion inverse = Quaternion.Inverse(_actor.transform.rotation); row.headActor = _actor.transform.InverseTransformPoint(_head.position); row.rightHandActor = _actor.transform.InverseTransformPoint(_rightHand.position); row.leftHandActor = _actor.transform.InverseTransformPoint(_leftHand.position); row.rightUpperArmActor = _actor.transform.InverseTransformPoint(_rightUpperArm.position); row.rightHandRelative = inverse * _rightHand.rotation; row.leftHandRelative = inverse * _leftHand.rotation; row.rightUpperArmRelative = inverse * _rightUpperArm.rotation;
            row.health = _actor.Model.Health.Current; row.charges = _actor.Model.HealingFlasks.CurrentCharges; row.healSequence = _actor.Model.HealSequence; row.releaseSequence = _actor.Model.RangedReleaseSequence; row.attackSequence = _actor.Model.AttackSequence;
            if (_rows.Count > 1) { Row before = _rows[_rows.Count - 2]; double elapsed = row.time - before.time; if (elapsed > 0) { row.rightHandForwardVelocity = (float)((row.rightHandActor.z - before.rightHandActor.z) / elapsed); row.rightHandVelocityMeasured = true; } }
            if (_knife != null)
            {
                row.heldVisible = _knife.HeldVisible; row.flightVisible = _knife.FlightVisible; row.visuals = _knife.ActiveVisualCount; row.projectiles = _launcher.ActiveProjectileCount; row.trailPoints = _knife.TrailPointCount; row.bridgeAge = _knife.BridgeAge; row.knifePosition = _knife.VisualPosition;
                var models = _knife.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && (r.transform.parent.name == "KnifeHeldVisual" || r.transform.parent.name.StartsWith("KnifeFlightVisual_", StringComparison.Ordinal))).ToArray();
                if (row.heldVisible || row.flightVisible) { Assert.That(models.Length, Is.GreaterThan(0)); row.knifeGeometryMeasured = true; row.knifeMin = float.PositiveInfinity; foreach (var renderer in models) { var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh; Assert.That(mesh, Is.Not.Null); if (!mesh.isReadable) { row.knifeMin = Mathf.Min(row.knifeMin, renderer.bounds.min.y); row.conservativeKnifeFloor = true; } else foreach (int i in mesh.triangles) row.knifeMin = Mathf.Min(row.knifeMin, renderer.localToWorldMatrix.MultiplyPoint3x4(mesh.vertices[i]).y); } Assert.That(float.IsNaN(row.knifeMin) || float.IsInfinity(row.knifeMin), Is.False); }
                string image = _images.Count == 0 && row.heldVisible ? "01-held.png" : _images.Count == 1 && _events.Any(e => e.frame == row.frame) && row.flightVisible ? "02-release.png" : _images.Count == 2 && row.flightVisible && row.bridgeAge >= PlayerKnifePresentation.BridgeDuration ? "03-flight.png" : null;
                if (image != null) { if (_entryNumber != 0) image = "entry-" + _entryNumber + "-" + image; var screenshot = ScreenCapture.CaptureScreenshotAsTexture(); Assert.That(screenshot, Is.Not.Null); _ownedAssets.Add(screenshot); _capturedImages.Add(image, screenshot); _images.Add(image); row.image = image; }
            }
        }
        void OnInputPerformed(InputAction.CallbackContext context) => RecordInput("performed", context.action.name);
        void RecordInput(string kind, string action)
        {
            var snapshot = _reader.CaptureSnapshot(); _inputs.Add(new InputRow { kind = kind, action = action, domain = _actor.Model?.State.ToString(), frame = Time.frameCount, time = Time.timeAsDouble, appFocused = UnityEngine.Application.isFocused, editorWindow = EditorWindow.focusedWindow?.GetType().FullName, keyboardEnabled = _keyboard.enabled, mouseEnabled = _mouse.enabled, keyboardId = _keyboard.deviceId, mouseId = _mouse.deviceId, background = InputSystem.settings.backgroundBehavior.ToString(), editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode.ToString(), rangedHeld = snapshot.RangedAttack, healHeld = snapshot.Heal, lightHeld = snapshot.LightAttack });
        }
        void Floor(out float body, out float cape, out float gear, out bool conservative)
        {
            body = cape = gear = float.PositiveInfinity; conservative = false;
            foreach (var r in _animator.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy))
            {
                Mesh scratch = null; float min = float.PositiveInfinity;
                try { Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh; if (mesh == null) continue;
                    if (r is SkinnedMeshRenderer skin) { scratch = new Mesh(); skin.BakeMesh(scratch, true); mesh = scratch; }
                    if (!mesh.isReadable) { min = r.bounds.min.y; conservative = true; } else { var vertices = mesh.vertices; for (int sub = 0; sub < mesh.subMeshCount; sub++) if (mesh.GetTopology(sub) == MeshTopology.Triangles) foreach (int index in mesh.GetIndices(sub)) min = Mathf.Min(min, r.localToWorldMatrix.MultiplyPoint3x4(vertices[index]).y); }
                    Assert.That(float.IsNaN(min) || float.IsInfinity(min), Is.False); if (r.name.Contains("Sword") || r.name.Contains("Shield")) gear = Mathf.Min(gear, min); else if (r.name.Contains("Cape")) cape = Mathf.Min(cape, min); else body = Mathf.Min(body, min);
                } finally { if (scratch != null) Object.DestroyImmediate(scratch); }
            }
            Assert.That(new[] { body, cape, gear }.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), Is.True, "Body/cape/gear must all be present; no invented zero floor.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            try
            {
            if (_actor != null) _actor.RangedAttackReleased -= OnRangedReleased;
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            }
            finally
            {
            // Keep real captured failure images too. Encoding occurs only after measured rows have ended.
            SaveCapturedImages();
            if (_globalsCaptured) InputSystem.settings = _previousSettings;
            foreach (var o in _ownedAssets) if (o != null) Object.Destroy(o);
            if (_globalsCaptured) { Cursor.lockState = _cursorLock; Cursor.visible = _cursorVisible; }
            bool settingsRestored = !_globalsCaptured || ReferenceEquals(InputSystem.settings, _previousSettings), cursorRestored = !_globalsCaptured || Cursor.lockState == _cursorLock && Cursor.visible == _cursorVisible;
            if (_trace != null && _globalsCaptured) { _trace.inputSettingsRestored = settingsRestored; _trace.cursorRestored = cursorRestored; _trace.inputSettingsAfterId = InputSystem.settings != null ? InputSystem.settings.GetInstanceID() : 0; _trace.cursorLockAfter = Cursor.lockState.ToString(); _trace.cursorVisibleAfter = Cursor.visible; }
            var files = _disk.Select(p => new DiskRow { path = p.Key, before = p.Value, after = File.Exists(p.Key) ? Hash(p.Key) : "MISSING" }).ToArray();
            var changes = _memory.Where(p => p.Key == null || EditorJsonUtility.ToJson(p.Key) != p.Value).Select(p => new MemoryRow { path = p.Key == null ? "DESTROYED" : AssetDatabase.GetAssetPath(p.Key), name = p.Key == null ? "DESTROYED" : p.Key.name, beforeJson = p.Value, afterJson = p.Key == null ? "DESTROYED" : EditorJsonUtility.ToJson(p.Key) }).ToArray();
            if (_trace != null) { _trace.sourcesUnchanged = files.All(p => p.before == p.after); _trace.sourceMemoryUnchanged = changes.Length == 0; _trace.files = files; _trace.changedMemory = changes; _trace.rows = _rows.ToArray(); _trace.events = _events.ToArray(); _trace.inputs = _inputs.ToArray(); _trace.images = _images.ToArray(); _trace.completedEntries = _entries.ToArray(); _trace.attemptedEntries = _attemptedEntries.ToArray(); _trace.attemptedEntryCount = _attemptedEntries.Count; _trace.executionCompletedEntryCount = _attemptedEntries.Count(e => e.executionCompleted); _trace.acceptedEntryCount = _attemptedEntries.Count(e => e.accepted); _trace.seenStates = _seenStates.OrderBy(s => s, StringComparer.Ordinal).ToArray(); _trace.status = !_trace.sourcesUnchanged || !_trace.sourceMemoryUnchanged || !settingsRestored || !cursorRestored ? "protection-failed" : _completed ? "completed" : "incomplete-or-failed"; File.WriteAllText(_output + "/actual-input.json", JsonUtility.ToJson(_trace, true)); }
            _ownedAssets.Clear(); _rows.Clear(); _events.Clear(); _disk.Clear(); _memory.Clear(); _allowed.Clear(); _seen.Clear(); _locomotion.Clear(); _seenStates.Clear(); _stateGuard.Clear(); _completed = _globalsCaptured = _upperBodyCandidate = false; _previousSettings = null; _mouse = null; _keyboard = null; _scene = default; _candidateController = null; _candidateSet = null; _originalRangedKey = null;
            _inputs.Clear(); _images.Clear(); _reader = null; _launcher = null; _knife = null; _withKnife = false;
            _capturedImages.Clear();
            _entries.Clear(); _domainEntrySync = false; _entryNumber = 0;
            _attemptedEntries.Clear(); _entryFailures.Clear(); _collectEntryDiagnostics = _atSignedFloor = false;
            _freezeLoops.Clear(); _freezeEvents.Clear(); _freezeFailures.Clear(); _freezeEntryObserved = false; _expectedFreezeClipTime = 0d;
            _freezeBefore = null; _freezeAfter = null; _freezeSpeed = null; _freezePresenter = null; _freezeSequenceField = null; _freezeEnded = null; _hitStop = null;
            Assert.That(files.All(p => p.before == p.after), Is.True, "Source bytes changed; raw retained, no rollback of unknown changes."); Assert.That(changes, Is.Empty, "Source memory changed; full before/after JSON retained.");
            Assert.That(settingsRestored && cursorRestored, Is.True, "InputSettings identity and cursor state must be exactly restored; observed before/after retained.");
            Assert.That(_trace == null ? "" : _trace.imageWriteError, Is.Null.Or.Empty);
            }
            yield return null;
        }
        Vector2 PlanarHips() { Vector3 p = _actor.transform.InverseTransformPoint(_hips.position); return new Vector2(p.x, p.z); }
        void SaveCapturedImages()
        {
            foreach (var capture in _capturedImages)
                try { if (!File.Exists(_output + "/" + capture.Key)) File.WriteAllBytes(_output + "/" + capture.Key, capture.Value.EncodeToPNG()); }
                catch (Exception error) { if (_trace != null) _trace.imageWriteError = (_trace.imageWriteError ?? "") + capture.Key + ": " + error + "\n"; }
        }
        string KnownState(int hash) { string name = KnownStates.SingleOrDefault(s => Animator.StringToHash(s) == hash); Assert.That(name, Is.Not.Null, "Unexpected reachable Animator state: " + hash); Assert.That(_stateGuard.Contains(name), Is.True, "State is outside this test's exact reachable set: " + name); return name; }
        GameObject Own(string name) { var o = new GameObject(name); SceneManager.MoveGameObjectToScene(o, _scene); return o; }
        Transform OwnChild(GameObject root, string name, Vector3 position) { var t = Own(name).transform; t.SetParent(root.transform, false); t.localPosition = position; return t; }
        static T Required<T>(string path) where T : Object { var o = AssetDatabase.LoadAssetAtPath<T>(path); Assert.That(o, Is.Not.Null, path); return o; }
        static string Doc(string yaml, int kind, long id) { var m = Regex.Match(yaml.Replace("\r", ""), "(?ms)^--- !u!" + kind + " &" + id + "\\n.*?(?=^---|\\z)"); Assert.That(m.Success, Is.True, "Exact authored record missing: " + id); return m.Value; }
        static float Number(string text, string name) { var m = Regex.Match(text, "(?m)^  " + Regex.Escape(name) + ": ([^\\n]+)$"); Assert.That(m.Success, Is.True, name); float v = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False); return v; }
        static Vector3 Vector(string text, string name) { var m = Regex.Match(text, "(?m)^  " + Regex.Escape(name) + ": \\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\\}"); Assert.That(m.Success, Is.True, name); return new Vector3(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)); }
        static string AssetPath(string text, string name) { var m = Regex.Match(text, "(?m)^  " + Regex.Escape(name) + ": \\{fileID: [^,]+, guid: ([a-f0-9]{32}), type: [^}]+\\}"); Assert.That(m.Success, Is.True, name); return AssetDatabase.GUIDToAssetPath(m.Groups[1].Value); }
        static void CopyFloats(Object target, string yaml, params string[] fields) { var so = new SerializedObject(target); foreach (string f in fields) { var p = so.FindProperty(f); Assert.That(p, Is.Not.Null, f); p.floatValue = Number(yaml, f); } so.ApplyModifiedPropertiesWithoutUndo(); }
        static void SetBool(Object target, string field, bool value) { var so = new SerializedObject(target); so.FindProperty(field).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    }
}
#endif
