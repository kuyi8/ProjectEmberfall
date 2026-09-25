using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class ExecutionAccentTests
    {
        [Test]
        public void Crescent_HasBakedThickTaperedGeometry_AndIndependentOrangeMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(M6ImpactGradeVfxSetup.ExecutionPath);
            var renderer = prefab.transform.Find("ExecutionCrescent").GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
            Assert.That(AssetDatabase.GetAssetPath(renderer.mesh), Is.EqualTo(M6ImpactGradeVfxSetup.ExecutionMeshPath));
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(M6ImpactGradeVfxSetup.ExecutionMaterialPath));
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Emberfall/M6ExecutionCrescent"));
            foreach (string property in new[] { "_EdgeColor", "_CoreColor" })
            {
                Color color = renderer.sharedMaterial.GetColor(property);
                Assert.That(color.r, Is.GreaterThan(color.g * 2));
                Assert.That(color.g, Is.GreaterThan(color.b * 4));
            }
            var vertices = renderer.mesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo(82));
            Assert.That(Vector3.Distance(vertices[40], vertices[41]), Is.EqualTo(.26f).Within(.001f));
            Assert.That(Vector3.Distance(vertices[0], vertices[1]), Is.LessThan(.02f));
            Assert.That(vertices.All(v => Mathf.Abs(v.z) < .0001f), Is.True);
            Assert.That(renderer.transform.localPosition.z, Is.LessThan(-.3f), "Surface-side placement, not hidden at the body centre.");
        }

        [TestCase(.02f, 1)]
        [TestCase(.08f, 1)]
        [TestCase(.16f, 1)]
        [TestCase(.25f, 1)]
        [TestCase(.40f, 0)]
        public void Crescent_IsImmediateAndShortLived_WithoutInheritedSimulationAcceleration(float seconds, int expected)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(M6ImpactGradeVfxSetup.ExecutionPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                var particle = instance.transform.Find("ExecutionCrescent").GetComponent<ParticleSystem>();
                Assert.That(particle.main.simulationSpeed, Is.EqualTo(1));
                Assert.That(particle.main.maxParticles, Is.EqualTo(1));
                particle.Simulate(seconds, false, true, true);
                Assert.That(particle.particleCount, Is.EqualTo(expected));
                Assert.That(instance.GetComponentsInChildren<Collider>(), Is.Empty);
                Assert.That(instance.GetComponentsInChildren<MonoBehaviour>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
