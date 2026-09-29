using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Read-only historical geometry fixture. Never overwrites the formal scenes or rebakes history.</summary>
    public static class EnvironmentNavigationBaseline
    {
        public const string Snapshot = "Builds/ArtReview/0.9.6-environment-production/20260928-175449-665";
        public const string Fixture = "Assets/_Game/Scenes/Review/NavigationBaseline/10_EmberValley.unity";
        private const string Output = "Builds/ArtReview/0.9.6-navigation-fix";

        public static void PrepareAndMeasure()
        {
            if (!UnityEngine.Application.isBatchMode) throw new InvalidOperationException("Batch-only evidence tool.");
            Directory.CreateDirectory(Path.GetDirectoryName(Fixture));
            byte[] original = File.ReadAllBytes(Snapshot + "/10_EmberValley-before.unity");
            if (!File.Exists(Fixture)) File.WriteAllBytes(Fixture, original);
            if (!original.SequenceEqual(File.ReadAllBytes(Fixture))) throw new InvalidOperationException("Historical fixture changed.");
            AssetDatabase.ImportAsset(Fixture, ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Output);
            var rows = new List<string> { "label,segment,centerX,centerZ,halfX,halfZ,clippedSurfaceArea,groundBandArea,totalArea,persistentNav,navAsset" };
            foreach (string path in new[] { Fixture, "Assets/_Game/Scenes/10_EmberValley.unity" })
            {
                EditorSceneManager.OpenScene(path);
                var surface = Object.FindObjectOfType<NavMeshSurface>();
                if (surface == null || surface.navMeshData == null) throw new InvalidOperationException("Missing historical/live navigation.");
                var nav = NavMesh.CalculateTriangulation();
                float total = 0;
                for (int i = 0; i < nav.indices.Length; i += 3)
                    total += Vector3.Cross(nav.vertices[nav.indices[i + 1]] - nav.vertices[nav.indices[i]],
                        nav.vertices[nav.indices[i + 2]] - nav.vertices[nav.indices[i]]).magnitude * .5f;
                // An old scene referring to a newer external bake would invalidate the comparison.
                // The first capture records this exact old area and scene-owned (nonpersistent) data.
                if (path == Fixture && (EditorUtility.IsPersistent(surface.navMeshData) || Mathf.Abs(total - 2523.25390625f) > .05f))
                    throw new InvalidOperationException("Historical navigation provenance mismatch; do not rebake or substitute current data.");
                foreach (var encounter in Object.FindObjectsOfType<CombatEncounterCoordinator>().OrderBy(e => e.TelemetrySegment))
                {
                    float area = 0, groundArea = 0;
                    var center = encounter.ArenaCenter; var half = encounter.ArenaHalfExtents;
                    for (int i = 0; i < nav.indices.Length; i += 3)
                    {
                        var poly = new List<Vector3> { nav.vertices[nav.indices[i]], nav.vertices[nav.indices[i+1]], nav.vertices[nav.indices[i+2]] };
                        poly = Clip(poly, 0, center.x-half.x, true); poly = Clip(poly, 0, center.x+half.x, false);
                        poly = Clip(poly, 2, center.z-half.y, true); poly = Clip(poly, 2, center.z+half.y, false);
                        float clipped = 0;
                        for (int k = 1; k+1 < poly.Count; k++) clipped += Vector3.Cross(poly[k]-poly[0],poly[k+1]-poly[0]).magnitude*.5f;
                        area += clipped;
                        // All four authored battle floors are at y≈0. Keep rooftop islands separate;
                        // summing every navigable layer is not a measure of usable battle-floor space.
                        if (poly.Count>0 && poly.All(v=>Mathf.Abs(v.y-center.y)<=.5f)) groundArea += clipped;
                    }
                    rows.Add(FormattableString.Invariant($"{(path==Fixture?"old":"new")},{encounter.TelemetrySegment},{center.x},{center.z},{half.x},{half.y},{area:R},{groundArea:R},{total:R},{EditorUtility.IsPersistent(surface.navMeshData)},{AssetDatabase.GetAssetPath(surface.navMeshData)}"));
                }
            }
            File.WriteAllLines(Output + "/areas-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".csv", rows);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static List<Vector3> Clip(List<Vector3> input, int axis, float limit, bool minimum)
        {
            var output = new List<Vector3>();
            if (input.Count == 0) return output;
            var previous = input[input.Count-1];
            bool priorInside = minimum ? previous[axis]>=limit : previous[axis]<=limit;
            foreach (var current in input)
            {
                bool inside = minimum ? current[axis]>=limit : current[axis]<=limit;
                if (inside != priorInside) output.Add(Vector3.LerpUnclamped(previous,current,(limit-previous[axis])/(current[axis]-previous[axis])));
                if (inside) output.Add(current);
                previous = current; priorInside = inside;
            }
            return output;
        }
    }
}
