using System;
using System.Linq;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Emberfall.Editor.Setup
{
    /// <summary>Idempotent repair of the shipped scene, without regenerating unrelated content.</summary>
    public static class M5dArenaRepair
    {
        [MenuItem("Emberfall/Setup/Repair Arena Layout 0.8.10a")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
            ApplyToLoadedScene();
            UnityEngine.Object.FindObjectOfType<NavMeshSurface>().BuildNavMesh();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_REPAIR_COMPLETE version=0.8.10a");
        }

        public static void ApplyToLoadedScene()
        {
            Material stone = Material("M_ArenaBoundaryStone", new Color(0.23f, 0.32f, 0.34f));
            Material trim = Material("M_ArenaBoundaryTrim", new Color(0.4f, 0.47f, 0.44f));
            Material barrier = Material("M_ArenaGateBarrier", new Color(0.08f, 0.65f, 0.8f, 0.62f), true);

            // Old greybox proxies have no relationship to the current rendered trees/columns.
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null) continue;
                if (t.name.StartsWith("Tree_West_") || t.name.StartsWith("Tree_East_") ||
                    t.name.StartsWith("CourtyardPillar_") || t.name.StartsWith("BridgeFence_"))
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            Transform marker = Find("Scout_EmberMarker");
            if (marker != null && marker.TryGetComponent(out Collider markerCollider))
                UnityEngine.Object.DestroyImmediate(markerCollider);
            Transform oldCampStone = Find("Camp_EmberStone");
            if (oldCampStone != null && oldCampStone.TryGetComponent(out Collider campCollider))
                UnityEngine.Object.DestroyImmediate(campCollider);

            foreach (EncounterBoundaryVisualMarker boundary in UnityEngine.Object.FindObjectsOfType<EncounterBoundaryVisualMarker>())
            {
                Transform t = boundary.transform;
                while (t.childCount > 0) UnityEngine.Object.DestroyImmediate(t.GetChild(0).gameObject);
                BoxCollider collider = t.GetComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.28f, 0f);
                collider.size = new Vector3(collider.size.x, 1.4f, 0.5f);
                GameObject visual = Cube(t, "VisibleStoneWall", collider.center, collider.size, stone);
                Cube(t, "WallCap", collider.center + Vector3.up * 0.63f,
                    new Vector3(collider.size.x, 0.14f, 0.56f), trim);
                // The visual is authored from exactly the collision dimensions, not FBX importer scale.
                boundary.Configure(boundary.Segment, boundary.Face, visual.GetComponent<Renderer>());
            }

            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name.StartsWith("GateBlocker_") && t.TryGetComponent(out Renderer r))
                {
                    r.sharedMaterial = barrier;
                    r.enabled = true;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // Open the combat center; diagonal cover remains useful without a narrow zigzag slalom.
            FitRock("ForestCoverRock_West", "CoverProxy_Forest_West", new Vector3(-5.7f, 0f, 30.8f), 1.3f);
            FitRock("ForestCoverRock_East", "CoverProxy_Forest_East", new Vector3(5.7f, 0f, 38.5f), 1.3f);
            for (int i = 0; i < 9; i++)
            {
                Move("ForestTree_West_" + i.ToString("00"), new Vector3(-14f - i % 2 * 3f, 0f, 10f + i * 4.6f), true);
                Move("ForestTree_East_" + i.ToString("00"), new Vector3(12f + i % 2 * 5f, 0f, 10f + i * 3.2f), true);
            }
            for (int i = 0; i < 9; i += 2)
            {
                Move("ForestBush_West_" + i.ToString("00"), new Vector3(-11.2f, 0f, 13f + i * 4.1f), true);
                Move("ForestRock_East_" + i.ToString("00"), new Vector3(11.5f, 0f, 11f + i * 3f), true);
            }
            Move("ForestFern_A", new Vector3(-8.8f, 0f, 28.5f), true);
            Move("ForestFern_B", new Vector3(9.2f, 0f, 35.5f), true);
            Move("BridgeDeadTree", new Vector3(10.8f, -0.2f, 49f), true);
            Move("CourtyardTree", new Vector3(23f, -0.2f, 55f), true);
            Move("RuneChoice_Ember", new Vector3(-4f, 0.5f, 24f), false);
            Move("RuneChoice_Guard", new Vector3(4f, 0.5f, 24f), false);
            // Separate the bridge seal from its entry mechanism instead of stacking both on the entry lane.
            Transform bridgeSeal = Find("Seal_Bridge");
            Vector3 sealPosition = new Vector3(15.5f, 1f, 41.2f);
            Vector3 sealDelta = sealPosition - bridgeSeal.position;
            bridgeSeal.position = sealPosition;
            Transform shell = Find("Seal_Bridge_StoneShell");
            if (shell != null && !shell.IsChildOf(bridgeSeal)) shell.position += sealDelta;
            Move("Enemy_RunePriest_Bridge_Right", new Vector3(13.3f, 0f, 43f), false);
            Surface("Zone_Forest", new Vector3(0f, -0.25f, 37f), new Vector3(20f, 0.5f, 20f));
            Surface("Path_Forest", new Vector3(0f, -0.25f, 18f), new Vector3(10f, 0.5f, 18f));
            Surface("Zone_Courtyard", new Vector3(27f, -0.25f, 44f), new Vector3(24f, 0.5f, 20f));

            // Non-wall interactables should not have invisible solid boxes above their small art.
            foreach (string n in new[] { "Seal_Forest", "Seal_Bridge", "Seal_Courtyard", "GateControl_Sanctum",
                         "RuneChoice_Ember", "RuneChoice_Guard", "Pickup_ForestSigil", "Checkpoint_Courtyard",
                         "TacticalPostureShrine_Courtyard" })
                if (Find(n) is Transform t && t.TryGetComponent(out Collider c)) c.isTrigger = true;
            FitSolidToVisual("SupplyCache_ForestBranch", "ForestSupplyCache_Art");

            foreach (CombatEncounterCoordinator coordinator in UnityEngine.Object.FindObjectsOfType<CombatEncounterCoordinator>())
            {
                var serialized = new SerializedObject(coordinator);
                foreach (string field in new[] { "_meleeEnemies", "_rangedEnemies", "_shieldEnemies" })
                {
                    SerializedProperty members = serialized.FindProperty(field);
                    for (int i = 0; i < members.arraySize; i++)
                    {
                        var member = members.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                        if (member == null) continue;
                        EncounterLeash leash = member.GetComponent<EncounterLeash>() ?? member.gameObject.AddComponent<EncounterLeash>();
                        leash.Configure(coordinator);
                        EditorUtility.SetDirty(leash);
                    }
                }
            }
        }

        private static Material Material(string name, Color color, bool transparent = false)
        {
            string path = "Assets/_Game/Art/Materials/M6Art/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(0.04f, 0.24f, 0.3f));
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static Transform Find(string name) => UnityEngine.Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t => t.name == name);

        private static void Move(string name, Vector3 position, bool ground)
        {
            Transform t = Find(name);
            if (t == null) return;
            t.position = position;
            if (ground) t.position += Vector3.up * (position.y - Bounds(t).min.y);
        }

        private static Bounds Bounds(Transform t)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private static void FitRock(string visual, string proxy, Vector3 position, float height)
        {
            Transform t = Find(visual);
            t.localScale *= height / Bounds(t).size.y;
            Move(visual, position, true);
            FitSolidToVisual(proxy, visual);
        }

        private static void FitSolidToVisual(string proxy, string visual)
        {
            Transform t = Find(proxy);
            Bounds bounds = Bounds(Find(visual));
            BoxCollider box = t.GetComponent<BoxCollider>();
            if (box == null) return;
            // Keep referenced interaction roots stationary; author the collider in their local space.
            box.center = t.InverseTransformPoint(bounds.center);
            Vector3 scale = t.lossyScale;
            box.size = new Vector3(bounds.size.x / scale.x, bounds.size.y / scale.y, bounds.size.z / scale.z);
            if (t.TryGetComponent(out NavMeshObstacle obstacle))
            {
                obstacle.center = box.center;
                obstacle.size = box.size;
            }
        }

        private static void Surface(string name, Vector3 position, Vector3 size)
        {
            Transform t = Find(name) ?? throw new InvalidOperationException("Missing surface: " + name);
            t.position = position;
            t.localScale = size;
        }
    }
}
