using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class ChoiceMarkerArtTests
    {
        [Serializable] sealed class TextEntry { public string id,value; }
        [Serializable] sealed class TextTable { public int schemaVersion;public TextEntry[] entries; }
        [Test] public void EnabledExceptionNormalizesOnlyTheNamedFieldForActualBooleanAndIntegerSerialization()
        {
            var method=typeof(ChoiceMarkerArtSetup).GetMethod("NormalizeEnabled",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            foreach(string value in new[]{"true","false","1","0"})
                Assert.That(method.Invoke(null,new object[]{"{\"m_Enabled\":"+value+",\"m_ReceiveShadows\":true}"}),Is.EqualTo("{\"m_Enabled\":false,\"m_ReceiveShadows\":true}"));
            Assert.That(method.Invoke(null,new object[]{"{\"different_m_Enabled\":true}"}),Is.EqualTo("{\"different_m_Enabled\":true}"));
        }

        [Test] public void ExceptionListIsExactlyTheSixExistingChoiceAnchors()
        {
            Assert.That(ChoiceMarkerArtSetup.Names,Is.EquivalentTo(new[]{"RuneChoice_Ember","RuneChoice_Guard","RouteChoice_Supply","RouteChoice_Risk","AshReinforcement_StagedReinforcement","AshReinforcement_TogetherReinforcement"}));
            Assert.That(ChoiceMarkerArtSetup.Names.Distinct().Count(),Is.EqualTo(6));
            Assert.That(File.ReadAllText("Assets/_Game/Scripts/Editor/Setup/WorldPresentationSetup.cs"),Does.Contain("ChoiceMarkerArtSetup.ApplyToScene(scene)"));
        }

        [Test] public void OwnedPlaqueIsPureVisualActualDecoratedSourceWithNoColliderOrCameraException()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ChoiceMarkerArtSetup.PrefabPath);Assert.That(prefab,Is.Not.Null);
            Assert.DoesNotThrow(()=>ImportedEnvironmentDressingSetup.ValidatePureVisual(prefab));
            var meshes=prefab.GetComponentsInChildren<MeshFilter>(true);Assert.That(meshes,Is.Not.Empty);
            foreach(var mesh in meshes)
            {
                Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh),Is.EqualTo(ChoiceMarkerArtSetup.Source));
                Assert.That(mesh.sharedMesh.vertexCount,Is.GreaterThan(0));
                Assert.That(mesh.GetComponent<Renderer>().sharedMaterial,Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>(ChoiceMarkerArtSetup.StoneMaterial)));
            }
            Assert.That(Hash(ChoiceMarkerArtSetup.Source),Is.EqualTo("1B52793C28CCEBCA91B5ED09D132C97DA7A3792DCAB8302CC29A9D51CCC4868B"));
            Assert.That(Hash(ChoiceMarkerArtSetup.Source+".meta"),Is.EqualTo("105C6869BB2D913AE55CDDD32BBACEBF8D59A6A4F8E35991D12BD326EDF6CED6"));
        }

        [Test] public void FourUpdatedRuneTextsMatchTheBuiltinBytesWithoutChangingIdsOrSchema()
        {
            var source=JsonUtility.FromJson<TextTable>(File.ReadAllText("Assets/_Game/Data/M2/texts.zh-CN.v1.json"));
            var builtin=Directory.GetFiles("Assets/StreamingAssets/BuiltinContent","texts.zh-CN.v1.json",SearchOption.AllDirectories);
            Assert.That(builtin,Has.Length.EqualTo(1));var packed=JsonUtility.FromJson<TextTable>(File.ReadAllText(builtin[0]));
            Assert.That(source.schemaVersion,Is.EqualTo(1));Assert.That(packed.schemaVersion,Is.EqualTo(1));
            foreach(var row in new[]{
                new TextEntry{id="text:interaction.choose-ember-rune",value="选择烈印：蓄满重击追加伤害与架势削减"},
                new TextEntry{id="text:interaction.choose-guard-rune",value="选择守印：精准防御或精准闪避后强化轻击"},
                new TextEntry{id="text:message.ember-rune-chosen",value="烈印已绑定：蓄满重击命中追加 16 伤害与 24 架势削减"},
                new TextEntry{id="text:message.guard-rune-chosen",value="守印已绑定：精准防御或精准闪避后，下一次轻击追加 12 伤害"}})
            {
                Assert.That(source.entries.Single(e=>e.id==row.id).value,Is.EqualTo(row.value));
                Assert.That(packed.entries.Single(e=>e.id==row.id).value,Is.EqualTo(row.value));
            }
        }
        static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
    }
}
