using System.Collections.Generic;
using Emberfall.AI.Unity;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Diagnostics;
using Emberfall.Gameplay.Input;
using UnityEngine;

namespace Emberfall.UI
{
    public sealed class InputTelemetryOverlay : MonoBehaviour
    {
        private const float LookDisplayLimit = 36f;
        private const float EventDuration = 1.1f;

        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private bool _visible = true;

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _keyStyle;
        private PlayerInputSnapshot _previous;
        private string _recentEvent = "等待输入";
        private float _eventRemaining;
        private readonly List<IEncounterTelemetrySource> _encounterSources = new List<IEncounterTelemetrySource>();
        private readonly List<ITacticalTelemetrySource> _tacticalSources = new List<ITacticalTelemetrySource>();
        private M2RouteHud _routeHud;
        private WardenActor _warden;
        private string _encounterTelemetry = "遭遇：—";
        private string _tacticalTelemetry = "屏外威胁 0　中立符文：—";
        private string _contentTelemetry = "内容：尚未初始化";
        private float _telemetryRefreshRemaining;

        public bool IsVisible => _visible;
        public PlayerInputSnapshot LatestSnapshot { get; private set; }
        public string EncounterTelemetryText => _encounterTelemetry;
        public string TacticalTelemetryText => _tacticalTelemetry;
        public string ContentTelemetryText => _contentTelemetry;

        public void Configure(PlayerInputReader input)
        {
            _input = input;
        }

        private void Start()
        {
            _routeHud = Object.FindObjectOfType<M2RouteHud>();
            _warden = Object.FindObjectOfType<WardenActor>();
            MonoBehaviour[] behaviours = Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IEncounterTelemetrySource encounter) _encounterSources.Add(encounter);
                if (behaviours[i] is ITacticalTelemetrySource tactical) _tacticalSources.Add(tactical);
            }
            RefreshCombatTelemetry();
            RefreshContentTelemetry();
        }

        private void Update()
        {
            if (_input == null)
            {
                return;
            }

            if (_input.ConsumeInputOverlayPressed())
            {
                _visible = !_visible;
            }

            LatestSnapshot = _input.CaptureSnapshot();
            ObserveRisingEdge(LatestSnapshot.Sprint, _previous.Sprint, "Sprint / Shift");
            ObserveRisingEdge(LatestSnapshot.Interact, _previous.Interact, "Interact / E");
            ObserveRisingEdge(LatestSnapshot.Guard, _previous.Guard, "Guard / Q");
            ObserveRisingEdge(LatestSnapshot.Dodge, _previous.Dodge, "Dodge / Space");
            ObserveRisingEdge(LatestSnapshot.Heal, _previous.Heal, "Heal / R");
            ObserveRisingEdge(LatestSnapshot.LightAttack, _previous.LightAttack, "LightAttack / LMB");
            ObserveRisingEdge(LatestSnapshot.HeavyAttack, _previous.HeavyAttack, "HeavyAttack / RMB");
            ObserveRisingEdge(LatestSnapshot.RangedAttack, _previous.RangedAttack, "ThrowingKnife / F");
            ObserveRisingEdge(LatestSnapshot.LockOn, _previous.LockOn, "LockToggle / MMB");
            _previous = LatestSnapshot;
            _eventRemaining = Mathf.Max(0f, _eventRemaining - Time.unscaledDeltaTime);
            _telemetryRefreshRemaining -= Time.unscaledDeltaTime;
            if (_telemetryRefreshRemaining <= 0f)
            {
                RefreshCombatTelemetry();
                _telemetryRefreshRemaining = 0.2f;
            }
        }

        private void OnGUI()
        {
            if (!_visible || _input == null)
            {
                return;
            }

            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.72f, 1.15f);
            float panelHeight = Debug.isDebugBuild ? 400f : 332f;
            Rect panel = new Rect(
                Screen.width - (432f * scale),
                Screen.height - ((panelHeight + 18f) * scale),
                414f * scale,
                panelHeight * scale);
            DrawPanel(panel, 0.9f);
            GUI.Label(ScaleRect(panel, 14f, 8f, 386f, 24f, scale), "录屏输入监视（F2 隐藏）", _titleStyle);

            float key = 38f * scale;
            float gap = 6f * scale;
            float keyX = panel.x + (18f * scale);
            float keyY = panel.y + (42f * scale);
            DrawKey(new Rect(keyX + key + gap, keyY, key, key), "W", LatestSnapshot.Move.y > 0.25f);
            DrawKey(new Rect(keyX + ((key + gap) * 2f), keyY, key, key), "SH", LatestSnapshot.Sprint);
            DrawKey(new Rect(keyX, keyY + key + gap, key, key), "A", LatestSnapshot.Move.x < -0.25f);
            DrawKey(new Rect(keyX + key + gap, keyY + key + gap, key, key), "S", LatestSnapshot.Move.y < -0.25f);
            DrawKey(new Rect(keyX + ((key + gap) * 2f), keyY + key + gap, key, key), "D", LatestSnapshot.Move.x > 0.25f);

            float actionX = panel.x + (150f * scale);
            DrawKey(new Rect(actionX, keyY, 40f * scale, key), "Q", LatestSnapshot.Guard);
            DrawKey(new Rect(actionX + (45f * scale), keyY, 40f * scale, key), "E", LatestSnapshot.Interact);
            DrawKey(new Rect(actionX + (90f * scale), keyY, 40f * scale, key), "R", LatestSnapshot.Heal);
            DrawKey(new Rect(actionX + (135f * scale), keyY, 40f * scale, key), "F", LatestSnapshot.RangedAttack);
            DrawKey(new Rect(actionX + (180f * scale), keyY, 62f * scale, key), "SPC", LatestSnapshot.Dodge);
            DrawKey(new Rect(actionX, keyY + key + gap, 72f * scale, key), "LMB", LatestSnapshot.LightAttack);
            DrawKey(new Rect(actionX + (78f * scale), keyY + key + gap, 72f * scale, key), "RMB", LatestSnapshot.HeavyAttack);
            DrawKey(new Rect(actionX + (156f * scale), keyY + key + gap, 68f * scale, key), "MMB", LatestSnapshot.LockOn);

            Rect vectorArea = new Rect(panel.x + (18f * scale), panel.y + (144f * scale), 118f * scale, 104f * scale);
            DrawVectorPad(vectorArea, LatestSnapshot.Look, LatestSnapshot.LookUsesPointer);
            GUI.Label(
                new Rect(panel.x + (150f * scale), panel.y + (145f * scale), 246f * scale, 50f * scale),
                $"Move ({LatestSnapshot.Move.x,5:0.00}, {LatestSnapshot.Move.y,5:0.00})\nLook  ({LatestSnapshot.Look.x,5:0.0}, {LatestSnapshot.Look.y,5:0.0})",
                _bodyStyle);
            GUI.Label(
                new Rect(panel.x + (150f * scale), panel.y + (207f * scale), 246f * scale, 34f * scale),
                _eventRemaining > 0f ? $"最近：{_recentEvent}" : "最近：—",
                _bodyStyle);
            GUI.Label(
                new Rect(panel.x + (18f * scale), panel.y + (252f * scale), 378f * scale, 42f * scale),
                _encounterTelemetry,
                _bodyStyle);
            GUI.Label(
                new Rect(panel.x + (18f * scale), panel.y + (298f * scale), 378f * scale, 24f * scale),
                _tacticalTelemetry,
                _bodyStyle);
            if (Debug.isDebugBuild)
            {
                GUI.Label(
                    new Rect(panel.x + (18f * scale), panel.y + (328f * scale), 378f * scale, 62f * scale),
                    _contentTelemetry,
                    _bodyStyle);
            }
        }

        private void RefreshCombatTelemetry()
        {
            EncounterTelemetrySnapshot best = default;
            bool hasEncounter = false;
            for (int i = 0; i < _encounterSources.Count; i++)
            {
                if (!_encounterSources[i].TryCaptureEncounterTelemetry(out EncounterTelemetrySnapshot candidate))
                {
                    continue;
                }
                if (!hasEncounter || candidate.Relevance > best.Relevance)
                {
                    best = candidate;
                    hasEncounter = true;
                }
            }

            _encounterTelemetry = _warden?.EncounterActive == true
                ? $"BOSS {_warden.Phase} / {_warden.State} · {_warden.Brain.CurrentAttack}\n" +
                  $"HP {_warden.HealthNormalized:P0}　POSTURE {_warden.SecondaryResourceNormalized:P0}　{_warden.LastBossEvent}"
                : hasEncounter
                    ? $"遭遇 {best.EncounterLabel}　ATTACK {DisplayOrDash(best.AttackerLabel)}\n" +
                      $"SUPPORT-L {DisplayOrDash(best.SupportLeftLabel)}　SUPPORT-R {DisplayOrDash(best.SupportRightLabel)}"
                    : "遭遇：—";

            string tacticalState = "—";
            for (int i = 0; i < _tacticalSources.Count; i++)
            {
                if (!_tacticalSources[i].TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot tactical)) continue;
                tacticalState = tactical.State == TacticalTelemetryState.Ready ? "READY" :
                    tactical.State == TacticalTelemetryState.Telegraph ? "ARMED" : "SPENT";
                break;
            }
            _tacticalTelemetry = $"屏外威胁 {_routeHud?.OffscreenThreatCount ?? 0}　中立符文：{tacticalState}";
        }

        private void RefreshContentTelemetry()
        {
            if (!ContentPackageRuntime.IsInitialized)
            {
                _contentTelemetry = $"内容 client {UnityEngine.Application.version} · 未初始化";
                return;
            }

            ContentPackageSelection selection = ContentPackageRuntime.Current;
            string source = selection.Source switch
            {
                ContentPackageSource.Patch => "PATCH",
                ContentPackageSource.FallbackToBuiltin => "FALLBACK → BUILTIN",
                _ => "BUILTIN"
            };
            string reason = string.IsNullOrWhiteSpace(selection.FallbackReason)
                ? "—"
                : selection.FallbackReason.Length <= 56
                    ? selection.FallbackReason
                    : selection.FallbackReason.Substring(0, 53) + "...";
            _contentTelemetry =
                $"内容 client {UnityEngine.Application.version} · schema {selection.Snapshot.Manifest.SchemaVersion} · " +
                $"content {selection.Snapshot.Manifest.ContentVersion}\n来源 {source} · 回退 {reason}";
        }

        private static string DisplayOrDash(string value) => string.IsNullOrEmpty(value) ? "—" : value;

        private void ObserveRisingEdge(bool current, bool previous, string actionName)
        {
            if (!current || previous)
            {
                return;
            }

            _recentEvent = actionName;
            _eventRemaining = EventDuration;
        }

        private void DrawVectorPad(Rect rect, Vector2 rawLook, bool pointer)
        {
            DrawPanel(rect, 0.78f);
            Vector2 normalized = pointer
                ? Vector2.ClampMagnitude(rawLook / LookDisplayLimit, 1f)
                : Vector2.ClampMagnitude(rawLook, 1f);
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.34f;
            DrawLine(new Vector2(rect.x + 8f, center.y), new Vector2(rect.xMax - 8f, center.y), 1f, new Color(0.35f, 0.42f, 0.45f));
            DrawLine(new Vector2(center.x, rect.y + 22f), new Vector2(center.x, rect.yMax - 8f), 1f, new Color(0.35f, 0.42f, 0.45f));
            Vector2 point = center + new Vector2(normalized.x, -normalized.y) * radius;
            Color previous = GUI.color;
            GUI.color = new Color(0.95f, 0.57f, 0.18f);
            GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(rect.x + 7f, rect.y + 2f, rect.width - 14f, 20f), pointer ? "Mouse Look Vector2" : "Stick Look Vector2", _bodyStyle);
        }

        private void DrawKey(Rect rect, string label, bool pressed)
        {
            Color previous = GUI.color;
            GUI.color = pressed ? new Color(1f, 0.43f, 0.08f, 0.96f) : new Color(0.16f, 0.2f, 0.22f, 0.96f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, label, _keyStyle);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.96f, 0.77f, 0.35f) }
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 13,
                normal = { textColor = new Color(0.86f, 0.9f, 0.92f) }
            };
            _keyStyle ??= new GUIStyle(_bodyStyle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static Rect ScaleRect(Rect panel, float x, float y, float width, float height, float scale) =>
            new Rect(panel.x + x * scale, panel.y + y * scale, width * scale, height * scale);

        private static void DrawPanel(Rect rect, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.035f, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawLine(Vector2 start, Vector2 end, float width, Color color)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Vector2 delta = end - start;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, delta.magnitude, width), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }
}
