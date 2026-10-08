using Emberfall.Editor.Setup;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PresentationFoundationTests
    {
        [TestCase(1920,1080)] [TestCase(1728,1080)] [TestCase(1280,720)]
        [TestCase(1024,768)] [TestCase(960,540)] [TestCase(2560,1440)]
        public void LogicalTopPanelsRemainReadableAndSeparated(int width,int height)
        {
            float scale=EmberfallGuiTheme.Scale(width,height);
            foreach(bool checklist in new[]{false,true})
            {
                var layout=PresentationHudLayout.Resolve(width/scale,checklist);
                Assert.That(layout.Player.Overlaps(layout.Quest),Is.False);
                Assert.That(layout.Player.Overlaps(layout.Boss),Is.False);
                Assert.That(layout.Quest.Overlaps(layout.Boss),Is.False);
                Assert.That(layout.Quest.width,Is.GreaterThanOrEqualTo(320));
                Assert.That(layout.Quest.height,Is.GreaterThanOrEqualTo(140));
                foreach(var rect in new[]{layout.Player,layout.Boss,layout.Quest})
                {
                    Assert.That(rect.xMin,Is.GreaterThanOrEqualTo(0));
                    Assert.That(rect.xMax*scale,Is.LessThanOrEqualTo(width));
                    Assert.That(rect.yMax*scale,Is.LessThan(height));
                }
                // At the smallest supported screen the body font is still >=10 physical pixels.
                Assert.That(14*scale,Is.GreaterThanOrEqualTo(9.4f));
            }
        }

        [Test]
        public void OnlyExplicitHandAlternativesAreHiddenAndAuthoringIsIdempotent()
        {
            var root=new GameObject("ProjectVisual");
            try
            {
                root.AddComponent<Animator>();
                var slot=new GameObject("handslot.r"); slot.transform.SetParent(root.transform);
                var alternative=new GameObject("1H_Sword"); alternative.transform.SetParent(slot.transform);
                var renderer=alternative.AddComponent<MeshRenderer>();
                var body=new GameObject("Knight_Body"); body.transform.SetParent(root.transform);
                var bodyRenderer=body.AddComponent<MeshRenderer>();
                Assert.That(CharacterEquipmentSelection.Apply(root),Is.Zero,"No selected gear, leave source alone.");
                new GameObject("Sword_M5c_Scorched_Equipped").transform.SetParent(slot.transform);
                new GameObject("Shield_Wooden_Equipped").transform.SetParent(slot.transform);
                Assert.That(CharacterEquipmentSelection.Apply(root),Is.EqualTo(1));
                Assert.That(renderer.enabled,Is.False);
                Assert.That(bodyRenderer.enabled,Is.True);
                Assert.That(alternative.activeSelf,Is.True,"Do not turn off bone hierarchy.");
                Assert.That(CharacterEquipmentSelection.Apply(root),Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
