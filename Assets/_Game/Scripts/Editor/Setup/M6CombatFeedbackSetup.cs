using System;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class M6CombatFeedbackSetup
    {
        public const string AudioPath = "Assets/_Game/Settings/CombatImpactAudio_M6.asset";
        private const string AudioRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/10_Audio/ImpactSounds/Audio/";

        [MenuItem("Emberfall/Setup/Apply 0.9.1 Combat Feedback")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var audio = AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>(AudioPath);
            bool created = audio == null;
            if (created)
            {
                audio = ScriptableObject.CreateInstance<CombatImpactAudioSet>();
                AssetDatabase.CreateAsset(audio, AudioPath);
                var serialized = new SerializedObject(audio);
                Assign(serialized, "_flesh", "impactSoft_medium_000");
                Assign(serialized, "_metal", "impactMetal_light_000");
                Assign(serialized, "_heavyFlesh", "impactPunch_medium_000");
                Assign(serialized, "_heavyMetal", "impactMetal_heavy_000");
                Assign(serialized, "_guardBreak", "impactPlate_heavy_000");
                Assign(serialized, "_execution", "impactPunch_heavy_000");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // Existing author/user sound selections survive every rebuild.
            M6ImpactAudioQualitySetup.EnsureConfigured(audio);
            M6ImpactGradeVfxSetup.EnsureAssets();
            try
            {
                foreach (string name in new[] { "10_EmberValley", "20_Sanctum", "90_CombatGym", "91_NetworkGym" })
                {
                    string path = $"Assets/_Game/Scenes/{name}.unity";
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var camera in root.GetComponentsInChildren<ThirdPersonCameraRig>(true))
                            Ensure<CombatCameraImpulse>(camera.gameObject);
                        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                            Ensure<AnimatorSpeedCoordinator>(animator.gameObject);
                        foreach (var target in root.GetComponentsInChildren<CombatTarget>(true))
                        {
                            var serialized = new SerializedObject(target);
                            serialized.FindProperty("_impactSurface").enumValueIndex =
                                target is ShieldEnemyActor || target is WardenActor ? (int)ImpactSurface.Metal : (int)ImpactSurface.Flesh;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                        }
                    }
                    var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CombatCameraImpulse>(true)).ToArray();
                    foreach (var player in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PlayerCombatActor>(true)))
                    {
                        Ensure<CombatHitFeedbackPresenter>(player.gameObject).Configure(player,
                            player.GetComponentInChildren<Animator>(true), audio, cameras.FirstOrDefault());
                        var vfx = Ensure<CombatImpactVfxPresenter>(player.gameObject);
                        vfx.Configure(player,
                            AssetDatabase.LoadAssetAtPath<GameObject>(M6ImpactGradeVfxSetup.Root + "P_M6_Impact_Steel.prefab"),
                            AssetDatabase.LoadAssetAtPath<GameObject>(M6ImpactGradeVfxSetup.Root + "P_M6_Impact_Guard.prefab"),
                            AssetDatabase.LoadAssetAtPath<GameObject>(M6ImpactGradeVfxSetup.Root + "P_M6_Impact_Ember.prefab"));
                        M6ImpactGradeVfxSetup.Bind(vfx);
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                string[] prefabs = { "P_M5_NetworkGymPlayer", "P_M5_NetworkGymEnemy", "P_M5_NetworkRunePriest", "P_M5_NetworkRuinGuard", "P_M5_NetworkWarden" };
                foreach (string name in prefabs)
                {
                    string path = $"Assets/_Game/Resources/Networking/{name}.prefab";
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var animator in root.GetComponentsInChildren<Animator>(true)) Ensure<AnimatorSpeedCoordinator>(animator.gameObject);
                        if (root.GetComponent<NetworkGymPlayer>() != null)
                        {
                            var camera = root.GetComponentInChildren<ThirdPersonCameraRig>(true);
                            var impulse = camera != null ? Ensure<CombatCameraImpulse>(camera.gameObject) : null;
                            Ensure<CombatHitFeedbackPresenter>(root).Configure(null, root.GetComponentInChildren<Animator>(true), audio, impulse);
                            const string vfxRoot = "Assets/_Game/Prefabs/VFX/M6Art/";
                            Ensure<CombatImpactVfxPresenter>(root).Configure(null,
                                AssetDatabase.LoadAssetAtPath<GameObject>(vfxRoot + "P_M6_Impact_Steel.prefab"),
                                AssetDatabase.LoadAssetAtPath<GameObject>(vfxRoot + "P_M6_Impact_Guard.prefab"),
                                AssetDatabase.LoadAssetAtPath<GameObject>(vfxRoot + "P_M6_Impact_Ember.prefab"));
                            M6ImpactGradeVfxSetup.Bind(root.GetComponent<CombatImpactVfxPresenter>());
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.Any(x => x.isLoaded && x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
            Debug.Log("[M6_FEEDBACK_SETUP] fiveGrades=ready audioAudition=pending domainChanges=none");
        }

        private static T Ensure<T>(GameObject root) where T : Component => root.GetComponent<T>() ?? root.AddComponent<T>();
        private static void Assign(SerializedObject asset, string field, string file)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioRoot + file + ".ogg");
            if (clip == null) throw new InvalidOperationException("Missing local audio: " + file);
            asset.FindProperty(field).objectReferenceValue = clip;
        }
    }
}
