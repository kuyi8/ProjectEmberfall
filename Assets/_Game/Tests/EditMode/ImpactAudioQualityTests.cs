using System;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class ImpactAudioQualityTests
    {
        [Test]
        public void StereoOnset_UsesEarliestChannelAndFrame_NotInterleavedIndex()
        {
            var samples = new float[200];
            samples[41] = .5f;
            var m = ImpactAudioMeasurements.Measure(samples, 2, 1000);
            Assert.That(m.onsetFrame, Is.EqualTo(20));
            Assert.That(m.onsetMs, Is.EqualTo(20));
            Assert.That(m.durationMs, Is.EqualTo(100));
            Assert.That(m.peakDbfs, Is.EqualTo(20 * Math.Log10(.5)).Within(1e-8));
            Assert.That(m.rmsDbfs, Is.EqualTo(20 * Math.Log10(Math.Sqrt(.25 / 200))).Within(1e-8));
            Assert.That(ImpactAudioMeasurements.TrimFrames(m), Is.EqualTo(19));
        }

        [TestCase(9, 0)]
        [TestCase(10, 0)]
        [TestCase(11, 10)]
        public void Trim_OnlyAboveTenMilliseconds_PreservesOneMillisecond(int onset, int expectedTrim)
        {
            var samples = new float[100]; samples[onset] = .1f;
            Assert.That(ImpactAudioMeasurements.TrimFrames(ImpactAudioMeasurements.Measure(samples, 1, 1000)), Is.EqualTo(expectedTrim));
        }

        [Test]
        public void SilentAndInvalidPcm_AreNotMisreportedAsImmediateAudio()
        {
            var m = ImpactAudioMeasurements.Measure(new float[10], 1, 1000);
            Assert.That(m.onsetFrame, Is.EqualTo(-1));
            Assert.That(m.onsetMs, Is.EqualTo(-1));
            Assert.That(m.rmsDbfs, Is.EqualTo(double.NegativeInfinity));
            Assert.Throws<ArgumentException>(() => ImpactAudioMeasurements.Measure(Array.Empty<float>(), 1, 1000));
            Assert.Throws<ArgumentException>(() => ImpactAudioMeasurements.Measure(new[] { float.NaN }, 1, 1000));
            Assert.Throws<ArgumentException>(() => ImpactAudioMeasurements.Measure(new float[3], 2, 1000));
        }

        [Test]
        public void LevelCompensation_AttenuatesOnly()
        {
            Assert.That(ImpactAudioMeasurements.Attenuation(-10, -16), Is.EqualTo(.501187f).Within(.00001f));
            Assert.That(ImpactAudioMeasurements.Attenuation(-20, -16), Is.EqualTo(1));
        }

        [TestCase(HitFeedbackGrade.Light, ImpactSurface.Flesh, 0)]
        [TestCase(HitFeedbackGrade.Light, ImpactSurface.Metal, 1)]
        [TestCase(HitFeedbackGrade.Heavy, ImpactSurface.Flesh, 2)]
        [TestCase(HitFeedbackGrade.Heavy, ImpactSurface.Metal, 3)]
        [TestCase(HitFeedbackGrade.GuardBreak, ImpactSurface.Metal, 4)]
        [TestCase(HitFeedbackGrade.Execution, ImpactSurface.Flesh, 5)]
        public void AuthoredBank_HasThreeMeasuredNonRepeatingVariants(HitFeedbackGrade grade, ImpactSurface surface, int slot)
        {
            var set = Load();
            var entries = new SerializedObject(set).FindProperty("_slots").GetArrayElementAtIndex(slot).FindPropertyRelative("variants");
            Assert.That(entries.arraySize, Is.EqualTo(3));
            var clips = new System.Collections.Generic.HashSet<AudioClip>();
            for (int i = 0; i < 3; i++)
            {
                var playback = set.Select(grade, surface, -1, (i + .1f) / 3, .5f);
                Assert.That(clips.Add(playback.Clip), Is.True, "Distinct candidates required.");
                Assert.That(playback.Index, Is.EqualTo(i));
                var m = ImpactAudioMeasurements.Measure(ImpactAudioMeasurements.Read(playback.Clip), playback.Clip.channels, playback.Clip.frequency);
                Assert.That(m.onsetMs, Is.InRange(0d, 10d));
                Assert.That(playback.Gain, Is.InRange(.0001f, 1f));
                for (int choice = 0; choice <= 10; choice++)
                    Assert.That(set.Select(grade, surface, i, choice / 10f, .5f).Index, Is.Not.EqualTo(i));
            }
            float basePitch = grade == HitFeedbackGrade.Execution ? .85f : 1f;
            Assert.That(set.Select(grade, surface, -1, 0, 0).Pitch, Is.EqualTo(basePitch * .95f).Within(1e-6));
            Assert.That(set.Select(grade, surface, -1, 0, 1).Pitch, Is.EqualTo(basePitch * 1.05f).Within(1e-6));
        }

        [TestCase(HitFeedbackGrade.Light)]
        [TestCase(HitFeedbackGrade.Heavy)]
        public void FleshMetal_CompensatedRmsSpreadAtMostTwoDb(HitFeedbackGrade grade)
        {
            var set = Load(); double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (var surface in new[] { ImpactSurface.Flesh, ImpactSurface.Metal })
                for (int i = 0; i < 3; i++)
                {
                    var sound = set.Select(grade, surface, -1, (i + .1f) / 3, .5f);
                    var m = ImpactAudioMeasurements.Measure(ImpactAudioMeasurements.Read(sound.Clip), sound.Clip.channels, sound.Clip.frequency);
                    double rms = m.rmsDbfs + ImpactAudioMeasurements.Db(sound.Gain);
                    low = Math.Min(low, rms); high = Math.Max(high, rms);
                }
            Assert.That(high - low, Is.LessThanOrEqualTo(2d));
        }

        [Test]
        public void LegacySingleClip_RemainsPlayableWithoutMutation()
        {
            var set = ScriptableObject.CreateInstance<CombatImpactAudioSet>();
            var clip = AudioClip.Create("Legacy", 100, 1, 1000, false);
            try
            {
                var serialized = new SerializedObject(set);
                serialized.FindProperty("_flesh").objectReferenceValue = clip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string before = EditorJsonUtility.ToJson(set);
                Assert.That(set.Resolve(HitFeedbackGrade.Light, ImpactSurface.Flesh), Is.SameAs(clip));
                Assert.That(set.Select(HitFeedbackGrade.Light, ImpactSurface.Flesh, 0, 1, 1).Gain, Is.EqualTo(1));
                Assert.That(EditorJsonUtility.ToJson(set), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(set); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void RebuildAndSelection_PreserveAuthoredBankAndUnityRandomState()
        {
            var set = Object.Instantiate(Load());
            try
            {
                var serialized = new SerializedObject(set);
                serialized.FindProperty("_slots").GetArrayElementAtIndex(0).FindPropertyRelative("variants").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("gain").floatValue = .123f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string before = EditorJsonUtility.ToJson(set);
                var randomBefore = UnityEngine.Random.state;
                M6ImpactAudioQualitySetup.EnsureConfigured(set);
                for (int i = 0; i < 20; i++) set.Select(HitFeedbackGrade.Light, ImpactSurface.Flesh, i % 3, .25f, .75f);
                Assert.That(EditorJsonUtility.ToJson(set), Is.EqualTo(before));
                Assert.That(UnityEngine.Random.state, Is.EqualTo(randomBefore));
            }
            finally { Object.DestroyImmediate(set); }
        }

        private static CombatImpactAudioSet Load() => AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>(M6CombatFeedbackSetup.AudioPath);
    }
}
