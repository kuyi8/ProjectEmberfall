using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;
using Emberfall.Quests.Domain;

namespace Emberfall.Infrastructure.Saves
{
    [Serializable]
    public sealed class SaveGameV1
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string checkpointId;
        public SaveVector3 checkpointPosition;
        public float checkpointYaw;
        public QuestProgressV1 mainQuest;
        public bool hasForestSealProgress;
        public ForestSealProgressV1 forestSeal;
        public bool hasRouteEnrichmentProgress;
        public RouteEnrichmentProgressV1 routeEnrichment;
        public bool hasSealConditionProgress;
        public SealConditionProgressV1 sealConditions;
        public float elapsedSeconds;
        public int deathCount;

        public static SaveGameV1 Create(
            ContentId checkpointId,
            float checkpointX,
            float checkpointY,
            float checkpointZ,
            float checkpointYaw,
            QuestProgressSnapshot questSnapshot,
            float elapsedSeconds = 0f,
            int deathCount = 0,
            ForestSealProgressSnapshot forestSealSnapshot = null,
            RouteEnrichmentSnapshot routeEnrichmentSnapshot = null,
            SealConditionSnapshot sealConditionSnapshot = null)
        {
            if (questSnapshot == null)
            {
                throw new ArgumentNullException(nameof(questSnapshot));
            }

            return new SaveGameV1
            {
                checkpointId = checkpointId.Value,
                checkpointPosition = new SaveVector3(checkpointX, checkpointY, checkpointZ),
                checkpointYaw = checkpointYaw,
                mainQuest = QuestProgressV1.FromSnapshot(questSnapshot),
                hasForestSealProgress = forestSealSnapshot != null,
                forestSeal = forestSealSnapshot == null
                    ? null
                    : ForestSealProgressV1.FromSnapshot(forestSealSnapshot),
                hasRouteEnrichmentProgress = routeEnrichmentSnapshot != null,
                routeEnrichment = routeEnrichmentSnapshot == null
                    ? null
                    : RouteEnrichmentProgressV1.FromSnapshot(routeEnrichmentSnapshot),
                hasSealConditionProgress = sealConditionSnapshot != null,
                sealConditions = sealConditionSnapshot == null
                    ? null
                    : SealConditionProgressV1.FromSnapshot(sealConditionSnapshot),
                elapsedSeconds = elapsedSeconds,
                deathCount = deathCount
            };
        }

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException($"Save schema {schemaVersion} is not supported.");
            }

            if (!ContentId.TryCreate(checkpointId, out ContentId parsedCheckpoint) ||
                !parsedCheckpoint.Value.StartsWith("checkpoint:", StringComparison.Ordinal))
            {
                throw new FormatException("Save checkpoint ID is invalid.");
            }

            if (!checkpointPosition.IsFinite || float.IsNaN(checkpointYaw) || float.IsInfinity(checkpointYaw))
            {
                throw new FormatException("Save checkpoint transform is invalid.");
            }

            if (mainQuest == null)
            {
                throw new FormatException("Save main quest progress is missing.");
            }

            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f || deathCount < 0)
            {
                throw new FormatException("Save session statistics are invalid.");
            }

            mainQuest.Validate();
            if (hasForestSealProgress)
            {
                if (forestSeal == null)
                {
                    throw new FormatException("Save forest seal progress is missing.");
                }

                forestSeal.Validate();
            }

            if (hasRouteEnrichmentProgress)
            {
                if (routeEnrichment == null)
                {
                    throw new FormatException("Save route enrichment progress is missing.");
                }

                routeEnrichment.Validate();
            }

            if (hasSealConditionProgress)
            {
                if (sealConditions == null)
                {
                    throw new FormatException("Save seal condition progress is missing.");
                }

                sealConditions.Validate();
            }
        }
    }

    [Serializable]
    public sealed class RouteEnrichmentProgressV1
    {
        public bool watchtowerDiscovered;
        public int routeChoice;
        public bool preSanctumEncounterCleared;
        public bool riskRewardClaimed;

        public static RouteEnrichmentProgressV1 FromSnapshot(RouteEnrichmentSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Validate();
            return new RouteEnrichmentProgressV1
            {
                watchtowerDiscovered = snapshot.WatchtowerDiscovered,
                routeChoice = (int)snapshot.RouteChoice,
                preSanctumEncounterCleared = snapshot.PreSanctumEncounterCleared,
                riskRewardClaimed = snapshot.RiskRewardClaimed
            };
        }

        public RouteEnrichmentSnapshot ToSnapshot()
        {
            Validate();
            return new RouteEnrichmentSnapshot(
                watchtowerDiscovered,
                (EmberValleyRouteChoice)routeChoice,
                preSanctumEncounterCleared,
                riskRewardClaimed);
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(EmberValleyRouteChoice), routeChoice))
            {
                throw new FormatException("Save route enrichment contains an unknown choice.");
            }


            new RouteEnrichmentSnapshot(
                watchtowerDiscovered,
                (EmberValleyRouteChoice)routeChoice,
                preSanctumEncounterCleared,
                riskRewardClaimed).Validate();
        }
    }

    [Serializable]
    public sealed class SealConditionProgressV1
    {
        public bool bridgeEncounterCleared;
        public bool bridgeMechanismAActivated;
        public bool bridgeMechanismBActivated;
        public bool courtyardGuardBroken;

        public static SealConditionProgressV1 FromSnapshot(SealConditionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Validate();
            return new SealConditionProgressV1
            {
                bridgeEncounterCleared = snapshot.BridgeEncounterCleared,
                bridgeMechanismAActivated = snapshot.BridgeMechanismAActivated,
                bridgeMechanismBActivated = snapshot.BridgeMechanismBActivated,
                courtyardGuardBroken = snapshot.CourtyardGuardBroken
            };
        }

        public SealConditionSnapshot ToSnapshot()
        {
            Validate();
            return new SealConditionSnapshot(
                bridgeEncounterCleared,
                bridgeMechanismAActivated,
                bridgeMechanismBActivated,
                courtyardGuardBroken);
        }

        public void Validate()
        {
            new SealConditionSnapshot(
                bridgeEncounterCleared,
                bridgeMechanismAActivated,
                bridgeMechanismBActivated,
                courtyardGuardBroken).Validate();
        }
    }

    [Serializable]
    public sealed class ForestSealProgressV1
    {
        public int phase;
        public bool supplyClaimed;
        public int runeChoice;

        public static ForestSealProgressV1 FromSnapshot(ForestSealProgressSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.Validate();
            return new ForestSealProgressV1
            {
                phase = (int)snapshot.Phase,
                supplyClaimed = snapshot.SupplyClaimed,
                runeChoice = (int)snapshot.RuneChoice
            };
        }

        public ForestSealProgressSnapshot ToSnapshot()
        {
            Validate();
            return new ForestSealProgressSnapshot(
                (ForestSealPhase)phase,
                supplyClaimed,
                (ForestRuneChoice)runeChoice);
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ForestSealPhase), phase) ||
                !Enum.IsDefined(typeof(ForestRuneChoice), runeChoice))
            {
                throw new FormatException("Save forest seal progress contains an unknown value.");
            }

            new ForestSealProgressSnapshot(
                (ForestSealPhase)phase,
                supplyClaimed,
                (ForestRuneChoice)runeChoice).Validate();
        }
    }

    [Serializable]
    public struct SaveVector3
    {
        public float x;
        public float y;
        public float z;

        public SaveVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool IsFinite =>
            !float.IsNaN(x) && !float.IsInfinity(x) &&
            !float.IsNaN(y) && !float.IsInfinity(y) &&
            !float.IsNaN(z) && !float.IsInfinity(z);
    }

    [Serializable]
    public sealed class QuestProgressV1
    {
        public string questId;
        public int stage;
        public string[] activatedSealIds;

        public static QuestProgressV1 FromSnapshot(QuestProgressSnapshot snapshot)
        {
            var seals = new string[snapshot.ActivatedSealIds.Count];
            for (int i = 0; i < seals.Length; i++)
            {
                seals[i] = snapshot.ActivatedSealIds[i];
            }

            return new QuestProgressV1
            {
                questId = snapshot.QuestId,
                stage = (int)snapshot.Stage,
                activatedSealIds = seals
            };
        }

        public QuestProgressSnapshot ToSnapshot()
        {
            Validate();
            return new QuestProgressSnapshot(questId, (MainQuestStage)stage, activatedSealIds);
        }

        public void Validate()
        {
            if (!ContentId.TryCreate(questId, out ContentId parsedQuest) ||
                !parsedQuest.Value.StartsWith("quest:", StringComparison.Ordinal))
            {
                throw new FormatException("Save quest ID is invalid.");
            }

            if (!Enum.IsDefined(typeof(MainQuestStage), stage) || activatedSealIds == null)
            {
                throw new FormatException("Save quest stage or seal list is invalid.");
            }

            var unique = new HashSet<ContentId>();
            foreach (string value in activatedSealIds)
            {
                if (!ContentId.TryCreate(value, out ContentId sealId) ||
                    !sealId.Value.StartsWith("seal:", StringComparison.Ordinal) ||
                    !unique.Add(sealId))
                {
                    throw new FormatException("Save contains an invalid or duplicate seal ID.");
                }
            }
        }
    }
}
