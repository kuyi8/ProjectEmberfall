using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Emberfall.Editor.Review
{
    /// <summary>Native NUnit root export for a FUTURE run only. Never reconstructs
    /// XML from MCP counts/DTOs or changes Test Runner/plugin/assertion state.</summary>
    [InitializeOnLoad]
    public static class VerificationNUnitCapture
    {
        const string PendingKey = "Emberfall.VerificationNUnitCapture.Pending.v1";
        const string OwnerSource = "Assets/_Game/Scripts/Editor/Review/VerificationNUnitCapture.cs";
        const string ApiSource = "Library/PackageCache/com.unity.test-framework@1.1.33/UnityEditor.TestRunner/Api/TestRunnerApi.cs";
        const string WriterSource = "Library/PackageCache/com.unity.test-framework@1.1.33/UnityEditor.TestRunner/CommandLineTest/ResultsWriter.cs";
        const string WriterName = "UnityEditor.TestTools.TestRunner.CommandLineTest.ResultsWriter";
        static TestRunnerApi api;
        static Sink sink;

        [Serializable] public sealed class ModuleEvidence
        {
            public string assembly, moduleVersionId, location, diskSha256;
        }
        [Serializable] public sealed class Record
        {
            public int schemaVersion = 1;
            public string label, mode, state, xmlPath, statusPath, armedUtc, startedUtc, finishedUtc;
            public string mcpJobId, nativeRootName, nativeRootId, nativeResultState, xmlSha256, error;
            public string startedRootAdaptorMode, finishedRootAdaptorMode, actualMcpMode;
            public string[] nativeAssemblyPlatforms = new string[0];
            public int startedTreeTestCaseCount, runStartedCallbacks, nativePassed, nativeFailed, nativeSkipped, nativeInconclusive, xmlLeafCount;
            public bool receivedRoot, nativeNUnitExport, captureSucceeded;
            public ModuleEvidence captureModule, frameworkModule, finishCaptureModule, finishFrameworkModule;
            public string captureSourceSha256, editorAsmdefSha256, apiSourceSha256, writerSourceSha256, writerSignature;
            public string[] events = new string[0];
            public string scope = "Actual future RunFinished ITestResultAdaptor exported by installed Unity Test Framework 1.1.33 ResultsWriter. Capture completion is NOT test success/full-regression acceptance; full/unfiltered mode requires the retained MCP request/job. No DTO/count-generated XML or retrospective capture.";
        }

        static VerificationNUnitCapture()
        {
            // Static registrations are lost on a domain reload. SessionState keeps
            // ONLY our pending export, and we register only our own new callback.
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                try { Register(); }
                catch (Exception exception) { Debug.LogError("[NUNIT_CAPTURE_RESTORE_FAILED] " + exception); }
        }

        /// <summary>Arm immediately before ONE future MCP run. A same-label retry
        /// returns the same status path without resetting/replaying any run.</summary>
        public static string Arm(string label, string mode)
        {
            if (string.IsNullOrEmpty(label) || !Regex.IsMatch(label, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]*\z"))
                throw new ArgumentException("A safe unique label is required.", nameof(label));
            if (mode != "EditMode" && mode != "PlayMode") throw new ArgumentException("Exact EditMode or PlayMode required.", nameof(mode));
            string directory = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "Builds/TestResults");
            string statusPath = Path.Combine(directory, label + "-nunit-capture.json");
            string xmlPath = Path.Combine(directory, label + "-nunit.xml");
            if (File.Exists(statusPath))
            {
                Record previous = JsonUtility.FromJson<Record>(File.ReadAllText(statusPath));
                if (previous == null || previous.label != label || previous.mode != mode || previous.xmlPath != xmlPath)
                    throw new InvalidDataException("Existing capture record does not match this request; evidence preserved.");
                return statusPath;
            }
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                throw new InvalidOperationException("One capture is still pending; do not arm another label or overwrite evidence.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || RunActive())
                throw new InvalidOperationException("Arm before a new run in idle Edit Mode; an already executing run cannot be retrospectively exported.");
            if (File.Exists(xmlPath)) throw new IOException("Existing NUnit XML preserved; use a fresh label.");
            MethodInfo method = WriterMethod();
            var record = new Record {
                label = label, mode = mode, state = "armed", statusPath = statusPath, xmlPath = xmlPath,
                armedUtc = DateTime.UtcNow.ToString("O"), captureModule = Module(typeof(VerificationNUnitCapture).Assembly),
                frameworkModule = Module(typeof(TestRunnerApi).Assembly), captureSourceSha256 = Hash(OwnerSource),
                editorAsmdefSha256 = Hash("Assets/_Game/Scripts/Editor/Emberfall.Editor.asmdef"),
                apiSourceSha256 = Hash(ApiSource), writerSourceSha256 = Hash(WriterSource), writerSignature = method.ToString(),
                events = new[] { "armed:" + DateTime.UtcNow.ToString("O") }
            };
            Directory.CreateDirectory(directory);
            using (var file = new FileStream(statusPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(JsonUtility.ToJson(record, true));
            SessionState.SetString(PendingKey, JsonUtility.ToJson(record));
            try { Register(); }
            catch (Exception exception) { Fail(record, exception); Disarm(); throw; }
            return statusPath;
        }

        public static string PendingStatus() => SessionState.GetString(PendingKey, "");

        /// <summary>Retain an interrupted capture only AFTER its actual owned run is gone.
        /// This never changes Test Runner/plugin status or fabricates a native result.</summary>
        public static string AbandonWhenIdle(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An observed reason is required.", nameof(reason));
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || RunActive())
                throw new InvalidOperationException("The actual run must be idle before abandoning its capture.");
            var record = Pending(); if (record == null) return "No pending capture.";
            record.state = "interrupted"; record.captureSucceeded = false;
            record.finishedUtc = DateTime.UtcNow.ToString("O"); record.error = reason;
            Save(record, "abandoned-after-observed-idle"); Disarm(); return record.statusPath;
        }

        sealed class Sink : ICallbacks
        {
            public void RunStarted(ITestAdaptor test)
            {
                Record record = Pending(); if (record == null) return;
                try
                {
                    if (test == null) throw new InvalidOperationException("Actual RunStarted root is missing.");
                    record.state = "running"; record.runStartedCallbacks++;
                    if (string.IsNullOrEmpty(record.startedUtc)) record.startedUtc = DateTime.UtcNow.ToString("O");
                    record.startedTreeTestCaseCount = test.TestCaseCount;
                    record.nativeRootName = test.FullName; record.nativeRootId = test.Id;
                    // UTF 1.1.33 local project roots have default TestMode=0.
                    // Record it, but bind mode to the actual MCP job and native
                    // assembly platform properties, not that default adaptor.
                    record.startedRootAdaptorMode = test.TestMode.ToString();
                    string job = CurrentMcpJobId();
                    record.actualMcpMode = McpJobMode(job);
                    if (record.actualMcpMode != record.mode)
                        throw new InvalidOperationException("Actual MCP job mode does not match the armed capture.");
                    if (!string.IsNullOrEmpty(record.mcpJobId) && record.mcpJobId != job)
                        throw new InvalidOperationException("A different MCP run reached the same pending capture.");
                    record.mcpJobId = job;
                    Save(record, "run-started");
                }
                catch (Exception exception) { Fail(record, exception); Disarm(); }
            }
            public void RunFinished(ITestResultAdaptor root)
            {
                Record record = Pending(); if (record == null) return;
                try
                {
                    if (record.runStartedCallbacks == 0 || root == null)
                        throw new InvalidOperationException("No actual future RunStarted/root received; no XML export allowed.");
                    if (root.Test == null || root.Test.Id != record.nativeRootId)
                        throw new InvalidOperationException("Actual RunFinished root does not match the captured RunStarted tree.");
                    record.finishedRootAdaptorMode = root.Test.TestMode.ToString();
                    if (McpJobMode(record.mcpJobId) != record.mode)
                        throw new InvalidOperationException("Retained MCP job mode changed during the capture.");
                    record.receivedRoot = true; record.nativeResultState = root.ResultState;
                    record.nativePassed = root.PassCount; record.nativeFailed = root.FailCount;
                    record.nativeSkipped = root.SkipCount; record.nativeInconclusive = root.InconclusiveCount;
                    record.finishCaptureModule = Module(typeof(VerificationNUnitCapture).Assembly);
                    record.finishFrameworkModule = Module(typeof(TestRunnerApi).Assembly);
                    if (record.finishCaptureModule.moduleVersionId != record.captureModule.moduleVersionId ||
                        record.finishFrameworkModule.moduleVersionId != record.frameworkModule.moduleVersionId)
                        throw new InvalidOperationException("Capture/framework module changed during the run; preserve status, do not silently rebind.");
                    MethodInfo method = WriterMethod();
                    // Native writer's stream overload lets US enforce CreateNew;
                    // WriteResultToFile otherwise overwrites and catches errors.
                    using (var file = new FileStream(record.xmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    {
                        object nativeWriter = Activator.CreateInstance(method.DeclaringType, true);
                        method.Invoke(nativeWriter, new object[] { root, writer, null });
                        writer.Flush(); file.Flush(true);
                    }
                    record.nativeNUnitExport = true; record.xmlSha256 = Hash(record.xmlPath);
                    var document = new XmlDocument(); document.Load(record.xmlPath);
                    record.xmlLeafCount = document.SelectNodes("//test-case").Count;
                    var platforms = new List<string>();
                    foreach (XmlNode property in document.SelectNodes("//test-suite[@type='Assembly']/properties/property[@name='platform']"))
                        if (!platforms.Contains(property.Attributes["value"].Value)) platforms.Add(property.Attributes["value"].Value);
                    record.nativeAssemblyPlatforms = platforms.ToArray();
                    if (platforms.Count != 1 || platforms[0] != record.mode)
                        throw new InvalidDataException("Native NUnit assembly platform does not match the actual MCP/armed mode; raw XML retained.");
                    if (document.DocumentElement == null || document.DocumentElement.Name != "test-run" ||
                        record.xmlLeafCount != root.PassCount + root.FailCount + root.SkipCount + root.InconclusiveCount)
                        throw new InvalidDataException("Native XML tree/leaf count disagrees with its real root; raw XML retained.");
                    record.state = "completed"; record.captureSucceeded = true;
                    record.finishedUtc = DateTime.UtcNow.ToString("O"); Save(record, "completed:" + root.ResultState);
                    Debug.Log("[NUNIT_CAPTURE_COMPLETE] " + record.statusPath + " root=" + root.ResultState + " leaves=" + record.xmlLeafCount);
                }
                catch (Exception exception) { Fail(record, exception); }
                finally { Disarm(); }
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        static void Register()
        {
            if (api == null) api = ScriptableObject.CreateInstance<TestRunnerApi>();
            if (sink != null) api.UnregisterCallbacks(sink);
            sink = new Sink(); api.RegisterCallbacks(sink, 10);
        }
        static void Disarm()
        {
            if (api != null && sink != null) api.UnregisterCallbacks(sink);
            sink = null; SessionState.EraseString(PendingKey);
            if (api != null) UnityEngine.Object.DestroyImmediate(api); api = null;
        }
        static Record Pending() { string json = SessionState.GetString(PendingKey, ""); return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Record>(json); }
        static void Save(Record record, string stateEvent)
        {
            var events = new List<string>(record.events); events.Add(stateEvent + ":" + DateTime.UtcNow.ToString("O")); record.events = events.ToArray();
            string json = JsonUtility.ToJson(record, true); SessionState.SetString(PendingKey, json);
            File.WriteAllText(record.statusPath, json, new UTF8Encoding(false));
        }
        static void Fail(Record record, Exception exception)
        {
            record.state = "capture-failed"; record.captureSucceeded = false; record.finishedUtc = DateTime.UtcNow.ToString("O");
            record.error = (exception is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : exception).ToString();
            try { if (File.Exists(record.xmlPath)) record.xmlSha256 = Hash(record.xmlPath); Save(record, "capture-failed"); }
            catch (Exception saveException) { Debug.LogError("[NUNIT_CAPTURE_STATUS_FAILED] " + saveException); }
            Debug.LogError("[NUNIT_CAPTURE_FAILED] " + record.label + " " + record.error);
        }
        static MethodInfo WriterMethod()
        {
            Type type = typeof(TestRunnerApi).Assembly.GetType(WriterName, true);
            MethodInfo method = type.GetMethod("WriteResultToStream", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(ITestResultAdaptor), typeof(StreamWriter), typeof(XmlWriterSettings) }, null);
            if (method == null) throw new MissingMethodException(WriterName, "WriteResultToStream(ITestResultAdaptor,StreamWriter,XmlWriterSettings)");
            return method;
        }
        static bool RunActive()
        {
            MethodInfo method = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("Cannot verify inactive Test Runner.");
            return (bool)method.Invoke(null, null);
        }
        static string CurrentMcpJobId()
        {
            Type type = Type.GetType("MCPForUnity.Editor.Services.TestJobManager, MCPForUnity.Editor");
            return type?.GetProperty("CurrentJobId", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as string ?? "";
        }
        static string McpJobMode(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) throw new InvalidOperationException("Actual MCP job ID is required; manual runs cannot be rebound.");
            Type type = Type.GetType("MCPForUnity.Editor.Services.TestJobManager, MCPForUnity.Editor");
            MethodInfo method = type?.GetMethod("GetJob", BindingFlags.Static | BindingFlags.NonPublic);
            object job = method?.Invoke(null, new object[] { jobId });
            string mode = job?.GetType().GetProperty("Mode")?.GetValue(job) as string;
            if (mode != "EditMode" && mode != "PlayMode") throw new InvalidOperationException("Cannot read the actual MCP job mode.");
            return mode;
        }
        static ModuleEvidence Module(Assembly assembly) => new ModuleEvidence {
            assembly = assembly.FullName, moduleVersionId = assembly.ManifestModule.ModuleVersionId.ToString(),
            location = assembly.Location, diskSha256 = Hash(assembly.Location)
        };
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
