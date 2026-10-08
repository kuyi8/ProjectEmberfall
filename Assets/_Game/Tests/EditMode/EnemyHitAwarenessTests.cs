using System;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class EnemyHitAwarenessTests
    {
        static DamageRequest Hit(int source = -12) => new DamageRequest(source, 1, 10, 1, AttackTag.Light);
        static DamageResult Accepted => new DamageResult(true, false, 10, false);

        [Test] public void LegalHit_IsBoundedToAttackerAndExpires()
        {
            var memory = new EnemyHitAwareness();
            Assert.That(memory.Record(Hit(), Accepted, -12, true, true, 2), Is.True);
            Assert.That(memory.IsAwareOf(-12), Is.True);
            Assert.That(memory.IsAwareOf(13), Is.False);
            memory.Tick(1.9f); Assert.That(memory.IsAwareOf(-12), Is.True);
            memory.Tick(.11f); Assert.That(memory.IsAwareOf(-12), Is.False);
            Assert.That(memory.Remaining, Is.Zero);
        }

        [TestCase(false, true)] [TestCase(true, false)]
        public void UnavailableOrOutsideEncounter_CannotWake(bool available, bool allowed)
        {
            var memory = new EnemyHitAwareness();
            Assert.That(memory.Record(Hit(), Accepted, -12, available, allowed, 3), Is.False);
            Assert.That(memory.Remaining, Is.Zero);
        }

        [Test] public void WrongAttacker_IgnoredEvadedAndLethalHits_CannotWake()
        {
            var memory = new EnemyHitAwareness();
            Assert.That(memory.Record(Hit(99), Accepted, -12, true, true, 3), Is.False);
            foreach (var result in new[] { DamageResult.Ignored, DamageResult.Evaded, new DamageResult(true, false, 10, true) })
                Assert.That(memory.Record(Hit(), result, -12, true, true, 3), Is.False);
            Assert.That(memory.Remaining, Is.Zero);
        }

        [Test] public void LegitimateShieldBlock_IsAlsoAHitStimulus()
        {
            var memory = new EnemyHitAwareness();
            Assert.That(memory.Record(Hit(), new DamageResult(false, false, 0, false, true), -12, true, true, 3), Is.True);
        }

        [Test] public void RepeatedHit_RefreshesOnlyAwareness_AndClearRemovesIt()
        {
            var memory = new EnemyHitAwareness();
            memory.Record(Hit(), Accepted, -12, true, true, 2); memory.Tick(1);
            memory.Record(Hit(), Accepted, -12, true, true, 2);
            Assert.That(memory.Remaining, Is.EqualTo(2));
            memory.Clear(); Assert.That(memory.IsAwareOf(-12), Is.False);
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidDuration_IsRejected(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyHitAwareness().Record(Hit(), Accepted, -12, true, true, value));
        }

        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidTick_IsRejected(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyHitAwareness().Tick(value));
        }

        [Test] public void AuthoredCombatTuning_RuntimeCopyOwnsTheValue()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.CreateRuntimeCopy().EnemyHitAwarenessSeconds, Is.EqualTo(3));
            var copy = Object.Instantiate(asset);
            try
            {
                var so = new SerializedObject(copy); so.FindProperty("_enemyHitAwarenessSeconds").floatValue = 1.25f;
                so.ApplyModifiedPropertiesWithoutUndo(); var runtime = copy.CreateRuntimeCopy();
                so.FindProperty("_enemyHitAwarenessSeconds").floatValue = 4f; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(runtime.EnemyHitAwarenessSeconds, Is.EqualTo(1.25f));
                Assert.That(asset.CreateRuntimeCopy().EnemyHitAwarenessSeconds, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(copy); }
        }
    }
}
