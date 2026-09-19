using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>
    /// Read-only bridge from Server-replicated network combatants to the existing
    /// local lock-on/camera presentation. Damage authority never crosses this adapter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkCombatTargetProxy : CombatTarget
    {
        [SerializeField] private NetworkGymEnemy _enemy;
        [SerializeField] private NetworkWarden _warden;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private string _displayName = "敌人";

        public string DisplayName => _displayName;
        public bool IsBoss => _warden != null;
        public string StateLabel => _warden != null
            ? ResolveWardenState(_warden.ReplicatedState)
            : _enemy != null ? _enemy.ReplicatedStateLabel : string.Empty;

        public override int CombatantId => _warden != null
            ? _warden.ServerCombatantId
            : _enemy != null ? _enemy.ServerCombatantId : 0;
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _warden != null ? _warden.IsAlive : _enemy != null && _enemy.IsAlive;
        public override float HealthNormalized => _warden != null
            ? _warden.HealthNormalized
            : _enemy != null && _enemy.MaximumHealth > 0f
                ? Mathf.Clamp01(_enemy.Health / _enemy.MaximumHealth)
                : 0f;
        public override bool HasSecondaryResource => _warden != null ||
            (_enemy != null && _enemy.Archetype == NetworkEnemyArchetype.RuinGuard);
        public override float SecondaryResourceNormalized => _warden != null
            ? _warden.PostureNormalized
            : _enemy != null ? _enemy.SecondaryResourceNormalized : 0f;
        public override bool IsThreatening => _warden != null
            ? _warden.ReplicatedState == WardenState.Windup || _warden.ReplicatedState == WardenState.Attack
            : _enemy != null && (_enemy.ReplicatedState == MeleeEnemyState.Windup ||
                                 _enemy.ReplicatedState == MeleeEnemyState.Attack);

        public void Configure(NetworkGymEnemy enemy, Transform aimPoint, string displayName)
        {
            _enemy = enemy;
            _warden = null;
            _aimPoint = aimPoint;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? "敌人" : displayName;
        }

        public void Configure(NetworkWarden warden, Transform aimPoint, string displayName)
        {
            _enemy = null;
            _warden = warden;
            _aimPoint = aimPoint;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? "余烬守望者" : displayName;
        }

        public override DamageResult ReceiveDamage(DamageRequest request) => DamageResult.Ignored;

        private static string ResolveWardenState(WardenState state) => state switch
        {
            WardenState.Windup => "招式前摇",
            WardenState.Attack => "攻击中",
            WardenState.Recovery => "收招破绽",
            WardenState.GuardBreak => "架势崩解",
            WardenState.PhaseTransition => "阶段转换",
            WardenState.Dead => "已击败",
            _ => "警戒"
        };
    }
}
