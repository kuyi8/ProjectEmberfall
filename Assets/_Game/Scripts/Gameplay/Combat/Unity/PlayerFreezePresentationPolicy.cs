using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Animation;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Offline player presentation only. No movement, damage or state-clock authority.</summary>
    public static class PlayerFreezePresentationPolicy
    {
#if UNITY_EDITOR
        // Developer A/B comparison only; never offers a Motor-pause alternative.
        public static bool KeepDodgeAnimationMoving { get; set; } = true;
#endif
        public static bool AllowOwnerFreeze(CombatState? state)
        {
#if UNITY_EDITOR
            if (!KeepDodgeAnimationMoving) return true;
#endif
            return state != CombatState.Dodge;
        }

        public static bool AllowPerfectDefenseFreeze(PerfectDefenseKind kind, CombatState? state)
        {
#if UNITY_EDITOR
            if (!KeepDodgeAnimationMoving) return true;
#endif
            return kind != PerfectDefenseKind.Dodge && AllowOwnerFreeze(state);
        }

        public static bool AllowOwnerFreeze(PlayerCombatActor actor, Animator animator)
        {
            if (!AllowOwnerFreeze(actor?.Model?.State)) return false;
            if (actor == null || animator == null) return true;
            // Queries happen only on presentation dispatch or the already-bound player Update.
            // No static owner identity, attack-sequence comparison or Coordinator-wide filtering.
            var motion = actor.GetComponent<PlayerAnimationPresenter>();
            return motion == null || !motion.KeepsOfflineRangedAnimationMoving(actor, animator);
        }

        public static bool AllowPerfectDefenseFreeze(PerfectDefenseKind kind, PlayerCombatActor actor, Animator animator) =>
            AllowPerfectDefenseFreeze(kind, actor?.Model?.State) && AllowOwnerFreeze(actor, animator);
    }
}
