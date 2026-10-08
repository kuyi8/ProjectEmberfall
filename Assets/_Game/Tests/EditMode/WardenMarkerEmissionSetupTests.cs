using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    /// <summary>Clone-only URP authoring fixed-point checks; never saves/imports production assets.</summary>
    public sealed class WardenMarkerEmissionSetupTests
    {
        private const string Folder = "Assets/_Game/Art/Materials/M5NetworkGym/";
        private static readonly string[] Names =
        {
            "M_NetworkWarden", "M_NetworkWardenAttack", "M_NetworkWardenPhase"
        };

        [TestCase("M_NetworkWarden")]
        [TestCase("M_NetworkWardenAttack")]
        [TestCase("M_NetworkWardenPhase")]
        public void RealSourceClone_EmissionSurvivesOfficialValidationWithExactSerializationFixedPoint(string name)
        {
            List<SourceSnapshot> sources = Names.Select(FreezeSource).ToList();
            Material source = sources.Single(row => row.material.name == name).material;
            Material clone = new Material(source) { name = source.name };
            try
            {
                Assert.That(EditorUtility.IsPersistent(clone), Is.False);
                Assert.That(clone.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                Color baseColor = clone.GetColor("_BaseColor"), emissionColor = clone.GetColor("_EmissionColor");
                string[] textureNames = clone.GetTexturePropertyNames();
                var textures = textureNames.ToDictionary(property => property, property => new TextureSnapshot
                {
                    texture = clone.GetTexture(property), scale = clone.GetTextureScale(property),
                    offset = clone.GetTextureOffset(property)
                });

                // Recreate the old inconsistent authoring on this owned clone even after the
                // production sources have been repaired. No source is preheated or exempted.
                clone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                clone.EnableKeyword("_EMISSION");
                M5NetworkingProjectSetup.ConfigureWardenMarkerEmission(clone);
                string configured = EditorJsonUtility.ToJson(clone);
                ValidateOfficialLit(clone);
                Assert.That(EditorJsonUtility.ToJson(clone), Is.EqualTo(configured),
                    "Generator output must equal the full subsequent official URP serialization.");
                M5NetworkingProjectSetup.ConfigureWardenMarkerEmission(clone);
                Assert.That(EditorJsonUtility.ToJson(clone), Is.EqualTo(configured), "Repeated helper must also be exact.");
                Assert.That(clone.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.None));
                Assert.That(clone.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(clone.GetColor("_BaseColor"), Is.EqualTo(baseColor), "Preserve authored base endpoint.");
                Assert.That(clone.GetColor("_EmissionColor"), Is.EqualTo(emissionColor), "Preserve authored emission endpoint.");
                AssertStoredAlias(clone.GetColor("_Color"), clone.GetColor("_BaseColor"));
                Assert.That(clone.GetTexturePropertyNames(), Is.EqualTo(textureNames));
                foreach (var row in textures)
                {
                    Assert.That(clone.GetTexture(row.Key), Is.SameAs(row.Value.texture), row.Key);
                    Assert.That(clone.GetTextureScale(row.Key), Is.EqualTo(row.Value.scale), row.Key);
                    Assert.That(clone.GetTextureOffset(row.Key), Is.EqualTo(row.Value.offset), row.Key);
                }
            }
            finally
            {
                Object.DestroyImmediate(clone);
                foreach (SourceSnapshot row in sources)
                {
                    Assert.That(EditorJsonUtility.ToJson(row.material), Is.EqualTo(row.json), row.path + " shared memory");
                    Assert.That(Digest(row.path), Is.EqualTo(row.disk), row.path + " asset SHA");
                    Assert.That(Digest(row.path + ".meta"), Is.EqualTo(row.meta), row.path + " meta SHA");
                }
            }
        }

        private static void ValidateOfficialLit(Material material)
        {
            Type type = Type.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor");
            Assert.That(type, Is.Not.Null, "Use the installed URP Editor implementation, not a test imitation.");
            MethodInfo method = type.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(Material) }, null);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
            method.Invoke(Activator.CreateInstance(type, true), new object[] { material });
        }

        private static SourceSnapshot FreezeSource(string name)
        {
            string path = Folder + name + ".mat";
            string disk = Digest(path), meta = Digest(path + ".meta");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, path);
            Assert.That(EditorUtility.IsPersistent(material), Is.True, path);
            Assert.That(material.name, Is.EqualTo(name));
            return new SourceSnapshot { path = path, material = material, json = EditorJsonUtility.ToJson(material), disk = disk, meta = meta };
        }

        private static string Digest(string path)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static void AssertStoredAlias(Color actual, Color expected)
        {
            // URP synchronizes its legacy alias through Unity's native colour storage. This
            // measured channel tolerance applies ONLY to that alias; full JSON remains exact.
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.000001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.000001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.000001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(.000001f));
        }

        private sealed class SourceSnapshot
        {
            public string path, json, disk, meta;
            public Material material;
        }

        private sealed class TextureSnapshot
        {
            public Texture texture;
            public Vector2 scale, offset;
        }
    }
}
