using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class HudInformationPresentationTests
    {
        [TestCase(.25f, .2f, "生命危急", "耐力偏低", true, true)]
        [TestCase(.2501f, .2f, "生命", "耐力偏低", false, true)]
        [TestCase(.25f, .2001f, "生命危急", "耐力", true, false)]
        [TestCase(1f, 1f, "生命", "耐力", false, false)]
        public void LowResourceThresholdsAreStableAndDoNotChangeCombatEligibility(
            float health, float stamina, string healthLabel, string staminaLabel, bool lowHealth, bool lowStamina)
        {
            for (int sample = 0; sample < 32; sample++)
            {
                var display = HudResourcePresentation.Resolve(health, stamina, CombatState.Locomotion, 2);
                Assert.That(display.HealthLabel, Is.EqualTo(healthLabel));
                Assert.That(display.StaminaLabel, Is.EqualTo(staminaLabel));
                Assert.That(display.HealthAccent, Is.EqualTo(lowHealth ? EmberfallGuiTheme.Danger : EmberfallGuiTheme.Text));
                Assert.That(display.StaminaAccent, Is.EqualTo(lowStamina ? EmberfallGuiTheme.Execution : EmberfallGuiTheme.Text));
                Assert.That(display.IsFlaskReady, Is.EqualTo(health < 1f), "Low stamina is not a heal restriction.");
            }
        }

        [TestCase(CombatState.Dead, false, 1f, 0, "不可使用", "已阵亡", false)]
        [TestCase(CombatState.Heal, true, .1f, 0, "不可使用", "已倒地", false)]
        [TestCase(CombatState.Locomotion, true, .1f, 2, "不可使用", "已倒地", false)]
        [TestCase(CombatState.Heal, false, 1f, 0, "治疗中", "生命", false)]
        [TestCase(CombatState.Heal, false, .1f, 2, "治疗中", "生命危急", false)]
        [TestCase(CombatState.Locomotion, false, 1f, 0, "药剂耗尽", "生命", false)]
        [TestCase(CombatState.Guard, false, .1f, 0, "药剂耗尽", "生命危急", false)]
        [TestCase(CombatState.Guard, false, 1f, 2, "生命已满", "生命", false)]
        [TestCase(CombatState.Locomotion, false, 1f, 2, "生命已满", "生命", false)]
        [TestCase(CombatState.Guard, false, .5f, 2, "动作中", "生命", false)]
        [TestCase(CombatState.HitReact, false, .1f, 2, "动作中", "生命危急", false)]
        [TestCase(CombatState.Locomotion, false, .5f, 2, "药剂就绪", "生命", true)]
        public void FlaskReasonsUseExplicitPriorityAndTerminalClearsLowStamina(
            CombatState state, bool downed, float health, int charges, string flaskLabel, string healthLabel, bool ready)
        {
            var display = HudResourcePresentation.Resolve(health, .1f, state, charges, downed);
            Assert.That(display.FlaskLabel, Is.EqualTo(flaskLabel));
            Assert.That(display.HealthLabel, Is.EqualTo(healthLabel));
            Assert.That(display.IsFlaskReady, Is.EqualTo(ready));
            bool terminal = downed || state == CombatState.Dead;
            Assert.That(display.StaminaLabel, Is.EqualTo(terminal ? "耐力" : "耐力偏低"));
            Assert.That(display.StaminaAccent, Is.EqualTo(terminal ? EmberfallGuiTheme.Text : EmberfallGuiTheme.Execution));
            Assert.That(display.HealthAccent, Is.EqualTo(terminal || health <= .25f ? EmberfallGuiTheme.Danger : EmberfallGuiTheme.Text));
            Assert.That(display.FlaskAccent, Is.EqualTo(ready || (!terminal && state == CombatState.Heal)
                ? EmberfallGuiTheme.Interaction : EmberfallGuiTheme.Muted));
        }

        [Test]
        public void EveryExistingCombatStateHasAnUnambiguousFlaskState()
        {
            foreach (CombatState state in Enum.GetValues(typeof(CombatState)))
            {
                var display = HudResourcePresentation.Resolve(.5f, 1f, state, 2);
                string expected = state == CombatState.Dead ? "不可使用" : state == CombatState.Heal ? "治疗中"
                    : state == CombatState.Locomotion ? "药剂就绪" : "动作中";
                Assert.That(display.FlaskLabel, Is.EqualTo(expected), state.ToString());
                Assert.That(display.IsFlaskReady, Is.EqualTo(state == CombatState.Locomotion), state.ToString());
            }
        }

        [TestCase(false, false, -1, "生命已满", false)]
        [TestCase(true, false, -1, "药剂就绪", true)]
        [TestCase(true, true, -1, "药剂耗尽", false)]
        [TestCase(true, false, (int)CombatCommand.GuardPressed, "动作中", false)]
        [TestCase(true, false, (int)CombatCommand.RangedAttack, "动作中", false)]
        [TestCase(true, false, (int)CombatCommand.LightAttack, "动作中", false)]
        [TestCase(true, false, (int)CombatCommand.Heal, "治疗中", false)]
        public void DisplayReadsRealDomainWithoutMutatingResourcesOrInventingHealRestrictions(
            bool damaged, bool empty, int initialCommand, string label, bool ready)
        {
            var model = new CombatStateMachine(CombatTuning.CreateDefault());
            if (damaged) model.Health.ApplyDamage(model.Health.Maximum * .5f, 0f);
            if (empty) while (model.HealingFlasks.TryConsume()) { }
            if (initialCommand >= 0) Assert.That(model.Submit((CombatCommand)initialCommand), Is.True);
            CombatState state = model.State;
            float health = model.Health.Current, stamina = model.Stamina.Current, posture = model.Posture.Current;
            float elapsed = model.StateElapsed, rangedCooldown = model.RangedCooldownRemaining;
            int charges = model.HealingFlasks.CurrentCharges, attacks = model.AttackSequence, heals = model.HealSequence;
            for (int sample = 0; sample < 32; sample++)
            {
                var display = HudResourcePresentation.Resolve(model.Health.Normalized, model.Stamina.Normalized, model.State, charges);
                Assert.That(display.FlaskLabel, Is.EqualTo(label));
                Assert.That(display.IsFlaskReady, Is.EqualTo(ready));
            }
            Assert.That(model.State, Is.EqualTo(state));
            Assert.That(model.Health.Current, Is.EqualTo(health));
            Assert.That(model.Stamina.Current, Is.EqualTo(stamina));
            Assert.That(model.Posture.Current, Is.EqualTo(posture));
            Assert.That(model.StateElapsed, Is.EqualTo(elapsed));
            Assert.That(model.RangedCooldownRemaining, Is.EqualTo(rangedCooldown));
            Assert.That(model.HealingFlasks.CurrentCharges, Is.EqualTo(charges));
            Assert.That(model.AttackSequence, Is.EqualTo(attacks));
            Assert.That(model.HealSequence, Is.EqualTo(heals));
            Assert.That(model.Submit(CombatCommand.Heal), Is.EqualTo(ready), "Existing domain, not HUD, decides acceptance.");
        }

        [Test]
        public void RangedCooldownDoesNotInventATreatmentCooldown()
        {
            var tuning = CombatTuning.CreateDefault();
            var model = new CombatStateMachine(tuning);
            model.Health.ApplyDamage(20f, 0f);
            Assert.That(model.Submit(CombatCommand.RangedAttack), Is.True);
            model.Tick(tuning.RangedDuration + .01f);
            Assert.That(model.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(model.RangedCooldownRemaining, Is.GreaterThan(0f));
            Assert.That(HudResourcePresentation.Resolve(model.Health.Normalized, model.Stamina.Normalized,
                model.State, model.HealingFlasks.CurrentCharges).IsFlaskReady, Is.True);
            Assert.That(model.Submit(CombatCommand.Heal), Is.True);
        }

        [Test]
        public void ExhaustedStaminaDoesNotInventATreatmentCost()
        {
            var model = new CombatStateMachine(CombatTuning.CreateDefault());
            model.Health.ApplyDamage(20f, 0f);
            Assert.That(model.Stamina.TrySpend(model.Stamina.Current), Is.True);
            Assert.That(model.Stamina.Current, Is.Zero);
            var display = HudResourcePresentation.Resolve(model.Health.Normalized, model.Stamina.Normalized,
                model.State, model.HealingFlasks.CurrentCharges);
            Assert.That(display.StaminaLabel, Is.EqualTo("耐力偏低"));
            Assert.That(display.FlaskLabel, Is.EqualTo("药剂就绪"));
            Assert.That(display.IsFlaskReady, Is.True);
            Assert.That(model.Submit(CombatCommand.Heal), Is.True);
        }

        [Test]
        public void NetworkGuideIsInstanceLocalAndConfigureExplicitlyResetsItWithoutSpawningActors()
        {
            var temporary = new GameObject("HudInformationNetworkGuideFixture");
            try
            {
                var hud = temporary.AddComponent<NetworkRouteHud>();
                Assert.That(hud.IsGuideVisible, Is.False);
                Assert.That(hud.ShouldShowCombatHud, Is.False, "Unspawned/absent Owner path; not ResultPublished coverage.");
                var field = typeof(NetworkRouteHud).GetField("_showGuide", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Assert.That(field.IsStatic, Is.False);
                // Only this temporary HUD's prior-open UI state; actual keyboard F1 is covered in PlayMode.
                field.SetValue(hud, true);
                Assert.That(hud.IsGuideVisible, Is.True);
                hud.Configure(null, null, null);
                Assert.That(hud.IsGuideVisible, Is.False);
                hud.Configure(null, null, null);
                Assert.That(hud.IsGuideVisible, Is.False);
                Assert.That(hud.ShouldShowCombatHud, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
        }

        [TestCase(1920, 1080)] [TestCase(1728, 1080)] [TestCase(1280, 720)]
        [TestCase(1024, 768)] [TestCase(960, 540)] [TestCase(2560, 1440)]
        public void LogicalBottomPanelsRemainInBoundsAndSeparateSupportedInformationGroups(int width, int height)
        {
            float scale = EmberfallGuiTheme.Scale(width, height), logicalWidth = width / scale, logicalHeight = height / scale;
            var layout = PresentationBottomHudLayout.Resolve(logicalWidth, logicalHeight);
            Assert.That(layout.Flask, Is.EqualTo(new Rect(24, logicalHeight - 116, 120, 64)));
            Assert.That(layout.Knife, Is.EqualTo(new Rect(152, logicalHeight - 116, 120, 64)));
            Assert.That(layout.Sweep, Is.EqualTo(new Rect(280, logicalHeight - 116, 120, 64)));
            Assert.That(layout.Interaction, Is.EqualTo(new Rect((logicalWidth - 440) * .5f, logicalHeight - 100, 440, 48)));
            Assert.That(layout.Feedback, Is.EqualTo(new Rect((logicalWidth - 540) * .5f, logicalHeight - 168, 540, 42)));
            Assert.That(layout.Guide, Is.EqualTo(new Rect(24, logicalHeight - 358, 320, 226)));
            Assert.That(layout.Footer, Is.EqualTo(new Rect(24, logicalHeight - 38, 300, 22)));
            Rect[] abilities = { layout.Flask, layout.Knife, layout.Sweep };
            Rect[] all = { layout.Flask, layout.Knife, layout.Sweep, layout.Interaction, layout.Feedback, layout.Guide, layout.Footer };
            foreach (Rect rect in all)
            {
                Assert.That(rect.width, Is.GreaterThan(0));
                Assert.That(rect.height, Is.GreaterThan(0));
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax * scale, Is.LessThanOrEqualTo(width));
                Assert.That(rect.yMax * scale, Is.LessThanOrEqualTo(height));
            }
            for (int i = 0; i < abilities.Length; i++)
            {
                for (int j = i + 1; j < abilities.Length; j++) Assert.That(abilities[i].Overlaps(abilities[j]), Is.False);
                Assert.That(abilities[i].Overlaps(layout.Interaction), Is.False);
                Assert.That(abilities[i].Overlaps(layout.Feedback), Is.False);
                Assert.That(abilities[i].Overlaps(layout.Guide), Is.False);
            }
            Assert.That(layout.Interaction.Overlaps(layout.Feedback), Is.False);
            foreach (Rect rect in all.Take(all.Length - 1)) Assert.That(layout.Footer.Overlaps(rect), Is.False);
            // Scope: these screen-panel groups, not arbitrary-width guide/feedback overlap, F2, or world-pixel markers.
        }

        [TestCase(1920, 1080)] [TestCase(1728, 1080)] [TestCase(1280, 720)]
        [TestCase(1024, 768)] [TestCase(960, 540)] [TestCase(2560, 1440)]
        public void NetworkTopPanelsRemainSeparatedInTheActualLogicalScalingContract(int width, int height)
        {
            float scale = EmberfallGuiTheme.Scale(width, height);
            var layout = NetworkHudLayout.Resolve(width / scale);
            Assert.That(layout.Player.Overlaps(layout.Quest), Is.False);
            Assert.That(layout.Player.Overlaps(layout.Boss), Is.False);
            Assert.That(layout.Quest.Overlaps(layout.Boss), Is.False);
            Assert.That(layout.Quest.width, Is.GreaterThanOrEqualTo(320));
            Assert.That(layout.Quest.height, Is.GreaterThanOrEqualTo(150));
            foreach (Rect rect in new[] { layout.Player, layout.Quest, layout.Boss })
            {
                Assert.That(rect.width, Is.GreaterThan(0));
                Assert.That(rect.height, Is.GreaterThan(0));
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax * scale, Is.LessThanOrEqualTo(width));
                Assert.That(rect.yMax * scale, Is.LessThan(height));
            }
            Assert.That(14 * scale, Is.GreaterThanOrEqualTo(9.4f));
            // No Camera/GUI.matrix calls: actual OnGUI restoration and pixel projection need runtime evidence.
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string name = string.Empty;
            public string[] references = Array.Empty<string>();
            public string[] includePlatforms = Array.Empty<string>();
            public bool noEngineReferences = false;
        }

        [Test]
        public void UiBatchKeepsExactlyEightRuntimeBoundariesAndExistingOneWayReferences()
        {
            var expected = new Dictionary<string, string[]>
            {
                { "Emberfall.Core", Array.Empty<string>() },
                { "Emberfall.Gameplay", new[] { "Emberfall.Core", "Unity.InputSystem" } },
                { "Emberfall.AI", new[] { "Emberfall.Core", "Emberfall.Gameplay" } },
                { "Emberfall.Quests", new[] { "Emberfall.Core" } },
                { "Emberfall.Infrastructure", new[] { "Emberfall.Core", "Emberfall.Quests" } },
                { "Emberfall.Application", new[] { "Emberfall.Core", "Emberfall.Gameplay", "Emberfall.AI", "Emberfall.Quests", "Emberfall.Infrastructure", "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime" } },
                { "Emberfall.Networking", new[] { "Emberfall.Core", "Emberfall.Gameplay", "Emberfall.AI", "Emberfall.Quests", "Unity.Netcode.Runtime", "Unity.Networking.Transport" } },
                { "Emberfall.UI", new[] { "Emberfall.Core", "Emberfall.Gameplay", "Emberfall.Infrastructure", "Emberfall.Application", "Emberfall.AI", "Emberfall.Networking", "Unity.Netcode.Runtime", "Unity.InputSystem" } }
            };
            var definitions = Directory.GetFiles("Assets/_Game/Scripts", "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(path)))
                .Where(value => value.includePlatforms == null || value.includePlatforms.Length == 0).ToArray();
            CollectionAssert.AreEquivalent(expected.Keys, definitions.Select(value => value.name));
            foreach (var definition in definitions)
            {
                CollectionAssert.AreEquivalent(expected[definition.name], definition.references ?? Array.Empty<string>(), definition.name);
                if (definition.name != "Emberfall.UI") Assert.That(definition.references, Does.Not.Contain("Emberfall.UI"));
                if (definition.name == "Emberfall.Core") Assert.That(definition.noEngineReferences, Is.True);
            }
            // Project-owned runtime boundaries only; not a package or ALL Managed assembly count.
        }
    }
}
