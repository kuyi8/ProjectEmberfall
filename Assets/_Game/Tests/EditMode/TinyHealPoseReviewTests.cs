using System;
using Emberfall.Editor.Review;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class TinyHealPoseReviewTests
    {
        [Test]
        public void OwnBottleTriangles_FaceOutwardAndHaveActual18cmSilhouette()
        {
            var mesh = TinyHealPoseReview.CreateBottleMeshForReview();
            try
            {
                Assert.That(mesh.bounds.size.y, Is.EqualTo(.18f).Within(.000001f));
                Assert.That(mesh.bounds.size.x, Is.EqualTo(.104f).Within(.000001f));
                var vertices = mesh.vertices; var triangles = mesh.triangles;
                for (int triangle = 0; triangle < 64; triangle++)
                {
                    Vector3 a = vertices[triangles[triangle * 3]], b = vertices[triangles[triangle * 3 + 1]], c = vertices[triangles[triangle * 3 + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a), center = (a + b + c) / 3;
                    Assert.That(Vector3.Dot(normal, new Vector3(center.x, 0, center.z)), Is.GreaterThan(0), "Side triangle " + triangle);
                }
                for (int triangle = 64; triangle < triangles.Length / 3; triangle++)
                {
                    Vector3 a = vertices[triangles[triangle * 3]], b = vertices[triangles[triangle * 3 + 1]], c = vertices[triangles[triangle * 3 + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    Assert.That(Vector3.Dot(normal, a.y < 0 ? Vector3.down : Vector3.up), Is.GreaterThan(0), "Cap triangle " + triangle);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SipTiming_UsesExistingDomainResolveAndEndsAtActualCommitDuration()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            Assert.That(tuning.HealResolveTime, Is.EqualTo(.78f).Within(.0001f));
            Assert.That(tuning.HealDuration, Is.EqualTo(1.05f).Within(.0001f));
            var timing = TinyHealPoseReview.GetTiming(tuning.HealDuration, tuning.HealResolveTime);
            Assert.That(timing.pickupEnd, Is.GreaterThan(0).And.LessThan(timing.mouthArrival));
            Assert.That(timing.mouthArrival, Is.LessThan(tuning.HealResolveTime));
            Assert.That(timing.resolve, Is.EqualTo(tuning.HealResolveTime));
            Assert.That(timing.end, Is.EqualTo(tuning.HealDuration));
            Assert.That(TinyHealPoseReview.SipWeight(0, timing.end, timing.resolve), Is.Zero);
            Assert.That(TinyHealPoseReview.SipWeight(timing.resolve, timing.end, timing.resolve), Is.EqualTo(1));
            Assert.That(TinyHealPoseReview.SipWeight(timing.end, timing.end, timing.resolve), Is.Zero);
        }

        [Test]
        public void SipEnvelope_IsBoundedContinuousAndHoldsAtTheDomainResolvePoint()
        {
            var timing = TinyHealPoseReview.GetTiming(1.05f, .78f);
            float previous = 0;
            for (int i = 0; i <= 1050; i++)
            {
                float time = i * .001f, value = TinyHealPoseReview.SipWeight(time, timing.end, timing.resolve);
                Assert.That(value, Is.InRange(0f, 1f));
                Assert.That(Mathf.Abs(value - previous), Is.LessThan(.01f));
                if (time >= timing.mouthArrival && time <= timing.resolve) Assert.That(value, Is.EqualTo(1));
                previous = value;
            }
            Assert.That(TinyHealPoseReview.Phase(.779f, timing.end, timing.resolve), Is.EqualTo("SipBeforeDomainResolve"));
            Assert.That(TinyHealPoseReview.Phase(.781f, timing.end, timing.resolve), Is.EqualTo("ReturnFlask"));
            Assert.That(TinyHealPoseReview.Phase(timing.end, timing.end, timing.resolve), Is.EqualTo("NativeIdle"));
        }

        [Test]
        public void InvalidTiming_IsRejectedRatherThanAuthoringAnotherHealingClock()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, 0f, -1f })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => TinyHealPoseReview.GetTiming(value, .78f));
                Assert.Throws<ArgumentOutOfRangeException>(() => TinyHealPoseReview.GetTiming(1.05f, value));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => TinyHealPoseReview.GetTiming(.78f, .78f));
            Assert.Throws<ArgumentOutOfRangeException>(() => TinyHealPoseReview.GetTiming(.7f, .78f));
        }
    }
}
