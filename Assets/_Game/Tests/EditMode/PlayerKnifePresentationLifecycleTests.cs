using System.Reflection;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerKnifePresentationLifecycleTests
    {
        [Test] public void EnableBeforeActorModel_StaysUnconfiguredAndDoesNotInventState()
        {
            // UTF may itself own an unsaved untitled scene. A preview scene preserves it without saving it.
            Scene isolated = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("KnifeBeforeActorModelFixture"); root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, isolated);
            try
            {
                var actor = root.AddComponent<PlayerCombatActor>();
                var visual = root.AddComponent<PlayerKnifePresentation>();
                typeof(PlayerKnifePresentation).GetField("_actor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(visual, actor);
                Assert.That(actor.Model, Is.Null);
                var enable = typeof(PlayerKnifePresentation).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.DoesNotThrow(() => enable.Invoke(visual, null));
                Assert.That(actor.Model, Is.Null);
                Assert.That(visual.IsConfigured || visual.HeldVisible || visual.FlightVisible, Is.False);
                Assert.That(typeof(PlayerKnifePresentation).GetField("_blockedSequence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(visual), Is.EqualTo(-1));
                typeof(PlayerKnifePresentation).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(visual, null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(isolated);
            }
        }
    }
}
