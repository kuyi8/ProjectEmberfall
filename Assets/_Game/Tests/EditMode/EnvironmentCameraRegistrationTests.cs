using System.Linq;
using Emberfall.Editor.Review;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class EnvironmentCameraRegistrationTests
    {
        [TestCase("10_EmberValley")]
        [TestCase("90_CombatGym")]
        public void WallRegistration_IsQueryOnly_Idempotent_AndActuallyIntersectsWorldRays(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/" + name + ".unity");
            try
            {
                string frozen = EnvironmentCameraBoundaryReview.CaptureFrozen(scene);
                M6EnvironmentSetup.RegisterCameraOccluders(scene);
                Assert.That(EnvironmentCameraBoundaryReview.CaptureFrozen(scene), Is.EqualTo(frozen));
                Assert.That(M6EnvironmentSetup.RegisterCameraOccluders(scene), Is.Zero);
                var root = scene.GetRootGameObjects().Single(r => r.name == M6EnvironmentSetup.RootName);
                var walls = root.GetComponentsInChildren<MeshFilter>(true).Where(f =>
                    AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(M6EnvironmentSetup.KitRoot + "fbx/wall")).ToArray();
                Assert.That(walls, Is.Not.Empty);
                foreach (var wall in walls)
                {
                    var occluder = wall.GetComponentInParent<CameraOccluder>();
                    Assert.That(occluder, Is.Not.Null, wall.transform.parent.name);
                    var bounds = wall.GetComponent<Renderer>().bounds;
                    float reach = bounds.size.magnitude + 1;
                    var ray = new Ray(bounds.center - wall.transform.forward * reach, wall.transform.forward);
                    Assert.That(occluder.TryGetDistance(ray, reach * 2, .22f, out var distance), Is.True, wall.transform.parent.name);
                    Assert.That(distance, Is.InRange(0, reach));
                    Assert.That(wall.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(wall.sharedMesh));
                }
            }
            finally { EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity"); }
        }
    }
}
