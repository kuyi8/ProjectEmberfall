#if UNITY_EDITOR
using System;
using System.IO;
using Emberfall.Application.Flow;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Protect interactive Editor users as well as command-line test runs.</summary>
    [SetUpFixture]
    public sealed class PlayModeSaveIsolation
    {
        private string _previous;

        [OneTimeSetUp]
        public void Begin()
        {
            _previous = M2RouteFlowController.EditorTestSavePath;
            string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,
                "../Builds/TestResults/IsolatedSaves", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            M2RouteFlowController.EditorTestSavePath = Path.Combine(directory, "emberfall-save-v1.json");
            Debug.Log("[TEST_SAVE_ISOLATION] " + M2RouteFlowController.EditorTestSavePath);
        }

        [OneTimeTearDown]
        public void End()
        {
            M2RouteFlowController.EditorTestSavePath = _previous;
        }
    }
}
#endif
