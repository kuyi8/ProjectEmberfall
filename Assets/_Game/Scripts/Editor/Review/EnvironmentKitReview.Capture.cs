using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Editor.Setup;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class EnvironmentKitReview
    {
        private static bool _useArchivedBaseline=true;
        public static void CaptureRefinement()
        {
            _useArchivedBaseline=false;
            try { CaptureProduction(); EnvironmentNavigationBaseline.PrepareAndMeasure(); }
            finally { _useArchivedBaseline=true; }
        }
        [Serializable] public sealed class Result
        {
            public string scene,navAsset,output;
            public bool doubleSceneIdentical,doubleNavIdentical,protectedUnchanged,reopenedPersistentNav;
            public float areaBefore,areaAfter;
            public int renderersBefore,renderersAfter,collidersBefore,collidersAfter,meshSubmeshesAfter;
            public string drawCallScope="Renderer/submesh counts are structural budgets, not measured GPU/DrawCalls; batch performance remains pending.";
        }
        public static void CaptureProduction()
        {
            if(!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit Mode batch required.");
            string output=Evidence+"/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"); Directory.CreateDirectory(output);
            foreach(string name in M6EnvironmentSetup.Scenes)
            {
                string path="Assets/_Game/Scenes/"+name+".unity";
                var scene=EditorSceneManager.OpenScene(path);
                File.Copy(path,output+"/"+name+"-before.unity");
                string protection=CourtyardKitBakeoff.ProtectedState(scene);
                string physics=M6BoundaryArtSetup.CapturePhysics(scene);
                File.WriteAllText(output+"/"+name+"-physics-before.json",physics);
                File.WriteAllText(output+"/"+name+"-protected-before.txt",protection);
                var result=new Result {scene=path,output=output,areaBefore=Area(),renderersBefore=Object.FindObjectsOfType<Renderer>(true).Length,
                    collidersBefore=Object.FindObjectsOfType<Collider>(true).Length};
                string navPath="Assets/_Game/Settings/Navigation/"+(name=="90_CombatGym"?"CombatGym":"EmberValley")+"NavMesh.asset";
                File.Copy(navPath,output+"/"+name+"-nav-before.asset");
                var before=Shots(name,output,"before");
                M6EnvironmentSetup.ApplyToScene(scene); M6EnvironmentSetup.BakeNavigation(scene);
                AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
                var once=File.ReadAllBytes(path); var navOnce=File.ReadAllBytes(navPath);
                File.WriteAllBytes(output+"/"+name+"-nav-once.asset",navOnce);
                File.WriteAllText(output+"/"+name+"-nav-once.json",EditorJsonUtility.ToJson(Object.FindObjectOfType<NavMeshSurface>().navMeshData,true));
                M6EnvironmentSetup.ApplyToScene(scene); M6EnvironmentSetup.BakeNavigation(scene);
                AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
                result.doubleSceneIdentical=once.SequenceEqual(File.ReadAllBytes(path));
                result.doubleNavIdentical=navOnce.SequenceEqual(File.ReadAllBytes(navPath));
                File.Copy(navPath,output+"/"+name+"-nav-twice.asset");
                File.WriteAllText(output+"/"+name+"-nav-twice.json",EditorJsonUtility.ToJson(Object.FindObjectOfType<NavMeshSurface>().navMeshData,true));
                result.protectedUnchanged=protection==CourtyardKitBakeoff.ProtectedState(scene);
                result.areaAfter=Area(); result.navAsset=navPath;
                result.renderersAfter=Object.FindObjectsOfType<Renderer>(true).Length;
                result.collidersAfter=Object.FindObjectsOfType<Collider>(true).Length;
                result.meshSubmeshesAfter=Object.FindObjectsOfType<MeshFilter>(true).Where(f=>f.sharedMesh!=null).Sum(f=>f.sharedMesh.subMeshCount);
                File.WriteAllText(output+"/"+name+"-physics-after.json",M6BoundaryArtSetup.CapturePhysics(scene));
                File.WriteAllText(output+"/"+name+"-protected-after.txt",CourtyardKitBakeoff.ProtectedState(scene));
                var after=Shots(name,output,"after");
                var sheet=new Texture2D(1920,540*before.Length,TextureFormat.RGB24,false);
                for(int i=0;i<before.Length;i++) { int y=(before.Length-i-1)*540; sheet.SetPixels(0,y,960,540,before[i].GetPixels()); sheet.SetPixels(960,y,960,540,after[i].GetPixels()); }
                sheet.Apply(); File.WriteAllBytes(output+"/"+name+"-comparison.png",sheet.EncodeToPNG());
                foreach(var img in before.Concat(after))Object.DestroyImmediate(img); Object.DestroyImmediate(sheet);
                scene=EditorSceneManager.OpenScene(path);
                var surface=Object.FindObjectOfType<NavMeshSurface>();
                result.reopenedPersistentNav=surface!=null && surface.navMeshData!=null && EditorUtility.IsPersistent(surface.navMeshData) && AssetDatabase.GetAssetPath(surface.navMeshData)==navPath;
                File.WriteAllText(output+"/"+name+"-result.json",JsonUtility.ToJson(result,true));
                Debug.Log("[ENVIRONMENT_CAPTURE] "+JsonUtility.ToJson(result));
                if(!result.protectedUnchanged || !result.doubleSceneIdentical || !result.doubleNavIdentical || !result.reopenedPersistentNav)
                    throw new InvalidOperationException("Environment authoring invariant failed; see preserved result: "+output);
            }
            File.WriteAllText(Evidence+"/latest.txt",output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
        private static float Area()
        {
            var t=NavMesh.CalculateTriangulation(); float area=0;
            for(int i=0;i<t.indices.Length;i+=3)area+=Vector3.Cross(t.vertices[t.indices[i+1]]-t.vertices[t.indices[i]],t.vertices[t.indices[i+2]]-t.vertices[t.indices[i]]).magnitude*.5f;
            return area;
        }
        private static Texture2D[] Shots(string scene,string output,string label)
        {
            // Keep the actual pre-production baseline after corrective reruns, never relabel an intermediate as old art.
            string baseline=Evidence+"/20260928-175449-665";
            if(_useArchivedBaseline && scene=="10_EmberValley" && label=="before" && File.Exists(baseline+"/before-overview.png"))
                return new[]{"overview","entry","left","right"}.Select(n=>{
                    var image=new Texture2D(2,2,TextureFormat.RGB24,false);
                    image.LoadImage(File.ReadAllBytes(baseline+"/before-"+n+".png")); return image; }).ToArray();
            if(scene=="90_CombatGym")return new[]{Shot(output+"/"+scene+"-"+label+".png",new Vector3(17,20,-22),new Vector3(0,0,2),55)};
            return new[]{Shot(output+"/"+label+"-overview.png",new Vector3(52,65,-8),new Vector3(18,0,34),55),
                Shot(output+"/"+label+"-entry.png",new Vector3(0,3,23),new Vector3(0,1,37),60),
                Shot(output+"/"+label+"-left.png",new Vector3(22,5,54),new Vector3(12,1,44),60),
                Shot(output+"/"+label+"-right.png",new Vector3(27,4,43),new Vector3(39,1,31),60)};
        }
        internal static Texture2D Shot(string path,Vector3 p,Vector3 target,float fov)
        {
            var go=new GameObject("Environment review camera"); var cam=go.AddComponent<Camera>();
            if(Camera.main!=null)cam.CopyFrom(Camera.main);
            cam.enabled=false; cam.fieldOfView=fov; cam.nearClipPlane=.08f; cam.farClipPlane=200;
            cam.transform.position=p; cam.transform.LookAt(target); cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32); var prior=RenderTexture.active;
            var img=new Texture2D(960,540,TextureFormat.RGB24,false);
            try { rt.Create(); cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
                File.AppendAllText(Path.GetDirectoryName(path)+"/editor-render-counters.txt",Path.GetFileName(path)+
                    " EditorStats.drawCalls="+UnityStats.drawCalls+" batches="+UnityStats.batches+" setPass="+UnityStats.setPassCalls+
                    " (immediate Editor counter, not isolated camera/GPU timing; zero or stale values are not performance evidence)\n");
                img.ReadPixels(new Rect(0,0,960,540),0,0); img.Apply(); File.WriteAllBytes(path,img.EncodeToPNG()); return img; }
            finally { cam.targetTexture=null; RenderTexture.active=prior; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
        }
    }
}
