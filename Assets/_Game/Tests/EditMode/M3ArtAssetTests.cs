using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class M3ArtAssetTests
    {
        private const string SourceRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline";

        [Test]
        public void SelectedArtBaseline_PreservesCc0LicensesAndACompactSourceSet()
        {
            string[] licenseGuids = AssetDatabase.FindAssets("License_Standard t:TextAsset", new[] { SourceRoot });
            Assert.That(licenseGuids, Has.Length.EqualTo(4));
            foreach (string guid in licenseGuids)
            {
                TextAsset license = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(license.text, Does.Contain("CC0 1.0"));
            }

            string[] models = AssetDatabase.FindAssets("t:Model", new[] { SourceRoot });
            Assert.That(models.Length, Is.GreaterThanOrEqualTo(40));
            Assert.That(models.Length, Is.LessThanOrEqualTo(50),
                "The curated baseline unexpectedly expanded into a full-package import.");
        }

        [Test]
        public void ProjectArtMaterials_UseUrpAndBoundedTextures()
        {
            string[] materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Game/Art/Materials/M3Art" });
            Assert.That(materials.Length, Is.GreaterThanOrEqualTo(10));
            foreach (string guid in materials)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"));
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SourceRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null);
                Assert.That(importer.maxTextureSize, Is.LessThanOrEqualTo(1024), path);
            }
        }

        [TestCase("Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab")]
        [TestCase("Assets/_Game/Prefabs/Characters/M3Art/P_Enemy_Peasant.prefab")]
        public void CharacterIdentityPrefab_HasValidHumanoidAndProjectMaterials(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, path);
            Assert.That(animator.isHuman, Is.True, path);
            Assert.That(animator.avatar, Is.Not.Null, path);
            Assert.That(animator.avatar.isValid, Is.True, path);

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(AssetDatabase.GetAssetPath(material), Does.StartWith("Assets/_Game/Art/Materials/Character/M3Art/"));
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"));
                }
            }
        }

        [Test]
        public void EmberValley_ReadabilityPass_UsesDistinctCharactersAndClearTreeLine()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
            GameObject playerVisual = GameObject.Find("Player/CharacterVisual");
            Assert.That(playerVisual, Is.Not.Null);
            Assert.That(playerVisual.GetComponentInChildren<Animator>(true), Is.Not.Null);
            Assert.That(GameObject.Find("Enemy_Fogwalker_Forest/EnemyVisual_Fogwalker/Model"), Is.Not.Null);

            GameObject[] routeStones = UnityEngine.Object.FindObjectsOfType<Transform>()
                .Where(item => item.name.StartsWith("RouteStone_", StringComparison.Ordinal))
                .Select(item => item.gameObject)
                .ToArray();
            Assert.That(routeStones, Has.Length.EqualTo(15));

            Transform[] forestTrees = UnityEngine.Object.FindObjectsOfType<Transform>()
                .Where(item => item.name.StartsWith("ForestTree_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(forestTrees.Length, Is.GreaterThanOrEqualTo(16));
            Assert.That(forestTrees.All(tree => Mathf.Abs(tree.position.x) >= 9f), Is.True);

            string[] routeSurfaceNames =
            {
                "Zone_Camp", "Path_Forest", "Zone_Forest", "Path_Bridge",
                "Zone_Courtyard", "Path_Sanctum", "Zone_Warden", "Path_Return"
            };
            Renderer[] routeSurfaces = routeSurfaceNames
                .Select(GameObject.Find)
                .Select(item => item != null ? item.GetComponent<Renderer>() : null)
                .ToArray();
            Assert.That(routeSurfaces.All(item => item != null), Is.True);
            for (int i = 0; i < routeSurfaces.Length; i++)
            {
                for (int j = i + 1; j < routeSurfaces.Length; j++)
                {
                    Bounds a = routeSurfaces[i].bounds;
                    Bounds b = routeSurfaces[j].bounds;
                    float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                    float overlapZ = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                    Assert.That(overlapX > 0.01f && overlapZ > 0.01f, Is.False,
                        $"Coplanar route surfaces overlap and may flicker: {routeSurfaces[i].name} / {routeSurfaces[j].name}");
                }
            }
        }
    }
}
