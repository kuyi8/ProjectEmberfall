using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;

namespace Emberfall.Application.Flow
{
    public static class SealConditionChecklistText
    {
        private static readonly ContentId BridgeTemplateId =
            new ContentId("text:ui.bridge-seal-conditions");
        private static readonly ContentId CourtyardTemplateId =
            new ContentId("text:ui.courtyard-seal-condition");
        private static readonly ContentId CompleteId =
            new ContentId("text:ui.condition-complete");
        private static readonly ContentId IncompleteId =
            new ContentId("text:ui.condition-incomplete");

        public static string Build(
            Func<ContentId, string> resolve,
            bool showBridge,
            bool bridgeMechanismAActivated,
            bool bridgeEncounterCleared,
            bool bridgeMechanismBActivated,
            bool showCourtyard,
            bool courtyardGuardBroken)
        {
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            string complete = resolve(CompleteId);
            string incomplete = resolve(IncompleteId);
            var lines = new List<string>(2);
            if (showBridge)
            {
                lines.Add(string.Format(
                    resolve(BridgeTemplateId),
                    bridgeMechanismAActivated ? complete : incomplete,
                    bridgeEncounterCleared ? complete : incomplete,
                    bridgeMechanismBActivated ? complete : incomplete));
            }

            if (showCourtyard)
            {
                lines.Add(string.Format(
                    resolve(CourtyardTemplateId),
                    courtyardGuardBroken ? complete : incomplete));
            }

            return string.Join("\n", lines);
        }
    }
}
