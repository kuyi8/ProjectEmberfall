using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
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
    public sealed class NaturalPriestContactTests
    {
        [UnityTest]
        public IEnumerator CaptureRealProjectileWithNaturalPlayerLoop()
        {
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-natural-contact"))
                Assert.Ignore("Explicit graphics evidence run only; no automatic capture on normal regression.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Natural contact images require a graphics device.");

            int oldVsync = QualitySettings.vSyncCount, oldRate = UnityEngine.Application.targetFrameRate;
            var textures = new List<Texture2D>();
            NaturalPriestContactRecorder recorder = null;
            RenderTexture target = null;
            GameObject cameraObject = null;
            string output = Path.GetFullPath("Builds/ArtReview/0.9.3-priest-natural-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            try
            {
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(Time.captureDeltaTime, Is.Zero);
                QualitySettings.vSyncCount = 0;
                UnityEngine.Application.targetFrameRate = 120;
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                yield return null;
                yield return null;
                var player = Object.FindObjectOfType<PlayerCombatActor>();
                foreach (var coordinator in Object.FindObjectsOfType<CombatEncounterCoordinator>()) coordinator.enabled = false;
                foreach (var actor in Object.FindObjectsOfType<MeleeEnemyActor>(true)) actor.enabled = false;
                foreach (var actor in Object.FindObjectsOfType<ShieldEnemyActor>(true)) actor.enabled = false;
                var priests = Object.FindObjectsOfType<RangedEnemyActor>(true);
                foreach (var actor in priests) actor.enabled = false;
                var priest = priests.First(a => a.gameObject.activeInHierarchy);
                var animator = priest.GetComponentInChildren<Animator>(true);
                var origin = (Transform)typeof(RangedEnemyActor).GetField("_castOrigin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(priest);
                var leash = priest.GetComponent<EncounterLeash>();
                var controller = player.GetComponent<CharacterController>();
                player.GetComponent<ThirdPersonMotor>().enabled = false;
                controller.enabled = false;
                bool found = false;
                for (int i = 0; i < 24; i++)
                {
                    Vector3 direction = Quaternion.Euler(0, i * 15f, 0) * Vector3.forward;
                    if (!NavMesh.SamplePosition(priest.transform.position + direction * 6.8f, out var ground, .6f, NavMesh.AllAreas)) continue;
                    Vector3 position = ground.position + Vector3.up * 1.05f;
                    if (leash != null && !leash.AllowsTarget(position)) continue;
                    player.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-direction));
                    priest.transform.rotation = Quaternion.LookRotation(direction);
                    Physics.SyncTransforms();
                    Vector3 aim = player.AimPoint.position - origin.position;
                    // Match the real finite-width projectile and the rotated muzzle, not a pre-turn center ray.
                    if (Physics.SphereCastAll(origin.position, priest.Definition.ProjectileRadius, aim.normalized,
                        aim.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(h =>
                        !h.collider.transform.IsChildOf(priest.transform) && !h.collider.transform.IsChildOf(player.transform))) continue;
                    found = true;
                    break;
                }
                Assert.That(found, Is.True, "No legal clear target position in the existing encounter.");
                controller.enabled = true;
                Physics.SyncTransforms();
                foreach (var rig in Object.FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
                Vector3 facing = Vector3.ProjectOnPlane(player.transform.position - priest.transform.position, Vector3.up).normalized;
                priest.transform.rotation = Quaternion.LookRotation(facing);
                cameraObject = new GameObject("Natural contact evidence camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(Camera.main);
                camera.enabled = false;
                camera.fieldOfView = 46;
                camera.transform.position = priest.transform.position + Vector3.Cross(Vector3.up, facing) * 4.8f + facing * 2.4f + Vector3.up * 2.8f;
                camera.transform.LookAt(priest.transform.position + Vector3.up * 1.1f + facing * .45f);
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
                target.Create();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                recorder = cameraObject.AddComponent<NaturalPriestContactRecorder>();
                recorder.Initialize(priest, player, animator, origin, camera, target, textures);
                // Warm the exact capture path before the action; no animation time is set.
                for (int i = 0; i < 45; i++)
                {
                    recorder.RenderWarmup();
                    yield return null;
                }
                priest.SetSimulationAuthority(true);
                priest.enabled = true;
                recorder.Recording = true;
                double deadline = Time.realtimeSinceStartupAsDouble + 6;
                while (!recorder.Complete && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                recorder.Recording = false;
                // Encode only after observation, so PNG compression cannot stall measured frames.
                var report = recorder.CreateReport();
                for (int i = 0; i < textures.Count; i++)
                    File.WriteAllBytes(Path.Combine(output, $"frame-{i:D4}.png"), textures[i].EncodeToPNG());
                File.WriteAllText(Path.Combine(output, "capture.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"[NATURAL_PRIEST] output={output} frames={report.frames.Length} releaseIndex={report.releaseIndex} maxCriticalGap={report.maxCriticalGap:R} projectileHit={report.projectileHit}");
                Assert.That(report.releaseIndex, Is.GreaterThan(0), "No actual projectile birth observed.");
                Assert.That(report.projectileHit, Is.True, "Actual projectile must resolve through real player damage.");
                Assert.That(report.frames.All(f => f.timeScale == 1 && f.captureDeltaTime == 0 && f.animatorSpeed > 0), Is.True);
                Assert.That(report.maxCriticalGap, Is.LessThanOrEqualTo(1f / 30f), "Critical samples too far apart; preserve as rejected evidence.");
            }
            finally
            {
                if (recorder != null) recorder.Recording = false;
                foreach (var texture in textures) Object.Destroy(texture);
                if (cameraObject != null) Object.Destroy(cameraObject);
                if (target != null) { target.Release(); Object.Destroy(target); }
                QualitySettings.vSyncCount = oldVsync;
                UnityEngine.Application.targetFrameRate = oldRate;
            }
        }
    }

    // Test assembly only, late read-only observation after the production Animator/Presenter.
    [DefaultExecutionOrder(30000)]
    public sealed class NaturalPriestContactRecorder : MonoBehaviour
    {
        [Serializable] public sealed class Frame
        {
            public int index, unityFrame, sequence, projectileId;
            public double realtime;
            public float deltaTime, timeScale, captureDeltaTime, stateElapsed, animatorSpeed;
            public float currentNormalized, nextNormalized, transitionNormalized, health;
            public string state, attack, currentClip, nextClip, aiEvent;
            public bool transition, projectilePresent;
            public Vector3 actorPosition, castOrigin, projectilePosition, rightHand, playerPosition, playerAim;
        }
        [Serializable] public sealed class Report
        {
            public string scope = "Natural Unity Update + actual actor/brain/presenter/projectile. Stationary repositioned player; other AI disabled. No manual animation/physics/particle stepping or freeze. Offscreen observer camera, not human gameplay or presented FPS.";
            public string clipPath, clipHash, actorName;
            public int releaseIndex;
            public float maxCriticalGap;
            public bool projectileHit;
            public Frame[] frames;
        }
        public bool Recording;
        public bool Complete { get; private set; }
        private RangedEnemyActor _priest;
        private PlayerCombatActor _player;
        private Animator _animator;
        private Transform _origin;
        private Camera _camera;
        private RenderTexture _target;
        private List<Texture2D> _textures;
        private readonly List<Frame> _frames = new List<Frame>();
        private readonly List<AnimatorClipInfo> _current = new List<AnimatorClipInfo>();
        private readonly List<AnimatorClipInfo> _next = new List<AnimatorClipInfo>();
        private int _releaseIndex = -1;
        private double _releasedAt;
        private float _initialHealth;
        public void Initialize(RangedEnemyActor priest, PlayerCombatActor player, Animator animator, Transform origin,
            Camera camera, RenderTexture target, List<Texture2D> textures)
        {
            _priest = priest; _player = player; _animator = animator; _origin = origin;
            _camera = camera; _target = target; _textures = textures; _initialHealth = player.Model.Health.Current;
        }
        public void RenderWarmup() => RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
        private void LateUpdate()
        {
            if (!Recording || Complete) return;
            var projectile = Object.FindObjectsOfType<RangedProjectile>().FirstOrDefault();
            _animator.GetCurrentAnimatorClipInfo(0, _current);
            _animator.GetNextAnimatorClipInfo(0, _next);
            var frame = new Frame
            {
                index = _frames.Count, unityFrame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                deltaTime = Time.deltaTime, timeScale = Time.timeScale, captureDeltaTime = Time.captureDeltaTime,
                state = _priest.State.ToString(), attack = _priest.Brain.CurrentAttack.ToString(),
                aiEvent = _priest.LastAiEvent,
                sequence = _priest.Brain.AttackSequence, stateElapsed = _priest.Brain.StateElapsed,
                animatorSpeed = _animator.speed, currentNormalized = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                nextNormalized = _animator.GetNextAnimatorStateInfo(0).normalizedTime,
                transition = _animator.IsInTransition(0), transitionNormalized = _animator.GetAnimatorTransitionInfo(0).normalizedTime,
                currentClip = _current.Count > 0 ? _current[0].clip.name : "", nextClip = _next.Count > 0 ? _next[0].clip.name : "",
                projectilePresent = projectile != null, projectileId = projectile != null ? projectile.GetInstanceID() : 0,
                projectilePosition = projectile != null ? projectile.transform.position : Vector3.zero,
                actorPosition = _priest.transform.position, castOrigin = _origin.position,
                playerPosition = _player.transform.position, playerAim = _player.AimPoint.position,
                rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand).position, health = _player.Model.Health.Current
            };
            if (_releaseIndex < 0 && projectile != null) { _releaseIndex = frame.index; _releasedAt = frame.realtime; }
            _frames.Add(frame);
            RenderWarmup();
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                RenderTexture.active = _target;
                texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0, false);
                texture.Apply(false);
                _textures.Add(texture);
            }
            finally { RenderTexture.active = old; }
            Complete = _releaseIndex >= 0 && frame.realtime - _releasedAt > .85;
        }
        public Report CreateReport()
        {
            float maxGap = 0;
            for (int i = 1; i < _frames.Count; i++)
                if (_releaseIndex >= 0 && Math.Abs(_frames[i].realtime - _releasedAt) <= .2)
                    maxGap = Mathf.Max(maxGap, (float)(_frames[i].realtime - _frames[i - 1].realtime), _frames[i].deltaTime);
            var report = new Report { frames = _frames.ToArray(), releaseIndex = _releaseIndex, maxCriticalGap = maxGap,
                projectileHit = _frames.Any(f => f.health < _initialHealth), actorName = _priest.name };
#if UNITY_EDITOR
            report.clipPath = "Assets/_Game/Art/Animations/Player/Derived/A_Priest_ProjectileRelease.anim";
            report.clipHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(report.clipPath).ToString();
#endif
            return report;
        }
    }
}
