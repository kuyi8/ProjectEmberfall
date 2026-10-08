using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    /// <summary>Hide source-kit alternatives only when the project has equipped BOTH hands.
    /// Keep all source transforms/bones/collision intact; never edit source FBX or its importer.</summary>
    public static class CharacterEquipmentSelection
    {
        private static readonly HashSet<string> Alternatives = new HashSet<string>(StringComparer.Ordinal)
        { "1H_Sword_Offhand", "1H_Sword", "2H_Sword", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield" };

        public static int Apply(GameObject root)
        {
            int changed=0;
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                Transform[] descendants=animator.GetComponentsInChildren<Transform>(true);
                bool sword=descendants.Any(t=>t.name=="Sword_M5c_Scorched_Equipped");
                bool shield=descendants.Any(t=>t.name=="Shield_Wooden_Equipped");
                if(!sword || !shield) continue;
                foreach(Renderer renderer in animator.GetComponentsInChildren<Renderer>(true))
                {
                    if(!Alternatives.Contains(renderer.name) ||
                        (renderer.transform.parent.name!="handslot.l" && renderer.transform.parent.name!="handslot.r") ||
                        !renderer.enabled) continue;
                    renderer.enabled=false;
                    if(PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    changed++;
                }
            }
            return changed;
        }

        [MenuItem("Emberfall/Art/Apply Explicit Equipped Character Visibility")]
        public static string ApplyProject()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play/compilation before authoring equipment visibility.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene work must be preserved.");
            var audit=new List<string>();
            // Audit both assembly paths, including every registered network enemy prefab.
            foreach(string path in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/_Game/Prefabs/Characters","Assets/_Game/Resources/Networking"})
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p))
            {
                var contents=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changed=Apply(contents); audit.Add(path+" hidden="+changed);
                    if(changed>0) PrefabUtility.SaveAsPrefabAsset(contents,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            try
            {
                foreach(string path in new[]{"Assets/_Game/Scenes/10_EmberValley.unity","Assets/_Game/Scenes/20_Sanctum.unity",
                    "Assets/_Game/Scenes/90_CombatGym.unity","Assets/_Game/Scenes/91_NetworkGym.unity"})
                {
                    var scene=EditorSceneManager.OpenScene(path);
                    int changed=scene.GetRootGameObjects().Sum(Apply);
                    audit.Add(path+" hidden="+changed);
                    if(changed>0) EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
            AssetDatabase.SaveAssets();
            return string.Join("\n",audit);
        }
    }
}
