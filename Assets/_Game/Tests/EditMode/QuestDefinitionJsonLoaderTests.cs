using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Quests.Data;
using Emberfall.Quests.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class QuestDefinitionJsonLoaderTests
    {
        private const string BuiltinQuestPath = "Assets/_Game/Data/M2/quests.v1.json";

        [Test]
        public void BuiltinQuestJson_LoadsStableIdDefinition()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuiltinQuestPath);
            Assert.That(asset, Is.Not.Null);

            ContentRegistry<QuestDefinition> registry = QuestDefinitionJsonLoader.Load(asset.text);
            QuestDefinition definition = registry.GetRequired(new ContentId("quest:emberfall.main"));
            QuestDefinition forest = registry.GetRequired(new ContentId("quest:forest-seal"));

            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(definition.TitleTextId.Value, Is.EqualTo("text:quest.main.title"));
            Assert.That(definition.RequiredSealCount, Is.EqualTo(3));
            Assert.That(forest.TitleTextId.Value, Is.EqualTo("text:quest.forest.title"));
            Assert.That(forest.RequiredSealCount, Is.EqualTo(1));
        }

        [Test]
        public void QuestJson_RejectsUnsupportedSchema()
        {
            const string json = "{\"schemaVersion\":2,\"quests\":[{\"id\":\"quest:test\",\"titleTextId\":\"text:test\",\"requiredSealCount\":1}]}";
            Assert.Throws<NotSupportedException>(() => QuestDefinitionJsonLoader.Load(json));
        }

        [Test]
        public void QuestJson_RejectsDuplicateStableIds()
        {
            const string json = "{\"schemaVersion\":1,\"quests\":[" +
                                "{\"id\":\"quest:test\",\"titleTextId\":\"text:first\",\"requiredSealCount\":1}," +
                                "{\"id\":\"quest:test\",\"titleTextId\":\"text:second\",\"requiredSealCount\":2}]}";
            Assert.Throws<ArgumentException>(() => QuestDefinitionJsonLoader.Load(json));
        }
    }
}
