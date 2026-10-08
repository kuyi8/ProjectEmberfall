using System.Linq;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class SummonerIdentityArtTests
    {
        const string Folder = "Assets/_Game/Art/SummonerIdentity";
        [Test] public void HollowCrestIsACompactSharedStaticMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/M_SummonerHollowCrown.asset");
            Assert.That(mesh, Is.Not.Null); Assert.That(mesh.vertexCount, Is.EqualTo(48));
            Assert.That(mesh.triangles.Length, Is.EqualTo(288)); Assert.That(mesh.bounds.size.x, Is.LessThan(1));
            Assert.That(mesh.bounds.size.y, Is.LessThan(.7f)); Assert.That(mesh.bounds.size.z, Is.EqualTo(.09f).Within(.0001f));
        }
        [Test] public void IdentityMaterialIsOwnOpaqueIvoryGoldNotSharedSpiritOrPriest()
        {
            var crest = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/M_SummonerIvoryGold.mat");
            var spirit = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/SummonedSpirit/M_SpiritSplinter.mat");
            var priest = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_Priest.mat");
            Assert.That(crest, Is.Not.Null); Assert.That(crest, Is.Not.SameAs(spirit)); Assert.That(crest, Is.Not.SameAs(priest));
            Assert.That(crest.GetColor("_BaseColor"), Is.Not.EqualTo(spirit.GetColor("_BaseColor")));
            Assert.That(crest.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(crest.GetFloat("_Surface"), Is.Zero); Assert.That(crest.GetFloat("_ZWrite"), Is.EqualTo(1));
        }
        [Test] public void ExistingOrdinaryAndSpiritVisualsDoNotAcquireOwnerCrestOrAuthority()
        {
            foreach (string path in new[] { "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab", "Assets/_Game/Art/SummonedSpirit/P_SummonedSpirit.prefab" })
            {
                var visual = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(visual, Is.Not.Null, path);
                Assert.That(visual.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Summoner_StaticIdentityCrown"), Is.False);
                Assert.That(visual.GetComponentsInChildren<CombatTarget>(true), Is.Empty);
            }
        }
    }
}
