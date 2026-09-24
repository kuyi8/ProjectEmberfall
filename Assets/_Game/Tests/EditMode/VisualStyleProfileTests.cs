using Emberfall.Application.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Tests.EditMode
{
    public sealed class VisualStyleProfileTests
    {
        [Test]
        public void RecalibrationSteps_IsolateExposureAndFogFromFirstStep()
        {
            var original = ScriptableObject.CreateInstance<VisualStyleProfile>();
            original.exposure = 0.1f;
            original.fogColor = new Color(0.105f, 0.14f, 0.155f);
            original.lightIntensity = 1.25f;
            original.tonemapping = TonemappingMode.ACES;
            var first = VisualStyleProfile.StepOne(original);
            var second = VisualStyleProfile.StepTwo(first, TonemappingMode.ACES);
            var neutral = VisualStyleProfile.StepTwo(first, TonemappingMode.Neutral);
            try
            {
                Assert.That(first.exposure, Is.EqualTo(original.exposure));
                Assert.That(first.fogColor, Is.EqualTo(original.fogColor));
                Assert.That(first.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(first.vignette, Is.EqualTo(0.12f));
                Assert.That(second.exposure, Is.EqualTo(0.5f));
                Assert.That(second.lightIntensity, Is.EqualTo(original.lightIntensity));
                Assert.That(neutral.exposure, Is.EqualTo(second.exposure));
                Assert.That(neutral.ground, Is.EqualTo(second.ground));
                Assert.That(neutral.tonemapping, Is.EqualTo(TonemappingMode.Neutral));
                Assert.That(original.exposure, Is.EqualTo(0.1f), "Preset factories must not mutate their input.");
            }
            finally
            {
                Object.DestroyImmediate(original); Object.DestroyImmediate(first);
                Object.DestroyImmediate(second); Object.DestroyImmediate(neutral);
            }
        }

        [Test]
        public void Cartoon_IsPresentationOnlyRuntimeCopy()
        {
            var original = ScriptableObject.CreateInstance<VisualStyleProfile>();
            original.lightIntensity = 1.25f;
            original.fog = true;
            var cartoon = VisualStyleProfile.Cartoon(original);
            try
            {
                Assert.That(cartoon.lightIntensity, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(cartoon.fog, Is.False);
                Assert.That(cartoon.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));
                Assert.That(cartoon.saturation, Is.EqualTo(25f));
                Assert.That(cartoon.ground, Is.EqualTo(new Color(0.24f, 0.22f, 0.20f)));
                Assert.That(original.fog, Is.True);
            }
            finally { Object.DestroyImmediate(original); Object.DestroyImmediate(cartoon); }
        }

        [Test]
        public void VolumeRuntimeProfile_ClonesComponentsRatherThanWritingSharedAsset()
        {
            var shared = ScriptableObject.CreateInstance<VolumeProfile>();
            shared.Add<ColorAdjustments>(true).postExposure.Override(0.1f);
            var root = new GameObject("test-volume");
            var volume = root.AddComponent<Volume>();
            volume.sharedProfile = shared;
            var runtime = volume.profile;
            try
            {
                runtime.TryGet(out ColorAdjustments copy);
                shared.TryGet(out ColorAdjustments source);
                Assert.That(copy, Is.Not.SameAs(source));
                copy.postExposure.Override(0.7f);
                Assert.That(source.postExposure.value, Is.EqualTo(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var component in shared.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(shared);
            }
        }
    }
}
