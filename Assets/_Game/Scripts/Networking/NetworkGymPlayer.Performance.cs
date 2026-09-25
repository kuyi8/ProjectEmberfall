#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Networking
{
    // Explicit diagnostic input only. No alternate RPC, health refill, teleport or domain tick.
    public sealed partial class NetworkGymPlayer
    {
        private readonly bool _performanceCombat = Array.Exists(Environment.GetCommandLineArgs(),
            x => x == "-emberfall-performance-combat");
        private NetworkWarden _performanceWarden;
        private float _performanceFindAt, _performanceInputAt, _performanceAttackAt, _performanceTraceAt;
        private int _performancePlayerSequence, _performanceBossSequence;
        private int _performanceMoveLeg;
        private float _performanceMoveTime;
        private Vector3 _performanceMoveStart;

        private bool PerformanceCombatActive => _performanceCombat &&
            _worldObjective != null && _worldObjective.ReplicatedStage == MainQuestStage.DefeatWarden;

        private NetworkWarden PerformanceWarden()
        {
            if (_performanceWarden == null && Time.unscaledTime >= _performanceFindAt)
            {
                _performanceFindAt = Time.unscaledTime + .25f;
                _performanceWarden = FindObjectOfType<NetworkWarden>();
            }
            return _performanceWarden;
        }

        private bool TryPerformanceCombatMovement(out Vector2 move)
        {
            move = Vector2.zero;
            if (!PerformanceCombatActive) return false;
            var boss = PerformanceWarden();
            if (boss == null || !boss.IsAlive) return true;
            if (_performanceMoveLeg < 4)
            {
                if (_performanceMoveTime == 0) _performanceMoveStart = transform.position;
                _performanceMoveTime += Time.deltaTime;
                if (_performanceMoveTime >= .3f)
                {
                    Debug.Log($"[PERF_MOVE_LEG] owner={OwnerClientId} leg={_performanceMoveLeg} start={_performanceMoveStart} end={transform.position}");
                    _performanceMoveLeg++;
                    _performanceMoveTime = 0;
                }
                if (_performanceMoveLeg < 4)
                {
                    move = _performanceMoveLeg == 0 ? Vector2.right : _performanceMoveLeg == 1 ? Vector2.left :
                        _performanceMoveLeg == 2 ? Vector2.up : Vector2.down;
                    return true;
                }
            }
            Vector3 delta = boss.transform.position - transform.position;
            delta.y = 0;
            if (delta.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(delta), TurnSpeed * Time.deltaTime);
            if (delta.sqrMagnitude > 2.0f * 2.0f)
                move = new Vector2(delta.normalized.x, delta.normalized.z);
            return true;
        }

        private bool TryPerformanceCombatInput()
        {
            if (!PerformanceCombatActive) return false;
            var boss = PerformanceWarden();
            if (boss == null || !boss.IsAlive || Time.unscaledTime < _performanceInputAt) return true;
            _performanceInputAt = Time.unscaledTime + .12f;
            Vector3 delta = boss.transform.position - transform.position;
            delta.y = 0;
            Vector2 aim = delta.sqrMagnitude > .001f ? new Vector2(delta.normalized.x, delta.normalized.z) : ForwardAimDirection();
            bool danger = boss.ReplicatedState == WardenState.Windup || boss.ReplicatedState == WardenState.Attack;
            if (danger)
            {
                if (boss.ReplicatedAttack == WardenAttackKind.DelayedBlast && ReplicatedCombatState == CombatState.Locomotion)
                    SubmitCombatIntent(CombatCommand.Dodge, -aim);
                else if (ReplicatedCombatState == CombatState.Locomotion)
                    SubmitCombatIntent(CombatCommand.GuardPressed, aim);
            }
            else if (ReplicatedCombatState == CombatState.Guard)
                SubmitCombatIntent(CombatCommand.GuardReleased, aim);
            else if (ReplicatedCombatState == CombatState.Locomotion)
            {
                if (Health < 75f && HealingFlaskCharges > 0)
                    SubmitCombatIntent(CombatCommand.Heal, aim);
                else if (delta.sqrMagnitude < 3f * 3f && Time.unscaledTime >= _performanceAttackAt)
                {
                    _performanceAttackAt = Time.unscaledTime + 5f;
                    SubmitCombatIntent(CombatCommand.LightAttack, aim);
                }
            }
            return true;
        }

        private void TickPerformanceCombatTelemetry()
        {
            if (!PerformanceCombatActive || !IsServer) return;
            if (_attackSequence.Value != _performancePlayerSequence)
            {
                _performancePlayerSequence = _attackSequence.Value;
                if (_performancePlayerSequence > 0)
                    Debug.Log($"[PERF_PLAYER_ATTACK] owner={OwnerClientId} sequence={_performancePlayerSequence} state={ReplicatedCombatState}");
            }
            if (Time.unscaledTime >= _performanceTraceAt)
            {
                _performanceTraceAt = Time.unscaledTime + 1f;
                Debug.Log($"[PERF_COMBAT_TRACE] owner={OwnerClientId} health={Health:F1} downed={IsDowned} defeated={PartyDefeated} position={transform.position} time={Time.realtimeSinceStartupAsDouble.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            }
            if (!IsOwner) return;
            var boss = PerformanceWarden();
            if (boss != null && boss.AttackSequence != _performanceBossSequence)
            {
                _performanceBossSequence = boss.AttackSequence;
                if (boss.AttackSequence > 0)
                    Debug.Log($"[PERF_WARDEN_ATTACK] sequence={boss.AttackSequence} kind={boss.ReplicatedAttack} health={boss.Health:F1}");
            }
        }
    }
}
#endif
