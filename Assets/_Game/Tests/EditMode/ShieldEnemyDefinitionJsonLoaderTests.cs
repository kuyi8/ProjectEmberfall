using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class ShieldEnemyDefinitionJsonLoaderTests
    {
        [Test]
        public void BuiltinEnemyJson_LoadsRuinGuardContract()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/_Game/Data/M2/enemies.v1.json");
            Assert.That(asset, Is.Not.Null);

            ContentRegistry<ShieldEnemyDefinition> catalog =
                ShieldEnemyDefinitionJsonLoader.Load(asset.text);
            ShieldEnemyDefinition definition = catalog.GetRequired(
                new ContentId("enemy:ruin-guard"));

            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(definition.DisplayNameTextId.Value,
                Is.EqualTo("text:enemy.ruin-guard.name"));
            Assert.That(definition.GuardCapacity, Is.EqualTo(80f));
            Assert.That(definition.FrontalBlockAngle, Is.EqualTo(140f));
            Assert.That(definition.BrokenDamageMultiplier, Is.GreaterThan(1f));
            Assert.That(definition.BashWindupDuration, Is.LessThan(definition.WindupDuration));
            Assert.That(definition.BashPostureDamage, Is.GreaterThan(definition.PostureDamage));

            ShieldEnemyDefinition scorched = catalog.GetRequired(
                new ContentId("enemy:ruin-guard-scorched"));
            Assert.That(scorched.DisplayNameTextId.Value,
                Is.EqualTo("text:enemy.ruin-guard-scorched.name"));
            Assert.That(scorched.ScorchedBurstEnabled, Is.True);
            Assert.That(scorched.MaximumHealth, Is.GreaterThan(definition.MaximumHealth * 1.25f));
            Assert.That(scorched.GuardCapacity, Is.GreaterThan(definition.GuardCapacity * 1.25f));
            Assert.That(scorched.HitReactDuration, Is.LessThan(definition.HitReactDuration));
            Assert.That(scorched.ScorchedBurstWindupDuration, Is.GreaterThan(definition.WindupDuration));
            Assert.That(scorched.ScorchedBurstRadius, Is.GreaterThan(definition.AttackRange));
        }
    }
}
