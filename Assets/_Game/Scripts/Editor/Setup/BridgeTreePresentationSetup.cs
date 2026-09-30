using System.Linq;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class BridgeTreePresentationSetup
    {
        public static void ApplyToScene(Scene scene)
        {
            if (scene.name != "10_EmberValley") return;
            var tree = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CameraOccluder>(true))
                .Single(x => x.name == "BridgeDeadTree");
            // Approved absolute position; preserve ground height and do not accumulate offsets.
            var position = tree.transform.position;
            tree.transform.position = new Vector3(9.3f, position.y, 50.5f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tree.transform);
            EditorUtility.SetDirty(tree.transform);
        }
    }
}
