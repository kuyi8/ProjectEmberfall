using System;
using System.Collections.Generic;

namespace Emberfall.AI.Domain
{
    /// <summary>
    /// Deterministic encounter-level melee quota. A future Server/session adapter supplies
    /// member observations; no Unity position, animation, or NavMesh fact enters this model.
    /// </summary>
    public sealed class CombatAttackQuotaModel
    {
        private readonly List<MemberState> _members = new List<MemberState>();
        private int _lastGrantedIndex = -1;

        public CombatAttackQuotaModel(int maximumConcurrentAttackers = 1)
        {
            if (maximumConcurrentAttackers <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentAttackers));
            }

            MaximumConcurrentAttackers = maximumConcurrentAttackers;
        }

        public int MaximumConcurrentAttackers { get; }
        public int MemberCount => _members.Count;
        public int GrantedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _members.Count; i++)
                {
                    if (_members[i].Granted) count++;
                }
                return count;
            }
        }

        public void Register(int combatantId)
        {
            if (combatantId == 0) throw new ArgumentOutOfRangeException(nameof(combatantId));
            if (FindIndex(combatantId) >= 0)
            {
                throw new ArgumentException("Combatant is already registered.", nameof(combatantId));
            }

            _members.Add(new MemberState(combatantId));
        }

        public void UpdateMember(int combatantId, bool eligible, bool committed)
        {
            int index = FindIndex(combatantId);
            if (index < 0) throw new KeyNotFoundException($"Unknown combatant: {combatantId}");
            MemberState member = _members[index];
            member.Eligible = eligible;
            member.Committed = committed;
            _members[index] = member;
        }

        public void Resolve()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                MemberState member = _members[i];
                if (!member.Granted) continue;
                if (member.Committed)
                {
                    member.HasCommitted = true;
                }
                else if (member.HasCommitted || !member.Eligible)
                {
                    member.Granted = false;
                    member.HasCommitted = false;
                }
                _members[i] = member;
            }

            int needed = MaximumConcurrentAttackers - GrantedCount;
            for (int granted = 0; granted < needed; granted++)
            {
                int next = FindNextEligibleIndex();
                if (next < 0) break;
                MemberState member = _members[next];
                member.Granted = true;
                member.HasCommitted = member.Committed;
                _members[next] = member;
                _lastGrantedIndex = next;
            }
        }

        public bool IsAttackAllowed(int combatantId)
        {
            int index = FindIndex(combatantId);
            return index >= 0 && _members[index].Granted;
        }

        public int GetSupportSide(int combatantId)
        {
            int index = FindIndex(combatantId);
            if (index < 0) throw new KeyNotFoundException($"Unknown combatant: {combatantId}");
            return (index & 1) == 0 ? -1 : 1;
        }

        public void Reset()
        {
            for (int i = 0; i < _members.Count; i++)
            {
                MemberState member = _members[i];
                member.Eligible = false;
                member.Committed = false;
                member.Granted = false;
                member.HasCommitted = false;
                _members[i] = member;
            }
            _lastGrantedIndex = -1;
        }

        private int FindNextEligibleIndex()
        {
            for (int offset = 1; offset <= _members.Count; offset++)
            {
                int index = (_lastGrantedIndex + offset) % _members.Count;
                MemberState member = _members[index];
                if (member.Eligible && !member.Granted) return index;
            }
            return -1;
        }

        private int FindIndex(int combatantId)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].CombatantId == combatantId) return i;
            }
            return -1;
        }

        private struct MemberState
        {
            public MemberState(int combatantId)
            {
                CombatantId = combatantId;
                Eligible = false;
                Committed = false;
                Granted = false;
                HasCommitted = false;
            }

            public int CombatantId;
            public bool Eligible;
            public bool Committed;
            public bool Granted;
            public bool HasCommitted;
        }
    }
}
