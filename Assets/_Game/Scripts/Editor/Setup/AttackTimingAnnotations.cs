using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    // Editor assembly only. Never referenced by a runtime prefab, presenter or damage authority.
    public sealed class AttackTimingAnnotations : ScriptableObject
    {
        [Serializable]
        public sealed class Contact
        {
            public string actionId;
            public AnimationClip clip;
            public float contactSeconds = -1f;
            public int sourceFrame = -1;
            public bool confirmed;
            public string observedBy;
            public string evidencePath;
            public string observation;
            public string observedClipHash;
            // Natural domain-clock measurements, NOT converted clip timestamps.
            public bool synchronizationObserved;
            public float naturalContactTime = -1f;
            public float firstDamageTime = -1f;
            public float maxSampleGap = -1f;
            public string observedTimingHash;
        }

        public List<Contact> contacts = new List<Contact>();
    }
}
