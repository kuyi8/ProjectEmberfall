using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.AI.Data
{
    public static class SummonerEnemyDefinitionJsonLoader
    {
        public static ContentRegistry<SummonerEnemyDefinition> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Summoner JSON is empty.", nameof(json));
            Document root;
            try { root = JsonUtility.FromJson<Document>(json); }
            catch (ArgumentException e) { throw new FormatException("Summoner JSON is malformed.", e); }
            if (root == null || root.schemaVersion != 1 || root.summoners == null || root.summoners.Length == 0)
                throw new NotSupportedException("Summoner schema 1 and non-empty summoners are required.");
            var items = new List<SummonerEnemyDefinition>(root.summoners.Length);
            foreach (Record r in root.summoners)
            {
                if (r == null || !ContentId.TryCreate(r.id, out ContentId id) ||
                    !ContentId.TryCreate(r.displayNameTextId, out ContentId text) ||
                    !ContentId.TryCreate(r.minionId, out ContentId minion)) throw new FormatException("Invalid summoner stable IDs.");
                var combat = new RangedEnemyDefinition(id, text, r.maximumHealth, r.armor,
                    r.detectionRange, r.loseTargetRange, r.fieldOfView, r.leashRange,
                    r.preferredMinimumRange, r.preferredMaximumRange, r.moveSpeed, r.rotationSpeed,
                    r.windupDuration, r.releaseDuration, r.recoveryDuration, r.projectileSpeed,
                    r.projectileLifetime, r.projectileRadius, r.attackDamage, r.postureDamage,
                    r.hitReactDuration, r.respawnDelay, r.maximumPosture, r.postureRegenPerSecond, r.postureRegenDelay);
                items.Add(new SummonerEnemyDefinition(combat, minion, r.summonWindup, r.summonRecovery,
                    r.summonCooldown, r.interruptCooldown, r.initialSummonDelay, r.minionHealth));
            }
            return new ContentRegistry<SummonerEnemyDefinition>(items);
        }

        [Serializable] private sealed class Document { public int schemaVersion; public Record[] summoners; }
        [Serializable] private sealed class Record
        {
            public string id, displayNameTextId, minionId;
            public float maximumHealth, armor, detectionRange, loseTargetRange, fieldOfView, leashRange;
            public float preferredMinimumRange, preferredMaximumRange, moveSpeed, rotationSpeed;
            public float windupDuration, releaseDuration, recoveryDuration, projectileSpeed, projectileLifetime, projectileRadius;
            public float attackDamage, postureDamage, hitReactDuration, respawnDelay;
            public float maximumPosture, postureRegenPerSecond, postureRegenDelay;
            public float summonWindup, summonRecovery, summonCooldown, interruptCooldown, initialSummonDelay, minionHealth;
        }
    }
}
