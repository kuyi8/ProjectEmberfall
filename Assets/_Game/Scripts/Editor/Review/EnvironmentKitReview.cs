using System;
using System.IO;
using System.Linq;
using Emberfall.Editor.Setup;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.Editor.Review
{
    public static partial class EnvironmentKitReview
    {
        public const string Evidence = "Builds/ArtReview/0.9.6-environment-production";
        public static void Inspect()
        {
            Directory.CreateDirectory(Evidence);
            CourtyardKitBakeoff.Measure();
            string[] names={"floor_tile_small_broken_B","floor_tile_small_weeds_B","barrel_small_stack"};
            var rows=names.Select(n=> { var p=CourtyardKitBakeoff.Kay(n); var m=CourtyardKitBakeoff.Load(p); var b=CourtyardKitBakeoff.BoundsOf(m);
                return new CourtyardKitBakeoff.Measurement { path=p,min=b.min,max=b.max,size=b.size,rootScale=m.transform.localScale,rootEuler=m.transform.eulerAngles }; }).ToArray();
            File.WriteAllText(Evidence+"/new-meshes.json",JsonUtility.ToJson(new CourtyardKitBakeoff.MeasureReport{meshes=rows},true));
            foreach(string name in new[]{"10_EmberValley","90_CombatGym","20_Sanctum"})
            {
                var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/"+name+".unity");
                var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Component>(true)).Where(c=>c!=null).ToArray();
                var surfaces=all.OfType<NavMeshSurface>().Select(s=>s.name+" data="+(s.navMeshData==null?"NULL":s.navMeshData.name)+
                    " persistent="+EditorUtility.IsPersistent(s.navMeshData)+" path="+AssetDatabase.GetAssetPath(s.navMeshData)+
                    " serialized="+EditorJsonUtility.ToJson(s)).ToArray();
                File.WriteAllLines(Evidence+"/"+name+"-navigation-before.txt",surfaces);
                File.WriteAllText(Evidence+"/"+name+"-physics-before.json",M6BoundaryArtSetup.CapturePhysics(scene));
                File.WriteAllLines(Evidence+"/"+name+"-objects-before.txt",all.OfType<Transform>()
                    .Where(t=>t.GetComponent<Collider>()!=null || t.name.Contains("Wall") || t.name.Contains("Seal") || t.name.Contains("Mechanism"))
                    .Select(t=>PathOf(t)+" pos="+t.position.ToString("F3")+" scale="+t.lossyScale.ToString("F3")+" rot="+t.eulerAngles.ToString("F3")));
                Debug.Log("[KIT_INSPECT] "+name+" "+string.Join("\n",surfaces));
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
        public static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
    }
}
