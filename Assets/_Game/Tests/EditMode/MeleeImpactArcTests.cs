using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using System.Linq;

namespace Emberfall.Tests.EditMode
{
    public sealed class MeleeImpactArcTests
    {
        [TestCase(.02f)]
        [TestCase(.08f)]
        public void GuardBreak_HasImmediateLocalizedFlash_NotAnExpandingGroundRing(float seconds)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Impact_GuardBreak.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                Assert.That(instance.GetComponentsInChildren<Transform>().Any(x => x.name.Contains("Shockwave")), Is.False);
                var flash = instance.transform.Find("ContactFlash").GetComponent<ParticleSystem>();
                flash.Simulate(seconds, false, true, true);
                Assert.That(flash.particleCount, Is.EqualTo(1));
                var shards = instance.transform.Find("ContactFracture").GetComponent<ParticleSystem>();
                shards.useAutoRandomSeed = false; shards.randomSeed = 42;
                shards.Simulate(seconds, false, true, true);
                Assert.That(shards.particleCount, Is.EqualTo(14));
                var particles = new ParticleSystem.Particle[14];
                int count = shards.GetParticles(particles);
                for (int i = 0; i < count; i++) Assert.That(particles[i].position.magnitude, Is.LessThan(1f));
                Assert.That(flash.main.simulationSpeed, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(2.45f, 170f, 0f)]
        [TestCase(2.7f, 240f, 137f)]
        [TestCase(1.3f, 90f, -53f)]
        [TestCase(4.1f, 300f, 179f)]
        public void GeneratedWorldGeometry_MatchesRadiusAndBothEdges(float radius, float angle, float yaw)
        {
            var arc = new MeleeImpactArcMesh();
            try
            {
                arc.SetSector(radius, angle);
                var origin = new Vector3(32, 1.7f, -25);
                var rotation = Quaternion.Euler(0, yaw, 0);
                var vertices = arc.Mesh.vertices;
                for (int i = 1; i < vertices.Length; i += 2)
                {
                    Vector3 world = origin + rotation * vertices[i];
                    Assert.That(Vector3.Distance(world, origin), Is.EqualTo(radius).Within(.0001f));
                    Assert.That(world.y, Is.EqualTo(origin.y).Within(.0001f));
                }
                Assert.That(Vector3.SignedAngle(rotation * Vector3.forward, rotation * vertices[1], Vector3.up),
                    Is.EqualTo(-angle * .5f).Within(.0001f));
                Assert.That(Vector3.SignedAngle(rotation * Vector3.forward, rotation * vertices[vertices.Length - 1], Vector3.up),
                    Is.EqualTo(angle * .5f).Within(.0001f));
                foreach (var vertex in vertices) Assert.That(vertex.magnitude, Is.LessThanOrEqualTo(radius + .0001f));
                var mesh = arc.Mesh;
                arc.SetSector(radius * .8f, angle * .5f);
                Assert.That(arc.Mesh, Is.SameAs(mesh), "Reconfiguration reuses the warmed mesh.");
            }
            finally { Object.DestroyImmediate(arc.Mesh); }
        }
    }
}
