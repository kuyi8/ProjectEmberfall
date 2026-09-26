using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class PriestProjectileAnimationSetup
    {
        // Observed source frames, not back-solved from the damage clock.
        public const float ReleaseSourceSeconds = 13f / 30f;
        public const float RecoveryEndSeconds = 1.3f;
        public const string WindupPath = M1AnimationSetup.DerivedFolder + "/A_Priest_ProjectileWindup.anim";
        public const string ReleasePath = M1AnimationSetup.DerivedFolder + "/A_Priest_ProjectileRelease.anim";

        [MenuItem("Emberfall/Setup/Apply Priest Projectile Phases")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            var source = set.GetClip(CombatState.HeavyAttack);
            var windup = Crop(source, 0, ReleaseSourceSeconds, WindupPath);
            var release = Crop(source, ReleaseSourceSeconds, RecoveryEndSeconds, ReleasePath);
            PreserveSourceFacing(source, windup, release);
            var controller = (AnimatorController)set.Controller;
            SetState(controller, "PriestProjectileWindup", windup);
            SetState(controller, "PriestProjectileRelease", release);
            set.ConfigurePriestProjectile(windup, release);
            var annotations = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
            var contact = annotations?.contacts.SingleOrDefault(c => c.actionId == "priest.projectile");
            if (contact != null && !contact.confirmed)
            {
                contact.clip = release;
                EditorUtility.SetDirty(annotations);
            }
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PRIEST_PHASES] windup={windup.length:R} release={release.length:R} sourceUnchanged={source.name}");
        }

        private static void SetState(AnimatorController controller, string name, AnimationClip clip)
        {
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = false;
            EditorUtility.SetDirty(state);
        }

        private static void PreserveSourceFacing(AnimationClip source, AnimationClip windup, AnimationClip release)
        {
            // Original orientation prevents crop-dependent body-forward rebasing, but must retain
            // the full source's existing facing as well. Measure once in an isolated editor scene.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab");
                var model = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Vector3 Facing(AnimationClip clip)
                {
                    var position = animator.transform.localPosition;
                    var rotation = animator.transform.localRotation;
                    var graph = PlayableGraph.Create("Priest authoring facing reference");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        playable.SetApplyFootIK(false);
                        playable.SetApplyPlayableIK(false);
                        AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                        graph.Play();
                        playable.SetTime(0);
                        graph.Evaluate(0);
                        animator.transform.SetLocalPositionAndRotation(position, rotation);
                        return Vector3.ProjectOnPlane(animator.GetBoneTransform(HumanBodyBones.Hips).forward, Vector3.up).normalized;
                    }
                    finally { graph.Destroy(); }
                }
                Vector3 reference = Facing(source);
                float delta = Vector3.SignedAngle(Facing(windup), reference, Vector3.up);
                float bestOffset = 0, bestError = float.PositiveInfinity;
                // Root extraction can invert the offset sign; verify against actual bones instead
                // of relying on importer conventions or changing actor/runtime facing.
                foreach (float offset in new[] { delta, -delta })
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(windup);
                    settings.orientationOffsetY = offset;
                    AnimationUtility.SetAnimationClipSettings(windup, settings);
                    float error = Vector3.Angle(Facing(windup), reference);
                    if (error < bestError) { bestError = error; bestOffset = offset; }
                }
                if (bestError > .05f) throw new InvalidOperationException("Priest source facing could not be preserved.");
                foreach (var clip in new[] { windup, release })
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.orientationOffsetY = bestOffset;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                }
                Debug.Log($"[PRIEST_FACING] authoringOffset={bestOffset:R} sourceFacingError={bestError:R}");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static AnimationClip Crop(AnimationClip source, float start, float end, string path)
        {
            if (start < 0 || end <= start || end > source.length) throw new ArgumentOutOfRangeException(nameof(end));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
            else EditorUtility.CopySerialized(source, clip);
            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original = AnimationUtility.GetEditorCurve(source, binding);
                var keys = original.keys.Where(k => k.time > start + .00001f && k.time < end - .00001f)
                    .Select(k => { k.time -= start; return k; }).ToList();
                keys.Insert(0, Boundary(original, start, 0));
                keys.Add(Boundary(original, end, end - start));
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys.ToArray()));
            }
            if (AnimationUtility.GetObjectReferenceCurveBindings(source).Length != 0)
                throw new InvalidOperationException("Object-reference tracks need an explicit crop adapter.");
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = end - start;
            settings.loopTime = false;
            settings.loopBlend = false;
            // A crop must not derive a new body-forward reference from its own first pose.
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static Keyframe Boundary(AnimationCurve curve, float time, float shiftedTime)
        {
            foreach (var key in curve.keys)
                if (Mathf.Abs(key.time - time) < .00001f) { var exact = key; exact.time = shiftedTime; return exact; }
            const float h = .0001f;
            float slope = (curve.Evaluate(time + h) - curve.Evaluate(time - h)) / (2 * h);
            return new Keyframe(shiftedTime, curve.Evaluate(time), slope, slope);
        }
    }
}
