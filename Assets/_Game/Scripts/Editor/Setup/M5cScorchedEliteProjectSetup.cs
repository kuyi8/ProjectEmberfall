using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Integrates the M5c scorched elite without changing the existing collision, navigation,
    /// encounter-membership, or server-authority boundaries.
    /// </summary>
    public static class M5cScorchedEliteProjectSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string MaterialRoot = "Assets/_Game/Art/Materials/M5c";
        private const string WarningMaterialPath = MaterialRoot + "/M_M5c_ScorchedBurstWarning.mat";
        private const string ImpactVfxPath = "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Warden_DelayedBlast.prefab";

        [MenuItem("Emberfall/Setup/Apply M5c Scorched Elite")]
        public static void Apply()
        {
            Directory.CreateDirectory(MaterialRoot);
            GameObject eliteVisual = M6ArtProjectSetup.CreateM5cScorchedEliteVisual();
            Material warningMaterial = EnsureWarningMaterial();
            GameObject impactVfx = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactVfxPath);
            if (impactVfx == null) throw new FileNotFoundException("Scorched impact VFX is missing.", ImpactVfxPath);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ShieldEnemyActor actor = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ShieldEnemyActor>(true))
                .Single(item => item.name == "Enemy_RuinGuard_Courtyard");

            Transform oldVisual = actor.transform.Cast<Transform>()
                .FirstOrDefault(item => item.name.StartsWith("EnemyVisual_RuinGuard", StringComparison.Ordinal));
            if (oldVisual != null) UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);

            GameObject visual = PrefabUtility.InstantiatePrefab(eliteVisual, actor.transform) as GameObject;
            if (visual == null) throw new InvalidOperationException("Could not instantiate the scorched elite visual.");
            visual.name = "EnemyVisual_RuinGuardScorched";
            visual.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            M1ProjectSetup.SetLayerRecursively(visual, 0);

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            Renderer bodyRenderer = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Cast<Renderer>()
                .FirstOrDefault() ?? M1ProjectSetup.FindLargestRenderer(visual);
            Transform shieldTransform = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "Shield_Wooden_Equipped");
            Renderer shieldRenderer = shieldTransform != null
                ? shieldTransform.GetComponentInChildren<Renderer>(true)
                : null;
            if (animator == null || !animator.isHuman || bodyRenderer == null || shieldRenderer == null)
                throw new InvalidDataException("Scorched elite visual, Humanoid rig, or shield binding is invalid.");

            var serializedActor = new SerializedObject(actor);
            SetString(serializedActor, "_enemyId", "enemy:ruin-guard-scorched");
            SetObject(serializedActor, "_bodyRenderer", bodyRenderer);
            SetObject(serializedActor, "_shieldRenderer", shieldRenderer);
            SetObject(serializedActor, "_scorchedWarningMaterial", warningMaterial);
            SetObject(serializedActor, "_scorchedImpactVfxPrefab", impactVfx);
            serializedActor.ApplyModifiedPropertiesWithoutUndo();
            actor.ConfigureScorchedPresentation(warningMaterial, impactVfx);

            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            ShieldEnemyAnimationPresenter presenter = actor.GetComponent<ShieldEnemyAnimationPresenter>();
            if (animationSet == null || presenter == null)
                throw new InvalidDataException("Scorched elite animation dependencies are missing.");
            presenter.Configure(animator, actor, animationSet);

            CombatEncounterCoordinator courtyard = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CombatEncounterCoordinator>(true))
                .Single(item => item.name.Contains("Courtyard"));
            var serializedEncounter = new SerializedObject(courtyard);
            SerializedProperty quota = serializedEncounter.FindProperty("_maximumConcurrentMeleeAttackers");
            if (quota == null) throw new MissingFieldException(nameof(CombatEncounterCoordinator), "_maximumConcurrentMeleeAttackers");
            quota.intValue = 2;
            serializedEncounter.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(actor);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(courtyard);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            M5cRouteEnrichmentProjectSetup.Apply();
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"EMBERFALL_M5C_SCORCHED_ELITE_SETUP_COMPLETE version={M5NetworkingProjectSetup.ReleaseLabel} " +
                "id=enemy:ruin-guard-scorched model=kaykit-knight authority=offline-or-server");
        }

        private static Material EnsureWarningMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(WarningMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = "M_M5c_ScorchedBurstWarning" };
                AssetDatabase.CreateAsset(material, WarningMaterialPath);
            }
            else material.shader = shader;
            material.color = new Color(0.82f, 0.08f, 0.015f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(2.4f, 0.12f, 0.01f));
            material.SetFloat("_Smoothness", 0.24f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetString(SerializedObject target, string name, string value)
        {
            SerializedProperty property = target.FindProperty(name) ??
                throw new MissingFieldException(target.targetObject.GetType().Name, name);
            property.stringValue = value;
        }

        private static void SetObject(SerializedObject target, string name, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(name) ??
                throw new MissingFieldException(target.targetObject.GetType().Name, name);
            property.objectReferenceValue = value;
        }
    }
}
