#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Opt-in observations of already executed queries; never repeats or changes physics.</summary>
    public static class KnifeQueryTrace
    {
        [Serializable]
        public struct Entry
        {
            public string kind, colliderName;
            public int frame, sequence, projectileId, colliderId, targetId;
            public double realtime;
            public Vector3 origin, direction, point, halfExtents;
            public Quaternion orientation;
            public float distance, radius;
            public bool hit;
        }
        public static Action<Entry> Observer;
        public static void Record(Entry entry)
        {
            entry.frame = Time.frameCount;
            entry.realtime = Time.realtimeSinceStartupAsDouble;
            Observer?.Invoke(entry);
        }
    }
}
#endif
