using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Project-owned surface and high architectural layer. Never authors walkable geometry or game facts.</summary>
    public static class WorldPresentationSetup
    {
        public const string RootName="[Art] Emberfall Sanctum Identity";
        public const string AssetRoot="Assets/_Game/Art/WorldPresentation";
        public static readonly string[] Scenes={"10_EmberValley","20_Sanctum"};
        public static readonly Color SanctumFillColor=new Color(.38f,.64f,.78f);
        public static readonly string[] SanctumSurfaceNames={"Slate","Stone","Edge","Inlay","Sigil","Wall_A","Wall_B","Wall_C","Mortar"};

        // Targeted palette pass. Existing assets, slots, opaque modes, geometry and
        // ambient settings remain owned by their original authoring paths.
        public static void ApplySanctumSurfaces(Scene scene)
        {
            RequireIdle();
            if(scene.name!="20_Sanctum")throw new InvalidOperationException("Sanctum only.");
            var light=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Light>(true))
                .Single(l=>l.name=="Sanctum Fill Light");
            if(light.type!=LightType.Point||light.lightmapBakeType!=LightmapBakeType.Realtime)
                throw new InvalidOperationException("Expected existing realtime fill light.");
            foreach(string name in SanctumSurfaceNames)
                if(AssetDatabase.LoadAssetAtPath<Material>(AssetRoot+"/M_Sanctum_"+name+".mat")==null)
                    throw new InvalidOperationException("Missing existing Sanctum material: "+name);
            foreach(string name in SanctumSurfaceNames)
                AssetDatabase.SaveAssetIfDirty(SanctumMaterial(name));
            light.color=SanctumFillColor;
            EditorUtility.SetDirty(light);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static Material SanctumMaterial(string name)
        {
            // Stone stays matte. The subdued bronze uses low metalness because
            // this room has no authored local reflection probe, not mirror gold.
            switch(name)
            {
                case "Slate":return Material("M_Sanctum_"+name,new Color(.22f,.29f,.30f),smoothness:.04f);
                case "Stone":return Material("M_Sanctum_"+name,new Color(.31f,.38f,.37f),smoothness:.06f);
                case "Edge":return Material("M_Sanctum_"+name,new Color(.38f,.40f,.34f),smoothness:.08f);
                case "Inlay":return Material("M_Sanctum_"+name,new Color(.30f,.26f,.19f),smoothness:.30f,metallic:.18f);
                case "Sigil":return Material("M_Sanctum_"+name,new Color(.16f,.36f,.42f),smoothness:.20f);
                case "Wall_A":return Material("M_Sanctum_"+name,new Color(.48f,.44f,.37f),smoothness:.07f);
                case "Wall_B":return Material("M_Sanctum_"+name,new Color(.42f,.39f,.34f),smoothness:.05f);
                case "Wall_C":return Material("M_Sanctum_"+name,new Color(.52f,.48f,.41f),smoothness:.09f);
                case "Mortar":return Material("M_Sanctum_"+name,new Color(.19f,.21f,.22f),smoothness:.03f);
                default:throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        [MenuItem("Emberfall/Presentation/Apply World Identity")]
        public static void Apply()
        {
            RequireIdle();
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Preserve unsaved scene edits.");
            var previous=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach(string name in Scenes)
                {
                    var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/"+name+".unity");
                    ApplyToScene(scene);EditorSceneManager.SaveScene(scene);
                }
            }
            finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
        }

        public static void ApplyToScene(Scene scene)
        {
            RequireIdle();
            if(!Scenes.Contains(scene.name))return;
            EnsureFolder(AssetRoot);
            if(scene.name=="10_EmberValley")ApplyValley(scene);
            else ApplySanctum(scene);
            // Save only our derivatives, not unrelated dirty assets.
            foreach(string guid in AssetDatabase.FindAssets("t:Material t:Mesh",new[]{AssetRoot}))
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            ImportedEnvironmentDressingSetup.ApplyToScene(scene);
            AshEncounterBannerSetup.ApplyToScene(scene);
            ChoiceMarkerArtSetup.ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void RequireIdle()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit Mode required.");
        }

        static void ApplyValley(Scene scene)
        {
            var root=scene.GetRootGameObjects().Single(r=>r.name==M6EnvironmentSetup.RootName);
            var floor=new Material[7];var stone=new Material[4];
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(M6EnvironmentSetup.KitRoot+"texture/dungeon_texture.png");
            if(texture==null)throw new InvalidOperationException("Existing licensed KayKit palette missing.");
            for(int i=0;i<floor.Length;i++)
                floor[i]=Material("M_Path_"+i,new Color(.62f,.60f,.50f)*( .88f+i*.035f),texture);
            for(int i=0;i<stone.Length;i++)
                stone[i]=Material("M_Ruin_"+i,new Color(.83f,.80f,.71f)*( .94f+i*.025f),texture);
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string current=renderer.sharedMaterial!=null?renderer.sharedMaterial.name:"";
                if(current=="M_KayKit_Paving"||current.StartsWith("M_Path_",StringComparison.Ordinal))
                {
                    var p=renderer.bounds.center;
                    float noise=Mathf.PerlinNoise((p.x+31)*.065f,(p.z+13)*.065f)*.8f+
                        Mathf.PerlinNoise((p.x+7)*.21f,(p.z+17)*.21f)*.2f;
                    int index=Mathf.Clamp(Mathf.RoundToInt(noise*6),0,6);
                    renderer.sharedMaterials=Enumerable.Repeat(floor[index],renderer.sharedMaterials.Length).ToArray();
                }
                else if(current=="M_KayKit_Courtyard"||current.StartsWith("M_Ruin_",StringComparison.Ordinal))
                {
                    int index=(int)(StableHash(renderer.transform.parent.name)%4);
                    renderer.sharedMaterials=Enumerable.Repeat(stone[index],renderer.sharedMaterials.Length).ToArray();
                }
            }
        }

        static void ApplySanctum(Scene scene)
        {
            var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var floor=all.Single(t=>t.name=="Sanctum_ArenaFloor").GetComponent<Renderer>();
            var b=floor.bounds;
            var center=new Vector3(b.center.x,b.max.y,b.center.z);
            var root=scene.GetRootGameObjects().SingleOrDefault(r=>r.name==RootName)??new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root,scene);
            root.transform.SetPositionAndRotation(center,Quaternion.identity);root.transform.localScale=Vector3.one;
            var paving=new[]{SanctumMaterial("Slate"),SanctumMaterial("Stone"),SanctumMaterial("Edge"),SanctumMaterial("Inlay")};
            var sigil=SanctumMaterial("Sigil");
            var walls=new[]{SanctumMaterial("Wall_A"),SanctumMaterial("Wall_B"),SanctumMaterial("Wall_C"),SanctumMaterial("Mortar")};
            ApplySanctumSurfaces(scene);
            foreach(var t in all.Where(t=>t.name.StartsWith("Sanctum_Wall",StringComparison.Ordinal)))
            {
                var r=t.GetComponent<MeshRenderer>();if(r==null)continue;
                r.sharedMaterials=walls; // Existing four-submesh masonry and collision remain identical.
            }
            foreach(string name in new[]{"Sanctum_PillarWest","Sanctum_PillarEast"})
            {
                var pillar=all.Single(t=>t.name==name);var renderer=pillar.GetComponent<MeshRenderer>();
                var collider=pillar.GetComponent<BoxCollider>();
                if(collider==null||Vector3.Distance(renderer.bounds.size,collider.bounds.size)>.001f)
                    throw new InvalidOperationException("Visible solid pillar must match existing blocker: "+name);
                // Keep the original opaque mesh, transform and collider. Thin stone faces
                // are only12mm outside it; never substitute a narrower cylinder/air blocker.
                renderer.sharedMaterial=walls[3];
                AddVisual(root.transform,name+"_StoneFaces",SaveMesh(BuildPillarFaces(renderer.bounds.size),"SanctumPillarFaces"),walls,
                    renderer.bounds.center-center,Quaternion.identity);
                AddVisual(root.transform,name+"_Inscription",SaveMesh(BuildSigil(.33f),"SanctumPillarInscription"),new[]{sigil},
                    renderer.bounds.center-center+new Vector3(0,.32f,-renderer.bounds.extents.z-.014f),Quaternion.Euler(-90,0,0));
                // The original solid square column is never narrowed or hidden. Crown
                // overhangs start above reachable character space, not at ankle height.
                AddVisual(root.transform,name+"_HighCrown",SaveMesh(BuildPillarCrown(renderer.bounds.size),"SanctumPillarCrown"),walls,
                    renderer.bounds.center-center,Quaternion.identity);
            }
            AddVisual(root.transform,"FloorMosaic",SaveMesh(BuildFloorMosaic(b.size.x-.7f,b.size.z-.7f),"SanctumFloorMosaic"),paving,
                new Vector3(0,.012f,0),Quaternion.identity);
            AddVisual(root.transform,"CentralSigil",SaveMesh(BuildSigil(1.65f),"SanctumCentralSigil"),new[]{sigil},
                new Vector3(0,.016f,3.6f),Quaternion.identity);
            AddVisual(root.transform,"RearMedallion",SaveMesh(BuildSigil(1.6f),"SanctumRearMedallion"),new[]{sigil},
                new Vector3(0,2,b.size.z*.5f-.335f),Quaternion.Euler(-90,0,0));
            // Decorative skyline sits behind the existing back wall. Its bases overlap
            // that wall top; no floating fragments, collider or camera-occlusion script.
            AddVisual(root.transform,"RearBrokenArch",SaveMesh(BuildSanctumSkyline(),"SanctumRearBrokenArch"),walls,
                new Vector3(0,0,b.size.z*.5f+.45f),Quaternion.identity);
            // Nothing emits dangerous orange or pulsing gold; these motifs are subdued architecture,
            // not threat sectors, interactables, execution markers or a new objective.
        }

        public static Mesh BuildFloorMosaic(float width,float depth)
        {
            if(width<=0||depth<=0||width>30||depth>30)throw new ArgumentOutOfRangeException();
            var builder=new FlatBuilder(4);const int count=10;
            float sx=width/count,sz=depth/count;
            for(int x=0;x<count;x++)for(int z=0;z<count;z++)
            {
                float cx=-width*.5f+(x+.5f)*sx,cz=-depth*.5f+(z+.5f)*sz;
                int slot=x==0||z==0||x==count-1||z==count-1?2:
                    Mathf.PerlinNoise((x+7)*.23f,(z+19)*.23f)>.5f?1:0;
                builder.Quad(new Vector3(cx-sx*.5f+.02f,0,cz-sz*.5f+.02f),new Vector3(cx-sx*.5f+.02f,0,cz+sz*.5f-.02f),
                    new Vector3(cx+sx*.5f-.02f,0,cz+sz*.5f-.02f),new Vector3(cx+sx*.5f-.02f,0,cz-sz*.5f+.02f),slot);
            }
            // Low-contrast concentric inlay, never an authoritative danger circle.
            builder.Ring(new Vector3(0,.001f,2),5.5f,5.56f,64,3);
            builder.Ring(new Vector3(0,.001f,2),6.25f,6.29f,64,3);
            return builder.Finish("SanctumFloorMosaic");
        }

        public static Mesh BuildSigil(float radius)
        {
            if(radius<=0||radius>3)throw new ArgumentOutOfRangeException(nameof(radius));
            var b=new FlatBuilder(1);b.Ring(Vector3.zero,radius*.91f,radius,8,0);
            b.Ring(Vector3.zero,radius*.52f,radius*.56f,8,0);
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;var axis=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                var side=new Vector3(axis.z,0,-axis.x);var p=axis*radius*.74f;
                b.Quad(p-axis*radius*.115f,p-side*radius*.05f,p+axis*radius*.115f,p+side*radius*.05f,0);
            }
            return b.Finish("EmberfallSigil");
        }

        public static Mesh BuildPillarFaces(Vector3 size)
        {
            if(size.x<=0||size.y<=0||size.z<=0||size.x>5||size.y>8||size.z>5)
                throw new ArgumentOutOfRangeException(nameof(size));
            var builder=new FlatBuilder(4);int rows=Mathf.CeilToInt(size.y/.4f);float height=size.y/rows;
            const float standOff=.012f,gap=.012f;
            for(int side=0;side<4;side++)
            {
                float width=side<2?size.x:size.z,half=width*.5f,depth=(side<2?size.z:size.x)*.5f+standOff;
                for(int row=0;row<rows;row++)for(int column=-1;column<2;column++)
                {
                    float phase=row%2==0?0:width*.25f;
                    float left=Mathf.Max(-half,-half+column*width*.5f+phase)+gap;
                    float right=Mathf.Min(half,-half+(column+1)*width*.5f+phase)-gap;
                    if(right<=left)continue;
                    float bottom=-size.y*.5f+row*height+gap,top=bottom+height-gap*2;
                    Vector3 a=new Vector3(left,bottom,-depth),b=new Vector3(left,top,-depth),c=new Vector3(right,top,-depth),d=new Vector3(right,bottom,-depth);
                    Func<Vector3,Vector3> face=p=>side==0?p:side==1?new Vector3(-p.x,p.y,-p.z):
                        side==2?new Vector3(-p.z,p.y,p.x):new Vector3(p.z,p.y,-p.x);
                    builder.Quad(face(a),face(b),face(c),face(d),(row+column+side+12)%3);
                }
            }
            return builder.Finish("SanctumPillarFaces");
        }

        public static Mesh BuildPillarCrown(Vector3 size)
        {
            if(size.x<=0||size.z<=0||size.x>5||size.z>5||size.y<3||size.y>8)
                throw new ArgumentOutOfRangeException(nameof(size));
            var b=new FlatBuilder(4);
            b.Box(new Vector3(0,size.y*.5f-.08f,0),new Vector3(size.x+.22f,.20f,size.z+.22f),Quaternion.identity,1);
            b.Box(new Vector3(0,size.y*.5f+.12f,0),new Vector3(size.x+.40f,.20f,size.z+.40f),Quaternion.identity,2);
            return b.Finish("SanctumPillarCrown");
        }

        public static Mesh BuildSanctumSkyline()
        {
            var b=new FlatBuilder(4);const float radius=3,centerHeight=3.7f;
            b.Box(new Vector3(-radius,3.50f,0),new Vector3(.72f,.70f,1.05f),Quaternion.identity,1);
            b.Box(new Vector3(radius,3.50f,0),new Vector3(.72f,.70f,1.05f),Quaternion.identity,1);
            for(int i=0;i<18;i++)
            {
                // A missing keystone span exposes the sky, with two attached arms.
                if(i>=8&&i<=11)continue;
                float angle=(i+.5f)*10*Mathf.Deg2Rad;
                Vector3 p=new Vector3(Mathf.Cos(angle)*radius,centerHeight+Mathf.Sin(angle)*radius,0);
                b.Box(p,new Vector3(.505f,.65f,1.05f),Quaternion.Euler(0,0,(i+.5f)*10-90),i%3);
            }
            return b.Finish("SanctumRearBrokenArch");
        }

        sealed class FlatBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<int>[] triangles;
            public FlatBuilder(int slots){triangles=Enumerable.Range(0,slots).Select(i=>new List<int>()).ToArray();}
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot)
            {
                int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
                triangles[slot].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            public void Ring(Vector3 center,float inner,float outer,int segments,int slot)
            {
                for(int i=0;i<segments;i++)
                {
                    float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
                    var first=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));var second=new Vector3(Mathf.Sin(b),0,Mathf.Cos(b));
                    Quad(center+first*inner,center+first*outer,center+second*outer,center+second*inner,slot);
                }
            }
            public void Box(Vector3 center,Vector3 size,Quaternion rotation,int slot)
            {
                var h=size*.5f;
                Vector3[] v={new Vector3(-h.x,-h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(h.x,-h.y,-h.z),
                    new Vector3(-h.x,-h.y,h.z),new Vector3(-h.x,h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(h.x,-h.y,h.z)};
                for(int i=0;i<v.Length;i++)v[i]=center+rotation*v[i];
                Quad(v[0],v[1],v[2],v[3],slot);Quad(v[7],v[6],v[5],v[4],slot);
                Quad(v[4],v[5],v[1],v[0],slot);Quad(v[3],v[2],v[6],v[7],slot);
                Quad(v[1],v[5],v[6],v[2],slot);Quad(v[4],v[0],v[3],v[7],slot);
            }
            public Mesh Finish(string name)
            {
                var mesh=new Mesh{name=name,subMeshCount=triangles.Length};mesh.SetVertices(vertices);
                for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
                mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            }
        }

        static void AddVisual(Transform parent,string name,Mesh mesh,Material[] materials,Vector3 position,Quaternion rotation)
        {
            var t=parent.Find(name);
            if(t==null){t=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)).transform;t.SetParent(parent,false);}
            t.localPosition=position;t.localRotation=rotation;t.localScale=Vector3.one;
            if(t.GetComponentsInChildren<Collider>(true).Length!=0||t.GetComponentsInChildren<MonoBehaviour>(true).Length!=0)
                throw new InvalidOperationException("Art layer unexpectedly owns collision or behaviour: "+name);
            t.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=t.GetComponent<MeshRenderer>();renderer.sharedMaterials=materials;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        static Material Material(string name,Color color,Texture2D texture=null,float smoothness=.10f,float metallic=0)
        {
            string path=AssetRoot+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetTexture("_BaseMap",texture);
            material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Metallic",metallic);
            material.DisableKeyword("_EMISSION");
            // URP's normal public ShaderGUI validation synchronizes legacy _Color/
            // _MainTex, render tags and keywords. Complete authoring before byte gates,
            // rather than letting a later importer/Inspector change our first save.
            Type validator=null;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                validator=assembly.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader");
                if(validator!=null)break;
            }
            if(validator==null)throw new InvalidOperationException("Installed URP14 Lit validator missing.");
            ((ShaderGUI)Activator.CreateInstance(validator)).ValidateMaterial(material);
            EditorUtility.SetDirty(material);return material;
        }
        static Mesh SaveMesh(Mesh generated,string name)
        {
            string path=AssetRoot+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){AssetDatabase.CreateAsset(generated,path);return generated;}
            EditorUtility.CopySerialized(generated,mesh);Object.DestroyImmediate(generated);EditorUtility.SetDirty(mesh);return mesh;
        }
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
        }
        static uint StableHash(string name){uint h=2166136261;foreach(char c in name)h=(h^c)*16777619;return h;}
    }
}
