using System;
using System.Collections.Generic;
using System.Reflection;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class KnifeTrailStyleTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private KnifeTrailStyle _style;
        private TrailRenderer _source, _copy;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _style = Own(ScriptableObject.CreateInstance<KnifeTrailStyle>());
            _material = Own(new Material(Shader.Find("Hidden/InternalErrorShader")));
            _source = Trail("source");
            _copy = Trail("visual copy");
            _source.sharedMaterial = _material;
            _copy.sharedMaterial = _material;
            _source.time = .18f;
            _source.widthMultiplier = 2;
            _source.widthCurve = new AnimationCurve(new Keyframe(0, .055f), new Keyframe(.55f, .032f), new Keyframe(1, 0));
            _source.colorGradient = Gradient(GradientMode.Fixed);
            _copy.time = .7f;
            _copy.widthMultiplier = 3;
            _copy.widthCurve = new AnimationCurve(new Keyframe(0, .02f), new Keyframe(1, 0));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void DefaultsAreExplicitAndDoNotRequireAnAuthoredAsset()
        {
            Assert.That(_style.LifetimeSeconds, Is.EqualTo(.10f));
            Assert.That(_style.WidthMultiplierScale, Is.EqualTo(.45f));
            Assert.That(_style.AlphaMultiplier, Is.EqualTo(.5625f));
            Assert.DoesNotThrow(_style.Validate);
        }

        [TestCase(GradientMode.Blend)]
        [TestCase(GradientMode.Fixed)]
        public void ApplyPreservesSourceMaterialKeysAndEveryUntargetedVisualParameter(GradientMode mode)
        {
            _source.colorGradient = Gradient(mode);
            string source = Json(_source), material = Json(_material);
            var widths = _copy.widthCurve.keys;
            float spacing = _copy.minVertexDistance;
            bool enabled = _copy.enabled, emitting = _copy.emitting;
            var alignment = _copy.alignment;
            var textureMode = _copy.textureMode;
            _style.ApplyToVisual(_copy, _source);
            AssertStyled(_copy, _source);
            Assert.That(_copy.widthCurve.keys, Is.EqualTo(widths));
            Assert.That(_copy.minVertexDistance, Is.EqualTo(spacing));
            Assert.That(_copy.enabled, Is.EqualTo(enabled));
            Assert.That(_copy.emitting, Is.EqualTo(emitting));
            Assert.That(_copy.alignment, Is.EqualTo(alignment));
            Assert.That(_copy.textureMode, Is.EqualTo(textureMode));
            Assert.That(_copy.sharedMaterial, Is.SameAs(_material));
            Assert.That(Json(_source), Is.EqualTo(source));
            Assert.That(Json(_material), Is.EqualTo(material));
        }

        [Test]
        public void ReapplyingUsesSourceRatherThanCompoundingCopyAlpha()
        {
            _style.ApplyToVisual(_copy, _source);
            string once = Json(_copy);
            _style.ApplyToVisual(_copy, _source);
            Assert.That(Json(_copy), Is.EqualTo(once));
            AssertStyled(_copy, _source);
        }

        [TestCase("_lifetimeSeconds", 0f)]
        [TestCase("_lifetimeSeconds", -1f)]
        [TestCase("_lifetimeSeconds", float.NaN)]
        [TestCase("_lifetimeSeconds", float.PositiveInfinity)]
        [TestCase("_lifetimeSeconds", float.NegativeInfinity)]
        [TestCase("_widthMultiplierScale", 0f)]
        [TestCase("_widthMultiplierScale", -1f)]
        [TestCase("_widthMultiplierScale", float.NaN)]
        [TestCase("_widthMultiplierScale", float.PositiveInfinity)]
        [TestCase("_widthMultiplierScale", float.NegativeInfinity)]
        [TestCase("_alphaMultiplier", -.01f)]
        [TestCase("_alphaMultiplier", 1.01f)]
        [TestCase("_alphaMultiplier", float.NaN)]
        [TestCase("_alphaMultiplier", float.PositiveInfinity)]
        [TestCase("_alphaMultiplier", float.NegativeInfinity)]
        public void InvalidSerializedParameterFailsBeforeAnyVisualOrSourceWrite(string field, float value)
        {
            Field(typeof(KnifeTrailStyle), field).SetValue(_style, value);
            string source = Json(_source), copy = Json(_copy), material = Json(_material);
            Assert.Throws<ArgumentOutOfRangeException>(() => _style.ApplyToVisual(_copy, _source));
            Assert.That(Json(_copy), Is.EqualTo(copy));
            Assert.That(Json(_source), Is.EqualTo(source));
            Assert.That(Json(_material), Is.EqualTo(material));
        }

        [Test]
        public void FiniteParametersWhoseWidthProductOverflowsAlsoFailBeforeWrites()
        {
            Field(typeof(KnifeTrailStyle), "_widthMultiplierScale").SetValue(_style, float.MaxValue);
            string source = Json(_source), copy = Json(_copy);
            Assert.Throws<ArgumentOutOfRangeException>(() => _style.ApplyToVisual(_copy, _source));
            Assert.That(Json(_copy), Is.EqualTo(copy));
            Assert.That(Json(_source), Is.EqualTo(source));
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void AlphaEndpointsAreValidAndPreserveTheZeroTail(float alpha)
        {
            Field(typeof(KnifeTrailStyle), "_alphaMultiplier").SetValue(_style, alpha);
            _style.ApplyToVisual(_copy, _source);
            Assert.That(_copy.colorGradient.alphaKeys[0].alpha, Is.EqualTo(.8f * alpha).Within(.000001f));
            Assert.That(_copy.colorGradient.alphaKeys[2].alpha, Is.Zero);
        }

        [Test]
        public void MissingOrSameTrailIsRejectedWithoutWritingSource()
        {
            string source = Json(_source);
            Assert.Throws<ArgumentNullException>(() => _style.ApplyToVisual(null, _source));
            Assert.Throws<ArgumentNullException>(() => _style.ApplyToVisual(_copy, null));
            Assert.Throws<ArgumentException>(() => _style.ApplyToVisual(_source, _source));
            Assert.That(Json(_source), Is.EqualTo(source));
        }

        [Test]
        public void PresenterCachesBeforeInitializationButNeverAcceptsAnInvalidReplacement()
        {
            var presenter = Presenter();
            Assert.That(presenter.TrailStyle, Is.Null);
            presenter.ConfigureTrailStyle(_style);
            Assert.That(presenter.TrailStyle, Is.SameAs(_style));
            var invalid = Own(ScriptableObject.CreateInstance<KnifeTrailStyle>());
            Field(typeof(KnifeTrailStyle), "_alphaMultiplier").SetValue(invalid, float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => presenter.ConfigureTrailStyle(invalid));
            Assert.That(presenter.TrailStyle, Is.SameAs(_style));
            presenter.ConfigureTrailStyle(null);
            Assert.That(presenter.TrailStyle, Is.Null);
            Assert.That(presenter.IsConfigured, Is.False);
        }

        [Test]
        public void PresenterStylesOnlyIndependentSlotsAndNullRestoresExactSourceParameters()
        {
            var otherSource = Trail("other source"); var otherCopy = Trail("other visual");
            otherSource.time = .23f; otherSource.widthMultiplier = .7f;
            otherSource.colorGradient = Gradient(GradientMode.Blend);
            otherCopy.sharedMaterial = _material;
            var presenter = Presenter();
            SetSlots(presenter, Slot(_copy, _source), Slot(otherCopy, otherSource));
            string source = Json(_source), other = Json(otherSource), material = Json(_material);
            presenter.ConfigureTrailStyle(_style);
            AssertStyled(_copy, _source); AssertStyled(otherCopy, otherSource);
            presenter.ConfigureTrailStyle(null);
            AssertRestored(_copy, _source); AssertRestored(otherCopy, otherSource);
            Assert.That(_copy.sharedMaterial, Is.SameAs(_material));
            Assert.That(otherCopy.sharedMaterial, Is.SameAs(_material));
            Assert.That(Json(_source), Is.EqualTo(source));
            Assert.That(Json(otherSource), Is.EqualTo(other));
            Assert.That(Json(_material), Is.EqualTo(material));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnyActiveLeaseRejectsBothStylingAndNullRestorationBeforeFirstSlotWrite(bool restore)
        {
            var presenter = Presenter(); presenter.ConfigureTrailStyle(_style);
            SetSlots(presenter, Slot(_copy, _source), Slot(Trail("leased visual"), Trail("leased source"), true));
            string copy = Json(_copy);
            Assert.Throws<InvalidOperationException>(() => presenter.ConfigureTrailStyle(restore ? null : _style));
            Assert.That(Json(_copy), Is.EqualTo(copy));
            Assert.That(presenter.TrailStyle, Is.SameAs(_style));
        }

        [Test]
        public void InvalidOrAliasedLaterSlotRejectsTheEntireBatchBeforeAnyWrite()
        {
            var presenter = Presenter();
            SetSlots(presenter, Slot(_copy, _source), Slot(_source, _source));
            string source = Json(_source), copy = Json(_copy);
            Assert.Throws<ArgumentException>(() => presenter.ConfigureTrailStyle(_style));
            Assert.That(Json(_copy), Is.EqualTo(copy)); Assert.That(Json(_source), Is.EqualTo(source));
            Assert.That(presenter.TrailStyle, Is.Null);
            SetSlots(presenter, Slot(_copy, _source), Slot(Trail("missing source visual"), null));
            Assert.Throws<ArgumentNullException>(() => presenter.ConfigureTrailStyle(_style));
            Assert.That(Json(_copy), Is.EqualTo(copy)); Assert.That(presenter.TrailStyle, Is.Null);
        }

        private void AssertStyled(TrailRenderer copy, TrailRenderer source)
        {
            Assert.That(copy.time, Is.EqualTo(.10f));
            Assert.That(copy.widthMultiplier, Is.EqualTo(source.widthMultiplier * .45f).Within(.000001f));
            var expected = source.colorGradient; var actual = copy.colorGradient;
            Assert.That(actual.mode, Is.EqualTo(expected.mode)); Assert.That(actual.colorSpace, Is.EqualTo(expected.colorSpace));
            Assert.That(actual.colorKeys, Is.EqualTo(expected.colorKeys));
            Assert.That(actual.alphaKeys.Length, Is.EqualTo(expected.alphaKeys.Length));
            for (int i = 0; i < actual.alphaKeys.Length; i++)
            {
                Assert.That(actual.alphaKeys[i].time, Is.EqualTo(expected.alphaKeys[i].time));
                Assert.That(actual.alphaKeys[i].alpha, Is.EqualTo(expected.alphaKeys[i].alpha * .5625f).Within(.000001f));
            }
        }

        private static void AssertRestored(TrailRenderer copy, TrailRenderer source)
        {
            Assert.That(copy.time, Is.EqualTo(source.time));
            Assert.That(copy.widthMultiplier, Is.EqualTo(source.widthMultiplier));
            Assert.That(copy.colorGradient.mode, Is.EqualTo(source.colorGradient.mode));
            Assert.That(copy.colorGradient.colorSpace, Is.EqualTo(source.colorGradient.colorSpace));
            Assert.That(copy.colorGradient.colorKeys, Is.EqualTo(source.colorGradient.colorKeys));
            Assert.That(copy.colorGradient.alphaKeys, Is.EqualTo(source.colorGradient.alphaKeys));
        }

        private static Gradient Gradient(GradientMode mode)
        {
            var gradient = new Gradient { mode = mode, colorSpace = ColorSpace.Linear };
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.66f, .9f, 1), 0), new GradientColorKey(new Color(.18f, .55f, 1), 1) },
                new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(.4f, .55f), new GradientAlphaKey(0, 1) });
            return gradient;
        }

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }
        private TrailRenderer Trail(string name)
        {
            var trail = Own(new GameObject(name)).AddComponent<TrailRenderer>();
            trail.emitting = false; trail.enabled = false; return trail;
        }
        private PlayerKnifePresentation Presenter()
        {
            var go = Own(new GameObject("own trail style presenter")); go.SetActive(false);
            return go.AddComponent<PlayerKnifePresentation>();
        }
        private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static string Json(Object value) => EditorJsonUtility.ToJson(value);
        private static object Slot(TrailRenderer copy, TrailRenderer source, bool leased = false)
        {
            var type = typeof(PlayerKnifePresentation).GetNestedType("Slot", BindingFlags.NonPublic);
            var slot = Activator.CreateInstance(type, true);
            type.GetField("Trail").SetValue(slot, copy); type.GetField("OriginalTrail").SetValue(slot, source);
            type.GetField("Leased").SetValue(slot, leased);
            type.GetField("Original").SetValue(slot, Array.Empty<Renderer>());
            type.GetField("Hidden").SetValue(slot, Array.Empty<bool>());
            return slot;
        }
        private static void SetSlots(PlayerKnifePresentation presenter, params object[] slots)
        {
            var type = typeof(PlayerKnifePresentation).GetNestedType("Slot", BindingFlags.NonPublic);
            var array = Array.CreateInstance(type, slots.Length);
            for (int i = 0; i < slots.Length; i++) array.SetValue(slots[i], i);
            Field(typeof(PlayerKnifePresentation), "_slots").SetValue(presenter, array);
        }
    }
}
