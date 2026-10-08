using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Editor.Content;
using Emberfall.Editor.Review;
using Emberfall.Gameplay.Animation;
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
    /// <summary>Additive, one-time main-route migration. Does not regenerate old gates, actors or NavMesh.</summary>
    public static class AshApproachEncounterSetup
    {
        public const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        public const string RootName = "Content_AshApproach_v1";
        public const string Segment = "ash-approach-encounter";
        public const string GuardRootName = "Content_AshGuardPass_v1";
        public const string GuardSegment = "ash-guard-pass-encounter";
        public const string ReturnRootName = "Content_AshReturn_v1";
        public const string ReturnSegment = "ash-return-encounter";
        private const string ArtFolder = "Assets/_Game/Art/SummonedSpirit";

        [MenuItem("Emberfall/Content/Add Formal Ash Caller Approach")]
        public static string Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                SceneManager.sceneCount != 1 || scene.isDirty || scene.path != ScenePath)
                throw new InvalidOperationException("Open the clean main Valley scene in idle Edit mode first.");
            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (existing.GetComponentsInChildren<SummonerEnemyActor>(true).Length != 1 ||
                    existing.GetComponentsInChildren<MeleeEnemyActor>(true).Length != 2)
                    throw new InvalidOperationException("Partial authored encounter preserved; inspect before repair.");
                return "Existing complete encounter preserved; no regeneration.";
            }
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var ordinary = Object.FindObjectsOfType<MeleeEnemyActor>().Single(e => e.name == "Enemy_Fogwalker_Forest");
            var network = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            var wall = GameObject.Find("Entry_East_1");
            if (player == null || network == null || wall == null) throw new InvalidOperationException("Missing current route bindings.");
            foreach (Vector3 p in new[] { new Vector3(-2.2f, 0, 12.4f), new Vector3(2.2f, 0, 12.4f), new Vector3(0, 0, 15.6f) })
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, .25f, NavMesh.AllAreas) || (hit.position - p).sqrMagnitude > .0625f)
                    throw new InvalidOperationException("Authored spawn is not on the existing walkable route: " + p);
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/10_EmberValley.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.Copy(ScenePath, backup, false); File.Copy(ScenePath + ".meta", backup + ".meta", false);
            SummonedMinionStyle style = SummonedMinionArtSetup.EnsureAssets();
            GameObject visual = EnsureCallerVisual();
            M4BuiltinContentPackager.BuildBuiltinPackage();
            GameObject content = new GameObject(RootName);
            try
            {
                var group = content.AddComponent<CombatEncounterCoordinator>();
                var melee = new MeleeEnemyActor[2];
                for (int i = 0; i < melee.Length; i++)
                {
                    // Clone the CURRENT reviewed scene actor, including weapon grip/audio/animation.
                    GameObject root = Object.Instantiate(ordinary.gameObject, content.transform);
                    root.name = "Enemy_Fogwalker_AshApproach_" + (i + 1);
                    root.transform.SetPositionAndRotation(new Vector3(i == 0 ? -2.2f : 2.2f, 0, 12.4f), Quaternion.Euler(0, 180, 0));
                    melee[i] = root.GetComponent<MeleeEnemyActor>();
                    root.GetComponent<EncounterLeash>().Configure(group);
                }
                SummonerEnemyActor caller = CreateCaller(content.transform, player, visual, style);
                caller.gameObject.AddComponent<EncounterLeash>().Configure(group);
                group.Configure(player, melee, null, null, 1, true, new Vector3(0, 0, 13.3f), new Vector2(4.2f, 3.7f),
                    3.2f, Segment, .15f);
                group.ConfigureSummoners(new[] { caller }, true);
                foreach (float z in new[] { 9.6f, 17.0f })
                    foreach (float x in new[] { -2.9f, 2.9f }) CreateEndWall(content.transform, wall, new Vector3(x, .4f, z));

                // Append real authored roots/behaviours to the serialized NET exclusion list.
                // Leave the existing four NGO encounter definitions and all other bindings untouched.
                var serialized = new SerializedObject(network);
                var roots = serialized.FindProperty("_offlineActorRoots");
                roots.InsertArrayElementAtIndex(roots.arraySize); roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = content;
                var behaviours = serialized.FindProperty("_offlineBehaviours");
                foreach (Behaviour b in content.GetComponentsInChildren<Behaviour>(true))
                {
                    behaviours.InsertArrayElementAtIndex(behaviours.arraySize);
                    behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = b;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Main-route scene save failed; backup: " + backup);
                return "Authored Summoner + 2 melee; original route preserved. Backup: " + backup;
            }
            catch
            {
                // Preserve partial state and its evidence. Never silently restore/delete user scene data.
                EditorSceneManager.MarkSceneDirty(scene);
                throw;
            }
        }

        private static GameObject EnsureCallerVisual()
        {
            string path = ArtFolder + "/P_AshCaller.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            const string materialPath = ArtFolder + "/M_AshCaller.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Require<Material>("Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_Priest.mat")) { name = "M_AshCaller" };
                material.SetColor("_BaseColor", new Color(.83f, .55f, .97f));
                material.SetColor("_Color", new Color(.83f, .55f, .97f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            GameObject root = Object.Instantiate(Require<GameObject>("Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab"));
            try
            {
                root.name = "P_AshCaller";
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    r.sharedMaterials = materials;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static SummonerEnemyActor CreateCaller(Transform parent, PlayerCombatActor player, GameObject prefab, SummonedMinionStyle style,
            Vector3? position = null, string encounterId = "encounter:ash-approach")
        {
            var root = new GameObject("Enemy_AshCaller_Approach"); root.SetActive(false); root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position ?? new Vector3(0, 0, 15.6f), Quaternion.Euler(0, 180, 0));
            if (position.HasValue) root.name = "Enemy_AshCaller_GuardPass";
            var agent = root.AddComponent<NavMeshAgent>(); agent.height = 2f; agent.radius = .42f; agent.acceleration = 13f;
            var body = root.AddComponent<CapsuleCollider>(); body.height = 2f; body.radius = .42f; body.center = Vector3.up * 1.05f;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); visual.transform.localPosition = Vector3.up * 1.05f;
            var aim = new GameObject("AimPoint").transform; aim.SetParent(root.transform, false); aim.localPosition = Vector3.up * 1.7f;
            var cast = new GameObject("CastOrigin").transform; cast.SetParent(root.transform, false); cast.localPosition = new Vector3(0, 1.42f, .82f);
            Material material = Require<Material>("Assets/_Game/Art/SummonedSpirit/M_SpiritSplinter.mat");
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "SummonWindupCue"; orb.layer = 2;
            orb.transform.SetParent(cast, false); orb.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(orb.GetComponent<Collider>());
            var actor = root.AddComponent<SummonerEnemyActor>();
            var json = Require<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json");
            actor.Configure(json, json, player, agent, body, aim, cast, orb.transform, style, material,
                visual.GetComponentInChildren<Animator>(), Require<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset"), false,
                encounterId, Require<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab"));
            SummonerIdentitySetup.EnsureCrown(actor);
            root.SetActive(true);
            return actor;
        }

        public static string RepairOwnedWalls()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Repair needs clean idle main route.");
            var root = GameObject.Find(RootName);
            var walls = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "AshApproach_VisibleEndWall").ToArray();
            if (walls.Length != 4) throw new InvalidOperationException("Preserve unexpected authoring state.");
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-wall-repair.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(ScenePath, backup, false);
            foreach (var wall in walls) Object.DestroyImmediate(wall.gameObject);
            foreach (var cut in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "AshApproach_NavCut").ToArray()) Object.DestroyImmediate(cut.gameObject);
            var source = GameObject.Find("Entry_East_1");
            foreach (float z in new[] { 9.6f, 17.0f }) foreach (float x in new[] { -2.9f, 2.9f }) CreateEndWall(root.transform, source, new Vector3(x, .4f, z));
            M4BuiltinContentPackager.BuildBuiltinPackage();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return backup;
        }

        /// <summary>Reconcile only this migration's appended exclusions; the original prefix is immutable.</summary>
        public static string RepairOwnedNetworkExclusions()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Reconciliation needs the clean idle main route.");
            const string beforePath = "Builds/SceneBackups/20261005-171751-665/10_EmberValley.unity";
            const string pattern = @"(?m)^  _offlineBehaviours:\r?\n(?<refs>(?:  - \{fileID: [0-9]+\}\r?\n)+)";
            var original = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(beforePath), pattern).Groups["refs"].Value.TrimEnd();
            var current = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(ScenePath), pattern).Groups["refs"].Value.TrimEnd();
            if (original.Length == 0 || !current.StartsWith(original + "\n", StringComparison.Ordinal) && !current.StartsWith(original + "\r\n", StringComparison.Ordinal))
                throw new InvalidOperationException("Original NET exclusions changed; preserve and inspect instead of removing anything.");
            int prefix = original.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var root = GameObject.Find(RootName);
            if (GameObject.Find(GuardRootName) != null)
                throw new InvalidOperationException("Original migration repair is closed after the shield combination; preserve later exclusions.");
            var serialized = new SerializedObject(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true));
            var list = serialized.FindProperty("_offlineBehaviours");
            var owned = root.GetComponentsInChildren<Behaviour>(true);
            int removed = 0, added = 0;
            for (int i = list.arraySize - 1; i >= prefix; i--)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                { list.DeleteArrayElementAtIndex(i); removed++; }
            foreach (Behaviour b in owned)
            {
                bool present = Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == b);
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = b; added++;
            }
            if (removed == 0 && added == 0) return "Owned exclusions already complete; no scene write.";
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-owned-exclusions.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(ScenePath, backup, false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed; backup preserved: " + backup);
            return "Original prefix=" + prefix + "; removed owned nulls=" + removed + "; added current owned behaviours=" + added + "; backup=" + backup;
        }

        [MenuItem("Emberfall/Content/Add Ash Caller Shield Combination")]
        public static string ApplyShieldCombination()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open the clean idle main route first.");
            var existing = GameObject.Find(GuardRootName);
            if (existing != null)
            {
                if (existing.GetComponentsInChildren<SummonerEnemyActor>(true).Length != 1 || existing.GetComponentsInChildren<ShieldEnemyActor>(true).Length != 1)
                    throw new InvalidOperationException("Partial shield combination preserved; inspect before repair.");
                return "Existing shield combination preserved; no regeneration.";
            }
            if (GameObject.Find(RootName) == null) throw new InvalidOperationException("Author and verify the first encounter first.");
            var shieldSource = Object.FindObjectsOfType<ShieldEnemyActor>().Single(a => a.name == "Enemy_RuinGuard_PreSanctum");
            if (new SerializedObject(shieldSource).FindProperty("_enemyId").stringValue != "enemy:ruin-guard")
                throw new InvalidOperationException("Use the ordinary guard, not the Scorched elite.");
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var network = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            var wall = GameObject.Find("Entry_East_1");
            Vector3 shieldPoint = new Vector3(-1.6f, 0, 21.2f), callerPoint = new Vector3(1.8f, 0, 24.2f);
            if (player == null || network == null || wall == null) throw new InvalidOperationException("Missing current route bindings.");
            foreach (Vector3 point in new[] { shieldPoint, callerPoint })
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, .25f, NavMesh.AllAreas) || (hit.position - point).sqrMagnitude > .0625f)
                    throw new InvalidOperationException("Shield combination spawn is not on the existing NavMesh: " + point);
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-ash-guard-pass.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(ScenePath, backup, false);
            var style = Require<SummonedMinionStyle>(ArtFolder + "/SummonedSpiritStyle.asset");
            // Use the existing derivative selected/rendered in the first formal encounter; never rewrite originals.
            var visual = Require<GameObject>(ArtFolder + "/P_AshCaller.prefab");
            var content = new GameObject(GuardRootName);
            try
            {
                var group = content.AddComponent<CombatEncounterCoordinator>();
                var guardRoot = Object.Instantiate(shieldSource.gameObject, content.transform); guardRoot.name = "Enemy_RuinGuard_AshGuardPass";
                guardRoot.transform.SetPositionAndRotation(shieldPoint, Quaternion.Euler(0, 180, 0));
                var guard = guardRoot.GetComponent<ShieldEnemyActor>(); guardRoot.GetComponent<EncounterLeash>().Configure(group);
                var caller = CreateCaller(content.transform, player, visual, style, callerPoint, "encounter:ash-guard-pass");
                caller.gameObject.AddComponent<EncounterLeash>().Configure(group);
                // Stop acquisition before the old north wall begins (25.50); its doorway is a real exit.
                group.Configure(player, null, new[] { guard }, null, 1, true, new Vector3(0, 0, 21.75f), new Vector2(4.2f, 3.35f), 3.2f, GuardSegment, .15f);
                group.ConfigureSummoners(new[] { caller }, true);
                foreach (float x in new[] { -2.9f, 2.9f }) CreateEndWall(content.transform, wall, new Vector3(x, .4f, 18.4f), "AshGuardPass");
                // North reuses the real Forest doorway at z25.5..26.5. A second wall there would overlap it.
                var serialized = new SerializedObject(network);
                var roots = serialized.FindProperty("_offlineActorRoots");
                roots.InsertArrayElementAtIndex(roots.arraySize); roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = content;
                var behaviours = serialized.FindProperty("_offlineBehaviours");
                foreach (Behaviour b in content.GetComponentsInChildren<Behaviour>(true))
                { behaviours.InsertArrayElementAtIndex(behaviours.arraySize); behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = b; }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                M4BuiltinContentPackager.BuildBuiltinPackage();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Shield combination save failed; backup: " + backup);
                return "Authored Summoner + ordinary shield; north doorway reused; backup=" + backup;
            }
            catch { EditorSceneManager.MarkSceneDirty(scene); throw; }
        }

        [MenuItem("Emberfall/Content/Add Ash Caller Return Combination")]
        public static string ApplyReturnCombination()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty || scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open the clean idle main route first.");
            // GameObject.Find omits the deliberately inactive return root.
            var existing = scene.GetRootGameObjects().SingleOrDefault(g => g.name == ReturnRootName);
            if (existing != null)
            {
                if (existing.GetComponentsInChildren<SummonerEnemyActor>(true).Length != 1 || existing.GetComponentsInChildren<RangedEnemyActor>(true).Length != 1)
                    throw new InvalidOperationException("Partial return combination preserved; inspect before repair.");
                return "Existing return combination preserved; no regeneration.";
            }
            var priestSource = Object.FindObjectsOfType<RangedEnemyActor>().Single(a => a.name == "Enemy_RunePriest_Forest");
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<Emberfall.Application.Flow.M2RouteFlowController>();
            var network = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            if (player == null || flow == null || network == null) throw new InvalidOperationException("Missing existing route bindings.");
            Vector3 callerPoint = new Vector3(19.2f, 0, 9.3f), priestPoint = new Vector3(21.5f, 0, 11.3f);
            foreach (Vector3 point in new[] { callerPoint, priestPoint })
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, .25f, NavMesh.AllAreas) || (hit.position - point).sqrMagnitude > .0625f)
                    throw new InvalidOperationException("Return spawn is not on existing NavMesh: " + point);
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(new Vector3(30.3f, 0, 10.2f), new Vector3(15.7f, 0, 10.2f), NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Keep the existing full return path; do not expand or bake around a failure.");
            var style = Require<SummonedMinionStyle>(ArtFolder + "/SummonedSpiritStyle.asset");
            var visual = Require<GameObject>(ArtFolder + "/P_AshCaller.prefab");
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-ash-return.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(ScenePath, backup, false); File.Copy(ScenePath + ".meta", backup + ".meta", false);
            var content = new GameObject(ReturnRootName); content.SetActive(false);
            try
            {
                var group = content.AddComponent<CombatEncounterCoordinator>();
                var priestRoot = Object.Instantiate(priestSource.gameObject, content.transform); priestRoot.name = "Enemy_RunePriest_AshReturn";
                priestRoot.transform.SetPositionAndRotation(priestPoint, Quaternion.Euler(0, 90, 0));
                var priest = priestRoot.GetComponent<RangedEnemyActor>(); priestRoot.GetComponent<EncounterLeash>().Configure(group);
                var caller = CreateCaller(content.transform, player, visual, style, callerPoint, "encounter:ash-return");
                caller.name = "Enemy_AshCaller_Return"; caller.transform.rotation = Quaternion.Euler(0, 90, 0);
                caller.gameObject.AddComponent<EncounterLeash>().Configure(group);
                group.Configure(player, null, null, new[] { priest }, 1, true, new Vector3(23, 0, 10.2f), new Vector2(6.5f, 2.3f), 3.2f, ReturnSegment, .15f);
                group.ConfigureSummoners(new[] { caller }, true);
                // Existing continuous Return_N/S masonry supplies the visible boundary; no new geometry.
                var serializedFlow = new SerializedObject(flow);
                serializedFlow.FindProperty("_ashReturnRoot").objectReferenceValue = content;
                serializedFlow.ApplyModifiedPropertiesWithoutUndo();
                var serializedNet = new SerializedObject(network);
                var roots = serializedNet.FindProperty("_offlineActorRoots");
                roots.InsertArrayElementAtIndex(roots.arraySize); roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = content;
                var behaviours = serializedNet.FindProperty("_offlineBehaviours");
                foreach (Behaviour b in content.GetComponentsInChildren<Behaviour>(true))
                { behaviours.InsertArrayElementAtIndex(behaviours.arraySize); behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = b; }
                serializedNet.ApplyModifiedPropertiesWithoutUndo();
                M4BuiltinContentPackager.BuildBuiltinPackage();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Return combination save failed; backup: " + backup);
                return "Authored initially inactive return Summoner + priest; existing route geometry/nav unchanged. Backup=" + backup;
            }
            catch { EditorSceneManager.MarkSceneDirty(scene); throw; }
        }

        private static void CreateEndWall(Transform parent, GameObject source, Vector3 center, string prefix = "AshApproach")
        {
            var wrapper = new GameObject(prefix + "_VisibleEndWall").transform; wrapper.SetParent(parent, false);
            // Existing low-poly masonry, renderer and matching collision; never an invisible blocker.
            GameObject copy = Object.Instantiate(source, wrapper); copy.name = "Masonry";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = source.transform.rotation;
            copy.transform.localScale = source.transform.lossyScale;
            wrapper.localRotation = Quaternion.Euler(0, 90, 0);
            wrapper.localScale = new Vector3(.5f, 1f, 2.6f / 3.5f);
            Collider collider = copy.GetComponentsInChildren<Collider>().Single(c => !c.isTrigger);
            Physics.SyncTransforms();
            wrapper.position += center - collider.bounds.center;
            Physics.SyncTransforms();
            if (Vector3.Distance(collider.bounds.size, new Vector3(2.6f, .8f, .5f)) > .001f)
                throw new InvalidOperationException("Cloned masonry dimensions do not match the approved footprint: " + collider.bounds.size);
            var cut = new GameObject(prefix + "_NavCut"); cut.transform.SetParent(parent, false); cut.transform.position = center;
            var obstacle = cut.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box; obstacle.center = Vector3.zero; obstacle.size = collider.bounds.size;
            obstacle.carving = true; obstacle.carveOnlyStationary = true;
        }
        private static T Require<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
    }
}
