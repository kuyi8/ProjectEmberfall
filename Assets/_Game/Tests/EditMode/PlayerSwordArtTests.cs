using System;
using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerSwordArtTests
    {
        GameObject Sword => AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSwordArtSetup.PrefabPath);
        [Test] public void OwnedSwordIsStaticRenderOnlyAndCanonical()
        { Assert.That(Sword, Is.Not.Null); Assert.DoesNotThrow(() => PlayerSwordArtSetup.ValidateCanonical(Sword)); }
        [Test] public void BladeTipIsActualMeshVertexRatherThanGuardBoundsCorner()
        {
            Vector3 tip = PlayerSwordArtSetup.LocalBladeTip(Sword.transform);
            var filter = Sword.GetComponentInChildren<MeshFilter>();
            Vector3[] vertices = filter.sharedMesh.vertices.Select(v => Sword.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            Assert.That(vertices.Min(v => Vector3.Distance(v, tip)), Is.LessThan(.00001f));
            Assert.That(tip.y, Is.EqualTo(vertices.Max(v => v.y)).Within(.00001f));
        }
        [Test] public void MaterialIsOwnedOpaqueUrpWithPersistedAtlas()
        {
            var material = Sword.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var data = new SerializedObject(material);
            Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(PlayerSwordArtSetup.MaterialPath));
            Assert.That(((Shader)data.FindProperty("m_Shader").objectReferenceValue).name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(material.renderQueue, Is.EqualTo(2000));
            var textures = data.FindProperty("m_SavedProperties.m_TexEnvs"); bool found = false;
            for (int i = 0; i < textures.arraySize; i++)
            {
                var pair = textures.GetArrayElementAtIndex(i); if (pair.FindPropertyRelative("first").stringValue != "_BaseMap") continue;
                Assert.That(pair.FindPropertyRelative("second.m_Texture").objectReferenceValue, Is.Not.Null); found = true;
            }
            Assert.That(found, Is.True);
        }
        [Test] public void MeshAndImportedAxisComeFromUnmodifiedLicensedSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSwordArtSetup.SourcePath);
            Assert.That(Sword.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(source.GetComponentInChildren<MeshFilter>().sharedMesh));
            Assert.That(Quaternion.Angle(Sword.transform.Find("Model").localRotation, Quaternion.identity), Is.LessThan(.01f));
        }
        [Test] public void MeasuredHandleCenterIsTheSocketOrigin()
        {
            var renderer = Sword.GetComponentInChildren<MeshRenderer>();
            var textures = new SerializedObject(renderer.sharedMaterial).FindProperty("m_SavedProperties.m_TexEnvs"); Texture atlas = null;
            for (int i = 0; i < textures.arraySize; i++)
            {
                var pair = textures.GetArrayElementAtIndex(i); if (pair.FindPropertyRelative("first").stringValue == "_BaseMap")
                    atlas = pair.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            }
            // Decode a transient PNG, never change the original texture's readability/importer.
            var readable = new Texture2D(2, 2);
            try
            {
                Assert.That(ImageConversion.LoadImage(readable, System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(atlas))), Is.True);
                var filter = Sword.GetComponentInChildren<MeshFilter>(); var vertices = filter.sharedMesh.vertices; var uv = filter.sharedMesh.uv;
                var wrapped = Enumerable.Range(0, vertices.Length).Where(i =>
                {
                    Color color = readable.GetPixelBilinear(uv[i].x, uv[i].y);
                    return color.r > color.g * 1.35f && color.r > color.b * 1.6f && color.g > .02f;
                }).Select(i => Sword.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i])).y).ToArray();
                Assert.That(wrapped, Has.Length.EqualTo(134));
                Assert.That(wrapped.Min(), Is.EqualTo(PlayerSwordArtSetup.GripMinimumY).Within(.0001f));
                Assert.That(wrapped.Max(), Is.EqualTo(PlayerSwordArtSetup.GripMaximumY).Within(.0001f));
                Assert.That((wrapped.Min() + wrapped.Max()) * .5f, Is.Zero.Within(.0001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
            Assert.That(PlayerSwordArtSetup.LocalGeometryBounds(Sword.transform).min.y, Is.EqualTo(-PlayerSwordArtSetup.PommelInset).Within(.001f));
        }
    }
}
