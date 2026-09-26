using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    internal static class HumanoidClipDerivation
    {
        public static void PreserveSourceFacing(AnimationClip source, float sourceTime, string prefabPath, params AnimationClip[] clips)
        {
            // Original orientation prevents crop-dependent body-forward rebasing, but must retain
            // the full source's existing facing as well. Measure once in an isolated editor scene.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null || clips.Length == 0) throw new InvalidOperationException("Missing facing reference.");
                var windup = clips[0];
                var model = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Vector3 Facing(AnimationClip clip, float time = 0)
                {
                    var position = animator.transform.localPosition;
                    var rotation = animator.transform.localRotation;
                    var graph = PlayableGraph.Create("Humanoid authoring facing reference");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        playable.SetApplyFootIK(false);
                        playable.SetApplyPlayableIK(false);
                        AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                        graph.Play();
                        playable.SetTime(time);
                        graph.Evaluate(0);
                        animator.transform.SetLocalPositionAndRotation(position, rotation);
                        return Vector3.ProjectOnPlane(animator.GetBoneTransform(HumanBodyBones.Hips).forward, Vector3.up).normalized;
                    }
                    finally { graph.Destroy(); }
                }
                Vector3 reference = Facing(source, sourceTime);
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
                if (bestError > .05f) throw new InvalidOperationException("Humanoid source facing could not be preserved.");
                foreach (var clip in clips)
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.orientationOffsetY = bestOffset;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                }
                Debug.Log($"[CLIP_FACING] authoringOffset={bestOffset:R} sourceFacingError={bestError:R}");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static AnimationClip Crop(AnimationClip source, float start, float end, string path)
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
