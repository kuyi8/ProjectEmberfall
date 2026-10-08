using System.Linq;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    internal static class M6EnvironmentAssertions
    {
        internal const string RootName = "[Art] M6 Environment";
        internal static MeshFilter[] AssertWalls(string[] prefixes)
        {
            var root = GameObject.Find(RootName);
            Assert.That(root, Is.Not.Null);
            var walls = root.GetComponentsInChildren<MeshFilter>(true).Where(f =>
                f.sharedMesh != null && f.sharedMesh.name.StartsWith("wall") &&
                prefixes.Any(prefix => f.transform.parent.name.StartsWith(prefix))).ToArray();
            Assert.That(walls, Is.Not.Empty, string.Join("/", prefixes));
            foreach (var wall in walls)
            {
                var renderer = wall.GetComponent<Renderer>(); var collider = wall.GetComponent<MeshCollider>();
                string label = wall.transform.parent.name;
                Assert.That(renderer, Is.Not.Null, label);
                Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, label);
                Assert.That(renderer.bounds.size.x, Is.GreaterThan(.01f), label);
                Assert.That(renderer.bounds.size.y, Is.GreaterThan(.01f), label);
                Assert.That(renderer.bounds.size.z, Is.GreaterThan(.01f), label);
                Assert.That(renderer.sharedMaterials, Is.Not.Empty, label);
                Assert.That(renderer.sharedMaterials.All(m => m != null), Is.True, label);
                Assert.That(collider, Is.Not.Null, label);
                Assert.That(collider.enabled && !collider.isTrigger, Is.True, label);
                Assert.That(collider.sharedMesh, Is.SameAs(wall.sharedMesh), label);
                Assert.That(wall.GetComponentInParent<CameraOccluder>(), Is.Not.Null, label);
                Assert.That(Vector3.Distance(collider.bounds.center, renderer.bounds.center), Is.LessThan(.02f), label);
                Assert.That(Vector3.Distance(collider.bounds.size, renderer.bounds.size), Is.LessThan(.02f), label);
#if UNITY_EDITOR
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(wall.sharedMesh), Does.Contain("KayKit_Dungeon_Remastered"), label);
#endif
            }
            return walls;
        }

        internal static void AssertGymSide(string side)
        {
            var walls = AssertWalls(new[] { "Gym_" + side + "_" });
            Assert.That(walls, Has.Length.EqualTo(6), "Four sides must contain all24 authored wall segments.");
            bool xAxis = side == "N" || side == "S";
            var intervals = walls.Select(w => w.GetComponent<Renderer>().bounds)
                .Select(b => new Vector2(xAxis ? b.min.x : b.min.z, xAxis ? b.max.x : b.max.z)).OrderBy(i => i.x).ToArray();
            // Run places neighbouring4m parts edge-to-edge;2cm is a floating/bounds tolerance, not an intentional gap.
            const float tolerance = .02f;
            Assert.That(intervals[0].x, Is.LessThanOrEqualTo(-12f + tolerance), side);
            float end = intervals[0].y;
            foreach (var interval in intervals.Skip(1))
            {
                Assert.That(interval.x - end, Is.LessThanOrEqualTo(tolerance), "Uncovered wall-part bounds: " + side);
                end = Mathf.Max(end, interval.y);
            }
            Assert.That(end, Is.GreaterThanOrEqualTo(12f - tolerance), side);
            Assert.That(end - intervals[0].x, Is.GreaterThanOrEqualTo(24f - tolerance), side);
            // Bounds coverage checks part placement, not the absence of intentional broken-wall visual holes.
        }
    }
}
