using System.Linq;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class MenuDioramaTests
    {
        [Test]
        public void DioramaHasRealGeometryButNoPhysicsOrGameplayAuthority()
        {
            var previous=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene(MenuDioramaSetup.ScenePath);
                var root=scene.GetRootGameObjects().Single(r=>r.name==MenuDioramaSetup.RootName);
                Assert.That(root.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(20));
                Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
                Assert.That(root.GetComponentsInChildren<Rigidbody>(true),Is.Empty);
                Assert.That(root.GetComponentsInChildren<PlayerCombatActor>(true),Is.Empty);
                Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c is AnimatorSpeedCoordinator),Is.True);
                var hero=root.transform.Find("MenuHero_Ranger");
                Assert.That(hero,Is.Not.Null);
                var animator=hero.GetComponentInChildren<Animator>(true);
                Assert.That(animator.avatar.isValid && animator.isHuman,Is.True);
                Assert.That(animator.applyRootMotion,Is.False);
                Assert.That(animator.GetComponent<AnimatorSpeedCoordinator>(),Is.Not.Null);
                Assert.That(root.transform.Find("EmberCrystal").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.EqualTo(24));
                Assert.That(hero.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="MenuSword"),Is.True);
                var menu=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MainMenuPlaceholder>(true)).Single();
                Assert.That(new SerializedObject(menu).FindProperty("_useSceneBackdrop").boolValue,Is.True);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        [Test]
        public void ReapplyingDoesNotDuplicateOrRebuildAcceptedMenuObjects()
        {
            var previous=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene(MenuDioramaSetup.ScenePath);
                var root=scene.GetRootGameObjects().Single(r=>r.name==MenuDioramaSetup.RootName);
                int identity=root.GetInstanceID(),count=root.GetComponentsInChildren<Transform>(true).Length;
                MenuDioramaSetup.ApplyToScene(scene);
                Assert.That(scene.GetRootGameObjects().Count(r=>r.name==MenuDioramaSetup.RootName),Is.EqualTo(1));
                Assert.That(root.GetInstanceID(),Is.EqualTo(identity));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(count));
                Assert.That(scene.isDirty,Is.False);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }
    }
}
