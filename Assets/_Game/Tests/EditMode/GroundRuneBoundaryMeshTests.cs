using System;
using Emberfall.AI.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class GroundRuneBoundaryMeshTests
    {
        [TestCase(.35f)]
        [TestCase(1f)]
        [TestCase(2.55f)]
        [TestCase(6f)]
        public void OuterEdgeUsesActualRadiusAndOnlyInnerEdgeHasThickness(float radius)
        {
            using (var boundary = new GroundRuneBoundaryMesh())
            {
                boundary.SetRadius(radius);
                Mesh mesh = boundary.Mesh;
                Assert.That(GroundRuneBoundaryMesh.Segments, Is.EqualTo(64));
                Assert.That(mesh.vertexCount, Is.EqualTo(130));
                Assert.That(mesh.triangles.Length, Is.EqualTo(384));
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                float thickness = Mathf.Min(.1f, radius * .08f);
                for (int i = 0; i <= GroundRuneBoundaryMesh.Segments; i++)
                {
                    Vector3 inner = vertices[i * 2];
                    Vector3 outer = vertices[i * 2 + 1];
                    Assert.That(inner.y, Is.Zero);
                    Assert.That(outer.y, Is.Zero);
                    Assert.That(outer.magnitude, Is.EqualTo(radius).Within(.00001f));
                    Assert.That(inner.magnitude, Is.EqualTo(radius - thickness).Within(.00001f));
                    Assert.That(outer.magnitude - inner.magnitude, Is.LessThanOrEqualTo(.10001f));
                    Assert.That(normals[i * 2], Is.EqualTo(Vector3.up));
                    Assert.That(normals[i * 2 + 1], Is.EqualTo(Vector3.up));
                }
                Assert.That(Vector3.Distance(vertices[0], vertices[128]), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(vertices[1], vertices[129]), Is.LessThan(.00001f));
                Assert.That(mesh.bounds.size.y, Is.Zero);
                Assert.That(mesh.bounds.size.x, Is.EqualTo(radius * 2).Within(.00001f));
                Assert.That(mesh.bounds.size.z, Is.EqualTo(radius * 2).Within(.00001f));
            }
        }

        [Test]
        public void RadiusUpdatesReuseMeshAndFixedIndexBufferWithUpwardNondegenerateFaces()
        {
            using (var boundary = new GroundRuneBoundaryMesh())
            {
                Mesh original = boundary.Mesh;
                int[] indices = original.triangles;
                foreach (float radius in new[] { 2.55f, .35f, 6f, 1f, 2.55f })
                {
                    boundary.SetRadius(radius);
                    Assert.That(boundary.Mesh, Is.SameAs(original));
                    Assert.That(original.vertexCount, Is.EqualTo(130));
                    CollectionAssert.AreEqual(indices, original.triangles);
                    Vector3[] vertices = original.vertices;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        for (int j = 0; j < 3; j++)
                            Assert.That(indices[i + j], Is.InRange(0, vertices.Length - 1));
                        Vector3 cross = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]],
                            vertices[indices[i + 2]] - vertices[indices[i]]);
                        Assert.That(cross.y, Is.GreaterThan(0f), "The annulus must face the camera above the floor.");
                    }
                }
            }
        }

        [Test]
        public void InvalidRadiusIsRejectedWithoutChangingPreviouslyValidGeometry()
        {
            using (var boundary = new GroundRuneBoundaryMesh())
            {
                boundary.SetRadius(2.55f);
                Vector3[] original = boundary.Mesh.vertices;
                foreach (float radius in new[] { 0f, -.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => boundary.SetRadius(radius));
                    CollectionAssert.AreEqual(original, boundary.Mesh.vertices);
                }
            }
        }

        [Test]
        public void DisposeDestroysOwnedMeshImmediatelyInEditModeAndIsIdempotent()
        {
            var boundary = new GroundRuneBoundaryMesh();
            Mesh owned = boundary.Mesh;
            try
            {
                boundary.SetRadius(1f);
                boundary.Dispose();
                Assert.That(boundary.Mesh, Is.Null);
                Assert.That(owned == null, Is.True, "Owned native Mesh must not survive disposal.");
                Assert.DoesNotThrow(() => boundary.Dispose());
                Assert.Throws<ObjectDisposedException>(() => boundary.SetRadius(1f));
            }
            finally { boundary.Dispose(); }
        }
    }
}
