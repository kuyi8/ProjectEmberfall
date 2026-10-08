using System;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Copy-only encounter sandbox. No main-route/built-in package/NavMesh/BuildSettings authoring.</summary>
    public static class SummonerEncounterReview
    {
        public const string ScenePath = "Assets/_Game/Scenes/Review/92_AshCallerEncounter.unity";
        private const string Gym = "Assets/_Game/Scenes/90_CombatGym.unity";

        [MenuItem("Emberfall/Review/Open Ash Caller Encounter")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save your own scene and stop Play before opening the isolated encounter.");
            Prepare();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static string Prepare()
        {
            var active = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || active.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Encounter preparation refuses dirty/multiple/playing scenes.");
            string original = active.path;
            try
            {
                var spiritStyle = SummonedMinionArtSetup.EnsureAssets();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                {
                    // Owned review-scene migration only. Never regenerate or save the Gym/main route.
                    var existing = EditorSceneManager.OpenScene(ScenePath);
                    var summoner = Object.FindObjectOfType<SummonerEnemyActor>();
                    if (summoner == null) throw new InvalidOperationException("Existing candidate is missing its summoner.");
                    var serialized = new SerializedObject(summoner);
                    serialized.FindProperty("_minionStyle").objectReferenceValue = spiritStyle;
                    var weapon = serialized.FindProperty("_minionWeapon");
                    if (weapon.objectReferenceValue == null)
                    {
                        weapon.objectReferenceValue = Require<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab");
                    }
                    bool changed = serialized.ApplyModifiedPropertiesWithoutUndo();
                    if (summoner.GetComponent<SummonerEncounterHint>() == null)
                    { summoner.gameObject.AddComponent<SummonerEncounterHint>().Configure(summoner); changed = true; }
                    // ApplyWithoutUndo does not reliably dirty the scene for a migrated asset reference.
                    // Mark ONLY our owned copy, then verify the saved reference independently before Play.
                    if (changed) EditorSceneManager.MarkSceneDirty(existing);
                    if (existing.isDirty) EditorSceneManager.SaveScene(existing);
                    return ScenePath;
                }
                var source = EditorSceneManager.OpenScene(Gym);
                if (!EditorSceneManager.SaveScene(source, ScenePath, true)) throw new InvalidOperationException("Copy-only save failed.");
                var copy = EditorSceneManager.OpenScene(ScenePath);
                foreach (CombatTarget target in Object.FindObjectsOfType<CombatTarget>())
                    if (!(target is PlayerCombatActor)) target.gameObject.SetActive(false);
                foreach (CombatEncounterCoordinator coordinator in Object.FindObjectsOfType<CombatEncounterCoordinator>()) coordinator.enabled = false;
                var player = Object.FindObjectOfType<PlayerCombatActor>();
                if (player == null) throw new InvalidOperationException("Gym copy has no player.");
                var controller = player.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                player.transform.SetPositionAndRotation(new Vector3(0f, .1f, -5f), Quaternion.identity);
                if (controller != null) controller.enabled = true;
                Create(player, new Vector3(0f, 0f, 5f));
                EditorSceneManager.SaveScene(copy);
                return ScenePath;
            }
            finally
            {
                if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void Create(PlayerCombatActor player, Vector3 position)
        {
            var root = new GameObject("Enemy_AshCaller_Candidate"); root.SetActive(false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 180, 0));
            var agent = root.AddComponent<NavMeshAgent>(); agent.height = 2f; agent.radius = .42f; agent.acceleration = 13f;
            var body = root.AddComponent<CapsuleCollider>(); body.height = 2f; body.radius = .42f; body.center = Vector3.up * 1.05f;
            var visualPrefab = Require<GameObject>("Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
            visual.transform.localPosition = Vector3.up * 1.05f;
            var aim = new GameObject("AimPoint").transform; aim.SetParent(root.transform, false); aim.localPosition = Vector3.up * 1.7f;
            var cast = new GameObject("CastOrigin").transform; cast.SetParent(root.transform, false); cast.localPosition = new Vector3(0f, 1.42f, .82f);
            Material material = Require<Material>("Assets/_Game/Art/Materials/M1/M_RuneProjectile.mat");
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "SummonWindupCue";
            orb.transform.SetParent(cast, false); orb.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(orb.GetComponent<Collider>());
            var actor = root.AddComponent<SummonerEnemyActor>();
            actor.Configure(Require<TextAsset>("Assets/_Game/Data/Review/SummonerCandidate.v1.json"),
                Require<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json"), player, agent, body, aim, cast, orb.transform,
                SummonedMinionArtSetup.EnsureAssets(), material,
                visual.GetComponentInChildren<Animator>(), Require<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset"), true,
                "encounter:ash-caller-candidate", Require<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab"));
            root.SetActive(true);
            root.AddComponent<SummonerEncounterHint>().Configure(actor);
        }
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
    }
}
