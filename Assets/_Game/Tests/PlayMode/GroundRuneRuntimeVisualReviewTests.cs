#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class GroundRuneRuntimeVisualReviewTests
    {
        const float Radius = 2.55f, Fuse = 2f;
        const string MaterialPath = "Assets/_Game/Art/Materials/M1/M_RuneProjectile.mat";
        [Serializable] sealed class SourceHash { public string path, before, after; }
        [Serializable] sealed class Frame
        {
            public string image, adapter;
            public int frame, playerLoopFrames, width = 960, height = 600;
            public float elapsed, outerRadiusMin, outerRadiusMax, internalProgressRadius, fieldOfView;
            public Vector3 cameraPosition, cameraEuler;
            public Color materialColor;
            public bool warningVisible, wholeRingInFrustum;
        }
        [Serializable] sealed class Evidence
        {
            public string scope = "Actual NewGame/isolated save, scene10 lighting/ground and PlayerLoop. Temporary diagnostic URP camera, not formal camera, natural combat, HUD, real input or performance. Both visual adapters receive the same production Priest warning material/radius/fuse; not NGO material-routing or Server synchronization acceptance. Offline authority=false; Client retains its existing local fuse. Annulus is sphere-query XZ projection, not an exact slope boundary or player-centre danger boundary (collider extent matters). Frustum coverage is not pixel visibility; inspect PNGs.";
            public string savePath, materialPath = MaterialPath, materialGuid, shader, graphicsDevice, graphicsType, pipeline, floorCollider, floorObject;
            public bool sceneBytesSame, fog;
            public float fogStart, fogEnd, radius = Radius, fuse = Fuse;
            public Vector3 floorPoint, floorNormal;
            public SourceHash[] sources;
            public Frame[] frames;
        }

        [UnityTest] public IEnumerator OfflineAndClientLocalRune_RealUrpEarlyLateFrames_PreserveSceneSources()
        {
            string isolated = M2RouteFlowController.EditorTestSavePath;
            string isolationRoot = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(isolated, Is.Not.Null.And.Not.Empty);
            Assert.That(Path.GetFullPath(isolated).StartsWith(isolationRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            var hashes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(p => new[] { p, p + ".meta" }).Concat(new[] { MaterialPath, MaterialPath + ".meta" }).ToDictionary(p => p, Hash);
            string directory = Path.GetFullPath("Builds/ArtReview/ground-rune-runtime/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Assert.That(Directory.Exists(directory), Is.False); Directory.CreateDirectory(directory);
            var evidence = new Evidence(); var frames = new List<Frame>();
            GameObject cameraRoot = null, runeRoot = null;
            try
            {
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 8f;
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
                { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
                Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True);
                evidence.savePath = flow.SavePath;
                Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(isolated)));
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("10_EmberValley"));
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(Object.FindObjectOfType<PlayerCombatActor>(), Is.Not.Null);
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
                Assert.That(GraphicsSettings.currentRenderPipeline, Is.InstanceOf<UniversalRenderPipelineAsset>());
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath); Assert.That(material, Is.Not.Null);
                evidence.materialGuid = AssetDatabase.AssetPathToGUID(MaterialPath); evidence.shader = material.shader.name;
                evidence.graphicsDevice = SystemInfo.graphicsDeviceName; evidence.graphicsType = SystemInfo.graphicsDeviceType.ToString();
                evidence.pipeline = GraphicsSettings.currentRenderPipeline.name;
                evidence.fog = RenderSettings.fog; evidence.fogStart = RenderSettings.fogStartDistance; evidence.fogEnd = RenderSettings.fogEndDistance;
                // Measured first-hit footprint: seventeen rays share the actual
                // Camp paving height here; the former north-edge footprint crosses a gap.
                RaycastHit floor = FloorAt(-3f, -5f);
                evidence.floorCollider = floor.collider.name; evidence.floorObject = floor.collider.transform.parent?.name;
                evidence.floorPoint = floor.point; evidence.floorNormal = floor.normal;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2f / 16f;
                    RaycastHit edge = FloorAt(floor.point.x + Mathf.Cos(angle) * Radius, floor.point.z + Mathf.Sin(angle) * Radius);
                    Assert.That(edge.point.y, Is.EqualTo(floor.point.y).Within(.01f));
                }
                Vector3 origin = floor.point + Vector3.up * .04f;
                cameraRoot = new GameObject("GroundRune_DiagnosticCamera", typeof(Camera));
                Camera camera = cameraRoot.GetComponent<Camera>(); Assert.That(Camera.main, Is.Not.Null);
                camera.CopyFrom(Camera.main); camera.enabled = false; camera.fieldOfView = 48f; camera.aspect = 1.6f;
                cameraRoot.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing =
                    Camera.main.GetComponent<UniversalAdditionalCameraData>()?.renderPostProcessing ?? false;
                camera.transform.position = origin + new Vector3(0, 7.5f, 7.5f); camera.transform.LookAt(origin);
                foreach (bool network in new[] { false, true })
                {
                    RangedGroundRune offline = null; NetworkEnemyAttackPresentation client = null;
                    if (!network)
                    {
                        runeRoot = new GameObject("GroundRune_VisualFixture_Offline"); runeRoot.transform.position = origin;
                        offline = runeRoot.AddComponent<RangedGroundRune>();
                        offline.Configure(1, 1, Fuse, Radius, 0, 0, material, false, _ => { });
                    }
                    else
                    {
                        NetworkEnemyAttackPresentation.Spawn(RangedAttackKind.GroundRune, origin, new Vector3(Radius, 0, 0), 0, 0, Fuse, material);
                        client = Object.FindObjectsOfType<NetworkEnemyAttackPresentation>().Single(); runeRoot = client.gameObject;
                    }
                    float start = Time.time; int startFrame = Time.frameCount;
                    foreach (float sampleTime in new[] { .12f, 1.4f })
                    {
                        while (Time.time - start < sampleTime) yield return null;
                        yield return new WaitForEndOfFrame();
                        Assert.That(runeRoot != null, Is.True, "Preserve failed timing; do not silently extend the production local fuse.");
                        Renderer boundary = network ? client.WarningBoundaryRenderer : offline.WarningBoundaryRenderer;
                        float progress = network ? client.transform.localScale.x * .5f :
                            Vector3.ProjectOnPlane(offline.transform.Find("HostileRuneSegment_00").localPosition, Vector3.up).magnitude;
                        frames.Add(Capture(camera, boundary, origin, progress, directory, network ? "client" : "offline", sampleTime, start, startFrame));
                    }
                    Assert.That(frames[frames.Count - 1].frame, Is.GreaterThan(frames[frames.Count - 2].frame));
                    Assert.That(frames[frames.Count - 1].internalProgressRadius, Is.GreaterThan(frames[frames.Count - 2].internalProgressRadius));
                    deadline = Time.realtimeSinceStartup + 4f;
                    while (runeRoot != null && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(runeRoot == null, Is.True); yield return null;
                }
                Assert.That(frames.Count, Is.EqualTo(4));
                Assert.That(hashes.All(p => Hash(p.Key) == p.Value), Is.True);
            }
            finally
            {
                if (runeRoot != null) Object.Destroy(runeRoot); if (cameraRoot != null) Object.Destroy(cameraRoot);
                evidence.sceneBytesSame = hashes.All(p => Hash(p.Key) == p.Value);
                evidence.sources = hashes.Select(p => new SourceHash { path = p.Key, before = p.Value, after = Hash(p.Key) }).ToArray();
                evidence.frames = frames.ToArray(); File.WriteAllText(directory + "/actual-rune.json", JsonUtility.ToJson(evidence, true));
            }
        }
        static RaycastHit FloorAt(float x, float z)
        {
            var hit = Physics.RaycastAll(new Vector3(x, 8, z), Vector3.down, 16, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance).FirstOrDefault(h => h.collider.GetComponentInParent<CombatTarget>() == null);
            Assert.That(hit.collider, Is.Not.Null);
            // Actual M6 paving is .03m above the greybox Camp box. Keep the
            // first real hit; never skip covering props to manufacture visibility.
            var parent = hit.collider.transform.parent;
            bool paving = hit.collider is MeshCollider && hit.collider.name == "Model" && parent != null &&
                (parent.name.StartsWith("Camp_Paving_", StringComparison.Ordinal) || parent.name.StartsWith("Camp_Edge_", StringComparison.Ordinal)) &&
                parent.parent != null && parent.parent.name == "[Art] M6 Environment";
            Assert.That(paving || hit.collider.name == "Zone_Camp", Is.True, "First hit is not the real Camp floor: " + hit.collider.name);
            Assert.That(hit.normal.y, Is.GreaterThan(.99f)); return hit;
        }
        static Frame Capture(Camera camera, Renderer boundary, Vector3 origin, float progress, string directory,
            string adapter, float sampleTime, float start, int startFrame)
        {
            Assert.That(boundary, Is.Not.Null);
            Vector3[] points = boundary.GetComponent<MeshFilter>().sharedMesh.vertices.Where((_, i) => i % 2 == 1)
                .Select(boundary.transform.TransformPoint).ToArray();
            float[] radii = points.Select(p => new Vector2(p.x - origin.x, p.z - origin.z).magnitude).ToArray();
            bool inFrustum = points.Select(camera.WorldToViewportPoint).All(p => p.z > camera.nearClipPlane && p.z < camera.farClipPlane && p.x > .03f && p.x < .97f && p.y > .03f && p.y < .97f);
            var row = new Frame { image = adapter + (sampleTime < 1 ? "-early.png" : "-late.png"), adapter = adapter,
                frame = Time.frameCount, playerLoopFrames = Time.frameCount - startFrame, elapsed = Time.time - start,
                outerRadiusMin = radii.Min(), outerRadiusMax = radii.Max(), internalProgressRadius = progress,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles, fieldOfView = camera.fieldOfView,
                warningVisible = boundary.enabled && boundary.gameObject.activeInHierarchy, wholeRingInFrustum = inFrustum,
                materialColor = boundary.sharedMaterial.color };
            var target = new RenderTexture(row.width, row.height, 24); var texture = new Texture2D(row.width, row.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, row.width, row.height), 0, 0); texture.Apply();
                File.WriteAllBytes(directory + "/" + row.image, texture.EncodeToPNG());
                File.WriteAllText(directory + "/" + row.image + ".json", JsonUtility.ToJson(row, true));
            }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(texture); }
            Assert.That(row.warningVisible && row.wholeRingInFrustum, Is.True);
            Assert.That(row.playerLoopFrames, Is.GreaterThanOrEqualTo(2)); Assert.That(row.elapsed, Is.LessThan(Fuse));
            Assert.That(row.outerRadiusMin, Is.EqualTo(Radius).Within(.002f)); Assert.That(row.outerRadiusMax, Is.EqualTo(Radius).Within(.002f));
            return row;
        }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return File.Exists(path) ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "") : "MISSING"; }
    }
}
#endif
