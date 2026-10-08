#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Emberfall.Application.Flow;
using Emberfall.Networking;
using Emberfall.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Actual rendered controlled HUD; not natural route, remote Client or Player acceptance.</summary>
    public sealed class LayeredHudRuntimeTests
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        object group, ownedSize;
        EditorWindow view;
        PropertyInfo selected;
        int previousIndex, ownedIndex;
        bool ownsHost;
        CursorLockMode previousLock;
        bool previousCursor;
        readonly List<CaptureRow> frames = new List<CaptureRow>();
        string output;

        [Serializable] sealed class CaptureRow
        {
            public string name;
            public int width, height, frame;
            public bool actualOwnerHud;
            public float scale;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Actual Game View IMGUI at 960x540 and 2560x1440; see capture names for scope. offline-* is isolated camp, host-* is one real local Host waiting for a peer, completion-* is the actual production offline result reached via controlled public story interactions/damage in the existing quest integration. Completion tests verify the real save/return handler, not a human GUI click. No natural route/difficulty, remote Client, foreground or performance acceptance. No replicated-fact injection or source authoring. Reflection changes only the Editor Game View size and removes this fixture's uniquely owned custom size.";
            public CaptureRow[] captures;
        }

        [SetUp]
        public void OwnOnlyTemporaryViewSize()
        {
            frames.Clear();
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
                "Never interrupt a user's active peer session.");
            previousLock = Cursor.lockState; previousCursor = Cursor.visible;
            output = Path.GetFullPath("Builds/ArtReview/hud-layered/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var assembly = typeof(EditorWindow).Assembly;
            Type viewType = assembly.GetType("UnityEditor.GameView");
            view = EditorWindow.GetWindow(viewType); view.Show(); view.Focus();
            selected = viewType.GetProperty("selectedSizeIndex", All);
            previousIndex = (int)selected.GetValue(view);
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize");
            Type kind = assembly.GetType("UnityEditor.GameViewSizeType");
            ownedSize = Activator.CreateInstance(sizeType, All, null,
                new object[] { Enum.Parse(kind, "FixedResolution"), 960, 540, "EmberfallHudFixture-" + Guid.NewGuid().ToString("N") }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { ownedSize });
            ownedIndex = Count() - 1;
            Assert.That(SizeAt(ownedIndex), Is.SameAs(ownedSize));
        }

        [TearDown]
        public void RestoreOwnViewAndSession()
        {
            if (ownsHost) SessionRuntime.Current.Shutdown();
            if (selected != null) selected.SetValue(view, previousIndex);
            if (group != null && ownedSize != null)
            {
                int index = -1;
                for (int i = 0; i < Count(); i++) if (ReferenceEquals(SizeAt(i), ownedSize)) index = i;
                if (index >= 0)
                {
                    int builtin = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
                    Assert.That(index, Is.GreaterThanOrEqualTo(builtin));
                    // Unity 2022.3 expects the total-list index and subtracts builtin internally.
                    group.GetType().GetMethod("RemoveCustomSize").Invoke(group, new object[] { index });
                    for (int i = 0; i < Count(); i++) Assert.That(SizeAt(i), Is.Not.SameAs(ownedSize));
                }
            }
            Cursor.lockState = previousLock; Cursor.visible = previousCursor;
            if (output != null) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(new Report { captures = frames.ToArray() }, true));
        }

        [UnityTest]
        public IEnumerator OfflineHudAtBothScales_PreservesIsolatedSaveAndModalVisibility()
        {
            string save = M2RouteFlowController.EditorTestSavePath;
            Assert.That(save, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            for (int i = 0; i < 10; i++) yield return null;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow.IsInitialized, Is.True);
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(save)));
            var hud = Object.FindObjectOfType<M2RouteHud>();
            Assert.That(hud.ShouldShowWorldMarkers, Is.True);
            yield return Capture("offline-small", ownedIndex, 960, 540, false);
            yield return Capture("offline-large", FindSize(2560, 1440), 2560, 1440, false);
            Assert.That(hud.IsPaused, Is.False);
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.EqualTo(save));
        }

        [UnityTest]
        public IEnumerator ActualSpawnedLocalHostHudAtBothScales_UsesReplicatedOwnerPath()
        {
            yield return null;
            Assert.That(SessionRuntime.Current.StartHost(47841), Is.True, SessionRuntime.Current.Snapshot.Message);
            ownsHost = true;
            yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            var deadline = Time.realtimeSinceStartup + 10f;
            NetworkRouteHud hud = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var candidate in Object.FindObjectsOfType<NetworkRouteHud>())
                    if (candidate.ShouldShowCombatHud) hud = candidate;
                if (hud != null && hud.HasReplicatedObjective) break;
                yield return null;
            }
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.HasReplicatedObjective, Is.True);
            var player = hud.GetComponent<NetworkGymPlayer>();
            Assert.That(player.IsSpawned && player.IsOwner && player.IsServer, Is.True);
            yield return Capture("host-small", ownedIndex, 960, 540, true);
            yield return Capture("host-large", FindSize(2560, 1440), 2560, 1440, true);
            Assert.That(hud.ShouldShowCombatHud, Is.True);
            SessionRuntime.Current.Shutdown(); ownsHost = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OfflineCompletionAtBothScales_PreservesSavedFactsPauseAndActualReturnButton()
        {
            // Reuse the existing controlled public-story integration, not a forged Complete flag.
            // Its nested assertions are NOT reported as additional native test leaves.
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            yield return new EmberValleySceneTests().EmberValley_QuestInteractionsReachResultsAndPersist();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var hud = Object.FindObjectOfType<M2RouteHud>();
            var rig = Object.FindObjectOfType<Emberfall.Gameplay.Movement.ThirdPersonCameraRig>();
            Assert.That(flow.IsComplete, Is.True); Assert.That(hud.IsCompletionPresented, Is.True);
            Assert.That(hud.ShouldShowWorldMarkers, Is.False); Assert.That(rig.IsLookInputBlocked, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Quaternion before = rig.transform.rotation;
            yield return Capture("completion-small", ownedIndex, 960, 540, false);
            yield return Capture("completion-large", FindSize(2560, 1440), 2560, 1440, false);
            Assert.That(Quaternion.Angle(before, rig.transform.rotation), Is.LessThan(.01f));
            Assert.That(Time.timeScale, Is.Zero);
            // Exercise the same production handler the actual GUI button binds, retaining save
            // isolation. This is handler coverage, not a synthetic click or human-input acceptance.
            typeof(M2RouteHud).GetMethod("ReturnToMainMenu", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
            yield return null; yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("01_MainMenu"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            var disk = new Emberfall.Infrastructure.Saves.JsonSaveGameStore(M2RouteFlowController.EditorTestSavePath).LoadOrCreate(() => null);
            Assert.That(disk.Save.mainQuest.ToSnapshot().Stage, Is.EqualTo(Emberfall.Quests.Domain.MainQuestStage.Complete));
        }

        IEnumerator Capture(string name, int index, int width, int height, bool owner)
        {
            selected.SetValue(view, index); view.Repaint(); view.Focus();
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Screen.width, Is.EqualTo(width)); Assert.That(Screen.height, Is.EqualTo(height));
            for (int i = 0; i < 5; i++) yield return null;
            string path = Path.Combine(output, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            byte[] bytes = null;
            deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                try { if (File.Exists(path)) bytes = File.ReadAllBytes(path); } catch (IOException) { bytes = null; }
                if (bytes != null && bytes.Length > 32 && bytes[bytes.Length - 8] == 73 && bytes[bytes.Length - 7] == 69 && bytes[bytes.Length - 6] == 78 && bytes[bytes.Length - 5] == 68) break;
                bytes = null; yield return null;
            }
            Assert.That(bytes, Is.Not.Null);
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, bytes), Is.True);
                Assert.That(texture.width, Is.EqualTo(width)); Assert.That(texture.height, Is.EqualTo(height));
            }
            finally { Object.DestroyImmediate(texture); }
            frames.Add(new CaptureRow { name = name, width = width, height = height, frame = Time.frameCount,
                scale = EmberfallGuiTheme.Scale(width, height), actualOwnerHud = owner });
        }

        int Count() => (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
        object SizeAt(int index) => group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { index });
        int FindSize(int width, int height)
        {
            for (int i = 0; i < Count(); i++)
            {
                object size = SizeAt(i); Type type = size.GetType();
                if ((int)type.GetProperty("width").GetValue(size) == width && (int)type.GetProperty("height").GetValue(size) == height
                    && type.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution") return i;
            }
            throw new InvalidOperationException("Required existing fixed Game View resolution absent; no replacement setting guessed.");
        }
    }
}
#endif
