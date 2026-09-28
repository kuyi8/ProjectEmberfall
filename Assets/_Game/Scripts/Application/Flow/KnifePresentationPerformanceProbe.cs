#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit standalone A/B fixture. Real Update/domain/presenter; no synthetic ticks or readbacks.</summary>
    [DefaultExecutionOrder(21000)]
    public sealed class KnifePresentationPerformanceProbe : MonoBehaviour
    {
        [Serializable] private sealed class Shot
        {
            public int ordinal, sequence;
            public double scheduled, submitted, released;
            public bool accepted;
        }
        [Serializable] private sealed class Result
        {
            public string scope = "CombatGym offline candidate, 15 legal throws at 1+4*n seconds; only visual trail differs. Not worst-case/network/display timing.";
            public string mode, targetName, avatar, gripSourceHash;
            public int targetId, releases, cleanups, flyingFrames, trailFrames, peakActive, visualCapacity, pendingProjectiles, remainingTrailPoints;
            public bool accepted, externalPresentationMeasured = false;
            public Vector3 actorPosition, cameraPosition;
            public Quaternion cameraRotation;
            public float initialHealth, finalHealth;
            public Shot[] shots;
            public KnifeQueryTrace.Entry[] queries;
        }
        private PresentedPerformanceProbe _probe;
        private PlayerCombatActor _actor;
        private PlayerThrowingKnifeLauncher _launcher;
        private PlayerKnifePresentation _visual;
        private TrainingDummy _target;
        private KnifeGripPose _pose;
        private string _mode, _output;
        private bool _recording, _complete, _wasFlying;
        private double _deadline;
        private int _releases, _cleanups, _flyingFrames, _trailFrames, _peak;
        private float _initialHealth;
        private readonly List<Shot> _shots = new List<Shot>(15);
        private readonly List<KnifeQueryTrace.Entry> _queries = new List<KnifeQueryTrace.Entry>(3000);
        private static string Argument(string key) => Environment.GetCommandLineArgs()
            .FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?.Substring(key.Length + 1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            string mode = Argument("-emberfall-knife-performance");
            if (mode == null) return;
            if ((mode != "on" && mode != "off") || UnityEngine.Application.isBatchMode ||
                !Environment.GetCommandLineArgs().Contains("-emberfall-presented-performance"))
                throw new InvalidOperationException("Knife A/B requires on/off and normal-window presented-performance.");
            var root = new GameObject("Development Knife Performance Fixture");
            DontDestroyOnLoad(root);
            var fixture = root.AddComponent<KnifePresentationPerformanceProbe>();
            fixture._mode = mode;
            fixture._output = Path.GetDirectoryName(Path.GetFullPath(Argument("-emberfall-frame-output")));
            fixture._deadline = Time.realtimeSinceStartupAsDouble + 115;
        }

        private IEnumerator Start()
        {
            // Respect bootstrap before loading the test scene. This scene does not write a playthrough save.
            while (SceneManager.GetActiveScene().name != "01_MainMenu") yield return null;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            UnityEngine.Application.runInBackground = true;
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            _probe = FindObjectOfType<PresentedPerformanceProbe>();
            _actor = FindObjectOfType<PlayerCombatActor>();
            foreach (var item in FindObjectsOfType<MonoBehaviour>())
                if (item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor) item.enabled = false;
            foreach (var agent in FindObjectsOfType<NavMeshAgent>()) if (agent.isOnNavMesh) agent.isStopped = true;
            foreach (var rig in FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
            _actor.GetComponent<PlayerInputReader>().enabled = false;
            _target = FindObjectsOfType<TrainingDummy>().OrderBy(x => x.transform.position.x)
                .ThenBy(x => x.transform.position.z).First();
            var controller = _actor.GetComponent<CharacterController>();
            controller.enabled = false;
            _actor.transform.SetPositionAndRotation(new Vector3(_target.transform.position.x, 1.05f,
                _target.transform.position.z - 4f), Quaternion.identity);
            _actor.GetComponent<ThirdPersonMotor>().ResetAfterTeleport();
            controller.enabled = true;
            Physics.SyncTransforms(); // Fixture setup only; no writes during the timed workload.
            var camera = Camera.main;
            Vector3 focus = _target.AimPoint.position + Vector3.back * 2;
            camera.transform.position = focus + new Vector3(3.8f, 1.6f, -2.85f);
            camera.transform.LookAt(focus);
            camera.fieldOfView = 48;
            _launcher = _actor.GetComponent<PlayerThrowingKnifeLauncher>();
            _visual = _launcher.PreparePresentationCandidate();
            _pose = Resources.FindObjectsOfTypeAll<KnifeGripPose>().Single(x => x.name == "KnifeGripPose_Ranger");
            _visual.ConfigureGrip(_pose);
            _visual.DiagnosticTrailEnabled = _mode == "on";
            _actor.RangedAttackReleased += Released;
            KnifeQueryTrace.Observer += ObserveQuery;
            yield return new WaitForSeconds(2);
            double groundDeadline = Time.realtimeSinceStartupAsDouble + 2;
            while (!controller.isGrounded && Time.realtimeSinceStartupAsDouble < groundDeadline) yield return null;
            Debug.Log($"[KNIFE_AB_FIXTURE] grounded={controller.isGrounded} position={_actor.transform.position:F4} state={_actor.Model.State} motor={_actor.GetComponent<ThirdPersonMotor>().enabled} controller={controller.enabled} scale={Time.timeScale} cooldown={_actor.Model.RangedCooldownRemaining} target={_target.name}:{_target.transform.position:F4}");
            if (!controller.isGrounded || !_actor.Model.Submit(CombatCommand.RangedAttack))
            {
                Debug.LogError("Fixture not grounded/ready for warmup throw.");
                _complete = true;
                UnityEngine.Application.Quit(2);
                yield break;
            }
            yield return new WaitForSeconds(10); // Includes one actual throw, shader/pool/animation warmup.
            _initialHealth = _target.HealthNormalized;
            string trigger = Argument("-emberfall-frame-start-file");
            if (File.Exists(trigger)) throw new InvalidOperationException("Refuse a stale sample trigger.");
            Directory.CreateDirectory(_output);
            File.WriteAllText(trigger, "ready");
            while (!_probe.IsSampling) yield return null;
            _recording = true;
            Debug.Log($"[KNIFE_AB_BEGIN] mode={_mode} target={_target.name} id={_target.CombatantId} capacity={_visual.VisualCapacity}");
        }

        private void Update()
        {
            if (_complete) return;
            if (Time.realtimeSinceStartupAsDouble > _deadline)
            { Debug.LogError("Knife performance fixture timed out; no passing result."); _complete = true; UnityEngine.Application.Quit(3); return; }
            if (!_recording) return;
            if (!_probe.IsSampling) { Complete(); return; }
            double elapsed = Time.realtimeSinceStartupAsDouble - _probe.SampleStartedAt;
            if (_shots.Count >= 15 || elapsed < 1 + 4 * _shots.Count) return;
            var shot = new Shot { ordinal = _shots.Count, scheduled = 1 + 4 * _shots.Count, submitted = elapsed };
            shot.accepted = _actor.Model.Submit(CombatCommand.RangedAttack);
            shot.sequence = _actor.Model.AttackSequence;
            _shots.Add(shot);
        }

        private void LateUpdate()
        {
            if (!_recording || _complete || !_probe.IsSampling) return;
            bool flying = _visual.FlightVisible;
            if (flying) _flyingFrames++;
            if (_visual.TrailPointCount > 1) _trailFrames++;
            _peak = Mathf.Max(_peak, _visual.ActiveVisualCount);
            if (_wasFlying && !flying) _cleanups++;
            _wasFlying = flying;
        }

        private void Released(RangedAttackRelease release)
        {
            if (!_recording || !_probe.IsSampling) return;
            _releases++;
            var shot = _shots.LastOrDefault(x => x.sequence == release.AttackSequence);
            if (shot != null) shot.released = Time.realtimeSinceStartupAsDouble - _probe.SampleStartedAt;
        }

        private void ObserveQuery(KnifeQueryTrace.Entry entry)
        {
            if (_recording && _probe.IsSampling) _queries.Add(entry);
        }

        private void Complete()
        {
            _complete = true;
            var camera = Camera.main;
            var result = new Result { mode = _mode, targetName = _target.name, targetId = _target.CombatantId,
                avatar = _pose.avatarPath, gripSourceHash = _pose.sourceHash,
                releases = _releases, cleanups = _cleanups, flyingFrames = _flyingFrames, trailFrames = _trailFrames,
                peakActive = _peak, visualCapacity = _visual.VisualCapacity, pendingProjectiles = _launcher.ActiveProjectileCount,
                remainingTrailPoints = _visual.TrailPointCount, initialHealth = _initialHealth, finalHealth = _target.HealthNormalized,
                actorPosition = _actor.transform.position, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                shots = _shots.ToArray(), queries = _queries.ToArray() };
            result.accepted = _shots.Count == 15 && _shots.All(x => x.accepted && x.released >= x.submitted) &&
                _releases == 15 && _cleanups == 15 && _flyingFrames > 0 && _peak == 1 && _visual.VisualCapacity == 4 &&
                _launcher.ActiveProjectileCount == 0 && _visual.TrailPointCount == 0 &&
                (_mode == "on" ? _trailFrames > 0 : _trailFrames == 0) &&
                _queries.Count(x => x.kind == "sweep" && x.hit && x.targetId == _target.CombatantId) == 15;
            File.WriteAllText(Path.Combine(_output, "knife-workload.json"), JsonUtility.ToJson(result, true));
            Debug.Log($"[KNIFE_AB_COMPLETE] mode={_mode} accepted={result.accepted} releases={_releases} cleanups={_cleanups} trailFrames={_trailFrames}");
            UnityEngine.Application.Quit(result.accepted ? 0 : 2);
        }

        private void OnDestroy()
        {
            KnifeQueryTrace.Observer -= ObserveQuery;
            if (_actor != null) _actor.RangedAttackReleased -= Released;
        }
    }
}
#endif
