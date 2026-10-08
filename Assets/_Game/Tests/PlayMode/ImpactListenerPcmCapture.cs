#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>
    /// Read-only filter on the fixture's actual AudioListener, after spatial mixing.
    /// The audio thread only copies bounded interleaved PCM; no Unity calls, logging,
    /// allocation or sample modification occur there. It does not synthesize a signal.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AudioListener))]
    public sealed class ImpactListenerPcmCapture : MonoBehaviour
    {
        readonly object _gate = new object();
        float[] _samples;
        int _written, _callbacks, _invalidChannels, _sampleRate;
        bool _armed;

        public int CapturedFrames { get { lock (_gate) return _written / 2; } }
        public int TargetFrames { get { lock (_gate) return (_samples?.Length ?? 0) / 2; } }

        // Called on the main thread, before any presentation endpoint dispatch.
        public void Arm(int sampleRate, double seconds)
        {
            if (sampleRate <= 0 || seconds <= 0 || seconds > 10 || double.IsNaN(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            var samples = new float[checked((int)Math.Ceiling(sampleRate * seconds) * 2)];
            lock (_gate)
            {
                _samples = samples; _written = _callbacks = _invalidChannels = 0;
                _sampleRate = sampleRate; _armed = true;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_gate)
            {
                if (!_armed || _written == _samples.Length) return;
                _callbacks++;
                if (channels != 2 || data.Length % 2 != 0) { _invalidChannels++; return; }
                int count = Math.Min(data.Length, _samples.Length - _written);
                Array.Copy(data, 0, _samples, _written, count);
                _written += count;
            }
        }

        public Snapshot Stop()
        {
            lock (_gate)
            {
                _armed = false;
                return new Snapshot(_samples, _written, _sampleRate, _callbacks, _invalidChannels);
            }
        }

        public sealed class Snapshot
        {
            readonly float[] _pcm;
            public readonly int samples, sampleRate, callbacks, invalidChannelCallbacks;
            public int frames => samples / 2;
            public Snapshot(float[] pcm, int count, int rate, int callbackCount, int invalidChannels)
            { _pcm = pcm; samples = count; sampleRate = rate; callbacks = callbackCount; invalidChannelCallbacks = invalidChannels; }

            public Statistics Analyze() => AnalyzePcm(_pcm, samples);
        }

        public sealed class Statistics
        {
            public const int WindowFrames = 1024, HopFrames = 256;
            public int blocks, nonzeroBlocks, saturatedSamples;
            public long sampleCount;
            public double maximumBlockRmsDbfs = -180, peakDbfs = -180, windowRmsDbfs = -180, squareSum;
        }

        // Main-thread analysis: one continuous time window, not duplicate/overlapping
        // frame polls. Sliding 1024-frame stereo RMS uses a fixed 256-frame hop for
        // both old/new authored A/B and matched solo/six runs. Peak/saturation inspect
        // EVERY captured sample. Whole-window energy is not the old polled energy.
        public static Statistics AnalyzePcm(float[] pcm, int count)
        {
            if (pcm == null || count < 0 || count > pcm.Length || count % 2 != 0)
                throw new ArgumentException("Valid interleaved stereo PCM is required.");
            var result = new Statistics { sampleCount = count };
            double peak = 0;
            for (int i = 0; i < count; i++)
            {
                double value = pcm[i];
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Non-finite listener PCM.");
                result.squareSum += value * value; peak = Math.Max(peak, Math.Abs(value));
                if (Math.Abs(value) >= .999f) result.saturatedSamples++;
            }
            if (peak > 1e-8) result.peakDbfs = 20 * Math.Log10(peak);
            if (result.squareSum > 0 && count > 0) result.windowRmsDbfs = 20 * Math.Log10(Math.Sqrt(result.squareSum / count));
            int windowSamples = Statistics.WindowFrames * 2, hopSamples = Statistics.HopFrames * 2;
            for (int start = 0; start + windowSamples <= count; start += hopSamples)
            {
                // Direct summation keeps a genuinely silent tail exactly zero;
                // subtracting past energy can leave a false-positive residual.
                double windowSquare = 0;
                for (int i = start; i < start + windowSamples; i++)
                    windowSquare += (double)pcm[i] * pcm[i];
                double rms = Math.Sqrt(windowSquare / windowSamples); result.blocks++;
                if (rms > 1e-8)
                { result.nonzeroBlocks++; result.maximumBlockRmsDbfs = Math.Max(result.maximumBlockRmsDbfs, 20 * Math.Log10(rms)); }
            }
            return result;
        }
    }
}
#endif
