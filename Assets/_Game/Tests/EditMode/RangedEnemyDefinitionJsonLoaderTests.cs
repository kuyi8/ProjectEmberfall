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
    public sealed class RangedEnemyDefinitionJsonLoaderTests
    {
        private const string BuiltinEnemyPath = "Assets/_Game/Data/M2/enemies.v1.json";

        [Test]
        public void BuiltinEnemyJson_LoadsCompleteRunePriestDefinition()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuiltinEnemyPath);
            Assert.That(asset, Is.Not.Null);

            ContentRegistry<RangedEnemyDefinition> catalog = RangedEnemyDefinitionJsonLoader.Load(asset.text);
            RangedEnemyDefinition definition = catalog.GetRequired(new ContentId("enemy:rune-priest"));

            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(definition.DisplayNameTextId.Value, Is.EqualTo("text:enemy.rune-priest.name"));
            Assert.That(definition.PreferredMinimumRange, Is.LessThan(definition.PreferredMaximumRange));
            Assert.That(definition.ProjectileSpeed, Is.GreaterThan(0f));
            Assert.That(definition.GroundRuneTriggerDelay, Is.GreaterThan(0f));
            Assert.That(definition.GroundRuneRadius, Is.GreaterThan(1f));
        }

        [Test]
        public void RangedEnemyJson_RejectsMissingCategory()
        {
            const string json = "{\"schemaVersion\":1,\"enemies\":[]}";
            Assert.Throws<NotSupportedException>(() => RangedEnemyDefinitionJsonLoader.Load(json));
        }
    }
}
