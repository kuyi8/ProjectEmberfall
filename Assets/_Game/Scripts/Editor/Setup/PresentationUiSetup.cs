using System;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class PresentationUiSetup
    {
        [MenuItem("Emberfall/UI/Apply Clean Player Facing Defaults")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Editor required.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Preserve unsaved scene work.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach(var path in new[]{"Assets/_Game/Scenes/10_EmberValley.unity","Assets/_Game/Scenes/90_CombatGym.unity"})
                {
                    var scene=EditorSceneManager.OpenScene(path); bool changed=false;
                    foreach(var overlay in UnityEngine.Object.FindObjectsOfType<InputTelemetryOverlay>(true))
                    {
                        var serialized=new SerializedObject(overlay);
                        var visible=serialized.FindProperty("_visible");
                        if(!visible.boolValue) continue;
                        visible.boolValue=false; serialized.ApplyModifiedPropertiesWithoutUndo(); changed=true;
                    }
                    if(changed) EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
    }
}
