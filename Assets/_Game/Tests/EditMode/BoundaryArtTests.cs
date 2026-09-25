using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class BoundaryArtTests
    {
        [Test]
        public void FloorFacing_PreservesAllGeometryAndTopUv_OnlySplitsMaterials()
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source=cube.GetComponent<MeshFilter>().sharedMesh;
            var mesh=M6BoundaryArtSetup.BuildFloorFaces(source);
            try
            {
                Assert.That(mesh.vertices,Is.EqualTo(source.vertices));
                Assert.That(mesh.normals,Is.EqualTo(source.normals));
                Assert.That(mesh.uv,Is.EqualTo(source.uv));
                Assert.That(mesh.bounds,Is.EqualTo(source.bounds));
                Assert.That(mesh.triangles,Is.EquivalentTo(source.triangles));
                Assert.That(mesh.GetTriangles(0).Length,Is.EqualTo(6));
                Assert.That(mesh.GetTriangles(0).All(i=>mesh.normals[i].y>.5f),Is.True);
                Assert.That(mesh.GetTriangles(1).All(i=>mesh.normals[i].y<.5f),Is.True);
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(cube); }
        }

        [TestCase(6f,1.4f,.5f)]
        [TestCase(.6f,4f,22f)]
        public void Masonry_MatchesOriginalUnitBounds_WithNoOversizedCollisionShell(float x,float y,float z)
        {
            var mesh=M6BoundaryArtSetup.BuildMasonry(new Vector3(x,y,z));
            try
            {
                Assert.That(Vector3.Distance(mesh.bounds.center,Vector3.zero),Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(mesh.bounds.size,Vector3.one),Is.LessThan(.0001f));
                Assert.That(mesh.vertexCount,Is.GreaterThan(100));
                Assert.That(mesh.vertexCount,Is.LessThan(65535));
                Assert.That(mesh.subMeshCount,Is.EqualTo(4));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Skirt_StaysBelowSurface_AndAddsNoWalkableTop()
        {
            var bounds=new Bounds(new Vector3(0,-.25f,0),new Vector3(12,.5f,10));
            var mesh=M6BoundaryArtSetup.BuildSkirt(bounds,new[]{bounds});
            try
            {
                Assert.That(mesh.bounds.max.y,Is.LessThan(bounds.max.y));
                Assert.That(mesh.vertices.All(v=>v.x>=bounds.min.x && v.x<=bounds.max.x && v.z>=bounds.min.z && v.z<=bounds.max.z),Is.True);
                Assert.That(mesh.normals.All(n=>Mathf.Abs(n.y)<.5f),Is.True,"No upward-facing walkable-looking top.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase("10_EmberValley")]
        [TestCase("20_Sanctum")]
        public void Authoring_IsIdempotentAndPreservesAllPhysicsAndNavigation(string name)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/"+name+".unity");
            var root=scene.GetRootGameObjects().Single(x=>x.name==M6BoundaryArtSetup.ArtRoot);
            Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(root.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true),Is.Empty);
            string before=M6BoundaryArtSetup.CapturePhysics(scene);
            int renderers=scene.GetRootGameObjects().Sum(x=>x.GetComponentsInChildren<Renderer>(true).Length);
            M6BoundaryArtSetup.ApplyToScene(scene);
            Assert.That(M6BoundaryArtSetup.CapturePhysics(scene),Is.EqualTo(before));
            Assert.That(scene.GetRootGameObjects().Sum(x=>x.GetComponentsInChildren<Renderer>(true).Length),Is.EqualTo(renderers));
            foreach(var marker in Object.FindObjectsOfType<EncounterBoundaryVisualMarker>())
            {
                Assert.That(Vector3.Distance(marker.VisibleRenderer.bounds.center,marker.GetComponent<BoxCollider>().bounds.center),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(marker.VisibleRenderer.bounds.size,marker.GetComponent<BoxCollider>().bounds.size),Is.LessThan(.001f));
            }
            // Exercise the non-idempotent branch without saving or removing existing authority objects.
            Object.DestroyImmediate(root);
            M6BoundaryArtSetup.ApplyToScene(scene);
            Assert.That(M6BoundaryArtSetup.CapturePhysics(scene),Is.EqualTo(before));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }
}
