#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit diagnostic settings/metadata only. Never drives simulation, camera or damage.</summary>
    public sealed class PresentedPerformanceProbe : MonoBehaviour
    {
        private float _next;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-presented-performance")) return;
            if (UnityEngine.Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Presented timing requires a normal graphics window.");
            QualitySettings.SetQualityLevel(5, true);
            QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = -1;
            var root = new GameObject("Development Presented Performance Metadata");
            DontDestroyOnLoad(root);
            root.AddComponent<PresentedPerformanceProbe>();
        }

        private void Update()
        {
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
    }
}
#endif
