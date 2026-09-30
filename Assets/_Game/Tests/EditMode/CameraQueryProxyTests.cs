using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class CameraQueryProxyTests
    {
        GameObject root;
        CameraQueryProxy proxy;
        Renderer renderer;

        [SetUp] public void SetUp()
        {
            root = new GameObject("QueryOnly");
            renderer = root.AddComponent<MeshRenderer>();
            proxy = root.AddComponent<CameraQueryProxy>();
            proxy.Configure(renderer, new[] { new Bounds(Vector3.zero, Vector3.one) });
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(root); }

        [Test] public void Outside_HitsExpandedSurface()
        {
            Assert.That(proxy.TryGetDistance(new Ray(new Vector3(0, 0, -3), Vector3.forward), 5, .2f, out var d), Is.True);
            Assert.That(d, Is.EqualTo(2.3f).Within(.0001f));
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
        }
        [Test] public void Inside_RemainsBlocked_NotGloballyIgnored()
        {
            Assert.That(proxy.TryGetDistance(new Ray(Vector3.zero, Vector3.forward), 5, .2f, out var d), Is.True);
            Assert.That(d, Is.Zero);
        }
        [Test] public void EmptyGap_InOverallBounds_DoesNotBlock()
        {
            proxy.Configure(renderer, new[] { new Bounds(Vector3.left * 2, Vector3.one), new Bounds(Vector3.right * 2, Vector3.one) });
            Assert.That(proxy.TryGetDistance(new Ray(Vector3.zero, Vector3.forward), 5, .2f, out _), Is.False);
        }
        [Test] public void BeyondMaximum_DoesNotBlock()
        { Assert.That(proxy.TryGetDistance(new Ray(new Vector3(0, 0, -3), Vector3.forward), 2, .2f, out _), Is.False); }
        [Test] public void InactiveProxy_IsIgnored()
        { root.SetActive(false); Assert.That(proxy.TryGetDistance(new Ray(Vector3.zero, Vector3.forward), 5, .2f, out _), Is.False); }
        [Test] public void DisabledRenderer_IsIgnored()
        { renderer.enabled = false; Assert.That(proxy.TryGetDistance(new Ray(Vector3.zero, Vector3.forward), 5, .2f, out _), Is.False); }
        [Test] public void DisabledProxy_IsIgnored()
        { proxy.enabled = false; Assert.That(proxy.TryGetDistance(new Ray(Vector3.zero, Vector3.forward), 5, .2f, out _), Is.False); }
        [Test] public void RotatedScaledProxy_ReturnsWorldDistance()
        {
            root.transform.SetPositionAndRotation(new Vector3(7, 2, 1), Quaternion.Euler(0, 90, 0));
            root.transform.localScale = Vector3.one * 2;
            var ray = new Ray(root.transform.TransformPoint(new Vector3(0, 0, -3)), root.transform.forward);
            Assert.That(proxy.TryGetDistance(ray, 10, .2f, out var d), Is.True);
            Assert.That(d, Is.EqualTo(4.8f).Within(.0001f));
        }
        [Test] public void Occluder_UsesExplicitProxy_WithoutCollider()
        {
            var occluder = root.AddComponent<CameraOccluder>();
            occluder.SetQueryProxy(proxy);
            Assert.That(occluder.TryGetDistance(new Ray(new Vector3(0, 0, -3), Vector3.forward), 5, .2f, out var d), Is.True);
            Assert.That(d, Is.EqualTo(2.3f).Within(.0001f));
            Assert.That(root.GetComponents<Collider>(), Is.Empty);
        }
        [Test, Explicit("Known independent inactive legacy-AABB defect; Harness forbids fixing it in the single-tree slice.")]
        public void LegacyInactiveOccluder_ShouldBeIgnored_PendingSeparateApproval()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var legacy = cube.AddComponent<CameraOccluder>();
                InitializeLegacy(legacy);
                cube.SetActive(false);
                Assert.That(legacy.TryGetDistance(new Ray(new Vector3(0, 0, -3), Vector3.forward), 5, .2f, out _), Is.False);
            }
            finally { Object.DestroyImmediate(cube); }
        }

        [Test] public void LegacyOccluder_StillBlocksOutsideAndInside()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var legacy = cube.AddComponent<CameraOccluder>(); InitializeLegacy(legacy);
                Assert.That(legacy.TryGetDistance(new Ray(new Vector3(0,0,-3),Vector3.forward),5,.2f,out var outside),Is.True);
                Assert.That(outside,Is.EqualTo(2.3f).Within(.0001f));
                Assert.That(legacy.TryGetDistance(new Ray(Vector3.zero,Vector3.forward),5,.2f,out var inside),Is.True);
                Assert.That(inside,Is.Zero);
            }
            finally { Object.DestroyImmediate(cube); }
        }

        [Test] public void Baking_IsDeterministicAndKeepsSeparatedTrianglesGap()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] { new Vector3(-2,0,0),new Vector3(-2,1,0),new Vector3(-2,0,1),
                    new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(2,0,1) };
                mesh.triangles = new[] {0,1,2,3,4,5};
                var cells = Emberfall.Editor.Setup.BridgeCameraProxySetup.BuildCells(mesh,.12f);
                Assert.That(cells,Is.EqualTo(Emberfall.Editor.Setup.BridgeCameraProxySetup.BuildCells(mesh,.12f)));
                Assert.That(cells,Is.Not.Empty);
                proxy.Configure(renderer,cells);
                Assert.That(proxy.TryGetDistance(new Ray(new Vector3(0,.3f,-2),Vector3.forward),5,.22f,out _),Is.False);
                Assert.That(proxy.TryGetDistance(new Ray(new Vector3(-2,.3f,-2),Vector3.forward),5,.22f,out _),Is.True);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        static void InitializeLegacy(CameraOccluder occluder)
        {
            // EditMode does not run MonoBehaviour.Awake; populate only its existing serialized renderer cache.
            var serialized = new UnityEditor.SerializedObject(occluder);
            var renderers = serialized.FindProperty("_renderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = occluder.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
