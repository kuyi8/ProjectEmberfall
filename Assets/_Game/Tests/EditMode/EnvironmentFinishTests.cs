using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class EnvironmentFinishTests
    {
        [TestCase(.2f, 1.2f)] // Covers neither original 3m segment midpoint: old mesh overlaps.
        [TestCase(.8f, 2.2f)] // Covers a midpoint but not the whole segment: old mesh leaves a hole.
        public void Cliff_PartialSharedEdgeRetainsExactlyTheExposedLength(float from, float to)
        {
            var floor = new Bounds(new Vector3(0, -.25f, 0), new Vector3(6, .5f, 6));
            var neighbour = new Bounds(new Vector3(6, -.25f, (from + to) / 2), new Vector3(6, .5f, to - from));
            var mesh = M6EnvironmentFinishSetup.BuildCliff(floor, new[] { floor, neighbour });
            try { Assert.That(EastTopLength(mesh, floor), Is.EqualTo(6 - (to - from)).Within(.0001f)); }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Cliff_OverlappingNeighboursUseUnion_IndependentOfInputOrder()
        {
            var floor = new Bounds(new Vector3(0, -.25f, 0), new Vector3(6, .5f, 6));
            var a = new Bounds(new Vector3(6, -.25f, -.25f), new Vector3(6, .5f, 2.5f));
            var b = new Bounds(new Vector3(6, -.25f, .75f), new Vector3(6, .5f, 2.5f));
            var first = M6EnvironmentFinishSetup.BuildCliff(floor, new[] { floor, a, b });
            var second = M6EnvironmentFinishSetup.BuildCliff(floor, new[] { b, floor, a });
            try
            {
                Assert.That(EastTopLength(first, floor), Is.EqualTo(2.5f).Within(.0001f));
                Assert.That(second.vertices, Is.EqualTo(first.vertices));
                Assert.That(second.triangles, Is.EqualTo(first.triangles));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void Cliff_LowerNeighbourDoesNotHideUpperLedge()
        {
            var floor = new Bounds(new Vector3(0, -.25f, 0), new Vector3(6, .5f, 6));
            var lower = new Bounds(new Vector3(6, -2.25f, 0), new Vector3(6, .5f, 6));
            var mesh = M6EnvironmentFinishSetup.BuildCliff(floor, new[] { floor, lower });
            try { Assert.That(EastTopLength(mesh, floor), Is.EqualTo(6).Within(.0001f)); }
            finally { Object.DestroyImmediate(mesh); }
        }

        static float EastTopLength(Mesh mesh, Bounds floor)
        {
            var vertices = mesh.vertices;
            float total = 0;
            // Each double-sided triangle emits six vertices; count its front side once.
            for (int i = 0; i < vertices.Length; i += 6)
            {
                var top = vertices.Skip(i).Take(3).Where(v =>
                    Mathf.Abs(v.y - (floor.max.y - .04f)) < .0001f &&
                    Mathf.Abs(v.x - (floor.max.x + .025f)) < .0001f).ToArray();
                if (top.Length == 2) total += Mathf.Abs(top[0].z - top[1].z);
            }
            return total;
        }

        [Test]
        public void Cliff_HasNoWalkableTop_AndStaysBelowPhysicalSurface()
        {
            var b = new Bounds(new Vector3(0, -.25f, 0), new Vector3(12, .5f, 10));
            var mesh = M6EnvironmentFinishSetup.BuildCliff(b, new[] { b });
            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.bounds.max.y, Is.LessThan(b.max.y - .03f));
                Assert.That(mesh.normals.All(n => Mathf.Abs(n.y) < .5f), Is.True);
                Assert.That(mesh.vertices.All(v => v.x >= b.min.x - .226f && v.x <= b.max.x + .226f &&
                    v.z >= b.min.z - .226f && v.z <= b.max.z + .226f), Is.True);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Cliff_RepeatedGenerationHasIdenticalVerticesAndTriangles()
        {
            var b = new Bounds(new Vector3(3, -.2f, 5), new Vector3(8, .4f, 8));
            var a = M6EnvironmentFinishSetup.BuildCliff(b, new[] { b });
            var second = M6EnvironmentFinishSetup.BuildCliff(b, new[] { b });
            try
            {
                Assert.That(second.vertices, Is.EqualTo(a.vertices));
                Assert.That(second.triangles, Is.EqualTo(a.triangles));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(second); }
        }

        [Test]
        public void Cliff_SuppressesSharedInteriorEdge()
        {
            var b = new Bounds(new Vector3(0, -.25f, 0), new Vector3(6, .5f, 6));
            var neighbour = new Bounds(new Vector3(6, -.25f, 0), new Vector3(6, .5f, 6));
            var alone = M6EnvironmentFinishSetup.BuildCliff(b, new[] { b });
            var joined = M6EnvironmentFinishSetup.BuildCliff(b, new[] { b, neighbour });
            try { Assert.That(joined.vertexCount, Is.EqualTo(alone.vertexCount * 3 / 4)); }
            finally { Object.DestroyImmediate(alone); Object.DestroyImmediate(joined); }
        }
    }
}
