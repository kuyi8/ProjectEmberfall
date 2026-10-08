using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class WorldPresentationTests
    {
        [Test] public void FloorOrnament_IsBoundedPlanarUpwardAndDoesNotRepresentNewWalkableGeometry()
        {
            var mesh=WorldPresentationSetup.BuildFloorMosaic(21.3f,21.3f);
            try
            {
                Assert.That(mesh.subMeshCount,Is.EqualTo(4));
                Assert.That(mesh.vertices.All(v=>Mathf.Abs(v.x)<=10.65001f&&Mathf.Abs(v.z)<=10.65001f&&v.y>=0&&v.y<=.00101f),Is.True);
                Assert.That(mesh.normals.All(n=>n.y>.999f),Is.True);
                Assert.That(mesh.vertexCount,Is.LessThan(1000));
                Assert.That(mesh.triangles.Length%3,Is.Zero);
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        [Test] public void SharedSigil_IsPlanarOctagonalBoundedAndReusableWithoutRuntimeAnimation()
        {
            var mesh=WorldPresentationSetup.BuildSigil(1.6f);
            try
            {
                Assert.That(mesh.subMeshCount,Is.EqualTo(1));
                Assert.That(mesh.vertices.All(v=>v.y==0&&new Vector2(v.x,v.z).magnitude<=1.60001f),Is.True);
                Assert.That(mesh.normals.All(n=>n.y>.999f),Is.True);
                Assert.That(mesh.vertexCount,Is.EqualTo(96));
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        [Test] public void ShippedSceneIdentity_HasNoPhysicsBehaviourOrEmissionAndUsesOwnedDerivatives()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(WorldPresentationSetup.AssetRoot+"/M_Sanctum_Sigil.mat");
            Assert.That(material,Is.Not.Null);Assert.That(material.IsKeywordEnabled("_EMISSION"),Is.False);
            Assert.That(material.GetColor("_BaseColor").maxColorComponent,Is.LessThan(.5f));
            Assert.That(WorldPresentationSetup.Scenes,Is.EquivalentTo(new[]{"10_EmberValley","20_Sanctum"}));
        }

        [Test] public void PillarStoneFaces_RetainFullSquareSolidSilhouetteAndOnlyTwelveMillimetreStandOff()
        {
            var mesh=WorldPresentationSetup.BuildPillarFaces(new Vector3(1.2f,3.6f,1.2f));
            try
            {
                Assert.That(mesh.subMeshCount,Is.EqualTo(4));
                Assert.That(mesh.bounds.size.x,Is.EqualTo(1.224f).Within(.0001f));
                Assert.That(mesh.bounds.size.z,Is.EqualTo(1.224f).Within(.0001f));
                Assert.That(mesh.vertices.All(v=>Mathf.Abs(v.x)<=.61201f&&Mathf.Abs(v.z)<=.61201f&&Mathf.Abs(v.y)<=1.80001f),Is.True);
                Assert.That(mesh.normals.Any(n=>n.x>.999f)&&mesh.normals.Any(n=>n.x<-.999f)&&
                    mesh.normals.Any(n=>n.z>.999f)&&mesh.normals.Any(n=>n.z<-.999f),Is.True);
                Assert.That(mesh.vertexCount,Is.LessThan(1000));
            }
            finally{Object.DestroyImmediate(mesh);}
        }

        [Test] public void PillarCrown_OverhangExistsOnlyAboveReachableSpace()
        {
            var mesh=WorldPresentationSetup.BuildPillarCrown(new Vector3(1.2f,3.6f,1.2f));
            try
            {
                // Translate from pillar-centred coordinates to height above the floor.
                Assert.That(mesh.bounds.min.y+1.8f,Is.GreaterThan(3.4f));
                Assert.That(mesh.bounds.max.y+1.8f,Is.LessThan(3.83f));
                Assert.That(mesh.bounds.size.x,Is.EqualTo(1.6f).Within(.0001f));
                Assert.That(mesh.bounds.size.z,Is.EqualTo(1.6f).Within(.0001f));
                Assert.That(mesh.vertexCount,Is.EqualTo(48));Assert.That(mesh.subMeshCount,Is.EqualTo(4));
                Assert.That(mesh.normals.All(n=>n.sqrMagnitude>.99f),Is.True);
            }
            finally{Object.DestroyImmediate(mesh);}
        }

        [Test] public void BrokenArch_IsAttachedHighAndOutsideTheBackWall_NotNewWalkableGeometry()
        {
            var mesh=WorldPresentationSetup.BuildSanctumSkyline();
            try
            {
                Assert.That(mesh.bounds.min.y,Is.EqualTo(3.15f).Within(.0001f));
                Assert.That(mesh.bounds.max.y,Is.InRange(6.7f,7.1f));
                Assert.That(mesh.bounds.size.x,Is.InRange(6.5f,6.9f));
                Assert.That(mesh.vertices.All(v=>v.y>=3.1499f&&Mathf.Abs(v.z)<=.52501f),Is.True);
                Assert.That(mesh.normals.All(n=>n.sqrMagnitude>.99f&&!float.IsNaN(n.x)),Is.True);
                Assert.That(mesh.vertexCount,Is.EqualTo(384).And.LessThan(1000));
                Assert.That(mesh.subMeshCount,Is.EqualTo(4));
            }
            finally{Object.DestroyImmediate(mesh);}
        }
    }
}
