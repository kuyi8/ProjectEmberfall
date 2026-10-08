#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
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
    public sealed class CharacterPaletteRuntimeReviewTests
    {
        [Serializable] sealed class Binding { public string field, renderer; public int rendererId; }
        [Serializable] sealed class Surface
        {
            public string key, renderer, material, assetPath, shader; public int rendererId, slot;
            public bool enabled, active, hasPropertyBlock, hasColor; public Color color, baseColor, emission, mpbBaseColor, mpbEmission;
        }
        [Serializable] sealed class Shot
        {
            public string image, actor, type, state, avatar, avatarPath, coverage, camera = "Temporary inspection camera, NOT formal third-person camera; 3D only, no IMGUI";
            public int frame, framesSinceAttempt; public float time, attemptTime, healthBefore, healthAfter, appliedDamage;
            public bool accepted, blocked, killed, actorEnabled, actorActive, boundsInFrustum, postProcessing;
            public string stateBefore, simulationAuthority; public Vector3 actorPosition, cameraPosition, cameraEuler; public float fov;
            public Binding[] bindings; public Surface[] surfaces;
        }
        [Serializable] sealed class FileProof { public string path, before, after; }
        [Serializable] sealed class Evidence
        {
            public string scope = "Real NewGame/Bootstrap, production Ranger and existing Valley actors after PlayerLoop in actual scene lighting. AI/flow stay active; no fixture relocation or state forcing. One synthetic public 1-damage request per enemy is NOT real-input combat, natural contact, difficulty, network tint or performance acceptance. Rejected/blocked/no-white-flash attempts stay notcovered. Frustum bounds do not prove pixel visibility; inspect PNGs.";
            public string savePath, editorTestSavePath; public bool sceneBytesSame; public FileProof[] sceneHashes; public Shot[] shots;
        }

        [UnityTest]
        public IEnumerator ValleyPalette_RealPlayerLoopIdleAndPublicDamageAttempts_PreserveSourceScenes()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Real graphics are required; no headless visual acceptance.");
            string isolation = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            string isolatedSave = Path.GetFullPath(M2RouteFlowController.EditorTestSavePath);
            Assert.That(isolatedSave.StartsWith(isolation, StringComparison.OrdinalIgnoreCase), Is.True);
            var hashes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(path => new[] { path, path + ".meta" }).ToDictionary(path => path, Hash);
            string directory = Path.GetFullPath("Builds/ArtReview/character-palette-runtime/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory); var shots = new List<Shot>(); string savePath = null; GameObject cameraObject = null;
            try
            {
                M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 8f; M2RouteFlowController flow = null;
                while (Time.realtimeSinceStartup < deadline)
                { flow = Object.FindObjectOfType<M2RouteFlowController>(); if (flow != null && flow.IsInitialized) break; yield return null; }
                Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True); savePath = flow.SavePath;
                Assert.That(Path.GetFullPath(savePath), Is.EqualTo(isolatedSave)); Assert.That(Time.timeScale, Is.EqualTo(1f));
                var player = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(player, Is.Not.Null); Assert.That(player.Model, Is.Not.Null);
                var ranger = player.GetComponentInChildren<Animator>(true); Assert.That(ranger, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(ranger.avatar), Is.EqualTo("Assets/_Game/Art/Characters/M3Art/Derived/Male_Ranger_Emberfall.fbx"));
                var fog = Object.FindObjectsOfType<MeleeEnemyActor>().Single(actor => actor.name == "Enemy_Fogwalker_Forest");
                var elite = Object.FindObjectsOfType<ShieldEnemyActor>().Single(actor => actor.name == "Enemy_RuinGuard_Courtyard");
                var warden = Object.FindObjectsOfType<WardenActor>().Single(actor => actor.name == "Enemy_EmberWarden");
                cameraObject = new GameObject("CharacterPaletteRuntimeInspectionCamera"); var camera = cameraObject.AddComponent<Camera>();
                var formal = Camera.main; if (formal != null) camera.CopyFrom(formal);
                var formalData = formal != null ? formal.GetComponent<UniversalAdditionalCameraData>() : null;
                var data = cameraObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = formalData != null && formalData.renderPostProcessing;
                if (formalData != null) data.volumeLayerMask = formalData.volumeLayerMask; data.volumeTrigger = camera.transform;
                camera.enabled = false; camera.fieldOfView = 42f; camera.aspect = 1.6f; camera.nearClipPlane = .08f; camera.farClipPlane = 150f;
                camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = true;
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                var actors = new MonoBehaviour[] { player, fog, elite, warden };
                for (int index = 0; index < actors.Length; index++)
                {
                    MonoBehaviour actor = actors[index]; FrameCamera(camera, actor);
                    yield return null; yield return new WaitForEndOfFrame();
                    var idle = Capture(camera, actor, directory, index + "-before.png"); idle.coverage = "idle/live-state only"; shots.Add(idle);
                    Assert.That(idle.boundsInFrustum, Is.True, idle.image);
                    if (actor == player) continue;
                    var field = actor.GetType().GetField("_bodyRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
                    var body = field?.GetValue(actor) as Renderer; Assert.That(body, Is.Not.Null, actor.name);
                    var before = Surfaces(actor); Color bodyBefore = body.sharedMaterials[0].color;
                    float healthBefore = Health(actor), attemptTime = Time.time; string stateBefore = State(actor); int attemptFrame = Time.frameCount;
                    var request = new DamageRequest(player.CombatantId, 98000 + index, 1f, 0f, AttackTag.Light);
                    DamageResult result = actor is MeleeEnemyActor melee ? melee.ReceiveDamage(request) :
                        actor is ShieldEnemyActor shield ? shield.ReceiveDamage(request) : ((WardenActor)actor).ReceiveDamage(request);
                    yield return new WaitForSeconds(.045f); yield return new WaitForEndOfFrame();
                    FrameCamera(camera, actor); var after = Capture(camera, actor, directory, index + "-damage-attempt.png"); shots.Add(after);
                    after.accepted = result.Accepted; after.blocked = result.Blocked; after.killed = result.Killed; after.appliedDamage = result.AppliedDamage;
                    after.healthBefore = healthBefore; after.healthAfter = Health(actor); after.stateBefore = stateBefore;
                    after.attemptTime = attemptTime; after.framesSinceAttempt = Time.frameCount - attemptFrame;
                    bool whiteShift = Vector3.Distance(Rgb(body.sharedMaterials[0].color), Vector3.one) < Vector3.Distance(Rgb(bodyBefore), Vector3.one) - .01f;
                    after.coverage = result.Accepted && result.AppliedDamage > 0f && after.healthAfter < healthBefore && whiteShift
                        ? "covered: accepted public damage + actual health loss + body moved toward white"
                        : "notcovered: damage refused/blocked/no health loss or no observed body white shift; no flow/AI forcing";
                    foreach (Surface previous in before.Where(surface => surface.material.StartsWith("M_CP_", StringComparison.Ordinal) && !(surface.rendererId == body.GetInstanceID() && surface.slot == 0)))
                    {
                        Surface current = after.surfaces.Single(surface => surface.key == previous.key);
                        Assert.That(current.color, Is.EqualTo(previous.color), "Other palette slot lost its authored color: " + previous.renderer);
                        Assert.That(current.baseColor, Is.EqualTo(previous.baseColor)); Assert.That(current.emission, Is.EqualTo(previous.emission));
                        Assert.That(current.mpbBaseColor, Is.EqualTo(previous.mpbBaseColor)); Assert.That(current.mpbEmission, Is.EqualTo(previous.mpbEmission));
                        Assert.That(current.hasPropertyBlock, Is.EqualTo(previous.hasPropertyBlock));
                    }
                    Assert.That(after.boundsInFrustum, Is.True, after.image);
                }
                Assert.That(shots.Count, Is.EqualTo(7)); Assert.That(hashes.All(pair => Hash(pair.Key) == pair.Value), Is.True);
            }
            finally
            {
                if (cameraObject != null) Object.Destroy(cameraObject);
                File.WriteAllText(directory + "/review.json", JsonUtility.ToJson(new Evidence { savePath = savePath,
                    editorTestSavePath = M2RouteFlowController.EditorTestSavePath, shots = shots.ToArray(),
                    sceneBytesSame = hashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value),
                    sceneHashes = hashes.Select(pair => new FileProof { path = pair.Key, before = pair.Value, after = File.Exists(pair.Key) ? Hash(pair.Key) : "MISSING" }).ToArray() }, true));
            }
        }

        static Shot Capture(Camera camera, MonoBehaviour actor, string directory, string filename)
        {
            var animator = actor.GetComponentInChildren<Animator>(true); var bounds = ActorBounds(actor);
            var row = new Shot { image = filename, actor = actor.name, type = actor.GetType().Name, state = State(actor), frame = Time.frameCount, time = Time.time,
                avatar = animator?.avatar?.name, avatarPath = animator?.avatar != null ? AssetDatabase.GetAssetPath(animator.avatar) : "",
                actorEnabled = actor.enabled, actorActive = actor.gameObject.activeInHierarchy,
                simulationAuthority = actor.GetType().GetProperty("HasSimulationAuthority")?.GetValue(actor)?.ToString() ?? "not exposed",
                actorPosition = actor.transform.position, cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles, fov = camera.fieldOfView,
                postProcessing = camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing,
                boundsInFrustum = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds),
                bindings = actor.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(field => typeof(Renderer).IsAssignableFrom(field.FieldType))
                    .Select(field => new Binding { field = field.Name, renderer = (field.GetValue(actor) as Renderer)?.name, rendererId = (field.GetValue(actor) as Renderer)?.GetInstanceID() ?? 0 }).ToArray(), surfaces = Surfaces(actor) };
            var target = new RenderTexture(960, 600, 24); var texture = new Texture2D(960, 600, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); texture.Apply(); File.WriteAllBytes(directory + "/" + filename, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(texture); }
            return row;
        }
        static void FrameCamera(Camera camera, MonoBehaviour actor)
        { var bounds = ActorBounds(actor); camera.transform.position = bounds.center + actor.transform.forward * 3.5f + actor.transform.right * 1.3f + Vector3.up * .85f; camera.transform.LookAt(bounds.center); }
        static Bounds ActorBounds(MonoBehaviour actor)
        {
            var renderers = actor.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMaterials.Any(material => material != null && material.name.StartsWith("M_CP_", StringComparison.Ordinal))).ToArray();
            Assert.That(renderers, Is.Not.Empty, "Saved production palette must already exist; tests do not migrate materials.");
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static Surface[] Surfaces(MonoBehaviour actor)
        {
            var rows = new List<Surface>();
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot]; if (material == null) continue;
                    rows.Add(new Surface { key = renderer.GetInstanceID() + ":" + slot, renderer = renderer.name, rendererId = renderer.GetInstanceID(), slot = slot,
                        material = material.name, assetPath = AssetDatabase.GetAssetPath(material), shader = material.shader?.name,
                        enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy,
                        hasColor = material.HasProperty("_BaseColor") || material.HasProperty("_Color"),
                        color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.GetColor("_Color") : default,
                        baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : default,
                        emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : default,
                        hasPropertyBlock = renderer.HasPropertyBlock(), mpbBaseColor = block.GetColor("_BaseColor"), mpbEmission = block.GetColor("_EmissionColor") });
                }
            }
            return rows.ToArray();
        }
        static float Health(MonoBehaviour actor) => actor is MeleeEnemyActor melee ? melee.HealthNormalized : actor is ShieldEnemyActor shield ? shield.HealthNormalized : ((WardenActor)actor).HealthNormalized;
        static string State(MonoBehaviour actor) => actor is PlayerCombatActor player ? player.Model.State.ToString() : actor.GetType().GetProperty("State")?.GetValue(actor)?.ToString();
        static Vector3 Rgb(Color color) => new Vector3(color.r, color.g, color.b);
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
#endif
