using System;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class MeleeEnemyDefinitionJsonLoaderTests
    {
        private const string BuiltinEnemyPath = "Assets/_Game/Data/M2/enemies.v1.json";

        [Test]
        public void BuiltinEnemyJson_LoadsCompleteFogwalkerDefinition()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuiltinEnemyPath);
            Assert.That(asset, Is.Not.Null);

            ContentRegistry<MeleeEnemyDefinition> catalog = MeleeEnemyDefinitionJsonLoader.Load(asset.text);
            MeleeEnemyDefinition definition = catalog.GetRequired(new ContentId("enemy:fogwalker"));

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(definition.DisplayNameTextId.Value, Is.EqualTo("text:enemy.fogwalker.name"));
            Assert.That(definition.DamageWindowStart, Is.LessThan(definition.DamageWindowEnd));
            Assert.That(definition.LoseTargetRange, Is.GreaterThanOrEqualTo(definition.DetectionRange));
            Assert.That(definition.ComboDamageWindow2Start, Is.GreaterThan(definition.ComboDamageWindow1End));
            Assert.That(definition.ComboHitDamage, Is.GreaterThan(0f));
        }

        [Test]
        public void EnemyJson_RejectsUnsupportedSchema()
        {
            const string json = "{\"schemaVersion\":2,\"enemies\":[]}";
            Assert.Throws<NotSupportedException>(() => MeleeEnemyDefinitionJsonLoader.Load(json));
        }

        [Test]
        public void EnemyJson_RejectsDuplicateStableIds()
        {
            const string definition =
                "{\"id\":\"enemy:test\",\"displayNameTextId\":\"text:enemy.test\"," +
                "\"maximumHealth\":100,\"armor\":0,\"detectionRange\":9,\"loseTargetRange\":13," +
                "\"fieldOfView\":170,\"leashRange\":16,\"attackRange\":1.5,\"moveSpeed\":2," +
                "\"rotationSpeed\":360,\"windupDuration\":0.5,\"attackDuration\":0.5," +
                "\"damageWindowStart\":0.1,\"damageWindowEnd\":0.3,\"recoveryDuration\":0.7," +
                "\"attackDamage\":20,\"postureDamage\":10,\"hitReactDuration\":0.3,\"respawnDelay\":3}";
            string json = "{\"schemaVersion\":1,\"enemies\":[" + definition + "," + definition + "]}";
            Assert.Throws<ArgumentException>(() => MeleeEnemyDefinitionJsonLoader.Load(json));
        }
    }
}
