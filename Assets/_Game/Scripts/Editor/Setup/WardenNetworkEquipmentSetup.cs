using System;
using System.IO;
using System.Linq;
using Emberfall.Networking;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Explicit Editor-only equipment authoring. Mutates only the supplied temporary or
    /// loaded project-owned NetworkWarden hierarchy. Caller owns saving and approval.
    /// World-scale sockets preserve metre-based equipment, not equal Actor-to-tip distances.
    /// </summary>
    public static class WardenNetworkEquipmentSetup
    {
        public const string NetworkPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab";
        public const string SwordPrefabPath = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RuneSword.prefab";
        public const string ShieldPrefabPath = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RoundShield.prefab";
        public const string SwordSocketName = "WeaponSocket_RightHand";
        public const string ShieldSocketName = "ShieldSocket_LeftHand";
        public const string SwordName = "Sword_M6_Warden_Equipped";
        public const string ShieldName = "Shield_Warden_Equipped";
        static readonly Vector3 SwordOffset = new Vector3(0f, .015f, 0f);
        static readonly Vector3 ShieldOffset = new Vector3(.02f, .05f, .03f);
        static readonly Quaternion ShieldRotation = Quaternion.Euler(5f, 92f, 88f);

        public sealed class EquipmentResult
        {
            public bool created;
            public string rightHandPath, leftHandPath;
            public Vector3 rightSocketWorldScale, leftSocketWorldScale;
            public GameObject sword, shield;
            public Renderer weaponRenderer, shieldRenderer;
        }

        public static EquipmentResult Apply(GameObject root)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Network Warden equipment authoring requires Edit Mode.");
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (EditorUtility.IsPersistent(root))
                throw new InvalidOperationException("Load or instantiate the project-owned prefab before authoring equipment.");
            NetworkWarden warden = root.GetComponent<NetworkWarden>();
            if (warden == null) throw new InvalidDataException("The supplied root has no NetworkWarden.");
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || !animators[0].isHuman || animators[0].avatar == null || !animators[0].avatar.isValid)
                throw new InvalidDataException("Network Warden requires exactly one valid Humanoid Animator.");
            Animator animator = animators[0];
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (rightHand == null || leftHand == null)
                throw new InvalidDataException("Network Warden requires explicit non-null RightHand and LeftHand bones.");
            GameObject swordPrefab = RequiredGear(SwordPrefabPath);
            GameObject shieldPrefab = RequiredGear(ShieldPrefabPath);

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            Transform[] swordSockets = Named(all, SwordSocketName), shieldSockets = Named(all, ShieldSocketName);
            Transform[] swords = Named(all, SwordName), shields = Named(all, ShieldName);
            bool missing = swordSockets.Length == 0 && shieldSockets.Length == 0 && swords.Length == 0 && shields.Length == 0;
            if (!missing && (swordSockets.Length != 1 || shieldSockets.Length != 1 || swords.Length != 1 || shields.Length != 1))
                throw new InvalidDataException("Unknown, duplicate or partial Warden equipment; no existing objects were removed.");
            ValidateReferencesBeforeAuthoring(warden, missing ? null : swords[0], missing ? null : shields[0]);
            if (missing)
            {
                Transform rightSocket = null, leftSocket = null;
                try
                {
                    rightSocket = M1ProjectSetup.CreateWorldScaleSocket(rightHand, SwordSocketName, SwordOffset, Quaternion.identity);
                    leftSocket = M1ProjectSetup.CreateWorldScaleSocket(leftHand, ShieldSocketName, ShieldOffset, ShieldRotation);
                    InstantiateGear(swordPrefab, rightSocket, SwordName, root.layer);
                    InstantiateGear(shieldPrefab, leftSocket, ShieldName, root.layer);
                    swordSockets = new[] { rightSocket }; shieldSockets = new[] { leftSocket };
                    swords = Named(root.GetComponentsInChildren<Transform>(true), SwordName);
                    shields = Named(root.GetComponentsInChildren<Transform>(true), ShieldName);
                }
                catch
                {
                    // Recover only the two sockets created by this call. Existing/unknown
                    // authoring is never deleted or silently repaired.
                    if (rightSocket != null) Object.DestroyImmediate(rightSocket.gameObject);
                    if (leftSocket != null) Object.DestroyImmediate(leftSocket.gameObject);
                    throw;
                }
            }
            ValidateSocket(swordSockets[0], rightHand, SwordOffset, Quaternion.identity);
            ValidateSocket(shieldSockets[0], leftHand, ShieldOffset, ShieldRotation);
            ValidateEquippedGear(swords[0], swordSockets[0], swordPrefab, SwordPrefabPath);
            ValidateEquippedGear(shields[0], shieldSockets[0], shieldPrefab, ShieldPrefabPath);
            Renderer weaponRenderer = swords[0].GetComponentsInChildren<MeshRenderer>(true).Single();
            Renderer shieldRenderer = shields[0].GetComponentsInChildren<MeshRenderer>(true).Single();
            warden.ConfigureEquipment(weaponRenderer, shieldRenderer, shields[0].gameObject);
            if (PrefabUtility.IsPartOfPrefabInstance(warden))
                PrefabUtility.RecordPrefabInstancePropertyModifications(warden);
            return new EquipmentResult
            {
                created = missing,
                rightHandPath = RelativePath(rightHand, root.transform),
                leftHandPath = RelativePath(leftHand, root.transform),
                rightSocketWorldScale = swordSockets[0].lossyScale,
                leftSocketWorldScale = shieldSockets[0].lossyScale,
                sword = swords[0].gameObject,
                shield = shields[0].gameObject,
                weaponRenderer = weaponRenderer,
                shieldRenderer = shieldRenderer
            };
        }

        static Transform[] Named(Transform[] transforms, string name) => transforms.Where(t => t.name == name).ToArray();

        static GameObject RequiredGear(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("Project-owned Warden equipment is missing.", path);
            ValidatePureGear(prefab);
            if (prefab.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                prefab.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                throw new InvalidDataException("Expected exactly one authored mesh/renderer in Warden equipment: " + path);
            return prefab;
        }

        static void ValidatePureGear(GameObject gear)
        {
            foreach (Transform item in gear.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(item.gameObject) != 0)
                    throw new InvalidDataException("Warden equipment cannot have Static flags: " + item.name);
                foreach (Component component in item.GetComponents<Component>())
                    if (component == null || (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer)))
                        throw new InvalidDataException("Warden equipment cannot add behaviours, animation or physics: " + item.name);
            }
            foreach (MeshFilter filter in gear.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null) throw new InvalidDataException("Warden equipment has no mesh: " + filter.name);
            foreach (MeshRenderer renderer in gear.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null))
                    throw new InvalidDataException("Warden equipment has missing material slots: " + renderer.name);
                if (renderer.sharedMaterials.Any(m => !m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)))
                    throw new InvalidDataException("Warden equipment requires existing URP materials: " + renderer.name);
            }
        }

        static void InstantiateGear(GameObject prefab, Transform socket, string name, int layer)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, socket) as GameObject;
            if (instance == null) throw new InvalidDataException("Failed to instantiate Warden equipment: " + prefab.name);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            M1ProjectSetup.SetLayerRecursively(instance, layer);
        }

        static void ValidateSocket(Transform socket, Transform bone, Vector3 offset, Quaternion rotation)
        {
            if (socket.parent != bone || socket.GetComponents<Component>().Length != 1 || socket.childCount != 1 ||
                GameObjectUtility.GetStaticEditorFlags(socket.gameObject) != 0)
                throw new InvalidDataException("Unexpected Warden socket hierarchy/components: " + socket.name);
            if (Vector3.Distance(socket.position, bone.position + bone.rotation * offset) > .0001f ||
                Quaternion.Angle(socket.localRotation, rotation) > .01f ||
                Vector3.Distance(Absolute(socket.lossyScale), Vector3.one) > .002f)
                throw new InvalidDataException("Existing Warden socket does not match protected world-scale authoring: " + socket.name);
        }

        static void ValidateEquippedGear(Transform gear, Transform socket, GameObject prefab, string path)
        {
            if (gear.parent != socket || gear.localPosition != Vector3.zero ||
                gear.localRotation != Quaternion.identity || gear.localScale != Vector3.one)
                throw new InvalidDataException("Unexpected Warden equipment root transform: " + gear.name);
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(gear.gameObject);
            if (source != prefab || AssetDatabase.GetAssetPath(source) != path)
                throw new InvalidDataException("Unknown Warden equipment source: " + gear.name);
            ValidatePureGear(gear.gameObject);
            Transform[] expected = prefab.GetComponentsInChildren<Transform>(true);
            Transform[] actual = gear.GetComponentsInChildren<Transform>(true);
            if (actual.Length != expected.Length)
                throw new InvalidDataException("Unexpected Warden equipment descendants: " + gear.name);
            foreach (Transform reference in expected)
            {
                string relative = RelativePath(reference, prefab.transform);
                Transform target = relative.Length == 0 ? gear : gear.Find(relative);
                if (target == null || target.gameObject.activeSelf != reference.gameObject.activeSelf)
                    throw new InvalidDataException("Unexpected Warden equipment descendant: " + relative);
                if (relative.Length != 0 && (target.localPosition != reference.localPosition ||
                    target.localRotation != reference.localRotation || target.localScale != reference.localScale))
                    throw new InvalidDataException("Unexpected Warden equipment descendant transform: " + relative);
                MeshFilter expectedFilter = reference.GetComponent<MeshFilter>(), actualFilter = target.GetComponent<MeshFilter>();
                if ((expectedFilter == null) != (actualFilter == null) ||
                    (expectedFilter != null && expectedFilter.sharedMesh != actualFilter.sharedMesh))
                    throw new InvalidDataException("Unexpected Warden equipment mesh: " + relative);
                MeshRenderer expectedRenderer = reference.GetComponent<MeshRenderer>(), actualRenderer = target.GetComponent<MeshRenderer>();
                if ((expectedRenderer == null) != (actualRenderer == null) || (expectedRenderer != null &&
                    (expectedRenderer.enabled != actualRenderer.enabled || !expectedRenderer.sharedMaterials.SequenceEqual(actualRenderer.sharedMaterials))))
                    throw new InvalidDataException("Unexpected Warden equipment renderer/materials: " + relative);
            }
        }

        static void ValidateReferencesBeforeAuthoring(NetworkWarden warden, Transform sword, Transform shield)
        {
            var serialized = new SerializedObject(warden);
            Object weaponReference = serialized.FindProperty("_weaponRenderer").objectReferenceValue;
            Object shieldReference = serialized.FindProperty("_shieldRenderer").objectReferenceValue;
            Object rootReference = serialized.FindProperty("_shieldVisualRoot").objectReferenceValue;
            if (weaponReference == null && shieldReference == null && rootReference == null) return;
            if (sword == null || shield == null || weaponReference != sword.GetComponentInChildren<MeshRenderer>(true) ||
                shieldReference != shield.GetComponentInChildren<MeshRenderer>(true) || rootReference != shield.gameObject)
                throw new InvalidDataException("Unknown or partial Warden equipment bindings; no existing references were replaced.");
        }

        static Vector3 Absolute(Vector3 scale) => new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        static string RelativePath(Transform item, Transform root)
        {
            if (item == root) return string.Empty;
            string path = item.name;
            for (Transform parent = item.parent; parent != root; parent = parent.parent)
            {
                if (parent == null) throw new InvalidDataException("Equipment path is outside supplied root.");
                path = parent.name + "/" + path;
            }
            return path;
        }
    }
}
