using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class RuneBlessingImpactPlayTests
    {
        readonly List<GameObject> _owned = new List<GameObject>();
        Scene _fixtureScene;
        Keyboard _keyboard;
        float _previousScale;

        [SetUp] public void Begin() { _previousScale = Time.timeScale; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator End()
        {
            Time.timeScale = _previousScale;
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            foreach (var root in _owned) if (root != null) Object.Destroy(root);
            _owned.Clear();
            if (_fixtureScene.IsValid() && _fixtureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(_fixtureScene);
            yield return null; yield return null;
        }

        Scene OwnScene()
        {
            _fixtureScene = SceneManager.CreateScene("RunePulseFixture-" + Guid.NewGuid().ToString("N"));
            return _fixtureScene;
        }

        RuneBlessingImpact Pulse(Scene scene, Color color, Vector3 point = default(Vector3), Camera camera = null)
        {
            var pulse = RuneBlessingImpact.Create(point, color, scene, camera);
            Assert.That(pulse, Is.Not.Null); _owned.Add(pulse.gameObject);
            Assert.That(pulse.GetComponentsInChildren<Collider>(true), Is.Empty, "Birth frame must have no collider, not deferred removal.");
            Assert.That(pulse.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(pulse.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            Assert.That(pulse.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(pulse.transform.parent, Is.Null);
            Assert.That(pulse.gameObject.scene, Is.EqualTo(scene));
            return pulse;
        }

        [UnityTest]
        public IEnumerator Pulse_NaturalExpiryPausesWithWorldTimeButNotAnimatorSpeed_AndReleasesResources()
        {
            Scene scene = OwnScene();
            var animatorObject = new GameObject("UnrelatedStoppedAnimator"); _owned.Add(animatorObject);
            SceneManager.MoveGameObjectToScene(animatorObject, scene);
            var animator = animatorObject.AddComponent<Animator>(); animator.speed = 0f;
            var pulse = Pulse(scene, Color.white);
            var mesh = pulse.GetComponent<MeshFilter>().sharedMesh;
            var material = pulse.GetComponent<MeshRenderer>().sharedMaterial;
            float initialAlpha = pulse.CurrentAlpha;
            yield return new WaitForSeconds(.10f);
            Assert.That(pulse, Is.Not.Null); Assert.That(pulse.ElapsedSeconds, Is.GreaterThan(.08f));
            Assert.That(pulse.CurrentAlpha, Is.LessThan(initialAlpha)); Assert.That(animator.speed, Is.Zero);
            Time.timeScale = 0f;
            yield return null; // The current frame's delta was computed before the pause request.
            float elapsed = pulse.ElapsedSeconds, alpha = pulse.CurrentAlpha; Vector3 scale = pulse.transform.localScale;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(pulse, Is.Not.Null); Assert.That(pulse.ElapsedSeconds, Is.EqualTo(elapsed));
            Assert.That(pulse.CurrentAlpha, Is.EqualTo(alpha)); Assert.That(pulse.transform.localScale, Is.EqualTo(scale));
            Time.timeScale = 1f;
            yield return new WaitForSeconds(.45f); yield return null; yield return null;
            Assert.That(pulse == null, Is.True); Assert.That(mesh == null && material == null, Is.True);
            Assert.That(animator.speed, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Pulse_EarlyDestroyReleasesOnlyItsOwnMeshAndMaterial()
        {
            Scene scene = OwnScene(); var a = Pulse(scene, Color.white); var b = Pulse(scene, Color.cyan);
            var mesh = a.GetComponent<MeshFilter>().sharedMesh; var material = a.GetComponent<MeshRenderer>().sharedMaterial;
            var otherMesh = b.GetComponent<MeshFilter>().sharedMesh; var otherMaterial = b.GetComponent<MeshRenderer>().sharedMaterial;
            Object.Destroy(a.gameObject); yield return null; yield return null;
            Assert.That(a == null && mesh == null && material == null, Is.True);
            Assert.That(b != null && otherMesh != null && otherMaterial != null, Is.True);
        }

        [UnityTest]
        public IEnumerator Pulse_OwnerSceneUnloadDestroysRootAndResourcesEvenBeforeExpiry()
        {
            Scene scene = OwnScene(); var pulse = Pulse(scene, new Color(.85f, .28f, 1f));
            var mesh = pulse.GetComponent<MeshFilter>().sharedMesh; var material = pulse.GetComponent<MeshRenderer>().sharedMaterial;
            yield return SceneManager.UnloadSceneAsync(scene); yield return null; yield return null;
            Assert.That(pulse == null, Is.True); Assert.That(mesh == null && material == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Actor_ActualQParry_RHeal_AndNeutralPosturePreserveFactsAndSpawnExpectedColours()
        {
            yield return LoadValley();
            var player = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(player, Is.Not.Null);
            Assert.That(player.enabled && player.gameObject.activeInHierarchy, Is.True);
            _keyboard = InputSystem.AddDevice<Keyboard>("RunePulseActualKeyboard");
            player.ApplyRuneBlessing(RuneBlessing.Guard);
            int parries = player.Model.PerfectGuardCount;
            float health = player.Model.Health.Current;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Q)); yield return null; yield return null;
            Assert.That(player.Model.State, Is.EqualTo(CombatState.Guard));
            Assert.That(player.Model.IsPerfectGuardWindow, Is.True);
            Vector3 guardPoint = player.AimPoint.position;
            var guard = player.ReceiveDamage(new DamageRequest(991337, 1, 20f, 10f, AttackTag.Light, true, true));
            Assert.That(guard.PerfectGuard && guard.Defended && !guard.Killed, Is.True);
            Assert.That(player.Model.Health.Current, Is.EqualTo(health));
            Assert.That(player.Model.PerfectGuardCount, Is.EqualTo(parries + 1));
            Assert.That(player.IsGuardCounterReady && player.Model.CanUseGuardCounter, Is.True);
            var white = FindPulse(player.gameObject.scene, Color.white); Assert.That(white, Is.Not.Null);
            Assert.That(white.BirthPosition, Is.EqualTo(guardPoint)); Assert.That(white.transform.parent, Is.Null);
            Assert.That(white.GetComponentsInChildren<Collider>(true), Is.Empty);
            float postureBefore = player.Model.Posture.Current;
            float neutral = player.ApplyNeutralPostureDamage(1f);
            Assert.That(neutral, Is.EqualTo(1f)); Assert.That(player.Model.Posture.Current, Is.EqualTo(postureBefore - neutral));
            Assert.That(player.Model.Health.Current, Is.EqualTo(health));
            var purple = FindPulse(player.gameObject.scene, new Color(.85f, .28f, 1f)); Assert.That(purple, Is.Not.Null);
            Assert.That(purple.GetComponentsInChildren<Collider>(true), Is.Empty);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return WaitForState(player, CombatState.Locomotion, 2f);
            var damage = player.ReceiveDamage(new DamageRequest(991337, 2, 20f, 0f, AttackTag.Light, false, false));
            Assert.That(damage.Accepted && !damage.Invulnerable && !damage.Killed && damage.AppliedDamage > 0f, Is.True);
            yield return WaitForState(player, CombatState.Locomotion, 2f);
            float injured = player.Model.Health.Current;
            int charges = player.Model.HealingFlasks.CurrentCharges, sequence = player.Model.HealSequence;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.R)); yield return null; yield return null;
            Assert.That(player.Model.State, Is.EqualTo(CombatState.Heal));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float deadline = Time.realtimeSinceStartup + 2f;
            while (player.Model.HealSequence == sequence && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(player.Model.HealSequence, Is.EqualTo(sequence + 1));
            Assert.That(player.Model.HealingFlasks.CurrentCharges, Is.EqualTo(charges - 1));
            Assert.That(player.Model.Health.Current, Is.GreaterThan(injured));
            var green = FindPulse(player.gameObject.scene, new Color(.28f, 1f, .45f)); Assert.That(green, Is.Not.Null);
            Assert.That(green.transform.parent, Is.Null); Assert.That(green.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(green.gameObject.scene, Is.EqualTo(player.gameObject.scene));
            yield return WaitForState(player, CombatState.Locomotion, 2f);
            yield return new WaitForSeconds(.5f);
            Assert.That(Object.FindObjectsOfType<RuneBlessingImpact>().Any(p => p.gameObject.scene == player.gameObject.scene), Is.False);
        }

        static RuneBlessingImpact FindPulse(Scene scene, Color color)
        {
            return Object.FindObjectsOfType<RuneBlessingImpact>().SingleOrDefault(p => p.gameObject.scene == scene && p.SourceColor == color);
        }
        static IEnumerator WaitForState(PlayerCombatActor player, CombatState state, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (player.Model.State != state && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(player.Model.State, Is.EqualTo(state));
        }
        static IEnumerator LoadValley()
        {
#if UNITY_EDITOR
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            Assert.That(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath).StartsWith(
                Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
#endif
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 8f; M2RouteFlowController flow = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                flow = Object.FindObjectOfType<M2RouteFlowController>();
                if (flow != null && flow.IsInitialized) break;
                yield return null;
            }
            Assert.That(flow != null && flow.IsInitialized, Is.True);
#if UNITY_EDITOR
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
#endif
            yield return new WaitForSeconds(.5f);
        }

#if UNITY_EDITOR
        [Serializable] sealed class VisualShot
        {
            public string name, background, cameraPoseSource, source = "Production Ranger/formal Camera projection; rear uses live production Rig LateUpdate, front is controlled fixture pose. Not natural player arrival/combat/HUD/performance.";
            public int frame, width, height, changedPixels, diagnosticHeadChangedPixels, diagnosticHeadRoiPixels, rigSettledFrames;
            public float time, meanAbsoluteLuminanceDifference, diagnosticHeadCoverage;
            public float lastCameraFrameDeltaMeters, lastCameraFrameDeltaDegrees;
            public bool rigEnabled;
            public Vector3 playerPosition, cameraPosition; public Quaternion cameraRotation;
            public float fieldOfView, productionAspect, captureAspect; public float[] elapsed, alpha; public int[] sortingOrder, birthCameraIds;
            public Rect diagnosticHeadBoneRoi;
        }
        [Serializable] sealed class VisualEvidence
        {
            public string scope = "Fresh real PlayerLoop scene/Ranger and the production Camera projection. Rear keeps ThirdPersonCameraRig enabled and waits for real LateUpdate collision/occluder distance to settle; no rear Camera pose or parameter write. Front alone uses a controlled fixture Camera pose. AI/Flow stay active. Optional zero-physics dark backdrop is fixture-only; no Actor warp, manual Animator evaluation, time pause or domain mutation. Four concurrent pulses are direct visual factory calls, NOT four simultaneous legal combat outcomes. Pixel differences compare same EOF render with ONLY these pulse Renderers toggled. Head ROI is a projected .4m Head-bone diagnostic box, not drawn head segmentation/acceptance. Inspect PNGs; counters alone do not grant readability or natural contact.";
            public string savePath, shader; public bool sceneBytesSame; public VisualShot[] shots;
        }
        [UnityTest]
        public IEnumerator Visual_FormalRangerProjection_FrontBackAndDarkBackdropFourColourConcurrency()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var hashes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(path => new[] { path, path + ".meta" }).ToDictionary(path => path, Hash);
            string directory = Path.GetFullPath("Builds/ArtReview/rune-pulse/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var shots = new List<VisualShot>(); var images = new List<KeyValuePair<string, Texture2D>>();
            Camera camera = null; ThirdPersonCameraRig rig = null; bool rigEnabled = false;
            Vector3 priorPosition = default(Vector3); Quaternion priorRotation = default(Quaternion);
            bool cameraSnapshotSaved = false;
            Mesh backdropMesh = null; Material backdropMaterial = null; GameObject backdrop = null;
            string savePath = null;
            try
            {
                yield return LoadValley();
                var player = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(player, Is.Not.Null);
                var animator = player.GetComponentInChildren<Animator>(true); Assert.That(animator != null && animator.isHuman, Is.True);
                Assert.That(animator.avatar.name.IndexOf("Ranger", StringComparison.OrdinalIgnoreCase), Is.GreaterThanOrEqualTo(0), "Production Ranger must remain the tested model.");
                camera = Camera.main; Assert.That(camera, Is.Not.Null);
                priorPosition = camera.transform.position; priorRotation = camera.transform.rotation; cameraSnapshotSaved = true;
                rig = camera.GetComponent<ThirdPersonCameraRig>(); Assert.That(rig, Is.Not.Null);
                rigEnabled = rig.enabled;
                savePath = Object.FindObjectOfType<M2RouteFlowController>().SavePath;
                Vector3 actorPosition = player.transform.position;
                Color[] colors = { Color.white, new Color(.28f, 1f, .45f), new Color(.25f, .95f, 1f), new Color(.85f, .28f, 1f) };
                for (int view = 0; view < 4; view++)
                {
                    bool front = (view & 1) != 0, dark = view >= 2;
                    int settledFrames = 0; float cameraDeltaMeters = 0f, cameraDeltaDegrees = 0f;
                    if (front)
                    {
                        rig.enabled = false; // Only front is explicitly controlled, never the rear production view.
                        Vector3 frontPoint = player.AimPoint.position;
                        camera.transform.position = frontPoint + player.transform.forward * 5.8f + Vector3.up * 1.45f;
                        camera.transform.LookAt(frontPoint - Vector3.up * .25f);
                    }
                    else
                    {
                        rig.enabled = true;
                        Vector3 lastPosition = camera.transform.position; Quaternion lastRotation = camera.transform.rotation;
                        float settleDeadline = Time.realtimeSinceStartup + 3f;
                        while (settledFrames < 3 && Time.realtimeSinceStartup < settleDeadline)
                        {
                            yield return new WaitForEndOfFrame(); // Observe the real enabled Rig's LateUpdate, no manual invocation.
                            cameraDeltaMeters = Vector3.Distance(camera.transform.position, lastPosition);
                            cameraDeltaDegrees = Quaternion.Angle(camera.transform.rotation, lastRotation);
                            settledFrames = cameraDeltaMeters <= .002f && cameraDeltaDegrees <= .05f ? settledFrames + 1 : 0;
                            lastPosition = camera.transform.position; lastRotation = camera.transform.rotation;
                        }
                        Assert.That(rig.enabled && settledFrames >= 3, Is.True, "Rear production Rig did not settle; retain failure rather than force a pose.");
                    }
                    Vector3 point = player.AimPoint.position;
                    if (dark)
                    {
                        backdrop = new GameObject("RunePulseDarkBackdropFixture"); _owned.Add(backdrop);
                        SceneManager.MoveGameObjectToScene(backdrop, player.gameObject.scene);
                        backdrop.transform.SetPositionAndRotation(point + camera.transform.forward * .8f, camera.transform.rotation);
                        backdropMesh = new Mesh { name = "RunePulseDarkBackdropMesh" };
                        backdropMesh.vertices = new[] { new Vector3(-1.2f, -1.2f, 0), new Vector3(1.2f, -1.2f, 0), new Vector3(-1.2f, 1.2f, 0), new Vector3(1.2f, 1.2f, 0) };
                        backdropMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
                        backdropMesh.uv = Enumerable.Repeat(new Vector2(.5f, .5f), 4).ToArray();
                        backdropMesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); backdropMesh.RecalculateBounds();
                        backdropMaterial = new Material(Shader.Find(RuneBlessingImpact.ShaderName)) { renderQueue = 2999 };
                        backdropMaterial.SetColor("_EdgeColor", Color.black); backdropMaterial.SetColor("_CoreColor", Color.black);
                        backdrop.AddComponent<MeshFilter>().sharedMesh = backdropMesh;
                        var backdropRenderer = backdrop.AddComponent<MeshRenderer>(); backdropRenderer.sharedMaterial = backdropMaterial;
                        backdropRenderer.shadowCastingMode = ShadowCastingMode.Off; backdropRenderer.receiveShadows = false;
                        Assert.That(backdrop.GetComponentsInChildren<Collider>(), Is.Empty);
                    }
                    var pulses = colors.Select(color => Pulse(player.gameObject.scene, color, point, camera)).ToArray();
                    yield return new WaitForSeconds(.12f); yield return new WaitForEndOfFrame();
                    Assert.That(pulses.All(pulse => pulse != null), Is.True);
                    foreach (var pulse in pulses)
                    {
                        Assert.That(pulse.BirthPosition, Is.EqualTo(point));
                        Assert.That(pulse.BirthCameraId, Is.EqualTo(camera.GetInstanceID()));
                        Assert.That(pulse.transform.IsChildOf(animator.transform), Is.False);
                    }
                    var renderers = pulses.Select(pulse => pulse.GetComponent<MeshRenderer>()).ToArray();
                    string stem = (front ? "front" : "back") + (dark ? "-dark" : "-scene");
                    var row = new VisualShot { name = stem, background = dark ? "temporary black zero-physics backdrop" : "unchanged scene",
                        cameraPoseSource = front ? "controlled front fixture pose" : "enabled production ThirdPersonCameraRig real LateUpdate rear",
                        rigEnabled = rig.enabled, rigSettledFrames = settledFrames,
                        lastCameraFrameDeltaMeters = cameraDeltaMeters, lastCameraFrameDeltaDegrees = cameraDeltaDegrees,
                        frame = Time.frameCount, time = Time.time, playerPosition = player.transform.position,
                        cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation, fieldOfView = camera.fieldOfView, productionAspect = camera.aspect,
                        elapsed = pulses.Select(pulse => pulse.ElapsedSeconds).ToArray(), alpha = pulses.Select(pulse => pulse.CurrentAlpha).ToArray(),
                        sortingOrder = renderers.Select(renderer => renderer.sortingOrder).ToArray(), birthCameraIds = pulses.Select(pulse => pulse.BirthCameraId).ToArray() };
                    foreach (var renderer in renderers) renderer.enabled = false;
                    Texture2D before;
                    try { before = Render(camera); }
                    finally { foreach (var renderer in renderers) if (renderer != null) renderer.enabled = true; }
                    images.Add(new KeyValuePair<string, Texture2D>(stem + "-baseline.png", before));
                    var after = Render(camera);
                    images.Add(new KeyValuePair<string, Texture2D>(stem + "-burst.png", after));
                    row.width = after.width; row.height = after.height; row.captureAspect = (float)row.width / row.height;
                    var head = animator.GetBoneTransform(HumanBodyBones.Head); Assert.That(head, Is.Not.Null);
                    row.diagnosticHeadBoneRoi = ProjectBounds(camera, new Bounds(head.position, Vector3.one * .4f), row.width, row.height);
                    Measure(before, after, row); shots.Add(row);
                    Assert.That(row.changedPixels, Is.GreaterThan(0), "No rendered VFX pixels; this is not readability acceptance.");
                    Assert.That(row.sortingOrder, Is.EqualTo(new[] { 0, 1, 2, 3 }));
                    Assert.That(Vector3.Distance(player.transform.position, actorPosition), Is.LessThan(.001f), "No fixture actor movement or warp allowed.");
                    if (front && !dark)
                    {
                        // One actual late PlayerLoop sample, not a seek/manual elapsed override.
                        while (pulses.All(pulse => pulse != null) && pulses.Min(pulse => pulse.ElapsedSeconds) < .30f)
                            yield return new WaitForEndOfFrame();
                        Assert.That(pulses.All(pulse => pulse != null), Is.True, "Retain a missed late capture rather than revive expired pulses.");
                        var tail = new VisualShot { name = "front-scene-tail", background = "unchanged scene",
                            cameraPoseSource = "controlled front fixture pose", rigEnabled = rig.enabled,
                            frame = Time.frameCount, time = Time.time, playerPosition = player.transform.position,
                            cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                            fieldOfView = camera.fieldOfView, productionAspect = camera.aspect,
                            elapsed = pulses.Select(pulse => pulse.ElapsedSeconds).ToArray(), alpha = pulses.Select(pulse => pulse.CurrentAlpha).ToArray(),
                            sortingOrder = renderers.Select(renderer => renderer.sortingOrder).ToArray(), birthCameraIds = pulses.Select(pulse => pulse.BirthCameraId).ToArray() };
                        Assert.That(tail.elapsed.All(elapsed => elapsed >= .30f && elapsed < RuneBlessingImpact.LifetimeSeconds), Is.True);
                        for (int i = 0; i < pulses.Length; i++) Assert.That(tail.alpha[i], Is.LessThan(row.alpha[i]));
                        foreach (var renderer in renderers) renderer.enabled = false;
                        Texture2D tailBefore;
                        try { tailBefore = Render(camera); }
                        finally { foreach (var renderer in renderers) if (renderer != null) renderer.enabled = true; }
                        images.Add(new KeyValuePair<string, Texture2D>(tail.name + "-baseline.png", tailBefore));
                        var tailAfter = Render(camera);
                        images.Add(new KeyValuePair<string, Texture2D>(tail.name + "-burst.png", tailAfter));
                        tail.width = tailAfter.width; tail.height = tailAfter.height; tail.captureAspect = (float)tail.width / tail.height;
                        tail.diagnosticHeadBoneRoi = ProjectBounds(camera, new Bounds(head.position, Vector3.one * .4f), tail.width, tail.height);
                        Measure(tailBefore, tailAfter, tail); shots.Add(tail);
                        Assert.That(tail.changedPixels, Is.GreaterThan(0), "A still-live late pulse must render, not vanish abruptly.");
                    }
                    yield return new WaitForSeconds(.5f);
                    Assert.That(pulses.All(pulse => pulse == null), Is.True);
                    if (backdrop != null) Object.Destroy(backdrop);
                    if (backdropMesh != null) Object.Destroy(backdropMesh);
                    if (backdropMaterial != null) Object.Destroy(backdropMaterial);
                    backdrop = null; backdropMesh = null; backdropMaterial = null;
                    yield return null;
                }
                Assert.That(hashes.All(pair => Hash(pair.Key) == pair.Value), Is.True);
            }
            finally
            {
                if (cameraSnapshotSaved && camera != null) camera.transform.SetPositionAndRotation(priorPosition, priorRotation);
                if (rig != null) rig.enabled = rigEnabled;
                if (backdrop != null) Object.Destroy(backdrop);
                if (backdropMesh != null) Object.Destroy(backdropMesh);
                if (backdropMaterial != null) Object.Destroy(backdropMaterial);
                // A failed fixture terminates its own outstanding clocks before expensive encoding.
                // Success reaches here only after the per-view natural-expiry checks above.
                foreach (var root in _owned)
                    if (root != null && root.GetComponent<RuneBlessingImpact>() != null)
                    { root.SetActive(false); Object.Destroy(root); }
                try
                {
                    foreach (var image in images) File.WriteAllBytes(Path.Combine(directory, image.Key), image.Value.EncodeToPNG());
                    File.WriteAllText(directory + "/review.json", JsonUtility.ToJson(new VisualEvidence { savePath = savePath,
                        shader = RuneBlessingImpact.ShaderName, shots = shots.ToArray(), sceneBytesSame = hashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value) }, true));
                }
                finally { foreach (var image in images) if (image.Value != null) Object.Destroy(image.Value); }
            }
        }

        static Texture2D Render(Camera camera)
        {
            const int width = 1280;
            int height = Mathf.Max(1, Mathf.RoundToInt(width / camera.aspect));
            var target = new RenderTexture(width, height, 24); var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); return texture;
            }
            catch { Object.Destroy(texture); throw; }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); }
        }
        static Rect ProjectBounds(Camera camera, Bounds bounds, int width, int height)
        {
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue), maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                Assert.That(point.z, Is.GreaterThan(0)); Vector2 pixel = new Vector2(point.x * width, point.y * height);
                minimum = Vector2.Min(minimum, pixel); maximum = Vector2.Max(maximum, pixel);
            }
            return Rect.MinMaxRect(Mathf.Clamp(minimum.x, 0, width), Mathf.Clamp(minimum.y, 0, height),
                Mathf.Clamp(maximum.x, 0, width), Mathf.Clamp(maximum.y, 0, height));
        }
        static void Measure(Texture2D before, Texture2D after, VisualShot row)
        {
            var a = before.GetPixels32(); var b = after.GetPixels32(); double difference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int dr = Math.Abs(a[i].r - b[i].r), dg = Math.Abs(a[i].g - b[i].g), db = Math.Abs(a[i].b - b[i].b);
                bool changed = Math.Max(dr, Math.Max(dg, db)) > 3;
                if (changed) row.changedPixels++;
                difference += Math.Abs((b[i].r - a[i].r) * .2126 + (b[i].g - a[i].g) * .7152 + (b[i].b - a[i].b) * .0722) / 255;
                if (row.diagnosticHeadBoneRoi.Contains(new Vector2(i % row.width + .5f, i / row.width + .5f)))
                { row.diagnosticHeadRoiPixels++; if (changed) row.diagnosticHeadChangedPixels++; }
            }
            row.meanAbsoluteLuminanceDifference = (float)(difference / a.Length);
            row.diagnosticHeadCoverage = row.diagnosticHeadRoiPixels > 0 ? (float)row.diagnosticHeadChangedPixels / row.diagnosticHeadRoiPixels : 0f;
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
#endif
    }
}
