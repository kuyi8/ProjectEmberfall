using System;
using System.Collections;
using System.Reflection;
using Emberfall.AI.Domain;
using Emberfall.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Client-local geometry/lifecycle only; no Server, replicated-clock or natural-combat acceptance.</summary>
    public sealed class NetworkGroundRuneBoundaryPresentationTests
    {
        private Scene _scene;
        private float _previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            _scene = SceneManager.CreateScene("NetworkGroundRuneBoundaryTest_" + Guid.NewGuid());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _previousTimeScale;
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoundaryIsFixedWorldRadiusWhileLegacyCylinderGrowsAndRotates()
        {
            const float radius = 2.55f;
            NetworkEnemyAttackPresentation presentation = SpawnGroundRune(radius, 3f);
            Renderer boundary = presentation.WarningBoundaryRenderer;
            Assert.That(boundary, Is.Not.Null);
            Mesh mesh = boundary.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(presentation.WarningRadius, Is.EqualTo(radius));
            Assert.That(presentation.transform.localScale.x, Is.EqualTo(radius * 2f * .25f).Within(.00001f));
            Assert.That(boundary.transform.parent, Is.Null, "The fixed ring cannot inherit the animated cylinder scale.");
            Assert.That(boundary.gameObject.scene, Is.EqualTo(_scene));
            Assert.That(boundary.transform.lossyScale, Is.EqualTo(Vector3.one));
            float initialScale = presentation.transform.localScale.x;
            for (int frame = 0; frame < 4; frame++)
            {
                Assert.That(presentation.IsWarningBoundaryVisible, Is.True);
                Assert.That(boundary.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                AssertWorldOuterRadius(presentation, boundary, mesh, radius);
                yield return null;
                Assert.That(presentation.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(boundary.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            Assert.That(presentation.transform.localScale.x, Is.GreaterThan(initialScale));
            Assert.That(presentation.transform.localScale.x, Is.LessThanOrEqualTo(radius * 2f));
            AssertWorldOuterRadius(presentation, boundary, mesh, radius);
        }

        [UnityTest]
        public IEnumerator FullLocalFuseDestroysCylinderBoundaryAndOwnedMeshWithoutOrphanRoots()
        {
            NetworkEnemyAttackPresentation presentation = SpawnGroundRune(2.55f, .8f);
            GameObject cylinder = presentation.gameObject;
            GameObject boundaryRoot = presentation.WarningBoundaryRenderer.gameObject;
            Mesh ownedMesh = presentation.WarningBoundaryRenderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(presentation.IsWarningBoundaryVisible, Is.True);
            float deadline = Time.time + 2f;
            while (presentation != null && Time.time < deadline)
            {
                if (ReadRemaining(presentation) > 0f)
                {
                    Assert.That(presentation.IsWarningBoundaryVisible, Is.True);
                }
                else
                {
                    // Update may have scheduled Destroy before end-of-frame native cleanup.
                    Assert.That(presentation.IsWarningBoundaryVisible, Is.False);
                    Assert.That(presentation.WarningBoundaryRenderer.enabled, Is.False);
                    Assert.That(presentation.GetComponent<Renderer>().enabled, Is.False);
                }
                yield return null;
            }
            Assert.That(presentation == null, Is.True, "The existing local fuse must expire through real Update.");
            yield return null;
            yield return null;
            Assert.That(cylinder == null, Is.True);
            Assert.That(boundaryRoot == null, Is.True, "Independent boundary ownership must still end with the cylinder.");
            Assert.That(ownedMesh == null, Is.True);
            Assert.That(_scene.GetRootGameObjects(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DisableHidesBothVisualsPausesExistingLocalClockAndReenableReusesBoundary()
        {
            NetworkEnemyAttackPresentation presentation = SpawnGroundRune(1.2f, 3f);
            Renderer cylinder = presentation.GetComponent<Renderer>();
            Renderer boundary = presentation.WarningBoundaryRenderer;
            Mesh mesh = boundary.GetComponent<MeshFilter>().sharedMesh;
            float remaining = ReadRemaining(presentation);
            presentation.enabled = false;
            Assert.That(presentation.IsWarningBoundaryVisible, Is.False);
            Assert.That(boundary.enabled, Is.False);
            Assert.That(cylinder.enabled, Is.False);
            yield return new WaitForSeconds(.2f);
            Assert.That(ReadRemaining(presentation), Is.EqualTo(remaining));
            presentation.enabled = true;
            Assert.That(presentation.IsWarningBoundaryVisible, Is.True);
            Assert.That(boundary.enabled, Is.True);
            Assert.That(cylinder.enabled, Is.True);
            Assert.That(boundary.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            AssertWorldOuterRadius(presentation, boundary, mesh, 1.2f);
            yield return null;
            Assert.That(ReadRemaining(presentation), Is.LessThan(remaining));
        }

        [UnityTest]
        public IEnumerator EarlyCylinderDestructionAlsoDisposesIndependentBoundaryAndMesh()
        {
            NetworkEnemyAttackPresentation presentation = SpawnGroundRune(2.55f, 3f);
            GameObject cylinder = presentation.gameObject;
            GameObject boundaryRoot = presentation.WarningBoundaryRenderer.gameObject;
            Mesh ownedMesh = presentation.WarningBoundaryRenderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(presentation.IsWarningBoundaryVisible, Is.True);
            Object.Destroy(cylinder);
            yield return null;
            yield return null;
            Assert.That(presentation == null, Is.True);
            Assert.That(cylinder == null, Is.True);
            Assert.That(boundaryRoot == null, Is.True);
            Assert.That(ownedMesh == null, Is.True);
            Assert.That(_scene.GetRootGameObjects(), Is.Empty);
        }

        private NetworkEnemyAttackPresentation SpawnGroundRune(float radius, float localFuse)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            try
            {
                Assert.That(SceneManager.SetActiveScene(_scene), Is.True);
                NetworkEnemyAttackPresentation.Spawn(RangedAttackKind.GroundRune,
                    new Vector3(10000f, .04f, 10000f), new Vector3(radius, 0f, 0f), 0f, 0f, localFuse, null);
                NetworkEnemyAttackPresentation result = null;
                foreach (GameObject root in _scene.GetRootGameObjects())
                {
                    NetworkEnemyAttackPresentation candidate = root.GetComponent<NetworkEnemyAttackPresentation>();
                    if (candidate == null) continue;
                    Assert.That(result, Is.Null, "An isolated Spawn must create exactly one attack presentation.");
                    result = candidate;
                }
                Assert.That(result, Is.Not.Null);
                return result;
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static float ReadRemaining(NetworkEnemyAttackPresentation presentation)
        {
            FieldInfo field = typeof(NetworkEnemyAttackPresentation).GetField("_remaining",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Observe the real existing local clock; do not invent a test clock.");
            return (float)field.GetValue(presentation);
        }

        private static void AssertWorldOuterRadius(NetworkEnemyAttackPresentation presentation,
            Renderer boundary, Mesh mesh, float radius)
        {
            Vector3[] vertices = mesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo(130));
            Assert.That(mesh.triangles.Length, Is.EqualTo(384));
            for (int i = 1; i < vertices.Length; i += 2)
            {
                Vector3 delta = boundary.transform.TransformPoint(vertices[i]) - presentation.transform.position;
                Assert.That(new Vector2(delta.x, delta.z).magnitude, Is.EqualTo(radius).Within(.002f));
            }
        }
    }
}
