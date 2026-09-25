using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Gameplay.Combat.Unity
{
    public enum CombatBurstKind { Steel, Guard, Ember, GroundBlast, GuardBreak, Execution, Sweep }

    /// <summary>
    /// Scene-owned caches, process-local shared budget. Decorative bursts only: never pool/drop warnings or damage.
    /// A category, not a prefab or attacker, owns the limit across additive scenes and all local cameras.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatBurstVfxPool : MonoBehaviour
    {
        public const int PerKindLimit = 3;
        private const int KindCount = 7;
        private static readonly Dictionary<int, CombatBurstVfxPool> Pools = new Dictionary<int, CombatBurstVfxPool>();
        private static readonly int[] Active = new int[KindCount];
        private readonly Slot[] _slots = new Slot[KindCount * PerKindLimit];
        private Transform _inactiveRoot;
        private int _sceneHandle;
        public int CreatedCount { get; private set; }
        public int DroppedCount { get; private set; }

        private sealed class Slot
        {
            public GameObject Prefab, Instance;
            public ParticleSystem[] Particles;
            public bool Leased;
            public double ExpiresAt;
            public MeleeImpactArcMesh Arc;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Pools.Clear(); Array.Clear(Active, 0, Active.Length); }

        public static int ActiveCount(CombatBurstKind kind) => Valid(kind) ? Active[(int)kind] : 0;
        private static bool Valid(CombatBurstKind kind) => (int)kind >= 0 && (int)kind < KindCount;

        public static CombatBurstVfxPool ForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded owner scene is required.");
            if (Pools.TryGetValue(scene.handle, out var cached) && cached != null) return cached;
            var root = new GameObject("[Presentation] Combat Burst Pool");
            SceneManager.MoveGameObjectToScene(root, scene);
            var pool = root.AddComponent<CombatBurstVfxPool>();
            pool._sceneHandle = scene.handle;
            Pools[scene.handle] = pool;
            return pool;
        }

        private void Awake()
        {
            var storage = new GameObject("Inactive Cache");
            storage.transform.SetParent(transform, false);
            storage.SetActive(false);
            _inactiveRoot = storage.transform;
        }

        public void Prewarm(CombatBurstKind kind, GameObject prefab)
        {
            if (!Valid(kind) || prefab == null || !isActiveAndEnabled) return;
            int start = (int)kind * PerKindLimit;
            for (int i = start; i < start + PerKindLimit; i++)
            {
                if (_slots[i]?.Leased == true) continue;
                EnsureSlot(i, prefab);
            }
        }

        public bool TrySpawn(CombatBurstKind kind, GameObject prefab, Vector3 position, Quaternion rotation, float lifetime,
            MeleeImpactSector sector = default)
        {
            if (!isActiveAndEnabled || !Valid(kind) || prefab == null || lifetime <= 0 ||
                float.IsNaN(lifetime) || float.IsInfinity(lifetime)) return false;
            if (ActiveCount(kind) >= PerKindLimit) { DroppedCount++; return false; }
            int start = (int)kind * PerKindLimit;
            for (int i = start; i < start + PerKindLimit; i++)
            {
                if (_slots[i]?.Leased == true) continue;
                Slot slot = EnsureSlot(i, prefab);
                if (slot.Arc != null)
                {
                    // Missing geometry may not masquerade as a correctly sized range arc.
                    if (!sector.IsValid) { DroppedCount++; return false; }
                    slot.Arc.SetSector(sector.Radius, sector.FullAngle);
                }
                slot.Leased = true;
                slot.ExpiresAt = Time.timeAsDouble + lifetime;
                Active[(int)kind]++;
                slot.Instance.transform.SetPositionAndRotation(position, rotation);
                slot.Instance.SetActive(true);
                foreach (var particle in slot.Particles) particle.Play(false);
                return true;
            }
            DroppedCount++;
            return false;
        }

        private Slot EnsureSlot(int index, GameObject prefab)
        {
            var slot = _slots[index];
            if (slot != null && slot.Prefab == prefab && slot.Instance != null) return slot;
            if (slot?.Instance != null)
            {
                slot.Instance.SetActive(false);
                Destroy(slot.Instance); // Only an authored prefab change replaces a cached instance.
            }
            if (slot?.Arc != null) Destroy(slot.Arc.Mesh);
            var instance = Instantiate(prefab, _inactiveRoot); // Inactive parent prevents play-on-awake.
            instance.name = "Pooled_" + prefab.name;
            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
            var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in particles)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None; // Pool alone owns lifetime.
            }
            slot = new Slot { Prefab = prefab, Instance = instance, Particles = particles };
            if (index / PerKindLimit == (int)CombatBurstKind.Sweep &&
                instance.transform.Find("ConfirmedRangeArc") != null)
            {
                slot.Arc = new MeleeImpactArcMesh();
                instance.transform.Find("ConfirmedRangeArc").GetComponent<ParticleSystemRenderer>().mesh = slot.Arc.Mesh;
            }
            _slots[index] = slot;
            CreatedCount++;
            return slot;
        }

        private void Update()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i];
                if (slot?.Leased == true && (slot.Instance == null || !slot.Instance.activeSelf || Time.timeAsDouble >= slot.ExpiresAt))
                    Release(i);
            }
        }

        private void Release(int index)
        {
            Slot slot = _slots[index];
            if (slot?.Leased != true) return;
            slot.Leased = false;
            Active[index / PerKindLimit] = Math.Max(0, Active[index / PerKindLimit] - 1);
            if (slot.Instance == null) return;
            foreach (var particle in slot.Particles)
                if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.Instance.SetActive(false);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _slots.Length; i++) Release(i);
        }

        private void OnDestroy()
        {
            OnDisable();
            foreach (var slot in _slots)
                if (slot?.Arc != null) Destroy(slot.Arc.Mesh);
            if (Pools.TryGetValue(_sceneHandle, out var pool) && pool == this) Pools.Remove(_sceneHandle);
        }
    }
}
