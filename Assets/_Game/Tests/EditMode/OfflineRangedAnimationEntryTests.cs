using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    // Entry-offset contract only: no Animator evaluation/input/release-phase acceptance.
    public sealed class OfflineRangedAnimationEntryTests
    {
        private const string ProductionSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private PlayerAnimationSet _set;
        private AnimationClip _clip;

        [SetUp]
        public void Setup()
        {
            _set = ScriptableObject.CreateInstance<PlayerAnimationSet>();
            _set.hideFlags = HideFlags.HideAndDontSave;
            _clip = new AnimationClip { name = "Disposable_Entry_056", frameRate = 120f, hideFlags = HideFlags.HideAndDontSave };
            AnimationUtility.SetEditorCurve(_clip,
                new EditorCurveBinding { path = "", type = typeof(Transform), propertyName = "m_LocalPosition.x" },
                AnimationCurve.Constant(0f, .56f, 0f));
            Assert.That(_clip.length, Is.EqualTo(.56f).Within(.000001f));
            _set.ConfigureOfflineRangedAttack(_clip);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_set != null) Object.DestroyImmediate(_set);
            if (_clip != null) Object.DestroyImmediate(_clip);
        }

        [Test]
        public void DefaultEntryTimeRemainsZeroForEveryCombatState()
        {
            foreach (CombatState state in Enum.GetValues(typeof(CombatState)))
                Assert.That(_set.GetOfflineEntryTimeOffset(state, .22f, 2f), Is.Zero, state.ToString());
            Assert.That(_set.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(_clip));
        }

        [Test]
        public void OptInAffectsOnlyOfflineRangedEntryAndDoesNotReplaceItsClip()
        {
            var before = Enum.GetValues(typeof(CombatState)).Cast<CombatState>()
                .ToDictionary(state => state, state => _set.GetOfflineClip(state));
            _set.ConfigureOfflineRangedEntryTime(true);
            foreach (var pair in before)
            {
                float expected = pair.Key == CombatState.RangedAttack ? .22f : 0f;
                Assert.That(_set.GetOfflineEntryTimeOffset(pair.Key, .11f, 2f), Is.EqualTo(expected).Within(.000001f), pair.Key.ToString());
                Assert.That(_set.GetOfflineClip(pair.Key), Is.SameAs(pair.Value), pair.Key.ToString());
            }
            Assert.That(_set.GetOfflineStateName(CombatState.RangedAttack), Is.EqualTo("PlayerRangedAttack"));
            _set.ConfigureOfflineRangedEntryTime(false);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 2f), Is.Zero);
        }

        [TestCase(.22f, 1f, .22f)]
        [TestCase(.11f, 2f, .22f)]
        [TestCase(1f, 2f, .56f)]
        [TestCase(-.01f, 1f, 0f)]
        [TestCase(float.NaN, 1f, 0f)]
        [TestCase(float.PositiveInfinity, 1f, 0f)]
        [TestCase(.22f, float.NaN, 0f)]
        [TestCase(.22f, float.PositiveInfinity, 0f)]
        public void EntryOffsetUsesClipSecondsAndBoundsInvalidInputs(float elapsed, float speed, float expected)
        {
            _set.ConfigureOfflineRangedEntryTime(true);
            float actual = _set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, elapsed, speed);
            Assert.That(float.IsNaN(actual) || float.IsInfinity(actual), Is.False);
            Assert.That(actual, Is.EqualTo(expected).Within(.000001f));
            if (expected == .22f)
                Assert.That(actual, Is.Not.EqualTo(.22f / _clip.length).Within(.000001f), "The fixed-time argument is seconds, not normalized clip time.");
            Assert.That(_set.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(_clip));
        }

        [Test]
        public void NonpositiveSpeedAndMissingOfflineClipCannotProduceAnEntryOffset()
        {
            _set.ConfigureOfflineRangedEntryTime(true);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 0f), Is.Zero);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, -1f), Is.Zero);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, 0f, 1f), Is.Zero);
            _set.ConfigureOfflineRangedAttack(null);
            Assert.That(_set.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 1f), Is.Zero);
        }

        [Test]
        public void EnablingADisposableCloneDoesNotWriteBackAssetsOrSceneReferences()
        {
            var source = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(ProductionSetPath);
            Assert.That(source, Is.Not.Null);
            string sourceJson = EditorJsonUtility.ToJson(source);
            var sourceClip = source.GetOfflineClip(CombatState.RangedAttack);
            Assert.That(sourceClip, Is.Not.Null);
            var sourceObjects = new Object[] { sourceClip, source.Controller };
            Assert.That(sourceObjects.All(item => item != null), Is.True);
            var sourceMemory = sourceObjects.ToDictionary(item => item, item => EditorJsonUtility.ToJson(item));
            var sceneReferences = SceneAnimationReferences();
            int activeScene = SceneManager.GetActiveScene().handle;
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Game/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath).Concat(new[] { ProductionSetPath }).Concat(sourceObjects.Select(AssetDatabase.GetAssetPath))
                .SelectMany(path => new[] { path, path + ".meta" }).Distinct().Where(File.Exists).ToArray();
            var disk = paths.ToDictionary(path => path, Hash);
            var clone = Object.Instantiate(source);
            try
            {
                clone.hideFlags = HideFlags.HideAndDontSave;
                Assert.That(EditorUtility.IsPersistent(clone), Is.False);
                Assert.That(AssetDatabase.GetAssetPath(clone), Is.Empty);
                clone.ConfigureOfflineRangedEntryTime(true);
                Assert.That(clone.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .11f, 2f), Is.EqualTo(Mathf.Min(.22f, sourceClip.length)).Within(.000001f));
                Assert.That(clone.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(sourceClip));
                Assert.That(clone.Controller, Is.SameAs(source.Controller));
                Assert.That(source.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 2f), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(clone);
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(sourceJson));
                foreach (var item in sourceMemory) Assert.That(EditorJsonUtility.ToJson(item.Key), Is.EqualTo(item.Value), item.Key.name);
                foreach (var file in disk) Assert.That(Hash(file.Key), Is.EqualTo(file.Value), file.Key);
                var after = SceneAnimationReferences();
                Assert.That(after.Count, Is.EqualTo(sceneReferences.Count));
                foreach (var pair in sceneReferences)
                {
                    Assert.That(pair.Key, Is.Not.Null);
                    Assert.That(after.ContainsKey(pair.Key), Is.True);
                    Assert.That(after[pair.Key], Is.EqualTo(pair.Value), pair.Key.name);
                }
                Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(activeScene));
            }
        }

        [Test]
        public void EveryAuthoredAnimationSetStillUsesLegacyEntryByDefault()
        {
            var paths = AssetDatabase.FindAssets("t:PlayerAnimationSet").Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
            Assert.That(paths, Is.Not.Empty);
            Assert.That(paths, Does.Contain(ProductionSetPath));
            foreach (string path in paths)
            {
                var source = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(path);
                Assert.That(source, Is.Not.Null, path);
                string before = EditorJsonUtility.ToJson(source);
                foreach (CombatState state in Enum.GetValues(typeof(CombatState)))
                    Assert.That(source.GetOfflineEntryTimeOffset(state, .22f, 2f), Is.Zero, path + ":" + state);
                // A missing/zero-length offline clip cannot make an enabled flag vacuously look disabled.
                var probe = Object.Instantiate(source);
                try
                {
                    probe.hideFlags = HideFlags.HideAndDontSave;
                    probe.ConfigureOfflineRangedAttack(_clip);
                    Assert.That(probe.GetOfflineEntryTimeOffset(CombatState.RangedAttack, .22f, 2f), Is.Zero, path);
                }
                finally { Object.DestroyImmediate(probe); }
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before), path);
            }
        }

        [Test]
        public void ActualOfflineRangedStateHasNoHiddenClockMultiplier()
        {
            var source = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(ProductionSetPath);
            Assert.That(source, Is.Not.Null);
            var controller = source.Controller as AnimatorController;
            Assert.That(controller, Is.Not.Null);
            var state = controller.layers[0].stateMachine.states.Select(item => item.state)
                .Single(item => item.name == source.GetOfflineStateName(CombatState.RangedAttack));
            Assert.That(state.name, Is.EqualTo("PlayerRangedAttack"));
            Assert.That(state.motion, Is.SameAs(source.GetOfflineClip(CombatState.RangedAttack)));
            Assert.That(state.speed, Is.EqualTo(1f));
            Assert.That(state.speedParameterActive, Is.False);
            Assert.That(state.timeParameterActive, Is.False);
            Assert.That(state.cycleOffsetParameterActive, Is.False);
            Assert.That(state.cycleOffset, Is.Zero);
        }

        private static Dictionary<MonoBehaviour, string> SceneAnimationReferences()
        {
            var result = new Dictionary<MonoBehaviour, string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var component in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Where(component => component != null))
                    if (new SerializedObject(component).FindProperty("_animationSet") != null)
                        result.Add(component, EditorJsonUtility.ToJson(component));
            }
            return result;
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
