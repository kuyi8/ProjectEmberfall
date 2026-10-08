using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class SummonedSpiritArtTests
    {
        private const string Folder = "Assets/_Game/Art/SummonedSpirit";
        private static SummonedMinionStyle Style => AssetDatabase.LoadAssetAtPath<SummonedMinionStyle>(Folder + "/SummonedSpiritStyle.asset");

        [Test] public void AuthoredStyle_ContainsOnlyValidPresentationReferences()
        {
            Assert.That(Style, Is.Not.Null); Assert.That(Style.IsValid, Is.True);
            Assert.That(Style.VisualPrefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(Style.VisualPrefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(Style.VisualPrefab.GetComponentsInChildren<CombatTarget>(true), Is.Empty);
            Assert.That(Style.FragmentMesh.vertexCount, Is.EqualTo(6));
            Assert.That(Style.FragmentMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
        }
        [Test] public void SpiritBody_UsesItsOwnOpaqueMaterialNotOrdinaryBoneAsset()
        {
            var bone = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_Bone.mat");
            var spirit = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/M_SpiritBone.mat");
            Assert.That(spirit, Is.Not.SameAs(bone));
            Assert.That(spirit.color, Is.Not.EqualTo(bone.color));
            Assert.That(spirit.GetFloat("_Surface"), Is.Zero);
            Assert.That(spirit.GetFloat("_ZWrite"), Is.EqualTo(1f));
            Assert.That(Style.VisualPrefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(spirit)), Is.True);
            Assert.That(Style.VisualPrefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Contains(bone)), Is.False);
        }
        [Test] public void DerivativeRetainsAvatarButChangesHelmetAndCloakSilhouetteOnlyInOwnedCopy()
        {
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab");
            Assert.That(Style.VisualPrefab.GetComponentInChildren<Animator>(true).avatar, Is.SameAs(original.GetComponentInChildren<Animator>(true).avatar));
            foreach (string suffix in new[] { "_Helmet", "_Cloak" })
            {
                Assert.That(original.GetComponentsInChildren<Renderer>(true).Single(r => r.name.EndsWith(suffix)).gameObject.activeSelf, Is.True);
                Assert.That(Style.VisualPrefab.GetComponentsInChildren<Renderer>(true).Single(r => r.name.EndsWith(suffix)).gameObject.activeSelf, Is.False);
            }
        }
    }
}
