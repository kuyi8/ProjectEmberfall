using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class RouteCompletionPresentationTests
    {
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public void IndependentFlagsDoNotInventParticipationOrScores(bool a, bool b, bool c)
        {
            string[] lines = RouteCompletionPresentation.DescribeEncounters(a, b, c).Split('\n');
            bool[] flags = { a, b, c };
            for (int i = 0; i < 3; i++)
                Assert.That(lines[i], Does.EndWith(flags[i] ? "已清场" : "尚未清场（可选）"));
            Assert.That(string.Join("\n", lines), Does.Not.Contain("未参加").And.Not.Contain("失败").And.Not.Contain("N/A"));
        }

        [TestCase("checkpoint:camp", "林地营地")]
        [TestCase("checkpoint:courtyard", "圣所前庭")]
        [TestCase(null, "未记录休整点")]
        [TestCase("", "未记录休整点")]
        [TestCase("checkpoint:unknown", "未知休整点")]
        public void JourneyNamesItsSavedScopeAndHandlesMissingCheckpoint(string checkpoint, string label)
        {
            string text = RouteCompletionPresentation.DescribeJourney(3661f, 4, checkpoint);
            Assert.That(text, Does.Contain("存档累计用时：61:01").And.Contain("存档累计倒地：4 次").And.Contain(label));
            Assert.That(text, Does.Not.Contain("本局").And.Not.Contain("有效游玩"));
        }

        [TestCase(960, 540)]
        [TestCase(1280, 720)]
        [TestCase(1280, 800)]
        [TestCase(1920, 1080)]
        [TestCase(2560, 1600)]
        public void CardsScopeAndButtonFitTheLogicalCanvas(int width, int height)
        {
            float scale = EmberfallGuiTheme.Scale(width, height);
            var l = RouteCompletionLayout.Resolve(width / scale, height / scale);
            Rect canvas = new Rect(0, 0, width / scale, height / scale);
            foreach (Rect r in new[] { l.Panel, l.Title, l.Subtitle, l.Choices, l.Rewards, l.Encounters, l.Journey, l.Scope, l.Return })
            { Assert.That(canvas.Contains(r.min), Is.True); Assert.That(canvas.Contains(r.max), Is.True); }
            Assert.That(l.Choices.Overlaps(l.Rewards), Is.False);
            Assert.That(l.Encounters.Overlaps(l.Journey), Is.False);
            Assert.That(l.Journey.yMax, Is.LessThan(l.Scope.yMin));
            Assert.That(l.Scope.yMax, Is.LessThan(l.Return.yMin));
            Assert.That(l.Return.height, Is.EqualTo(48));
        }
    }
}
