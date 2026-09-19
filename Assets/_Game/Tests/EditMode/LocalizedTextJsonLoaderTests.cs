using System;
using Emberfall.Application.Content;
using Emberfall.Core.Identifiers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class LocalizedTextJsonLoaderTests
    {
        private const string BuiltinTextPath = "Assets/_Game/Data/M2/texts.zh-CN.v1.json";

        [Test]
        public void BuiltinChineseTextJson_ResolvesStableTextIds()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(BuiltinTextPath);
            Assert.That(asset, Is.Not.Null);

            LocalizedTextCatalog catalog = LocalizedTextJsonLoader.Load(asset.text);

            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(17));
            Assert.That(catalog.Resolve(new ContentId("text:quest.main.title")), Is.EqualTo("主线：余烬封印"));
            Assert.That(catalog.Resolve(new ContentId("text:interaction.activate-seal")), Does.Contain("封印碑"));
            Assert.That(catalog.Resolve(new ContentId("text:network.quest.defeat-warden")),
                Is.EqualTo("击败余烬守墓人"));
            Assert.That(catalog.Resolve(new ContentId("text:network.interaction.seal-activated")),
                Is.EqualTo("封印已激活"));
            Assert.That(catalog.Resolve(new ContentId("text:network.warden.state-transition")),
                Is.EqualTo("重铸护甲 · 暂时无法受击"));
            Assert.That(catalog.Resolve(new ContentId("text:interaction.bridge-mechanism-b-locked")),
                Does.Contain("需先清除桥上守卫"));
            Assert.That(catalog.Resolve(new ContentId("text:ui.bridge-seal-conditions")),
                Does.Contain("前置机关"));
        }

        [Test]
        public void TextJson_RejectsUnsupportedSchemaAndDuplicateIds()
        {
            const string unsupported = "{\"schemaVersion\":2,\"entries\":[{\"id\":\"text:test\",\"value\":\"A\"}]}";
            const string duplicate = "{\"schemaVersion\":1,\"entries\":[" +
                                     "{\"id\":\"text:test\",\"value\":\"A\"}," +
                                     "{\"id\":\"text:test\",\"value\":\"B\"}]}";

            Assert.Throws<NotSupportedException>(() => LocalizedTextJsonLoader.Load(unsupported));
            Assert.Throws<ArgumentException>(() => LocalizedTextJsonLoader.Load(duplicate));
        }
    }
}
