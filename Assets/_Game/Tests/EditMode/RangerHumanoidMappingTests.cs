using System;
using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class RangerHumanoidMappingTests
    {
        private GameObject _model;
        [SetUp] public void Setup()
        {
            _model = new GameObject("MappingTest");
            Transform control = Node("root", _model.transform);
            Transform pelvis = Node("pelvis", control);
            Node("spine_01", pelvis); Node("thigh_l", pelvis); Node("thigh_r", pelvis);
        }
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(_model);
        private static Transform Node(string name, Transform parent)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        private static HumanDescription Description(string hips = "root") => new HumanDescription
        {
            human = new[] { new HumanBone { humanName = "Hips", boneName = hips, limit = new HumanLimit { useDefaultValues = true } },
                new HumanBone { humanName = "Spine", boneName = "spine_01", limit = new HumanLimit { min = new Vector3(1, 2, 3) } } },
            skeleton = new[] { new SkeletonBone { name = "root", position = new Vector3(2, 3, 4), scale = Vector3.one } },
            upperArmTwist = .37f, lowerArmTwist = .29f, upperLegTwist = .41f,
            lowerLegTwist = .33f, armStretch = .031f, legStretch = .025f,
            feetSpacing = .13f, hasTranslationDoF = true
        };

        [Test] public void CorrectsOnlyHipsWithoutMutatingInputOrOtherDescriptionFields()
        {
            var before = Description(); var corrected = RangerHumanoidMappingSetup.CorrectedDescription(before, _model.transform);
            Assert.That(before.human[0].boneName, Is.EqualTo("root"));
            Assert.That(corrected.human[0].boneName, Is.EqualTo("pelvis"));
            Assert.That(corrected.human[0].limit.useDefaultValues, Is.True);
            Assert.That(corrected.human[1], Is.EqualTo(before.human[1]));
            Assert.That(corrected.skeleton, Is.SameAs(before.skeleton));
            Assert.That(corrected.upperArmTwist, Is.EqualTo(before.upperArmTwist));
            Assert.That(corrected.lowerArmTwist, Is.EqualTo(before.lowerArmTwist));
            Assert.That(corrected.upperLegTwist, Is.EqualTo(before.upperLegTwist));
            Assert.That(corrected.lowerLegTwist, Is.EqualTo(before.lowerLegTwist));
            Assert.That(corrected.armStretch, Is.EqualTo(before.armStretch));
            Assert.That(corrected.legStretch, Is.EqualTo(before.legStretch));
            Assert.That(corrected.feetSpacing, Is.EqualTo(before.feetSpacing));
            Assert.That(corrected.hasTranslationDoF, Is.EqualTo(before.hasTranslationDoF));
        }
        [Test] public void AlreadyCorrectMappingIsIdempotent()
        {
            var before = Description("pelvis");
            var first = RangerHumanoidMappingSetup.CorrectedDescription(before, _model.transform);
            var second = RangerHumanoidMappingSetup.CorrectedDescription(first, _model.transform);
            Assert.That(second.human, Is.EqualTo(first.human));
        }
        [Test] public void WrongTopologyIsRejectedInsteadOfFixingAnotherRig()
        {
            _model.GetComponentsInChildren<Transform>().Single(t => t.name == "thigh_r").SetParent(_model.transform, false);
            Assert.Throws<InvalidOperationException>(() => RangerHumanoidMappingSetup.CorrectedDescription(Description(), _model.transform));
        }
        [Test] public void UnknownMappingIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            RangerHumanoidMappingSetup.CorrectedDescription(Description("OtherBone"), _model.transform));
        [Test] public void DuplicateRequiredAnatomicalNodeIsRejected()
        {
            Node("pelvis", _model.transform);
            Assert.Throws<InvalidOperationException>(() => RangerHumanoidMappingSetup.CorrectedDescription(Description(), _model.transform));
        }
        [Test] public void DuplicateHipsMappingIsRejected()
        {
            var desc = Description(); desc.human = new[] { desc.human[0], desc.human[0] };
            Assert.Throws<InvalidOperationException>(() => RangerHumanoidMappingSetup.CorrectedDescription(desc, _model.transform));
        }
        [Test] public void ActualOwnedAvatarMapsAnatomicalPelvis()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RangerHumanoidMappingSetup.DerivedPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RangerHumanoidMappingSetup.DerivedPath);
            Assert.That(importer.humanDescription.human.Single(h => h.humanName == "Hips").boneName, Is.EqualTo("pelvis"));
            Assert.That(model.GetComponent<Animator>().avatar.isHuman, Is.True);
            Assert.That(model.GetComponent<Animator>().avatar.isValid, Is.True);
            Assert.That(model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).name, Is.EqualTo("pelvis"));
        }
    }
}
