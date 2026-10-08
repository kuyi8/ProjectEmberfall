#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Domain;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class HudInformationRuntimeTests
    {
        private M2RouteHud _hud;
        private PlayerCombatActor _actor;
        private HudInformationGuiProbe _probe;
        private bool _controlledResources;
        private float _previousTimeScale;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        [Serializable] private sealed class Frame
        {
            public string image, combatState, expectedHealthLabel, expectedFlaskLabel;
            public int requestedFrame, fileReadyFrame, width, height, flasks, guiRepaints;
            public float health, stamina, timeScale;
            public bool paused, worldMarkersVisible, pngDecoded;
            public Vector3 actorPosition;
        }
        [Serializable] private sealed class Evidence
        {
            public string status = "failed", scope = "Editor PlayerLoop and actual Game View ScreenCapture including IMGUI. Three controlled offline UI states only. Low health uses public Health.ApplyDamage and empty flasks use TryConsume directly: NOT natural damage, healing input, encounter or quest results. No actor/camera relocation, AI disabling, physics/source edits or input injection. Not foreground/display, Player-build CJK, network, natural combat or performance acceptance; inspect PNGs. Expected labels are domain/helper expectations, not OCR of pixels.";
            public string savePath, saveOverride, graphics, font, missingGlyphs, bossMissingGlyphs;
            public bool sourceBytesSame, canvasCompositionValid, canvasRestoreValid, saveOverrideUnchanged;
            public float measuredFlaskLabelHeight;
            public bool bossLabelsFit;
            public float maximumBossLabelHeight;
            public int bossLabelMeasurements;
            public Frame[] frames;
        }

        [SetUp]
        public void RememberRuntimeScope()
        {
            _previousTimeScale = Time.timeScale;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
        }

        [TearDown]
        public void RestoreOnlyFixtureRuntimeState()
        {
            if (_hud != null) _hud.SetPaused(false);
            // The fresh fixture was asserted full before these two explicitly controlled mutations.
            if (_controlledResources && _actor != null && _actor.Model != null)
            { _actor.Model.Health.RestoreFull(); _actor.Model.HealingFlasks.Refill(); }
            if (_probe != null) Object.DestroyImmediate(_probe.gameObject);
            Time.timeScale = _previousTimeScale;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            // Input settings, devices and the namespace save override are never changed by this fixture.
        }

        [UnityTest]
        public IEnumerator ActualGameView_NormalLowEmptyAndPause_PreserveMatrixAndIsolatedSave()
        {
            string project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string isolatedRoot = Path.GetFullPath(Path.Combine(project, "Builds/TestResults/IsolatedSaves")) + Path.DirectorySeparatorChar;
            string saveOverride = M2RouteFlowController.EditorTestSavePath;
            Assert.That(saveOverride, Is.Not.Null.And.Not.Empty, "PlayModeSaveIsolation must run before NewGame.");
            Assert.That(Path.GetFullPath(saveOverride).StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True,
                "Do not interrupt an existing peer session to obtain offline HUD evidence.");
            string directory = Path.Combine(project, "Builds/ArtReview/hud-information",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Assert.That(Directory.Exists(directory), Is.False);
            Directory.CreateDirectory(directory);
            string[] inputs = Directory.GetFiles(Path.Combine(project, "Assets/_Game/Scenes"), "*.unity", SearchOption.AllDirectories)
                .Concat(new[] { "M2RouteHud.cs", "NetworkRouteHud.cs", "EmberfallGuiTheme.cs" }
                    .Select(p => Path.Combine(project, "Assets/_Game/Scripts/UI", p)))
                .SelectMany(p => new[] { p, p + ".meta" }).ToArray();
            var hashes = inputs.ToDictionary(p => p, Hash);
            var frames = new List<Frame>();
            var evidence = new Evidence { saveOverride = saveOverride, graphics = SystemInfo.graphicsDeviceType.ToString() };
            try
            {
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 8f;
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
                { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
                Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True);
                evidence.savePath = flow.SavePath;
                Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(saveOverride)));
                _hud = Object.FindObjectOfType<M2RouteHud>(); _actor = Object.FindObjectOfType<PlayerCombatActor>();
                Assert.That(_hud, Is.Not.Null); Assert.That(_actor, Is.Not.Null); Assert.That(_actor.Model, Is.Not.Null);
                Assert.That(_hud.IsGuideVisible, Is.False); Assert.That(_hud.IsPaused, Is.False);
                Assert.That(Object.FindObjectOfType<InputTelemetryOverlay>().IsVisible, Is.False);
                Assert.That(_actor.Model.Health.Normalized, Is.EqualTo(1f));
                Assert.That(_actor.Model.HealingFlasks.CurrentCharges, Is.EqualTo(_actor.Model.HealingFlasks.MaximumCharges));
                Assert.That(Camera.main, Is.Not.Null); Assert.That(Camera.main.enabled, Is.True);
                var gameView = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                gameView.Show(); gameView.Focus(); // Editor tab only; not OS foreground/display acceptance.
                _probe = new GameObject("HUD information fixture GUI probe").AddComponent<HudInformationGuiProbe>();
                yield return Capture(directory, "normal.png", frames);

                _controlledResources = true;
                _actor.Model.Health.ApplyDamage(_actor.Model.Health.Current - _actor.Model.Health.Maximum * .2f, 0f);
                while (_actor.Model.HealingFlasks.CurrentCharges > 0)
                    Assert.That(_actor.Model.HealingFlasks.TryConsume(), Is.True);
                yield return null; yield return null;
                Assert.That(_actor.Model.Health.Normalized, Is.InRange(.01f, .25f));
                Assert.That(_actor.Model.HealingFlasks.CurrentCharges, Is.Zero);
                yield return Capture(directory, "controlled-low-empty.png", frames);

                _hud.SetPaused(true);
                yield return null; yield return null;
                Assert.That(_hud.IsPaused, Is.True); Assert.That(_hud.ShouldShowWorldMarkers, Is.False);
                Assert.That(Time.timeScale, Is.Zero);
                yield return Capture(directory, "paused.png", frames);
                Assert.That(frames.Count, Is.EqualTo(3));
                Assert.That(_probe.repaintCount, Is.GreaterThan(0));
                Assert.That(_probe.compositionValid && _probe.restoreValid, Is.True);
                Assert.That(_probe.missingGlyphs, Is.Empty, "Actual Editor dynamic-font request must contain the tested CJK glyphs.");
                Assert.That(_probe.flaskLabelHeight, Is.GreaterThan(0).And.LessThanOrEqualTo(24f));
                Assert.That(_probe.bossLabelMeasurements, Is.GreaterThan(0));
                Assert.That(_probe.bossLabelsFit, Is.True, "Real GUIStyle must fit the unchanged 20-unit NET Boss row.");
                Assert.That(_probe.bossMissingGlyphs, Is.Empty);
                Assert.That(hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True);
                Assert.That(M2RouteFlowController.EditorTestSavePath, Is.EqualTo(saveOverride));
                evidence.status = "complete";
            }
            finally
            {
                evidence.sourceBytesSame = hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                evidence.saveOverrideUnchanged = M2RouteFlowController.EditorTestSavePath == saveOverride;
                evidence.frames = frames.ToArray();
                if (_probe != null)
                {
                    evidence.canvasCompositionValid = _probe.compositionValid; evidence.canvasRestoreValid = _probe.restoreValid;
                    evidence.font = _probe.fontName; evidence.missingGlyphs = _probe.missingGlyphs;
                    evidence.measuredFlaskLabelHeight = _probe.flaskLabelHeight;
                    evidence.bossLabelsFit = _probe.bossLabelsFit;
                    evidence.maximumBossLabelHeight = _probe.maximumBossLabelHeight;
                    evidence.bossLabelMeasurements = _probe.bossLabelMeasurements;
                    evidence.bossMissingGlyphs = _probe.bossMissingGlyphs;
                }
                File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(evidence, true));
            }
        }

        private IEnumerator Capture(string directory, string image, List<Frame> frames)
        {
            int initialRepaints = _probe.repaintCount;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (_probe.repaintCount <= initialRepaints && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_probe.repaintCount, Is.GreaterThan(initialRepaints), "Require actual Game View OnGUI repaint, not manual OnGUI invocation.");
            var display = HudResourcePresentation.Resolve(_actor.Model.Health.Normalized, _actor.Model.Stamina.Normalized,
                _actor.Model.State, _actor.Model.HealingFlasks.CurrentCharges);
            var row = new Frame
            {
                image = image, requestedFrame = Time.frameCount, width = Screen.width, height = Screen.height,
                combatState = _actor.Model.State.ToString(), health = _actor.Model.Health.Current, stamina = _actor.Model.Stamina.Current,
                flasks = _actor.Model.HealingFlasks.CurrentCharges, paused = _hud.IsPaused, timeScale = Time.timeScale,
                worldMarkersVisible = _hud.ShouldShowWorldMarkers, actorPosition = _actor.transform.position,
                expectedHealthLabel = display.HealthLabel, expectedFlaskLabel = display.FlaskLabel, guiRepaints = _probe.repaintCount
            };
            frames.Add(row);
            string path = Path.Combine(directory, image);
            ScreenCapture.CaptureScreenshot(path);
            byte[] bytes = null;
            deadline = Time.realtimeSinceStartup + 8f;
            for (int attempts = 0; attempts < 300 && Time.realtimeSinceStartup < deadline; attempts++)
            {
                try { if (File.Exists(path)) bytes = File.ReadAllBytes(path); } catch (IOException) { bytes = null; }
                if (bytes != null && bytes.Length > 32 && bytes[bytes.Length - 8] == 73 && bytes[bytes.Length - 7] == 69 &&
                    bytes[bytes.Length - 6] == 78 && bytes[bytes.Length - 5] == 68) break; // final PNG IEND chunk
                bytes = null; yield return null;
            }
            row.fileReadyFrame = Time.frameCount;
            Assert.That(bytes, Is.Not.Null, "Timed-out/partial screenshot remains a failed capture, never a fabricated image.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                row.pngDecoded = ImageConversion.LoadImage(texture, bytes);
                Assert.That(row.pngDecoded, Is.True);
                Assert.That(texture.width, Is.EqualTo(row.width)); Assert.That(texture.height, Is.EqualTo(row.height));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }

    /// <summary>Editor-test-only probe; all GUI access occurs in an actual OnGUI callback.</summary>
    public sealed class HudInformationGuiProbe : MonoBehaviour
    {
        public int repaintCount;
        public bool compositionValid = true, restoreValid = true;
        public string fontName, missingGlyphs = "not sampled";
        public float flaskLabelHeight;
        // Actual OnGUI font/layout measurement, not a rendered Boss/network session.
        public bool bossLabelsFit = true;
        public float maximumBossLabelHeight;
        public int bossLabelMeasurements;
        public string bossMissingGlyphs = "not sampled";
        private GUIStyle _label;
        private GUIStyle _bossLabel;
        private const string Characters = "生命危急耐力偏低药剂耗尽游戏已暂停继续游戏保存并返回主菜单操作指南录制输入生命已满治疗中动作中不可使用";

        private void OnGUI()
        {
            Matrix4x4 original = GUI.matrix;
            try
            {
                using (new EmberfallGuiTheme.CanvasScope(1.125f))
                {
                    Matrix4x4 incoming = GUI.matrix;
                    using (EmberfallGuiTheme.Canvas())
                    {
                        float scale = EmberfallGuiTheme.Scale(Screen.width, Screen.height);
                        compositionValid &= Same(GUI.matrix, incoming * Matrix4x4.Scale(new Vector3(scale, scale, 1f)));
                    }
                    restoreValid &= Same(GUI.matrix, incoming);
                }
                restoreValid &= Same(GUI.matrix, original);
            }
            finally { GUI.matrix = original; }
            if (Event.current.type != EventType.Repaint) return;
            repaintCount++;
            _label ??= EmberfallGuiTheme.Label(14);
            Font font = _label.font;
            fontName = font != null ? font.name : "NULL";
            missingGlyphs = string.Empty;
            if (font == null) missingGlyphs = Characters;
            else
            {
                font.RequestCharactersInTexture(Characters, 14, FontStyle.Normal);
                foreach (char character in Characters)
                    if (!font.GetCharacterInfo(character, out _, 14, FontStyle.Normal)) missingGlyphs += character;
            }
            flaskLabelHeight = _label.CalcHeight(new GUIContent("药剂耗尽"), 100f);
            _bossLabel ??= new GUIStyle(GUI.skin.label) {
                font = _label.font, fontSize = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(0, 0, 0, 0)
            };
            const string bossCharacters = "第一阶段第二阶段正在蓄势攻击中收招破绽二连斩盾击直线冲锋符文劈斩延迟爆破";
            bossMissingGlyphs = string.Empty;
            _bossLabel.font.RequestCharactersInTexture(bossCharacters, 16, FontStyle.Bold);
            foreach (char character in bossCharacters)
                if (!_bossLabel.font.GetCharacterInfo(character, out _, 16, FontStyle.Bold)) bossMissingGlyphs += character;
            foreach (var size in new[] { new Vector2(960,540), new Vector2(1024,768), new Vector2(1920,1080), new Vector2(2560,1440) })
            {
                float logicalWidth = size.x / EmberfallGuiTheme.Scale(size.x, size.y);
                float labelWidth = NetworkHudLayout.Resolve(logicalWidth).Boss.width - 36f;
                foreach (var phase in new[] { "第一阶段", "第二阶段" })
                foreach (var state in new[] { WardenState.Windup, WardenState.Attack, WardenState.Recovery })
                foreach (WardenAttackKind attack in Enum.GetValues(typeof(WardenAttackKind)))
                {
                    string stateName = state == WardenState.Windup ? "正在蓄势" : state == WardenState.Attack ? "攻击中" : "收招破绽";
                    string label = NetworkRouteHud.BuildBossStatus(phase, stateName, state, attack);
                    float height = _bossLabel.CalcHeight(new GUIContent(label), labelWidth);
                    maximumBossLabelHeight = Mathf.Max(maximumBossLabelHeight, height);
                    bossLabelsFit &= height <= 20f;
                    bossLabelMeasurements++;
                }
            }
        }

        private static bool Same(Matrix4x4 left, Matrix4x4 right)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(left[i] - right[i]) > .000001f) return false;
            return true;
        }
    }
}
#endif
