using System;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenAnimationTimingTests
    {
        [TestCase("ChargeWindup", 0f)]
        [TestCase("Charge", .24f)]
        [TestCase("ChargeRecovery", .78f)]
        [TestCase("Combo", 0f)]
        public void CurrentBaselineStillPassesLiteralOffsets(string state, float fraction)
        {
            var root = new GameObject("Warden offset fixture");
            try
            {
                var presenter = root.AddComponent<WardenAnimationPresenter>();
                var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
                presenter.Configure(null, null, set);
                var method = typeof(WardenAnimationPresenter).GetMethod("ResolveNormalizedOffset", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var phase = Enum.Parse(method.GetParameters()[0].ParameterType, state);
                float actual = (float)method.Invoke(presenter, new[] { phase });
                Assert.That(actual, Is.EqualTo(fraction).Within(.000001f), "Baseline diagnostic only, not alignment acceptance.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void ChargeAuditUsesMeasuredSpeedScaledEntry()
        {
            var map = AttackTimingAudit.ReadMappings().Find(m => m.id == "warden.charge");
            float speed = Mathf.Clamp(map.clip.length * .54f / map.duration, .25f, 3f);
            Assert.That(map.clipStart, Is.EqualTo(.24f * speed).Within(.000001f));
            Assert.That(map.clipEnd - map.clipStart, Is.EqualTo(.54f * map.clip.length).Within(.000001f));
            Assert.That(map.duration, Is.EqualTo(.76f).Within(.000001f));
            Assert.That(map.windowStart, Is.EqualTo(.08f).Within(.000001f));
            Assert.That(map.windowEnd, Is.EqualTo(.68f).Within(.000001f));
        }
    }
}
