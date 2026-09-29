using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Isolated measured environment candidate; does not modify third-party imports.</summary>
    public static class CourtyardKitBakeoff
    {
        public const string SourceScene = "Assets/_Game/Scenes/10_EmberValley.unity";
        public const string KitRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/";
        public const string KayRoot = KitRoot + "KayKit_Dungeon_Remastered/addons/kaykit_dungeon_remastered/Assets/";
        public const string VillageRoot = KitRoot + "Medieval_Village_MegaKit/";
        public const string OutputRoot = M6EnvironmentSetup.AssetRoot;
        [Serializable] public sealed class Measurement
        {
            public string path;
            public Vector3 rootScale, rootEuler, min, max, size;
            public string[] materialSlots;
        }
        [Serializable] public sealed class MeasureReport { public Measurement[] meshes; }
        [Serializable] private sealed class CandidateResult
        {
            public string candidate = CandidateScene;
            public bool twoApplicationsByteIdentical, sourceSceneUnchanged, protectedUnchanged;
            public Vector3 encounterCenter;
            public Vector2 encounterHalfExtents;
            public float encounterActivationMargin;
        }
        public static string Kay(string name) => KayRoot + "fbx/" + name + ".fbx";
        public static Bounds BoundsOf(GameObject model)
        {
            bool found = false; Bounds bounds = default;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                // Include the FBX root's authored axis/scale conversion (Village uses scale100 and -90deg).
                var matrix = filter.transform.localToWorldMatrix;
                var b = filter.sharedMesh.bounds;
                for (int mask = 0; mask < 8; mask++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1)));
                    if (!found) { bounds = new Bounds(p,Vector3.zero); found=true; } else bounds.Encapsulate(p);
                }
            }
            if (!found) throw new InvalidDataException("No meshes: " + model.name);
            return bounds;
        }
        public static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path)
            ?? throw new FileNotFoundException("Missing imported model",path);
        public static void Measure()
        {
            var names = new[] {"wall","wall_corner","wall_endcap","wall_Tsplit","wall_doorway","wall_doorway_sides",
                "wall_half","wall_half_endcap","wall_broken","wall_cracked","wall_arched","floor_tile_large",
                "floor_tile_small","floor_tile_small_corner","floor_tile_small_broken_A","floor_tile_small_weeds_A",
                "floor_dirt_large","stairs_walled","column","pillar","pillar_decorated","rubble_large","rubble_half",
                "torch_lit","torch_mounted","barrel_large","box_stacked","banner_red"};
            var paths = names.Select(Kay).Concat(new[] {"Wall_Plaster_Straight","Corner_Exterior_Brick","DoorFrame_Round_Brick"}
                .Select(n=>VillageRoot+"FBX/"+n+".fbx"));
            var rows = paths.Select(path => { var model=Load(path); var b=BoundsOf(model);
                return new Measurement { path=path,rootScale=model.transform.localScale,rootEuler=model.transform.localEulerAngles,min=b.min,max=b.max,size=b.size,
                    materialSlots=model.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                        .Select(m=>m==null?"null":m.name).Distinct().ToArray() }; }).ToArray();
            Directory.CreateDirectory("Builds/ArtReview/0.9.6-kit-measure");
            File.WriteAllText("Builds/ArtReview/0.9.6-kit-measure/meshes.json",JsonUtility.ToJson(new MeasureReport{meshes=rows},true));
            Debug.Log("[KIT_MEASURE] meshes="+rows.Length);
        }

        public const string CandidateScene = "Assets/_Game/Scenes/Review/91_CourtyardKitCandidate.unity";
        private const string GeneratedRoot = "[Art] Courtyard Kit Bakeoff";
        private static Material _kitMaterial, _floorMaterial;
        private static readonly HashSet<string> Expected = new HashSet<string>();
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/'); Folder(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static Material Material(string name, string texturePath, Color tint)
        {
            Folder(OutputRoot);
            string path=OutputRoot+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor",tint); material.SetFloat("_Smoothness",.12f);
            EditorUtility.SetDirty(material); return material;
        }
        private static Transform Find(Scene scene,string name) => scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name);
        private static GameObject Piece(Transform root,string name,string asset,Vector3 p,float yaw,Vector3 scale,bool solid,Material material)
        {
            Expected.Add(name);
            var existing=root.Find(name);
            var wrapper=existing!=null?existing.gameObject:new GameObject(name);
            wrapper.transform.SetParent(root,false); wrapper.transform.SetLocalPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
            wrapper.transform.localScale=scale;
            if (wrapper.transform.childCount==0)
            {
                var child=Object.Instantiate(Load(asset),wrapper.transform,false); child.name="Model";
            }
            foreach (var renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            foreach (var filter in wrapper.GetComponentsInChildren<MeshFilter>(true))
            {
                var collider=filter.GetComponent<MeshCollider>();
                if (solid) { if(collider==null) collider=filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=filter.sharedMesh; collider.convex=false; }
                else if(collider!=null) Object.DestroyImmediate(collider);
            }
            return wrapper;
        }
        public static void ApplyToScene(Scene scene)
        {
            if(scene.path!=CandidateScene) throw new InvalidOperationException("Bake-off is restricted to its isolated candidate scene.");
            _kitMaterial=Material("M_KayKit_Courtyard",KayRoot+"texture/dungeon_texture.png",Color.white);
            _floorMaterial=Material("M_KayKit_Paving",KayRoot+"texture/dungeon_texture.png",new Color(.91f,.93f,.87f));
            var old=Find(scene,GeneratedRoot);
            var root=old!=null?old:new GameObject(GeneratedRoot).transform;
            SceneManager.MoveGameObjectToScene(root.gameObject,scene); Expected.Clear();
            // This is the explicitly authorized courtyard group, never other encounter boundaries or any gate.
            foreach(var marker in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EncounterBoundaryVisualMarker>(true)).ToArray())
                if(marker.Segment=="courtyard-encounter") Object.DestroyImmediate(marker.gameObject);
            var north=Find(scene,"Courtyard_NorthWall"); if(north!=null) Object.DestroyImmediate(north.gameObject);
            // Retain the existing support floor; cover it at +.025m to avoid coplanar surfaces.
            // Keep the visible support under broken/corner tiles and along the exterior strip: no invisible floor.
            var floor=Find(scene,"Zone_Courtyard"); if(floor!=null) floor.GetComponent<Renderer>().enabled=true;
            void K(string name,string mesh,float x,float z,float yaw=0,float sx=1,float sy=1,float sz=1,bool solid=true,float y=0)
                =>Piece(root,name,Kay(mesh),new Vector3(x,y,z),yaw,new Vector3(sx,sy,sz),solid,_kitMaterial);
            // U shell: 20m x16m. 4m measured pitch; real corner offsets extend 2m into each adjoining run.
            K("Wall_NW","wall_corner",18,52,90); K("Wall_NE","wall_corner",38,52,180);
            K("Wall_SW","wall_corner",18,36); K("Wall_SE_Pier","pillar",38,36);
            foreach(float x in new[]{22f,30f,34f}) K("Wall_N_"+x,"wall",x,52);
            foreach(float z in new[]{40f,44f,48f}) K("Wall_E_"+z,"wall",38,z,90);
            K("Wall_E_Foot","wall_half",38,36,90);
            // The native wall_doorway contains a CLOSED wooden leaf. Reject it for a traversable opening.
            // Assemble real endcaps and a lintel from source meshes; never hide a still-solid door.
            void Portal(string name,Vector3 position,float yaw)
            {
                var rotation=Quaternion.Euler(0,yaw,0);
                foreach(int side in new[]{-1,1})
                    Piece(root,name+"_Jamb_"+side,Kay("wall_half_endcap"),position+rotation*new Vector3(side*2,0,0),
                        yaw+(side<0?180:0),Vector3.one,true,_kitMaterial);
                Piece(root,name+"_Lintel",Kay("wall"),position+Vector3.up*3.25f,yaw,new Vector3(1,.1875f,1),true,_kitMaterial);
            }
            Portal("Door_Entry",new Vector3(18,0,42),90);
            K("Wall_W_North","wall",18,48,90);
            K("Wall_S_22","wall",22,36); K("Wall_S_26","wall",26,36);
            K("Wall_S_Half","wall_half",30,36);
            Portal("Door_Exit",new Vector3(34,0,36),0);
            // A rear T junction terminates in a visible buttress; no disconnected half wall in the arena.
            K("Rear_T","wall_Tsplit",26,52);
            K("Rear_T_End","wall_endcap",26,54,90);
            // Grid fills the arena while 2m corner tiles give broken edge transitions at the entry strip.
            for(int ix=0;ix<5;ix++) for(int iz=0;iz<4;iz++)
                Piece(root,$"Paving_{ix}_{iz}",Kay("floor_tile_large"),new Vector3(20+ix*4,-.025f,38+iz*4),0,Vector3.one,true,_floorMaterial);
            for(int iz=0;iz<4;iz++)
                Piece(root,"EntryPaving_"+iz,Kay(iz==0?"floor_tile_small_corner":iz==3?"floor_tile_small_broken_A":"floor_tile_small"),
                    new Vector3(17,-.025f,39+iz*2),0,Vector3.one,true,_floorMaterial);
            // Human-use clusters, confined to the rear corners rather than scattered through the battle lane.
            K("Rear_Rubble_W","rubble_half",23,50,0,.6f,.6f,.6f);
            K("Rear_Rubble_E","rubble_large",35,50,0,.4f,.4f,.4f);
            K("Supply_Barrels","barrel_small_stack",20,49,0,.55f,.55f,.55f);
            K("Supply_Crates","box_stacked",20,46.8f,25,.45f,.45f,.45f);
            K("Rear_Column_W","pillar_decorated",20,52,0,1,1.35f,1);
            K("Rear_Column_E","pillar_decorated",36,52,0,1,1.35f,1);
            K("Entry_Banner","banner_red",18.6f,45.2f,90,.75f,.75f,.75f,false);
            K("Exit_Banner","banner_red",30.5f,36.6f,0,.75f,.75f,.75f,false);
            K("Torch_Entry","torch_lit",19,45.2f,0,.8f,.8f,.8f,false,2);
            K("Torch_Exit","torch_lit",30.6f,37,0,.8f,.8f,.8f,false,2);
            // Existing tree geometry becomes a rear group; no imported tree assets or outside physics edits.
            var tree=Find(scene,"CourtyardTree");
            if(tree!=null && !root.Find("BackdropTree_A"))
            {
                for(int i=0;i<3;i++)
                {
                    var clone=Object.Instantiate(tree.gameObject,root,false); clone.name="BackdropTree_"+(char)('A'+i);
                    clone.transform.position=new Vector3(21+i*7,-.2f,55.8f+(i%2));
                    foreach(var collider in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                    foreach(var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                }
            }
            for(int i=0;i<3;i++) Expected.Add("BackdropTree_"+(char)('A'+i));
            var seal=Find(scene,"Seal_Forest");
            if(seal!=null)
            {
                Vector3 delta=new Vector3(31,1,39)-seal.position; seal.position+=delta;
                var shell=Find(scene,"Seal_Forest_StoneShell"); if(shell!=null&&!shell.IsChildOf(seal)) shell.position+=delta;
            }
            // Remove only obsolete objects inside this generator's own root, keeping file IDs on a second application.
            foreach(Transform t in root.Cast<Transform>().ToArray()) if(!Expected.Contains(t.name)) Object.DestroyImmediate(t.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void Capture()
        {
            if(!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Isolated Edit Mode batch only.");
            Measure(); Folder("Assets/_Game/Scenes/Review");
            string output="Builds/ArtReview/0.9.6-courtyard-kit-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            byte[] sourceBytes=File.ReadAllBytes(SourceScene);
            var scene=EditorSceneManager.OpenScene(SourceScene);
            string physicsBefore=M6BoundaryArtSetup.CapturePhysics(scene);
            string protectedBefore=ProtectedState(scene);
            var before=RenderPair(output,"before");
            // SaveAs leaves the shipping scene untouched; this candidate is not in Build Settings.
            EditorSceneManager.SaveScene(scene,CandidateScene);
            ApplyToScene(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            byte[] once=File.ReadAllBytes(CandidateScene);
            string physicsAfter=M6BoundaryArtSetup.CapturePhysics(scene);
            ApplyToScene(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            bool identical=once.SequenceEqual(File.ReadAllBytes(CandidateScene));
            string protectedAfter=ProtectedState(scene);
            var coordinator=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CombatEncounterCoordinator>(true))
                .Single(c=>c.TelemetrySegment=="courtyard-encounter");
            var result=new CandidateResult { twoApplicationsByteIdentical=identical,
                sourceSceneUnchanged=sourceBytes.SequenceEqual(File.ReadAllBytes(SourceScene)),
                protectedUnchanged=protectedBefore==protectedAfter, encounterCenter=coordinator.ArenaCenter,
                encounterHalfExtents=coordinator.ArenaHalfExtents, encounterActivationMargin=coordinator.TelemetryActivationMargin };
            File.WriteAllText(output+"/physics-before.json",physicsBefore);
            File.WriteAllText(output+"/physics-after.json",physicsAfter);
            File.WriteAllText(output+"/protected-before.txt",protectedBefore);
            File.WriteAllText(output+"/protected-after.txt",protectedAfter);
            ValidateGeometry(scene,output);
            var after=RenderPair(output,"after");
            var contact=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            contact.SetPixels(0,540,960,540,before[0].GetPixels()); contact.SetPixels(960,540,960,540,after[0].GetPixels());
            contact.SetPixels(0,0,960,540,before[1].GetPixels()); contact.SetPixels(960,0,960,540,after[1].GetPixels()); contact.Apply();
            File.WriteAllBytes(output+"/comparison.png",contact.EncodeToPNG());
            foreach(var t in before.Concat(after)) Object.DestroyImmediate(t); Object.DestroyImmediate(contact);
            CaptureAlternative(output);
            File.WriteAllText(output+"/result.json",JsonUtility.ToJson(result,true));
            if(!identical) throw new InvalidOperationException("Second generator application changed scene bytes; keep failure evidence.");
            if(!result.protectedUnchanged) throw new InvalidOperationException("Protected gates/encounters/anchor contract changed.");
            if(!sourceBytes.SequenceEqual(File.ReadAllBytes(SourceScene))) throw new InvalidOperationException("Shipping scene was modified.");
            Debug.Log("[COURTYARD_KIT] "+output+" idempotent=true shippingUnchanged=true");
        }
        public static string ProtectedState(Scene scene)
        {
            string ObjectPath(Transform t)=>t.parent==null?t.name:ObjectPath(t.parent)+"/"+t.name;
            var rows=new List<string>();
            foreach(var root in scene.GetRootGameObjects())
                foreach(var component in root.GetComponentsInChildren<Component>(true))
                {
                    if(component==null) continue;
                    bool gate=root.name.StartsWith("GateBlocker_");
                    bool encounter=component is CombatEncounterCoordinator || component is EncounterLeash;
                    bool anchor=component.transform.name=="Seal_Forest" && !(component is Transform);
                    if(gate||encounter||anchor) rows.Add(ObjectPath(component.transform)+"|"+component.GetType().Name+"|"+EditorJsonUtility.ToJson(component));
                }
            rows.Sort(StringComparer.Ordinal); return string.Join("\n",rows);
        }
        private static void ValidateGeometry(Scene scene,string output)
        {
            Physics.SyncTransforms();
            var root=Find(scene,GeneratedRoot);
            int mismatches=0;
            foreach(var c in root.GetComponentsInChildren<MeshCollider>(true))
                if(c.sharedMesh!=c.GetComponent<MeshFilter>().sharedMesh) mismatches++;
            if(mismatches!=0) throw new InvalidOperationException("Collider does not use the visible mesh.");
            var result=new List<string>();
            foreach(var entry in new[]{("Door_Entry",new Vector3(18,0,42),90f),("Door_Exit",new Vector3(34,0,36),0f)})
            {
                var rotation=Quaternion.Euler(0,entry.Item3,0); bool clear=true;
                // A 4m wide x2m high prism through the portal. Check owned architecture only, not actors.
                for(int ix=-19;ix<=19;ix++) foreach(float height in new[]{.15f,1f,2f})
                {
                    var ray=new Ray(entry.Item2+rotation*new Vector3(ix*.1f,height,-1.2f),rotation*Vector3.forward);
                    foreach(var collider in root.GetComponentsInChildren<MeshCollider>(true))
                        if(collider.Raycast(ray,out _,2.4f)) clear=false;
                }
                result.Add(entry.Item1+": interior[-1.9,1.9] x heights[.15,1,2] clear="+clear);
                if(!clear) throw new InvalidOperationException("Portal blocked by generated geometry: "+entry.Item1);
            }
            File.WriteAllLines(output+"/geometry-check.txt",result.Concat(new[]{"All generated MeshColliders reference the rendering mesh.",
                "Discrete geometry check only; not a player/NavMesh traversal test."}));
        }
        private static Texture2D[] RenderPair(string output,string label)
        {
            return new[]{Render(output+"/"+label+"-overview.png",new Vector3(11,19,27),new Vector3(28,1,44),52),
                Render(output+"/"+label+"-entry.png",new Vector3(20.5f,3.1f,41.5f),new Vector3(32,1.5f,46),65)};
        }
        private static Texture2D Render(string path,Vector3 position,Vector3 look,float fov)
        {
            var go=new GameObject("Bakeoff camera"); var camera=go.AddComponent<Camera>();
            if(Camera.main!=null) camera.CopyFrom(Camera.main);
            camera.enabled=false; camera.fieldOfView=fov; camera.nearClipPlane=.08f; camera.farClipPlane=180;
            camera.transform.position=position; camera.transform.LookAt(look);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32); var prior=RenderTexture.active;
            var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
            try { rt.Create(); camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,960,540),0,0); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); return texture; }
            finally { camera.targetTexture=null; RenderTexture.active=prior; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
        }
        private static void CaptureAlternative(string output)
        {
            // Native imported axes/scales retained. This is a kit/material comparison, not an alternative gameplay layout.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientLight=new Color(.6f,.65f,.7f); RenderSettings.skybox=null;
            var light=new GameObject("Comparison sun").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f;
            light.transform.rotation=Quaternion.Euler(48,-32,0);
            var root=new GameObject("Kit comparison").transform;
            for(int i=0;i<3;i++) Piece(root,"Kay_"+i,Kay(new[]{"wall","wall_corner","wall_doorway"}[i]),
                new Vector3(i*5,0,0),0,Vector3.one,false,_kitMaterial);
            var villageNames=new[]{"Wall_Plaster_Straight","Corner_Exterior_Brick","DoorFrame_Round_Brick"};
            for(int i=0;i<3;i++)
            {
                var model=Object.Instantiate(Load(VillageRoot+"FBX/"+villageNames[i]+".fbx"),root,false);
                model.transform.position+=new Vector3(i*5,0,-7);
                foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{
                        string type=m.name.Contains("Wood")?"WoodTrim":m.name.Contains("Plaster")?"Plaster":"RockTrim";
                        return Material("M_Village_"+type,VillageRoot+"Textures/T_"+type+"_BaseColor.png",Color.white); }).ToArray();
            }
            AssetDatabase.SaveAssets();
            var img=Render(output+"/kit-alternative.png",new Vector3(15,10,-17),new Vector3(5,1,-2),50); Object.DestroyImmediate(img);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
