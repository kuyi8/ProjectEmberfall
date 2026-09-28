using System;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Editor-sampled same-source whole-hand pose; never runtime state.</summary>
    public sealed class KnifeGripPose : ScriptableObject
    {
        [Serializable] public struct Joint
        {
            public HumanBodyBones bone;
            public Quaternion rotation;
        }
        public Joint[] joints;
        public Vector3 gripLocalPosition;
        public Quaternion gripLocalRotation;
        public string sourcePath, sourceHash, avatarPath;
        public float sourceTime;
    }
}
