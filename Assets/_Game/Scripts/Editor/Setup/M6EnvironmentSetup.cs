using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Measured KayKit production layout. Scene rules and dynamic gates are not authored here.</summary>
    public static class M6EnvironmentSetup
    {
        public const string RootName = "[Art] M6 Environment";
        public const string AssetRoot = "Assets/_Game/Art/M6Environment";
        public const string KitRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered/addons/kaykit_dungeon_remastered/Assets/";
        public static readonly string[] Scenes = { "10_EmberValley", "90_CombatGym" };

        [MenuItem("Emberfall/Setup/Apply 0.9.6 Environment Kit")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string name in Scenes)
            {
                var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/"+name+".unity");
                ApplyToScene(scene);
                BakeNavigation(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
            }
        }

        public static void ApplyToScene(Scene scene)
        {
            if (!Scenes.Contains(scene.name)) throw new InvalidOperationException("Not an approved production scene: "+scene.path);
            MigrateMaterials();
            var all = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            // Old project-owned facade wrappers only. Gate hierarchy and SanctumArch are deliberately excluded.
            string[] prefixes = {"CampWall_","CourtyardWall_","CourtyardParapet_","WardenEastWall_","WardenSouthWall_",
                "WardenNorthWall_","WardenWestWall_","ReturnFence_","BridgeFence_","NorthRuins_","SouthRuins_","EastRuins_","WestRuins_"};
            string[] oldWalls = scene.name=="10_EmberValley"
                ? new[]{"Camp_BackWall","Courtyard_NorthWall","Warden_EastWall","Warden_SouthWall"}
                : new[]{"Wall_North","Wall_South","Wall_East","Wall_West"};
            foreach (var t in all)
            {
                if(t==null || t.root.name.StartsWith("GateBlocker_",StringComparison.Ordinal)) continue;
                if(t.GetComponent<EncounterBoundaryVisualMarker>()!=null || oldWalls.Contains(t.name) ||
                    (t.root.name.StartsWith("[Art]",StringComparison.Ordinal) && prefixes.Any(p=>t.name.StartsWith(p,StringComparison.Ordinal))))
                    Object.DestroyImmediate(t.gameObject);
            }
            var existing=scene.GetRootGameObjects().FirstOrDefault(r=>r.name==RootName);
            var root=existing!=null?existing:new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root,scene);
            var builder=new Layout(root.transform);
            if(scene.name=="10_EmberValley")
            {
                BuildValley(builder);
                var seal=Find(scene,"Seal_Forest");
                if(seal!=null)
                {
                    var delta=new Vector3(5.4f,1,42.3f)-seal.position;
                    seal.position+=delta;
                    var shell=Find(scene,"Seal_Forest_StoneShell");
                    if(shell!=null && !shell.IsChildOf(seal)) shell.position+=delta;
                    // Network presentation lives outside the offline root; keep its marker at the shared anchor.
                    var ring=Find(scene,"Seal_Forest_HighlightRing");
                    var networkRing=Find(scene,"NetworkSeal_Forest_HighlightRing");
                    if(ring!=null && networkRing!=null)networkRing.position=ring.position;
                }
                builder.Backdrop(Find(scene,"CourtyardTree"));
            }
            else BuildGym(builder);
            builder.Finish();
            LiftGroundMarkersAbovePaving(scene,root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void LiftGroundMarkersAbovePaving(Scene scene,Transform root)
        {
            // Visible seal/checkpoint rings only: never move triggers, light/aim anchors or gate art.
            var floors=root.GetComponentsInChildren<MeshCollider>().Where(c=>c.GetComponentInParent<NavMeshModifier>()==null).ToArray();
            Physics.SyncTransforms();
            foreach(var marker in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)))
            {
                if(!marker.name.EndsWith("_HighlightRing",StringComparison.Ordinal) ||
                    !(marker.name.StartsWith("Seal_",StringComparison.Ordinal) || marker.name.StartsWith("Checkpoint_",StringComparison.Ordinal) ||
                      marker.name=="NetworkSeal_Forest_HighlightRing")) continue;
                var position=marker.position; float y=position.y;
                var ray=new Ray(new Vector3(position.x,position.y+2,position.z),Vector3.down);
                foreach(var floor in floors)
                    if(floor.Raycast(ray,out var hit,4))y=Mathf.Max(y,hit.point.y+.04f);
                if(position.y!=y) { position.y=y; marker.position=position; }
            }
        }

        private static Transform Find(Scene scene,string name)=>scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name);

        public static void MigrateMaterials()
        {
            const string old="Assets/_Game/Art/CourtyardBakeoff";
            if(AssetDatabase.IsValidFolder(old) && !AssetDatabase.IsValidFolder(AssetRoot))
            {
                string error=AssetDatabase.MoveAsset(old,AssetRoot);
                if(!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            if(!AssetDatabase.IsValidFolder(AssetRoot)) AssetDatabase.CreateFolder("Assets/_Game/Art","M6Environment");
        }

        public static void BakeNavigation(Scene scene)
        {
            var surface=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<NavMeshSurface>(true)).Single();
            // BuildNavMesh replaces data with a transient object. Persist into the existing GUID, then rebind.
            string path="Assets/_Game/Settings/Navigation/"+(scene.name=="90_CombatGym"?"CombatGym":"EmberValley")+"NavMesh.asset";
            surface.BuildNavMesh();
            var baked=surface.navMeshData;
            if(baked==null) throw new InvalidOperationException("Navigation bake failed: "+scene.name);
            var saved=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            surface.RemoveData();
            if(saved==null) { AssetDatabase.CreateAsset(baked,path); saved=baked; }
            else { EditorUtility.CopySerialized(baked,saved); Object.DestroyImmediate(baked); }
            saved.name=Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(saved);
            surface.navMeshData=saved;
            EditorUtility.SetDirty(surface);
            surface.AddData();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildValley(Layout b)
        {
            // Low ruined camp/forest edges are connected, not full-height boxes.
            b.Run("Camp_Back",-8,-8,8,-8,.5f,true);
            b.Run("Camp_West",-8,-8,-8,6,.45f,true);
            b.Run("Camp_East",8,-8,8,6,.45f,true);
            b.Pave("Camp",-6,-6,4,4);
            b.Run("Entry_West",-5,10,-5,24,.2f,true);
            b.Run("Entry_East",5,10,5,24,.2f,true);
            b.Pave("Entry",-2,12,2,4);
            // The west opening leads to the existing watchtower, not a new quest.
            b.Run("Forest_SW",-9,26,-2.5f,26,.25f,true);
            b.Run("Forest_SE",2.5f,26,7.35f,26,.25f,true);
            b.Run("Forest_W_Lower",-9,26,-9,34,.45f,true);
            b.Run("Forest_W_Upper",-9,42,-9,46,.45f,true);
            b.Run("Forest_North",-9,46,7,46,.45f,true);
            b.Run("Forest_Gate_Lower",7.35f,26,7.35f,28.2f,.65f,false);
            b.Run("Forest_Gate_Upper",7.35f,32.8f,7.35f,38.8f,.55f,true);
            b.Pave("Forest",-6,30,4,4);
            b.Run("Watchtower_N",-17,42,-9,42,.4f,true);
            b.Run("Watchtower_W",-17,34,-17,42,.4f,true);
            b.Run("Watchtower_S",-17,34,-9,34,.4f,true);
            b.Pave("Watchtower",-15,36,2,2);
            b.K("Watchtower_Column","pillar_decorated",-16,40,0,.65f,1.2f,.65f);
            b.Cluster("Watchtower",-16,35);
            // Bridge and courtyard share one edge, leaving both route-choice objects reachable.
            b.Run("Bridge_N",9,50,18,50,.45f,true);
            b.Run("Bridge_W",9,46.5f,9,50,.4f,true);
            b.Run("Bridge_S",9,39,18,39,.4f,true);
            b.Pave("Bridge",11,41,2,2,.02f);
            // Place exterior stone outside the fixed encounter rectangle, not through its battle floor.
            b.K("Court_NW","wall_corner",18,54,90,1,.45f,1);
            b.K("Court_NE","wall_corner",40,54,180,1,.45f,1);
            b.K("Court_SW","wall_corner",18,36);
            foreach(float x in new[]{22f,30f,34f,38f}) b.K("Court_N_"+x,"wall_broken",x,54,0,1,.45f,1);
            b.K("Court_T","wall_Tsplit",26,54,0,1,.45f,1);
            b.K("Court_T_End","wall_endcap",26,56,90,1,.45f,1);
            b.Run("Court_East",40,36,40,52,.45f,true);
            b.Run("Court_SE_Link",38,36,40,36,1,false);
            b.Portal("Court_Entry",18,43,90);
            b.Run("Court_West_N",18,47,18,52,.45f,true);
            b.Run("Court_West_S",18,38,18,39,1,false);
            b.Run("Court_South",20,36,30,36,1,false);
            b.Portal("Court_Exit",34,36,0);
            b.Pave("Court",20,38,5,4);
            b.Cluster("Court_NW",20,52);
            b.Cluster("Court_NE",38,52);
            b.K("Court_RearColumn","pillar_decorated",26,55,0,.7f,.85f,.7f);
            b.K("Court_Rubble","rubble_half",25,52.5f,0,.4f,.3f,.4f);
            // Existing dynamic gates at z32.5/z31 are untouched. Full-height sides prevent lateral bypass.
            b.Run("Approach_W",34.1f,27.3f,34.1f,34.5f,1,false);
            b.Run("Approach_E",44,27.3f,44,35.5f,1,false);
            b.Pave("Approach",36,29,2,2,.02f);
            // The offline Warden area remains in Valley; Sanctum's separate indoor scene is report-only.
            b.Run("Warden_E",57,9,57,31,.55f,true);
            b.Run("Warden_S",35,9,57,9,.45f,true);
            b.Run("Warden_N",44,31,57,31,1,false);
            b.Run("Warden_W",35,14,35,27.3f,.65f,true);
            b.Pave("Warden",37,11,5,5);
            b.Cluster("Warden",55,28);
            b.Run("Return_N",12.5f,13.5f,34,13.5f,.35f,true);
            b.Run("Return_S",12.5f,6.5f,34,6.5f,.35f,true);
            b.Pave("Return",14.5f,8.5f,5,1);
        }

        private static void BuildGym(Layout b)
        {
            b.Run("Gym_N",-12,12,12,12,.6f,true);
            b.Run("Gym_S",-12,-12,12,-12,.4f,true);
            b.Run("Gym_W",-12,-12,-12,12,.45f,true);
            b.Run("Gym_E",12,-12,12,12,.45f,true);
            b.Pave("Gym",-10,-10,6,6);
            b.Cluster("Gym_NW",-10,10);
        }

        private sealed class Layout
        {
            private readonly Transform _root;
            private readonly HashSet<string> _expected=new HashSet<string>();
            private readonly Material _stone,_paving;
            public Layout(Transform root)
            {
                _root=root;
                _stone=Material("M_KayKit_Courtyard",Color.white);
                _paving=Material("M_KayKit_Paving",new Color(.55f,.61f,.52f));
            }
            private static Material Material(string name,Color tint)
            {
                string path=AssetRoot+"/"+name+".mat";
                var m=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path); }
                m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(KitRoot+"texture/dungeon_texture.png"));
                m.SetColor("_BaseColor",tint); m.SetFloat("_Smoothness",.12f); EditorUtility.SetDirty(m); return m;
            }
            public void K(string name,string mesh,float x,float z,float yaw=0,float sx=1,float sy=1,float sz=1,bool solid=true,float y=0,bool floor=false)
            {
                _expected.Add(name);
                var t=_root.Find(name);
                if(t==null) { t=new GameObject(name).transform; t.SetParent(_root,false); }
                var position=new Vector3(x,y,z); var rotation=Quaternion.Euler(0,yaw,0); var scale=new Vector3(sx,sy,sz);
                if(!t.localPosition.Equals(position))t.localPosition=position;
                if(!t.localRotation.Equals(rotation))t.localRotation=rotation;
                if(!t.localScale.Equals(scale))t.localScale=scale;
                string path=KitRoot+"fbx/"+mesh+".fbx";
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(path)??throw new FileNotFoundException("Missing measured kit part",path);
                var existing=t.GetComponentInChildren<MeshFilter>();
                if(existing!=null && AssetDatabase.GetAssetPath(existing.sharedMesh)!=path)
                    while(t.childCount>0) Object.DestroyImmediate(t.GetChild(0).gameObject);
                if(t.childCount==0) { var model=Object.Instantiate(source,t,false); model.name="Model"; }
                foreach(var r in t.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterials=Enumerable.Repeat(floor?_paving:_stone,r.sharedMaterials.Length).ToArray();
                foreach(var f in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    var c=f.GetComponent<MeshCollider>();
                    if(solid) { if(c==null)c=f.gameObject.AddComponent<MeshCollider>(); if(c.sharedMesh!=f.sharedMesh)c.sharedMesh=f.sharedMesh; if(c.convex)c.convex=false; }
                    else if(c!=null)Object.DestroyImmediate(c);
                }
                var modifier=t.GetComponent<NavMeshModifier>();
                if(!floor && solid) { if(modifier==null)modifier=t.gameObject.AddComponent<NavMeshModifier>(); if(!modifier.overrideArea)modifier.overrideArea=true; if(modifier.area!=1)modifier.area=1; }
                else if(modifier!=null)Object.DestroyImmediate(modifier);
            }
            public void Run(string name,float x0,float z0,float x1,float z1,float height,bool broken)
            {
                var a=new Vector3(x0,0,z0); var delta=new Vector3(x1-x0,0,z1-z0); float length=delta.magnitude;
                float yaw=-Mathf.Atan2(delta.z,delta.x)*Mathf.Rad2Deg;
                int count=Mathf.Max(1,Mathf.CeilToInt(length/4)); float pitch=length/count;
                for(int i=0;i<count;i++)
                {
                    var p=a+delta*((i+.5f)/count);
                    K(name+"_"+i,broken && i%3!=0?"wall_broken":"wall",p.x,p.z,yaw,pitch/4,height,1);
                }
                K(name+"_Start","column",x0,z0,0,1,height*4/1.4f,1);
                K(name+"_End","column",x1,z1,0,1,height*4/1.4f,1);
            }
            public void Portal(string name,float x,float z,float yaw)
            {
                var center=new Vector3(x,0,z); var q=Quaternion.Euler(0,yaw,0);
                foreach(int side in new[]{-1,1})
                {
                    var p=center+q*new Vector3(side*2,0,0);
                    K(name+"_Jamb_"+side,"wall_half_endcap",p.x,p.z,yaw+(side<0?180:0));
                }
                K(name+"_Lintel","wall",x,z,yaw,1,.1875f,1,true,3.25f);
                var trim=center+q*new Vector3(-3.2f,0,.7f);
                K(name+"_Flag","banner_red",trim.x,trim.z,yaw,.65f,.65f,.65f,false);
                K(name+"_Torch","torch_lit",trim.x,trim.z,yaw,.7f,.7f,.7f,false,2);
            }
            public void Pave(string name,float x,float z,int nx,int nz,float baseHeight=0)
            {
                for(int ix=0;ix<nx;ix++)for(int iz=0;iz<nz;iz++)
                {
                    // One connected weathered corner/wall-root cluster; no alternating dark holes
                    // throughout the combat floor or narrow transit routes.
                    bool weathered=nx>=3 && nz>=3 && ((ix==0 && iz>=nz-2)||(iz==nz-1 && ix<=1));
                    bool dirt=weathered && ix==0 && iz==nz-1;
                    float px=x+ix*4,pz=z+iz*4;
                    float yaw=((ix*3+iz*7)%4)*90f;
                    if(dirt) K(name+"_Dirt_"+ix+"_"+iz,"floor_dirt_large",px,pz,yaw,1,1,1,true,baseHeight-.025f,true);
                    else if(weathered)
                    {
                        for(int a=0;a<2;a++)for(int c=0;c<2;c++)
                        {
                            bool outer=(ix==0 && a==0)||(iz==nz-1 && c==1);
                            string mesh=outer?(ix==0?"floor_tile_small_weeds_B":"floor_tile_small_broken_B"):"floor_tile_small";
                            K(name+"_Edge_"+ix+"_"+iz+"_"+a+"_"+c,mesh,px-1+a*2,pz-1+c*2,yaw+(a+c)*90f,1,1,1,true,baseHeight-.025f,true);
                        }
                    }
                    else K(name+"_Paving_"+ix+"_"+iz,"floor_tile_large",px,pz,yaw,1,1,1,true,baseHeight-.025f,true);
                }
            }
            public void Cluster(string name,float x,float z)
            {
                K(name+"_Barrels","barrel_small_stack",x,z,20,.6f,.6f,.6f);
                K(name+"_Rubble","rubble_large",x,z+1,0,.3f,.25f,.3f);
            }
            public void Backdrop(Transform source)
            {
                if(source==null)return;
                for(int i=0;i<3;i++)
                {
                    string name="Court_Backdrop_"+i; _expected.Add(name); var t=_root.Find(name);
                    if(t==null) { t=Object.Instantiate(source.gameObject,_root,false).transform; t.name=name;
                        foreach(var c in t.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
                        foreach(var c in t.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(c); }
                    t.position=new Vector3(21+i*7,-.2f,55.8f+i%2);
                }
            }
            public void Finish()
            {
                foreach(Transform t in _root.Cast<Transform>().ToArray()) if(!_expected.Contains(t.name))Object.DestroyImmediate(t.gameObject);
            }
        }
    }
}
