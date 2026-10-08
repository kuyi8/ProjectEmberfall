#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Real offline NewGame/cache lifecycle only; no Peer, warp, natural camera, image or performance claim.</summary>
    public sealed class CameraSceneLifecycleTests
    {
        private const string Sanctum = "20_Sanctum";
        private const string CameraSource = "Assets/_Game/Scripts/Gameplay/Movement/ThirdPersonCameraRig.cs";
        private static readonly string[] SanctumWrappers =
            { "SanctumArchiveWest", "SanctumArchiveEast", "SanctumCapWest", "SanctumCapEast" };
        private static readonly string[] Parameters =
        {
            "_target", "_input", "_targeting", "_distance", "_pivotHeight", "_mouseSensitivity",
            "_gamepadDegreesPerSecond", "_minimumPitch", "_maximumPitch", "_collisionRadius", "_collisionMask"
        };
        private ThirdPersonCameraRig _rig;
        private PlayerCombatActor _actor;
        private Vector3 _birthPosition;
        private Quaternion _birthRotation;
        private bool _enabledTouched, _originalEnabled, _ownSanctumRequested;
        private Scene _ownSanctum;
        private string _saveOverride;
        private CameraOccluder[] _baseline;
        private Dictionary<string, string> _sources;
        private Dictionary<string, object> _parameters;

        [SetUp]
        public void FreezeSourcesAndRequireExistingIsolatedOfflineSave()
        {
            _rig = null; _actor = null; _baseline = null; _parameters = null; _sources = null;
            _enabledTouched = _ownSanctumRequested = false; _ownSanctum = default;
            _saveOverride = M2RouteFlowController.EditorTestSavePath;
            Assert.That(_saveOverride, Is.Not.Null.And.Not.Empty, "PlayModeSaveIsolation must precede NewGame.");
            string isolation = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_saveOverride).StartsWith(isolation, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(SessionRuntime.Current.Snapshot.Mode, Is.EqualTo(SessionMode.Offline));
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
                "Never stop or replace a pre-existing listening Peer.");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            _sources = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(path => new[] { path, path + ".meta" }).Concat(new[] { CameraSource, CameraSource + ".meta" })
                .Distinct().ToDictionary(path => path, Hash);
        }

        [UnityTearDown]
        public IEnumerator RestoreOnlyOwnedSceneAndChangedEnabledState()
        {
            try
            {
                if (_ownSanctumRequested)
                {
                    Scene scene = _ownSanctum.IsValid() ? LoadedSceneByHandle(_ownSanctum.handle) :
                        SceneManager.GetSceneByName(Sanctum);
                    if (scene.IsValid() && scene.isLoaded && scene.name == Sanctum)
                        yield return SceneManager.UnloadSceneAsync(scene);
                    _ownSanctumRequested = false;
                    _ownSanctum = default;
                }
            }
            finally
            {
                if (_enabledTouched && _rig != null) _rig.enabled = _originalEnabled;
                if (_sources != null)
                    foreach (var source in _sources)
                        Assert.That(Hash(source.Key), Is.EqualTo(source.Value), source.Key + " source SHA");
                Assert.That(M2RouteFlowController.EditorTestSavePath, Is.EqualTo(_saveOverride), "Never change global save references.");
            }
            if (_rig != null && _baseline != null && _rig.enabled) AssertCache("cleanup", _baseline);
        }

        [UnityTest]
        public IEnumerator RealNewGame_TwoAdditiveCyclesAndDisabledRig_UseExactOriginalDiscovery()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 8f;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
            { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
            Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized && flow.enabled, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(_saveOverride)));
            Assert.That(ContentPackageRuntime.IsInitialized, Is.True);
            _actor = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(_actor, Is.Not.Null);
            Assert.That(_actor.enabled && _actor.Model != null, Is.True);
            Assert.That(Camera.main, Is.Not.Null);
            _rig = Camera.main.GetComponent<ThirdPersonCameraRig>(); Assert.That(_rig, Is.Not.Null);
            Assert.That(_rig.enabled, Is.True); _originalEnabled = _rig.enabled;
            yield return null; yield return null;
            _birthPosition = _actor.transform.position; _birthRotation = _actor.transform.rotation;
            Assert.That(Vector3.Distance(_birthPosition, new Vector3(0f, 1.05f, -4f)), Is.LessThan(.01f),
                "Keep the actual default Actor and camera; never stage another position.");
            _parameters = Parameters.ToDictionary(name => name, name => Field(name).GetValue(_rig));
            Assert.That(SceneManager.GetSceneByName(Sanctum).IsValid(), Is.False);
            _baseline = AssertCache("newgame").ToArray();
            Assert.That(_baseline.Any(item => item.gameObject.scene.name == "10_EmberValley"), Is.True);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                yield return LoadOwnedSanctum();
                CameraOccluder[] additions = NamedSanctumOccluders(), cache = AssertCache("loaded-" + cycle);
                foreach (var old in _baseline) Assert.That(cache, Does.Contain(old));
                foreach (var added in additions) Assert.That(cache.Count(item => item == added), Is.EqualTo(1));
                int[] removedIds = additions.Select(item => item.GetInstanceID()).ToArray();
                yield return UnloadOwnedSanctum();
                cache = AssertCache("unloaded-" + cycle, _baseline);
                Assert.That(cache.Select(item => item.GetInstanceID()).Intersect(removedIds), Is.Empty);
                Assert.That(additions.All(item => item == null), Is.True, "Actual scene objects must be destroyed.");
                AssertOriginalActorAndParameters();
            }
            _enabledTouched = true; _rig.enabled = false;
            AssertDisabled("disabled-before-load");
            yield return LoadOwnedSanctum();
            AssertDisabled("disabled-after-load");
            CameraOccluder[] disabledLoadAdditions = NamedSanctumOccluders();
            _rig.enabled = true; // OnEnable must refresh synchronously, not wait for Start or the next scene event.
            var enabledCache = AssertCache("reenabled-with-sanctum");
            foreach (var old in _baseline) Assert.That(enabledCache, Does.Contain(old));
            foreach (var added in disabledLoadAdditions) Assert.That(enabledCache.Count(item => item == added), Is.EqualTo(1));
            _rig.enabled = false;
            AssertDisabled("disabled-before-unload");
            yield return UnloadOwnedSanctum();
            AssertDisabled("disabled-after-unload"); // An unremoved sceneUnloaded subscription would repopulate it.
            _rig.enabled = true;
            AssertCache("reenabled-baseline", _baseline);
            AssertOriginalActorAndParameters();
        }

        private IEnumerator LoadOwnedSanctum()
        {
            Scene before = SceneManager.GetSceneByName(Sanctum);
            Assert.That(!before.IsValid() || !before.isLoaded, Is.True);
            _ownSanctum = default;
            _ownSanctumRequested = true;
            yield return SceneManager.LoadSceneAsync(Sanctum, LoadSceneMode.Additive);
            Scene scene = SceneManager.GetSceneByName(Sanctum);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True); _ownSanctum = scene;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("10_EmberValley"));
        }

        private IEnumerator UnloadOwnedSanctum()
        {
            Assert.That(_ownSanctumRequested && _ownSanctum.IsValid() && _ownSanctum.isLoaded, Is.True);
            Scene scene = LoadedSceneByHandle(_ownSanctum.handle);
            Assert.That(scene.IsValid() && scene.isLoaded && scene.name == Sanctum, Is.True);
            Assert.That(scene.handle, Is.EqualTo(_ownSanctum.handle), "Unload only this fixture's requested scene.");
            yield return SceneManager.UnloadSceneAsync(scene);
            _ownSanctumRequested = false; _ownSanctum = default;
            yield return null;
        }

        private static CameraOccluder[] NamedSanctumOccluders()
        {
            var root = SceneManager.GetSceneByName(Sanctum).GetRootGameObjects().Single(item => item.name == "ImportedEnvironmentDressing");
            return SanctumWrappers.Select(name =>
            {
                Transform wrapper = root.transform.Find(name); Assert.That(wrapper, Is.Not.Null, name);
                var cameras = wrapper.GetComponents<CameraOccluder>(); Assert.That(cameras, Has.Length.EqualTo(1), name);
                return cameras[0];
            }).ToArray();
        }

        private CameraOccluder[] AssertCache(string stage, CameraOccluder[] expected = null)
        {
            var cache = (CameraOccluder[])Field("_artOccluders").GetValue(_rig);
            Assert.That(cache, Is.Not.Null, stage);
            Assert.That(cache.All(item => item != null), Is.True, stage + " no destroyed/fake-null references");
            Assert.That(cache.Select(item => item.GetInstanceID()).Distinct().Count(), Is.EqualTo(cache.Length), stage + " no duplicates");
            Assert.That(cache, Is.EqualTo(Object.FindObjectsOfType<CameraOccluder>(true)), stage + " original inclusion/order");
            if (expected != null) Assert.That(cache, Is.EquivalentTo(expected), stage + " exact baseline membership");
            TestContext.Out.WriteLine(stage + ": frame=" + Time.frameCount + ", cache=" + cache.Length);
            return cache;
        }

        private void AssertDisabled(string stage)
        {
            Assert.That(_rig.enabled, Is.False);
            Assert.That(Field("_artOccluders").GetValue(_rig), Is.Null, stage + " disabled/unsubscribed cache");
        }

        private void AssertOriginalActorAndParameters()
        {
            Assert.That(Vector3.Distance(_actor.transform.position, _birthPosition), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(_actor.transform.rotation, _birthRotation), Is.LessThan(.01f));
            foreach (var parameter in _parameters)
                Assert.That(Field(parameter.Key).GetValue(_rig), Is.EqualTo(parameter.Value), parameter.Key);
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
        }

        private static FieldInfo Field(string name)
        {
            var field = typeof(ThirdPersonCameraRig).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name); return field;
        }

        private static Scene LoadedSceneByHandle(int handle) => Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt).FirstOrDefault(scene => scene.handle == handle);

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
    }
}
#endif
