#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class RuneBlessingImpactVisualTests
    {
        Scene _scene;
        readonly List<Object> _ownedResources = new List<Object>();
        [SetUp] public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void TearDown()
        {
            try
            {
                if (_scene.IsValid())
                    foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            }
            finally
            {
                try
                {
                    // Non-ExecuteAlways callbacks need not run in this pure Edit preview.
                    // Keep exact references even if a test has already destroyed their root.
                    foreach (var resource in _ownedResources)
                        if (resource != null) Object.DestroyImmediate(resource);
                }
                finally
                {
                    _ownedResources.Clear();
                    if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                }
            }
        }

        RuneBlessingImpact Create(Vector3 point, Color color, Camera camera = null)
        {
            var impact = RuneBlessingImpact.Create(point, color, _scene, camera);
            if (impact != null)
            {
                var filter = impact.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) _ownedResources.Add(filter.sharedMesh);
                var renderer = impact.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null) _ownedResources.Add(renderer.sharedMaterial);
            }
            return impact;
        }

        static readonly Color[] Colors = { Color.white, new Color(.28f, 1f, .45f),
            new Color(.25f, .95f, 1f), new Color(.85f, .28f, 1f) };

        [Test]
        public void Geometry_HasOpenCentreAndSixOuterSparks_InsideOriginalSphereEnvelope()
        {
            var impact = Create(Vector3.zero, Color.white);
            Assert.That(impact, Is.Not.Null);
            var mesh = impact.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo((32 + 6) * 4));
            Assert.That(mesh.triangles.Length, Is.EqualTo((32 + 6) * 6));
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            foreach (Vector3 vertex in vertices)
            {
                Assert.That(vertex.z, Is.Zero);
                Assert.That(vertex.magnitude, Is.LessThanOrEqualTo(.5f));
                Assert.That(vertex.magnitude * RuneBlessingImpact.FinalScale, Is.LessThanOrEqualTo(.575f));
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Assert.That(ContainsOrigin(a, b, c), Is.False, "A pulse triangle covers the centre.");
                Assert.That(Mathf.Abs(Cross(b - a, c - a)), Is.GreaterThan(.000001f));
            }
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void Material_PreservesCallerRgbAndUsesExistingFixedAlphaShader(int colorIndex)
        {
            Shader shader = Shader.Find(RuneBlessingImpact.ShaderName);
            Assert.That(shader, Is.Not.Null);
            string before = EditorJsonUtility.ToJson(shader);
            var impact = Create(new Vector3(3, 4, 5), Colors[colorIndex]);
            Assert.That(impact, Is.Not.Null);
            var renderer = impact.GetComponent<MeshRenderer>(); var material = renderer.sharedMaterial;
            Assert.That(material.shader, Is.SameAs(shader));
            Assert.That(material.renderQueue, Is.EqualTo(3000));
            Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
            Assert.That(material.GetColor("_EdgeColor"), Is.EqualTo(Colors[colorIndex]));
            Assert.That(material.GetColor("_CoreColor"), Is.EqualTo(Colors[colorIndex]));
            Assert.That(renderer.sortingOrder, Is.EqualTo(colorIndex));
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            Assert.That(EditorJsonUtility.ToJson(shader), Is.EqualTo(before));
            string source = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(shader));
            StringAssert.Contains("Blend SrcAlpha OneMinusSrcAlpha", source);
            StringAssert.Contains("ZWrite Off", source); StringAssert.Contains("Cull Off", source);
            Assert.That(material.HasProperty("_Surface"), Is.False, "This is not the URP Lit surface API.");
        }

        [Test]
        public void Birth_HasNoPhysicsOrActorParent_AndSamplesCameraOrientationOnlyOnce()
        {
            var cameraObject = new GameObject("RunePulseEditCamera") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, _scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(12, 73, 0);
            Vector3 point = new Vector3(6, 2, -3);
            var impact = Create(point, Colors[2], camera);
            Assert.That(impact, Is.Not.Null);
            Assert.That(impact.transform.parent, Is.Null); Assert.That(impact.gameObject.scene, Is.EqualTo(_scene));
            Assert.That(impact.gameObject.layer, Is.EqualTo(2));
            Assert.That(impact.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(impact.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(impact.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(impact.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            Assert.That(impact.BirthPosition, Is.EqualTo(point));
            Assert.That(Quaternion.Angle(impact.BirthRotation, camera.transform.rotation), Is.LessThan(.001f));
            Assert.That(impact.BirthCameraId, Is.EqualTo(camera.GetInstanceID()));
            camera.transform.SetPositionAndRotation(Vector3.one * 50, Quaternion.Euler(0, 180, 0));
            SetElapsedAndApply(impact, .2f);
            Assert.That(impact.transform.position, Is.EqualTo(point));
            Assert.That(Quaternion.Angle(impact.transform.rotation, impact.BirthRotation), Is.LessThan(.001f));
        }

        [Test]
        public void Alpha_GraduallyFadesReusedVertexBuffer_WithUnchangedMaterialRgb()
        {
            var impact = Create(Vector3.zero, Colors[1]);
            var material = impact.GetComponent<MeshRenderer>().sharedMaterial;
            Color edgeBefore = material.GetColor("_EdgeColor");
            var field = typeof(RuneBlessingImpact).GetField("_colors", BindingFlags.Instance | BindingFlags.NonPublic);
            object buffer = field.GetValue(impact); float previous = impact.CurrentAlpha;
            Assert.That(previous, Is.EqualTo(.65f).Within(1f / 255f));
            foreach (float elapsed in new[] { .1f, .2f, .3f, .41f, .42f })
            {
                SetElapsedAndApply(impact, elapsed);
                Assert.That(impact.CurrentAlpha, Is.LessThan(previous)); previous = impact.CurrentAlpha;
                Assert.That(field.GetValue(impact), Is.SameAs(buffer));
                Assert.That(material.GetColor("_EdgeColor"), Is.EqualTo(edgeBefore));
                Assert.That(material.GetColor("_CoreColor"), Is.EqualTo(edgeBefore));
                var colors = impact.GetComponent<MeshFilter>().sharedMesh.colors32;
                foreach (var color in colors) Assert.That(color.a / 255f, Is.EqualTo(impact.CurrentAlpha));
            }
            Assert.That(impact.CurrentAlpha, Is.Zero);
            Assert.That(impact.transform.localScale.x, Is.EqualTo(RuneBlessingImpact.FinalScale));
        }

        [Test]
        public void InstanceResources_ExplicitReleaseIsIdempotentAndReleasesOnlyTheOwner()
        {
            var a = Create(Vector3.zero, Colors[0]);
            var b = Create(Vector3.zero, Colors[0]);
            var aMesh = a.GetComponent<MeshFilter>().sharedMesh; var aMaterial = a.GetComponent<MeshRenderer>().sharedMaterial;
            var bMesh = b.GetComponent<MeshFilter>().sharedMesh; var bMaterial = b.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(aMesh, Is.Not.SameAs(bMesh)); Assert.That(aMaterial, Is.Not.SameAs(bMaterial));
            // Test the real cleanup body explicitly; automatic OnDestroy belongs to unchanged Play tests.
            var release = typeof(RuneBlessingImpact).GetMethod("ReleaseOwned", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(release, Is.Not.Null); release.Invoke(a, null);
            Assert.That(aMesh == null && aMaterial == null, Is.True);
            Assert.That(bMesh != null && bMaterial != null, Is.True);
            Assert.That(a.GetComponent<MeshFilter>().sharedMesh, Is.Null);
            Assert.That(a.GetComponent<MeshRenderer>().sharedMaterial, Is.Null);
            Assert.DoesNotThrow(() => release.Invoke(a, null), "Explicit cleanup must also be idempotent.");
            Assert.That(aMesh == null && aMaterial == null, Is.True);
            Assert.That(bMesh != null && bMaterial != null, Is.True);
            Object.DestroyImmediate(a.gameObject);
        }

        [Test]
        public void InvalidConfigure_ReleasesAnyOwnedResourcesAndDoesNotCreatePhysics()
        {
            var root = new GameObject("InvalidRunePulse") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, _scene);
            var component = root.AddComponent<RuneBlessingImpact>();
            var configure = typeof(RuneBlessingImpact).GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => configure.Invoke(component, new object[] { null, Color.white, null }));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentNullException>());
            Assert.That(component.GetComponent<MeshFilter>(), Is.Null);
            Assert.That(component.GetComponent<Collider>(), Is.Null);
            Assert.That(typeof(RuneBlessingImpact).GetField("_mesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component), Is.Null);
            Assert.That(typeof(RuneBlessingImpact).GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component), Is.Null);
        }

        static void SetElapsedAndApply(RuneBlessingImpact impact, float elapsed)
        {
            typeof(RuneBlessingImpact).GetField("_elapsed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(impact, elapsed);
            typeof(RuneBlessingImpact).GetMethod("ApplyVisual", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(impact, null);
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool ContainsOrigin(Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(b - a, -a), bc = Cross(c - b, -b), ca = Cross(a - c, -c);
            return ab >= 0 && bc >= 0 && ca >= 0 || ab <= 0 && bc <= 0 && ca <= 0;
        }
    }
}
#endif
