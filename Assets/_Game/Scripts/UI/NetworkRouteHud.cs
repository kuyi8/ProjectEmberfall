using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Targeting;
using Emberfall.Networking;
using UnityEngine;

namespace Emberfall.UI
{
    public readonly struct NetworkHudLayout
    {
        public NetworkHudLayout(Rect player, Rect boss, Rect quest)
        {
            Player = player;
            Boss = boss;
            Quest = quest;
        }

        public Rect Player { get; }
        public Rect Boss { get; }
        public Rect Quest { get; }

        public static NetworkHudLayout Resolve(float screenWidth)
        {
            const float margin = 18f;
            const float gap = 16f;
            float playerWidth = Mathf.Clamp(screenWidth * 0.2f, 300f, 350f);
            float questWidth = Mathf.Clamp(screenWidth * 0.23f, 320f, 420f);
            var player = new Rect(margin, margin, playerWidth, 190f);
            var quest = new Rect(screenWidth - margin - questWidth, margin, questWidth, 150f);
            float corridorStart = player.xMax + gap;
            float corridorEnd = quest.xMin - gap;
            float corridorWidth = corridorEnd - corridorStart;
            if (corridorWidth >= 300f)
            {
                float bossWidth = Mathf.Min(corridorWidth, 620f);
                float bossX = corridorStart + ((corridorWidth - bossWidth) * 0.5f);
                return new NetworkHudLayout(player, new Rect(bossX, margin, bossWidth, 94f), quest);
            }

            float stackedBossWidth = Mathf.Min(620f, Mathf.Max(300f, screenWidth - (margin * 2f)));
            float stackedBossX = (screenWidth - stackedBossWidth) * 0.5f;
            float stackedBossY = margin + Mathf.Max(player.height, quest.height) + gap;
            return new NetworkHudLayout(
                player,
                new Rect(stackedBossX, stackedBossY, stackedBossWidth, 94f),
                quest);
        }
    }

    /// <summary>
    /// Player-facing IMGUI adapter for the co-op route. It reads replicated facts only;
    /// all combat, interaction and quest mutations remain in the networking authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkRouteHud : MonoBehaviour
    {
        [SerializeField] private NetworkGymPlayer _player;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private LockOnTargeting _targeting;

        private NetworkGymWorldObjective _objective;
        private NetworkWarden _warden;
        private float _nextWorldRefresh;
        private bool _showGuide = false;
        private HudResourcePresentation _resources;
        private bool _resourcesReady;
        private string _flaskText;
        private string _lastFlaskLabel;
        private int _lastFlaskCharges = -1;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _centerStyle;
        private GUIStyle _bossStatusStyle;

        public bool IsConfigured => _player != null && _input != null && _targeting != null;
        public bool IsLockOnConfigured => _targeting != null;
        public bool HasReplicatedObjective => _objective != null;
        public bool IsGuideVisible => _showGuide;
        public bool ShouldShowCombatHud => _player != null && _player.IsSpawned && _player.IsOwner &&
            (_objective == null || !_objective.ResultPublished);

        public void Configure(NetworkGymPlayer player, PlayerInputReader input, LockOnTargeting targeting)
        {
            _player = player;
            _input = input;
            _targeting = targeting;
            _showGuide = false;
            _resourcesReady = false;
        }

        public static string BuildInteractionPrompt(string interactionLabel, bool inRange, float distance)
        {
            if (string.IsNullOrWhiteSpace(interactionLabel)) return string.Empty;
            return inRange
                ? $"E　{interactionLabel}"
                : $"前往目标 · {interactionLabel} · {Mathf.Max(0f, distance):0.0}m";
        }

        public static string BuildBossStatus(string phaseLabel, string stateLabel,
            Emberfall.AI.Domain.WardenState state, Emberfall.AI.Domain.WardenAttackKind attack)
            => WardenHudPresentation.AppendCommittedAttack($"{phaseLabel} · {stateLabel}", state, attack);

        private void Awake()
        {
            _showGuide = false; // Hot reload can retain private fields on cached assets; each live instance starts clean.
            _resourcesReady = false;
            _player ??= GetComponent<NetworkGymPlayer>();
            _input ??= GetComponent<PlayerInputReader>();
            _targeting ??= GetComponent<LockOnTargeting>();
        }

        private void Update()
        {
            if (_player == null || !_player.IsOwner) return;
            if (_input != null && _input.ConsumeGuidePressed()) _showGuide = !_showGuide;
            // Display-only state is sampled once per Update, not driven by IMGUI event passes.
            _resources = HudResourcePresentation.Resolve(
                _player.Health / _player.MaximumHealth, _player.Stamina / _player.MaximumStamina,
                _player.ReplicatedCombatState, _player.HealingFlaskCharges, _player.IsDowned);
            _resourcesReady = true;
            if (_lastFlaskCharges != _player.HealingFlaskCharges || _lastFlaskLabel != _resources.FlaskLabel)
            {
                _lastFlaskCharges = _player.HealingFlaskCharges;
                _lastFlaskLabel = _resources.FlaskLabel;
                _flaskText = $"R　药剂 {_lastFlaskCharges} · {_lastFlaskLabel}";
            }
            if (Time.unscaledTime < _nextWorldRefresh) return;
            _nextWorldRefresh = Time.unscaledTime + 0.35f;
            _objective = FindObjectOfType<NetworkGymWorldObjective>();
            _warden = FindObjectOfType<NetworkWarden>();
        }

        private void OnGUI()
        {
            if (!ShouldShowCombatHud || !_resourcesReady) return;
            EnsureStyles();
            bool hasInteractionTarget = TryGetInteractionGuidance(
                out Vector3 position, out float distance, out bool inRange);
            using (EmberfallGuiTheme.Canvas())
            {
                NetworkHudLayout layout = NetworkHudLayout.Resolve(EmberfallGuiTheme.Width);
                DrawPlayerPanel(layout.Player);
                DrawQuestPanel(layout.Quest);
                DrawBossPanel(layout.Boss);
                DrawInteractionPrompt(hasInteractionTarget, distance, inRange);
                DrawSessionState();
                if (_showGuide) DrawGuide();
                GUI.Label(new Rect(18f, EmberfallGuiTheme.Height - 38f, 360f, 22f),
                    "F1 操作指南 · Esc 离开会话", _bodyStyle);
            }
            // WorldToScreenPoint already returns physical pixels. The canvas restores GUI.matrix.
            DrawLockedTarget();
            if (hasInteractionTarget) DrawInteractionMarker(position, distance, inRange);
        }

        private void DrawPlayerPanel(Rect panel)
        {
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Ember);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 7f, panel.width - 32f, 28f), "余烬谷 · 双人协作", _titleStyle);
            float barWidth = panel.width - 32f;
            DrawResourceLine(panel, 38f, _resources.HealthLabel,
                $"{_player.Health:0} / {_player.MaximumHealth:0}", _resources.HealthAccent);
            DrawBar(new Rect(panel.x + 16f, panel.y + 62f, barWidth, 10f),
                _player.Health / _player.MaximumHealth, EmberfallGuiTheme.Danger);
            DrawResourceLine(panel, 76f, _resources.StaminaLabel,
                $"{_player.Stamina:0} / {_player.MaximumStamina:0}", _resources.StaminaAccent);
            DrawBar(new Rect(panel.x + 16f, panel.y + 98f, barWidth, 7f),
                _player.Stamina / _player.MaximumStamina, EmberfallGuiTheme.Stamina);
            DrawResourceLine(panel, 110f, "架势", $"{_player.Posture:0}", EmberfallGuiTheme.Text);
            DrawBar(new Rect(panel.x + 16f, panel.y + 132f, barWidth, 5f),
                _player.Posture / _player.MaximumPosture, EmberfallGuiTheme.Execution);
            Color previous = GUI.contentColor;
            try
            {
                GUI.contentColor = _resources.FlaskAccent;
                EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 141f, barWidth, 20f), _flaskText, _bodyStyle);
            }
            finally { GUI.contentColor = previous; }
            string status = _player.IsDowned
                ? $"已倒地 · 等待救援 {_player.RescueProgress:P0}"
                : _player.IsDead ? "已阵亡"
                : _player.ApproximateRangedCooldownRemaining <= 0f
                    ? "F　飞刀 · 就绪"
                    : $"F　飞刀冷却 {_player.ApproximateRangedCooldownRemaining:0.0}s";
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 164f, barWidth, 20f), status, _bodyStyle);
        }

        private void DrawResourceLine(Rect panel, float y, string label, string value, Color accent)
        {
            Color previous = GUI.contentColor;
            try
            {
                GUI.contentColor = accent;
                EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + y, panel.width - 148f, 20f), label, _bodyStyle);
                EmberfallGuiTheme.HudLabel(new Rect(panel.xMax - 116f, panel.y + y, 100f, 20f), value, _valueStyle);
            }
            finally { GUI.contentColor = previous; }
        }

        private void DrawQuestPanel(Rect panel)
        {
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Interaction);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 8f, panel.width - 32f, 28f), "主线 · 余烬封印", _titleStyle);
            string objective = NetworkLocalizedText.Resolve(
                _objective != null ? _objective.GetObjectiveTextId() : "text:network.quest.syncing");
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 39f, panel.width - 32f, 70f), objective, _objectiveStyle);
            EmberfallGuiTheme.HudLabel(
                new Rect(panel.x + 16f, panel.y + 116f, panel.width - 32f, 22f),
                $"共享余烬：{_player.SharedRewardCount}/1",
                _bodyStyle);
        }

        private void DrawBossPanel(Rect panel)
        {
            if (_warden == null || !_warden.IsAlive) return;
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Danger);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 18f, panel.y + 6f, panel.width - 36f, 27f), "余烬守望者", _titleStyle);
            DrawBar(new Rect(panel.x + 42f, panel.y + 38f, panel.width - 84f, 14f),
                _warden.HealthNormalized, new Color(0.78f, 0.12f, 0.07f));
            DrawBar(new Rect(panel.x + 42f, panel.y + 58f, panel.width - 84f, 7f),
                _warden.PostureNormalized, new Color(0.12f, 0.62f, 0.9f));
            EmberfallGuiTheme.HudLabel(
                new Rect(panel.x + 18f, panel.y + 67f, panel.width - 36f, 20f),
                BuildBossStatus(ResolveWardenPhase(), ResolveWardenState(),
                    _warden.ReplicatedState, _warden.ReplicatedAttack),
                _bossStatusStyle);
        }

        private void DrawLockedTarget()
        {
            if (_targeting == null || !_targeting.IsLocked || Camera.main == null) return;
            CombatTarget target = _targeting.CurrentTarget;
            Vector3 screen = Camera.main.WorldToScreenPoint(target.AimPoint.position);
            if (screen.z <= 0f) return;

            float x = screen.x;
            float y = Screen.height - screen.y;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.62f, 0.12f, 0.98f);
            DrawCornerBrackets(x, y, 31f);
            GUI.color = previous;
            DrawBar(new Rect(x - 58f, y - 47f, 116f, 8f), target.HealthNormalized, new Color(0.86f, 0.24f, 0.12f));
            if (target.HasSecondaryResource)
                DrawBar(new Rect(x - 58f, y - 36f, 116f, 5f), target.SecondaryResourceNormalized,
                    new Color(0.2f, 0.68f, 0.92f));
            string label = target is NetworkCombatTargetProxy proxy
                ? $"{proxy.DisplayName} · {proxy.StateLabel}"
                : "锁定目标";
            GUI.Label(new Rect(x - 120f, y + 31f, 240f, 23f), label, _centerStyle);
        }

        private bool TryGetInteractionGuidance(out Vector3 position, out float distance, out bool inRange)
        {
            position = default;
            distance = 0f;
            inRange = false;
            if (_objective == null || !_objective.TryGetCurrentInteractionTarget(out position)) return false;
            Vector3 offset = position - _player.transform.position;
            offset.y = 0f;
            distance = offset.magnitude;
            inRange = distance <= _objective.PlayerInteractionRange;
            return true;
        }

        private void DrawInteractionMarker(Vector3 position, float distance, bool inRange)
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 screen = camera.WorldToScreenPoint(position + Vector3.up * 1.15f);
                if (screen.z > 0f)
                {
                    float x = screen.x;
                    float y = Screen.height - screen.y;
                    Color previous = GUI.color;
                    GUI.color = inRange
                        ? new Color(1f, 0.72f, 0.16f, 0.98f)
                        : new Color(0.52f, 0.76f, 1f, 0.82f);
                    GUI.Label(new Rect(x - 80f, y - 44f, 160f, 34f), "◆", _centerStyle);
                    GUI.Label(new Rect(x - 100f, y - 12f, 200f, 28f), $"任务目标 {distance:0.0}m", _centerStyle);
                    GUI.color = previous;
                }
            }
        }

        private void DrawInteractionPrompt(bool hasTarget, float distance, bool inRange)
        {
            if (!hasTarget)
            {
                DrawInteractionFeedback();
                return;
            }
            string interaction = NetworkLocalizedText.Resolve(_objective.GetInteractionTextId());
            string prompt = BuildInteractionPrompt(interaction, inRange, distance);
            if (string.IsNullOrEmpty(prompt))
            {
                DrawInteractionFeedback();
                return;
            }
            Rect panel = new Rect((EmberfallGuiTheme.Width - 470f) * 0.5f, EmberfallGuiTheme.Height - 112f, 470f, 50f);
            DrawPanel(panel, 0.93f);
            GUI.Label(panel, prompt, _centerStyle);
            DrawInteractionFeedback();
        }

        private void DrawInteractionFeedback()
        {
            if (!_player.HasInteractionFeedback) return;
            Rect rect = new Rect((EmberfallGuiTheme.Width - 460f) * 0.5f, EmberfallGuiTheme.Height - 164f, 460f, 38f);
            DrawPanel(rect, 0.9f);
            Color previous = GUI.contentColor;
            GUI.contentColor = _player.LastInteractionAccepted
                ? new Color(0.32f, 1f, 0.58f)
                : new Color(1f, 0.4f, 0.22f);
            GUI.Label(rect, NetworkLocalizedText.Resolve(_player.InteractionFeedback), _centerStyle);
            GUI.contentColor = previous;
        }

        private void DrawSessionState()
        {
            if (_player.MatchStarted && !_player.InputSuppressed && !_player.PartyDefeated) return;
            string message = !_player.IsReady
                ? "按 E 准备"
                : !_player.MatchStarted
                    ? "已准备 · 等待另一名玩家"
                    : _player.PartyDefeated ? "队伍战败 · Server 正在重置遭遇" : "正在同步场景……";
            Rect panel = new Rect((EmberfallGuiTheme.Width - 460f) * 0.5f, (EmberfallGuiTheme.Height - 72f) * 0.5f, 460f, 72f);
            DrawPanel(panel, 0.95f);
            GUI.Label(panel, message, _centerStyle);
        }

        private void DrawGuide()
        {
            Rect panel = new Rect(18f, EmberfallGuiTheme.Height - 268f, 304f, 218f);
            DrawPanel(panel, 0.84f);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, panel.width - 28f, 25f), "联机操作（F1 隐藏）", _titleStyle);
            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 38f, panel.width - 28f, panel.height - 44f),
                "WASD 移动　Shift 冲刺　鼠标控制镜头\n中键锁定 / 再按解除\n左右切换输入切换目标\n左键轻击　右键蓄力重击\nF 飞刀　Q 防御　R 治疗\nSpace 闪避　E 互动/救援\nEsc 离开联机会话",
                _bodyStyle);
        }

        private string ResolveWardenPhase() => _warden.ReplicatedPhase switch
        {
            Emberfall.AI.Domain.WardenPhase.Transition => NetworkLocalizedText.Resolve("text:network.warden.phase-transition"),
            Emberfall.AI.Domain.WardenPhase.PhaseTwo => NetworkLocalizedText.Resolve("text:network.warden.phase-two"),
            _ => NetworkLocalizedText.Resolve("text:network.warden.phase-one")
        };

        private string ResolveWardenState() => _warden.ReplicatedState switch
        {
            Emberfall.AI.Domain.WardenState.Windup => NetworkLocalizedText.Resolve("text:network.warden.state-windup"),
            Emberfall.AI.Domain.WardenState.Attack => NetworkLocalizedText.Resolve("text:network.warden.state-attack"),
            Emberfall.AI.Domain.WardenState.Recovery => NetworkLocalizedText.Resolve("text:network.warden.state-recovery"),
            Emberfall.AI.Domain.WardenState.GuardBreak => NetworkLocalizedText.Resolve("text:network.warden.state-guard-break"),
            Emberfall.AI.Domain.WardenState.PhaseTransition => NetworkLocalizedText.Resolve("text:network.warden.state-transition"),
            _ => NetworkLocalizedText.Resolve("text:network.warden.state-alert")
        };

        private static void DrawCornerBrackets(float x, float y, float radius)
        {
            GUI.DrawTexture(new Rect(x - radius, y - radius, 14f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - radius, y - radius, 3f, 14f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + radius - 14f, y - radius, 14f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + radius - 3f, y - radius, 3f, 14f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - radius, y + radius - 3f, 14f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - radius, y + radius - 14f, 3f, 14f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + radius - 14f, y + radius - 3f, 14f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + radius - 3f, y + radius - 14f, 3f, 14f), Texture2D.whiteTexture);
        }

        private void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = EmberfallGuiTheme.Text }
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 14,
                normal = { textColor = new Color(0.86f, 0.9f, 0.92f) }
            };
            _valueStyle ??= new GUIStyle(_bodyStyle)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };
            _objectiveStyle ??= new GUIStyle(_bodyStyle)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _centerStyle ??= new GUIStyle(_objectiveStyle) { fontStyle = FontStyle.Bold };
            // Remove the inherited skin padding, not font size or the Boss row bounds.
            _bossStatusStyle ??= new GUIStyle(_centerStyle) { padding = new RectOffset(0, 0, 0, 0) };
        }

        private static void DrawPanel(Rect rect, float alpha)
        {
            EmberfallGuiTheme.Panel(rect,alpha);
        }

        private static void DrawBar(Rect rect, float normalized, Color fill)
        {
            EmberfallGuiTheme.Bar(rect,normalized,fill);
        }
    }
}
