using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class ArenaLayoutAudit
    {
        public static void Run()
        {
            var output = new StringBuilder();
            foreach (string scene in new[] { "10_EmberValley", "20_Sanctum", "90_CombatGym", "91_NetworkGym" })
            {
                EditorSceneManager.OpenScene($"Assets/_Game/Scenes/{scene}.unity");
                output.AppendLine("SCENE " + scene);
                foreach (Collider c in Object.FindObjectsOfType<Collider>(true))
                {
                    if (c.isTrigger || c is CharacterController || !c.enabled) continue;
                    output.AppendLine($"COLLIDER {PathOf(c.transform)} active={c.gameObject.activeInHierarchy} pos={c.transform.position:F2} scale={c.transform.lossyScale:F2} bounds={c.bounds} renderers=" +
                        string.Join(";", c.GetComponentsInChildren<Renderer>(true).Select(r => $"{r.name}:{r.enabled}:{r.bounds}")));
                }
                if (scene != "10_EmberValley") continue;
                foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
                    if (t.name.StartsWith("Forest") || t.name.StartsWith("Bridge") || t.name.StartsWith("Cover") ||
                        (t.parent != null && t.parent.name.StartsWith("[Art]")))
                        output.AppendLine($"PROP {PathOf(t)} pos={t.position:F2} scale={t.lossyScale:F2}");
            }
            Directory.CreateDirectory("Builds/ArtReview/0.8.10a");
            File.WriteAllText("Builds/ArtReview/0.8.10a/layout-audit.txt", output.ToString());
            Debug.Log("ARENA_LAYOUT_AUDIT_COMPLETE");
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}
