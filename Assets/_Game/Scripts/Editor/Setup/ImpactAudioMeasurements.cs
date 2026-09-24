using System;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    // Offline asset analysis, never executed in a Player or from the combat tick.
    public static class ImpactAudioMeasurements
    {
        [Serializable]
        public struct Metrics
        {
            public int channels, frequency, frames, onsetFrame;
            public double onsetMs, durationMs, peakDbfs, rmsDbfs;
        }

        public static float[] Read(AudioClip clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
                throw new InvalidOperationException("Cannot load PCM: " + clip.name);
            var samples = new float[checked(clip.samples * clip.channels)];
            if (!clip.GetData(samples, 0)) throw new InvalidOperationException("GetData failed: " + clip.name);
            return samples;
        }

        public static Metrics Measure(float[] samples, int channels, int frequency)
        {
            if (samples == null || samples.Length == 0 || channels <= 0 || frequency <= 0 || samples.Length % channels != 0)
                throw new ArgumentException("Non-empty interleaved PCM, valid channels and frequency required.");
            double sum = 0, peak = 0;
            int onset = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                double value = samples[i];
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Non-finite PCM.");
                double absolute = Math.Abs(value);
                if (absolute > .01d && onset < 0) onset = i / channels; // Strictly above -40 dBFS.
                peak = Math.Max(peak, absolute);
                sum += value * value;
            }
            return new Metrics { channels = channels, frequency = frequency, frames = samples.Length / channels,
                onsetFrame = onset, onsetMs = onset < 0 ? -1 : onset * 1000d / frequency,
                durationMs = samples.Length / channels * 1000d / frequency,
                peakDbfs = Db(peak), rmsDbfs = Db(Math.Sqrt(sum / samples.Length)) };
        }

        public static int TrimFrames(Metrics metrics) => metrics.onsetMs > 10d
            ? Math.Max(0, metrics.onsetFrame - (int)Math.Ceiling(metrics.frequency * .001d)) : 0;

        public static double Db(double amplitude) => amplitude > 0 ? 20d * Math.Log10(amplitude) : double.NegativeInfinity;
        public static float Attenuation(double measuredDb, double targetDb) =>
            (float)Math.Pow(10d, Math.Min(0d, targetDb - measuredDb) / 20d);
    }
}
