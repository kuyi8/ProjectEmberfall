using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class ImportedCharacterTests
    {
        private const string SourceRoot = "Assets/_Game/Art/Universal Base Characters[Standard]/Universal Base Characters[Standard]";
        private const string ModelPath = SourceRoot + "/Base Characters/Unity/Superhero_Male_FullBody.fbx";
        private const string PrefabPath = "Assets/_Game/Prefabs/Characters/P_Player_UniversalBase_Male.prefab";

        [Test]
        public void SourceModel_UsesValidHumanoidAvatar()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "角色源 FBX 未导入。 ");
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            Assert.That(avatar, Is.Not.Null);
            Assert.That(avatar.isValid, Is.True);
            Assert.That(avatar.isHuman, Is.True);
        }

        [Test]
        public void GeneratedPlayerPrefab_ContainsSkinnedCharacter()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "请先执行 M1 Project Setup 生成项目自有角色预制体。 ");
            Assert.That(prefab.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null);
        }

        [Test]
        public void ImportedPackage_DeclaresCc0License()
        {
            TextAsset license = AssetDatabase.LoadAssetAtPath<TextAsset>(SourceRoot + "/License_Standard.txt");
            Assert.That(license, Is.Not.Null);
            Assert.That(license.text, Does.Contain("CC0 1.0"));
        }
    }
}
