using UnityEngine;

namespace Emberfall.Gameplay.Input
{
    public readonly struct PlayerInputSnapshot
    {
        public PlayerInputSnapshot(
            Vector2 move,
            Vector2 look,
            bool lookUsesPointer,
            bool sprint,
            bool lightAttack,
            bool heavyAttack,
            bool rangedAttack,
            bool guard,
            bool dodge,
            bool heal,
            bool lockOn,
            bool interact,
            bool guide,
            bool pause)
        {
            Move = move;
            Look = look;
            LookUsesPointer = lookUsesPointer;
            Sprint = sprint;
            LightAttack = lightAttack;
            HeavyAttack = heavyAttack;
            RangedAttack = rangedAttack;
            Guard = guard;
            Dodge = dodge;
            Heal = heal;
            LockOn = lockOn;
            Interact = interact;
            Guide = guide;
            Pause = pause;
        }

        public Vector2 Move { get; }
        public Vector2 Look { get; }
        public bool LookUsesPointer { get; }
        public bool Sprint { get; }
        public bool LightAttack { get; }
        public bool HeavyAttack { get; }
        public bool RangedAttack { get; }
        public bool Guard { get; }
        public bool Dodge { get; }
        public bool Heal { get; }
        public bool LockOn { get; }
        public bool Interact { get; }
        public bool Guide { get; }
        public bool Pause { get; }
    }
}
