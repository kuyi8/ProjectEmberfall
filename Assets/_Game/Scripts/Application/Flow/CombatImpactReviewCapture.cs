#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit Development-only evidence. Attacks resolve through the production combat actor.</summary>
    public sealed class CombatImpactReviewCapture
    {
        private static readonly string Output = Environment.GetCommandLineArgs().Contains("-emberfall-impact-refined")
            ? "Builds/ArtReview/0.9.2-impact-layered-refined" : "Builds/ArtReview/0.9.2-impact-layered-baseline";
        private static readonly float[] SampleSeconds = { .02f, .08f, .16f, .25f, .40f };
        private CombatImpactPresentationEvent _last;
        private readonly List<Confirmation> _confirmations = new List<Confirmation>();
        private readonly List<VisualSample> _samples = new List<VisualSample>();

        [Serializable] private sealed class RendererRecord
        {
            public string path, type, material, layer;
            public bool enabled, forceRenderingOff;
            public Vector3 worldPosition, boundsMin, boundsMax;
            public float simulationSpeed;
            public int particleCount;
        }
        [Serializable] private sealed class VisualSample
        {
            public string grade, layer, image;
            public float requestedSimulationSeconds, scaledSceneTime, poolLifetimeSeconds;
            public int captureFrame;
            public Vector3 cameraPosition, cameraEuler, attackerPosition, targetPosition, impactPosition;
            public RendererRecord[] renderers;
        }

        [Serializable] private sealed class Confirmation
        {
            public string grade;
            public ulong sequence;
            public int attackSequence, targetId, frame;
        }
        [Serializable] private sealed class Report
        {
            public string version = "0.9.2-impact-vfx";
            public string evidence = "Development isolated-save production scene, real domain commands and physics hits; fixed camera, AI disabled; offscreen URP StandardRequest, no IMGUI";
            public bool fullVisualAcceptance = false, presentedFpsMeasured = false, gpuTimeMeasured = false;
            public bool ssaoActive, postProcessing;
            public float renderScale;
            public float[] particleSampleSeconds = SampleSeconds;
            public string sampling = "ParticleSystem.Simulate requested time; each system retains its recorded simulationSpeed; seed 42; pose and sword trail frozen at confirmation, NOT natural animation or contact timing";
            public bool poseFrozenAtConfirmation = true;
            public Confirmation[] confirmations;
            public VisualSample[] samples;
            public string measurement = "Frozen scene, synchronous camera-render/readback wall time; not ordinary gameplay FPS or GPU time";
            public double baselineMedianMs, baselineP95Ms, burstsMedianMs, burstsP95Ms;
            public int stressBursts;
        }

        public IEnumerator Run(Camera camera, PlayerCombatActor player, MeleeEnemyActor enemy, RenderTexture target)
        {
            Directory.CreateDirectory(Output);
            var report = new Report();
            report.ssaoActive = Resources.FindObjectsOfTypeAll<UniversalRendererData>()
                .SelectMany(x => x.rendererFeatures).Single(x => x != null && x.name == "Emberfall SSAO").isActive;
            report.postProcessing = camera.GetUniversalAdditionalCameraData().renderPostProcessing;
            report.renderScale = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale;
            if (report.ssaoActive || !report.postProcessing || report.renderScale != 1)
                throw new InvalidOperationException("Impact capture must match the production no-SSAO pipeline.");
            float savedTimeScale = Time.timeScale;
            Action<CombatImpactPresentationEvent> record = impact =>
            {
                _last = impact;
                _confirmations.Add(new Confirmation { grade = impact.Grade.ToString(), sequence = impact.Sequence,
                    attackSequence = impact.AttackSequence, targetId = impact.TargetId, frame = Time.frameCount });
                // Pause only this explicitly requested diagnostic session at the real confirmation.
                // Otherwise a shader warm-up frame can skip past a short-lived effect before capture.
                Time.timeScale = 0;
            };
            player.ImpactPresented += record;
            try
            {
                foreach (var agent in UnityEngine.Object.FindObjectsOfType<NavMeshAgent>()) agent.enabled = false;
                Time.timeScale = 1;
                player.transform.SetPositionAndRotation(enemy.transform.position + new Vector3(0, 1f, -1.35f), Quaternion.identity);
                enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
                camera.transform.position = enemy.transform.position + new Vector3(2.6f, 2.7f, -4.5f);
                camera.transform.LookAt(enemy.transform.position + Vector3.up * .85f);
                Physics.SyncTransforms();
                player.Model.Submit(CombatCommand.Sweep);
                yield return WaitFor(HitFeedbackGrade.Sweep);
                yield return Capture("sweep", target, camera, player, enemy);
                yield return Recover(player);
                player.transform.SetPositionAndRotation(enemy.transform.position + new Vector3(0, 1f, -1.35f), Quaternion.identity);
                Physics.SyncTransforms();
                enemy.ApplyNeutralPostureDamage(Mathf.Max(0, enemy.Brain.Posture.Current - 1));
                player.Model.Submit(CombatCommand.LightAttack);
                yield return WaitFor(HitFeedbackGrade.GuardBreak);
                yield return Capture("guard-break", target, camera, player, enemy);
                yield return Recover(player);
                if (!player.TryHandleExecutionInput()) throw new InvalidOperationException("Execution input not handled.");
                yield return WaitFor(HitFeedbackGrade.Execution);
                yield return Capture("execution", target, camera, player, enemy);
                yield return Recover(player);
                yield return new WaitForSeconds(1);

                // Repeatable presentation-only stress after the real combat evidence, not synthetic damage proof.
                Time.timeScale = 0;
                yield return Measure(value => { report.baselineMedianMs = value[0]; report.baselineP95Ms = value[1]; });
                var pool = CombatBurstVfxPool.ForScene(player.gameObject.scene);
                foreach (var kind in new[] { CombatBurstKind.GuardBreak, CombatBurstKind.Execution, CombatBurstKind.Sweep })
                {
                    // Loaded prefab assets, never a leased/inactive pool instance (whose lifetime belongs to the pool).
                    string name = "P_M6_Impact_" + kind;
                    var variant = Resources.FindObjectsOfTypeAll<GameObject>().First(x => x.name == name && !x.scene.IsValid());
                    for (int i = 0; i < CombatBurstVfxPool.PerKindLimit; i++)
                        if (pool.TrySpawn(kind, variant, enemy.transform.position + new Vector3((i - 1) * .55f, 1f, -.2f), Quaternion.identity, 1f))
                            report.stressBursts++;
                }
                foreach (var particle in pool.GetComponentsInChildren<ParticleSystem>())
                {
                    particle.useAutoRandomSeed = false; particle.randomSeed = 42;
                    particle.Simulate(.08f, false, true, true); particle.Pause(false);
                }
                yield return Measure(value => { report.burstsMedianMs = value[0]; report.burstsP95Ms = value[1]; });
                report.confirmations = _confirmations.ToArray();
                report.samples = _samples.ToArray();
                File.WriteAllText(Path.Combine(Output, "evidence.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[IMPACT_REVIEW_COMPLETE] realGrades=3 stressBursts=" + report.stressBursts + " measurement=offscreen-wall-not-fps");
            }
            finally { player.ImpactPresented -= record; Time.timeScale = savedTimeScale; }
        }

        private IEnumerator WaitFor(HitFeedbackGrade grade)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (_last.Grade != grade && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (_last.Grade != grade) throw new InvalidOperationException("No real confirmation for " + grade + "; last=" + _last.Grade);
        }

        private static IEnumerator Recover(PlayerCombatActor player)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (player.Model.State != CombatState.Locomotion && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (player.Model.State != CombatState.Locomotion) throw new InvalidOperationException("Attack did not recover.");
        }

        private IEnumerator Capture(string label, RenderTexture target, Camera camera, PlayerCombatActor player, MeleeEnemyActor enemy)
        {
            Time.timeScale = 0;
            // Finish the confirmation frame's LateUpdate before selecting the real active renderers.
            yield return null; yield return null;
            var pools = UnityEngine.Object.FindObjectsOfType<CombatBurstVfxPool>();
            var particles = pools.SelectMany(x => x.GetComponentsInChildren<ParticleSystem>()).ToArray();
            var impactRenderers = pools.SelectMany(x => x.GetComponentsInChildren<Renderer>()).ToArray();
            var trailRenderers = player.GetComponentsInChildren<MeshRenderer>()
                .Where(x => x.name == "SwordTrail_Runtime").Cast<Renderer>().ToArray();
            var renderers = impactRenderers.Concat(trailRenderers).ToArray();
            var originalHidden = renderers.Select(x => x.forceRenderingOff).ToArray();
            var presenter = player.GetComponent<CombatImpactVfxPresenter>();
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (float seconds in SampleSeconds)
                {
                    foreach (var particle in particles)
                    {
                        particle.useAutoRandomSeed = false; particle.randomSeed = 42;
                        particle.Simulate(seconds, false, true, true); particle.Pause(false);
                    }
                    foreach (string layer in new[] { "full", "impact-only", "trail-only" })
                    {
                        // Only rendering changes: never deactivate a pool slot, invoke OnDisable, or clear the trail.
                        for (int i = 0; i < renderers.Length; i++)
                            renderers[i].forceRenderingOff = originalHidden[i] ||
                                (i < impactRenderers.Length ? layer == "trail-only" : layer == "impact-only");
                        yield return null; yield return null;
                        string file = label + "-" + Mathf.RoundToInt(seconds * 1000).ToString("D3") + "ms-" + layer + ".png";
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                        File.WriteAllBytes(Path.Combine(Output, file), image.EncodeToPNG());
                        _samples.Add(new VisualSample
                        {
                            grade = label, layer = layer, image = file,
                            requestedSimulationSeconds = seconds, scaledSceneTime = Time.time,
                            poolLifetimeSeconds = presenter.GradeLifetimeSeconds, captureFrame = Time.frameCount,
                            cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                            attackerPosition = player.transform.position, targetPosition = enemy.transform.position,
                            impactPosition = _last.Position,
                            renderers = renderers.Select(x => Describe(x, impactRenderers.Contains(x) ? "impact" : "trail")).ToArray()
                        });
                    }
                }
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].forceRenderingOff = originalHidden[i];
                RenderTexture.active = previous; UnityEngine.Object.Destroy(image); Time.timeScale = 1;
            }
        }

        private static RendererRecord Describe(Renderer renderer, string layer)
        {
            string path = renderer.name;
            for (var parent = renderer.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            var particle = renderer.GetComponent<ParticleSystem>();
            return new RendererRecord
            {
                path = path, layer = layer, type = renderer.GetType().Name,
                material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none",
                enabled = renderer.enabled, forceRenderingOff = renderer.forceRenderingOff,
                worldPosition = renderer.transform.position, boundsMin = renderer.bounds.min, boundsMax = renderer.bounds.max,
                simulationSpeed = particle != null ? particle.main.simulationSpeed : 0,
                particleCount = particle != null ? particle.particleCount : 0
            };
        }

        private static IEnumerator Measure(Action<double[]> receive)
        {
            for (int i = 0; i < 30; i++) yield return null;
            var samples = new double[120];
            double previous = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < samples.Length; i++)
            {
                yield return null;
                double now = Time.realtimeSinceStartupAsDouble;
                samples[i] = (now - previous) * 1000; previous = now;
            }
            Array.Sort(samples); receive(new[] { (samples[59] + samples[60]) / 2, samples[113] });
        }
    }
}
#endif
