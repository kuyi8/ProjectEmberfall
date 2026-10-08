using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class SanctumSurfaceTests
    {
        [TestCase("Slate",.04f,0f)]
        [TestCase("Stone",.06f,0f)]
        [TestCase("Edge",.08f,0f)]
        [TestCase("Inlay",.30f,.18f)]
        [TestCase("Sigil",.20f,0f)]
        [TestCase("Wall_A",.07f,0f)]
        [TestCase("Wall_B",.05f,0f)]
        [TestCase("Wall_C",.09f,0f)]
        [TestCase("Mortar",.03f,0f)]
        public void Surface_RemainsOpaqueNonEmissiveAndHasAuthoredResponse(string name,float smooth,float metal)
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(WorldPresentationSetup.AssetRoot+"/M_Sanctum_"+name+".mat");
            Assert.That(m,Is.Not.Null);
            Assert.That(m.shader.name,Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(m.GetFloat("_Surface"),Is.Zero);
            Assert.That(m.GetColor("_BaseColor").a,Is.EqualTo(1));
            Assert.That(m.GetFloat("_Smoothness"),Is.EqualTo(smooth));
            Assert.That(m.GetFloat("_Metallic"),Is.EqualTo(metal));
            Assert.That(m.IsKeywordEnabled("_EMISSION"),Is.False);
            Assert.That(m.renderQueue,Is.EqualTo(2000));
        }

        [Test] public void WarmMasonryAndCoolFloor_AreDifferent_NotADangerSignal()
        {
            var wall=AssetDatabase.LoadAssetAtPath<Material>(WorldPresentationSetup.AssetRoot+"/M_Sanctum_Wall_A.mat").GetColor("_BaseColor");
            var floor=AssetDatabase.LoadAssetAtPath<Material>(WorldPresentationSetup.AssetRoot+"/M_Sanctum_Slate.mat").GetColor("_BaseColor");
            Assert.That(wall.r,Is.GreaterThan(wall.b));
            Assert.That(floor.b,Is.GreaterThan(floor.r));
            var line=AssetDatabase.LoadAssetAtPath<Material>(WorldPresentationSetup.AssetRoot+"/M_Sanctum_Inlay.mat").GetColor("_BaseColor");
            Assert.That(line.maxColorComponent,Is.LessThan(.5f));
        }

        [Test] public void ExistingFill_IsOneRealtimePoint_OnlyColourDesignChanged()
        {
            var previous=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/20_Sanctum.unity");
                var lights=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Light>(true)).ToArray();
                Assert.That(lights.Length,Is.EqualTo(1));
                var light=lights[0];Assert.That(light.name,Is.EqualTo("Sanctum Fill Light"));
                Assert.That(light.type,Is.EqualTo(LightType.Point));
                Assert.That(light.lightmapBakeType,Is.EqualTo(LightmapBakeType.Realtime));
                Assert.That(light.color,Is.EqualTo(WorldPresentationSetup.SanctumFillColor));
                Assert.That(light.intensity,Is.EqualTo(2.2f));Assert.That(light.range,Is.EqualTo(30));
                Assert.That(light.shadows,Is.EqualTo(LightShadows.None));
                Assert.That(light.transform.position,Is.EqualTo(new Vector3(1000,-73,1000)));
            }
            finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
        }
    }
}
