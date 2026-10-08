using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>One bounded, passive ordinary-attack observation. Never advances or disables combat.</summary>
    [InitializeOnLoad]
    public static class EnemyContactTimingReview
    {
        internal const string Key = "Emberfall.EnemyContact.";
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Prefix = "Assets/_Game/Scenes/__EnemyContact_";
        static Gamepad pad;

        static EnemyContactTimingReview() { EditorApplication.playModeStateChanged += OnMode; }

        public static string BeginScorched()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                scene.isDirty || scene.path != Source || UnityEngine.SceneManagement.SceneManager.sceneCount != 1 ||
                SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Clean idle formal Valley required; no overlapping observation.");
            string output = Path.GetFullPath("Builds/ArtReview/p1-time/scorched-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            string temp = Prefix + Guid.NewGuid().ToString("N") + ".unity";
            Directory.CreateDirectory(output);
            EditorSceneManager.SaveScene(scene, temp, true);
            try
            {
                scene = EditorSceneManager.OpenScene(temp);
                var enemy = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_Courtyard");
                var player = Object.FindObjectOfType<PlayerCombatActor>();
                var cc = player.GetComponent<CharacterController>();
                Vector3 foot = enemy.transform.position + enemy.transform.forward * 1.1f;
                if (!Physics.Raycast(foot + Vector3.up * 2f, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < .6f)
                    throw new InvalidOperationException("Fixture spawn has no existing walkable floor.");
                Vector3 spawn = floor.point + Vector3.up * (cc.height * .5f - cc.center.y + .05f);
                Quaternion facing = Quaternion.LookRotation(-enemy.transform.forward, Vector3.up);
                player.transform.SetPositionAndRotation(spawn, facing);
                var flow = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
                ((Transform)flow.FindProperty("_defaultCheckpoint").objectReferenceValue).SetPositionAndRotation(spawn, facing);
                flow.FindProperty("_saveFileName").stringValue = Path.Combine(output, "isolated-save.json");
                flow.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene);
                SessionState.SetString(Key + "output", output);
                SessionState.SetString(Key + "temp", temp);
                SessionState.SetBool(Key + "active", true);
                M2LaunchIntent.RequestNewGame();
                EditorApplication.isPlaying = true;
                return output;
            }
            catch
            {
                EditorSceneManager.OpenScene(Source);
                AssetDatabase.DeleteAsset(temp);
                throw;
            }
        }

        static void OnMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                pad = InputSystem.AddDevice<Gamepad>("EnemyContactObservation");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                new GameObject("EDITOR_ONLY_PassiveEnemyContact").AddComponent<EnemyContactTimingRecorder>();
            }
            if (mode == PlayModeStateChange.ExitingPlayMode && pad != null && pad.added)
                InputSystem.RemoveDevice(pad);
            if (mode == PlayModeStateChange.EnteredEditMode)
            {
                string temp = SessionState.GetString(Key + "temp", "");
                if (!temp.StartsWith(Prefix, StringComparison.Ordinal) || !temp.EndsWith(".unity", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected fixture cleanup target.");
                EditorSceneManager.OpenScene(Source);
                AssetDatabase.DeleteAsset(temp);
                SessionState.SetBool(Key + "active", false);
                SessionState.EraseString(Key + "temp");
            }
        }
    }

    // Editor assembly only. Observe after production presenters; no Tick/Animator.Update/physics stepping.
    [DefaultExecutionOrder(30000)]
    public sealed class EnemyContactTimingRecorder : MonoBehaviour
    {
        [Serializable] public sealed class Frame
        {
            public int index, unityFrame, sequence;
            public double realtime;
            public float deltaTime, timeScale, captureDeltaTime, elapsed, health, animatorSpeed, normalized, bladeColliderGap;
            public string state, attack, aiEvent, clip;
            public bool damageWindow, transition;
            public Vector2 actualMove, actualLook;
            public Vector3 enemyPosition, playerPosition, bladeRoot, bladeTip;
        }
        [Serializable] public sealed class Report
        {
            public string scope = "Pre-Play nearby-spawn fixture; real stationary input, unchanged production AI/coordinators/collision/navigation/authority. LateUpdate observation and offscreen camera only. No manual animation/physics/domain stepping, injected damage, AI disable, invulnerability or progress injection. NOT natural-route/difficulty acceptance. Blade-vs-controller gap is a geometric diagnostic, NOT rendered contact confirmation.";
            public string savePath, actor, clipPath, clipHash, error;
            public float maximumCriticalGap;
            public bool saveIsolated, finishedFirstAttack;
            public string[] visibleMeshes;
            public Frame[] frames;
        }
        readonly List<Frame> frames = new List<Frame>();
        readonly List<Texture2D> images = new List<Texture2D>();
        readonly List<AnimatorClipInfo> clips = new List<AnimatorClipInfo>();
        ShieldEnemyActor enemy;
        PlayerCombatActor player;
        Animator animator;
        PlayerInputReader input;
        MeshFilter sword;
        CharacterController body;
        Camera camera;
        RenderTexture target;
        Report report;
        string output;
        double start;
        bool recording, done;

        void Awake() { start = Time.realtimeSinceStartupAsDouble; output = SessionState.GetString(EnemyContactTimingReview.Key + "output", ""); }

        void LateUpdate()
        {
            if (done) return;
            try
            {
                if (Time.realtimeSinceStartupAsDouble - start > 12) { Finish("Bounded observation timeout."); return; }
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                if (flow == null || !flow.IsInitialized) return;
                if (report == null)
                {
                    report = new Report { savePath = flow.SavePath };
                    report.saveIsolated = Path.GetFullPath(flow.SavePath) == Path.Combine(output, "isolated-save.json");
                    if (!report.saveIsolated) throw new InvalidOperationException("Actual SavePath is not isolated.");
                    enemy = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_Courtyard");
                    player = Object.FindObjectOfType<PlayerCombatActor>();
                    animator = enemy.GetComponentInChildren<Animator>();
                    input = player.GetComponent<PlayerInputReader>(); body = player.GetComponent<CharacterController>();
                    sword = enemy.GetComponentsInChildren<MeshFilter>().Single(m => m.sharedMesh != null && m.sharedMesh.name == "ShortSword");
                    report.actor = enemy.name;
                    report.visibleMeshes = enemy.GetComponentsInChildren<MeshFilter>().Where(m => m.GetComponent<Renderer>() != null && m.GetComponent<Renderer>().enabled).Select(m => m.name + "/" + m.sharedMesh.name).ToArray();
                    camera = gameObject.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
                    Vector3 focus = enemy.transform.position + Vector3.up + enemy.transform.forward * .5f;
                    camera.transform.position = focus + Vector3.Cross(Vector3.up, enemy.transform.forward) * 3.4f + enemy.transform.forward * 2.5f + Vector3.up * 1.2f;
                    camera.transform.LookAt(focus); camera.fieldOfView = 43f;
                    target = new RenderTexture(640, 360, 24); target.Create();
                }
                if (enemy.Brain == null || player.Model == null) return;
                if (!recording && enemy.State == ShieldEnemyState.Windup) recording = true;
                if (!recording) return;
                if (enemy.State == ShieldEnemyState.Recovery && frames.Count > 0) { report.finishedFirstAttack = true; Finish(""); return; }
                if (!player.IsAvailable) { Finish("Player died; no forced continuation."); return; }
                Bounds bounds = sword.sharedMesh.bounds;
                Vector3 root = bounds.center, tip = bounds.center;
                // The registered ShortSword source points along -Z; this includes its handle and is diagnostic only.
                root.z = bounds.max.z; tip.z = bounds.min.z;
                root = sword.transform.TransformPoint(root); tip = sword.transform.TransformPoint(tip);
                float gap = float.MaxValue;
                for (int i = 0; i <= 24; i++) { Vector3 p = Vector3.Lerp(root, tip, i / 24f); gap = Mathf.Min(gap, Vector3.Distance(p, body.ClosestPoint(p))); }
                animator.GetCurrentAnimatorClipInfo(0, clips);
                if (clips.Count > 0 && report.clipPath == null) { report.clipPath = AssetDatabase.GetAssetPath(clips[0].clip); report.clipHash = AssetDatabase.GetAssetDependencyHash(report.clipPath).ToString(); }
                frames.Add(new Frame {
                    index = frames.Count, unityFrame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble,
                    deltaTime = Time.deltaTime, timeScale = Time.timeScale, captureDeltaTime = Time.captureDeltaTime,
                    sequence = enemy.Brain.AttackSequence, state = enemy.State.ToString(), elapsed = enemy.Brain.StateElapsed,
                    attack = enemy.Brain.CurrentAttack.ToString(), aiEvent = enemy.LastAiEvent, health = player.Model.Health.Current,
                    damageWindow = enemy.Brain.IsDamageWindowOpen, transition = animator.IsInTransition(0), animatorSpeed = animator.speed,
                    normalized = animator.GetCurrentAnimatorStateInfo(0).normalizedTime, clip = clips.Count == 0 ? "" : clips[0].clip.name,
                    actualMove = input.Move, actualLook = input.Look, enemyPosition = enemy.transform.position, playerPosition = player.transform.position,
                    bladeRoot = root, bladeTip = tip, bladeColliderGap = gap
                });
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); images.Add(image); }
                finally { RenderTexture.active = previous; }
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        void Finish(string error)
        {
            if (done) return;
            done = true;
            if (report == null) report = new Report();
            report.error = error; report.frames = frames.ToArray();
            var critical = frames.Where(f => f.state == "Attack" || (f.state == "Windup" && f.elapsed >= .5f)).ToArray();
            for (int i = 1; i < critical.Length; i++) report.maximumCriticalGap = Mathf.Max(report.maximumCriticalGap, (float)(critical[i].realtime - critical[i - 1].realtime));
            for (int i = 0; i < images.Count; i++) File.WriteAllBytes(Path.Combine(output, "frame-" + i.ToString("D4") + ".png"), images[i].EncodeToPNG());
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[ENEMY_CONTACT] " + output + " error=" + error + " frames=" + frames.Count);
            EditorApplication.isPlaying = false;
        }
        void OnDestroy()
        {
            foreach (var image in images) if (image != null) Object.Destroy(image);
            if (target != null) { target.Release(); Object.Destroy(target); }
        }
    }
}
