using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class LayeredHudPresentationTests
    {
        [TestCase(400, 200)] [TestCase(400, 20)] [TestCase(900, 20)]
        public void SummonerPlateStaysAboveIdentityOrBesideItAtScreenEdge(float x, float y)
        {
            var panel = M2RouteHud.ResolveSummonerPlate(new Vector2(x, y), 960, 540);
            Assert.That(panel.width, Is.EqualTo(320)); Assert.That(panel.height, Is.EqualTo(72));
            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(4)); Assert.That(panel.xMax, Is.LessThanOrEqualTo(956));
            Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(4)); Assert.That(panel.yMax, Is.LessThanOrEqualTo(536));
            Assert.That(panel.yMax <= y - 8 || panel.xMin >= x + 48 || panel.xMax <= x - 48, Is.True);
        }
        [TestCase(960, false, 40f)] [TestCase(960, true, 64f)]
        [TestCase(1280, false, 40f)] [TestCase(1280, true, 64f)]
        public void MultiLineChoiceReceiptKeepsItsFullAllocationAndPanelSeparation(int width, bool checklist, float height)
        {
            var layout = PresentationHudLayout.Resolve(width, checklist, height);
            Rect message = layout.QuestMessage(checklist);
            Assert.That(message.height, Is.GreaterThanOrEqualTo(height));
            Assert.That(message.yMax, Is.LessThanOrEqualTo(layout.Quest.yMax - 12));
            Assert.That(layout.Quest.Overlaps(layout.Player), Is.False);
            Assert.That(layout.Quest.Overlaps(layout.Boss), Is.False);
            Assert.That(message.width, Is.EqualTo(298));
        }

        [TestCase(TextAnchor.UpperLeft, 0, 0)]
        [TestCase(TextAnchor.UpperCenter, 1, 0)]
        [TestCase(TextAnchor.UpperRight, 2, 0)]
        [TestCase(TextAnchor.MiddleLeft, 0, 1)]
        [TestCase(TextAnchor.MiddleCenter, 1, 1)]
        [TestCase(TextAnchor.MiddleRight, 2, 1)]
        [TestCase(TextAnchor.LowerLeft, 0, 2)]
        [TestCase(TextAnchor.LowerCenter, 1, 2)]
        [TestCase(TextAnchor.LowerRight, 2, 2)]
        public void LocalBackingFollowsLabelAlignmentWithoutExpandingContent(TextAnchor anchor, int horizontal, int vertical)
        {
            var content = new Rect(100, 200, 300, 70);
            Rect backing = EmberfallGuiTheme.HudTextBacking(content, new Vector2(60, 18), anchor);
            Assert.That(backing.width, Is.EqualTo(68));
            Assert.That(backing.height, Is.EqualTo(22));
            Assert.That(backing.x, Is.EqualTo(100 + horizontal * (300 - 68) / 2f));
            Assert.That(backing.y, Is.EqualTo(200 + vertical * (70 - 22) / 2f));
            Assert.That(content, Is.EqualTo(new Rect(100, 200, 300, 70)));
        }

        [TestCase(20f)] [TestCase(200f)] [TestCase(440f)] [TestCase(464f)]
        public void SummonExecutionMarkerAvoidsOwnerPlateWithoutShrinkingText(float y)
        {
            var plate = new Rect(400, y, 320, 72);
            var marker = new Rect(500, y + 26, 208, 28);
            Assert.That(marker.Overlaps(plate), Is.True);
            Rect moved = M2RouteHud.AvoidSummonerPlate(marker, plate, 540);
            Assert.That(moved.Overlaps(plate), Is.False);
            Assert.That(moved.width, Is.EqualTo(marker.width)); Assert.That(moved.height, Is.EqualTo(marker.height));
            Assert.That(moved.x, Is.EqualTo(marker.x));
            Assert.That(moved.yMin, Is.GreaterThanOrEqualTo(4)); Assert.That(moved.yMax, Is.LessThanOrEqualTo(536));
        }

        [Test] public void SeparateExecutionMarkerIsNotMovedByOwnerPlate()
        {
            var marker = new Rect(20, 20, 208, 28);
            Assert.That(M2RouteHud.AvoidSummonerPlate(marker, new Rect(400, 200, 320, 72), 540), Is.EqualTo(marker));
        }

        [TestCase(960, 540)] [TestCase(1024, 768)] [TestCase(1920, 1080)] [TestCase(2560, 1440)]
        public void WrappedBackingStaysWithinBothExistingOfflineAndNetworkAllocations(int width, int height)
        {
            float scale = EmberfallGuiTheme.Scale(width, height);
            var offline = PresentationHudLayout.Resolve(width / scale, true);
            var network = NetworkHudLayout.Resolve(width / scale);
            foreach (Rect panel in new[] { offline.Player, offline.Quest, offline.Boss, network.Player, network.Quest, network.Boss })
            {
                Rect content = new Rect(panel.x + 16, panel.y + 8, panel.width - 32, 26);
                foreach (TextAnchor anchor in System.Enum.GetValues(typeof(TextAnchor)))
                {
                    Rect backing = EmberfallGuiTheme.HudTextBacking(content, new Vector2(1200, 120), anchor);
                    Assert.That(backing, Is.EqualTo(content), "Long/wrapped text retains its entire original region.");
                    Assert.That(backing.xMin * scale, Is.GreaterThanOrEqualTo(0));
                    Assert.That(backing.xMax * scale, Is.LessThanOrEqualTo(width));
                }
            }
        }
    }
}
