using System.IO;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>Applies the Harness-approved 0.8.9 combat-feel batch without changing route content.</summary>
    public static class M5cCombatFeelProjectSetup
    {
        private const string EmberValleyScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string CombatGymScenePath = "Assets/_Game/Scenes/90_CombatGym.unity";

        [MenuItem("Emberfall/Setup/Apply M5c 0.8.9 Combat Feel")]
        public static void Apply()
        {
            M1ProjectSetup.EnsureInputActions();
            M1ProjectSetup.EnsureCombatTuning();
            M1AnimationSetup.Apply();
            M5cRouteEnrichmentProjectSetup.Apply();

            ConfigureOfflinePlayer(EmberValleyScenePath);
            if (File.Exists(CombatGymScenePath)) ConfigureOfflinePlayer(CombatGymScenePath);

            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "EMBERFALL_M5C_COMBAT_FEEL_SETUP_COMPLETE version=0.8.9 " +
                "melee=sector(2.45m,170deg) perfectGuard=0.20 perfectDodge=0.18 " +
                "sprint=8.2 execution=enabled-offline executionNetwork=disabled");
        }

        private static void ConfigureOfflinePlayer(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            if (player == null) throw new InvalidDataException($"PlayerCombatActor missing in {scenePath}.");

            var serialized = new SerializedObject(player);
            serialized.FindProperty("_attackRadius").floatValue = 2.45f;
            serialized.FindProperty("_attackAngle").floatValue = 170f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Animator animator = player.GetComponentInChildren<Animator>(true);
            PerfectDefenseFeedbackPresenter feedback =
                player.GetComponent<PerfectDefenseFeedbackPresenter>() ??
                player.gameObject.AddComponent<PerfectDefenseFeedbackPresenter>();
            feedback.Configure(player, animator);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
        }
    }
}
