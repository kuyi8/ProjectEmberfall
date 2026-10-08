using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    /// <summary>
    /// Authored data, real source/prefab and saved-scene structure only. Production
    /// scenes are never opened, regenerated or saved by these tests, so opening
    /// SceneLighting cannot change RenderSettings. Migration bounds/frozen-byte
    /// evidence is separate; this does not certify the old full chain, natural
    /// traversal/camera, licence of other resources or frame-time performance.
    /// </summary>
    public sealed class ImportedEnvironmentDressingTests
    {
        const string Kit = M6EnvironmentSetup.KitRoot;
        const string Licence = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered/LICENSE.txt";
        static readonly string[] Models = { "wall_archedwindow_open", "wall_sloped", "floor_foundation_allsides", "banner_patternA_blue" };
        static readonly string[] OldPrefixes = { "CampWall_", "CourtyardWall_", "CourtyardParapet_", "WardenEastWall_",
            "WardenSouthWall_", "WardenNorthWall_", "WardenWestWall_", "ReturnFence_", "BridgeFence_",
            "NorthRuins_", "SouthRuins_", "EastRuins_", "WestRuins_" };
        static readonly string[] OldWalls = { "Camp_BackWall", "Courtyard_NorthWall", "Warden_EastWall", "Warden_SouthWall",
            "Wall_North", "Wall_South", "Wall_East", "Wall_West" };
        const string CameraOccluderGuid = "f344a19ec6123594ca8b550acbea5cf9";
        // Independent acceptance list: a newly authored decoration is NOT opted in by its model/name pattern.
        static readonly string[] CameraEntries =
        {
            "10_EmberValley/CampArchiveWest", "10_EmberValley/CampArchiveEast", "10_EmberValley/CampSideWindow",
            "10_EmberValley/CampBrokenWest", "10_EmberValley/CampBrokenEast", "10_EmberValley/CampCapWest", "10_EmberValley/CampCapEast",
            "10_EmberValley/CourtArchiveWest", "10_EmberValley/CourtArchiveCenter", "10_EmberValley/CourtArchiveEast", "10_EmberValley/CourtBrokenEnd",
            "10_EmberValley/WardenArchiveEastA", "10_EmberValley/WardenArchiveEastB", "10_EmberValley/WardenArchiveEastC",
            "10_EmberValley/WardenBrokenNorthWest", "10_EmberValley/WardenBrokenNorthEast",
            "20_Sanctum/SanctumArchiveWest", "20_Sanctum/SanctumArchiveEast", "20_Sanctum/SanctumCapWest", "20_Sanctum/SanctumCapEast"
        };

        // Measured from these six actual originals and their meta files before
        // this batch's authoring. This is deliberately NOT an all-art inventory.
        sealed class FrozenSource
        {
            public readonly string path, guid, sha, metaSha;
            public FrozenSource(string path, string guid, string sha, string metaSha)
            { this.path = path; this.guid = guid; this.sha = sha; this.metaSha = metaSha; }
        }
        static readonly FrozenSource[] Sources =
        {
            new FrozenSource(Kit + "fbx/wall_archedwindow_open.fbx", "e801c9b2046e6b245a293b294c883776",
                "5421F6D2C9A1EFD717CA3960B30E24357C7382AA4B298E8F188816A5FBE31B80", "23499CBDBB4BDBBDDB49DE053A02B3390F7B41B27CBCE0B32D786F633152D192"),
            new FrozenSource(Kit + "fbx/wall_sloped.fbx", "7768e3d31859ae542aea806f0453b535",
                "CF3DF32B450D23A9E442F8EA784D68D05FCDED260277B86581C7A531F36DC0BD", "38FDF6FB98B0AE5F88C501BCDF2AD5CA4799CBB82D3AD6BC8260D3738E30E5B9"),
            new FrozenSource(Kit + "fbx/floor_foundation_allsides.fbx", "669eb5b078a9fa64a8a253d627c590bb",
                "0C26F5822E020B9FEEC92249741FB795F412F5415195AB7E7513ADFA95C1BDC2", "96766EB8F841C39A3B86444646703E52C6D05B1FD02091890234D026017046A5"),
            new FrozenSource(Kit + "fbx/banner_patternA_blue.fbx", "a4dded256add43b4b9b243dcafd2d17a",
                "ACCCB61BC8A8816013C2614A25C3675935DBE8E44B6A4064555F1878E7617286", "068111264C4A379A1CAC31658A407F848F46F5529851BCF2985CB1C9274202F2"),
            new FrozenSource(Kit + "texture/dungeon_texture.png", "49c9d4719aa41ae469f9d2b600644451",
                "F9AE182518F908BD09461A56A430B9CA7369812AC10890224CC6F32E7DA3E1EE", "DD2903D5525F43E013909FF6B8478623474EF5FBE54ED2CAEFA8AC827C124446"),
            new FrozenSource(Licence, "387c0e7c3eb8a194b99e3d54c7c65741",
                "C5F17ABF619731F57F6BBFF450F6484214605C13AA96BB75BBCFC70930941FF7", "4EFE4F5B88BF101E99C77483C8927F889DE3CAD259B5E91B9DF10444236C956C")
        };

        [Test]
        public void AuthoredLayout_IsCompleteFiniteUniqueAndAvoidsLegacyCleanupNames()
        {
            var layout = ImportedEnvironmentDressingSetup.ReadLayout();
            Assert.That(layout.format, Is.EqualTo(1));
            Assert.That(layout.entries.Length, Is.InRange(1, 64));
            Assert.That(ImportedEnvironmentDressingSetup.Scenes, Is.EquivalentTo(new[] { "10_EmberValley", "20_Sanctum" }));
            Assert.That(ImportedEnvironmentDressingSetup.AllowedModels, Is.EquivalentTo(Models));
            Assert.That(layout.entries.Select(e => e.scene).Distinct(), Is.EquivalentTo(ImportedEnvironmentDressingSetup.Scenes));
            Assert.That(layout.entries.Select(e => e.model).Distinct(), Is.EquivalentTo(Models));
            Assert.That(layout.entries.Select(e => e.scene + "/" + e.name).Distinct().Count(), Is.EqualTo(layout.entries.Length));
            Assert.That(ImportedEnvironmentDressingSetup.RootName.StartsWith("[Art]", StringComparison.Ordinal), Is.False);
            Assert.That(ImportedEnvironmentDressingSetup.RootName, Is.Not.EqualTo(M6EnvironmentSetup.RootName));
            foreach (var e in layout.entries)
            {
                Assert.That(e.name, Is.Not.Empty.And.EqualTo(e.name.Trim()));
                Assert.That(Reserved(e.name), Is.False, e.name);
                Assert.That(e.anchor.StartsWith("GateBlocker_", StringComparison.Ordinal), Is.False, "A gate must not become an art mounting anchor.");
                Assert.That(new[] { "Stone", "Camp", "Warden", "Banner" }, Does.Contain(e.palette));
                Assert.That(Finite(e.offset) && Finite(e.scale) && Finite(e.yaw), Is.True, e.name);
                Assert.That(e.scale.x, Is.InRange(float.Epsilon, 3));
                Assert.That(e.scale.y, Is.InRange(float.Epsilon, 3));
                Assert.That(e.scale.z, Is.InRange(float.Epsilon, 3));
            }
        }

        [Test]
        public void CameraException_IsExactlyTwentyNamedStructureWrappersAndExcludesAllEightBanners()
        {
            Assert.That(ImportedEnvironmentDressingSetup.CameraOccluderEntries, Is.EquivalentTo(CameraEntries));
            Assert.That(ImportedEnvironmentDressingSetup.CameraOccluderEntries.Distinct().Count(), Is.EqualTo(20));
            var entries = ImportedEnvironmentDressingSetup.ReadLayout().entries;
            var selected = entries.Where(e => CameraEntries.Contains(e.scene + "/" + e.name)).ToArray();
            Assert.That(selected, Has.Length.EqualTo(20));
            Assert.That(selected.Count(e => e.scene == "10_EmberValley"), Is.EqualTo(16));
            Assert.That(selected.Count(e => e.scene == "20_Sanctum"), Is.EqualTo(4));
            Assert.That(selected.Count(e => e.model == "wall_archedwindow_open" || e.model == "wall_sloped"), Is.EqualTo(16));
            Assert.That(selected.Count(e => e.model == "floor_foundation_allsides"), Is.EqualTo(4));
            var banners = entries.Where(e => e.model == "banner_patternA_blue").ToArray();
            Assert.That(banners, Has.Length.EqualTo(8));
            Assert.That(banners.Any(e => CameraEntries.Contains(e.scene + "/" + e.name)), Is.False);
            string path = "Assets/_Game/Scripts/Gameplay/Movement/CameraOccluder.cs";
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(CameraOccluderGuid));
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(path).GetClass(), Is.EqualTo(typeof(CameraOccluder)));
        }

        [Test]
        public void SixSelectedOriginals_RetainMeasuredGuidBytesAndActualCc0Licence()
        {
            Assert.That(ImportedEnvironmentDressingSetup.SelectedSourcePaths, Is.EquivalentTo(Sources.Select(s => s.path)));
            foreach (var s in Sources)
            {
                Assert.That(File.Exists(s.path), Is.True, s.path);
                Assert.That(File.Exists(s.path + ".meta"), Is.True, s.path);
                Assert.That(AssetDatabase.AssetPathToGUID(s.path), Is.EqualTo(s.guid), s.path);
                Assert.That(Hash(s.path), Is.EqualTo(s.sha), s.path);
                Assert.That(Hash(s.path + ".meta"), Is.EqualTo(s.metaSha), s.path + ".meta");
            }
            string text = File.ReadAllText(Licence);
            Assert.That(text, Does.Contain("KayKit : Dungeon Remastered (1.0)"));
            Assert.That(text, Does.Contain("Creative Commons Zero, CC0"));
            Assert.That(text, Does.Contain("creativecommons.org/publicdomain/zero/1.0/"));
            Assert.That(text, Does.Contain("commercial projects"));
            foreach (string model in Models)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + "fbx/" + model + ".fbx");
                Assert.That(source, Is.Not.Null, model);
                Assert.That(source.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty, model);
                var filters = source.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters, Is.Not.Empty, model);
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null, model);
                    Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), model);
                    Assert.That(filter.sharedMesh.subMeshCount, Is.GreaterThan(0), model);
                    Assert.That(Enumerable.Range(0, filter.sharedMesh.subMeshCount).Sum(i => (long)filter.sharedMesh.GetIndexCount(i)), Is.GreaterThan(0), model);
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Kit + "texture/dungeon_texture.png");
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width * texture.height, Is.GreaterThan(0));
        }

        [Test]
        public void OwnedPrefabs_UseActualSourceMeshesPureVisualComponentsAndNonEmissiveUrpMaterials()
        {
            foreach (var e in ImportedEnvironmentDressingSetup.ReadLayout().entries.GroupBy(e => e.model + "/" + e.palette).Select(g => g.First()))
            {
                string path = ImportedEnvironmentDressingSetup.AssetRoot + "/Prefabs/P_" + e.model + "_" + e.palette + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.DoesNotThrow(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(prefab), path);
                Assert.That(ImportedEnvironmentDressingSetup.BoundsOf(prefab).size.sqrMagnitude, Is.GreaterThan(.000001f));
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                Assert.That(filters, Is.Not.Empty, path);
                foreach (var filter in filters)
                {
                    Assert.That(filter.sharedMesh, Is.Not.Null, path);
                    Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh), Is.EqualTo(Kit + "fbx/" + e.model + ".fbx"), path);
                    Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), path);
                    var renderer = filter.GetComponent<MeshRenderer>();
                    Assert.That(renderer, Is.Not.Null, path);
                    Assert.That(renderer.enabled, Is.True, path);
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(filter.sharedMesh.subMeshCount), path);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, path);
                        Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(ImportedEnvironmentDressingSetup.AssetRoot + "/Materials/M_Dressing_" + e.palette + ".mat"));
                        Assert.That(material.shader, Is.Not.Null);
                        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                        Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False);
                        Assert.That(material.HasProperty("_EmissionColor"), Is.True);
                        var emission = material.GetColor("_EmissionColor");
                        Assert.That(new Vector3(emission.r, emission.g, emission.b).sqrMagnitude, Is.Zero);
                        Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(Kit + "texture/dungeon_texture.png"));
                    }
                }
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetStaticEditorFlags(t.gameObject), Is.EqualTo((StaticEditorFlags)0), path);
            }
        }

        [TestCase("10_EmberValley")]
        [TestCase("20_Sanctum")]
        public void SavedScene_HasOneIndependentRootEveryAuthoredChildAndOnlyItsRealSelectedVisualReferences(string name)
        {
            // Read saved records, not a loose name substring or an unsaved scene.
            // This tiny reader only resolves GameObject/Transform/component links;
            // it deliberately does not implement general Unity deserialization.
            var docs = SavedRecords("Assets/_Game/Scenes/" + name + ".unity");
            var gameObjects = docs.Values.Where(d => d.type == 1).ToArray();
            var matches = gameObjects.Where(d => Field(d.body, "m_Name") == ImportedEnvironmentDressingSetup.RootName).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), name + ": missing or duplicate saved dressing root must fail.");
            var root = TransformOf(matches[0], docs);
            Assert.That(Reference(root.body, "m_Father"), Is.Zero, "The dressing must not be nested inside the M6 cleanup root.");
            var entries = ImportedEnvironmentDressingSetup.ReadLayout().entries.Where(e => e.scene == name).ToArray();
            Assert.That(entries, Is.Not.Empty);
            var children = DirectChildren(root, docs);
            Assert.That(children.Select(t => Field(docs[Reference(t.body, "m_GameObject")].body, "m_Name")), Is.EquivalentTo(entries.Select(e => e.name)));
            // Both serialized sides must agree; an orphan m_Father link is not
            // sufficient evidence of an instantiated child hierarchy.
            var childSection = Regex.Match(root.body, @"(?ms)^  m_Children:(?<value>.*?)^  m_Father:").Groups["value"].Value;
            var childIds = Regex.Matches(childSection, @"fileID: (-?\d+)").Cast<Match>().Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.That(childIds, Is.EquivalentTo(children.Select(c => c.id)));
            var cameraOwners = new HashSet<long>(children.Where(t => CameraEntries.Contains(name + "/" +
                Field(docs[Reference(t.body, "m_GameObject")].body, "m_Name"))).Select(t => Reference(t.body, "m_GameObject")));
            foreach (var node in Descendants(root, docs))
            {
                var go = docs[Reference(node.body, "m_GameObject")];
                Assert.That(Reserved(Field(go.body, "m_Name")), Is.False, name);
                Assert.That(Field(go.body, "m_StaticEditorFlags"), Is.EqualTo("0"), name);
                foreach (long id in Components(go.body))
                {
                    var component = docs[id];
                    if (component.type == 114)
                    {
                        Assert.That(cameraOwners.Contains(go.id), Is.True, "Only a named direct wrapper may own the camera exception; never root/model/banner.");
                        Assert.That(Regex.Match(Field(component.body, "m_Script"), @"guid: ([0-9a-f]{32})").Groups[1].Value, Is.EqualTo(CameraOccluderGuid));
                        Assert.That(Reference(component.body, "m_GameObject"), Is.EqualTo(go.id));
                        Assert.That(Field(component.body, "m_Enabled"), Is.EqualTo("1"));
                    }
                    else Assert.That(new[] { 4, 23, 33 }, Does.Contain(component.type), "Dressing still cannot own physics, other behaviours, interaction or animation.");
                }
            }
            foreach (var e in entries)
            {
                var wrapper = children.Single(t => Field(docs[Reference(t.body, "m_GameObject")].body, "m_Name") == e.name);
                var subtree = Descendants(wrapper, docs).SelectMany(t => Components(docs[Reference(t.body, "m_GameObject")].body)).Select(id => docs[id]).ToArray();
                var filters = subtree.Where(d => d.type == 33).ToArray();
                Assert.That(filters, Is.Not.Empty, e.name);
                string sourcePath = Kit + "fbx/" + e.model + ".fbx", sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
                var sourceMeshIds = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Mesh>().Select(MeshFileId).ToArray();
                Assert.That(sourceMeshIds, Is.Not.Empty, sourcePath);
                foreach (var filter in filters)
                {
                    string mesh = Field(filter.body, "m_Mesh");
                    Assert.That(mesh, Does.Contain("guid: " + sourceGuid), e.name);
                    Assert.That(sourceMeshIds, Does.Contain(long.Parse(Regex.Match(mesh, @"fileID: (-?\d+)").Groups[1].Value, CultureInfo.InvariantCulture)), e.name);
                }
                foreach (var renderer in subtree.Where(d => d.type == 23))
                    Assert.That(Field(renderer.body, "m_Enabled"), Is.EqualTo("1"), e.name);
                var behaviours = subtree.Where(d => d.type == 114).ToArray();
                bool cameraEntry = CameraEntries.Contains(name + "/" + e.name);
                Assert.That(behaviours.Length, Is.EqualTo(cameraEntry ? 1 : 0), e.name + ": exact scene-only behaviour count.");
                if (cameraEntry)
                {
                    string references = Regex.Match(behaviours[0].body, @"(?ms)^  _renderers:(?<value>.*?)(?=^  [A-Za-z_]\w*:|\z)").Groups["value"].Value;
                    Assert.That(references, Is.Not.Empty, e.name + ": a registered cache must be serialized.");
                    Assert.That(references, Does.Not.Contain("guid:"), e.name + ": no external renderer references.");
                    var ids = Regex.Matches(references, @"(?m)^  - \{fileID: (-?\d+)\}\r?$").Cast<Match>()
                        .Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                    Assert.That(ids, Is.EquivalentTo(subtree.Where(d => d.type == 23).Select(d => d.id)), e.name + ": 100% own renderer coverage, no null/duplicate/foreign references.");
                }
                // This is a retained visible wall anchor, not a replacement
                // barrier or an interactable used as a decoration mount.
                var anchors = gameObjects.Where(d => Field(d.body, "m_Name") == e.anchor).ToArray();
                Assert.That(anchors, Has.Length.EqualTo(1), e.anchor);
                var anchorNodes = Descendants(TransformOf(anchors[0], docs), docs);
                var anchorComponents = anchorNodes.SelectMany(t => Components(docs[Reference(t.body, "m_GameObject")].body)).Select(id => docs[id]).ToArray();
                Assert.That(anchorComponents.Any(d => d.type == 23 && Field(d.body, "m_Enabled") == "1"), Is.True, e.anchor);
                Assert.That(anchorComponents.Any(d => (d.type == 64 || d.type == 65) && Field(d.body, "m_Enabled") == "1"), Is.True, e.anchor + ": retain the original solid wall.");
                foreach (var behaviour in anchorComponents.Where(d => d.type == 114))
                {
                    var guid = Regex.Match(Field(behaviour.body, "m_Script"), @"guid: ([0-9a-f]{32})").Groups[1].Value;
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                    var type = script == null ? null : script.GetClass();
                    Assert.That(type == null || !typeof(IInteractable).IsAssignableFrom(type), Is.True, e.anchor);
                }
            }
        }

        [Test]
        public void StaticHook_KeepsDressingConnectedWithoutInvokingTheOldEnvironmentChain()
        {
            string source = File.ReadAllText("Assets/_Game/Scripts/Editor/Setup/WorldPresentationSetup.cs");
            Assert.That(Regex.IsMatch(source, @"\bImportedEnvironmentDressingSetup\s*\.\s*ApplyToScene\s*\(\s*scene\s*\)\s*;"), Is.True,
                "Static source connection only; not evidence that the full M6/nav regeneration has executed safely.");
            Assert.That(ImportedEnvironmentDressingSetup.RootName.StartsWith("[Art]", StringComparison.Ordinal), Is.False);
        }

        [TestCase(typeof(BoxCollider))]
        [TestCase(typeof(Animator))]
        [TestCase(typeof(CameraOccluder))]
        public void PureVisualValidation_RejectsColliderAndAnimatorEvenUnderAnOtherwiseValidMesh(Type component)
        {
            WithTemporaryMesh(root =>
            {
                Assert.DoesNotThrow(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
                root.AddComponent(component);
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
            });
        }

        [TestCase("10_EmberValley")]
        [TestCase("20_Sanctum")]
        public void SceneVisualValidation_AcceptsOnlyCompleteNamedWrapperCameraCaches(string sceneName)
        {
            WithTemporaryDressing(sceneName, root =>
            {
                Assert.DoesNotThrow(() => ImportedEnvironmentDressingSetup.ValidateSceneVisual(root, sceneName));
                Assert.That(root.GetComponentsInChildren<CameraOccluder>(true).Length, Is.EqualTo(sceneName == "10_EmberValley" ? 16 : 4));
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root),
                    "Scene exceptions must not turn source prefab validation into a permissive contract.");
            });
        }

        [TestCase("root")]
        [TestCase("model")]
        [TestCase("banner")]
        [TestCase("animator")]
        [TestCase("foreign-cache")]
        [TestCase("empty-cache")]
        [TestCase("disabled")]
        public void SceneVisualValidation_RejectsAnyBroaderExceptionOrIncompleteRendererReferences(string fault)
        {
            WithTemporaryDressing("10_EmberValley", root =>
            {
                var selected = root.transform.Find("CampArchiveWest");
                var occluder = selected.GetComponent<CameraOccluder>();
                if (fault == "root") root.AddComponent<CameraOccluder>();
                else if (fault == "model") selected.Find("Model").gameObject.AddComponent<CameraOccluder>();
                else if (fault == "banner") root.transform.Find("CampBlueStandardWest").gameObject.AddComponent<CameraOccluder>();
                else if (fault == "animator") selected.gameObject.AddComponent<Animator>();
                else if (fault == "disabled") occluder.enabled = false;
                else SetRenderers(occluder, fault == "empty-cache" ? Array.Empty<Renderer>() :
                    root.transform.Find("CampArchiveEast").GetComponentsInChildren<Renderer>(true));
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidateSceneVisual(root, "10_EmberValley"));
            });
        }

        [Test]
        public void PureVisualValidation_RejectsStaticFlagsOnAChild()
        {
            WithTemporaryMesh(root =>
            {
                var child = EditorUtility.CreateGameObjectWithHideFlags("StaticChild", HideFlags.HideAndDontSave);
                child.transform.SetParent(root.transform, false);
                GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic);
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
            });
        }

        [Test]
        public void PureVisualValidation_RejectsEmptyRootNullMeshAndZeroVertexMesh()
        {
            WithTemporaryMesh(root =>
            {
                Object.DestroyImmediate(root.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(root.GetComponent<MeshFilter>());
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
                var filter = root.AddComponent<MeshFilter>(); root.AddComponent<MeshRenderer>();
                Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
                var empty = new Mesh();
                try
                {
                    filter.sharedMesh = empty;
                    Assert.Throws<InvalidDataException>(() => ImportedEnvironmentDressingSetup.ValidatePureVisual(root));
                }
                finally { Object.DestroyImmediate(empty); }
            });
        }

        static void WithTemporaryMesh(Action<GameObject> action)
        {
            // No active-scene swap or production object. HideAndDontSave prevents
            // an inspector-style temporary object from becoming authored state.
            var preview = EditorSceneManager.NewPreviewScene();
            var root = EditorUtility.CreateGameObjectWithHideFlags("DressingValidationFixture", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(root, preview);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>();
            try { action(root); }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void WithTemporaryDressing(string sceneName, Action<GameObject> action)
        {
            // Independent preview hierarchy only: never load/save a production scene or mutate the pure prefab.
            var preview = EditorSceneManager.NewPreviewScene();
            var root = EditorUtility.CreateGameObjectWithHideFlags(ImportedEnvironmentDressingSetup.RootName, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                foreach (var e in ImportedEnvironmentDressingSetup.ReadLayout().entries.Where(e => e.scene == sceneName))
                {
                    var wrapper = new GameObject(e.name).transform; wrapper.SetParent(root.transform, false);
                    string path = ImportedEnvironmentDressingSetup.AssetRoot + "/Prefabs/P_" + e.model + "_" + e.palette + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Assert.That(prefab, Is.Not.Null, path);
                    Object.Instantiate(prefab, wrapper, false).name = "Model";
                    if (CameraEntries.Contains(sceneName + "/" + e.name))
                        SetRenderers(wrapper.gameObject.AddComponent<CameraOccluder>(), wrapper.GetComponentsInChildren<Renderer>(true));
                }
                action(root);
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void SetRenderers(CameraOccluder occluder, Renderer[] renderers)
        {
            var serialized = new SerializedObject(occluder); var array = serialized.FindProperty("_renderers");
            Assert.That(array, Is.Not.Null); array.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Narrow saved-record reader: only IDs/types, scalar fields and hierarchy.
        sealed class SavedRecord { public long id; public int type; public string body; }
        static Dictionary<long, SavedRecord> SavedRecords(string path)
        {
            Assert.That(File.Exists(path), Is.True, path);
            return Regex.Matches(File.ReadAllText(path), @"(?ms)^--- !u!(?<type>\d+) &(?<id>-?\d+)(?: stripped)?\r?\n(?<body>.*?)(?=^--- !u!|\z)")
                .Cast<Match>().Select(m => new SavedRecord { id = long.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture),
                    type = int.Parse(m.Groups["type"].Value, CultureInfo.InvariantCulture), body = m.Groups["body"].Value }).ToDictionary(d => d.id);
        }
        static string Field(string body, string field)
        { return Regex.Match(body, @"(?m)^  " + Regex.Escape(field) + @":\s*([^\r\n]*)").Groups[1].Value.Trim().Trim('"'); }
        static long Reference(string body, string field)
        {
            var match = Regex.Match(Field(body, field), @"fileID: (-?\d+)");
            Assert.That(match.Success, Is.True, "Missing saved reference " + field);
            return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        static long[] Components(string body)
        { return Regex.Matches(body, @"(?m)^  - component: \{fileID: (-?\d+)\}").Cast<Match>().Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray(); }
        static SavedRecord TransformOf(SavedRecord gameObject, Dictionary<long, SavedRecord> docs)
        { return Components(gameObject.body).Select(id => docs[id]).Single(d => d.type == 4); }
        static SavedRecord[] DirectChildren(SavedRecord transform, Dictionary<long, SavedRecord> docs)
        {
            // Unrelated prefab-instance stripped Transform records do not have
            // a local m_Father. They are not members of this explicitly saved
            // dressing hierarchy; do not demand their full deserialization.
            string parent = @"(?m)^  m_Father: \{fileID: " + transform.id.ToString(CultureInfo.InvariantCulture) + @"\}\r?$";
            return docs.Values.Where(d => d.type == 4 && Regex.IsMatch(d.body, parent)).ToArray();
        }
        static SavedRecord[] Descendants(SavedRecord transform, Dictionary<long, SavedRecord> docs)
        {
            var result = new List<SavedRecord>(); var pending = new Queue<SavedRecord>(); var visited = new HashSet<long>(); pending.Enqueue(transform);
            while (pending.Count != 0)
            {
                var current = pending.Dequeue(); Assert.That(visited.Add(current.id), Is.True, "Saved transform cycle."); result.Add(current);
                foreach (var child in DirectChildren(current, docs)) pending.Enqueue(child);
            }
            return result.ToArray();
        }
        static long MeshFileId(Mesh mesh)
        {
            string guid; long id;
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out guid, out id), Is.True, mesh.name);
            return id;
        }
        static bool Reserved(string name)
        { return name.StartsWith("[Art]", StringComparison.Ordinal) || name.StartsWith("GateBlocker_", StringComparison.Ordinal) || OldWalls.Contains(name) || OldPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)); }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
