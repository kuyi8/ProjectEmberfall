#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using NUnit.Framework;
using UnityEditor;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>
    /// Saved production references through real offline initialization and a real NGO/UTP
    /// Host Spawn. Read-only observations, not a second peer, phase-two combat, natural
    /// contact, rendered visual acceptance or performance proof. No AI/Flow/state forcing.
    /// </summary>
    public sealed class WardenSelectedSourceRuntimeTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string CharacterPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab";
        const string SwordPath = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RuneSword.prefab";
        const string NetworkPath = "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab";
        const string BladePath = "Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_WardenMetal.mat";
        const string HelmetMeshPath = "Assets/_Game/Art/WardenSilhouetteCandidate/Meshes/M_Warden_KayHelmet.asset";
        const string SwordMeshPath = "Assets/_Game/Art/WardenSilhouetteCandidate/Meshes/M_Warden_KaySword.asset";
        const string SwordName = "Sword_M6_Warden_Equipped", ShieldName = "Shield_Warden_Equipped";
        string previousOverride, ownedSavePath, directory;
        bool overrideInstalled, ownHostAttempted, runtimeSceneOwned;
        ISessionService service;
        GameObject ownNetworkActor;
        Dictionary<string, string> frozen;
        Dictionary<Material, string> materialMemory;
        Dictionary<Material, string> materialPaths;
        Evidence evidence;

        [Serializable] sealed class FileProof { public string path, before, after; }
        [Serializable] sealed class MaterialProof
        {
            public string stage, path, beforeJson, afterJson;
            public int frame;
            public bool available, changed;
        }
        [Serializable] sealed class Pose
        {
            public Vector3 position, localPosition, localScale;
            public Quaternion rotation, localRotation;
        }
        [Serializable] sealed class RendererSample
        {
            public string field, renderer, runtimeFirstMaterialName, runtimeFirstMaterialPath;
            public string[] materialNames, materialPaths;
            public int rendererId, frame;
            public float time;
            public bool enabled, active, slot0MpbEmpty, rendererMpbEmpty;
            public Color materialFirstColor, slot0BaseColor, slot0LegacyColor, slot0Emission, rendererBaseColor, rendererEmission;
        }
        [Serializable] sealed class Evidence
        {
            public string scope = "Real NewGame/Bootstrap initialization with existing AI/Flow left active and saved canonical blade slot 0; separate actual NGO/UTP Host Spawn of production P_M5_NetworkWarden. Only observed PhaseOne initialization/Server facts. No phase/state assignment, warp, AI disable, synthetic damage, second peer, natural-contact, human, visual or performance acceptance.";
            public string test, savePath, previousSaveOverride, actualSerializedBladePath, actualNetworkBladePath, transport;
            public string phase, state;
            public int frame;
            public bool initialized, aiEnabled, flowEnabled, spawned, host, server, client, serverFactsMatch;
            public bool rootUnchanged, sourceBytesSame, sharedMaterialMemorySame, fixtureOverrideRestored, fixtureNetworkStopped;
            public Color selectedSteel, actualWeaponBaseColor, actualShieldBaseColor;
            public Pose rootBefore, rootAfter;
            public RendererSample[] renderers;
            public FileProof[] sources;
            public MaterialProof[] materials;
            public List<MaterialProof> changedMaterialStages = new List<MaterialProof>();
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            overrideInstalled = ownHostAttempted = runtimeSceneOwned = false;
            ownNetworkActor = null; service = null; frozen = null; materialMemory = null; materialPaths = null; evidence = null; directory = null;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty,
                "Reuse the real PlayModeSaveIsolation; never run NewGame on a user's save.");
            previousOverride = M2RouteFlowController.EditorTestSavePath;
            string isolation = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(previousOverride).StartsWith(isolation, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(SessionRuntime.Current.Snapshot.Mode, Is.EqualTo(SessionMode.Offline),
                "Do not stop a pre-existing live user session.");
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
            runtimeSceneOwned = true;
            yield return SceneManager.LoadSceneAsync("01_MainMenu", LoadSceneMode.Single);
            yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>(), Is.Null);
            string label = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            directory = Path.GetFullPath("Builds/ArtReview/warden-selected-runtime/" + label);
            Directory.CreateDirectory(directory);
            string saves = Path.GetFullPath("Builds/TestResults/IsolatedSaves/warden-selected-" + label);
            Directory.CreateDirectory(saves);
            ownedSavePath = Path.Combine(saves, "emberfall-save-v1.json");
            M2RouteFlowController.EditorTestSavePath = ownedSavePath; overrideInstalled = true;
            evidence = new Evidence { previousSaveOverride = previousOverride, savePath = ownedSavePath };
            frozen = FreezeSources();
            materialMemory = frozen.Keys.Where(path => path.EndsWith(".mat", StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<Material>).Distinct().ToDictionary(material => material, material => EditorJsonUtility.ToJson(material));
            materialPaths = materialMemory.Keys.ToDictionary(material => material, material => AssetDatabase.GetAssetPath(material));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!runtimeSceneOwned) yield break;
            try
            {
                ObserveMaterialMemory("teardown-before-owned-despawn");
                if (ownNetworkActor != null)
                {
                    var networkObject = ownNetworkActor.GetComponent<NetworkObject>();
                    if (networkObject != null && networkObject.IsSpawned) networkObject.Despawn(true);
                    else Object.Destroy(ownNetworkActor);
                }
                if (ownHostAttempted && service != null) service.Shutdown();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (ownHostAttempted && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
                       Time.realtimeSinceStartup < deadline) yield return null;
                ObserveMaterialMemory("teardown-after-owned-host-shutdown-before-menu-load");
                // Unload only the runtime fixture; no Editor scene/prefab Save calls.
                yield return SceneManager.LoadSceneAsync("01_MainMenu", LoadSceneMode.Single);
                yield return null;
            }
            finally
            {
                if (overrideInstalled) M2RouteFlowController.EditorTestSavePath = previousOverride;
                overrideInstalled = false;
                if (evidence != null)
                {
                    evidence.fixtureOverrideRestored = M2RouteFlowController.EditorTestSavePath == previousOverride;
                    evidence.fixtureNetworkStopped = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
                    evidence.sources = frozen == null ? new FileProof[0] : frozen.Select(pair => new FileProof
                    { path = pair.Key, before = pair.Value, after = File.Exists(pair.Key) ? Hash(pair.Key) : "MISSING" }).ToArray();
                    evidence.sourceBytesSame = frozen != null && evidence.sources.All(row => row.before == row.after);
                    // Retain EVERY exact baseline/current JSON, not only a short-circuit
                    // bool. A missing native material is distinct from changed fields.
                    evidence.materials = MaterialProofs("final-after-menu-load", false);
                    evidence.sharedMaterialMemorySame = materialMemory != null && materialMemory.All(pair =>
                        pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value);
                    File.WriteAllText(Path.Combine(directory, "review.json"), JsonUtility.ToJson(evidence, true));
                }
            }
            if (evidence != null)
            {
                Assert.That(evidence.sourceBytesSame, Is.True, "Production prefab/material/meta/source bytes changed.");
                Assert.That(evidence.sharedMaterialMemorySame, Is.True, "A shared source material was mutated in memory.");
                Assert.That(evidence.fixtureOverrideRestored, Is.True);
                Assert.That(evidence.fixtureNetworkStopped, Is.True);
                Assert.That(Object.FindObjectOfType<M2RouteFlowController>(), Is.Null);
                Assert.That(Object.FindObjectsOfType<NetworkWarden>(), Is.Empty);
            }
        }

        [UnityTest]
        public IEnumerator ValleyNewGame_CapturesSerializedSteelBeforeActorAwake_WithLiveOriginalFlowAndAI()
        {
            evidence.test = nameof(ValleyNewGame_CapturesSerializedSteelBeforeActorAwake_WithLiveOriginalFlowAndAI);
            Material steel = SelectedSteel();
            evidence.selectedSteel = steel.color;
            // Read the already saved canonical prefab before the real scene lifecycle.
            evidence.actualSerializedBladePath = SerializedBladePath();
            Assert.That(evidence.actualSerializedBladePath, Is.EqualTo(BladePath));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 8f;
            M2RouteFlowController flow = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                flow = Object.FindObjectOfType<M2RouteFlowController>();
                if (flow != null && flow.IsInitialized) break;
                yield return null;
            }
            Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(ownedSavePath));
            Assert.That(ContentPackageRuntime.IsInitialized, Is.True);
            Assert.That(flow.Stage, Is.EqualTo(MainQuestStage.MeetScout));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(flow.enabled, Is.True);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(player, Is.Not.Null); Assert.That(player.Model, Is.Not.Null);
            WardenActor warden = Object.FindObjectsOfType<WardenActor>().Single(actor => actor.name == "Enemy_EmberWarden");
            Assert.That(flow.Warden, Is.SameAs(warden)); Assert.That(warden.Brain, Is.Not.Null);
            Assert.That(warden.enabled && warden.gameObject.activeInHierarchy && warden.HasSimulationAuthority, Is.True);
            yield return null; yield return null;
            evidence.rootBefore = CapturePose(warden.transform);
            evidence.actualWeaponBaseColor = Field<Color>(warden, "_weaponBaseColor");
            evidence.actualShieldBaseColor = Field<Color>(warden, "_shieldBaseColor");
            evidence.renderers = new[] { "_bodyRenderer", "_shieldRenderer", "_weaponRenderer", "_telegraphRenderer" }
                .Select(field => Sample(field, RequiredRenderer(warden, field), warden.transform)).ToArray();
            Renderer weapon = RequiredRenderer(warden, "_weaponRenderer");
            Renderer shield = RequiredRenderer(warden, "_shieldRenderer");
            AssertColor(evidence.actualWeaponBaseColor, steel.color, "Awake captured the pre-serialized steel endpoint, not late runtime replacement.");
            Assert.That(weapon.sharedMaterials[0].name, Does.StartWith(steel.name));
            AssertColor(weapon.sharedMaterials[0].color, steel.color, "Actual PhaseOne instance blade.");
            Assert.That(AssetDatabase.GetAssetPath(weapon.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(SwordMeshPath));
            Assert.That(weapon.GetComponent<MeshFilter>().sharedMesh.subMeshCount, Is.EqualTo(3));
            GameObject shieldRoot = Field<GameObject>(warden, "_shieldVisualRoot");
            Assert.That(shieldRoot, Is.Not.Null); Assert.That(shieldRoot.name, Is.EqualTo(ShieldName));
            Assert.That(shield.transform.IsChildOf(shieldRoot.transform), Is.True);
            AssertSelectedHelmet(warden.transform);
            MeleeEnemyActor[] melee = Object.FindObjectsOfType<MeleeEnemyActor>();
            ShieldEnemyActor[] elites = Object.FindObjectsOfType<ShieldEnemyActor>();
            Assert.That(melee, Is.Not.Empty); Assert.That(elites, Is.Not.Empty);
            Assert.That(melee.All(actor => actor.enabled && actor.HasSimulationAuthority), Is.True);
            Assert.That(elites.All(actor => actor.enabled && actor.HasSimulationAuthority), Is.True);
            ObserveMaterialMemory("offline-initialized-live-before-final-frames");
            yield return null; yield return null;
            evidence.rootAfter = CapturePose(warden.transform); evidence.rootUnchanged = SamePose(evidence.rootBefore, evidence.rootAfter);
            evidence.initialized = flow.IsInitialized; evidence.aiEnabled = warden.enabled;
            evidence.flowEnabled = flow.enabled; evidence.phase = warden.Phase.ToString(); evidence.state = warden.State.ToString();
            evidence.frame = Time.frameCount;
            Assert.That(evidence.rootUnchanged, Is.True, "Observation may not move/rotate/scale the Actor root.");
            Assert.That(warden.Phase, Is.EqualTo(WardenPhase.PhaseOne));
            Assert.That(warden.State, Is.EqualTo(WardenState.Dormant));
            AssertColor(Field<Color>(warden, "_weaponBaseColor"), steel.color, "The cached source endpoint remains steel.");
        }

        [UnityTest]
        public IEnumerator NgoHost_ActuallySpawnsSavedEquippedWarden_AndReadsUnforcedServerPhaseOneMpb()
        {
            evidence.test = nameof(NgoHost_ActuallySpawnsSavedEquippedWarden_AndReadsUnforcedServerPhaseOneMpb);
            Material steel = SelectedSteel(); evidence.selectedSteel = steel.color;
            evidence.actualSerializedBladePath = SerializedBladePath();
            GameObject prefab = Resources.Load<GameObject>("Networking/P_M5_NetworkWarden");
            Assert.That(prefab, Is.Not.Null);
            NetworkWarden source = prefab.GetComponent<NetworkWarden>(); Assert.That(source, Is.Not.Null);
            Renderer sourceWeapon = RequiredRenderer(source, "_weaponRenderer");
            evidence.actualNetworkBladePath = AssetDatabase.GetAssetPath(sourceWeapon.sharedMaterials[0]);
            Assert.That(evidence.actualNetworkBladePath, Is.EqualTo(BladePath));
            Assert.That(AssetDatabase.GetAssetPath(sourceWeapon.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(SwordMeshPath));
            RequiredRenderer(source, "_shieldRenderer");
            Assert.That(Field<GameObject>(source, "_shieldVisualRoot"), Is.Not.Null);
            foreach (string field in new[] { "_stateMarker", "_attackIndicator", "_phaseIndicator" }) RequiredRenderer(source, field);
            Assert.That(Field<Animator>(source, "_animator"), Is.Not.Null);
            ObserveMaterialMemory("host-prefab-read-before-listen");

            service = SessionRuntime.Current;
            Assert.That(service, Is.InstanceOf<DirectSessionService>());
            NetworkManager manager = NetworkManager.Singleton;
            Assert.That(manager, Is.Not.Null); Assert.That(manager.IsListening, Is.False);
            Assert.That(manager.NetworkConfig.NetworkTransport, Is.Not.Null);
            evidence.transport = manager.NetworkConfig.NetworkTransport.GetType().FullName;
            Assert.That(evidence.transport, Is.EqualTo("Unity.Netcode.Transports.UTP.UnityTransport"));
            // Reuse the existing production DirectSessionService + bootstrap registration.
            // No test-added NetworkManager, mock ownership, custom transport or forced facts.
            ownHostAttempted = true;
            Assert.That(service.StartHost(47780), Is.True, service.Snapshot.Message);
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((!manager.IsHost || !manager.IsConnectedClient) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(manager.IsListening && manager.IsHost && manager.IsServer && manager.IsConnectedClient, Is.True);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(), Is.Null, "This is a minimal Host spawn, not the two-peer Sanctum run.");
            ObserveMaterialMemory("host-listening-before-spawn");

            ownNetworkActor = Object.Instantiate(prefab);
            NetworkWarden warden = ownNetworkActor.GetComponent<NetworkWarden>();
            NetworkObject networkObject = ownNetworkActor.GetComponent<NetworkObject>();
            evidence.rootBefore = CapturePose(ownNetworkActor.transform);
            networkObject.Spawn(true); // Same genuine NGO spawn call as the production controller.
            yield return null; yield return null;
            ObserveMaterialMemory("host-spawn-after-two-real-frames");
            Assert.That(networkObject.IsSpawned && warden.IsSpawned && warden.IsServer, Is.True);
            Assert.That(manager.SpawnManager.SpawnedObjects[networkObject.NetworkObjectId], Is.SameAs(networkObject));
            WardenBrain brain = Field<WardenBrain>(warden, "_brain"); Assert.That(brain, Is.Not.Null);
            evidence.spawned = warden.IsSpawned; evidence.host = manager.IsHost; evidence.server = warden.IsServer; evidence.client = warden.IsClient;
            evidence.serverFactsMatch = warden.ReplicatedPhase == brain.Phase && warden.ReplicatedState == brain.State &&
                warden.Health == brain.Health.Current && warden.MaximumHealth == brain.Health.Maximum;
            evidence.phase = warden.ReplicatedPhase.ToString(); evidence.state = warden.ReplicatedState.ToString();
            evidence.actualWeaponBaseColor = Field<Color>(warden, "_weaponBaseColor");
            evidence.actualShieldBaseColor = Field<Color>(warden, "_shieldBaseColor");
            var samples = new List<RendererSample>();
            foreach (string field in new[] { "_stateMarker", "_attackIndicator", "_phaseIndicator", "_weaponRenderer", "_shieldRenderer" })
                samples.Add(Sample(field, RequiredRenderer(warden, field), warden.transform));
            Animator animator = Field<Animator>(warden, "_animator"); Assert.That(animator, Is.Not.Null);
            Renderer[] body = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(body, Is.Not.Empty, "NET body is the existing Animator-descendant skin, not an invented _bodyRenderer field.");
            samples.AddRange(body.Select(renderer => Sample("Animator/body", renderer, warden.transform)));
            evidence.renderers = samples.ToArray(); evidence.aiEnabled = warden.enabled; evidence.initialized = brain != null;
            Renderer weapon = RequiredRenderer(warden, "_weaponRenderer"), shield = RequiredRenderer(warden, "_shieldRenderer");
            GameObject shieldRoot = Field<GameObject>(warden, "_shieldVisualRoot");
            Assert.That(shieldRoot, Is.Not.Null); Assert.That(shieldRoot.name, Is.EqualTo(ShieldName));
            Assert.That(shield.transform.IsChildOf(shieldRoot.transform), Is.True);
            Transform swordRoot = Unique(warden.transform, SwordName);
            Assert.That(swordRoot.GetComponentsInChildren<Renderer>(true).Single(), Is.SameAs(weapon));
            Assert.That(swordRoot.parent.parent, Is.SameAs(animator.GetBoneTransform(HumanBodyBones.RightHand)));
            Assert.That(shieldRoot.transform.parent.parent, Is.SameAs(animator.GetBoneTransform(HumanBodyBones.LeftHand)));
            Assert.That(swordRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(shieldRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            AssertSelectedHelmet(warden.transform);
            Assert.That(evidence.serverFactsMatch, Is.True, "Read real Server brain facts; never assign phase/state to get expected output.");
            Assert.That(warden.ReplicatedPhase, Is.EqualTo(WardenPhase.PhaseOne));
            Assert.That(warden.ReplicatedState, Is.EqualTo(WardenState.Dormant));
            AssertColor(evidence.actualWeaponBaseColor, steel.color, "Actual spawn initialized base steel.");
            AssertColor(evidence.renderers.Single(sample => sample.field == "_weaponRenderer").slot0BaseColor, steel.color, "Real spawned weapon MPB, slot 0.");
            Assert.That(evidence.renderers.Single(sample => sample.field == "_weaponRenderer").slot0MpbEmpty, Is.False);
            AssertColor(evidence.renderers.Single(sample => sample.field == "_shieldRenderer").slot0BaseColor, evidence.actualShieldBaseColor, "Real spawned shield MPB.");
            Assert.That(shieldRoot.activeSelf, Is.True);
            Assert.That(weapon.sharedMaterials[0], Is.SameAs(steel), "NET presentation must not create a material instance.");
            Assert.That(RequiredRenderer(warden, "_attackIndicator").enabled, Is.False);
            Assert.That(RequiredRenderer(warden, "_phaseIndicator").enabled, Is.False);
            yield return null; yield return null;
            evidence.rootAfter = CapturePose(warden.transform); evidence.rootUnchanged = SamePose(evidence.rootBefore, evidence.rootAfter);
            evidence.frame = Time.frameCount;
            Assert.That(evidence.rootUnchanged, Is.True);
            RendererSample laterWeapon = Sample("_weaponRenderer/final", weapon, warden.transform);
            evidence.renderers = evidence.renderers.Concat(new[] { laterWeapon }).ToArray();
            AssertColor(laterWeapon.slot0BaseColor, steel.color, "Steel persists under actual Host Update.");
            Assert.That(warden.enabled, Is.True, "Do not disable Server AI for the observation.");
            ObserveMaterialMemory("host-final-live-observation");
        }

        MaterialProof[] MaterialProofs(string stage, bool changedOnly)
        {
            if (materialMemory == null || materialPaths == null) return new MaterialProof[0];
            return materialMemory.Select(pair =>
            {
                bool available = pair.Key != null;
                string after = available ? EditorJsonUtility.ToJson(pair.Key) : "[UNAVAILABLE_NATIVE_MATERIAL]";
                return new MaterialProof {
                    stage = stage, path = materialPaths[pair.Key], frame = Time.frameCount,
                    beforeJson = pair.Value, afterJson = after, available = available,
                    changed = !available || after != pair.Value
                };
            }).Where(row => !changedOnly || row.changed).OrderBy(row => row.path, StringComparer.Ordinal).ToArray();
        }
        void ObserveMaterialMemory(string stage)
        {
            if (evidence != null) evidence.changedMaterialStages.AddRange(MaterialProofs(stage, true));
        }

        static Material SelectedSteel()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BladePath);
            Assert.That(material, Is.Not.Null); Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            return material;
        }
        static string SerializedBladePath()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath); Assert.That(prefab, Is.Not.Null);
            Transform model = prefab.transform.Find("Model"); Assert.That(model, Is.Not.Null);
            Material[] materials = model.GetComponent<MeshRenderer>().sharedMaterials; Assert.That(materials.Length, Is.EqualTo(3));
            return AssetDatabase.GetAssetPath(materials[0]);
        }
        static T Field<T>(Object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Private); Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }
        static Renderer RequiredRenderer(Object actor, string name)
        {
            Renderer renderer = Field<Renderer>(actor, name); Assert.That(renderer, Is.Not.Null, name);
            Assert.That(renderer.transform.IsChildOf(((Component)actor).transform), Is.True, name);
            return renderer;
        }
        static RendererSample Sample(string field, Renderer renderer, Transform actor)
        {
            Material[] materials = renderer.sharedMaterials; Assert.That(materials, Is.Not.Empty, field);
            Assert.That(materials.All(material => material != null), Is.True, field);
            var slot = new MaterialPropertyBlock(); renderer.GetPropertyBlock(slot, 0);
            var wide = new MaterialPropertyBlock(); renderer.GetPropertyBlock(wide);
            return new RendererSample {
                field = field, renderer = Relative(renderer.transform, actor), rendererId = renderer.GetInstanceID(), frame = Time.frameCount, time = Time.time,
                enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy,
                runtimeFirstMaterialName = materials[0].name, runtimeFirstMaterialPath = AssetDatabase.GetAssetPath(materials[0]),
                materialNames = materials.Select(material => material.name).ToArray(), materialPaths = materials.Select(AssetDatabase.GetAssetPath).ToArray(),
                materialFirstColor = MainColor(materials[0]), slot0MpbEmpty = slot.isEmpty, rendererMpbEmpty = wide.isEmpty,
                slot0BaseColor = slot.GetColor("_BaseColor"), slot0LegacyColor = slot.GetColor("_Color"), slot0Emission = slot.GetColor("_EmissionColor"),
                rendererBaseColor = wide.GetColor("_BaseColor"), rendererEmission = wide.GetColor("_EmissionColor")
            };
        }
        static Color MainColor(Material material) => material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
            material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        static Transform Unique(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true).Where(transform => transform.name == name).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), name); return matches[0];
        }
        static void AssertSelectedHelmet(Transform root)
        {
            MeshFilter[] meshes = Unique(root, "Helmet_Closed").GetComponentsInChildren<MeshFilter>(true);
            Assert.That(meshes.Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(meshes[0].sharedMesh), Is.EqualTo(HelmetMeshPath));
        }
        static string Relative(Transform item, Transform root)
        {
            string path = item.name;
            while (item.parent != null && item != root) { item = item.parent; if (item != root) path = item.name + "/" + path; }
            return path;
        }
        static Pose CapturePose(Transform transform) => new Pose {
            position = transform.position, rotation = transform.rotation, localPosition = transform.localPosition,
            localRotation = transform.localRotation, localScale = transform.localScale };
        static bool SamePose(Pose left, Pose right) => left.position.Equals(right.position) && left.rotation.Equals(right.rotation) &&
            left.localPosition.Equals(right.localPosition) && left.localRotation.Equals(right.localRotation) && left.localScale.Equals(right.localScale);
        static void AssertColor(Color actual, Color expected, string reason)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-6f), reason);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-6f), reason);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-6f), reason);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-6f), reason);
        }
        static Dictionary<string, string> FreezeSources()
        {
            string[] targets = { CharacterPath, SwordPath, NetworkPath, BladePath, HelmetMeshPath, SwordMeshPath,
                "Assets/_Game/Scenes/01_MainMenu.unity", "Assets/_Game/Scenes/10_EmberValley.unity",
                "Assets/_Game/Scenes/20_Sanctum.unity", "Assets/_Game/Scenes/90_CombatGym.unity", "Assets/_Game/Scenes/91_NetworkGym.unity" };
            foreach (string target in targets)
            {
                if (!File.Exists(target) || !File.Exists(target + ".meta"))
                    throw new FileNotFoundException("Required frozen production source/meta is missing.", target);
            }
            string[] paths = targets.Concat(AssetDatabase.GetDependencies(targets, true)).Where(File.Exists)
                .SelectMany(path => File.Exists(path + ".meta") ? new[] { path, path + ".meta" } : new[] { path })
                .Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
            return paths.ToDictionary(path => path, Hash);
        }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
#endif
