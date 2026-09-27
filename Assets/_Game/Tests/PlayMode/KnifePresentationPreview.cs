using System;
using System.Linq;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    // Preview gate only: lives in TestAssemblies, never serialized into production assets.
    [DefaultExecutionOrder(20000)]
    public sealed class KnifePresentationPreview : MonoBehaviour
    {
        public const float BridgeDuration = .06f;
        public const float GripToCenter = .13f;
        public bool HeldVisible => _held != null && _held.gameObject.activeSelf;
        public bool FlightVisible => _flight != null && _flight.gameObject.activeSelf;
        public bool SwordVisible => _swords.Any(r => r != null && r.enabled && !r.forceRenderingOff);
        public bool SwordTrailVisible => _swordTrail != null && _swordTrail.enabled && !_swordTrail.forceRenderingOff;
        public float BridgeAge { get; private set; } = -1;
        public float AuthorityMutation { get; private set; }
        public Vector3 VisualPosition => FlightVisible ? _flight.position : HeldVisible ? _held.position : Vector3.zero;
        public Vector3 GripPosition => _sword.position;

        private PlayerCombatActor _actor;
        private Transform _sword, _held, _flight;
        private Renderer[] _swords = Array.Empty<Renderer>();
        private bool[] _swordHidden;
        private Renderer _swordTrail;
        private bool _trailHidden;
        private PlayerThrowingKnifeProjectile[] _pool;
        private PlayerThrowingKnifeProjectile _projectile;
        private Renderer[] _projectileRenderers;
        private bool[] _projectileHidden;
        private Vector3 _offset;
        private Quaternion _releaseRotation;
        private int _releaseSequence;
        private double _birth;

        public void Initialize(PlayerCombatActor actor)
        {
            _actor = actor;
            _sword = actor.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Sword_M6_Player_Equipped");
            _swords = _sword.GetComponentsInChildren<Renderer>(true);
            _swordHidden = _swords.Select(r => r.forceRenderingOff).ToArray();
            _swordTrail = actor.transform.Find("SwordTrail_Runtime").GetComponent<Renderer>();
            _trailHidden = _swordTrail.forceRenderingOff;
            _pool = actor.GetComponentsInChildren<PlayerThrowingKnifeProjectile>(true);
            if (_pool.Length == 0) throw new InvalidOperationException("Expected prewarmed production knife pool.");
            var sourceModel = _pool[0].transform.Find("Model");
            Transform CopyModel(string name)
            {
                var root = new GameObject(name).transform;
                // Unit world scale; imported hand-bone scale must never resize metre-authored gear.
                var copy = Instantiate(sourceModel.gameObject, root).transform;
                copy.localPosition = sourceModel.localPosition;
                copy.localRotation = sourceModel.localRotation;
                copy.localScale = sourceModel.localScale;
                if (root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                    throw new InvalidOperationException("Preview Model must contain render components only.");
                root.gameObject.SetActive(false);
                return root;
            }
            _held = CopyModel("KnifePreview_Held");
            _flight = CopyModel("KnifePreview_FlightVisual");
            _releaseSequence = actor.Model.RangedReleaseSequence;
        }

        private void LateUpdate()
        {
            if (_actor == null || _held == null) return;
            bool throwing = _actor.enabled && _actor.Model.State == CombatState.RangedAttack;
            for (int i = 0; i < _swords.Length; i++) _swords[i].forceRenderingOff = throwing || _swordHidden[i];
            _swordTrail.forceRenderingOff = throwing || _trailHidden;
            // Sword authoring points +Y along blade; knife model points +Z. Grip is .13m behind its center.
            Vector3 heldPosition = _sword.position + _sword.up * GripToCenter;
            Quaternion heldRotation = Quaternion.LookRotation(_sword.up, _sword.forward);
            _held.SetPositionAndRotation(heldPosition, heldRotation);
            if (_actor.Model.RangedReleaseSequence != _releaseSequence)
            {
                _releaseSequence = _actor.Model.RangedReleaseSequence;
                RestoreProjectile();
                _projectile = _pool.FirstOrDefault(p => p.IsInFlight);
                if (_projectile != null)
                {
                    _birth = Time.timeAsDouble;
                    _offset = heldPosition - _projectile.transform.position;
                    _releaseRotation = heldRotation;
                    _projectileRenderers = _projectile.GetComponentsInChildren<Renderer>(true);
                    _projectileHidden = _projectileRenderers.Select(r => r.forceRenderingOff).ToArray();
                    foreach (var renderer in _projectileRenderers) renderer.forceRenderingOff = true;
                }
            }
            _held.gameObject.SetActive(throwing && _actor.Model.StateElapsed < .22f);
            if (_projectile == null || !_projectile.IsInFlight || !_projectile.gameObject.activeInHierarchy)
            {
                _flight.gameObject.SetActive(false);
                RestoreProjectile();
                BridgeAge = -1;
                return;
            }
            Vector3 authorityBefore = _projectile.transform.position;
            Quaternion rotationBefore = _projectile.transform.rotation;
            BridgeAge = (float)(Time.timeAsDouble - _birth);
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(BridgeAge / BridgeDuration));
            _flight.SetPositionAndRotation(authorityBefore + _offset * (1 - t),
                Quaternion.Slerp(_releaseRotation, rotationBefore, t));
            _flight.gameObject.SetActive(true);
            AuthorityMutation = Mathf.Max(AuthorityMutation, Vector3.Distance(authorityBefore, _projectile.transform.position),
                Quaternion.Angle(rotationBefore, _projectile.transform.rotation));
        }

        private void RestoreProjectile()
        {
            if (_projectileRenderers != null)
                for (int i = 0; i < _projectileRenderers.Length; i++)
                    if (_projectileRenderers[i] != null) _projectileRenderers[i].forceRenderingOff = _projectileHidden[i];
            _projectileRenderers = null;
            _projectile = null;
        }

        private void OnDestroy()
        {
            RestoreProjectile();
            for (int i = 0; i < _swords.Length; i++) if (_swords[i] != null) _swords[i].forceRenderingOff = _swordHidden[i];
            if (_swordTrail != null) _swordTrail.forceRenderingOff = _trailHidden;
            if (_held != null) Destroy(_held.gameObject);
            if (_flight != null) Destroy(_flight.gameObject);
        }
    }
}
