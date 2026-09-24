#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit Development-only render evidence; entirely absent from Release.</summary>
    public sealed class VisualFoundationReviewProbe : MonoBehaviour
    {
        private const string Output = "Builds/ArtReview/0.9.0";
        private readonly List<Sample> _samples = new List<Sample>();
        private Camera _camera;
        private ScriptableRendererFeature _ssao;
        private RenderTexture _target;
        private Texture2D _completionPixel;
        public static bool IsActive { get; private set; }
        public static string SavePath { get; private set; }

        [Serializable]
        private sealed class Sample
        {
            public string mode;
            public int frames;
            public double medianMs;
            public double p95Ms;
            public double[] frameMs;
        }

        [Serializable]
        private sealed class Report
        {
            public string version = "0.9.0";
            public string build = "Development";
            public string measurement = "Hidden-window offscreen camera render + synchronous 1px completion readback wall time; NOT GPU timing or presented FPS; frozen scene; no IMGUI";
            public string historicalBaseline = "not measured: old 0.8.10b player has no identical probe";
            public string authoredDefault = "no-ssao";
            public string device;
            public string api;
            public int width, height, quality, vSync, targetFrameRate;
            public float renderScale;
            public Vector3 cameraPosition, cameraEuler;
            public Sample[] samples;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-visual-review") &&
                !Environment.GetCommandLineArgs().Contains("-emberfall-style-lab") &&
                !Environment.GetCommandLineArgs().Contains("-emberfall-production-review")) return;
            if (UnityEngine.Application.isBatchMode)
                throw new InvalidOperationException("Use a graphics Development player without -batchmode: batch screenshots can be black.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Visual review needs a real graphics device, not -nographics.");
            var root = new GameObject("Development Visual Review Probe");
            IsActive = true;
            // A unique verification save never touches the normal user's playthrough or another test.
            SavePath = Path.GetFullPath(Path.Combine(Output, "Saves", Guid.NewGuid().ToString("N") + ".json"));
            DontDestroyOnLoad(root);
            root.AddComponent<VisualFoundationReviewProbe>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Output);
            QualitySettings.SetQualityLevel(5, true);
            QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = -1;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            double menuDeadline = Time.realtimeSinceStartupAsDouble + 15;
            while (SceneManager.GetActiveScene().name != "01_MainMenu" && Time.realtimeSinceStartupAsDouble < menuDeadline)
                yield return null;
            if (SceneManager.GetActiveScene().name != "01_MainMenu") throw new InvalidOperationException("Bootstrap did not reach menu.");
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.renderScale != 1f || Screen.width != 1920 || Screen.height != 1080)
                throw new InvalidOperationException("Visual review resolution/render scale contract not met.");
            _ssao = Resources.FindObjectsOfTypeAll<UniversalRendererData>()
                .SelectMany(x => x.rendererFeatures).Single(x => x != null && x.name == "Emberfall SSAO");
            var player = FindObjectOfType<PlayerCombatActor>();
            var enemy = FindObjectsOfType<MeleeEnemyActor>().First(x => x.name.Contains("Forest"));
            foreach (var actor in FindObjectsOfType<MeleeEnemyActor>()) actor.enabled = false;
            foreach (var actor in FindObjectsOfType<RangedEnemyActor>()) actor.enabled = false;
            foreach (var actor in FindObjectsOfType<ShieldEnemyActor>()) actor.enabled = false;
            player.GetComponent<ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            foreach (var rig in FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
            _camera = Camera.main;
            player.transform.position = enemy.transform.position + new Vector3(0f, 1f, -1.6f);
            _camera.transform.position = enemy.transform.position + new Vector3(3f, 3.5f, -6f);
            _camera.transform.LookAt(enemy.AimPoint.position);
            if (Environment.GetCommandLineArgs().Contains("-emberfall-style-lab") ||
                Environment.GetCommandLineArgs().Contains("-emberfall-production-review"))
            {
                Physics.SyncTransforms();
                Time.timeScale = 0f;
                yield return new VisualStyleLabCapture().Run(_camera, player, enemy, _ssao,
                    Environment.GetCommandLineArgs().Contains("-emberfall-production-review"));
                Time.timeScale = 1f;
                UnityEngine.Application.Quit(0);
                yield break;
            }
            _camera.enabled = false;
            _target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            _target.Create();
            _completionPixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            Physics.SyncTransforms();
            Time.timeScale = 0f; // Identical animated pose in each A/B; not a gameplay performance claim.
            yield return CapturePair("forest");
            enemy.ApplyNeutralPostureDamage(999f);
            foreach (var animator in enemy.GetComponentsInChildren<Animator>()) animator.Update(0.18f);
            yield return CapturePair("posture-break");
            if (!player.TryHandleExecutionInput()) throw new InvalidOperationException("Real execution did not start.");
            if (player.Model.State != Emberfall.Gameplay.Combat.Domain.CombatState.Execution || !enemy.IsExecutionClaimed)
                throw new InvalidOperationException("Execution input was handled but the real target was not claimed.");
            player.Model.Tick(0.18f);
            yield return null;
            foreach (var animator in player.GetComponentsInChildren<Animator>()) animator.Update(0.18f);
            yield return CapturePair("execution");

            // Keep the same real scene, pose and camera for every measurement. Reverse order on repeat.
            foreach (string mode in new[] { "off", "full", "no-ssao", "no-ssao", "full", "off" })
                yield return Measure(mode);
            var report = new Report
            {
                device = SystemInfo.graphicsDeviceName,
                api = SystemInfo.graphicsDeviceType.ToString(),
                width = Screen.width, height = Screen.height,
                quality = QualitySettings.GetQualityLevel(), vSync = QualitySettings.vSyncCount,
                targetFrameRate = UnityEngine.Application.targetFrameRate, renderScale = pipeline.renderScale,
                cameraPosition = _camera.transform.position, cameraEuler = _camera.transform.eulerAngles,
                samples = _samples.ToArray()
            };
            File.WriteAllText(Path.Combine(Output, "render-ab-frame-times.json"), JsonUtility.ToJson(report, true));
            SetMode("full");
            Time.timeScale = 1f;
            Debug.Log("[VISUAL_FOUNDATION_REVIEW_COMPLETE] frameTiming=wall-not-gpu historicalBaseline=not-measured");
            UnityEngine.Application.Quit(0);
        }

        private void LateUpdate()
        {
            if (_target == null || _camera == null) return;
            // StandardRequest follows the normal base-camera path, including UpdateVolumeFramework.
            // SingleCameraRequest intentionally bypasses that step and would falsely capture SSAO without grading.
            RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _target });
            // A hidden OS window can skip presentation. Explicit rendering and readback prove GPU completion;
            // the reported wall time includes this readback and must never be labelled ordinary player FPS.
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            _completionPixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = previous;
        }

        private void OnDestroy()
        {
            IsActive = false;
            if (_target != null) { _target.Release(); Destroy(_target); }
            if (_completionPixel != null) Destroy(_completionPixel);
        }

        private void SetMode(string mode)
        {
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = mode != "off";
            _ssao.SetActive(mode == "full");
        }

        private IEnumerator CapturePair(string label)
        {
            foreach (string mode in new[] { "off", "full", "no-ssao" })
            {
                SetMode(mode);
                for (int i = 0; i < 20; i++) yield return null;
                if (VolumeManager.instance.stack.GetComponent<Tonemapping>().mode.value != TonemappingMode.Neutral)
                    throw new InvalidOperationException("The actual render Volume stack has not applied Neutral.");
                string path = Path.GetFullPath(Path.Combine(Output, label + "-" + mode + ".png"));
                var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                try
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = _target;
                        texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                        texture.Apply();
                    }
                    finally { RenderTexture.active = previous; }
                    var pixels = texture.GetPixels32();
                    byte min = 255, max = 0;
                    for (int i = 0; i < pixels.Length; i += 97)
                    {
                        min = (byte)Math.Min(min, pixels[i].g);
                        max = (byte)Math.Max(max, pixels[i].g);
                    }
                    if (texture.width != 1920 || texture.height != 1080 || max - min < 20)
                        throw new IOException("Empty/incorrect-sized rendered evidence: " + path);
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally { Destroy(texture); }
            }
        }

        private IEnumerator Measure(string mode)
        {
            SetMode(mode);
            for (int i = 0; i < 180; i++) yield return null;
            var values = new double[600];
            double previous = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < values.Length; i++)
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                values[i] = (now - previous) * 1000;
                previous = now;
            }
            var sorted = values.OrderBy(x => x).ToArray();
            var sample = new Sample
            {
                mode = mode, frames = values.Length, frameMs = values,
                medianMs = (sorted[299] + sorted[300]) / 2,
                p95Ms = sorted[569]
            };
            _samples.Add(sample);
            Debug.Log($"[VISUAL_RENDER_AB] mode={mode} medianMs={sample.medianMs:F3} p95Ms={sample.p95Ms:F3}");
        }
    }
}
#endif
