#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;

namespace Emberfall.Application.Flow
{
    /// <summary>Diagnostic only. No frame-rate or gameplay authority.</summary>
    public static class FrameIntervalStatistics
    {
        [Serializable]
        public sealed class Summary
        {
            public int count;
            public double medianMs, p95Ms, maxMs;
            public string[] bins = { "<=8.33", "(8.33,16.667]", "(16.667,20]", "(20,33.333]", "(33.333,50]", ">50" };
            public int[] histogram = new int[6];
        }

        public static Summary Calculate(IEnumerable<double> samples)
        {
            var values = new List<double>();
            foreach (double value in samples)
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0) values.Add(value);
            var result = new Summary { count = values.Count };
            if (values.Count == 0) return result; // count=0 explicitly means unsupported/no samples, never 0ms performance.
            values.Sort();
            int n = values.Count;
            result.medianMs = n % 2 == 0 ? (values[n / 2 - 1] + values[n / 2]) / 2 : values[n / 2];
            result.p95Ms = values[(int)Math.Ceiling(n * .95) - 1];
            result.maxMs = values[n - 1];
            foreach (double v in values)
                result.histogram[v <= 8.33 ? 0 : v <= 16.667 ? 1 : v <= 20 ? 2 : v <= 33.333 ? 3 : v <= 50 ? 4 : 5]++;
            return result;
        }
    }
}
#endif
