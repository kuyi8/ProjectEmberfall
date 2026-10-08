using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Pure inlaid choice plaques; keeps the old triggers, anchors and floating highlight semantics.</summary>
    public static class ChoiceMarkerArtSetup
    {
        public const string ChildName="[Art] Choice Stone Plaque";
        public const string AssetRoot="Assets/_Game/Art/ChoiceMarkers";
        public const string PrefabPath=AssetRoot+"/P_ChoiceStonePlaque.prefab";
        public const string SourceRoot="Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered/addons/kaykit_dungeon_remastered/Assets/";
        public const string Source=SourceRoot+"fbx/floor_tile_small_decorated.fbx";
        public const string StoneMaterial="Assets/_Game/Art/ImportedEnvironmentDressing/Materials/M_Dressing_Stone.mat";
        public static readonly string[] Names={"RuneChoice_Ember","RuneChoice_Guard","RouteChoice_Supply","RouteChoice_Risk","AshReinforcement_StagedReinforcement","AshReinforcement_TogetherReinforcement"};
        [Serializable] sealed class FileRow {public string path,before,after;}
        [Serializable] sealed class MaterialRow {public string guid,before,after;public long localId;}
        [Serializable] sealed class AnchorRow {public string name;public Vector3 position,scale;public Quaternion rotation;public int parent;public int[] children;}
        [Serializable] sealed class ComponentChange {public string path,type,before,after;public int instanceId;public bool permitted;}
        [Serializable] sealed class PreviewResult {public string scope="Fixed neutral URP mesh review, not gameplay/readability acceptance.",failure;public bool filesSame,sourceMaterialMemorySame,sceneMemorySame;public FileRow[] files;public MaterialRow[] materials;}
        [Serializable] sealed class FinalizationResult
        {
            public string scope="Correct only six owned plaque transforms: bottom is 5mm above the highest of nine authored-floor probes, top 25mm above those probes, not an entire-footprint clearance proof. Append exactly the two existing RuneChoice roots to existing NET isolation, preserve old roots prefix and whole behaviour list. All other old component JSON, source raw materials and protected file bytes unchanged. Not natural discovery, performance or a package.";
            public bool outsideComponentsSame,prefixSame,behavioursSame,filesSame,sourceMaterialMemorySame;
            public string netBefore,netAfter;public ComponentChange[] artChanges;public FileRow[] files;public MaterialRow[] materials;
        }
        [Serializable] sealed class Result
        {
            public string scope="Pure art migration of the exact six existing choice markers. Six old cylinder renderers disabled but mesh/materials/anchors/triggers/logic/highlight references retained. New thin plaques are decorative overlays, NOT solid pedestals, walkable terrain or new collision/navigation. Source files/material raw memory and old component JSON protected except exact six renderer.enabled, root child-array additions and appending two existing rune roots to NET isolation; root TRS/parent/original child prefixes and entire NET behaviour list separately checked. Not natural traversal/readability or new delivery acceptance.";
            public bool filesSame,sourceMaterialMemorySame,oldComponentsSame,anchorsSame;public int oldComponents,choiceCount;public FileRow[] files;public MaterialRow[] materials;public AnchorRow[] anchors;public ComponentChange[] changes;
        }

        public static string Preview(string label)
        {
            RequireIdle();RequireAssets();if(!Regex.IsMatch(label??"","^[A-Za-z0-9_-]{8,72}$"))throw new ArgumentException("Fresh ASCII label required.");
            string folder=Path.GetFullPath("Builds/ArtReview/choice-marker-candidates/"+label);if(Directory.Exists(folder))throw new IOException("Preserve previous preview.");Directory.CreateDirectory(folder);
            var files=Files();var watched=Materials();string memory=SceneMemory(SceneManager.GetActiveScene());
            string failure=null;
            try { foreach(string modelName in new[]{"floor_tile_small","floor_tile_small_decorated"})
            {
                var preview=new PreviewRenderUtility();
                try
                {
                    var root=EditorUtility.CreateGameObjectWithHideFlags("CandidatePlaque",HideFlags.HideAndDontSave);preview.AddSingleGO(root);
                    Clone(AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot+"fbx/"+modelName+".fbx").transform,root.transform);
                    var b=BoundsOf(root);root.transform.localScale=new Vector3(.86f/b.size.x,.02f/b.size.y,.86f/b.size.z);
                    b=BoundsOf(root);root.transform.position+=new Vector3(0,.015f,0)-new Vector3(b.center.x,b.max.y,b.center.z);
                    preview.lights[0].enabled=true;preview.lights[1].enabled=true;
                    preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(45,-35,0);preview.lights[1].intensity=.6f;
                    var camera=preview.camera;camera.scene=root.scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.19f,.25f,.28f);camera.orthographic=true;camera.orthographicSize=.64f;camera.nearClipPlane=.03f;camera.farClipPlane=20;
                    camera.transform.position=new Vector3(1.3f,1.8f,1.4f);camera.transform.LookAt(Vector3.zero);
                    Shot(camera,folder+"/"+modelName+".png");
                }
                finally{preview.Cleanup();}
            } }
            catch(Exception ex){failure=ex.ToString();throw;}
            finally
            {
                foreach(var f in files)f.after=Hash(f.path);foreach(var m in watched)m.Value.after=m.Key==null?"DESTROYED":EditorJsonUtility.ToJson(m.Key);
                var result=new PreviewResult{failure=failure,filesSame=files.All(f=>f.before==f.after),sourceMaterialMemorySame=watched.Values.All(m=>m.before==m.after),sceneMemorySame=memory==SceneMemory(SceneManager.GetActiveScene()),files=files,materials=watched.Values.ToArray()};
                File.WriteAllText(folder+"/protection.json",JsonUtility.ToJson(result,true));
                if(failure==null&&(!result.filesSame||!result.sourceMaterialMemorySame||!result.sceneMemorySame))throw new InvalidOperationException("Preview protection mismatch; preserve evidence="+folder);
            }
            File.WriteAllText(folder+"/scope.txt","Neutral URP mesh/texture review at .86m footprint, .02m depth; decorative top .015m above review zero. Fixed Editor preview only, no character/input/game HUD. Both source models, palette, license, owned material, nav/content bytes and actual source material JSON unchanged.");return folder;
        }

        public static string Apply()
        {
            RequireIdle();var scene=SceneManager.GetActiveScene();var anchors=FindAnchors(scene);
            if(anchors.All(a=>a.Find(ChildName)!=null)){Validate(scene);return "All six completed plaques preserved; no writes.";}
            if(anchors.Any(a=>a.Find(ChildName)!=null))throw new InvalidOperationException("Partial migration preserved; inspect before repair.");
            var legacy=anchors.Select(LegacyRenderer).ToArray();var anchorRows=anchors.Select(a=>new AnchorRow{name=a.name,position=a.localPosition,rotation=a.localRotation,scale=a.localScale,parent=a.parent==null?0:a.parent.GetInstanceID(),children=a.Cast<Transform>().Select(t=>t.GetInstanceID()).ToArray()}).ToArray();
            var net=NetworkAdapter(scene);var netData=new SerializedObject(net);var oldRoots=References(netData.FindProperty("_offlineActorRoots"));var oldBehaviours=References(netData.FindProperty("_offlineBehaviours"));
            var old=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
            var files=Files();var watched=Materials();string folder=Path.GetFullPath("Builds/SceneBackups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-pre-choice-plaques");Directory.CreateDirectory(folder);
            File.Copy(scene.path,folder+"/10_EmberValley.unity",false);File.Copy(scene.path+".meta",folder+"/10_EmberValley.unity.meta",false);
            foreach(var a in anchors)Floor(a);
            ApplyToScene(scene);Validate(scene);
            netData.Update();var newRoots=References(netData.FindProperty("_offlineActorRoots"));
            bool sameNet=newRoots.Take(oldRoots.Length).SequenceEqual(oldRoots)&&newRoots.Length==oldRoots.Length+anchors.Take(2).Count(a=>!oldRoots.Contains(a.gameObject))&&References(netData.FindProperty("_offlineBehaviours")).SequenceEqual(oldBehaviours);
            var changes=old.Select(p=>new ComponentChange{path=ComponentPath(p.Key),type=p.Key==null?"DESTROYED":p.Key.GetType().FullName,instanceId=p.Key==null?0:p.Key.GetInstanceID(),before=p.Value,after=p.Key==null?"DESTROYED":EditorJsonUtility.ToJson(p.Key),
                permitted=p.Key!=null&&(anchors.Contains(p.Key as Transform)||(p.Key==net&&sameNet)||(legacy.Contains(p.Key as Renderer)&&NormalizeEnabled(p.Value)==NormalizeEnabled(EditorJsonUtility.ToJson(p.Key))))})
                .Where(r=>r.before!=r.after).ToArray();
            bool components=changes.All(r=>r.permitted)&&changes.Count(r=>r.type==typeof(MeshRenderer).FullName)==6;
            bool sameAnchors=anchors.Select((a,i)=>a.localPosition==anchorRows[i].position&&a.localRotation==anchorRows[i].rotation&&a.localScale==anchorRows[i].scale&&
                (a.parent==null?0:a.parent.GetInstanceID())==anchorRows[i].parent&&a.Cast<Transform>().Take(anchorRows[i].children.Length).Select(t=>t.GetInstanceID()).SequenceEqual(anchorRows[i].children)).All(x=>x);
            foreach(var f in files)f.after=Hash(f.path);foreach(var m in watched)m.Value.after=m.Key==null?"DESTROYED":EditorJsonUtility.ToJson(m.Key);
            var result=new Result{filesSame=files.All(f=>f.before==f.after),sourceMaterialMemorySame=watched.Values.All(m=>m.before==m.after),oldComponentsSame=components,anchorsSame=sameAnchors,oldComponents=old.Count,choiceCount=anchors.Length,files=files,materials=watched.Values.ToArray(),anchors=anchorRows,changes=changes};
            File.WriteAllText(folder+"/protection.json",JsonUtility.ToJson(result,true));
            if(!result.filesSame||!result.sourceMaterialMemorySame||!components||!sameAnchors)throw new InvalidOperationException("Protection mismatch; do not save or restore unknowns; backup="+folder);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Preserve unsaved scene and backup="+folder);
            return folder;
        }

        public static int ApplyToScene(Scene scene)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Idle Edit mode required.");
            if(scene.name!="10_EmberValley")return 0;
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            if(!Names.All(n=>all.Any(t=>t.name==n)))return 0; // Earlier authoring cannot invent missing choices.
            var anchors=FindAnchors(scene);
            if(anchors.All(a=>a.Find(ChildName)!=null)){Validate(scene);return 0;}
            if(anchors.Any(a=>a.Find(ChildName)!=null))throw new InvalidOperationException("Partial plaques preserved.");
            RequireAssets();var prefab=EnsurePrefab();
            foreach(var anchor in anchors)
            {
                var old=LegacyRenderer(anchor);if(!old.enabled)throw new InvalidOperationException("Unexpected legacy state: "+anchor.name);
                float floor=Floor(anchor);
                var art=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);art.name=ChildName;art.transform.SetParent(anchor,false);
                art.transform.rotation=Quaternion.identity;art.transform.localScale=Vector3.one;
                var b=BoundsOf(art);var parentScale=anchor.lossyScale;
                if(parentScale.x<=0||parentScale.y<=0||parentScale.z<=0)throw new InvalidOperationException("Positive authored scale required.");
                // Bounds include the real inherited scale, so this factor directly produces
                // the target WORLD footprint/depth without moving the gameplay anchor.
                art.transform.localScale=new Vector3(.86f/b.size.x,.02f/b.size.y,.86f/b.size.z);
                b=BoundsOf(art);art.transform.position+=new Vector3(anchor.position.x,floor+.005f,anchor.position.z)-new Vector3(b.center.x,b.min.y,b.center.z);
                old.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(old);EditorUtility.SetDirty(old);
            }
            EnsureRuneIsolation(scene,anchors);Validate(scene);EditorSceneManager.MarkSceneDirty(scene);return 6;
        }
        public static void Validate(Scene scene)
        {
            RequireAssets();
            foreach(var a in FindAnchors(scene))
            {
                if(LegacyRenderer(a).enabled)throw new InvalidOperationException("Duplicate legacy visual: "+a.name);
                var art=a.Find(ChildName);if(art==null||a.Cast<Transform>().Count(t=>t.name==ChildName)!=1)throw new InvalidOperationException("One complete plaque per choice required.");
                ImportedEnvironmentDressingSetup.ValidatePureVisual(art.gameObject);
                var b=BoundsOf(art.gameObject);if(Mathf.Abs(b.size.x-.86f)>.001f||Mathf.Abs(b.size.z-.86f)>.001f||Mathf.Abs(b.size.y-.02f)>.001f)throw new InvalidOperationException("Thin overlay footprint mismatch.");
                if(Mathf.Abs(b.min.y-Floor(a)-.005f)>.001f)throw new InvalidOperationException("Plaque bottom must follow sampled solid floor, not the elevated navigation surface.");
                foreach(var r in art.GetComponentsInChildren<Renderer>(true))if(!r.enabled||r.sharedMaterial!=AssetDatabase.LoadAssetAtPath<Material>(StoneMaterial))throw new InvalidOperationException("Owned plaque material/visibility mismatch.");
            }
            var roots=References(new SerializedObject(NetworkAdapter(scene)).FindProperty("_offlineActorRoots"));
            foreach(var a in FindAnchors(scene).Take(2))if(roots.Count(r=>r==a.gameObject)!=1)throw new InvalidOperationException("Whole offline rune root isolation required.");
        }
        public static string FinalizePlaqueFloorAndIsolation()
        {
            RequireIdle();var scene=SceneManager.GetActiveScene();var anchors=FindAnchors(scene);var arts=anchors.Select(a=>a.Find(ChildName)).ToArray();
            if(arts.Any(a=>a==null))throw new InvalidOperationException("Complete six plaques required; preserve partial migration.");
            var net=NetworkAdapter(scene);var data=new SerializedObject(net);var roots=References(data.FindProperty("_offlineActorRoots"));var behaviours=References(data.FindProperty("_offlineBehaviours"));
            bool final=arts.Select((a,i)=>Mathf.Abs(BoundsOf(a.gameObject).min.y-Floor(anchors[i])-.005f)<.001f).All(x=>x);
            if(final&&anchors.Take(2).All(a=>roots.Contains(a.gameObject))){Validate(scene);return "Completed plaque floor and isolation preserved; no writes.";}
            string folder=Path.GetFullPath("Builds/SceneBackups/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-pre-plaque-finalization");Directory.CreateDirectory(folder);
            File.Copy(scene.path,folder+"/10_EmberValley.unity",false);File.Copy(scene.path+".meta",folder+"/10_EmberValley.unity.meta",false);
            var artTransforms=arts.SelectMany(a=>a.GetComponentsInChildren<Transform>(true)).ToArray();
            var outside=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c!=net&&!artTransforms.Contains(c as Transform)).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
            var artBefore=artTransforms.ToDictionary(t=>t,t=>EditorJsonUtility.ToJson(t));string netBefore=EditorJsonUtility.ToJson(net);var files=Files();var watched=Materials();
            for(int i=0;i<arts.Length;i++){var b=BoundsOf(arts[i].gameObject);arts[i].position+=Vector3.up*(Floor(anchors[i])+.005f-b.min.y);PrefabUtility.RecordPrefabInstancePropertyModifications(arts[i]);EditorUtility.SetDirty(arts[i]);}
            EnsureRuneIsolation(scene,anchors);Validate(scene);data.Update();var afterRoots=References(data.FindProperty("_offlineActorRoots"));
            foreach(var f in files)f.after=Hash(f.path);foreach(var m in watched)m.Value.after=m.Key==null?"DESTROYED":EditorJsonUtility.ToJson(m.Key);
            var result=new FinalizationResult{outsideComponentsSame=outside.All(p=>p.Key!=null&&p.Value==EditorJsonUtility.ToJson(p.Key)),prefixSame=afterRoots.Take(roots.Length).SequenceEqual(roots)&&afterRoots.Length==roots.Length+anchors.Take(2).Count(a=>!roots.Contains(a.gameObject)),behavioursSame=References(data.FindProperty("_offlineBehaviours")).SequenceEqual(behaviours),filesSame=files.All(f=>f.before==f.after),sourceMaterialMemorySame=watched.Values.All(m=>m.before==m.after),netBefore=netBefore,netAfter=EditorJsonUtility.ToJson(net),files=files,materials=watched.Values.ToArray(),
                artChanges=artBefore.Where(p=>p.Value!=EditorJsonUtility.ToJson(p.Key)).Select(p=>new ComponentChange{path=ComponentPath(p.Key),type=p.Key.GetType().FullName,instanceId=p.Key.GetInstanceID(),before=p.Value,after=EditorJsonUtility.ToJson(p.Key),permitted=arts.Contains(p.Key)}).ToArray()};
            File.WriteAllText(folder+"/protection.json",JsonUtility.ToJson(result,true));
            if(!result.outsideComponentsSame||!result.prefixSame||!result.behavioursSame||!result.filesSame||!result.sourceMaterialMemorySame||result.artChanges.Length!=6||result.artChanges.Any(c=>!c.permitted))throw new InvalidOperationException("Finalization protection mismatch; preserve unsaved scene and backup="+folder);
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Preserve finalization scene and backup="+folder);return folder;
        }
        static NetworkEmberValleyModeAdapter NetworkAdapter(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NetworkEmberValleyModeAdapter>(true)).Single();
        static Object[] References(SerializedProperty array)=>Enumerable.Range(0,array.arraySize).Select(i=>array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        static void EnsureRuneIsolation(Scene scene,Transform[] anchors)
        {
            var data=new SerializedObject(NetworkAdapter(scene));var roots=data.FindProperty("_offlineActorRoots");
            foreach(var a in anchors.Take(2))if(!References(roots).Contains(a.gameObject)){roots.InsertArrayElementAtIndex(roots.arraySize);roots.GetArrayElementAtIndex(roots.arraySize-1).objectReferenceValue=a.gameObject;}
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        static Renderer LegacyRenderer(Transform anchor)=>anchor.name.StartsWith("RuneChoice_")?anchor.GetComponent<Renderer>():anchor.Find("InteractionMarker").GetComponent<Renderer>();
        static void RequireAssets()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(StoneMaterial);
            if(material==null||material.shader==null||material.shader.name!="Universal Render Pipeline/Lit")throw new InvalidOperationException("Existing owned URP stone material required; no fallback.");
        }
        static float Floor(Transform anchor)
        {
            NavMeshHit nav;
            if(!NavMesh.SamplePosition(anchor.position,out nav,1.5f,NavMesh.AllAreas))throw new InvalidOperationException("Existing authored nav required: "+anchor.name);
            // The existing paving has small bevels. A centre normal alone is not
            // a reliable floor-height measurement; retain all physical geometry
            // and fit this flat decorative top above the highest of nine probes.
            float highest=float.NegativeInfinity;
            foreach(float x in new[]{-.43f,0,.43f})foreach(float z in new[]{-.43f,0,.43f})
            {
                var ground=Physics.RaycastAll(anchor.position+new Vector3(x,3,z),Vector3.down,5,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>IsAuthoredFloor(h.collider)).OrderBy(h=>h.distance).FirstOrDefault();
                if(ground.collider==null||Mathf.Abs(nav.position.y-ground.point.y)>.25f)
                    throw new InvalidOperationException("Existing solid floor plus adjacent nav required: "+anchor.name);
                highest=Mathf.Max(highest,ground.point.y);
            }
            return highest;
        }
        static bool IsAuthoredFloor(Collider collider)
        {
            var t=collider.transform;
            return (t.root.name==M6EnvironmentSetup.RootName&&t.parent!=null&&t.parent.name.Contains("_Paving_"))||
                collider.name=="Zone_Forest"||collider.name=="Zone_Courtyard"||collider.name=="Path_Bridge"||collider.name=="Path_Forest";
        }
        static Transform[] FindAnchors(Scene scene)
        {var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();return Names.Select(n=>all.Single(t=>t.name==n)).ToArray();}
        static string NormalizeEnabled(string json)=>Regex.Replace(json,"\"m_Enabled\"\\s*:\\s*(true|false|[01])(?=\\s*[,}])","\"m_Enabled\":false");
        static string ComponentPath(Component c){if(c==null)return "DESTROYED";var t=c.transform;string path=t.name;while(t.parent!=null){t=t.parent;path=t.name+"/"+path;}return path;}
        static void RequireIdle(){var s=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||s.isDirty||SceneManager.sceneCount!=1||s.path!=AshApproachEncounterSetup.ScenePath)throw new InvalidOperationException("One clean idle Valley required.");}
        static GameObject EnsurePrefab()
        {
            var found=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);if(found!=null){ImportedEnvironmentDressingSetup.ValidatePureVisual(found);return found;}
            Folder(AssetRoot);var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
            try{root=new GameObject("P_ChoiceStonePlaque");SceneManager.MoveGameObjectToScene(root,scene);Clone(AssetDatabase.LoadAssetAtPath<GameObject>(Source).transform,root.transform);ImportedEnvironmentDressingSetup.ValidatePureVisual(root);return PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);}
            finally{if(root!=null)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void Clone(Transform source,Transform parent)
        {
            var node=new GameObject(source.name).transform;node.SetParent(parent,false);node.localPosition=source.localPosition;node.localRotation=source.localRotation;node.localScale=source.localScale;
            var mesh=source.GetComponent<MeshFilter>();if(mesh!=null&&mesh.sharedMesh!=null){node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;var r=node.gameObject.AddComponent<MeshRenderer>();r.sharedMaterials=Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Material>(StoneMaterial),mesh.sharedMesh.subMeshCount).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=true;}
            foreach(Transform child in source)Clone(child,node);
        }
        static Bounds BoundsOf(GameObject root){var rs=root.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)throw new InvalidOperationException("Empty plaque.");var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static FileRow[] Files()
        {
            var paths=new[]{Source,SourceRoot+"fbx/floor_tile_small.fbx",SourceRoot+"texture/dungeon_texture.png",StoneMaterial,
                "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered/LICENSE.txt",
                "Assets/_Game/Settings/Navigation/EmberValleyNavMesh.asset","Assets/StreamingAssets/BuiltinContent/manifest.json"};
            return paths.Concat(Directory.GetFiles("Assets/_Game/Settings","*",SearchOption.AllDirectories)).SelectMany(p=>File.Exists(p+".meta")?new[]{p,p+".meta"}:new[]{p}).Distinct().OrderBy(p=>p).Select(p=>new FileRow{path=p,before=Hash(p)}).ToArray();
        }
        static Dictionary<Material,MaterialRow> Materials()=>new[]{Source,SourceRoot+"fbx/floor_tile_small.fbx"}.SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>()).Distinct().ToDictionary(m=>m,m=>{AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string guid,out long id);return new MaterialRow{guid=guid,localId=id,before=EditorJsonUtility.ToJson(m)};});
        static string SceneMemory(Scene scene)=>string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Select(c=>c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c)).OrderBy(x=>x));
        static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        static void Shot(Camera camera,string path)
        {
            var rt=RenderTexture.GetTemporary(640,640,24);var previous=RenderTexture.active;Texture2D image=null;
            try{RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;image=new Texture2D(640,640,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(image!=null)Object.DestroyImmediate(image);}
        }
    }
}
