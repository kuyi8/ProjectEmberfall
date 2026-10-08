using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Editor.Content;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Additive optional variant; never regenerates verified encounters or old environment.</summary>
    public static class AshReinforcementChoiceSetup
    {
        public const string RootName = "Content_AshReinforcementChoice_v1";
        [MenuItem("Emberfall/Content/Add Ash Reinforcement Choice")]
        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path != AshApproachEncounterSetup.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Use the clean idle main route; preserve unsaved work.");
            var existing = scene.GetRootGameObjects().SingleOrDefault(g => g.name == RootName);
            if (existing != null)
            {
                if (Object.FindObjectsOfType<RouteChoiceEncounterModifier>().Count(m => m.IsReinforcementVariant) != 1)
                    throw new InvalidOperationException("Partial authoring preserved; inspect before repair.");
                return "Existing reinforcement choice preserved; no regeneration.";
            }
            var content = GameObject.Find(AshApproachEncounterSetup.GuardRootName);
            var group = content.GetComponent<CombatEncounterCoordinator>();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var source = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Enemy_Fogwalker_Forest");
            var choiceSource = GameObject.Find("RouteChoice_Supply");
            var wallSource = GameObject.Find("Entry_East_1");
            if (choiceSource == null || wallSource == null || flow == null || player == null)
                throw new InvalidOperationException("Missing established route sources.");
            Vector3 point = new Vector3(0, 0, 24.1f);
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, .25f, NavMesh.AllAreas) || (hit.position - point).sqrMagnitude > .0625f)
                throw new InvalidOperationException("Reinforcement point is not on the existing navigation.");
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-reinforcement-choice.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(scene.path, backup, false); File.Copy(scene.path + ".meta", backup + ".meta", false);
            var choices = new GameObject(RootName);
            try
            {
                CreateChoice(choiceSource, choices.transform, flow, RouteEnrichmentInteractionKind.StagedReinforcement, new Vector3(-1.05f, 0, 17.7f));
                CreateChoice(choiceSource, choices.transform, flow, RouteEnrichmentInteractionKind.TogetherReinforcement, new Vector3(1.05f, 0, 17.7f));
                var reinforcementRoot = Object.Instantiate(source.gameObject, content.transform);
                reinforcementRoot.name = "Enemy_Fogwalker_AshReinforcement"; reinforcementRoot.SetActive(false);
                reinforcementRoot.transform.SetPositionAndRotation(point, Quaternion.Euler(0, 180, 0));
                var reinforcement = reinforcementRoot.GetComponent<MeleeEnemyActor>(); reinforcement.GetComponent<EncounterLeash>().Configure(group);
                var narrow = new GameObject("AshReinforcement_NarrowWalls"); narrow.transform.SetParent(content.transform, false);
                // South end walls finish at18.65. Side walls start18.75, avoiding overlap/shimmer.
                foreach (float x in new[] { -2.8f, 2.8f }) CreateSideWall(wallSource, narrow.transform, new Vector3(x, .4f, 21.925f));
                narrow.SetActive(false);
                var cue = new GameObject("AshReinforcement_ArrivalCue", typeof(MeshFilter), typeof(MeshRenderer));
                cue.transform.SetParent(content.transform, false); cue.transform.position = point + Vector3.up * 1.3f;
                cue.transform.localScale = new Vector3(.25f, .45f, .25f);
                cue.GetComponent<MeshFilter>().sharedMesh = Require<Mesh>("Assets/_Game/Art/Meshes/M3Art/M_RuneOctahedron.asset");
                cue.GetComponent<MeshRenderer>().sharedMaterial = Require<Material>("Assets/_Game/Art/Materials/M3Art/M_Art_RouteGlow.mat"); cue.SetActive(false);
                var modifier = content.AddComponent<RouteChoiceEncounterModifier>();
                modifier.ConfigureReinforcement(flow, group, player, content.GetComponentInChildren<ShieldEnemyActor>(),
                    content.GetComponentInChildren<SummonerEnemyActor>(), reinforcement, narrow, cue);
                var flowData = new SerializedObject(flow); flowData.FindProperty("_reinforcementModifier").objectReferenceValue = modifier;
                flowData.ApplyModifiedPropertiesWithoutUndo();
                var netData = new SerializedObject(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true));
                var roots = netData.FindProperty("_offlineActorRoots"); roots.InsertArrayElementAtIndex(roots.arraySize);
                roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = choices;
                var behaviours = netData.FindProperty("_offlineBehaviours");
                foreach (Behaviour b in content.GetComponentsInChildren<Behaviour>(true).Concat(choices.GetComponentsInChildren<Behaviour>(true)))
                {
                    if (Enumerable.Range(0, behaviours.arraySize).Any(i => behaviours.GetArrayElementAtIndex(i).objectReferenceValue == b)) continue;
                    behaviours.InsertArrayElementAtIndex(behaviours.arraySize); behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = b;
                }
                netData.ApplyModifiedPropertiesWithoutUndo();
                M4BuiltinContentPackager.BuildBuiltinPackage(); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Choice save failed; preserve backup: " + backup);
                return "Optional staged/together choice authored; default B unchanged; backup=" + backup;
            }
            catch { EditorSceneManager.MarkSceneDirty(scene); throw; }
        }
        private static void CreateChoice(GameObject source, Transform parent, M2RouteFlowController flow,
            RouteEnrichmentInteractionKind kind, Vector3 point)
        {
            var copy = Object.Instantiate(source, parent); copy.name = "AshReinforcement_" + kind; copy.transform.position = point;
            copy.GetComponent<RouteEnrichmentInteractable>().Configure(flow, kind,
                copy.transform.Find("InteractionMarker").GetComponent<Renderer>(), copy.GetComponentInChildren<M2QuestHighlightPresenter>());
        }
        private static void CreateSideWall(GameObject source, Transform parent, Vector3 center)
        {
            var wrapper = new GameObject("AshReinforcement_VisibleSideWall").transform; wrapper.SetParent(parent, false);
            var copy = Object.Instantiate(source, wrapper); copy.name = "Masonry";
            copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = source.transform.rotation;
            copy.transform.localScale = source.transform.lossyScale;
            wrapper.localScale = new Vector3(.4f, 1f, 6.35f / 3.5f);
            var collider = copy.GetComponentsInChildren<Collider>().Single(c => !c.isTrigger); Physics.SyncTransforms();
            wrapper.position += center - collider.bounds.center; Physics.SyncTransforms();
            if (Vector3.Distance(collider.bounds.size, new Vector3(.4f, .8f, 6.35f)) > .001f)
                throw new InvalidOperationException("Side-wall source orientation mismatch: " + collider.bounds.size);
            var cut = new GameObject("AshReinforcement_NavCut", typeof(NavMeshObstacle)); cut.transform.SetParent(parent, false); cut.transform.position = center;
            var obstacle = cut.GetComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = collider.bounds.size; obstacle.carving = true; obstacle.carveOnlyStationary = true;
        }
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
    }
}
