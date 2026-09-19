using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class M5cContentChecklistTests
    {
        private const string ChecklistPath = "Tools/Verification/m5c-content-checklist.v1.json";

        [Test]
        public void Checklist_HasUniqueDesignBackedItemsAndIndependentEventMappings()
        {
            ChecklistDocument document = Load();
            Assert.That(document.schemaVersion, Is.EqualTo(1));
            Assert.That(document.items, Is.Not.Empty);
            Assert.That(document.items.Select(item => item.id).Distinct().Count(), Is.EqualTo(document.items.Length));
            foreach (ChecklistItem item in document.items)
            {
                Assert.That(item.id, Is.Not.Empty);
                Assert.That(item.category, Is.Not.Empty);
                Assert.That(item.designEvidence, Is.Not.Empty, item.id);
                Assert.That(item.eventKeys, Is.Not.Null.And.Not.Empty, item.id);
                Assert.That(item.eventKeys.All(key => key.Contains(":")), Is.True, item.id);
            }
        }

        [Test]
        public void Checklist_RiskRouteRunCoversEveryApplicableItem()
        {
            ChecklistDocument document = Load();
            var observed = new HashSet<string>(new[]
            {
                "forest-encounter:cleared", "bridge-encounter:cleared", "courtyard-encounter:cleared",
                "pre-sanctum-encounter:cleared", "warden-encounter:defeated", "forest-rune-ember:completed",
                "route-choice-risk:selected", "old-watchtower:discovered", "forest-seal:activated",
                "bridge-mechanism-a:activated", "bridge-condition:completed", "bridge-mechanism-b:activated",
                "courtyard-guard:broken", "route-risk-reward:claimed"
            }, StringComparer.Ordinal);

            foreach (ChecklistItem item in document.items)
            {
                bool applicable = item.requiredWhenAnyOf == null || item.requiredWhenAnyOf.Length == 0 ||
                                  item.requiredWhenAnyOf.Any(observed.Contains);
                if (applicable)
                    Assert.That(item.eventKeys.Any(observed.Contains), Is.True, item.id);
            }
        }

        private static ChecklistDocument Load()
        {
            string path = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ChecklistPath));
            Assert.That(File.Exists(path), Is.True, path);
            return JsonUtility.FromJson<ChecklistDocument>(File.ReadAllText(path));
        }

        [Serializable]
        private sealed class ChecklistDocument
        {
            public int schemaVersion;
            public ChecklistItem[] items;
        }

        [Serializable]
        private sealed class ChecklistItem
        {
            public string id;
            public string category;
            public string[] eventKeys;
            public string[] requiredWhenAnyOf;
            public string designEvidence;
        }
    }
}
