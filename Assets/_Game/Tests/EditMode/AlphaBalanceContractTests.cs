using System.Linq;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class AlphaBalanceContractTests
    {
        [Test]
        public void BuiltinRoute_PreservesReadableDifficultyAndRecoveryBudget()
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json");
            CombatTuningAsset tuningAsset = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(
                "Assets/_Game/Settings/CombatTuning_M1.asset");
            Assert.That(json, Is.Not.Null);
            Assert.That(tuningAsset, Is.Not.Null);

            var tuning = tuningAsset.CreateRuntimeCopy();
            MeleeEnemyDefinition fogwalker = MeleeEnemyDefinitionJsonLoader.Load(json.text)
                .GetRequired(new ContentId("enemy:fogwalker"));
            RangedEnemyDefinition priest = RangedEnemyDefinitionJsonLoader.Load(json.text)
                .GetRequired(new ContentId("enemy:rune-priest"));
            ShieldEnemyDefinition guard = ShieldEnemyDefinitionJsonLoader.Load(json.text)
                .GetRequired(new ContentId("enemy:ruin-guard"));
            WardenDefinition warden = WardenDefinitionJsonLoader.Load(json.text)
                .GetRequired(new ContentId("boss:ember-warden"));

            Assert.That(priest.MaximumHealth, Is.LessThan(fogwalker.MaximumHealth));
            Assert.That(fogwalker.MaximumHealth, Is.LessThan(guard.MaximumHealth));
            Assert.That(guard.MaximumHealth, Is.LessThan(warden.MaximumHealth));

            float maximumSingleHit = new[]
            {
                fogwalker.AttackDamage,
                priest.GroundRuneDamage,
                guard.AttackDamage,
                guard.BashDamage,
                warden.SwordCombo.Damage,
                warden.ShieldBash.Damage,
                warden.Charge.Damage,
                warden.RuneCleave.Damage,
                warden.DelayedBlast.Damage
            }.Max();
            Assert.That(maximumSingleHit, Is.LessThanOrEqualTo(tuning.MaxHealth * 0.3f),
                "The Alpha curve must not kill a full-health player with one readable action.");
            Assert.That(tuning.HealingFlaskCharges * tuning.HealHealthFraction,
                Is.GreaterThanOrEqualTo(0.9f), "Two committed heals must restore roughly one full health bar.");
            Assert.That(warden.RuneCleave.WindupDuration, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(warden.DelayedBlastFuse, Is.GreaterThanOrEqualTo(1f));
            Assert.That(warden.PhaseTwoThreshold, Is.EqualTo(0.55f));
        }
    }
}
