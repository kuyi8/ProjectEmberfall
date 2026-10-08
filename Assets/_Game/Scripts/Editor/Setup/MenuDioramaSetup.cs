using System;
using System.IO;
using System.Linq;
using Emberfall.Editor.Review;
using Emberfall.Gameplay.Animation;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>Menu-only set dressing. No actor, collider, navmesh, quest, save or session authority.</summary>
    public static class MenuDioramaSetup
    {
        public const string ScenePath="Assets/_Game/Scenes/01_MainMenu.unity";
        public const string RootName="[Art] Emberfall Menu Diorama";
        const string Materials="Assets/_Game/Art/Materials/Menu";
        const string Hero="Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";

        [MenuItem("Emberfall/Art/Apply Menu Ruin Diorama")]
        public static void Apply() => Apply(false);

        public static void Apply(bool rebuild)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Editor required.");
            var previous=EditorSceneManager.GetSceneManagerSetup();
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scenes.");
            try
            {
                var scene=EditorSceneManager.OpenScene(ScenePath);
                ApplyToScene(scene,rebuild); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        public static void ApplyToScene(Scene scene,bool rebuild=false)
        {
            bool newMenu=string.IsNullOrEmpty(scene.path) && scene.GetRootGameObjects()
                .SelectMany(r=>r.GetComponentsInChildren<MainMenuPlaceholder>(true)).Count()==1;
            if(scene.path!=ScenePath && !newMenu) throw new InvalidOperationException("Menu scene only.");
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author in Edit Mode.");
            var old=scene.GetRootGameObjects().SingleOrDefault(r=>r.name==RootName);
            if(old!=null && !rebuild) return;
            if(old!=null) UnityEngine.Object.DestroyImmediate(old);
            Directory.CreateDirectory(Materials); AssetDatabase.Refresh();
            var root=new GameObject(RootName); SceneManager.MoveGameObjectToScene(root,scene);
            var stone=Material("M_MenuStone",new Color(.85f,.93f,.92f),false,true);
            var darkStone=Material("M_MenuStoneDark",new Color(.64f,.77f,.75f),false,true);
            var floor=Material("M_MenuFloor",new Color(.8f,.88f,.75f),false,true);
            var plinth=Material("M_MenuPlinth",new Color(.52f,.65f,.61f));
            var underlay=Material("M_MenuUnderlay",new Color(.22f,.35f,.35f));
            var ember=Material("M_MenuEmber",new Color(1f,.39f,.12f),true);
            var rune=Material("M_MenuRune",new Color(.12f,.63f,.62f),true);

            var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.16f,.29f,.29f);
            camera.orthographic=true; camera.orthographicSize=2.65f; camera.nearClipPlane=.1f; camera.farClipPlane=65;
            camera.transform.position=new Vector3(0,3.4f,-10);
            camera.transform.LookAt(new Vector3(0,1.1f,0));
            RenderSettings.skybox=null; RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.53f,.65f,.64f);
            RenderSettings.ambientEquatorColor=new Color(.27f,.38f,.36f);
            RenderSettings.ambientGroundColor=new Color(.14f,.20f,.17f);
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=camera.backgroundColor; RenderSettings.fogStartDistance=13; RenderSettings.fogEndDistance=28;

            Primitive(root.transform,"Underlay",PrimitiveType.Cube,new Vector3(2,-.22f,3),new Vector3(14,.25f,15),underlay);
            for(int x=0;x<3;x++) for(int z=0;z<3;z++)
                Kit(root.transform,"floor_tile_large","Paving_"+x+"_"+z,new Vector3(-1+x*3,0,-1+z*3),3,0,floor,false);
            Kit(root.transform,"wall_arched","RuinArch",new Vector3(3.55f,0,3.8f),3.7f,-12,stone,true);
            Kit(root.transform,"wall_broken","BrokenWall",new Vector3(.1f,0,5.3f),2.4f,16,darkStone,true);
            Kit(root.transform,"pillar_decorated","PillarRight",new Vector3(5.3f,0,2.2f),3.7f,0,stone,true);
            Kit(root.transform,"pillar","PillarLeft",new Vector3(.55f,0,2.4f),2.6f,0,darkStone,true);
            Kit(root.transform,"rubble_half","RubbleRight",new Vector3(4.25f,0,.1f),1.1f,-24,darkStone,false);
            Kit(root.transform,"rubble_half","RubbleRear",new Vector3(.25f,0,3.25f),.8f,35,darkStone,false);

            Primitive(root.transform,"HeroPlinth",PrimitiveType.Cylinder,new Vector3(2.05f,.035f,.2f),new Vector3(2.3f,.1f,2.3f),plinth);
            Primitive(root.transform,"RunePedestal",PrimitiveType.Cylinder,new Vector3(3.9f,.22f,2.35f),new Vector3(.55f,.28f,.55f),underlay);
            var crystal=new GameObject("EmberCrystal",typeof(MeshFilter),typeof(MeshRenderer));
            crystal.transform.SetParent(root.transform,false);
            crystal.transform.SetPositionAndRotation(new Vector3(3.9f,.88f,2.35f),Quaternion.Euler(0,30,12));
            crystal.transform.localScale=new Vector3(.34f,.62f,.34f);
            crystal.GetComponent<MeshFilter>().sharedMesh=CrystalMesh();
            crystal.GetComponent<Renderer>().sharedMaterial=ember;
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4;
                var mark=Primitive(root.transform,"RuneMark_"+i,PrimitiveType.Cube,
                    new Vector3(3.9f+Mathf.Sin(angle)*.40f,.075f,2.35f+Mathf.Cos(angle)*.40f),new Vector3(.055f,.024f,.12f),rune);
                mark.transform.rotation=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0);
            }

            var hero=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Hero),root.transform) as GameObject;
            if(hero==null) throw new InvalidOperationException("Reviewed Ranger visual missing.");
            hero.name="MenuHero_Ranger"; StripAuthority(hero);
            hero.transform.SetPositionAndRotation(new Vector3(2.05f,0,.2f),Quaternion.Euler(0,158,0));
            var animator=hero.GetComponentInChildren<Animator>(true);
            var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            animator.runtimeAnimatorController=set.Controller; animator.applyRootMotion=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            hero.transform.position+=Vector3.up*(.135f-TinyHeroBakeoff.VertexBounds(hero).min.y);
            // Current production Avatar only; no candidate rig or movement controller is selected here.
            animator.gameObject.AddComponent<AnimatorSpeedCoordinator>();
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var socket=new GameObject("MenuSwordSocket").transform; socket.SetParent(hand,false);
            // Same Ranger palm reference as the accepted gameplay socket; metre gear cancels bone scale.
            socket.localPosition=new Vector3(0,.0155958f,0);
            socket.localScale=new Vector3(1/hand.lossyScale.x,1/hand.lossyScale.y,1/hand.lossyScale.z);
            var sword=PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab"),socket) as GameObject;
            sword.name="MenuSword"; StripAuthority(sword);

            Directional(root.transform,"Sun",new Vector3(32,-34,0),1.25f,new Color(1f,.84f,.65f));
            Directional(root.transform,"Rim",new Vector3(25,142,0),.65f,new Color(.4f,.8f,.78f));
            var glow=new GameObject("EmberGlow").AddComponent<Light>(); glow.transform.SetParent(root.transform,false);
            glow.transform.position=new Vector3(3.9f,1.2f,2.35f); glow.type=LightType.Point;
            glow.range=3; glow.intensity=1.3f; glow.color=new Color(1,.38f,.12f); glow.shadows=LightShadows.None;
            var menu=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MainMenuPlaceholder>(true)).Single();
            var serialized=new SerializedObject(menu); serialized.FindProperty("_useSceneBackdrop").boolValue=true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static Material Material(string label,Color color,bool emission=false,bool textured=false)
        {
            string path=Materials+"/"+label+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null)
            {
                mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.name=label;
                mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",.25f);
                if(emission) { mat.SetColor("_EmissionColor",color*1.3f); mat.EnableKeyword("_EMISSION"); }
                AssetDatabase.CreateAsset(mat,path);
            }
            if(mat.GetColor("_BaseColor")!=color) { mat.SetColor("_BaseColor",color); EditorUtility.SetDirty(mat); }
            // Palette-atlas UVs belong to KayKit meshes, NOT Unity primitive UVs.
            var atlas=textured ? AssetDatabase.LoadAssetAtPath<Texture2D>(M6EnvironmentSetup.KitRoot+"texture/dungeon_texture.png") : null;
            if(mat.GetTexture("_BaseMap")!=atlas) { mat.SetTexture("_BaseMap",atlas); EditorUtility.SetDirty(mat); }
            return mat;
        }
        static Mesh CrystalMesh()
        {
            const string folder="Assets/_Game/Art/Meshes/Menu";
            const string path=folder+"/EmberCrystal.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(existing!=null) return existing;
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var points=new[]{Vector3.up*.5f,Vector3.down*.5f,Vector3.right*.5f,Vector3.forward*.5f,Vector3.left*.5f,Vector3.back*.5f};
            var indices=new[]{0,3,2,0,4,3,0,5,4,0,2,5,1,2,3,1,3,4,1,4,5,1,5,2};
            var mesh=new Mesh {name="EmberCrystal"};
            mesh.vertices=indices.Select(i=>points[i]).ToArray(); mesh.triangles=Enumerable.Range(0,indices.Length).ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
        static GameObject Primitive(Transform parent,string label,PrimitiveType type,Vector3 position,Vector3 scale,Material mat)
        {
            var go=GameObject.CreatePrimitive(type); go.name=label; go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(position,Quaternion.identity); go.transform.localScale=scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial=mat;
            return go;
        }
        static void Kit(Transform parent,string source,string label,Vector3 position,float size,float yaw,Material mat,bool height)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(M6EnvironmentSetup.KitRoot+"fbx/"+source+".fbx");
            if(asset==null) throw new InvalidOperationException("Reviewed kit member missing: "+source);
            var go=PrefabUtility.InstantiatePrefab(asset,parent) as GameObject; go.name=label; StripAuthority(go);
            var renderers=go.GetComponentsInChildren<Renderer>(true);
            var bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            go.transform.localScale*=size/(height ? bounds.size.y : bounds.size.x);
            go.transform.rotation=Quaternion.Euler(0,yaw,0);
            bounds=renderers[0].bounds; foreach(var r in renderers) { r.sharedMaterials=r.sharedMaterials.Select(_=>mat).ToArray(); bounds.Encapsulate(r.bounds); }
            // Fit geometry to an actual ground plane, including imported pivot offsets.
            go.transform.position+=position-new Vector3(bounds.center.x,
                source.StartsWith("floor_",StringComparison.Ordinal) ? bounds.max.y : bounds.min.y,bounds.center.z);
        }
        static void StripAuthority(GameObject go)
        {
            foreach(var component in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach(var collider in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var body in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
        }
        static void Directional(Transform parent,string label,Vector3 rotation,float intensity,Color color)
        {
            var light=new GameObject(label).AddComponent<Light>(); light.transform.SetParent(parent,false);
            light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(rotation);
            light.intensity=intensity; light.color=color; light.shadows=label=="Sun" ? LightShadows.Soft : LightShadows.None;
        }
    }
}
