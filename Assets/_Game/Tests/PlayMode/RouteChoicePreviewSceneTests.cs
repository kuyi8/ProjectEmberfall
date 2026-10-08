#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Quests.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Controlled story preparation, actual InputSystem E and rendered production HUD.</summary>
    public sealed class RouteChoicePreviewSceneTests
    {
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        M2RouteFlowController flow; PlayerCombatActor player; PlayerInteractor interactor; M2RouteHud hud;
        Keyboard keyboard; EditorWindow view; PropertyInfo selected; object group,ownedSize; int previousIndex,ownedIndex;
        CursorLockMode previousLock; bool previousCursor; string output;
        [Serializable] sealed class Capture { public string choice;public int width,height,selectedIndex,frame;public Rect panel,overlay,guide; }
        [Serializable] sealed class Report
        {
            public string scope="Actual isolated Valley story facts and production choice HUD at 960x540/2560x1440. Controlled public interactions/lethal callbacks prepare the rune phase; synthetic native InputSystem E chooses the actual nearest candidate, with no direct choice call. Own editor view sizes only. Modal suppression, actual encounter closure and Continue are separate checks. Not natural route/difficulty, human input, foreground, performance, unreachable crowded-state render, NET choice parity or a new package.";
            public Capture[] captures;
        }
        readonly System.Collections.Generic.List<Capture> captures=new System.Collections.Generic.List<Capture>();

        [UnitySetUp] public IEnumerator Begin()
        {
            keyboard=null;captures.Clear();Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            previousLock=Cursor.lockState;previousCursor=Cursor.visible;
            M2LaunchIntent.RequestNewGame();yield return SceneManager.LoadSceneAsync("10_EmberValley",LoadSceneMode.Single);yield return null;yield return null;
            Bind();Assert.That(flow.SavePath,Is.EqualTo(M2RouteFlowController.EditorTestSavePath));Assert.That(flow.TryTalkToScout(),Is.True);
            keyboard=InputSystem.AddDevice<Keyboard>();
            output=Path.GetFullPath("Builds/ArtReview/choice-preview/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            var assembly=typeof(EditorWindow).Assembly;var viewType=assembly.GetType("UnityEditor.GameView");view=EditorWindow.GetWindow(viewType);view.Show();view.Focus();
            selected=viewType.GetProperty("selectedSizeIndex",All);previousIndex=(int)selected.GetValue(view);
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes");var sizes=typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            group=sizesType.GetMethod("GetGroup").Invoke(sizes,new object[]{0});var sizeType=assembly.GetType("UnityEditor.GameViewSize");var kind=assembly.GetType("UnityEditor.GameViewSizeType");
            ownedSize=Activator.CreateInstance(sizeType,All,null,new object[]{Enum.Parse(kind,"FixedResolution"),960,540,"EmberfallChoiceFixture-"+Guid.NewGuid().ToString("N")},null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{ownedSize});ownedIndex=Count()-1;
            Assert.That(SizeAt(ownedIndex),Is.SameAs(ownedSize));
        }
        void Bind()
        {flow=Object.FindObjectOfType<M2RouteFlowController>();player=Object.FindObjectOfType<PlayerCombatActor>();interactor=Object.FindObjectOfType<PlayerInteractor>();hud=Object.FindObjectOfType<M2RouteHud>();}
        [UnityTearDown] public IEnumerator End()
        {
            if(keyboard!=null){InputSystem.ResetDevice(keyboard);InputSystem.RemoveDevice(keyboard);keyboard=null;}
            if(selected!=null)selected.SetValue(view,previousIndex);
            if(group!=null&&ownedSize!=null)
            {
                int index=-1;for(int i=0;i<Count();i++)if(ReferenceEquals(SizeAt(i),ownedSize))index=i;
                if(index>=0){int builtin=(int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group,null);Assert.That(index,Is.GreaterThanOrEqualTo(builtin));group.GetType().GetMethod("RemoveCustomSize").Invoke(group,new object[]{index});}
                for(int i=0;i<Count();i++)Assert.That(SizeAt(i),Is.Not.SameAs(ownedSize));
            }
            Cursor.lockState=previousLock;Cursor.visible=previousCursor;
            if(output!=null)File.WriteAllText(output+"/report.json",JsonUtility.ToJson(new Report{captures=captures.ToArray()},true));
            yield return null;
        }

        [UnityTest] public IEnumerator EmberRune_ActualEAndContinue() => Scenario("EmberRune");
        [UnityTest] public IEnumerator GuardRune_ActualEAndContinue() => Scenario("GuardRune");
        [UnityTest] public IEnumerator SupplyRoute_ActualEAndContinue() => Scenario("SupplyRoute");
        [UnityTest] public IEnumerator RiskRoute_ActualEAndContinue() => Scenario("RiskRoute");
        [UnityTest] public IEnumerator StagedReinforcement_ActualEAndContinue() => Scenario("StagedReinforcement");
        [UnityTest] public IEnumerator TogetherReinforcement_ActualEAndContinue() => Scenario("TogetherReinforcement");

        IEnumerator Scenario(string choice)
        {
            bool rune=choice=="EmberRune"||choice=="GuardRune";
            if(rune)
            {
                var context=new InteractionContext(player);var forest=flow.ForestTemplate;
                foreach(var actor in Object.FindObjectsOfType<CombatTarget>())
                    if(actor.name=="Enemy_Fogwalker_Forest"||actor.name=="Enemy_RunePriest_Forest")
                        Assert.That(actor.ReceiveDamage(new DamageRequest(player.CombatantId,91200+actor.CombatantId,9999,100,AttackTag.Heavy)).Killed,Is.True);
                Assert.That(forest.SigilPickup.TryInteract(context),Is.True);
                Assert.That(Object.FindObjectsOfType<M2RouteInteractable>().Single(i=>i.Role==M2RouteRole.Seal&&i.StableId.Value=="seal:forest").TryInteract(context),Is.True);
                Assert.That(forest.Phase,Is.EqualTo(ForestSealPhase.RuneChoice));
            }
            IInteractable candidate=rune?(IInteractable)(choice=="EmberRune"?flow.ForestTemplate.EmberRune:flow.ForestTemplate.GuardRune)
                :Object.FindObjectsOfType<RouteEnrichmentInteractable>().Single(i=>i.Kind.ToString()==choice);
            Vector3 point=candidate.InteractionTransform.position;Place(point);yield return null;yield return null;
            Assert.That(interactor.CurrentCandidate,Is.SameAs(candidate));Assert.That(candidate.IsAvailable,Is.True);
            Assert.That(hud.TryGetChoicePreview(out var p,out _),Is.True);
            int expected=choice=="EmberRune"||choice=="SupplyRoute"||choice=="StagedReinforcement"?0:1;
            Assert.That(p.SelectedIndex,Is.EqualTo(expected));AssertTextFits(p);
            // Use the same existing modal predicate; do not introduce another pause path.
            var rig=Object.FindObjectOfType<ThirdPersonCameraRig>();var pause=typeof(M2RouteHud).GetMethod("SetPaused",All);
            pause.Invoke(hud,new object[]{true});Assert.That(hud.TryGetChoicePreview(out _,out _),Is.False);Assert.That(rig.IsLookInputBlocked,Is.True);
            pause.Invoke(hud,new object[]{false});Assert.That(Time.timeScale,Is.EqualTo(1));Assert.That(hud.TryGetChoicePreview(out _,out _),Is.True);
            Object.FindObjectOfType<InputTelemetryOverlay>().SetVisible(true);
            typeof(M2RouteHud).GetField("_showGuide",All).SetValue(hud,true); // Fixture-only F1 visibility, not authoring.
            yield return CaptureAt(choice,ownedIndex,960,540,expected);
            yield return CaptureAt(choice,FindSize(2560,1440),2560,1440,expected);
            Assert.That(interactor.CurrentCandidate,Is.SameAs(candidate),"Stationary pre-input target must remain the highlighted choice.");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.E));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            AssertSavedChoice(choice);Assert.That(candidate.IsAvailable,Is.False);Assert.That(hud.TryGetChoicePreview(out _,out _),Is.False);
            M2LaunchIntent.RequestContinue();yield return SceneManager.LoadSceneAsync("10_EmberValley",LoadSceneMode.Single);yield return null;yield return null;Bind();
            Assert.That(flow.SavePath,Is.EqualTo(M2RouteFlowController.EditorTestSavePath));AssertSavedChoice(choice);
            Place(point);yield return null;Assert.That(hud.TryGetChoicePreview(out _,out _),Is.False,"Saved choice cannot become another available selection after Continue.");
        }

        [UnityTest] public IEnumerator ActualBEngagementClosesChoiceWithoutInventingACrowdedSelectableState()
        {
            var candidate=Object.FindObjectsOfType<RouteEnrichmentInteractable>().Single(i=>i.Kind==RouteEnrichmentInteractionKind.StagedReinforcement);
            Place(candidate.transform.position);yield return null;Assert.That(hud.TryGetChoicePreview(out _,out _),Is.True);
            Place(new Vector3(0,0,21));yield return null;yield return null;
            Assert.That(GameObject.Find("Content_AshGuardPass_v1").GetComponent<CombatEncounterCoordinator>().IsTelemetryActive,Is.True);
            Assert.That(flow.CanChooseReinforcement(),Is.False);Assert.That(candidate.IsAvailable,Is.False);
            Place(candidate.transform.position);yield return null;Assert.That(hud.TryGetChoicePreview(out _,out _),Is.False);
        }

        void AssertSavedChoice(string choice)
        {
            switch(choice)
            {
                case "EmberRune":Assert.That(flow.ForestTemplate.RuneChoice,Is.EqualTo(ForestRuneChoice.Ember));Assert.That(player.ActiveRuneBlessing,Is.EqualTo(RuneBlessing.Ember));break;
                case "GuardRune":Assert.That(flow.ForestTemplate.RuneChoice,Is.EqualTo(ForestRuneChoice.Guard));Assert.That(player.ActiveRuneBlessing,Is.EqualTo(RuneBlessing.Guard));break;
                case "SupplyRoute":Assert.That(flow.RouteChoice,Is.EqualTo(EmberValleyRouteChoice.Supply));break;
                case "RiskRoute":Assert.That(flow.RouteChoice,Is.EqualTo(EmberValleyRouteChoice.Risk));break;
                case "StagedReinforcement":Assert.That(flow.ReinforcementChoice,Is.EqualTo(AshReinforcementChoice.Staged));break;
                case "TogetherReinforcement":Assert.That(flow.ReinforcementChoice,Is.EqualTo(AshReinforcementChoice.Together));break;
                default:Assert.Fail("Unknown fixture choice");break;
            }
        }
        static void AssertTextFits(RouteChoicePreview p)
        {
            var fontType=typeof(M2RouteHud).Assembly.GetType("Emberfall.UI.RuntimeGuiFont",true);
            var font=(Font)fontType.GetProperty("Chinese",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
            Assert.That(font,Is.Not.Null);
            var body=new GUIStyle{font=font,fontSize=16,wordWrap=true,padding=new RectOffset(0,0,0,0)};
            var heading=new GUIStyle(body){fontSize=18,fontStyle=FontStyle.Bold};var note=new GUIStyle(body){fontSize=14};
            foreach(string text in new[]{p.LeftBody,p.RightBody})Assert.That(body.CalcHeight(new GUIContent(text),196),Is.LessThanOrEqualTo(78),text);
            foreach(string text in new[]{p.Left,p.Right})Assert.That(heading.CalcHeight(new GUIContent(text),196),Is.LessThanOrEqualTo(24),text);
            Assert.That(note.CalcHeight(new GUIContent(RouteChoicePreview.Note),452),Is.LessThanOrEqualTo(20));
        }
        void Place(Vector3 feet)
        {Vector3 offset=player.transform.position-player.NavigationFootPosition;var body=player.GetComponent<CharacterController>();body.enabled=false;player.transform.position=feet+offset+Vector3.up*.04f;body.enabled=true;Physics.SyncTransforms();}
        IEnumerator CaptureAt(string choice,int index,int width,int height,int expected)
        {
            selected.SetValue(view,index);view.Repaint();view.Focus();float deadline=Time.realtimeSinceStartup+10;
            while((Screen.width!=width||Screen.height!=height)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(Screen.width,Is.EqualTo(width));Assert.That(Screen.height,Is.EqualTo(height));for(int i=0;i<5;i++)yield return null;
            Assert.That(hud.TryGetChoicePreview(out var p,out var l),Is.True);Assert.That(p.SelectedIndex,Is.EqualTo(expected));
            float scale=EmberfallGuiTheme.Scale(width,height);Rect overlay=Object.FindObjectOfType<InputTelemetryOverlay>().PanelRect;overlay=new Rect(overlay.x/scale,overlay.y/scale,overlay.width/scale,overlay.height/scale);
            Rect guide=PresentationBottomHudLayout.Resolve(width/scale,height/scale).Guide;
            Assert.That(l.Panel.Overlaps(overlay),Is.False);Assert.That(l.Panel.Overlaps(guide),Is.False);
            string path=output+"/"+choice+"-"+width+"x"+height+".png";ScreenCapture.CaptureScreenshot(path);byte[] bytes=null;deadline=Time.realtimeSinceStartup+10;
            while(Time.realtimeSinceStartup<deadline)
            {
                try{if(File.Exists(path))bytes=File.ReadAllBytes(path);}catch(IOException){bytes=null;}
                if(bytes!=null&&bytes.Length>32&&bytes[bytes.Length-8]==73&&bytes[bytes.Length-7]==69&&bytes[bytes.Length-6]==78&&bytes[bytes.Length-5]==68)break;
                bytes=null;yield return null;
            }
            Assert.That(bytes,Is.Not.Null);var texture=new Texture2D(2,2);
            try{Assert.That(ImageConversion.LoadImage(texture,bytes),Is.True);Assert.That(texture.width,Is.EqualTo(width));Assert.That(texture.height,Is.EqualTo(height));}finally{Object.DestroyImmediate(texture);}
            captures.Add(new Capture{choice=choice,width=width,height=height,selectedIndex=expected,frame=Time.frameCount,panel=l.Panel,overlay=overlay,guide=guide});
        }
        int Count()=>(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null);
        object SizeAt(int index)=>group.GetType().GetMethod("GetGameViewSize").Invoke(group,new object[]{index});
        int FindSize(int width,int height)
        {for(int i=0;i<Count();i++){var size=SizeAt(i);var type=size.GetType();if((int)type.GetProperty("width").GetValue(size)==width&&(int)type.GetProperty("height").GetValue(size)==height&&type.GetProperty("sizeType").GetValue(size).ToString()=="FixedResolution")return i;}throw new InvalidOperationException("Required actual fixed Game View absent; no setting guessed.");}
    }
}
#endif
