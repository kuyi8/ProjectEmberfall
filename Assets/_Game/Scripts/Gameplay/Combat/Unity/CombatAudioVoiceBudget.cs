using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public enum CombatAudioImportance { Strike, Critical, WorldConfirmation }

    /// <summary>
    /// One presentation-only budget per listening client, not per attacker. Authored and
    /// solo gains survive; overlapping voices are attenuated, never discarded or delayed.
    /// This is not a limiter or a mathematical all-content clipping guarantee.
    /// </summary>
    [DefaultExecutionOrder(180)]
    public sealed class CombatAudioVoiceBudget : MonoBehaviour
    {
        sealed class Voice
        {
            public AudioSource source;
            public CombatAudioImportance importance;
            public float nominalVolume;
            public float gain=1f;
            public readonly List<double> ends=new List<double>();
        }
        static CombatAudioVoiceBudget _instance;
        readonly List<Voice> _voices=new List<Voice>();
        AudioListener _listener;
        public int ActiveVoiceCount { get; private set; }
        public static int CurrentActiveVoiceCount => _instance!=null?_instance.ActiveVoiceCount:0;
        public const float ReleaseSeconds=.12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { _instance=null; }

        public static float Gain(CombatAudioImportance importance,int activeVoices)
        {
            if(activeVoices<=1)return 1f;
            float other=activeVoices-1;
            float weight=importance==CombatAudioImportance.Critical?.2f:
                importance==CombatAudioImportance.WorldConfirmation?1.4f:1f;
            return 1f/Mathf.Sqrt(1f+weight*other);
        }

        public static void Track(AudioSource source,AudioClip clip,CombatAudioImportance importance,
            bool replace=false,float? nominalVolume=null)
        {
            if(!Application.isPlaying||source==null||clip==null)return;
            if(_instance==null)
            {
                var go=new GameObject("[Presentation] Combat Audio Budget");
                // DontSave makes runtime objects survive some Test Runner scene restores.
                // A normal runtime DontDestroyOnLoad root survives scene loads, but is
                // destroyed on leaving Play Mode instead of leaking into the Editor.
                DontDestroyOnLoad(go);
                _instance=go.AddComponent<CombatAudioVoiceBudget>();
            }
            _instance.Register(source,clip,importance,replace,nominalVolume);
        }

        void Register(AudioSource source,AudioClip clip,CombatAudioImportance importance,bool replace,float? nominalVolume)
        {
            Prune();
            var voice=_voices.Find(v=>v.source==source);
            if(voice==null)
            {voice=new Voice{source=source,nominalVolume=nominalVolume??source.volume};_voices.Add(voice);}
            else if(nominalVolume.HasValue)voice.nominalVolume=nominalVolume.Value;
            voice.importance=importance;
            if(replace)voice.ends.Clear();
            voice.ends.Add(AudioSettings.dspTime+clip.length/Math.Max(.01f,Math.Abs(source.pitch)));
            // Apply before the next mixer block; later dispatch in this frame updates all
            // already registered sources too. Never touch PCM or Unity from an audio thread.
            Apply(false);
        }

        void Update() { Prune();Apply(true); }

        void Prune()
        {
            double now=AudioSettings.dspTime;
            for(int i=_voices.Count-1;i>=0;i--)
            {
                var v=_voices[i];v.ends.RemoveAll(t=>t<=now);
                if(v.source!=null&&v.ends.Count>0&&v.source.isPlaying)continue;
                if(v.source!=null)v.source.volume=v.nominalVolume;
                _voices.RemoveAt(i);
            }
        }

        bool Contributes(Voice voice)
        {
            var source=voice.source;
            if(source==null||source.mute||!source.enabled||!source.gameObject.activeInHierarchy||voice.nominalVolume<=0)return false;
            // Only exclude a known zero-contribution spatial source. Logarithmic max
            // distance is NOT silence and a partial-2D local strike must not be culled.
            if(_listener!=null&&source.spatialBlend>=.999f&&source.rolloffMode==AudioRolloffMode.Linear&&
                Vector3.Distance(source.transform.position,_listener.transform.position)>=source.maxDistance)return false;
            return true;
        }

        void Apply(bool advanceRelease)
        {
            if(_listener==null||!_listener.enabled||!_listener.gameObject.activeInHierarchy)
            {
                _listener=null;
                foreach(var ear in FindObjectsOfType<AudioListener>())if(ear.enabled&&ear.gameObject.activeInHierarchy){_listener=ear;break;}
            }
            int count=0;
            foreach(var v in _voices)
                if(Contributes(v))count+=v.ends.Count;
            ActiveVoiceCount=count;
            foreach(var v in _voices)
                if(v.source!=null)
                {
                    float desired=Gain(v.importance,count);
                    if(desired<v.gain)v.gain=desired; // immediate attack, before mixer dispatch
                    else if(advanceRelease)v.gain=Mathf.MoveTowards(v.gain,desired,Time.unscaledDeltaTime/ReleaseSeconds);
                    v.source.volume=v.nominalVolume*v.gain;
                }
        }

        void OnDestroy()
        {
            foreach(var v in _voices)if(v.source!=null)v.source.volume=v.nominalVolume;
            _voices.Clear();if(_instance==this)_instance=null;
        }
    }
}
