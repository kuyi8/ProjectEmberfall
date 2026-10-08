using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Emberfall.Editor.Review
{
    /// <summary>Actual Game View UI evidence, neutral input, active AI and isolated save.
    /// Camp residency performance is NOT combat/worst-case/Player performance.</summary>
    [InitializeOnLoad]
    public static class PresentationFoundationReview
    {
        const string Key="Emberfall.PresentationReview.";
        const string Source="Assets/_Game/Scenes/10_EmberValley.unity";
        const string Prefix="Assets/_Game/Scenes/__PresentationReview_";
        static int stage,nextFrame,lastRecordedFrame;
        static double deadline;
        static string output;
        static Gamepad pad;
        static float menuLoadMs;
        static AsyncOperation menuLoad;
        static double menuLoadStarted;
        static readonly List<float> Frames=new List<float>();
        static PresentationFoundationReview()
        { EditorApplication.playModeStateChanged+=OnMode; EditorApplication.update+=Tick; }
        public static string Begin()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path!=Source || UnityEngine.SceneManagement.SceneManager.sceneCount!=1 || SessionState.GetBool(Key+"active",false))
                throw new InvalidOperationException("Single saved idle Valley required.");
            string dir=Path.GetFullPath("Builds/ArtReview/presentation/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            string temp=Prefix+Guid.NewGuid().ToString("N")+".unity";
            Directory.CreateDirectory(dir);
            if(!EditorSceneManager.SaveScene(scene,temp,true)) throw new IOException("Cannot preserve scene copy.");
            try
            {
                scene=EditorSceneManager.OpenScene(temp);
                var flow=new SerializedObject(UnityEngine.Object.FindObjectOfType<M2RouteFlowController>());
                flow.FindProperty("_saveFileName").stringValue=Path.Combine(dir,"isolated-save.json");
                flow.ApplyModifiedPropertiesWithoutUndo(); EditorSceneManager.SaveScene(scene);
                SessionState.SetString(Key+"output",dir); SessionState.SetString(Key+"temp",temp);
                SessionState.SetBool(Key+"active",true);
                M2LaunchIntent.RequestNewGame(); EditorApplication.isPlaying=true; return dir;
            }
            catch { EditorSceneManager.OpenScene(Source); AssetDatabase.DeleteAsset(temp); throw; }
        }
        static void OnMode(PlayModeStateChange mode)
        {
            if(!SessionState.GetBool(Key+"active",false)) return;
            if(mode==PlayModeStateChange.EnteredPlayMode)
            {
                output=SessionState.GetString(Key+"output",""); stage=0; Frames.Clear();
                // Preserve isolation even if a user activates a route after our menu capture.
                M2RouteFlowController.EditorTestSavePath=Path.Combine(output,"isolated-save.json");
                lastRecordedFrame=-1; nextFrame=Time.frameCount+60; deadline=EditorApplication.timeSinceStartup+75;
                pad=InputSystem.AddDevice<Gamepad>("PresentationNeutralInput");
                InputSystem.QueueStateEvent(pad,new GamepadState());
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            }
            if(mode==PlayModeStateChange.ExitingPlayMode && pad!=null && pad.added) InputSystem.RemoveDevice(pad);
            if(mode==PlayModeStateChange.EnteredEditMode)
            {
                string temp=SessionState.GetString(Key+"temp","");
                if(!temp.StartsWith(Prefix,StringComparison.Ordinal) || !temp.EndsWith(".unity",StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected cleanup target.");
                EditorSceneManager.OpenScene(Source); AssetDatabase.DeleteAsset(temp);
                M2RouteFlowController.EditorTestSavePath=null;
                SessionState.SetBool(Key+"active",false); SessionState.EraseString(Key+"temp");
            }
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key+"active",false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new TimeoutException("Presentation capture timeout.");
                if((stage==1 || stage==5) && Time.frameCount!=lastRecordedFrame)
                { Frames.Add(Time.unscaledDeltaTime*1000); lastRecordedFrame=Time.frameCount; }
                if(Time.frameCount<nextFrame) return;
                if(stage==0)
                {
                    var flow=UnityEngine.Object.FindObjectOfType<M2RouteFlowController>();
                    if(flow==null || !flow.IsInitialized) return;
                    if(Path.GetFullPath(flow.SavePath)!=Path.Combine(output,"isolated-save.json")) throw new InvalidOperationException("Save isolation failed.");
                    Capture("01-route"); stage=1; nextFrame=Time.frameCount+1200; return;
                }
                if(stage==1)
                {
                    var sorted=Frames.OrderBy(x=>x).ToArray();
                    File.WriteAllText(output+"/frame-residency.json",JsonUtility.ToJson(new Performance {
                        scope="Editor active-AI neutral-input camp residency; elapsed frames, NOT CPU/GPU/GC, worst combat or Player",
                        frames=sorted.Length, meanMs=sorted.Length>0 ? sorted.Average() : 0,
                        p99Ms=sorted.Length>0 ? sorted[Math.Min(sorted.Length-1,(int)(sorted.Length*.99))] : 0,
                        width=Screen.width,height=Screen.height, saveIsolated=true },true));
                    UnityEngine.Object.FindObjectOfType<M2RouteHud>().SetPaused(true);
                    stage=2; nextFrame=Time.frameCount+20; return;
                }
                if(stage==2) { Capture("02-pause"); stage=3; nextFrame=Time.frameCount+12; return; }
                if(stage==3)
                {
                    if(!File.Exists(output+"/02-pause.png")) return;
                    menuLoadStarted=EditorApplication.timeSinceStartup;
                    menuLoad=UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("01_MainMenu");
                    stage=31; nextFrame=Time.frameCount; return;
                }
                if(stage==31)
                {
                    if(menuLoad==null || !menuLoad.isDone) return;
                    if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="01_MainMenu")
                        throw new InvalidOperationException("Async menu load did not become active.");
                    menuLoadMs=(float)((EditorApplication.timeSinceStartup-menuLoadStarted)*1000);
                    stage=4; nextFrame=Time.frameCount+40; return;
                }
                if(stage==4)
                { Capture("03-menu"); Frames.Clear(); lastRecordedFrame=-1; stage=5; nextFrame=Time.frameCount+1200; return; }
                if(stage==5 && File.Exists(output+"/03-menu.png"))
                {
                    if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="01_MainMenu")
                        throw new InvalidOperationException("Menu changed during measurement; do not mislabel another scene.");
                    var sorted=Frames.OrderBy(x=>x).ToArray();
                    File.WriteAllText(output+"/menu-residency.json",JsonUtility.ToJson(new Performance {
                        scope="Editor menu residency and warm async scene load through completion; NOT cold Player startup/CPU/GPU/combat",
                        frames=sorted.Length,meanMs=sorted.Average(),p99Ms=sorted[Math.Min(sorted.Length-1,(int)(sorted.Length*.99))],
                        width=Screen.width,height=Screen.height,saveIsolated=true,sceneLoadMs=menuLoadMs },true));
                    // Open a view ONLY for layout inspection. Keyboard behavior is independently
                    // tested through real injected actions, not certified by this screenshot.
                    UnityEngine.Object.FindObjectOfType<MainMenuPlaceholder>().SetNetworkPanelVisible(true);
                    stage=6; nextFrame=Time.frameCount+30; return;
                }
                if(stage==6) { Capture("04-room"); stage=7; nextFrame=Time.frameCount+12; return; }
                if(stage==7 && File.Exists(output+"/04-room.png"))
                {
                    File.WriteAllText(output+"/complete.txt","Actual route/pause/menu/room UI; room view directly opened for layout ONLY. Keyboard tested separately. No task-completion/natural-route claim.");
                    EditorApplication.isPlaying=false;
                }
            }
            catch(Exception exception)
            { File.WriteAllText(output+"/failed.txt",exception.ToString()); Debug.LogException(exception); EditorApplication.isPlaying=false; }
        }
        static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
        [Serializable] sealed class Performance
        { public string scope; public int frames,width,height; public float meanMs,p99Ms,sceneLoadMs; public bool saveIsolated; }
    }
}
