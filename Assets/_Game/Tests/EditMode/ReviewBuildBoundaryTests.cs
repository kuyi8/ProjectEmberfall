using Emberfall.Editor.Build;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ReviewBuildBoundaryTests
    {
        [TestCase("Assets/_Game/Art/Review/KayKitRogue/human/Rogue.fbx",true)]
        [TestCase("Assets/_Game/Art/Review/KayKitRogue/generic/Rogue.fbx",true)]
        [TestCase("Assets/_Game/Art/Review/KayKitRogueNotIsolated/Rogue.fbx",false)]
        [TestCase("Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx/Wizard.fbx",false)]
        [TestCase("Assets/_Game/Art/ChoiceMarkers/P_ChoiceStonePlaque.prefab",false)]
        [TestCase(null,false)]
        public void RogueCheckIsTheExactOwnedReviewRootNotSharedProductionSources(string path,bool expected)
            =>Assert.That(VerificationBuildRunner.IsRogueCandidatePath(path),Is.EqualTo(expected));

        [Test] public void ActualEnabledSceneClosureHasNoRogueReviewButRetainsProductionAdventurers()
        {
            Assert.That(VerificationBuildRunner.FindRogueCandidateDependencies(),Is.Empty);
            Assert.That(VerificationBuildRunner.FindProductionAdventurersDependencies(),Is.Not.Empty);
        }
    }
}
