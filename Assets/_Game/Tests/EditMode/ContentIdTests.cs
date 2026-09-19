using System;
using Emberfall.Core.Identifiers;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ContentIdTests
    {
        [TestCase("enemy:fog_thrall")]
        [TestCase("quest:main.01")]
        [TestCase("ability:rune-burst")]
        public void Constructor_AcceptsStableIds(string value)
        {
            var contentId = new ContentId(value);

            Assert.That(contentId.Value, Is.EqualTo(value));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("enemy")]
        [TestCase(":enemy")]
        [TestCase("enemy:")]
        [TestCase("Enemy:fog_thrall")]
        [TestCase("enemy:fog thrall")]
        [TestCase("enemy:fog:thrall")]
        public void Constructor_RejectsUnstableIds(string value)
        {
            Assert.Throws<ArgumentException>(() => _ = new ContentId(value));
        }
    }
}

