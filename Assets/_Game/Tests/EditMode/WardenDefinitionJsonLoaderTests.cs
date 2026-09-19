using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenDefinitionJsonLoaderTests
    {
        [Test]
        public void ProjectCatalog_LoadsDedicatedTwoPhaseWarden()
        {
            TextAsset catalog = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json");
            WardenDefinition warden = WardenDefinitionJsonLoader.Load(catalog.text)
                .GetRequired(new ContentId("boss:ember-warden"));

            Assert.That(warden.MaximumHealth, Is.EqualTo(620f));
            Assert.That(warden.PhaseTwoThreshold, Is.EqualTo(0.55f));
            Assert.That(warden.SwordCombo.HasSecondHit, Is.True);
            Assert.That(warden.ShieldBash.Kind, Is.EqualTo(WardenAttackKind.ShieldBash));
            Assert.That(warden.Charge.Kind, Is.EqualTo(WardenAttackKind.Charge));
            Assert.That(warden.PhaseTransitionDuration, Is.EqualTo(4.2f));
            Assert.That(warden.PhaseTwoPostureCapacity, Is.EqualTo(105f));
            Assert.That(warden.RuneCleave.Kind, Is.EqualTo(WardenAttackKind.RuneCleave));
            Assert.That(warden.RuneCleaveAngle, Is.EqualTo(110f));
            Assert.That(warden.DelayedBlast.Kind, Is.EqualTo(WardenAttackKind.DelayedBlast));
            Assert.That(warden.DelayedBlastFuse, Is.EqualTo(1.15f));
            Assert.That(warden.DelayedBlastRadius, Is.EqualTo(2.55f));
        }
    }
}
