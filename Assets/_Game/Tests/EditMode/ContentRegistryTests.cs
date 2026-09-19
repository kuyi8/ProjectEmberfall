using System;
using System.Collections.Generic;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ContentRegistryTests
    {
        [Test]
        public void Constructor_IndexesDefinitionsByStableId()
        {
            var expected = new StubDefinition("enemy:fog_thrall");
            var registry = new ContentRegistry<StubDefinition>(new[] { expected });

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.GetRequired(expected.Id), Is.SameAs(expected));
        }

        [Test]
        public void Constructor_RejectsDuplicateIds()
        {
            var definitions = new[]
            {
                new StubDefinition("enemy:fog_thrall"),
                new StubDefinition("enemy:fog_thrall")
            };

            Assert.Throws<ArgumentException>(() => _ = new ContentRegistry<StubDefinition>(definitions));
        }

        [Test]
        public void GetRequired_RejectsUnknownId()
        {
            var registry = new ContentRegistry<StubDefinition>(Array.Empty<StubDefinition>());

            Assert.Throws<KeyNotFoundException>(() => registry.GetRequired(new ContentId("enemy:missing")));
        }

        private sealed class StubDefinition : IContentDefinition
        {
            public StubDefinition(string id)
            {
                Id = new ContentId(id);
            }

            public ContentId Id { get; }
        }
    }
}
