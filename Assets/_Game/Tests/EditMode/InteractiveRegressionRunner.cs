using System;
using System.IO;
using System.Reflection;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    // Public Unity Test Runner fallback when the MCP job cache remains running after cancellation.
    // Lives in the existing test assembly and never ships in a Player.
    [InitializeOnLoad]
    public sealed class InteractiveRegressionRunner : ICallbacks
    {
        const string Key = "Emberfall.InteractiveRegression.Output";
        static readonly TestRunnerApi Api;
        static readonly InteractiveRegressionRunner Callback;

        static InteractiveRegressionRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Callback = new InteractiveRegressionRunner();
            Api.RegisterCallbacks(Callback);
        }

        public static string BeginPlayMode(string[] testNames = null)
            => Begin(TestMode.PlayMode, testNames);

        public static string BeginEditMode(string[] testNames = null)
            => Begin(TestMode.EditMode, testNames);

        private static string Begin(TestMode mode, string[] testNames)
        {
            bool active = (bool)typeof(TestRunnerApi).GetMethod("IsRunActive",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (active || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Idle Editor with one saved scene required.");
            string output = Path.GetFullPath("Builds/TestResults/0.9.4-isolated-" + mode.ToString().ToLowerInvariant() + "-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            SessionState.SetString(Key, output);
            Api.Execute(new ExecutionSettings(new Filter { testMode = mode, testNames = testNames }));
            return output;
        }

        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            string output = SessionState.GetString(Key, "");
            if (output.Length == 0 || result.HasChildren) return;
            File.AppendAllText(output + "/progress.txt", result.ResultState + " " + result.FullName + "\n");
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            string output = SessionState.GetString(Key, "");
            if (output.Length == 0) return;
            using (var writer = XmlWriter.Create(output + "/results.xml", new XmlWriterSettings { Indent = true }))
                result.ToXml().WriteTo(writer);
            File.WriteAllText(output + "/summary.txt", "pass=" + result.PassCount + " fail=" +
                result.FailCount + " skip=" + result.SkipCount + " state=" + result.ResultState);
            SessionState.EraseString(Key);
            // Unity restores the original scene itself; do not save or replace a changed scene here.
            Debug.Log("[REGRESSION_COMPLETE] " + output);
        }
    }
}
