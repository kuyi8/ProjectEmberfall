using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static class WorldPresentationReview
    {
        [Serializable] sealed class Result
        {
            public string scene,scope="Fixed-camera Editor images and scene/asset invariants, NOT natural camera, combat or Player performance acceptance.";
            public bool physicsSame,originalObjectsSame,sceneIdempotent,assetsIdempotent,frozenSame;
            public int renderersBefore,renderersAfter,newColliders,newBehaviours;
        }

        public static string ApplyAndCapture()
        {
            var active=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||active.isDirty||SceneManager.sceneCount!=1||
                active.path!="Assets/_Game/Scenes/10_EmberValley.unity")throw new InvalidOperationException("Single clean idle Valley required.");
            string output="Builds/ArtReview/world-identity/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(output);
            var previous=EditorSceneManager.GetSceneManagerSetup();
            var frozen=Directory.GetFiles("Assets/_Game/Settings","*",SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/_Game/Prefabs/Characters","*",SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/_Game/Resources/Networking","*",SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/_Game/Art/Animations","*",SearchOption.AllDirectories))
                .Concat(new[]{"Assets/_Game/Scenes/90_CombatGym.unity","Assets/_Game/Scenes/91_NetworkGym.unity",
                    M6EnvironmentSetup.KitRoot+"texture/dungeon_texture.png",M6EnvironmentSetup.KitRoot+"texture/dungeon_texture.png.meta"})
                .ToDictionary(p=>p,Hash);
            File.WriteAllLines(output+"/frozen-before.txt",frozen.Select(p=>p.Value+" "+p.Key));
            try
            {
                foreach(string name in WorldPresentationSetup.Scenes)
                {
                    string path="Assets/_Game/Scenes/"+name+".unity";var scene=EditorSceneManager.OpenScene(path);
                    File.Copy(path,output+"/"+name+"-before.unity");
                    string physics=M6BoundaryArtSetup.CapturePhysics(scene),objects=CaptureOriginal(scene);
                    File.WriteAllText(output+"/"+name+"-physics-before.json",physics);
                    File.WriteAllText(output+"/"+name+"-objects-before.txt",objects);
                    var result=new Result{scene=name,renderersBefore=scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<Renderer>(true).Length)};
                    Shot(scene,output+"/"+name+"-before.png");
                    WorldPresentationSetup.ApplyToScene(scene);
                    result.physicsSame=physics==M6BoundaryArtSetup.CapturePhysics(scene);
                    result.originalObjectsSame=objects==CaptureOriginal(scene);
                    File.WriteAllText(output+"/"+name+"-physics-after.json",M6BoundaryArtSetup.CapturePhysics(scene));
                    File.WriteAllText(output+"/"+name+"-objects-after.txt",CaptureOriginal(scene));
                    if(!result.physicsSame||!result.originalObjectsSame)throw new InvalidOperationException("Protected delta; target scene NOT saved: "+name);
                    EditorSceneManager.SaveScene(scene);string once=Hash(path);
                    var owned=Directory.GetFiles(WorldPresentationSetup.AssetRoot,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Hash);
                    File.WriteAllLines(output+"/"+name+"-owned-once.txt",owned.Select(p=>p.Value+" "+p.Key));
                    WorldPresentationSetup.ApplyToScene(scene);EditorSceneManager.SaveScene(scene);
                    result.sceneIdempotent=Hash(path)==once;result.assetsIdempotent=owned.All(p=>Hash(p.Key)==p.Value);
                    File.WriteAllLines(output+"/"+name+"-owned-delta.txt",owned.Where(p=>Hash(p.Key)!=p.Value).Select(p=>p.Key));
                    var art=scene.GetRootGameObjects().SingleOrDefault(r=>r.name==WorldPresentationSetup.RootName);
                    result.newColliders=art==null?0:art.GetComponentsInChildren<Collider>(true).Length;
                    result.newBehaviours=art==null?0:art.GetComponentsInChildren<MonoBehaviour>(true).Length;
                    result.renderersAfter=scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<Renderer>(true).Length);
                    result.frozenSame=frozen.All(p=>Hash(p.Key)==p.Value);
                    Shot(scene,output+"/"+name+"-after.png");
                    File.WriteAllText(output+"/"+name+"-result.json",JsonUtility.ToJson(result,true));
                    if(!result.sceneIdempotent||!result.assetsIdempotent||!result.frozenSame||result.newColliders!=0||result.newBehaviours!=0)
                        throw new InvalidOperationException("World authoring invariant failed; retain evidence: "+output);
                }
                File.WriteAllText(output+"/complete.txt","Two formal scenes; physics/old transforms/old behaviours/nav/source assets unchanged; repeated scene/assets byte-identical. Fixed Editor views only.");
                return output;
            }
            finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
        }

        static string CaptureOriginal(Scene scene)
        {
            var rows=new List<string>();
            foreach(var root in scene.GetRootGameObjects().Where(r=>r.name!=WorldPresentationSetup.RootName))
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string path=EnvironmentKitReview.PathOf(t);
                    rows.Add(path+"|transform|"+JsonUtility.ToJson(t.localToWorldMatrix)+"|"+t.gameObject.activeSelf);
                    foreach(var behaviour in t.GetComponents<Behaviour>())
                        rows.Add(path+"|"+behaviour.GetType().FullName+"|"+EditorJsonUtility.ToJson(behaviour));
                    foreach(var body in t.GetComponents<Rigidbody>())rows.Add(path+"|body|"+EditorJsonUtility.ToJson(body));
                }
            rows.Sort(StringComparer.Ordinal);return string.Join("\n",rows);
        }

        public static string CaptureFormalLightingPair()
        {
            var valley=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||valley.isDirty||SceneManager.sceneCount!=1||
                valley.path!="Assets/_Game/Scenes/10_EmberValley.unity")throw new InvalidOperationException("Single clean idle Valley required.");
            const string baseline="Builds/ArtReview/world-identity/20261002-043451-725/20_Sanctum-before.unity";
            const string current="Assets/_Game/Scenes/20_Sanctum.unity";
            string oldValley=Hash(valley.path),oldSanctum=Hash(current);
            string dir="Builds/ArtReview/world-identity/formal-lighting-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string temp="Assets/_Game/Scenes/__WorldIdentityBaseline_"+Guid.NewGuid().ToString("N")+".unity";
            Directory.CreateDirectory(dir);var previous=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                // Exact retained pre-pass backup, never a reconstructed old appearance.
                File.Copy(baseline,temp);AssetDatabase.ImportAsset(temp,ImportAssetOptions.ForceSynchronousImport);
                var before=EditorSceneManager.OpenScene(temp,OpenSceneMode.Additive);SceneManager.SetActiveScene(valley);
                Shot(before,dir+"/before.png");EditorSceneManager.CloseScene(before,true);
                var after=EditorSceneManager.OpenScene(current,OpenSceneMode.Additive);SceneManager.SetActiveScene(valley);
                Shot(after,dir+"/after.png");EditorSceneManager.CloseScene(after,true);
                if(Hash(valley.path)!=oldValley||Hash(current)!=oldSanctum||valley.isDirty)
                    throw new InvalidOperationException("Source changed during read-only composition review.");
                File.WriteAllText(dir+"/provenance.txt","Same fixed view, actual10+20 additive assets/lights with10 active; baseline="+baseline+
                    "; after="+current+". No actors/AI/input/game HUD; NOT actual PlayerLoop, natural camera or performance acceptance. Both original scene bytes unchanged.");
                return dir;
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previous);
                // Only the exact owned temporary copy created above, never a source scene.
                if(AssetDatabase.LoadAssetAtPath<SceneAsset>(temp)!=null)AssetDatabase.DeleteAsset(temp);
            }
        }
        static void Shot(Scene scene,string path)
        {
            // URP lazily adds AdditionalLightData when a legacy point light is first
            // rendered. Preserve original components; remove only this capture's
            // newly-added render metadata before protection checks or scene saves.
            var original=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(true))
                .Select(c=>c.GetInstanceID()).ToHashSet();
            Vector3 p,target;
            if(scene.name=="10_EmberValley"){p=new Vector3(0,3.4f,-4.5f);target=new Vector3(0,1.2f,12);}
            else{p=new Vector3(1000,-76.8f,992.8f);target=new Vector3(1000,-79,1005);}
            try{var image=EnvironmentKitReview.Shot(path,p,target,60);Object.DestroyImmediate(image);}
            finally
            {
                foreach(var data in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(true)).ToArray())
                    if(!original.Contains(data.GetInstanceID()))Object.DestroyImmediate(data);
            }
        }
        static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    }
}
