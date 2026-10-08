using System.Reflection;
using Emberfall.AI.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenWeaponTintTests
    {
        const string Palette = "Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_WardenMetal.mat";
        static readonly MethodInfo Blend = typeof(WardenActor).GetMethod("BlendWeaponTint",
            BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void MatchingTint_PreservesExactStoredPaletteAcrossRepeatedUpdates()
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(Palette);
            Assert.That(source, Is.Not.Null);
            Assert.That(Blend, Is.Not.Null);
            Color sourceBefore = source.GetColor("_BaseColor");
            var instance = new Material(source);
            try
            {
                Color stored = instance.GetColor("_BaseColor");
                Color target = instance.color;
                for (int i = 0; i < 100; i++)
                    Blend.Invoke(null, new object[] { instance, target, .3f });
                Assert.That(instance.GetColor("_BaseColor"), Is.EqualTo(stored));
                Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(sourceBefore));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(.2f)]
        [TestCase(1f)]
        public void DifferentTint_PreservesExistingPhaseInterpolation(float blend)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(Palette);
            Assert.That(source, Is.Not.Null);
            Assert.That(Blend, Is.Not.Null);
            Color sourceBefore = source.GetColor("_BaseColor");
            var actual = new Material(source);
            var previousBehaviour = new Material(source);
            try
            {
                Color target = new Color(1f, .16f, .035f);
                previousBehaviour.color = Color.Lerp(previousBehaviour.color, target, blend);
                Blend.Invoke(null, new object[] { actual, target, blend });
                Assert.That(actual.GetColor("_BaseColor"), Is.EqualTo(previousBehaviour.GetColor("_BaseColor")));
                Assert.That(actual.GetColor("_BaseColor"), Is.Not.EqualTo(sourceBefore));
                Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(sourceBefore));
            }
            finally
            {
                Object.DestroyImmediate(actual);
                Object.DestroyImmediate(previousBehaviour);
            }
        }
    }
}
