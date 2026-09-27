using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class NaturalPlayerContactTests
    {
        internal static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);

        [UnityTest]
        public IEnumerator CaptureSevenActionsThroughNaturalPlayerLoop()
        {
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-natural-player"))
                Assert.Ignore("Explicit natural graphics observation only; not an alignment assertion.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Graphics required.");
            int oldRate = UnityEngine.Application.targetFrameRate, oldVsync = QualitySettings.vSyncCount;
            string root = Path.GetFullPath("Builds/ArtReview/0.9.3-player-natural-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(root);
            try
            {
                QualitySettings.vSyncCount = 0;
                UnityEngine.Application.targetFrameRate = 120;
                bool lightSide = Environment.GetCommandLineArgs().Contains("-emberfall-light-side-only");
                bool lightEnvelope = lightSide || Environment.GetCommandLineArgs().Contains("-emberfall-light-envelope");
                var scenarios = lightSide ? new[] { "light-side-initial", "light-side" } : lightEnvelope
                    ? new[] { "light-near", "light-middle", "light-far" }
                    : new[] { "light-combo", "heavy", "sweep", "knife", "execution" };
                foreach (string captureName in scenarios)
                {
                    string scenario = lightEnvelope ? "light-combo" : captureName;
                    float fixtureDistance = lightSide ? 1.25f : lightEnvelope
                        ? (captureName == "light-near" ? 1f : captureName == "light-middle" ? 1.25f : 1.65f)
                        : (scenario == "knife" ? 4f : 1.25f);
                    // Same radial distance as middle; target is 30 degrees to actor's left.
                    Vector3 fixtureOffset = lightSide
                        ? Quaternion.Euler(0, -30f, 0) * Vector3.back * fixtureDistance
                        : Vector3.back * fixtureDistance;
                    if (Environment.GetCommandLineArgs().Contains("-emberfall-sweep-only") && scenario != "sweep") continue;
                    if (Environment.GetCommandLineArgs().Contains("-emberfall-knife-only") && scenario != "knife") continue;
                    if (Environment.GetCommandLineArgs().Contains("-emberfall-light-only") && scenario != "light-combo") continue;
                    if (Environment.GetCommandLineArgs().Contains("-emberfall-heavy-execution-only") && scenario != "heavy" && scenario != "execution") continue;
                    bool referenceOnly = Environment.GetCommandLineArgs().Contains("-emberfall-reference-only");
                    yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
                    yield return null;
                    yield return null;
                    var player = Object.FindObjectOfType<PlayerCombatActor>();
                    var enemy = Object.FindObjectOfType<MeleeEnemyActor>();
                    foreach (var actor in Object.FindObjectsOfType<MeleeEnemyActor>()) actor.enabled = false;
                    foreach (var actor in Object.FindObjectsOfType<RangedEnemyActor>()) actor.enabled = false;
                    foreach (var actor in Object.FindObjectsOfType<ShieldEnemyActor>()) actor.enabled = false;
                    foreach (var dummy in Object.FindObjectsOfType<TrainingDummy>()) dummy.gameObject.SetActive(false);
                    foreach (var rig in Object.FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
                    enemy.GetComponent<NavMeshAgent>().isStopped = true;
                    var motor = player.GetComponent<ThirdPersonMotor>();
                    var controller = player.GetComponent<CharacterController>();
                    controller.enabled = false;
                    // Fixture setup only. From warmup onward the real Motor alone owns player displacement.
                    player.transform.SetPositionAndRotation(enemy.transform.position + Vector3.up * 1.05f +
                        fixtureOffset, Quaternion.identity);
                    enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
                    motor.ResetAfterTeleport();
                    controller.enabled = true;
                    enemy.Brain.Reset();
                    GameObject referenceObject = null;
                    Collider referenceBody = null;
                    if (referenceOnly)
                    {
                        // A non-damageable copy of the SAME target volume. No target authority in its parents.
                        // Setup only: never move the reference or manually evaluate animation during capture.
                        var body = (CapsuleCollider)Field<Collider>(enemy, "_bodyCollider");
                        referenceObject = new GameObject("Non-damageable target volume");
                        referenceObject.transform.SetPositionAndRotation(body.transform.position, body.transform.rotation);
                        referenceObject.transform.localScale = body.transform.lossyScale;
                        var capsule = referenceObject.AddComponent<CapsuleCollider>();
                        capsule.center = body.center; capsule.radius = body.radius;
                        capsule.height = body.height; capsule.direction = body.direction; capsule.isTrigger = true;
                        referenceBody = capsule;
                        foreach (var collider in enemy.GetComponentsInChildren<Collider>()) collider.enabled = false;
                    }
                    if (scenario == "execution")
                        enemy.ReceiveDamage(new DamageRequest(player.CombatantId, 901,
                            enemy.Brain.Health.Maximum * .93f + enemy.Definition.Armor, 0, AttackTag.Light));
                    Physics.SyncTransforms();
                    var animator = player.GetComponentInChildren<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var cameraObject = new GameObject("Natural player contact observer");
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.CopyFrom(Camera.main);
                    camera.enabled = false;
                    camera.fieldOfView = scenario == "knife" ? 48 : 43;
                    Vector3 focus = enemy.transform.position + Vector3.up * 1.05f +
                        Vector3.back * (scenario == "knife" ? 2f : .65f);
                    camera.transform.position = focus + new Vector3(3.8f, 1.6f, -2.85f);
                    camera.transform.LookAt(focus);
                    camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                    var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
                    target.Create();
                    var recorder = cameraObject.AddComponent<NaturalPlayerContactRecorder>();
                    recorder.Initialize(player, enemy, camera, target, referenceBody);
                    bool knifePreview = scenario == "knife" && Environment.GetCommandLineArgs().Contains("-emberfall-knife-visual-preview");
                    GameObject previewWall = null;
                    if (knifePreview)
                    {
                        var preview = cameraObject.AddComponent<KnifePresentationPreview>();
                        preview.Initialize(player);
                        recorder.KnifePreview = preview;
                        if (Environment.GetCommandLineArgs().Contains("-emberfall-knife-wall-preview"))
                        {
                            previewWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            previewWall.name = "Preview obstruction (fixture only)";
                            previewWall.transform.position = new Vector3(player.transform.position.x, 1.5f, player.transform.position.z + 1f);
                            previewWall.transform.localScale = new Vector3(2, 3, .15f);
                            Physics.SyncTransforms();
                        }
                    }
                    try
                    {
                        for (int i = 0; i < 60; i++) { recorder.Render(); yield return null; }
                        Assert.That(motor.enabled && controller.enabled && player.enabled, Is.True);
                        Assert.That(player.Model.State, Is.EqualTo(CombatState.Locomotion));
                        recorder.Recording = true;
                        yield return null;
                        double started = Time.realtimeSinceStartupAsDouble;
                        if (scenario == "execution")
                        {
                            // Reference has no authoritative target: play the same domain/presenter state,
                            // without claiming/holding/damaging an enemy. Normal evidence uses real input.
                            Assert.That(referenceOnly ? player.Model.Submit(CombatCommand.Execution) :
                                player.TryHandleExecutionInput(), Is.True);
                        }
                        else Assert.That(player.Model.Submit(scenario == "light-combo" ? CombatCommand.LightAttack :
                            scenario == "heavy" ? CombatCommand.HeavyPressed : scenario == "sweep" ? CombatCommand.Sweep :
                            CombatCommand.RangedAttack), Is.True);
                        int queued = 0;
                        bool heavyReleased = false;
                        while (Time.realtimeSinceStartupAsDouble - started < (scenario == "light-combo" ? 2.2 : 1.8))
                        {
                            if (scenario == "light-combo" && queued < 2 &&
                                player.Model.State == (CombatState)((int)CombatState.LightAttack1 + queued) &&
                                player.Model.StateElapsed >= player.Model.LightRecoveryStart - .045f)
                            { player.Model.Submit(CombatCommand.LightAttack); queued++; }
                            if (scenario == "heavy" && !heavyReleased && player.Model.State == CombatState.HeavyCharge &&
                                player.Model.StateElapsed >= .56f)
                            { player.Model.Submit(CombatCommand.HeavyReleased); heavyReleased = true; }
                            yield return null;
                        }
                        recorder.Recording = false;
                        var report = recorder.Save(root, knifePreview ? captureName + (previewWall != null ? "-visual-wall-preview" : "-visual-preview") : captureName,
                            referenceOnly, fixtureDistance, fixtureOffset);
                        if (knifePreview)
                        {
                            Assert.That(report.frames.Where(f => f.state == "RangedAttack").All(f => !f.swordVisible && !f.swordTrailVisible), Is.True);
                            Assert.That(report.frames.Any(f => f.heldKnifeVisible), Is.True);
                            Assert.That(report.frames.Any(f => f.flightKnifeVisible), Is.True);
                            Assert.That(report.frames.All(f => f.flightKnifeVisible == f.projectilePresent), Is.True, "No orphan visuals after authority finishes.");
                            Assert.That(report.frames.All(f => f.authorityMutation == 0), Is.True);
                            Assert.That(report.frames.Last().swordVisible, Is.True, "Restore sword on action exit.");
                            if (previewWall != null) Assert.That(report.frames.Last().targetHealth, Is.EqualTo(report.frames[0].targetHealth));
                        }
                        if (referenceOnly) Assert.That(report.frames.Select(f => f.targetHealth).Distinct().Count(), Is.EqualTo(1));
                        Assert.That(report.frames.All(f => f.timeScale == 1 && f.captureDeltaTime == 0), Is.True);
                        // A production hit stop is allowed and recorded, unlike artificial time stepping.
                        Assert.That(report.frames.Any(f => scenario == "execution" ? f.executionSequence > 0 : f.sequence > 0), Is.True);
                        Debug.Log($"[NATURAL_PLAYER] scenario={captureName} distance={fixtureDistance:R} frames={report.frames.Length} maxActionGap={report.maxActionGap:R} output={root}");
                    }
                    finally
                    {
                        recorder.DisposeFrames();
                        target.Release();
                        Object.Destroy(target);
                        Object.Destroy(cameraObject);
                        if (referenceObject != null) Object.Destroy(referenceObject);
                        if (previewWall != null) Object.Destroy(previewWall);
                    }
                }
            }
            finally { QualitySettings.vSyncCount = oldVsync; UnityEngine.Application.targetFrameRate = oldRate; }
        }
    }

    [DefaultExecutionOrder(30000)]
    public sealed class NaturalPlayerContactRecorder : MonoBehaviour
    {
        [Serializable] public sealed class Frame
        {
            public int index, unityFrame, sequence, rangedSequence, executionSequence;
            public double realtime;
            public float deltaTime, timeScale, captureDeltaTime, elapsed, duration, animatorSpeed, targetHealth;
            public float currentNormalized, nextNormalized, currentClipSeconds, nextClipSeconds, bladeColliderGap;
            public string state, currentClip, nextClip, combatEvent;
            public bool damageWindow, transition, projectilePresent;
            public Vector3 playerPosition, playerForward, targetPosition, bladeRoot, bladeTip, projectilePosition, hand;
            public bool knifePreview, heldKnifeVisible, flightKnifeVisible, swordVisible, swordTrailVisible;
            public float bridgeAge, authorityMutation;
            public Vector3 knifeVisualPosition, knifeGripPosition;
        }
        [Serializable] public sealed class ClipBinding
        { public string state, path, hash; public float length; }
        [Serializable] public sealed class Report
        {
            public string scope = "Real PlayerCombatActor/Motor/Presenter and damage queries through natural PlayerLoop; fixture target AI disabled, actors positioned before warmup only. Production hit stop retained. No manual Tick/Animator.Update/Simulate/timeScale. Offscreen observer, not human gameplay/FPS; no automatic contact acceptance.";
            public string scenario, targetName;
            public string tuningHash;
            public bool referenceOnly;
            public bool testOnlyKnifePresentation;
            public float previewBridgeDuration, previewGripToCenter;
            public float maxActionGap, fixtureDistance;
            public Vector3 fixturePlayerOffset;
            public Frame[] frames;
            public ClipBinding[] clips;
        }
        public bool Recording;
        public KnifePresentationPreview KnifePreview;
        private PlayerCombatActor _player;
        private MeleeEnemyActor _enemy;
        private Animator _animator;
        private Transform _bladeRoot, _bladeTip;
        private Collider _body;
        private Camera _camera;
        private RenderTexture _target;
        private readonly List<Frame> _frames = new List<Frame>();
        private readonly List<Texture2D> _images = new List<Texture2D>();
        private readonly List<AnimatorClipInfo> _current = new List<AnimatorClipInfo>();
        private readonly List<AnimatorClipInfo> _next = new List<AnimatorClipInfo>();
        public void Initialize(PlayerCombatActor player, MeleeEnemyActor enemy, Camera camera, RenderTexture target, Collider reference = null)
        {
            _player = player; _enemy = enemy; _camera = camera; _target = target;
            _animator = player.GetComponentInChildren<Animator>();
            var trail = player.GetComponent<SwordTrailPresenter>();
            _bladeRoot = NaturalPlayerContactTests.Field<Transform>(trail, "_bladeRoot");
            _bladeTip = NaturalPlayerContactTests.Field<Transform>(trail, "_bladeTip");
            _body = reference != null ? reference : NaturalPlayerContactTests.Field<Collider>(enemy, "_bodyCollider");
        }
        public void Render() => RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
        private void LateUpdate()
        {
            if (!Recording) return;
            _animator.GetCurrentAnimatorClipInfo(0, _current);
            _animator.GetNextAnimatorClipInfo(0, _next);
            float currentN = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float nextN = _animator.GetNextAnimatorStateInfo(0).normalizedTime;
            var projectile = Object.FindObjectsOfType<PlayerThrowingKnifeProjectile>().FirstOrDefault(p => p.IsInFlight);
            float gap = float.MaxValue;
            for (int i = 0; i <= 24; i++)
            {
                Vector3 sample = Vector3.Lerp(_bladeRoot.position, _bladeTip.position, i / 24f);
                gap = Mathf.Min(gap, Vector3.Distance(sample, _body.ClosestPoint(sample)));
            }
            _frames.Add(new Frame {
                index = _frames.Count, unityFrame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                deltaTime = Time.deltaTime, timeScale = Time.timeScale, captureDeltaTime = Time.captureDeltaTime,
                sequence = _player.Model.AttackSequence, rangedSequence = _player.Model.RangedReleaseSequence,
                executionSequence = _player.Model.ExecutionResolveSequence, elapsed = _player.Model.StateElapsed,
                duration = _player.Model.StateDuration, state = _player.Model.State.ToString(),
                animatorSpeed = _animator.speed, targetHealth = _enemy.Brain.Health.Current,
                damageWindow = _player.Model.IsDamageWindowOpen, transition = _animator.IsInTransition(0),
                currentNormalized = currentN, nextNormalized = nextN,
                currentClip = _current.Count > 0 ? _current[0].clip.name : "", nextClip = _next.Count > 0 ? _next[0].clip.name : "",
                currentClipSeconds = _current.Count > 0 ? currentN * _current[0].clip.length : -1,
                nextClipSeconds = _next.Count > 0 ? nextN * _next[0].clip.length : -1,
                combatEvent = _player.LastCombatEvent, bladeColliderGap = _body.enabled ? gap : -1,
                playerPosition = _player.transform.position, playerForward = _player.transform.forward,
                targetPosition = _enemy.transform.position, bladeRoot = _bladeRoot.position, bladeTip = _bladeTip.position,
                hand = _animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                projectilePresent = projectile != null, projectilePosition = projectile != null ? projectile.transform.position : Vector3.zero,
                knifePreview = KnifePreview != null,
                heldKnifeVisible = KnifePreview != null && KnifePreview.HeldVisible,
                flightKnifeVisible = KnifePreview != null && KnifePreview.FlightVisible,
                swordVisible = KnifePreview != null && KnifePreview.SwordVisible,
                swordTrailVisible = KnifePreview != null && KnifePreview.SwordTrailVisible,
                bridgeAge = KnifePreview != null ? KnifePreview.BridgeAge : -1,
                authorityMutation = KnifePreview != null ? KnifePreview.AuthorityMutation : 0,
                knifeVisualPosition = KnifePreview != null ? KnifePreview.VisualPosition : Vector3.zero,
                knifeGripPosition = KnifePreview != null ? KnifePreview.GripPosition : Vector3.zero
            });
            Render();
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try { RenderTexture.active = _target; texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0, false); texture.Apply(false); }
            finally { RenderTexture.active = old; }
            _images.Add(texture);
        }
        public Report Save(string root, string scenario, bool referenceOnly, float fixtureDistance, Vector3 fixtureOffset)
        {
            string output = Path.Combine(root, scenario);
            Directory.CreateDirectory(output);
            var report = new Report { scenario = scenario, targetName = _enemy.name, frames = _frames.ToArray(),
                referenceOnly = referenceOnly, fixtureDistance = fixtureDistance, fixturePlayerOffset = fixtureOffset };
            if (KnifePreview != null)
            {
                report.testOnlyKnifePresentation = true;
                report.previewBridgeDuration = KnifePresentationPreview.BridgeDuration;
                report.previewGripToCenter = KnifePresentationPreview.GripToCenter;
                report.scope += " TEST-ONLY presentation override: held knife and flight visual bridge; sword/sword trail and original projectile renderers hidden. No production integration or authority-origin change; cloned flight model has no trail. Harness visual approval pending.";
            }
            for (int i = 1; i < _frames.Count; i++)
                if (_frames[i].state != "Locomotion") report.maxActionGap = Mathf.Max(report.maxActionGap,
                    _frames[i].deltaTime, (float)(_frames[i].realtime - _frames[i - 1].realtime));
#if UNITY_EDITOR
            report.tuningHash = UnityEditor.AssetDatabase.GetAssetDependencyHash("Assets/_Game/Settings/CombatTuning_M1.asset").ToString();
            var set = NaturalPlayerContactTests.Field<PlayerAnimationSet>(_player.GetComponent<PlayerAnimationPresenter>(), "_animationSet");
            report.clips = new[] { CombatState.LightAttack1, CombatState.LightAttack2, CombatState.LightAttack3,
                CombatState.HeavyAttack, CombatState.Sweep, CombatState.RangedAttack, CombatState.Execution }.Select(s => {
                var clip = set.GetOfflineClip(s); string path = UnityEditor.AssetDatabase.GetAssetPath(clip);
                return new ClipBinding { state = s.ToString(), path = path, length = clip.length, hash = UnityEditor.AssetDatabase.GetAssetDependencyHash(path).ToString() };
            }).ToArray();
#endif
            for (int i = 0; i < _images.Count; i++) File.WriteAllBytes(Path.Combine(output, $"frame-{i:D4}.png"), _images[i].EncodeToPNG());
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonUtility.ToJson(report, true));
            return report;
        }
        public void DisposeFrames() { Recording = false; foreach (var image in _images) Object.Destroy(image); _images.Clear(); }
    }
}
