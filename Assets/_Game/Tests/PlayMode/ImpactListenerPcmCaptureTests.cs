#if UNITY_EDITOR
using System;
using NUnit.Framework;

namespace Emberfall.Tests.PlayMode
{
    public sealed class ImpactListenerPcmCaptureTests
    {
        [Test]
        public void Silence_HasNoPositiveWindowsOrEnergy()
        {
            var result = ImpactListenerPcmCapture.AnalyzePcm(new float[4096], 4096);
            Assert.That(result.blocks, Is.EqualTo(5));
            Assert.That(result.nonzeroBlocks, Is.Zero);
            Assert.That(result.squareSum, Is.Zero);
            Assert.That(result.peakDbfs, Is.EqualTo(-180));
        }

        [Test]
        public void TransientFollowedBySilence_DoesNotInventPositiveTailWindows()
        {
            var pcm = new float[8192];
            pcm[0] = .7f;
            pcm[1] = .2f;
            var result = ImpactListenerPcmCapture.AnalyzePcm(pcm, pcm.Length);
            Assert.That(result.blocks, Is.EqualTo(13));
            Assert.That(result.nonzeroBlocks, Is.EqualTo(1));
            Assert.That(result.squareSum, Is.EqualTo((double)pcm[0] * pcm[0] + (double)pcm[1] * pcm[1]));
            Assert.That(result.sampleCount, Is.EqualTo(pcm.Length));
        }

        [Test]
        public void PeakAndSaturation_InspectSamplesBeyondLastCompleteRmsWindow()
        {
            var pcm = new float[2050];
            pcm[2048] = 1f;
            pcm[2049] = -1f;
            var result = ImpactListenerPcmCapture.AnalyzePcm(pcm, pcm.Length);
            Assert.That(result.blocks, Is.EqualTo(1));
            Assert.That(result.nonzeroBlocks, Is.Zero);
            Assert.That(result.saturatedSamples, Is.EqualTo(2));
            Assert.That(result.peakDbfs, Is.Zero);
            Assert.That(result.squareSum, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() =>
                ImpactListenerPcmCapture.AnalyzePcm(new[] { float.NaN, 0f }, 2));
        }
    }
}
#endif
