using System;
using System.Collections;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Isolated real-PlayerLoop lifecycle contracts, not rendered/natural-combat acceptance.</summary>
    public sealed class GroundRuneBoundaryPresentationTests
    {
        private Scene _scene;
        private float _previousTimeScale;
        private Material _authoredMaterial;

        [SetUp]
        public void SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("GroundRuneBoundaryTest_" + Guid.NewGuid());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _previousTimeScale;
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
            if (_authoredMaterial != null) Object.Destroy(_authoredMaterial);
            _authoredMaterial = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarningOuterEdgeStaysAtQueryRadiusAcrossFramesAndNonuniformRootScale()
        {
            RangedGroundRune rune = NewRune("ConstantRadiusRune");
            rune.transform.localScale = new Vector3(2f, 1f, .5f);
            rune.transform.rotation = Quaternion.Euler(0f, 29f, 0f);
            rune.Configure(1, 1, 4f, 2.55f, 10f, 10f, null, false, _ => { });
            Renderer boundary = rune.WarningBoundaryRenderer;
            Mesh mesh = boundary.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10));
            Assert.That(rune.WarningRadius, Is.EqualTo(2.55f));
            Assert.That(rune.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(11));
            for (int frame = 0; frame < 4; frame++)
            {
                Assert.That(rune.IsWarningBoundaryVisible, Is.True);
                Assert.That(boundary.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                AssertWorldOuterRadius(rune, mesh, boundary.transform, 2.55f);
                yield return null;
                Assert.That(rune.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "The ten legacy primitives and new boundary must not retain physical colliders.");
            }
        }

        [UnityTest]
        public IEnumerator ResolveHidesAllDangerBeforeSingleIgnoredCallbackAndDestroysOwnedResources()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            _authoredMaterial = new Material(shader);
            RangedGroundRune rune = NewRune("ShortFuseRune");
            int callbacks = 0;
            bool hiddenAtCallback = false;
            int rendererCountAtCallback = 0;
            DamageResult observed = default;
            rune.Configure(1, 2, .05f, 2.55f, 10f, 10f, _authoredMaterial, false, result =>
            {
                callbacks++;
                observed = result;
                Renderer[] renderers = rune.GetComponentsInChildren<Renderer>(true);
                rendererCountAtCallback = renderers.Length;
                hiddenAtCallback = !rune.IsWarningBoundaryVisible;
                foreach (Renderer renderer in renderers) hiddenAtCallback &= !renderer.enabled;
            });
            Renderer boundary = rune.WarningBoundaryRenderer;
            Mesh ownedMesh = boundary.GetComponent<MeshFilter>().sharedMesh;
            Material ownedMaterial = boundary.sharedMaterial;
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            Assert.That(ownedMaterial, Is.Not.SameAs(_authoredMaterial));
            yield return WaitForCallback(() => callbacks);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(DamageResult.Ignored));
            Assert.That(rendererCountAtCallback, Is.EqualTo(11));
            Assert.That(hiddenAtCallback, Is.True,
                "Danger semantics must already be hidden inside the real resolve callback, not one frame later.");
            if (rune != null)
            {
                rune.enabled = false;
                rune.enabled = true;
                Assert.That(rune.IsWarningBoundaryVisible, Is.False, "Resolved warnings must not reappear on re-enable.");
            }
            yield return new WaitForSeconds(.3f);
            yield return null;
            yield return null;
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(rune == null, Is.True);
            Assert.That(ownedMesh == null, Is.True, "Runtime boundary Mesh must be destroyed with its rune.");
            Assert.That(ownedMaterial == null, Is.True, "Runtime warning Material must be destroyed with its rune.");
            Assert.That(_authoredMaterial != null, Is.True, "Cleanup must not destroy the authored source material.");
        }

        [UnityTest]
        public IEnumerator DisableHidesDangerAndReenableRestoresOnlyUnresolvedWarningWithoutChangingMesh()
        {
            RangedGroundRune rune = NewRune("DisabledWarningRune");
            rune.Configure(1, 3, 4f, 1.2f, 10f, 10f, null, false, _ => { });
            Mesh mesh = rune.WarningBoundaryRenderer.GetComponent<MeshFilter>().sharedMesh;
            rune.enabled = false;
            Assert.That(rune.IsWarningBoundaryVisible, Is.False);
            foreach (Renderer renderer in rune.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.False);
            yield return null;
            rune.enabled = true;
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            Assert.That(rune.WarningBoundaryRenderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10));
            foreach (Renderer renderer in rune.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator SaturatedDecorativePoolCannotSuppressBoundaryOrActualFuseCallback()
        {
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.GroundBlast), Is.Zero,
                "Fixture requires no leaked GroundBlast leases from another test.");
            GameObject prefab = NewObject("BoundaryTestDecorativePrefab");
            ParticleSystem particles = prefab.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 10f;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            prefab.SetActive(false);
            CombatBurstVfxPool pool = CombatBurstVfxPool.ForScene(_scene);
            for (int i = 0; i < CombatBurstVfxPool.PerKindLimit; i++)
                Assert.That(pool.TrySpawn(CombatBurstKind.GroundBlast, prefab, Vector3.zero, Quaternion.identity, 10f), Is.True);
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.GroundBlast), Is.EqualTo(3));
            RangedGroundRune rune = NewRune("SaturatedWarningRune");
            int callbacks = 0;
            bool hiddenAtCallback = false;
            int dropsAtCallback = 0;
            DamageResult observed = default;
            rune.Configure(1, 4, .8f, 2.55f, 10f, 10f, null, false, result =>
            {
                callbacks++;
                observed = result;
                hiddenAtCallback = !rune.IsWarningBoundaryVisible;
                dropsAtCallback = pool.DroppedCount;
            }, prefab);
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10));
            yield return null;
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            Assert.That(rune.GetComponentsInChildren<Collider>(true), Is.Empty);
            yield return WaitForCallback(() => callbacks);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(DamageResult.Ignored));
            Assert.That(hiddenAtCallback, Is.True);
            Assert.That(dropsAtCallback, Is.EqualTo(1), "Only the saturated decoration is dropped, never the warning or fuse.");
            yield return new WaitForSeconds(.3f);
            yield return null;
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(rune == null, Is.True);
            Assert.That(pool.DroppedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MeshDisposeClearsOwnershipImmediatelyAndNativeMeshAfterFrameBoundary()
        {
            var boundary = new GroundRuneBoundaryMesh();
            Mesh owned = boundary.Mesh;
            try
            {
                boundary.SetRadius(1f);
                boundary.Dispose();
                Assert.That(boundary.Mesh, Is.Null);
                Assert.DoesNotThrow(() => boundary.Dispose());
                Assert.Throws<ObjectDisposedException>(() => boundary.SetRadius(1f));
                yield return null;
                Assert.That(owned == null, Is.True);
            }
            finally { boundary.Dispose(); }
        }

        [UnityTest]
        public IEnumerator DestructionBeforeFuseReleasesOwnedMeshAndMaterialWithoutResolving()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            _authoredMaterial = new Material(shader);
            RangedGroundRune rune = NewRune("ResetBeforeFuseRune");
            int callbacks = 0;
            rune.Configure(1, 5, 4f, 2.55f, 10f, 10f, _authoredMaterial, false, _ => callbacks++);
            Mesh ownedMesh = rune.WarningBoundaryRenderer.GetComponent<MeshFilter>().sharedMesh;
            Material ownedMaterial = rune.WarningBoundaryRenderer.sharedMaterial;
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            Object.Destroy(rune.gameObject);
            yield return null;
            yield return null;
            Assert.That(callbacks, Is.Zero, "Encounter reset/destruction is not a fuse-resolution event.");
            Assert.That(rune == null, Is.True);
            Assert.That(ownedMesh == null, Is.True);
            Assert.That(ownedMaterial == null, Is.True);
            Assert.That(_authoredMaterial != null, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedConfigureIsRejectedBeforeReplacingWarningGeometryMaterialOrFuse()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            _authoredMaterial = new Material(shader);
            RangedGroundRune rune = NewRune("RejectRepeatedConfigureRune");
            int originalCallbacks = 0;
            int rejectedCallbacks = 0;
            rune.Configure(1, 6, 4f, 2.55f, 10f, 10f, _authoredMaterial, false, _ => originalCallbacks++);
            Renderer boundary = rune.WarningBoundaryRenderer;
            Mesh ownedMesh = boundary.GetComponent<MeshFilter>().sharedMesh;
            Material ownedMaterial = boundary.sharedMaterial;
            Vector3[] originalVertices = ownedMesh.vertices;

            InvalidOperationException rejection = Assert.Throws<InvalidOperationException>(() =>
                rune.Configure(91, 99, .05f, 8f, 999f, 999f, null, true, _ => rejectedCallbacks++));
            Assert.That(rejection.Message, Is.EqualTo("A ground rune can only be configured once."));
            Assert.That(rune.WarningBoundaryRenderer, Is.SameAs(boundary));
            Assert.That(boundary.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(ownedMesh));
            Assert.That(boundary.sharedMaterial, Is.SameAs(ownedMaterial));
            CollectionAssert.AreEqual(originalVertices, ownedMesh.vertices);
            Assert.That(rune.WarningRadius, Is.EqualTo(2.55f));
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10));
            Assert.That(rune.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(11));
            Assert.That(rune.IsWarningBoundaryVisible, Is.True);
            yield return null;
            Assert.That(originalCallbacks, Is.Zero, "A rejected configure must not replace the original four-second fuse.");
            Assert.That(rejectedCallbacks, Is.Zero);
            Assert.That(rune.GetComponentsInChildren<Collider>(true), Is.Empty);

            Object.Destroy(rune.gameObject);
            yield return null;
            yield return null;
            Assert.That(rune == null, Is.True);
            Assert.That(ownedMesh == null, Is.True);
            Assert.That(ownedMaterial == null, Is.True);
            Assert.That(_authoredMaterial != null, Is.True);
            Assert.That(originalCallbacks, Is.Zero);
            Assert.That(rejectedCallbacks, Is.Zero);
        }

        private RangedGroundRune NewRune(string name)
        {
            GameObject root = NewObject(name);
            root.transform.position = new Vector3(10000f, 0f, 10000f);
            return root.AddComponent<RangedGroundRune>();
        }

        private GameObject NewObject(string name)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, _scene);
            return root;
        }

        private static IEnumerator WaitForCallback(Func<int> readCount)
        {
            float deadline = Time.time + 2f;
            while (readCount() == 0 && Time.time < deadline) yield return null;
            Assert.That(readCount(), Is.EqualTo(1), "Real Update must resolve the original domain fuse exactly once.");
        }

        private static void AssertWorldOuterRadius(RangedGroundRune rune, Mesh mesh, Transform boundary, float radius)
        {
            Vector3[] vertices = mesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo(130));
            Assert.That(mesh.triangles.Length, Is.EqualTo(384));
            for (int i = 1; i < vertices.Length; i += 2)
            {
                Vector3 delta = boundary.TransformPoint(vertices[i]) - rune.transform.position;
                float planarDistance = new Vector2(delta.x, delta.z).magnitude;
                Assert.That(planarDistance, Is.EqualTo(radius).Within(.002f),
                    "Display radius is in world metres, like the real OverlapSphere query, not inherited root scale.");
            }
        }
    }
}
