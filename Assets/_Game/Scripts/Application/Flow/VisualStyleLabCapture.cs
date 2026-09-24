#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit Development-only look-dev; normal backbuffer screenshots include actual HUD.</summary>
    public sealed class VisualStyleLabCapture
    {
        private string Output = "Builds/ArtReview/style-lab";
        private readonly List<Shot> _shots = new List<Shot>();
        [Serializable] private sealed class Shot
        {
            public string view, style, source = "ScreenCapture.CaptureScreenshotAsTexture: normal player backbuffer, not an offscreen request";
            public int width, height;
            public Vector3 cameraPosition, cameraEuler;
            public float fieldOfView;
        }
        [Serializable] private sealed class ColorCheck
        {
            public string scope = "Frozen scene, same camera/pose, consecutive render paths; central ROI excludes HUD; no physical display calibration claim";
            public string colorSpace, targetFormat;
            public bool targetSrgb;
            public double rawMeanError, gammaEncodedMeanError;
            public double screenMean, offscreenMean;
            public string result;
        }
        [Serializable] private sealed class Manifest
        {
            public string version = "0.9.0-lookdev", device, skybox;
            public string portraitScope = "Isolated material portraits: the other actor's renderers are temporarily hidden and restored; no animation/grounding acceptance.";
            public bool ssao = false, outline = false, characterReplacement = false, materialEdits = false;
            public string acceptance = "Look-dev only. User aesthetics and presented combat performance remain open.";
            public Shot[] shots;
        }

        public IEnumerator Run(Camera camera, PlayerCombatActor player, MeleeEnemyActor enemy, ScriptableRendererFeature ssao, bool production = false)
        {
            if (production) Output = "Builds/ArtReview/0.9.0-bright";
            Directory.CreateDirectory(Output);
            var volume = UnityEngine.Object.FindObjectsOfType<Volume>().Single(x => x.isGlobal);
            VolumeProfile runtime = volume.profile; // Unity deep-clones component instances here.
            Light sun = UnityEngine.Object.FindObjectsOfType<Light>().First(x => x.type == LightType.Directional);
            var current = VisualStyleProfile.Snapshot(camera, runtime, sun);
            var step1 = VisualStyleProfile.StepOne(current);
            var step2 = VisualStyleProfile.StepTwo(step1, TonemappingMode.ACES);
            var neutral = VisualStyleProfile.StepTwo(step1, TonemappingMode.Neutral);
            var cartoon = VisualStyleProfile.Cartoon(current);
            var solid = cartoon.Copy("cartoon-solid");
            solid.clearFlags = CameraClearFlags.SolidColor;
            var profiles = new[] { current, step1, step2, neutral, cartoon, solid };
            var allProfiles = profiles;
            if (production) { current.id = "production"; profiles = new[] { current }; }
            var portraits = production ? profiles : new[] { current, cartoon };
            Material oldSky = RenderSettings.skybox;
            bool oldSsao = ssao.isActive;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            ssao.SetActive(false);
            camera.enabled = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            // Built-in procedural sky ships with Unity; do not add a shader or imported resource.
            if (oldSky == null) throw new InvalidOperationException("No existing skybox material; do not silently invent an external dependency.");
            try
            {
                foreach (var p in profiles)
                    File.WriteAllText(Path.Combine(Output, "parameters-" + p.id + ".json"), JsonUtility.ToJson(p, true));
                yield return ColorPipelineCheck(camera);
                // Formal recalibration candidates are runtime-only until the color chain and images are reviewed.
                yield return Capture("forest", profiles, camera, runtime, sun);
                var target = player.transform.position + Vector3.up * 0.9f;
                // Isolated material portraits only: hide the other actor, restoring it before gameplay views.
                Vector3 portraitFocus = player.transform.position - Vector3.up * 0.2f;
                camera.transform.position = portraitFocus + player.transform.forward * 3.8f + Vector3.up * 0.3f;
                camera.transform.LookAt(portraitFocus);
                camera.fieldOfView = 40f;
                yield return CapturePortrait("player-front", portraits, camera, runtime, sun, enemy.gameObject);
                camera.fieldOfView = originalFov;
                camera.transform.position = target - player.transform.forward * 3.5f + Vector3.up * 0.3f;
                camera.transform.LookAt(target);
                yield return CapturePortrait("player-back", portraits, camera, runtime, sun, enemy.gameObject);
                camera.transform.position = enemy.AimPoint.position + enemy.transform.forward * 3.6f + Vector3.up * 0.3f;
                camera.transform.LookAt(enemy.AimPoint.position);
                yield return CapturePortrait("enemy-fogwalker", portraits, camera, runtime, sun, player.gameObject);
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                // Forest already contains the real HUD: don't count duplicate files as another view.
                enemy.ApplyNeutralPostureDamage(999f);
                foreach (var animator in enemy.GetComponentsInChildren<Animator>()) animator.Update(0.18f);
                yield return Capture("posture-break", profiles.Take(5).ToArray(), camera, runtime, sun);
                if (!player.TryHandleExecutionInput() || !enemy.IsExecutionClaimed ||
                    player.Model.State != Emberfall.Gameplay.Combat.Domain.CombatState.Execution)
                    throw new InvalidOperationException("Style lab requires real execution, not a staged idle pose.");
                player.Model.Tick(0.18f);
                yield return null;
                foreach (var animator in player.GetComponentsInChildren<Animator>()) animator.Update(0.18f);
                yield return Capture("execution", profiles.Take(5).ToArray(), camera, runtime, sun);
                File.WriteAllText(Path.Combine(Output, "manifest.json"), JsonUtility.ToJson(new Manifest
                {
                    version = production ? "0.9.0-bright-production" : "0.9.0-lookdev",
                    acceptance = production ? "Authored production appearance. Harness visual acceptance and presented combat performance remain open." : "Look-dev only. User aesthetics and presented combat performance remain open.",
                    device = SystemInfo.graphicsDeviceName, skybox = oldSky.name, shots = _shots.ToArray()
                }, true));
            }
            finally
            {
                current.Apply(camera, runtime, sun);
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.fieldOfView = originalFov;
                ssao.SetActive(oldSsao);
                RenderSettings.skybox = oldSky;
                foreach (var p in allProfiles) UnityEngine.Object.Destroy(p);
            }
            if (production) yield return VerifyAuthoredScenes();
            Debug.Log("[VISUAL_STYLE_LAB_COMPLETE] source=normal-backbuffer assetsWritten=false performanceAccepted=false");
        }

        private IEnumerator VerifyAuthoredScenes()
        {
            var records = new List<string>();
            foreach (string sceneName in new[] { "10_EmberValley", "20_Sanctum", "90_CombatGym", "91_NetworkGym" })
            {
                yield return SceneManager.LoadSceneAsync(sceneName);
                yield return null;
                if (RenderSettings.fog || RenderSettings.ambientMode != AmbientMode.Trilight ||
                    RenderSettings.ambientSkyColor != new Color(.55f, .62f, .72f) ||
                    RenderSettings.ambientEquatorColor != new Color(.4f, .42f, .42f) ||
                    RenderSettings.ambientGroundColor != new Color(.24f, .22f, .2f))
                    throw new InvalidOperationException("Built scene differs from selected style: " + sceneName);
                var cameras = SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(x => x.GetComponentsInChildren<Camera>(true)).ToArray();
                foreach (var c in cameras) VerifyCamera(c);
                string record = $"scene={sceneName} ambient=Trilight sky=.55,.62,.72 equator=.4,.42,.42 ground=.24,.22,.2 fog=false cameras={cameras.Length}";
                records.Add(record);
                Debug.Log("[BRIGHT_STYLE_BUILT_SCENE] " + record);
            }
            var owner = Resources.Load<GameObject>("Networking/P_M5_NetworkGymPlayer").GetComponentInChildren<Camera>(true);
            VerifyCamera(owner);
            records.Add("NGO Owner prefab: SolidColor .45,.68,.85 postProcessing=true");
            File.WriteAllLines(Path.Combine(Output, "built-scene-values.txt"), records);
        }

        private static void VerifyCamera(Camera camera)
        {
            if (camera.clearFlags != CameraClearFlags.SolidColor || camera.backgroundColor != new Color(.45f, .68f, .85f) ||
                !camera.GetUniversalAdditionalCameraData().renderPostProcessing)
                throw new InvalidOperationException("Built camera style differs: " + camera.name);
        }

        private IEnumerator CapturePortrait(string view, VisualStyleProfile[] profiles, Camera camera, VolumeProfile runtime, Light sun, GameObject otherActor)
        {
            var renderers = otherActor.GetComponentsInChildren<Renderer>(true);
            var previous = renderers.Select(renderer => renderer.forceRenderingOff).ToArray();
            try
            {
                foreach (var renderer in renderers) renderer.forceRenderingOff = true;
                yield return Capture(view, profiles, camera, runtime, sun);
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].forceRenderingOff = previous[i];
            }
        }

        private IEnumerator Capture(string view, VisualStyleProfile[] profiles, Camera camera, VolumeProfile runtime, Light sun)
        {
            foreach (var p in profiles)
            {
                p.Apply(camera, runtime, sun);
                for (int i = 0; i < 10; i++) yield return null;
                yield return new WaitForEndOfFrame();
                if (VolumeManager.instance.stack.GetComponent<Tonemapping>().mode.value != p.tonemapping)
                    throw new InvalidOperationException("Rendered Volume did not select " + p.id);
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                try { Save(texture, view + "-" + p.id); }
                finally { UnityEngine.Object.Destroy(texture); }
                _shots.Add(new Shot { view = view, style = p.id, width = Screen.width, height = Screen.height,
                    cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                    fieldOfView = camera.fieldOfView });
            }
        }

        private IEnumerator ColorPipelineCheck(Camera camera)
        {
            for (int i = 0; i < 10; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var screen = ScreenCapture.CaptureScreenshotAsTexture();
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var offscreen = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Save(screen, "color-chain-screen");
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                offscreen.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                offscreen.Apply();
                Save(offscreen, "color-chain-offscreen-raw");
                Color32[] a = screen.GetPixels32(), b = offscreen.GetPixels32();
                double raw = 0, encoded = 0, screenMean = 0, offMean = 0;
                int n = 0;
                for (int y = 260; y < 810; y += 3)
                for (int x = 480; x < 1440; x += 3)
                {
                    int i = y * 1920 + x;
                    raw += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                    encoded += Mathf.Abs(a[i].r - Encode(b[i].r)) + Mathf.Abs(a[i].g - Encode(b[i].g)) + Mathf.Abs(a[i].b - Encode(b[i].b));
                    screenMean += (a[i].r + a[i].g + a[i].b) / 3.0;
                    offMean += (b[i].r + b[i].g + b[i].b) / 3.0;
                    n++;
                }
                var check = new ColorCheck { colorSpace = QualitySettings.activeColorSpace.ToString(),
                    targetFormat = target.graphicsFormat.ToString(), targetSrgb = target.sRGB,
                    rawMeanError = raw / (n * 3), gammaEncodedMeanError = encoded / (n * 3),
                    screenMean = screenMean / n, offscreenMean = offMean / n,
                    result = raw / (n * 3) <= 2 ? "raw-offscreen-matches-backbuffer" :
                        encoded / (n * 3) <= 2 ? "raw-offscreen-missing-srgb-encoding-use-backbuffer" : "paths-differ-use-backbuffer-only" };
                File.WriteAllText(Path.Combine(Output, "color-chain.json"), JsonUtility.ToJson(check, true));
                Debug.Log("[VISUAL_COLOR_CHAIN] " + JsonUtility.ToJson(check));
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(offscreen);
                UnityEngine.Object.Destroy(screen);
            }
        }

        private static int Encode(byte value) => Mathf.RoundToInt(Mathf.LinearToGammaSpace(value / 255f) * 255f);

        private void Save(Texture2D texture, string label)
        {
            if (texture == null || texture.width != 1920 || texture.height != 1080)
                throw new InvalidOperationException("Backbuffer must be 1920x1080: " + label);
            var pixels = texture.GetPixels32();
            byte low = 255, high = 0;
            for (int i = 0; i < pixels.Length; i += 97)
            {
                low = (byte)Math.Min(low, pixels[i].g);
                high = (byte)Math.Max(high, pixels[i].g);
            }
            if (high - low < 20) throw new InvalidOperationException("Empty render rejected: " + label);
            File.WriteAllBytes(Path.Combine(Output, label + ".png"), texture.EncodeToPNG());
        }
    }
}
#endif
