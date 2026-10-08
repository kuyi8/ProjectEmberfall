#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Real AudioSource -> listener PCM at gameplay camera distance. Not OS/speaker/hearing approval.</summary>
    public sealed class ImpactAudioOutputTests
    {
        readonly List<Object> _owned=new List<Object>();
        readonly List<Row> _rows=new List<Row>();
        readonly List<MixRow> _mixRows=new List<MixRow>();
        readonly List<AudioSource> _otherSources=new List<AudioSource>();
        readonly List<bool> _sourceMute=new List<bool>();
        readonly List<AudioListener> _otherListeners=new List<AudioListener>();
        readonly List<bool> _listenerEnabled=new List<bool>();
        int _rate,_vsync;
        string _output;
        AudioSource _voice;
        Transform _ear;
        ImpactListenerPcmCapture _capture;
        double _previousPollAt;

        [Serializable] sealed class CaptureEvidence
        {
            public string source="Read-only OnAudioFilterRead on actual fixture AudioListener, interleaved stereo post-spatial-mix; no source/clip PCM substitution.";
            public int channels=2,sampleRate,callbacks,invalidChannelCallbacks,capturedFrames,targetFrames;
            public int rmsWindowFrames=1024,rmsHopFrames=256,framePollBlocks,framePollNonzeroBlocks;
            public double requestedSeconds,dspStart,dspEnd,maximumFramePollGapSeconds;
            public double framePollMaximumBlockRmsDbfs=-180,framePollPeakDbfs=-180;
        }

        [Serializable] sealed class MixRow
        {
            public string layout,scope;
            public Vector3 listener;
            public string[] voices;
            public int blocks,nonzeroBlocks,saturatedSamples;
            public int maximumTrackedVoices;
            public double maximumBlockRmsDbfs=-180,peakDbfs=-180;
            public double sampledWindowRmsDbfs=-180,squareSum;
            public long sampleCount;
            public CaptureEvidence capture;
        }
        [Serializable] sealed class MixEvidence
        {
            public string scope="Actual continuous two-channel Unity Listener PCM, fixed 1024 frames/channel RMS with hop256; whole-window energy is consecutive PCM, NOT prior overlapping frame-poll energy or LUFS. Parallel old GetOutputData is diagnostic only. Controlled presentation dispatch, not a natural fight, external device output, all variants or a music mix. Remote authored preset is unchanged; concurrent runtime attenuation DOES affect its output. No limiter or master-volume override.";
            public MixRow[] rows;
        }

        [Serializable] sealed class Row
        {
            public string grade,surface,clip,scope;
            public float distance,volume,spatialBlend,minDistance,pitch;
            public int blocks,nonzeroBlocks;
            public double maximumBlockRmsDbfs,peakDbfs;
            public CaptureEvidence capture;
        }
        [Serializable] sealed class Evidence
        {
            public string scope="Actual continuous two-channel Unity listener PCM in a disposable isolated fixture, 1024 samples/channel RMS windows with hop256, peak block RMS NOT whole-clip/LUFS. Same clip, pitch, placement and sampling method for authored legacy/new A/B. Parallel old GetOutputData is diagnostic only. No spatializer/mixer/other sounds. Does NOT certify external speaker level, simultaneous-combat headroom, human loudness or Flesh/Metal perceptual equality.";
            public Row[] rows;
        }

        [UnitySetUp] public IEnumerator Setup()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            foreach(var flow in Object.FindObjectsOfType<M2RouteFlowController>())Assert.That(flow.SavePath,Does.Contain("IsolatedSaves"));
            Assert.That(AudioListener.pause,Is.False);
            Assert.That(AudioListener.volume,Is.EqualTo(1f),"Do not silently override master volume for a passed audibility test.");
            Assert.That(AudioSettings.GetConfiguration().speakerMode,Is.EqualTo(AudioSpeakerMode.Stereo));
            foreach(var source in Object.FindObjectsOfType<AudioSource>())
            {_otherSources.Add(source);_sourceMute.Add(source.mute);source.mute=true;}
            foreach(var listener in Object.FindObjectsOfType<AudioListener>())
            {_otherListeners.Add(listener);_listenerEnabled.Add(listener.enabled);listener.enabled=false;}
            var ear=new GameObject("Review_ImpactListener");_owned.Add(ear);
            ear.transform.position=new Vector3(900,2,900);ear.AddComponent<AudioListener>();
            _capture=ear.AddComponent<ImpactListenerPcmCapture>();
            _ear=ear.transform;
            _rate=UnityEngine.Application.targetFrameRate;_vsync=QualitySettings.vSyncCount;
            UnityEngine.Application.targetFrameRate=120;QualitySettings.vSyncCount=0;
            _output=Path.GetFullPath("Builds/ArtReview/impact-audio/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(_output);yield return new WaitForSecondsRealtime(.12f);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(_capture!=null)_capture.Stop();
            if(_output!=null)File.WriteAllText(_output+"/actual-listener.json",JsonUtility.ToJson(new Evidence{rows=_rows.ToArray()},true));
            if(_output!=null&&_mixRows.Count>0)File.WriteAllText(_output+"/actual-mix.json",JsonUtility.ToJson(new MixEvidence{rows=_mixRows.ToArray()},true));
            foreach(var item in _owned)if(item!=null)Object.Destroy(item);
            for(int i=0;i<_otherSources.Count;i++)if(_otherSources[i]!=null)_otherSources[i].mute=_sourceMute[i];
            for(int i=0;i<_otherListeners.Count;i++)if(_otherListeners[i]!=null)_otherListeners[i].enabled=_listenerEnabled[i];
            UnityEngine.Application.targetFrameRate=_rate;QualitySettings.vSyncCount=_vsync;
            _owned.Clear();_rows.Clear();_mixRows.Clear();_otherSources.Clear();_sourceMute.Clear();_otherListeners.Clear();_listenerEnabled.Clear();
            yield return null;
        }

        [UnityTest] public IEnumerator SimultaneousCategories_AtMidDistance_HaveActualListenerHeadroom()
        { yield return PlayMix("mid-distance",6,5); }

        [UnityTest] public IEnumerator SimultaneousCategories_ListenerBetweenVoices_HaveActualListenerHeadroom()
        { yield return PlayMix("between-voices",2,2.5f); }

        [UnityTest] public IEnumerator IndividualCategories_ReportActualContributionAtNearPlacement()
        {
            foreach(string category in new[]{"local","remote","warden","defense","seal"})
                yield return PlayMix("individual-"+category,2,2.5f,category);
        }

        [UnityTest] public IEnumerator MixedWindowEnergy_IsNotInvertedAgainstMatchedSoloStrike()
        {
            yield return PlayMix("matched-solo",2,2.5f,"local");var solo=_mixRows[_mixRows.Count-1];
            yield return PlayMix("matched-six",2,2.5f);var mix=_mixRows[_mixRows.Count-1];
            Assert.That(mix.sampledWindowRmsDbfs,Is.GreaterThanOrEqualTo(solo.sampledWindowRmsDbfs-.5d),
                "Predeclared matched 1.5s overlapping-block energy comparison, not LUFS or human loudness. Preserve any inverted result.");
        }

        [UnityTest] public IEnumerator RepeatedConfirmedPresentation_ThreeDispatchesDoNotDropOrAccumulateGain()
        {yield return PlayMix("repeated-three",2,2.5f,null,true);}

        [UnityTest] public IEnumerator LocalFleshPresence_AllVariantsProduceMeasuredFiveDbLift()
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            string immutable=EditorJsonUtility.ToJson(set);
            var old=Object.Instantiate(set);_owned.Add(old);
            var so=new SerializedObject(old);so.FindProperty("_ownerFleshPresenceDb").floatValue=0;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach(var grade in new[]{HitFeedbackGrade.Light,HitFeedbackGrade.Heavy,HitFeedbackGrade.Sweep})
                for(int i=0;i<3;i++){
                    yield return PlayAndMeasure(old,true,grade,ImpactSurface.Flesh,6,"pre-presence-same-placement",i);
                    var baseline=_rows[_rows.Count-1];
                    yield return PlayAndMeasure(set,true,grade,ImpactSurface.Flesh,6,"flesh-presence-five-db",i);
                    var revised=_rows[_rows.Count-1];
                    Assert.That(revised.clip,Is.EqualTo(baseline.clip));Assert.That(revised.pitch,Is.EqualTo(baseline.pitch));
                    Assert.That(revised.maximumBlockRmsDbfs-baseline.maximumBlockRmsDbfs,Is.InRange(4.5d,5.5d),"Actual same-clip listener boost, not source-PCM substitution.");
                    Assert.That(revised.peakDbfs,Is.LessThanOrEqualTo(-3d));
                }
            Assert.That(EditorJsonUtility.ToJson(set),Is.EqualTo(immutable));
        }

        [UnityTest] public IEnumerator SameVoice_LocalFleshThenRemoteMetal_DoesNotLeakPresence()
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            var root=new GameObject("Review_SameVoiceOwnerChange");_owned.Add(root);root.transform.position=_ear.position+Vector3.forward*6;
            var presenter=root.AddComponent<CombatHitFeedbackPresenter>();presenter.Configure(null,null,set,null);
            presenter.Enqueue(new CombatImpactPresentationEvent(root.transform.position,CombatImpactStyle.Steel,1,1,9,HitFeedbackGrade.Light,ImpactSurface.Flesh));
            yield return null;yield return null;
            var source=root.GetComponentInChildren<AudioSource>();Assert.That(source.clip,Is.Not.Null);
            var sameSource=source;
            yield return new WaitForSecondsRealtime(.1f);
            presenter.SetOwner(false);
            presenter.Enqueue(new CombatImpactPresentationEvent(root.transform.position,CombatImpactStyle.Steel,2,2,10,HitFeedbackGrade.Light,ImpactSurface.Metal));
            yield return null;yield return null;
            Assert.That(root.GetComponentInChildren<AudioSource>(),Is.SameAs(sameSource));
            Assert.That(source.spatialBlend,Is.EqualTo(.65f));Assert.That(source.minDistance,Is.EqualTo(2f));
            bool match=false;
            for(int i=0;i<3;i++){
                var p=set.Select(HitFeedbackGrade.Light,ImpactSurface.Metal,-1,(i+.1f)/3f,.5f);
                if(p.Clip==source.clip){Assert.That(source.volume,Is.EqualTo(.6f*p.Gain).Within(.000001f));match=true;}
            }
            Assert.That(match,Is.True);
        }

        [UnityTest] public IEnumerator BoostedFleshMetalGuardBreakExecution_ActualConcurrentListenerHasHeadroom()
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            var roots=new List<GameObject>();
            var grades=new[]{HitFeedbackGrade.Heavy,HitFeedbackGrade.Light,HitFeedbackGrade.GuardBreak,HitFeedbackGrade.Execution};
            var surfaces=new[]{ImpactSurface.Flesh,ImpactSurface.Metal,ImpactSurface.Metal,ImpactSurface.Flesh};
            var capture=StartCapture(1.5);
            for(int i=0;i<grades.Length;i++){
                var root=new GameObject("Review_SpecialMix_"+grades[i]);_owned.Add(root);roots.Add(root);
                root.transform.position=_ear.position+Vector3.forward*2;
                var presenter=root.AddComponent<CombatHitFeedbackPresenter>();presenter.Configure(null,null,set,null);
                typeof(CombatHitFeedbackPresenter).GetField("_audioRandom",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(presenter,new VariantRandom(0));
                presenter.Enqueue(new CombatImpactPresentationEvent(root.transform.position,CombatImpactStyle.Steel,1,1,10+i,grades[i],surfaces[i]));
            }
            var row=new MixRow{layout="boosted-flesh-metal-break-execution",listener=_ear.position,capture=capture,
                scope="Four simultaneous confirmed presenter dispatches at2m; boosted HeavyFlesh plus unchanged LightMetal/GuardBreak/Execution. Controlled stress, not all-content clipping guarantee.",voices=new string[4]};
            _mixRows.Add(row);
            yield return null;yield return null;
            for(int i=0;i<roots.Count;i++)row.voices[i]=VoiceLabel(roots[i].GetComponentInChildren<AudioSource>());
            var left=new float[1024];var right=new float[1024];
            double end=Time.realtimeSinceStartupAsDouble+1.5;
            while(Time.realtimeSinceStartupAsDouble<end){
                row.maximumTrackedVoices=Math.Max(row.maximumTrackedVoices,CombatAudioVoiceBudget.CurrentActiveVoiceCount);
                PollDiagnostic(capture,left,right);yield return null;
            }
            yield return AwaitCapture();var measured=StopCapture(capture);
            row.blocks=measured.blocks;row.nonzeroBlocks=measured.nonzeroBlocks;row.saturatedSamples=measured.saturatedSamples;
            row.maximumBlockRmsDbfs=measured.maximumBlockRmsDbfs;row.peakDbfs=measured.peakDbfs;
            row.sampledWindowRmsDbfs=measured.windowRmsDbfs;row.squareSum=measured.squareSum;row.sampleCount=measured.sampleCount;
            Assert.That(row.nonzeroBlocks,Is.GreaterThan(0));Assert.That(row.peakDbfs,Is.LessThanOrEqualTo(-.1d));
            Assert.That(row.saturatedSamples,Is.Zero);Assert.That(row.maximumTrackedVoices,Is.EqualTo(4));
        }

        IEnumerator PlayMix(string layout,float strikeDistance,float bossDistance,string only=null,bool repeated=false)
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            string immutable=EditorJsonUtility.ToJson(set);
            var roots=new List<GameObject>();
            Func<string,Vector3,GameObject> create=(name,offset)=>
            {var go=new GameObject(name);go.transform.position=_ear.position+offset;roots.Add(go);_owned.Add(go);return go;};
            var localRoot=create("Review_LocalStrike",Vector3.forward*strikeDistance);
            var local=localRoot.AddComponent<CombatHitFeedbackPresenter>();local.Configure(null,null,set,null);
            var remoteRoot=create("Review_RemotePlayerStrike",Vector3.back*strikeDistance);
            var remote=remoteRoot.AddComponent<CombatHitFeedbackPresenter>();remote.Configure(null,null,set,null);remote.SetOwner(false);
            foreach(var presenter in new[]{local,remote})
                typeof(CombatHitFeedbackPresenter).GetField("_audioRandom",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(presenter,new System.Random(17));
            var defense=create("Review_Defense",Vector3.forward*strikeDistance).AddComponent<PerfectDefenseFeedbackPresenter>();
            var bossRoot=create("Review_Warden",Vector3.right*bossDistance);
            var bossSource=bossRoot.AddComponent<AudioSource>();
            var boss=bossRoot.AddComponent<WardenAudioPresenter>();boss.Configure(null,bossSource);
            var gateRoot=create("Review_Seal",Vector3.left*5);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);_owned.Add(blocker);blocker.transform.SetParent(gateRoot.transform,false);
            var barrier=M2StageBarrier.CreateManual(gateRoot,blocker);
            yield return null;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var capture=StartCapture(1.5);
            if(only==null||only=="local")local.Enqueue(new CombatImpactPresentationEvent(localRoot.transform.position,CombatImpactStyle.Steel,1,1,9,HitFeedbackGrade.Heavy,ImpactSurface.Metal));
            if(only==null||only=="remote")remote.Enqueue(new CombatImpactPresentationEvent(remoteRoot.transform.position,CombatImpactStyle.Steel,1,1,10,HitFeedbackGrade.Light,ImpactSurface.Metal));
            // Invoke the actual presentation endpoints only. No damage/quest/fight is synthesized.
            if(only==null||only=="defense")typeof(PerfectDefenseFeedbackPresenter).GetMethod("Present",flags).Invoke(defense,new object[]{PerfectDefenseKind.Guard});
            var phase=(AudioClip)typeof(WardenAudioPresenter).GetField("_phaseBreak",flags).GetValue(boss);
            if(only==null||only=="warden")
            {typeof(WardenAudioPresenter).GetMethod("Play",flags).Invoke(boss,new object[]{phase,.92f});typeof(WardenAudioPresenter).GetMethod("PlayBlastImpact",flags).Invoke(boss,null);}
            if(only==null||only=="seal")barrier.SetOpen(true);
            yield return null;
            Assert.That(local.LastAudioFrame,Is.EqualTo(local.LastFeedbackFrame));
            Assert.That(remote.LastAudioFrame,Is.EqualTo(remote.LastFeedbackFrame));
            if(only==null||only=="local")Assert.That(local.LastAudioFrame,Is.GreaterThanOrEqualTo(0));
            if(only==null||only=="remote")Assert.That(remote.LastAudioFrame,Is.GreaterThanOrEqualTo(0));
            Assert.That(barrier.SoundSequence,Is.EqualTo(only==null||only=="seal"?1:0));
            var row=new MixRow{layout=layout,listener=_ear.position,scope=only==null?"Simultaneous six one-shot/clip voices from five actual presenters; representative and stress placement, not evidence that a natural encounter produced this overlap.":"Single category ablation at the same near placement; only "+only+" is dispatched; other configured voices below remain silent.",
                capture=capture,
                voices=new[]{VoiceLabel(localRoot.GetComponentInChildren<AudioSource>()),VoiceLabel(remoteRoot.GetComponentInChildren<AudioSource>()),
                    "PerfectDefenseConfirm pitch1.15 gain.62 at"+strikeDistance+"m", "Warden_PhaseBreak gain.92 + Warden_BlastImpact gain.9 at"+bossDistance+"m", "SealDissolve pitch1.2 gain.65 at5m"}};
            _mixRows.Add(row);
            var left=new float[1024];var right=new float[1024];
            int dispatches=1;double started=Time.realtimeSinceStartupAsDouble;
            double deadline=Time.realtimeSinceStartupAsDouble+1.5;
            while(Time.realtimeSinceStartupAsDouble<deadline)
            {
                row.maximumTrackedVoices=Math.Max(row.maximumTrackedVoices,CombatAudioVoiceBudget.CurrentActiveVoiceCount);
                if(repeated&&dispatches<3&&Time.realtimeSinceStartupAsDouble-started>=dispatches*.18)
                {
                    dispatches++;
                    local.Enqueue(new CombatImpactPresentationEvent(localRoot.transform.position,CombatImpactStyle.Steel,(ulong)dispatches,dispatches,9,HitFeedbackGrade.Heavy,ImpactSurface.Metal));
                    remote.Enqueue(new CombatImpactPresentationEvent(remoteRoot.transform.position,CombatImpactStyle.Steel,(ulong)dispatches,dispatches,10,HitFeedbackGrade.Light,ImpactSurface.Metal));
                    typeof(PerfectDefenseFeedbackPresenter).GetMethod("Present",flags).Invoke(defense,new object[]{PerfectDefenseKind.Guard});
                }
                PollDiagnostic(capture,left,right);
                yield return null;
            }
            yield return AwaitCapture();
            var measured=StopCapture(capture);
            row.blocks=measured.blocks;row.nonzeroBlocks=measured.nonzeroBlocks;row.saturatedSamples=measured.saturatedSamples;
            row.maximumBlockRmsDbfs=measured.maximumBlockRmsDbfs;row.peakDbfs=measured.peakDbfs;
            row.squareSum=measured.squareSum;row.sampleCount=measured.sampleCount;row.sampledWindowRmsDbfs=measured.windowRmsDbfs;
            Assert.That(row.nonzeroBlocks,Is.GreaterThan(0));
            Assert.That(row.peakDbfs,Is.LessThanOrEqualTo(-.1d),"Predeclared final-listener peak gate; preserve failure before any mix correction.");
            Assert.That(row.saturatedSamples,Is.Zero,"No sampled channel may saturate.");
            if(only==null)Assert.That(row.maximumTrackedVoices,Is.EqualTo(6),"Both one-shots on the Warden source must count separately.");
            if(only==null||only=="local")Assert.That(localRoot.GetComponentInChildren<AudioSource>().volume,Is.EqualTo(.7f).Within(.00001f),"The local nominal gain must return, not accumulate attenuation.");
            if(only==null||only=="remote")Assert.That(remoteRoot.GetComponentInChildren<AudioSource>().volume,Is.EqualTo(.5467938f).Within(.00001f));
            if(repeated)
            {
                Assert.That(local.PresentedCount,Is.EqualTo(3));Assert.That(remote.PresentedCount,Is.EqualTo(3));
                Assert.That(local.LastAudioFrame,Is.EqualTo(local.LastFeedbackFrame));Assert.That(remote.LastAudioFrame,Is.EqualTo(remote.LastFeedbackFrame));
            }
            Assert.That(EditorJsonUtility.ToJson(set),Is.EqualTo(immutable));
            foreach(var root in roots)if(root!=null)Object.Destroy(root);
            yield return new WaitForSecondsRealtime(.12f);
        }

        static string VoiceLabel(AudioSource source)
        {
            if(source.clip==null)return "not dispatched";
            return source.clip.name+" volume"+source.volume+" blend"+source.spatialBlend+" minDistance"+source.minDistance+" pitch"+source.pitch;
        }

        [UnityTest] public IEnumerator ConfirmedStrikes_ReachLocalCameraFloor_AndRemoteMixRemainsLegacy()
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            string immutable=EditorJsonUtility.ToJson(set);
            var old=Object.Instantiate(set);_owned.Add(old);
            var so=new SerializedObject(old);so.FindProperty("_ownerVoiceVolume").floatValue=.6f;
            so.FindProperty("_ownerSpatialBlend").floatValue=.65f;so.FindProperty("_ownerMinDistance").floatValue=2;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach(var grade in new[]{HitFeedbackGrade.Light,HitFeedbackGrade.Heavy})
                foreach(var surface in new[]{ImpactSurface.Flesh,ImpactSurface.Metal})
                {
                    yield return PlayAndMeasure(old,true,grade,surface,6,"legacy-local");
                    Row baseline=_rows[_rows.Count-1];
                    yield return PlayAndMeasure(set,true,grade,surface,6,"new-local");
                    Row revised=_rows[_rows.Count-1];
                    Assert.That(revised.clip,Is.EqualTo(baseline.clip));Assert.That(revised.pitch,Is.EqualTo(baseline.pitch));
                    Assert.That(revised.nonzeroBlocks,Is.GreaterThan(0),"No actual listener signal.");
                    Assert.That(revised.maximumBlockRmsDbfs,Is.GreaterThanOrEqualTo(-30d),"Predeclared local transient audibility floor at6m; not external hearing approval.");
                    Assert.That(revised.peakDbfs,Is.LessThanOrEqualTo(-3d),"Reserve single-voice headroom; mixed-combat headroom still needs its own review.");
                    Assert.That(revised.maximumBlockRmsDbfs-baseline.maximumBlockRmsDbfs,Is.GreaterThanOrEqualTo(3d),"Expected measurable local improvement, not parameter-only proof.");
                }
            yield return PlayAndMeasure(set,false,HitFeedbackGrade.Light,ImpactSurface.Metal,6,"remote-unchanged");
            var remote=_rows[_rows.Count-1];
            Assert.That(remote.spatialBlend,Is.EqualTo(.65f));Assert.That(remote.minDistance,Is.EqualTo(2));
            Assert.That(set.VoiceVolume(false),Is.EqualTo(.6f));
            Assert.That(EditorJsonUtility.ToJson(set),Is.EqualTo(immutable));
        }

        sealed class VariantRandom : System.Random
        {
            readonly float _choice;bool _pitch;
            public VariantRandom(int variant){_choice=(variant+.1f)/3f;}
            public override double NextDouble(){bool pitch=_pitch;_pitch=!_pitch;return pitch?.5d:_choice;}
        }

        IEnumerator PlayAndMeasure(CombatImpactAudioSet set,bool owner,HitFeedbackGrade grade,ImpactSurface surface,float distance,string scope,int? variant=null)
        {
            var root=new GameObject("Review_ConfirmedImpactVoice");_owned.Add(root);
            root.transform.position=new Vector3(900,2,900+distance);
            var presenter=root.AddComponent<CombatHitFeedbackPresenter>();presenter.Configure(null,null,set,null);presenter.SetOwner(owner);
            typeof(CombatHitFeedbackPresenter).GetField("_audioRandom",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(presenter,variant.HasValue?(System.Random)new VariantRandom(variant.Value):new System.Random(17));
            var plannedRandom=variant.HasValue?(System.Random)new VariantRandom(variant.Value):new System.Random(17);
            var planned=set.Select(grade,surface,-1,(float)plannedRandom.NextDouble(),(float)plannedRandom.NextDouble());
            Assert.That(planned.Clip,Is.Not.Null);
            var capture=StartCapture(planned.Clip.length/planned.Pitch+.15);
            presenter.Enqueue(new CombatImpactPresentationEvent(root.transform.position,CombatImpactStyle.Steel,1,1,9,grade,surface));
            yield return null;_voice=root.GetComponentInChildren<AudioSource>();
            Assert.That(_voice.clip,Is.Not.Null);Assert.That(presenter.LastAudioFrame,Is.EqualTo(presenter.LastFeedbackFrame));
            Assert.That(_voice.clip,Is.EqualTo(planned.Clip));Assert.That(_voice.pitch,Is.EqualTo(planned.Pitch));
            var row=new Row{grade=grade.ToString(),surface=surface.ToString(),clip=_voice.clip.name,scope=scope,distance=distance,
                volume=_voice.volume,spatialBlend=_voice.spatialBlend,minDistance=_voice.minDistance,pitch=_voice.pitch,
                maximumBlockRmsDbfs=-180,peakDbfs=-180,capture=capture};_rows.Add(row);
            var left=new float[1024];var right=new float[1024];
            double deadline=Time.realtimeSinceStartupAsDouble+_voice.clip.length/_voice.pitch+.15;
            while(Time.realtimeSinceStartupAsDouble<deadline)
            {
                PollDiagnostic(capture,left,right);
                yield return null;
            }
            yield return AwaitCapture();
            var measured=StopCapture(capture);
            row.blocks=measured.blocks;row.nonzeroBlocks=measured.nonzeroBlocks;
            row.maximumBlockRmsDbfs=measured.maximumBlockRmsDbfs;row.peakDbfs=measured.peakDbfs;
            presenter.enabled=false;Object.Destroy(root);yield return new WaitForSecondsRealtime(.12f);
        }

        CaptureEvidence StartCapture(double seconds)
        {
            int rate=AudioSettings.outputSampleRate;
            var evidence=new CaptureEvidence{sampleRate=rate,requestedSeconds=seconds,dspStart=AudioSettings.dspTime,
                targetFrames=(int)Math.Ceiling(rate*seconds)};
            _previousPollAt=Time.realtimeSinceStartupAsDouble;
            _capture.Arm(rate,seconds);return evidence;
        }

        void PollDiagnostic(CaptureEvidence evidence,float[] left,float[] right)
        {
            double now=Time.realtimeSinceStartupAsDouble;
            evidence.maximumFramePollGapSeconds=Math.Max(evidence.maximumFramePollGapSeconds,now-_previousPollAt);
            _previousPollAt=now;
            AudioListener.GetOutputData(left,0);AudioListener.GetOutputData(right,1);
            double square=0,peak=0;
            for(int i=0;i<left.Length;i++)
            {square+=(double)left[i]*left[i]+(double)right[i]*right[i];peak=Math.Max(peak,Math.Max(Math.Abs(left[i]),Math.Abs(right[i])));}
            double rms=Math.Sqrt(square/(2*left.Length));evidence.framePollBlocks++;
            if(rms>1e-8){evidence.framePollNonzeroBlocks++;evidence.framePollMaximumBlockRmsDbfs=Math.Max(evidence.framePollMaximumBlockRmsDbfs,20*Math.Log10(rms));}
            if(peak>1e-8)evidence.framePollPeakDbfs=Math.Max(evidence.framePollPeakDbfs,20*Math.Log10(peak));
        }

        IEnumerator AwaitCapture()
        {
            // Waiting for the last DSP chunk does not extend the fixed PCM time window.
            double deadline=Time.realtimeSinceStartupAsDouble+.25;
            while(_capture.CapturedFrames<_capture.TargetFrames&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
        }

        ImpactListenerPcmCapture.Statistics StopCapture(CaptureEvidence evidence)
        {
            var snapshot=_capture.Stop();
            evidence.dspEnd=AudioSettings.dspTime;evidence.callbacks=snapshot.callbacks;
            evidence.invalidChannelCallbacks=snapshot.invalidChannelCallbacks;evidence.capturedFrames=snapshot.frames;
            Assert.That(snapshot.callbacks,Is.GreaterThan(0),"The actual Listener audio-thread callback must run; no synthetic PCM fallback.");
            Assert.That(snapshot.invalidChannelCallbacks,Is.Zero,"The listener PCM must actually be interleaved stereo.");
            Assert.That(snapshot.frames,Is.EqualTo(evidence.targetFrames),"Capture the entire predeclared bounded window, not selected positive portions.");
            return snapshot.Analyze();
        }
    }
}
#endif
