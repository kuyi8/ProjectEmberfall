#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit Development-only engine timing. Not external present/display timing.</summary>
    public sealed class PresentedPerformanceProbe : MonoBehaviour
    {
        private float _next;
        private string _trigger, _output;
        private double _started = -1, _nextTriggerCheck;
        private int _seconds = 60, _unfocused, _expectedVSync;
        private bool _complete, _invalidSettings;
        private readonly List<double> _intervals = new List<double>(30000);
        private readonly List<double> _cpu = new List<double>(30000);
        private readonly List<double> _gpu = new List<double>(30000);
        private readonly List<Sample> _samples = new List<Sample>(30000);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private ulong _lastTiming;
        [Serializable] private sealed class Sample
        {
            public int frame;
            public double elapsedSeconds, updateIntervalMs, cpuMs, gpuMs;
            public bool freshEngineTiming, focused;
        }
        [Serializable] private sealed class Result
        {
            public string measurement = "Unity Update unscaledDeltaTime; optional FrameTimingManager CPU/GPU. NOT external present/display intervals.";
            public string scope = "Scripted scene residency; no historical comparison and no worst-case combat proof.";
            public string version, scene, device, unavailableTimingMeaning = "count=0 means no valid timing, NOT zero cost";
            public bool accepted, externalPresentationMeasured = false, frameTimingEnabled;
            public int quality, vSync, width, height, unfocusedFrames;
            public double durationSeconds, startRealtimeSeconds;
            public float renderScale;
            public FrameIntervalStatistics.Summary updateIntervals, cpu, gpu;
            public Sample[] samples;
        }
        private static string Argument(string key) => Environment.GetCommandLineArgs()
            .FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal))?.Substring(key.Length + 1);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-presented-performance")) return;
            if (UnityEngine.Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Presented timing requires a normal graphics window.");
            QualitySettings.SetQualityLevel(5, true);
            QualitySettings.vSyncCount = Argument("-emberfall-perf-vsync") == "0" ? 0 : 1;
            UnityEngine.Application.targetFrameRate = -1;
            var root = new GameObject("Development Presented Performance Metadata");
            DontDestroyOnLoad(root);
            var probe = root.AddComponent<PresentedPerformanceProbe>();
            probe._expectedVSync = QualitySettings.vSyncCount;
            probe._trigger = Argument("-emberfall-frame-start-file");
            probe._output = Argument("-emberfall-frame-output");
            if (int.TryParse(Argument("-emberfall-frame-seconds"), out int seconds)) probe._seconds = Mathf.Clamp(seconds, 3, 60);
        }

        private void Update()
        {
            SampleFrame();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            // Diagnostic builds preserve SSAO variants, but the selected production default is off.
            foreach (var renderer in Resources.FindObjectsOfTypeAll<UniversalRendererData>())
                foreach (var feature in renderer.rendererFeatures)
                    if (feature != null && feature.name == "Emberfall SSAO") feature.SetActive(false);
            var camera = Camera.main;
            Debug.Log($"[PRESENTED_PERF_SETTINGS] quality={QualitySettings.GetQualityLevel()} vsync={QualitySettings.vSyncCount} target={UnityEngine.Application.targetFrameRate} size={Screen.width}x{Screen.height} scale={pipeline?.renderScale} scene={SceneManager.GetActiveScene().name} camera={camera?.transform.position} ssao=false simulation=unchanged");
        }

        private void SampleFrame()
        {
            if (_complete || string.IsNullOrEmpty(_trigger) || string.IsNullOrEmpty(_output)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (_started < 0)
            {
                if (now < _nextTriggerCheck) return;
                _nextTriggerCheck = now + .25;
                if (!File.Exists(_trigger)) return;
                _started = now;
                Debug.Log("[APPLICATION_FRAME_SAMPLE_BEGIN] measurement=update-interval not-external-present");
                return; // Discard the frame interval preceding the trigger.
            }
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            _invalidSettings |= Screen.width != 1920 || Screen.height != 1080 ||
                QualitySettings.GetQualityLevel() != 5 || QualitySettings.vSyncCount != _expectedVSync ||
                pipeline == null || Mathf.Abs(pipeline.renderScale - 1f) > .001f;
            if (now - _started >= _seconds)
            {
                _complete = true;
                var result = new Result {
                    scope = Environment.GetCommandLineArgs().Contains("-emberfall-performance-combat")
                        ? "Scripted two-player Sanctum light/block combat; consult activity audit. No network Sweep/Execution, no worst-case proof."
                        : "Scripted scene residency; no historical comparison and no worst-case combat proof.",
                    accepted = !_invalidSettings && _intervals.Count >= 100,
                    version = UnityEngine.Application.version, scene = SceneManager.GetActiveScene().name,
                    device = SystemInfo.graphicsDeviceName, frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
                    quality = QualitySettings.GetQualityLevel(), vSync = QualitySettings.vSyncCount,
                    width = Screen.width, height = Screen.height, renderScale = pipeline == null ? 0 : pipeline.renderScale,
                    durationSeconds = now - _started, startRealtimeSeconds = _started, unfocusedFrames = _unfocused,
                    updateIntervals = FrameIntervalStatistics.Calculate(_intervals),
                    cpu = FrameIntervalStatistics.Calculate(_cpu), gpu = FrameIntervalStatistics.Calculate(_gpu), samples = _samples.ToArray()
                };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_output)));
                File.WriteAllText(_output, JsonUtility.ToJson(result, true));
                Debug.Log($"[APPLICATION_FRAME_SAMPLE_COMPLETE] accepted={result.accepted} samples={_intervals.Count} medianMs={result.updateIntervals.medianMs.ToString("F3", CultureInfo.InvariantCulture)} p95Ms={result.updateIntervals.p95Ms.ToString("F3", CultureInfo.InvariantCulture)} output={_output}");
                return;
            }
            var sample = new Sample { frame = Time.frameCount, elapsedSeconds = now - _started,
                updateIntervalMs = Time.unscaledDeltaTime * 1000.0, focused = UnityEngine.Application.isFocused };
            if (!sample.focused) _unfocused++;
            _intervals.Add(sample.updateIntervalMs);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].frameStartTimestamp > _lastTiming)
            {
                _lastTiming = _timing[0].frameStartTimestamp;
                sample.freshEngineTiming = true;
                sample.cpuMs = _timing[0].cpuFrameTime;
                sample.gpuMs = _timing[0].gpuFrameTime;
                _cpu.Add(sample.cpuMs);
                _gpu.Add(sample.gpuMs);
            }
            _samples.Add(sample);
        }
    }
}
#endif
