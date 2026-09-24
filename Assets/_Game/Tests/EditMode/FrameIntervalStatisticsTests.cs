using Emberfall.Application.Flow;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class FrameIntervalStatisticsTests
    {
        [Test]
        public void OddAndEvenMedianUseSortedSamplesWithoutMutatingInput()
        {
            var input = new double[] { 9, 1, 4, 2 };
            Assert.That(FrameIntervalStatistics.Calculate(input).medianMs, Is.EqualTo(3));
            Assert.That(input, Is.EqualTo(new double[] { 9, 1, 4, 2 }));
            Assert.That(FrameIntervalStatistics.Calculate(new double[] { 9, 1, 4 }).medianMs, Is.EqualTo(4));
        }

        [Test]
        public void PercentileUsesNearestRankAndHistogramAccountsForEverySample()
        {
            var values = new double[100];
            for (int i = 0; i < values.Length; i++) values[i] = i + 1;
            var result = FrameIntervalStatistics.Calculate(values);
            Assert.That(result.p95Ms, Is.EqualTo(95));
            Assert.That(result.maxMs, Is.EqualTo(100));
            Assert.That(result.histogram, Is.EqualTo(new[] { 8, 8, 4, 13, 17, 50 }));
        }

        [Test]
        public void MissingOrInvalidGpuSamplesDoNotBecomeZeroCostEvidence()
        {
            var result = FrameIntervalStatistics.Calculate(new[] { 0, -1, double.NaN, double.PositiveInfinity });
            Assert.That(result.count, Is.Zero);
            Assert.That(result.histogram, Is.EqualTo(new int[6]));
        }
    }
}
