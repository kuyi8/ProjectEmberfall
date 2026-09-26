using System;
using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class AttackTimingAuditTests
    {
        private static AttackTimingAudit.Mapping Heavy() => AttackTimingAudit.ReadMappings().Single(m => m.id == "player.heavy");
        private static AttackTimingAnnotations.Contact Fixture(AttackTimingAudit.Mapping map, float domainTime)
        {
            // Synthetic unit-test evidence, NEVER saved to the production annotations.
            return new AttackTimingAnnotations.Contact { actionId = map.id, clip = map.clip,
                contactSeconds = map.clipStart + domainTime * (map.clipEnd - map.clipStart) / map.playbackDuration,
                confirmed = true, observedBy = "Unit test fixture", observation = "Synthetic conversion test only",
                evidencePath = AttackTimingAudit.TuningPath, observedClipHash = AttackTimingAudit.ClipHash(map.clip),
                synchronizationObserved = true, naturalContactTime = domainTime, firstDamageTime = domainTime,
                maxSampleGap = .01f, observedTimingHash = AttackTimingAudit.TimingHash(map) };
        }

        [Test] public void CatalogCoversEveryRequiredActionWithoutDuplicates()
        {
            var maps = AttackTimingAudit.ReadMappings();
            Assert.That(AttackTimingAudit.CoverageErrors(maps.Select(m => m.id)), Is.Empty);
            Assert.That(maps.All(m => m.clip != null), Is.True);
            Assert.That(maps.Count, Is.EqualTo(23));
            Assert.That(AttackTimingAudit.RequiredIds.Length, Is.EqualTo(23));
        }

        [Test] public void PointKindsAreDerivedFromDomainReleaseFields()
        {
            var maps = AttackTimingAudit.ReadMappings();
            Assert.That(maps.Where(m => m.pointEvent).Select(m => m.id), Is.EquivalentTo(new[] {
                "player.knife", "player.execution", "priest.projectile", "priest.rune", "scorched.burst" }));
            foreach (var map in maps)
                Assert.That(map.pointEvent, Is.EqualTo(map.windowStart == map.windowEnd), map.id);
        }

        [TestCaseSource(nameof(RequiredIds))]
        public void MissingAnyChecklistActionFails(string id)
        {
            var remaining = AttackTimingAudit.ReadMappings().Select(m => m.id).Where(m => m != id);
            Assert.That(AttackTimingAudit.CoverageErrors(remaining), Does.Contain("missing:" + id));
        }
        private static string[] RequiredIds() => AttackTimingAudit.RequiredIds;

        [Test] public void DuplicateActionFails()
        {
            Assert.That(AttackTimingAudit.CoverageErrors(AttackTimingAudit.RequiredIds.Concat(new[] { "player.light1" })),
                Does.Contain("duplicate:player.light1"));
        }
        [Test] public void UnobservedContactIsNotAValidAlignment()
        {
            Assert.That(AttackTimingAudit.Evaluate(Heavy(), null).failures, Does.Contain("contact-unconfirmed"));
        }
        [Test] public void ValidObservedContactConvertsAndPasses()
        {
            var map = Heavy();
            var row = AttackTimingAudit.Evaluate(map, Fixture(map, .4f));
            Assert.That(row.accepted, Is.True);
            Assert.That(row.actualContactTime, Is.EqualTo(.4f).Within(.00001));
        }
        [Test] public void ObservationWithStaleClipHashFails()
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.observedClipHash = "old";
            Assert.That(AttackTimingAudit.Evaluate(map, contact).failures, Does.Contain("invalid-or-stale-observation"));
        }
        [Test] public void ContactCannotBeReusedUnderAnotherActionId()
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.actionId = "player.execution";
            Assert.That(AttackTimingAudit.Evaluate(map, contact).failures, Does.Contain("invalid-or-stale-observation"));
        }
        [Test] public void ClampRetainsRequiredAndActualSpeedsAndFails()
        {
            var map = Heavy(); map.maxSpeed = .5f;
            var row = AttackTimingAudit.Evaluate(map, Fixture(map, .4f));
            Assert.That(row.failures, Does.Contain("speed-clamped"));
            Assert.That(row.requiredSpeed, Is.GreaterThan(row.clampedSpeed));
            Assert.That(row.actualContactTime, Is.GreaterThan(row.unclampedContactTime));
            Assert.That(row.finishesInTime, Is.False);
        }
        [Test] public void LowerClampAlsoFailsEvenThoughItFinishesEarly()
        {
            var map = Heavy(); map.minSpeed = 2.5f;
            Assert.That(AttackTimingAudit.Evaluate(map, Fixture(map, .4f)).failures, Does.Contain("speed-clamped"));
        }
        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidDurationFails(float duration)
        {
            var map = Heavy(); map.playbackDuration = duration;
            Assert.That(AttackTimingAudit.Evaluate(map, null).failures, Does.Contain("invalid-playback-mapping"));
        }
        [Test] public void WindowCannotCrossStateOrCollapse()
        {
            var map = Heavy(); map.windowEnd = map.duration + .01f;
            Assert.That(AttackTimingAudit.Evaluate(map, null).failures, Does.Contain("invalid-domain-window"));
            map.windowEnd = map.windowStart;
            Assert.That(AttackTimingAudit.Evaluate(map, null).failures, Does.Contain("invalid-domain-window"));
        }
        [Test] public void GenuinePointEventIsNotInventedAsPositiveWidthWindow()
        {
            var map = Heavy(); map.pointEvent = true; map.windowStart = map.windowEnd = .4f;
            Assert.That(AttackTimingAudit.Evaluate(map, Fixture(map, .4f)).accepted, Is.True);
            map.windowEnd += .01f;
            Assert.That(AttackTimingAudit.Evaluate(map, null).failures, Does.Contain("invalid-domain-window"));
        }
        [TestCase(.03f, true)] [TestCase(.035f, false)]
        public void ContactToleranceDoesNotExceedOneThirtyFpsFrame(float late, bool accepted)
        {
            var map = Heavy();
            Assert.That(AttackTimingAudit.Evaluate(map, Fixture(map, map.windowEnd + late)).accepted, Is.EqualTo(accepted));
        }
        [Test] public void NonZeroClipOffsetIsSubtractedBeforeConversion()
        {
            var map = Heavy(); map.clipStart = .2f;
            Assert.That(AttackTimingAudit.Evaluate(map, Fixture(map, .4f)).actualContactTime, Is.EqualTo(.4f).Within(.00001));
        }

        [TestCase(-.034f, false)] [TestCase(-.033f, true)] [TestCase(.033f, true)] [TestCase(.034f, false)]
        public void NaturalDamageMustBeWithinThirtyThreeMilliseconds(float delta, bool accepted)
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.firstDamageTime += delta;
            var row = AttackTimingAudit.Evaluate(map, contact);
            Assert.That(row.windowAccepted, Is.True);
            Assert.That(row.synchronizationAccepted, Is.EqualTo(accepted));
            Assert.That(row.accepted, Is.EqualTo(accepted));
            Assert.That(row.synchronizationDeltaMs, Is.EqualTo(delta * 1000).Within(.001f));
        }
        [Test] public void StaticWindowPassCannotSubstituteForNaturalObservation()
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.synchronizationObserved = false;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).failures, Does.Contain("synchronization-unobserved"));
        }
        [Test] public void TimingChangeInvalidatesNaturalEvidence()
        {
            var map = Heavy(); var contact = Fixture(map, .4f); map.windowStart += .01f;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).failures, Does.Contain("invalid-or-stale-synchronization"));
        }
        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidNaturalTimesFail(float invalid)
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.naturalContactTime = invalid;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).accepted, Is.False);
            contact = Fixture(map, .4f); contact.firstDamageTime = invalid;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).accepted, Is.False);
        }
        [TestCase(-1f)] [TestCase(0f)] [TestCase(.034f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void MissingOrSparseNaturalSamplingFails(float gap)
        {
            var map = Heavy(); var contact = Fixture(map, .4f); contact.maxSampleGap = gap;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).accepted, Is.False);
        }
        [Test] public void SynchronizedButOutsideWindowStillFails()
        {
            var map = Heavy(); var contact = Fixture(map, .4f);
            contact.naturalContactTime = contact.firstDamageTime = .7f;
            Assert.That(AttackTimingAudit.Evaluate(map, contact).failures, Does.Contain("natural-contact-or-damage-outside-window"));
        }
        [Test] public void ExecutionRequiresSynchronizationButProjectileReleaseDoesNot()
        {
            var maps = AttackTimingAudit.ReadMappings();
            foreach (string id in new[] { "player.execution", "priest.projectile" })
            {
                var map = maps.Single(m => m.id == id); var contact = Fixture(map, map.windowStart);
                contact.synchronizationObserved = false;
                var row = AttackTimingAudit.Evaluate(map, contact);
                Assert.That(row.synchronizationRequired, Is.EqualTo(id == "player.execution"));
                Assert.That(row.accepted, Is.EqualTo(id != "player.execution"));
            }
        }
    }
}
