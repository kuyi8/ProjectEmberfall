using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Review
{
    /// <summary>Scoped authoring audit, not a rendered or gameplay acceptance test.</summary>
    public static class SanctumSurfaceReview
    {
        [Serializable] sealed class Result
        {
            public string scope="Nine existing owned opaque materials plus one existing realtime light colour. Full frozen-file proof is separate/external. No render/gameplay/performance acceptance.", output, failure;
            public bool physicsSame, componentsSameExceptFillColour, sceneTwiceSame, materialsTwiceSame;
            public int renderersBefore, renderersAfter, lightsBefore, lightsAfter;
        }

        static string pending;
        public static string Begin(string label)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(label??"","^[a-zA-Z0-9][a-zA-Z0-9._-]*$"))
                throw new ArgumentException("Safe fresh label required.");
            if(pending!=null)throw new InvalidOperationException("An owned surface job is pending.");
            string output="Builds/ArtReview/sanctum-surfaces/"+label;
            if(!File.Exists(output+"/frozen-before.json")||File.Exists(output+"/result.json"))
                throw new InvalidOperationException("Fresh external before proof required; old results preserved.");
            pending=label;EditorApplication.update+=RunPending;
            return "Queued owned surface job: "+label;
        }

        static void RunPending()
        {
            EditorApplication.update-=RunPending;
            string label=pending;
            try{ApplyAndAudit(label);}
            catch(Exception ex){Debug.LogError("[SANCTUM_SURFACE_FAILED] "+label+" "+ex);}
            finally{pending=null;}
        }

        static string ApplyAndAudit(string label)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle required.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Preserve user scene edits.");
            string output="Builds/ArtReview/sanctum-surfaces/"+label;
            if(File.Exists(output+"/result.json"))throw new InvalidOperationException("Preserve existing job evidence.");
            const string scenePath="Assets/_Game/Scenes/20_Sanctum.unity";
            var allowed=WorldPresentationSetup.SanctumSurfaceNames.Select(n=>WorldPresentationSetup.AssetRoot+"/M_Sanctum_"+n+".mat").ToArray();
            File.Copy(scenePath,output+"/20_Sanctum-before.unity");
            foreach(string path in allowed)File.Copy(path,output+"/"+Path.GetFileName(path)+".before");
            var previous=EditorSceneManager.GetSceneManagerSetup();
            var result=new Result{output=output};
            try
            {
                var scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
                string physics=M6BoundaryArtSetup.CapturePhysics(scene),components=ProtectedComponents(scene);
                File.WriteAllText(output+"/physics-before.json",physics);
                File.WriteAllText(output+"/components-before.txt",components);
                result.renderersBefore=Count<Renderer>(scene);result.lightsBefore=Count<Light>(scene);
                WorldPresentationSetup.ApplySanctumSurfaces(scene);
                result.physicsSame=physics==M6BoundaryArtSetup.CapturePhysics(scene);
                result.componentsSameExceptFillColour=components==ProtectedComponents(scene);
                File.WriteAllText(output+"/physics-after.json",M6BoundaryArtSetup.CapturePhysics(scene));
                File.WriteAllText(output+"/components-after.txt",ProtectedComponents(scene));
                if(!result.physicsSame||!result.componentsSameExceptFillColour)
                    throw new InvalidOperationException("Unexpected component delta; target scene not saved.");
                EditorSceneManager.SaveScene(scene);
                string once=Hash(scenePath);
                var materialOnce=allowed.ToDictionary(p=>p,Hash);
                File.WriteAllLines(output+"/materials-once.txt",materialOnce.Select(p=>p.Value+" "+p.Key));
                WorldPresentationSetup.ApplySanctumSurfaces(scene);EditorSceneManager.SaveScene(scene);
                result.sceneTwiceSame=once==Hash(scenePath);
                result.materialsTwiceSame=materialOnce.All(p=>p.Value==Hash(p.Key));
                result.renderersAfter=Count<Renderer>(scene);result.lightsAfter=Count<Light>(scene);
                if(!result.sceneTwiceSame||!result.materialsTwiceSame||
                    result.renderersBefore!=result.renderersAfter||result.lightsBefore!=result.lightsAfter)
                    throw new InvalidOperationException("Surface authoring invariant failed; preserve evidence.");
                return output;
            }
            catch(Exception ex){result.failure=ex.ToString();throw;}
            finally
            {
                File.WriteAllText(output+"/result.json",JsonUtility.ToJson(result,true));
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        static int Count<T>(Scene s) where T:Component
        {return s.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<T>(true).Length);}

        static string ProtectedComponents(Scene scene)
        {
            return string.Join("\n",scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Component>(true))
                .Select(c=>{
                    if(c==null)throw new InvalidOperationException("Missing script.");
                    string value=EditorJsonUtility.ToJson(c);
                    if(c is Light&&c.name=="Sanctum Fill Light")
                    {
                        var json=JObject.Parse(value);
                        // Exactly the declared colour field, not intensity/range/shadows/transform.
                        var body=json["Light"] as JObject;
                        if(body==null||!body.Remove("m_Color"))throw new InvalidOperationException("Unexpected light serialization.");
                        value=json.ToString(Newtonsoft.Json.Formatting.None);
                    }
                    return EnvironmentKitReview.PathOf(c.transform)+"|"+c.GetType().FullName+"|"+value;
                }).OrderBy(x=>x,StringComparer.Ordinal));
        }
        static string Hash(string p)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    }
}
