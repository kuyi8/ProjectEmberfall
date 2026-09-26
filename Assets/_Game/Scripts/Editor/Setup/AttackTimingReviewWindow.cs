using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public sealed class AttackTimingReviewWindow : EditorWindow
    {
        private int _selected;
        private float _seconds;
        private Animator _actor;
        private bool _ownsSampling;
        private string _evidence = "", _observer = "", _observation = "";
        private bool _naturalReviewed;
        private float _naturalContact = -1, _firstDamage = -1, _sampleGap = -1;

        [MenuItem("Emberfall/Review/Attack Timing/Observe Contact")]
        private static void Open() => GetWindow<AttackTimingReviewWindow>("Attack Contact");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Pose preview is not natural contact proof. Confirm only with reviewed natural frames. Synchronization uses domain timestamps, not converted clip seconds. No runtime authority changes.", MessageType.Info);
            if (EditorApplication.isPlayingOrWillChangePlaymode) { StopSampling(); return; }
            var maps = AttackTimingAudit.ReadMappings();
            int selected = EditorGUILayout.Popup("Action", _selected, maps.Select(m => m.id).ToArray());
            if (selected != _selected) { StopSampling(); _seconds = 0; _selected = selected; _naturalReviewed = false; _naturalContact = _firstDamage = _sampleGap = -1; }
            var map = maps[_selected];
            Animator actor = (Animator)EditorGUILayout.ObjectField("Scene actor Animator", _actor, typeof(Animator), true);
            if (actor != _actor) { StopSampling(); _actor = actor; }
            _seconds = EditorGUILayout.Slider("Clip seconds", _seconds, map.clipStart, map.clipEnd);
            EditorGUILayout.LabelField("Frame", (_seconds * map.clip.frameRate).ToString("F2"));
            _evidence = EditorGUILayout.TextField("Evidence file", _evidence);
            _observer = EditorGUILayout.TextField("Observed by", _observer);
            _observation = EditorGUILayout.TextField("What crosses target", _observation);
            _naturalReviewed = EditorGUILayout.Toggle("Natural evidence reviewed", _naturalReviewed);
            if (AttackTimingAudit.RequiresSynchronization(map))
            {
                _naturalContact = EditorGUILayout.FloatField("Natural contact (domain sec)", _naturalContact);
                _firstDamage = EditorGUILayout.FloatField("First damage (domain sec)", _firstDamage);
                _sampleGap = EditorGUILayout.FloatField("Maximum sample gap (sec)", _sampleGap);
            }
            using (new EditorGUI.DisabledScope(_actor == null || !_actor.gameObject.scene.IsValid() ||
                EditorUtility.IsPersistent(_actor)))
            {
                if (GUILayout.Button("Sample pose (restored on close)"))
                {
                    if (!_ownsSampling && AnimationMode.InAnimationMode())
                        throw new InvalidOperationException("Another animation preview owns sampling. Close it first.");
                    if (!_ownsSampling) { AnimationMode.StartAnimationMode(); _ownsSampling = true; }
                    AnimationMode.BeginSampling();
                    try { AnimationMode.SampleAnimationClip(_actor.gameObject, map.clip, _seconds); }
                    finally { AnimationMode.EndSampling(); }
                    SceneView.RepaintAll();
                }
            }
            if (GUILayout.Button("Stop preview / restore scene")) StopSampling();
            using (new EditorGUI.DisabledScope(!_naturalReviewed || !File.Exists(_evidence) ||
                       string.IsNullOrWhiteSpace(_observer) || string.IsNullOrWhiteSpace(_observation)))
            {
                if (GUILayout.Button("Confirm observed contact in annotation asset"))
                {
                    AttackTimingAudit.CreateMissingAnnotations();
                    var asset = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
                    Undo.RecordObject(asset, "Confirm attack contact observation");
                    var contact = asset.contacts.Single(c => c.actionId == map.id);
                    contact.clip = map.clip;
                    contact.contactSeconds = _seconds;
                    contact.sourceFrame = Mathf.RoundToInt(_seconds * map.clip.frameRate);
                    contact.observedBy = _observer;
                    contact.observation = _observation;
                    contact.evidencePath = _evidence;
                    contact.observedClipHash = AttackTimingAudit.ClipHash(map.clip);
                    contact.confirmed = true;
                    contact.synchronizationObserved = AttackTimingAudit.RequiresSynchronization(map) && _naturalReviewed;
                    contact.naturalContactTime = _naturalContact;
                    contact.firstDamageTime = _firstDamage;
                    contact.maxSampleGap = _sampleGap;
                    contact.observedTimingHash = AttackTimingAudit.TimingHash(map);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }
            }
            if (GUILayout.Button("Export audit (does not imply acceptance)")) AttackTimingAudit.Export();
        }

        private void StopSampling()
        {
            if (_ownsSampling) AnimationMode.StopAnimationMode();
            _ownsSampling = false;
        }
        private void OnDisable() => StopSampling();

        // Coarse visual evidence only. No guessed contact markers are written by this entry point.
        [MenuItem("Emberfall/Review/Attack Timing/Capture Initial Pose Sequences")]
        public static void CaptureInitialPoseSequences()
        {
            if (AnimationMode.InAnimationMode() || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop playback/animation preview first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard scene changes explicitly before capture.");
                if (!UnityEngine.Application.isBatchMode && string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Save the untitled scene before capture so it can be restored safely.");
            }
            var previous = EditorSceneManager.GetSceneManagerSetup();
            string output = Path.GetFullPath("Builds/ArtReview/0.9.3-timing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerCombatActor>(true)).First();
                var melee = roots.SelectMany(r => r.GetComponentsInChildren<MeleeEnemyActor>(true)).First();
                var priest = roots.SelectMany(r => r.GetComponentsInChildren<RangedEnemyActor>(true)).First();
                var maps = AttackTimingAudit.ReadMappings();
                Directory.CreateDirectory(output);
                foreach (string id in new[] { "player.sweep", "fogwalker.combo1", "priest.projectile" })
                {
                    var map = maps.Single(m => m.id == id);
                    GameObject actor = id.StartsWith("player.") ? player.gameObject : id.StartsWith("fogwalker.") ? melee.gameObject : priest.gameObject;
                    for (int frame = 0; frame <= 12; frame++)
                        M6ArtReviewTool.CaptureActorCloseup(Path.Combine(output, id + "-" + frame.ToString("D2") + ".png"),
                            actor, 1f, map.clip, frame / 12f);
                    File.WriteAllText(Path.Combine(output, id + ".txt"),
                        $"Actor={actor.name}\nClip={AssetDatabase.GetAssetPath(map.clip)}\nLength={map.clip.length:R}\nTime(i)=i*Length/12, i=0..12\nCoarse static poses, not confirmed contact, no crossfade/freeze or timing authority.\n");
                }
            }
            finally
            {
                if (previous.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(previous);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("[ATTACK_TIMING_POSES] " + output);
        }
    }
}
